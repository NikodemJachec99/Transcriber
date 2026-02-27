namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class ChunkingOptions
{
    public int ChunkLengthSeconds { get; set; } = 10;

    public int TargetSampleRate { get; set; } = 16_000;

    public bool EmitPartialChunkOnComplete { get; set; } = true;

    public bool SkipSilentChunks { get; set; } = true;

    public float SilenceRmsThreshold { get; set; } = 0.003f;
}
