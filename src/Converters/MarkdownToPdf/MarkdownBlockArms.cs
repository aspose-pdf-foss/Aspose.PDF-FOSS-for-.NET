using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class MarkdownToPdfConverter
{
    /// <summary>Gathers a paragraph: consecutive non-block lines, a trailing double space or backslash forcing a hard line break.</summary>
    private static void CollectMarkdownParagraph(MarkdownBlocksState mb)
    {
        while (mb.i < mb.lines.Length)
        {
            var p = mb.lines[mb.i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(p) || IsBlockLine(p)
                || p.TrimStart().StartsWith("<", StringComparison.Ordinal)) break;
            // A table interrupts the paragraph only when its divider row follows.
            if (p.Contains('|') && mb.i + 1 < mb.lines.Length
                && IsTableDividerLine(mb.lines[mb.i + 1].TrimEnd('\r'))) break;
            if (mb.i + 1 < mb.lines.Length)
            {
                var nx = mb.lines[mb.i + 1].TrimEnd('\r').Trim();
                if (nx.Length > 0 && (nx.All(c => c == '=') || nx.All(c => c == '-'))) break;
            }
            var hard = p.EndsWith("  ", StringComparison.Ordinal);
            if (mb.buf.Length > 0) mb.buf.Append(' ');
            mb.buf.Append(p.Trim());
            if (hard)
            {
                mb.hardLines.Add(ParseInline(mb.buf.ToString()));
                mb.buf.Clear();
            }
            mb.i++;
        }
    }

    /// <summary>A raw HTML block: the lines up to the closing tag or the next blank line, kept verbatim. True when handled.</summary>
    private static bool ParseMarkdownHtmlBlock(MarkdownBlocksState mb)
    {
        if (mb.trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            var html = new StringBuilder();
            while (mb.i < mb.lines.Length && !string.IsNullOrWhiteSpace(mb.lines[mb.i]))
                html.Append(mb.lines[mb.i++].TrimEnd('\r')).Append('\n');
            var h = html.ToString();
            if (Regex.IsMatch(h, "align\\s*=\\s*[\"']center[\"']", RegexOptions.IgnoreCase))
                mb.pendingCenter = true;
            var img = Regex.Match(h, "<img[^>]*src=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            if (img.Success && LoadImage(img.Groups[1].Value) is { } data
                && TryReadPngSize(data) is (var pw, var ph))
            {
                mb.blocks.Add(new ImgBlk(data, pw * PxToPt, ph * PxToPt, mb.pendingCenter));
            }
            if (h.Contains("</p>", StringComparison.OrdinalIgnoreCase))
                mb.pendingCenter = false;
            return true;
        }
        return false;
    }

    /// <summary>An indented code block: four-space or tab-indented lines gathered with their indent stripped. True when handled.</summary>
    private static bool ParseMarkdownIndentedCode(MarkdownBlocksState mb)
    {
        if (mb.line.StartsWith("    ", StringComparison.Ordinal) || mb.line.StartsWith("\t", StringComparison.Ordinal))
        {
            var code = new List<string>();
            while (mb.i < mb.lines.Length)
            {
                var c = mb.lines[mb.i].TrimEnd('\r');
                if (!(c.StartsWith("    ", StringComparison.Ordinal) || c.StartsWith("\t", StringComparison.Ordinal))) break;
                code.Add(c.Trim());
                mb.i++;
            }
            mb.blocks.Add(new CodeBlk(code, CodeBlockSize));
            return true;
        }
        return false;
    }

    /// <summary>A bullet or numbered list: consecutive items at their indent levels, each item's text parsed inline. True when handled.</summary>
    private static bool ParseMarkdownList(MarkdownBlocksState mb)
    {
        if (mb.ulMatch.Success || mb.olMatch.Success)
        {
            var items = new List<(string, List<Run>)>();
            var num = 1;
            while (mb.i < mb.lines.Length)
            {
                var l2 = mb.lines[mb.i].TrimEnd('\r');
                var u2 = Regex.Match(l2, @"^(\s*)[*+\-]\s+(.+)$");
                var o2 = Regex.Match(l2, @"^(\s*)(\d+)[.)]\s+(.+)$");
                if (u2.Success && !Regex.IsMatch(l2, @"^\s*([-*_])(\s*\1){2,}\s*$"))
                    items.Add(("\u2022", ParseInline(u2.Groups[2].Value.TrimEnd())));
                else if (o2.Success)
                    items.Add((num++ + ".", ParseInline(o2.Groups[3].Value.TrimEnd())));
                else break;
                mb.i++;
            }
            mb.blocks.Add(new ListBlk(items));
            return true;
        }
        return false;
    }

    /// <summary>A block quote: consecutive lines starting with the marker, their text parsed inline. True when handled.</summary>
    private static bool ParseMarkdownBlockQuote(MarkdownBlocksState mb)
    {
        if (mb.line.StartsWith(">", StringComparison.Ordinal))
        {
            var quoteLines = new List<List<Run>>();
            while (mb.i < mb.lines.Length)
            {
                var q = mb.lines[mb.i].TrimEnd('\r');
                if (!q.StartsWith(">", StringComparison.Ordinal)) break;
                while (q.StartsWith(">", StringComparison.Ordinal)) q = q.TrimStart('>').TrimStart();
                quoteLines.Add(ParseInline(q.TrimEnd()));
                mb.i++;
            }
            mb.blocks.Add(new QuoteBlk(quoteLines));
            return true;
        }
        return false;
    }

    /// <summary>A pipe table: a header row followed by an alignment row and body rows split at the pipes. True when handled.</summary>
    private static bool ParseMarkdownTable(MarkdownBlocksState mb)
    {
        if (mb.line.Contains('|') && mb.i + 1 < mb.lines.Length
            && IsTableDividerLine(mb.lines[mb.i + 1].TrimEnd('\r')))
        {
            var rows = new List<List<List<Run>>> { SplitTableRow(mb.line) };
            var j = mb.i + 2;
            for (; j < mb.lines.Length; j++)
            {
                var rowLine = mb.lines[j].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(rowLine) || !rowLine.Contains('|')) break;
                rows.Add(SplitTableRow(rowLine));
            }
            mb.blocks.Add(new TableBlk(rows));
            mb.i = j;
            return true;
        }
        return false;
    }
}
