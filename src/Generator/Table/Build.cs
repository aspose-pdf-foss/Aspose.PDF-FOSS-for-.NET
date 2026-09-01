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
    /// <summary>
    /// Build the table content stream bytes for the given page.
    /// Registers a Helvetica font in the page resources.
    /// </summary>
    public byte[] Build(Page page)
    {
        var fontName = RegisterFont(page);
        var colWidths = ParseColumnWidths(GetTableUsableWidth(page));
        var builder = new ContentStreamBuilder();
        // The MEDIA frame's height (see Page.LayoutFrameHeight): a /Rotate page's
        // table seats against the media edges and paints upright in them.
        var pageHeight = page.LayoutFrameHeight;

        // Table origin in PDF coordinates (bottom-left origin)
        var marginLeft = Margin?.Left ?? 0;
        var marginTop = Margin?.Top ?? 0;
        var tableX = Left + marginLeft;
        var tableTopY = pageHeight - Top - marginTop;

        builder.SaveState();

        // Draw table background if specified
        if (BackgroundColor is not null)
        {
            var totalWidth = 0.0;
            foreach (var w in colWidths) totalWidth += w;
            var totalHeight = CalculateTotalHeight(colWidths);

            builder.SetFillColor(BackgroundColor);
            builder.Rectangle(tableX, tableTopY - totalHeight, totalWidth, totalHeight);
            builder.Fill();
        }

        // Render rows
        var currentY = tableTopY;
        for (var rowIdx = 0; rowIdx < Rows.Count; rowIdx++)
        {
            var row = Rows.At(rowIdx);
            var rowHeight = CalculateRowHeight(row, colWidths);
            var cellX = tableX;

            for (var colIdx = 0; colIdx < row.Cells.Count && colIdx < colWidths.Length; colIdx++)
            {
                var cell = row.Cells.At(colIdx);
                var cellWidth = GetCellWidth(colWidths, colIdx, cell.ColSpan);
                var cellTopY = currentY;

                // Effective padding
                var padding = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
                var padLeft = padding?.Left ?? 2;
                var padRight = padding?.Right ?? 2;
                var padTop = padding?.Top ?? 2;
                var padBottom = padding?.Bottom ?? 2;

                // Draw cell background
                var bgColor = cell.BackgroundColor ?? row.BackgroundColor;
                if (bgColor is not null)
                {
                    builder.SetFillColor(bgColor);
                    builder.Rectangle(cellX, cellTopY - rowHeight, cellWidth, rowHeight);
                    builder.Fill();
                }

                // Draw cell border
                if (!cell.IsNoBorder)
                {
                    var cellBorder = cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
                    if (cellBorder is not null)
                    {
                        DrawBorder(builder, cellBorder, cellX, cellTopY - rowHeight, cellWidth, rowHeight);
                    }
                }

                // Draw cell text content
                var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
                var fontSize = ResolveCellFontSize(cell, row);
                var textX = cellX + padLeft;
                var textY = cellTopY - padTop - fontSize;

                foreach (var paragraph in cell.Paragraphs)
                {
                    if (paragraph is TextFragment tf)
                    {
                        var fragFontSize = ResolveCellParagraphFontSize(tf, fontSize, cell, row);
                        var effectiveTextY = cellTopY - padTop - fragFontSize;

                        builder.BeginText();
                        builder.SetFont(fontName, fragFontSize);

                        // Apply text color
                        if (tf.TextState.ForegroundColor is { } fg)
                            builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
                        else if (textState?.ForegroundColor is { } tsFg)
                            builder.SetFillColor(tsFg.R / 255.0, tsFg.G / 255.0, tsFg.B / 255.0);
                        else
                            builder.SetFillColor(0, 0, 0);

                        builder.MoveTextPosition(textX, effectiveTextY);

                        if (cell.IsWordWrapped && tf.Text.Length > 0)
                        {
                            var availWidth = cellWidth - padLeft - padRight;
                            var lines = WrapText(tf.Text, fragFontSize, availWidth);
                            for (var li = 0; li < lines.Count; li++)
                            {
                                if (li > 0)
                                {
                                    builder.MoveTextPosition(0, -fragFontSize * 1.2);
                                }
                                builder.ShowText(lines[li]);
                            }
                        }
                        else
                        {
                            builder.ShowText(tf.Text);
                        }

                        builder.EndText();
                        textY -= fragFontSize * 1.2;
                    }
                }

                cellX += cellWidth;
            }

            currentY -= rowHeight;
        }

        // Draw outer table border last so it sits on top of cell backgrounds and borders.
        if (Border is not null)
        {
            var totalWidth = 0.0;
            foreach (var w in colWidths) totalWidth += w;
            var totalHeight = CalculateTotalHeight(colWidths);

            DrawBorder(builder, Border, tableX, tableTopY - totalHeight, totalWidth, totalHeight);
        }

        builder.RestoreState();
        return builder.Build();
    }

    private double[] ParseColumnWidths(double availableWidth = 0, bool clampToAvailable = true)
    {
        // AutoFitToContent OVERRIDES declared widths — the content sizing IS the
        // adjustment, not a fallback for a missing ColumnWidths: a table declaring
        // "50 50 50" still lays every cell on one line.
        // A column-paginating table is exempt; there the declared widths drive the
        // slice packing.
        var contentOverridesWidths = ColumnAdjustment == ColumnAdjustment.AutoFitToContent
            && !string.IsNullOrWhiteSpace(ColumnWidths)
            && RepeatingColumnsCount == 0
            && Broken is not TableBroken.Vertical and not TableBroken.VerticalInSamePage;
        if (string.IsNullOrWhiteSpace(ColumnWidths) || contentOverridesWidths)
        {
            // AutoFitToWindow with no explicit widths: distribute the usable
            // width equally across the columns.
            if (ColumnAdjustment == ColumnAdjustment.AutoFitToWindow && availableWidth > 0)
            {
                var cols = 1;
                for (var i = 0; i < Rows.Count; i++)
                {
                    var cc = Rows.At(i).Cells.Count;
                    if (cc > cols) cols = cc;
                }
                var equal = new double[cols];
                for (var i = 0; i < cols; i++) equal[i] = availableWidth / cols;
                return equal;
            }
            // AutoFitToContent with no explicit widths: each column takes its
            // MAX-content width — the full unwrapped text — so a fitting table never
            // wraps a cell it could lay on one line (the widest cell stays whole:
            // its full text draws on a single line). The exception is an
            // HtmlFragment cell, which measures MIN-content (the widest unbreakable
            // word) because the HTML shrink-to-fit measure reports that: "Crossing
            // Type" sizes its column to "Crossing" and wraps.
            if (ColumnAdjustment == ColumnAdjustment.AutoFitToContent)
            {
                var cols = 1;
                for (var i = 0; i < Rows.Count; i++)
                    if (Rows.At(i).Cells.Count > cols) cols = Rows.At(i).Cells.Count;
                var w = new double[cols];
                // A column-paginating auto-fit table (TableBroken.Vertical /
                // VerticalInSamePage / repeating columns) is max-content for the same
                // reason plus one more: the slicing carries the overflow to further
                // pages/bands instead of wrapping (the 97-cell factor sheet lays every
                // header on one line across 13 pages).
                var sliceMaxContent = RepeatingColumnsCount > 0
                    || Broken is TableBroken.Vertical or TableBroken.VerticalInSamePage;
                var anyMinContent = false;
                // Columns whose cells hold block-structured HTML: they fill whatever the
                // measured columns leave (see HasFillHtmlContent).
                var fill = new bool[cols];
                for (var ri = 0; ri < Rows.Count; ri++)
                {
                    var row = Rows.At(ri);
                    for (var ci = 0; ci < row.Cells.Count && ci < cols; ci++)
                    {
                        var cell = row.Cells[ci];
                        // A cell that spans columns sizes NONE of them: its content is
                        // laid out across the span and wraps there, so it never widens a
                        // single column. Probed on the 17-column repeating-column sheet —
                        // the five spanned header cells leave their columns at the data
                        // cells' width while the six single-column headers set theirs.
                        if (cell.ColSpan > 1) continue;
                        var pad = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
                        // An HtmlFragment cell shrinks to its widest word; a slicing
                        // table keeps every cell max-content regardless of dialect.
                        var minContent = !sliceMaxContent && HasHtmlContent(cell);
                        if (minContent) anyMinContent = true;
                        if (!sliceMaxContent && HasFillHtmlContent(cell)) fill[ci] = true;
                        // A bold-serif HTML cell with no declared padding butts its column
                        // against the text width exactly (no padding is added).
                        // Max-content columns carry the generator's +0.01 pt measure
                        // guard on top of text + declared padding (probed: the factor
                        // sheet's columns land at text+pad+0.01 to the third decimal,
                        // and GetWidth reports 44.47 for a 44.46 pt cell).
                        var need = minContent
                            ? MaxWordWidth(cell, row) + (pad is null && AllBoldSerifHtml(cell)
                                ? 0
                                : (pad?.Left ?? 2) + (pad?.Right ?? 2))
                            : MaxLineWidth(cell, row, exact: true) + (pad?.Left ?? 0) + (pad?.Right ?? 0)
                              + AutoFitMeasureGuardPt;
                        if (need > w[ci]) w[ci] = need;
                    }
                }
                // Max-content cells NEVER wrap — the inflated wrap estimate would split
                // lines the generator draws whole. Withheld when any cell in the table
                // was sized min-content, because that cell is MEANT to wrap.
                if (!anyMinContent) AutoFitMaxContentCells = true;
                for (var i = 0; i < cols; i++) if (w[i] <= 0) w[i] = 100;
                // Never exceed the usable band: proportionally shrink an over-wide
                // auto-fit table to the page content width. Skipped when the table
                // column-paginates instead of shrinking (TableBroken.Vertical /
                // VerticalInSamePage / repeating columns slice the overflow away).
                if (availableWidth > 0 && RepeatingColumnsCount == 0
                    && Broken is not TableBroken.Vertical and not TableBroken.VerticalInSamePage)
                {
                    // A fill column takes an equal share of what the measured columns
                    // leave of the content box — never less than its own min-content.
                    var fillCount = 0;
                    double measured = 0;
                    for (var i = 0; i < w.Length; i++)
                        if (i < fill.Length && fill[i]) fillCount++;
                        else measured += w[i];
                    if (fillCount > 0 && availableWidth - measured > 0)
                    {
                        var share = (availableWidth - measured) / fillCount;
                        for (var i = 0; i < w.Length; i++)
                            if (i < fill.Length && fill[i] && share > w[i]) w[i] = share;
                    }
                    double total = 0;
                    foreach (var cw in w) total += cw;
                    if (total > availableWidth + 1e-3)
                        for (var i = 0; i < w.Length; i++) w[i] *= availableWidth / total;
                }
                return w;
            }
            // Default: one column per max cells in any row. DefaultColumnWidth names
            // that width when the caller set it (a caller paces a 28-column factor sheet
            // at "1cm" and lets TableBroken.VerticalInSamePage slice it); 100 pt is the
            // fallback when it is unset or unreadable.
            var maxCols = 1;
            for (var i = 0; i < Rows.Count; i++)
            {
                var cc = Rows.At(i).Cells.Count;
                if (cc > maxCols) maxCols = cc;
            }
            var defWidth = 100.0;
            if (!string.IsNullOrWhiteSpace(DefaultColumnWidth))
            {
                var dtok = DefaultColumnWidth!.Trim();
                if (dtok.EndsWith("%", StringComparison.Ordinal)
                    && TryParseWidthToken(dtok.Substring(0, dtok.Length - 1), out var dpct)
                    && availableWidth > 0)
                    defWidth = dpct / 100.0 * availableWidth;
                else if (TryParseWidthToken(dtok, out var dpts) && dpts > 0)
                    defWidth = dpts;
            }
            var result = new double[maxCols];
            for (var i = 0; i < maxCols; i++) result[i] = defWidth;
            return result;
        }

        // Accept space, tab, comma, or semicolon as column-width separators —
        // callers in the wild write ColumnWidths as "100 200 100" or "100, 200, 100"
        // interchangeably. A trailing '%' makes the value a percentage of the table's
        // available width (e.g. "3% 97%"); resolved here so percentage columns fill the
        // content area instead of collapsing to the 100pt parse-failure fallback.
        // A string with whitespace separators may carry decimal COMMAS ("91,3 323,8"
        // from string.Format under a comma-decimal culture like de-DE) — split those
        // on whitespace only and read the comma as a decimal point, so the
        // authored widths survive a culture-formatted round trip.
        var hasSpaceSep = ColumnWidths.IndexOfAny(new[] { ' ', '\t' }) >= 0;
        var parts = hasSpaceSep
            ? ColumnWidths.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries)
            : ColumnWidths.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var widths = new double[parts.Length];
        // The share a column actually DECLARED, resolved against the real box — the
        // ceiling on how far it may grow once the resolver below re-fits the grid.
        // 0 = this column declared nothing (its emitted share is the synthetic
        // leftover the builder spreads over the auto columns).
        var declShare = new double[parts.Length];
        var declMask = HtmlColPctDeclaredCols;
        for (var i = 0; i < parts.Length; i++)
        {
            var tok = parts[i].TrimEnd(',');
            var isPercent = tok.EndsWith("%", StringComparison.Ordinal);
            var num = isPercent ? tok.Substring(0, tok.Length - 1) : tok;
            if (!TryParseWidthToken(num, out var w)) w = 100;
            if (isPercent)
                w = availableWidth > 0 ? w / 100.0 * availableWidth : w;
            widths[i] = w;
            if (isPercent && declMask is not null && i < declMask.Length && declMask[i])
                declShare[i] = w;
        }

        bool HtmlColFixed(int i)
            => HtmlColFixedCols is { } fx && i < fx.Length && fx[i];


        // The lifted HTML dialect resolves its grids by the AUTO column rule at
        // DRAW time, when the real box is finally known (the build's available
        // width is the outer table's stand-in): when the max-content columns fit
        // the box they are taken whole with the surplus ∝ max; otherwise each
        // column floors at max(declared, min-content) and a deficit squeezes the
        // above-floor slack / a surplus grows ∝ the post-floor width.
        if (HtmlColMinPt is { } hMins && hMins.Length == widths.Length && availableWidth > 0)
        {
            var hMaxs = HtmlColMaxPt is { } hm && hm.Length == widths.Length ? hm : null;
            double sumMax = 0;
            if (hMaxs is not null) foreach (var m in hMaxs) sumMax += m;
            if (hMaxs is not null && sumMax > 0 && sumMax <= availableWidth + 0.01)
            {
                var surplus = availableWidth - sumMax;
                for (var i = 0; i < widths.Length; i++)
                    widths[i] = hMaxs[i];
                if (HtmlSurplusCol >= 0 && HtmlSurplusCol < widths.Length)
                    widths[HtmlSurplusCol] += surplus;
                else
                {
                    // CSS auto layout: the surplus belongs to the columns that
                    // DECLARED a percent — up to that percent of the box — while the
                    // auto columns hug their max-content (the risks pill: only
                    // `.CategoryName {width:80%}` grows, the detail button keeps its
                    // min-content 25 pt). Whatever the declared shares cannot hold
                    // falls back to the proportional rule.
                    surplus -= GrowDeclaredPercentColumns(widths, declShare, surplus);
                    if (surplus > 0.01)
                    {
                        // …and a column with a DECLARED absolute width is FIXED: the
                        // surplus belongs to the auto columns beside it.
                        double sumAutoMax = 0;
                        for (var i = 0; i < widths.Length; i++)
                            if (!HtmlColFixed(i)) sumAutoMax += hMaxs[i];
                        var share = sumAutoMax > 0 ? sumAutoMax : sumMax;
                        for (var i = 0; i < widths.Length; i++)
                            if (sumAutoMax <= 0 || !HtmlColFixed(i))
                                widths[i] += surplus * hMaxs[i] / share;
                    }
                }
            }
            else
            {
                // Floors, then surplus grows each column toward its MAX-CONTENT
                // (∝ remaining room): the wide text columns get
                // the space, not the already-satisfied pill column. The emitted
                // shares only matter when they exceed a column's min (a truly
                // DECLARED percent like the milestone 13%s).
                for (var i = 0; i < widths.Length; i++)
                    widths[i] = HtmlColPctDeclared ? Math.Max(widths[i], hMins[i]) : hMins[i];
                double sumW = 0;
                foreach (var w in widths) sumW += w;
                if (sumW > availableWidth + 0.01)
                {
                    var excess = sumW - availableWidth;
                    double slack = 0;
                    for (var i = 0; i < widths.Length; i++) slack += widths[i] - hMins[i];
                    if (slack > 0)
                    {
                        for (var i = 0; i < widths.Length; i++)
                            widths[i] -= (widths[i] - hMins[i]) / slack * Math.Min(excess, slack);
                        excess -= Math.Min(excess, slack);
                    }
                    // Already on the floors and STILL over the box: the floors
                    // themselves squeeze, proportionally. A grid that keeps them
                    // overhangs its host cell's right edge instead (the milestone
                    // grid's Notes column stuck 3 pt past the report frame) —
                    // narrowing the columns keeps the grid inside.
                    if (excess > 0.01)
                    {
                        double onFloors = 0;
                        foreach (var w in widths) onFloors += w;
                        if (onFloors > 0)
                            for (var i = 0; i < widths.Length; i++)
                                widths[i] *= availableWidth / onFloors;
                    }
                }
                else if (sumW > 0 && sumW < availableWidth - 0.01)
                {
                    var surplus = availableWidth - sumW;
                    if (HtmlSurplusCol >= 0 && HtmlSurplusCol < widths.Length)
                        widths[HtmlSurplusCol] += surplus;
                    else if (hMaxs is not null)
                    {
                        double sumRoom = 0;
                        for (var i = 0; i < widths.Length; i++)
                            if (!HtmlColFixed(i)) sumRoom += Math.Max(0, hMaxs[i] - widths[i]);
                        if (sumRoom > 0)
                            for (var i = 0; i < widths.Length; i++)
                                widths[i] += HtmlColFixed(i) ? 0
                                    : surplus * Math.Max(0, hMaxs[i] - widths[i]) / sumRoom;
                        else
                            for (var i = 0; i < widths.Length; i++)
                                widths[i] += surplus * widths[i] / sumW;
                    }
                    else
                        for (var i = 0; i < widths.Length; i++)
                            widths[i] += surplus * widths[i] / sumW;
                }
            }
        }

        // A row may carry more cells than the specified column widths; the table is padded
        // to the widest row rather than dropping the surplus cells. Append auto-width
        // columns (reusing the last specified width) so every cell is laid out.
        var neededCols = widths.Length;
        for (var i = 0; i < Rows.Count; i++)
            if (Rows.At(i).Cells.Count > neededCols) neededCols = Rows.At(i).Cells.Count;
        if (neededCols > widths.Length)
        {
            var fill = widths.Length > 0 ? widths[widths.Length - 1] : 100;
            var extended = new double[neededCols];
            Array.Copy(widths, extended, widths.Length);
            for (var i = widths.Length; i < neededCols; i++) extended[i] = fill;
            widths = extended;
        }

        // AutoFitToWindow with declared widths: they are SHARES, scaled to fill the
        // usable band ("8 10" lays out as 356.4/445.6 across the
        // 802 pt content width — the cell text never wraps to the 8 pt literal).
        // The rule is the adjustment mode's, not the XML dialect's: a DOM
        // table declaring "50 50 50" spreads the three
        // columns across the whole 415 pt band.
        if (ColumnAdjustment == ColumnAdjustment.AutoFitToWindow
            && availableWidth > 0 && widths.Length > 0)
        {
            double xmlTotal = 0;
            foreach (var w in widths) xmlTotal += w;
            if (xmlTotal > 0 && Math.Abs(xmlTotal - availableWidth) > 0.01)
                for (var i = 0; i < widths.Length; i++)
                    widths[i] *= availableWidth / xmlTotal;
        }

        // Clamp to the page's usable width: when the (fixed) column widths overflow
        // the content area, shrink them proportionally to fit — matching the
        // generator's shrink-to-window behaviour. Without this a column wider than
        // the page (e.g. ColumnWidths="600" inside a ~415pt content band) pushes the
        // cell content — notably an image fitted to the cell width — off the page's
        // right edge. Skipped when RepeatingColumnsCount > 0, where an over-wide table
        // is sliced across pages (column-pagination) rather than shrunk, and for
        // Broken=VerticalInSamePage, where the overflow columns wrap into bands
        // stacked below on the same page.
        if (clampToAvailable && RepeatingColumnsCount == 0
            && Broken != TableBroken.VerticalInSamePage && Broken != TableBroken.Vertical
            && availableWidth > 0
            // The auto-rule resolution above already fitted (or deliberately
            // overflowed at min floors) — the sequential clamp would squeeze
            // columns BELOW their floors and mid-word-break their headers.
            && HtmlColMinPt is null)
        {
            double total = 0;
            for (var i = 0; i < widths.Length; i++) total += widths[i];
            if (total > availableWidth + 1e-3)
            {
                // The generator keeps the DECLARED widths and squeezes only the
                // columns that no longer fit: each column is clamped to the width
                // remaining after its predecessors (a "25 17 390 90" table in a
                // 468 pt band keeps 25/17/390 and gives the last column the
                // 36 pt remainder).
                double cum = 0;
                for (var i = 0; i < widths.Length; i++)
                {
                    var wRemain = Math.Max(0, availableWidth - cum);
                    if (widths[i] > wRemain) widths[i] = wRemain;
                    cum += widths[i];
                }
            }
        }
        return widths;
    }

    /// <summary>Grows the columns that DECLARED a percent toward that percent of the
    /// box, sharing the surplus between them in proportion to their declared shares,
    /// and returns how much it placed. A column already at or past its declared share
    /// takes nothing — CSS treats the percent as the column's target, not a floor.</summary>
    private static double GrowDeclaredPercentColumns(double[] widths, double[] declShare, double surplus)
    {
        if (surplus <= 0.01) return 0;
        double room = 0, shares = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (declShare[i] <= 0) continue;
            room += Math.Max(0, declShare[i] - widths[i]);
            shares += declShare[i];
        }
        if (room <= 0 || shares <= 0) return 0;
        var give = Math.Min(surplus, room);
        double placed = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (declShare[i] <= 0) continue;
            // Proportional to the declared share, but never past this column's own
            // room — with one declared column (the pill's name cell) it simply takes
            // the whole surplus up to its percent.
            var add = Math.Min(give * declShare[i] / shares, Math.Max(0, declShare[i] - widths[i]));
            widths[i] += add;
            placed += add;
        }
        return placed;
    }

    /// <summary>Width available to the table's columns: the page content area inset by
    /// the table's left offset (mirrored on the right). Used to resolve percentage
    /// <see cref="ColumnWidths"/>.</summary>
    /// <summary>When set, the exact content width available to this table — a
    /// float-band COLUMN's width. The symmetric-margin guess below reads a
    /// right-column table (FlowLeftOffset ≈ half the page) as having almost no
    /// room and collapses its columns to per-character wraps.</summary>
    internal double UsableWidthOverride { get; set; }

    private double GetTableUsableWidth(Page page)
    {
        if (UsableWidthOverride > 0) return UsableWidthOverride;
        // A declared Left pins the table at an ABSOLUTE page x, so its band is what
        // is left between the pin and the page's right content margin — a 595 pt page
        // with 90 pt margins gives a table pinned at 440 a 65 pt band, and its
        // declared 85 pt column clamps into it.
        var leftOff = Left > 0
            ? Left + (Margin?.Left ?? 0)
            : (FlowLeftOffset > 0 ? FlowLeftOffset : (Margin?.Left ?? 0));
        // The columns run to the page's RIGHT content margin when the page declares
        // one (a 16.7/13.3 mm margin pair keeps a 510 pt grid whole); a page without
        // an explicit right margin mirrors the flow's left content offset, falling
        // back to the left offset itself. Mirroring the LEFT OFFSET would be wrong
        // for a pinned table, whose offset is a position on the page, not a margin.
        var rightMargin = page.PageInfo?.Margin is { RightTouched: true } pm
            ? pm.Right
            : FlowLeftOffset > 0 ? FlowLeftOffset : leftOff;
        var usable = page.Width - leftOff - rightMargin;
        if (usable <= 0) usable = page.Width - leftOff - 36;
        return usable > 0 ? usable : page.Width;
    }

    /// <summary>Width a MEASUREMENT (<see cref="GetHeight(Page?)"/>) resolves relative
    /// column widths against: the page's content band — its width less both page
    /// margins — because a table asked for its height has not been placed in the flow
    /// yet and so carries no <see cref="FlowLeftOffset"/>. Probed against the generator:
    /// a "50% 50%" table on a 595 pt page wraps a 208.25 pt word at a 90 pt margin
    /// (column 207.5) and keeps it whole at a 20 pt margin (column 277.5), i.e. the base
    /// tracks the margins, not the full page width.</summary>
    private double GetMeasureBandWidth(Page page)
    {
        if (UsableWidthOverride > 0) return UsableWidthOverride;
        if (FlowLeftOffset > 0) return GetTableUsableWidth(page);
        var info = page.PageInfo;
        var band = page.Width - (info?.Margin?.Left ?? 0) - (info?.Margin?.Right ?? 0);
        return band > 0 ? band : GetTableUsableWidth(page);
    }

    private double GetCellWidth(double[] colWidths, int colIndex, int colSpan)
    {
        var span = Math.Max(1, colSpan);
        var width = 0.0;
        for (var i = colIndex; i < colIndex + span && i < colWidths.Length; i++)
            width += colWidths[i];
        return width;
    }

    private double CalculateRowHeight(Row row, double[] colWidths)
    {
        if (row.FixedRowHeight > 0) return row.FixedRowHeight;

        var maxHeight = row.MinRowHeight;
        var defaultPad = row.DefaultCellPadding ?? DefaultCellPadding;

        for (var colIdx = 0; colIdx < row.Cells.Count && colIdx < colWidths.Length; colIdx++)
        {
            var cell = row.Cells.At(colIdx);
            if (cell.SpanContinuation) continue;
            var padding = cell.Margin ?? defaultPad;
            var padTop = padding?.Top ?? 2;
            var padBottom = padding?.Bottom ?? 2;
            var padLeft = padding?.Left ?? 2;
            var padRight = padding?.Right ?? 2;

            var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
            var fontSize = ResolveCellFontSize(cell, row);
            var cellWidth = GetCellWidth(colWidths, colIdx, cell.ColSpan);
            var availWidth = cellWidth - padLeft - padRight;

            var contentHeight = 0.0;
            foreach (var paragraph in cell.Paragraphs)
            {
                if (paragraph is TextFragment tf)
                {
                    var fragFontSize = ResolveCellParagraphFontSize(tf, fontSize, cell, row);
                    // Cell line pitch is exactly the font size (K = 1.0; the
                    // row formula is padTop + padBottom + borders +
                    // lineCount·fontSize) — the old 1.2× leading made every row
                    // ~20% too tall.
                    if (cell.IsWordWrapped && tf.Text.Length > 0)
                    {
                        var lines = WrapText(tf.Text, fragFontSize, availWidth);
                        contentHeight += lines.Count * fragFontSize;
                    }
                    else
                    {
                        contentHeight += fragFontSize;
                    }
                }
                else if (paragraph is HtmlFragment html)
                {
                    var plainText = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                    if (plainText.Length > 0)
                        contentHeight += fontSize;
                }
            }

            var cellHeight = contentHeight + padTop + padBottom;
            if (cellHeight > maxHeight) maxHeight = cellHeight;
        }

        return maxHeight > 0 ? maxHeight : 20; // fallback minimum
    }

    private double CalculateTotalHeight(double[] colWidths)
    {
        var total = 0.0;
        for (var i = 0; i < Rows.Count; i++)
            total += CalculateRowHeight(Rows.At(i), colWidths);
        return total;
    }

    /// <summary>Width of the table's own box border — the space it claims outside the
    /// column block on each side. Zero when the table carries no full-box border.</summary>
    /// <summary>Left/right stroke widths the table's <see cref="DefaultCellBorder"/>
    /// adds to every column's pitch (see <see cref="CellBorderInPitch"/>); (0, 0)
    /// when the dialect sizes its own cell boxes or no horizontal side draws.</summary>
    private (double left, double right) CellBorderPitch()
    {
        if (!GeneratorCellModel) return (0, 0);
        // The table's own default border is the usual source of the pitch. A table
        // that instead gives EVERY cell the same border draws the same grid, so it
        // joins the pitch the same way: the column box grows by the strokes and the
        // DECLARED width stays the text box. Reading it as an inset instead shrank
        // every column by a stroke and wrapped its headings a line early.
        var b = DefaultCellBorder ?? UniformAssignedCellBorder();
        if (b is null) return (0, 0);
        // A DOUBLED side claims its clearance and its second rule on top of the stroke.
        return (OccupiedSideWidth(b, BorderSide.Left, b.LeftAssigned, b.RawLeft),
                OccupiedSideWidth(b, BorderSide.Right, b.RightAssigned, b.RawRight));
    }

    private BorderInfo? _uniformCellBorder;
    private bool _uniformCellBorderResolved;

    /// <summary>Descent of the face a cell's text draws in, as a positive fraction of
    /// the em: the hhea descender of a TrueType face, the AFM descender of a
    /// Standard-14 face, Helvetica's 0.207 when nothing names one. The generator seats
    /// every cell baseline one descent above the full-em drop and bounds the text
    /// clip a descent below the last baseline (Helvetica 0.207, Calibri 0.25 —
    /// a header row seats at top − 0.75 × 15).</summary>
    private (double DescentEm, string? Face, bool FragmentFace) CellFontDescentEm(Cell cell, Row row)
    {
        Aspose.Pdf.Text.Font? face = null;
        foreach (var p in cell.Paragraphs)
            // TextState.Font is never null (it defaults to the shared Helvetica) — a
            // fragment NAMES a face only when it was assigned one.
            if (p is TextFragment tf && tf.TextState.Font is { } f
                && !ReferenceEquals(f, FontInfo.DefaultHelvetica)) { face = f; break; }
        var fragmentFace = face is not null;
        face ??= cell.DefaultCellTextState?.Font ?? row.DefaultCellTextState?.Font ?? DefaultCellTextState?.Font;
        // A default state that names a face only by NAME (TextState("Arial")) still
        // counts as a named default face.
        var namedDefault = !fragmentFace && (face is not null
            || !string.IsNullOrEmpty(cell.DefaultCellTextState?.FontName)
            || !string.IsNullOrEmpty(row.DefaultCellTextState?.FontName)
            || !string.IsNullOrEmpty(DefaultCellTextState?.FontName));
        if (face is null) return (HelveticaDescentEm, null, !namedDefault);
        double d = 0;
        try
        {
            if (face.SourceFontData?.TtfData is { } ttf) d = TextBuilder.HheaDescentPerMille(ttf);
            if (d == 0 && face.FontName is { Length: > 0 } name) d = Standard14Fonts.GetDescent(name);
        }
        catch { d = 0; }
        return (d != 0 ? Math.Abs(d) / 1000.0 : HelveticaDescentEm, face.FontName, fragmentFace);
    }

    /// <summary>How far a subscript run extends the clip BELOW the line's descent and
    /// ABOVE its line box, in ems of the line size, per face. Probed at 20 pt on
    /// eight faces (gt2_subclip_probe): the pair is a property of the face that no
    /// ascent/descent metric reproduces (Arial 0.2119 and Helvetica 0.207 descents
    /// give 0.4558 and 0.4214), so the probed values are carried as measured; an
    /// unprobed face takes Helvetica's. The superscript extension (0.14616) and the
    /// sub+superscript coupling (the top grows 1.9135 × the subscript's rise) are the
    /// same on every face.</summary>
    private static readonly Dictionary<string, (double Below, double Above)> SubscriptClipEm = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica"] = (0.42136, 0.02158), ["Arial"] = (0.45580, 0.02606),
        ["Times-Roman"] = (0.42302, 0.02099), ["Times New Roman"] = (0.45709, 0.02582),
        ["Courier"] = (0.36899, 0.01833), ["Calibri"] = (0.45896, 0.02332),
        ["Verdana"] = (0.47103, 0.02834), ["Tahoma"] = (0.46790, 0.02814),
    };
    private static (double Below, double Above) SubscriptClip(string? face)
    {
        if (face is { Length: > 0 })
        {
            if (SubscriptClipEm.TryGetValue(face, out var v)) return v;
            // Styled names ("Helvetica-Bold", "Arial,Bold", "Calibri Bold") share the family's pair.
            foreach (var kv in SubscriptClipEm)
                if (face.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        }
        return SubscriptClipEm["Helvetica"];
    }

    /// <summary>Per-side (left, bottom, right, top) doubled clearances of a border.</summary>
    private static (double l, double b, double r, double t) DoubledOutsets(BorderInfo border)
        => (DoubledOutset(border, BorderSide.Left, border.LeftAssigned, border.RawLeft),
            DoubledOutset(border, BorderSide.Bottom, border.BottomAssigned, border.RawBottom),
            DoubledOutset(border, BorderSide.Right, border.RightAssigned, border.RawRight),
            DoubledOutset(border, BorderSide.Top, border.TopAssigned, border.RawTop));

    /// <summary>Per-side (left, bottom, right, top) insets of a pitch-mode cell box.
    /// With <paramref name="half"/> the strokes' insets: half a width on every
    /// flag-enabled side so the stroke's outer edge rides the box edge (an
    /// assignment-enabled side already insets itself in DrawBorder); without it
    /// the full stroke widths, which bound the fill and the text clip.</summary>
    private static (double l, double b, double r, double t) SideInsets(BorderInfo border, bool half)
    {
        double Side(BorderSide flag, bool assigned, GraphInfo? gi)
        {
            var w = DrawnSideWidth(border, flag, assigned, gi);
            if (!half) return w;
            return assigned && !border.Side.HasFlag(flag) ? 0 : w / 2;
        }
        return (Side(BorderSide.Left, border.LeftAssigned, border.RawLeft),
                Side(BorderSide.Bottom, border.BottomAssigned, border.RawBottom),
                Side(BorderSide.Right, border.RightAssigned, border.RawRight),
                Side(BorderSide.Top, border.TopAssigned, border.RawTop));
    }

    // ---- Bold-serif HTML cell path -------------------------------------------------
    // An HtmlFragment whose whole content is a single <b>/<strong> run renders on
    // HTML-engine metrics: embedded Times New Roman Bold at the HTML default
    // 12pt, laid out in a CSS line box (pixel-quantized normal leading over the win
    // content box — computed in BoldSerifTtf) with pair-kerned advances and no cell
    // padding. Gated on exactly this shape so all other HtmlFragment cells keep the
    // legacy plain-text path.

    // Installed-face TTF bytes by (family, bold, italic) for HonorCellTtfFaces cells;
    // a miss is cached too so unavailable faces fall through to the Standard-14 path once.
    private static readonly Dictionary<(string fam, bool bold, bool italic), byte[]?> _cellFaceTtfs = new();

    private static byte[]? _serifTtf;          // Times New Roman (root strut face)

    private static byte[]? _serifBoldTtf;      // Times New Roman Bold

    private static bool _serifTried;

    private static double _serifRootBox;       // pt: root line-box height at 12pt (13.5)

    private static double _serifBaseDrop;      // pt: line-box top → baseline (10.79883)

    private static double _serifDescFrac;      // usWinDescent / upm (descent per pt of size)

    /// <summary>Resolve the serif faces (regular + bold Times New Roman) and the root
    /// CSS line-box metrics: the hhea line height rounds to whole CSS pixels (12pt em =
    /// 16px), the surplus over the win content box splits into half-leading, the baseline
    /// sits winAscent + halfLead below the box top. Every line of an HTML-engine cell
    /// occupies the ROOT 12pt box regardless of its own run sizes; the cell's content
    /// height ends at lastBaseline + winDescent·lastSize. (Exact for
    /// serif/sans faces at 9-13pt.) Null when the faces are unavailable.</summary>
    private static readonly object _serifInit = new();

    // Root-face metrics behind the CSS line box, kept in font units so any size resolves.
    private static double _serifUpm, _serifHheaSum, _serifWinAsc, _serifWinDesc;

    /// <summary>The CSS line box of the root serif face at <paramref name="size"/>:
    /// the hhea line height rounds to whole CSS pixels, the surplus over the win
    /// content box splits into half-leading, and the baseline sits winAscent +
    /// half-leading below the box top. Returns (box height, baseline drop) in points.</summary>
    private static (double Box, double Drop) SerifLineBox(double size)
    {
        if (_serifUpm <= 0 || size <= 0) return (0, 0);
        var pxem = size * 96.0 / 72.0;
        var lpx = Math.Round(_serifHheaSum * pxem / _serifUpm, MidpointRounding.AwayFromZero);
        var lunits = lpx * _serifUpm / pxem;
        var halfLead = (lunits - (_serifWinAsc + _serifWinDesc)) / 2;
        return (lunits * size / _serifUpm, (_serifWinAsc + halfLead) * size / _serifUpm);
    }


    /// <summary>A span's inline style, as the engine dialect reads it: colour (named or
    /// #hex), an own font-size (px scales at 0.75 pt/px; pt and UNITLESS are points -
    /// probed 2026-08-28: font-size:33 with no unit renders 33pt glyphs, font-size:12
    /// renders 12pt), and text-decoration: underline.</summary>
    private static (Color? Color, double Size, bool Underline) ParseSpanStyle(string tag)
    {
        Color? color = null;
        double size = 0;
        var underline = false;
        var style = Regex.Match(tag, "style" + @"\s*=\s*(?:'(?<v>[^']*)'|""(?<v>[^""]*)"")",
            RegexOptions.IgnoreCase);
        if (!style.Success) return (null, 0, false);
        var css = style.Groups["v"].Value;
        var cm = Regex.Match(css, @"(?:^|;)\s*color(?!-)\s*:\s*(?<c>#[0-9a-fA-F]{3,8}|[A-Za-z]+)",
            RegexOptions.IgnoreCase);
        if (cm.Success)
        {
            var cv = cm.Groups["c"].Value;
            try
            {
                var sys = cv.StartsWith('#')
                    ? System.Drawing.ColorTranslator.FromHtml(cv)
                    : System.Drawing.Color.FromName(cv);
                if (sys.IsKnownColor || cv.StartsWith('#')) color = Color.FromRgb(sys);
            }
            catch { }
        }
        var fm = Regex.Match(css, @"font-size\s*:\s*(?<n>[\d.]+)\s*(?<u>px|pt)?",
            RegexOptions.IgnoreCase);
        if (fm.Success && double.TryParse(fm.Groups["n"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var fv) && fv > 0)
            size = string.Equals(fm.Groups["u"].Value, "px", StringComparison.OrdinalIgnoreCase)
                ? fv * 0.75 : fv;
        if (Regex.IsMatch(css, @"text-decoration\s*:[^;]*underline", RegexOptions.IgnoreCase))
            underline = true;
        return (color, size, underline);
    }

    // ── The escaped-newline footer fragment ────────────────────────────────
    // A footer HtmlFragment authored as ONE source line whose newlines are the
    // literal two-character "\n" sequences (the author escaped them). Measured
    // directly, the whole rendering falls out of
    // parsing the markup exactly as written:
    //  · the "\n" pairs are TEXT and draw as backslash+n glyph runs;
    //  · every <style> declaration starts with that "\n" junk, so CSS error
    //    recovery drops them ALL — the serif default (Times 12, 13.5 pt boxes)
    //    typesets everything;
    //  · attribute values written as \"…\" are unquoted values starting with a
    //    backslash — invalid, so colspan and text-align are ignored (the title
    //    <th> is confined to column 1 and every th centres, the HTML default);
    //  · stray "\n" text between the table's structural tags foster-parents to
    //    one run ABOVE the table (centred inside <center>, at the band's left
    //    edge outside it);
    //  · columns get the HTML default chrome (cellspacing 2px, cellpadding
    //    1px) across the full band width, distributed min-content plus the
    //    surplus in proportion to (max − min) — all five measured columns
    //    reproduce to 0.01 pt.

    /// <summary>Exact Standard-14 advance width (no wrap-inflation) for positioning inline
    /// cell runs, so a graph/text sequence lands where the generator places it.</summary>
    // Cache of glyph-outline parsers keyed by the raw TTF bytes, so a per-segment embedded
    // font (e.g. NotoSans / NotoSansArabic) is measured with its real advances once.
    private static readonly Dictionary<byte[], Aspose.Pdf.Text.GlyphOutlineParser?> _inlineGlyphParsers =
        new(ReferenceEqualityComparer.Instance);

}
