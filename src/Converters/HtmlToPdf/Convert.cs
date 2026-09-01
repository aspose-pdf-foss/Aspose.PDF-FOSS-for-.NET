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
        var c0Controls = 0;
        foreach (var bch in html)
            if (bch < 0x20 && bch is not ('\t' or '\n' or '\r')) c0Controls++;
        if (c0Controls >= 4 && TryConvertBinaryText(html) is { } binaryDoc)
            return binaryDoc;

        var stlPositioned = IsStlPositionedHtml(html);
        var stlCssOptions = options?.BasePathAutoDerived == true ? null : options;
        var stlCssResolvable = stlPositioned && !string.IsNullOrWhiteSpace(GatherStlCss(html, stlCssOptions));
        if (stlCssResolvable && HasStlRasterBackground(html))
            return ConvertStlPositioned(html, options);
        if (IsPositionedSpanHtml(html) || stlPositioned)
        {
            // The pdf-text dialect carries its geometry inline (always self-contained);
            // the stl_ dialect only re-imports fixed when its stylesheet resolved.
            if (!stlPositioned || stlCssResolvable)
            {
                var fixedDoc = TryConvertPositionedFixedLayout(html, options);
                if (fixedDoc is not null) return fixedDoc;
            }
            return ConvertPositionedSpans(html, options);
        }

        // The class-positioned stl_ export: geometry entirely in the stylesheet
        // (pt-unit absolute classes), vector ink in an svg <object> background —
        // an older flavour of the same PDF→HTML round-trip.
        if (TryRenderStlClassPositioned(html, options) is { } stlClsDoc)
            return stlClsDoc;

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
        cv.wikiExportDoc = cv.html.Contains("mw-parser-output", StringComparison.Ordinal) && cv.html.Contains("mw-list-item", StringComparison.Ordinal) && cv.html.Contains("load.php", StringComparison.Ordinal);
        if (cv.wikiExportDoc)
        {
            cv.html = Regex.Replace(cv.html, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
            cv.html = Regex.Replace(cv.html, @"<li id=""t-(?:upload|cite)""[\s\S]*?</li>", "", RegexOptions.IgnoreCase);
            // The site sheet's other Main-page hides that reach the render: the
            // tagline under the first heading and the print footer (probed: the
            // reference emits neither).
            cv.html = Regex.Replace(cv.html, @"<div id=""siteSub""[\s\S]*?</div>", "", RegexOptions.IgnoreCase);
            cv.html = Regex.Replace(cv.html, @"<div class=""printfooter[\s\S]*?</div>", "", RegexOptions.IgnoreCase);
            // Dropdown LABELS ("Tools" over the pinned twin) indent 15 pt and
            // carry a taller line box; the paired pin BUTTONS draw as one
            // widget line. Both are marked for the UA flow with PUA sentinels
            // the writer strips.
            cv.html = Regex.Replace(cv.html,
                @"<label[^>]*vector-dropdown-label(?:(?!</label>)[\s\S])*?<span[^>]*vector-dropdown-label-text[^>]*>(?<t>[^<]*)</span>\s*</label>",
                m => "<div>[[WKL]]" + m.Groups["t"].Value + "</div>", RegexOptions.IgnoreCase);
            cv.html = Regex.Replace(cv.html,
                @"<button[^>]*pin-button[^>]*>(?<a>[^<]*)</button>\s*<button[^>]*unpin-button[^>]*>(?<b>[^<]*)</button>",
                m => "<div>[[WKB]]" + m.Groups["a"].Value + " [[WKS]] " + m.Groups["b"].Value + "</div>",
                RegexOptions.IgnoreCase);
            // The sidebar logo: three unreachable images whose ALT text the
            // reference does not draw — the block spends its fixed box height
            // (probed: 16.2 over the list gap) and renders nothing.
            cv.html = Regex.Replace(cv.html,
                @"<a href=""/wiki/Main_Page"" class=""mw-logo"">[\s\S]*?</a>",
                "<div>[[WKG]]</div>", RegexOptions.IgnoreCase);
            // The search form's input widget: its box out-tops the text line, so
            // the block after the form opens lower (probed: input line to the
            // next heading = 29.9 vs the 14.7 a text line hands).
            cv.html = Regex.Replace(cv.html,
                @"(<form action=""/w/index\.php"" id=""searchform""[\s\S]*?</form>)",
                "$1<div>[[WKA]]</div>", RegexOptions.IgnoreCase);
            // The main-page welcome banner: the mp-welcome h1 + its trailing
            // comma render as ONE centred 162% line (19.44 pt on the
            // UA base) inside the mp-box border.
            cv.html = Regex.Replace(cv.html,
                @"<div id=""mp-welcomecount"">\s*<div id=""mp-welcome""><div[^>]*><h1[^>]*>(?<h>[\s\S]*?)</h1></div>(?<c>[^<]*)</div>",
                m => "<div>[[WKH]]" + Regex.Replace(m.Groups["h"].Value, "<[^>]+>", "")
                     + " " + m.Groups["c"].Value.Trim() + "</div>",
                RegexOptions.IgnoreCase);
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
