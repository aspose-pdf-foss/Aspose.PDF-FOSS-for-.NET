using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The hospital DISCHARGE LETTER preview (the `.letterPreview` table of the clinical-letters
// stylesheet): a five-cell thead that repeats on every page, one td.main cell whose
// div#content holds the right-floated hospital header, the centred "Inpatient Letter"
// title, the patient table, the titled paragraph sections, the TTO grid and the GP tables
// between UA hr rules, and a tfoot reference that closes every page's fragment. The sheet
// sets a 62.5% body (7.5 pt) in Tahoma and gives every div, table, td and span 1.02em, so
// a size compounds along its ancestor chain; the print media shows the thead at a 14px
// line and keeps every content div and table whole across pages, a block that cannot fit
// leaving its top margin behind and opening the next page under the repeated thead.
// Constants not read from the stylesheet are measured on the licensed render.
internal static partial class HtmlToPdfConverter
{
    private const double LpRootPt = 7.5;                // body { font-size: 62.5% } of the 12 pt UA root
    private const double LpEmStep = 1.02;               // div, table, td, span { font-size: 1.02em }
    private const double LpUaMarginX = 90.0;            // the UA sheet's side margins
    private const double LpUaMarginY = 72.0;            // …and its top and bottom
    private const double LpTheadLine = 10.5;            // table thead th { line-height: 14px } (print)
    private const double LpTheadPad = 0.75;             // the UA 1px th padding
    private const double LpMainPadSide = 7.5;           // td.main { padding: 0 10px 5px 10px }
    private const double LpMainPadBottom = 3.75;
    private const double LpDivPad = 1.5;                // .letterPreview div { padding: 2px }
    private const double LpDivMargin = 7.5;             // .letterPreview div { margin: 10px 0 0 0 }
    private const double LpHeader1MarginRight = 7.5;    // div#tdHeader1 { margin-right: 10px }
    private const double LpHrMarginEm = 0.5;            // the UA hr margin
    private const double LpHrHeight = 1.5;              // the UA hr's 1px inset border, top and bottom
    private const double LpParaPadBottom = 6.0;         // div.paragraph { padding-bottom: 8px }
    private const double LpH2MarginEm = 0.75;           // the h2 margin of the current era
    private const double LpH2Scale = 1.1;               // #divTTO h2 { font-size: 1.1em }
    private const string LpH2Rgb = "0 0 1";             // … color: Blue
    private const int LpContentDepth = 3;               // table.letterPreview > td.main > div#content
    private static readonly double[] LpHeaderEms = { 2.0, 1.8, 1.4, 1.2 };  // div#tdHeader1..4

    private static double LpFsAt(int depth) => LpRootPt * Math.Pow(LpEmStep, depth);

    /// <summary>Render the discharge-letter preview, or null when the page does not carry
    /// the idiom's markup.</summary>
    private static Document? TryRenderLetterPreview(ConvertState cv)
    {
        var html = cv.html;
        if (!Regex.IsMatch(html, "<table\\b[^>]*class\\s*=\\s*['\"]letterPreview['\"]", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, "<td\\b[^>]*class\\s*=\\s*['\"]main['\"]", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, "id\\s*=\\s*['\"]content['\"]", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, "id\\s*=\\s*['\"]tdHeader1['\"]", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor(LpFace) is not { } tm) return null;
        var lp = new LetterPreviewState
        {
            html = html,
            tm = tm,
            invc = System.Globalization.CultureInfo.InvariantCulture,
            pageWidth = cv.pageWidth,
            pageHeight = cv.pageHeight,
            marginL = cv.marginsExplicit ? cv.marginLeft : LpUaMarginX,
            marginR = cv.marginsExplicit ? cv.marginRight : LpUaMarginX,
            marginT = cv.marginsExplicit ? cv.marginTop : LpUaMarginY,
            marginB = cv.marginsExplicit ? cv.marginBottom : LpUaMarginY,
            sb = new System.Text.StringBuilder(),
        };
        lp.contentL = lp.marginL;
        lp.contentR = lp.pageWidth - lp.marginR;
        lp.innerL = lp.contentL + LpMainPadSide + LpDivPad;
        lp.innerR = lp.contentR - LpMainPadSide + LpDivPad;
        lp.limit = lp.pageHeight - lp.marginB;
        if (!LpParseLetter(lp)) return null;
        lp.doc = new Document();
        LpRenderPages(lp);
        return lp.doc;
    }

    /// <summary>The thead cells, the footer reference and div#content's children in order.</summary>
    private static bool LpParseLetter(LetterPreviewState lp)
    {
        var s = lp.html;
        var thead = Regex.Match(s, "<thead\\b[^>]*>([\\s\\S]*?)</thead>", RegexOptions.IgnoreCase);
        if (!thead.Success) return false;
        foreach (Match th in Regex.Matches(thead.Groups[1].Value, "<th\\b([^>]*)>([\\s\\S]*?)</th>", RegexOptions.IgnoreCase))
        {
            var pct = Regex.Match(th.Groups[1].Value, "width\\s*:\\s*(\\d+(?:\\.\\d+)?)\\s*%", RegexOptions.IgnoreCase);
            lp.thead.Add((LpFlat(th.Groups[2].Value),
                pct.Success ? double.Parse(pct.Groups[1].Value, lp.invc) / 100.0 : 0));
        }
        var foot = Regex.Match(s, "<td\\b[^>]*id\\s*=\\s*['\"]tdFooter['\"][^>]*>([\\s\\S]*?)</td>", RegexOptions.IgnoreCase);
        lp.footer = foot.Success ? LpFlat(foot.Groups[1].Value) : "";
        var content = Regex.Match(s, "<div\\b[^>]*id\\s*=\\s*['\"]content['\"][^>]*>", RegexOptions.IgnoreCase);
        if (!content.Success) return false;
        var start = content.Index + content.Length;
        var inner = s[start..DivClose(s, start)];
        LpParseContent(lp, inner);
        return lp.items.Count > 0;
    }

    private static readonly Regex LpItemOpenRx = new("<(div|br|hr|table)\\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void LpParseContent(LetterPreviewState lp, string inner)
    {
        var pos = 0;
        while (pos < inner.Length)
        {
            var m = LpItemOpenRx.Match(inner, pos);
            if (!m.Success) break;
            var tag = m.Groups[1].Value.ToLowerInvariant();
            var attrs = m.Groups[2].Value;
            var after = m.Index + m.Length;
            if (tag == "br")
            {
                // the clearing br ends the empty line beside the floats; only the next opens a line
                if (!Regex.IsMatch(attrs, "clear\\s*:", RegexOptions.IgnoreCase)) lp.items.Add(new LpItem { Kind = LpKind.Br });
                pos = after;
            }
            else if (tag == "hr") { lp.items.Add(new LpItem { Kind = LpKind.Hr }); pos = after; }
            else if (tag == "table")
            {
                var end = LpTableClose(inner, m.Index);
                lp.items.Add(new LpItem { Kind = LpKind.Table, Table = LpParseTable(inner[m.Index..end], LpContentDepth + 1, null) });
                pos = end;
            }
            else
            {
                var end = DivClose(inner, after);
                LpParseDiv(lp, attrs, inner[after..end]);
                pos = Math.Min(inner.Length, end + "</div>".Length);
            }
        }
    }

    private static void LpParseDiv(LetterPreviewState lp, string attrs, string body)
    {
        var id = Regex.Match(attrs, "id\\s*=\\s*['\"]?([^'\"\\s>]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        var cls = Regex.Match(attrs, "class\\s*=\\s*['\"]?([^'\"\\s>]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        var hdr = Regex.Match(id, "^tdHeader([1-4])$");
        var baseFs = LpFsAt(LpContentDepth);
        if (hdr.Success)
        {
            var n = int.Parse(hdr.Groups[1].Value, lp.invc);
            lp.items.Add(new LpItem
            {
                Kind = LpKind.HeaderFloat, Text = LpFlat(body), Fs = baseFs * LpHeaderEms[n - 1],
                RightInset = LpDivPad + (n == 1 ? LpHeader1MarginRight : 0),
            });
            return;
        }
        if (id == "divTTO") { LpParseTto(lp, body); return; }
        if (cls is "paragraph" or "paragraphOptional") { LpParseParagraph(lp, body); return; }
        // a styled div: centred at its inline em size, its own margin and padding when the style zeroes them
        var em = Regex.Match(attrs, "font-size\\s*:\\s*(\\d+(?:\\.\\d+)?)\\s*em", RegexOptions.IgnoreCase);
        var zeroBox = Regex.IsMatch(attrs, "margin\\s*:\\s*0", RegexOptions.IgnoreCase);
        lp.items.Add(new LpItem
        {
            Kind = LpKind.CenterDiv, Text = LpFlat(body),
            Fs = em.Success ? baseFs * double.Parse(em.Groups[1].Value, lp.invc) : baseFs * LpEmStep,
            Bold = Regex.IsMatch(attrs, "font-weight\\s*:\\s*bold", RegexOptions.IgnoreCase),
            MarginTop = zeroBox ? 0 : LpDivMargin, Pad = zeroBox ? 0 : LpDivPad,
        });
    }

    /// <summary>A titled section: div.paragraph > div.content > span.title + text with br lines.</summary>
    private static void LpParseParagraph(LetterPreviewState lp, string body)
    {
        var content = Regex.Match(body, "<div\\b[^>]*class\\s*=\\s*['\"]content['\"][^>]*>", RegexOptions.IgnoreCase);
        var innerStart = content.Success ? content.Index + content.Length : 0;
        var inner = content.Success ? body[innerStart..DivClose(body, innerStart)] : body;
        var title = Regex.Match(inner, "<span\\b[^>]*class\\s*=\\s*['\"]title['\"][^>]*>([\\s\\S]*?)</span>", RegexOptions.IgnoreCase);
        var rest = title.Success ? inner[(title.Index + title.Length)..] : inner;
        var item = new LpItem { Kind = LpKind.Paragraph, Text = title.Success ? LpFlat(title.Groups[1].Value) : "" };
        var lead = Regex.Match(rest, "^\\s*<br\\s*/?>", RegexOptions.IgnoreCase);
        item.TitleAlone = lead.Success;
        if (lead.Success) rest = rest[lead.Length..];
        item.Segments = LpSegments(rest);
        if (!item.TitleAlone && item.Segments.Count > 0)
        {
            // the text on the title's line keeps its leading space
            var first = Regex.Replace(DecodeEntities(Regex.Replace(Regex.Split(rest, "<br\\s*/?>", RegexOptions.IgnoreCase)[0], "<[^>]+>", "")), "[ \\t\\r\\n]+", " ");
            item.Segments[0] = first.TrimEnd(' ');
        }
        lp.items.Add(item);
    }

    private static void LpParseTto(LetterPreviewState lp, string body)
    {
        var item = new LpItem { Kind = LpKind.TtoDiv };
        var h2 = Regex.Match(body, "<h2\\b[^>]*>([\\s\\S]*?)</h2>", RegexOptions.IgnoreCase);
        item.H2 = h2.Success ? LpFlat(h2.Groups[1].Value) : "";
        var tbl = Regex.Match(body, "<table\\b", RegexOptions.IgnoreCase);
        if (tbl.Success)
            item.Table = LpParseTable(body[tbl.Index..LpTableClose(body, tbl.Index)], LpContentDepth + 3, null);
        var auth = Regex.Match(body, "<div\\b[^>]*id\\s*=\\s*['\"]authoriseTTO['\"][^>]*>", RegexOptions.IgnoreCase);
        if (auth.Success)
        {
            var s = auth.Index + auth.Length;
            item.Authorise = LpFlat(body[s..DivClose(body, s)]);
        }
        lp.items.Add(item);
    }
}
