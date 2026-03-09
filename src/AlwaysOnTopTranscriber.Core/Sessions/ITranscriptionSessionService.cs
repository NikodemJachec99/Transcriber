using System;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Sessions;

public interface ITranscriptionSessionService
{
    event EventHandler<bool>? RecordingStateChanged;

    event EventHandler<SessionState>? SessionStateChanged;

    event EventHandler<LiveTranscriptUpdate>? LiveTranscriptUpdated;

    event EventHandler<string>? WarningRaised;

    event EventHandler<SessionEntity>? SessionSaved;

    event EventHandler<float>? AudioLevelChanged;

    bool IsRecording { get; }

    SessionState CurrentState { get; }

    Task StartAsync(string sessionName);

    Task PauseAsync();

    Task ResumeAsync();

    Task StopAsync();

    /// <summary>
    /// Ręcznie startuje transkrypcję dla nagrania w stanie "Recorded" (deferred mode).
    /// </summary>
    Task StartTranscriptionAsync();
}
