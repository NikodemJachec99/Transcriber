using System;
using System.Linq;
using AlwaysOnTopTranscriber.Hybrid.Services.System;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using UIXamlWindow = Microsoft.UI.Xaml.Window;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace AlwaysOnTopTranscriber.Hybrid.Services.Windows;

public sealed class WindowsMiniWidgetHost(ILogger<WindowsMiniWidgetHost> logger) : IMiniWidgetHost
{
    private const int GwlExStyle = -20;
    private const int WmSysCommand = 0x0112;
    private const int ScMove = 0xF010;
    private const int HtCaption = 0x0002;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;
    private const byte MiniAlpha = 238;
    private const int DefaultMiniCornerRadiusDip = 18;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpDefault = 0;
    private const uint DwmColorDefault = 0xFFFFFFFF;
    private const uint DwmColorNone = 0xFFFFFFFE;

    private bool _hasSavedBounds;
    private PointInt32 _savedPosition;
    private SizeInt32 _savedSize;

    public bool IsVisible { get; private set; }
    public int MiniWidgetCornerRadiusDip => DefaultMiniCornerRadiusDip;

    public void Show()
    {
        try
        {
            if (!TryGetWindowContext(out var appWindow, out var presenter, out var hwnd))
            {
                logger.LogWarning("Unable to enter mini widget mode.");
                return;
            }

            if (!_hasSavedBounds)
            {
                _savedPosition = appWindow.Position;
                _savedSize = appWindow.Size;
                _hasSavedBounds = true;
            }

            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;

            var displayArea = DisplayArea.GetFromWindowId(
                appWindow.Id,
                DisplayAreaFallback.Primary);

            var width = 424;
            var height = 214;
            var workArea = displayArea.WorkArea;
            var x = workArea.X + Math.Max(0, workArea.Width - width - 24);
            var y = workArea.Y + Math.Max(0, workArea.Height - height - 56);

            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(x, y));

            SetCornerPreference(hwnd, DwmwcpDefault);
            SetBorderColor(hwnd, DwmColorNone);
            SetWindowTransparency(hwnd, MiniAlpha);
            ApplyRoundedRegion(hwnd, MiniWidgetCornerRadiusDip);
            IsVisible = true;
            logger.LogInformation("Mini widget mode enabled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enter mini widget mode.");
        }
    }

    public void Hide()
    {
        try
        {
            if (!TryGetWindowContext(out var appWindow, out var presenter, out var hwnd))
            {
                logger.LogWarning("Unable to exit mini widget mode.");
                return;
            }

            presenter.IsAlwaysOnTop = false;
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            presenter.SetBorderAndTitleBar(true, true);

            if (_hasSavedBounds)
            {
                appWindow.Resize(_savedSize);
                appWindow.Move(_savedPosition);
                _hasSavedBounds = false;
            }

            ClearRoundedRegion(hwnd);
            SetCornerPreference(hwnd, DwmwcpDefault);
            SetBorderColor(hwnd, DwmColorDefault);
            ResetWindowTransparency(hwnd);
            IsVisible = false;
            logger.LogInformation("Mini widget mode disabled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to exit mini widget mode.");
        }
    }

    public void BeginDrag()
    {
        try
        {
            if (!TryGetWindowContext(out _, out _, out var hwnd))
            {
                return;
            }

            ReleaseCapture();
            SendMessage(hwnd, WmSysCommand, (IntPtr)(ScMove + HtCaption), IntPtr.Zero);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to drag mini widget window.");
        }
    }

    private static void SetCornerPreference(IntPtr hwnd, int cornerPreference)
    {
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());
    }

    private static void SetBorderColor(IntPtr hwnd, uint color)
    {
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmwaBorderColor,
            ref color,
            Marshal.SizeOf<uint>());
    }

    private static void SetWindowTransparency(IntPtr hwnd, byte alpha)
    {
        var currentStyle = GetWindowLong(hwnd, GwlExStyle);
        if ((currentStyle & WsExLayered) == 0)
        {
            SetWindowLong(hwnd, GwlExStyle, currentStyle | WsExLayered);
        }

        SetLayeredWindowAttributes(hwnd, 0, alpha, LwaAlpha);
    }

    private static void ResetWindowTransparency(IntPtr hwnd)
    {
        var currentStyle = GetWindowLong(hwnd, GwlExStyle);
        if ((currentStyle & WsExLayered) != 0)
        {
            SetWindowLong(hwnd, GwlExStyle, currentStyle & ~WsExLayered);
        }
    }

    private static void ApplyRoundedRegion(IntPtr hwnd, int cornerRadiusDip)
    {
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        var radiusPx = Math.Max(2, (int)Math.Round(cornerRadiusDip * (dpi / 96d)));
        var diameter = radiusPx * 2;

        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region == IntPtr.Zero)
        {
            return;
        }

        // System przejmuje ownership regionu przy sukcesie SetWindowRgn.
        if (SetWindowRgn(hwnd, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private static void ClearRoundedRegion(IntPtr hwnd)
    {
        _ = SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    private static bool TryGetWindowContext(
        out AppWindow appWindow,
        out OverlappedPresenter presenter,
        out IntPtr hwnd)
    {
        appWindow = null!;
        presenter = null!;
        hwnd = IntPtr.Zero;

        var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        var nativeWindow = mauiWindow?.Handler?.PlatformView as UIXamlWindow;
        if (nativeWindow is null)
        {
            return false;
        }

        hwnd = WindowNative.GetWindowHandle(nativeWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        appWindow = AppWindow.GetFromWindowId(windowId);

        var overlapped = appWindow.Presenter as OverlappedPresenter;
        if (overlapped is null)
        {
            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            overlapped = appWindow.Presenter as OverlappedPresenter;
            if (overlapped is null)
            {
                return false;
            }
        }

        presenter = overlapped;
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr hwnd,
        uint crKey,
        byte bAlpha,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(
        int nLeftRect,
        int nTopRect,
        int nRightRect,
        int nBottomRect,
        int nWidthEllipse,
        int nHeightEllipse);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
