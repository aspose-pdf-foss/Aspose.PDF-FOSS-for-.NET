using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    private const string MarkCheck = "check";
    private const string MarkCross = "cross";
    private const string MarkStar = "star";
    private const string MarkDiamond = "diamond";
    private const string MarkSquare = "square";
    private const string MarkCircle = "circle";

    /// <summary>The mark a set check box or option shows: the one its button names, else the viewer's default for
    /// its shape - a cross in a square box (as a viewer draws a box that names none), a dot in a round one.</summary>
    private static string CheckMarkOf(XmlElement? button, bool round)
    {
        var mark = button?.GetAttribute("mark") ?? string.Empty;
        return mark is MarkCheck or MarkCross or MarkStar or MarkDiamond or MarkSquare or MarkCircle
            ? mark
            : round ? MarkCircle : MarkCross;
    }

    /// <summary>The mark of a set check box or option whose outline has its top left corner at (x, top) in top-down
    /// coordinates: a square or a dot in the middle of the outline, a little under half its side; a drawn mark
    /// (check, cross, star, diamond) over a larger middle part of it.</summary>
    private static Item CheckMark(string mark, double x, double top, double size, double pageH)
    {
        var black = new[] { 0.0, 0.0, 0.0 };
        if (mark is MarkSquare or MarkCircle)
            return new Item
            {
                Kind = mark == MarkCircle ? "dot" : "fill", X = x + size * CheckMarkStart, Y = pageH - (top + size * CheckMarkEnd),
                W = size * CheckMarkSize, H = size * CheckMarkSize, Color = black,
            };
        return new Item
        {
            Kind = "mark", Text = mark, X = x + size * DrawnMarkStart, Y = pageH - (top + size * (1 - DrawnMarkStart)),
            W = size * DrawnMarkSize, H = size * DrawnMarkSize, Color = black,
        };
    }

    /// <summary>Where the mark starts and ends inside the outline and how large it is, as parts of the outline's side.</summary>
    private const double CheckMarkStart = 0.28;
    private const double CheckMarkEnd = 0.72;
    private const double CheckMarkSize = 0.44;
    private const double DrawnMarkStart = 0.2;
    private const double DrawnMarkSize = 0.6;

    // The outlines of the drawn marks in a unit square, y up.
    private static readonly double[] CheckOutline = { 0.05, 0.52, 0.18, 0.66, 0.40, 0.42, 0.82, 0.92, 0.95, 0.80, 0.40, 0.12 };
    private static readonly double[] DiamondOutline = { 0.5, 1, 1, 0.5, 0.5, 0, 0, 0.5 };
    private const double StarInnerRadius = 0.19;
    private const int StarPoints = 5;
    // The cross's strokes are this share of its side wide.
    private const double CrossStroke = 0.15;

    /// <summary>Draws a check, cross, star or diamond mark item in its box.</summary>
    private static void EmitMark(Content.ContentStreamBuilder b, Item it)
    {
        b.SaveState().SetFillColor(it.Color[0], it.Color[1], it.Color[2]).SetStrokeColor(it.Color[0], it.Color[1], it.Color[2]);
        if (it.Text == MarkCross)
        {
            b.SetLineWidth(it.W * CrossStroke).SetLineCap(0);
            b.MoveTo(it.X, it.Y).LineTo(it.X + it.W, it.Y + it.H)
             .MoveTo(it.X, it.Y + it.H).LineTo(it.X + it.W, it.Y).Stroke();
        }
        else
        {
            var outline = it.Text switch
            {
                MarkCheck => CheckOutline,
                MarkDiamond => DiamondOutline,
                _ => StarOutline(),
            };
            b.MoveTo(it.X + outline[0] * it.W, it.Y + outline[1] * it.H);
            for (int i = 2; i < outline.Length; i += 2) b.LineTo(it.X + outline[i] * it.W, it.Y + outline[i + 1] * it.H);
            b.ClosePath().Fill();
        }
        b.RestoreState();
    }

    /// <summary>A five-pointed star in a unit square, its first point up.</summary>
    private static double[] StarOutline()
    {
        var points = new double[StarPoints * 4];
        for (int i = 0; i < StarPoints * 2; i++)
        {
            double r = i % 2 == 0 ? 0.5 : StarInnerRadius;
            double a = Math.PI / 2 + i * Math.PI / StarPoints;
            points[2 * i] = 0.5 + r * Math.Cos(a);
            points[2 * i + 1] = 0.5 + r * Math.Sin(a);
        }
        return points;
    }
}
