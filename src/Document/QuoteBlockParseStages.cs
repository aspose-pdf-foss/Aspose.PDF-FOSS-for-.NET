using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The stages of the quote-schedule block parse: one matched tag at a time.</summary>
    private static void ParseQuoteBlockMatch(QuoteBlockParseState qb, Match m)
    {
        var t = m.Value;
        if (t.StartsWith("<div", StringComparison.OrdinalIgnoreCase))
        {
            if (Regex.IsMatch(t, @"page-break-after\s*:\s*always", RegexOptions.IgnoreCase))
                qb.blocks.Add(new QsBlock { Kind = "pagebreak" });
            return;
        }
        if (t.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
        {
            qb.blocks.Add(new QsBlock { Kind = "line" });
            return;
        }
        if (t.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
        {
            var srcM = Regex.Match(t, @"src\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!srcM.Success) return;
            var raw = System.Net.WebUtility.HtmlDecode(srcM.Groups[1].Value);
            var path = raw;
            if (!File.Exists(path) && qb.options?.BasePath is { Length: > 0 } bp)
                path = Path.Combine(bp, raw);
            if (!File.Exists(path)) return;
            byte[] data;
            try { data = File.ReadAllBytes(path); } catch { return; }
            if (QsTryPngSize(data) is not (var pw, var ph)) return;
            qb.blocks.Add(new QsBlock
            {
                Kind = "img", Image = data,
                ImgW = pw * QsPxToPt, ImgH = ph * QsPxToPt,
            });
            return;
        }
        if (t.StartsWith("<p", StringComparison.OrdinalIgnoreCase))
        {
            var blk = new QsBlock { Kind = "para" };
            if (Regex.Match(t, @"class\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)
                    is { Success: true } clsM)
                foreach (var cn in clsM.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (qb.css.TryGetValue(cn, out var rule))
                    {
                        if (rule.fontPx > 0) blk.FontPx = rule.fontPx;
                        blk.Centre |= rule.centre;
                        blk.Underline |= rule.underline;
                    }
            blk.Bold = Regex.IsMatch(t, @"<b\b|<strong\b", RegexOptions.IgnoreCase);
            blk.Italic = Regex.IsMatch(t, @"<i\b|<em\b", RegexOptions.IgnoreCase);
            blk.Underline |= Regex.IsMatch(t, @"<u\b", RegexOptions.IgnoreCase);
            blk.Text = Flat(t);
            // a paragraph holding only a hard space is a bare line box
            if (blk.Text.Length == 0)
            {
                qb.blocks.Add(new QsBlock { Kind = "line", FontPx = blk.FontPx });
                return;
            }
            qb.blocks.Add(blk);
            return;
        }
        var tbl = new QsBlock { Kind = "table" };
        foreach (Match rm in Regex.Matches(t, @"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
        {
            var cells = new List<QsCell>();
            foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                @"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
            {
                var attrs = cm.Groups[1].Value;
                var inner = cm.Groups[2].Value;
                var cell = new QsCell { Text = Flat(inner) };
                if (Regex.Match(attrs, @"class\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)
                        is { Success: true } ccm)
                    foreach (var cn in ccm.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        if (qb.css.TryGetValue(cn, out var rule))
                        {
                            if (rule.width > 0) cell.WidthPx = rule.width;
                            if (rule.fontPx > 0) cell.FontPx = rule.fontPx;
                            cell.Centre |= rule.centre;
                        }
                if (Regex.Match(attrs, @"colspan\s*=\s*""?(\d+)", RegexOptions.IgnoreCase)
                        is { Success: true } spanM
                    && int.TryParse(spanM.Groups[1].Value, out var sp) && sp > 1)
                    cell.ColSpan = sp;
                cell.Bold = Regex.IsMatch(inner, @"<b\b|<strong\b", RegexOptions.IgnoreCase);
                cell.Italic = Regex.IsMatch(inner, @"<i\b|<em\b", RegexOptions.IgnoreCase);
                cells.Add(cell);
            }
            if (cells.Count > 0) tbl.Rows.Add(cells);
        }
        if (tbl.Rows.Count > 0) qb.blocks.Add(tbl);
    }

    /// <summary>The text of a tag run: markup stripped, entities decoded, whitespace collapsed.</summary>
    private static string Flat(string s) => Regex.Replace(
        System.Net.WebUtility.HtmlDecode(Regex.Replace(s, @"<[^>]+>", ""))
            .Replace(' ', ' '), @"\s+", " ").Trim();
}
