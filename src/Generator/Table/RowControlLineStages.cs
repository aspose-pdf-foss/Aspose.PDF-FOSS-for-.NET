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
// Per-line rendering of a cell's control lines: text measurement and the line stage.
    private static double MeasureText(ControlLinesState cl, string ms, double mfs) => cl.serifText
        ? MeasureTimesRoman(ms, mfs) : MeasureWidth(ms, mfs);

    /// <summary>Render one control line of the cell: its checkbox, radio option, inline inputs, inline buttons, inline options and text at the walked or stacked position.</summary>
    private bool RenderControlLine(ControlLinesState cl, int li, ContentStreamBuilder builder, List<CellLine> cellLines, int lastLine, double leftX, double lineHeight, string fontName, List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink, List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink, double? seatBottom, Page? fontPage)
    {
        cl.line = cellLines[li];
        cl.lineTop = cl.exactStack ? cl.yCursor : cl.walkY;
        // DataWorks control cells stack each line by its OWN box: button
        // lines take the button's real box (13 when another button line
        // follows — the boxes share margins; 15.6 otherwise, both
        // measured), other lines their css box. The row PLAN keeps
        // the plain 13.5 boxes — the rows are shorter than the
        // drawn button stack and the boxes overflow the row bottom.
        cl.walkY -= DwFormCells && cl.line.Text.IndexOf(InlineButtonChar) >= 0
            ? (li + 1 < lastLine
                && cellLines[li + 1].Text.IndexOf(InlineButtonChar) >= 0
                ? Converters.HtmlToPdfConverter.DwButtonFollowPt
                : Converters.HtmlToPdfConverter.DwButtonLinePt)
            : DwFormCells && cl.line.OwnLinePt > 0 ? cl.line.OwnLinePt
            : cl.line.ImgReserve && cl.line.FontSize > 0 ? cl.line.FontSize : lineHeight;
        cl.yCursor -= cl.line.Checkbox is not null && HtmlUaControlGrid && cl.line.BoxH > 0 ? cl.line.BoxH
            : cl.line.Checkbox is { } adv && (adv.Height > 0)
            ? adv.Height
            : cl.exactStack ? cl.line.FontSize : lineHeight;
        cl.textX = leftX + cl.line.LeftIndent;

        PlaceControlLineCheckbox(cl, cellLines, leftX, seatBottom, checkboxSink);

        if (cl.line.Option is { } opt)
        {
            var glyphW = opt.Width > 0 ? opt.Width : cl.line.FontSize;
            var glyphH = opt.Height > 0 ? opt.Height : cl.line.FontSize;
            // Centre the glyph on the line; nudge it down from the cell top.
            var cx = leftX + glyphW / 2;
            var cy = cl.lineTop - glyphH / 2;
            var c = opt.Characteristics.Border;
            DrawEllipse(builder, cx, cy, glyphW / 2, glyphH / 2,
                c.R / 255.0, c.G / 255.0, c.B / 255.0);
            cl.textX = leftX + glyphW + 4;
            // The option's widget annotation is placed over the glyph so it
            // round-trips as an interactive form control at the laid-out cell
            // position (the sink owner adds it to the page /Annots).
            optionSink?.Add((opt, new Rectangle(leftX, cl.lineTop - glyphH, leftX + glyphW, cl.lineTop)));
        }

        // Inline push buttons: each bracketed caption draws with the 3D
        // button chrome (face fill, bevel strokes, black outline), advancing the
        // pen by the whole outlined box; text outside the markers draws normally.
        // DataWorks form-grid controls: an InlineInputChar draws the input's
        // declared box with its value typeset inside (mono for textareas, at
        // the box top; sans for inputs, vertically centred, clipped to fit);
        // an InlineCheckChar draws a bare checkmark.
        if (cl.line.Text.IndexOf(InlineInputChar) >= 0
            || cl.line.Text.IndexOf(InlineCheckChar) >= 0
            || cl.line.Text.IndexOf(InlineCheckboxGapChar) >= 0)
        {
            if (RenderInlineInputs(cl, builder, fontName, fontPage)) return true;
        }
        if (cl.line.Text.IndexOf(InlineButtonChar) >= 0)
        {
            if (RenderInlineButtons(cl, builder, fontName)) return true;
        }

        // Inline radio options: the line's marker chars draw as circle glyphs IN
        // the text run (`◯ ◯Yes ◉ ◉No` on one line), each advancing the pen by
        // the control box; caption text between markers draws normally.
        if (cl.line.InlineOptions is { Count: > 0 } inlineOpts)
        {
            if (RenderInlineOptions(inlineOpts, cl, builder, optionSink)) return true;
        }

        if (!string.IsNullOrEmpty(cl.line.Text))
        {
            if (cl.line.LinkRuns is { Count: > 0 } || cl.line.Hyperlink is not null)
            {
                ShowLineWithLinks(builder, cl.line, cl.textFont, cl.textX, cl.lineTop - cl.line.FontSize);
                return true;
            }
            builder.BeginText();
            builder.SetFont(cl.textFont, cl.line.FontSize);
            ApplyColor(builder, cl.line.ForegroundColor);
            builder.MoveTextPosition(cl.textX, cl.lineTop - cl.line.FontSize);
            builder.ShowText(cl.line.Text);
            builder.EndText();
        }
        return true;
    }

    /// <summary>A checkbox line's widget: the UA grid seats it inside its inline box on the baseline; the generator cell seats it at the line top (a lone box on the row bottom).</summary>
    private void PlaceControlLineCheckbox(ControlLinesState cl, List<CellLine> cellLines, double leftX, double? seatBottom,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink)
    {
        if (cl.line.Checkbox is { } uaCbf && HtmlUaControlGrid)
        {
            // the widget sits one point inside the inline box, whose bottom is the baseline
            var uaBase = cl.lineTop - cl.line.FontSize;
            var uaX = cl.textX + UaCheckboxLeadPt + UaCheckboxWidgetInsetPt;
            var uaY = uaBase - UaCheckboxWidgetInsetPt;
            checkboxSink?.Add((uaCbf, new Rectangle(uaX, uaY, uaX + UaCheckboxWidgetPt, uaY + UaCheckboxWidgetPt)));
            cl.textX += UaCheckboxMarginBoxPt;
        }
        else if (cl.line.Checkbox is { } cbf)
        {
            var bw = cbf.Width > 0 ? cbf.Width : cl.line.FontSize;
            var bh = cbf.Height > 0 ? cbf.Height : cl.line.FontSize;
            // The widget's /AP draws the box + check glyph; just record its rectangle.
            // A cell holding only the control seats the box on the ROW bottom — a
            // lone checkbox bottom-aligns with the (taller) neighbouring text line
            // (widget (90,60)-(100,70) beside a 14 pt caption in a 14 pt row).
            var boxTop = cellLines.Count == 1 && seatBottom is { } sb ? sb + bh : cl.lineTop;
            checkboxSink?.Add((cbf, new Rectangle(leftX, boxTop - bh, leftX + bw, boxTop)));
            cl.textX = leftX + bw + 4;
        }
    }

    /// <summary>The line's inline radio options: each drawn at its pen with its label, the option rectangles booked for the sink.</summary>
    private bool RenderInlineOptions(List<Aspose.Pdf.Forms.RadioButtonOptionField> inlineOpts, ControlLinesState cl, ContentStreamBuilder builder, List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink)
    {
        var iScale = cl.line.FontSize / InlineRadioProbeBasePt;
        // DataWorks radios draw the full 12 pt widget circle with the
        // caption at its right edge (no trailing gap).
        var iLeadPt = DwFormCells ? DwRadioLeadPt : InlineRadioLeadPt;
        var iDPt = DwFormCells ? DwRadioGlyphDPt : InlineRadioGlyphDPt;
        var iTrailPt = DwFormCells ? 0.0 : InlineRadioTrailPt;
        var iGlyphD = iDPt * iScale;
        var iBase = cl.lineTop - cl.line.FontSize;
        // The circle's centre rides just above the caption baseline.
        var iCy = iBase + InlineRadioCenterRisePt * iScale;
        var pen = cl.textX;
        var oi = 0;
        var runSb = new System.Text.StringBuilder();
        void FlushRun()
        {
            if (runSb.Length == 0) return;
            var s = runSb.ToString(); runSb.Clear();
            builder.BeginText();
            builder.SetFont(cl.textFont, cl.line.FontSize);
            ApplyColor(builder, cl.line.ForegroundColor);
            builder.MoveTextPosition(pen, iBase);
            builder.ShowText(s);
            builder.EndText();
            pen += MeasureText(cl, s, cl.line.FontSize);
        }
        foreach (var ch in cl.line.Text)
        {
            if (ch is not (InlineRadioChar or InlineRadioCheckedChar))
            {
                runSb.Append(ch);
                continue;
            }
            FlushRun();
            pen += iLeadPt * iScale;
            var icx = pen + iGlyphD / 2;
            DrawEllipse(builder, icx, iCy, iGlyphD / 2, iGlyphD / 2, 0, 0, 0);
            if (ch == InlineRadioCheckedChar)
            {
                builder.SetFillColor(0, 0, 0);
                FillEllipse(builder, icx, iCy,
                    InlineRadioDotDPt / 2 * iScale, InlineRadioDotDPt / 2 * iScale);
            }
            if (oi < inlineOpts.Count)
            {
                inlineOpts[oi].InlineGlyphDrawn = true;
                optionSink?.Add((inlineOpts[oi++],
                    new Rectangle(pen, iCy - iGlyphD / 2, pen + iGlyphD, iCy + iGlyphD / 2)));
            }
            pen += (iDPt + iTrailPt) * iScale;
        }
        FlushRun();
        return true;
    }

    /// <summary>A line carrying inline buttons: the text segments and the buttons between them, each button framed at its measured width.</summary>
    private bool RenderInlineButtons(ControlLinesState cl, ContentStreamBuilder builder, string fontName)
    {
        cl.bScale = cl.line.FontSize / InlineButtonProbeBasePt;
        cl.bCapFs = DwFormCells
            ? Converters.HtmlToPdfConverter.DwButtonCapPt : cl.line.FontSize;
        cl.bBase = cl.lineTop - cl.line.FontSize
            + (DwFormCells ? Converters.HtmlToPdfConverter.DwButtonBoxRaisePt : 0);
        cl.bFaceTop = cl.bBase + InlineButtonBaseDropPt * cl.bScale;
        cl.bFaceH = InlineButtonFaceHPt * cl.bScale;
        cl.bPen = cl.textX;
        cl.bSb = new System.Text.StringBuilder();
        cl.bConsumed = 0;
        cl.bti = 0;
        while (cl.bti < cl.line.Text.Length)
        {
            if (!PlaceInlineButtonChar(cl, builder, fontName)) break;
        }
        FlushButtonRun(cl, builder);
        return true;
    }

    /// <summary>A line carrying inline text inputs: the text segments and the input boxes between them, framed and seated on the line.</summary>
    private bool RenderInlineInputs(ControlLinesState cl, ContentStreamBuilder builder, string fontName, Page? fontPage)
    {
        cl.dwBase = cl.lineTop - cl.line.FontSize
            + (DwFormCells && cl.line.Text.Length > 0
                && cl.line.Text[0] == InlineCheckboxGapChar
                ? Converters.HtmlToPdfConverter.DwGapLineLiftPt : 0);
        cl.dwPen = cl.textX;
        cl.dwBoxIdx = 0;
        cl.dwSb = new System.Text.StringBuilder();
        cl.dwConsumed = 0;
        cl.dwTi = 0;
        foreach (var dch in cl.line.Text)
        {
            if (!PlaceInlineInputChar(dch, cl, builder, fontName, fontPage)) break;
        }
        DwFlush(cl, builder);
        return true;
    }
}
