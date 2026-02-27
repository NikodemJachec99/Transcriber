using System;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class SessionQueryOptions
{
    public string? Query { get; init; }

    public string? Tag { get; init; }

    public string? ModelName { get; init; }

    public DateTimeOffset? DateFromUtc { get; init; }

    public DateTimeOffset? DateToUtc { get; init; }
}
