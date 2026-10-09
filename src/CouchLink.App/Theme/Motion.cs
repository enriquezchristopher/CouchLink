using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace CouchLink.App.Theme;

/// <summary>
/// Every duration, distance and easing in the app, and the code-driven transitions (spec 3.5). Templates read
/// the same values as resources (<see cref="Resources"/>), which ThemeManager swaps when Reduce motion changes;
/// the helpers here check <see cref="Reduced"/> when they start. With Reduce motion on, everything is instant or
/// a plain fade of at most 120 ms, with no movement or scaling. Only Opacity and RenderTransforms animate, never
/// layout. The approval countdown bar and the spinners are not here: they always move.
/// </summary>
internal static class Motion
{
    // Templates (Theme/Controls.xaml)
    public static readonly TimeSpan Hover = Ms(150), PressDown = Ms(90), PressUp = Ms(160), Move = Ms(180), Reveal = Ms(180);
    public const double PressScale = 0.97, RevealRise = 6;

    // Code-driven
    public static readonly TimeSpan ScreenOut = Ms(120), ScreenIn = Ms(240), ListItem = Ms(220), ListStep = Ms(30),
        Segment = Ms(220), DialogIn = Ms(200), DialogOut = Ms(120), PopupIn = Ms(280), PopupOut = Ms(160), BannerIn = Ms(200);
    public const double ScreenRise = 12, ListRise = 8, BannerDrop = 6, PopupSlide = 40, DialogScale = 0.96;
    public const int MaxStaggered = 10;

    /// <summary>The plain fade that stands in for an entrance with Reduce motion on.</summary>
    public static readonly TimeSpan ReducedFade = Ms(100);

