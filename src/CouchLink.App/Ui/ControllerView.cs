using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CouchLink.App.Theme;
using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.App.Ui;

/// <summary>
/// The session's controller, drawn from reference photos, lit as the player presses keys. Every button, D-pad arm,
/// bumper, trigger and stick is a focusable button named for screen readers; clicking one raises
/// <see cref="PartClicked"/>. A pressed part fills with the primary colour, glows and shrinks to 92% (no shrink with
/// Reduce motion); a trigger fills; a stick's thumb leans the way it is held. No logos: PS and Guide are plain.
/// The thumb of a stick is its click (L3/R3); the ring around it stands for the stick's direction keys.
/// </summary>
internal sealed class ControllerView : Viewbox
{
    private sealed record Part(PadControl Control, Button Hit, Canvas Content, Shape[] Faces, string FillKey,
        bool KeepFill, ScaleTransform Scale, Shape? TriggerFill);

    private static readonly ControlTemplate Bare = new(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };

    private readonly Canvas _surface = new() { ClipToBounds = false };
    private readonly Canvas _drawing = new();
    private readonly Dictionary<PadControl, Part> _parts = [];
    private readonly TranslateTransform _leftThumb = new(), _rightThumb = new();
    private PadKind _kind = PadKind.DualShock4;
    private PadPicture _picture = PadPicture.Neutral;
    private PadControl? _outlined;
    private double _unit = 1, _tilt = 20;
    private Rect _box;

    public ControllerView()
    {
        Stretch = Stretch.Uniform;
        _surface.Children.Add(_drawing);
        Child = _surface;
        Build();
    }

    public event Action<PadControl>? PartClicked;

    public PadKind Kind
    {
        get => _kind;
        set
        {
            if (value == _kind)
                return;
            _kind = value;
            Build();
        }
    }

    /// <summary>The part whose key is being changed: drawn with a dashed outline.</summary>
    public PadControl? Outlined
    {
        get => _outlined;
        set
        {
            _outlined = value;
            Paint();
        }
    }

    /// <summary>The editor sends a picture 60 times a second; only a change repaints.</summary>
    public void Show(PadPicture picture)
    {
        if (picture.Lit.SetEquals(_picture.Lit) && (picture.LeftX, picture.LeftY, picture.RightX, picture.RightY)
                == (_picture.LeftX, _picture.LeftY, _picture.RightX, _picture.RightY))
            return;
        _picture = picture;
        Paint();
    }

    internal IReadOnlyCollection<PadControl> Parts => _parts.Keys;

    internal bool IsLit(PadControl control) => _parts.ContainsKey(control) && _picture.Lit.Contains(control);

    internal string PartName(PadControl control) => AutomationProperties.GetName(_parts[control].Hit);

    internal object? GlowOf(PadControl control) => _parts[control].Content.Effect;

    internal bool IsTabStop(PadControl control) => _parts[control].Hit.IsTabStop;

    internal void ClickPart(PadControl control) => _parts[control].Hit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private string NameOf(PadControl control) => KeyNames.Of(control, _kind);

    private void Build()
    {
        _parts.Clear();
        _drawing.Children.Clear();
        if (_kind == PadKind.Xbox360)
            DrawXbox();
        else
            DrawDs4();
        _surface.Width = _box.Width;
        _surface.Height = _box.Height;
        _drawing.RenderTransform = new TranslateTransform(-_box.X, -_box.Y);
        Paint();
    }

    // ---- shapes ----

    private T Styled<T>(T shape, string fill, string stroke = "BorderStrongBrush") where T : Shape
    {
        shape.SetResourceReference(Shape.FillProperty, fill);
        shape.SetResourceReference(Shape.StrokeProperty, stroke);
        shape.StrokeThickness = 1.2 * _unit;
        return shape;
    }

    private Ellipse Circle(double x, double y, double r, string fill = "RaisedBrush")
    {
        var e = Styled(new Ellipse { Width = 2 * r, Height = 2 * r }, fill);
        Canvas.SetLeft(e, x - r);
        Canvas.SetTop(e, y - r);
        return e;
    }

