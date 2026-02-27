namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class TranscriptionRequest
{
    public required string ModelName { get; init; }

    public required string ModelPath { get; init; }

    public string Language { get; init; } = "auto";

    public bool AutoPunctuation { get; init; } = true;
}
