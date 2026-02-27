using System;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Sessions;

public interface ITranscriptionSessionService
{
    event EventHandler<bool>? RecordingStateChanged;

    event EventHandler<LiveTranscriptUpdate>? LiveTranscriptUpdated;

    event EventHandler<string>? WarningRaised;

    event EventHandler<SessionEntity>? SessionSaved;

    bool IsRecording { get; }

    Task StartAsync(string sessionName);

    Task StopAsync();
}
