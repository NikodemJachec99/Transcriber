using System;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Transcription;

public sealed class CloudEnginePlaceholder : ITranscriptionEngine
{
    public string EngineName => "CloudPlaceholder";

    public Task<TranscriptionChunkResult> TranscribeAsync(
        AudioChunk chunk,
        TranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(
            "Tryb Cloud nie został skonfigurowany. Wybierz Local albo dodaj providera API.");
    }
}
