using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using AlwaysOnTopTranscriber.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlwaysOnTopTranscriber.Core.Audio;

public sealed class AudioChunker
{
    private readonly ILogger<AudioChunker> _logger;

    public AudioChunker(ILogger<AudioChunker> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<AudioChunk> RunAsync(
        IAsyncEnumerable<AudioFrame> frames,
        ChunkingOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var targetRate = options.TargetSampleRate <= 0 ? 16_000 : options.TargetSampleRate;
        var chunkLengthSeconds = options.ChunkLengthSeconds <= 0 ? 10 : options.ChunkLengthSeconds;
        var chunkSizeBytes = chunkLengthSeconds * targetRate * sizeof(short);

        using var buffer = new MemoryStream(capacity: chunkSizeBytes * 2);
        long readPosition = 0;
        var sequence = 0;
        var chunkOffset = TimeSpan.Zero;

        await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var converted = AudioConverter.ToPcm16Mono(frame, targetRate);
            if (converted.Length == 0)
            {
                continue;
            }

            buffer.Position = buffer.Length;
            buffer.Write(converted, 0, converted.Length);

            while (buffer.Length - readPosition >= chunkSizeBytes)
            {
                var chunkBytes = new byte[chunkSizeBytes];
                buffer.Position = readPosition;
                buffer.ReadExactly(chunkBytes, 0, chunkSizeBytes);
                readPosition += chunkSizeBytes;

                var duration = TimeSpan.FromSeconds((double)chunkBytes.Length / (targetRate * sizeof(short)));
                if (options.SkipSilentChunks && AudioConverter.IsSilentChunk(chunkBytes, options.SilenceRmsThreshold))
                {
                    _logger.LogDebug("Pomijam cichy chunk seq={Sequence} RMS poniżej progu.", sequence);
                    sequence++;
                    chunkOffset += duration;
                    continue;
                }

                yield return new AudioChunk(chunkBytes, targetRate, chunkOffset, duration, sequence++);
                chunkOffset += duration;
            }

            CompactIfNeeded(buffer, ref readPosition);
        }

        var remainingBytes = (int)(buffer.Length - readPosition);
        if (options.EmitPartialChunkOnComplete && remainingBytes > 0)
        {
            var tailChunk = new byte[remainingBytes];
            buffer.Position = readPosition;
            buffer.ReadExactly(tailChunk, 0, remainingBytes);

            var duration = TimeSpan.FromSeconds((double)tailChunk.Length / (targetRate * sizeof(short)));
            if (options.SkipSilentChunks && AudioConverter.IsSilentChunk(tailChunk, options.SilenceRmsThreshold))
            {
                yield break;
            }

            yield return new AudioChunk(tailChunk, targetRate, chunkOffset, duration, sequence);
        }
    }

    private static void CompactIfNeeded(MemoryStream stream, ref long readPosition)
    {
        if (readPosition == 0)
        {
            return;
        }

        if (readPosition == stream.Length)
        {
            stream.SetLength(0);
            readPosition = 0;
            return;
        }

        if (readPosition < 512 * 1024)
        {
            return;
        }

        var remaining = (int)(stream.Length - readPosition);
        var bytes = new byte[remaining];
        stream.Position = readPosition;
        stream.ReadExactly(bytes, 0, remaining);
        stream.SetLength(0);
        stream.Position = 0;
        stream.Write(bytes, 0, bytes.Length);
        readPosition = 0;
    }
}
