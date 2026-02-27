using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using AlwaysOnTopTranscriber.Core.Models;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

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

            var converted = ConvertFrameToPcm16Mono(frame, targetRate);
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
                if (options.SkipSilentChunks && IsSilentChunk(chunkBytes, options.SilenceRmsThreshold))
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
            if (options.SkipSilentChunks && IsSilentChunk(tailChunk, options.SilenceRmsThreshold))
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

    private byte[] ConvertFrameToPcm16Mono(AudioFrame frame, int targetSampleRate)
    {
        if (frame.SampleRate <= 0 || frame.Channels <= 0)
        {
            return [];
        }

        var monoSamples = DecodeToMonoFloat(frame);
        if (monoSamples.Length == 0)
        {
            return [];
        }

        var resampled = frame.SampleRate == targetSampleRate
            ? monoSamples
            : Resample(monoSamples, frame.SampleRate, targetSampleRate);

        var output = new byte[resampled.Length * sizeof(short)];
        for (var i = 0; i < resampled.Length; i++)
        {
            var clamped = Math.Clamp(resampled[i], -1.0f, 1.0f);
            var pcm = (short)Math.Round(clamped * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(i * sizeof(short), sizeof(short)), pcm);
        }

        return output;
    }

    private float[] DecodeToMonoFloat(AudioFrame frame)
    {
        var bytesPerSample = Math.Max(1, frame.BitsPerSample / 8);
        var bytesPerFrame = bytesPerSample * frame.Channels;
        if (bytesPerFrame <= 0 || frame.Buffer.Length < bytesPerFrame)
        {
            return [];
        }

        var sampleCount = frame.Buffer.Length / bytesPerFrame;
        var mono = new float[sampleCount];

        for (var frameIndex = 0; frameIndex < sampleCount; frameIndex++)
        {
            var frameOffset = frameIndex * bytesPerFrame;
            float sum = 0;
            for (var channel = 0; channel < frame.Channels; channel++)
            {
                var sampleOffset = frameOffset + (channel * bytesPerSample);
                sum += ReadSample(frame.Buffer, sampleOffset, frame.Encoding, frame.BitsPerSample);
            }

            mono[frameIndex] = sum / frame.Channels;
        }

        return mono;
    }

    private float ReadSample(byte[] buffer, int offset, WaveFormatEncoding encoding, int bitsPerSample)
    {
        if (encoding == WaveFormatEncoding.IeeeFloat && bitsPerSample == 32)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        if (encoding == WaveFormatEncoding.Pcm && bitsPerSample == 16)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, sizeof(short)));
            return sample / (float)short.MaxValue;
        }

        if (encoding == WaveFormatEncoding.Pcm && bitsPerSample == 24)
        {
            var b0 = buffer[offset];
            var b1 = buffer[offset + 1];
            var b2 = buffer[offset + 2];
            var value = b0 | (b1 << 8) | (b2 << 16);
            if ((value & 0x800000) != 0)
            {
                value |= unchecked((int)0xFF000000);
            }

            return value / 8_388_608f;
        }

        if (encoding == WaveFormatEncoding.Pcm && bitsPerSample == 32)
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset, sizeof(int)));
            return value / (float)int.MaxValue;
        }

        _logger.LogWarning("Nieobsługiwany format audio: {Encoding} {Bits}bit", encoding, bitsPerSample);
        return 0;
    }

    private static float[] Resample(float[] input, int sourceSampleRate, int targetSampleRate)
    {
        if (input.Length == 0)
        {
            return [];
        }

        if (sourceSampleRate == targetSampleRate)
        {
            return input;
        }

        var outputLength = Math.Max(1, (int)Math.Round(input.Length * targetSampleRate / (double)sourceSampleRate));
        var output = new float[outputLength];

        for (var i = 0; i < outputLength; i++)
        {
            var sourcePosition = i * sourceSampleRate / (double)targetSampleRate;
            var leftIndex = (int)Math.Floor(sourcePosition);
            var rightIndex = Math.Min(leftIndex + 1, input.Length - 1);
            var fraction = sourcePosition - leftIndex;
            var left = input[Math.Clamp(leftIndex, 0, input.Length - 1)];
            var right = input[Math.Clamp(rightIndex, 0, input.Length - 1)];
            output[i] = (float)(left + ((right - left) * fraction));
        }

        return output;
    }

    // Inspirowane rozwiązaniami realtime-whisper: energy-based gate ogranicza puste transkrypcje i koszty CPU.
    private static bool IsSilentChunk(byte[] pcm16Bytes, float rmsThreshold)
    {
        if (pcm16Bytes.Length < sizeof(short))
        {
            return true;
        }

        var sampleCount = pcm16Bytes.Length / sizeof(short);
        double sumSquares = 0;
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(
                pcm16Bytes.AsSpan(i * sizeof(short), sizeof(short)));
            var normalized = sample / (double)short.MaxValue;
            sumSquares += normalized * normalized;
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        return rms < rmsThreshold;
    }
}
