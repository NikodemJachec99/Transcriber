using System;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed record TranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    float? Confidence = null,
    bool IsFinal = true);
