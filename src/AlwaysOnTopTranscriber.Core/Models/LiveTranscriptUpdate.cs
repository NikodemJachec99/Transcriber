using System;
using System.Collections.Generic;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class LiveTranscriptUpdate
{
    public required IReadOnlyList<TranscriptSegment> Segments { get; init; }

    public required IReadOnlyList<string> PreviewLines { get; init; }

    public required string FullText { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required float CurrentAudioLevel { get; init; }

    public required float SmoothedAudioLevel { get; init; }

    public required int PendingAudioFrames { get; init; }

    public required int PendingChunks { get; init; }

    public required int ProcessedChunks { get; init; }

    public required TimeSpan ProcessingLag { get; init; }
}
