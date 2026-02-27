using System;
using System.Collections.Generic;
using System.Linq;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Transcription;

public sealed class TranscriptAggregator
{
    private readonly object _sync = new();
    private readonly List<TranscriptSegment> _segments = [];

    public void Clear()
    {
        lock (_sync)
        {
            _segments.Clear();
        }
    }

    public void Apply(TranscriptionChunkResult chunkResult)
    {
        lock (_sync)
        {
            foreach (var segment in chunkResult.Segments)
            {
                var normalizedText = segment.Text.Trim();
                if (string.IsNullOrWhiteSpace(normalizedText))
                {
                    continue;
                }

                // Wzorzec z aplikacji realtime-whisper: odfiltrowujemy oczywiste pętle słów.
                if (LooksLikeRepetition(normalizedText))
                {
                    continue;
                }

                var normalizedSegment = segment with { Text = normalizedText };

                if (_segments.Count > 0)
                {
                    var last = _segments[^1];
                    var startDeltaMs = Math.Abs((last.Start - normalizedSegment.Start).TotalMilliseconds);

                    if (startDeltaMs <= 300 &&
                        string.Equals(last.Text, normalizedSegment.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (startDeltaMs <= 700 &&
                        normalizedSegment.Text.StartsWith(last.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        _segments[^1] = normalizedSegment;
                        continue;
                    }
                }

                _segments.Add(normalizedSegment);
            }
        }
    }

    public IReadOnlyList<TranscriptSegment> Snapshot()
    {
        lock (_sync)
        {
            return _segments.ToList();
        }
    }

    public string BuildTranscriptText()
    {
        return BuildTranscriptText(TranscriptDisplayMode.AppendAndCorrect, "\n\n");
    }

    public string BuildTranscriptText(TranscriptDisplayMode mode, string? lineSeparator)
    {
        lock (_sync)
        {
            var separator = string.IsNullOrWhiteSpace(lineSeparator) ? "\n\n" : lineSeparator;

            if (mode == TranscriptDisplayMode.AppendBelow)
            {
                return string.Join(separator, _segments.Select(static segment => segment.Text)).Trim();
            }

            if (mode == TranscriptDisplayMode.AppendAbove)
            {
                return string.Join(separator, _segments.AsEnumerable().Reverse().Select(static segment => segment.Text)).Trim();
            }

            var merged = string.Empty;
            foreach (var segment in _segments)
            {
                merged = MergeTextNoOverlap(merged, segment.Text);
            }

            return merged.Trim();
        }
    }

    public IReadOnlyList<string> GetPreviewLines(int maxLines = 6)
    {
        return GetPreviewLines(maxLines, newestFirst: false);
    }

    public IReadOnlyList<string> GetPreviewLines(int maxLines, bool newestFirst)
    {
        lock (_sync)
        {
            var take = Math.Max(1, maxLines);
            var preview = _segments
                .TakeLast(take)
                .Select(static segment => $"[{segment.Start:hh\\:mm\\:ss}] {segment.Text}");

            return (newestFirst ? preview.Reverse() : preview).ToList();
        }
    }

    private static bool LooksLikeRepetition(string text)
    {
        var words = text
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => part.ToLowerInvariant())
            .ToList();

        if (words.Count < 8)
        {
            return false;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var word in words)
        {
            counts[word] = counts.GetValueOrDefault(word) + 1;
        }

        var maxRepeat = counts.Values.Max();
        var repetitionRatio = maxRepeat / (double)words.Count;
        return repetitionRatio >= 0.70d;
    }

    // Z Buzz: proste scalanie overlapu segmentów ogranicza powtórzenia przy chunkach z kontekstem.
    private static string MergeTextNoOverlap(string previous, string next)
    {
        if (string.IsNullOrWhiteSpace(previous))
        {
            return next.Trim();
        }

        if (string.IsNullOrWhiteSpace(next))
        {
            return previous.Trim();
        }

        previous = previous.TrimEnd();
        next = next.TrimStart();

        var overlap = 0;
        var maxOverlap = Math.Min(previous.Length, next.Length);
        for (var i = 1; i <= maxOverlap; i++)
        {
            if (string.Equals(previous[^i..], next[..i], StringComparison.OrdinalIgnoreCase))
            {
                overlap = i;
            }
        }

        if (overlap == 0)
        {
            return $"{previous} {next}".Trim();
        }

        return (previous + next[overlap..]).Trim();
    }
}
