#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.PdfToMarkdown;

internal static partial class MarkdownRenderer
{
    private static List<LinkInfo> CollectLinks(Page page)
    {
        var links = new List<LinkInfo>();
        foreach (var annotation in page.Annotations)
        {
            if (annotation is Aspose.Pdf.Annotations.LinkAnnotation link &&
                !string.IsNullOrEmpty(link.Uri) && link.Rect != null)
                links.Add(new LinkInfo(link.Rect, link.Uri));
        }
        return links;
    }

    private static string LinkFor(Elem e, List<LinkInfo> links)
    {
        if (links.Count == 0 || e.IsImage) return null;
        var cx = (e.LLX + e.URX) / 2;
        var cy = (e.LLY + e.URY) / 2;
        foreach (var link in links)
        {
            var lr = link.Rect;
            if (cx >= lr.LLX && cx <= lr.URX && cy >= lr.LLY && cy <= lr.URY)
                return link.Uri;
        }
        return null;
    }

    private static string RenderHeading(Line line, int level, bool styled)
    {
        var text = line.Text;
        var img = line.CharIsImage;
        var n = text.Length;

        var coreStart = 0;
        while (coreStart < n && (img[coreStart] || text[coreStart] == ' ')) coreStart++;
        var coreEnd = n;
        while (coreEnd > coreStart && (img[coreEnd - 1] || text[coreEnd - 1] == ' ')) coreEnd--;

        var prefix = text.Substring(0, coreStart);
        var suffix = text.Substring(coreEnd);
        // A heading collapses a run of trailing spaces to one (e.g. source "Education:  ").
        if (suffix.Length > 1 && suffix.All(c => c == ' ')) suffix = " ";
        // An HTML image tag flanking a heading's text declares which side it floats on.
        prefix = InjectImgAlign(prefix, "left");
        suffix = InjectImgAlign(suffix, "right");
        var core = text.Substring(coreStart, coreEnd - coreStart);

        // Headings are already visually bold, so bold is never marked; only italic is.
        var inner = ApplyStyle(EscapeText(core), (byte)(styled && line.IsItalic ? 2 : 0));
        return new string('#', level) + " " + prefix + inner + suffix;
    }

    private static bool IsHtmlImageToken(string token)
        => token != null && token.StartsWith("<img ", StringComparison.Ordinal);

    /// <summary>Insert <c>align="…"</c> into each HTML image tag of a heading's flank,
    /// ahead of its <c>width</c> attribute (markdown tokens pass through untouched).</summary>
    private static string InjectImgAlign(string flank, string side)
    {
        if (!flank.Contains("<img ")) return flank;
        return flank.Replace(" width=\"", $" align=\"{side}\"  width=\"");
    }

    private static string RenderParagraph(List<Line> paragraph, bool columnar = false)
    {
        var logical = new List<(string text, List<string> uris, List<bool> img, List<byte> style)>();
        var buf = new StringBuilder();
        var bufUris = new List<string>();
        var bufImg = new List<bool>();
        var bufStyle = new List<byte>();
        for (var k = 0; k < paragraph.Count; k++)
        {
            AppendMerged(buf, bufUris, bufImg, bufStyle, paragraph[k]);
            var isLast = k == paragraph.Count - 1;
            // Soft break at a source-line boundary. The default (single-column) rule keeps a
            // line only when it ends a sentence ('.') and the next begins one. The columnar
            // rule uses a richer line-break heuristic (a new list item,
            // a colon/semicolon-terminated line, a numbered item, or a capitalised sentence
            // start after a period), which the tight column layouts depend on.
            var lineBreak = false;
            if (!isLast)
            {
                if (columnar)
                {
                    lineBreak = IsLineBreakRequired(paragraph, k);
                }
                else
                {
                    var cur = paragraph[k].Text.TrimEnd();
                    var next = paragraph[k + 1].Text.TrimStart();
                    lineBreak = cur.EndsWith(".", StringComparison.Ordinal) && next.Length > 0
                        && (char.IsUpper(next[0]) || char.IsDigit(next[0]));
                }
            }
            if (isLast || lineBreak)
            {
                logical.Add((buf.ToString(), new List<string>(bufUris),
                    new List<bool>(bufImg), new List<byte>(bufStyle)));
                buf.Clear();
                bufUris.Clear();
                bufImg.Clear();
                bufStyle.Clear();
            }
        }

        var sb = new StringBuilder();
        for (var j = 0; j < logical.Count; j++)
        {
            var emitted = EmitRuns(logical[j].text, logical[j].uris, logical[j].img,
                logical[j].style, 0, logical[j].text.Length);
            if (j < logical.Count - 1)
                sb.Append(emitted.TrimEnd(' ')).Append(SoftBreak).Append(NewLine);
            else
                sb.Append(emitted);
        }
        return sb.ToString();
    }

