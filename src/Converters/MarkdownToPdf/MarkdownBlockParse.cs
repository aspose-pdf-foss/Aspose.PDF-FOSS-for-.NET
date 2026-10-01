using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class MarkdownToPdfConverter
{
    /// <summary>Parses one block at the cursor: a fenced code block, a thematic break, a pipe table, a setext or ATX heading, a block quote, a list, an indented code block, an inline code line, raw HTML, or a paragraph of hard lines.</summary>
    private static bool ParseMarkdownBlock(MarkdownBlocksState mb)
    {
        mb.line = mb.lines[mb.i].TrimEnd('\r');
        if (string.IsNullOrWhiteSpace(mb.line)) { mb.i++; return true; }
        mb.trimmed = mb.line.Trim();

        // Fenced code.
        if (mb.trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var code = new List<string>();
            mb.i++;
            while (mb.i < mb.lines.Length && !mb.lines[mb.i].TrimEnd('\r').TrimStart().StartsWith("```", StringComparison.Ordinal))
                code.Add(mb.lines[mb.i++].TrimEnd('\r').TrimEnd());
            mb.i++; // closing fence
            mb.blocks.Add(new CodeBlk(code, CodeBlockSize));
            return true;
        }

        // Thematic break: 3+ of the same * - _ character, spaces allowed between.
        if (Regex.IsMatch(mb.line, @"^\s*([-*_])(\s*\1){2,}\s*$"))
        {
            mb.blocks.Add(new HrBlk());
            mb.i++;
            return true;
        }

        // Pipe table: a cell row directly above a divider row of dashes.
        if (ParseMarkdownTable(mb)) return true;

        // Setext heading: a plain text line underlined by a run of = (H1) or - (H2).
        if (!IsBlockLine(mb.line) && mb.i + 1 < mb.lines.Length)
        {
            var next = mb.lines[mb.i + 1].TrimEnd('\r').Trim();
            if (next.Length > 0 && (next.All(c => c == '=') || next.All(c => c == '-')))
            {
                mb.blocks.Add(new HeadBlk(next[0] == '=' ? 1 : 2, ParseInline(mb.trimmed)));
                mb.i += 2;
                return true;
            }
        }

        mb.headingMatch = Regex.Match(mb.line, @"^(#{1,6})\s+(.+)$");
        if (mb.headingMatch.Success)
        {
            mb.blocks.Add(new HeadBlk(mb.headingMatch.Groups[1].Value.Length,
                ParseInline(mb.headingMatch.Groups[2].Value.Trim())));
            mb.i++;
            return true;
        }

        // Block quote.
        if (ParseMarkdownBlockQuote(mb)) return true;

        mb.ulMatch = Regex.Match(mb.line, @"^(\s*)[*+\-]\s+(.+)$");
        mb.olMatch = Regex.Match(mb.line, @"^(\s*)(\d+)[.)]\s+(.+)$");
        if (ParseMarkdownList(mb)) return true;

        // Indented code (4 spaces or a tab).
        if (ParseMarkdownIndentedCode(mb)) return true;

        // A whole line wrapped in single backticks = inline code.
        if (mb.trimmed.Length >= 2 && mb.trimmed.StartsWith("`", StringComparison.Ordinal)
            && mb.trimmed.EndsWith("`", StringComparison.Ordinal))
        {
            mb.blocks.Add(new CodeBlk(new List<string> { mb.trimmed.Substring(1, mb.trimmed.Length - 2) }, InlineCodeSize));
            mb.i++;
            return true;
        }

        // HTML block: runs to the next blank line. An <img> inside becomes an image
        // block (centred under align="center"); everything else is dropped.
        if (ParseMarkdownHtmlBlock(mb)) return true;

        mb.hardLines = new List<List<Run>>();
        mb.buf = new StringBuilder();
        CollectMarkdownParagraph(mb);
        if (mb.buf.Length > 0) mb.hardLines.Add(ParseInline(mb.buf.ToString()));
        if (mb.hardLines.Count > 0) mb.blocks.Add(new ParaBlk(mb.hardLines));
        else mb.i++;
        return true;
    }
}
