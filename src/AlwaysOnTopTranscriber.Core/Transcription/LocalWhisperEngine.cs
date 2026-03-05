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
            _factory = WhisperFactory.FromPath(modelPath);
            _loadedModelPath = modelPath;
            _logger.LogInformation("Załadowano model Whisper: {ModelPath}", modelPath);
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
    /// Detectuje dostępny GPU provider (CUDA, DirectML, ROCm, CPU).
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
        // Na Windows - spróbuj DirectML (AMD Vega, Intel Arc) lub CUDA (NVIDIA)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Spróbuj znaleźć AMD GPU przez DXGI/DirectX
            if (HasWindowsGpu("AMD"))
            {
                _logger.LogInformation("Detectowano GPU: AMD (Vega, RDNA) - będzie używany DirectML");
                return "directml";
            }

            if (HasWindowsGpu("NVIDIA"))
            {
                _logger.LogInformation("Detectowano GPU: NVIDIA - będzie używany CUDA");
                return "cuda";
            }

            if (HasWindowsGpu("Intel"))
            {
                _logger.LogInformation("Detectowano GPU: Intel Arc - będzie używany DirectML");
                return "directml";
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
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-WmiObject Win32_VideoController | Select-Object -ExpandProperty Name\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            return output.Contains(vendor, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Nie można detectować GPU {Vendor}", vendor);
            return false;
        }
    }
}
