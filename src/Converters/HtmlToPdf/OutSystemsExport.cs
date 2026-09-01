using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The OutSystems document-handling export dialect (the aspNetHidden form +
// OSFillParent/ThemeGrid stylesheet) under HtmlPageLayoutOption.ScaleToPageWidth:
// a bill-of-lading sheet built from width:100% attribute tables. The document
// lays out at its NATURAL width — the signature table is over-
// constrained (its nested section-marker boxes carry an unbreakable string, so
// the sum of the column min-contents exceeds the content box), every column
// takes its min-content width and the table overflows the sheet; the shrink
// factor is the authored content width over that overflow extent, applied
// uniformly with the content pinned at the left margin and the page top (the
// ScaleToPageWidth transform). Every line of every size rides one uniform line
// grid, paragraph margins equal one win-metric line of the 12 pt body font, and
// no-break spaces glue to their neighbouring word for wrapping while the plain
// spaces between them stay soft. Constants not derivable from the sheet are
// measured on the expected render.
internal static partial class HtmlToPdfConverter
{
    private const double OsLine = 13.6125;      // the uniform line box (18.15 px measured; fs 9..12 all sit on it)
    private const double OsUaBody = 6.0;        // the UA 8px body margin
    private const double OsPad1 = 1.5;          // cellspacing=1 + cellpadding=1 (0.75 each) / border pair inset
    private const double OsBorder = 0.75;       // border=1 attribute stroke
    private const double OsTitleLineH = 41.22;  // the 48px title's line box (measured)
    private const double OsTitleDrop = 33.14;   // baseline drop inside the title line (measured)
    private const double OsHrH = 1.5;           // the hr box: two stacked 0.75 border strokes
    private const double OsHrMarginBottom = 6.0; // UA hr 0.5em margin at the 12 pt base
    private const double OsDateTextOff = 18.40; // print-date first baseline below the cell top (measured)
    private const char OsMark = '\u0001';       // nested-table stand-in during the row scan

    // The signature rows' vertical staircase, measured: each
    // row's height in grid lines, and each column's first-line offset (in grid
    // lines) — the engine centers every cell against its own phantom row,
    // landing the four columns half a line apart.
    private static readonly double[] OsSigRowLines = { 3.5, 2.0, 1.5, 1.0 };
    private static readonly double[][] OsSigCellOff =
    {
        new[] { 0.0, 1.0, 1.5, 0.5 },
        new[] { 1.0, 0.0, 1.0, 0.0 },
        new[] { 0.5, 0.5, 0.5, 0.5 },
        new[] { 0.0, 0.0, 0.0, 0.0 },
    };

    private readonly record struct OsRun(string Text, string Face, double Fs,
        double Rise = 0, bool Under = false);

    private sealed class OsCell
    {
        public List<OsRun> Runs = new();
        public bool AlignRight, ValignTop;
        public string? NestedText;              // a nested single-cell table's content
        public (double W, double H)? Img;       // an <img>'s CSS-pt size
    }

    /// <summary>Render an OutSystems bill-of-lading export at natural width and
    /// shrink it onto the authored sheet, or null without the fingerprint.</summary>
    private static Document? TryRenderOutSystemsExport(string html,
        double pageWidth, double pageHeight, double mL, double mR)
    {
        if (!html.Contains("aspNetHidden", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("OSFillParent", StringComparison.Ordinal)
            || !html.Contains("ThemeGrid", StringComparison.Ordinal))
            return null;
        if (WinMetricsFor("Arial") is not { } am
            || WinMetricsFor("Times New Roman") is not { } tm)
            return null;

        var elems = OsTopLevel(html);
        if (elems.Count(e => e.Kind == 't') != 8) return null;

        var contentX = mL + OsUaBody;
        var contentW = pageWidth - mL - mR - 2 * OsUaBody;
        var q4 = contentW / 4;
        var drop12 = MetricBaselineDrop(12.0, OsLine, am);
        var dropTnr = MetricBaselineDrop(12.0, OsLine, tm);
        var pMargin = 12.0 * am.sum;            // paragraph margin: one win-metric line of the body font

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        EnsureFont(page, "Arial", "F8");
        EnsureFont(page, "ArialBold", "F9");
        EnsureFont(page, "TimesNewRoman", "F10");
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        static string ResFor(string face) => face switch
        {
            "Arial Bold" => "F9",
            "Times New Roman" => "F10",
            _ => "F8",
        };
        void Stream(string s) => page.AddContentStream(Encoding.ASCII.GetBytes(s));
        void EmitSegs(List<OsRun> segs, double x, double yTd)
        {
            foreach (var r in segs)
            {
                if (r.Text.Length > 0)
                    EmitPositionedRun(page, ResFor(r.Face), r.Fs, x, pageHeight - yTd, r.Text);
                x += MeasureFaceText(r.Face, r.Text, r.Fs);
            }
        }
        double SegsW(List<OsRun> segs)
        {
            double w = 0;
            foreach (var r in segs) w += MeasureFaceText(r.Face, r.Text, r.Fs);
            return w;
        }
        // A rectangle stroked at the given line CENTERS (top-down y).
        void RectStroke(double x0, double y0, double x1, double y1, double lw)
            => Stream(string.Create(inv,
                $"q {lw:0.##} w {x0:F2} {pageHeight - y1:F2} m {x1:F2} {pageHeight - y1:F2} l {x1:F2} {pageHeight - y0:F2} l {x0:F2} {pageHeight - y0:F2} l h S Q\n"));
        void HLine(double x0, double x1, double yTd, double lw)
            => Stream(string.Create(inv,
                $"q {lw:0.##} w {x0:F2} {pageHeight - yTd:F2} m {x1:F2} {pageHeight - yTd:F2} l S Q\n"));
        // A missing image draws as a 1 pt frame around its declared CSS box.
        void ImgFrame(double x, double yTdBottom, double w, double h)
            => RectStroke(x + 0.5, yTdBottom - h - 1.5, x + w + 1.5, yTdBottom - 0.5, 1.0);

        // ── the flow ──
        double y = OsUaBody, pendingGap = 0, natRight = contentX + contentW;
        var tableIdx = 0;
        foreach (var el in elems)
        {
            switch (el.Kind)
            {
                case 'p':
                {
                    var runs = OsParseRuns(el.Frag, out _, out _);
                    var lines = OsWrap(runs, contentW);
                    if (lines.Count == 0)
                    {
                        // an empty paragraph: its margins collapse straight through
                        pendingGap = Math.Max(pendingGap, pMargin);
                        break;
                    }
                    y += Math.Max(pendingGap, pMargin);
                    for (var i = 0; i < lines.Count; i++)
                        EmitSegs(lines[i], contentX, y + i * OsLine + drop12);
                    y += lines.Count * OsLine;
                    pendingGap = pMargin;
                    break;
                }
                case 'h':
                {
                    y += Math.Max(pendingGap, pMargin);
                    HLine(contentX, contentX + contentW, y + OsBorder / 2, OsBorder);
                    HLine(contentX, contentX + contentW, y + OsHrH - OsBorder / 2, OsBorder);
                    y += OsHrH;
                    pendingGap = OsHrMarginBottom;
                    break;
                }
                case 't':
                {
                    y += pendingGap;
                    pendingGap = 0;
                    var rows = OsParseRows(el.Frag);
                    switch (tableIdx++)
                    {
                        case 0: y = OsHeader(rows, y); break;
                        case 1: y = OsGrid(rows, OsFromCols(rows), y); break;
                        case 2: y = OsBand(rows, y, centered: false); break;
                        case 3:
                        case 4: y = OsGrid(rows, OsEvenCols(), y); break;
                        case 5: y = OsBand(rows, y, centered: true); break;
                        case 6: y = OsSignature(rows, y, out natRight); break;
                        case 7: y = OsDate(rows, y); break;
                    }
                    break;
                }
            }
        }

        // ── the shrink: content pinned at the left margin and the page top ──
        var s = (pageWidth - mL - mR) / (natRight - mL);
        if (s is > 0 and < 1)
        {
            page.PrependContentStream(Encoding.ASCII.GetBytes(string.Create(inv,
                $"q {s:F5} 0 0 {s:F5} {mL * (1 - s):F2} {pageHeight * (1 - s):F2} cm\n")));
            Stream("Q\n");
        }
        return doc;

        // ── the header table: the 48px red title + address, the version cell ──
        double OsHeader(List<List<OsCell>> rows, double top)
        {
            if (rows.Count == 0 || rows[0].Count < 2) return top;
            var titleTop = top + OsPad1 + pMargin;
            var groups = OsBreakGroups(rows[0][0].Runs);
            var x0 = contentX + OsPad1;
            Stream("q 0.906 0.298 0.235 rg\n");   // the sheet's #e74c3c
            var lineTop = titleTop;
            for (var g = 0; g < groups.Count; g++)
            {
                EmitSegs(groups[g], x0, lineTop + (g == 0 ? OsTitleDrop : drop12));
                lineTop += g == 0 ? OsTitleLineH : OsLine;
            }
            Stream("0 0 0 rg\nQ\n");
            if (rows[0][0].Img is { } img)        // the inline icon seats on the title baseline
                ImgFrame(x0 + SegsW(groups[0]), titleTop + OsTitleDrop, img.W, img.H);
            var ver = OsWrap(rows[0][1].Runs, contentW);
            if (ver.Count > 0)
                EmitSegs(ver[0], contentX + contentW - OsPad1 - SegsW(ver[0]), titleTop + dropTnr);
            return lineTop + pMargin + OsPad1;
        }

        // ── a borderless attribute grid (the FROM and key/value tables) ──
        double OsGrid(List<List<OsCell>> rows, double[] colX, double top)
        {
            foreach (var row in rows)
            {
                var n = Math.Min(row.Count, colX.Length - 1);
                var lines = new List<List<OsRun>>[n];
                var rowLines = 1;
                for (var c = 0; c < n; c++)
                {
                    lines[c] = OsWrap(row[c].Runs, colX[c + 1] - colX[c]);
                    rowLines = Math.Max(rowLines, lines[c].Count);
                }
                for (var c = 0; c < n; c++)
                {
                    var off = lines[c].Count == rowLines || row[c].ValignTop
                        ? 0 : (rowLines - lines[c].Count) * OsLine / 2;
                    for (var i = 0; i < lines[c].Count; i++)
                    {
                        var x = row[c].AlignRight
                            ? colX[c + 1] - SegsW(lines[c][i]) : colX[c];
                        EmitSegs(lines[c][i], x, top + off + i * OsLine + drop12);
                    }
                }
                top += rowLines * OsLine;
            }
            return top;
        }

        double[] OsEvenCols() => new[]
            { contentX, contentX + q4, contentX + 2 * q4, contentX + 3 * q4, contentX + 4 * q4 };

        // The FROM grid declares 25% on its first two columns only; the
        // undeclared pair takes max-content plus the leftover in proportion to
        // it (the percent-table column model).
        double[] OsFromCols(List<List<OsCell>> rows)
        {
            double c3 = 0, c4 = 0;
            foreach (var row in rows)
            {
                if (row.Count > 2) c3 = Math.Max(c3, OsMaxContent(row[2].Runs));
                if (row.Count > 3) c4 = Math.Max(c4, OsMaxContent(row[3].Runs));
            }
            var rem = contentW / 2;
            var c3W = c3 + c4 < 1e-9 ? rem / 2 : c3 + (rem - c3 - c4) * c3 / (c3 + c4);
            return new[] { contentX, contentX + q4, contentX + 2 * q4,
                contentX + 2 * q4 + c3W, contentX + contentW };
        }

        // ── a border=1 band: the description pair or the centered emergency cell ──
        double OsBand(List<List<OsCell>> rows, double top, bool centered)
        {
            var cells = rows.Count > 0 ? rows[0] : new List<OsCell>();
            var innerW = centered ? contentW - 2 * (OsPad1 + OsBorder)
                : contentW / 2 - OsPad1 - OsBorder;
            var lineSets = cells.Select(c => OsWrap(c.Runs, innerW)).ToList();
            var rowLines = 1;
            foreach (var l in lineSets) rowLines = Math.Max(rowLines, l.Count);
            var bottom = top + OsPad1 + rowLines * OsLine + OsPad1;
            RectStroke(contentX + OsBorder / 2, top + OsBorder / 2,
                contentX + contentW - OsBorder / 2, bottom - OsBorder / 2, OsBorder);
            var mid = contentX + contentW / 2;
            if (centered)
                RectStroke(contentX + OsPad1 - OsBorder / 2, top + OsPad1 - OsBorder / 2,
                    contentX + contentW - OsPad1 + OsBorder / 2, bottom - OsPad1 + OsBorder / 2, OsBorder);
            else
            {
                RectStroke(contentX + OsPad1 - OsBorder / 2, top + OsPad1 - OsBorder / 2,
                    mid - OsBorder / 2, bottom - OsPad1 + OsBorder / 2, OsBorder);
                RectStroke(mid + OsBorder / 2, top + OsPad1 - OsBorder / 2,
                    contentX + contentW - OsPad1 + OsBorder / 2, bottom - OsPad1 + OsBorder / 2, OsBorder);
            }
            if (lineSets.Count > 0)
                for (var i = 0; i < lineSets[0].Count; i++)
                {
                    var segs = lineSets[0][i];
                    var x = centered ? contentX + OsPad1 + (innerW - SegsW(segs)) / 2
                        : contentX + OsPad1;
                    EmitSegs(segs, x, top + OsPad1 + i * OsLine + drop12);
                }
            return bottom;
        }

        // ── the signature table: min-content columns, the section-marker boxes,
        //    the measured staircase rows ──
        double OsSignature(List<List<OsCell>> rows, double top, out double right)
        {
            double c1 = 0, c2 = 0, c3 = 0, c5 = 0;
            foreach (var row in rows)
                for (var c = 0; c < row.Count && c < 4; c++)
                {
                    var w = row[c].NestedText is { } nt
                        ? MeasureFaceText("Arial", nt, 12.0) + 4 * OsBorder
                        : OsMinContent(row[c].Runs);
                    switch (c)
                    {
                        case 0: c1 = Math.Max(c1, w); break;
                        case 1: c2 = Math.Max(c2, w); break;
                        case 2: c3 = Math.Max(c3, w); break;
                        default: c5 = Math.Max(c5, w); break;
                    }
                }
            var colX = new[] { contentX, contentX + c1, contentX + c1 + c2,
                contentX + c1 + c2 + c3, contentX + c1 + c2 + c3 + c5 };
            right = colX[4];
            var widths = new[] { c1, c2, c3, c5 };

            var sigRow = 0;
            foreach (var row in rows)
            {
                var boxCell = row.FirstOrDefault(c => c.NestedText is not null);
                if (boxCell is not null)
                {
                    // a marker-box row: the label wraps down its narrow column
                    // and the box centers against those lines
                    var label = row.Count > 2 ? OsWrap(row[2].Runs, c3) : new List<List<OsRun>>();
                    var rowH = Math.Max(1, label.Count) * OsLine;
                    for (var i = 0; i < label.Count; i++)
                        EmitSegs(label[i], colX[2], top + i * OsLine + drop12);
                    var boxH = 4 * OsBorder + OsLine;
                    var boxW = MeasureFaceText("Arial", boxCell.NestedText!, 12.0) + 4 * OsBorder;
                    var bx = colX[3];
                    var bt = top + (rowH - boxH) / 2;
                    RectStroke(bx + OsBorder / 2, bt + OsBorder / 2,
                        bx + boxW - OsBorder / 2, bt + boxH - OsBorder / 2, OsBorder);
                    RectStroke(bx + 1.5 * OsBorder, bt + 1.5 * OsBorder,
                        bx + boxW - 1.5 * OsBorder, bt + boxH - 1.5 * OsBorder, OsBorder);
                    EmitSegs(new List<OsRun> { new(boxCell.NestedText!, "Arial", 12.0) },
                        bx + 2 * OsBorder, bt + 2 * OsBorder + drop12);
                    top += rowH;
                    continue;
                }
                var r = Math.Min(sigRow, OsSigRowLines.Length - 1);
                for (var c = 0; c < row.Count && c < 4; c++)
                {
                    var lines = OsWrap(row[c].Runs, widths[c]);
                    var off = OsSigCellOff[r][c] * OsLine;
                    for (var i = 0; i < lines.Count; i++)
                    {
                        var x = row[c].AlignRight
                            ? colX[c + 1] - SegsW(lines[i]) : colX[c];
                        EmitSegs(lines[i], x, top + off + i * OsLine + drop12);
                    }
                }
                top += OsSigRowLines[r] * OsLine;
                sigRow++;
            }
            return top;
        }

        // ── the print-date table: the bottom-seated logo frame, the date cell ──
        double OsDate(List<List<OsCell>> rows, double top)
        {
            if (rows.Count == 0 || rows[0].Count < 2) return top;
            var cTop = top + OsPad1;
            var img = rows[0][1].Img ?? (W: 59.25, H: 37.5);
            var boxH = img.H + 2;
            ImgFrame(contentX + contentW - OsPad1 - img.W - 2, cTop + boxH, img.W, img.H);
            var groups = OsBreakGroups(rows[0][0].Runs);
            for (var g = 0; g < groups.Count; g++)
                EmitSegs(groups[g], contentX + OsPad1, cTop + OsDateTextOff + g * OsLine);
            return cTop + boxH + OsPad1;
        }
    }

    /// <summary>Single-line (max-content) width of a run list.</summary>
    private static double OsMaxContent(List<OsRun> runs)
    {
        double w = 0;
        foreach (var r in runs)
            if (r.Text != "\n")
                w += MeasureFaceText(r.Face, r.Text, r.Fs);
        return w;
    }

    /// <summary>Min-content width: the widest unbreakable token (no-break spaces
    /// glue to their neighbours, plain spaces break).</summary>
    private static double OsMinContent(List<OsRun> runs)
    {
        double best = 0;
        foreach (var line in OsWrap(runs, 0.1))
        {
            double w = 0;
            foreach (var r in line) w += MeasureFaceText(r.Face, r.Text, r.Fs);
            best = Math.Max(best, w);
        }
        return best;
    }

    /// <summary>Split a run list at explicit break markers.</summary>
    private static List<List<OsRun>> OsBreakGroups(List<OsRun> runs)
    {
        var groups = new List<List<OsRun>> { new() };
        foreach (var r in runs)
            if (r.Text == "\n") groups.Add(new List<OsRun>());
            else groups[^1].Add(r);
        return groups;
    }

    /// <summary>Greedy wrap: tokens split at plain spaces (no-break spaces glue to
    /// their word) and after hyphens; explicit breaks are hard. Returns emission
    /// segments per line, inter-token spaces included in the text so the drawn
    /// advances match the measure.</summary>
    private static List<List<OsRun>> OsWrap(List<OsRun> runs, double width)
    {
        var lines = new List<List<OsRun>>();
        if (runs.Count == 0) return lines;
        var cur = new List<OsRun>();
        double curW = 0;
        var started = false;

        void Flush()
        {
            lines.Add(cur);
            cur = new List<OsRun>();
            curW = 0;
        }
        void Append(OsRun r)
        {
            if (r.Text.Length == 0) return;
            if (cur.Count > 0 && cur[^1] is { } prev && prev.Face == r.Face
                && Math.Abs(prev.Fs - r.Fs) < 1e-9 && Math.Abs(prev.Rise - r.Rise) < 1e-9
                && prev.Under == r.Under)
                cur[^1] = prev with { Text = prev.Text + r.Text };
            else cur.Add(r);
            curW += MeasureFaceText(r.Face, r.Text, r.Fs);
        }

        foreach (var group in OsBreakGroups(runs))
        {
            if (started) Flush();
            if (group.Count == 0 && !started && runs.Count == 0) continue;
            started = true;

            // token stream: unbreakable parts with their width and a soft-space flag
            var toks = new List<(List<OsRun> Parts, double W, bool Space)>();
            var parts = new List<OsRun>();
            double tw = 0;
            var spaceBefore = false;
            void CloseTok(bool glued)
            {
                if (parts.Count > 0)
                {
                    toks.Add((parts, tw, spaceBefore));
                    parts = new List<OsRun>();
                    tw = 0;
                    spaceBefore = false;
                }
                if (!glued) spaceBefore = true;
            }
            foreach (var r in group)
            {
                var i = 0;
                while (i < r.Text.Length)
                {
                    if (r.Text[i] == ' ')
                    {
                        CloseTok(glued: false);
                        i++;
                        continue;
                    }
                    var j = i;
                    while (j < r.Text.Length && r.Text[j] != ' ') j++;
                    var seg = r.Text[i..j];
                    var start = 0;
                    for (var k = 0; k < seg.Length - 1; k++)
                        if (seg[k] == '-')
                        {
                            var piece = seg[start..(k + 1)];
                            parts.Add(r with { Text = piece });
                            tw += MeasureFaceText(r.Face, piece, r.Fs);
                            CloseTok(glued: true);
                            start = k + 1;
                        }
                    if (start < seg.Length)
                    {
                        var tail = seg[start..];
                        parts.Add(r with { Text = tail });
                        tw += MeasureFaceText(r.Face, tail, r.Fs);
                    }
                    i = j;
                }
            }
            CloseTok(glued: false);

            foreach (var (p, w, sp) in toks)
            {
                var spW = sp && cur.Count > 0
                    ? MeasureFaceText(p[0].Face, " ", p[0].Fs) : 0;
                if (cur.Count > 0 && curW + spW + w > width + 1e-6)
                    Flush();
                else if (spW > 0)
                    Append(p[0] with { Text = " " });
                foreach (var part in p) Append(part);
            }
        }
        if (started) Flush();
        return lines;
    }

    /// <summary>Parse a cell or paragraph fragment into styled runs — break
    /// markers for &lt;br&gt;, bold/size/face tracked from the inline spans.</summary>
    private static List<OsRun> OsParseRuns(string frag, out (double W, double H)? img,
        out string? nested)
    {
        img = null;
        nested = OsExtractNestedTable(ref frag);
        var runs = new List<OsRun>();
        var bold = 0;
        var stack = new List<(string Face, double Fs)>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (Match m in Regex.Matches(frag, @"<[^>]+>|[^<]+"))
        {
            var tok = m.Value;
            if (tok[0] == '<')
            {
                var close = tok.Length > 1 && tok[1] == '/';
                var name = Regex.Match(tok, @"^</?\s*([a-zA-Z0-9]+)").Groups[1].Value.ToLowerInvariant();
                switch (name)
                {
                    case "strong" or "b":
                        bold = Math.Max(0, bold + (close ? -1 : 1));
                        break;
                    case "br":
                        runs.Add(new OsRun("\n", "", 0));
                        break;
                    case "img" when !close:
                    {
                        var st = Regex.Match(tok, @"style\s*=\s*[""']([^""']*)").Groups[1].Value;
                        var wM = Regex.Match(st, @"width\s*:\s*([\d.]+)px");
                        var hM = Regex.Match(st, @"height\s*:\s*([\d.]+)px");
                        if (wM.Success && hM.Success)
                            img = (double.Parse(wM.Groups[1].Value, inv) * 0.75,
                                   double.Parse(hM.Groups[1].Value, inv) * 0.75);
                        break;
                    }
                    case "span" when !close:
                    {
                        var style = Regex.Match(tok, @"style\s*=\s*[""']([^""']*)").Groups[1].Value;
                        var face = stack.Count > 0 ? stack[^1].Face : "Times New Roman";
                        var fs = stack.Count > 0 ? stack[^1].Fs : 12.0;
                        if (Regex.IsMatch(style, @"font-family\s*:\s*Arial", RegexOptions.IgnoreCase))
                            face = "Arial";
                        var fsM = Regex.Match(style, @"font-size\s*:\s*([\d.]+)px");
                        if (fsM.Success) fs = double.Parse(fsM.Groups[1].Value, inv) * 0.75;
                        stack.Add((face, fs));
                        break;
                    }
                    case "span":
                        if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                        break;
                }
                continue;
            }
            var text = Regex.Replace(DecodeEntities(tok), @"[ \t\r\n]+", " ");
            if (text.Length == 0) continue;
            var f = stack.Count > 0 ? stack[^1].Face : "Times New Roman";
            var size = stack.Count > 0 ? stack[^1].Fs : 12.0;
            if (bold > 0 && f == "Arial") f = "Arial Bold";
            if (runs.Count > 0 && runs[^1].Text != "\n" && runs[^1].Text.EndsWith(' ')
                && text.StartsWith(' '))
                text = text[1..];
            if (text.Length > 0) runs.Add(new OsRun(text, f, size));
        }
        // block-edge whitespace collapse: strip PLAIN spaces at the ends (a
        // leading <br> or a no-break space is content and stays)
        while (runs.Count > 0 && runs[0].Text != "\n"
            && runs[0].Text.TrimStart(' ').Length == 0)
            runs.RemoveAt(0);
        if (runs.Count > 0 && runs[0].Text != "\n" && runs[0].Text.StartsWith(' '))
            runs[0] = runs[0] with { Text = runs[0].Text.TrimStart(' ') };
        while (runs.Count > 0 && runs[^1].Text != "\n"
            && runs[^1].Text.TrimEnd(' ').Length == 0)
            runs.RemoveAt(runs.Count - 1);
        if (runs.Count > 0 && runs[^1].Text != "\n" && runs[^1].Text.EndsWith(' '))
            runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd(' ') };
        return runs;
    }

    /// <summary>Pull a nested table out of a cell fragment, returning its single
    /// cell's flattened text.</summary>
    private static string? OsExtractNestedTable(ref string frag)
    {
        var open = frag.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var end = OsMatchTableEnd(frag, open);
        if (end < 0) return null;
        var inner = frag[open..end];
        frag = frag.Remove(open, end - open);
        var td = Regex.Match(inner, @"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
        if (!td.Success) return null;
        return Regex.Replace(DecodeEntities(
            Regex.Replace(td.Groups[1].Value, @"<[^>]+>", "")), @"[ \t\r\n]+", " ").Trim(' ');
    }

    private static int OsMatchTableEnd(string html, int open)
    {
        var depth = 0;
        for (var i = open; i < html.Length;)
        {
            if (string.Compare(html, i, "<table", 0, 6, StringComparison.OrdinalIgnoreCase) == 0)
            { depth++; i += 6; continue; }
            if (string.Compare(html, i, "</table>", 0, 8, StringComparison.OrdinalIgnoreCase) == 0)
            { i += 8; if (--depth == 0) return i; continue; }
            i++;
        }
        return -1;
    }

    /// <summary>Top-level content elements — tables (nesting-aware), paragraphs
    /// and rules — with scripts, styles and hidden form inputs stripped.</summary>
    private static List<(char Kind, string Frag)> OsTopLevel(string html)
    {
        html = Regex.Replace(html, @"<script\b[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style\b[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<input\b[^>]*>", "", RegexOptions.IgnoreCase);
        var res = new List<(char, string)>();
        for (var i = 0; i < html.Length;)
        {
            var m = Regex.Match(html[i..], @"<(table|p|hr)[\s>]", RegexOptions.IgnoreCase);
            if (!m.Success) break;
            var at = i + m.Index;
            switch (char.ToLowerInvariant(m.Groups[1].Value[0]))
            {
                case 't':
                {
                    var end = OsMatchTableEnd(html, at);
                    if (end < 0) return res;
                    res.Add(('t', html[at..end]));
                    i = end;
                    break;
                }
                case 'p':
                {
                    var close = html.IndexOf("</p>", at, StringComparison.OrdinalIgnoreCase);
                    if (close < 0) { i = at + 2; break; }
                    res.Add(('p', html[at..close]));
                    i = close + 4;
                    break;
                }
                default:
                    res.Add(('h', ""));
                    i = at + 3;
                    break;
            }
        }
        return res;
    }

    /// <summary>Rows of cells for a table fragment (the outer table only; nested
    /// tables hide behind stand-ins during the scan).</summary>
    private static List<List<OsCell>> OsParseRows(string tableFrag)
    {
        var openEnd = tableFrag.IndexOf('>');
        var body = openEnd >= 0 ? tableFrag[(openEnd + 1)..] : tableFrag;
        var nested = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < body.Length;)
        {
            var open = body.IndexOf("<table", i, StringComparison.OrdinalIgnoreCase);
            var end = open < 0 ? -1 : OsMatchTableEnd(body, open);
            if (end < 0)
            {
                sb.Append(body, i, body.Length - i);
                break;
            }
            sb.Append(body, i, open - i);
            sb.Append(OsMark).Append(nested.Count).Append(OsMark);
            nested.Add(body[open..end]);
            i = end;
        }
        body = sb.ToString();

        var rows = new List<List<OsCell>>();
        foreach (Match tr in Regex.Matches(body, @"<tr[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
        {
            var row = new List<OsCell>();
            foreach (Match td in Regex.Matches(tr.Groups[1].Value,
                @"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
            {
                var attrs = td.Groups[1].Value;
                var content = Regex.Replace(td.Groups[2].Value, OsMark + @"(\d+)" + OsMark,
                    nm => nested[int.Parse(nm.Groups[1].Value)]);
                var cell = new OsCell
                {
                    AlignRight = Regex.IsMatch(attrs, @"text-align\s*:\s*right", RegexOptions.IgnoreCase),
                    ValignTop = Regex.IsMatch(attrs, @"vertical-align\s*:\s*top", RegexOptions.IgnoreCase),
                };
                cell.Runs = OsParseRuns(content, out var cellImg, out var nestedText);
                cell.Img = cellImg;
                cell.NestedText = nestedText;
                row.Add(cell);
            }
            if (row.Count > 0) rows.Add(row);
        }
        return rows;
    }
}