    private Rectangle Box(double x, double y, double w, double h, double r, string fill = "RaisedBrush")
    {
        var b = Styled(new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r }, fill);
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        return b;
    }

    private Path Outline(string data, string fill) => Styled(new Path { Data = Geometry.Parse(data) }, fill);

    private static TextBlock Text(double x, double y, double size, string text, string brush = "TextMutedBrush", bool bold = false)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size, Width = 300, TextAlignment = TextAlignment.Center,
            FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold, IsHitTestVisible = false,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Canvas.SetLeft(t, x - 150);
        Canvas.SetTop(t, y - size * 0.68);
        return t;
    }

    private static Path Stroke(string data, Brush brush, double thickness, double x = 0, double y = 0)
    {
        var p = new Path
        {
            Data = Geometry.Parse(data), Stroke = brush, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(p, x);
        Canvas.SetTop(p, y);
        return p;
    }

    private void Decor(params UIElement[] elements)
    {
        foreach (var e in elements)
        {
            e.IsHitTestVisible = false;
            _drawing.Children.Add(e);
        }
    }

    /// <summary>Marks a shape as a part's face and remembers the fill it returns to when released.</summary>
    private static Shape Face(Shape s, string fill = "RaisedBrush")
    {
        s.Tag = fill;
        return s;
    }

    /// <summary>
    /// One clickable part. The button spans the whole drawing so WPF's pressed-state check (pointer inside the
    /// button) holds; with no background, only its shapes take clicks.
    /// </summary>
    private void AddPart(PadControl control, string name, Point center, Shape[] faces, UIElement[]? decor = null,
        Transform? wrap = null, bool keepFill = false, Shape? triggerFill = null)
    {
        var content = new Canvas();
        foreach (var f in faces)
            content.Children.Add(f);
        if (triggerFill is not null)
        {
            triggerFill.IsHitTestVisible = false;
            content.Children.Add(triggerFill);
        }
        foreach (var d in decor ?? Array.Empty<UIElement>())
        {
            d.IsHitTestVisible = false;
            content.Children.Add(d);
        }
        var scale = new ScaleTransform(1, 1, center.X, center.Y);
        content.RenderTransform = wrap is null ? scale : new TransformGroup { Children = { scale, wrap } };
        var hit = new Button
        {
            // Not a tab stop: keyboard users change keys from the rows below, without tabbing through ~20 parts first.
            Template = Bare, Content = content, Cursor = Cursors.Hand, FocusVisualStyle = null, IsTabStop = false,
            Margin = new Thickness(0), Padding = new Thickness(0), Background = null,
            Width = _box.Right, Height = _box.Bottom,
        };
        AutomationProperties.SetName(hit, name);
        AutomationProperties.SetAutomationId(hit, $"Pad_{control}");
        hit.Click += (_, _) => PartClicked?.Invoke(control);
        hit.GotKeyboardFocus += (_, _) => Paint();
        hit.LostKeyboardFocus += (_, _) => Paint();
        _drawing.Children.Add(hit);
        string fillKey = faces[0].Tag as string ?? "RaisedBrush";
        _parts[control] = new Part(control, hit, content, faces, fillKey, keepFill, scale, triggerFill);
    }

    private void Stick(PadControl click, PadControl well, string side, double x, double y, double wellR, double r, double ringR,
        TranslateTransform thumb)
    {
        AddPart(well, $"{side} stick, show keys", new Point(x, y), [Face(Circle(x, y, wellR, "InputBrush"), "InputBrush")]);
        var ring = Circle(x, y, ringR);
        ring.Fill = null;
        AddPart(click, $"{side} stick click, {NameOf(click)}, change key", new Point(x, y), [Face(Circle(x, y, r))], [ring], wrap: thumb);
    }

    private void DpadArms(Func<Shape> arm, Point armCenter, double cx, double cy)
    {
        foreach (var (control, angle, word) in new[] { (PadControl.DpadUp, 0, "up"), (PadControl.DpadRight, 90, "right"),
                     (PadControl.DpadDown, 180, "down"), (PadControl.DpadLeft, 270, "left") })
            AddPart(control, $"D-pad {word}, change key", armCenter, [Face(arm())], wrap: new RotateTransform(angle, cx, cy));
    }

    // ---- the two controllers ----

    private void DrawDs4()
    {
        _box = new Rect(180, 268, 952, 744);
        _unit = _box.Height / 190;
        _tilt = 20;
        foreach (var (control, x) in new[] { (PadControl.L2, 300.0), (PadControl.R2, 892.0) })
        {
            var fill = Box(x + 6, 275, 108, 30, 12, "PrimaryBrush");
            fill.Stroke = null;
            AddPart(control, $"{NameOf(control)}, change key", new Point(x + 60, 290), [Face(Box(x, 270, 120, 42, 16))],
                [Text(x + 60, 290, 20, NameOf(control))], triggerFill: fill);
        }
        foreach (var (control, x) in new[] { (PadControl.L1, 290.0), (PadControl.R1, 882.0) })
            AddPart(control, $"{NameOf(control)}, change key", new Point(x + 70, 332), [Face(Box(x, 312, 140, 48, 14))],
                [Text(x + 70, 330, 18, NameOf(control))]);
        Decor(Outline(ControllerShapes.Ds4Body, "InputBrush"));
        AddPart(PadControl.Touchpad, "Touchpad, change key", new Point(656, 448), [Face(Box(490, 368, 332, 160, 12))]);
        Decor(Text(456, 382, 13, "SHARE", "TextHintBrush"), Text(856, 382, 13, "OPTIONS", "TextHintBrush"));
        AddPart(PadControl.Share, "Share, change key", new Point(456, 413), [Face(Box(442, 392, 28, 42, 14))]);
        AddPart(PadControl.Options, "Options, change key", new Point(856, 413), [Face(Box(842, 392, 28, 42, 14))]);
        foreach (var (dx, y) in new[] { (-18, 556), (0, 556), (18, 556), (-27, 570), (-9, 570), (9, 570), (27, 570), (-18, 584), (0, 584), (18, 584) })
        {
            var dot = Circle(656 + dx, y, 4, "ChromeBrush");
            dot.Stroke = null;
            Decor(dot);
        }
        Decor(Circle(656, 642, 21, "InputBrush")); // PS button, plain
        Decor(Circle(337, 497, 106, "InputBrush"));
        DpadArms(() => Styled(new Polygon { Points = PointCollection.Parse("314,415 360,415 360,464 337,484 314,464"), StrokeLineJoin = PenLineJoin.Round }, "RaisedBrush"),
            new Point(337, 449), 337, 497);
        Decor(Circle(975, 497, 112, "InputBrush"));
        var symbols = new Dictionary<PadControl, (string Data, Color Color)>
        {
            [PadControl.Triangle] = ("M0 -14 L13 9 L-13 9 Z", Color.FromRgb(0x3E, 0xB4, 0x89)),
            [PadControl.Circle] = ("M13 0 A13 13 0 1 1 -13 0 A13 13 0 1 1 13 0", Color.FromRgb(0xE5, 0x53, 0x4B)),
            [PadControl.Cross] = ("M-11 -11 L11 11 M11 -11 L-11 11", Color.FromRgb(0x7B, 0x8F, 0xD9)),
            [PadControl.Square] = ("M-11 -11 L11 -11 L11 11 L-11 11 Z", Color.FromRgb(0xD6, 0x7F, 0xC7)),
        };
        foreach (var (control, x, y) in new[] { (PadControl.Triangle, 975.0, 439.0), (PadControl.Circle, 1033.0, 497.0),
                     (PadControl.Cross, 975.0, 555.0), (PadControl.Square, 917.0, 497.0) })
            AddPart(control, $"{NameOf(control)}, change key", new Point(x, y), [Face(Circle(x, y, 30))],
                [Stroke(symbols[control].Data, new SolidColorBrush(symbols[control].Color), 3.4, x, y)]);
        Stick(PadControl.L3, PadControl.LeftUp, "Left", 484, 606, 90, 52, 38, _leftThumb);
        Stick(PadControl.R3, PadControl.RightUp, "Right", 828, 606, 90, 52, 38, _rightThumb);
    }

    private void DrawXbox()
    {
        _box = new Rect(178, 40, 600, 360);
        _unit = _box.Height / 190;
        _tilt = 12;
        foreach (var (control, x) in new[] { (PadControl.L2, 305.0), (PadControl.R2, 607.0) })
        {
            var fill = Box(x + 4, 54, 36, 40, 7, "PrimaryBrush");
            fill.Stroke = null;
            AddPart(control, $"{NameOf(control)}, change key", new Point(x + 22, 79), [Face(Box(x, 50, 44, 58, 9))],
                [Text(x + 22, 72, 13, NameOf(control))], triggerFill: fill);
        }
        Decor(Box(405, 82, 146, 30, 8, "InputBrush"));
        Decor(Outline(ControllerShapes.Xbox360Body, "InputBrush"));
        foreach (var (control, x) in new[] { (PadControl.L1, 256.0), (PadControl.R1, 588.0) })
            AddPart(control, $"{NameOf(control)}, change key", new Point(x + 56, 126), [Face(Box(x, 110, 112, 32, 14))],
                [Text(x + 56, 126, 13, NameOf(control))]);
        var slot = Box(442, 124, 72, 10, 5, "ChromeBrush");
        slot.Stroke = null;
        Decor(slot);
        var back = Stroke("M422 222 L412 228 L422 234", Brushes.Transparent, 2);
        var start = Stroke("M534 222 L544 228 L534 234", Brushes.Transparent, 2);
        back.SetResourceReference(Shape.StrokeProperty, "TextMutedBrush"); // follows a theme switch
        start.SetResourceReference(Shape.StrokeProperty, "TextMutedBrush");
        AddPart(PadControl.Share, "Back, change key", new Point(418, 228), [Face(Box(403, 219, 30, 18, 9))], [back]);
        AddPart(PadControl.Options, "Start, change key", new Point(538, 228), [Face(Box(523, 219, 30, 18, 9))], [start]);
        var guideRing = new Ellipse { Width = 54, Height = 54, Stroke = new SolidColorBrush(Color.FromRgb(0x7C, 0xC2, 0x42)), StrokeThickness = 2.5 * _unit };
        Canvas.SetLeft(guideRing, 451);
        Canvas.SetTop(guideRing, 201);
        Decor(guideRing, Circle(478, 228, 20, "InputBrush")); // Guide, plain
        Stick(PadControl.L3, PadControl.LeftUp, "Left", 313, 243, 56, 36, 26, _leftThumb);
        Decor(Circle(397, 318, 52, "InputBrush"), Circle(397, 318, 44, "RaisedBrush"));
        DpadArms(() => Box(384, 278, 26, 34, 5), new Point(397, 295), 397, 318);
        Stick(PadControl.R3, PadControl.RightUp, "Right", 559, 330, 56, 36, 26, _rightThumb);
        var caps = new Dictionary<PadControl, Color>
        {
            [PadControl.Triangle] = Color.FromRgb(0xE8, 0xB9, 0x0F), [PadControl.Circle] = Color.FromRgb(0xD9, 0x36, 0x3E),
            [PadControl.Cross] = Color.FromRgb(0x4F, 0xAE, 0x3C), [PadControl.Square] = Color.FromRgb(0x2D, 0x73, 0xD2),
        };
        foreach (var (control, x, y) in new[] { (PadControl.Triangle, 650.0, 183.0), (PadControl.Circle, 693.0, 226.0),
                     (PadControl.Cross, 650.0, 269.0), (PadControl.Square, 607.0, 226.0) })
        {
            var cap = Circle(x, y, 21);
            cap.Fill = new SolidColorBrush(caps[control]);
            var letter = Text(x, y + 1, 22, NameOf(control), bold: true);
            letter.Foreground = new SolidColorBrush(Color.FromArgb(0x9E, 0x0F, 0x0F, 0x23));
            AddPart(control, $"{NameOf(control)}, change key", new Point(x, y), [cap], [letter], keepFill: true);
        }
    }

    // ---- state ----

    private static readonly DoubleCollection Dashes = Frozen(new DoubleCollection { 3, 2 });
    private DropShadowEffect? _glow;

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>One shared glow, rebuilt only when the theme's colour changes.</summary>
    private DropShadowEffect Glow()
    {
        var color = TryFindResource("PrimaryTextBrush") is SolidColorBrush b ? b.Color : Colors.MediumPurple;
        if (_glow is null || _glow.Color != color)
            _glow = Frozen(new DropShadowEffect { ShadowDepth = 0, BlurRadius = 14, Color = color, Opacity = 0.9 });
        return _glow;
    }

    private void Paint()
    {
        var glow = Glow();
        foreach (var part in _parts.Values)
        {
            bool lit = _picture.Lit.Contains(part.Control);
            bool marked = _outlined == part.Control || part.Hit.IsKeyboardFocused;
            foreach (var face in part.Faces)
            {
                if (!part.KeepFill)
                    face.SetResourceReference(Shape.FillProperty, lit ? "PrimaryBrush" : part.FillKey);
                face.SetResourceReference(Shape.StrokeProperty, lit ? "OnPrimaryBrush" : marked ? "PrimaryTextBrush" : "BorderStrongBrush");
                face.StrokeThickness = (lit || marked ? 2.5 : 1.2) * _unit;
                face.StrokeDashArray = _outlined == part.Control && !lit ? Dashes : null;
            }
            if (part.TriggerFill is { } fill)
                fill.Opacity = lit ? 1 : 0;
            part.Content.Effect = lit ? glow : null;
            double s = lit && !Motion.Reduced ? 0.92 : 1;
            part.Scale.ScaleX = part.Scale.ScaleY = s;
        }
        _leftThumb.X = _picture.LeftX * _tilt;
        _leftThumb.Y = _picture.LeftY * _tilt;
        _rightThumb.X = _picture.RightX * _tilt;
        _rightThumb.Y = _picture.RightY * _tilt;
    }
}
