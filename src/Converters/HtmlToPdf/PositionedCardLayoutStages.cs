using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The helpers of the positioned card layout: the text draw and the serif baseline drop.
    private static double CWidth(string s, bool bold, double pt)
        => MeasureFixedText(bold ? "Times New Roman Bold" : "Times New Roman", s, pt, 0);

    private static void CDrawText(PositionedCardLayoutState pk, string s, bool bold, double x, double baseline, double pt, Color col)
    {
        var f = bold && pk.cSerifB.ttf is not null ? pk.cSerifB : pk.cSerifR;
        if (pk.fontDictC is null || f.ttf is null || s.Length == 0) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(pk.fontDictC, f.ttf,
            bold ? "TimesNewRomanBold" : "TimesNewRoman", s, stripSpacesInBaseFont: true);
        var t = new StringBuilder();
        t.Append("BT ").Append((col.R / 255.0).ToString("0.###", pk.invC)).Append(' ')
            .Append((col.G / 255.0).ToString("0.###", pk.invC)).Append(' ')
            .Append((col.B / 255.0).ToString("0.###", pk.invC)).Append(" rg ");
        t.Append($"/{rn} {pt.ToString("F1", pk.invC)} Tf ");
        t.Append($"1 0 0 1 {x.ToString("F3", pk.invC)} {baseline.ToString("F3", pk.invC)} Tm ");
        t.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ET ");
        pk.flow.page.AddContentStream(Encoding.ASCII.GetBytes(t.ToString()));
    }

    private static void COps(PositionedCardLayoutState pk, string ops)
    => pk.flow.page.AddContentStream(Encoding.ASCII.GetBytes(ops));

    private static double CSerifDrop(double pt)
    {
        var box = PxLinePt(pt, SerifWinLineRatio);
        return (box - pt * SerifWinLineRatio) / 2 + pt * SerifWinAscent;
    }
}
