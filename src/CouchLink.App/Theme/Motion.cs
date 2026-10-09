using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CouchLink.App.Theme;

/// <summary>The one enter animation: fade in and slide up 8 px in 180 ms. Nothing when Windows animations are off.</summary>
internal static class Motion
{
    private static readonly Duration Enter180 = new(TimeSpan.FromMilliseconds(180));

    public static void Enter(UIElement element)
    {
        if (!ThemeManager.AnimationsOn)
            return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform(0, 8);
        element.RenderTransform = slide;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Enter180) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, Enter180) { EasingFunction = ease });
    }
}
