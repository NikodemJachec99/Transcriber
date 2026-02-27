using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Audio;
using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Storage;
using AlwaysOnTopTranscriber.Core.Transcription;
using AlwaysOnTopTranscriber.Core.Utilities;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace AlwaysOnTopTranscriber.Core.Sessions;

public sealed class TranscriptionSessionService : ITranscriptionSessionService, IDisposable
{
    private const int MinFrameBufferCapacity = 256;
    private const int MaxFrameBufferCapacity = 4096;
    private const int DefaultFrameBufferCapacity = 2048;
    private const int WarningThrottleMs = 5000;

    private readonly IAudioCaptureService _audioCaptureService;
    private readonly AudioChunker _audioChunker;
    private readonly ITranscriptionEngineFactory _engineFactory;
    private readonly ISettingsService _settingsService;
    private readonly ISessionRepository _sessionRepository;
    private readonly ITranscriptFileWriter _transcriptFileWriter;
    private readonly AppPaths _appPaths;
    private readonly ILogger<TranscriptionSessionService> _logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly TranscriptAggregator _aggregator = new();

    private Channel<AudioFrame>? _frameChannel;
    private Channel<AudioChunk>? _chunkChannel;
    private CancellationTokenSource? _sessionCts;
    private Task? _chunkerTask;
    private Task? _transcriberTask;
    private Timer? _timer;
    private AppSettings? _activeSettings;
    private DateTimeOffset _sessionStartUtc;
    private string _sessionName = string.Empty;
    private string _engineType = "LocalWhisper";
    private string _modelName = "base";
    private Exception? _pipelineError;
    private int _isRecording;
    private long _framesEnqueued;
    private long _framesDequeued;
    private long _chunksEnqueued;
    private long _chunksDequeued;
    private long _chunksProcessed;
    private long _processingLagTicks;
    private long _framesDropped;
    private long _lastFrameBufferWarningTick;
    private long _lastChunkQueueWarningTick;
    private long _transcriptVersion;
    private long _cachedTranscriptVersion;
    private float _currentAudioLevel;
    private float _smoothedAudioLevel;
    private int _frameBufferCapacity = DefaultFrameBufferCapacity;
    private IReadOnlyList<TranscriptSegment> _cachedSegments = Array.Empty<TranscriptSegment>();
    private IReadOnlyList<string> _cachedPreviewLinesOldestFirst = Array.Empty<string>();
    private IReadOnlyList<string> _cachedPreviewLinesNewestFirst = Array.Empty<string>();
    private string _cachedFullText = string.Empty;
    private TranscriptDisplayMode _cachedDisplayMode = TranscriptDisplayMode.AppendAndCorrect;
    private string _cachedLineSeparator = "\n\n";
    private readonly object _cacheSync = new();

    public TranscriptionSessionService(
        IAudioCaptureService audioCaptureService,
        AudioChunker audioChunker,
        ITranscriptionEngineFactory engineFactory,
        ISettingsService settingsService,
        ISessionRepository sessionRepository,
        ITranscriptFileWriter transcriptFileWriter,
        AppPaths appPaths,
        ILogger<TranscriptionSessionService> logger)
    {
        _audioCaptureService = audioCaptureService;
        _audioChunker = audioChunker;
        _engineFactory = engineFactory;
        _settingsService = settingsService;
        _sessionRepository = sessionRepository;
        _transcriptFileWriter = transcriptFileWriter;
        _appPaths = appPaths;
        _logger = logger;
    }

    public event EventHandler<bool>? RecordingStateChanged;

    public event EventHandler<LiveTranscriptUpdate>? LiveTranscriptUpdated;

    public event EventHandler<string>? WarningRaised;

    public event EventHandler<SessionEntity>? SessionSaved;

    public bool IsRecording => Volatile.Read(ref _isRecording) == 1;

