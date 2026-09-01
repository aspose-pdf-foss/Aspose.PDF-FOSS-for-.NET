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
    private void RenderControlLines(ContentStreamBuilder builder, List<CellLine> cellLines,
        int firstLine, int lastLine, double leftX, double topY, double lineHeight, string fontName,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink = null,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink = null,
        double? seatBottom = null, Page? fontPage = null)
    {
        // A multi-line control cell stacks by each line's OWN height (a 10pt
        // spacer line directly above an 8.5pt box: the box top sits exactly
        // 10pt below the cell content top) instead of the row's uniform pitch.
        var exactStack = false;
        if (lastLine - firstLine > 1)
            for (var li = firstLine; li < lastLine; li++)
                if (cellLines[li].Checkbox is not null) { exactStack = true; break; }

        // Serif-faced tables (the DataWorks form grid and its kin) typeset the
        // flow text INSIDE control cells in the cell's serif face while button
        // captions stay in the UI sans; std14 Times-Roman advances track the
        // expected Times New Roman.
        var serifText = DwFormCells && fontPage is not null;
        var textFont = serifText ? RegisterFont(fontPage!, "Times-Roman") : fontName;
        double MeasureText(string ms, double mfs) => serifText
            ? MeasureTimesRoman(ms, mfs) : MeasureWidth(ms, mfs);

        var yCursor = topY;
        // The non-exact walk still advances a nested-grid RESERVE line by its own
        // FontSize (an equal share of the grid's real height) — the uniform pitch
        // over-advances a reserve that doesn't divide evenly and pushes everything
        // after the grid down by the quantization slack.
        var walkY = topY;
        for (var li = firstLine; li < lastLine; li++)
        {
            var line = cellLines[li];
            var lineTop = exactStack ? yCursor : walkY;
            // DataWorks control cells stack each line by its OWN box: button
            // lines take the button's real box (13 when another button line
            // follows — the boxes share margins; 15.6 otherwise, both
            // measured), other lines their css box. The row PLAN keeps
            // the plain 13.5 boxes — the rows are shorter than the
            // drawn button stack and the boxes overflow the row bottom.
            walkY -= DwFormCells && line.Text.IndexOf(InlineButtonChar) >= 0
                ? (li + 1 < lastLine
                    && cellLines[li + 1].Text.IndexOf(InlineButtonChar) >= 0
                    ? Converters.HtmlToPdfConverter.DwButtonFollowPt
                    : Converters.HtmlToPdfConverter.DwButtonLinePt)
                : DwFormCells && line.OwnLinePt > 0 ? line.OwnLinePt
                : line.ImgReserve && line.FontSize > 0 ? line.FontSize : lineHeight;
            yCursor -= line.Checkbox is { } adv && (adv.Height > 0)
                ? adv.Height
                : exactStack ? line.FontSize : lineHeight;
            var textX = leftX + line.LeftIndent;

            if (line.Checkbox is { } cbf)
            {
                var bw = cbf.Width > 0 ? cbf.Width : line.FontSize;
                var bh = cbf.Height > 0 ? cbf.Height : line.FontSize;
                // The widget's /AP draws the box + check glyph; just record its rectangle.
                // A cell holding only the control seats the box on the ROW bottom — a
                // lone checkbox bottom-aligns with the (taller) neighbouring text line
                // (widget (90,60)-(100,70) beside a 14 pt caption in a 14 pt row).
                var boxTop = cellLines.Count == 1 && seatBottom is { } sb ? sb + bh : lineTop;
                checkboxSink?.Add((cbf, new Rectangle(leftX, boxTop - bh, leftX + bw, boxTop)));
                textX = leftX + bw + 4;
            }

            if (line.Option is { } opt)
            {
                var glyphW = opt.Width > 0 ? opt.Width : line.FontSize;
                var glyphH = opt.Height > 0 ? opt.Height : line.FontSize;
                // Centre the glyph on the line; nudge it down from the cell top.
                var cx = leftX + glyphW / 2;
                var cy = lineTop - glyphH / 2;
                var c = opt.Characteristics.Border;
                DrawEllipse(builder, cx, cy, glyphW / 2, glyphH / 2,
                    c.R / 255.0, c.G / 255.0, c.B / 255.0);
                textX = leftX + glyphW + 4;
                // The option's widget annotation is placed over the glyph so it
                // round-trips as an interactive form control at the laid-out cell
                // position (the sink owner adds it to the page /Annots).
                optionSink?.Add((opt, new Rectangle(leftX, lineTop - glyphH, leftX + glyphW, lineTop)));
            }

            // Inline push buttons: each bracketed caption draws with the 3D
            // button chrome (face fill, bevel strokes, black outline), advancing the
            // pen by the whole outlined box; text outside the markers draws normally.
            // DataWorks form-grid controls: an InlineInputChar draws the input's
            // declared box with its value typeset inside (mono for textareas, at
            // the box top; sans for inputs, vertically centred, clipped to fit);
            // an InlineCheckChar draws a bare checkmark.
            if (line.Text.IndexOf(InlineInputChar) >= 0
                || line.Text.IndexOf(InlineCheckChar) >= 0
                || line.Text.IndexOf(InlineCheckboxGapChar) >= 0)
            {
                // A control line OPENED by a hidden checkbox (the upload cell's
                // filename) seats its whole run higher — it is drawn
                // 4.3 pt up from the uniform first-line seat; the walk advance
                // is untouched so the lines below hold their measured places.
                var dwBase = lineTop - line.FontSize
                    + (DwFormCells && line.Text.Length > 0
                        && line.Text[0] == InlineCheckboxGapChar
                        ? Converters.HtmlToPdfConverter.DwGapLineLiftPt : 0);
                var dwPen = textX;
                var dwBoxIdx = 0;
                var dwSb = new System.Text.StringBuilder();
                var dwConsumed = 0;
                Color? DwRunColorAt(int p)
                {
                    if (line.ColorRuns is not null)
                        foreach (var (rs, rl, rc) in line.ColorRuns)
                            if (p >= rs && p < rs + rl) return rc;
                    return null;
                }
                void DwFlush()
                {
                    if (dwSb.Length == 0) return;
                    var s2 = dwSb.ToString(); dwSb.Clear();
                    // Span-scoped colours: the red validation star draws in its
                    // own ink beside the black flow.
                    var segStart = 0;
                    while (segStart < s2.Length)
                    {
                        var segCol = DwRunColorAt(dwConsumed + segStart);
                        var segEnd = segStart + 1;
                        while (segEnd < s2.Length && Equals(DwRunColorAt(dwConsumed + segEnd), segCol)) segEnd++;
                        var seg = s2[segStart..segEnd];
                        builder.BeginText();
                        builder.SetFont(textFont, line.FontSize);
                        ApplyColor(builder, segCol ?? line.ForegroundColor);
                        builder.MoveTextPosition(dwPen, dwBase);
                        builder.ShowText(seg);
                        builder.EndText();
                        dwPen += MeasureText(seg, line.FontSize);
                        segStart = segEnd;
                    }
                }
                var dwTi = 0;
                foreach (var dch in line.Text)
                {
                    var dwThisIdx = dwTi++;
                    if (dch == InlineInputChar)
                    {
                        DwFlush();
                        if (line.InputBoxes is null || dwBoxIdx >= line.InputBoxes.Count) continue;
                        var (bw, bh, bval, bmono, blift) = line.InputBoxes[dwBoxIdx++];
                        var bx = dwPen + (DwFormCells
                            ? Converters.HtmlToPdfConverter.DwInputLeadPt : 0);
                        // box bottom rides a couple points under the baseline; a
                        // TALL box (the textarea) hangs from its line's top instead
                        var boxBottom = blift + (bh > 2 * line.FontSize
                            ? dwBase + line.FontSize - bh
                            // centred on the line, lifted (measured: the row-1
                            // input box top rides at its cell content top)
                            : dwBase - (bh - line.FontSize) / 2 + DwBoxSeatLiftPt);
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
                        dwPen = bx + bw + (DwFormCells
                            ? Converters.HtmlToPdfConverter.DwAfterBoxPenPt : 2);
                        continue;
                    }
                    if (dch == InlineCheckboxGapChar)
                    {
                        DwFlush();
                        dwPen += DwFormCells ? DwCheckboxDrawWPt : DwHiddenInlinePt;
                        continue;
                    }
                    if (dch == InlineCheckChar)
                    {
                        DwFlush();
                        var cs = DwFormCells ? DwCheckScale : 1.0;
                        var cox = DwFormCells ? DwCheckIndentPt : 0.0;
                        var coy = DwFormCells ? DwCheckRisePt : 0.0;
                        builder.SetStrokeColor(0, 0, 0);
                        builder.SetLineWidth(cs);
                        builder.MoveTo(dwPen + cox + 0.7 * cs, dwBase + coy + 2.6 * cs)
                            .LineTo(dwPen + cox + 2.4 * cs, dwBase + coy + 0.7 * cs)
                            .LineTo(dwPen + cox + 5.8 * cs, dwBase + coy + 5.6 * cs).Stroke();
                        dwPen += DwFormCells ? DwCheckboxDrawWPt : 7.5;
                        continue;
                    }
                    if (dwSb.Length == 0) dwConsumed = dwThisIdx;
                    dwSb.Append(dch);
                }
                DwFlush();
                continue;
            }
            if (line.Text.IndexOf(InlineButtonChar) >= 0)
            {
                var bScale = line.FontSize / InlineButtonProbeBasePt;
                // DataWorks: the caption draws in the 10 pt UI sans while the box
                // chrome keeps the line's 12 pt scale, and the whole control seats
                // one point higher (both measured on the expected buttons).
                var bCapFs = DwFormCells
                    ? Converters.HtmlToPdfConverter.DwButtonCapPt : line.FontSize;
                var bBase = lineTop - line.FontSize
                    + (DwFormCells ? Converters.HtmlToPdfConverter.DwButtonBoxRaisePt : 0);
                var bFaceTop = bBase + InlineButtonBaseDropPt * bScale;
                var bFaceH = InlineButtonFaceHPt * bScale;
                var bPen = textX;
                var bSb = new System.Text.StringBuilder();
                var bConsumed = 0;
                Color? RunColorAt(int p)
                {
                    if (line.ColorRuns is not null)
                        foreach (var (rs, rl, rc) in line.ColorRuns)
                            if (p >= rs && p < rs + rl) return rc;
                    return null;
                }
                void FlushButtonRun()
                {
                    if (bSb.Length == 0) return;
                    var s = bSb.ToString(); bSb.Clear();
                    // DataWorks: the whitespace between adjacent inputs collapses —
                    // the Search/Remove boxes touch edge to edge.
                    if (DwFormCells && s.Trim().Length == 0) return;
                    // Span-scoped colours: emit maximal same-colour segments.
                    var segStart = 0;
                    while (segStart < s.Length)
                    {
                        var segCol = RunColorAt(bConsumed + segStart);
                        var segEnd = segStart + 1;
                        while (segEnd < s.Length && Equals(RunColorAt(bConsumed + segEnd), segCol)) segEnd++;
                        var seg = s[segStart..segEnd];
                        builder.BeginText();
                        builder.SetFont(textFont, line.FontSize);
                        ApplyColor(builder, segCol ?? line.ForegroundColor);
                        builder.MoveTextPosition(bPen, bBase);
                        builder.ShowText(seg);
                        builder.EndText();
                        bPen += MeasureText(seg, line.FontSize);
                        segStart = segEnd;
                    }
                }
                var bti = 0;
                while (bti < line.Text.Length)
                {
                    var bch = line.Text[bti];
                    if (bch != InlineButtonChar)
                    {
                        if (bSb.Length == 0) bConsumed = bti;
                        bSb.Append(bch); bti++; continue;
                    }
                    FlushButtonRun();
                    var bEnd = line.Text.IndexOf(InlineButtonEndChar, bti + 1);
                    if (bEnd < 0) bEnd = line.Text.Length;
                    var bCap = line.Text[(bti + 1)..bEnd];
                    bti = Math.Min(bEnd + 1, line.Text.Length);
                    var bCapW = MeasureWidth(bCap, bCapFs);
                    var bFaceW = bCapW + (InlineButtonPadLPt + InlineButtonPadRPt) * bScale;
                    var bFaceX = bPen + InlineButtonOutlineOutHPt * bScale;
                    // Face fill.
                    builder.SetFillColor(InlineButtonFaceGray, InlineButtonFaceGray, InlineButtonFaceGray);
                    builder.Rectangle(bFaceX, bFaceTop - bFaceH, bFaceW, bFaceH);
                    builder.Fill();
                    // Bevel strokes: left/right verticals + bottom horizontal.
                    var bIn = InlineButtonBevelInsetPt * bScale;
                    builder.SetStrokeColor(InlineButtonBevelGray, InlineButtonBevelGray, InlineButtonBevelGray);
                    builder.SetLineWidth(InlineButtonBevelWPt * bScale);
                    builder.MoveTo(bFaceX + bIn, bFaceTop - bFaceH).LineTo(bFaceX + bIn, bFaceTop).Stroke();
                    builder.MoveTo(bFaceX + bFaceW - bIn, bFaceTop - bFaceH).LineTo(bFaceX + bFaceW - bIn, bFaceTop).Stroke();
                    builder.MoveTo(bFaceX, bFaceTop - bFaceH + bIn).LineTo(bFaceX + bFaceW, bFaceTop - bFaceH + bIn).Stroke();
                    // Black outline around the face — except the DataWorks file
                    // control, whose reference chrome is a flat gray border.
                    if (DwFormCells
                        && bCap == Converters.HtmlToPdfConverter.DwFileButtonCaption)
                        builder.SetStrokeColor(InlineButtonBevelGray,
                            InlineButtonBevelGray, InlineButtonBevelGray);
                    else
                        builder.SetStrokeColor(0, 0, 0);
                    builder.SetLineWidth(1.0);
                    builder.Rectangle(bFaceX - InlineButtonOutlineOutHPt * bScale,
                        bFaceTop - bFaceH - InlineButtonOutlineOutVPt * bScale,
                        bFaceW + 2 * InlineButtonOutlineOutHPt * bScale,
                        bFaceH + 2 * InlineButtonOutlineOutVPt * bScale);
                    builder.Stroke();
                    // Caption inside the face.
                    builder.BeginText();
                    builder.SetFont(fontName, bCapFs);
                    ApplyColor(builder, line.ForegroundColor);
                    builder.MoveTextPosition(bFaceX + InlineButtonPadLPt * bScale, bBase);
                    builder.ShowText(bCap);
                    builder.EndText();
                    bPen = bFaceX + bFaceW + InlineButtonOutlineOutHPt * bScale;
                }
                FlushButtonRun();
                continue;
            }

            // Inline radio options: the line's marker chars draw as circle glyphs IN
            // the text run (`◯ ◯Yes ◉ ◉No` on one line), each advancing the pen by
            // the control box; caption text between markers draws normally.
            if (line.InlineOptions is { Count: > 0 } inlineOpts)
            {
                var iScale = line.FontSize / InlineRadioProbeBasePt;
                // DataWorks radios draw the full 12 pt widget circle with the
                // caption at its right edge (no trailing gap).
                var iLeadPt = DwFormCells ? DwRadioLeadPt : InlineRadioLeadPt;
                var iDPt = DwFormCells ? DwRadioGlyphDPt : InlineRadioGlyphDPt;
                var iTrailPt = DwFormCells ? 0.0 : InlineRadioTrailPt;
                var iGlyphD = iDPt * iScale;
                var iBase = lineTop - line.FontSize;
                // The circle's centre rides just above the caption baseline.
                var iCy = iBase + InlineRadioCenterRisePt * iScale;
                var pen = textX;
                var oi = 0;
                var runSb = new System.Text.StringBuilder();
                void FlushRun()
                {
                    if (runSb.Length == 0) return;
                    var s = runSb.ToString(); runSb.Clear();
                    builder.BeginText();
                    builder.SetFont(textFont, line.FontSize);
                    ApplyColor(builder, line.ForegroundColor);
                    builder.MoveTextPosition(pen, iBase);
                    builder.ShowText(s);
                    builder.EndText();
                    pen += MeasureText(s, line.FontSize);
                }
                foreach (var ch in line.Text)
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
                continue;
            }

            if (!string.IsNullOrEmpty(line.Text))
            {
                if (line.LinkRuns is { Count: > 0 } || line.Hyperlink is not null)
                {
                    ShowLineWithLinks(builder, line, textFont, textX, lineTop - line.FontSize);
                    continue;
                }
                builder.BeginText();
                builder.SetFont(textFont, line.FontSize);
                ApplyColor(builder, line.ForegroundColor);
                builder.MoveTextPosition(textX, lineTop - line.FontSize);
                builder.ShowText(line.Text);
                builder.EndText();
            }
        }
    }
}
