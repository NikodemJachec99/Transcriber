using System;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class AppSettings
{
    public EngineMode Engine { get; set; } = EngineMode.Local;

    public string ModelName { get; set; } = "base";

    public string? ModelPath { get; set; }

    public int ChunkLengthSeconds { get; set; } = 10;

    public float SilenceRmsThreshold { get; set; } = 0.003f;

    public bool AutoPunctuation { get; set; } = true;

    public string Language { get; set; } = "auto";

    public int MaxBufferedAudioFrames { get; set; } = 2048;

    public TranscriptDisplayMode TranscriptDisplayMode { get; set; } = TranscriptDisplayMode.AppendAndCorrect;

    public string LineSeparator { get; set; } = "\n\n";

    public bool StartMinimizedToTray { get; set; }

    public bool HotkeysEnabled { get; set; } = true;

    public string ToggleRecordingHotkey { get; set; } = "Ctrl+Alt+R";

    public string OpenPanelHotkey { get; set; } = "Ctrl+Alt+P";

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public WidgetBounds WidgetBounds { get; set; } = new();

    public WidgetBounds MiniWidgetBounds { get; set; } = new()
    {
        Left = 80,
        Top = 80,
        Width = 420,
        Height = 252
    };

    public int WidgetOpacityPercent { get; set; } = 92;

    public bool DarkMode { get; set; } = true;

    public string UiLanguage { get; set; } = "pl";

    public string UiMode { get; set; } = "basic";

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Włącz live transkrypcję - wyłączenie oszczędza CPU/RAM dla długich sesji.
    /// Transkrypcja będzie zapisywana bez względu na tę ustawienie.
    /// </summary>
    public bool EnableLiveTranscript { get; set; } = false;

    /// <summary>
    /// Włącz transkrypcję opóźnioną (deferred) - nagrywaj szybko, transkrybuj ręcznie później.
    /// Na słabym sprzęcie eliminuje dropsy audio. Domyślnie ON dla <8GB RAM.
    /// </summary>
    public bool EnableDeferredTranscription { get; set; } = true;

    /// <summary>
    /// Spróbuj użyć GPU acceleration dla Whisper.net.
    /// Wymaga zainstalowanych odpowiednich pakietów runtime (Cuda, OpenVino).
    /// Fallback do CPU jeśli GPU niedostępna.
    /// </summary>
    public bool TryGpuAcceleration { get; set; } = true;

    /// <summary>
    /// Dostawca GPU: "auto" (auto-detect), "cuda" (NVIDIA RTX/GTX),
    /// "openvino" (AMD Vega/Intel Arc), "rocm" (AMD Linux), "cpu" (force CPU).
    /// Note: Whisper.net.Runtime.Cuda i Whisper.net.Runtime.OpenVino są wymagane.
    /// </summary>
    public string GpuProvider { get; set; } = "auto";
}

public sealed class WidgetBounds
{
    public double Left { get; set; } = 40;

    public double Top { get; set; } = 40;

    public double Width { get; set; } = 440;

    public double Height { get; set; } = 320;
}

public enum EngineMode
{
    Local = 0,
    Cloud = 1
}

public enum TranscriptDisplayMode
{
    AppendAndCorrect = 0,
    AppendBelow = 1,
    AppendAbove = 2
}

public enum SessionState
{
    Idle = 0,           // Brak sesji
    Recording = 1,      // Nagrywanie w toku
    Recorded = 2,       // Nagranie bez transkrypcji (new - for deferred mode)
    Transcribing = 3,   // Transkrypcja w toku
    Completed = 4       // Sesja gotowa
}
