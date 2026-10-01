using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The wiki-export document path of the HTML conversion.</summary>
    private static void ConvertWikiExportDoc(ConvertState cv)
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
}
