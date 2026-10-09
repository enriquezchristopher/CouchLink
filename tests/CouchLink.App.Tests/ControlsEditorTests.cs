using System.Windows;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.Core.Input;

namespace CouchLink.App.Tests;

/// <summary>The editor edits the app-wide AppServices.Controls, so each test resets it.</summary>
public class ControlsEditorTests
{
    // Default layout (KeyLayout.Defaults): Cross = K only, Square = J and left click. Num 5 is free.
    private const ushort K = 0x4B, J = 0x4A, Num5 = 0x65, Esc = 0x1B;

    private static void WithEditor(Action<ControlsWindow> test)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            AppServices.Controls.ResetToDefault();
            var editor = new ControlsWindow();
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
}
