using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileStamp
{
// The stages of a text stamp on a page: the draw operators, and the placement for an upright, a rotated foreground and a rotated background stamp.
    // The stamp's drawing operators in local coordinates, first baseline at the
    // origin: an optional background box, the fill/stroke colour + render mode,
    // then one Tj per line. Shared by both the Form-XObject and inline paths.
    private static string DrawOps(TextStampApplyState ta, string fontRes)
    {
        var b = new StringBuilder();
        if (!ta.text.BackgroundColor.IsEmpty)
        {
            double descent = (Aspose.Pdf.Text.Standard14Fonts.IsStandard14(ta.fontName)
                ? Aspose.Pdf.Text.Standard14Fonts.GetDescent(ta.fontName) : -207) * ta.text.FontSize / 1000.0;
            b.Append($"{NormColor(ta.text.BackgroundColor.R)} {NormColor(ta.text.BackgroundColor.G)} {NormColor(ta.text.BackgroundColor.B)} rg\n");
            b.Append($"0 {Format(descent)} {Format(ta.text.TextWidth)} {Format(ta.text.FontSize - descent)} re f\n");
        }
        if (ta.ts?.ForegroundColor is { } fg)
            b.Append($"{NormColor(fg.R)} {NormColor(fg.G)} {NormColor(fg.B)} rg\n");
        else
            b.Append($"{NormColor(ta.text.ForegroundColor.R)} {NormColor(ta.text.ForegroundColor.G)} {NormColor(ta.text.ForegroundColor.B)} rg\n");
        if (ta.ts?.StrokingColor is { } sc)
            b.Append($"{NormColor(sc.R)} {NormColor(sc.G)} {NormColor(sc.B)} RG\n");
        if (ta.ts is not null && (int)ta.ts.RenderingMode != 0)
            b.Append($"{(int)ta.ts.RenderingMode} Tr\n");
        b.Append($"BT /{fontRes} {Format(ta.text.FontSize)} Tf 0 0 Td ");
        for (int i = 0; i < ta.lines.Count; i++)
        {
            if (i > 0) b.Append($"0 {Format(-ta.fontSize)} Td ");
            b.Append($"({EscapePdfString(ta.lines[i])}) Tj ");
        }
        b.Append("ET\n");
        return b.ToString();
    }

    /// <summary></summary>
    private static void PlaceRotatedBackgroundTextStamp(TextStampApplyState ta)
    {
        // Rotated BACKGROUND stamp: it is drawn through an UNROTATED
        // Form XObject whose BBox spans [0 0 max(TextWidth, pageWidth) pageHeight],
        // TextWidth being the real system-face advance sum (unrounded hmtx units,
        // e.g. Windows Arial for "Arial" — not the rounded Standard-14 AFM). The
        // rotation lives in the page-level cm, translated so the rotated block
        // rect [0,W]×[0,(N+0.1)·S] stays in the first quadrant, and the text
        // baseline inside the form is lifted by the font descent.
        double realW = 0;
        foreach (var line in ta.lines)
            realW = Math.Max(realW, MeasureSystemFaceWidth(line, ta.text, ta.fontSize));
        var mbox = ta.page.MediaBox;
        double bboxW = Math.Max(realW, mbox.Width);
        double bboxH = mbox.Height;

        var descent = Aspose.Pdf.Text.Standard14Fonts.IsStandard14(ta.fontName)
            ? Aspose.Pdf.Text.Standard14Fonts.GetDescent(ta.fontName)
            : Aspose.Pdf.Text.Standard14Fonts.GetDescent("Helvetica");
        var lift = (descent < 0 ? -descent : 207) * ta.fontSize / 1000.0;

        var fg = ta.ts?.ForegroundColor ?? ta.text.ForegroundColor;
        var fb = new StringBuilder();
        fb.Append("q\n0 0 0 0 re\n0 0 0 rg\n0 0 0 RG\nf*\nq\n");
        fb.Append($"BT\n/F0 {Format(ta.fontSize)} Tf\n");
        fb.Append($"{NormColor(fg.R)} {NormColor(fg.G)} {NormColor(fg.B)} rg\n");
        if (ta.ts?.StrokingColor is { } strokeCol)
            fb.Append($"{NormColor(strokeCol.R)} {NormColor(strokeCol.G)} {NormColor(strokeCol.B)} RG\n");
        if (ta.ts is not null && (int)ta.ts.RenderingMode != 0)
            fb.Append($"{(int)ta.ts.RenderingMode} Tr\n");
        for (int i = 0; i < ta.lines.Count; i++)
        {
            var lineY = lift + (ta.lines.Count - 1 - i) * ta.fontSize;
            fb.Append($"1 0 0 1 0 {Format(lineY)} Tm\n({EscapePdfString(ta.lines[i])}) Tj\n");
        }
        fb.Append("0 g\n1 0 0 1 0 0 Tm\nET\nQ\nQ\n");

        var fmName = AddTextStampFormWithBBox(ta.page, fb.ToString(), ta.fontName, bboxW, bboxH);

        double rad = ta.rot * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        // Shift the rotated block rect into the first quadrant: e/f undo the
        // most-negative rotated corner of [0,realW]×[0,blockH].
        double blockH = (ta.lines.Count + 0.1) * ta.fontSize;
        double minX = Math.Min(Math.Min(0, realW * cos),
                      Math.Min(-blockH * sin, realW * cos - blockH * sin));
        double minY = Math.Min(Math.Min(0, realW * sin),
                      Math.Min(blockH * cos, realW * sin + blockH * cos));
        if (ta.stamp.Opacity < 1f)
        {
            var gsName = ta.page.AddExtGState(new Content.ExtGState
            {
                FillAlpha = ta.stamp.Opacity,
            });
            ta.sb.Append($"/{gsName} gs\n");
        }
        ta.sb.Append($"{Format(cos)} {Format(sin)} {Format(-sin)} {Format(cos)} {Format(ta.ox - minX)} {Format(ta.oy - minY)} cm\n");
        ta.sb.Append($"/{fmName} Do\n");
    }

    /// <summary></summary>
    private static void PlaceRotatedTextStamp(TextStampApplyState ta)
    {
        // Rotated FOREGROUND stamp: the exact operator sequence of the historical
        // inline placement — the rotation cm followed by the text ops — wrapped in
        // a Form XObject invoked at IDENTITY, so the stamp lands in Resources.Forms
        // (the public-API shape) while the renderer walks an identical operator
        // stream and the era-calibrated placement stays pixel-exact. The BBox spans
        // the page so nothing the inline form drew is clipped away.
        double radInline = ta.rot * Math.PI / 180.0;
        double cosInline = Math.Cos(radInline), sinInline = Math.Sin(radInline);
        var inner = new StringBuilder();
        inner.Append($"{Format(cosInline)} {Format(sinInline)} {Format(-sinInline)} {Format(cosInline)} {Format(ta.ox)} {Format(ta.oy)} cm\n");
        inner.Append(DrawOps(ta, "F0"));
        var mboxFg = ta.page.MediaBox;
        var fmNameFg = AddTextStampFormCore(ta.page, inner.ToString(), ta.fontName,
            mboxFg.LLX, mboxFg.LLY, mboxFg.URX, mboxFg.URY);
        ta.sb.Append($"/{fmNameFg} Do\n");
    }
}
