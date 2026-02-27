using System.Drawing;
using AlwaysOnTopTranscriber.Hybrid.Services.System;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using WinRT.Interop;
using Forms = System.Windows.Forms;
using UIXamlWindow = Microsoft.UI.Xaml.Window;
using System.Runtime.InteropServices;

namespace AlwaysOnTopTranscriber.Hybrid.Services.Windows;

public sealed class WindowsTrayService(
    ILogger<WindowsTrayService> logger,
    IWindowCoordinator windowCoordinator,
    IMiniWidgetHost miniWidgetHost) : ITrayService
{
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ContextMenuStrip? _contextMenu;
    private Icon? _icon;
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (_initialized)
                    {
                        return;
                    }

                    _contextMenu = BuildContextMenu();
                    _icon = ResolveTrayIcon();
                    _notifyIcon = new Forms.NotifyIcon
                    {
                        Text = "Transcriber v1.2",
                        Visible = true,
                        Icon = _icon,
                        ContextMenuStrip = _contextMenu
                    };
                    _notifyIcon.DoubleClick += HandleOpenRequested;

                    _initialized = true;
                    logger.LogInformation("Windows tray icon initialized.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Tray initialization failed.");
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to invoke tray initialization on main thread.");
        }
    }

    public void Shutdown()
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    if (!_initialized)
                    {
                        return;
                    }

                    if (_notifyIcon is not null)
                    {
                        _notifyIcon.DoubleClick -= HandleOpenRequested;
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                        _notifyIcon = null;
                    }

                    _contextMenu?.Dispose();
                    _contextMenu = null;

                    _icon?.Dispose();
                    _icon = null;

                    _initialized = false;
                    logger.LogInformation("Windows tray icon shutdown.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Tray shutdown failed.");
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to invoke tray shutdown on main thread.");
        }
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        menu.Items.Add("Otworz panel", null, (_, _) =>
        {
            windowCoordinator.ShowLive();
            ActivateMainWindow();
        });
        menu.Items.Add("Live", null, (_, _) =>
        {
            windowCoordinator.ShowLive();
            ActivateMainWindow();
        });
        menu.Items.Add("Sesje", null, (_, _) =>
        {
            windowCoordinator.ShowSessions();
            ActivateMainWindow();
        });
        menu.Items.Add("Ustawienia", null, (_, _) =>
        {
            windowCoordinator.ShowSettings();
            ActivateMainWindow();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Zamknij", null, (_, _) =>
        {
            Shutdown();
            MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit());
        });

        return menu;
    }

    private static Icon ResolveTrayIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "appicon.ico");
        if (File.Exists(icoPath))
        {
            try
            {
                return new Icon(icoPath);
            }
            catch
            {
                // Fallback to PNG / process icon.
            }
        }

        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "logoo.png"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png")
        };

        foreach (var path in candidatePaths)
        {
            var iconFromPng = TryCreateIconFromPng(path);
            if (iconFromPng is not null)
            {
                return iconFromPng;
            }
        }

        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var extracted = Icon.ExtractAssociatedIcon(processPath);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
        }
        catch
        {
            // fallback below
        }

        return SystemIcons.Application;
    }

    private static Icon? TryCreateIconFromPng(string pngPath)
    {
        try
        {
            if (!File.Exists(pngPath))
            {
                return null;
            }

            using var bitmap = new Bitmap(pngPath);
            var handle = bitmap.GetHicon();
            try
            {
                using var icon = Icon.FromHandle(handle);
                return (Icon)icon.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    private void HandleOpenRequested(object? sender, EventArgs e)
    {
        windowCoordinator.ShowLive();
        ActivateMainWindow();
    }

    private void ActivateMainWindow()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            miniWidgetHost.Hide();

            var mauiWindow = Application.Current?.Windows.FirstOrDefault();
            var nativeWindow = mauiWindow?.Handler?.PlatformView as UIXamlWindow;
            if (nativeWindow is null)
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(nativeWindow);
            ShowWindow(hwnd, SwRestore);
            SetForegroundWindow(hwnd);
        });
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
