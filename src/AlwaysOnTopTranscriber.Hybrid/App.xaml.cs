using AlwaysOnTopTranscriber.Hybrid.Services.System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using UIXamlWindow = Microsoft.UI.Xaml.Window;

namespace AlwaysOnTopTranscriber.Hybrid;

public partial class App : Application
{
    private readonly ITrayService _trayService;

    public App(ITrayService trayService)
    {
        _trayService = trayService;
        InitializeComponent();
        MainPage = new MainPage();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
        try
        {
            _trayService.Initialize();
        }
        catch
        {
            // Tray jest opcjonalny; aplikacja ma działać nawet jeśli host systemowy zgłosi błąd.
        }

        window.Created += HandleWindowCreated;
        window.Destroying += HandleWindowDestroying;
        return window;
    }

    private void HandleWindowCreated(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Created -= HandleWindowCreated;
        TrySetTaskbarIcon(window);
    }

    private void HandleWindowDestroying(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Destroying -= HandleWindowDestroying;
        }

        try
        {
            _trayService.Shutdown();
        }
        catch
        {
            // Ignored on shutdown.
        }
    }

    private static void TrySetTaskbarIcon(Window window)
    {
        try
        {
            var nativeWindow = window.Handler?.PlatformView as UIXamlWindow;
            if (nativeWindow is null)
            {
                return;
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "appicon.ico");
            if (!File.Exists(iconPath))
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(nativeWindow);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon(iconPath);
        }
        catch
        {
            // Ignore icon setup errors to avoid startup regressions.
        }
    }
}