    /// <summary>Ease-out for everything; the press spring-back overshoots a little.</summary>
    public static readonly IEasingFunction EaseOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseSpring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 });

    /// <summary>Help → Reduce motion. Set through ThemeManager.SetReducedMotion, which also swaps the template values.</summary>
    public static bool Reduced { get; internal set; }

    /// <summary>The keys of <see cref="Resources"/>, used by Theme/Controls.xaml.</summary>
    public static IReadOnlyList<string> ResourceKeys { get; } =
        ["MotionHover", "MotionPressDown", "MotionPressUp", "MotionMove", "MotionReveal", "PressScale", "RevealRise", "EaseOut", "EaseSpring"];

    /// <summary>The values Theme/Controls.xaml's storyboards read with StaticResource.</summary>
    public static ResourceDictionary Resources(bool reduced) => new()
    {
        ["MotionHover"] = new Duration(reduced ? ReducedFade : Hover),
        ["MotionPressDown"] = new Duration(reduced ? TimeSpan.Zero : PressDown),
        ["MotionPressUp"] = new Duration(reduced ? TimeSpan.Zero : PressUp),
        ["MotionMove"] = new Duration(reduced ? TimeSpan.Zero : Move),
        ["MotionReveal"] = new Duration(reduced ? ReducedFade : Reveal),
        ["PressScale"] = reduced ? 1.0 : PressScale,
        ["RevealRise"] = reduced ? 0.0 : RevealRise,
        ["EaseOut"] = EaseOut,
        ["EaseSpring"] = EaseSpring,
    };

    /// <summary>
    /// A main-window screen comes in: fades in and rises 12 px, after <paramref name="delay"/> (the old screen's
    /// fade-out). With Reduce motion on, a plain short fade.
    /// </summary>
    public static void EnterScreen(UIElement view, TimeSpan delay)
    {
        if (Reduced)
            Appear(view, TimeSpan.Zero, ReducedFade, rise: 0);
        else
            Appear(view, delay, ScreenIn, ScreenRise);
    }

    /// <summary>
    /// A picture of the old screen, taken before it is disposed, so it can fade out while whatever it owned is
    /// already freed. Null when there is nothing to show or motion is reduced.
    /// </summary>
    public static ImageSource? Snapshot(FrameworkElement view)
    {
        if (Reduced || !view.IsVisible || view.ActualWidth < 1 || view.ActualHeight < 1)
            return null;
        var dpi = VisualTreeHelper.GetDpi(view);
        var size = new Size(view.ActualWidth, view.ActualHeight);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var picture = new DrawingVisual();
        using (var context = picture.RenderOpen())
        {
            var brush = new VisualBrush(view)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(size),
            };
            context.DrawRectangle(brush, null, new Rect(size));
        }
        bitmap.Render(picture);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The old screen's picture fades out (120 ms), then <paramref name="done"/>.</summary>
    public static void ExitScreen(UIElement snapshot, Action done)
    {
        var fade = new DoubleAnimation(1, 0, ScreenOut) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => done();
        snapshot.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>List items fade in and rise 8 px one after another, 30 ms apart; past the 10th they come with the 10th.</summary>
    public static void Stagger(IEnumerable<UIElement> items)
    {
        int index = 0;
        foreach (var item in items)
            RiseIn(item, index++);
    }

    /// <summary>One list item at its place in a <see cref="Stagger"/>. Nothing with Reduce motion on.</summary>
    public static void RiseIn(UIElement item, int index)
    {
        if (Reduced)
            return;
        Appear(item, ListStep * Math.Min(index, MaxStaggered - 1), ListItem, ListRise);
    }

    /// <summary>A banner shows: it slides down 6 px and fades in. Nothing with Reduce motion on.</summary>
    public static void DropIn(UIElement banner)
    {
        if (Reduced)
            return;
        Appear(banner, TimeSpan.Zero, BannerIn, -BannerDrop);
    }

    /// <summary>The segmented control's indicator glides to <paramref name="x"/>; with Reduce motion on it jumps.</summary>
    public static void SlideTo(TranslateTransform slide, double x)
    {
        if (Reduced)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            slide.X = x;
            return;
        }
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, Segment) { EasingFunction = EaseOut }, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>A dialog's content fades in and grows from 96 % (200 ms); with Reduce motion on, a plain fade.</summary>
    public static void PopIn(FrameworkElement body)
    {
        if (Reduced)
        {
            Fade(body, 0, 1, ReducedFade);
            return;
        }
        var scale = Transform<ScaleTransform>(body);
        body.RenderTransformOrigin = new Point(0.5, 0.5);
        Fade(body, 0, 1, DialogIn);
        var grow = new DoubleAnimation(DialogScale, 1, DialogIn) { EasingFunction = EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>The approval popup's card slides in from the right (40 px) and fades in; with Reduce motion on, a plain fade.</summary>
    public static void SlideInFromRight(UIElement card)
    {
        if (Reduced)
        {
            Fade(card, 0, 1, ReducedFade);
            return;
        }
        var slide = Transform<TranslateTransform>(card);
        Fade(card, 0, 1, PopupIn);
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(PopupSlide, 0, PopupIn) { EasingFunction = EaseOut }, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>The card slides back out to the right and fades (160 ms), then <paramref name="done"/>.</summary>
    public static void SlideOutToRight(UIElement card, Action done)
    {
        var slide = Transform<TranslateTransform>(card);
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(PopupSlide, PopupOut) { EasingFunction = EaseOut }, HandoffBehavior.SnapshotAndReplace);
        FadeOut(card, PopupOut, done);
    }

    /// <summary>
    /// A themed window's open and close: on load <paramref name="body"/> pops in; a close (OK, Cancel or Esc,
    /// Alt+F4, the close button) is held once while it fades out (120 ms), then goes ahead with the same
    /// DialogResult. With Reduce motion on, the open is a plain fade and the close is instant.
    /// </summary>
    public static void AnimateWindow(Window window, FrameworkElement body)
    {
        window.Loaded += (_, _) => PopIn(body);
        AnimateClose(window, done => FadeOut(body, DialogOut, done));
    }

    /// <summary>
    /// Holds the first close of a shown window while <paramref name="play"/> runs its exit animation, then closes
    /// it for real, keeping a dialog's DialogResult (WPF clears it when a close is cancelled). Closes during the
    /// animation are absorbed. Instant with Reduce motion on, for a window that isn't showing, and when Windows
    /// closes it regardless (app shutdown, owner closing).
    /// </summary>
    public static void AnimateClose(Window window, Action<Action> play)
    {
        bool playing = false, finishing = false, closed = false;
        window.Closed += (_, _) => closed = true;
        window.Closing += (_, e) =>
        {
            if (finishing || e.Cancel || Reduced || !window.IsVisible)
                return;
            e.Cancel = true;
            if (playing)
                return;
            playing = true;
            bool? result = window.DialogResult;
            play(() =>
            {
                if (closed)
                    return;
                finishing = true;
                if (result is { } answer && window.DialogResult != answer)
                    window.DialogResult = answer; // closes the dialog with the answer it was closing with
                else
                    window.Close();
            });
        };
    }

    private static void FadeOut(UIElement element, TimeSpan duration, Action done)
    {
        var fade = new DoubleAnimation(0, duration) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => done();
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Fade(UIElement element, double from, double to, TimeSpan duration) =>
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, to, duration) { EasingFunction = EaseOut }, HandoffBehavior.SnapshotAndReplace);

    /// <summary>
    /// Fades <paramref name="element"/> in from 0 and moves it <paramref name="rise"/> px up to its place, starting after
    /// <paramref name="delay"/>. Key frames hold it hidden during the delay, so its base values stay 1 and 0.
    /// </summary>
    private static void Appear(UIElement element, TimeSpan delay, TimeSpan duration, double rise)
    {
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > TimeSpan.Zero)
            fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay + duration), EaseOut));
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        if (rise == 0)
            return;
        var move = new DoubleAnimationUsingKeyFrames();
        move.KeyFrames.Add(new DiscreteDoubleKeyFrame(rise, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > TimeSpan.Zero)
            move.KeyFrames.Add(new DiscreteDoubleKeyFrame(rise, KeyTime.FromTimeSpan(delay)));
        move.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay + duration), EaseOut));
        Transform<TranslateTransform>(element).BeginAnimation(TranslateTransform.YProperty, move, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>The element's own transform of type <typeparamref name="T"/>, put in place if it has none yet.</summary>
    private static T Transform<T>(UIElement element) where T : Transform, new()
    {
        if (element.RenderTransform is T own && !own.IsFrozen)
            return own;
        var transform = new T();
        element.RenderTransform = transform;
        return transform;
    }

    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private static IEasingFunction Frozen(EasingFunctionBase ease)
    {
        ease.Freeze();
        return ease;
    }
}
