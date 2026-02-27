namespace AlwaysOnTopTranscriber.Hybrid.Services.System;

public interface IWindowCoordinator
{
    string CurrentRoute { get; }

    event Action<string>? RouteChanged;

    void ShowLive();

    void ShowSessions();

    void ShowSettings();

    void ShowMiniWidget();
}
