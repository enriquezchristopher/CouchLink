using System.Windows;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.App.Tests;

/// <summary>The editor edits the app-wide AppServices.Controls, so each test resets it.</summary>
public class ControlsEditorTests
{
    // Default layout (KeyLayout.Defaults): Cross = K only, Square = J and left click. Num 5 is free.
    private const ushort K = 0x4B, J = 0x4A, Num5 = 0x65, Esc = 0x1B;

    private static void WithEditor(Action<ControlsWindow> test, PadKind kind = PadKind.DualShock4)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            AppServices.Controls.ResetToDefault();
            var editor = new ControlsWindow(kind);
            try
            {
                test(editor);
            }
            finally
            {
                editor.Close();
                AppServices.Controls.ResetToDefault();
            }
        });
    }

    [Fact]
    public void Find_hides_rows_and_groups_that_do_not_match()
    {
        WithEditor(editor =>
        {
            editor.FindBox.Text = "square";
            Assert.True(editor.IsRowVisible(PadControl.Square));
            Assert.False(editor.IsRowVisible(PadControl.Cross));
            Assert.True(editor.IsGroupVisible("Buttons"));
            Assert.False(editor.IsGroupVisible("Left stick"));
            editor.FindBox.Text = "";
            Assert.True(editor.IsRowVisible(PadControl.Cross));
            Assert.True(editor.IsGroupVisible("Left stick"));
        });
    }

    [Fact]
    public void Find_matches_labels()
    {
        WithEditor(editor =>
        {
            AppServices.Controls.SetLabel(PadControl.Square, "Shoot");
            editor.FindBox.Text = "shoot";
            Assert.True(editor.IsRowVisible(PadControl.Square));
            Assert.False(editor.IsRowVisible(PadControl.Circle));
        });
    }

    [Fact]
    public void Moving_a_key_marks_the_row_that_lost_it_and_says_so()
    {
        WithEditor(editor =>
        {
            editor.Listen(PadControl.Circle);
            editor.Bind(PadControl.Circle, K); // K is Cross's only key
            Assert.Equal(PadControl.Cross, editor.Moved);
            Assert.Equal(BannerKind.Warning, editor.MessageBanner.Kind);
            Assert.Equal("K moved here from Cross. Cross has no key now.", editor.MessageBanner.Text);
            Assert.Equal(Visibility.Visible, editor.MessageBanner.Visibility);
        });
    }

    [Fact]
    public void A_control_that_keeps_another_key_is_not_called_keyless()
    {
        WithEditor(editor =>
        {
            editor.Listen(PadControl.Circle);
            editor.Bind(PadControl.Circle, J); // Square keeps left click
            Assert.Equal(PadControl.Square, editor.Moved);
            Assert.Equal("J moved here from Square.", editor.MessageBanner.Text);
        });
    }

    [Fact]
    public void A_reserved_key_keeps_listening_and_says_why()
    {
        WithEditor(editor =>
        {
            editor.Listen(PadControl.Square);
            editor.Bind(PadControl.Square, Esc);
            Assert.Equal(PadControl.Square, editor.Listening);
            Assert.Equal("Esc is reserved. Press another key.", editor.MessageBanner.Text);
        });
    }

    [Fact]
    public void A_free_key_binds_without_a_warning()
    {
        WithEditor(editor =>
        {
            editor.Listen(PadControl.Square);
            editor.Bind(PadControl.Square, Num5);
            Assert.Null(editor.Moved);
            Assert.Null(editor.Listening);
            Assert.Equal(new ushort[] { Num5 }, AppServices.Controls.Layout.KeysFor(PadControl.Square)); // Bind replaces the row's keys
        });
    }

    [Fact]
    public void An_Xbox_editor_has_Xbox_rows_and_no_touchpad()
    {
        WithEditor(editor =>
        {
            Assert.False(editor.HasRow(PadControl.Touchpad));
            Assert.Equal("A", editor.RowAutomationName(PadControl.Cross));
            editor.FindBox.Text = "lb";
            Assert.True(editor.IsRowVisible(PadControl.L1));
            Assert.False(editor.IsRowVisible(PadControl.R1));
        }, PadKind.Xbox360);
    }

    [Fact]
    public void A_DualShock_editor_keeps_its_touchpad_row()
    {
        WithEditor(editor =>
        {
            Assert.True(editor.HasRow(PadControl.Touchpad));
            Assert.Equal("Cross", editor.RowAutomationName(PadControl.Cross));
        });
    }

    [Fact]
    public void A_held_key_lights_its_part_and_key_cap()
    {
        WithEditor(editor =>
        {
            editor.PreviewDown(K);
            editor.UpdatePreview();
            Assert.True(editor.Pad.IsLit(PadControl.Cross));
            Assert.True(editor.IsKeyCapLit(PadControl.Cross, K));
            editor.PreviewUp(K);
            editor.UpdatePreview();
            Assert.False(editor.Pad.IsLit(PadControl.Cross));
            Assert.False(editor.IsKeyCapLit(PadControl.Cross, K));
        });
    }

    [Fact]
    public void Clicking_a_part_starts_changing_its_key_and_outlines_it()
    {
        WithEditor(editor =>
        {
            editor.Pad.ClickPart(PadControl.Circle);
            Assert.Equal(PadControl.Circle, editor.Listening);
            Assert.Equal(PadControl.Circle, editor.Pad.Outlined);
            Assert.Equal("Changing Circle: press a key · Esc cancels", editor.PadCaption.Text);
        });
    }

    [Fact]
    public void Clicking_a_stick_ring_does_not_listen()
    {
        WithEditor(editor =>
        {
            editor.Pad.ClickPart(PadControl.LeftUp);
            Assert.Null(editor.Listening);
        });
    }

    [Fact]
    public void The_preview_pauses_while_listening()
    {
        WithEditor(editor =>
        {
            editor.PreviewDown(K);
            editor.Listen(PadControl.Circle);
            editor.UpdatePreview();
            Assert.False(editor.Pad.IsLit(PadControl.Cross));
        });
    }

    [Fact]
    public void Releasing_the_preview_clears_everything()
    {
        WithEditor(editor =>
        {
            editor.PreviewDown(K);
            editor.ReleasePreview();          // what Deactivated does
            editor.UpdatePreview();
            Assert.False(editor.Pad.IsLit(PadControl.Cross));
            Assert.False(editor.IsKeyCapLit(PadControl.Cross, K));
        });
    }

    [Fact]
    public void A_reset_while_a_key_is_held_leaves_nothing_lit()
    {
        WithEditor(editor =>
        {
            editor.PreviewDown(K);
            editor.UpdatePreview();
            AppServices.Controls.ResetToDefault();      // raises Changed; the editor refreshes on the dispatcher
            Wpf.PumpFor(50);
            editor.UpdatePreview();
            Assert.False(editor.Pad.IsLit(PadControl.Cross));
            Assert.False(editor.IsKeyCapLit(PadControl.Cross, K));
        });
    }

    [Fact]
    public void Typing_in_a_text_box_does_not_drive_the_preview()
    {
        Wpf.Run(() =>
        {
            Assert.True(ControlsWindow.PreviewIgnores(new System.Windows.Controls.TextBox()));
            Assert.False(ControlsWindow.PreviewIgnores(new System.Windows.Controls.Button()));
            Assert.False(ControlsWindow.PreviewIgnores(null));
        });
    }

    [Fact]
    public void The_picture_follows_the_kind_and_the_caption_invites_testing()
    {
        WithEditor(editor =>
        {
            Assert.Equal(PadKind.Xbox360, editor.Pad.Kind);
            Assert.Equal("Press your keys to test them · Click a button to change its key", editor.PadCaption.Text);
        }, PadKind.Xbox360);
    }

    [Fact]
    public void A_click_on_the_picture_while_listening_switches_instead_of_binding_left_click()
    {
        WithEditor(editor =>
        {
            Assert.False(editor.MouseDownBinds(editor.Pad));
            Assert.True(editor.MouseDownBinds(editor.FindBox));
            Assert.True(editor.MouseDownBinds(null));
        });
    }
}
