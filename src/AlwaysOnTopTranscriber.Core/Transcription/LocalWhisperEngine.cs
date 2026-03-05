using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace AlwaysOnTopTranscriber.Core.Transcription;

public sealed class LocalWhisperEngine : ITranscriptionEngine, IDisposable
{
    private readonly ILogger<LocalWhisperEngine> _logger;
    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private readonly AppSettings? _settings;
    private WhisperFactory? _factory;
    private string? _loadedModelPath;
    private string? _detectedGpuProvider;

    public LocalWhisperEngine(ILogger<LocalWhisperEngine> logger, AppSettings? settings = null)
    {
        _logger = logger;
        _settings = settings;
        _detectedGpuProvider = DetectGpuProvider();

        // Configure ONNX Runtime environment variables for GPU acceleration
        ConfigureGpuEnvironmentVariables();
    }

    private void ConfigureGpuEnvironmentVariables()
    {
        try
        {
            // Clear any existing GPU environment variables to ensure clean state
            Environment.SetEnvironmentVariable("ORT_CUDA_DEVICE_ID", null);
            Environment.SetEnvironmentVariable("ORT_DIRECTML_DEVICE_ID", null);

            if (_settings?.TryGpuAcceleration != true || _settings.GpuProvider == "cpu")
            {
                _logger.LogInformation("GPU acceleration disabled or forced to CPU - will use Whisper.net CPU runtime");
                return;
            }

            var gpuProvider = _settings.GpuProvider;
            if (gpuProvider == "auto")
            {
                gpuProvider = _detectedGpuProvider;
            }

            switch (gpuProvider)
            {
                case "cuda":
                    Environment.SetEnvironmentVariable("ORT_CUDA_DEVICE_ID", "0");
                    _logger.LogInformation("✓ GPU configured: CUDA (NVIDIA RTX/GTX) - Whisper.net.Runtime.Cuda required");
                    break;
                case "openvino":
                    _logger.LogWarning("OpenVINO is not currently available (packaging issues in Whisper.net 1.9.0). Falling back to CPU mode. TODO: Re-enable in future version.");
                    break;
                case "directml":
                    // Note: DirectML is not directly exposed in Whisper.net; use OpenVINO instead on Windows for AMD/Intel
                    _logger.LogInformation("⚠ DirectML not directly supported in Whisper.net - falling back to OpenVINO or CPU");
                    break;
                case "rocm":
                    _logger.LogInformation("✓ GPU configured: ROCm (AMD Linux)");
                    break;
                default:
                    _logger.LogInformation("GPU acceleration not configured - will use CPU");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error configuring GPU environment variables - will fallback to CPU");
        }
    }

    public string EngineName => "LocalWhisper";

    public async Task<TranscriptionChunkResult> TranscribeAsync(
        AudioChunk chunk,
        TranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ModelPath))
        {
            throw new FileNotFoundException(
                $"Nie znaleziono modelu Whisper: {request.ModelPath}. Ustaw model w Ustawieniach.");
        }

        await EnsureFactoryAsync(request.ModelPath, cancellationToken).ConfigureAwait(false);

        var segments = new List<TranscriptSegment>();
        await using var waveStream = BuildWavePcm16MonoStream(chunk);

        var builder = _factory!.CreateBuilder();
        if (!string.Equals(request.Language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.WithLanguage(request.Language);
        }

        // Tworzymy procesor per chunk, bo to bezpieczny i przewidywalny model pracy dla długich sesji.
        using var processor = builder.Build();

        // Time transcription to detect GPU usage
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await foreach (var result in processor.ProcessAsync(waveStream).WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            var text = (result.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var start = chunk.StartOffset + result.Start;
            var end = chunk.StartOffset + result.End;
            segments.Add(new TranscriptSegment(start, end, text));
        }

        sw.Stop();

        // Log transcription timing - GPU will be significantly faster
        var audioSeconds = chunk.Duration.TotalSeconds;
        var realTimeRatio = sw.Elapsed.TotalSeconds / audioSeconds;
        var gpuIndicator = realTimeRatio < 0.5 ? " [GPU DETECTED ✓]" : "";

        _logger.LogInformation(
            "Transcribed {AudioSeconds:F1}s audio in {ElapsedMs}ms ({RealtimeRatio:F2}x){GpuIndicator}",
            audioSeconds,
            sw.ElapsedMilliseconds,
            realTimeRatio,
            gpuIndicator);

        return new TranscriptionChunkResult
        {
            ChunkStartOffset = chunk.StartOffset,
            Segments = segments,
            IsPartial = false
        };
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _modelLock.Dispose();
    }

    private async Task EnsureFactoryAsync(string modelPath, CancellationToken cancellationToken)
    {
        await _modelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_factory is not null && string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _factory?.Dispose();

            // Enable GPU if configured - REQUIRED for CUDA to work in Whisper.net
            var factoryOptions = new WhisperFactoryOptions
            {
                UseGpu = _settings?.TryGpuAcceleration == true,
                GpuDevice = 0
            };

            _factory = WhisperFactory.FromPath(modelPath, factoryOptions);
            _loadedModelPath = modelPath;

            if (factoryOptions.UseGpu)
            {
                _logger.LogInformation("Załadowano model Whisper z GPU acceleration: {ModelPath}", modelPath);
            }
            else
            {
                _logger.LogInformation("Załadowano model Whisper (CPU mode): {ModelPath}", modelPath);
            }
        }
        finally
        {
            _modelLock.Release();
        }
    }

