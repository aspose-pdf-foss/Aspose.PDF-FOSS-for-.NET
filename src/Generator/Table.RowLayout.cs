using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;
namespace Aspose.Pdf;

public partial class Table : BaseParagraph
{
    private RowPlan BuildRowPlan(Row row, double[] colWidths, int[] cellMap,
        int[]? gridToCell = null, int[]? effRowSpan = null, double svgFillHeight = 0)
    {
        var plan = new RowPlan { Row = row, GridToCell = gridToCell, EffRowSpan = effRowSpan };
        // Non-grid rows walk cells by GRID position: a ColSpan cell consumes span
        // columns, so a cell after it starts past the span (not at the next column
        // index). Only with an identity cellMap — column-band chunking (non-identity)
        // keeps its own mapping.
        if (gridToCell is null)
        {
            var identityMap = true;
            for (var i = 0; i < cellMap.Length; i++)
                if (cellMap[i] != i) { identityMap = false; break; }
            if (identityMap)
            {
                var colToCell = new int[colWidths.Length];
                for (var i = 0; i < colToCell.Length; i++) colToCell[i] = -1;
                var gc = 0;
                for (var ci = 0; ci < row.Cells.Count && gc < colToCell.Length; ci++)
                {
                    colToCell[gc] = ci;
                    var sp = Math.Max(1, Math.Min(row.Cells.At(ci).ColSpan, colToCell.Length - gc));
                    for (var s = 1; s < sp; s++) colToCell[gc + s] = -2;
                    gc += sp;
                }
                plan.ColToCell = colToCell;
            }
        }
        var defaultPad = row.DefaultCellPadding ?? DefaultCellPadding;
        double maxLineHeight = 0;
        // Tight (no-leading) height of whatever set maxLineHeight. A block of n lines occupies
        // (n-1)·LineHeight + TightLine, so a single text line takes its glyph height (≈ FontSize)
        // rather than a full 1.2× leading slot — matching the generator's row height.
        // Non-text content (images, control glyphs) keeps its full height as the tight value.
        double tightForMax = 0;
        void Consider(double lineHeight, double tight)
        {
            if (lineHeight > maxLineHeight) { maxLineHeight = lineHeight; tightForMax = tight; }
        }
        double maxVertPad = 0;
        double maxTopPad = 0;
        var cellTotals = new List<(double padV, int lineCount, double tight, double exact, double ownStack)>();

        for (var col = 0; col < colWidths.Length; col++)
        {
            int origIdx;
            if (gridToCell is not null)
            {
                origIdx = col < gridToCell.Length ? gridToCell[col] : -1;
                if (origIdx < 0 || origIdx >= row.Cells.Count) { plan.CellLines.Add(new List<CellLine>()); continue; }
                // A row-spanning cell's content, padding and metrics belong to the span
                // block, not this row's plan — its grid columns stay blank here.
                if (effRowSpan is not null && effRowSpan[origIdx] > 1)
                { plan.CellLines.Add(new List<CellLine>()); continue; }
            }
            else if (plan.ColToCell is { } colToCell)
            {
                origIdx = colToCell[col];
                if (origIdx < 0) { plan.CellLines.Add(new List<CellLine>()); continue; }
            }
            else
            {
                origIdx = cellMap[col];
                if (origIdx >= row.Cells.Count) { plan.CellLines.Add(new List<CellLine>()); continue; }
            }
            var cell = row.Cells.At(origIdx);
            var padding = EffectivePad(cell, row);
            var dp = DefaultPad(cell, row);
            // Vertical padding defaults to ZERO — row pitch is
            // exactly lineCount×fontSize for borderless cells; a cell border
            // adds its stroke width above and below (a 0.5 pt bordered 10 pt
            // row pitches at 11 pt). Explicit padding is
            // honoured verbatim.
            var vb = cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
            var borderV = BorderTopBottom(vb);
            var padV = (padding?.Top ?? 0) + (padding?.Bottom ?? 0) + borderV;
            if (padV > maxVertPad) maxVertPad = padV;
            if ((padding?.Top ?? 0) > maxTopPad) maxTopPad = padding?.Top ?? 0;

            var padLeft = padding?.Left ?? dp;
            var padRight = padding?.Right ?? dp;
            var span = Math.Max(1, Math.Min(cell.ColSpan, colWidths.Length - col));
            var cellWidth = GetCellWidth(colWidths, col, span);
            var availWidth = cellWidth - padLeft - padRight;
            // A column the HTML layout sized carries the markup's cell rule inside its
            // width — the text box is what is left after it, the same box that pass
            // wrapped in when it worked out the row's height.
            if ((HtmlLayoutWrap || CssRunBoxes) && HtmlCellBorderPt > 0) availWidth -= 2 * HtmlCellBorderPt;

            var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
            var defaultFontSize = ResolveCellFontSize(cell, row);
            var cellAlign = ResolveCellAlignment(cell, row);
            var lines = new List<CellLine>();

            var cellNeedsInline = false;
            foreach (var gp in cell.Paragraphs)
                if (gp is Aspose.Pdf.Drawing.Graph || IsInlineParagraph(gp) || IsMultiSegmentFragment(gp))
                { cellNeedsInline = true; break; }
            if (cellNeedsInline)
            {
                // Cells mixing Graph paragraphs with inline text (e.g. a colour-swatch
                // legend or a horizontal bar graph) get a left-to-right inline layout;
                // reserve one blank text line per inline row for height accounting.
                var inlineRows = BuildInlineCellLayout(cell, availWidth, defaultFontSize, textState, out var inlineH);
                (plan.CellInline ??= new())[col] = inlineRows;
                foreach (var _ in inlineRows) lines.Add(new CellLine { Text = "", FontSize = defaultFontSize });
                Consider(inlineH, inlineH);
            }
            else
            foreach (var paragraph in cell.Paragraphs)
            {
                // Nested table: flatten each inner row into one line per row so
                // height accounting and pagination see the inner content. Cell
                // text from each inner cell is joined with " | " as a visual
                // separator; proper nested-table rendering would need its own
                // slice pass, but this keeps pagination honest.
                if (paragraph is Table inner)
                {
                    // The REAL slice pass (opt-in): measure the inner grid at the
                    // cell's content width and reserve its height; the draw pass
                    // renders it in place. Falls back to the legacy flatten when the
                    // measurement cannot run.
                    if (NestedTableRender && _buildPage is not null)
                    {
                        double innerH = 0;
                        try
                        {
                            inner.NestedTableRender = true;
                            inner.FlowLeftOffset = 0;
                            inner.UsableWidthOverride = availWidth - 2 * inner.HtmlCapsuleOutsetHPt
                                - inner.HtmlListIndentPt;
                            inner.BuildMultiPage(_buildPage, 1_000_000, 0, 0, measureOnly: true);
                            innerH = inner.LastRenderedHeight;
                            // A capsule-wrapped grid reserves its WRAPPER's box: the
                            // pill's padding, spacing band and margin are real space
                            // in the host cell, not paint that hangs outside it.
                            if (innerH > 0) innerH += 2 * inner.HtmlCapsuleOutsetVPt
                                + inner.HtmlMarginTopPt;
                        }
                        catch { innerH = 0; }
                        if (innerH > 0)
                        {
                            plan.CellTables ??= new Dictionary<int, List<CellNestedTable>>();
                            if (!plan.CellTables.TryGetValue(col, out var ctList))
                                plan.CellTables[col] = ctList = new List<CellNestedTable>();
                            // The reserve is SPLITTABLE: N lines, each carrying an
                            // equal share of the grid's height as its FontSize, give
                            // the host row a page-break point every ~line pitch. The
                            // exact-stack total is unchanged (N · innerH/N = innerH);
                            // the draw pass hands the grid's k-th page slice to the
                            // k-th host slice covering the reserve.
                            var resLines = Math.Max(1,
                                (int)Math.Ceiling(innerH / DefaultLineHeightPt));
                            ctList.Add(new CellNestedTable
                            {
                                Table = inner,
                                HeightPt = innerH,
                                LineOffset = lines.Count,
                                LineCount = resLines,
                            });
                            // The reserve lines carry the grid's height in their
                            // FontSize (an equal share each), and the row sizes
                            // through the EXACT-stack path (cellOwnStack sums the
                            // reserve back to innerH) — pricing them at the row's
                            // final LineHeight instead would let a sibling cell's
                            // taller fragments inflate the reserve past the real
                            // grid (the row grows in LineHeight quanta).
                            // …and they must not raise the row's SHARED line pitch
                            // either: the grid's own height rides in the reserve
                            // FontSize, while the table's default 10 pt em set a
                            // 12 pt pitch that the sibling text columns (8 pt) then
                            // stacked on — overrunning their own row band and
                            // drawing over the row below.
                            if (!NestedTableRender)
                                Consider(defaultFontSize * CssNormalLineHeight,
                                    defaultFontSize * CssNormalLineHeight);
                            var resLineH = innerH / resLines;
                            for (var rk = 0; rk < resLines; rk++)
                                lines.Add(new CellLine
                                    { Text = "", FontSize = resLineH, ImgReserve = true });
                            continue;
                        }
                    }
                    // Flatten each inner row into lines, preserving block
                    // boundaries from HtmlFragment text (via \n after StripHtmlTags)
                    // so the outer cell's height budget reflects the inner table's
                    // true visual extent. Each inner row contributes at least one
                    // line per non-empty segment.
                    var innerRows = inner.Rows;
                    Consider(defaultFontSize * 1.2, defaultFontSize);
                    for (int ri = 0; ri < innerRows.Count; ri++)
                    {
                        var irow = innerRows.At(ri);
                        var segments = new List<string>();
                        for (int ici = 0; ici < irow.Cells.Count; ici++)
                        {
                            var icell = irow.Cells.At(ici);
                            foreach (var ip in icell.Paragraphs)
                            {
                                string? rawText = null;
                                if (ip is TextFragment itf) rawText = itf.Text;
                                else if (ip is HtmlFragment ihtml) rawText = HtmlFragment.StripHtmlTags(ihtml.HtmlContent ?? "");
                                if (string.IsNullOrEmpty(rawText)) continue;
                                foreach (var part in rawText.Split('\n'))
                                {
                                    var trimmed = part.Trim();
                                    if (trimmed.Length > 0) segments.Add(trimmed);
                                }
                            }
                        }
                        // Ensure each inner row renders a minimum of one blank
                        // line when it carries non-text content; otherwise the
                        // height collapses and pagination under-counts.
                        if (segments.Count == 0) segments.Add(" ");
                        foreach (var seg in segments)
                        {
                            foreach (var l in WrapText(seg, defaultFontSize, availWidth))
                                lines.Add(new CellLine { Text = l, FontSize = defaultFontSize, ForegroundColor = textState?.ForegroundColor });
                        }
                    }
                    continue;
                }

                // An Image paragraph in a cell is a variable-height block. Resolve its
                // display size (explicit Fix* or natural, fit to the cell width), reserve
                // matching vertical space as blank lines so the row's height budget and
                // pagination cover it, and stash the bytes for the render pass to blit.
                if (paragraph is Image cellImg)
                {
                    var rawBytes = ReadRawImageBytes(cellImg);
                    if (rawBytes is null) continue;
                    var svgSource = cellImg.FileType == ImageFileType.Svg;
                    var svgData = rawBytes.Length > 0 && IsSvg(cellImg, rawBytes);
                    double imgXOffset = 0;
                    double dispW, dispH;
                    // A vector source with NO intrinsic size (viewBox-only or bare root)
                    // fills the space it sits in: the full column footprint wide, and
                    // down from the row top to the page's bottom content margin. The
                    // artwork stretches to that box regardless of viewBox aspect — a
                    // circle in the resulting tall cell renders as a portrait ellipse.
                    // Explicit root width/height (or Fix*) sizing keeps the paths below.
                    if (cellImg.FixWidth <= 0 && cellImg.FixHeight <= 0
                        && svgData && svgFillHeight > 0 && SvgLacksIntrinsicSize(rawBytes))
                    {
                        dispW = cellWidth;
                        dispH = svgFillHeight;
                        imgXOffset = -padLeft;
                        var sized = ImageRasterizer.RasterizeSvgOnPageCanvas(rawBytes);
                        if (sized is null) continue;
                        AddCellImage(plan, col, new CellImage
                        {
                            Data = sized, Width = dispW, Height = dispH, Align = cellImg.HorizontalAlignment,
                            XOffset = imgXOffset,
                            LineOffset = lines.Count,
                        });
                        // Reserve lines summing EXACTLY to the fill height (n lines of
                        // dispH/n each, n = how many default-leading lines fit) so the
                        // row bottom lands on the page's bottom content margin instead
                        // of a line-quantised
                        // overshoot.
                        var fillLines = Math.Max(1, (int)Math.Floor(dispH / (defaultFontSize * 1.2)));
                        var fillLineH = dispH / fillLines;
                        Consider(fillLineH, fillLineH);
                        for (var k = 0; k < fillLines; k++)
                            lines.Add(new CellLine { Text = "", FontSize = fillLineH / 1.2, ImgReserve = true });
                        continue;
                    }
                    var imgBytes = svgData ? ImageRasterizer.RasterizeSvg(rawBytes) ?? rawBytes : rawBytes;
                    if (cellImg.FixWidth > 0 && cellImg.FixHeight > 0)
                    {
                        dispW = cellImg.FixWidth;
                        dispH = cellImg.FixHeight;
                        // A vector (SVG) source keeps its aspect ratio inside the
                        // declared Fix box and is centred in it horizontally,
                        // instead of being stretched like a raster source.
                        if (svgSource && TryGetCellImageSizePt(imgBytes, out var svgW, out var svgH)
                            && svgW > 0 && svgH > 0)
                        {
                            var fit = Math.Min(dispW / svgW, dispH / svgH);
                            var fw = svgW * fit;
                            var fh = svgH * fit;
                            imgXOffset = (dispW - fw) / 2;
                            dispW = fw;
                            dispH = fh;
                        }
                    }
                    else if (TryGetCellImageSizePt(imgBytes, out var natW, out var natH) && natW > 0 && natH > 0)
                    {
                        if (cellImg.IsApplyResolution)
                        {
                            // Resolution-aware: fit to the cell's content width preserving the
                            // aspect ratio (IsApplyResolution behaviour — a wide
                            // image is scaled down to the column, height shrinks proportionally).
                            if (availWidth > 0 && natW > availWidth)
                            {
                                dispH = natH * (availWidth / natW);
                                dispW = availWidth;
                            }
                            else
                            {
                                dispW = natW;
                                dispH = natH;
                            }
                        }
                        else
                        {
                            // Default (no resolution applied): the width is clamped to the cell's
                            // content width while the height stays at the image's natural
                            // point-height (aspect is not preserved — a wide image is squeezed to
                            // the column and rendered at full height). Explicit Fix* sizing above
                            // is the documented way to avoid this stretch.
                            dispW = availWidth > 0 && natW > availWidth ? availWidth : natW;
                            dispH = natH;
                        }
                    }
                    else
                    {
                        dispW = availWidth > 0 ? availWidth : 100;
                        dispH = dispW;
                    }
                    AddCellImage(plan, col, new CellImage
                    {
                        Data = imgBytes, Width = dispW, Height = dispH, Align = cellImg.HorizontalAlignment,
                        XOffset = imgXOffset,
                        LineOffset = lines.Count,
                    });
                    var imgLineH = defaultFontSize * 1.2;
                    var imgLines = Math.Max(1, (int)Math.Ceiling(dispH / imgLineH));
                    // The reserve must sum to the image's OWN height: pricing each line at
                    // the table's default font size while counting them at the line BOX
                    // left every tall image short by the difference (a 112.5 pt image
                    // reserved 100), so a column of them overlapped and the last one ran
                    // past the section it sits in. The lifted render sizes the stack
                    // exactly; the legacy grid keeps its calibrated quantisation.
                    var imgLinePt = NestedTableRender ? dispH / imgLines : defaultFontSize;
                    Consider(imgLineH, imgLineH);
                    for (var k = 0; k < imgLines; k++)
                        lines.Add(new CellLine { Text = "", FontSize = imgLinePt, ImgReserve = true });
                    continue;
                }

                // A radio-button option in a cell renders as a glyph (circle) followed
                // by its caption. Emit one line carrying the option so the row's height
                // budget covers the glyph and the render pass can draw it.
                if (paragraph is Aspose.Pdf.Forms.RadioButtonOptionField opt)
                {
                    var capSize = opt.Caption?.TextState.FontSize > 0
                        ? opt.Caption!.TextState.FontSize
                        : defaultFontSize;
                    var glyphH = opt.Height > 0 ? opt.Height : capSize;
                    // A control row is sized to its glyph/caption without the extra
                    // text leading — the glyph is a fixed box, not a line of type.
                    var lh = Math.Max(glyphH, capSize);
                    Consider(lh, lh);
                    lines.Add(new CellLine
                    {
                        Text = opt.Caption?.Text ?? "",
                        FontSize = capSize,
                        ForegroundColor = opt.Caption?.TextState.ForegroundColor ?? textState?.ForegroundColor,
                        Option = opt,
                    });
                    continue;
                }

                // A checkbox in a cell occupies a fixed glyph box; record a control line so
                // the row height covers it and the render pass repositions its widget.
                if (paragraph is Aspose.Pdf.Forms.CheckboxField cbf)
                {
                    var boxH = cbf.Height > 0 ? cbf.Height : defaultFontSize;
                    Consider(boxH, boxH);
                    lines.Add(new CellLine { Text = "", FontSize = defaultFontSize, Checkbox = cbf });
                    continue;
                }

                string? text = null;
                double fragFontSize = defaultFontSize;
                Color? color = null;
                Hyperlink? fragLink = null;
                List<(string Text, string Url)>? fragAnchors = null;
                // A fragment-level embedded font (e.g. a CJK Unicode fallback assigned when the
                // cell text has chars outside WinAnsi) — drawn as Type0/CID by the render pass.
                byte[]? fragEmbeddedTtf = null;
                string? fragEmbeddedName = null;
                // CSS line-box metrics from the HTML styled-cell path (zero = legacy).
                double fragCssAsc = 0, fragCssDesc = 0;
                var fragKeepBlank = false;
                var fragCssForce = false;
                // Cell text / TextFragments follow the cell's resolved alignment; a fragment
                // that sets its own non-default alignment wins. An HtmlFragment keeps its own
                // block alignment (left unless its style centres/right-aligns).
                var lineAlign = cellAlign;
                double htmlCssBoxPx = 0;
                var fragBold = false;
                var fragItalic = false;
                List<(string Text, bool Bold)>? fragGridRuns = null;
                if (paragraph is TextFragment tf)
                {
                    text = tf.Text;
                    fragFontSize = ResolveFragmentFontSize(tf, defaultFontSize);
                    color = tf.TextState.ForegroundColor ?? textState?.ForegroundColor;
                    fragBold = tf.TextState.IsBold;
                    if (!fragBold)
                        foreach (var fseg in tf.Segments)
                            if (fseg.TextState.IsBold && !string.IsNullOrEmpty(fseg.Text))
                            { fragBold = true; break; }
                    fragItalic = tf.TextState.IsItalic;
                    fragGridRuns = tf.FormGridRuns;
                    fragLink = tf.HyperlinkValue;
                    fragAnchors = tf.HtmlAnchors;
                    fragEmbeddedTtf = tf.TextState.Font?.SourceFontData?.TtfData;
                    fragEmbeddedName = tf.TextState.Font?.FontName;
                    fragCssAsc = tf.CssAscent; fragCssDesc = tf.CssDescent;
                    fragKeepBlank = tf.CssKeepBlank;
                    fragCssForce = tf.CssLineBoxAlways;
                    if (tf.TextState.HorizontalAlignment != HorizontalAlignment.Left &&
                        tf.TextState.HorizontalAlignment != HorizontalAlignment.None)
                        lineAlign = tf.TextState.HorizontalAlignment;
                }
                else if (paragraph is HtmlFragment html)
                {
                    // HTML-engine cell: markup in the b/strong/small/div/br family lays
                    // out as serif CSS line boxes (pixel-quantized leading, mixed bold/
                    // small runs per line, kerned TJ output — see ParseHtmlEngineCell).
                    // A fragment carrying its OWN TextState sizes the HTML from it (the
                    // face does not follow — HTML text takes its family from CSS, so an
                    // Arial TextState still sets in the UA serif). IsBreakWords lets a
                    // word wider than the column break inside itself.
                    if (ParseHtmlEngineCell(html.HtmlContent, availWidth,
                            html.TextState?.FontSize > 0 ? html.TextState.FontSize : HtmlCellFontSize,
                            html.IsBreakWords) is { } engineLines)
                    {
                        foreach (var el in engineLines)
                        {
                            el.ForegroundColor = textState?.ForegroundColor;
                            el.Align = ParseHtmlAlignment(html.HtmlContent);
                            lines.Add(el);
                        }
                        continue;
                    }
                    // List cell: block text followed by <ul>/<ol> items. Items render
                    // left-aligned with a hanging bullet — the item text indents by
                    // the list margin-start (CSS default 40px), continuation lines
                    // keep the indent, and the bullet hangs to the left of the first
                    // line. The fragment's own stylesheet font-size (px) sizes every
                    // line.
                    if (BuildHtmlListCellLines(html.HtmlContent, availWidth, fragFontSize,
                            textState?.ForegroundColor, cellAlign) is { } listLines)
                    {
                        foreach (var ll in listLines)
                        {
                            Consider(ll.FontSize, ll.FontSize);
                            lines.Add(ll);
                        }
                        continue;
                    }
                    text = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                    color = textState?.ForegroundColor;
                    lineAlign = ParseHtmlAlignment(html.HtmlContent);
                    // A block-level (div/p) text-align rule in the fragment's own
                    // stylesheet wins over alignment hits from unrelated selectors
                    // elsewhere in the sheet.
                    var blockRule = Regex.Match(html.HtmlContent ?? "",
                        @"(?:^|[,{}\s])(?:div|p)\s*(?:,[^{]*)?\{[^}]*text-align\s*:\s*(left|right|center)",
                        RegexOptions.IgnoreCase);
                    if (blockRule.Success)
                        lineAlign = blockRule.Groups[1].Value.ToLowerInvariant() switch
                        {
                            "right" => HorizontalAlignment.Right,
                            "center" => HorizontalAlignment.Center,
                            _ => HorizontalAlignment.Left,
                        };
                    var cssPx = Regex.Match(html.HtmlContent ?? "", @"font-size\s*:\s*([\d.]+)\s*px",
                        RegexOptions.IgnoreCase);
                    if (blockRule.Success && cssPx.Success
                        && double.TryParse(cssPx.Groups[1].Value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var cssPxV) && cssPxV > 0)
                        htmlCssBoxPx = cssPxV;
                    // The markup's own <a href> runs annotate exactly like the ones a
                    // TextFragment carries in HtmlAnchors — the tag-stripped text keeps the
                    // anchor's characters, so each run is located on its laid-out line.
                    fragAnchors = ParseCellHtmlAnchors(html.HtmlContent);
                }
                if (text is null) continue;
                // Cell text lines pitch at exactly the font size (K = 1.0): a
                // multi-paragraph 8 pt header cell stacks 8 pt per
                // line. The old 1.2× leading only showed on multi-line cells
                // (single-line rows already used the tight height).
                // A fragment carrying an explicit CSS line-height pitches at that
                // instead (the source page's `font: 1em/1.4em …` box model).
                var fragLineH = (paragraph as TextFragment)?.CssLineHeightPt ?? 0;
                // A fragment with inline boxes keeps its box height OUT of the row's
                // uniform LineHeight (which would inflate a sibling cell's nested-table
                // reserve lines); its height rides the css-box stack via BoxH instead.
                var boxedFrag = (paragraph as TextFragment)?.InlineBoxes is { Count: > 0 };
                var thisLineHeight = !boxedFrag && fragLineH > 0 ? Math.Max(fragFontSize, fragLineH) : fragFontSize;
                // CSS run boxes: the fragment's own `line-height: normal` box IS its line
                // box, so a cell stacks each run on its own size's pitch instead of the
                // flat 1.2 em the mixed-size path assumes.
                var runBoxH = CssRunBoxes && fragLineH > 0 ? fragLineH : 0.0;
                // With a CSS line box, a SINGLE-line cell also occupies the box
                // (tight = box height), so one-line rows pitch like wrapped ones.
                Consider(thisLineHeight, thisLineHeight > fragFontSize ? thisLineHeight : fragFontSize);

                // An empty TextFragment is a deliberate spacer in many cell
                // layouts (e.g. TextFragment with LineSpacing set and no
                // text). Emit it as one blank line so the row's height
                // budget includes the spacer — dropping it here would
                // collapse vertical padding that tests rely on.
                if (text.Length == 0)
                {
                    var blank = new CellLine
                    {
                        Text = "", FontSize = fragFontSize, ForegroundColor = color, Align = lineAlign,
                        CssAsc = fragKeepBlank ? fragCssAsc : 0, CssDesc = fragKeepBlank ? fragCssDesc : 0,
                        CssForce = fragCssForce,
                        // A kept blank line is a real line box under CSS run boxes — the
                        // row of a cell holding nothing but an invisible character is
                        // still one line tall.
                        BoxH = runBoxH, BaseOff = runBoxH > 0 ? runBoxH : 0,
                        // A DELIBERATE blank (an explicit <br> line box) is markup-real:
                        // the exact-stack and slice pricers must count it — the draw
                        // walk advances it like any line.
                        HtmlEngine = fragKeepBlank,
                    };
                    // A blank line can still CARRY inline boxes (a standalone badge
                    // circle in an otherwise-empty cell).
                    if (paragraph is TextFragment { InlineBoxes: { Count: > 0 } blankBoxes })
                    {
                        blank.Boxes = blankBoxes;
                        if (fragLineH > blank.FontSize && blank.BoxH <= 0)
                            blank.BoxH = fragLineH;
                    }
                    lines.Add(blank);
                    continue;
                }

                // Arabic/RTL cell text: the table draws cells with a single Standard-14 font in
                // single-byte encoding, which has no Arabic glyphs. Shape the text (contextual
                // presentation forms + visual bidi order) and emit it as one line flagged to be
                // drawn with an embedded Arabic-capable font (Type0/CID) by the render pass.
                if (Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(text))
                {
                    // The form-grid dialect's Arabic sets in the SERIF fallback
                    // face (the dialect's Verdana carries no Arabic
                    // program), and WRAPS at the cell box — the long RTL name runs
                    // over two lines there. Other dialects keep the one-line Arial
                    // path byte-stable.
                    var arabicFont = Aspose.Pdf.Text.FontRepository.FindFont(
                        FormGridCells ? "Times New Roman" : "Arial");
                    var arabicTtf = arabicFont?.SourceFontData?.TtfData;
                    if (arabicTtf is not null)
                    {
                        // Form-grid measure path: the wrap runs on the BASE
                        // face's advances with every non-WinAnsi char at the '?'
                        // fallback width (the draw then substitutes the serif face) —
                        // the shaped serif line is far narrower than the measure, so
                        // the name row breaks two words earlier than a shaped-width
                        // wrap would.
                        var fgBaseTtf = FormGridCells
                            ? CellFaceTtf((paragraph as TextFragment)?.TextState.Font?.FontName
                                is { Length: > 0 } fgFam ? fgFam : "Verdana", false)
                            : null;
                        double MeasFallback(string s)
                        {
                            // Raw text against the base face, unmapped Arabic chars at
                            // Verdana's average char width (OS/2 xAvgCharWidth 1229 of
                            // the 2048 em) — the measure path: the wrap is
                            // decided BEFORE the draw substitutes the serif face, so
                            // the name row breaks after 'حياة' though the shaped serif
                            // line is far narrower than the box.
                            return fgBaseTtf is null
                                ? MeasureWidthWithFont(
                                    Aspose.Pdf.Text.ArabicTextShaper.Shape(s), fragFontSize, arabicTtf)
                                : MeasureWidthWithFont(s, fragFontSize, fgBaseTtf,
                                    unmappedEm: FormGridUnmappedAdvanceEm);
                        }
                        IEnumerable<string> arSegs;
                        if (FormGridCells && availWidth > 0)
                        {
                            var arLines = new List<string>();
                            var arCur = "";
                            foreach (var arW in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            {
                                var arCand = arCur.Length == 0 ? arW : arCur + " " + arW;
                                if (arCur.Length > 0 && MeasFallback(arCand) > availWidth)
                                { arLines.Add(arCur); arCur = arW; }
                                else arCur = arCand;
                            }
                            if (arCur.Length > 0) arLines.Add(arCur);
                            arSegs = arLines;
                        }
                        else arSegs = new[] { text };
                        // LTR-base visual ordering (form-grid): an R-block spans from
                        // an Arabic char through any interior neutrals to the last
                        // Arabic char it can reach, and shapes (reversing) as one RTL
                        // run; neutrals after the last block keep their logical place
                        // (the name row draws its '?' tail after the
                        // leftmost Arabic word, not reversed ahead of it).
                        string ShapeLtrBase(string logical)
                        {
                            if (!FormGridCells) return Aspose.Pdf.Text.ArabicTextShaper.Shape(logical);
                            bool IsR(char c) => c >= '؀' && c <= 'ۿ'
                                || c >= 'ﭐ' && c <= '﷿'
                                || c >= 'ﹰ' && c <= '﻿';
                            var vsb = new System.Text.StringBuilder(logical.Length);
                            var i = 0;
                            while (i < logical.Length)
                            {
                                if (!IsR(logical[i])) { vsb.Append(logical[i]); i++; continue; }
                                var j = i;
                                var k = i;
                                while (k < logical.Length)
                                {
                                    if (IsR(logical[k])) { j = k; k++; continue; }
                                    var m = k;
                                    while (m < logical.Length && !IsR(logical[m])
                                           && !char.IsLetterOrDigit(logical[m])) m++;
                                    if (m < logical.Length && IsR(logical[m])) { k = m; continue; }
                                    break;
                                }
                                vsb.Append(Aspose.Pdf.Text.ArabicTextShaper.Shape(logical[i..(j + 1)]));
                                i = j + 1;
                            }
                            return vsb.ToString();
                        }
                        foreach (var arSeg in arSegs)
                        {
                            var arShaped = ShapeLtrBase(arSeg);
                            // Draw the shaped visual line as mixed runs: Arabic
                            // segments in the serif fallback, everything else (the
                            // '?' mojibake, dots, spaces) in the cell's own base
                            // face — the run alternation the page must carry.
                            List<(string Text, byte[] Ttf, string Name)>? arRuns = null;
                            var arKernW = 0.0;
                            if (FormGridCells && fgBaseTtf is not null)
                            {
                                arRuns = new();
                                var segStart = 0;
                                bool SegArabic(char c) => c >= '؀' && c <= 'ۿ'
                                        || c >= 'ﭐ' && c <= '﷿'
                                        || c >= 'ﹰ' && c <= '﻿';
                                for (var si = 1; si <= arShaped.Length; si++)
                                {
                                    if (si < arShaped.Length
                                        && SegArabic(arShaped[si]) == SegArabic(arShaped[segStart])) continue;
                                    var segText = arShaped[segStart..si];
                                    var segAr = SegArabic(arShaped[segStart]);
                                    arRuns.Add((segText, segAr ? arabicTtf : fgBaseTtf,
                                        segAr ? arabicFont!.FontName : "Verdana"));
                                    arKernW += MeasureWidthWithFont(segText, fragFontSize,
                                        segAr ? arabicTtf : fgBaseTtf);
                                    segStart = si;
                                }
                            }
                            lines.Add(new CellLine
                            {
                                Text = arShaped,
                                FontSize = fragFontSize,
                                ForegroundColor = color,
                                Align = lineAlign,
                                Type0Ttf = arabicTtf,
                                Type0FontName = arabicFont!.FontName,
                                StyleRuns = arRuns,
                                KernedWidth = arRuns is not null ? arKernW : 0,
                                // Form-grid Arabic lines are CSS boxes like their Latin
                                // siblings: own line box, measured baseline seat.
                                BoxH = FormGridCells && (paragraph as TextFragment)?.CssLineHeightPt > 0
                                    ? (paragraph as TextFragment)!.CssLineHeightPt : 0,
                                BaseOff = FormGridCells
                                    ? (paragraph as TextFragment)?.CssBaseDrop ?? 0 : 0,
                            });
                        }
                        continue;
                    }
                }

                // Cell text carrying a fragment-level embedded font AND CJK content: draw each
                // newline-split line as Type0/CID with that font. Scoped to CJK so a fragment
                // that merely carries an embedded Latin font keeps the existing Standard-14 path.
                if (fragEmbeddedTtf is not null && CjkCoveredBy(text, fragEmbeddedTtf))
                {
                    foreach (var segment in text.Split('\n'))
                    {
                        if (segment.Length == 0) continue;
                        lines.Add(new CellLine
                        {
                            Text = segment, FontSize = fragFontSize, ForegroundColor = color, Align = lineAlign,
                            Type0Ttf = fragEmbeddedTtf, Type0FontName = fragEmbeddedName, Type0SplitTokens = true,
                        });
                    }
                    continue;
                }

                // CJK cell text whose fragment font can't cover it — either no font resolved or
                // the named face is unavailable (e.g. "Arial Unicode MS" isn't installed). The
                // Standard-14 path below has no CJK glyphs and would emit '?'. Substitute an
                // installed system CJK font (MS Gothic when available) and draw as
                // Type0/CID, mirroring the Arabic fallback above.
                if (ContainsCjk(text))
                {
                    var cjkTtf = Aspose.Pdf.Text.CjkFallbackFont.ResolveEmbeddableBytes(text);
                    if (cjkTtf is not null && CjkCoveredBy(text, cjkTtf))
                    {
                        foreach (var segment in text.Split('\n'))
                        {
                            if (segment.Length == 0) continue;
                            // Char-level width wrap (CJK has no ASCII spaces to break at),
                            // measured with the fallback font, so long CJK cell text stays
                            // inside the column. Every character — including spaces — is kept,
                            // so mixed CJK+ASCII like "繋がって or つながって" still reconstructs
                            // fully across the wrapped lines. Each line draws as one Type0/CID run.
                            foreach (var lineText in WrapCjkToWidth(segment, fragFontSize, availWidth, cjkTtf))
                            {
                                lines.Add(new CellLine
                                {
                                    Text = lineText, FontSize = fragFontSize, ForegroundColor = color, Align = lineAlign,
                                    Type0Ttf = cjkTtf, Type0FontName = "MSGothic",
                                });
                            }
                        }
                        continue;
                    }
                }

                // A fragment carrying an inline BUTTON marker renders as ONE control
                // line too — the render pass draws the button chrome around the
                // bracketed caption, so the line must not word-wrap through it.
                if (NestedTableRender && text.IndexOf(InlineButtonChar) >= 0)
                {
                    lines.Add(new CellLine
                    {
                        Text = text, FontSize = fragFontSize, ForegroundColor = color,
                        Align = lineAlign,
                        LeftIndent = ParagraphMargin(paragraph) is { Left: > 0 } btnInd
                            ? btnInd.Left : 0,
                    });
                    continue;
                }

                // A fragment carrying inline radio options renders as ONE control line:
                // the markers in its text draw as circle glyphs in the run (`◯ ◯Yes
                // ◉ ◉No` sits on a single line), so the line is
                // never word-wrapped — its glyph advances are the control boxes.
                if (paragraph is TextFragment { InlineOptions: { Count: > 0 } fragOpts })
                {
                    lines.Add(new CellLine
                    {
                        Text = text, FontSize = fragFontSize, ForegroundColor = color,
                        Align = lineAlign, InlineOptions = fragOpts,
                        // The control line seats on its item's list indent like any
                        // other line the fragment margin carries.
                        LeftIndent = ParagraphMargin(paragraph) is { Left: > 0 } optInd
                            ? optInd.Left : 0,
                    });
                    continue;
                }

                // Always wrap when text would overflow the column; IsWordWrapped=false
                // only suppresses mid-word breaks, not inter-word wrapping — otherwise
                // a cell with long text would clip horizontally. Also split on embedded
                // newlines (from HtmlFragment block-element boundaries) so each HTML
                // block starts on its own line.
                // Each inline <a> run annotates the first laid-out line containing its
                // text — one Link annotation per anchor, over just the anchor's run,
                // pre-measured with the SAME metrics that lay the line out.
                var pendingAnchors = fragAnchors is { Count: > 0 }
                    ? new List<(string Text, string Url)>(fragAnchors) : null;
                List<(double XOff, double W, Hyperlink Link)>? TakeAnchorRuns(
                    string lineText, Func<string, double> measure)
                {
                    if (pendingAnchors is not { Count: > 0 }) return null;
                    List<(double XOff, double W, Hyperlink Link)>? runs = null;
                    for (var ai = 0; ai < pendingAnchors.Count; ai++)
                    {
                        var (atext, url) = pendingAnchors[ai];
                        var idx = atext.Length > 0 ? lineText.IndexOf(atext, StringComparison.Ordinal) : -1;
                        if (idx < 0) continue;
                        (runs ??= new()).Add((idx > 0 ? measure(lineText[..idx]) : 0,
                            measure(atext), new WebHyperlink(url)));
                        pendingAnchors.RemoveAt(ai);
                        ai--;
                    }
                    return runs;
                }

                // Fully-bold styled serif paragraph (e.g. <p style="font-family:georgia">
                // <strong>… in an HTML table cell): the HTML engine draws it in the
                // embedded bold serif face with kerned advances — wrap, align and
                // annotate with those metrics, not the Standard-14 estimate.
                if (paragraph is TextFragment btf && btf.TextState.IsBold && fragCssAsc > 0
                    && IsSerifCssFamily(btf.TextState.Font?.FontName)
                    && BoldSerifTtf() is { } serifBoldTtf)
                {
                    // Band dialect: the paragraph's explicit margin-top becomes a
                    // silent spacer box above its first line (the css-box stack
                    // consumes BoxH for empty-text lines without drawing them).
                    var bMargin = HonorCellFontFaces ? ParagraphMargin(paragraph) : null;
                    if (bMargin is { Top: > 0 })
                        lines.Add(new CellLine { Text = "", FontSize = fragFontSize, BoxH = bMargin.Top, Align = lineAlign });
                    double MeasSerif(string s) => MeasureWidthKerned(s, fragFontSize, serifBoldTtf);
                    foreach (var segment in text.Split('\n'))
                    {
                        if (segment.Length == 0) continue;
                        foreach (var l in WrapKernedLines(segment, fragFontSize, serifBoldTtf, availWidth))
                            lines.Add(new CellLine
                            {
                                Text = l,
                                FontSize = fragFontSize,
                                ForegroundColor = color,
                                Hyperlink = fragLink,
                                LinkRuns = TakeAnchorRuns(l, MeasSerif),
                                Align = lineAlign,
                                CssAsc = fragCssAsc, CssForce = fragCssForce,
                                CssDesc = fragCssDesc,
                                KernTj = true,
                                KernedWidth = MeasSerif(l),
                                // Band tables: route through the per-line render path
                                // (anyType0) so the serif runs draw even in a uniform
                                // left-aligned row — the plain text-object path ignores
                                // Runs and would fall back to Helvetica.
                                Type0Ttf = HonorCellFontFaces ? serifBoldTtf : null,
                                Type0FontName = HonorCellFontFaces ? "Times New Roman Bold" : null,
                                Runs = new List<HtmlRun>
                                {
                                    new HtmlRun { Text = l, X = 0, Size = fragFontSize, Bold = true },
                                },
                            });
                    }
                    continue;
                }

                // Opted-in band tables (HonorCellFontFaces): a cell fragment that resolved
                // a serif font draws in the embedded serif face via the Type0 path, wrapped
                // with the same kerned metrics the column width was measured with. Without
                // this the text wraps and renders on the wider Standard-14 Helvetica
                // estimates, over-wrapping the serif-measured column and overgrowing rows.
                if (HonorCellFontFaces && paragraph is TextFragment stf
                    && IsSerifCssFamily(stf.TextState.Font?.FontName)
                    && (fragBold ? BoldSerifTtf() : SerifTtf()) is { } cellSerifTtf)
                {
                    // The serif faces don't cover the ballot boxes (☐ U+2610 / ☒ U+2612)
                    // vote forms mark their choices with — draw them as the covered white
                    // square so each box leaves its outline on the page (the WinAnsi path
                    // shows '?', an uncovered Type0 run nothing at all).
                    if (text.IndexOf('☐') >= 0 || text.IndexOf('☒') >= 0)
                        text = text.Replace('☐', '□').Replace('☒', '□');
                    double MeasCell(string s) => MeasureWidthKerned(s, fragFontSize, cellSerifTtf);
                    foreach (var segment in text.Split('\n'))
                    {
                        if (segment.Length == 0) continue;
                        foreach (var l in WrapKernedLines(segment, fragFontSize, cellSerifTtf, availWidth))
                            lines.Add(new CellLine
                            {
                                Text = l,
                                FontSize = fragFontSize,
                                ForegroundColor = color,
                                Hyperlink = fragLink,
                                LinkRuns = TakeAnchorRuns(l, MeasCell),
                                Align = lineAlign,
                                CssAsc = fragCssAsc, CssForce = fragCssForce,
                                CssDesc = fragCssDesc,
                                Type0Ttf = cellSerifTtf,
                                Type0FontName = fragBold ? "Times New Roman Bold" : "Times New Roman",
                                KernTj = true,
                                KernedWidth = MeasCell(l),
                            });
                    }
                    continue;
                }

                // Form-document dialect (HonorCellTtfFaces): a cell fragment that resolved
                // any real installed face wraps and draws with that face's kerned hmtx
                // advances via the Type0 path. The Standard-14 Helvetica estimate is
                // narrower than faces like Verdana — it under-wraps the lines the
                // real face wraps and shortens every band below.
                if (HonorCellTtfFaces && paragraph is TextFragment ftf
                    && ftf.TextState.Font?.FontName is { Length: > 0 } cellFaceName
                    && !ContainsCjk(text)
                    && !Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(text)
                    && CellFaceTtf(cellFaceName, fragBold, fragItalic) is { } cellFaceTtf)
                {
                    // Mixed bold runs on ONE line (the owner band's 'Owner Team:
                    // <b>bv Designers</b>'): one CellLine whose StyleRuns the render
                    // draws sequentially, each in its own face variant.
                    if (fragGridRuns is { Count: > 1 })
                    {
                        var runCells = new List<(string Text, byte[] Ttf, string Name)>();
                        var runsW = 0.0;
                        var runsOk = true;
                        foreach (var (runText, runBold) in fragGridRuns)
                        {
                            var runTtf = CellFaceTtf(cellFaceName, runBold || fragBold, fragItalic);
                            if (runTtf is null) { runsOk = false; break; }
                            runCells.Add((runText, runTtf,
                                CellFaceName(cellFaceName, runBold || fragBold, fragItalic)));
                            runsW += MeasureWidthKerned(runText, fragFontSize, runTtf);
                        }
                        if (runsOk)
                        {
                            lines.Add(new CellLine
                            {
                                Text = text,
                                FontSize = fragFontSize,
                                ForegroundColor = color,
                                Align = lineAlign,
                                CssAsc = fragCssAsc, CssForce = fragCssForce,
                                CssDesc = fragCssDesc,
                                Type0Ttf = cellFaceTtf,
                                Type0FontName = CellFaceName(cellFaceName, fragBold, fragItalic),
                                StyleRuns = runCells,
                                KernTj = true,
                                KernedWidth = runsW,
                                // Form-grid lines are CSS boxes: their own line box and
                                // the measured baseline seat within it.
                                BoxH = FormGridCells && fragLineH > 0 ? fragLineH : 0,
                                BaseOff = FormGridCells ? ftf.CssBaseDrop : 0,
                            });
                            continue;
                        }
                    }
                    double MeasFace(string s) => MeasureWidthKerned(s, fragFontSize, cellFaceTtf);
                    foreach (var segment in text.Split('\n'))
                    {
                        if (segment.Length == 0) continue;
                        foreach (var l in WrapKernedLines(segment, fragFontSize, cellFaceTtf, availWidth))
                            lines.Add(new CellLine
                            {
                                Text = l,
                                FontSize = fragFontSize,
                                ForegroundColor = color,
                                Hyperlink = fragLink,
                                LinkRuns = TakeAnchorRuns(l, MeasFace),
                                Align = lineAlign,
                                CssAsc = fragCssAsc, CssForce = fragCssForce,
                                CssDesc = fragCssDesc,
                                Type0Ttf = cellFaceTtf,
                                Type0FontName = CellFaceName(cellFaceName, fragBold, fragItalic),
                                KernTj = true,
                                KernedWidth = MeasFace(l),
                                BoxH = FormGridCells && fragLineH > 0 ? fragLineH : 0,
                                BaseOff = FormGridCells ? ftf.CssBaseDrop : 0,
                            });
                    }
                    continue;
                }

                var linesBeforeFrag = lines.Count;
                // The HTML layout pass measured this cell in its own face at exact
                // Standard-14 advances; wrapping against anything else would put the
                // break somewhere other than where the column width came from.
                // CSS run boxes wrap on the same exact advances too: the coarse estimate
                // runs ~5 % wide, which breaks a token the draw then fits comfortably
                // (a 24 pt "content." measured 89.7 against an 86.9 box, drawn at 85.4).
                Func<string, double>? htmlMeas = HtmlLayoutWrap || CssRunBoxes || NestedTableRender
                    ? s => MeasureFaceExact(s, fragFontSize, fragBold)
                    : null;
                // The CELL'S OWN `line-height` is each line's BOX height in the lifted
                // dialect (the css-box stack advance) — the 1.2-em default otherwise
                // over-pitches a cell the author paced tighter. Document-level pitches
                // stay row-level; the calibrated dialects depend on that.
                var ownBoxH = NestedTableRender && fragLineH > 0
                    && (paragraph as TextFragment)?.CssLineHeightFromCell == true
                    ? fragLineH : 0.0;
                // A paragraph's own vertical margin is a silent spacer box above its
                // first line (the converter hands the COLLAPSED value — max of this
                // top and the previous paragraph's bottom).
                if (NestedTableRender && ParagraphMargin(paragraph) is { Top: > 0 } pMargin)
                    lines.Add(new CellLine { Text = "", FontSize = fragFontSize, BoxH = pMargin.Top, Align = lineAlign });
                foreach (var segment in text.Split('\n'))
                {
                    if (segment.Length == 0) continue;
                    var estWidth = htmlMeas is null ? MeasureWidth(segment, fragFontSize) : htmlMeas(segment);
                    // The lifted render sizes columns off the same estimate the wrap
                    // uses, so a line that EXACTLY fills its column must not break on
                    // sub-point rounding (a trailing "…" spilling to its own line).
                    var wrapSlack = NestedTableRender ? 1.5 : 0.0;
                    if (!cell.HtmlNoWrap && (cell.IsWordWrapped || estWidth > availWidth + wrapSlack))
                    {
                        foreach (var l in WrapText(segment, fragFontSize, availWidth + wrapSlack, htmlMeas,
                                     overflowLongWords: HtmlLayoutWrap))
                            lines.Add(new CellLine { Text = StripZeroWidth(l), FontSize = fragFontSize, ForegroundColor = color, Bold = fragBold, Hyperlink = fragLink, LinkRuns = TakeAnchorRuns(l, s => MeasureWidth(s, fragFontSize)), Align = lineAlign, CssAsc = fragCssAsc, CssForce = fragCssForce, CssDesc = fragCssDesc, BoxH = runBoxH > 0 ? runBoxH : htmlCssBoxPx > 0 ? Math.Round(htmlCssBoxPx * 1.15) * 0.75 : ownBoxH, BaseOff = runBoxH > 0 ? CssRunBaseOff(runBoxH, fragFontSize, fragCssAsc, fragCssDesc) : htmlCssBoxPx > 0 || ownBoxH > 0 ? fragFontSize : 0 });
                    }
                    else
                    {
                        lines.Add(new CellLine { Text = StripZeroWidth(segment), FontSize = fragFontSize, ForegroundColor = color, Bold = fragBold, Hyperlink = fragLink, LinkRuns = TakeAnchorRuns(segment, s => MeasureWidth(s, fragFontSize)), Align = lineAlign, CssAsc = fragCssAsc, CssForce = fragCssForce, CssDesc = fragCssDesc, BoxH = runBoxH > 0 ? runBoxH : htmlCssBoxPx > 0 ? Math.Round(htmlCssBoxPx * 1.15) * 0.75 : ownBoxH, BaseOff = runBoxH > 0 ? CssRunBaseOff(runBoxH, fragFontSize, fragCssAsc, fragCssDesc) : htmlCssBoxPx > 0 || ownBoxH > 0 ? fragFontSize : 0 });
                    }
                }
                // The fragment's FootNote marker attaches after its last laid-out line.
                if (paragraph is TextFragment { FootNote: { } fragNote }
                    && lines.Count > linesBeforeFrag)
                    lines[^1].FootNote = fragNote;
                // The fragment's own LEFT margin (an <li>'s hanging list indent from
                // the converter) indents every line it laid out.
                if (NestedTableRender && ParagraphMargin(paragraph) is { Left: > 0 } pIndent)
                    for (var ii = linesBeforeFrag; ii < lines.Count; ii++)
                        lines[ii].LeftIndent = pIndent.Left;
                // Inline boxes (title plates, status pills) ride the fragment's first
                // laid-out line; the box line height (text pitch + pads) becomes the
                // line's own CSS box so the row reserves the pill's full height.
                if (paragraph is TextFragment { InlineBoxes: { Count: > 0 } fragBoxes }
                    && lines.Count > linesBeforeFrag)
                {
                    var deco = lines[linesBeforeFrag];
                    deco.Boxes = fragBoxes;
                    // The boxes' laid-out extent IS the line's width (the box model
                    // owns the pen) — alignment must flush the boxes, not the flat
                    // text they replaced.
                    double boxExtent = 0;
                    var boxText = false;
                    foreach (var fb in fragBoxes)
                    {
                        boxExtent = Math.Max(boxExtent, fb.XOff + fb.Width);
                        if (fb.Text is not null) boxText = true;
                    }
                    if (boxText && boxExtent > 0) deco.KernedWidth = boxExtent;
                    if (fragLineH > fragFontSize * 1.2 + 0.01 && deco.BoxH <= 0)
                    {
                        var decoPadT = 0.0;
                        foreach (var fb in fragBoxes) decoPadT = Math.Max(decoPadT, fb.PadTop);
                        deco.BoxH = fragLineH;
                        deco.BaseOff = decoPadT + fragFontSize;
                    }
                }
            }
            // CSS line-box mode: a cell whose lines carry mixed font sizes (styled HTML
            // paragraphs) stacks each line as its own box (1.2 × em) with the baseline at
            // ascent + half-leading — the uniform LineHeight grid can't express this.
            var cssMode = false;
            var preBox = false;
            if (plan.CellInline is null || !plan.CellInline.ContainsKey(col))
            {
                double sz0 = -1; var mixed = false; var anyCss = false; var anyControl = false;
                var anyForce = false;
                foreach (var l in lines)
                {
                    if (l.Option is not null || l.Checkbox is not null
                        || l.InlineOptions is not null
                        || l.Text.IndexOf(InlineButtonChar) >= 0) { anyControl = true; break; }
                    if (l.CssAsc > 0) anyCss = true;
                    if (l.CssForce) anyForce = true;   // form-dialect cell: CSS boxes at a uniform size too
                    if (l.Boxes is { Count: > 0 }) anyForce = true;   // inline boxes stack by their own BoxH
                    if (l.BoxH > 0) preBox = true;   // box set at line build (bold-serif HTML cell)
                    if (sz0 < 0) sz0 = l.FontSize;
                    else if (Math.Abs(l.FontSize - sz0) > 0.01) mixed = true;
                }
                cssMode = !anyControl && (anyCss && mixed || preBox || anyForce);
            }
            // A STANDALONE BADGE cell — every line text-less and carrying only inline
            // boxes (the risks pill's traffic-light circle) — owns its box height.
            // Folding it into the row's SHARED content height and then stacking a
            // sibling cell's padding on top sizes the row at box + other-cell padding
            // (24.5 pt instead of the correct 19.2, where the circle sits in its own
            // 17.25 pt cell beside a 19.2 pt padded button). It sizes as its own stack
            // instead, and the row takes the max over cells of (padding + content).
            var badgeOnlyCell = NestedTableRender && lines.Count > 0
                && lines.TrueForAll(l => l.Text.Length == 0 && l.Boxes is { Count: > 0 });
            if (cssMode)
            {
                double sum = 0;
                foreach (var l in lines)
                {
                    if (l.BoxH <= 0)
                    {
                        // A nested-table reserve line's FontSize IS the grid's exact
                        // height — the 1.2 line-box factor would pad the row by 20%
                        // of the whole grid.
                        l.BoxH = NestedTableRender && l.ImgReserve
                            ? l.FontSize : l.FontSize * 1.2;
                        l.BaseOff = l.CssAsc > 0
                            ? l.FontSize * (l.CssAsc + (1.2 - l.CssAsc - l.CssDesc) / 2)
                            : l.FontSize;
                    }
                    sum += l.BoxH;
                }
                (plan.CssCells ??= new HashSet<int>()).Add(col);
                if (sum > plan.CssContentH && !badgeOnlyCell)
                {
                    plan.CssContentH = sum;
                    // ⚠ The content box ends on its LAST BASELINE, but trimming to
                    // it here is the wrong lever: it recovers only ~2.2 pt and hurts the
                    // page overall. Row 0's real excess is one whole 12.75 pt line box —
                    // the cell's leading zero-width space takes a line of its own, where a
                    // browser merges it into the first text line (whose box then grows by
                    // descent × (24 − 9.75) = 3.562, giving its 35.812 first-baseline step).
                    plan.CssContentTight = 0;
                }
            }
            else if (lines.Count > plan.NonCssLineCount) plan.NonCssLineCount = lines.Count;

            // A styled-paragraph cell (a block-aligned fragment with its own
            // stylesheet) drops leading blank spacers — the empty companion
            // fragment contributes no line to it.
            var hasStyledPara = lines.Exists(l => l.Text.Length > 0 && l.BoxH > 0 && l.BoxH != l.FontSize);
            if (hasStyledPara)
                while (lines.Count > 0 && lines[0].Text.Length == 0 && !lines[0].ImgReserve
                       && !lines[0].HtmlEngine && lines[0].Boxes is null)
                    lines.RemoveAt(0);
            plan.CellLines.Add(lines);
            if (lines.Count > plan.LineCount) plan.LineCount = lines.Count;
            double cellTight = 0;
            foreach (var cl in lines) if (cl.FontSize > cellTight) cellTight = cl.FontSize;
            // A multi-line control cell (text line(s) stacked above a checkbox)
            // sizes as the EXACT stack of its parts — each text line at its own
            // font size and the box at its box height (a " " + 8.5pt checkbox
            // cell is 10 + 8.5 = 18.5, not two 10pt grid lines).
            double cellExact = 0;
            double cellOwnStack = 0;
            var cellHasBox = false;
            var cellHasReserve = false;
            foreach (var cl in lines)
            {
                if (cl.Checkbox is { } cb)
                {
                    cellHasBox = true;
                    cellOwnStack += cb.Height > 0 ? cb.Height : cl.FontSize;
                }
                // Nested-table reserve line: its FontSize IS the grid's full height,
                // and a boxed line's own height is its BoxH — the exact stack must
                // price them truly (lifted render only; legacy stays byte-stable).
                // EMPTY filler lines around the placeholder draw nothing and take
                // nothing (they were padding the row ~30 pt below the grid).
                else if (NestedTableRender)
                {
                    if (cl.ImgReserve) cellHasReserve = true;
                    // A text line occupies its CSS line BOX, the same pitch the draw
                    // stacks it at — pricing it at the bare em here let a tall cell's
                    // lines run past the row band and draw over the row below.
                    if (cl.ImgReserve || cl.Text.Length > 0 || cl.Boxes is { Count: > 0 } || cl.BoxH > 0
                        || cl.HtmlEngine)
                        cellOwnStack += cl.BoxH > 0 ? cl.BoxH
                            : cl.ImgReserve ? cl.FontSize
                            : CssLineBoxPt(cl.FontSize);
                }
                else cellOwnStack += cl.FontSize;
            }
            if (cellHasBox && lines.Count > 1) cellExact = cellOwnStack;
            if (badgeOnlyCell) cellExact = cellOwnStack;
            // A nested-grid cell sizes as its exact stack (the reserve's height plus
            // any sibling lines) — the uniform line grid would re-quantize it.
            if (cellHasReserve) cellExact = cellOwnStack;
            cellTotals.Add((padV, lines.Count, cellTight, cellExact, cellOwnStack));
        }
        plan.LineHeight = maxLineHeight > 0 ? maxLineHeight : DefaultLineHeightPt;
        plan.TightLine = tightForMax > 0 ? tightForMax : plan.LineHeight;
        // Band-doc tables (HonorCellFontFaces): unstyled text rows advance at the CSS
        // line box of their font size — round(pt·(4/3)·1.15)px·0.75, e.g. 9 pt for an
        // 8 pt line — not at the bare font size, which packs multi-line cells ~1 pt/line
        // tighter than a browser lays them out.
        // The lifted HTML render lays its text out on the same browser line box (its
        // 8 pt body columns pitch at 9 pt, line for line with a browser).
        // …and so does a grid the stylesheet styles: its 8 pt body columns pitch at 9,
        // line for line with a browser. A table the stylesheet never addresses
        // keeps the calibrated bare-em pitch — re-pitching it grows every row ~12 %
        // and walks the whole table down the page.
        if ((HonorCellFontFaces || (NestedTableRender && HtmlChainStyledCells))
            && plan.CssContentH <= 0 && maxLineHeight > 0)
        {
            plan.LineHeight = CssLineBoxPt(plan.LineHeight);
            plan.TightLine = CssLineBoxPt(plan.TightLine);
        }
        // Row height = MAX over cells of (its own padding + its own content),
        // not max-content + max-padding: a title row with
        // a 14 pt/pad-8 title cell next to a 10 pt/pad-11 number cell sizes at 22 pt
        // (the title cell's total), not 25. Expressed as effective padding on
        // top of the row's content grid.
        var rowContentH = plan.LineCount == 0 ? 0.0
            : (plan.LineCount - 1) * plan.LineHeight + plan.TightLine;
        var maxCellTotal = 0.0;
        var anyExactCell = false;
        var maxOwnTotal = 0.0;
        foreach (var (cpv, cn, ctight, cexact, cown) in cellTotals)
        {
            var ch = cexact > 0 ? cexact : cn == 0 ? 0 : (cn - 1) * plan.LineHeight + ctight;
            if (cexact > 0) anyExactCell = true;
            if (cpv + ch > maxCellTotal) maxCellTotal = cpv + ch;
            if (cn > 0 && cpv + cown > maxOwnTotal) maxOwnTotal = cpv + cown;
        }
        // A row holding an exact-stack control cell sizes to the max over cells
        // of each cell's OWN stacked height (every text line at its own font
        // size, boxes at their box height) — the uniform grid would price a
        // 7pt side label at the row's 10pt pitch and oversize the row.
        plan.ExactTotalH = anyExactCell ? maxOwnTotal : 0;
        // UA cell boxes: the row's content already stacks on the CSS line-box grid, so
        // the padding is simply the widest cell's own — deriving it from
        // maxCellTotal−rowContentH would net off the difference between the per-cell
        // tight line (the glyph height) and the row's line box and swallow most of it.
        // CSS run boxes stack on the same CSS line-box grid, so the padding is likewise
        // the widest cell's own: netting maxCellTotal against rowContentH would cancel
        // the difference between a cell's TIGHT line (its glyph height) and the row's
        // line box and swallow most of the padding.
        // A grid drawing in the document's own face stacks on that face's CSS line box
        // as well, so its padding is the widest cell's own for the same reason. The
        // lifted nested-table render keeps the cells' own padding too: its rows stack
        // reserve lines/css boxes exactly, and the net-off would swallow the
        // cellspacing bands the outer table declares.
        plan.VertPadding = UaCellBoxes || CssRunBoxes || HonorCellTtfFaces || NestedTableRender
            ? maxVertPad
            : plan.LineCount == 0 || maxCellTotal <= 0
            ? maxVertPad
            : Math.Max(0, maxCellTotal - rowContentH);
        plan.TopPad = maxTopPad;
        plan.MinBlankHeight = Math.Max(row.FixedRowHeight, row.MinRowHeight);
        // A content-less row reserves a single line (no padding, see the slice loop) —
        // matching the generator's tight spacer rows rather than a full row. Band-doc
        // tables (HonorCellFontFaces) collapse it to nothing instead: their all-empty
        // rows are CSS column-width definitions (<td width="6%"></td>…), which browsers
        // lay out at zero height.
        if (plan.MinBlankHeight <= 0)
            plan.MinBlankHeight = plan.LineCount != 0 ? 20
                : HonorCellFontFaces ? 0 : plan.LineHeight;
        // A whitespace-only row (e.g. a " " spacer) is likewise a tight spacer drawn
        // without cell padding so it reserves just its line.
        // …but under CSS run boxes a row of empty cells is an ORDINARY row: the browser
        // gives it its cells' padding plus one line box (the invisible character those
        // cells hold is still a line), which is what draws its rules.
        // Under the lifted nested-table render, a row whose "blank" lines are
        // nested-table or image RESERVES is content, not a spacer — dropping its
        // padding would strip the cellspacing bands and jam the nested grid
        // against the row border. Legacy dialects keep the historical rule.
        plan.IsBlankRow = !CssRunBoxes && plan.CellInline is null && plan.LineCount > 0
            && row.FixedRowHeight <= 0
            && !(NestedTableRender && plan.CellTables is not null)
            && System.Linq.Enumerable.All(plan.CellLines,
                cl => System.Linq.Enumerable.All(cl,
                    l => string.IsNullOrWhiteSpace(l.Text) && !(NestedTableRender && l.ImgReserve)));
        return plan;
    }

    /// <summary>Effective font size for a cell text fragment: the fragment's own size when
    /// set, else the first segment that carries one (callers commonly set size on the
    /// TextSegment rather than the TextFragment), else the cell default.</summary>
    private static double ResolveFragmentFontSize(Aspose.Pdf.Text.TextFragment tf, double fallback)
    {
        // A TextFragment built via the parameterless ctor + Segments.Add carries a
        // default empty leading segment, so prefer the size of a segment that actually
        // has text (where callers set an explicit per-segment size) over the fragment's
        // own default state.
        if (tf.Segments is { Count: > 0 })
            foreach (var s in tf.Segments)
                if (s.TextState.FontSizeTouched && !string.IsNullOrEmpty(s.Text))
                    return s.TextState.FontSize;
        if (tf.TextState.FontSizeTouched) return tf.TextState.FontSize;
        return fallback;
    }

    /// <summary>Read the (type-shadowed) IsInLineParagraph flag — TextFragment redeclares
    /// it with <c>new</c>, so a BaseParagraph-typed read would miss the value callers set.</summary>
    private static bool IsInlineParagraph(BaseParagraph p) => p switch
    {
        Aspose.Pdf.Text.TextFragment tf => tf.IsInLineParagraph,
        _ => p.IsInLineParagraph,
    };

    /// <summary>Read the (type-shadowed) Margin — TextFragment redeclares it with
    /// <c>new</c>, so a BaseParagraph-typed read would miss the value callers set.</summary>
    private static MarginInfo? ParagraphMargin(BaseParagraph p) => p switch
    {
        Aspose.Pdf.Text.TextFragment tf => tf.Margin,
        _ => p.Margin,
    };

    /// <summary>A TextFragment carrying more than one non-empty segment — each segment has its own
    /// TextState (size/colour/super-subscript) and is laid out as a distinct inline run.</summary>
    private static bool IsMultiSegmentFragment(BaseParagraph p) =>
        p is Aspose.Pdf.Text.TextFragment tf
        && System.Linq.Enumerable.Count(tf.Segments, s => !string.IsNullOrEmpty(s.Text)) > 1;

    /// <summary>Lay a graph-bearing cell out into left-to-right inline rows, wrapping at
    /// the cell's content width. Each row is positioned <see cref="InlineItem"/>s (a text
    /// run or a Graph) with x-offsets from the cell content-left; the render pass draws the
    /// text and blits each graph's content stream at the resolved position.</summary>
    private List<List<InlineItem>> BuildInlineCellLayout(
        Cell cell, double availWidth, double defaultFontSize,
        Aspose.Pdf.Text.TextState? cellTextState, out double lineHeight)
    {
        var rows = new List<List<InlineItem>>();
        var current = new List<InlineItem>();
        double x = 0;
        var maxH = defaultFontSize * 1.2;
        var contentW = availWidth > 0 ? availWidth : double.MaxValue;

        var lineHasText = false;
        var rowItemSources = 0;
        var rowRightCount = 0;

        void NoteAlign(HorizontalAlignment a)
        {
            rowItemSources++;
            if (a == HorizontalAlignment.Right) rowRightCount++;
        }

        void Flush()
        {
            if (current.Count > 0)
            {
                // Right-aligned inline row: paragraph order packs
                // from the RIGHT content edge (first paragraph rightmost), so an
                // image + joined right-aligned text renders [text][image] against
                // the cell's right padding edge.
                if (rowItemSources > 0 && rowRightCount == rowItemSources && contentW < double.MaxValue)
                {
                    var xr = contentW;
                    foreach (var it in current) { it.X = xr - it.Width; xr -= it.Width; }
                }
                rows.Add(current);
                current = new List<InlineItem>();
            }
            x = 0;
            lineHasText = false;
            rowItemSources = 0;
            rowRightCount = 0;
        }

        static HorizontalAlignment FragAlign(Aspose.Pdf.Text.TextFragment f)
        {
            if (f.TextState.HorizontalAlignment == HorizontalAlignment.Right)
                return HorizontalAlignment.Right;
            foreach (var s in f.Segments)
                if (s.TextState.HorizontalAlignment == HorizontalAlignment.Right)
                    return HorizontalAlignment.Right;
            return f.TextState.HorizontalAlignment;
        }

        foreach (var para in cell.Paragraphs)
        {
            if (para is Aspose.Pdf.Drawing.Graph g)
            {
                if (!g.IsInLineParagraph) Flush();
                var marginL = g.Margin?.Left ?? 0;
                if (current.Count > 0 && x + marginL + g.Width > contentW) Flush();
                x += marginL;
                current.Add(new InlineItem { Graph = g, X = x, Width = g.Width, Height = g.Height });
                x += g.Width;
                if (g.Height > maxH) maxH = g.Height;
                if (!g.IsInLineParagraph) Flush();
            }
            else if (para is Aspose.Pdf.Text.TextFragment tf)
            {
                var marginL = tf.Margin?.Left ?? 0;
                if (!tf.IsInLineParagraph) Flush();

                // A multi-segment fragment lays its segments out as consecutive inline runs on
                // the SAME line, each keeping its own size / colour / baseline (sub-superscript),
                // instead of being flattened to one merged run. Every segment is emitted —
                // including the parameterless TextFragment() ctor's default empty leading segment,
                // which the generator renders as an empty run (a leading empty fragment).
                var segs = System.Linq.Enumerable.ToList(tf.Segments);
                var textCount = System.Linq.Enumerable.Count(segs, s => !string.IsNullOrEmpty(s.Text));
                if (textCount > 1)
                {
                    x += marginL;
                    // A line's first EMBEDDED-font run gets an empty marker run in the
                    // default table font before it (a font-resource
                    // prelude the absorber surfaces as an empty fragment). Standard-font
                    // runs get no marker.
                    void EnsureLineMarker(double fs)
                    {
                        if (lineHasText) return;
                        lineHasText = true;
                        current.Add(new InlineItem { Text = "", Empty = true, FontSize = fs, X = x, Width = 0, Height = fs * 1.2 });
                        if (fs * 1.2 > maxH) maxH = fs * 1.2;
                    }
                    foreach (var seg in segs)
                    {
                        var ss = seg.TextState;
                        var baseFs = ss.FontSizeTouched ? ss.FontSize
                            : (tf.TextState.FontSizeTouched ? tf.TextState.FontSize : defaultFontSize);
                        var segColor = ss.ForegroundColor ?? tf.TextState.ForegroundColor ?? cellTextState?.ForegroundColor;
                        if (string.IsNullOrEmpty(seg.Text))
                        {
                            current.Add(new InlineItem { Text = "", Empty = true, FontSize = baseFs, Color = segColor, X = x, Width = 0, Height = baseFs * 1.2 });
                            if (baseFs * 1.2 > maxH) maxH = baseFs * 1.2;
                            continue;
                        }

                        // Newline characters break the inline row: each empty piece is
                        // an empty run at the pen position (before the break, and again
                        // at the new line's start — both are emitted).
                        var segPieces = seg.Text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                        for (var spi = 0; spi < segPieces.Length; spi++)
                        {
                        if (spi > 0) Flush();
                        var segPiece = segPieces[spi];
                        if (segPiece.Length == 0)
                        {
                            current.Add(new InlineItem { Text = "", Empty = true, FontSize = baseFs, Color = segColor, X = x, Width = 0, Height = baseFs * 1.2 });
                            if (baseFs * 1.2 > maxH) maxH = baseFs * 1.2;
                            continue;
                        }

                        // Per-segment embedded font (e.g. NotoSans / NotoSansArabic supplied on the
                        // segment's TextState): the run is drawn with that font embedded as Type0, so
                        // it is measured with the font's real glyph advances. Arabic is shaped first
                        // (contextual presentation forms + bidi visual order) and kept as one run.
                        var segTtf = ss.Font?.SourceFontData?.TtfData;
                        var segFontName = ss.Font?.FontName;
                        var isArabic = Aspose.Pdf.Text.ArabicTextShaper.ContainsArabic(segPiece);
                        if (segTtf is not null && isArabic)
                        {
                            var shaped = Aspose.Pdf.Text.ArabicTextShaper.Shape(segPiece);
                            var aw = MeasureWidthWithFont(shaped, baseFs, segTtf);
                            if (current.Count > 0 && x + aw > contentW) { Flush(); x += marginL; }
                            EnsureLineMarker(baseFs);
                            current.Add(new InlineItem
                            {
                                Text = shaped, FontSize = baseFs, Color = segColor, X = x, Width = aw,
                                Height = baseFs * 1.2, BaseFontSize = baseFs, Ttf = segTtf, FontName = segFontName,
                            });
                            x += aw;
                            if (baseFs * 1.2 > maxH) maxH = baseFs * 1.2;
                            continue;
                        }
                        if (segTtf is not null)
                        {
                            // Latin/other run with an embedded font. A piece that fits at
                            // the current pen stays ONE run (one show
                            // op per segment); only an overflowing piece word-wraps at the
                            // cell width using the font's real advances.
                            var fullW = MeasureWidthWithFont(segPiece, baseFs, segTtf);
                            if (x + fullW <= contentW)
                            {
                                EnsureLineMarker(baseFs);
                                current.Add(new InlineItem
                                {
                                    Text = segPiece, FontSize = baseFs, Color = segColor, X = x, Width = fullW,
                                    Height = baseFs * 1.2, BaseFontSize = baseFs, Ttf = segTtf, FontName = segFontName,
                                });
                                x += fullW;
                                if (baseFs * 1.2 > maxH) maxH = baseFs * 1.2;
                                continue;
                            }
                            foreach (var token in SplitKeepingSpaces(segPiece))
                            {
                                var tw = MeasureWidthWithFont(token, baseFs, segTtf);
                                if (current.Count > 0 && x + tw > contentW) { Flush(); x += marginL; }
                                EnsureLineMarker(baseFs);
                                current.Add(new InlineItem
                                {
                                    Text = token, FontSize = baseFs, Color = segColor, X = x, Width = tw,
                                    Height = baseFs * 1.2, BaseFontSize = baseFs, Ttf = segTtf, FontName = segFontName,
                                });
                                x += tw;
                            }
                            if (baseFs * 1.2 > maxH) maxH = baseFs * 1.2;
                            continue;
                        }

                        // No per-segment font: keep the existing Standard-14 path (sub/superscript
                        // at a reduced size with a baseline shift).
                        var sup = ss.Superscript; var sub = ss.Subscript;
                        var segFs = (sup || sub) ? baseFs * SubSuperScale : baseFs;
                        var shift = sup ? baseFs * SuperscriptRise : sub ? -baseFs * SubscriptRise : 0.0;
                        var sw = MeasureWidthExact(segPiece, segFs);
                        if (current.Count > 0 && x + sw > contentW) { Flush(); x += marginL; }
                        lineHasText = true;
                        current.Add(new InlineItem
                        {
                            Text = segPiece, FontSize = segFs, Color = segColor,
                            X = x, Width = sw, Height = baseFs * 1.2, BaseFontSize = baseFs, BaselineShift = shift,
                        });
                        x += sw;
                        if (baseFs * 1.2 > maxH) maxH = baseFs * 1.2;
                        }
                    }
                }
                else
                {
                    var text = tf.Text ?? string.Empty;
                    var fs = ResolveFragmentFontSize(tf, defaultFontSize);
                    var color = tf.TextState.ForegroundColor ?? cellTextState?.ForegroundColor;
                    // Fragment-level embedded font AND CJK content: draw it as Type0/CID with the
                    // font's real advances instead of the Standard-14 path (which would emit '?').
                    // Scoped to CJK so an embedded Latin font keeps the existing inline path.
                    var fragTtf = tf.TextState.Font?.SourceFontData?.TtfData;
                    if (fragTtf is not null && CjkCoveredBy(text, fragTtf))
                    {
                        var w0 = MeasureWidthWithFont(text, fs, fragTtf);
                        if (current.Count > 0 && x + marginL + w0 > contentW) Flush();
                        x += marginL;
                        current.Add(new InlineItem
                        {
                            Text = text, FontSize = fs, Color = color, X = x, Width = w0,
                            Height = fs * 1.2, BaseFontSize = fs, Ttf = fragTtf,
                            FontName = tf.TextState.Font?.FontName,
                        });
                        x += w0;
                        if (fs * 1.2 > maxH) maxH = fs * 1.2;
                    }
                    else
                    {
                        var w = MeasureWidthExact(text, fs);
                        if (current.Count > 0 && x + marginL + w > contentW) Flush();
                        x += marginL;
                        current.Add(new InlineItem { Text = text, FontSize = fs, Color = color, X = x, Width = w, Height = fs * 1.2 });
                        x += w;
                        if (fs * 1.2 > maxH) maxH = fs * 1.2;
                    }
                }
                NoteAlign(FragAlign(tf));
                if (!tf.IsInLineParagraph) Flush();
            }
            else if (para is Image inlineImg)
            {
                // An Image among inline paragraphs joins the line as a fixed box;
                // following IsInLineParagraph text continues on the same line
                // (so the row does NOT flush after the image).
                var bytes = ReadImageBytes(inlineImg);
                if (bytes is null) continue;
                if (!inlineImg.IsInLineParagraph) Flush();
                double dispW, dispH;
                if (inlineImg.FixWidth > 0 && inlineImg.FixHeight > 0)
                {
                    dispW = inlineImg.FixWidth;
                    dispH = inlineImg.FixHeight;
                }
                else if (TryGetCellImageSizePt(bytes, out var nw, out var nh) && nw > 0 && nh > 0)
                {
                    dispW = contentW < double.MaxValue && nw > contentW ? contentW : nw;
                    dispH = nh;
                }
                else
                {
                    dispW = dispH = 24;
                }
                if (current.Count > 0 && x + dispW > contentW) Flush();
                current.Add(new InlineItem { ImageData = bytes, X = x, Width = dispW, Height = dispH });
                x += dispW;
                if (dispH > maxH) maxH = dispH;
                NoteAlign(inlineImg.HorizontalAlignment);
            }
            // Other paragraph kinds inside a graph cell are not laid out inline.
        }
        Flush();
        if (rows.Count == 0) rows.Add(new List<InlineItem>());
        lineHeight = maxH;
        return rows;
    }

    /// <summary>Resolve an image's natural size in points for in-cell layout. On Windows
    /// the platform decoder is used so images without explicit density (JFIF units=0)
    /// resolve at the 96-DPI default the generator assumes; elsewhere it falls back to the
    /// header parser (which defaults such images to 72 DPI).</summary>
    private static bool TryGetCellImageSizePt(byte[] data, out double widthPt, out double heightPt)
    {
        widthPt = 0; heightPt = 0;
        if (OperatingSystem.IsWindows())
        {
            try
            {
#pragma warning disable CA1416
                using var ms = new MemoryStream(data);
                using var img = System.Drawing.Image.FromStream(ms, false, false);
                var dpiX = img.HorizontalResolution > 0 ? img.HorizontalResolution : 96;
                var dpiY = img.VerticalResolution > 0 ? img.VerticalResolution : 96;
                widthPt = img.Width * 72.0 / dpiX;
                heightPt = img.Height * 72.0 / dpiY;
                if (widthPt > 0 && heightPt > 0) return true;
#pragma warning restore CA1416
            }
            catch { /* fall through to the header parser */ }
        }
        return Document.TryGetImageNaturalSizePt(data, out widthPt, out heightPt);
    }

    /// <summary>Read an <see cref="Image"/> paragraph's bytes from its stream or file,
    /// rewinding a seekable stream so a second build pass still sees the data.</summary>
    private static byte[]? ReadImageBytes(Image img)
    {
        var raw = ReadRawImageBytes(img);

        // Page.AddImage only accepts raster formats. An SVG source (FileType=Svg or
        // detected from the bytes) is rasterised first so a vector image embedded in
        // a cell renders instead of throwing "Unsupported image format".
        if (raw is { Length: > 0 } && IsSvg(img, raw))
            return ImageRasterizer.RasterizeSvg(raw) ?? raw;
        return raw;
    }

    /// <summary>The source bytes of an <see cref="Image"/> paragraph as authored —
    /// an SVG stays vector text here (no rasterisation), so callers can inspect
    /// its root attributes before deciding a raster size.</summary>
    private static byte[]? ReadRawImageBytes(Image img)
    {
        if (img.ImageStream is not null)
        {
            var stream = img.ImageStream;
            var pos = stream.CanSeek ? stream.Position : -1L;
            try
            {
                if (stream.CanSeek) stream.Position = 0;
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            finally
            {
                if (pos >= 0) stream.Position = pos;
            }
        }
        if (!string.IsNullOrEmpty(img.File) && System.IO.File.Exists(img.File))
            return System.IO.File.ReadAllBytes(img.File);
        return null;
    }

    /// <summary>True when an SVG root declares no width/height (viewBox-only or bare):
    /// the artwork has no intrinsic size, so a cell placement sizes it to the space
    /// it sits in rather than to the raster's natural dimensions.</summary>
    private static bool SvgLacksIntrinsicSize(byte[] svgData)
    {
        try
        {
            var head = System.Text.Encoding.UTF8.GetString(
                svgData, 0, System.Math.Min(2048, svgData.Length));
            var m = System.Text.RegularExpressions.Regex.Match(head, "<svg\\b[^>]*>");
            if (!m.Success) return false;
            return !System.Text.RegularExpressions.Regex.IsMatch(
                m.Value, "\\s(width|height)\\s*=");
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsSvg(Image img, byte[] data)
    {
        if (img.FileType == ImageFileType.Svg) return true;
        // Sniff: an SVG file starts with an XML prolog or the <svg root, possibly
        // after a UTF-8 BOM / leading whitespace.
        int i = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) i = 3;
        while (i < data.Length && (data[i] == ' ' || data[i] == '\t' || data[i] == '\r' || data[i] == '\n')) i++;
        var head = System.Text.Encoding.ASCII.GetString(data, i, System.Math.Min(512, data.Length - i));
        return head.StartsWith("<?xml") ? head.Contains("<svg") : head.StartsWith("<svg");
    }

    /// <summary>Emit content for the slices that landed on the current page.</summary>
    private byte[] BuildSlicesContent(List<RowSlice> slices, double[] colWidths,
        double tableX, string fontName, int[] cellMap, Page? linkPage = null,
        List<SpanBlock>? spanBlocks = null)
    {
        // A measure-only build (page-break pre-flight) must not add annotations or
        // widgets to the page — only the real build emits them.
        if (_measureOnly) linkPage = null;
        var builder = new ContentStreamBuilder();
        var links = linkPage is not null ? new List<(Rectangle rect, Hyperlink link)>() : null;
        var optionSink = linkPage is not null
            ? new List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>() : null;
        var checkboxSink = linkPage is not null
            ? new List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>() : null;
        var pageImages = new List<(byte[] data, Rectangle rect)>();
        var pageGraphs = new List<byte[]>();
        var pageFootnotes = _measureOnly
            ? null : new List<(Note note, double x, double baseline, double size)>();
        // See HtmlSpaceClassFirst: unify space/no-break-space on the laid-out lines. The
        // lines are post-wrap and both characters measure at the space width, so this
        // changes only which of the two identical-looking characters the page carries.
        if (HtmlSpaceClassFirst is ' ' or ' ')
            foreach (var slice in slices)
                foreach (var cellLines in slice.Plan.CellLines)
                    foreach (var cl in cellLines)
                        if (cl.Text is { Length: > 0 })
                            cl.Text = HtmlSpaceClassFirst == ' '
                                ? cl.Text.Replace(' ', ' ')
                                : cl.Text.Replace(' ', ' ');
        builder.SaveState();
        foreach (var slice in slices)
            RenderRowSlice(builder, slice, colWidths, tableX, fontName, cellMap, links, pageImages, optionSink, pageGraphs, checkboxSink, linkPage, pageFootnotes);
        _pageImages.Add(pageImages);
        _pageGraphs.Add(pageGraphs);
        _pageFootnotes.Add(pageFootnotes ?? new List<(Note, double, double, double)>());

        // Row-spanning cells: draw each block once over the union of its rows' slices
        // on this page. A block split by a page break re-draws its background, border
        // and (re-centred) content in the portion visible on each page — matching the
        // generator's continuation rendering.
        if (spanBlocks is { Count: > 0 })
        {
            foreach (var block in spanBlocks)
            {
                double top = double.MinValue, bottom = double.MaxValue;
                int minRow = int.MaxValue, maxRow = int.MinValue;
                foreach (var slice in slices)
                {
                    if (slice.RowIndex < block.StartRow || slice.RowIndex >= block.EndRow) continue;
                    if (slice.TopY > top) top = slice.TopY;
                    if (slice.TopY - slice.Height < bottom) bottom = slice.TopY - slice.Height;
                    if (slice.RowIndex < minRow) minRow = slice.RowIndex;
                    if (slice.RowIndex > maxRow) maxRow = slice.RowIndex;
                }
                if (top <= double.MinValue) continue;   // no rows of this span on this page
                // A block split by a page break draws its content once, on the page
                // holding the block's middle row (the generator's behaviour);
                // the other pages get background and border only.
                var midRow = (int)Math.Round((block.StartRow + block.EndRow - 1) / 2.0,
                    MidpointRounding.AwayFromZero);
                var drawContent = midRow >= minRow && midRow <= maxRow;

                var x = tableX;
                for (var c = 0; c < block.GridCol && c < colWidths.Length; c++) x += colWidths[c];
                var w = GetCellWidth(colWidths, block.GridCol, block.ColSpan);
                var h = top - bottom;
                var cell = block.Cell;
                var row = block.Row;

                var bgColor = cell.BackgroundColor ?? row.BackgroundColor;
                if (bgColor is not null)
                {
                    builder.SetFillColor(bgColor);
                    builder.Rectangle(x, bottom, w, h);
                    builder.Fill();
                }
                if (!cell.IsNoBorder)
                {
                    var cellBorder = cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
                    if (cellBorder is not null)
                        DrawBorder(builder, cellBorder, x, bottom, w, h);
                }

                // Record the block's page-space rect (union across pages) for Cell.Rect readers.
                cell.Width = w;
                var blockRect = new Rectangle(x, bottom, x + w, top);
                cell.Rect = cell.Rect is null
                    ? blockRect
                    : new Rectangle(
                        Math.Min(cell.Rect.LLX, blockRect.LLX), Math.Min(cell.Rect.LLY, blockRect.LLY),
                        Math.Max(cell.Rect.URX, blockRect.URX), Math.Max(cell.Rect.URY, blockRect.URY));

                if (block.Lines.Count == 0 || !drawContent) continue;
                var padding = EffectivePad(cell, row);
                var dp = DefaultPad(cell, row);
                var padLeft = padding?.Left ?? dp;
                var padRight = padding?.Right ?? dp;
                var padTop = padding?.Top ?? 0;
                var gapsTotal = 0.0;
                foreach (var l in block.Lines) gapsTotal += l.TopGap;
                var blockH = (block.Lines.Count - 1) * block.LineHeight + block.TightLine + gapsTotal;
                // A spanning cell's content block: an unset alignment centres it
                // vertically in the portion visible on the page; an EXPLICIT Top
                // pins it to the span top (stacking down at line pitch), and
                // Bottom seats it on the span bottom.
                var effVA = cell.VerticalAlignment != VerticalAlignment.None ? cell.VerticalAlignment : row.VerticalAlignment;
                var offset = effVA == VerticalAlignment.Bottom
                    ? Math.Max(padTop, h - (padding?.Bottom ?? 0) - blockH)
                    : effVA == VerticalAlignment.Top
                        ? padTop
                        : Math.Max(padTop, (h - blockH) / 2);
                var gapAccum = 0.0;
                for (var li = 0; li < block.Lines.Count; li++)
                {
                    var line = block.Lines[li];
                    gapAccum += line.TopGap;
                    if (line.Text.Length == 0) continue;
                    var tw = line.KernedWidth > 0 ? line.KernedWidth
                        : MeasureWidth(line.Text, line.FontSize);
                    var lineX = line.Align == HorizontalAlignment.Center
                        ? x + Math.Max(padLeft, (w - tw) / 2)
                        : line.Align == HorizontalAlignment.Right
                            ? Math.Max(x + padLeft, x + w - padRight - tw)
                            : x + padLeft;
                    var lineBase = top - offset - gapAccum - li * block.LineHeight - line.FontSize;
                    // Embedded-serif span line (HonorCellFontFaces): draw through the
                    // Type0 path with kerned advances, like the grid-cell renderer.
                    if (line.Type0Ttf is not null && linkPage is not null)
                    {
                        var sbFontDict = ResolvePageFontDict(linkPage);
                        var (sbRes, sbHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                            sbFontDict, line.Type0Ttf, line.Type0FontName ?? "Arial", line.Text,
                            stripSpacesInBaseFont: true);
                        builder.BeginText();
                        builder.SetFont(sbRes, line.FontSize);
                        ApplyColor(builder, line.ForegroundColor);
                        builder.MoveTextPosition(lineX, lineBase);
                        if (line.KernTj && KernAdjustments(line.Text, line.Type0Ttf) is { } sbKern)
                            builder.ShowTextHexKerned(sbHex, sbKern);
                        else
                            builder.ShowTextHex(sbHex);
                        builder.EndText();
                        continue;
                    }
                    builder.BeginText();
                    builder.SetFont(line.Bold && linkPage is not null
                        ? RegisterFont(linkPage, "Helvetica-Bold") : fontName, line.FontSize);
                    ApplyColor(builder, line.ForegroundColor);
                    builder.MoveTextPosition(lineX, lineBase);
                    builder.ShowText(line.Text);
                    builder.EndText();
                }
            }
        }

        // Outer table.Border wraps the slices that landed on this page.
        // Drawn after slices so it sits on top of cell backgrounds/borders.
        if (Border is not null && slices.Count > 0)
        {
            var totalWidth = 0.0;
            foreach (var w in colWidths) totalWidth += w;
            var topY = slices[0].TopY;
            var bottomY = slices[^1].TopY - slices[^1].Height;
            // Stroke on the mid-line of a border that lies outside the column block:
            // half a width out from the content box on every side.
            var outerWidth = OuterBorderWidth();
            var half = outerWidth / 2;
            if (FormGridCells)
                DrawFormGridBorder(builder, Border, tableX - outerWidth, bottomY - outerWidth,
                    totalWidth + 2 * outerWidth, topY - bottomY + 2 * outerWidth);
            else
                DrawBorder(builder, Border, tableX - half, bottomY - half,
                    totalWidth + outerWidth, topY - bottomY + outerWidth);
        }
        builder.RestoreState();

        if (linkPage is not null && links is { Count: > 0 })
        {
            foreach (var (rect, link) in links)
            {
                if (link is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
                    // Record the target URL as the link's /Contents so it is
                    // recoverable from the annotation after a save/reload round-trip.
                    linkPage.Annotations.AddLinkAnnotation(rect, wh.Url).Contents = wh.Url;
                else if (link is LocalHyperlink lh && lh.TargetPageNumber > 0)
                    linkPage.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(lh.TargetPageNumber, 0, 0, 0)));
            }
        }

        // Radio-option widgets laid out in cells: place each option's widget at its
        // glyph rectangle and add it to the page /Annots so it round-trips as an
        // interactive control alongside the drawn glyph.
        if (linkPage is not null && optionSink is { Count: > 0 })
            foreach (var (opt, rect) in optionSink)
                opt.OwnerRadio?.PlaceOptionWidget(opt, linkPage, rect);

        // Checkbox widgets laid out in cells: move each widget to its glyph rectangle
        // (its /AP appearance draws the box and check at that position).
        if (linkPage is not null && checkboxSink is { Count: > 0 })
            foreach (var (cbf, rect) in checkboxSink)
                cbf.PlaceWidget(linkPage, rect);

        return builder.Build();
    }

    private void RenderRowSlice(ContentStreamBuilder builder, RowSlice slice,
        double[] colWidths, double tableX, string fontName, int[] cellMap,
        List<(Rectangle rect, Hyperlink link)>? links = null,
        List<(byte[] data, Rectangle rect)>? imageSink = null,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink = null,
        List<byte[]>? graphSink = null,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink = null,
        Page? page = null,
        List<(Note note, double x, double baseline, double size)>? footnoteSink = null)
    {
        var row = slice.Plan.Row;
        var defaultPad = row.DefaultCellPadding ?? DefaultCellPadding;
        var cellX = tableX;

        var gridToCell = slice.Plan.GridToCell;
        for (var col = 0; col < colWidths.Length; col++)
        {
            int origIdx;
            if (gridToCell is not null)
            {
                origIdx = col < gridToCell.Length ? gridToCell[col] : -1;
                if (origIdx == -2) continue;                       // own ColSpan cover — x already advanced
                if (origIdx < 0 || origIdx >= row.Cells.Count) { cellX += colWidths[col]; continue; }
            }
            else if (slice.Plan.ColToCell is { } colToCell)
            {
                origIdx = col < colToCell.Length ? colToCell[col] : -1;
                if (origIdx == -2) continue;                       // covered by an earlier cell's span
                if (origIdx < 0) { cellX += colWidths[col]; continue; }
            }
            else
            {
                origIdx = cellMap[col];
                if (origIdx >= row.Cells.Count) { cellX += colWidths[col]; continue; }
            }
            var cell = row.Cells.At(origIdx);
            // Clamp ColSpan to the slice's remaining columns so chunked rendering
            // doesn't read past the end of the slice's colWidths.
            var span = Math.Max(1, Math.Min(cell.ColSpan, colWidths.Length - col));
            var cellWidth = GetCellWidth(colWidths, col, span);
            // A row-spanning cell is drawn by the span-block pass (its rect covers
            // several rows); reserve its columns and move on.
            if (gridToCell is not null && slice.Plan.EffRowSpan is not null &&
                slice.Plan.EffRowSpan[origIdx] > 1)
            { cellX += cellWidth; continue; }
            var padding = EffectivePad(cell, row);
            var dp = DefaultPad(cell, row);
            var padLeft = padding?.Left ?? dp;
            var padTop = padding?.Top ?? 0;

            // Record the cell's laid-out rectangle (page space) for callers that
            // query Cell.Rect/Width after save. Union across slices when a row is
            // split across pages.
            cell.Width = cellWidth;
            var sliceRect = new Rectangle(cellX, slice.TopY - slice.Height, cellX + cellWidth, slice.TopY);
            cell.Rect = cell.Rect is null
                ? sliceRect
                : new Rectangle(
                    Math.Min(cell.Rect.LLX, sliceRect.LLX), Math.Min(cell.Rect.LLY, sliceRect.LLY),
                    Math.Max(cell.Rect.URX, sliceRect.URX), Math.Max(cell.Rect.URY, sliceRect.URY));

            // HTML cellspacing: the cell's border box insets half a spacing from the
            // row band on ALL sides — adjacent rows (and side-by-side cells) keep
            // the page visible between their borders.
            var bandInset = HtmlRowSpacingPt / 2;

            // Background — rounded when the cell's border is (the detail buttons).
            var bgColor = cell.BackgroundColor ?? row.BackgroundColor;
            if (bgColor is not null)
            {
                builder.SetFillColor(bgColor);
                var bgRadius = (cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder)
                    ?.RoundedBorderRadius ?? 0;
                if (bgRadius > 0)
                    FillRoundedRect(builder, cellX + bandInset,
                        slice.TopY - slice.Height + bandInset,
                        cellWidth - 2 * bandInset, slice.Height - 2 * bandInset, bgRadius);
                else
                {
                    builder.Rectangle(cellX + bandInset, slice.TopY - slice.Height + bandInset,
                        cellWidth - 2 * bandInset, slice.Height - 2 * bandInset);
                    builder.Fill();
                }
            }

            // Border
            if (!cell.IsNoBorder)
            {
                var cellBorder = cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
                // Form-grid cells stroke INSIDE their box, CSS-fashion: the stroke
                // centre sits half a width in from the cell edge, so two abutting
                // cells show a pair of lines one width apart (e.g. the
                // 185.45/186.20 doublet), not one shared line; each side runs the
                // box's full extent so the corners paint.
                if (cellBorder is not null && FormGridCells)
                    DrawFormGridBorder(builder, cellBorder, cellX + bandInset,
                        slice.TopY - slice.Height + bandInset,
                        cellWidth - 2 * bandInset, slice.Height - 2 * bandInset);
                else if (cellBorder is not null)
                    DrawBorder(builder, cellBorder, cellX + bandInset, slice.TopY - slice.Height + bandInset,
                        cellWidth - 2 * bandInset, slice.Height - 2 * bandInset);
            }

            // Text content — render the slice's line window for this cell. Inline cells carry only
            // blank placeholder lines here (their text is drawn by the inline pass below); skip them
            // so the line-based path doesn't emit empty show-text runs that read back as stray
            // (empty) text fragments.
            var cellIsInline = slice.Plan.CellInline?.ContainsKey(col) ?? false;
            var cellLines = col < slice.Plan.CellLines.Count ? slice.Plan.CellLines[col] : null;

            // Vertical alignment: when the slice is taller than the cell's content block
            // (e.g. a MinRowHeight-floored row), Center/Bottom shift the text down within
            // the cell. Top (the default) keeps the historical top-seated placement.
            var padBot = padding?.Bottom ?? 0;
            var effVA = cell.VerticalAlignment != VerticalAlignment.None ? cell.VerticalAlignment : row.VerticalAlignment;
            // Text sharing a row with a (taller) cell image is vertically centred
            // even under the default alignment.
            if (effVA is VerticalAlignment.Top or VerticalAlignment.None
                && slice.Plan.CellImages is { Count: > 0 } && !slice.Plan.CellImages.ContainsKey(col))
                effVA = VerticalAlignment.Center;
            // HTML-engine metrics: cells centre in their row by default (a short label
            // sharing a row with a tall HTML cell sits at the row's vertical middle).
            // The same holds under UA cell boxes and the lifted nested-table render,
            // where it is simply the `vertical-align: middle` a browser gives every
            // cell (a chain rule's explicit `vertical-align: top` was set on the cell
            // and skips this default).
            if ((HtmlEngineMetrics || UaCellBoxes)
                && effVA is VerticalAlignment.Top or VerticalAlignment.None)
                effVA = VerticalAlignment.Center;
            else if ((NestedTableRender || FormGridCells) && effVA is VerticalAlignment.None)
                effVA = VerticalAlignment.Center;
            // CSS line-box cell (mixed per-line font sizes): lines stack by their own box
            // heights with ascent-based baselines. Falls back to the uniform grid when the
            // row is sliced across pages (LineStart > 0).
            var cssCell = slice.LineStart == 0 && slice.Plan.CssCells?.Contains(col) == true;
            // A cell border's stroke width insets the content from the border
            // edges (a 5 pt border seats text 5 pt further in/down,
            // including the per-side GraphInfo border case).
            var insetBorder = cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
            var borderInsetLeft = 0.0;
            var borderInsetTop = 0.0;
            if (insetBorder is not null)
            {
                if (insetBorder.Side.HasFlag(BorderSide.Left) || insetBorder.LeftAssigned)
                    borderInsetLeft = insetBorder.RawLeft?.LineWidth > 0 ? insetBorder.RawLeft.LineWidth : insetBorder.Width;
                if (insetBorder.Side.HasFlag(BorderSide.Top) || insetBorder.TopAssigned)
                    borderInsetTop = insetBorder.RawTop?.LineWidth > 0 ? insetBorder.RawTop.LineWidth : insetBorder.Width;
            }
            var vaOffset = 0.0;
            if (!cssCell && (effVA is VerticalAlignment.Center or VerticalAlignment.Bottom) && cellLines is { Count: > 0 } && slice.LineCount > 0)
            {
                var visLines = Math.Max(0, Math.Min(slice.LineCount, cellLines.Count - slice.LineStart));
                // A FixedRowHeight row centres on the cell's FULL content height —
                // an overflowing cell clamps to the top pad (its excess lines clip
                // at the row bottom) while a fitting neighbour centres normally.
                // Other rows centre the lines the slice actually shows.
                var countForVa = slice.Plan.Row.FixedRowHeight > 0
                    ? Math.Max(0, cellLines.Count - slice.LineStart)
                    : visLines;
                // The content block stacks at each line's OWN height (pitch =
                // font size; a checkbox line is its box height).
                double blockH = 0;
                for (var bi = slice.LineStart; bi < slice.LineStart + countForVa && bi < cellLines.Count; bi++)
                    blockH += cellLines[bi].Checkbox is { Height: > 0 } vcb ? vcb.Height
                        // UA cell boxes: a line occupies its LINE BOX, not its glyph
                        // height — measuring the block at the bare font size would
                        // leave a full-height cell looking short and nudge it down.
                        // The lifted nested-table render prices its slices AND walks
                        // its draw at the uniform line box too, so the centering must
                        // measure with the same ruler — at bare font size every line
                        // fabricates (lineBox − fontSize) of slack and a tall cell
                        // sinks by half of it. A reserve line's box is its own
                        // FontSize (a share of the nested grid's real height).
                        : NestedTableRender && cellLines[bi].ImgReserve && cellLines[bi].FontSize > 0
                            ? cellLines[bi].FontSize
                        : UaCellBoxes || NestedTableRender ? slice.Plan.LineHeight
                        : cellLines[bi].FontSize;
                var avail = slice.Height - padTop - padBot - borderInsetTop;
                if (avail > blockH)
                    vaOffset = effVA == VerticalAlignment.Center ? (avail - blockH) / 2 : (avail - blockH);
            }
            // A bordered cell's content starts at the border's inner edge with no
            // implicit horizontal padding ("x" seats 5 pt in from a
            // 5 pt border, not 5+2); explicit padding still applies.
            if (borderInsetLeft > 0 && padding?.Left is null)
                padLeft = 0;
            padLeft += borderInsetLeft;
            // Text seats at the CELL's own effective top padding (EffectivePad —
            // zero margin components fall back to the default padding, so a
            // Margin(-25,0,0,0) cell still aligns with (0,5,0,5)-padded
            // neighbours while a Margin(0,8,0,3)/(0,8,0,0) pair keeps its
            // explicit 3/0 tops).
            var contentTop = padTop + vaOffset + borderInsetTop;
            // Every cell baseline sits a descender ABOVE the full-em drop
            // (box-bottom-minus-descender; 0.207 = the Helvetica AFM descender).
            const double borderLiftFactor = 0.207;

            if (!cellIsInline && cellLines is { Count: > 0 } && slice.LineCount > 0)
            {
                var firstLine = slice.LineStart;
                var lastLine = Math.Min(firstLine + slice.LineCount, cellLines.Count);
                if (firstLine < lastLine)
                {
                    var hasOption = false;
                    var anyNonLeft = false;
                    var anyType0 = false;
                    var anyBoxes = false;
                    var anyLinks = false;
                    for (var li = firstLine; li < lastLine; li++)
                    {
                        var cl = cellLines[li];
                        if (cl.Option is not null || cl.Checkbox is not null
                            || cl.InlineOptions is not null
                            || cl.Text.IndexOf(InlineButtonChar) >= 0) { hasOption = true; break; }
                        if (cl.Align is HorizontalAlignment.Center or HorizontalAlignment.Right) anyNonLeft = true;
                        if (cl.Type0Ttf is not null) anyType0 = true;
                        if (cl.Boxes is { Count: > 0 }) anyBoxes = true;
                        // Linked lines draw per-line so the anchor runs can take the
                        // link blue + underline styling (lifted dialect only —
                        // the legacy stream stays byte-stable).
                        if (NestedTableRender
                            && (cl.LinkRuns is { Count: > 0 } || cl.Hyperlink is not null)) anyLinks = true;
                    }

                    if (hasOption)
                    {
                        // Form-control lines need path drawing (the glyph) interleaved with
                        // text, which a single text object can't hold — render line by line.
                        RenderControlLines(builder, cellLines, firstLine, lastLine,
                            cellX + padLeft, slice.TopY - contentTop, slice.Plan.LineHeight, fontName, optionSink, checkboxSink,
                            slice.TopY - slice.Height + (padding?.Bottom ?? 0));
                    }
                    else if (anyNonLeft || anyType0 || cssCell || anyBoxes || anyLinks)
                    {
                        // The cell mixes alignments (e.g. a centred title above a left HtmlFragment)
                        // or carries an embedded-font (Arabic/Type0) line, an inline-box
                        // decoration, so each line is positioned
                        // absolutely. Lines can differ in width, hence per-line placement.
                        var padRight = padding?.Right ?? dp;
                        var cssCum = 0.0;
                        // vertical-align: middle for a css-box stack — the whole stack
                        // shifts down by half the cell's slack (the uniform path does
                        // this through vaOffset; the css stack owns its own cursor).
                        if (cssCell && effVA == VerticalAlignment.Center)
                        {
                            double cssTotal = 0;
                            var anyPlate = false;
                            for (var li2 = firstLine; li2 < lastLine; li2++)
                            {
                                var cl2 = cellLines[li2];
                                cssTotal += cl2.BoxH > 0 ? cl2.BoxH : slice.Plan.LineHeight;
                                if (cl2.Boxes is { } bxs)
                                    foreach (var b7 in bxs)
                                        if (b7.Height > 0) anyPlate = true;
                            }
                            // Form-grid cells centre within their BORDER-inset content
                            // band — the cell border pair is not distributable slack.
                            var availV = slice.Height - padTop - padBot
                                - (FormGridCells ? 2 * borderInsetTop : 0);
                            // cssTop subtracts the cursor, so a POSITIVE seed moves
                            // the stack down into the slack. A stack holding a
                            // declared-height plate is near-exact — split only its
                            // small residual tail (clamped), the outer band does the
                            // real centring.
                            if (availV > cssTotal && !anyPlate)
                                cssCum = (availV - cssTotal) / 2;
                        }
                        for (var li = firstLine; li < lastLine; li++)
                        {
                            var line = cellLines[li];
                            // A css-box line advances the stack by its OWN box height —
                            // including deliberate blank lines, which occupy space silently.
                            var cssTop = slice.TopY - contentTop - cssCum;
                            if (cssCell && line.BoxH > 0) cssCum += line.BoxH;
                            // A blank line still draws its inline boxes (the
                            // standalone badge circles).
                            if (line.Text.Length == 0 && line.Boxes is null) continue;
                            var w = line.KernedWidth > 0 ? line.KernedWidth
                                : MeasureWidth(line.Text, line.FontSize);
                            // A right-anchored BOX line (the overall-signal pill) keeps the
                            // UA cell pad off the border box, the same white the full-width
                            // bars below reserve — the pill must never sit flush against
                            // the frame.
                            var boxRightPad = line.Align == HorizontalAlignment.Right
                                && line.Boxes is { Count: > 0 } ? UaCellBoxPadPt + bandInset : 0;
                            var lineX = line.Align == HorizontalAlignment.Center
                                ? cellX + Math.Max(padLeft, (cellWidth - w) / 2)
                                : line.Align == HorizontalAlignment.Right
                                    ? Math.Max(cellX + padLeft, cellX + cellWidth - padRight - w - boxRightPad)
                                    : cellX + padLeft + line.LeftIndent;
                            var lineTop = cssCell && line.BoxH > 0
                                ? cssTop
                                : slice.TopY - contentTop - (li - firstLine) * slice.Plan.LineHeight;
                            // Baseline: ascent + half-leading below the box top for css-box
                            // lines; the legacy full-em drop otherwise, lifted a descender
                            // for explicitly-bordered cells (see borderLiftFactor).
                            var lineBase = lineTop - (cssCell && line.BaseOff > 0
                                ? line.BaseOff
                                : line.FontSize - (SuppressBaselineLift ? 0 : borderLiftFactor * line.FontSize));
                            // Inline boxes behind this line (title plates, status pills):
                            // rounded fill, the trailing traffic-light circle with its
                            // letter, and each box's OWN positioned text run. When the
                            // boxes carry text they own the whole line's ink.
                            if (line.Boxes is { Count: > 0 } lineBoxes)
                            {
                                var lineBoxH = line.BoxH > 0 ? line.BoxH
                                    : Math.Max(slice.Plan.LineHeight, line.FontSize * 1.2);
                                var boxesCarryText = false;
                                // Boxes stay where the pen put them — LEFT-anchored
                                // (the user-settled model: any column slack belongs to
                                // the nested grid's column, never to a shift of the
                                // plates; the old right-pack fought the column surplus
                                // and kept resurrecting a left gap).
                                const double packShift = 0.0;
                                foreach (var ib in lineBoxes)
                                {
                                    var ibStretch = 0.0;
                                    var bx = lineX + ib.XOff + packShift;
                                    var ibW = ib.Width;
                                    // A block-level bar spans the cell's BORDER BOX
                                    // minus the UA pad — a sibling div's padding must
                                    // not inset it (the bars run border to
                                    // border with ~1 pt of white).
                                    if (ib.FullWidth)
                                    {
                                        bx = cellX + bandInset + 0.75;
                                        ibW = Math.Max(ib.Width, cellWidth - 2 * bandInset - 1.5);
                                    }
                                    var drawH = ib.Height > 0 ? ib.Height : lineBoxH - 2 * ib.InsetV;
                                    // A declared-height plate seats a full inset pair
                                    // lower — its stack keeps 2·InsetV of breathing and
                                    // the rect centres in it. A FULL-WIDTH bar opening
                                    // the cell anchors at the BORDER BOX top (+UA pad):
                                    // a padded sibling div must not displace it.
                                    var ibTopOff = ib.Height > 0 ? 2 * ib.InsetV : ib.InsetV;
                                    var drawTop = ib.FullWidth && li == firstLine
                                        ? slice.TopY - bandInset - 0.75
                                        : lineTop - ibTopOff;
                                    if (ib.Fill is { } boxFill)
                                    {
                                        builder.SetFillColor(boxFill.R / 255.0, boxFill.G / 255.0, boxFill.B / 255.0);
                                        FillRoundedRect(builder, bx, drawTop - drawH, ibW, drawH, ib.Radius);
                                    }
                                    if (ib.CircleFill is { } circleFill)
                                    {
                                        var dD = ib.CircleD > 0 ? ib.CircleD : 14.25;
                                        var ccx = bx + ibW - ib.PadRight - dD / 2;
                                        var ccy = drawTop - drawH / 2;
                                        builder.SetFillColor(circleFill.R / 255.0, circleFill.G / 255.0, circleFill.B / 255.0);
                                        FillCircle(builder, ccx, ccy, dD / 2);
                                        if (ib.CircleLetter is { Length: > 0 } circleLetter && page is not null)
                                        {
                                            var lw = MeasureWidth(circleLetter, 9);
                                            builder.BeginText();
                                            builder.SetFont(RegisterFont(page, "Helvetica-Bold"), 9);
                                            ApplyColor(builder, ib.CircleLetterColor ?? Color.White);
                                            builder.MoveTextPosition(ccx - lw / 2, ccy - 3.1);
                                            builder.ShowText(circleLetter);
                                            builder.EndText();
                                        }
                                    }
                                    if (ib.Text is { Length: > 0 } boxText)
                                    {
                                        boxesCarryText = true;
                                        var btSize = ib.TextSize > 0 ? ib.TextSize : line.FontSize;
                                        var btX = ib.TextCentered
                                            ? bx + Math.Max(0, (ibW - MeasureWidth(boxText, btSize)) / 2)
                                            : lineX + ib.TextX + packShift + ibStretch / 2;
                                        builder.BeginText();
                                        builder.SetFont(ib.TextBold && page is not null
                                            ? RegisterFont(page, "Helvetica-Bold") : fontName, btSize);
                                        if (ib.TextLetterSpacing > 0)
                                            builder.SetCharSpacing(ib.TextLetterSpacing);
                                        ApplyColor(builder, ib.TextColor ?? line.ForegroundColor);
                                        builder.MoveTextPosition(btX,
                                            drawTop - ib.PadTop - btSize);
                                        builder.ShowText(boxText);
                                        if (ib.TextLetterSpacing > 0)
                                            builder.SetCharSpacing(0);
                                        builder.EndText();
                                    }
                                }
                                if (boxesCarryText) continue;
                            }
                            // HTML-engine line: styled serif runs at per-run x-offsets on
                            // the common baseline (mixed bold/small within one line).
                            if (line.Runs is { Count: > 0 } htmlRuns && page is not null)
                            {
                                var runFontDict = ResolvePageFontDict(page);
                                foreach (var run in htmlRuns)
                                {
                                    if (run.Text.Length == 0) continue;
                                    var runTtf = run.Bold ? _serifBoldTtf : _serifTtf;
                                    if (runTtf is null) continue;
                                    var (runRes, runHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                        runFontDict, runTtf,
                                        run.Bold ? "Times New Roman Bold" : "Times New Roman",
                                        run.Text, stripSpacesInBaseFont: true);
                                    builder.BeginText();
                                    builder.SetFont(runRes, run.Size);
                                    ApplyColor(builder, line.ForegroundColor);
                                    builder.MoveTextPosition(lineX + run.X, lineBase);
                                    if (line.KernTj && KernAdjustments(run.Text, runTtf) is { } runKern)
                                        builder.ShowTextHexKerned(runHex, runKern);
                                    else
                                        builder.ShowTextHex(runHex);
                                    builder.EndText();
                                }
                                // An <a> run inside the engine line annotates over its own
                                // glyphs, on the same baseline the runs drew at.
                                if (links is not null && line.LinkRuns is { Count: > 0 })
                                    foreach (var (xo, rw, rlink) in line.LinkRuns)
                                        links.Add((new Rectangle(lineX + xo, lineBase,
                                            lineX + xo + rw, lineTop), rlink));
                                continue;
                            }
                            // Embedded-font line (Arabic/Unicode): embed the TrueType as a Type0/CID
                            // font on this page and draw the shaped glyphs as hex glyph IDs.
                            if (line.Type0Ttf is not null && page is not null)
                            {
                                var fontDict = ResolvePageFontDict(page);
                                // Mixed-style form-grid line: each run in its own face,
                                // advancing by its kerned width.
                                if (line.StyleRuns is { Count: > 0 })
                                {
                                    var runX = lineX;
                                    foreach (var (runText, runTtf, runName) in line.StyleRuns)
                                    {
                                        var (runRes, runHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                            fontDict, runTtf, runName, runText,
                                            stripSpacesInBaseFont: true);
                                        builder.BeginText();
                                        builder.SetFont(runRes, line.FontSize);
                                        ApplyColor(builder, line.ForegroundColor);
                                        builder.MoveTextPosition(runX, lineBase);
                                        if (line.KernTj && KernAdjustments(runText, runTtf) is { } runKern)
                                            builder.ShowTextHexKerned(runHex, runKern);
                                        else
                                            builder.ShowTextHex(runHex);
                                        builder.EndText();
                                        runX += MeasureWidthKerned(runText, line.FontSize, runTtf);
                                    }
                                    continue;
                                }
                                if (line.Type0SplitTokens)
                                {
                                    // Space-separated CJK: emit each token as its own positioned
                                    // Type0 run so the absorber surfaces per-token fragments. Embed
                                    // the font ONCE for the whole line and slice the returned hex per
                                    // token — embedding per token would duplicate the full font
                                    // program for every glyph (pathological on large documents).
                                    var (rn, fullHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                        fontDict, line.Type0Ttf, line.Type0FontName ?? "Arial", line.Text,
                                        stripSpacesInBaseFont: true);
                                    var tokX = lineX;
                                    var spaceW = MeasureWidthWithFont(" ", line.FontSize, line.Type0Ttf);
                                    var charIdx = 0; // char index into line.Text (2 hex bytes per char)
                                    foreach (var token in line.Text.Split(' '))
                                    {
                                        if (token.Length > 0 && (charIdx + token.Length) * 2 <= fullHex.Length)
                                        {
                                            var tokHex = new byte[token.Length * 2];
                                            System.Array.Copy(fullHex, charIdx * 2, tokHex, 0, tokHex.Length);
                                            builder.BeginText();
                                            builder.SetFont(rn, line.FontSize);
                                            ApplyColor(builder, line.ForegroundColor);
                                            builder.MoveTextPosition(tokX, lineBase);
                                            builder.ShowTextHex(tokHex);
                                            builder.EndText();
                                            tokX += MeasureWidthWithFont(token, line.FontSize, line.Type0Ttf);
                                        }
                                        charIdx += token.Length + 1; // token chars + the space separator
                                        tokX += spaceW;
                                    }
                                    continue;
                                }
                                var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                    fontDict, line.Type0Ttf, line.Type0FontName ?? "Arial", line.Text,
                                    stripSpacesInBaseFont: true);
                                builder.BeginText();
                                builder.SetFont(resName, line.FontSize);
                                ApplyColor(builder, line.ForegroundColor);
                                builder.MoveTextPosition(lineX, lineBase);
                                // Bold-serif HTML lines are kerned;
                                // all other Type0 lines (shaped Arabic, CJK) stay unkerned.
                                if (line.KernTj && KernAdjustments(line.Text, line.Type0Ttf) is { } kernAdj)
                                    builder.ShowTextHexKerned(hex, kernAdj);
                                else
                                    builder.ShowTextHex(hex);
                                builder.EndText();
                                continue;
                            }
                            var plResolvedFont = line.Bold && page is not null
                                ? RegisterFont(page, "Helvetica-Bold") : fontName;
                            if (NestedTableRender
                                && (line.LinkRuns is { Count: > 0 } || line.Hyperlink is not null))
                                ShowLineWithLinks(builder, line, plResolvedFont, lineX, lineBase);
                            else
                            {
                                builder.BeginText();
                                builder.SetFont(plResolvedFont, line.FontSize);
                                ApplyColor(builder, line.ForegroundColor);
                                builder.MoveTextPosition(lineX, lineBase);
                                builder.ShowText(line.Text);
                                builder.EndText();
                            }
                            if (links is not null && line.Hyperlink is not null)
                                links.Add((new Rectangle(lineX, lineBase, lineX + w, lineTop), line.Hyperlink));
                            // Inline <a> runs: annotate each hyperlinked glyph run
                            // at its pre-measured offset within the line.
                            if (links is not null && line.LinkRuns is { Count: > 0 })
                                foreach (var (xo, rw, rlink) in line.LinkRuns)
                                    links.Add((new Rectangle(lineX + xo, lineBase,
                                        lineX + xo + rw, lineTop), rlink));
                            if (footnoteSink is not null && line.FootNote is not null)
                                footnoteSink.Add((line.FootNote,
                                    lineX + MeasureWidthExact(line.Text, line.FontSize),
                                    lineBase, line.FontSize));
                        }
                    }
                    else
                    {
                        var first = cellLines[firstLine];
                        var textX = cellX + padLeft;
                        // HTML-engine metrics: the baseline sits a descender ABOVE the
                        // block bottom (box = n·size), i.e. lifted by 0.207·size vs the
                        // legacy full-em drop (0.207 = the Helvetica AFM descender).
                        // Explicitly-bordered cells get the same lift.
                        var engineLift = SuppressBaselineLift
                            ? 0
                            : (HtmlEngineMetrics ? 0.207 : borderLiftFactor) * first.FontSize;
                        var textY = slice.TopY - contentTop - first.FontSize + engineLift;

                        string FontFor(CellLine l) => l.Bold && page is not null
                            ? RegisterFont(page, "Helvetica-Bold") : fontName;
                        // A truly empty <td> collapses in a browser and shows NO text —
                        // unlike an &nbsp; cell, whose text is U+00A0 and survives this
                        // test. Emitting the empty run anyway reads back as a spurious
                        // text fragment, shifting every fragment
                        // index after it. Lifted render only; the legacy dialects round
                        // trip their leading empty segment through it.
                        var cellHasInk = !NestedTableRender;
                        for (var li = firstLine; li < lastLine && !cellHasInk; li++)
                            if (cellLines[li].Text.Length > 0) cellHasInk = true;
                        if (cellHasInk)
                        {
                            // The lifted dialect keeps deliberate blank line boxes as
                            // vertical space; they must not EMIT — an empty Tj reads
                            // back as an empty text fragment and shifts every absorber
                            // index after it. Legacy keeps its byte-exact stream
                            // (a leading empty run round-trips through it).
                            var fi = firstLine;
                            var leadDy = 0.0;
                            if (NestedTableRender)
                                while (fi < lastLine && cellLines[fi].Text.Length == 0)
                                {
                                    // A reserve line's box is its own FontSize (a share
                                    // of the nested grid's real height), not the pitch.
                                    leadDy += cellLines[fi].ImgReserve && cellLines[fi].FontSize > 0
                                        ? cellLines[fi].FontSize : slice.Plan.LineHeight;
                                    fi++;
                                }
                            var firstInk = fi < lastLine ? cellLines[fi] : first;
                            builder.BeginText();
                            builder.SetFont(FontFor(firstInk), firstInk.FontSize);
                            ApplyColor(builder, firstInk.ForegroundColor);
                            builder.MoveTextPosition(textX + firstInk.LeftIndent, textY - leadDy);
                            builder.ShowText(firstInk.Text);

                            var lastFontSize = firstInk.FontSize;
                            var lastBold = firstInk.Bold;
                            var lastIndent = firstInk.LeftIndent;
                            var skippedDy = 0.0;
                            for (var li = fi + 1; li < lastLine; li++)
                            {
                                var line = cellLines[li];
                                if (NestedTableRender && line.Text.Length == 0)
                                {
                                    skippedDy += line.ImgReserve && line.FontSize > 0
                                        ? line.FontSize : slice.Plan.LineHeight;
                                    continue;
                                }
                                if (line.FontSize != lastFontSize || line.Bold != lastBold)
                                {
                                    builder.SetFont(FontFor(line), line.FontSize);
                                    lastFontSize = line.FontSize;
                                    lastBold = line.Bold;
                                }
                                ApplyColor(builder, line.ForegroundColor);
                                builder.MoveTextPosition(line.LeftIndent - lastIndent,
                                    -slice.Plan.LineHeight - skippedDy);
                                skippedDy = 0;
                                lastIndent = line.LeftIndent;
                                builder.ShowText(line.Text);
                            }
                            builder.EndText();
                        }

                        // Collect link annotations over hyperlinked lines (page-space rects).
                        if (links is not null)
                        {
                            for (var li = firstLine; li < lastLine; li++)
                            {
                                var line = cellLines[li];
                                if (line.Text.Length == 0) continue;
                                var lineTop = slice.TopY - contentTop - (li - firstLine) * slice.Plan.LineHeight;
                                var lineBottom = lineTop - line.FontSize;
                                if (line.Hyperlink is not null)
                                {
                                    var w = MeasureWidth(line.Text, line.FontSize);
                                    links.Add((new Rectangle(textX, lineBottom, textX + w, lineTop), line.Hyperlink));
                                }
                                // Inline <a> runs: annotate each hyperlinked glyph run
                                // at its pre-measured offset within the line.
                                if (line.LinkRuns is { Count: > 0 })
                                    foreach (var (xo, rw, rlink) in line.LinkRuns)
                                        links.Add((new Rectangle(textX + xo, lineBottom,
                                            textX + xo + rw, lineTop), rlink));
                            }
                        }
                        if (footnoteSink is not null)
                        {
                            for (var li = firstLine; li < lastLine; li++)
                            {
                                var line = cellLines[li];
                                if (line.FootNote is null || line.Text.Length == 0) continue;
                                var lb = slice.TopY - contentTop
                                         - (li - firstLine) * slice.Plan.LineHeight
                                         - line.FontSize + engineLift;
                                footnoteSink.Add((line.FootNote,
                                    textX + MeasureWidthExact(line.Text, line.FontSize),
                                    lb, line.FontSize));
                            }
                        }
                    }
                }
            }

            // Image content — recorded once, at the row's top slice, for the caller to blit
            // onto the materialised page (overflow pages don't exist yet during the build).
            // The image is collected as a page-space rect; the cell border drawn above into
            // builder frames it once both content streams land on the page.
            if (imageSink is not null
                && slice.Plan.CellImages is { } imgs && imgs.TryGetValue(col, out var colImgs))
                foreach (var ci in colImgs)
                {
                    // Each image belongs to the slice holding ITS line, and is seated from
                    // THAT slice's top — an absolute line offset would push a continuation
                    // slice's image off the bottom of the page it was never drawn on.
                    // Identity on an unsplit row: LineStart is 0 and every line is in range.
                    if (ci.LineOffset < slice.LineStart
                        || ci.LineOffset >= slice.LineStart + slice.LineCount) continue;
                    var rel = ci.LineOffset - slice.LineStart;
                    var padRight = padding?.Right ?? dp;
                    var imgX = cellX + padLeft + ci.XOffset;
                    if (ci.Align == HorizontalAlignment.Center)
                        imgX = cellX + Math.Max(0, (cellWidth - ci.Width) / 2);
                    else if (ci.Align == HorizontalAlignment.Right)
                        imgX = cellX + Math.Max(0, cellWidth - padRight - ci.Width);
                    // Cell VerticalAlignment centres/bottoms the image within the row's
                    // content band (the row is usually taller than the image because its
                    // height is reserved in whole text lines). With SEVERAL images stacked
                    // in one cell there is no single band to centre in, so they sit where
                    // their lines put them.
                    var imgVaOffset = 0.0;
                    if (colImgs.Count == 1 && effVA is VerticalAlignment.Center or VerticalAlignment.Bottom)
                    {
                        var availH = slice.Height - padTop - padBot - rel * slice.Plan.LineHeight;
                        if (availH > ci.Height)
                            imgVaOffset = effVA == VerticalAlignment.Center
                                ? (availH - ci.Height) / 2
                                : availH - ci.Height;
                    }
                    // Seat the image below any text lines that precede it in the cell (e.g. a
                    // title line above a centred logo) rather than at the cell top.
                    var imgTopY = slice.TopY - padTop - rel * slice.Plan.LineHeight - imgVaOffset;
                    imageSink.Add((ci.Data, new Rectangle(imgX, imgTopY - ci.Height, imgX + ci.Width, imgTopY)));
                }

            // Nested tables — each inner grid renders in place on the slice holding its
            // reserved lines (the cell-image window precedent), as its own page-space
            // content stream via graphSink so the outer table's stream stays untouched.
            if (!_measureOnly && _buildPage is not null
                && slice.Plan.CellTables is { } ctabs && ctabs.TryGetValue(col, out var colTabs))
                foreach (var ct in colTabs)
                {
                    // The reserve may span several slices (the host row splits at its
                    // reserve-line boundaries); this slice participates when their
                    // line windows overlap.
                    if (ct.LineOffset >= slice.LineStart + slice.LineCount
                        || ct.LineOffset + ct.LineCount <= slice.LineStart) continue;
                    var innerT = ct.Table;
                    if (ct.Slices is null)
                    {
                        // First covering slice: build the grid ONCE against the same
                        // page bounds as this host build, so it breaks where the page
                        // really ends and its continuation slices are positioned for
                        // the same fresh-page top the host's own continuation uses.
                        var ctRel = ct.LineOffset - slice.LineStart;
                        // Lines preceding the reserve advance by the same ruler the
                        // draw walk uses: an earlier grid's reserve lines at their
                        // own FontSize, everything else at the uniform pitch.
                        var ctLead = 0.0;
                        for (var pli = slice.LineStart;
                             pli < ct.LineOffset && cellLines is not null && pli < cellLines.Count; pli++)
                            ctLead += cellLines[pli].ImgReserve && cellLines[pli].FontSize > 0
                                ? cellLines[pli].FontSize : slice.Plan.LineHeight;
                        var tabTopY = slice.TopY - padTop - ctLead;
                        // vertical-align: middle — a nested grid shorter than its row
                        // centres in the cell band (the cell-image precedent). Only
                        // when the reserve is the cell's LAST content: interleaved
                        // lines after the grid occupy the rest of the slice, so the
                        // band available to the grid is its own reserve window.
                        if (effVA == VerticalAlignment.Center && colTabs.Count == 1
                            && (cellLines is null || ct.LineOffset + ct.LineCount >= cellLines.Count))
                        {
                            var ctAvail = slice.Height - padTop - padBot - ctRel * slice.Plan.LineHeight;
                            if (ctAvail > ct.HeightPt) tabTopY -= (ctAvail - ct.HeightPt) / 2;
                        }
                        // The reserved box holds the capsule's wrapper; the grid itself
                        // starts one outset inside it on both axes.
                        tabTopY -= innerT.HtmlCapsuleOutsetVPt + innerT.HtmlMarginTopPt;
                        innerT.FlowLeftOffset = cellX + padLeft + innerT.HtmlCapsuleOutsetHPt
                            + innerT.HtmlListIndentPt;
                        try
                        {
                            ct.Slices = innerT.BuildMultiPage(_buildPage, tabTopY,
                                _curPageBottom, _curFreshTopMargin);
                        }
                        catch { ct.Slices = new List<byte[]>(); }
                        ct.Consumed = 0;
                    }
                    if (ct.Slices.Count > ct.Consumed)
                    {
                        // Splice the grid's next page slice into THIS stream, right
                        // where its cell draws, instead of appending it to the page as
                        // a separate stream: the page is then laid out in document
                        // order for anything that reads the operators back (a text
                        // absorber walks streams in order, and a whole grid arriving
                        // early shifts every fragment index after it).
                        var s = ct.Consumed++;
                        builder.AppendStream(ct.Slices[s]);
                        if (graphSink is not null && innerT.LastGraphDraws.Count > s)
                            foreach (var ig in innerT.LastGraphDraws[s]) graphSink.Add(ig);
                        if (imageSink is not null && innerT.LastImageDraws.Count > s)
                            foreach (var im in innerT.LastImageDraws[s]) imageSink.Add(im);
                    }
                }

            // Inline graph/text content (legend swatches, bar graphs): drawn once at the
            // cell top on the first slice. Text is shown in the table stream; each graph
            // is emitted as its own page-space content stream via graphSink.
            if (slice.LineStart == 0 &&
                slice.Plan.CellInline is { } inlineMap && inlineMap.TryGetValue(col, out var inlineRows))
            {
                for (var ri = 0; ri < inlineRows.Count; ri++)
                {
                    var lineTop = slice.TopY - padTop - ri * slice.Plan.LineHeight;
                    foreach (var item in inlineRows[ri])
                    {
                        var ix = cellX + padLeft + item.X;
                        if (item.ImageData is { } inlineImgData)
                            imageSink?.Add((inlineImgData,
                                new Rectangle(ix, lineTop - item.Height, ix + item.Width, lineTop)));
                        else if (item.Graph is { } g)
                            graphSink?.Add(g.Build(null, ix, lineTop - slice.Plan.LineHeight));
                        else if (item.Empty)
                        {
                            // Emit an explicit empty run so the generator's leading empty
                            // segment round-trips as an (empty) text fragment.
                            builder.BeginText();
                            builder.SetFont(fontName, item.FontSize);
                            builder.MoveTextPosition(ix, lineTop - item.FontSize);
                            builder.ShowText("");
                            builder.EndText();
                        }
                        else if (item.Text is { Length: > 0 } t)
                        {
                            // Seat the run on the line baseline (the pre-shrink size), then apply any
                            // sub/superscript shift — so a reduced-size super/subscript glyph still
                            // hangs off the common baseline rather than its own smaller box.
                            var baseSize = item.BaseFontSize > 0 ? item.BaseFontSize : item.FontSize;
                            // Per-run embedded font (e.g. NotoSans / NotoSansArabic): embed it as a
                            // Type0/CID font and emit the run as hex glyph IDs.
                            if (item.Ttf is not null && page is not null)
                            {
                                var fontDict = ResolvePageFontDict(page);
                                var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                    fontDict, item.Ttf, item.FontName ?? "Font", t, stripSpacesInBaseFont: true);
                                builder.BeginText();
                                builder.SetFont(resName, item.FontSize);
                                ApplyColor(builder, item.Color);
                                builder.MoveTextPosition(ix, lineTop - baseSize + item.BaselineShift);
                                builder.ShowTextHex(hex);
                                builder.EndText();
                                continue;
                            }
                            builder.BeginText();
                            builder.SetFont(fontName, item.FontSize);
                            ApplyColor(builder, item.Color);
                            builder.MoveTextPosition(ix, lineTop - baseSize + item.BaselineShift);
                            builder.ShowText(t);
                            builder.EndText();
                        }
                    }
                }
            }
            cellX += cellWidth;
        }
    }

