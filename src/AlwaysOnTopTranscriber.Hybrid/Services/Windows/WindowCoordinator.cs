using AlwaysOnTopTranscriber.Hybrid.Services.System;

namespace AlwaysOnTopTranscriber.Hybrid.Services.Windows;

public sealed class WindowCoordinator : IWindowCoordinator
{
    public string CurrentRoute { get; private set; } = "/";

    public event Action<string>? RouteChanged;

    public void ShowLive() => SetRoute("/");

    public void ShowSessions() => SetRoute("/sessions");

    public void ShowSettings() => SetRoute("/settings");

    public void ShowMiniWidget() => SetRoute("/mini-widget");

    private void SetRoute(string route)
    {
        CurrentRoute = route;
        RouteChanged?.Invoke(route);
    }
}
