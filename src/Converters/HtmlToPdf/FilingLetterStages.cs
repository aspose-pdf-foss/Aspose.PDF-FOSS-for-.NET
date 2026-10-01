using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the filing-letter parse: the line end and one scanned token.
    private static void EndLine(FilingLetterState fl, bool blankIfEmpty)
    {
        var t = fl.cur.Trim();
        fl.cur = "";
        if (t.Length > 0)
        {
            fl.parsed.Add(new FilingItem
            {
                Text = t, ExtraGap = fl.gapNext, AlignLeft = fl.leftDepth > 0, IndentPt = fl.indent,
            });
            fl.gapNext = 0;
        }
        else if (blankIfEmpty)
        {
            fl.parsed.Add(new FilingItem { Blank = true, ExtraGap = fl.gapNext });
            fl.gapNext = 0;
        }
    }

    /// <summary>The stages of the filing-letter parse: the line end and one scanned token.</summary>
    private static bool ScanFilingLetterToken(FilingLetterState fl)
    {
        if (fl.body[fl.i] != '<')
        {
            var j = fl.body.IndexOf('<', fl.i);
            if (j < 0) j = fl.n;
            var txt = Regex.Replace(DecodeEntities(fl.body[fl.i..j]).Replace('\u00A0', ' '), @"\s+", " ");
            if (txt.Trim().Length > 0 || fl.cur.Length > 0 && txt.Length > 0)
                fl.cur += fl.cur.Length == 0 ? txt.TrimStart() : txt;
            fl.i = j;
            return true;
        }
        var end = fl.body.IndexOf('>', fl.i);
        if (end < 0) return false;
        var tagStr = fl.body[fl.i..(end + 1)];
        fl.i = end + 1;
        var nm = Regex.Match(tagStr, @"^</?\s*([A-Za-z][A-Za-z0-9]*)");
        if (!nm.Success) return true;
        var tag = nm.Groups[1].Value.ToLowerInvariant();
        var isClose = tagStr[1] == '/';
        var style = Regex.Match(tagStr, @"style\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase).Groups[2].Value;

        if (tag == "br") { EndLine(fl, blankIfEmpty: true); return true; }
        if (tag == "img" && !isClose)
        {
            EndLine(fl, blankIfEmpty: false);
            var sm = Regex.Match(tagStr, @"\bsrc\s*=\s*['""]?([^'""\s>]+)", RegexOptions.IgnoreCase);
            if (sm.Success)
            {
                fl.parsed.Add(new FilingItem { ImgSrc = sm.Groups[1].Value, ExtraGap = fl.gapNext });
                fl.gapNext = 0;
            }
            return true;
        }
        if (tag is "div" or "p" or "table" or "thead" or "tbody" or "tr")
        {
            EndLine(fl, blankIfEmpty: false);
            if (tag == "p" && !isClose
                && Regex.IsMatch(style, @"margin-left\s*:\s*1cm", RegexOptions.IgnoreCase))
                fl.indentDepth = fl.depth + 1;
            if (tag == "div" || tag == "p")
            {
                if (!isClose)
                {
                    fl.depth++;
                    if (Regex.IsMatch(style, @"text-align\s*:\s*left", RegexOptions.IgnoreCase) && fl.leftDepth == 0)
                        fl.leftDepth = fl.depth;
                }
                else
                {
                    if (fl.leftDepth == fl.depth) fl.leftDepth = 0;
                    if (fl.indentDepth == fl.depth) fl.indentDepth = 0;
                    fl.depth--;
                }
            }
            fl.indent = fl.indentDepth > 0 ? 28.35 : 0;
            return true;
        }
        if (tag is "td" or "th") { if (!isClose) fl.cur += fl.cur.Length > 0 ? "  " : ""; return true; }
        return true;
    }
}
