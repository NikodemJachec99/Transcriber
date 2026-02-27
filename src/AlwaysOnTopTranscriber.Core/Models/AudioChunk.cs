using System;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed record AudioChunk(
    byte[] Pcm16MonoData,
    int SampleRate,
    TimeSpan StartOffset,
    TimeSpan Duration,
    int SequenceNumber);
