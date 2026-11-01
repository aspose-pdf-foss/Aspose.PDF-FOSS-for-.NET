using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>Control-point distance of the cubic that stands for a quarter circle, per unit radius.</summary>
    private const double QuarterArc = 0.5523;

    /// <summary>The radius a border rounds its corners with: its corner's, when that corner is joined
    /// round. The corner shapes the fill under the border as well, whether or not it is itself stroked.</summary>
    private static double CornerRadius(XmlElement? border)
    {
        var corner = border is null ? null : FirstChild(border, "corner");
        if (corner is null || corner.GetAttribute("join") != "round") return 0;
        return Math.Max(0, Len(corner.GetAttribute("radius"), 0));
    }

    /// <summary>The colour a border is stroked in: its first visible edge's, black when that edge names none.</summary>
    private static double[] EdgeColor(XmlElement? border)
    {
        var edge = border?.ChildNodes.OfType<XmlElement>()
            .FirstOrDefault(c => c.LocalName == "edge" && c.GetAttribute("presence") is not ("hidden" or "invisible"));
        return ColorOf(edge) ?? new double[] { 0, 0, 0 };
    }

    /// <summary>The colour a style attribute of rich text sets (color:#rrggbb), or null when it sets none.</summary>
    private static double[]? StyleColor(string style)
    {
        foreach (var part in style.Split(';'))
        {
            var kv = part.Split(':');
            if (kv.Length != 2 || kv[0].Trim() != "color") continue;
            var v = kv[1].Trim();
            if (v.Length != HexColorLength || v[0] != '#'
                || !int.TryParse(v.Substring(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
                return null;
            return new[] { ((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0 };
        }
        return null;
    }

    /// <summary>Length of a #rrggbb colour.</summary>
    private const int HexColorLength = 7;

    /// <summary>A rectangle path whose corners are quarter circles of the given radius (no more than half a side).</summary>
    private static void RoundedRectangle(Content.ContentStreamBuilder b, double x, double y, double w, double h, double radius)
    {
        double r = Math.Min(radius, Math.Min(w, h) / 2), k = r * QuarterArc;
        b.MoveTo(x + r, y)
         .LineTo(x + w - r, y).CurveTo(x + w - r + k, y, x + w, y + r - k, x + w, y + r)
         .LineTo(x + w, y + h - r).CurveTo(x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h)
         .LineTo(x + r, y + h).CurveTo(x + r - k, y + h, x, y + h - r + k, x, y + h - r)
         .LineTo(x, y + r).CurveTo(x, y + r - k, x + r - k, y, x + r, y)
         .ClosePath();
    }

    /// <summary>The path of a box item: a plain rectangle, or a rounded one when the item carries a corner radius.</summary>
    private static void BoxPath(Content.ContentStreamBuilder b, Item it)
    {
        if (it.Radius > 0 && it.W > 0 && it.H > 0) RoundedRectangle(b, it.X, it.Y, it.W, it.H, it.Radius);
        else b.Rectangle(it.X, it.Y, it.W, it.H);
    }
}
