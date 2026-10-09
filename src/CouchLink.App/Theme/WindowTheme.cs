using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace CouchLink.App.Theme;

/// <summary>
/// What every window needs from the theme: background, text color, font, and a dark native title bar
/// (DWM; Windows 10 20H1 and later, with the caption color on Windows 11). Call from the constructor.
/// Windows with their own title bar also call <see cref="UseCustomChrome"/>.
/// </summary>
internal static partial class WindowTheme
{
    private const int DarkModeBefore20H1 = 19, DarkMode = 20, CornerPreference = 33, CaptionColor = 35, RoundPreference = 2;
    private const int SmCxSizeFrame = 32, SmCySizeFrame = 33, SmCxPaddedBorder = 92;
    private const int ChromeColorRef = 0x001A0B0B; // #0B0B1A as COLORREF (0x00BBGGRR)

    public static void Apply(Window window)
    {
        window.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        window.SetResourceReference(Control.FontFamilyProperty, "BodyFont");
        window.FontSize = 14;
        window.UseLayoutRounding = true;
        window.SourceInitialized += (_, _) => DarkTitleBar(new WindowInteropHelper(window).Handle);
    }

    /// <summary>
    /// Replaces the native title bar with the app's own (WindowChrome): the top <paramref name="captionHeight"/> px is the
    /// drag area, and the window keeps its shadow, Aero Snap, edge resizing and system menu. Wraps the window's content in a
    /// border that draws the Windows 10 outline and pads the content while maximized. Call after InitializeComponent.
    /// </summary>
    public static void UseCustomChrome(Window window, double captionHeight)
    {
        bool resizable = window.ResizeMode != ResizeMode.NoResize;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = captionHeight,
            ResizeBorderThickness = resizable ? SystemParameters.WindowResizeBorderThickness : new Thickness(0),
            GlassFrameThickness = new Thickness(0, 0, 0, 1), // keeps the DWM shadow without painting glass over the window
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        var outline = IsWindows11 ? new Thickness(0) : new Thickness(1);
        var root = new Border { BorderThickness = outline };
        root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var content = window.Content;
        window.Content = null;
        root.Child = content as UIElement;
        window.Content = root;

        void FitState()
        {
            bool maximized = window.WindowState == WindowState.Maximized;
            root.BorderThickness = maximized ? new Thickness(0) : outline;
            root.Padding = maximized ? HiddenFrame(window) : new Thickness(0);
        }

        window.StateChanged += (_, _) => FitState();
        window.DpiChanged += (_, _) => FitState();
        window.SourceInitialized += (_, _) =>
        {
            RoundCorners(new WindowInteropHelper(window).Handle);
            FitState();
        };
    }

    private static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    private static void RoundCorners(nint hwnd)
    {
        if (!IsWindows11)
            return;
        int round = RoundPreference;
        _ = DwmSetWindowAttribute(hwnd, CornerPreference, ref round, sizeof(int));
    }

    /// <summary>
    /// A maximized WindowChrome window extends past the screen by the frame Windows no longer draws; padding the content by
    /// that much keeps the header and buttons on screen.
    /// </summary>
    private static Thickness HiddenFrame(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        uint dpiX = (uint)Math.Round(dpi.PixelsPerInchX), dpiY = (uint)Math.Round(dpi.PixelsPerInchY);
        double x = (GetSystemMetricsForDpi(SmCxSizeFrame, dpiX) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpiX)) / dpi.DpiScaleX;
        double y = (GetSystemMetricsForDpi(SmCySizeFrame, dpiY) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpiY)) / dpi.DpiScaleY;
        return new Thickness(x, y, x, y);
    }

    private static void DarkTitleBar(nint hwnd)
    {
        if (SystemParameters.HighContrast)
            return; // Windows draws High Contrast title bars itself
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, DarkMode, ref on, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(hwnd, DarkModeBefore20H1, ref on, sizeof(int));
        int color = ChromeColorRef;
        _ = DwmSetWindowAttribute(hwnd, CaptionColor, ref color, sizeof(int)); // fails quietly before Windows 11
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);
}
