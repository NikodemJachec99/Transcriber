namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class ModelDescriptor
{
    public required string Name { get; init; }

    public required string DownloadUrl { get; init; }

    public required string FileName { get; init; }

    public string? LocalPath { get; init; }

    public bool IsDownloaded { get; init; }

    public long? ApproximateSizeBytes { get; init; }
}
