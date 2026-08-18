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
        var pageHeight = page.Height;

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
                        var fragFontSize = ResolveFragmentFontSize(tf, fontSize);
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
        if (string.IsNullOrWhiteSpace(ColumnWidths))
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
            // AutoFitToContent with no explicit widths: each column shrinks to its
            // MIN-content width — the widest single unbreakable word across its cells
            // (plus padding). Multi-word content therefore wraps rather than widening
            // the column ("Crossing Type" sizes its column to "Crossing" and wraps).
            if (ColumnAdjustment == ColumnAdjustment.AutoFitToContent)
            {
                var cols = 1;
                for (var i = 0; i < Rows.Count; i++)
                    if (Rows.At(i).Cells.Count > cols) cols = Rows.At(i).Cells.Count;
                var w = new double[cols];
                for (var ri = 0; ri < Rows.Count; ri++)
                {
                    var row = Rows.At(ri);
                    for (var ci = 0; ci < row.Cells.Count && ci < cols; ci++)
                    {
                        var cell = row.Cells[ci];
                        var pad = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
                        // A bold-serif HTML cell with no declared padding butts its column
                        // against the text width exactly (no padding is added).
                        var need = MaxWordWidth(cell, row) + (pad is null && AllBoldSerifHtml(cell)
                            ? 0
                            : (pad?.Left ?? 2) + (pad?.Right ?? 2));
                        if (need > w[ci]) w[ci] = need;
                    }
                }
                for (var i = 0; i < cols; i++) if (w[i] <= 0) w[i] = 100;
                // Never exceed the usable band: proportionally shrink an over-wide
                // auto-fit table to the page content width.
                if (availableWidth > 0)
                {
                    double total = 0;
                    foreach (var cw in w) total += cw;
                    if (total > availableWidth + 1e-3)
                        for (var i = 0; i < w.Length; i++) w[i] *= availableWidth / total;
                }
                return w;
            }
            // Default: one column per max cells in any row, 100pt each
            var maxCols = 1;
            for (var i = 0; i < Rows.Count; i++)
            {
                var cc = Rows.At(i).Cells.Count;
                if (cc > maxCols) maxCols = cc;
            }
            var result = new double[maxCols];
            for (var i = 0; i < maxCols; i++) result[i] = 100;
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
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                && !double.TryParse(num.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out w))
                w = 100;
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

        // Clamp to the page's usable width: when the (fixed) column widths overflow
        // the content area, shrink them proportionally to fit — matching the
        // generator's shrink-to-window behaviour. Without this a column wider than
        // the page (e.g. ColumnWidths="600" inside a ~415pt content band) pushes the
        // cell content — notably an image fitted to the cell width — off the page's
        // right edge. Skipped when RepeatingColumnsCount > 0, where an over-wide table
        // is sliced across pages (column-pagination) rather than shrunk, and for
        // Broken=VerticalInSamePage, where the overflow columns wrap into bands
        // stacked below on the same page.
        if (clampToAvailable && RepeatingColumnsCount == 0 && Broken != TableBroken.VerticalInSamePage
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
        var leftOff = FlowLeftOffset > 0 ? FlowLeftOffset : (Left + (Margin?.Left ?? 0));
        var usable = page.Width - 2 * leftOff;
        if (usable <= 0) usable = page.Width - leftOff - 36;
        return usable > 0 ? usable : page.Width;
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
                    var fragFontSize = ResolveFragmentFontSize(tf, fontSize);
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

    private void BuildRow(ContentStreamBuilder builder, Row row, double rowHeight,
        double[] colWidths, double tableX, double currentY, string fontName)
    {
        var cellX = tableX;
        for (var colIdx = 0; colIdx < row.Cells.Count && colIdx < colWidths.Length; colIdx++)
        {
            var cell = row.Cells.At(colIdx);
            var cellWidth = GetCellWidth(colWidths, colIdx, cell.ColSpan);
            var cellTopY = currentY;

            var padding = cell.Margin ?? row.DefaultCellPadding ?? DefaultCellPadding;
            var padLeft = padding?.Left ?? 2;
            var padRight = padding?.Right ?? 2;
            var padTop = padding?.Top ?? 2;

            var bgColor = cell.BackgroundColor ?? row.BackgroundColor;
            if (bgColor is not null)
            {
                builder.SetFillColor(bgColor);
                builder.Rectangle(cellX, cellTopY - rowHeight, cellWidth, rowHeight);
                builder.Fill();
            }

            if (!cell.IsNoBorder)
            {
                var cellBorder = cell.Border ?? row.DefaultCellBorder ?? row.Border ?? DefaultCellBorder;
                if (cellBorder is not null)
                    DrawBorder(builder, cellBorder, cellX, cellTopY - rowHeight, cellWidth, rowHeight);
            }

            var textState = cell.DefaultCellTextState ?? row.DefaultCellTextState ?? DefaultCellTextState;
            var fontSize = ResolveCellFontSize(cell, row);
            var textX = cellX + padLeft;
            var textY = cellTopY - padTop - fontSize;

            foreach (var paragraph in cell.Paragraphs)
            {
                string? cellText = null;
                double fragFontSize = fontSize;

                if (paragraph is TextFragment tf)
                {
                    cellText = tf.Text;
                    fragFontSize = ResolveFragmentFontSize(tf, fontSize);

                    if (tf.TextState.ForegroundColor is { } fg)
                        builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
                    else if (textState?.ForegroundColor is { } tsFg)
                        builder.SetFillColor(tsFg.R / 255.0, tsFg.G / 255.0, tsFg.B / 255.0);
                    else
                        builder.SetFillColor(0, 0, 0);
                }
                else if (paragraph is HtmlFragment html)
                {
                    cellText = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
                    builder.SetFillColor(0, 0, 0);
                }

                if (cellText is not null && cellText.Length > 0)
                {
                    var effectiveTextY = cellTopY - padTop - fragFontSize;

                    builder.BeginText();
                    builder.SetFont(fontName, fragFontSize);
                    builder.MoveTextPosition(textX, effectiveTextY);

                    if (cell.IsWordWrapped && cellText.Length > 0)
                    {
                        var availWidth = cellWidth - padLeft - (padding?.Right ?? 2);
                        var lines = WrapText(cellText, fragFontSize, availWidth);
                        for (var li = 0; li < lines.Count; li++)
                        {
                            if (li > 0)
                                builder.MoveTextPosition(0, -fragFontSize * 1.2);
                            builder.ShowText(lines[li]);
                        }
                    }
                    else
                    {
                        builder.ShowText(cellText);
                    }

                    builder.EndText();
                    textY -= fragFontSize * 1.2;
                }
            }

            cellX += cellWidth;
        }
    }

    /// <summary>Form-grid border: the band paints INSIDE the given box, each side
    /// stroked over the box's FULL extent so the corners paint — the
    /// side lines run corner to corner; four inset segments would leave a notch
    /// at every corner.</summary>
    private static void DrawFormGridBorder(ContentStreamBuilder builder, BorderInfo border,
        double x, double y, double w, double h)
    {
        var half = border.Width / 2;
        DrawSide(builder, border, border.RawBottom, x, y + half, x + w, y + half);
        DrawSide(builder, border, border.RawTop, x, y + h - half, x + w, y + h - half);
        DrawSide(builder, border, border.RawLeft, x + half, y, x + half, y + h);
        DrawSide(builder, border, border.RawRight, x + w - half, y, x + w - half, y + h);
    }

    private static void DrawBorder(ContentStreamBuilder builder, BorderInfo border, double x, double y, double w, double h)
    {
        // Rounded box: when a radius is set on a full-box border, stroke a single rounded-corner
        // rectangle path instead of four straight sides (BorderInfo.RoundedBorderRadius).
        if (border.RoundedBorderRadius > 0 && border.Side.HasFlag(BorderSide.Box))
        {
            DrawRoundedBox(builder, border, x, y, w, h);
            return;
        }

        // A uniformly-styled full box with a dash pattern is stroked as one continuous rectangle
        // path so the dashes wrap around the corners in phase — matching the generator.
        // Drawing the four sides separately would restart the dash at every corner and drift the
        // segments out of alignment with the template.
        if (border.Side.HasFlag(BorderSide.Box) && IsUniformBox(border) && border.RawTop?.DashArray is { Length: > 0 })
        {
            var gi = border.RawTop!;
            builder.SetLineWidth(gi.LineWidth);
            if (gi.StrokeColor is { } bsc)
                builder.SetStrokeColor(bsc.R, bsc.G, bsc.B);
            else
                builder.SetStrokeColor(border.Color);
            builder.SetDashPattern(Array.ConvertAll(gi.DashArray!, d => (double)d), gi.DashPhase);
            builder.Rectangle(x, y, w, h).Stroke();
            builder.SetDashPattern(Array.Empty<double>(), 0);
            return;
        }

        // A side draws when the Side flags name it OR its GraphInfo was
        // explicitly assigned (the generator enables a side on assignment);
        // the per-side GraphInfo supplies stroke styling either way.
        // Assignment-enabled sides paint INSIDE the cell box (the stroke's
        // outer edge on the box edge — adjacent cells show abutting double
        // bands); flag-enabled sides keep the legacy
        // centered stroke.
        double Inset(bool assigned, bool flagged, GraphInfo? gi) =>
            assigned && !flagged ? (gi?.LineWidth > 0 ? gi.LineWidth : border.Width) / 2 : 0;
        if (border.Side.HasFlag(BorderSide.Bottom) || border.BottomAssigned)
        {
            var ib = Inset(border.BottomAssigned, border.Side.HasFlag(BorderSide.Bottom), border.RawBottom);
            DrawSide(builder, border, border.RawBottom, x, y + ib, x + w, y + ib);
        }
        if (border.Side.HasFlag(BorderSide.Top) || border.TopAssigned)
        {
            var it = Inset(border.TopAssigned, border.Side.HasFlag(BorderSide.Top), border.RawTop);
            DrawSide(builder, border, border.RawTop, x, y + h - it, x + w, y + h - it);
        }
        if (border.Side.HasFlag(BorderSide.Left) || border.LeftAssigned)
        {
            var il = Inset(border.LeftAssigned, border.Side.HasFlag(BorderSide.Left), border.RawLeft);
            DrawSide(builder, border, border.RawLeft, x + il, y, x + il, y + h);
        }
        if (border.Side.HasFlag(BorderSide.Right) || border.RightAssigned)
        {
            var ir = Inset(border.RightAssigned, border.Side.HasFlag(BorderSide.Right), border.RawRight);
            DrawSide(builder, border, border.RawRight, x + w - ir, y, x + w - ir, y + h);
        }
    }

    /// <summary>Width of the table's own box border — the space it claims outside the
    /// column block on each side. Zero when the table carries no full-box border.</summary>
    private double OuterBorderWidth()
    {
        if (Border is not { } b || !b.Side.HasFlag(BorderSide.Box)) return 0;
        var w = b.RawTop?.LineWidth > 0 ? b.RawTop.LineWidth : b.Width;
        return w > 0 ? w : 0;
    }

    // True when every side carries the same styling — either no per-side GraphInfo at all, or the
    // single shared instance produced by the BorderInfo(BorderSide, GraphInfo) constructor.
    private static bool IsUniformBox(BorderInfo border)
    {
        var t = border.RawTop;
        return ReferenceEquals(t, border.RawBottom)
            && ReferenceEquals(t, border.RawLeft)
            && ReferenceEquals(t, border.RawRight);
    }

    private static void DrawSide(ContentStreamBuilder builder, BorderInfo border, GraphInfo? gi,
        double x1, double y1, double x2, double y2)
    {
        builder.SetLineWidth(gi is not null ? gi.LineWidth : border.Width);
        if (gi?.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        else
            builder.SetStrokeColor(border.Color);

        var dash = gi?.DashArray;
        var dashed = dash is { Length: > 0 };
        if (dashed)
            builder.SetDashPattern(Array.ConvertAll(dash!, d => (double)d), gi!.DashPhase);

        builder.MoveTo(x1, y1).LineTo(x2, y2).Stroke();

        if (dashed)
            builder.SetDashPattern(Array.Empty<double>(), 0); // reset to a solid line
    }

    // 0.5523 ≈ (4/3)·(√2−1): the Bézier control-point ratio that approximates a quarter circle.
    private const double RoundCornerKappa = 0.5522847498307936;

    // Super/subscript segments in a cell render at a reduced size with a baseline shift
    // (fractions of the base font size), matching the generator's metrics.
    private const double SubSuperScale = 0.583;

    private const double SuperscriptRise = 0.421;

    private const double SubscriptRise = 0.245;

    private static void DrawRoundedBox(ContentStreamBuilder builder, BorderInfo border, double x, double y, double w, double h)
    {
        var gi = border.RawTop; // a box created from a GraphInfo shares one instance across all sides
        builder.SetLineWidth(gi is not null ? gi.LineWidth : border.Width);
        if (gi?.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        else
            builder.SetStrokeColor(border.Color);

        var dash = gi?.DashArray;
        var dashed = dash is { Length: > 0 };
        if (dashed)
            builder.SetDashPattern(Array.ConvertAll(dash!, d => (double)d), gi!.DashPhase);

        // Clamp the radius so the corner arcs never overlap on a small box.
        var r = Math.Min(border.RoundedBorderRadius, Math.Min(w, h) / 2);
        var k = r * RoundCornerKappa;

        builder.MoveTo(x + r, y)
            .LineTo(x + w - r, y)
            .CurveTo(x + w - r + k, y, x + w, y + r - k, x + w, y + r) // bottom-right
            .LineTo(x + w, y + h - r)
            .CurveTo(x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h) // top-right
            .LineTo(x + r, y + h)
            .CurveTo(x + r - k, y + h, x, y + h - r + k, x, y + h - r) // top-left
            .LineTo(x, y + r)
            .CurveTo(x, y + r - k, x + r - k, y, x + r, y) // bottom-left
            .ClosePath()
            .Stroke();

        if (dashed)
            builder.SetDashPattern(Array.Empty<double>(), 0);
    }

    /// <summary>
    /// Simple word-wrap: splits text into lines that fit within the available width.
    /// Uses an approximate character width of 0.5 * fontSize for Helvetica.
    /// </summary>
    private static List<string> WrapText(string text, double fontSize, double availWidth,
        Func<string, double>? measure = null, bool overflowLongWords = false)
    {
        var lines = new List<string>();
        if (availWidth <= 0 || fontSize <= 0)
        {
            lines.Add(text);
            return lines;
        }

        // Measure with real Helvetica AFM widths instead of a flat 0.5 em
        // estimate — the old estimate let noticeably more characters per
        // line than GDI+ and under-counted page breaks for long cell text.
        // A caller that sized the column itself passes the very measure it used,
        // so the column and the wrap agree to the last bit.
        double MeasureWidth(string s, double sz) => measure is null ? MeasureWidthDefault(s, sz) : measure(s);
        var words = text.Split(' ');
        var spaceW = MeasureWidth(" ", fontSize);
        string currentLine = "";
        double currentWidth = 0;

        // A single word wider than the column splits at character level
        // ("Jurisdiction" in a squeezed 24 pt column renders as
        // "Juris/dictio/n"), filling each line to the width. A hyphen or en-dash
        // inside the word is a soft break opportunity tried FIRST ("B13-9876"
        // wraps to "B13-"/"9876"); only a segment still too wide char-splits.
        void StartWithWord(string word, double wordW)
        {
            // A column auto-fit to exactly this word's width must accept it — the
            // width comparison tolerates the last-bit error the pad add/subtract
            // round-trip introduces.
            if (wordW <= availWidth + 1e-6) { currentLine = word; currentWidth = wordW; return; }
            // HTML layout: a word too wide for its column spills past the cell edge —
            // the column was sized knowing that, and breaking it would show a split
            // a browser never shows.
            if (overflowLongWords) { currentLine = word; currentWidth = wordW; return; }
            if (word.IndexOf('-') > 0 || word.IndexOf('–') > 0)
            {
                var segs = new List<string>();
                var start = 0;
                for (var ci = 0; ci < word.Length; ci++)
                    if (word[ci] is '-' or '–' || ci == word.Length - 1)
                    {
                        segs.Add(word.Substring(start, ci - start + 1));
                        start = ci + 1;
                    }
                if (segs.Count > 1)
                {
                    currentLine = ""; currentWidth = 0;
                    foreach (var seg in segs)
                    {
                        var segW = MeasureWidth(seg, fontSize);
                        if (currentLine.Length == 0) { StartWithWord(seg, segW); continue; }
                        if (currentWidth + segW <= availWidth + 1e-6)
                        {
                            currentLine += seg;
                            currentWidth += segW;
                        }
                        else
                        {
                            lines.Add(currentLine);
                            StartWithWord(seg, segW);
                        }
                    }
                    return;
                }
            }
            var cur = ""; double cw = 0;
            foreach (var ch in word)
            {
                var chW = MeasureWidth(ch.ToString(), fontSize);
                if (cur.Length > 0 && cw + chW > availWidth + 1e-6)
                {
                    lines.Add(cur);
                    cur = ""; cw = 0;
                }
                cur += ch; cw += chW;
            }
            currentLine = cur;
            currentWidth = cw;
        }

        foreach (var word in words)
        {
            var wordW = MeasureWidth(word, fontSize);
            if (currentLine.Length == 0)
            {
                StartWithWord(word, wordW);
                continue;
            }
            var withSpaceW = currentWidth + spaceW + wordW;
            if (withSpaceW <= availWidth + 1e-6)
            {
                currentLine += " " + word;
                currentWidth = withSpaceW;
            }
            else if (!TryZeroWidthSplit(word))
            {
                lines.Add(currentLine);
                StartWithWord(word, wordW);
            }
        }

        // U+200B is invisible and carries no advance, but it IS a legal wrap point. A
        // word that will not fit whole is retried at its zero-width spaces, so a line
        // packs the way a browser packs it instead of pushing the whole run down.
        bool TryZeroWidthSplit(string word)
        {
            if (word.IndexOf(ZeroWidthSpace) < 0) return false;
            var segs = new List<string>();
            var segStart = 0;
            for (var ci = 0; ci < word.Length; ci++)
                if (word[ci] == ZeroWidthSpace || ci == word.Length - 1)
                {
                    segs.Add(word.Substring(segStart, ci - segStart + 1));
                    segStart = ci + 1;
                }
            if (segs.Count < 2) return false;
            var needSpace = true;
            foreach (var seg in segs)
            {
                // A segment that is nothing but the break character carries no ink and
                // no box: breaking AT a zero-width space must not leave an empty line.
                if (seg.Trim(ZeroWidthSpace).Length == 0)
                {
                    if (currentLine.Length > 0) currentLine += seg;
                    continue;
                }
                var segW = MeasureWidth(seg, fontSize);
                if (currentLine.Length == 0) { StartWithWord(seg, segW); needSpace = false; continue; }
                var add = (needSpace ? spaceW : 0) + segW;
                if (currentWidth + add <= availWidth + 1e-6)
                {
                    currentLine += (needSpace ? " " : "") + seg;
                    currentWidth += add;
                }
                else
                {
                    lines.Add(currentLine);
                    currentLine = ""; currentWidth = 0;
                    StartWithWord(seg, segW);
                }
                needSpace = false;
            }
            return true;
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine);

        if (lines.Count == 0)
            lines.Add("");

        return lines;
    }

    /// <summary>
    /// Measure a string's width in points using Arial glyph widths (1/1000
    /// text units scaled by font size). Arial is what GDI+ defaults to for
    /// HTML/table text when no explicit font is set, and it is ~8% wider
    /// than Helvetica on mixed lowercase text. Using Arial widths here
    /// puts our wrap breakpoints in line with the expected layout.
    /// Characters outside WinAnsi fall back to the font's default width.
    /// </summary>
    /// <summary>Widest single unbreakable word across a cell's paragraphs, at each
    /// paragraph's effective font size. Drives AutoFitToContent column sizing: the
    /// column must be at least this wide so no word is split, but multi-word content
    /// wraps within it.</summary>
    private double MaxWordWidth(Cell cell, Row row)
    {
        var cellFs = ResolveCellFontSize(cell, row);
        double max = 0;
        foreach (var p in cell.Paragraphs)
        {
            string? text;
            var fs = cellFs;
            if (p is Text.TextFragment tf) { text = tf.Text; fs = ResolveFragmentFontSize(tf, cellFs); }
            else if (p is HtmlFragment h)
            {
                // Bold-serif HTML cell: the column sizes to the kerned Times New Roman
                // Bold advance at the HTML default size, not the Helvetica estimate.
                if (TryBoldOnlyHtml(h.HtmlContent, out var boldText) && BoldSerifTtf() is { } serifTtf)
                {
                    var bw = MeasureWidthKerned(boldText, HtmlCellFontSize, serifTtf);
                    if (bw > max) max = bw;
                    continue;
                }
                text = HtmlFragment.StripHtmlTags(h.HtmlContent ?? string.Empty);
            }
            else continue;
            if (string.IsNullOrEmpty(text)) continue;
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                foreach (var word in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var wWidth = MeasureWidth(word, fs);
                    if (wWidth > max) max = wWidth;
                }
        }
        return max;
    }

    // ---- Bold-serif HTML cell path -------------------------------------------------
    // An HtmlFragment whose whole content is a single <b>/<strong> run renders on
    // HTML-engine metrics: embedded Times New Roman Bold at the HTML default
    // 12pt, laid out in a CSS line box (pixel-quantized normal leading over the win
    // content box — computed in BoldSerifTtf) with pair-kerned advances and no cell
    // padding. Gated on exactly this shape so all other HtmlFragment cells keep the
    // legacy plain-text path.

    private const double HtmlCellFontSize = 12.0;

    private static readonly Regex BoldOnlyHtmlRegex = new(
        @"^\s*<(b|strong)\b[^>]*>(?<t>[^<>]+)</\1\s*>\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool TryBoldOnlyHtml(string? html, out string text)
    {
        text = "";
        if (string.IsNullOrEmpty(html)) return false;
        var m = BoldOnlyHtmlRegex.Match(html);
        if (!m.Success) return false;
        text = HtmlFragment.StripHtmlTags(m.Groups["t"].Value);
        return text.Length > 0;
    }

    private const double HtmlSmallFontSize = 10.0;

    // Installed-face TTF bytes by (family, bold, italic) for HonorCellTtfFaces cells;
    // a miss is cached too so unavailable faces fall through to the Standard-14 path once.
    private static readonly Dictionary<(string fam, bool bold, bool italic), byte[]?> _cellFaceTtfs = new();

    /// <summary>The form-grid measure path's advance for a char the base face does
    /// not map: Verdana's OS/2 xAvgCharWidth (1229 units of the 2048 em). The
    /// name-row wrap point pins it (words 1..10 fit its 285pt box,
    /// word 11 overflows; only an advance in (0.510, 0.605] em satisfies both).</summary>
    private const double FormGridUnmappedAdvanceEm = 1229.0 / 2048.0;

    /// <summary>The installed variant's face name ("Verdana Bold Italic").</summary>
    private static string CellFaceName(string family, bool bold, bool italic) =>
        bold && italic ? family + " Bold Italic"
        : bold ? family + " Bold"
        : italic ? family + " Italic"
        : family;

    private static byte[]? CellFaceTtf(string family, bool bold, bool italic = false)
    {
        lock (_cellFaceTtfs)
        {
            if (_cellFaceTtfs.TryGetValue((family, bold, italic), out var cached)) return cached;
            byte[]? ttf = null;
            try { ttf = Aspose.Pdf.Text.FontRepository.GetTtfData(CellFaceName(family, bold, italic)); }
            catch { }
            if (ttf is null && (bold || italic))
                try { ttf = Aspose.Pdf.Text.FontRepository.GetTtfData(family); }
                catch { }
            _cellFaceTtfs[(family, bold, italic)] = ttf;
            return ttf;
        }
    }

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

    private static byte[]? BoldSerifTtf()
    {
        // The corpus runs fixtures 8-wide: the resolve must be atomic, or a second
        // thread can read half-written metrics (upm set, win ascent not) and lay a
        // cell out on a zero line box.
        lock (_serifInit) return BoldSerifTtfCore();
    }

    private static byte[]? BoldSerifTtfCore()
    {
        if (_serifTried) return _serifBoldTtf;
        _serifTried = true;
        try
        {
            var reg = Aspose.Pdf.Text.FontRepository.GetTtfData("Times New Roman");
            var bold = Aspose.Pdf.Text.FontRepository.GetTtfData("Times New Roman Bold");
            if (reg is not null && bold is not null)
            {
                var tp = new Aspose.Pdf.Text.TrueTypeParser(reg);
                tp.Parse();
                if (tp.UsWinAscent > 0 && tp.UnitsPerEm > 0)
                {
                    _serifUpm = tp.UnitsPerEm;
                    _serifHheaSum = tp.Ascent + Math.Abs(tp.Descent) + tp.LineGap;
                    _serifWinAsc = tp.UsWinAscent;
                    _serifWinDesc = tp.UsWinDescent;
                    (_serifRootBox, _serifBaseDrop) = SerifLineBox(HtmlCellFontSize);
                    _serifDescFrac = tp.UsWinDescent / _serifUpm;
                    _serifTtf = reg;
                    _serifBoldTtf = bold;
                }
            }
        }
        catch { /* faces unavailable: the legacy path stays */ }
        return _serifBoldTtf;
    }

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

    /// <summary>The regular serif face (resolved together with the bold one).</summary>
    private static byte[]? SerifTtf()
    {
        BoldSerifTtf();
        return _serifTtf;
    }

    /// <summary>One styled run on an HTML-engine cell line: text at an x-offset from the
    /// cell content-left, regular or bold serif, at its own size.</summary>
    private sealed class HtmlRun
    {
        public string Text = "";
        public double X;
        public double Size;
        public bool Bold;
        /// The href of the enclosing anchor, when this run sits inside one.
        public string? Url;
    }

    private static readonly Regex HtmlEngineTagRegex = new(
        @"<(/?)(b|strong|small|div|br|p|a)\b[^>]*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HrefRegex = new(
        @"\bhref\s*=\s*(?:'(?<u>[^']*)'|""(?<u>[^""]*)""|(?<u>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnyTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    /// <summary>Decode the common HTML entities in tag-free text WITHOUT trimming or
    /// tag-stripping (unlike <see cref="HtmlFragment.StripHtmlTags"/>), so inter-word
    /// spaces at run boundaries survive.</summary>
    private static string DecodeHtmlEntities(string s) => s
        .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
        .Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&#39;", "'")
        .Replace("&nbsp;", " ");

    /// <summary>Parse an HtmlFragment whose markup uses only the b/strong/small/div/br
    /// family into HTML-engine cell lines: serif runs (bold via b/strong, 10pt via small
    /// — nested smalls do NOT compound), greedy kerned wrap at <paramref name="availWidth"/>,
    /// div/small as block boundaries, br as a forced (possibly empty) line. Returns null
    /// when the markup falls outside the family (legacy path) or the faces are missing.</summary>
    private static List<CellLine>? ParseHtmlEngineCell(string? html, double availWidth,
        double baseSize = HtmlCellFontSize, bool breakWords = false)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf('<') < 0) return null;
        if (BoldSerifTtf() is null) return null;
        if (baseSize <= 0) baseSize = HtmlCellFontSize;
        var smallSize = baseSize * HtmlSmallFontSize / HtmlCellFontSize;
        var (rootBox, baseDrop) = SerifLineBox(baseSize);
        // Every tag present must belong to the allowed family.
        foreach (Match any in AnyTagRegex.Matches(html))
            if (!HtmlEngineTagRegex.IsMatch(any.Value)) return null;

        var lines = new List<CellLine>();
        var curRuns = new List<HtmlRun>();
        double curX = 0;
        var boldDepth = 0;
        var smallDepth = 0;
        var anyText = false;
        var anchors = new Stack<string>();

        void FlushLine(bool force)
        {
            // Trim the trailing spaces of the last run (a wrapped line never ends
            // in a visible space; fragment widths exclude it).
            while (curRuns.Count > 0)
            {
                var last = curRuns[^1];
                var trimmed = last.Text.TrimEnd(' ');
                if (trimmed.Length == 0) { curRuns.RemoveAt(curRuns.Count - 1); continue; }
                last.Text = trimmed;
                break;
            }
            if (curRuns.Count == 0 && !force) { curX = 0; return; }
            double maxSize = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var r in curRuns)
            {
                if (r.Size > maxSize) maxSize = r.Size;
                sb.Append(r.Text);
            }
            // Consecutive runs of one anchor become a single Link rectangle over exactly
            // the anchor's characters, measured with the metrics that laid the line out.
            List<(double XOff, double W, Hyperlink Link)>? linkRuns = null;
            for (var ri = 0; ri < curRuns.Count; ri++)
            {
                var url = curRuns[ri].Url;
                if (string.IsNullOrEmpty(url)) continue;
                var x0 = curRuns[ri].X;
                var end = curRuns[ri].X + MeasureWidthKerned(curRuns[ri].Text, curRuns[ri].Size,
                    curRuns[ri].Bold ? _serifBoldTtf! : _serifTtf!);
                while (ri + 1 < curRuns.Count && curRuns[ri + 1].Url == url)
                {
                    ri++;
                    end = curRuns[ri].X + MeasureWidthKerned(curRuns[ri].Text, curRuns[ri].Size,
                        curRuns[ri].Bold ? _serifBoldTtf! : _serifTtf!);
                }
                (linkRuns ??= new()).Add((x0, end - x0, new WebHyperlink(url)));
            }
            lines.Add(new CellLine
            {
                Text = sb.ToString(),
                FontSize = maxSize > 0 ? maxSize : baseSize,
                Runs = curRuns.Count > 0 ? new List<HtmlRun>(curRuns) : null,
                LinkRuns = linkRuns,
                KernTj = true,
                HtmlEngine = true,
                BoxH = rootBox,
                BaseOff = baseDrop,
            });
            curRuns.Clear();
            curX = 0;
        }

        void EmitText(string raw)
        {
            // HTML whitespace collapse: any run of whitespace is one space. The text
            // between tags carries no markup, so decode entities WITHOUT trimming — the
            // trailing space before an inline element (e.g. "Min. Fee " before <small>)
            // is a real inter-word space that must render; leading spaces at a line
            // start are dropped separately below.
            var text = DecodeHtmlEntities(Regex.Replace(raw, @"\s+", " "));
            if (text.Length == 0) return;
            if (text == " ")
            {
                // Inter-tag whitespace: a space only mid-line, never at a line start.
                if (curRuns.Count == 0 && curX == 0) return;
            }
            var size = smallDepth > 0 ? smallSize : baseSize;
            var ttf = boldDepth > 0 ? _serifBoldTtf! : _serifTtf!;
            HtmlRun? run = null;   // runs split at tag boundaries: one piece = one run chain
            foreach (var token in SplitKeepingSpaces(text))
            {
                if (curRuns.Count == 0 && curX == 0 && token.TrimStart(' ').Length == 0) continue;
                var tokenText = curRuns.Count == 0 && curX == 0 && run is null ? token.TrimStart(' ') : token;
                if (tokenText.Length == 0) continue;
                var w = MeasureWidthKerned(tokenText, size, ttf);
                var visible = tokenText.TrimEnd(' ');
                var visibleW = visible.Length == tokenText.Length ? w : MeasureWidthKerned(visible, size, ttf);
                if (availWidth > 0 && curX + visibleW > availWidth + 1e-6 && (curRuns.Count > 0 || curX > 0))
                {
                    FlushLine(force: false);
                    run = null;
                    tokenText = tokenText.TrimStart(' ');
                    if (tokenText.Length == 0) continue;
                    w = MeasureWidthKerned(tokenText, size, ttf);
                }
                // IsBreakWords: a word still too wide for an EMPTY line breaks inside
                // itself, as many characters as fit per line, instead of overflowing
                // the column (which is what the flag off does).
                while (breakWords && availWidth > 0 && curX <= 1e-6
                       && MeasureWidthKerned(tokenText, size, ttf) > availWidth + 1e-6)
                {
                    var fit = 0;
                    while (fit + 1 < tokenText.Length
                           && MeasureWidthKerned(tokenText[..(fit + 1)], size, ttf) <= availWidth + 1e-6)
                        fit++;
                    if (fit <= 0) break;
                    curRuns.Add(new HtmlRun
                    {
                        Text = tokenText[..fit], X = 0, Size = size, Bold = boldDepth > 0,
                        Url = anchors.Count > 0 ? anchors.Peek() : null,
                    });
                    anyText = true;
                    FlushLine(force: false);
                    run = null;
                    tokenText = tokenText[fit..];
                    if (tokenText.Length == 0) break;
                    w = MeasureWidthKerned(tokenText, size, ttf);
                }
                if (tokenText.Length == 0) continue;
                if (run is null)
                {
                    run = new HtmlRun
                    {
                        Text = tokenText, X = curX, Size = size, Bold = boldDepth > 0,
                        Url = anchors.Count > 0 ? anchors.Peek() : null,
                    };
                    curRuns.Add(run);
                }
                else run.Text += tokenText;
                curX += w;
                anyText = true;
            }
        }

        var pos = 0;
        foreach (Match m in HtmlEngineTagRegex.Matches(html))
        {
            if (m.Index > pos) EmitText(html.Substring(pos, m.Index - pos));
            pos = m.Index + m.Length;
            var closing = m.Groups[1].Value.Length > 0;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            switch (tag)
            {
                case "b" or "strong":
                    boldDepth += closing ? -1 : 1;
                    if (boldDepth < 0) boldDepth = 0;
                    break;
                case "small":
                    // Inline: size drops to 10pt (no compounding when nested); the line
                    // structure comes from div/br only.
                    smallDepth += closing ? -1 : 1;
                    if (smallDepth < 0) smallDepth = 0;
                    break;
                case "div" or "p":
                    FlushLine(force: false);  // block boundary on open AND close
                    break;
                case "a":
                    // Inline anchor: its runs draw like their neighbours and carry the
                    // href so the line can annotate them.
                    if (closing) { if (anchors.Count > 0) anchors.Pop(); }
                    else
                    {
                        var href = HrefRegex.Match(m.Value);
                        anchors.Push(href.Success ? href.Groups["u"].Value.Trim() : "");
                    }
                    break;
                case "br":
                    FlushLine(force: true);   // forced line — empty box when nothing pending
                    break;
            }
        }
        if (pos < html.Length) EmitText(html.Substring(pos));
        FlushLine(force: false);

        if (!anyText || lines.Count == 0) return null;
        // The cell's content box ends at the LAST baseline + the last line's win
        // descent — not at the full line-box bottom (no bottom leading).
        var last = lines[^1];
        last.BoxH = baseDrop + _serifDescFrac * last.FontSize;
        return lines;
    }

    /// <summary>Draw an HtmlFragment's HTML-engine lines with the fragment's content box
    /// top-left at (<paramref name="x"/>, <paramref name="topY"/>) — the same serif line
    /// model a table cell uses, so a fragment hosted outside a cell (a FloatingBox child)
    /// sets in the identical face, size and rhythm. Returns the height consumed, or null
    /// when the markup falls outside the engine family (the caller keeps its own path).</summary>
    internal static double? DrawHtmlEngineFragment(ContentStreamBuilder b, Page page,
        string? html, double x, double topY, double availWidth)
    {
        if (page is null) return null;
        if (ParseHtmlEngineCell(html, availWidth) is not { Count: > 0 } lines) return null;
        var fontDict = ResolvePageFontDict(page);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineBase = topY - _serifBaseDrop - i * _serifRootBox;
            if (lines[i].Runs is not { Count: > 0 } runs) continue;
            foreach (var run in runs)
            {
                if (run.Text.Length == 0) continue;
                var ttf = run.Bold ? _serifBoldTtf : _serifTtf;
                if (ttf is null) continue;
                var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    fontDict, ttf, run.Bold ? "Times New Roman Bold" : "Times New Roman",
                    run.Text, stripSpacesInBaseFont: true);
                b.BeginText();
                b.SetFont(resName, run.Size);
                b.MoveTextPosition(x + run.X, lineBase);
                if (KernAdjustments(run.Text, ttf) is { } kern) b.ShowTextHexKerned(hex, kern);
                else b.ShowTextHex(hex);
                b.EndText();
            }
        }
        // The last line ends at its own baseline + descent, not at a full box.
        return (lines.Count - 1) * _serifRootBox + lines[^1].BoxH;
    }

    /// <summary>True when every paragraph of the cell is a bold-only HtmlFragment (and the
    /// serif face resolves), i.e. the cell lays out on HTML-engine metrics
    /// with zero autofit padding.</summary>
    private static bool AllBoldSerifHtml(Cell cell)
    {
        if (cell.Paragraphs.Count == 0 || BoldSerifTtf() is null) return false;
        foreach (var p in cell.Paragraphs)
            if (p is not HtmlFragment h || !TryBoldOnlyHtml(h.HtmlContent, out _)) return false;
        return true;
    }

    /// <summary>Width of <paramref name="s"/> in points using the embedded font's real
    /// advances plus 'kern' pair adjustments — HTML-engine runs are kerned,
    /// and autofit columns size to the kerned width exactly.</summary>
    private static double MeasureWidthKerned(string s, double fontSize, byte[] ttf)
    {
        var gp = GetInlineGlyphParser(ttf);
        if (gp is null) return MeasureWidthExact(s, fontSize);
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
        double total = 0;
        var prev = -1;
        foreach (var ch in s)
        {
            var gid = gp.GlyphIdOrLookAlike(ch);
            if (IsZeroAdvanceMark(ch)) continue;   // a combining mark occupies no width
            if (prev >= 0) total += gp.GetKernAdjustment(prev, gid);
            total += gp.GetAdvanceWidth(gid);
            prev = gid;
        }
        return total * fontSize / upm;
    }

    /// <summary>A combining mark — one that draws ON the character beside it (an enclosing
    /// circle round an option letter, an accent) — occupies NO advance of its own. A face
    /// that has no glyph for one would otherwise spend its whole missing-glyph advance on
    /// it, widening the run and the column that has to hold it.</summary>
    private static bool IsZeroAdvanceMark(char c) =>
        System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
            is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.EnclosingMark;

    /// <summary>TJ adjustment array (thousandths of text space; positive pulls the following
    /// glyphs left) for the pair-kerning of <paramref name="s"/>, or null when no pair kerns.</summary>
    private static double[]? KernAdjustments(string s, byte[] ttf)
    {
        if (s.Length < 2) return null;
        var gp = GetInlineGlyphParser(ttf);
        if (gp is null) return null;
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
        double[]? adj = null;
        var prev = -1;
        for (var i = 0; i < s.Length; i++)
        {
            var gid = gp.GlyphIdOrLookAlike(s[i]);
            // A combining mark draws where the pen already is: pull the run back by the
            // whole advance the face would otherwise spend on it, so the character it
            // marks sits directly under it.
            if (IsZeroAdvanceMark(s[i]) && i < s.Length - 1)
            {
                adj ??= new double[s.Length - 1];
                adj[i] += gp.GetAdvanceWidth(gid) * 1000.0 / upm;
                continue;
            }
            if (prev >= 0)
            {
                var kern = gp.GetKernAdjustment(prev, gid);
                if (kern != 0)
                {
                    adj ??= new double[s.Length - 1];
                    adj[i - 1] += -kern * 1000.0 / upm;
                }
            }
            prev = gid;
        }
        return adj;
    }

    /// <summary>True when a CSS font-family resolves to a serif face the HTML
    /// engine substitutes with its embedded serif (Times New Roman) family.</summary>
    private static bool IsSerifCssFamily(string? family)
    {
        if (string.IsNullOrEmpty(family)) return false;
        return family.IndexOf("georgia", StringComparison.OrdinalIgnoreCase) >= 0
            || family.IndexOf("times", StringComparison.OrdinalIgnoreCase) >= 0
            || family.IndexOf("serif", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Greedy word wrap measured with the embedded face's kerned advances
    /// (the metrics the styled serif cell line renders with).</summary>
    private static List<string> WrapKernedLines(string s, double size, byte[] ttf, double avail)
    {
        var res = new List<string>();
        var cur = "";
        foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var cand = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && avail > 0 && MeasureWidthKerned(cand, size, ttf) > avail + 1e-6)
            { res.Add(cur); cur = word; }
            else cur = cand;
        }
        if (cur.Length > 0) res.Add(cur);
        return res;
    }

    private static double MeasureWidth(string s, double fontSize) => MeasureWidthDefault(s, fontSize);

    private static double MeasureWidthDefault(string s, double fontSize)
    {
        var total = 0;
        foreach (var ch in s)
        {
            var code = (int)ch;
            int w;
            if (code >= 0 && code <= 255)
            {
                w = Standard14Fonts.GetWidth("Helvetica", code);
                if (w <= 0) w = Standard14Fonts.GetDefaultWidth("Helvetica");
            }
            else
            {
                w = Standard14Fonts.GetDefaultWidth("Helvetica");
            }
            total += w;
        }
        // Arial (the layout engine's default) is only marginally wider than
        // the Helvetica AFM widths used here, so apply a small ~5% inflation rather than a
        // heavy fudge — an over-large factor wraps cell text a word too early (e.g. a
        // "living facility" run that fits the column gets split across two lines).
        return total * fontSize * 1.05 / 1000.0;
    }

    /// <summary>Exact Standard-14 advance width (no wrap-inflation) for positioning inline
    /// cell runs, so a graph/text sequence lands where the generator places it.</summary>
    // Cache of glyph-outline parsers keyed by the raw TTF bytes, so a per-segment embedded
    // font (e.g. NotoSans / NotoSansArabic) is measured with its real advances once.
    private static readonly Dictionary<byte[], Aspose.Pdf.Text.GlyphOutlineParser?> _inlineGlyphParsers =
        new(ReferenceEqualityComparer.Instance);

    private static Aspose.Pdf.Text.GlyphOutlineParser? GetInlineGlyphParser(byte[] ttf)
    {
        if (_inlineGlyphParsers.TryGetValue(ttf, out var cached)) return cached;
        Aspose.Pdf.Text.GlyphOutlineParser? p = null;
        try { p = new Aspose.Pdf.Text.GlyphOutlineParser(ttf); } catch { }
        _inlineGlyphParsers[ttf] = p;
        return p;
    }

    /// <summary>Width of <paramref name="s"/> in points using an embedded font's real glyph
    /// advances (cmap → hmtx), for laying out a per-segment Type0 inline run.</summary>
    private static double MeasureWidthWithFont(string s, double fontSize, byte[] ttf,
        double unmappedEm = 0)
    {
        var gp = GetInlineGlyphParser(ttf);
        if (gp is null) return MeasureWidthExact(s, fontSize);
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
        double total = 0;
        foreach (var ch in s)
        {
            if (!gp.CMap.TryGetValue(ch, out var g))
            {
                // An unmapped char at the caller's own advance (the form-grid
                // measure path prices missing glyphs at the face's average char
                // width); zero keeps the legacy
                // notdef advance.
                if (unmappedEm > 0) { total += unmappedEm * upm; continue; }
                g = 0;
            }
            total += gp.GetAdvanceWidth(g);
        }
        return total * fontSize / upm;
    }

    /// <summary>Greedy character-level width wrap for CJK cell text (which has no ASCII spaces
    /// to break at), measured with the given fallback font. Every character is preserved —
    /// including spaces at a break — so the concatenated lines reconstruct the input exactly.
    /// A single character wider than the box is left on its own overflowing line.</summary>
    private static List<string> WrapCjkToWidth(string s, double fontSize, double availWidth, byte[] ttf)
    {
        var lines = new List<string>();
        if (availWidth <= 0) { lines.Add(s); return lines; }
        var cur = new System.Text.StringBuilder();
        double curW = 0;
        foreach (var ch in s)
        {
            double chW = MeasureWidthWithFont(ch.ToString(), fontSize, ttf);
            if (cur.Length > 0 && curW + chW > availWidth)
            {
                lines.Add(cur.ToString());
                cur.Clear();
                curW = 0;
            }
            cur.Append(ch);
            curW += chW;
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        return lines;
    }

    /// <summary>Split text into wrap tokens, each keeping its trailing space, so word-wrapping
    /// an inline run preserves inter-word spacing (e.g. "a b " → ["a ", "b "]).</summary>
    private static IEnumerable<string> SplitKeepingSpaces(string s)
    {
        var start = 0;
        for (var i = 0; i < s.Length; i++)
            if (s[i] == ' ') { yield return s.Substring(start, i - start + 1); start = i + 1; }
        if (start < s.Length) yield return s.Substring(start);
    }

    /// <summary>Exact Standard-14 advance in the cell's own face — the measure the HTML
    /// layout pass sizes columns with, so a wrap made here falls where that pass expects.</summary>
    private static double MeasureFaceExact(string s, double fontSize, bool bold)
    {
        if (s.Length == 0) return 0;
        var face = bold ? "Helvetica-Bold" : "Helvetica";
        try
        {
            var f = Aspose.Pdf.Text.FontRepository.FindFont(face);
            if (f is not null) return f.MeasureString(s, fontSize);
        }
        catch { }
        return MeasureWidthExact(s, fontSize);
    }

    private static double MeasureWidthExact(string s, double fontSize)
    {
        var total = 0;
        foreach (var ch in s)
        {
            var code = (int)ch;
            var w = code is >= 0 and <= 255 ? Standard14Fonts.GetWidth("Helvetica", code) : 0;
            if (w <= 0) w = Standard14Fonts.GetDefaultWidth("Helvetica");
            total += w;
        }
        return total * fontSize / 1000.0;
    }

    /// <summary>
    /// Register a Helvetica font in the page resources and return the font resource name.
    /// </summary>
    /// <summary>Resolve (creating if needed) the page's /Resources /Font dictionary, used to
    /// register an embedded Type0 font for Arabic/Unicode cell text.</summary>
    internal static PdfDictionary ResolvePageFontDict(Page page)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) { resources = new PdfDictionary(); page.Dict.Set("Resources", resources); }
        var fontDict = page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) { fontDict = new PdfDictionary(); resources.Set("Font", fontDict); }
        return fontDict;
    }

    internal static string RegisterFont(Page page) => RegisterFont(page, "Helvetica");

    /// <summary>Register a standard Type1 base font (e.g. "Helvetica",
    /// "Helvetica-Bold") on the page's resource dictionary, reusing an existing
    /// matching entry, and return its resource name.</summary>
    internal static string RegisterFont(Page page, string baseFont)
    {
        // Resolve indirect /Resources and /Font in place; a bare cast would miss a
        // PdfReference and replace the real dictionary with an empty one, dropping
        // the page's existing fonts and XObjects (e.g. a background image).
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }

        // Reuse an already-registered entry for the same base font
        foreach (var key in fontDict.Keys)
        {
            if (page.Reader.Resolve(fontDict.Get(key)) is PdfDictionary existing)
            {
                var baseFontName = existing.GetName("BaseFont");
                if (baseFontName == baseFont)
                    return key;
            }
        }

        // Find a unique font name
        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFont));
        // Cell text is written as WinAnsi bytes (see ContentStreamBuilder.ToWinAnsi);
        // without the matching /Encoding the CP1252 0x80-0x9F range (€, dashes,
        // curly quotes) is undefined in the font's default StandardEncoding.
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set(name, font);
        return name;
    }
}
