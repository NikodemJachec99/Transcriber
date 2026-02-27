using System;
using System.Collections.Generic;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class SessionSnapshot
{
    public required string Name { get; init; }

    public required DateTimeOffset StartTimeUtc { get; init; }

    public required DateTimeOffset EndTimeUtc { get; init; }

    public required string EngineType { get; init; }

    public required string ModelName { get; init; }

    public required IReadOnlyList<TranscriptSegment> Segments { get; init; }

    public required string TranscriptText { get; init; }
}
