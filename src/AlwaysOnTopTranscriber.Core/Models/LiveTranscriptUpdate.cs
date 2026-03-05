using System;
using System.Collections.Generic;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class LiveTranscriptUpdate
{
    public required IReadOnlyList<TranscriptSegment> Segments { get; init; }

    public required IReadOnlyList<string> PreviewLines { get; init; }

    public required string FullText { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required float CurrentAudioLevel { get; init; }

    public required float SmoothedAudioLevel { get; init; }

    public required int PendingAudioFrames { get; init; }

    public required int PendingChunks { get; init; }

    public required int ProcessedChunks { get; init; }

    public required TimeSpan ProcessingLag { get; init; }

    /// <summary>
    /// Całkowita liczba chunks'ów do przetworzenia (dla deferred transcription).
    /// </summary>
    public required int TotalChunksToTranscribe { get; init; }

    /// <summary>
    /// Liczba już przetworzonych chunks'ów (dla deferred transcription).
    /// </summary>
    public required int TranscribedChunks { get; init; }

    /// <summary>
    /// True jeśli transkrypcja jest w toku (deferred mode).
    /// </summary>
    public required bool IsTranscriptionInProgress { get; init; }

    /// <summary>
    /// Procent postępu transkrypcji (0-100). Przydatne dla progress bar'a.
    /// </summary>
    public int TranscriptionProgressPercent => TotalChunksToTranscribe > 0
        ? (int)((TranscribedChunks * 100L) / TotalChunksToTranscribe)
        : 0;
}
