namespace AlwaysOnTopTranscriber.Hybrid.Services.System;

public interface IMiniWidgetHost
{
    bool IsVisible { get; }
    int MiniWidgetCornerRadiusDip { get; }

    void Show();

    void Hide();

    void BeginDrag();
}
