using System;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class ModelDownloadProgress
{
    public required string ModelName { get; init; }

    public required long DownloadedBytes { get; init; }

    public required long? TotalBytes { get; init; }

    public required double FractionCompleted { get; init; }

    public TimeSpan? EstimatedRemaining { get; init; }

    public required bool IsResumedDownload { get; init; }
}
