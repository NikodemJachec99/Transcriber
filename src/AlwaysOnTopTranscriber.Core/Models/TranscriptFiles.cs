namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class TranscriptFiles
{
    public required string MarkdownPath { get; init; }

    public required string JsonPath { get; init; }

    public required string TextPath { get; init; }
}
