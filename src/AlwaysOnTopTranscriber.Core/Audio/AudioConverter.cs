using System;
using System.Buffers.Binary;
using AlwaysOnTopTranscriber.Core.Models;
using NAudio.Wave;

namespace AlwaysOnTopTranscriber.Core.Audio;

internal static class AudioConverter
{
    internal static byte[] ToPcm16Mono(AudioFrame frame, int targetSampleRate)
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

    internal static bool IsSilentChunk(byte[] pcm16Bytes, float rmsThreshold)
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

    private static float[] DecodeToMonoFloat(AudioFrame frame)
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

    private static float ReadSample(byte[] buffer, int offset, WaveFormatEncoding encoding, int bitsPerSample)
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
}
