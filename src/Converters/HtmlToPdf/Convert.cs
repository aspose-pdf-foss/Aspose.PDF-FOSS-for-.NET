using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Regex-replace applied only OUTSIDE inline <c>&lt;svg&gt;…&lt;/svg&gt;</c>
    /// islands — SVG content keeps its own element vocabulary.</summary>
    private static string ReplaceOutsideSvg(string html, string pattern, string replacement)
    {
        var sb = new StringBuilder(html.Length);
        var pos = 0;
        while (pos < html.Length)
        {
            var open = html.IndexOf("<svg", pos, System.StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                sb.Append(Regex.Replace(html[pos..], pattern, replacement, RegexOptions.IgnoreCase));
                break;
            }
            sb.Append(Regex.Replace(html[pos..open], pattern, replacement, RegexOptions.IgnoreCase));
            var close = html.IndexOf("</svg", open, System.StringComparison.OrdinalIgnoreCase);
            var end = close < 0 ? html.Length : html.IndexOf('>', close) is var gt && gt >= 0 ? gt + 1 : html.Length;
            sb.Append(html, open, end - open);
            pos = end;
        }
        return sb.ToString();
    }

    private static Document ConvertFromHtml(string html, HtmlLoadOptions? options)
    {
        // HTML produced by this library's own PDF→HTML converter (absolutely-positioned
        // pdf-text spans inside fixed-size pdf-page divs) round-trips through a
        // dedicated geometric path. The PNG-page-background stl_ dialect re-imports
        // through the padded POSITIONED path (each line keeps its
        // 6pt-inset offset and the page widens to the pinned content). Otherwise,
        // when the page's stylesheet is resolvable (inline, or linked and reachable)
        // the content re-imports at its fixed positions onto print sheets; when it
        // is not, the spans are regrouped into source lines and reflowed as text.
        //
        // The stl_ dialect's geometry lives ENTIRELY in its stylesheet — class boxes,
        // font sizes, the background wrapper. When that stylesheet is a linked file
        // and the base path was not one the caller supplied, it is not
        // reached (external resources resolve only against an explicit base
        // path, not one auto-derived from the loaded file's own directory): none of
        // the fixed geometry is then available, and the converter reflows the
        // positioned spans into text rather than replaying an empty fixed layout.
        // An auto-derived base path is therefore treated as absent when deciding
        // whether the stl_ CSS resolves (mirrors TryConvertPositionedFixedLayout).
        // A binary file fed through HtmlLoadOptions (an OLE2 document renamed .html):
        // the mojibake lays out as ONE anonymous Times 12 pt
        // paragraph on a page WIDENED to its min-content width. C0 control bytes are
        // the signature — real HTML text never carries them.
        if (TryConvertSpecialHtmlForm(html, options) is { } special) return special;
        // The archaic <image> tag parses as <img> (the HTML standard's alias) —
        // without it a legacy page's pictures never reach the image pipeline.
        // Only OUTSIDE inline <svg> islands: SVG's own <image> element is a real
        // element there (an exported page's photo rides one), and rewriting it
        // to <img> silently drops the picture from the SVG render.
        html = ReplaceOutsideSvg(html, @"<image\b", "<img");

        // A table-part tag AFTER the document's LAST </table> is IGNORED by the
        // HTML5 "in body" insertion mode (its text content still flows). A stray
        // <tr><td class="page-break"/></tr> left behind the final </table> must
        // not reach the flow — its break class would page-break a document the
        // reference keeps whole. Only the TRAILING junk is dropped: a stray part
        // in the middle of the document rides markup too broken to re-balance here.
        {
            var lastTableClose = -1;
            foreach (Match tc in Regex.Matches(html, @"</table\s*>", RegexOptions.IgnoreCase))
                lastTableClose = tc.Index + tc.Length;
            if (lastTableClose >= 0
                && !Regex.IsMatch(html[lastTableClose..], @"<table\b", RegexOptions.IgnoreCase)
                && Regex.IsMatch(html[lastTableClose..], @"<t[rdh]\b", RegexOptions.IgnoreCase))
                html = html[..lastTableClose] + Regex.Replace(html[lastTableClose..],
                    @"</?(tr|td|th|tbody|thead|tfoot|caption|colgroup|col)\b[^>]*>", "",
                    RegexOptions.IgnoreCase);
        }

        // Page scripts run before layout: a straight-line
        // script that only builds a string and appends a text node contributes that
        // text to the flow. The micro-interpreter replaces each fully-evaluable
        // <script> with its appendChild output in place; every other script keeps
        // the existing strip (see HtmlToPdfConverter.Script.cs).
        if (html.Contains("<script", StringComparison.OrdinalIgnoreCase))
            html = ApplyTrivialDomScripts(html);

        var cv = new ConvertState();
        cv.html = html;
        // A bogus comment (`<!SUMMARY TOTALS>` - a `<!` that opens neither a comment, a doctype nor a
        // conditional block) is dropped whole, the way the browser's tokenizer drops it.
        cv.html = Regex.Replace(cv.html, @"<!(?!--|\[|doctype)[^>]*>", "", RegexOptions.IgnoreCase);
        cv.wikiExportDoc = cv.html.Contains("mw-parser-output", StringComparison.Ordinal) && cv.html.Contains("mw-list-item", StringComparison.Ordinal) && cv.html.Contains("load.php", StringComparison.Ordinal);
        if (cv.wikiExportDoc)
        {
            ConvertWikiExportDoc(cv);
        }

        // Fold external <link rel="stylesheet"> files into the document as inline <style>
        // blocks so the legacy flow's CSS scan (ParseStyleSheet, ParseBeforeMarkers, …) sees
        // their rules — a browser applies a linked stylesheet identically to an inline one.
        // Resolved through the same loader as images (CustomLoaderOfExternalResources first,
        // then the BasePath); an unreachable stylesheet leaves the tag untouched.
        if (ConvertPageSetup(options, cv) is { } setupDoc) return setupDoc;
        if (ConvertBodyScan(options, cv) is { } scanDoc) return scanDoc;
        if (ConvertBlockBuild(options, cv) is { } buildDoc) return buildDoc;
        return ConvertRenderPages(options, cv);
    }
}