    // Line-break heuristic used for multi-column paragraphs. A source line keeps its own
    // output line when the NEXT line begins a list item ('-'), a number, or a capitalised
    // sentence following a period/TOC leader, or when THIS line ends with a colon/semicolon.
    private static bool IsLineBreakRequired(List<Line> lines, int lineIndex)
    {
        if (lineIndex >= lines.Count - 1) return false;
        var cur = lines[lineIndex];
        var next = lines[lineIndex + 1];

        // A hyperlink that continues across the break stays on one line.
        var u1 = LastCharUri(cur);
        var u2 = FirstCharUri(next);
        if (u1 != null && u2 != null && string.Equals(u1, u2, StringComparison.Ordinal))
            return false;

        return IsNeedNewLine(cur.Text, next.Text);
    }

    private static bool IsNeedNewLine(string last, string next)
    {
        if (StartsWithMinus(next)) return true;
        if (EndsWithColonOrSemicolon(last)) return true;
        if (StartsWithNumbering(next)) return true;
        var capital = StartsWithCapital(next);
        if (capital && EndsInADot(last)) return true;
        if (capital && TableOfContentsSecond(last)) return true;
        return false;
    }

    private static bool StartsWithMinus(string w)
    {
        foreach (var c in w)
        {
            if (char.IsWhiteSpace(c)) continue;
            return c is '-' or '•' or '·' or '–' or '—'; // -, •, ·, –, —
        }
        return false;
    }

    private static bool StartsWithCapital(string w)
    {
        foreach (var c in w)
        {
            if (char.IsWhiteSpace(c)) continue;
            return char.IsUpper(c);
        }
        return false;
    }

