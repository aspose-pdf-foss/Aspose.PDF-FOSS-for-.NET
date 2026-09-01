using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Lays out one table block of the escaped-attribute dialect and advances the flow past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutEscapedAttrTable(
        Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, Dictionary<string, Dictionary<string, string>> css, Color? dialectButtonFill, string dialectButtonTextRg, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, double lineHeight)
    {
            // Escaped-attr dialect grid — the HTML4 default frame:
            // outer OUTSET border (top/left
            // #555, bottom/right black), every cell INSET (top/left black,
            // bottom/right #555), 0.75 pt lines, 2.25 pt edge spacing and 1.5 pt
            // between cells; Times 12 cells with bold headers, columns sized to
            // the widest cell content + 1.5 pt side padding; form controls occupy
            // their control boxes INSIDE cells.
flow.afterEscapedRule = false;
            var trRows = new List<List<(bool Header, List<Block> Items)>>();
            foreach (Match trm in Regex.Matches(block.TableHtml ?? "",
                @"<tr\b[^>]*>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
            {
                var cellsRow = new List<(bool, List<Block>)>();
                foreach (Match cm in Regex.Matches(trm.Groups[1].Value,
                    @"<(td|th)\b[^>]*>([\s\S]*?)</\1\s*>", RegexOptions.IgnoreCase))
                {
                    var isTh = cm.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase);
                    var items = new List<Block>();
                    foreach (var cb in ParseBlocks(cm.Groups[2].Value, css,
                        bodyFontSize: 12, controlBoxes: true))
                    {
                        if (cb.InlineItems is { } inner) items.AddRange(inner);
                        else if (!cb.IsHardBreak || !string.IsNullOrEmpty(cb.Text)) items.Add(cb);
                    }
                    cellsRow.Add((isTh, items));
                }
                if (cellsRow.Count > 0) trRows.Add(cellsRow);
            }
            if (trRows.Count == 0) { flow.lastWasHardBreak = false; return; }

            const double GridEdgePad = 2.25, GridCellGap = 1.5, GridCellPad = 1.5;
            var nCols = 0;
            foreach (var r in trRows) nCols = System.Math.Max(nCols, r.Count);

            // Column widths: every column
            // floors at its MIN-CONTENT — the widest unbreakable piece across its
            // cells, where a hyphen IS a break opportunity, controls count whole,
            // and a BUTTON counts nothing (it overhangs its cell) — and the
            // remaining space distributes proportional to SLACK (the unwrapped
            // width still wanted over the floor). This sizes
            // all eight Employer-grid columns within a point: 'Employer Name'
            // rides one line while 'Contact Person' wraps. Widths measure in the
            // REAL TimesNewRoman metrics the cells draw with.
            double GridMeasure(bool bold, string s, double pt)
                => MeasureFaceText(bold ? "Times New Roman Bold" : "Times New Roman", s, pt);
            (double Full, double Min) CellWidths(List<Block> items, bool header)
            {
                double full = 0, min = 0;
                foreach (var it in items)
                {
                    if (it.IsButton) continue;
                    if (it.IsInputField)
                    {
                        var w = it.InputWidth + (it.IsSelectBox ? 2 * SelectSideBearingPt : 0);
                        full += w; min = System.Math.Max(min, w);
                    }
                    else if (!string.IsNullOrEmpty(it.Text))
                    {
                        var bold = header || it.FontRes == "F2";
                        var fpt = it.FontSize > 0 ? it.FontSize : EscapedBodyFontPt;
                        full += GridMeasure(bold, it.Text, fpt);
                        foreach (var word in it.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var hy = word.IndexOf('-');
                            if (hy > 0 && hy < word.Length - 1)
                            {
                                min = System.Math.Max(min, GridMeasure(bold, word[..(hy + 1)], fpt));
                                min = System.Math.Max(min, GridMeasure(bold, word[(hy + 1)..], fpt));
                            }
                            else
                                min = System.Math.Max(min, GridMeasure(bold, word, fpt));
                        }
                    }
                }
                return (full, min);
            }
            var colFull = new double[nCols];
            var colMin = new double[nCols];
            foreach (var r in trRows)
                for (int ci = 0; ci < r.Count; ci++)
                {
                    var (cf, cm) = CellWidths(r[ci].Items, r[ci].Header);
                    colFull[ci] = System.Math.Max(colFull[ci], cf + 2 * GridCellPad);
                    colMin[ci] = System.Math.Max(colMin[ci], cm + 2 * GridCellPad);
                }
            var colW = new double[nCols];
            var gridChrome = 2 * GridEdgePad + GridCellGap * (nCols - 1);
            {
                // An empty column still keeps a sliver of a cell.
                for (int ci = 0; ci < nCols; ci++)
                {
                    colMin[ci] = System.Math.Max(colMin[ci], 10.5);
                    colFull[ci] = System.Math.Max(colFull[ci], colMin[ci]);
                }
                double fullSum = gridChrome, minSum = gridChrome;
                foreach (var w in colFull) fullSum += w;
                foreach (var w in colMin) minSum += w;
                if (fullSum <= flow.contentWidth)
                    for (int ci = 0; ci < nCols; ci++) colW[ci] = colFull[ci];
                else if (minSum >= flow.contentWidth)
                {
                    var scale = (flow.contentWidth - gridChrome) / (minSum - gridChrome);
                    for (int ci = 0; ci < nCols; ci++) colW[ci] = colMin[ci] * scale;
                }
                else
                {
                    var surplus = flow.contentWidth - minSum;
                    double slackSum = 0;
                    for (int ci = 0; ci < nCols; ci++)
                        slackSum += System.Math.Max(0, colFull[ci] - colMin[ci]);
                    for (int ci = 0; ci < nCols; ci++)
                    {
                        var s = System.Math.Max(0, colFull[ci] - colMin[ci]);
                        colW[ci] = colMin[ci] + (slackSum > 1e-6 ? surplus * s / slackSum : 0);
                    }
                }
            }

            // Assemble every cell's wrapped lines: row height comes from the
            // tallest cell (16.5 floor = one 13.5 line + the cell's 3 pt of
            // vertical padding) and a shorter cell CENTRES in its row.
            (List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH)
                AssembleCell(List<Block> items, bool header, double availW)
            {
                var cellLines = new List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)>, double)>();
                var cl = new List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)>();
                double pen = 0, clH = 13.5;
                void EndCellLine()
                {
                    if (cl.Count == 0) return;
                    cellLines.Add((cl, clH));
                    cl = new List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)>();
                    pen = 0; clH = 13.5;
                }
                foreach (var it in items)
                {
                    if (it.IsInputField || it.IsButton)
                    {
                        var w = it.IsInputField
                            ? it.InputWidth + (it.IsSelectBox ? 2 * SelectSideBearingPt : 0)
                            : it.ButtonCaption.Length > 0
                                ? MeasureStd14("Helvetica", it.ButtonCaption, 10) + ButtonChromeWPt : EmptyButtonWPt;
                        var h = it.IsInputField ? it.InputHeight
                            : it.ButtonCaption.Length > 0 ? ButtonHeightPt : EmptyButtonHPt;
                        if (cl.Count > 0 && pen + w > availW + 1e-6) EndCellLine();
                        cl.Add((it, null, pen, 0, ""));
                        // A control FILLS its cell: a 16.17 combo
                        // sits flush in a 16.4 cell (borders coincide), so the
                        // control's line costs its height minus the cell's own
                        // 3 pt of vertical padding.
                        clH = System.Math.Max(clH, h - 3);
                        pen += w;
                    }
                    else if (!string.IsNullOrEmpty(it.Text))
                    {
                        var res = header || it.FontRes == "F2" ? "F6"
                            : it.FontRes == "F3" ? "F7" : "F5";
                        var bold = res == "F6";
                        var fpt = it.FontSize > 0 ? it.FontSize : EscapedBodyFontPt;
                        int p = 0;
                        while (p < it.Text.Length)
                        {
                            var sp = it.Text.IndexOf(' ', p);
                            var wordEnd = sp < 0 ? it.Text.Length : sp + 1;
                            while (wordEnd < it.Text.Length && it.Text[wordEnd] == ' ') wordEnd++;
                            var word = it.Text.Substring(p, wordEnd - p);
                            p = wordEnd;
                            // A hyphen inside a word is a break opportunity too —
                            // 'Perfetto-Tullo' wraps after the hyphen.
                            var segStart = 0;
                            while (segStart < word.Length)
                            {
                                var hy = word.IndexOf('-', segStart);
                                var segEnd = hy >= 0 && hy < word.Length - 1 ? hy + 1 : word.Length;
                                var token = word[segStart..segEnd];
                                segStart = segEnd;
                                var wTrim = GridMeasure(bold, token.TrimEnd(' '), fpt);
                                if (cl.Count > 0 && pen + wTrim > availW + 1e-6) EndCellLine();
                                var drawTok = cl.Count == 0 ? token.TrimStart(' ') : token;
                                if (drawTok.Length == 0) continue;
                                cl.Add((null, drawTok, pen, fpt, res));
                                pen += GridMeasure(bold, drawTok, fpt);
                            }
                        }
                    }
                }
                EndCellLine();
                double contentH = 3;
                foreach (var (_, lh) in cellLines) contentH += lh;
                if (cellLines.Count == 0) contentH = 16.5;
                return (cellLines, contentH);
            }
            var planRows = new List<(List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH)[]>();
            var rowHs = new List<double>();
            foreach (var r in trRows)
            {
                var plans = new (List<(List<(Block? Ctl, string? Txt, double XOff, double FontPt, string Res)> Items, double H)> Lines, double ContentH)[r.Count];
                double rh = 16.5;
                for (int ci = 0; ci < r.Count; ci++)
                {
                    plans[ci] = AssembleCell(r[ci].Items, r[ci].Header, colW[ci] - 2 * GridCellPad);
                    rh = System.Math.Max(rh, plans[ci].ContentH);
                }
                planRows.Add(plans);
                rowHs.Add(rh);
            }
            double tableW = gridChrome;
            foreach (var w in colW) tableW += w;
            double tableH = 2 * GridEdgePad + GridCellGap * (trRows.Count - 1);
            foreach (var rh in rowHs) tableH += rh;

            // The grid's top edge sits one text ascent above the cursor (the flow
            // runs in baseline space); a grid that no longer fits moves whole.
            var gridTop = flow.y + 0.9 * 12;
            if (gridTop - tableH < marginBottom
                && tableH <= FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop) - marginBottom
                && flow.y < FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop) - 1e-3)
            {
                flow.page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(flow.page, docFontDict);
                flow.y = FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop); flow.pendingTopDrop = profile.hasZeroTopMargin;
                gridTop = flow.y + 0.9 * 12;
            }
            var gridDark = ParseCssColor("#555555");
            var gx = marginLeft;
            // Outer OUTSET frame.
            DrawBox(flow.page, gx, gridTop - 0.75, tableW, 0.75, null, 0, gridDark);
            DrawBox(flow.page, gx, gridTop - tableH, tableW, 0.75, null, 0, Color.Black);
            DrawBox(flow.page, gx, gridTop - tableH, 0.75, tableH, null, 0, gridDark);
            DrawBox(flow.page, gx + tableW - 0.75, gridTop - tableH, 0.75, tableH, null, 0, Color.Black);
            var rowTop = gridTop - GridEdgePad;
            for (int ri = 0; ri < trRows.Count; ri++)
            {
                var r = trRows[ri];
                var rh = rowHs[ri];
                var cx = gx + GridEdgePad;
                for (int ci = 0; ci < r.Count; ci++)
                {
                    var cw = colW[ci];
                    // Cell INSET frame.
                    DrawBox(flow.page, cx, rowTop - 0.75, cw, 0.75, null, 0, Color.Black);
                    DrawBox(flow.page, cx, rowTop - rh, cw, 0.75, null, 0, gridDark);
                    DrawBox(flow.page, cx, rowTop - rh, 0.75, rh, null, 0, Color.Black);
                    DrawBox(flow.page, cx + cw - 0.75, rowTop - rh, 0.75, rh, null, 0, gridDark);
                    var (cellLines, cellContentH) = planRows[ri][ci];
                    // vertical-align: middle — a shorter cell centres in its row.
                    var vaOff = System.Math.Max(0, (rh - cellContentH) / 2);
                    var lineBase = rowTop - vaOff - 12.3;
                    foreach (var (lineItems, lineH) in cellLines)
                    {
                        // A header cell centres each of its lines (the th default).
                        double cellHOff = 0;
                        if (r[ci].Header)
                        {
                            double lineW = 0;
                            foreach (var (lc, lt, lx, lp, lr) in lineItems)
                                lineW = System.Math.Max(lineW, lx + (lc is null
                                    ? GridMeasure(lr == "F6", lt ?? "", lp)
                                    : lc.IsInputField
                                        ? lc.InputWidth + (lc.IsSelectBox ? 2 * SelectSideBearingPt : 0)
                                        : lc.ButtonCaption.Length > 0
                                            ? MeasureStd14("Helvetica", lc.ButtonCaption, 10) + ButtonChromeWPt
                                            : EmptyButtonWPt));
                            cellHOff = System.Math.Max(0, (cw - 2 * GridCellPad - lineW) / 2);
                        }
                        foreach (var (ctl, txt, xOff, fpt, res) in lineItems)
                        {
                            var ix = cx + GridCellPad + cellHOff + xOff;
                            if (ctl is null)
                            {
                                if (!string.IsNullOrEmpty(txt)) EmitSerifRun(txt, res, fpt, ix, lineBase, flow);
                            }
                            else if (ctl.IsInputField)
                            {
                                // Centre the control box in its ROW — flush with
                                // the cell borders for a full-height control.
                                var ctlH = ctl.InputHeight > 0 ? ctl.InputHeight : 15.75;
                                var ctlAbove = ctl.IsSelectBox
                                    ? SelectBoxAboveBaselinePt : InputBoxAboveBaselinePt;
                                EmitControlAt(ctl, ix, rowTop - (rh - ctlH) / 2 - ctlAbove, flow, doc, lineHeight);
                            }
                            else
                            {
                                var bcw = ctl.ButtonCaption.Length > 0
                                    ? MeasureStd14("Helvetica", ctl.ButtonCaption, 10) + ButtonChromeWPt : EmptyButtonWPt;
                                var bch = ctl.ButtonCaption.Length > 0 ? ButtonHeightPt : EmptyButtonHPt;
                                // A cell button CENTRES vertically in its row and
                                // OVERHANGS horizontally: its
                                // left edge sits half a point outside the cell box,
                                // so its outline rides the cell's own border.
                                var bcy = rowTop - (rh - bch) / 2 - bch;
                                var bcx = ix - GridCellPad - 0.5;
                                DrawBox(flow.page, bcx, bcy, bcw, bch,
                                    border: Color.Black, borderWidth: 1, fill: null);
                                if (bcw > 4 && bch > 3)
                                    DrawBox(flow.page, bcx + 2, bcy + 1.5, bcw - 4, bch - 3,
                                        null, 0, dialectButtonFill);
                                if (ctl.ButtonCaption.Length > 0)
                                    flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                                        $"q BT /F1 10 Tf {dialectButtonTextRg} 1 0 0 1 {bcx + ButtonCaptionInsetXPt:0.##} {bcy + bch - ButtonCaptionDropPt:0.##} Tm ({EscapePdfString(ctl.ButtonCaption)}) Tj ET Q\n")));
                            }
                        }
                        lineBase -= lineH;
                    }
                    cx += cw + GridCellGap;
                }
                rowTop -= rh + GridCellGap;
            }
            flow.contentPage = flow.page;
            // Back into baseline space: the next text baseline sits one ascent
            // below the grid's bottom edge (plus its own margins).
            flow.y = gridTop - tableH - 0.9 * 12;
            flow.lastWasHardBreak = false;
            flow.prevFlowMarginBottom = 0;
            flow.prevFlowLineHeight = 0;
    }

    /// <summary>The paragraph margin the user-agent flow puts between blocks.</summary>
    private const double UaParagraphMarginPt = 13.44;

    // A control-bearing table still renders as a GRID when its visible controls
    // are all radios (plus button-family inputs, which draw nothing in a cell):
    // the radio factory carries the options into the cells as inline glyphs —
    // `◯ ◯Yes ◉ ◉No` on one line, the form-report shape. Any
    // text-like control (text input, select, textarea) keeps its table on the
    // flat path, whose blocks emit the AcroForm fields for it.
    private static bool RadioGridableControls(string markup)
    {
        var hasRadio = false;
        foreach (Match fim in Regex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>",
                     RegexOptions.IgnoreCase))
        {
            if (HiddenInlineRx.IsMatch(fim.Value)) continue;
            if (!fim.Groups[1].Value.Equals("input", StringComparison.OrdinalIgnoreCase))
                return false;
            var tyM = Regex.Match(fim.Value, @"type\s*=\s*[""']?([A-Za-z]+)",
                RegexOptions.IgnoreCase);
            var ty = tyM.Success ? tyM.Groups[1].Value.ToLowerInvariant() : "text";
            if (ty == "radio") hasRadio = true;
            else if (ty is not ("hidden" or "button" or "submit" or "reset" or "image")) return false;
        }
        return hasRadio;
    }

    /// <summary>Lays out one table block and advances the flow past it.</summary>
    /// <remarks>Lifted verbatim out of the block-dispatch loop in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void LayoutTableBlock(Block block, HtmlFlowCursor flow, HtmlDocProfile profile, Document doc, Core.PdfDictionary docFontDict, Dictionary<string, Dictionary<string, string>> css, HtmlLoadOptions? options, List<byte[]> inlineSvgs, List<(Page page, byte[] ops)> floatFirstOps, Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)> bandStack, string? bodyCssFace, bool dwFormDoc, double marginBottom, double marginLeft, double marginTop, double pageHeight, double pageWidth, bool tableAfterSpacer, bool tableAfterText)
    {
            // Bare UA document: whichever arm draws this table, a break
            // paragraph that follows it stands the UA margin the table
            // never read (see breakAfterTable).
            if (profile.uaBareDoc) flow.lastWasMetricTable = true;
            // Metric flow: tables render through the metric layouter (real HTML
            // geometry + win-metric line boxes), not the generator table. A
            // RADIO-bearing table is the exception: the metric layouter neither
            // grids nested tables nor draws form controls, so it takes the
            // generator grid below (whose lift + slice pass carry both).
            // Quirks-mode CSS-run documents (no <!DOCTYPE>) take the same
            // layouter: the body rule's pixel font does not inherit into table
            // cells there — cells render at the UA 16px base in the body face
            // (measured: Calibri cells at 12 pt, 18 pt row pitch).
            var quirksRunTable = !profile.metricFlow && profile.quirksCssRun;
            var tableFace = profile.metricFlow ? profile.metricFace : quirksRunTable ? bodyCssFace : null;
            // The inline-body-margin dialect draws its tables as a collapsed
            // 1px grid with mid-row pagination — a shape the metric layouter
            // does not model (it paginates row-at-a-time and knows no
            // border-collapse).
            if (profile.bodyBoxGridDoc && WinMetricsFor(profile.metricFace) is { } bgm)
            {
                RenderBodyBoxGridTable(doc, ref flow.page, ref flow.y, block.TableHtml ?? "",
                    marginLeft, flow.contentWidth, pageWidth, pageHeight, marginBottom,
                    profile.metricFace, bgm, profile.metricLineSum, docFontDict);
                flow.lastWasHardBreak = false;
                return;   // the block is laid out; the loop this came from would continue
            }
            if (tableFace is not null && WinMetricsFor(tableFace) is { } tfm
                && !RadioGridableControls(block.TableHtml ?? "")
                // The over-declared attribute-grid document needs its nested
                // grids drawn as GRIDS — the metric layouter flattens them.
                && !profile.overDeclaredGridDoc)
            {
                // Measured on the width:100%-body sheet only — the plain-
                // body serif docs are calibrated without this gap.
                if (profile.uaStdSerif && profile.bodyWidthFullDoc && tableAfterText)
                    flow.y -= TableAfterTextGapPt;
                // A table that follows a SPACER break paragraph opens the
                // break's bottom UA margin (a text neighbour would have
                // realised it through its own margin-top; the table path
                // reads none).
                if (profile.uaBareDoc && tableAfterSpacer)
                    flow.y -= UaParagraphMarginPt;
                RenderMetricTable(doc, ref flow.page, ref flow.y, block.TableHtml ?? "", css,
                    marginLeft + flow.fsIndentLive,
                    // inside a fieldset the FRAME's content box is the
                    // table's available width, not the page content box
                    flow.fsIndentLive > 0 && profile.fsBoxW > 0
                        ? profile.fsBoxW - FsPadLeftPt - FsPadRightPt
                        : flow.contentWidth - flow.fsIndentLive,
                    pageWidth, pageHeight,
                    marginTop, marginBottom, tableFace, tfm, docFontDict,
                    stdSerif: profile.uaStdSerif,
                    baseFontSize: profile.printGrid ? profile.printGridBase
                        : profile.ptReportDoc && profile.ptTableFontPt > 0 ? profile.ptTableFontPt
                        : profile.uaStdSerif || quirksRunTable ? 12 : 11,
                    // The wrapper-stack recursion serves the legacy nested-
                    // markup corpus; the dead-css greens were calibrated on
                    // the flat merge and keep it. A zero body margin has no
                    // symmetric body inset for the grid either.
                    wrapperStacks: (profile.uaStdSerif && !profile.deadExternalCss) || profile.ptReportDoc,
                    symInsetPt: profile.bodyZeroMargin ? 0.0 : UaBodyMarginPt,
                    rtl: profile.rtlDoc,
                    // the SSRS report export drives the serif flow's cells
                    // through the paragraph-segment model too
                    paragraphCells: profile.emailNewsletterDoc || profile.ssrsReportDoc,
                    serifReportCells: profile.ssrsReportDoc,
                    loadOptions: options);
                flow.lastWasMetricTable = true;
                // A PAGE-BREAK-AFTER div closed with this table (the close tag
                // parses in a later segment): open the fresh page here.
                if (block.PageBreakAfterTable)
                {
                    flow.page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(flow.page, docFontDict);
                    flow.y = pageHeight - marginTop;
                    flow.pendingTopDrop = profile.hasZeroTopMargin;
                    flow.contentPage = null;
                }
                flow.lastWasHardBreak = false;
                return;   // the block is laid out; the loop this came from would continue
            }
            // The band dialect's table conventions (nbsp spacer rows keep their line
            // boxes, CSS row pitches, zero empty rows) hold for a filing document's
            // top-level tables too — the ToC listing and the shaded proposals grid
            // page on the same conventions.
            // A synthesized form-horizontal row: the control-group's CSS rhythm —
            // the value span's 5px margin-top, the controls div's 1px padding-top
            // and the collapsed 3px inter-group margins — separates it from the
            // block above (9px per row is the row pitch).
            var fhTableHtml = block.TableHtml ?? "";
            var fhRow = profile.formHorizontalDoc
                && fhTableHtml.Contains("class=\"fh-row\"", StringComparison.OrdinalIgnoreCase);
            if (fhRow) flow.y -= 9 * 0.75;
            // The pinned-body report's tables carry real CSS margins: the
            // sheet's `table { margin-top: 5px }` element rule (or a larger
            // inline one) is space above every grid — the first grid included,
            // whose margin offsets it from the page top (measured: the title
            // row's border at 72 + 3.75 + the 1px cellspacing).
            if (profile.bodyPinnedW > 0)
            {
                double pbMtPx = 0;
                if (css.TryGetValue("table", out var pbtR)
                    && pbtR.TryGetValue("margin-top", out var pbtV)
                    && Regex.Match(pbtV, @"([\d.]+)\s*px") is { Success: true } pbtM)
                    double.TryParse(pbtM.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out pbMtPx);
                if (Regex.Match(fhTableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase)
                        is { Success: true } pbTag
                    && Regex.Match(DivStyleOf(pbTag.Value),
                        @"(?<![-\w])margin-top\s*:\s*([\d.]+)\s*px",
                        RegexOptions.IgnoreCase) is { Success: true } pbMt
                    && double.TryParse(pbMt.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pbInline)
                    && pbInline > pbMtPx)
                    pbMtPx = pbInline;
                if (pbMtPx > 0) flow.y -= pbMtPx * 0.75;
                // The grid's FIRST border-spacing band sits above row one —
                // the declared `cellspacing="1px"` the generator's rows carry
                // only between and below themselves.
                if (Regex.Match(fhTableHtml, @"cellspacing\s*=\s*[""']?(\d+(?:\.\d+)?)",
                        RegexOptions.IgnoreCase) is { Success: true } pbCs
                    && double.TryParse(pbCs.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pbCsPx)
                    && pbCsPx > 0)
                    flow.y -= pbCsPx * 0.75;
            }
            // A metric-flow doc's radio table grids through the generator (see the
            // metric bypass above); its cells keep the metric dialect's base font
            // and pitch on the browser line box the items lay out on.
            var radioGridTable = profile.metricFlow && RadioGridableControls(fhTableHtml);
            var radioGridFontPt = profile.uaStdSerif ? 12.0 : 11.0;
            // The radio factory for this grid: one RadioButtonField per HTML
            // `name` (an anonymous group per unnamed input), anchored on the page
            // the table starts on. Options join their group at creation so the
            // render pass can place each widget via OwnerRadio; the groups are
            // registered on doc.Form after the flow pass.
            var tablePage = flow.page;
            Aspose.Pdf.Forms.RadioButtonOptionField MakeGridRadio(string group, bool chk)
            {
                var key = string.IsNullOrEmpty(group) ? "__gridradio" + flow.gridRadioAnon++ : group;
                if (!profile.gridRadioGroups.TryGetValue(key, out var rbf))
                {
                    rbf = new Aspose.Pdf.Forms.RadioButtonField(tablePage);
                    profile.gridRadioGroups[key] = rbf;
                    profile.gridRadioPages.Add((rbf, tablePage));
                }
                profile.gridRadioCounts.TryGetValue(key, out var optIdx);
                profile.gridRadioCounts[key] = optIdx + 1;
                var ropt = new Aspose.Pdf.Forms.RadioButtonOptionField
                {
                    Style = Aspose.Pdf.Forms.BoxStyle.Circle,
                    OptionName = key + "_" + optIdx,
                };
                ropt.Characteristics.Border = System.Drawing.Color.Black;
                rbf.Add(ropt);
                return ropt;
            }
            // The over-declared grid document resolves EVERY table — any
            // nesting depth — against the same standard box: pageW − margins
            // − the UA body gutter − the filing shell's host chrome pair
            // (measured: pageW − 201 at every page width).
            // The certificate dialect lays a table out at the width it DECLARES and
            // lets it overflow the content box, exactly as a browser does: the
            // reference keeps its 720 px (540 pt) grid on a 595 pt sheet and clips
            // it at the page edge, where squeezing it into the 403 pt content box
            // wraps every long session title onto a second line and costs a page.
            var certTableW = profile.floatBothSidesDoc
                && CertDeclaredTableWidthPt(fhTableHtml) is { } certDw
                && certDw > flow.contentWidth ? certDw : 0;
            var tableAvailW = profile.overDeclaredGridDoc
                ? flow.contentWidth - UaBodyMarginPt - OverDeclaredHostChromePt
                : certTableW > 0 ? certTableW
                : flow.contentWidth;
            var table = BuildTableFromHtml(fhTableHtml, tableAvailW, out var renderNatW, options, inlineSvgs, css,
                bandDialect: profile.floatBandDoc,
                makeRadio: MakeGridRadio,
                // Sectioned-report rhythm: cell lines pitch on the browser's own
                // line box too, not the flow's legacy em multiple.
                cellLineHeightPt: radioGridTable ? Table.CssLineBoxPt(radioGridFontPt)
                    : profile.sectionedReport && profile.formBodyFontPt > 0
                    ? NormalLineHeightPt(profile.formBodyFontPt)
                    // a scaled layout paces cells on the UA 18px line
                    : profile.scaleToPageWidth ? NormalLineHeightPt(DefaultBodyFontPt)
                    : profile.bodyLineHeightPt,
                // The page stylesheet's own base size seeds the grid (see bodyCssFontPt);
                // the probe above must measure the same cells this render builds.
                defaultCellFontPt: dwFormDoc ? 12.0
                    : radioGridTable ? radioGridFontPt
                    : profile.scaleToPageWidth ? DefaultBodyFontPt : profile.bodyCssFontPt,
                defaultCellFace: dwFormDoc ? "Times New Roman" : null,
                cssRunFace: bodyCssFace, bodyTextColor: profile.bodyCssColor,
                // Sectioned reports lay their grids out on the browser's own cell
                // box: the UA's 1px vertical cell padding and pre-wrap line boxes.
                uaCellBoxes: profile.sectionedReport,
                // Nested tables render as real grids, and the chain-selector dialect
                // that rides the same switch is on: a stylesheet's descendant rules
                // reach the cells they address instead of being dropped.
                liftNestedTables: true,
                ptCellWidths: profile.ptStyledFragment,
                redlineCells: profile.redlineDiffDoc,
                dwFormCells: dwFormDoc,
                docElementGrid: profile.elementGridDoc,
                pinnedBodyGrid: profile.bodyPinnedW > 0,
                // The over-declared grid document RENDERS on the honest CJK
                // model too — its reference draws full-em ideographs breaking
                // at every ideograph, and the legacy estimates mis-floor its
                // radical/plane-2 columns badly.
                fullWidthCjkMin: profile.overDeclaredGridDoc,
                overDeclaredDraw: profile.overDeclaredGridDoc,
                chainRules: profile.docChainRules);
            if (table is not null)
            {
                // The over-declared grid document's tables keep the STANDARD
                // content box — the same pageW−201 the nested grids resolve
                // against (margins, the UA body gutter, and the host chrome
                // pair all come off) — while their row-band FILLS bleed to
                // the page's right EDGE: the section bands paint
                // page-wide with the content staying put.
                table.FlowLeftOffset = marginLeft;
                if (profile.overDeclaredGridDoc)
                {
                    table.UsableWidthOverride = flow.contentWidth - UaBodyMarginPt
                        - OverDeclaredHostChromePt;
                    // The shipped-era reference stops its bands a left-margin's
                    // width short of the right edge (probed off the template:
                    // 96.75 → pageW−96 at every section).
                    table.HtmlBandBleedRightPt = pageWidth - marginLeft;
                    // <table align="center"> centres its NATURAL box in the
                    // standard box (the 物業地點 address card sits mid-page).
                    if (renderNatW > 0 && renderNatW < table.UsableWidthOverride
                        && Regex.IsMatch(
                            Regex.Match(fhTableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase).Value,
                            @"align\s*=\s*[""']?center", RegexOptions.IgnoreCase))
                    {
                        table.FlowLeftOffset = marginLeft
                            + (table.UsableWidthOverride - renderNatW) / 2;
                        table.UsableWidthOverride = renderNatW;
                    }
                }
                // Inside a float column the usable width IS the column width —
                // the symmetric-margin guess in GetTableUsableWidth reads a
                // right column's offset as a right margin and collapses it.
                if (bandStack.Count > 0) table.UsableWidthOverride = flow.contentWidth;
                // A form-horizontal row keeps its natural cell widths even when
                // they overflow the float column (browser floats overflow; the
                // squeeze would re-wrap value text that belongs on
                // one line).
                if (fhRow)
                {
                    var fhw = Regex.Match(fhTableHtml, @"data-fhw=""([\d.]+)""");
                    if (fhw.Success && double.TryParse(fhw.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var fhwPx)
                        && fhwPx * 0.75 > flow.contentWidth)
                        table.UsableWidthOverride = fhwPx * 0.75;
                }
                // …and the certificate grid keeps the width it declared (above),
                // overflowing the content box rather than re-wrapping into it.
                if (certTableW > 0) table.UsableWidthOverride = certTableW;
                // Band-card tables render serif cell fragments in the real serif
                // face (see Table.HonorCellFontFaces) — the Helvetica fallback
                // over-wraps their serif-measured columns.
                table.HonorCellFontFaces = profile.floatBandDoc;
                // Form-document dialect: cells wrap and draw in their resolved
                // real faces (td { font: 10px Verdana }) — see HonorCellTtfFaces.
                // The pt-styled fragment's cells carry inline Verdana spans the
                // same way.
                // …and the redline diff document's cells carry inline Times
                // spans; their runs draw with the real face.
                // (an inline-face grid set the flag at build — keep it)
                table.HonorCellTtfFaces |= profile.formDialectTables || profile.ptStyledFragment
                    || profile.redlineDiffDoc || dwFormDoc;
                table.RedlineCellSeat = profile.redlineDiffDoc;
                table.HtmlWrapInsetsCellMargins = profile.ptStyledFragment;
                // Sectioned report: the cursor runs in baseline space, so the table's
                // own box top — the top edge of its first row band — sits one baseline
                // offset above it. Without this the whole grid hangs a full ascent too
                // low and every row band misses the one a browser paints.
                if ((profile.sectionedReport || profile.ptStyledFragment) && flow.prevFlowFontSize > 0)
                    flow.y += BaselineInLineBoxPt(flow.prevFlowFontSize)
                        // pt-styled fragment: the grid's TOP STROKE anchors the
                        // seat (probed against the drawn border positions —
                        // the generic rise leaves every mid-flow table 0.3 low).
                        + (profile.ptStyledFragment ? PtTableBoxRisePt : 0);
                // Redline: the flow's last line spent a full 1.125 em box;
                // a table opens at the paragraph's BOTTOM (DescLead below
                // the last baseline) — repay the difference.
                else if (profile.redlineDiffDoc && flow.prevFlowFontSize > 0)
                    flow.y += (RedlineLineFactor - RedlineDescLeadEm) * flow.prevFlowFontSize;
                // Paginate the table from the current cursor; the first slice lands on this
                // page, further slices spill onto fresh pages (matching a browser splitting a
                // long table across pages). Borders/graphics come back via LastGraphDraws.
                // A table that spills onto a fresh page resumes at the page's TOP
                // MARGIN, not at the sheet edge: without the page margin the
                // continuation draws right off the top of the paper.
                // Every page this conversion adds shares docFontDict (see
                // EnsureFonts) — spill slices may embed real faces through it.
                table.SpillPagesShareFontDict = true;
                var slices = table.BuildMultiPage(flow.page, flow.y, marginBottom,
                    bodyCssFace is not null ? marginTop
                    // Escaped-attr dialect: continuation slices resume at the
                    // page's real top margin (the flow's fresh-page top), not at
                    // the sheet edge.
                    : profile.escapedAttrDoc ? pageHeight - FreshPageTopY(profile.escapedAttrDoc, pageHeight, marginTop)
                    // Chain-dialect documents likewise resume below the top
                    // margin — a spilled report row must not draw at the sheet
                    // edge (the y≈9 artefact).
                    : profile.docChainRules is not null ? marginTop
                    // …and the over-declared grid document's continuation
                    // slices resume at the page's top margin too.
                    : profile.overDeclaredGridDoc ? marginTop
                    // …and an inline-styled grid's do as well (the Verdana
                    // report resumes 50 pt below the sheet edge).
                    : table.InlineFaceGridRatio > 0 ? marginTop
                    : 0);
                var graphs = table.LastGraphDraws;
                var imageDraws = table.LastImageDraws;
                // A float-band column is an overflow:hidden box: a table reaching the
                // page bottom CLIPS there instead of paginating (a browser never splits
                // a float box), so the band's other columns stay on the band's page.
                var bandClipped = false;
                if (bandStack.Count > 0 && slices.Count > 1)
                {
                    bandClipped = true;
                    slices = new List<byte[]> { slices[0] };
                    if (graphs.Count > 1) graphs = new List<List<byte[]>> { graphs[0] };
                    if (imageDraws.Count > 1)
                        imageDraws = new List<List<(byte[] data, Rectangle rect)>> { imageDraws[0] };
                }
                for (var si = 0; si < slices.Count; si++)
                {
                    if (si > 0)
                    {
                        flow.page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(flow.page, docFontDict);
                    }
                    // A floated table's ops are collected and PREPENDED to its page's
                    // content after the flow pass — floats paint first,
                    // so their text leads the fragment order. Geometry is unchanged.
                    if (block.FloatFirst)
                    {
                        if (si < graphs.Count)
                            foreach (var g in graphs[si]) floatFirstOps.Add((flow.page, g));
                        floatFirstOps.Add((flow.page, slices[si]));
                    }
                    else
                    {
                        if (si < graphs.Count)
                            foreach (var g in graphs[si]) flow.page.AddContentStream(g);
                        flow.page.AddContentStream(slices[si]);
                    }
                    // Cell images (logos, SVG diagrams) recorded by the layout pass;
                    // blit them onto the slice's page at their resolved rectangles.
                    if (si < imageDraws.Count)
                        foreach (var (imgData, imgRect) in imageDraws[si])
                            try { flow.page.AddImage(imgData, imgRect); }
                            catch { /* undecodable image: keep the table flow */ }
                }
                // A clipped band column consumed its page down to the bottom margin —
                // LastRenderedHeight/LastPageEndY describe the discarded overflow pages.
                flow.y = bandClipped ? marginBottom
                    : slices.Count > 1 ? table.LastPageEndY : flow.y - table.LastRenderedHeight;
                // …and an inline margin-bottom is real space below it (the
                // pinned report's `margin-bottom: 5px` summary grid).
                if (profile.bodyPinnedW > 0
                    && Regex.Match(fhTableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase)
                        is { Success: true } pbTag2
                    && Regex.Match(DivStyleOf(pbTag2.Value),
                        @"(?<![-\w])margin-bottom\s*:\s*([\d.]+)\s*px",
                        RegexOptions.IgnoreCase) is { Success: true } pbMb
                    && double.TryParse(pbMb.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pbMbPx)
                    && pbMbPx > 0)
                    flow.y -= pbMbPx * 0.75;
                // Back into baseline space: the cursor sits on the table's bottom
                // EDGE, and the next text block draws its baseline one offset below
                // its own box top — the mirror of the entry adjustment above.
                if (profile.sectionedReport && flow.prevFlowFontSize > 0)
                    flow.y -= BaselineInLineBoxPt(flow.prevFlowFontSize);
                flow.contentPage = flow.page;
                // The cursor now sits ON the table's bottom edge. Text draws its
                // first BASELINE at the cursor, so in the form-document dialect
                // the next text block drops a line box first — else its ink rides
                // up into the last (bordered) row. The legacy flow keeps its
                // calibrated tight rhythm outside the dialect.
                // The chain dialect owes the same drop: its report footnote drew its
                // baseline ON the table's bottom border, striking through the last
                // row — and never reached the due page break.
                flow.pendingTableDrop = profile.formDialectTables || profile.docChainRules is not null
                    // the pt-styled fragment's paragraphs seat one line box
                    // below each grid the same way
                    || profile.ptStyledFragment;
                if (profile.redlineDiffDoc)
                {
                    // the cursor sits on the table's bottom edge; the next
                    // paragraph's first baseline seats one AscLead below it
                    flow.prevFlowFontSize = 0;
                    flow.prevFlowLineHeight = 0;
                    flow.pendingTableDrop = false;
                }
                if (profile.ptStyledFragment)
                {
                    flow.pendingTableDropBordered = false;
                    foreach (Row ptbR in table.Rows)
                    {
                        foreach (Cell ptbC in ptbR.Cells)
                            if (ptbC.Border is { Width: > 0 } ptbB
                                && ptbB.Side != BorderSide.None) { flow.pendingTableDropBordered = true; break; }
                        if (flow.pendingTableDropBordered) break;
                    }
                }
            }
            flow.lastWasHardBreak = false;
            flow.prevFlowMarginBottom = 0;
            flow.prevFlowLineHeight = 0;
            flow.afterRuleDrop = false;
            flow.afterFhTable = fhRow;
    }
}
