using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The stages of the search-form layout: the side link and the button row.</summary>
    private static void LayoutSearchFormButtons(SearchFormLayoutState sm)
    {
        var bfpt = sm.sf.ButtonFontPx * PxPt;
        var widths = new double[sm.sf.Buttons.Count];
        double btotal = 0;
        for (var bi = 0; bi < sm.sf.Buttons.Count; bi++)
        {
            widths[bi] = MeasureFaceText("Arial", sm.sf.Buttons[bi].Label, bfpt) + 2 * sm.sf.ButtonPadPx * PxPt;
            btotal += widths[bi];
        }
        btotal += (sm.sf.Buttons.Count - 1) * sm.sf.ButtonGapPx * PxPt;
        var bx = sm.cellX + (sm.sf.InputContentPx * PxPt - btotal) / 2;
        var bh = sm.sf.ButtonHeightPx * PxPt;
        var g1 = new StringBuilder();
        for (var bi = 0; bi < sm.sf.Buttons.Count; bi++)
        {
            var bw = widths[bi];
            g1.Append("q ");
            g1.Append($"{(sm.sf.ButtonBg.R / 255.0).ToString("F5", sm.invf)} {(sm.sf.ButtonBg.G / 255.0).ToString("F5", sm.invf)} {(sm.sf.ButtonBg.B / 255.0).ToString("F5", sm.invf)} rg ");
            g1.Append($"{bx.ToString("F2", sm.invf)} {(sm.cv.flow.y - bh).ToString("F2", sm.invf)} {bw.ToString("F2", sm.invf)} {bh.ToString("F2", sm.invf)} re f ");
            g1.Append("0 0 0 RG 0.75 w ");
            g1.Append($"{(bx + 0.375).ToString("F2", sm.invf)} {(sm.cv.flow.y - bh + 0.375).ToString("F2", sm.invf)} {(bw - 0.75).ToString("F2", sm.invf)} {(bh - 0.75).ToString("F2", sm.invf)} re S Q ");
            var label = sm.sf.Buttons[bi].Label;
            var (rn2, hex2) = Text.Type0FontEmbedder.Embed(sm.fdict!, sm.arial.ttf!, "Arial", label, stripSpacesInBaseFont: true);
            var tw = MeasureFaceText("Arial", label, bfpt);
            var tx = bx + (bw - tw) / 2;
            var tbase = sm.cv.flow.y - (bh + 0.72 * bfpt) / 2;
            g1.Append("BT ");
            g1.Append($"{(sm.sf.ButtonFg.R / 255.0).ToString("F5", sm.invf)} {(sm.sf.ButtonFg.G / 255.0).ToString("F5", sm.invf)} {(sm.sf.ButtonFg.B / 255.0).ToString("F5", sm.invf)} rg ");
            g1.Append($"/{rn2} {bfpt.ToString("F1", sm.invf)} Tf 1 0 0 1 {tx.ToString("F2", sm.invf)} {tbase.ToString("F2", sm.invf)} Tm ");
            g1.Append('<').Append(Compat.ToHexString(hex2)).Append("> Tj ET ");
            bx += bw + sm.sf.ButtonGapPx * PxPt;
        }
        sm.cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(g1.ToString()));
    }

    /// <summary>x</summary>
    private static void LayoutSearchFormLink(SearchFormLayoutState sm)
    {
        var lx = sm.cellX + sm.cellW + sm.sf.LinkMarginLeftPx * PxPt;
        var lf = sm.sf.LinkFontPx * PxPt;
        var lbase = sm.cv.flow.y - 5 * PxPt - 0.85 * lf;
        var g0 = new StringBuilder();
        // clip at the content box so an overlong side link ends at the margin
        g0.Append("q ");
        g0.Append($"{sm.cv.marginLeft.ToString("F2", sm.invf)} {(sm.cv.flow.y - sm.inputH - 30).ToString("F2", sm.invf)} {sm.cv.flow.contentWidth.ToString("F2", sm.invf)} {(sm.inputH + 60).ToString("F2", sm.invf)} re W n ");
        var (rn, hex) = Text.Type0FontEmbedder.Embed(sm.fdict!, sm.arial.ttf!, "Arial", sm.sf.LinkText!, stripSpacesInBaseFont: true);
        g0.Append("BT ");
        g0.Append($"{(sm.sf.LinkColor.R / 255.0).ToString("F5", sm.invf)} {(sm.sf.LinkColor.G / 255.0).ToString("F5", sm.invf)} {(sm.sf.LinkColor.B / 255.0).ToString("F5", sm.invf)} rg ");
        g0.Append($"/{rn} {lf.ToString("F1", sm.invf)} Tf 1 0 0 1 {lx.ToString("F2", sm.invf)} {lbase.ToString("F2", sm.invf)} Tm ");
        g0.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ET Q ");
        sm.cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(g0.ToString()));
        if (!string.IsNullOrEmpty(sm.sf.LinkUrl))
            sm.cv.pendingLinks.Add((sm.cv.flow.page, new Rectangle(lx, lbase - 3,
                Math.Min(lx + MeasureFaceText("Arial", sm.sf.LinkText!, lf), sm.cv.marginLeft + sm.cv.flow.contentWidth),
                lbase + lf), sm.sf.LinkUrl!, sm.sf.LinkText));
    }
}
