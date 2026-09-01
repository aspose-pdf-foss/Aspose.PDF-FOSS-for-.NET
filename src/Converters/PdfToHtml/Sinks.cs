using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
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
}
