using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    internal static void ApplyAutoWidths(Table t, double avail, bool fill = false)
    {
        if (t.Rows.Count == 0) return;
        var cols = 0;
        foreach (var r in t.Rows)
        {
            var n = 0;
            foreach (var c in r.Cells) n += Math.Max(1, c.ColSpan);
            cols = Math.Max(cols, n);
        }
        if (cols == 0) return;

        var pad = LayoutCellPad(t);
        // A rule the cells carry from a style rule is shared with the neighbour, so the
        // column only advances by one of them; a rule the table's own BORDER attribute
        // put on every cell is the cell's alone, and costs both sides.
        var border = t.HtmlCellBorderPt > 0
            ? (t.HtmlCellBorderShared ? 1 : 2) * t.HtmlCellBorderPt : 0.0;
        var mins = new double[cols];
        var maxs = new double[cols];
        // a cell that spans rows keeps holding its columns on the rows
        // below, so the cells there start after it — without that the
        // sub-header row lands under the label columns
        var occupied = new int[cols];
        foreach (var r in t.Rows)
        {
            var ci = 0;
            foreach (var c in r.Cells)
            {
                while (ci < cols && occupied[ci] > 0) ci++;
                if (ci >= cols) break;
                var span = Math.Max(1, c.ColSpan);
                var (cmin, cmax) = LayoutCellSpan(c, pad, border);
                if (span == 1)
                {
                    mins[ci] = Math.Max(mins[ci], cmin);
                    maxs[ci] = Math.Max(maxs[ci], cmax);
                }
                if (c.RowSpan > 1)
                    for (var k = ci; k < Math.Min(cols, ci + span); k++)
                        occupied[k] = c.RowSpan;
                ci += span;
            }
            for (var k = 0; k < cols; k++)
                if (occupied[k] > 0) occupied[k]--;
        }
        double wmin = 0, wmax = 0;
        for (var i = 0; i < cols; i++)
        {
            if (maxs[i] <= 0) maxs[i] = mins[i];
            wmin += mins[i];
            wmax += maxs[i];
        }
        if (wmin <= 0) return;

        var used = fill
            ? Math.Max(avail, wmin)
            : Compat.Clamp(avail, wmin, Math.Max(wmin, wmax));
        var outW = new double[cols];
        if ((fill || used >= wmax - 0.01) && wmax > 0)
            // sharing the width out in proportion must never take a column below the
            // narrowest it can be — that would force a wrap the layout must not have
            for (var i = 0; i < cols; i++) outW[i] = Math.Max(mins[i], maxs[i] * used / wmax);
        else
            for (var i = 0; i < cols; i++) outW[i] = mins[i];

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < cols; i++)
        {
            if (i > 0) sb.Append(' ');
            // Full precision: a column measured to the exact width of its widest word
            // must not lose a thousandth on the way to the renderer, or the word it was
            // sized for wraps. (The renderer wraps against the same measure and the same
            // box, so no slack is needed on top.)
            sb.Append(outW[i].ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        }
        t.ColumnWidths = sb.ToString();
    }

    /// <summary>Give every row the height its own content asks for: each cell wraps
    /// at the column width it was just given, and the row takes the tallest cell —
    /// its lines on the face's own line height, plus the cell's padding and the rule
    /// the grid boxes it with. Without this a row falls back to the generic model,
    /// which leaves the whole sheet drifting.</summary>
    internal static void ApplyAutoRowHeights(Table t)
    {
        if (t.Rows.Count == 0 || string.IsNullOrEmpty(t.ColumnWidths)) return;
        var cols = new List<double>();
        foreach (var w in t.ColumnWidths!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(w, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wv))
                cols.Add(wv);
        if (cols.Count == 0) return;

        var pad = LayoutCellPad(t);
        var padV = (t.DefaultCellPadding?.Top ?? 0) + (t.DefaultCellPadding?.Bottom ?? 0);
        var border = t.HtmlCellBorderPt > 0 ? 2 * t.HtmlCellBorderPt : 0.0;
        var occupied = new int[cols.Count];

        // a cell that spans rows asks for height across ALL of them, so it is measured
        // in a second pass and only makes up a shortfall — letting it drive the first
        // row alone would make every spanned row as tall as the whole cell
        var heights = new double[t.Rows.Count];
        var spanning = new List<(int Row, int Span, double Need)>();
        for (var ri = 0; ri < t.Rows.Count; ri++)
        {
            var row = t.Rows.At(ri);
            var rowH = 0.0;
            var ci = 0;
            foreach (var cell in row.Cells)
            {
                while (ci < cols.Count && occupied[ci] > 0) ci++;
                if (ci >= cols.Count) break;
                var span = Math.Max(1, cell.ColSpan);
                var boxW = 0.0;
                for (var k = ci; k < Math.Min(cols.Count, ci + span); k++) boxW += cols[k];
                var innerW = Math.Max(4, boxW - pad - border);

                var cellH = 0.0;
                foreach (var cp in cell.Paragraphs)
                {
                    if (cp is not Aspose.Pdf.Text.TextFragment ctf || string.IsNullOrEmpty(ctf.Text)) continue;
                    var size = ctf.TextState.FontSize > 0 ? ctf.TextState.FontSize : 8;
                    var bold = ctf.TextState.IsBold
                        || (ctf.TextState.Font?.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) ?? false);
                    var face = bold ? "Helvetica-Bold" : "Helvetica";
                    var lh = FaceLineHeight(face, size);
                    var logicals = ctf.Text!.Replace("\r\n", "\n").Split('\n');
                    for (var li = 0; li < logicals.Length; li++)
                    {
                        // a trailing break closes the last line, it does not open another
                        if (logicals[li].Length == 0 && logicals.Length > 1
                            && li == logicals.Length - 1) continue;
                        cellH += Math.Max(1, WrappedLineCount(logicals[li], face, size, innerW,
                            cell.HtmlNoWrap)) * lh;
                    }
                }
                if (cellH > 0)
                {
                    if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_ROWH") == "1")
                        Console.WriteLine($"    r{ri} ci={ci} span={span} rs={cell.RowSpan} "
                            + $"innerW={innerW:0.00} cellH={cellH:0.00} nowrap={cell.HtmlNoWrap} "
                            + $"text='{FirstText(cell)}'");
                    if (cell.RowSpan > 1) spanning.Add((ri, cell.RowSpan, cellH + padV + border));
                    else rowH = Math.Max(rowH, cellH + padV + border);
                }
                if (cell.RowSpan > 1)
                    for (var k = ci; k < Math.Min(cols.Count, ci + span); k++)
                        occupied[k] = cell.RowSpan;
                ci += span;
            }
            for (var k = 0; k < cols.Count; k++)
                if (occupied[k] > 0) occupied[k]--;
            heights[ri] = rowH;
        }
        foreach (var (ri, span, need) in spanning)
        {
            var have = 0.0;
            for (var k = ri; k < Math.Min(heights.Length, ri + span); k++) have += heights[k];
            var last = Math.Min(heights.Length, ri + span) - 1;
            if (last >= 0 && need > have) heights[last] += need - have;
        }
        for (var ri = 0; ri < t.Rows.Count; ri++)
            if (heights[ri] > 0) t.Rows.At(ri).FixedRowHeight = heights[ri];
    }

    private static string FirstText(Cell c)
    {
        foreach (var p in c.Paragraphs)
            if (p is Aspose.Pdf.Text.TextFragment tf && !string.IsNullOrEmpty(tf.Text))
                return tf.Text!.Length > 18 ? tf.Text![..18] : tf.Text!;
        return "";
    }

    /// <summary>How many lines a run takes in the width it is given — one when the
    /// cell refuses to break.</summary>
    private static int WrappedLineCount(string text, string face, double size, double width, bool noWrap)
    {
        if (text.Length == 0) return 1;
        double W(string t)
        {
            if (t.Length == 0) return 0;
            try
            {
                return Aspose.Pdf.Text.FontRepository.TryFindFont(face)?.MeasureString(t, size)
                       ?? t.Length * size * 0.5;
            }
            catch { return t.Length * size * 0.5; }
        }
        if (noWrap || W(text) <= width) return 1;
        var lines = 1;
        var cur = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // A word that opens a line always takes it, even when it is wider than
            // the column: it overflows, it does not buy a second line.
            if (cur.Length == 0) { cur = word; continue; }
            var cand = cur + " " + word;
            if (W(cand) <= width) { cur = cand; continue; }
            lines++;
            cur = word;
        }
        return lines;
    }

    /// <summary>Marker left in a cell where a nested table was lifted out.</summary>
    private const string NestedMark = "\u0001NT[";

    /// <summary>Lift every table nested inside a cell out of <paramref name="html"/>,
    /// leaving a marker in its place and collecting its markup in
    /// <paramref name="captured"/>. The outer table's own structure is untouched.</summary>
    private static string ExtractNestedTables(string html, List<string> captured)
    {
        var outerOpen = Regex.Match(html, @"<table[^>]*>", RegexOptions.IgnoreCase);
        if (!outerOpen.Success) return html;
        var sb = new StringBuilder(html[..(outerOpen.Index + outerOpen.Length)]);
        var i = outerOpen.Index + outerOpen.Length;
        while (i < html.Length)
        {
            var open = Regex.Match(html[i..], @"<table[^>]*>", RegexOptions.IgnoreCase);
            if (!open.Success) { sb.Append(html[i..]); break; }
            var start = i + open.Index;
            sb.Append(html[i..start]);
            // walk to this table's matching close
            var depth = 0;
            var j = start;
            var end = html.Length;
            foreach (Match t in Regex.Matches(html[start..], @"</?table[^>]*>", RegexOptions.IgnoreCase))
            {
                depth += t.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
                if (depth == 0) { end = start + t.Index + t.Length; break; }
            }
            _ = j;
            captured.Add(html[start..end]);
            sb.Append(NestedMark).Append(captured.Count - 1).Append(']');
            i = end;
        }
        return sb.ToString();
    }

    /// <summary>The right padding the header band's own container declares — e.g.
    /// <c>.header-changelog { padding-right: 42px }</c> — resolved against the fragment's
    /// inline styles AND its linked stylesheet when the load options can reach it. The
    /// band's right-aligned lines anchor that much inside the band's right margin. 0 when
    /// no reachable rule declares one.</summary>
    internal static double BandPaddingRightPt(string? html, HtmlLoadOptions? options)
    {
        if (string.IsNullOrEmpty(html)) return 0;
        var holder = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*(['""])(?<cls>[^'""]*header-right[^'""]*)\1",
            RegexOptions.IgnoreCase);
        if (!holder.Success) return 0;
        var withCss = options is not null ? InlineLinkedStylesheets(html, options) : html;
        foreach (var cls in holder.Groups["cls"].Value
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rule = Regex.Match(withCss,
                @"\." + Regex.Escape(cls) + @"\s*\{[^}]*padding-right\s*:\s*([\d.]+)\s*px",
                RegexOptions.IgnoreCase);
            if (rule.Success && double.TryParse(rule.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var px) && px > 0)
                return px * 0.75;
        }
        return 0;
    }
}
