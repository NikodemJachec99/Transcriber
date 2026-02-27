using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AlwaysOnTopTranscriber.Core.Audio;

public sealed class WasapiLoopbackCaptureService : IAudioCaptureService
{
    private readonly ILogger<WasapiLoopbackCaptureService> _logger;
    private readonly object _sync = new();
    private WasapiLoopbackCapture? _capture;
    private Stopwatch? _offsetStopwatch;
    private int _isCapturing;
    private int _isRestarting;

    public WasapiLoopbackCaptureService(ILogger<WasapiLoopbackCaptureService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<AudioFrame>? FrameCaptured;

    public event EventHandler<string>? WarningRaised;

    public bool IsCapturing => Volatile.Read(ref _isCapturing) == 1;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _isCapturing, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        try
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            lock (_sync)
            {
                _capture = new WasapiLoopbackCapture(device);
                _capture.DataAvailable += HandleDataAvailable;
                _capture.RecordingStopped += HandleRecordingStopped;
                _offsetStopwatch = Stopwatch.StartNew();
                _capture.StartRecording();
            }

            _logger.LogInformation("Uruchomiono WASAPI loopback na urządzeniu: {Device}", device.FriendlyName);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _isCapturing, 0);
            _logger.LogError(ex, "Nie udało się uruchomić WASAPI loopback.");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _isCapturing, 0, 1) != 1)
        {
            return;
        }

        WasapiLoopbackCapture? capture;
        lock (_sync)
        {
            capture = _capture;
        }

        if (capture is null)
        {
            return;
        }

        var stopSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(object? sender, StoppedEventArgs args) => stopSignal.TrySetResult();

        capture.RecordingStopped += OnStopped;
        try
        {
            capture.StopRecording();
            using var registration = cancellationToken.Register(() => stopSignal.TrySetCanceled(cancellationToken));
            await stopSignal.Task.ConfigureAwait(false);
        }
        finally
        {
            capture.RecordingStopped -= OnStopped;
            DisposeCapture();
            _logger.LogInformation("Zatrzymano WASAPI loopback.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isCapturing, 0) == 1)
        {
            try
            {
                _capture?.StopRecording();
            }
            catch
            {
                // Dispose ma być bezpieczne nawet jeśli urządzenie zostało odłączone.
            }
        }

        DisposeCapture();
    }

    private void HandleDataAvailable(object? sender, WaveInEventArgs args)
    {
        WasapiLoopbackCapture? capture;
        Stopwatch? stopwatch;
        lock (_sync)
        {
            capture = _capture;
            stopwatch = _offsetStopwatch;
        }

        if (capture is null || args.BytesRecorded <= 0)
        {
            return;
        }

        var copy = new byte[args.BytesRecorded];
        Buffer.BlockCopy(args.Buffer, 0, copy, 0, args.BytesRecorded);

        var waveFormat = capture.WaveFormat;
        var frame = new AudioFrame(
            copy,
            waveFormat.SampleRate,
            waveFormat.BitsPerSample,
            waveFormat.Channels,
            waveFormat.Encoding,
            stopwatch?.Elapsed ?? TimeSpan.Zero,
            DateTimeOffset.UtcNow);

        FrameCaptured?.Invoke(this, frame);
    }

    private void HandleRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null)
        {
            _logger.LogWarning(args.Exception, "WASAPI loopback został zatrzymany przez wyjątek.");
            WarningRaised?.Invoke(this, "Przechwytywanie audio zostało zatrzymane przez błąd urządzenia.");

            if (IsCapturing)
            {
                _ = Task.Run(TryRestartAfterDeviceErrorAsync);
            }
        }
    }

    private async Task TryRestartAfterDeviceErrorAsync()
    {
        if (Interlocked.CompareExchange(ref _isRestarting, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            if (!IsCapturing)
            {
                return;
            }

            lock (_sync)
            {
                DisposeCaptureUnsafe();
            }

            var deviceEnumerator = new MMDeviceEnumerator();
            var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            lock (_sync)
            {
                _capture = new WasapiLoopbackCapture(device);
                _capture.DataAvailable += HandleDataAvailable;
                _capture.RecordingStopped += HandleRecordingStopped;
                _offsetStopwatch = Stopwatch.StartNew();
                _capture.StartRecording();
            }

            _logger.LogInformation("WASAPI loopback wznowiony po błędzie urządzenia: {Device}", device.FriendlyName);
            WarningRaised?.Invoke(this, "Przechwytywanie audio zostało automatycznie wznowione.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się wznowić WASAPI loopback po błędzie urządzenia.");
            WarningRaised?.Invoke(this, "Nie udało się wznowić przechwytywania audio. Zatrzymaj i uruchom sesję ponownie.");
        }
        finally
        {
            Interlocked.Exchange(ref _isRestarting, 0);
        }
    }

    private void DisposeCapture()
    {
        lock (_sync)
        {
            DisposeCaptureUnsafe();
        }
    }

    private void DisposeCaptureUnsafe()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= HandleDataAvailable;
        _capture.RecordingStopped -= HandleRecordingStopped;
        _capture.Dispose();
        _capture = null;
        _offsetStopwatch = null;
    }
}
