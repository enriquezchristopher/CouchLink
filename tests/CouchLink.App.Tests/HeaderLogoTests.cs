using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using Path = System.Windows.Shapes.Path;

namespace CouchLink.App.Tests;

/// <summary>The logo at the left of the app header is the app icon: the controller on the violet tile.</summary>
public class HeaderLogoTests
{
    private static Path? FindPath(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is Path path)
                return path;
            if (FindPath(child) is { } found)
                return found;
        }
        return null;
    }

    [Fact]
    public void The_header_logo_draws_the_controller_and_is_not_an_empty_square()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var header = new AppHeader();

            var controller = FindPath(header.LogoMark);

            Assert.NotNull(controller);
            Assert.False(controller.Data.IsEmpty());
            // The d-pad and the two buttons are holes in the controller, not shapes on top of it.
            Assert.Equal(FillRule.EvenOdd, Assert.IsType<StreamGeometry>(controller.Data).FillRule);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(controller.Fill).Color);
        });
    }

    [Fact]
    public void The_header_logo_tile_has_the_icons_violet_gradient()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var header = new AppHeader();

            var tile = Assert.IsType<LinearGradientBrush>(header.LogoMark.Background);

            // The same two colors as the tile in assets/couchlink.svg.
            Assert.Equal(new[] { Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x5B, 0x21, 0xB6) },
                tile.GradientStops.OrderBy(s => s.Offset).Select(s => s.Color).ToArray());
        });
    }
}
