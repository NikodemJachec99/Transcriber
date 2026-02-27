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
