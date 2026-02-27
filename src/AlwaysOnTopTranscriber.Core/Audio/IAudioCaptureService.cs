using System;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Audio;

public interface IAudioCaptureService : IDisposable
{
    event EventHandler<AudioFrame>? FrameCaptured;

    event EventHandler<string>? WarningRaised;

    bool IsCapturing { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
