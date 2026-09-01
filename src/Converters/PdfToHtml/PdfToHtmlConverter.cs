using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts PDF pages to HTML markup.
/// Text fragments are positioned with absolute CSS positioning.
/// Supports images (base64 data URIs), link annotations, ToUnicode CMap decoding,
/// and vector path rendering as inline SVG.
/// </summary>
public sealed partial class PdfToHtmlConverter
{
    /// <summary>
    /// Minimum text rise (Ts, text-space units) treated as a genuine
    /// superscript/subscript rather than a hinting-level baseline tweak.
    /// </summary>
    private const double RiseThreshold = 0.25;

    // Shared inline style prefixes (the converter emits no <style> block).
    private const string PageDivStyle = "position:relative;margin:10px auto;border:1px solid #ccc;overflow:hidden;";

    private const string TextSpanStyle = "position:absolute;white-space:pre;";

    // Neutralise the browser's default sup/sub shrink-and-shift: the rise is baked
    // into `top` and the reduced font size is explicit on the span.
    private const string SupSubStyle = "font-size:inherit;vertical-align:baseline;";

    // The colour a run drawn in an invisible text rendering mode is saved in: fully
    // transparent, so the OCR layer stays selectable and searchable without painting
    // over the page raster it describes.
    private const string TransparentTextColor = "rgba(0, 0, 0, 0)";

    // One substitutor per source font (keyed by font object number), shared across
    // all pages this converter instance renders: the minted stand-in characters
    // (U+A880 upward) must stay stable document-wide, like the shared font they
    // decorate. Each Document.Save creates its own converter, so no cross-save
    // or cross-thread state leaks.
    private readonly Dictionary<int, LigatureSubstitutor> _substitutors = new();

    /// <summary>
    /// HTML flavour to declare at the top of the output. Set from
    /// <see cref="HtmlSaveOptions.DocumentType"/> before a save; controls only the
    /// emitted <c>&lt;!DOCTYPE&gt;</c> line.
    /// </summary>
    internal HtmlDocumentType DocumentType { get; set; } = HtmlDocumentType.Html5;

    /// <summary>Text for the emitted <c>&lt;title&gt;</c> element (from
    /// <see cref="HtmlSaveOptions.Title"/>); HTML-escaped on output.</summary>
    internal string? Title { get; set; }

    /// <summary>The <c>&lt;title&gt;</c> line, with the configured title HTML-escaped.</summary>
    private string TitleElement() => $"<title>{EscapeHtml(Title ?? string.Empty)}</title>";

    /// <summary>The DOCTYPE line matching <see cref="DocumentType"/>.</summary>
    internal string DocTypeDeclaration() => DocumentType == HtmlDocumentType.Xhtml
        ? "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">"
        : "<!DOCTYPE html>";

