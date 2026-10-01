using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
// Inline button rendering of a control line: the run colour lookup, the pending-text flush and the per-character placement.
    private static Color? RunColorAt(ControlLinesState cl, int p)
    {
        if (cl.line.ColorRuns is not null)
            foreach (var (rs, rl, rc) in cl.line.ColorRuns)
                if (p >= rs && p < rs + rl) return rc;
        return null;
    }

    private void FlushButtonRun(ControlLinesState cl, ContentStreamBuilder builder)
    {
        if (cl.bSb.Length == 0) return;
        var s = cl.bSb.ToString(); cl.bSb.Clear();
        // DataWorks: the whitespace between adjacent inputs collapses —
        // the Search/Remove boxes touch edge to edge.
        if (DwFormCells && s.Trim().Length == 0) return;
        // Span-scoped colours: emit maximal same-colour segments.
        var segStart = 0;
        while (segStart < s.Length)
        {
            var segCol = RunColorAt(cl, cl.bConsumed + segStart);
            var segEnd = segStart + 1;
            while (segEnd < s.Length && Equals(RunColorAt(cl, cl.bConsumed + segEnd), segCol)) segEnd++;
            var seg = s[segStart..segEnd];
            builder.BeginText();
            builder.SetFont(cl.textFont, cl.line.FontSize);
            ApplyColor(builder, segCol ?? cl.line.ForegroundColor);
            builder.MoveTextPosition(cl.bPen, cl.bBase);
            builder.ShowText(seg);
            builder.EndText();
            cl.bPen += MeasureText(cl, seg, cl.line.FontSize);
            segStart = segEnd;
        }
    }

    /// <summary>One character of an inline-button line: text joins the pending run, a button marker flushes it and frames the button face at the pen.</summary>
    private bool PlaceInlineButtonChar(ControlLinesState cl, ContentStreamBuilder builder, string fontName)
    {
        var bch = cl.line.Text[cl.bti];
        if (bch != InlineButtonChar)
        {
            if (cl.bSb.Length == 0) cl.bConsumed = cl.bti;
            cl.bSb.Append(bch); cl.bti++; return true;
        }
        FlushButtonRun(cl, builder);
        var bEnd = cl.line.Text.IndexOf(InlineButtonEndChar, cl.bti + 1);
        if (bEnd < 0) bEnd = cl.line.Text.Length;
        var bCap = cl.line.Text[(cl.bti + 1)..bEnd];
        cl.bti = Math.Min(bEnd + 1, cl.line.Text.Length);
        var bCapW = MeasureWidth(bCap, cl.bCapFs);
        var bFaceW = bCapW + (InlineButtonPadLPt + InlineButtonPadRPt) * cl.bScale;
        var bFaceX = cl.bPen + InlineButtonOutlineOutHPt * cl.bScale;
        // Face fill.
        builder.SetFillColor(InlineButtonFaceGray, InlineButtonFaceGray, InlineButtonFaceGray);
        builder.Rectangle(bFaceX, cl.bFaceTop - cl.bFaceH, bFaceW, cl.bFaceH);
        builder.Fill();
        // Bevel strokes: left/right verticals + bottom horizontal.
        var bIn = InlineButtonBevelInsetPt * cl.bScale;
        builder.SetStrokeColor(InlineButtonBevelGray, InlineButtonBevelGray, InlineButtonBevelGray);
        builder.SetLineWidth(InlineButtonBevelWPt * cl.bScale);
        builder.MoveTo(bFaceX + bIn, cl.bFaceTop - cl.bFaceH).LineTo(bFaceX + bIn, cl.bFaceTop).Stroke();
        builder.MoveTo(bFaceX + bFaceW - bIn, cl.bFaceTop - cl.bFaceH).LineTo(bFaceX + bFaceW - bIn, cl.bFaceTop).Stroke();
        builder.MoveTo(bFaceX, cl.bFaceTop - cl.bFaceH + bIn).LineTo(bFaceX + bFaceW, cl.bFaceTop - cl.bFaceH + bIn).Stroke();
        // Black outline around the face — except the DataWorks file
        // control, whose reference chrome is a flat gray border.
        if (DwFormCells
            && bCap == Converters.HtmlToPdfConverter.DwFileButtonCaption)
            builder.SetStrokeColor(InlineButtonBevelGray,
                InlineButtonBevelGray, InlineButtonBevelGray);
        else
            builder.SetStrokeColor(0, 0, 0);
        builder.SetLineWidth(1.0);
        builder.Rectangle(bFaceX - InlineButtonOutlineOutHPt * cl.bScale,
            cl.bFaceTop - cl.bFaceH - InlineButtonOutlineOutVPt * cl.bScale,
            bFaceW + 2 * InlineButtonOutlineOutHPt * cl.bScale,
            cl.bFaceH + 2 * InlineButtonOutlineOutVPt * cl.bScale);
        builder.Stroke();
        // Caption inside the face.
        builder.BeginText();
        builder.SetFont(fontName, cl.bCapFs);
        ApplyColor(builder, cl.line.ForegroundColor);
        builder.MoveTextPosition(bFaceX + InlineButtonPadLPt * cl.bScale, cl.bBase);
        builder.ShowText(bCap);
        builder.EndText();
        cl.bPen = bFaceX + bFaceW + InlineButtonOutlineOutHPt * cl.bScale;
        return true;
    }
}