    // Anchor styling: link text draws pure
    // blue (#0000FF) with a 1.2 pt blue underline 1.24 pt below the baseline,
    // one bar per word (the bars break at the word gaps). Values scale with the
    // line's font size from the 12 pt base.
    private const double LinkUnderlineDropPt = 1.24;

    private const double LinkUnderlineWPt = 1.2;

    private const double LinkProbeBasePt = 12.0;

    /// <summary>Draw a line whose text carries hyperlink runs: non-link segments in
    /// the line's own colour, each anchor run in link blue with per-word underlines.
    /// Segment boundaries are recovered from the runs' pre-measured x-offsets by
    /// accumulating glyph advances.</summary>
    private void ShowLineWithLinks(ContentStreamBuilder builder, CellLine line,
        string resolvedFont, double lineX, double lineBase)
    {
        var text = line.Text;
        var fs = line.FontSize;
        var lScale = fs / LinkProbeBasePt;
        var runs = new List<(double XOff, double W)>();
        if (line.LinkRuns is { Count: > 0 })
            foreach (var (xo, rw, _) in line.LinkRuns) runs.Add((xo, rw));
        else if (line.Hyperlink is not null) runs.Add((0, MeasureWidth(text, fs)));
        runs.Sort((a, b) => a.XOff.CompareTo(b.XOff));

        void ShowSeg(string seg, double atX, bool blue)
        {
            if (seg.Length == 0) return;
            builder.BeginText();
            builder.SetFont(resolvedFont, fs);
            if (blue) builder.SetFillColor(0, 0, 1);
            else ApplyColor(builder, line.ForegroundColor);
            builder.MoveTextPosition(lineX + atX, lineBase);
            builder.ShowText(seg);
            builder.EndText();
            if (!blue) return;
            // Per-word underline bars.
            builder.SetStrokeColor(0, 0, 1);
            builder.SetLineWidth(LinkUnderlineWPt * lScale);
            var wx = atX;
            var wi = 0;
            while (wi < seg.Length)
            {
                if (seg[wi] == ' ') { wx += MeasureWidth(" ", fs); wi++; continue; }
                var we = wi;
                while (we < seg.Length && seg[we] != ' ') we++;
                var wordW = MeasureWidth(seg[wi..we], fs);
                var uy = lineBase - LinkUnderlineDropPt * lScale;
                builder.MoveTo(lineX + wx, uy).LineTo(lineX + wx + wordW, uy).Stroke();
                wx += wordW;
                wi = we;
            }
        }

        var ci = 0;
        var cum = 0.0;
        foreach (var (xo, rw) in runs)
        {
            var segStart = ci;
            var segX = cum;
            while (ci < text.Length
                   && cum + MeasureWidth(text[ci].ToString(), fs) / 2 < xo)
            { cum += MeasureWidth(text[ci].ToString(), fs); ci++; }
            ShowSeg(text[segStart..ci], segX, blue: false);
            var runStart = ci;
            var runX = cum;
            while (ci < text.Length
                   && cum + MeasureWidth(text[ci].ToString(), fs) / 2 <= xo + rw)
            { cum += MeasureWidth(text[ci].ToString(), fs); ci++; }
            ShowSeg(text[runStart..ci], runX, blue: true);
        }
        if (ci < text.Length) ShowSeg(text[ci..], cum, blue: false);
    }

