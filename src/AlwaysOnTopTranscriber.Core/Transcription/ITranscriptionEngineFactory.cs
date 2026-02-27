using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Transcription;

public interface ITranscriptionEngineFactory
{
    ITranscriptionEngine Create(AppSettings settings);
}
