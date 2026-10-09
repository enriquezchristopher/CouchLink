using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace CouchLink.App.Theme;

/// <summary>
/// What every window needs from the theme: background, text color, font, and a dark native title bar
/// (DWM; Windows 10 20H1 and later, with the caption color on Windows 11). Call from the constructor.
/// </summary>
internal static partial class WindowTheme
{
    private const int DarkModeBefore20H1 = 19, DarkMode = 20, CaptionColor = 35;
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
}
