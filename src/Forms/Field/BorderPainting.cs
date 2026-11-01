using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms;

public partial class Field
{
    /// <summary>Default border width of a widget whose /BS gives none (PDF 32000-1, 12.5.4).</summary>
    private const double DefaultBorderWidth = 1;

    /// <summary>Share of a beveled face's colour its shadowed edges take.</summary>
    private const double BevelShadowShare = 216.0 / 255.0;

    /// <summary>The fill of an inset border's shadowed (top-left) edges and of its lit (bottom-right) ones.</summary>
    private const string InsetShadowFill = "0.4980392157 0.4980392157 0.4980392157 rg";
    private const string InsetLitFill = "0.8470588235 0.8470588235 0.8470588235 rg";

    /// <summary>A border set on the field is shown: an appearance already drawn is drawn again with it.</summary>
    private protected override void OnBorderChanged() => RedrawChrome();

    /// <summary>A border or background colour set on the field's characteristics is shown: an appearance
    /// already drawn is drawn again with it.</summary>
    private protected override void OnCharacteristicsColorChanged() => RedrawChrome();

    /// <summary>Draws the field's own appearance again after its chrome changed; a field drawn by its widget
    /// kids, or not drawn yet, is left as it is - its next drawing takes the new chrome.</summary>
    private void RedrawChrome()
    {
        var drawn = Dict.Get("AP");
        if (drawn is null) return;
        Dict.Remove("AP");
        GenerateAppearance();
        // A field the library draws no appearance for keeps the one it has.
        if (!Dict.ContainsKey("AP")) Dict.Set("AP", drawn);
    }

    /// <summary>True when a widget's /BS asks for more than the plain one-point solid box: another style, another
    /// width or a dash.</summary>
    private protected bool HasStyledBorder(PdfDictionary widget)
    {
        var bs = Reader.ResolveDict(widget.Get("BS"));
        if (bs is null) return false;
#pragma warning disable S1244 // only the exact default width is the plain box - any other value is drawn as given
        return bs.GetName("S") is { } style && style != "S"
            || Functions.PdfArrayHelper.GetDoubleFromDict(bs, "W", DefaultBorderWidth) != DefaultBorderWidth;
#pragma warning restore S1244
    }

    /// <summary>The border a widget's /BS asks for, stroked with <paramref name="strokeOp"/>, in a q/Q of its own:
    /// a box inset by half the width (/S /S), dashed with the /D pattern (/D), a rule along the bottom (/U), or
    /// the lit and shadowed chevrons of a beveled (/B) or inset (/I) border inside the box. Empty when the width
    /// is zero. <paramref name="bevelBase"/> is the face colour a beveled border shades.</summary>
    private protected string BuildBsBorder(PdfDictionary widget, double w, double h, string strokeOp, System.Drawing.Color bevelBase)
    {
        var bs = Reader.ResolveDict(widget.Get("BS"));
        double bw = bs is null ? DefaultBorderWidth : Functions.PdfArrayHelper.GetDoubleFromDict(bs, "W", DefaultBorderWidth);
        if (bw <= 0) return string.Empty;
        var style = bs?.GetName("S") ?? "S";
        var innerRect = $"{FmtNum(bw / 2)} {FmtNum(bw / 2)} {FmtNum(w - bw)} {FmtNum(h - bw)} re";
        var sb = new System.Text.StringBuilder("q ");
        switch (style)
        {
            case "U":
                sb.Append($"{strokeOp} {FmtNum(bw)} w 0 {FmtNum(bw / 2)} m {FmtNum(w)} {FmtNum(bw / 2)} l S ");
                break;
            case "D":
                sb.Append($"{strokeOp} {FmtNum(bw)} w {DashOperand(bs!)} 0 d {innerRect} S ");
                break;
            case "B":
            case "I":
                sb.Append(BevelChevrons(style == "B", w, h, bw, bevelBase));
                sb.Append($"{strokeOp} {FmtNum(bw)} w {innerRect} S ");
                break;
            default:
                sb.Append($"{strokeOp} {FmtNum(bw)} w {innerRect} S ");
                break;
        }
        return sb.Append("Q ").ToString();
    }

    /// <summary>The colour of an /MK colour array: gray, RGB or CMYK components.</summary>
    private protected static System.Drawing.Color MkColor(PdfArray components)
    {
        double C(int i) => i < components.Count ? Functions.PdfArrayHelper.GetDouble(components, i) : 0;
        var (r, g, b) = components.Count switch
        {
            1 => (C(0), C(0), C(0)),
            4 => ((1 - C(0)) * (1 - C(3)), (1 - C(1)) * (1 - C(3)), (1 - C(2)) * (1 - C(3))),
            _ => (C(0), C(1), C(2)),
        };
        int Byte(double v) => (int)System.Math.Round(Compat.Clamp(v, 0, 1) * byte.MaxValue);
        return System.Drawing.Color.FromArgb(Byte(r), Byte(g), Byte(b));
    }

    /// <summary>The dash array of a dashed /BS: its /D, else a three-point dash.</summary>
    private string DashOperand(PdfDictionary bs)
    {
        if (Reader.Resolve(bs.Get("D")) is not PdfArray d || d.Count == 0) return "[3]";
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < d.Count; i++) parts.Add(FmtNum(Functions.PdfArrayHelper.GetDouble(d, i)));
        return $"[{string.Join(" ", parts)}]";
    }

    /// <summary>The two chevrons of a beveled or inset border of width <paramref name="bw"/>: the top-left one and
    /// the bottom-right one, each filled. A beveled border lights its top-left edges and shades the others with
    /// the face colour; an inset one shades its top-left edges.</summary>
    private protected static string BevelChevrons(bool beveled, double w, double h, double bw, System.Drawing.Color bevelBase)
    {
        double b2 = bw * 2;
        var (lit, shadow) = beveled
            ? ("1 g", $"{FmtNum(bevelBase.R / 255.0 * BevelShadowShare)} {FmtNum(bevelBase.G / 255.0 * BevelShadowShare)} {FmtNum(bevelBase.B / 255.0 * BevelShadowShare)} rg")
            : (InsetShadowFill, InsetLitFill);
        return $"{lit} {FmtNum(bw)} {FmtNum(bw)} m {FmtNum(bw)} {FmtNum(h - bw)} l " +
               $"{FmtNum(w - bw)} {FmtNum(h - bw)} l {FmtNum(w - b2)} {FmtNum(h - b2)} l " +
               $"{FmtNum(b2)} {FmtNum(h - b2)} l {FmtNum(b2)} {FmtNum(b2)} l f " +
               $"{shadow} {FmtNum(w - bw)} {FmtNum(h - bw)} m {FmtNum(w - bw)} {FmtNum(bw)} l " +
               $"{FmtNum(bw)} {FmtNum(bw)} l {FmtNum(b2)} {FmtNum(b2)} l {FmtNum(w - b2)} {FmtNum(b2)} l " +
               $"{FmtNum(w - b2)} {FmtNum(h - b2)} l f ";
    }
}