    /// <summary>
    /// Hand every distinct font of the document to the caller's
    /// <see cref="HtmlSaveOptions.CustomResourceSavingStrategy"/>. Each font that has a
    /// TrueType program — an embedded FontFile2 (simple or Type0/CID), or, under
    /// <see cref="HtmlSaveOptions.FontSavingModes.AlwaysSaveAsTTF"/>, a system face
    /// resolved by BaseFont name — is dispatched once (deduped by BaseFont) as a
    /// <see cref="SaveOptions.ResourceSavingInfo"/> with ResourceType=Font, the proposed
    /// file name, and the program bytes. Embedded non-TrueType programs (CFF/Type1)
    /// have no TTF form and are not dispatched.
    /// </summary>
    internal static void DispatchFontResourceCallbacks(Document doc, HtmlSaveOptions options)
    {
        var strategy = options.CustomResourceSavingStrategy;
        if (strategy is null) return;
        var resolveSystem = options.FontSavingMode == HtmlSaveOptions.FontSavingModes.AlwaysSaveAsTTF;
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            var reader = page.Reader;
            if (reader is null) continue;
            var resources = reader.ResolveDict(page.Dict.Get("Resources"));
            var fontDict = resources is not null ? reader.ResolveDict(resources.Get("Font")) : null;
            if (fontDict is null) continue;
            foreach (var key in fontDict.Keys)
            {
                var font = reader.ResolveDict(fontDict.Get(key));
                var baseFont = font?.GetName("BaseFont");
                if (font is null || string.IsNullOrEmpty(baseFont) || !seen.Add(baseFont)) continue;

                var ttf = GetEmbeddedTtf(font, reader);
                if (ttf is null && resolveSystem) ttf = TryResolveSystemTtf(baseFont);
                if (ttf is null) continue;

                var info = new SaveOptions.ResourceSavingInfo
                {
                    ResourceType = SaveOptions.NodeLevelResourceType.Font,
                    SupposedFileName = SanitizeFontFileName(baseFont) + ".ttf",
                    ContentStream = new System.IO.MemoryStream(ttf),
                    ContentStreamData = ttf,
                };
                string returnedPath;
                try { returnedPath = strategy(info); }
                catch { continue; /* a failing caller callback must not abort the save */ }

                // The path the caller hands back is written verbatim into an href/url
                // attribute; quote and newline chars would break out of that context,
                // so a returned path containing them is rejected.
                if (returnedPath != null && returnedPath.IndexOfAny(ForbiddenResourcePathChars) >= 0)
                    throw new System.ArgumentException(
                        "Custom resource saving method returned resource path that contains char(s) forbidden in that context (('\"' or ''' or '\n' or '\r')).");
            }
        }
    }

    /// <summary>Characters a <see cref="HtmlSaveOptions.ResourceSavingStrategy"/> may not
    /// return in a resource path (they would break the surrounding href/url attribute).</summary>
    private static readonly char[] ForbiddenResourcePathChars = { '"', '\'', '\n', '\r' };

    /// <summary>Build a resource reference under <paramref name="baseUrl"/>; an empty
    /// base (resource sits next to the referencing file) yields the bare name.</summary>
    private static string Ref(string baseUrl, string name) =>
        string.IsNullOrEmpty(baseUrl) ? name : baseUrl + "/" + name;

    /// <summary>Escape <c>&amp;</c> for an href/src attribute without double-escaping
    /// ampersands the caller's resource strategy already escaped itself.</summary>
    private static string EscapeHrefAmpersands(string url) =>
        url.Contains('&') ? System.Text.RegularExpressions.Regex.Replace(url, "&(?!amp;)", "&amp;") : url;

    /// <summary>Decoded FontFile2 (TrueType) program of a simple or Type0/CID font, or null.</summary>
    private static byte[]? GetEmbeddedTtf(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var descriptor = reader.ResolveDict(font.Get("FontDescriptor"));
            if (descriptor is null && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da && da.Count > 0)
            {
                var descFont = reader.ResolveDict(da[0]);
                descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
            }
            var fontFile = descriptor is not null ? reader.ResolveStream(descriptor.Get("FontFile2")) : null;
            return fontFile is not null ? reader.DecodeStream(fontFile) : null;
        }
        catch { return null; }
    }

    /// <summary>Decoded FontFile3 program when it is a full OpenType sfnt (Subtype
    /// OpenType — a CFF outline table wrapped in an sfnt), which the WOFF wrapper can
    /// package like any sfnt. Bare CFF (Type1C / CIDFontType0C) has no sfnt wrapper and
    /// is not returned.</summary>
    private static byte[]? GetEmbeddedOpenType(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var descriptor = reader.ResolveDict(font.Get("FontDescriptor"));
            if (descriptor is null && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da && da.Count > 0)
            {
                var descFont = reader.ResolveDict(da[0]);
                descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
            }
            var fontFile = descriptor is not null ? reader.ResolveStream(descriptor.Get("FontFile3")) : null;
            if (fontFile is null) return null;
            var bytes = reader.DecodeStream(fontFile);
            // Only a wrapped sfnt (tag 'OTTO' or a TrueType version) can be WOFF-wrapped.
            if (bytes.Length < 4) return null;
            var tag = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            return tag is 0x4F54544F or 0x00010000 or 0x74727565 ? bytes : null;
        }
        catch { return null; }
    }

    /// <summary>Resolve a non-embedded BaseFont to an installed TrueType program, trying
    /// the common PDF name spellings ("ArialNarrow-Bold" → "Arial Narrow Bold",
    /// "CourierNewPSMT" → "Courier New").</summary>
    private static byte[]? TryResolveSystemTtf(string baseFont)
    {
        var name = baseFont;
        var plus = name.IndexOf('+');
        if (plus >= 0 && plus + 1 < name.Length) name = name[(plus + 1)..];
        foreach (var candidate in FontNameCandidates(name))
        {
            try
            {
                if (Text.FontRepository.GetTtfData(candidate) is { Length: > 0 } ttf) return ttf;
            }
            catch { }
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<string> FontNameCandidates(string name)
    {
        yield return name;
        // Strip PostScript name suffixes ("CourierNewPSMT" → "CourierNew").
        var stripped = System.Text.RegularExpressions.Regex.Replace(name, @"(PSMT|PS|MT)$", "");
        if (stripped != name) yield return stripped;
        // Hyphenated style → spaced ("ArialNarrow-Bold" → "ArialNarrow Bold").
        var spacedStyle = stripped.Replace("-", " ");
        if (spacedStyle != stripped) yield return spacedStyle;
        // Split camel-case words ("ArialNarrow Bold" → "Arial Narrow Bold").
        var camel = System.Text.RegularExpressions.Regex.Replace(spacedStyle, @"(\p{Ll})(\p{Lu})", "$1 $2");
        if (camel != spacedStyle) yield return camel;
    }

    /// <summary>A BaseFont name reduced to filesystem-safe characters for SupposedFileName.</summary>
    private static string SanitizeFontFileName(string baseFont)
    {
        var sb = new StringBuilder(baseFont.Length);
        foreach (var ch in baseFont)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '+' ? ch : '_');
        return sb.ToString();
    }

    /// <summary>
    /// Convert all pages to a single HTML document.
    /// </summary>
    public string SaveAsHtml(Document doc)
    {
        // All layout styling is emitted INLINE on each element (no shared <style>
        // block): the HTML carries no bare stylesheet text, so tag-stripping
        // consumers (text diff/search over the output) see only the document's
        // real text, not converter CSS.
        var sb = new StringBuilder();
        sb.AppendLine(DocTypeDeclaration());
        sb.AppendLine("<html><head><meta charset=\"utf-8\" />");
        sb.AppendLine(TitleElement());
        sb.AppendLine("</head><body>");

        for (var i = 1; i <= doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            sb.Append(RenderPage(page, i));
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Convert each page to a separate HTML fragment and return as an array.
    /// </summary>
    public string[] SaveAllPagesAsHtml(Document doc)
    {
        var results = new string[doc.PageCount];
        for (var i = 1; i <= doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            results[i - 1] = RenderPage(page, i);
        }
        return results;
    }

    /// <summary>
    /// Convert a single page to an HTML fragment (a &lt;div&gt; with positioned text).
    /// </summary>
    public string SavePageAsHtml(Document doc, int pageNumber)
    {
        var page = doc.Pages.At(pageNumber);
        return RenderPage(page, pageNumber);
    }

    /// <summary>
    /// Render one page as a standalone HTML file body. When <paramref name="bodyOnly"/>
    /// is true (HtmlMarkupGenerationMode.WriteOnlyBodyContent) only the page
    /// <c>&lt;div&gt;</c> is returned; otherwise it is wrapped in a full
    /// document envelope with the configured doctype. Used by the split-into-pages
    /// file writer.
    /// </summary>
    public string RenderPageAsDocument(Document doc, int pageNumber, bool bodyOnly)
    {
        var page = doc.Pages.At(pageNumber);
        var div = RenderPage(page, pageNumber);
        if (bodyOnly) return div;
        var sb = new StringBuilder();
        sb.AppendLine(DocTypeDeclaration());
        sb.AppendLine("<html><head><meta charset=\"utf-8\" /></head><body>");
        sb.Append(div);
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Convert all pages to HTML and write to a stream.
    /// </summary>
    public void SaveAsHtml(Document doc, Stream output)
    {
        var html = SaveAsHtml(doc);
        output.Write(Encoding.UTF8.GetBytes(HtmlTextFormat.Crlfify(html)));
    }

}
