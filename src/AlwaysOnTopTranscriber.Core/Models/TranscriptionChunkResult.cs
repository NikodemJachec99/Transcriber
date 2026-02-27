using System;
using System.Collections.Generic;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class TranscriptionChunkResult
{
    public required TimeSpan ChunkStartOffset { get; init; }

    public required IReadOnlyList<TranscriptSegment> Segments { get; init; }

    public bool IsPartial { get; init; }
}
