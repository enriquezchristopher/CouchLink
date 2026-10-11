using System.Globalization;
using System.Text;

namespace CouchLink.App.Ui;

/// <summary>
/// Path data for the controller outlines, in the reference images' pixels (DS4: a 1280 px top-view photo;
/// Xbox 360: a 960 px front diagram). Only the left half is written; the right half is its mirror.
/// </summary>
internal static class ControllerShapes
{
    /// <summary>
    /// A closed outline symmetric about x = <paramref name="cx"/>. <paramref name="segments"/> run from
    /// <paramref name="start"/> (on the axis) around the left half back to the axis: one point is a line to it,
    /// three points are a cubic curve (control 1, control 2, end).
    /// </summary>
    public static string Symmetric((double X, double Y) start, (double X, double Y)[][] segments, double cx)
    {
        (double X, double Y) M((double X, double Y) p) => (2 * cx - p.X, p.Y);
        static string P((double X, double Y) p) => string.Create(CultureInfo.InvariantCulture, $"{p.X} {p.Y}");

        var d = new StringBuilder("M" + P(start));
        var points = new List<(double X, double Y)> { start };
        foreach (var s in segments)
        {
            d.Append(s.Length == 1 ? " L" + P(s[0]) : $" C{P(s[0])} {P(s[1])} {P(s[2])}");
            points.Add(s[^1]);
        }
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            var s = segments[i];
            var from = points[i];
            d.Append(s.Length == 1 ? " L" + P(M(from)) : $" C{P(M(s[1]))} {P(M(s[0]))} {P(M(from))}");
        }
        return d.Append(" Z").ToString();
    }

    public static readonly string Ds4Body = Symmetric((656, 366),
    [
        [(440, 362)],
        [(395, 348), (330, 340), (282, 358)],
        [(238, 376), (214, 428), (207, 500)],
        [(201, 570), (204, 645), (214, 712)],
        [(227, 820), (250, 925), (286, 973)],
        [(312, 1003), (366, 998), (386, 962)],
        [(402, 925), (411, 845), (425, 792)],
        [(438, 752), (470, 738), (522, 738)],
        [(656, 738)],
    ], 656);

    public static readonly string Xbox360Body = Symmetric((478, 92),
    [
        [(400, 92), (340, 78), (292, 88)],
        [(246, 98), (222, 148), (212, 200)],
        [(202, 252), (199, 302), (209, 346)],
        [(217, 377), (250, 394), (292, 389)],
        [(326, 385), (339, 362), (350, 346)],
        [(362, 338), (400, 340), (430, 341)],
        [(478, 341)],
    ], 478);
}
