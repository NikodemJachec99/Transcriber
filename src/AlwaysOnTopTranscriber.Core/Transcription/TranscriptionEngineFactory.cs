using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Transcription;

public sealed class TranscriptionEngineFactory : ITranscriptionEngineFactory
{
    private readonly LocalWhisperEngine _localWhisperEngine;
    private readonly CloudEnginePlaceholder _cloudEnginePlaceholder;

    public TranscriptionEngineFactory(
        LocalWhisperEngine localWhisperEngine,
        CloudEnginePlaceholder cloudEnginePlaceholder)
    {
        _localWhisperEngine = localWhisperEngine;
        _cloudEnginePlaceholder = cloudEnginePlaceholder;
    }

    public ITranscriptionEngine Create(AppSettings settings)
    {
        _ = settings;
        return _localWhisperEngine;
    }
}
