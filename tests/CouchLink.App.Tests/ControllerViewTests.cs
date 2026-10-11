using System.Windows;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.App.Tests;

public class ControllerViewTests
{
    private static readonly PadControl[] Ds4Parts =
    [
        PadControl.Cross, PadControl.Circle, PadControl.Square, PadControl.Triangle,
        PadControl.L1, PadControl.R1, PadControl.L2, PadControl.R2, PadControl.L3, PadControl.R3,
        PadControl.Options, PadControl.Share, PadControl.Touchpad,
        PadControl.DpadUp, PadControl.DpadDown, PadControl.DpadLeft, PadControl.DpadRight,
        PadControl.LeftUp, PadControl.RightUp,
    ];

    private static ControllerView View(PadKind kind)
    {
        ThemeManager.Install(Application.Current);
        return new ControllerView { Kind = kind };
    }

    [Fact]
    public void A_DualShock_has_every_part_with_a_name()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.DualShock4);
            Assert.Equal(Ds4Parts.Order(), view.Parts.Order());
            Assert.Equal("Cross, change key", view.PartName(PadControl.Cross));
            Assert.Equal("Left stick click, L3, change key", view.PartName(PadControl.L3));
            Assert.Equal("Left stick, show keys", view.PartName(PadControl.LeftUp));
        });
    }

    [Fact]
    public void An_Xbox_pad_has_no_touchpad_and_Xbox_names()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.Xbox360);
            Assert.Equal(Ds4Parts.Where(p => p != PadControl.Touchpad).Order(), view.Parts.Order());
            Assert.Equal("A, change key", view.PartName(PadControl.Cross));
            Assert.Equal("Start, change key", view.PartName(PadControl.Options));
        });
    }

    [Fact]
    public void Show_lights_exactly_the_pressed_parts()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.DualShock4);
            view.Show(PadPicture.From(PadState.Neutral with { Buttons = PadButtons.Cross | PadButtons.L1 }, PadKind.DualShock4));
            Assert.True(view.IsLit(PadControl.Cross));
            Assert.True(view.IsLit(PadControl.L1));
            Assert.False(view.IsLit(PadControl.Circle));
            view.Show(PadPicture.Neutral);
            Assert.False(view.IsLit(PadControl.Cross));
        });
    }

    [Fact]
    public void Clicking_a_part_raises_PartClicked()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.Xbox360);
            PadControl? clicked = null;
            view.PartClicked += c => clicked = c;
            view.ClickPart(PadControl.Square);
            Assert.Equal(PadControl.Square, clicked);
        });
    }

    [Fact]
    public void Switching_kind_rebuilds_the_parts()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.DualShock4);
            view.Kind = PadKind.Xbox360;
            Assert.DoesNotContain(PadControl.Touchpad, view.Parts);
        });
    }

    [Fact]
    public void Symmetric_mirrors_the_left_half()
    {
        string d = ControllerShapes.Symmetric((10, 0), [[(0, 0)], [(0, 5), (2, 9), (10, 10)]], cx: 10);
        Assert.Equal("M10 0 L0 0 C0 5 2 9 10 10 C18 9 20 5 20 0 L10 0 Z", d);
    }
}
