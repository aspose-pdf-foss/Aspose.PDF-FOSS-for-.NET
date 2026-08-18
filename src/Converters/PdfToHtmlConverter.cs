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
        output.Write(Encoding.UTF8.GetBytes(html));
    }

    /// <summary>A file the caller must write next to the HTML, under
    /// <c>&lt;base&gt;_files/</c>. Image-typed sidecars (page graphics and raster
    /// images, <see cref="IsImage"/>) may be redirected by the caller to
    /// <see cref="HtmlSaveOptions.SpecialFolderForAllImages"/> instead.</summary>
    internal sealed class SidecarFile
    {
        public string Name { get; init; } = "";
        public byte[] Content { get; init; } = System.Array.Empty<byte>();
        public bool IsImage { get; init; }
    }

    /// <summary>Diverts raster images drawn during content rendering to sidecar
    /// <c>img_NN.png</c> files (referenced from the HTML) instead of inlining them as
    /// data URIs. Shares one 1-based counter with the page-SVG numbering so every
    /// <c>img_NN</c> name is unique document-wide. With <see cref="SvgImageRefs"/>
    /// (RasterImagesSavingModes.AsExternalPngFilesReferencedViaSvg) the reference is an
    /// <c>&lt;image xlink:href&gt;</c> inside the page SVG rather than an HTML
    /// <c>&lt;img&gt;</c>.</summary>
    private sealed class ExternalImageSink
    {
        private readonly List<SidecarFile> _sidecars;
        private readonly string _imagesUrl;
        private readonly Dictionary<string, (string Href, bool FromCallback)> _byContent = new();
        public int Counter;
        /// <summary>1-based count of page SVGs emitted so far — the <c>id="body_K"</c>
        /// index, which counts EMITTED page SVGs, not PDF page numbers (a page with no
        /// graphics consumes no index).</summary>
        public int SvgBodyCounter;
        public bool SvgImageRefs;
        /// <summary>AsPngImagesEmbeddedIntoSvg: every raster is inlined as a
        /// <c>data:image/png</c> URI inside the page SVG — no sidecar file and no
        /// <c>img_NN</c> number consumed (the page SVGs then number img_01, img_02…).</summary>
        public bool EmbedDataUris;
        /// <summary>The INLINE page-SVG dialect leaves the y flip to each element
        /// (its wrapper does not flip the axis), so an image's placement group uses
        /// top-down coordinates instead of the sidecar wrapper's flipped ones.</summary>
        public bool InlineSvgAxes;
        /// <summary>When set (and carrying a CustomResourceSavingStrategy), each distinct
        /// image is offered to the caller's strategy; the URL it returns replaces the
        /// default sidecar file and is referenced from inside the page SVG.</summary>
        public HtmlSaveOptions? Options;
        public int CurrentPdfPage;
        public int HtmlHostPage = 1;

        public ExternalImageSink(List<SidecarFile> sidecars, string imagesUrl)
        {
            _sidecars = sidecars;
            _imagesUrl = imagesUrl;
        }

        public void Emit(StringBuilder sb, StringBuilder? svgBuf, ImageXObject img, CtmState ctm, double pageHeight)
        {
            var png = img.ToPng();
            if (EmbedDataUris && svgBuf is not null)
            {
                EmitSvgImageRef(svgBuf, img, ctm, pageHeight,
                    "data:image/png;base64," + Convert.ToBase64String(png), InlineSvgAxes);
                return;
            }
            // The same image drawn on many pages (a logo, a letterhead) is written once
            // and referenced repeatedly, keyed by content.
            var key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(png));
            if (!_byContent.TryGetValue(key, out var entry))
            {
                var name = $"img_{++Counter:00}.png";
                var url = DispatchImageResourceCallback(Options, png, name, CurrentPdfPage, HtmlHostPage);
                if (url is null)
                {
                    _sidecars.Add(new SidecarFile { Name = name, Content = png, IsImage = true });
                    entry = (name, false);
                }
                else
                {
                    entry = (url, true);
                }
                _byContent[key] = entry;
            }
            // A strategy-supplied URL is referenced from inside the page SVG (the
            // AsPngImagesEmbeddedIntoSvg shape) rather than as an HTML <img>.
            if ((SvgImageRefs || entry.FromCallback) && svgBuf is not null)
                EmitSvgImageRef(svgBuf, img, ctm, pageHeight, EscapeHrefAmpersands(entry.Href), InlineSvgAxes);
            else
                EmitImage(sb, img, ctm, pageHeight,
                    entry.FromCallback ? EscapeHrefAmpersands(entry.Href) : Ref(_imagesUrl, entry.Href));
        }

        /// <summary>Sequence source for SVG soft-mask ids: shared through the sink so
        /// masks emitted from nested form recursions stay unique per document.</summary>
        public int MaskSeq;

        /// <summary>Write a raster the converter produced itself (a rasterised
        /// luminosity soft-mask group) through the same naming, dedupe and
        /// callback machinery as a source image, and return the URL the page SVG
        /// should reference. A self-contained save gets a data URI instead of a
        /// sidecar (and burns no image number, like every other embedded raster).</summary>
        public string AddRawPng(byte[] png)
        {
            if (EmbedDataUris)
                return "data:image/png;base64," + Convert.ToBase64String(png);
            var key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(png));
            if (!_byContent.TryGetValue(key, out var entry))
            {
                var name = $"img_{++Counter:00}.png";
                var url = DispatchImageResourceCallback(Options, png, name, CurrentPdfPage, HtmlHostPage);
                if (url is null)
                {
                    _sidecars.Add(new SidecarFile { Name = name, Content = png, IsImage = true });
                    entry = (name, false);
                }
                else
                {
                    entry = (url, true);
                }
                _byContent[key] = entry;
            }
            return entry.FromCallback ? EscapeHrefAmpersands(entry.Href) : entry.Href;
        }

        /// <summary>Reference the sidecar PNG from inside the page SVG. The placement
        /// matrix positions the image's pixel box in the SVG's y-flipped PDF-point
        /// space (the negative vertical scale re-flips the bitmap upright); the sidecar
        /// name is relative because the SVG lives in the same folder.</summary>
        private static void EmitSvgImageRef(StringBuilder svgBuf, ImageXObject img,
            CtmState ctm, double pageHeight, string name, bool inlineAxes = false)
        {
            var widthPt = Math.Abs(ctm.A);
            var heightPt = Math.Abs(ctm.D);
            if (widthPt < 0.01) widthPt = img.Width;
            if (heightPt < 0.01) heightPt = img.Height;
            var sx = widthPt / img.Width;
            var sy = heightPt / img.Height;
            // The sidecar wrapper flips the y axis, so the group re-flips the bitmap
            // upright from the box's PDF top. The inline wrapper keeps y top-down:
            // the group scales positively from the box's top-down origin instead.
            var g = inlineAxes
                ? $"matrix({F(sx)} 0 0 {F(sy)} {F(ctm.E)} {F(pageHeight - ctm.F - heightPt)})"
                : $"matrix({F(sx)} 0 0 {F(-sy)} {F(ctm.E)} {F(ctm.F + heightPt)})";
            svgBuf.Append($"<g transform=\"{g}\">")
                .Append("<g transform=\"matrix(1 0 0 1 0 0)\">")
                .Append($"<image x=\"0\" y=\"0\" xlink:href=\"{name}\" width=\"{img.Width}\" height=\"{img.Height}\" />")
                .Append("</g></g>");
        }
    }

    /// <summary>Maps stl_ class numbers to HTML class attributes and CSS selectors under
    /// an optional <see cref="HtmlSaveOptions.CssClassNamesPrefix"/>. The prefix has two
    /// forms: dotted (<c>".gDV__ .stl_"</c>) scopes rules under a wrapper class via a
    /// descendant selector; plain (<c>"my_prefix_"</c>) renames the class stem itself.</summary>
    private readonly struct ClassNamer
    {
        private readonly string _wrapper;   // e.g. "gDV__" / "my_prefix_" / "" (none)
        private readonly string _stem;      // e.g. "stl_" / "my_prefix_"

        public ClassNamer(string? prefix)
        {
            if (string.IsNullOrEmpty(prefix)) { _wrapper = ""; _stem = "stl_"; }
            else if (prefix.Contains(" ."))
            {
                var parts = prefix.Split(' ');
                _wrapper = parts[0].TrimStart('.');
                _stem = parts[1].TrimStart('.');
            }
            else { _wrapper = ""; _stem = prefix; }
        }

        /// <summary>The class stem itself ("stl_" or the plain prefix).</summary>
        public string Stem => _stem;

        public string Cls(int n) => Cls(Pad(n));
        public string Cls(string name) => _wrapper.Length == 0 ? _stem + name : _wrapper + " " + _stem + name;

        /// <summary>The page container's class attribute: the bare stem token
        /// followed by the page-box class (<c>"stl_ stl_02"</c>).</summary>
        public string PageCls() => _wrapper.Length == 0 ? _stem + " " + Cls("02") : Cls("02");
        public string Attr(params int[] ns) => string.Join(" ", System.Array.ConvertAll(ns, Cls));
        public string Sel(int n) => Sel(Pad(n));
        public string Sel(string name) => _wrapper.Length == 0 ? "." + _stem + name : "." + _wrapper + " ." + _stem + name;
        /// <summary>The bare class token (stem + padded number), without wrapper.</summary>
        public string Token(int n) => _stem + Pad(n);
        private static string Pad(int n) => n < 100 ? n.ToString("00") : n.ToString();
    }

    /// <summary>Allocates the document-wide <c>stl_NN</c> classes for text appearance,
    /// line-height and letter-spacing in first-use order (from stl_07), and emits the
    /// matching CSS rules with per-class <c>@font-face</c> and an IE letter-spacing
    /// variant. Exact numbers depend on the letter-spacing values encountered,
    /// but the structure and class scheme are stable.</summary>
    private sealed class StyleRegistry
    {
        private readonly Dictionary<string, int> _fonts = new();
        private readonly Dictionary<string, int> _lineHeights = new();
        private readonly Dictionary<string, int> _letterSpacings = new();
        // Issued letter-spacing classes with their first-seen value, for the
        // near-duplicate tolerance match in LetterSpacing().
        private readonly List<(double Value, int Num)> _letterSpacingValues = new();
        private readonly System.Collections.Generic.HashSet<string> _faceSeen = new(StringComparer.Ordinal);
        // Rules and IE-variants in allocation order so the emitted CSS mirrors first use.
        private readonly List<string> _rules = new();
        private int _next = 7;
        private bool _basePinned;

        /// <summary>Pin the first dynamic class number, before any dynamic class is
        /// issued. Classes are numbered with one document-wide counter, so
        /// where the dynamic range starts depends on how many structural classes the
        /// dialect emits: a page WITH a backdrop wrapper (PNG background / SVG
        /// object) numbers 03/04 for it and 05/06 for the text layer (dynamic from
        /// 07); a page without one numbers the text layer 03/04 and its fonts start
        /// at 05. First caller wins; later pages keep the established base.</summary>
        public void EnsureBase(int first)
        {
            if (_basePinned) return;
            _basePinned = true;
            BackdropLayout = first >= 7;
            if (_fonts.Count == 0 && _lineHeights.Count == 0 && _letterSpacings.Count == 0)
                _next = first;
        }

        /// <summary>True when the pinned layout reserves 03/04 for a backdrop wrapper
        /// (text layer at 05/06); false when the text layer itself takes 03/04.</summary>
        public bool BackdropLayout { get; private set; } = true;

        public int Font(string family, double sizeEm, string color, string? faceUrl,
            string? fallbackFamily = null)
        {
            var key = $"{family}|{sizeEm:F6}|{color}|{fallbackFamily}";
            if (_fonts.TryGetValue(key, out var n)) return n;
            n = _next++;
            _fonts[key] = n;
            var face = faceUrl is not null && _faceSeen.Add(family)
                ? $"@font-face {{\n\tfont-family:\"{family}\";\n\tsrc:url(\"{faceUrl}\") format(\"woff\");\n}}\n"
                : "";
            var familyList = fallbackFamily is null
                ? $"\"{family}\""
                : $"\"{family}\", \"{fallbackFamily}\"";
            _rules.Add($"{face}%SEL{n}% {{\n\tfont-size: {Em(sizeEm)};\n\tfont-family: {familyList};\n\tcolor: {color};\n}}\n");
            return n;
        }

        public int LineHeight(double em)
        {
            var key = em.ToString("F6", CultureInfo.InvariantCulture);
            if (_lineHeights.TryGetValue(key, out var n)) return n;
            n = _next++;
            _lineHeights[key] = n;
            _rules.Add($"%SEL{n}% {{\n\tline-height: {Em(em)};\n}}\n");
            return n;
        }

        public int LetterSpacing(double em)
        {
            var key = em.ToString("F6", CultureInfo.InvariantCulture);
            if (_letterSpacings.TryGetValue(key, out var n)) return n;
            // Per-segment anchor solving jitters one source spacing by a
            // ten-thousandth (0.5005 vs 0.5006, -0.1247 vs -0.125): a value within
            // half a thousandth of an already-issued class reuses that class, so
            // metric noise cannot mint near-duplicate rules. Truly distinct
            // spacings (0 vs -0.0045) sit further apart and keep their own class.
            foreach (var (v, num) in _letterSpacingValues)
                if (Math.Abs(v - em) <= 0.0004) { _letterSpacings[key] = num; return num; }
            n = _next++;
            _letterSpacings[key] = n;
            _letterSpacingValues.Add((em, n));
            // The px variant (approx em*13) drives the IE-only override the tests look for.
            _rules.Add($"%SEL{n}% {{\n\tletter-spacing: {Em(em)};\n}}\n\n%IE{n}% {{\n\tletter-spacing: {Px(em * 13.0)};\n}}\n");
            return n;
        }

        /// <summary>The hover-container class of a page-menu widget (relative
        /// inline-block wrapper). Allocated fresh per widget, before the caption
        /// span's letter-spacing class.</summary>
        public int PopupBox()
        {
            var n = _next++;
            _rules.Add($"%SEL{n}% {{ position: relative; display: inline-block;}}\n");
            return n;
        }

        /// <summary>The widget's drop-up list class (hidden until the box is
        /// hovered). Allocated after the caption span's classes.</summary>
        public int PopupList(int boxNum)
        {
            var n = _next++;
            _rules.Add($"%SEL{n}% {{ display: none; position: absolute; bottom: 0px; " +
                $"min-width: 160px; background-color: #eee; z-index: 10;}}\n" +
                $"%SEL{boxNum}%:hover %SEL{n}% {{ display: block; }}\n");
            return n;
        }

        /// <summary>Letter-spacing class from the exact solved value: em and the
        /// device-px twin, both rounded half-away to 4 decimals. Classes are
        /// shared by equal printed EM values — no tolerance interning on the
        /// solver path, and the px twin keeps the FIRST allocation's value (a
        /// smaller font size reusing the same em keeps the original class).</summary>
        public int LetterSpacingExact(double em, double px)
        {
            var emS = em.ToString("0.####", CultureInfo.InvariantCulture);
            var pxS = px.ToString("0.####", CultureInfo.InvariantCulture);
            var key = "X|" + emS;
            if (_letterSpacings.TryGetValue(key, out var n)) return n;
            n = _next++;
            _letterSpacings[key] = n;
            _rules.Add($"%SEL{n}% {{\n\tletter-spacing: {emS}em;\n}}\n\n%IE{n}% {{\n\tletter-spacing: {pxS}px;\n}}\n");
            return n;
        }

        private readonly Dictionary<string, int> _rotations = new();
        private readonly Dictionary<string, int> _pageRotations = new();

        /// <summary>A rotation class: each transform property on its own line,
        /// vendor prefixes first (-o-, -webkit-, -moz-, then the standard
        /// property).</summary>
        /// <summary>The class that turns a whole page layer over for a page whose
        /// /Rotate is 180. Unlike <see cref="Rotation"/> — which slants a single
        /// run and therefore pins its origin — this one is a zero-sized absolute
        /// box, so the rotation turns about the layer's own anchor point and the
        /// content inside is placed in the turned frame. The class is spelled
        /// exactly this way, and its four transform declarations are what a page
        /// rotation contributes to the stylesheet.</summary>
        public int PageRotation(double deg)
        {
            var key = deg.ToString("0.##", CultureInfo.InvariantCulture);
            if (_pageRotations.TryGetValue(key, out var n)) return n;
            n = _next++;
            _pageRotations[key] = n;
            _rules.Add($"%SEL{n}% {{\n\t-o-transform: rotate({key}deg);\n" +
                $"\t-webkit-transform: rotate({key}deg);\n" +
                $"\t-moz-transform: rotate({key}deg);\n" +
                $"\ttransform: rotate({key}deg);\n" +
                $"\twidth: 0pt;\n\theight: 0pt;\n\tposition: absolute;\n}}\n");
            return n;
        }

        public int Rotation(double deg)
        {
            var key = deg.ToString("0.##", CultureInfo.InvariantCulture);
            if (_rotations.TryGetValue(key, out var n)) return n;
            n = _next++;
            _rotations[key] = n;
            _rules.Add($"%SEL{n}% {{\n\t-o-transform: rotate({key}deg);\n" +
                $"\t-webkit-transform: rotate({key}deg);\n" +
                $"\t-moz-transform: rotate({key}deg);\n" +
                $"\ttransform: rotate({key}deg);\n" +
                $"\ttransform-origin: left bottom;\n}}\n");
            return n;
        }

        /// <summary>First dynamic class number under the pinned layout (07 with a
        /// backdrop wrapper, 05 without).</summary>
        public int DynamicBase => BackdropLayout ? 7 : 5;

        /// <summary>One past the highest allocated class number.</summary>
        public int NextNumber => _next;

        /// <summary>Remap the allocated class numbers (old → new) after the body's
        /// divs have been reordered: every %SELn%/%IEn% token is rewritten and the
        /// rules re-sorted so the stylesheet lists classes in the REORDERED body's
        /// first-use order, mirroring an allocation that had happened in that
        /// order.</summary>
        public void Renumber(Dictionary<int, int> map)
        {
            for (var i = 0; i < _rules.Count; i++)
                _rules[i] = System.Text.RegularExpressions.Regex.Replace(_rules[i],
                    @"%(SEL|IE)(\d+)%",
                    m =>
                    {
                        var n = int.Parse(m.Groups[2].Value);
                        return $"%{m.Groups[1].Value}{(map.TryGetValue(n, out var nn) ? nn : n)}%";
                    });
            int RuleKey(string rule)
            {
                var m = System.Text.RegularExpressions.Regex.Match(rule, @"%SEL(\d+)%");
                return m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue;
            }
            var keyed = new List<(int Key, int Idx, string Rule)>(_rules.Count);
            for (var i = 0; i < _rules.Count; i++) keyed.Add((RuleKey(_rules[i]), i, _rules[i]));
            keyed.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Idx.CompareTo(b.Idx));
            _rules.Clear();
            foreach (var (_, _, rule) in keyed) _rules.Add(rule);
        }

        /// <summary>Expand the accumulated rules, resolving %SELn%/%IEn% placeholders to
        /// prefixed selectors.</summary>
        public string Css(ClassNamer namer)
        {
            var sb = new StringBuilder();
            foreach (var r in _rules)
            {
                var expanded = System.Text.RegularExpressions.Regex.Replace(r, @"%SEL(\d+)%",
                    m => namer.Sel(int.Parse(m.Groups[1].Value)));
                expanded = System.Text.RegularExpressions.Regex.Replace(expanded, @"%IE(\d+)%",
                    m => namer.Sel("ie") + " " + namer.Sel(int.Parse(m.Groups[1].Value)));
                sb.Append(expanded);
            }
            return sb.ToString();
        }

        private static string Em(double v) => v.ToString("0.######", CultureInfo.InvariantCulture) + "em";
        private static string Px(double v) => v.ToString("0.####", CultureInfo.InvariantCulture) + "px";
    }

    /// <summary>
    /// Render the document as ONE self-contained fixed-layout HTML document — the
    /// stl_ scheme with the stylesheet inline in a <c>&lt;STYLE&gt;</c> block. Used
    /// for the PNG-page-background raster mode's single-stream saves: file saves
    /// with <see cref="HtmlSaveOptions.PartsEmbeddingModes.EmbedAllIntoHtml"/> embed
    /// every resource (page rasters, font files) as a <c>data:</c> URI; a save NOT
    /// embedding everything (a stream target has no sidecar folder to write into)
    /// first offers each resource to the caller's
    /// <see cref="HtmlSaveOptions.CustomResourceSavingStrategy"/> and references the
    /// URL it returns, inlining only what no strategy took over.
    /// </summary>
    internal string RenderDocumentEmbedded(Document doc, HtmlSaveOptions options, bool pngBackground)
    {
        int[] pageList;
        if (options.ExplicitListOfSavedPages is { Length: > 0 } explicitPages)
        {
            pageList = explicitPages;
        }
        else
        {
            pageList = new int[doc.PageCount];
            for (var k = 0; k < pageList.Length; k++) pageList[k] = k + 1;
        }

        var namer = new ClassNamer(options.CssClassNamesPrefix);
        var styleReg = new StyleRegistry();
        var sidecars = new List<SidecarFile>();
        var embedAll = options.PartsEmbeddingMode == HtmlSaveOptions.PartsEmbeddingModes.EmbedAllIntoHtml;
        var imageSink = new ExternalImageSink(sidecars, imagesUrl: "")
        {
            Options = options,
            InlineSvgAxes = embedAll,
        };

        var body = new StringBuilder();
        for (var pos = 1; pos <= pageList.Length; pos++)
        {
            imageSink.HtmlHostPage = pos;
            RenderPageExternalDiv(doc, pageList[pos - 1], body, namer, styleReg, imageSink, sidecars,
                imagesUrl: "", pngBackground, htmlPageNumber: pos, options: options,
                dispatchPngBackground: !embedAll, inlineSvg: embedAll,
                // A fully self-contained save renders its background with the text
                // ink SUPPRESSED (the text lives on as the selectable spans; the
                // background carries images/graphics only) and frames
                // it at ImageResolution.
                embedResources: embedAll
                    && options.LettersPositioningMethod
                        == HtmlSaveOptions.LettersPositioningMethods.UseEmUnitsAndCompensationOfRoundingErrorsInCss);
        }

        // Text divs leave the content stream in DRAW order; the emitted document
        // orders each page's lines VISUALLY (ascending top, then left) and numbers
        // the dynamic classes by first use in that order — a page whose header line
        // is painted last still lists it first, with the small class numbers.
        SortAndRenumberStlBody(body, namer, styleReg);

        // Stylesheet: structural prologue + accumulated stl_ classes + @font-face.
        // Fonts dispatch through the resource strategy exactly like a file save; a
        // face nothing claimed is inlined below with the other sidecars.
        var fontMode = options.FontSavingMode;
        var css = new StringBuilder("\n").Append(BuildBaseCss(doc, pageList, namer, styleReg));
        foreach (var f in EmitFontSidecars(doc, pageList, sidecars, fontMode, options))
            css.Append(FontFaceCss(f, fontUrlPrefix: "", fontMode));

        var sb = new StringBuilder();
        sb.AppendLine(DocTypeDeclaration());
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine(TitleElement());
        sb.AppendLine($"<STYLE>{css}</STYLE>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.Append(body);
        sb.AppendLine("</body></html>");

        // Inline every sidecar no strategy claimed as a data: URI where the markup
        // and @font-face rules reference its default (quoted) name.
        var html = sb.ToString();
        foreach (var f in sidecars)
            html = html.Replace("\"" + f.Name + "\"",
                "\"data:" + MimeFor(f.Name) + ";base64," + System.Convert.ToBase64String(f.Content) + "\"");
        return html;
    }

    /// <summary>Reorder each contiguous run of positioned text divs into visual
    /// order — ascending top, then left — and renumber the dynamic classes so
    /// their first-use order follows the REORDERED body (stylesheet rules are
    /// remapped and re-sorted to match). Runs are bounded by any non-text-div
    /// line (page wrappers, backdrops), so divs never cross their page region.</summary>
    private static void SortAndRenumberStlBody(StringBuilder body, ClassNamer namer,
        StyleRegistry styleReg)
    {
        var textDivPrefix = "<div class=\"" + namer.Cls("01");
        var posRx = new System.Text.RegularExpressions.Regex(
            @"style=""left:(-?[0-9.]+)em;top:(-?[0-9.]+)em");
        var lines = body.ToString().Split('\n');
        var sorted = new List<string>(lines.Length);
        var run = new List<(double Top, double Left, int Idx, string Line)>();
        void FlushRun()
        {
            if (run.Count > 1)
                run.Sort((a, b) => a.Top != b.Top ? a.Top.CompareTo(b.Top)
                    : a.Left != b.Left ? a.Left.CompareTo(b.Left)
                    : a.Idx.CompareTo(b.Idx));
            foreach (var (_, _, _, l) in run) sorted.Add(l);
            run.Clear();
        }
        foreach (var line in lines)
        {
            var m = line.StartsWith(textDivPrefix, StringComparison.Ordinal)
                ? posRx.Match(line) : System.Text.RegularExpressions.Match.Empty;
            if (m.Success
                && double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var top)
                && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var left))
            {
                run.Add((top, left, run.Count, line));
                continue;
            }
            FlushRun();
            sorted.Add(line);
        }
        FlushRun();
        var text = string.Join("\n", sorted);

        // Dynamic classes renumber by first appearance in the reordered body.
        // Tokens are only rewritten inside class="..." attributes, so document
        // text that happens to contain a class-like word stays untouched.
        var baseN = styleReg.DynamicBase;
        var tokenRx = new System.Text.RegularExpressions.Regex(
            System.Text.RegularExpressions.Regex.Escape(namer.Stem) + @"(\d{2,})");
        var attrRx = new System.Text.RegularExpressions.Regex(@"class=""[^""]*""");
        var map = new Dictionary<int, int>();
        var next = baseN;
        foreach (System.Text.RegularExpressions.Match attr in attrRx.Matches(text))
            foreach (System.Text.RegularExpressions.Match tok in tokenRx.Matches(attr.Value))
            {
                var n = int.Parse(tok.Groups[1].Value);
                if (n >= baseN && !map.ContainsKey(n)) map[n] = next++;
            }
        // Allocated-but-unreferenced classes keep a stable tail position.
        for (var n = baseN; n < styleReg.NextNumber; n++)
            if (!map.ContainsKey(n)) map[n] = next++;
        var identity = true;
        foreach (var (k, v) in map) if (k != v) { identity = false; break; }
        if (!identity)
        {
            text = attrRx.Replace(text, attr => tokenRx.Replace(attr.Value, tok =>
            {
                var n = int.Parse(tok.Groups[1].Value);
                return n >= baseN && map.TryGetValue(n, out var nn) ? namer.Token(nn) : tok.Value;
            }));
            styleReg.Renumber(map);
        }
        body.Clear();
        body.Append(text);
    }

    private static string MimeFor(string name) =>
        name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
        : name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml"
        : name.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ? "application/font-woff"
        : name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? "font/truetype"
        : name.EndsWith(".eot", StringComparison.OrdinalIgnoreCase) ? "application/vnd.ms-fontobject"
        : name.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ? "text/css"
        : "application/octet-stream";

    /// <summary>
    /// Render the document referencing external resources: each page's vector graphics
    /// go to a sidecar <c>img_NN.svg</c> and the stylesheet to <c>style.css</c>, both
    /// under <paramref name="filesUrl"/> (the <c>&lt;base&gt;_files</c> directory name).
    /// The returned HTML links the stylesheet and embeds each page SVG via
    /// <c>&lt;object&gt;</c>; the sidecar files to write are appended to
    /// <paramref name="sidecars"/>. Text and links stay inline in the HTML.
    /// With <paramref name="pngBackground"/> (RasterImagesSavingModes
    /// .AsEmbeddedPartsOfPngPageBackground) each page's full graphics are flattened
    /// to one sidecar <c>img_NN.png</c> shown behind the selectable text layer, and
    /// no SVGs or individual images are emitted.
    /// </summary>
    internal string RenderDocumentExternal(Document doc, string filesUrl, List<SidecarFile> sidecars,
        int[]? pages = null, string? cssClassNamesPrefix = null, bool pngBackground = false,
        bool svgImageRefs = false, HtmlSaveOptions? options = null, string? imagesUrl = null)
    {
        int[] pageList;
        if (pages is { Length: > 0 })
        {
            pageList = pages;
        }
        else
        {
            pageList = new int[doc.PageCount];
            for (var k = 0; k < pageList.Length; k++) pageList[k] = k + 1;
        }

        var namer = new ClassNamer(cssClassNamesPrefix);
        var styleReg = new StyleRegistry();

        var cssUrl = ResolveCssUrl(options, filesUrl, part: 0);
        // EmbedCssOnly / EmbedAllIntoHtml: the stylesheet is part of the page itself
        // (a <STYLE> block), not a style.css sidecar — "embed into html" means the
        // document must not depend on reaching the sidecar for its OWN appearance.
        // The CSS text is only complete after every page has rendered, so a
        // placeholder is patched in at the end.
        var embedCss = options?.PartsEmbeddingMode
            is HtmlSaveOptions.PartsEmbeddingModes.EmbedCssOnly
            or HtmlSaveOptions.PartsEmbeddingModes.EmbedAllIntoHtml;
        var sb = new StringBuilder();
        sb.AppendLine(DocTypeDeclaration());
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine(TitleElement());
        if (embedCss)
            sb.AppendLine($"<STYLE>{CssPlaceholder(0)}</STYLE>");
        else
            sb.AppendLine($"<link rel=\"stylesheet\" type=\"text/css\" href=\"{cssUrl}\" />");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        imagesUrl ??= filesUrl;
        var imageSink = new ExternalImageSink(sidecars, imagesUrl)
        {
            SvgImageRefs = svgImageRefs,
            EmbedDataUris = options?.RasterImagesSavingMode
                == HtmlSaveOptions.RasterImagesSavingModes.AsPngImagesEmbeddedIntoSvg,
            Options = options,
        };
        // Asking for every part in one file leaves nothing to reference: the page
        // vector graphics go into the HTML as inline SVG markup rather than as a
        // sidecar the embedding pass would have to claim afterwards — and the
        // rasters drawn INSIDE that inline SVG ride along as data: URIs (a
        // sidecar reference from inside the HTML would not be self-contained).
        var inlineSvg = options?.PartsEmbeddingMode
            == HtmlSaveOptions.PartsEmbeddingModes.EmbedAllIntoHtml;
        if (inlineSvg && svgImageRefs) imageSink.EmbedDataUris = true;
        imageSink.InlineSvgAxes = inlineSvg;
        for (var pos = 1; pos <= pageList.Length; pos++)
            RenderPageExternalDiv(doc, pageList[pos - 1], sb, namer, styleReg, imageSink, sidecars, imagesUrl,
                pngBackground, htmlPageNumber: pos, options: options, dispatchPngBackground: false,
                inlineSvg: inlineSvg);

        sb.AppendLine("</body></html>");

        if (embedCss)
        {
            var css = new StringBuilder("\n").Append(BuildBaseCss(doc, pageList, namer, styleReg));
            var fontMode = options?.FontSavingMode ?? HtmlSaveOptions.FontSavingModes.AlwaysSaveAsWOFF;
            foreach (var font in EmitFontSidecars(doc, pageList, sidecars, fontMode, options))
                css.Append(FontFaceCss(font, filesUrl + "/", fontMode));
            return sb.ToString().Replace(CssPlaceholder(0), css.ToString());
        }

        FinalizeExternalCss(doc, pageList, namer, styleReg, sidecars, options, cssUrl);
        return sb.ToString();
    }

    /// <summary>
    /// Render the document as ONE self-contained HTML (PartsEmbeddingModes
    /// .EmbedAllIntoHtml with the PNG-page-background raster mode): the same stl_
    /// fixed-layout markup as the external save, but the stylesheet lives in an
    /// inline <c>&lt;style&gt;</c> block, each page background PNG is a base64 data
    /// URI (rendered at <see cref="HtmlSaveOptions.ImageResolution"/>), and each
    /// font's program is a base64 data URI inside its <c>@font-face</c>.
    /// </summary>
    internal string RenderDocumentEmbedded(Document doc, int[]? pages, HtmlSaveOptions options)
    {
        int[] pageList;
        if (pages is { Length: > 0 })
        {
            pageList = pages;
        }
        else
        {
            pageList = new int[doc.PageCount];
            for (var k = 0; k < pageList.Length; k++) pageList[k] = k + 1;
        }

        var namer = new ClassNamer(options.CssClassNamesPrefix);
        var styleReg = new StyleRegistry();
        var sidecars = new List<SidecarFile>(); // embed mode adds none; required by the shared page renderer
        var imageSink = new ExternalImageSink(sidecars, "") { Options = options };

        var body = new StringBuilder();
        for (var pos = 1; pos <= pageList.Length; pos++)
            RenderPageExternalDiv(doc, pageList[pos - 1], body, namer, styleReg, imageSink,
                sidecars, imagesUrl: "", pngBackground: true, htmlPageNumber: pos,
                options: options, dispatchPngBackground: false, embedResources: true);

        // The stylesheet (structural + accumulated classes + data-URI font faces)
        // is only complete after every page has rendered.
        var css = new StringBuilder(BuildBaseCss(doc, pageList, namer, styleReg));
        if (options.FontSavingMode != HtmlSaveOptions.FontSavingModes.DontSave)
        {
            foreach (var font in CollectEmbeddedFonts(doc, pageList, options))
            {
                var ttf = options.FontSavingMode == HtmlSaveOptions.FontSavingModes.AlwaysSaveAsTTF;
                var bytes = ttf ? font.Ttf : font.Woff;
                if (bytes is not { Length: > 0 }) continue;
                var dataUri = "data:application/octet-stream;base64," + System.Convert.ToBase64String(bytes);
                css.Append($"@font-face {{\n\tfont-family:\"{font.Family}\";\n\tsrc:url(\"{dataUri}\") format(\"{(ttf ? "truetype" : "woff")}\");\n}}\n");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(DocTypeDeclaration());
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine(TitleElement());
        sb.AppendLine("<style type=\"text/css\">");
        sb.AppendLine(css.ToString());
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.Append(body);
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Render the selected pages as SEPARATE per-page HTML documents (SplitIntoPages)
    /// sharing one <c>&lt;stem&gt;_files</c> sidecar folder (style.css, fonts, page
    /// graphics). Page h (1-based) of the returned array corresponds to
    /// <paramref name="pages"/>[h-1]. With <paramref name="bodyOnly"/>
    /// (WriteOnlyBodyContent) a page file carries only the page markup — no doctype /
    /// html / head / body wrapper and no stylesheet link. In
    /// <paramref name="pngBackground"/> mode each page's background PNG is offered to
    /// <see cref="HtmlSaveOptions.CustomResourceSavingStrategy"/> (as an
    /// <see cref="HtmlSaveOptions.HtmlImageSavingInfo"/> carrying the PDF and HTML page
    /// numbers); the URL it returns replaces the default sidecar reference.
    /// </summary>
    internal string[] RenderDocumentExternalSplit(Document doc, string filesUrl, List<SidecarFile> sidecars,
        int[] pages, bool bodyOnly, bool pngBackground, bool svgImageRefs, HtmlSaveOptions? options,
        string? imagesUrl = null)
    {
        var namer = new ClassNamer(options?.CssClassNamesPrefix);
        var styleReg = new StyleRegistry();
        imagesUrl ??= filesUrl;
        var imageSink = new ExternalImageSink(sidecars, imagesUrl)
        {
            SvgImageRefs = svgImageRefs,
            EmbedDataUris = options?.RasterImagesSavingMode
                == HtmlSaveOptions.RasterImagesSavingModes.AsPngImagesEmbeddedIntoSvg,
            Options = options,
        };

        // EmbedCssOnly: each page carries its stylesheet in a <STYLE> block instead
        // of linking a style.css sidecar. The CSS text is only complete after every
        // page has rendered (shared class registry), so a placeholder is patched in.
        var embedCss = !bodyOnly && options?.PartsEmbeddingMode
            == HtmlSaveOptions.PartsEmbeddingModes.EmbedCssOnly;

        var splitCss = options?.SplitCssIntoPages == true;
        var result = new string[pages.Length];
        for (var h = 1; h <= pages.Length; h++)
        {
            var sb = new StringBuilder();
            if (!bodyOnly)
            {
                sb.AppendLine(DocTypeDeclaration());
                sb.AppendLine("<html>");
                sb.AppendLine("<head>");
                sb.AppendLine("<meta charset=\"utf-8\" />");
                sb.AppendLine(TitleElement());
                if (embedCss)
                    sb.AppendLine($"<STYLE>{CssPlaceholder(h)}</STYLE>");
                else
                    sb.AppendLine("<link rel=\"stylesheet\" type=\"text/css\" " +
                        $"href=\"{ResolveCssUrl(options, filesUrl, splitCss ? h : 0)}\" />");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
            }
            imageSink.HtmlHostPage = h;
            RenderPageExternalDiv(doc, pages[h - 1], sb, namer, styleReg, imageSink, sidecars, imagesUrl,
                pngBackground, htmlPageNumber: h, options: options, dispatchPngBackground: true);
            if (!bodyOnly) sb.AppendLine("</body></html>");
            result[h - 1] = sb.ToString();
        }

        if (embedCss)
        {
            var fontMode = options!.FontSavingMode;
            var baseCss = BuildBaseCss(doc, pages, namer, styleReg);
            var fonts = EmitFontSidecars(doc, pages, sidecars, fontMode, options);
            for (var h = 1; h <= pages.Length; h++)
            {
                var css = new StringBuilder("\n").Append(baseCss);
                foreach (var f in PageFonts(doc, fonts, pages[h - 1], splitCss))
                    css.Append(FontFaceCss(f, filesUrl + "/", fontMode));
                result[h - 1] = result[h - 1].Replace(CssPlaceholder(h), css.ToString());
            }
        }
        else if (splitCss)
        {
            // One stylesheet per page (style1.css… or the caller's URL template),
            // each carrying only that page's @font-face rules.
            var fontMode = options!.FontSavingMode;
            var baseCss = BuildBaseCss(doc, pages, namer, styleReg);
            var fonts = EmitFontSidecars(doc, pages, sidecars, fontMode, options);
            for (var h = 1; h <= pages.Length; h++)
            {
                var css = new StringBuilder(baseCss);
                foreach (var f in PageFonts(doc, fonts, pages[h - 1], perPage: true))
                    css.Append(FontFaceCss(f, fontUrlPrefix: "", fontMode));
                EmitCssPart(options, sidecars, ResolveCssUrl(options, filesUrl, h), h, css.ToString());
            }
        }
        else
        {
            FinalizeExternalCss(doc, pages, namer, styleReg, sidecars, options,
                ResolveCssUrl(options, filesUrl, part: 0));
        }
        return result;
    }

    /// <summary>The fonts whose @font-face rules page <paramref name="pdfPage"/> needs:
    /// all of them, or (per-page CSS) only those visibly used on that page.</summary>
    private static List<EmbeddedFont> PageFonts(Document doc, List<EmbeddedFont> fonts,
        int pdfPage, bool perPage)
    {
        if (!perPage) return fonts;
        var usedOnPage = new System.Collections.Generic.HashSet<PdfObject>();
        ScanUsedFontObjectsOnPage(doc, pdfPage, usedOnPage);
        return fonts.FindAll(f => f.Objects.Exists(usedOnPage.Contains));
    }

    /// <summary>Token standing in for page <paramref name="h"/>'s embedded CSS until
    /// the shared stylesheet is finalized.</summary>
    private static string CssPlaceholder(int h) => $"/*__page_css_{h}__*/";

    /// <summary>Render one page's <c>page_N</c> container (background graphics +
    /// stl_view text layer) into <paramref name="sb"/>, appending any page graphics
    /// files to the shared sidecar list.</summary>
    private void RenderPageExternalDiv(Document doc, int i, StringBuilder sb,
        ClassNamer namer, StyleRegistry styleReg, ExternalImageSink imageSink,
        List<SidecarFile> sidecars, string imagesUrl, bool pngBackground,
        int htmlPageNumber, HtmlSaveOptions? options, bool dispatchPngBackground,
        bool embedResources = false, bool inlineSvg = false)
    {
        var page = doc.Pages[i];
        var reader = page.Reader;
        var preferFontCmap = options?.FontEncodingStrategy
            == HtmlSaveOptions.FontEncodingRules.DecreaseToUnicodePriorityLevel;
        // DefaultFontName forces the emitted class family (and, with it, the embedded
        // @font-face) onto the requested face — but only when fonts are actually
        // saved. With FontSavingMode.DontSave nothing is embedded, so the class must
        // keep each source font's own (friendly) family name for the viewer to match;
        // the substitute is then not applied.
        var fontsNotSaved = options?.FontSavingMode == HtmlSaveOptions.FontSavingModes.DontSave;
        var effectiveDefaultFont = fontsNotSaved ? null : options?.DefaultFontName;
        var fonts = ResolveFonts(page.Dict, reader,
            preferFontCmap: preferFontCmap,
            substitutors: _substitutors,
            defaultFontName: effectiveDefaultFont,
            friendlyFamilies: fontsNotSaved);
        var imageXObjects = ResolveImageXObjects(page.Dict, reader);
        var pageResources = reader.ResolveDict(page.Dict.Get("Resources"));
        imageSink.CurrentPdfPage = i;

        // A page whose /Rotate is 180 is presented upside down: the whole text
        // layer is turned over with a zero-sized rotation box and every line is
        // placed in the turned frame, one page box left and one page box up. The
        // quarter turns are left alone — they resize the page box instead, and
        // no rotated text is emitted for them at all.
        var pageTurnedOver = page.Rotate == Rotation.on180;
        var textBuf = new StringBuilder();
        var svgPaths = new StringBuilder();
        var destAnchors = DestAnchorsFor(doc);
        var linkTargets = CollectLinkTargets(page.Dict, reader, doc, destAnchors);
        // Fixed-layout geometry references: x from the MediaBox left edge, page
        // top from LLY + floor(height). UseZOrder gets a fresh per-page counter.
        var mb = page.MediaBox;
        var zCounter = options?.UseZOrder == true ? new ZCounter() : null;
        var content = ConcatContentStreams(page.Dict, reader);
        // The dynamic class numbering must be pinned BEFORE the text render issues
        // its first font class: a page with a backdrop wrapper numbers the text
        // layer 05/06 (dynamic from 07); a backdrop-less page numbers it 03/04
        // (fonts from 05). The wrapper's existence in the SVG-graphics mode is only
        // certain after rendering, so predict it from the content stream's paint
        // operators — over-predicting is harmless (it just keeps the 07 base).
        var pageHasPaint = HasVectorPaintOps(content);
        // The background raster exists to carry what the text layer cannot — images,
        // fills, strokes, shadings. A page that paints nothing else needs no backdrop:
        // the self-contained save would embed a blank white raster (the text is
        // suppressed there), and the sidecar save would re-paint, as pixels, the very
        // text it also emits as selectable spans.
        var emitPngBackground = pngBackground && pageHasPaint;
        var hasBackdrop = emitPngBackground || pageHasPaint;
        styleReg.EnsureBase(hasBackdrop ? 7 : 5);
        RenderContentToHtml(content, fonts, imageXObjects, reader, textBuf,
            page.Height, page.Width,
            saveTransparentTexts: options?.SaveTransparentTexts == true,
            emCompensation: options?.LettersPositioningMethod
                == HtmlSaveOptions.LettersPositioningMethods.UseEmUnitsAndCompensationOfRoundingErrorsInCss,
            textOnly: pngBackground,
            externalSvgPaths: pngBackground ? null : svgPaths,
            imageSink: pngBackground ? null : imageSink,
            styleReg: styleReg, classNamer: namer, linkTargets: linkTargets,
            resources: pageResources, preferFontCmap: preferFontCmap,
            substitutors: _substitutors,
            cssTextDecorations: options?.TrySaveTextUnderliningAndStrikeoutingInCss == true,
            pageLLX: mb.LLX, yTopRef: mb.LLY + Math.Floor(mb.URY - mb.LLY),
            zCounter: zCounter,
            defaultFontName: effectiveDefaultFont, authoredPathShape: inlineSvg,
            ocLayers: options?.ConvertMarkedContentToLayers == true
                ? BuildOcLayerMap(pageResources, reader) : null,
            pageTurnedOver: pageTurnedOver);

        // page_N container -> optional SVG background -> stl_view/stl_05/stl_06 text layer.
        // No inline style: the page box (width/height/margin/border) lives in the
        // structural stl_02 CSS class, and tests match the exact div markup.
        sb.AppendLine($"<div id=\"page_{i - 1}\" class=\"{namer.PageCls()}\">");

        if (emitPngBackground)
        {
            // The page's full graphics flattened to one background PNG. The caller's
            // resource strategy (split saves) may take over writing it and supply the
            // URL; otherwise it becomes a sidecar file with the default name — or,
            // for a fully self-contained save (EmbedAllIntoHtml), a base64 data URI
            // rendered at ImageResolution with the truncated page box as the pixel
            // frame (595.5pt → 595pt → 793px at 96dpi).
            var pngName = $"img_{++imageSink.Counter:00}.png";
            byte[] png;
            Aspose.Pdf.Devices.PngDevice device;
            // The em-compensation dialect's background is IMAGES-ONLY at CSS
            // pixels in the sidecar save too, not just the self-contained one —
            // the sidecar raster is a text-free 793×1123 page image. A
            // text-carrying backdrop under the (substitute-basis) text layer
            // double-strikes every glyph at slightly different metrics.
            var emGridBg = options?.LettersPositioningMethod
                == HtmlSaveOptions.LettersPositioningMethods.UseEmUnitsAndCompensationOfRoundingErrorsInCss;
            if (embedResources || emGridBg)
            {
                // Untouched ImageResolution frames the self-contained background at
                // CSS pixels (96 dpi) — the data-URI page raster comes out
                // 793×1121 for a 595.5×841.9 page.
                var dpi = options?.ImageResolution is > 0 and var res ? (int)res : 96;
                var pw = (int)System.Math.Round(System.Math.Floor(page.Width) * dpi / 72.0);
                var ph = (int)System.Math.Round(System.Math.Floor(page.Height) * dpi / 72.0);
                device = new Aspose.Pdf.Devices.PngDevice(pw, ph, new Aspose.Pdf.Devices.Resolution(dpi));
            }
            else
            {
                device = new Aspose.Pdf.Devices.PngDevice(new Aspose.Pdf.Devices.Resolution(150));
            }
            using (var ms = new System.IO.MemoryStream())
            {
                // The embedded save's background raster carries the page GRAPHICS
                // only — the text lives on as the visible HTML spans, so the
                // data-URI page PNGs have all text ink stripped.
                if (embedResources || emGridBg)
                {
                    try
                    {
                        Aspose.Pdf.Devices.PageRenderFlags.SuppressText = true;
                        device.Process(page, ms);
                    }
                    finally { Aspose.Pdf.Devices.PageRenderFlags.SuppressText = false; }
                }
                else
                {
                    device.Process(page, ms);
                }
                png = ms.ToArray();
            }
            WritePngIntermediate(options?.PngIntermediateFileIfAny, page, htmlPageNumber);
            string url;
            if (embedResources)
            {
                url = "data:image/png;base64," + System.Convert.ToBase64String(png);
            }
            else
            {
                var strategyUrl = dispatchPngBackground
                    ? DispatchImageResourceCallback(options, png, pngName, i, htmlPageNumber)
                    : null;
                if (strategyUrl is null)
                {
                    sidecars.Add(new SidecarFile { Name = pngName, Content = png, IsImage = true });
                    url = Ref(imagesUrl, pngName);
                }
                else
                {
                    url = EscapeHrefAmpersands(strategyUrl);
                }
            }
            sb.AppendLine($"<div class=\"{namer.Cls("03")}\"><img src=\"{url}\" " +
                $"class=\"{namer.Cls("04")}\" style=\"width:100%;height:100%;\" /></div>");
        }
        else if (svgPaths.Length > 0)
        {
            var svgDoc = BuildSvgDocument(svgPaths.ToString(),
                page.Width, page.Height, ++imageSink.SvgBodyCounter, inlineSvg,
                inlineSvg ? namer.Cls("04") : null);
            if (inlineSvg)
            {
                // A fully self-contained save carries the page graphics as INLINE SVG
                // markup: a base64 <object> would hide the vector content from anything
                // reading the HTML, and there is no sidecar to reference. The element
                // takes the positioning class and the explicit page size the <object>
                // carried, so re-importing the markup still lays it out as the page's
                // backdrop rather than as a default-sized inline image.
                sb.AppendLine($"<div class=\"{namer.Cls("03")}\">{svgDoc}</div>");
            }
            else
            {
                var svgName = $"img_{++imageSink.Counter:00}.svg";
                var svgUrl = Ref(imagesUrl, svgName);
                sidecars.Add(new SidecarFile
                {
                    Name = svgName,
                    Content = Encoding.UTF8.GetBytes(svgDoc),
                    IsImage = true,
                });
                sb.AppendLine($"<div class=\"{namer.Cls("03")}\"><object data=\"{svgUrl}\" " +
                    $"type=\"image/svg+xml\" class=\"{namer.Cls("04")}\">" +
                    $"<embed src=\"{svgUrl}\" type=\"image/svg+xml\" /></object></div>");
            }
        }

        // Text layer classes come from the document-wide counter: after a backdrop
        // wrapper (which took 03/04) the layer is 05/06 and dynamic classes start at
        // 07; with no backdrop the layer itself is 03/04 and fonts start at 05
        // (the backdrop-less numbering, pinned before the render).
        var layerCls = hasBackdrop
            ? $"{namer.Cls("05")} {namer.Cls("06")}"
            : $"{namer.Cls("03")} {namer.Cls("04")}";
        sb.AppendLine($"<div class=\"{namer.Cls("view")}\"><div class=\"{layerCls}\">");
        if (pageTurnedOver)
            sb.Append($"<div class=\"{namer.Cls(styleReg.PageRotation(180))}\">");
        sb.Append(ReorderStlLineDivs(textBuf.ToString(), namer.Cls("01")));
        if (pageTurnedOver) sb.Append("</div>");
        // Internal-link destinations into THIS page materialize as positioned,
        // named anchors at the end of the text layer — the "#page_index" hrefs
        // land on them.
        if (destAnchors.PageDests.TryGetValue(i, out var pageDests))
        {
            var yTop = mb.LLY + Math.Floor(mb.URY - mb.LLY);
            for (var di = 0; di < pageDests.Count; di++)
            {
                var (dx, dy) = pageDests[di];
                // The anchor sits a 10pt lead above the destination point so a
                // scrolled-to target line stays fully visible.
                sb.AppendLine($"<a name=\"{i}_{di}\" style=\"position:absolute;" +
                    $"left:{Em4T((dx - mb.LLX) / 12.0)}em;top:{Em4T((yTop - dy - 10.0) / 12.0)}em;\">&nbsp;</a>");
            }
        }
        sb.AppendLine("</div></div>");
        // Links whose rect covered no text still need a click surface: the
        // class-less overlay div goes after the text layer, as the page div's
        // last children.
        // The z-ordered variant never emits overlays — text runs under a link
        // rect already carry inline anchors, so no overlay is needed there.
        if (options?.UseZOrder != true)
            EmitGrlinkOverlays(linkTargets, sb, page.Height, namer);
        sb.AppendLine("</div>");
    }
}
