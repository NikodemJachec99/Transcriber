using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Transcription;

public interface ITranscriptionEngine
{
    string EngineName { get; }

    Task<TranscriptionChunkResult> TranscribeAsync(
        AudioChunk chunk,
        TranscriptionRequest request,
        CancellationToken cancellationToken);
}
