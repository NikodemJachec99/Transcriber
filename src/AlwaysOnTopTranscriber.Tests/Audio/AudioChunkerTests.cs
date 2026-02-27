using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Audio;
using AlwaysOnTopTranscriber.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

namespace AlwaysOnTopTranscriber.Tests.Audio;

public sealed class AudioChunkerTests
{
    [Fact]
    public async Task RunAsync_EmitsExpectedOffsetsAndChunkCount_ForFiveSecondChunks()
    {
        var chunker = new AudioChunker(NullLogger<AudioChunker>.Instance);
        var sourceFrames = BuildSilenceFrames(totalSeconds: 12, frameSeconds: 1, sampleRate: 16_000);
        var options = new ChunkingOptions
        {
            ChunkLengthSeconds = 5,
            TargetSampleRate = 16_000,
            EmitPartialChunkOnComplete = true,
            SkipSilentChunks = false
        };

        var chunks = await CollectAsync(chunker.RunAsync(sourceFrames, options, CancellationToken.None));

        Assert.Equal(3, chunks.Count);
        Assert.Equal(TimeSpan.Zero, chunks[0].StartOffset);
        Assert.Equal(TimeSpan.FromSeconds(5), chunks[1].StartOffset);
        Assert.Equal(TimeSpan.FromSeconds(10), chunks[2].StartOffset);
        Assert.Equal(TimeSpan.FromSeconds(2), chunks[2].Duration);
    }

    [Fact]
    public async Task RunAsync_EmitsTailChunkOnComplete()
    {
        var chunker = new AudioChunker(NullLogger<AudioChunker>.Instance);
        var sourceFrames = BuildSilenceFrames(totalSeconds: 6, frameSeconds: 2, sampleRate: 16_000);
        var options = new ChunkingOptions
        {
            ChunkLengthSeconds = 5,
            TargetSampleRate = 16_000,
            EmitPartialChunkOnComplete = true,
            SkipSilentChunks = false
        };

        var chunks = await CollectAsync(chunker.RunAsync(sourceFrames, options, CancellationToken.None));

        Assert.Equal(2, chunks.Count);
        Assert.Equal(TimeSpan.FromSeconds(5), chunks[1].StartOffset);
        Assert.Equal(TimeSpan.FromSeconds(1), chunks[1].Duration);
    }

    [Fact]
    public async Task RunAsync_SkipsSilentChunks_WhenEnabled()
    {
        var chunker = new AudioChunker(NullLogger<AudioChunker>.Instance);
        var sourceFrames = BuildSilenceFrames(totalSeconds: 10, frameSeconds: 1, sampleRate: 16_000);
        var options = new ChunkingOptions
        {
            ChunkLengthSeconds = 5,
            TargetSampleRate = 16_000,
            EmitPartialChunkOnComplete = true,
            SkipSilentChunks = true
        };

        var chunks = await CollectAsync(chunker.RunAsync(sourceFrames, options, CancellationToken.None));
        Assert.Empty(chunks);
    }

    private static async IAsyncEnumerable<AudioFrame> BuildSilenceFrames(int totalSeconds, int frameSeconds, int sampleRate)
    {
        var frameCount = totalSeconds / frameSeconds;
        var bytesPerSecond = sampleRate * sizeof(short);
        var frameBytes = bytesPerSecond * frameSeconds;

        for (var i = 0; i < frameCount; i++)
        {
            yield return new AudioFrame(
                new byte[frameBytes],
                sampleRate,
                16,
                1,
                WaveFormatEncoding.Pcm,
                TimeSpan.FromSeconds(i * frameSeconds),
                DateTimeOffset.UtcNow);
            await Task.Yield();
        }
    }

    private static async Task<List<AudioChunk>> CollectAsync(IAsyncEnumerable<AudioChunk> source)
    {
        var result = new List<AudioChunk>();
        await foreach (var item in source)
        {
            result.Add(item);
        }

        return result;
    }
}
