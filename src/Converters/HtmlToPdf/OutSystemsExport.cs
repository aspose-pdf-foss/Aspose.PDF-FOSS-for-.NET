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
    private static (List<OsRun> result, (double W, double H)? img, string? nested) OsParseRuns(string frag)
    {
        (double W, double H)? img = default;
        string? nested = default;
        img = null;
        (nested, frag) = OsExtractNestedTable(frag);
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
        return (runs, img, nested);
    }

    /// <summary>Pull a nested table out of a cell fragment, returning its single
    /// cell's flattened text.</summary>
    /// <summary>The first nested table's cell text, and the fragment with that table cut out.</summary>
    private static (string? nested, string frag) OsExtractNestedTable(string frag)
    {
        var open = frag.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return (null, frag);
        var end = OsMatchTableEnd(frag, open);
        if (end < 0) return (null, frag);
        var inner = frag[open..end];
        frag = frag.Remove(open, end - open);
        var td = Regex.Match(inner, @"<td[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
        if (!td.Success) return (null, frag);
        return (Regex.Replace(DecodeEntities(
            Regex.Replace(td.Groups[1].Value, @"<[^>]+>", "")), @"[ \t\r\n]+", " ").Trim(' '), frag);
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
                (cell.Runs, var cellImg, var nestedText) = OsParseRuns(content);
                cell.Img = cellImg;
                cell.NestedText = nestedText;
                row.Add(cell);
            }
            if (row.Count > 0) rows.Add(row);
        }
        return rows;
    }
}