    private static bool EndsWithColonOrSemicolon(string w)
    {
        for (var i = w.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(w[i])) continue;
            return w[i] == ':' || w[i] == ';';
        }
        return false;
    }

    private static bool EndsInADot(string w)
    {
        for (var i = w.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(w[i])) continue;
            return w[i] == '.';
        }
        return false;
    }

    // Leading digits with optional dotted groups then whitespace (1 / 1. / 1.2 / 1.2.).
    private static bool StartsWithNumbering(string w)
    {
        bool lastNum = false, lastDot = false;
        foreach (var c in w)
        {
            if (char.IsDigit(c)) { lastDot = false; lastNum = true; }
            else if (c == '.')
            {
                if (lastDot || !lastNum) return false;
                lastDot = true; lastNum = false;
            }
            else if (char.IsWhiteSpace(c))
                return lastNum || lastDot;
            else
                return false;
        }
        return false;
    }

    // The line ends with a table-of-contents leader: three-or-more dot groups then a number.
    private static bool TableOfContentsSecond(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        var index = word.Length - 1;
        while (index >= 0 && char.IsWhiteSpace(word[index])) index--;
        if (index < 0) return false;
        var digitCount = 0;
        while (index >= 0 && char.IsDigit(word[index])) { index--; digitCount++; }
        if (digitCount == 0) return false;
        while (index >= 0 && char.IsWhiteSpace(word[index])) index--;
        if (index < 0) return false;
        var dotGroups = 0;
        while (index >= 0)
        {
            while (index >= 0 && char.IsWhiteSpace(word[index])) index--;
            if (index < 0 || word[index] != '.') break;
            index--;
            while (index >= 0 && char.IsWhiteSpace(word[index])) index--;
            dotGroups++;
        }
        return dotGroups >= 3;
    }

    private static string LastCharUri(Line line)
    {
        for (var i = line.CharUris.Count - 1; i >= 0; i--)
        {
            if (i < line.Text.Length && char.IsWhiteSpace(line.Text[i])) continue;
            if (line.CharUris[i] != null) return line.CharUris[i];
            break;
        }
        return null;
    }

    private static string FirstCharUri(Line line)
    {
        for (var i = 0; i < line.CharUris.Count; i++)
        {
            if (i < line.Text.Length && char.IsWhiteSpace(line.Text[i])) continue;
            return line.CharUris[i];
        }
        return null;
    }

    private static string EmitRuns(string raw, List<string> uris, List<bool> img,
        List<byte> style, int start, int end)
    {
        var sb = new StringBuilder();
        var i = start;
        var prevWasLink = false;
        while (i < end)
        {
            var uri = uris[i];
            var isImg = img[i];
            var st = style[i];
            var j = i;
            while (j < end && uris[j] == uri && img[j] == isImg && style[j] == st) j++;
            var seg = raw.Substring(i, j - i);
            if (isImg)
            {
                sb.Append(seg);
                prevWasLink = false;
            }
            else if (seg.Trim().Length == 0)
            {
                // A whitespace-only run passes through as-is (lead and trail would
                // otherwise both capture the same characters and double them).
                sb.Append(seg);
                prevWasLink = false;
            }
            else
            {
                // Style markers wrap the trimmed run; surrounding whitespace stays outside.
                var lead = seg.Substring(0, seg.Length - seg.TrimStart().Length);
                var trail = seg.Substring(seg.TrimEnd().Length);
                var coreText = ApplyStyle(EscapeText(seg.Trim()), st);
                var isLink = uri != null && coreText.Length > 0;
                if (isLink)
                    coreText = "[" + coreText + "](" + uri + ")";
                // A link is space-delimited on both sides, even from an abutting glyph
                // (the punctuation after "[TLS encryption](…) ," or the literal bracket
                // of a "\[ [edit](…) \]" wiki affordance).
                if ((isLink || prevWasLink) && lead.Length == 0 && coreText.Length > 0
                    && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    lead = " ";
                sb.Append(lead).Append(coreText).Append(trail);
                prevWasLink = isLink && trail.Length == 0;
            }
            i = j;
        }
        return sb.ToString();
    }

    /// <summary>Wrap already-escaped text in emphasis markers. Superscript/subscript use
    /// <c>^…^</c>/<c>~…~</c>; otherwise strikethrough is outermost, then bold+italic.</summary>
    private static string ApplyStyle(string escaped, byte style)
    {
        if (escaped.Length == 0) return escaped;
        if ((style & 8) != 0) return "^" + escaped + "^";   // superscript
        if ((style & 16) != 0) return "~" + escaped + "~";  // subscript

        var bold = (style & 1) != 0;
        var italic = (style & 2) != 0;
        var emphasis = (bold, italic) switch
        {
            (true, true) => "***",
            (true, false) => "**",
            (false, true) => "*",
            _ => string.Empty,
        };
        var result = emphasis.Length == 0 ? escaped : emphasis + escaped + emphasis;
        if ((style & 4) != 0) result = "~~" + result + "~~";  // strikethrough, outermost
        return result;
    }

    private static void AppendMerged(StringBuilder buf, List<string> bufUris, List<bool> bufImg,
        List<byte> bufStyle, Line line)
    {
        if (buf.Length != 0)
        {
            var last = buf[buf.Length - 1];
            var first = line.Text.Length > 0 ? line.Text[0] : '\0';
            if (last != ' ' && first != ' ' && first != '\0')
            {
                buf.Append(' ');
                bufUris.Add(null);
                bufImg.Add(false);
                // Keep the joining space inside a same-style span across a wrapped line.
                var lastStyle = bufStyle.Count > 0 ? bufStyle[bufStyle.Count - 1] : (byte)0;
                var firstStyle = line.CharStyle.Count > 0 ? line.CharStyle[0] : (byte)0;
                bufStyle.Add(lastStyle == firstStyle ? lastStyle : (byte)0);
            }
        }
        buf.Append(line.Text);
        bufUris.AddRange(line.CharUris);
        bufImg.AddRange(line.CharIsImage);
        bufStyle.AddRange(line.CharStyle);
    }

    private static string EscapeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '-')
            {
                // A hyphen is escaped except when it is a leading list-marker dash — the first
                // character followed by whitespace ("- item"). A mid-line or joined hyphen
                // ("July.09 - Present", "T-SQL", "-(X)") is escaped.
                var next = i + 1 < text.Length ? text[i + 1] : ' ';
                if (!(i == 0 && char.IsWhiteSpace(next))) sb.Append('\\');
            }
            else if (c is '_' or '#' or '[' or ']' or '*')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