    /// <summary>Render a cell's visible lines one at a time, drawing a form-control
    /// glyph (currently the radio-button circle) ahead of any option line's caption.</summary>
    private void RenderControlLines(ContentStreamBuilder builder, List<CellLine> cellLines,
        int firstLine, int lastLine, double leftX, double topY, double lineHeight, string fontName,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink = null,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink = null,
        double? seatBottom = null)
    {
        // A multi-line control cell stacks by each line's OWN height (a 10pt
        // spacer line directly above an 8.5pt box: the box top sits exactly
        // 10pt below the cell content top) instead of the row's uniform pitch.
        var exactStack = false;
        if (lastLine - firstLine > 1)
            for (var li = firstLine; li < lastLine; li++)
                if (cellLines[li].Checkbox is not null) { exactStack = true; break; }

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
            walkY -= line.ImgReserve && line.FontSize > 0 ? line.FontSize : lineHeight;
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
            if (line.Text.IndexOf(InlineButtonChar) >= 0)
            {
                var bScale = line.FontSize / InlineButtonProbeBasePt;
                var bBase = lineTop - line.FontSize;
                var bFaceTop = bBase + InlineButtonBaseDropPt * bScale;
                var bFaceH = InlineButtonFaceHPt * bScale;
                var bPen = textX;
                var bSb = new System.Text.StringBuilder();
                void FlushButtonRun()
                {
                    if (bSb.Length == 0) return;
                    var s = bSb.ToString(); bSb.Clear();
                    builder.BeginText();
                    builder.SetFont(fontName, line.FontSize);
                    ApplyColor(builder, line.ForegroundColor);
                    builder.MoveTextPosition(bPen, bBase);
                    builder.ShowText(s);
                    builder.EndText();
                    bPen += MeasureWidth(s, line.FontSize);
                }
                var bti = 0;
                while (bti < line.Text.Length)
                {
                    var bch = line.Text[bti];
                    if (bch != InlineButtonChar) { bSb.Append(bch); bti++; continue; }
                    FlushButtonRun();
                    var bEnd = line.Text.IndexOf(InlineButtonEndChar, bti + 1);
                    if (bEnd < 0) bEnd = line.Text.Length;
                    var bCap = line.Text[(bti + 1)..bEnd];
                    bti = Math.Min(bEnd + 1, line.Text.Length);
                    var bCapW = MeasureWidth(bCap, line.FontSize);
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
                    // Black outline around the face.
                    builder.SetStrokeColor(0, 0, 0);
                    builder.SetLineWidth(1.0);
                    builder.Rectangle(bFaceX - InlineButtonOutlineOutHPt * bScale,
                        bFaceTop - bFaceH - InlineButtonOutlineOutVPt * bScale,
                        bFaceW + 2 * InlineButtonOutlineOutHPt * bScale,
                        bFaceH + 2 * InlineButtonOutlineOutVPt * bScale);
                    builder.Stroke();
                    // Caption inside the face.
                    builder.BeginText();
                    builder.SetFont(fontName, line.FontSize);
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
                var iGlyphD = InlineRadioGlyphDPt * iScale;
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
                    builder.SetFont(fontName, line.FontSize);
                    ApplyColor(builder, line.ForegroundColor);
                    builder.MoveTextPosition(pen, iBase);
                    builder.ShowText(s);
                    builder.EndText();
                    pen += MeasureWidth(s, line.FontSize);
                }
                foreach (var ch in line.Text)
                {
                    if (ch is not (InlineRadioChar or InlineRadioCheckedChar))
                    {
                        runSb.Append(ch);
                        continue;
                    }
                    FlushRun();
                    pen += InlineRadioLeadPt * iScale;
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
                    pen += (InlineRadioGlyphDPt + InlineRadioTrailPt) * iScale;
                }
                FlushRun();
                continue;
            }

            if (!string.IsNullOrEmpty(line.Text))
            {
                if (line.LinkRuns is { Count: > 0 } || line.Hyperlink is not null)
                {
                    ShowLineWithLinks(builder, line, fontName, textX, lineTop - line.FontSize);
                    continue;
                }
                builder.BeginText();
                builder.SetFont(fontName, line.FontSize);
                ApplyColor(builder, line.ForegroundColor);
                builder.MoveTextPosition(textX, lineTop - line.FontSize);
                builder.ShowText(line.Text);
                builder.EndText();
            }
        }
    }

    /// <summary>Fill a rounded rectangle (radius clamped so corner arcs never overlap).</summary>
    private static void FillRoundedRect(ContentStreamBuilder builder, double x, double y,
        double w, double h, double radius)
    {
        if (w <= 0 || h <= 0) return;
        var r = Math.Max(0, Math.Min(radius, Math.Min(w, h) / 2));
        var k = r * RoundCornerKappa;
        builder.MoveTo(x + r, y)
            .LineTo(x + w - r, y)
            .CurveTo(x + w - r + k, y, x + w, y + r - k, x + w, y + r)
            .LineTo(x + w, y + h - r)
            .CurveTo(x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h)
            .LineTo(x + r, y + h)
            .CurveTo(x + r - k, y + h, x, y + h - r + k, x, y + h - r)
            .LineTo(x, y + r)
            .CurveTo(x, y + r - k, x + r - k, y, x + r, y)
            .ClosePath();
        builder.Fill();
    }

    /// <summary>Fill an axis-aligned ellipse centred at (cx, cy) — the checked
    /// radio's inner dot (the dot draws taller than wide).</summary>
    private static void FillEllipse(ContentStreamBuilder builder, double cx, double cy,
        double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) return;
        const double k = 0.5522847498;
        builder.MoveTo(cx + rx, cy);
        builder.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
        builder.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
        builder.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
        builder.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
        builder.ClosePath();
        builder.Fill();
    }

    /// <summary>Fill a circle centred at (cx, cy), four cubic Béziers.</summary>
    private static void FillCircle(ContentStreamBuilder builder, double cx, double cy, double radius)
    {
        if (radius <= 0) return;
        const double k = 0.5522847498;
        var rx = radius; var ry = radius;
        builder.MoveTo(cx + rx, cy);
        builder.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
        builder.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
        builder.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
        builder.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
        builder.ClosePath();
        builder.Fill();
    }

    /// <summary>Stroke an axis-aligned ellipse centred at (cx, cy), approximated with
    /// four cubic Béziers.</summary>
    private static void DrawEllipse(ContentStreamBuilder builder, double cx, double cy,
        double rx, double ry, double r, double g, double b)
    {
        if (rx <= 0 || ry <= 0) return;
        const double k = 0.5522847498;
        builder.SetLineWidth(1);
        builder.SetStrokeColor(r, g, b);
        builder.MoveTo(cx + rx, cy);
        builder.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
        builder.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
        builder.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
        builder.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
        builder.ClosePath();
        builder.Stroke();
    }

    private static void ApplyColor(ContentStreamBuilder builder, Color? color)
    {
        if (color is { } c)
            builder.SetFillColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
        else
            builder.SetFillColor(0, 0, 0);
    }
}