    public async Task StartAsync(string sessionName)
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRecording)
            {
                return;
            }

            _activeSettings = _settingsService.Load();
            _sessionName = string.IsNullOrWhiteSpace(sessionName)
                ? $"Sesja_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}"
                : sessionName.Trim();
            _sessionStartUtc = DateTimeOffset.UtcNow;
            _pipelineError = null;
            _aggregator.Clear();
            _modelName = _activeSettings.ModelName;
            ResetMetrics();

            var modelPath = ResolveModelPath(_activeSettings);
            if (!File.Exists(modelPath))
            {
                if (TryResolveFallbackModelPath(out var fallbackPath, out var fallbackModelName))
                {
                    modelPath = fallbackPath;
                    _modelName = fallbackModelName;
                    _activeSettings.ModelName = fallbackModelName;
                    _activeSettings.ModelPath = null;
                    await _settingsService.SaveAsync(_activeSettings, CancellationToken.None).ConfigureAwait(false);
                    WarningRaised?.Invoke(
                        this,
                        $"Wybrany model nie był dostępny lokalnie. Użyto modelu: {fallbackModelName}.");
                }
                else
                {
                    throw new FileNotFoundException(
                        $"Brak modelu lokalnego: {modelPath}. Pobierz model w Ustawieniach.");
                }
            }

            _frameBufferCapacity = Math.Clamp(
                _activeSettings.MaxBufferedAudioFrames,
                MinFrameBufferCapacity,
                MaxFrameBufferCapacity);

            _frameChannel = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(_frameBufferCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
            _chunkChannel = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(32)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            _sessionCts = new CancellationTokenSource();

            _audioCaptureService.FrameCaptured += OnFrameCaptured;
            _audioCaptureService.WarningRaised += OnCaptureWarning;

            _chunkerTask = RunChunkerAsync(_sessionCts.Token);
            _transcriberTask = RunTranscriberAsync(_sessionCts.Token);

            await _audioCaptureService.StartAsync(_sessionCts.Token).ConfigureAwait(false);

            _timer = new Timer(_ => PublishUpdate(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            Interlocked.Exchange(ref _isRecording, 1);
            RecordingStateChanged?.Invoke(this, true);
            _logger.LogInformation("Rozpoczęto sesję transkrypcji: {Name}", _sessionName);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRecording)
            {
                return;
            }

            Interlocked.Exchange(ref _isRecording, 0);
            RecordingStateChanged?.Invoke(this, false);

            _timer?.Dispose();
            _timer = null;

            _audioCaptureService.FrameCaptured -= OnFrameCaptured;
            _audioCaptureService.WarningRaised -= OnCaptureWarning;

            try
            {
                if (_audioCaptureService.IsCapturing)
                {
                    await _audioCaptureService.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Błąd przy zatrzymywaniu capture.");
            }

            _frameChannel?.Writer.TryComplete();

            if (_chunkerTask is not null)
            {
                await _chunkerTask.ConfigureAwait(false);
            }

            _chunkChannel?.Writer.TryComplete();
            if (_transcriberTask is not null)
            {
                await _transcriberTask.ConfigureAwait(false);
            }

            var endUtc = DateTimeOffset.UtcNow;
            var transcript = BuildTranscriptTextFromSettings();
            var segments = _aggregator.Snapshot();
            var snapshot = new SessionSnapshot
            {
                Name = _sessionName,
                StartTimeUtc = _sessionStartUtc,
                EndTimeUtc = endUtc,
                EngineType = _engineType,
                ModelName = _modelName,
                Segments = segments,
                TranscriptText = transcript
            };

            var files = await _transcriptFileWriter.WriteAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
            var session = new SessionEntity
            {
                Name = _sessionName,
                StartTimeUtc = _sessionStartUtc,
                EndTimeUtc = endUtc,
                Duration = endUtc - _sessionStartUtc,
                MarkdownPath = files.MarkdownPath,
                JsonPath = files.JsonPath,
                TextPath = files.TextPath,
                TranscriptText = transcript,
                EngineType = _engineType,
                ModelName = _modelName,
                WordCount = CountWords(transcript)
            };
            session.Id = await _sessionRepository.AddSessionAsync(session, CancellationToken.None).ConfigureAwait(false);

            PublishUpdate();
            SessionSaved?.Invoke(this, session);

            var droppedFrames = Interlocked.Read(ref _framesDropped);
            if (droppedFrames > 0)
            {
                WarningRaised?.Invoke(
                    this,
                    $"Sesja zapisana, ale pominięto {droppedFrames} ramek audio z powodu przeciążenia. Dla długich sesji wybierz lżejszy model.");
                _logger.LogWarning("Session {Name}: dropped {DroppedFrames} audio frames due to frame buffer pressure.", _sessionName, droppedFrames);
            }

            if (_pipelineError is not null)
            {
                WarningRaised?.Invoke(this, $"Sesja zatrzymana po błędzie: {_pipelineError.Message}");
            }

            _logger.LogInformation("Zakończono sesję transkrypcji: {Name}", _sessionName);
            CleanupState();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _lifecycleLock.Dispose();
    }

    private void OnFrameCaptured(object? sender, AudioFrame frame)
    {
        UpdateAudioLevels(frame);
        var writer = _frameChannel?.Writer;
        if (writer is null)
        {
            return;
        }

        if (writer.TryWrite(frame))
        {
            Interlocked.Increment(ref _framesEnqueued);
            return;
        }

        Interlocked.Increment(ref _framesDropped);
        RaiseThrottledWarning(
            ref _lastFrameBufferWarningTick,
            $"Bufor audio osiągnął limit ({_frameBufferCapacity} ramek). Aplikacja pomija część audio, żeby nie zużywać całej pamięci RAM.");
    }

    private void OnCaptureWarning(object? sender, string warning)
    {
        WarningRaised?.Invoke(this, warning);
    }

    private async Task RunChunkerAsync(CancellationToken cancellationToken)
    {
        if (_frameChannel is null || _chunkChannel is null || _activeSettings is null)
        {
            return;
        }

        try
        {
            var options = new ChunkingOptions
            {
                ChunkLengthSeconds = _activeSettings.ChunkLengthSeconds,
                TargetSampleRate = 16_000,
                EmitPartialChunkOnComplete = true,
                SilenceRmsThreshold = Math.Clamp(_activeSettings.SilenceRmsThreshold, 0.0005f, 0.05f)
            };

            await foreach (var chunk in _audioChunker.RunAsync(ReadFramesAsync(_frameChannel.Reader, cancellationToken), options, cancellationToken)
                               .ConfigureAwait(false))
            {
                Interlocked.Increment(ref _chunksEnqueued);
                var pendingChunks = Math.Max(0, Interlocked.Read(ref _chunksEnqueued) - Interlocked.Read(ref _chunksDequeued));
                if (pendingChunks >= 24)
                {
                    RaiseThrottledWarning(
                        ref _lastChunkQueueWarningTick,
                        "Kolejka transkrypcji rośnie. Rozważ mniejszy model lub dłuższy chunk.");
                }

                await _chunkChannel.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normalne zakończenie.
        }
        catch (Exception ex)
        {
            _pipelineError ??= ex;
            _logger.LogError(ex, "Błąd w chunkerze audio.");
            WarningRaised?.Invoke(this, "Błąd buforowania audio.");
        }
        finally
        {
            _chunkChannel.Writer.TryComplete(_pipelineError);
        }
    }

    private async Task RunTranscriberAsync(CancellationToken cancellationToken)
    {
        if (_chunkChannel is null || _activeSettings is null)
        {
            return;
        }

        var engine = _engineFactory.Create(_activeSettings);
        _engineType = engine.EngineName;
        var modelPath = ResolveModelPath(_activeSettings);

        var request = new TranscriptionRequest
        {
            ModelName = _activeSettings.ModelName,
            ModelPath = modelPath,
            Language = _activeSettings.Language,
            AutoPunctuation = _activeSettings.AutoPunctuation
        };

        try
        {
            await foreach (var chunk in _chunkChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _chunksDequeued);
                var result = await engine.TranscribeAsync(chunk, request, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _chunksProcessed);
                var elapsedNow = DateTimeOffset.UtcNow - _sessionStartUtc;
                var lag = elapsedNow - (chunk.StartOffset + chunk.Duration);
                Interlocked.Exchange(ref _processingLagTicks, Math.Max(0, lag.Ticks));
                _aggregator.Apply(result);
                Interlocked.Increment(ref _transcriptVersion);
                PublishUpdate();
            }
        }
        catch (OperationCanceledException)
        {
            // Normalne zakończenie.
        }
        catch (Exception ex)
        {
            _pipelineError ??= ex;
            _logger.LogError(ex, "Silnik transkrypcji zakończył się błędem.");
            WarningRaised?.Invoke(this, $"Błąd transkrypcji: {ex.Message}");
            _sessionCts?.Cancel();
            _ = Task.Run(StopAsync);
        }
    }

    private void PublishUpdate()
    {
        var elapsed = IsRecording
            ? DateTimeOffset.UtcNow - _sessionStartUtc
            : TimeSpan.Zero;
        var mode = _activeSettings?.TranscriptDisplayMode ?? TranscriptDisplayMode.AppendAndCorrect;
        var separator = _activeSettings?.LineSeparator ?? "\n\n";
        var previewNewestFirst = mode == TranscriptDisplayMode.AppendAbove;

        var (segments, previewLines, fullText) = GetCachedTranscriptView(mode, separator, previewNewestFirst);

        var update = new LiveTranscriptUpdate
        {
            Segments = segments,
            PreviewLines = previewLines,
            FullText = fullText,
            Elapsed = elapsed,
            CurrentAudioLevel = Volatile.Read(ref _currentAudioLevel),
            SmoothedAudioLevel = Volatile.Read(ref _smoothedAudioLevel),
            PendingAudioFrames = (int)Math.Max(0, Interlocked.Read(ref _framesEnqueued) - Interlocked.Read(ref _framesDequeued)),
            PendingChunks = (int)Math.Max(0, Interlocked.Read(ref _chunksEnqueued) - Interlocked.Read(ref _chunksDequeued)),
            ProcessedChunks = (int)Interlocked.Read(ref _chunksProcessed),
            ProcessingLag = TimeSpan.FromTicks(Math.Max(0, Interlocked.Read(ref _processingLagTicks)))
        };
        LiveTranscriptUpdated?.Invoke(this, update);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private string BuildTranscriptTextFromSettings()
    {
        var mode = _activeSettings?.TranscriptDisplayMode ?? TranscriptDisplayMode.AppendAndCorrect;
        var separator = _activeSettings?.LineSeparator ?? "\n\n";
        return _aggregator.BuildTranscriptText(mode, separator);
    }

    private (IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<string> PreviewLines, string FullText) GetCachedTranscriptView(
        TranscriptDisplayMode mode,
        string separator,
        bool previewNewestFirst)
    {
        var version = Interlocked.Read(ref _transcriptVersion);

        lock (_cacheSync)
        {
            if (_cachedTranscriptVersion != version)
            {
                _cachedSegments = _aggregator.Snapshot();
                _cachedPreviewLinesOldestFirst = _aggregator.GetPreviewLines(6, newestFirst: false);
                _cachedPreviewLinesNewestFirst = _aggregator.GetPreviewLines(6, newestFirst: true);
                _cachedFullText = _aggregator.BuildTranscriptText(mode, separator);
                _cachedDisplayMode = mode;
                _cachedLineSeparator = separator;
                _cachedTranscriptVersion = version;
            }
            else if (_cachedDisplayMode != mode || !string.Equals(_cachedLineSeparator, separator, StringComparison.Ordinal))
            {
                _cachedFullText = _aggregator.BuildTranscriptText(mode, separator);
                _cachedDisplayMode = mode;
                _cachedLineSeparator = separator;
            }

            var preview = previewNewestFirst ? _cachedPreviewLinesNewestFirst : _cachedPreviewLinesOldestFirst;
            return (_cachedSegments, preview, _cachedFullText);
        }
    }

    private void RaiseThrottledWarning(ref long lastWarningTickField, string message)
    {
        var nowTick = Environment.TickCount64;
        var lastWarningTick = Interlocked.Read(ref lastWarningTickField);
        if (nowTick - lastWarningTick < WarningThrottleMs)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref lastWarningTickField, nowTick, lastWarningTick) == lastWarningTick)
        {
            WarningRaised?.Invoke(this, message);
        }
    }

    private string ResolveModelPath(AppSettings settings)
    {
        var customModelPath = NormalizeOptionalPath(settings.ModelPath);
        if (!string.IsNullOrWhiteSpace(customModelPath))
        {
            if (File.Exists(customModelPath))
            {
                return customModelPath;
            }

            var customFileName = Path.GetFileName(customModelPath);
            if (!string.IsNullOrWhiteSpace(customFileName))
            {
                var relocatedPath = Path.Combine(_appPaths.ModelsDirectory, customFileName);
                if (File.Exists(relocatedPath))
                {
                    _logger.LogWarning(
                        "Custom model path not found: {CustomPath}. Falling back to model with the same file name in app models directory: {RelocatedPath}",
                        customModelPath,
                        relocatedPath);
                    return relocatedPath;
                }
            }

            _logger.LogWarning("Custom model path not found: {CustomPath}. Falling back to selected model name.", customModelPath);
        }

        var mappedFileName = settings.ModelName.Equals("basics", StringComparison.OrdinalIgnoreCase)
            ? "ggml-medium-q5_0.bin"
            : $"ggml-{settings.ModelName}.bin";
        return Path.Combine(_appPaths.ModelsDirectory, mappedFileName);
    }

    private bool TryResolveFallbackModelPath(out string modelPath, out string modelName)
    {
        static (string Name, string FileName)[] OrderedKnownModels() =>
        [
            ("tiny", "ggml-tiny.bin"),
            ("base", "ggml-base.bin"),
            ("small", "ggml-small.bin"),
            ("basics", "ggml-medium-q5_0.bin")
        ];

        foreach (var candidate in OrderedKnownModels())
        {
            var path = Path.Combine(_appPaths.ModelsDirectory, candidate.FileName);
            if (File.Exists(path))
            {
                modelPath = path;
                modelName = candidate.Name;
                return true;
            }
        }

        var anyModel = Directory.EnumerateFiles(_appPaths.ModelsDirectory, "*.bin")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(anyModel))
        {
            modelPath = anyModel;
            modelName = Path.GetFileNameWithoutExtension(anyModel)
                .Replace("ggml-", string.Empty, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        modelPath = string.Empty;
        modelName = string.Empty;
        return false;
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        return Path.GetFullPath(expanded);
    }

    private void CleanupState()
    {
        _sessionCts?.Dispose();
        _sessionCts = null;
        _chunkerTask = null;
        _transcriberTask = null;
        _frameChannel = null;
        _chunkChannel = null;
        _activeSettings = null;
        _sessionName = string.Empty;
    }

    private void ResetMetrics()
    {
        Interlocked.Exchange(ref _framesEnqueued, 0);
        Interlocked.Exchange(ref _framesDequeued, 0);
        Interlocked.Exchange(ref _chunksEnqueued, 0);
        Interlocked.Exchange(ref _chunksDequeued, 0);
        Interlocked.Exchange(ref _chunksProcessed, 0);
        Interlocked.Exchange(ref _framesDropped, 0);
        Interlocked.Exchange(ref _processingLagTicks, 0);
        Interlocked.Exchange(ref _lastFrameBufferWarningTick, 0);
        Interlocked.Exchange(ref _lastChunkQueueWarningTick, 0);
        Interlocked.Exchange(ref _transcriptVersion, 0);
        Interlocked.Exchange(ref _cachedTranscriptVersion, -1);
        Volatile.Write(ref _currentAudioLevel, 0);
        Volatile.Write(ref _smoothedAudioLevel, 0);

        lock (_cacheSync)
        {
            _cachedSegments = Array.Empty<TranscriptSegment>();
            _cachedPreviewLinesOldestFirst = Array.Empty<string>();
            _cachedPreviewLinesNewestFirst = Array.Empty<string>();
            _cachedFullText = string.Empty;
            _cachedDisplayMode = TranscriptDisplayMode.AppendAndCorrect;
            _cachedLineSeparator = "\n\n";
        }
    }

    private async IAsyncEnumerable<AudioFrame> ReadFramesAsync(
        ChannelReader<AudioFrame> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Increment(ref _framesDequeued);
            yield return frame;
        }
    }

    private void UpdateAudioLevels(AudioFrame frame)
    {
        var current = Math.Clamp(ComputeFrameLevel(frame), 0, 1);
        var previousSmoothed = Volatile.Read(ref _smoothedAudioLevel);

        // Z Buzz: prosty smoothing utrzymuje stabilny meter zamiast migania na pojedynczych pikach.
        var smoothed = Math.Max(current, previousSmoothed * 0.93f);

        Volatile.Write(ref _currentAudioLevel, current);
        Volatile.Write(ref _smoothedAudioLevel, smoothed);
    }

    private static float ComputeFrameLevel(AudioFrame frame)
    {
        var bytesPerSample = Math.Max(1, frame.BitsPerSample / 8);
        var bytesPerFrame = bytesPerSample * frame.Channels;
        if (bytesPerFrame <= 0 || frame.Buffer.Length < bytesPerFrame)
        {
            return 0;
        }

        var frameCount = frame.Buffer.Length / bytesPerFrame;
        if (frameCount <= 0)
        {
            return 0;
        }

        double sumSquares = 0;
        var samplesCount = 0;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frameOffset = frameIndex * bytesPerFrame;
            float mono = 0;

            for (var channel = 0; channel < frame.Channels; channel++)
            {
                var sampleOffset = frameOffset + (channel * bytesPerSample);
                mono += ReadSampleAsFloat(frame.Buffer, sampleOffset, frame.Encoding, frame.BitsPerSample);
            }

            mono /= Math.Max(1, frame.Channels);
            sumSquares += mono * mono;
            samplesCount++;
        }

        if (samplesCount == 0)
        {
            return 0;
        }

        return (float)Math.Sqrt(sumSquares / samplesCount);
    }

    private static float ReadSampleAsFloat(byte[] buffer, int offset, WaveFormatEncoding encoding, int bitsPerSample)
    {
        if (encoding == WaveFormatEncoding.IeeeFloat && bitsPerSample == 32)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        if (encoding == WaveFormatEncoding.Pcm && bitsPerSample == 16)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, sizeof(short)));
            return value / (float)short.MaxValue;
        }

        if (encoding == WaveFormatEncoding.Pcm && bitsPerSample == 24)
        {
            var b0 = buffer[offset];
            var b1 = buffer[offset + 1];
            var b2 = buffer[offset + 2];
            var value = b0 | (b1 << 8) | (b2 << 16);
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }

            return value / 8_388_608f;
        }

        if (encoding == WaveFormatEncoding.Pcm && bitsPerSample == 32)
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, sizeof(int)));
            return value / (float)int.MaxValue;
        }

        return 0;
    }
}