    private static MemoryStream BuildWavePcm16MonoStream(AudioChunk chunk)
    {
        var stream = new MemoryStream(chunk.Pcm16MonoData.Length + 44);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            var blockAlign = (short)2;
            var byteRate = chunk.SampleRate * blockAlign;
            var dataLength = chunk.Pcm16MonoData.Length;

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(chunk.SampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            writer.Write(chunk.Pcm16MonoData);
            writer.Flush();
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Detectuje dostępny GPU provider (CUDA, ROCm, CPU).
    /// Wymagane: Whisper.net.Runtime.Cuda dla CUDA acceleration.
    /// OpenVINO support disabled temporarily (Whisper.net 1.9.0 packaging issues).
    /// Loguje znaleziony provider. Wsparcie GPU jest opcjonalne - fallback na CPU.
    /// </summary>
    private string DetectGpuProvider()
    {
        // Jeśli użytkownik wymusił CPU, wykorzystaj CPU
        if (_settings?.TryGpuAcceleration == false ||
            (_settings?.GpuProvider?.Equals("cpu", StringComparison.OrdinalIgnoreCase) == true))
        {
            _logger.LogInformation("GPU acceleration wyłączony przez użytkownika - CPU mode");
            return "cpu";
        }

        try
        {
            // Automatyczna detekcja
            var detectedProvider = TryDetectGpu();
            _logger.LogInformation("GPU Provider: {Provider}", detectedProvider);
            return detectedProvider;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd przy detectowaniu GPU - fallback to CPU");
            return "cpu";
        }
    }

    /// <summary>
    /// Próbuje detectować dostępny GPU na podstawie systemu operacyjnego i zainstalowanych bibliotek.
    /// </summary>
    private string TryDetectGpu()
    {
        // Na Windows - spróbuj CUDA (NVIDIA) lub OpenVINO (AMD/Intel)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Spróbuj znaleźć NVIDIA GPU - najlepsze wsparcie
            if (HasWindowsGpu("NVIDIA"))
            {
                _logger.LogInformation("Detectowano GPU: NVIDIA - będzie używany CUDA");
                return "cuda";
            }

            // AMD Vega/RDNA - OpenVINO not available in current Whisper.net version
            if (HasWindowsGpu("AMD"))
            {
                _logger.LogWarning("Detectowano GPU: AMD (Vega, RDNA), ale OpenVINO nie jest dostępne. Będzie używany CPU mode. TODO: Add OpenVINO support");
                return "cpu";
            }

            // Intel Arc - OpenVINO not available in current Whisper.net version
            if (HasWindowsGpu("Intel"))
            {
                _logger.LogWarning("Detectowano GPU: Intel Arc, ale OpenVINO nie jest dostępne. Będzie używany CPU mode. TODO: Add OpenVINO support");
                return "cpu";
            }

            _logger.LogInformation("Brak detectowanego GPU - użycie CPU");
            return "cpu";
        }

        // Na Linux - spróbuj ROCm (AMD)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (File.Exists("/opt/rocm/bin/rocm_agent_enumerator"))
            {
                _logger.LogInformation("Detectowano ROCm na Linuxie - AMD GPU");
                return "rocm";
            }

            _logger.LogInformation("Brak AMD ROCm - użycie CPU");
            return "cpu";
        }

        // Na macOS - CoreML (Apple Silicon)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _logger.LogInformation("macOS detectowany - będzie używany CoreML na Apple Silicon jeśli dostępny");
            return "coreml";
        }

        return "cpu";
    }

    /// <summary>
    /// Sprawdza czy zainstalowany jest sprzęt GPU danego producenta (Windows only).
    /// Używa WMI/PowerShell do zdobycia informacji o GPU.
    /// </summary>
    private bool HasWindowsGpu(string vendor)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            // Spróbuj uruchomić PowerShell aby sprawdzić GPU
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-WmiObject Win32_VideoController | Select-Object -ExpandProperty Name\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            bool exited = proc.WaitForExit(5000);  // Increased timeout to 5 seconds

            if (!exited)
            {
                _logger.LogWarning("PowerShell GPU detection timeout after 5 seconds for vendor: {Vendor}", vendor);
                try { proc.Kill(); } catch { }
                return false;
            }

            _logger.LogDebug("PowerShell GPU output: {Output}", output);
            bool found = output.Contains(vendor, StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("GPU detection for vendor {Vendor}: {Found}", vendor, found);

            return found;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie można detectować GPU {Vendor}", vendor);
            return false;
        }
    }
}
