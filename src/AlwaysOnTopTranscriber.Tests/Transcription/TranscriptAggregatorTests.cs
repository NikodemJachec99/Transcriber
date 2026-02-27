using System;
using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Transcription;

namespace AlwaysOnTopTranscriber.Tests.Transcription;

public sealed class TranscriptAggregatorTests
{
    [Fact]
    public void Apply_DeduplicatesAndStabilizesOverlappingSegments()
    {
        var aggregator = new TranscriptAggregator();

        aggregator.Apply(new TranscriptionChunkResult
        {
            ChunkStartOffset = TimeSpan.Zero,
            Segments =
            [
                new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "hello"),
                new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "world")
            ]
        });

        aggregator.Apply(new TranscriptionChunkResult
        {
            ChunkStartOffset = TimeSpan.FromSeconds(2),
            Segments =
            [
                new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "world"),
                new TranscriptSegment(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "again")
            ]
        });

        var snapshot = aggregator.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal("hello", snapshot[0].Text);
        Assert.Equal("world", snapshot[1].Text);
        Assert.Equal("again", snapshot[2].Text);
    }

    [Fact]
    public void GetPreviewLines_ReturnsOnlyTail()
    {
        var aggregator = new TranscriptAggregator();
        for (var i = 0; i < 10; i++)
        {
            aggregator.Apply(new TranscriptionChunkResult
            {
                ChunkStartOffset = TimeSpan.FromSeconds(i),
                Segments = [new TranscriptSegment(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 1), $"line {i}")]
            });
        }

        var preview = aggregator.GetPreviewLines(6);
        Assert.Equal(6, preview.Count);
        Assert.Contains("line 9", preview[^1]);
        Assert.DoesNotContain(preview, line => line.Contains("line 0", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildTranscriptText_MergesOverlapBetweenSegments()
    {
        var aggregator = new TranscriptAggregator();
        aggregator.Apply(new TranscriptionChunkResult
        {
            ChunkStartOffset = TimeSpan.Zero,
            Segments =
            [
                new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "hello world"),
                new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "world again")
            ]
        });

        var text = aggregator.BuildTranscriptText();
        Assert.Equal("hello world again", text);
    }
}
