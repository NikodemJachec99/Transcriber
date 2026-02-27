using System;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class SessionEntity
{
    public long Id { get; set; }

    public required string Name { get; init; }

    public required DateTimeOffset StartTimeUtc { get; init; }

    public required DateTimeOffset EndTimeUtc { get; init; }

    public required TimeSpan Duration { get; init; }

    public required string MarkdownPath { get; init; }

    public required string JsonPath { get; init; }

    public required string TextPath { get; init; }

    public required string TranscriptText { get; init; }

    public required string EngineType { get; init; }

    public required string ModelName { get; init; }

    public required int WordCount { get; init; }

    public string Notes { get; init; } = string.Empty;

    public string Tags { get; init; } = string.Empty;
}
