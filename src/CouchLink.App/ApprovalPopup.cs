using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.Core.Session;

namespace CouchLink.App;

/// <summary>
/// "PC-07 wants to join. Allow / Deny", topmost in the bottom-right corner over the game, without
/// taking the keyboard from it; the taskbar button flashes. Several stack upwards. Closing it with
/// the X denies. The session denies by itself after 30 s and closes it with <see cref="CloseByHost"/>.
/// </summary>
internal sealed partial class ApprovalPopup : Window
{
    private const uint FLASHW_ALL = 3, FLASHW_TIMERNOFG = 12;

    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _shown = Stopwatch.StartNew();
    private readonly TextBlock _remaining;
    private bool _answered;

    public ApprovalPopup(string name, int stackIndex, Action allow, Action deny)
    {
        Title = "CouchLink";
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowActivated = false;

        var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 300 };
        panel.Children.Add(new TextBlock { Text = $"{name} wants to join.", FontSize = 16, FontWeight = FontWeights.SemiBold });
        _remaining = new TextBlock { Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.Gray };
        panel.Children.Add(_remaining);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        buttons.Children.Add(MakeButton("Allow", () => Answer(allow)));
        buttons.Children.Add(MakeButton("Deny", () => Answer(deny)));
        panel.Children.Add(buttons);
        Content = panel;

        UpdateRemaining();
        _countdown.Tick += (_, _) => UpdateRemaining();
        _countdown.Start();
        Loaded += (_, _) => PlaceAt(stackIndex);
        SourceInitialized += (_, _) => Flash();
        Closed += (_, _) =>
        {
            _countdown.Stop();
            if (!_answered)
                deny();
        };
    }

    /// <summary>The session already answered (timed out, or the client gave up): close without denying again.</summary>
    public void CloseByHost()
    {
        _answered = true;
        Close();
    }

    private void Answer(Action answer)
    {
        _answered = true;
        answer();
        Close();
    }

    private void UpdateRemaining()
    {
        var left = HostSession.AskTimeout - _shown.Elapsed;
        _remaining.Text = $"Denied automatically in {Math.Max(0, (int)Math.Ceiling(left.TotalSeconds))} s";
    }

    private void PlaceAt(int stackIndex)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = Math.Max(area.Top, area.Bottom - (ActualHeight + 8) * (stackIndex + 1) - 8);
    }

    private void Flash()
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = new WindowInteropHelper(this).Handle,
            Flags = FLASHW_ALL | FLASHW_TIMERNOFG,
        };
        FlashWindowEx(ref info);
    }

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, MinWidth = 88, Height = 32, Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);
}
