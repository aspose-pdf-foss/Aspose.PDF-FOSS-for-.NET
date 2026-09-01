using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static void RenderMetricRows(MetricParseState mps, List<List<MetricCell>> rows, double[] colW, int nCols,
        double availW, double s, double lineH, string face, string boldFace, double hheaSum, (double asc, double sum) fm,
        double p, double pageWidth, double pageHeight, double marginTop, double marginBottom,
        double tableWpt, double tablePct, double baseFontSize, bool paragraphCells, string tableHtml,
        double symInsetPt, IReadOnlyDictionary<string, Dictionary<string, string>> css, Document doc,
        Core.PdfDictionary docFontDict, HtmlLoadOptions? loadOptions, System.Globalization.CultureInfo invc,
        Dictionary<string, string> flatRes, bool reportCells, bool serifReportCells, bool stdSerif, bool wrapperStacks,
        double collapseBoxW, double[] rowSpanExtra, bool tableHasText, bool tableRuleFace, double tableX,
        double marginLeft, double contentWidth, ref Page page, ref double y)
    {
    for (var ri = 0; ri < rows.Count; ri++)
    {
        var r = rows[ri];
        // an all-empty row still holds one line box; a row whose every
        // sized cell takes its font from a CLASS skin is content-paced (the
        // boleto rows carry no base-font strut)
        var classPaced = false;
        if (mps.widthClassTable)
            foreach (var mc in r)
            {
                if (mc.Text.Length == 0) continue;
                if (mc.FontFromClass || (mps.tableClassFont && mc.FontSize is null))
                { classPaced = true; }
                else { classPaced = false; break; }
            }
        double rowContentH = tableHasText && !classPaced ? lineH : 0;
        // A ROWSPAN cell's content overlays the FOLLOWING rows — it never
        // inflates its own row's box (the header's rowspan=4 address cell).
        // …and an nbsp-only cell in a row with REAL text never raises it
        // either (the financial grid's 12 pt spacer cells ride their 11 pt
        // label rows at the labels' pitch — probed on the statement ladder).
        var rowRealTextH = 0.0;
        var rowHasRealText = false;
        foreach (var mc in r)
            if (mc.RowSpan <= 1 && mc.Text.Replace(" ", "").Trim().Length > 0)
            {
                rowHasRealText = true;
                rowRealTextH = Math.Max(rowRealTextH, mc.ContentH);
            }
        foreach (var mc in r)
            if (mc.RowSpan <= 1)
            {
                var mcH = mc.ContentH;
                if (rowHasRealText && stdSerif
                    && mc.Text.Length > 0
                    && mc.Text.Replace(" ", "").Trim().Length == 0)
                    mcH = Math.Min(mcH, rowRealTextH);
                rowContentH = Math.Max(rowContentH, mcH);
            }
        // Statement idiom: a row with real text pitches on that text's own
        // line boxes — the table's 12 pt strut floor does not apply. A
        // blank nbsp spacer row pitches on the 1.2 normal line box of its
        // cells' size instead (probed: the 12 pt spacers band 14.25 = 19px).
        if (mps.inlineStatementGrid && rowHasRealText && rowRealTextH > 0
            && rowRealTextH < rowContentH)
            rowContentH = rowRealTextH;
        else if (mps.inlineStatementGrid && !rowHasRealText && rowContentH > 0)
        {
            double blankFs = 0;
            foreach (var mc in r)
                if (mc.Text.Length > 0)
                    blankFs = Math.Max(blankFs, mc.FontSize ?? mps.fontSize);
            if (blankFs > 0)
                rowContentH = MetricLineHeight(blankFs, Table.CssNormalLineHeight);
        }
        // …and a rule-carrying row is taller by its border width (probed:
        // the 1pt single rules add 1, the 2.8pt double adds 2.84).
        if (mps.inlineStatementGrid)
        {
            double rowRuleW = 0;
            foreach (var mc in r)
                rowRuleW = Math.Max(rowRuleW, mc.BorderBottomW);
            rowContentH += rowRuleW;
        }
        if (ri < rowSpanExtra.Length) rowContentH += rowSpanExtra[ri];
        // A row whose every cell is TRULY empty (no text, not even an
        // &nbsp;) keeps no line strut — its band is the padding alone
        // (measured: the empty spacer row is exactly 2p + s;
        // nbsp spacer rows keep their calibrated line boxes).
        if (stdSerif && wrapperStacks && rowContentH > 0)
        {
            var rowTrulyEmpty = true;
            foreach (var mc in r)
                if (mc.Text.Length > 0 || mc.DivSegs is { Count: > 0 }
                    || mc.SubTables is { Count: > 0 } || mc.ImgHPt > 0
                    || mc.HrRule)
                { rowTrulyEmpty = false; break; }
            if (rowTrulyEmpty)
                rowContentH = ri < rowSpanExtra.Length ? rowSpanExtra[ri] : 0;
        }
        // Outer-frame collapse grid: an all-empty row is its padding alone
        // (the height-0 width-setter and blank separator rows: 1.5 pt bands).
        if (collapseBoxW > 0)
        {
            var cbRowHasText = false;
            foreach (var mc in r)
                if (mc.Text.Trim().Length > 0) { cbRowHasText = true; break; }
            if (!cbRowHasText) rowContentH = 0;
        }
        var rowBoxH = rowContentH + 2 * p + (mps.collapsedGrid ? 0.75 : 0);
        var rowNaturalBoxH = rowBoxH;
        // a tr style height floors the row's box (the letter's paced rows);
        // a CLASS height — on the row or a cell — paces it exactly (the
        // boleto's h13/h12 grid rows and its 1px .cut tear-off row)
        double rowCellClassH = 0;
        var rowHasText = false;
        foreach (var mc in r)
        {
            rowCellClassH = Math.Max(rowCellClassH, mc.HeightPt);
            // report cells hold their ink in SEGMENTS (and images) — a
            // declared row height is a MIN for those too, not an override
            if (mc.Text.Length > 0 || (reportCells
                && (mc.DivSegs is { Count: > 0 } || mc.SubTables is { Count: > 0 }
                    || mc.ImgHPt > 0)))
                rowHasText = true;
        }
        var rowClassH = rowCellClassH;
        if (ri < mps.rowHeights.Count && mps.rowHeightExact[ri])
            rowClassH = Math.Max(rowClassH, mps.rowHeights[ri]);
        if (rowClassH > 0)
        {
            // a class height is a MIN-height: the two-line address row
            // outgrows its h12; an EMPTY spacer/rule row is EXACTLY the
            // declared height (the 1px .cut tear-off keeps no line floor)
            if (!rowHasText) rowBoxH = rowClassH;
            else if (rowClassH > rowBoxH) rowBoxH = rowClassH;
        }
        else if (ri < mps.rowHeights.Count && mps.rowHeights[ri] > rowBoxH) rowBoxH = mps.rowHeights[ri];
        // A report grid's WIDTH-SETTER row (every cell empty, inline
        // WIDTH+MIN-WIDTH pairs, no height anywhere) sizes the columns
        // and occupies NO band of its own.
        if (!rowHasText && rowCellClassH == 0
            && !(ri < mps.rowHeights.Count && mps.rowHeights[ri] > 0))
        {
            var wsSetter = false;
            var wsBare = true;
            foreach (var mc in r)
            {
                if (mc.Text.Length > 0 || mc.SubTables is { Count: > 0 }
                    || mc.DivSegs is { Count: > 0 } || mc.ImgHPt > 0)
                { wsBare = false; break; }
                if (mc.WidthSetterCell) wsSetter = true;
            }
            if (wsBare && wsSetter) rowBoxH = 0;
        }
        // report mode: a WHITESPACE-only row (an &nbsp; spacer) with a
        // declared height IS that height — its blank line box carries no
        // strut of its own (the sidebar's 13px separator rows)
        if (paragraphCells && !stdSerif && wrapperStacks && ri < mps.rowHeights.Count && mps.rowHeights[ri] > 0)
        {
            var allWsRow = true;
            foreach (var mc in r)
            {
                if (mc.SubTables is { Count: > 0 } || mc.DivSegs is { Count: > 0 }
                    || mc.ImgHPt > 0) { allWsRow = false; break; }
                foreach (var ch in mc.Text)
                    if (ch is not (' ' or '\u00A0' or '\u0001')) { allWsRow = false; break; }
                if (!allWsRow) break;
            }
            if (allWsRow) rowBoxH = mps.rowHeights[ri];
        }

        // Band rows (the table's inline-style height shared to the rows):
        // content centres in the grown box (probed: the 135px band's cell
        // baselines sit on the band's vertical middle).
        var bandCenterPad = mps.tableStyleHPt > 0 && rowBoxH > rowNaturalBoxH
            ? (rowBoxH - rowNaturalBoxH) / 2 : 0.0;

        // Pagination: the row moves whole to the next page when its box bottom
        // would cross the bottom margin; the continuation page resumes at the raw
        // content top (no body top margin).
        if (y - s - rowBoxH < marginBottom)
        {
            page = doc.Pages.Add(pageWidth, pageHeight);
            EnsureFonts(page, docFontDict);
            y = pageHeight - marginTop;
        }

        // The inline-style band's background fills the declared width × height
        // rectangle before any cell ink (probed: 96 118.5 361.5 101.25 re — one
        // uniform fill under the whole band).
        if (ri == 0 && mps.tableStyleBg is { } tsBand2 && mps.tableStyleHPt > 0)
        {
            var bandW2 = tableWpt > 0 ? tableWpt : availW - symInsetPt;
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"q {tsBand2.R / 255.0:0.###} {tsBand2.G / 255.0:0.###} {tsBand2.B / 255.0:0.###} rg " +
                $"{tableX:F2} {y - mps.tableStyleHPt:F2} {bandW2:F2} {mps.tableStyleHPt:F2} re f Q\n")));
        }

        var contentTop = y - s - p - bandCenterPad - (mps.collapsedGrid ? 0.75 : 0);
        // collapsed class grid: the shared 1px borders — row rule across the
        // grid, column rules down this row, in the class rule's colour.
        if (mps.collapsedGrid)
        {
            var gInv = System.Globalization.CultureInfo.InvariantCulture;
            var gW = availW - symInsetPt;
            var gsb = new StringBuilder(string.Create(gInv,
                $"q {mps.collapsedCol.R / 255.0:0.###} {mps.collapsedCol.G / 255.0:0.###} {mps.collapsedCol.B / 255.0:0.###} RG 0.75 w "));
            gsb.Append(string.Create(gInv,
                $"{tableX:F2} {y - 0.38:F2} m {tableX + gW:F2} {y - 0.38:F2} l S "));
            gsb.Append(string.Create(gInv,
                $"{tableX:F2} {y - s - rowBoxH + 0.38:F2} m {tableX + gW:F2} {y - s - rowBoxH + 0.38:F2} l S "));
            var gx = tableX;
            gsb.Append(string.Create(gInv,
                $"{gx + 0.38:F2} {y:F2} m {gx + 0.38:F2} {y - s - rowBoxH:F2} l S "));
            for (var gc = 0; gc < nCols; gc++)
            {
                gx += (gc == 0 ? s : 0) + colW[gc] + 2 * p + s;
                var gxe = gc == nCols - 1 ? tableX + gW - 0.38 : gx + 0.38;
                gsb.Append(string.Create(gInv,
                    $"{gxe:F2} {y:F2} m {gxe:F2} {y - s - rowBoxH:F2} l S "));
            }
            gsb.Append("Q\n");
            page.AddContentStream(Encoding.ASCII.GetBytes(gsb.ToString()));
        }
        var colX = tableX + s + (mps.collapsedGrid ? 0.75 : 0)
            + (collapseBoxW > 0 ? collapseBoxW : 0);
        var rowSubBottom = double.MaxValue;
        var rowRealBottom = double.MaxValue;   // deepest drawn text bottom (wrapper rows)
        var flatSkip = 0;
        for (var c = 0; c < nCols; c++)
        {
            var boxW = colW[c] + 2 * p;
            if (flatSkip > 0)
            {
                // a phantom slot under a spanning cell: nothing of its own,
                // no advance — the spanning cell already covered it.
                flatSkip--;
                continue;
            }
            if (c < r.Count)
                for (var k = 1; k < r[c].ColSpan && c + k < nCols; k++)
                {
                    boxW += s + colW[c + k] + 2 * p;
                    flatSkip++;
                }
            // bgcolor cell fill: the whole cell box, behind the text - inset
            // inside the collapsed grid so the shared border strokes stay.
            if (c < r.Count && r[c].Bg is { } cbg)
            {
                var fIn = mps.collapsedGrid ? 0.75 : 0;
                page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                    $"q {cbg.R / 255.0:0.###} {cbg.G / 255.0:0.###} {cbg.B / 255.0:0.###} rg " +
                    $"{colX + fIn:F2} {y - s - rowBoxH + fIn:F2} {boxW - 2 * fIn:F2} {rowBoxH - 2 * fIn:F2} re f Q\n")));
            }
            // <hr> in a cell: the browser's 3-D groove. A box HrGrooveH tall,
            // centred in the row band, spanning the cell's content box, with
            // its top and left edges black and its bottom and right in the
            // UA's grey — each stroke half a width inside its own edge.
            if (c < r.Count && r[c].HrRule)
            {
                var hrX0 = colX + p;
                var hrX1 = colX + boxW - p;
                var hrTop = y - s - (rowBoxH - HrGrooveH) / 2;
                var hrBot = hrTop - HrGrooveH;
                var half = HrGrooveW / 2;
                var hsb = new StringBuilder(string.Create(invc, $"q {HrGrooveW:0.##} w "));
                hsb.Append(string.Create(invc,
                    $"0 0 0 RG {hrX0:F2} {hrTop - half:F2} m {hrX1:F2} {hrTop - half:F2} l S "));
                hsb.Append(string.Create(invc,
                    $"{hrX0 + half:F2} {hrTop:F2} m {hrX0 + half:F2} {hrBot:F2} l S "));
                hsb.Append(string.Create(invc,
                    $"{HrGrooveGrey:0.###} {HrGrooveGrey:0.###} {HrGrooveGrey:0.###} RG " +
                    $"{hrX0:F2} {hrBot + half:F2} m {hrX1:F2} {hrBot + half:F2} l S "));
                hsb.Append(string.Create(invc,
                    $"{hrX1 - half:F2} {hrTop:F2} m {hrX1 - half:F2} {hrBot:F2} l S Q\n"));
                page.AddContentStream(Encoding.ASCII.GetBytes(hsb.ToString()));
            }
            // class-skin side borders (the boleto field grid): each declared
            // side strokes its own edge of the cell box in black; a dashed
            // top is the tear-off rule.
            if (c < r.Count && (r[c].BorderLeftW > 0 || r[c].BorderRightW > 0
                || r[c].BorderBottomW > 0 || r[c].BorderTopW > 0))
            {
                var bc2 = r[c];
                var rowTopY = y - s;
                var rowBotY = y - s - rowBoxH;
                var bsb = new StringBuilder("q 0 0 0 RG ");
                void SideLine(double w2, double sx0, double sy0, double sx1, double sy1, bool dash)
                    => bsb.Append(string.Create(invc,
                        $"{w2:0.##} w {(dash ? "[2.25 2.25] 0 d " : "")}" +
                        $"{sx0:F2} {sy0:F2} m {sx1:F2} {sy1:F2} l S "));
                if (bc2.BorderLeftW > 0)
                    SideLine(bc2.BorderLeftW, colX, rowTopY, colX, rowBotY, false);
                if (bc2.BorderRightW > 0)
                    SideLine(bc2.BorderRightW, colX + boxW, rowTopY, colX + boxW, rowBotY, false);
                if (bc2.BorderBottomW > 0 && bc2.BorderBottomDouble)
                {
                    // a `double` rule: the pair of thin lines the sum rows
                    // close with, spanning the declared width
                    SideLine(0.7, colX, rowBotY + bc2.BorderBottomW - 0.35,
                        colX + boxW, rowBotY + bc2.BorderBottomW - 0.35, false);
                    SideLine(0.7, colX, rowBotY + 0.35, colX + boxW, rowBotY + 0.35, false);
                }
                else if (bc2.BorderBottomW > 0)
                    SideLine(bc2.BorderBottomW, colX, rowBotY, colX + boxW, rowBotY, false);
                if (bc2.BorderTopW > 0)
                    SideLine(bc2.BorderTopW, colX, rowTopY, colX + boxW, rowTopY,
                        bc2.BorderTopDashed);
                bsb.Append("Q\n");
                page.AddContentStream(Encoding.ASCII.GetBytes(bsb.ToString()));
            }
            if (c < r.Count && (r[c].Lines.Length > 0 || r[c].SubTables is { Count: > 0 }
                || r[c].DivSegs is { Count: > 0 } || r[c].ImgBytes is not null))
            {
                var mc = r[c];
                var cellFs = mc.FontSize ?? mps.fontSize;
                var cellLineH = CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, mc, cellFs);
                // Middle vertical alignment (the HTML cell default);
                // a valign='top' cell seats its first line at the row top.
                // the collapsed class grid top-aligns its cells
                var lineTop = mc.VAlignBottom ? contentTop - (rowContentH - mc.ContentH)
                    // a rowspan cell hangs from its row top over the rows below
                    : mc.VAlignTop || mps.collapsedGrid || mc.RowSpan > 1 ? contentTop
                    : contentTop - (rowContentH - mc.ContentH) / 2;
                lineTop -= mc.PadTopPt;
                var mFace = CellFaceName(face, boldFace, mc);
                // Browser-UA flow draws the Standard-14 serif faces (F5/F6);
                // the MSHTML metric flow keeps its embedded-face resources;
                // a <font face>/font-family cell brings its own WinAnsi face.
                var fontRes = mc.Face is not null
                    ? ResOfFlatOn(mps, flatRes, face, boldFace, page, mc)
                    // pt-report cells draw the table's own real face (the
                    // measure face) rather than the Standard-14 Helvetica;
                    // a `table { font: … }` shorthand face — or a UA-flow
                    // body face other than the serif — draws likewise.
                    : (tableRuleFace || (!stdSerif && wrapperStacks)
                        || (stdSerif && !face.Equals("Times New Roman",
                            StringComparison.OrdinalIgnoreCase)))
                        && PosFace(face + (mc.Bold ? " Bold" : "")).ttf is not null
                    ? ResOfFlatOn(mps, flatRes, face, boldFace, page, new MetricCell
                        { Face = face, Bold = mc.Bold, FontSize = mc.FontSize })
                    : mc.Bold ? (stdSerif ? "F6" : "F2")
                    : mc.Italic ? (stdSerif ? "F7" : "F3")
                    : (stdSerif ? "F5" : "F1");
                if (mc.Fore is { } fc)
                    page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                        $"{fc.R / 255.0:0.###} {fc.G / 255.0:0.###} {fc.B / 255.0:0.###} rg")));
                // div-stacked cells draw band by band, each with its own
                // class typography; a .BB band strokes its bottom edge
                if (mc.DivSegs is { Count: > 0 } dsegs2)
                {
                    var segTop = lineTop;
                    // An abs-positioned data-URI PNG draws over the cell at
                    // its left:N% offset from the content box, natural size
                    // (50px = 37.5pt), out of the flow.
                    if (mc.AbsPng is { } apng && apng.Length >= 24)
                    {
                        var apW = ((apng[16] << 24) | (apng[17] << 16)
                            | (apng[18] << 8) | apng[19]) * PxPt;
                        var apH = ((apng[20] << 24) | (apng[21] << 16)
                            | (apng[22] << 8) | apng[23]) * PxPt;
                        var apIn = mps.collapsedGrid ? 0.75 : 0.0;
                        if (apW > 0 && apH > 0)
                        {
                            var apx = colX + apIn + p
                                + mc.AbsPngLeftFrac * (boxW - 2 * apIn - 2 * p);
                            page.AddImage(apng, new Rectangle(
                                apx, segTop - apH, apx + apW, segTop));
                        }
                    }
                    // the intrinsic-aspect JPEG opens the cell — its
                    // paragraphs stack below it
                    if (mc.ImgBytes is { } jpg2 && mc.ImgWPt > 0)
                    {
                        var jx = colX + mc.BorderLeftW
                            + (mc.PadLeft >= 0 ? mc.PadLeft : p);
                        page.AddImage(jpg2, new Rectangle(
                            jx, segTop - mc.ImgHPt, jx + mc.ImgWPt, segTop));
                        segTop -= mc.ImgHPt;
                    }
                    var sgPrevMb = 0.0;
                    foreach (var sg in dsegs2)
                    {
                        segTop -= Math.Max(sg.MarginTopPt, sgPrevMb);
                        sgPrevMb = sg.MarginBottomPt;
                        var sgFs = sg.FontSize ?? mps.fontSize;
                        var sgProbe = new MetricCell
                        { Face = sg.Face, Bold = sg.Bold, FontSize = sg.FontSize };
                        var sgFace = CellFaceName(face, boldFace, sgProbe);
                        var sgRes = sgProbe.Face is not null ? ResOfFlatOn(mps, flatRes, face, boldFace, page, sgProbe)
                            // newsletter segments draw the flow's real face,
                            // exactly like the plain-cell path above
                            : paragraphCells
                                && PosFace(face + (sg.Bold ? " Bold" : "")).ttf is not null
                            ? ResOfFlatOn(mps, flatRes, face, boldFace, page, new MetricCell
                                { Face = face, Bold = sg.Bold, FontSize = sg.FontSize })
                            : sg.Bold ? (stdSerif ? "F6" : "F2") : (stdSerif ? "F5" : "F1");
                        var sgFmv = CellFm(fm, sgProbe);
                        var sgSum0 = sgFmv.sum <= 1.0 ? 1.2 : sgFmv.sum;
                        var sgLineH = paragraphCells
                            ? CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, sgProbe, sgFs)
                            : MetricLineHeight(sgFs, sgSum0);
                        // the overflowing image grew the content box — wrap
                        // at its width, exactly like the layout pass
                        var sgWrapW = mc.ImgBytes is not null
                            && mc.ImgWPt > boxW - 2 * p
                            ? mc.ImgWPt
                            : boxW - 2 * p - sg.PadLeft - mc.BorderLeftW;
                        var sgLines = sg.Text.Length == 0 ? System.Array.Empty<string>()
                            : MeasuredWordWrap(sg.Text, sgWrapW, sgFace, sgFs);
                        // a class background fills the segment's band over
                        // the cell content width (the green bar:
                        // 97.5..497.5 × the class height, measured)
                        if (sg.Bg is { } sgBgC)
                        {
                            var sgBandH = Math.Max(sg.LineBoxPt,
                                sgLines.Length * sgLineH);
                            var sgIn = mps.collapsedGrid ? 0.75 : 0.0;
                            if (sgBandH > 0)
                                page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                                    $"q {sgBgC.R / 255.0:0.###} {sgBgC.G / 255.0:0.###} {sgBgC.B / 255.0:0.###} rg " +
                                    $"{colX + sgIn:F2} {segTop - sgBandH:F2} {boxW - 2 * sgIn:F2} {sgBandH:F2} re f Q\n")));
                        }
                        if (sg.Fore is { } sgc)
                            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                                $"{sgc.R / 255.0:0.###} {sgc.G / 255.0:0.###} {sgc.B / 255.0:0.###} rg")));
                        var segLy = segTop;
                        foreach (var ln in sgLines)
                        {
                            var sgDrop = MetricBaselineDrop(sgFs, sgLineH, sgFmv);
                            var sgLw = MeasureFaceText(sgFace, ln, sgFs);
                            var sgLx = mc.Align switch
                            {
                                HorizontalAlignment.Right => colX + boxW - p - sgLw,
                                HorizontalAlignment.Center => colX + (boxW - sgLw) / 2,
                                _ => colX + mc.BorderLeftW + sg.PadLeft + p,
                            };
                            if (ln.Length > 0)
                                EmitCellLineRuns(page, sgRes, sgFs, sgLx, segLy - sgDrop, ln, sgFace);
                            segLy -= sgLineH;
                        }
                        if (sg.Fore is not null)
                            page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
                        var bandH = Math.Max(sg.LineBoxPt, sgLines.Length * sgLineH);
                        if (sg.BorderBottom)
                            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                                $"q 0 0 0 RG 0.75 w {colX + mc.BorderLeftW:F2} {segTop - bandH:F2} m {colX + boxW:F2} {segTop - bandH:F2} l S Q\n")));
                        segTop -= bandH;
                    }
                    lineTop = segTop;   // the cell's drawn bottom
                }
                else if (mc.Flow is null)
                {
                if (mc.ImgBytes is { } jpg3 && mc.ImgWPt > 0)
                {
                    var jx3 = colX + mc.BorderLeftW
                        + (mc.PadLeft >= 0 ? mc.PadLeft : p);
                    page.AddImage(jpg3, new Rectangle(
                        jx3, lineTop - mc.ImgHPt, jx3 + mc.ImgWPt, lineTop));
                    lineTop -= mc.ImgHPt;
                }
                for (var li = 0; li < mc.Lines.Length; li++)
                {
                    var ln = mc.Lines[li];
                    var boxH = cellLineH + (mc.HasSpan && li == 0 ? 3.0 : 0);
                    var drop = CellDropOf(mps, stdSerif, fm, mc, cellFs, boxH);
                    var lw = MeasureFaceText(mFace, ln, cellFs);
                    var lx = mc.Align switch
                    {
                        HorizontalAlignment.Right => colX + boxW - p - lw,
                        HorizontalAlignment.Center => colX + (boxW - lw) / 2,
                        // A span cell's content sits 1.5 pt further in; a
                        // class border-left pushes the content past itself.
                        _ => colX + mc.BorderLeftW + (mc.PadLeft >= 0 ? mc.PadLeft : p)
                             + (mc.HasSpan ? 1.5 : 0),
                    };
                    if (ln.Length > 0)
                        EmitCellLineRuns(page, fontRes, cellFs, lx, lineTop - drop, ln, mFace);
                    lineTop -= boxH;
                }
                }
                if (mc.Fore is not null)
                    page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
                if (mc.RowSpan <= 1 && mc.SubTables is not { Count: > 0 })
                    rowRealBottom = Math.Min(rowRealBottom, lineTop);
                // Interleaved flow cells: text runs and nested grids draw in
                // SOURCE order — a <br> closes its line (an empty one is a
                // blank line box), runs carry their own bold, and a page
                // break resumes at the raw content top like any table row.
                if (mc.Flow is { Count: > 0 } flowRuns)
                {
                    var fCursor = lineTop;
                    var fPage = page;
                    var effWf = boxW - 2 * p;
                    string FlowRes(bool fb) => mc.Face is not null
                        ? ResOfFlatOn(mps, flatRes, face, boldFace, fPage, new MetricCell
                            { Face = mc.Face, Bold = fb, FontSize = mc.FontSize })
                        : (tableRuleFace || (!stdSerif && wrapperStacks)
                            || (stdSerif && !face.Equals("Times New Roman",
                                StringComparison.OrdinalIgnoreCase)))
                           && PosFace(face + (fb ? " Bold" : "")).ttf is not null
                        ? ResOfFlatOn(mps, flatRes, face, boldFace, fPage, new MetricCell
                            { Face = face, Bold = fb, FontSize = mc.FontSize })
                        : fb ? (stdSerif ? "F6" : "F2") : (stdSerif ? "F5" : "F1");
                    var pendingLine = new List<(string T, bool B)>();
                    void FlushLine()
                    {
                        if (fCursor - cellLineH < marginBottom)
                        {
                            fPage = doc.Pages.Add(pageWidth, pageHeight);
                            EnsureFonts(fPage, docFontDict);
                            fCursor = pageHeight - marginTop;
                        }
                        var fDrop = CellDropOf(mps, stdSerif, fm, mc, cellFs, cellLineH);
                        var fx = colX + mc.BorderLeftW + (mc.PadLeft >= 0 ? mc.PadLeft : p);
                        foreach (var (rt, rb) in pendingLine)
                        {
                            if (rt.Length == 0) continue;
                            EmitCellLineRuns(fPage, FlowRes(rb), cellFs, fx,
                                fCursor - fDrop, rt, rb ? boldFace : face);
                            fx += MeasureFaceText(rb ? boldFace : face, rt, cellFs);
                        }
                        pendingLine.Clear();
                        fCursor -= cellLineH;
                    }
                    foreach (var fi in flowRuns)
                    {
                        if (fi.TableHtml is { } subHtml)
                        {
                            if (pendingLine.Count > 0) FlushLine();
                            // A nested grid moves to the next page WHOLE
                            // when it cannot fit — these tables are never
                            // split across the break.
                            var subEst = NestedTableWrappedHeight(subHtml,
                                cellLineH, face, cellFs, effWf);
                            if (fCursor - subEst < marginBottom
                                && subEst <= pageHeight - marginTop - marginBottom)
                            {
                                fPage = doc.Pages.Add(pageWidth, pageHeight);
                                EnsureFonts(fPage, docFontDict);
                                fCursor = pageHeight - marginTop;
                            }
                            RenderMetricTable(doc, ref fPage, ref fCursor, subHtml, css,
                                colX + p, effWf, pageWidth, pageHeight,
                                marginTop, marginBottom, face, fm, docFontDict,
                                stdSerif, baseFontSize,
                                wrapperStacks: true, symInsetPt: 0,
                                paragraphCells: paragraphCells,
                                serifReportCells: serifReportCells,
                                loadOptions: loadOptions);
                            continue;
                        }
                        var fParts = fi.Text.Split('\u0001');
                        for (var fpi = 0; fpi < fParts.Length; fpi++)
                        {
                            if (fpi > 0) FlushLine();
                            var fpt = fParts[fpi];
                            if (fpt.Length == 0) continue;
                            if (pendingLine.Count == 0 && MeasureFaceText(
                                    fi.Bold ? boldFace : face, fpt, cellFs) > effWf)
                            {
                                var fWls = MeasuredWordWrap(fpt, effWf,
                                    fi.Bold ? boldFace : face, cellFs);
                                for (var wli = 0; wli < fWls.Length; wli++)
                                {
                                    pendingLine.Add((fWls[wli], fi.Bold));
                                    if (wli < fWls.Length - 1) FlushLine();
                                }
                            }
                            else pendingLine.Add((fpt, fi.Bold));
                        }
                    }
                    if (pendingLine.Count > 0) FlushLine();
                    page = fPage;
                    lineTop = fCursor;
                    if (mc.RowSpan <= 1)
                        rowSubBottom = Math.Min(rowSubBottom, fCursor);
                }
                // nested grids render inside the cell, stacked below its
                // own lines at the cell's content width
                else if (mc.SubTables is { Count: > 0 })
                {
                    var subCursor = mc.Lines.Length > 0
                        ? lineTop - mc.Lines.Length * CellLineOf(mps, stdSerif, wrapperStacks, hheaSum, face, fm, mc, cellFs)
                        : lineTop;
                    foreach (var sub in mc.SubTables)
                        RenderMetricTable(doc, ref page, ref subCursor, sub, css,
                            colX + p, boxW - 2 * p, pageWidth, pageHeight,
                            marginTop, marginBottom, face, fm, docFontDict,
                            stdSerif, baseFontSize,
                            wrapperStacks: true, symInsetPt: 0,
                            paragraphCells: paragraphCells, serifReportCells: serifReportCells,
                            loadOptions: loadOptions);
                    // a ROWSPAN cell's nested grid overlays the rows below —
                    // it must not carry its own row's bottom with it
                    if (mc.RowSpan <= 1)
                        rowSubBottom = Math.Min(rowSubBottom, subCursor);
                }
            }
            colX += boxW + s;
        }
        // a recursed sub-grid that outgrew the estimate carries the row
        // with it — the next row opens below the real drawn bottom. In the
        // report/newsletter wrapper mode a sub-table row's advance IS its
        // real drawn extent — the estimate is a pre-pass floor only, and
        // letting it win strands the next table a page early.
        var rowAdvance = rowBoxH;
        if (rowSubBottom < double.MaxValue)
        {
            var subAdv = (y - s) - rowSubBottom + p;
            if (!stdSerif && wrapperStacks)
            {
                var textAdv = rowRealBottom < double.MaxValue
                    ? (y - s) - rowRealBottom + p : 0;
                rowAdvance = Math.Max(subAdv, textAdv);
            }
            else rowAdvance = Math.Max(rowAdvance, subAdv);
        }
        y -= s + rowAdvance;
    }
    }
}
