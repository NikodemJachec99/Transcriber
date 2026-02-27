using System;
using NAudio.Wave;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed record AudioFrame(
    byte[] Buffer,
    int SampleRate,
    int BitsPerSample,
    int Channels,
    WaveFormatEncoding Encoding,
    TimeSpan CaptureOffset,
    DateTimeOffset CapturedAtUtc);
