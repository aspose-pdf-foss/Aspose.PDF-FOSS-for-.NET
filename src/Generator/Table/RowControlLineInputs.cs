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
// Inline text-input rendering of a control line: the run colour lookup, the pending-text flush and the per-character placement.
    private static Color? DwRunColorAt(ControlLinesState cl, int p)
    {
        if (cl.line.ColorRuns is not null)
            foreach (var (rs, rl, rc) in cl.line.ColorRuns)
                if (p >= rs && p < rs + rl) return rc;
        return null;
    }

    private static void DwFlush(ControlLinesState cl, ContentStreamBuilder builder)
    {
        if (cl.dwSb.Length == 0) return;
        var s2 = cl.dwSb.ToString(); cl.dwSb.Clear();
        // Span-scoped colours: the red validation star draws in its
        // own ink beside the black flow.
        var segStart = 0;
        while (segStart < s2.Length)
        {
            var segCol = DwRunColorAt(cl, cl.dwConsumed + segStart);
            var segEnd = segStart + 1;
            while (segEnd < s2.Length && Equals(DwRunColorAt(cl, cl.dwConsumed + segEnd), segCol)) segEnd++;
            var seg = s2[segStart..segEnd];
            builder.BeginText();
            builder.SetFont(cl.textFont, cl.line.FontSize);
            ApplyColor(builder, segCol ?? cl.line.ForegroundColor);
            builder.MoveTextPosition(cl.dwPen, cl.dwBase);
            builder.ShowText(seg);
            builder.EndText();
            cl.dwPen += MeasureText(cl, seg, cl.line.FontSize);
            segStart = segEnd;
        }
    }

    /// <summary>One character of an inline-input line: text characters join the pending run, an input marker flushes it and frames the box at the pen.</summary>
    private bool PlaceInlineInputChar(char dch, ControlLinesState cl, ContentStreamBuilder builder, string fontName, Page? fontPage)
    {
        var dwThisIdx = cl.dwTi++;
        if (dch == InlineInputChar)
        {
            DwFlush(cl, builder);
            if (cl.line.InputBoxes is null || cl.dwBoxIdx >= cl.line.InputBoxes.Count) return true;
            var (bw, bh, bval, bmono, blift) = cl.line.InputBoxes[cl.dwBoxIdx++];
            var bx = cl.dwPen + (DwFormCells
                ? Converters.HtmlToPdfConverter.DwInputLeadPt : 0);
            // box bottom rides a couple points under the baseline; a
            // TALL box (the textarea) hangs from its line's top instead
            var boxBottom = blift + (bh > 2 * cl.line.FontSize
                ? cl.dwBase + cl.line.FontSize - bh
                // centred on the line, lifted (measured: the row-1
                // input box top rides at its cell content top)
                : cl.dwBase - (bh - cl.line.FontSize) / 2 + DwBoxSeatLiftPt);
            builder.SetFillColor(1, 1, 1);
            builder.Rectangle(bx, boxBottom, bw, bh);
            builder.Fill();
            // DataWorks control borders render as the expected
            // inset chrome: two full-intensity device rows of the
            // dark gray that sits within the channel budget of every
            // measured side (top 64, right 32, bottom 0).
            if (DwFormCells)
            {
                builder.SetStrokeColor(
                    Converters.HtmlToPdfConverter.DwBoxBorderGray,
                    Converters.HtmlToPdfConverter.DwBoxBorderGray,
                    Converters.HtmlToPdfConverter.DwBoxBorderGray);
                builder.SetLineWidth(0.96);
            }
            else
            {
                builder.SetStrokeColor(0, 0, 0);
                builder.SetLineWidth(0.75);
            }
            builder.Rectangle(bx, boxBottom, bw, bh);
            builder.Stroke();
            if (!string.IsNullOrEmpty(bval))
            {
                var vFont = fontPage is not null
                    ? RegisterFont(fontPage, bmono ? "Courier" : "Helvetica")
                    : fontName;
                var fit = bval;
                // dw values measure in the real UI sans with the
                // tight 2 pt clip inset ('creatio' stays
                // visible in the 177px precis box).
                while (fit.Length > 1
                       && (bmono ? fit.Length * 0.6 * DwValuePt
                           : DwFormCells ? MeasureHelvetica(fit, DwValuePt)
                           : MeasureWidth(fit, DwValuePt))
                           > bw - (DwFormCells && !bmono ? 2 : 4))
                    fit = fit[..^1];
                var vBase = bmono
                    ? boxBottom + bh - DwValuePt
                        + (DwFormCells
                            ? Converters.HtmlToPdfConverter.DwMonoValueRaisePt : -1.5)
                    : boxBottom + (bh - DwValuePt) / 2 + 1.5;
                builder.BeginText();
                builder.SetFont(vFont, DwValuePt);
                builder.SetFillColor(0, 0, 0);
                builder.MoveTextPosition(bx + 2.5, vBase);
                builder.ShowText(fit);
                builder.EndText();
            }
            cl.dwPen = bx + bw + (DwFormCells
                ? Converters.HtmlToPdfConverter.DwAfterBoxPenPt : 2);
            return true;
        }
        if (dch == InlineCheckboxGapChar)
        {
            DwFlush(cl, builder);
            cl.dwPen += DwFormCells ? DwCheckboxDrawWPt : DwHiddenInlinePt;
            return true;
        }
        if (dch == InlineCheckChar)
        {
            DwFlush(cl, builder);
            var cs = DwFormCells ? DwCheckScale : 1.0;
            var cox = DwFormCells ? DwCheckIndentPt : 0.0;
            var coy = DwFormCells ? DwCheckRisePt : 0.0;
            builder.SetStrokeColor(0, 0, 0);
            builder.SetLineWidth(cs);
            builder.MoveTo(cl.dwPen + cox + 0.7 * cs, cl.dwBase + coy + 2.6 * cs)
                .LineTo(cl.dwPen + cox + 2.4 * cs, cl.dwBase + coy + 0.7 * cs)
                .LineTo(cl.dwPen + cox + 5.8 * cs, cl.dwBase + coy + 5.6 * cs).Stroke();
            cl.dwPen += DwFormCells ? DwCheckboxDrawWPt : 7.5;
            return true;
        }
        if (cl.dwSb.Length == 0) cl.dwConsumed = dwThisIdx;
        cl.dwSb.Append(dch);
        return true;
    }
}
