using System.Globalization;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>The colour of a linked run in an engine cell.</summary>
    private static readonly Color EngineLinkColor = Color.FromArgb(0, 0, 255);

    /// <summary>A nested table's UA insets: 2 px between cells, 1 px inside them (probed: a
    /// one-cell table in a float column starts its text 2.25 pt in and 2.25 pt down).</summary>
    private const double EngineTableCellInsetPt = (2 + 1) * 0.75;

    /// <summary>The width of a div that floats left with a percent width: that share of the
    /// cell's inner width (probed: 70 % of a 415 pt column is 290.5, of a 300 pt column 210);
    /// null for any other div.</summary>
    private static double? EngineFloatWidth(HtmlEngineCellState hc, string tag)
    {
        var css = EngineStyleValue(tag);
        if (css is null || !Regex.IsMatch(css, @"(?<![-\w])float\s*:\s*left", RegexOptions.IgnoreCase)) return null;
        var wm = Regex.Match(css, @"(?<![-\w])width\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase);
        if (!wm.Success || !double.TryParse(wm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) || pct <= 0)
            return null;
        return hc.availWidth * pct / 100.0;
    }

    /// <summary>Lay a float column out: its markup parsed on its own width, its lines shifted
    /// to the column's x and merged with the columns before it by line top; the parse
    /// resumes after the div's close.</summary>
    private static void LayoutEngineFloat(HtmlEngineCellState hc, Match open, double width)
    {
        var inner = EngineElementInner(hc.html!, open, "div", out var end);
        hc.pos = end;
        FlushLine(hc, force: false);
        var lines = ParseHtmlEngineCell(inner, width, hc.baseSize, hc.breakWords, plainText: false) ?? new List<CellLine>();
        var pen = hc.floatPen;
        hc.floatPen += width;
        foreach (var line in lines)
        {
            if (line.Runs is { } runs) foreach (var r in runs) r.X += pen;
            if (line.LinkRuns is { } links)
                for (var i = 0; i < links.Count; i++) links[i] = (links[i].XOff + pen, links[i].W, links[i].Link);
        }
        if (hc.floatLines is null) hc.floatLines = lines;
        else MergeFloatColumn(hc.floatLines, lines);
        hc.anyText |= lines.Count > 0;
    }

    /// <summary>The markup between an element's open tag and its matching close, and the index after that close.</summary>
    private static string EngineElementInner(string html, Match open, string tag, out int end)
    {
        var rx = new Regex(@"<(/?)" + tag + @"\b[^>]*>", RegexOptions.IgnoreCase);
        var depth = 1;
        var from = open.Index + open.Length;
        for (var m = rx.Match(html, from); m.Success; m = m.NextMatch())
        {
            depth += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (depth == 0)
            {
                end = m.Index + m.Length;
                return html.Substring(from, m.Index - from);
            }
        }
        end = html.Length;
        return html.Substring(from);
    }

    /// <summary>Merge a later column's lines into the first column's by line top: each line
    /// joins the line whose box holds its top, its runs already at the column's x.</summary>
    private static void MergeFloatColumn(List<CellLine> into, List<CellLine> column)
    {
        var tops = new double[into.Count + 1];
        for (var i = 0; i < into.Count; i++) tops[i + 1] = tops[i] + into[i].BoxH;
        var y = 0.0;
        foreach (var line in column)
        {
            var target = into.Count - 1;
            for (var i = 0; i < into.Count; i++)
                if (y >= tops[i] && y < tops[i + 1]) { target = i; break; }
            y += line.BoxH;
            if (target < 0) { into.Add(line); continue; }
            var host = into[target];
            if (line.Runs is { Count: > 0 } runs) (host.Runs ??= new List<HtmlRun>()).AddRange(runs);
            if (line.LinkRuns is { Count: > 0 } links) (host.LinkRuns ??= new()).AddRange(links);
            host.Text = host.Text.Length == 0 ? line.Text : host.Text + " " + line.Text;
        }
    }

    /// <summary>Close the float columns: their merged lines join the cell's lines and the
    /// next content starts under them.</summary>
    private static void CommitFloats(HtmlEngineCellState hc)
    {
        if (hc.floatLines is null) return;
        hc.lines.AddRange(hc.floatLines);
        hc.floatLines = null;
        hc.floatPen = 0;
        hc.curX = hc.lineIndent;
    }

    /// <summary>A nested one-cell table: its cell's content sits the UA inset (spacing plus
    /// padding) in from the table's edges on every side.</summary>
    private static void EngineTableTag(HtmlEngineCellState hc, bool closing)
    {
        FlushLine(hc, force: false);
        if (!closing)
        {
            CommitFloats(hc);
            hc.tableStack.Push((hc.lineIndent, hc.availWidth));
            hc.lineIndent += EngineTableCellInsetPt;
            hc.availWidth = Math.Max(0, hc.availWidth - 2 * EngineTableCellInsetPt);
            hc.curX = hc.lineIndent;
            hc.pendingBlockMargin = Math.Max(hc.pendingBlockMargin, EngineTableCellInsetPt);
            return;
        }
        if (hc.lines.Count > 0) hc.lines[^1].BoxH += EngineTableCellInsetPt;
        if (hc.tableStack.Count > 0)
        {
            var (indent, avail) = hc.tableStack.Pop();
            hc.lineIndent = indent;
            hc.availWidth = avail;
        }
        hc.curX = hc.lineIndent;
    }

    /// <summary>A picture whose file is not there draws its alt attribute's value as text, as
    /// written (probed: <c>alt=\"action.png\"</c> in an escaped fixture draws with its quotes).</summary>
    private static void EngineImageAlt(HtmlEngineCellState hc, string tag)
    {
        var alt = Regex.Match(tag, @"\balt\s*=\s*(?:'(?<v>[^']*)'|""(?<v>[^""]*)""|(?<v>[^\s>]+))", RegexOptions.IgnoreCase);
        if (alt.Success && alt.Groups["v"].Value.Length > 0) EmitText(hc, alt.Groups["v"].Value);
    }

    /// <summary>The structures the engine models: a nested table of one-cell rows, and a
    /// picture whose source does not resolve to a file (a real picture keeps the image arms).</summary>
    private static bool EngineAcceptsStructure(string html)
    {
        foreach (Match tr in Regex.Matches(html, @"<tr\b[^>]*>(.*?)</tr\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            if (Regex.Matches(tr.Groups[1].Value, @"<t[dh]\b", RegexOptions.IgnoreCase).Count > 1) return false;
        foreach (Match img in Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase))
        {
            // Only a picture that cannot be a file - no source, or a source that opens with
            // the escaped quote - is the engine's (its alt text); a real one keeps the image arms.
            var src = Regex.Match(img.Value, @"\bsrc\s*=\s*(?:'(?<v>[^']*)'|""(?<v>[^""]*)""|(?<v>[^\s>]+))", RegexOptions.IgnoreCase);
            if (src.Success && src.Groups["v"].Value.Length > 0 && !src.Groups["v"].Value.StartsWith("\\\"", StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
