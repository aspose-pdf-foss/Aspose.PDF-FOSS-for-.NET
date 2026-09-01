using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>Write one result page's full raster render to
    /// <see cref="HtmlSaveOptions.PngIntermediateFileIfAny"/>, suffixing the file stem
    /// with <c>_p&lt;seq&gt;</c> (1-based output page order, no zero-padding). The
    /// intermediate is always a fixed-96dpi render with each pixel axis computed as
    /// trunc(trunc(points) · 96/72) — the page size in points truncates to an integer
    /// BEFORE the dpi scale (475.2pt → 633px, not 634) — and is written whenever the
    /// option is set, independent of what raster the HTML itself embeds.</summary>
    internal static void WritePngIntermediate(string? pathTemplate, Page page, int seq)
    {
        if (string.IsNullOrEmpty(pathTemplate)) return;
        var dir = System.IO.Path.GetDirectoryName(pathTemplate) ?? "";
        var stem = System.IO.Path.GetFileNameWithoutExtension(pathTemplate);
        var ext = System.IO.Path.GetExtension(pathTemplate);
        var w = (int)page.Width * 96 / 72;
        var h = (int)page.Height * 96 / 72;
        var device = new Aspose.Pdf.Devices.PngDevice(w, h, new Aspose.Pdf.Devices.Resolution(96));
        using var ms = new System.IO.MemoryStream();
        device.Process(page, ms);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{stem}_p{seq}{ext}"), ms.ToArray());
    }

    /// <summary>Offer a page-background PNG to the caller's
    /// <see cref="HtmlSaveOptions.CustomResourceSavingStrategy"/>. Returns the URL the
    /// strategy supplied, or null when there is no strategy / it cancelled (the caller
    /// then writes the default sidecar file).</summary>
    private static string? DispatchImageResourceCallback(HtmlSaveOptions? options,
        byte[] png, string supposedName, int pdfPageNumber, int htmlPageNumber)
    {
        var strategy = options?.CustomResourceSavingStrategy;
        if (strategy is null) return null;
        var info = new HtmlSaveOptions.HtmlImageSavingInfo
        {
            ResourceType = SaveOptions.NodeLevelResourceType.Image,
            SupposedFileName = supposedName,
            ContentStream = new System.IO.MemoryStream(png),
            ContentStreamData = png,
            PdfHostPageNumber = pdfPageNumber,
            HtmlHostPageNumber = htmlPageNumber,
        };
        string? url;
        try { url = strategy(info); }
        catch { return null; /* a failing caller callback must not abort the save */ }
        if (info.CustomProcessingCancelled || string.IsNullOrEmpty(url)) return null;
        if (url.IndexOfAny(ForbiddenResourcePathChars) >= 0)
            throw new System.ArgumentException(
                "Custom resource saving method returned resource path that contains char(s) forbidden in that context (('\"' or ''' or '\n' or '\r')).");
        return url;
    }

    /// <summary>Emit the shared stylesheet (structural prologue + accumulated stl_
    /// classes + @font-face per emitted font) and the font sidecars for the pages
    /// actually saved.</summary>
    private void FinalizeExternalCss(Document doc, int[] pageList, ClassNamer namer,
        StyleRegistry styleReg, List<SidecarFile> sidecars, HtmlSaveOptions? options = null,
        string? cssUrl = null)
    {
        var css = new StringBuilder(BuildBaseCss(doc, pageList, namer, styleReg));
        var fontMode = options?.FontSavingMode ?? HtmlSaveOptions.FontSavingModes.AlwaysSaveAsWOFF;
        foreach (var font in EmitFontSidecars(doc, pageList, sidecars, fontMode, options))
            css.Append(FontFaceCss(font, fontUrlPrefix: "", fontMode));
        EmitCssPart(options, sidecars, cssUrl ?? "style.css", part: 0, css.ToString());
    }

    /// <summary>The URL written into a stylesheet <c>href</c>: the caller's
    /// <see cref="HtmlSaveOptions.CustomStrategyOfCssUrlCreation"/> template formatted
    /// with the page number (<paramref name="part"/> 0 = the single whole-document
    /// stylesheet, formatted with ""), else the default
    /// <c>&lt;files&gt;/style[N].css</c>.</summary>
    internal static string ResolveCssUrl(HtmlSaveOptions? options, string filesUrl, int part)
    {
        if (options?.CustomStrategyOfCssUrlCreation is { } urlStrategy)
        {
            var req = new HtmlSaveOptions.CssUrlRequestInfo();
            string? template = null;
            try { template = urlStrategy(req); }
            catch { /* a throwing caller strategy falls back to the default URL */ }
            if (!req.CustomProcessingCancelled && !string.IsNullOrEmpty(template))
                return string.Format(template, part == 0 ? "" : part.ToString(CultureInfo.InvariantCulture));
        }
        return part == 0 ? $"{filesUrl}/style.css" : $"{filesUrl}/style{part}.css";
    }

    /// <summary>Write one stylesheet part: handed to the caller's
    /// <see cref="HtmlSaveOptions.CustomCssSavingStrategy"/> (which writes the file
    /// itself) when set, else appended as a <c>style[N].css</c> sidecar.</summary>
    private static void EmitCssPart(HtmlSaveOptions? options, List<SidecarFile> sidecars,
        string url, int part, string css)
    {
        var bytes = Encoding.UTF8.GetBytes(HtmlTextFormat.Crlfify(css));
        if (options?.CustomCssSavingStrategy is { } saver)
        {
            using var ms = new System.IO.MemoryStream(bytes);
            saver(new HtmlSaveOptions.CssSavingInfo
                { ContentStream = ms, CssNumber = part == 0 ? 1 : part, SupposedURL = url });
            return;
        }
        sidecars.Add(new SidecarFile
            { Name = part == 0 ? "style.css" : $"style{part}.css", Content = bytes });
    }

    /// <summary>The structural prologue + the document's accumulated stl_ appearance
    /// classes (everything except the @font-face rules).</summary>
    private static string BuildBaseCss(Document doc, int[] pageList, ClassNamer namer, StyleRegistry styleReg)
    {
        // The page box truncates the point size before the em conversion (595.5pt →
        // 595pt → 49.58333em) and prints at single precision (G7).
        var firstPage = doc.Pages[pageList[0]];
        return BuildStructuralCss(namer, System.Math.Floor(firstPage.Width) / 12.0,
            System.Math.Floor(firstPage.Height) / 12.0, styleReg.BackdropLayout) + styleReg.Css(namer);
    }

    /// <summary>The fixed structural stylesheet prologue (positioned text, page box, view
    /// scaling, sup/sub, IE hooks), with class selectors run through <paramref name="namer"/>
    /// so a CssClassNamesPrefix scopes them like the dynamic rules.</summary>
    private static string BuildStructuralCss(ClassNamer namer, double pageWidthEm, double pageHeightEm,
        bool backdropLayout)
    {
        string Em(double v) => ((float)v).ToString("G7", CultureInfo.InvariantCulture) + "em";
        var bare = "." + namer.Stem;
        var view = namer.Sel("view");
        var ie = namer.Sel("ie");
        var prologue =
            $"{bare} sup {{ vertical-align: baseline; position: relative; top: -0.4em; }}\n" +
            $"{bare} sub {{ vertical-align: baseline; position: relative; top: 0.4em; }}\n" +
            $"{bare} a:link {{text-decoration:none;}}\n" +
            $"{bare} a:visited {{text-decoration:none;}}\n" +
            $"@media screen and (min-device-pixel-ratio:0), (-webkit-min-device-pixel-ratio:0), (min--moz-device-pixel-ratio: 0) {{{view}{{ font-size:10em; transform:scale(0.1); -moz-transform:scale(0.1); -webkit-transform:scale(0.1); -moz-transform-origin:top left; -webkit-transform-origin:top left; }} }}\n" +
            $"{namer.Sel("layer")} {{ }}{ie} {{ font-size: 1pt; }}\n" +
            $"{ie} body {{ font-size: 12em; }}\n" +
            $"@media print{{{view} {{font-size:1em; transform:scale(1);}}}}\n" +
            $"{namer.Sel("grlink")} {{ position:relative;width:100%;height:100%;z-index:1000000; }}\n" +
            $"{namer.Sel("01")} {{\n\tposition: absolute;\n\twhite-space: nowrap;\n}}\n" +
            $"{namer.Sel("02")} {{\n\tfont-size: 1em;\n\tline-height: 0.0em;\n\twidth: {Em(pageWidthEm)};\n\theight: {Em(pageHeightEm)};\n\tborder-style: none;\n\tdisplay: block;\n\tmargin: 0em;\n}}\n" +
            // The page box is followed by an Edge-only overflow clamp
            // (present in both stl_ dialects' emitted stylesheets).
            $"\n@supports(-ms-ime-align:auto) {{ {namer.Sel("02")} {{overflow: hidden;}}}}\n";
        // With a backdrop wrapper, 03/04 are its rules and the text layer sits at
        // 05/06; a backdrop-less document's text layer takes 03/04 directly.
        return backdropLayout
            ? prologue +
              $"{namer.Sel("03")} {{\n\tposition: relative;\n}}\n" +
              $"{namer.Sel("04")} {{\n\tposition: absolute;\n\tleft: 0em;\n\ttop: 0em;\n}}\n" +
              $"{namer.Sel("05")} {{\n\tposition: relative;\n\twidth: {Em(pageWidthEm)};\n}}\n" +
              $"{namer.Sel("06")} {{\n\theight: {Em(pageHeightEm / 10.0)};\n}}\n"
            : prologue +
              $"{namer.Sel("03")} {{\n\tposition: relative;\n\twidth: {Em(pageWidthEm)};\n}}\n" +
              $"{namer.Sel("04")} {{\n\theight: {Em(pageHeightEm / 10.0)};\n}}\n";
    }

    /// <summary>Wrap a page's collected SVG path elements in a standalone SVG document
    /// with the viewer-independent header (px viewBox = points × 4/3). The inner matrix
    /// scales points to px and — for a sidecar document — also flips PDF's bottom-left
    /// origin to SVG's top-left. An INLINE document leaves the flip to each path's own
    /// transform, so the composition is the same either way.</summary>
    private static string BuildSvgDocument(string paths, double widthPt, double heightPt,
        int pageNumber, bool inlineSvg = false, string? inlineClass = null)
    {
        var pw = (int)System.Math.Round(widthPt * 4.0 / 3.0);
        var ph = (int)System.Math.Round(heightPt * 4.0 / 3.0);
        var g = inlineSvg
            ? "matrix(1.3333333 0 0 1.3333333 0 0)"
            : $"matrix(1.3333333 0 0 -1.3333333 0 {ph})";
        // An inline element states the size and positioning class the sidecar's
        // <object> wrapper used to carry; a standalone document needs neither.
        var inlineAttrs = inlineSvg
            ? $" class=\"{inlineClass}\" width=\"{pw}\" height=\"{ph}\""
            : "";
        return "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
            $"version=\"1.1\" id=\"body_{pageNumber}\"{inlineAttrs} preserveAspectRatio=\"xMinYMin meet\" viewBox=\"0 0 {pw} {ph}\">" +
            $"<g transform=\"{g}\">{paths}</g></svg>";
    }

    /// <summary>Collect the document's fonts as WOFF sidecars: one file per font
    /// OBJECT that shows at least one visible (non-whitespace) glyph in the content
    /// streams — a font merely declared in a resource dictionary, or used only for
    /// whitespace, gets no file; two distinct objects sharing a BaseFont each get
    /// their own. Embedded TrueType/OpenType programs are wrapped as-is, bare CFF
    /// (Type1C) is converted to a TrueType sfnt, and non-embedded BaseFonts resolve
    /// to a system face (deduped by name).</summary>
    /// <summary>Map a page's <c>/Resources/Properties</c> marked-content names
    /// (<c>/Xi0</c>, …) to the optional-content group they select, for
    /// <see cref="HtmlSaveOptions.ConvertMarkedContentToLayers"/>. Each entry carries the
    /// group's own <c>/Name</c> and, when the group is listed under a titled
    /// <c>/OCProperties/D/Order</c> entry (an array whose first element is the title
    /// string), that title — the layer panel shows the group inside it, and the HTML
    /// mirrors that with a nested box. Groups hidden by <c>/D/OFF</c> are included: the
    /// markup describes the document's layers, not the default view.</summary>
    private static Dictionary<string, (string Name, string? GroupTitle)>? BuildOcLayerMap(
        PdfDictionary? resources, PdfReader reader)
    {
        if (reader.ResolveDict(resources?.Get("Properties")) is not { } props) return null;

        // /D/Order titles: [ … (Title) ocg ocg … ] — the string heads the array it opens.
        var titleOf = new Dictionary<PdfObject, string>();
        if (reader.ResolveDict(reader.Catalog?.Get("OCProperties")) is { } ocProps
            && reader.ResolveDict(ocProps.Get("D")) is { } dConfig
            && reader.Resolve(dConfig.Get("Order")) is PdfArray order)
        {
            void Walk(PdfArray arr, string? inherited)
            {
                var title = arr.Count > 0 && reader.Resolve(arr[0]) is PdfString s
                    ? s.ToText() : inherited;
                foreach (var item in arr)
                {
                    if (reader.Resolve(item) is PdfArray nested) Walk(nested, title);
                    else if (reader.ResolveDict(item) is { } ocg && title is not null)
                        titleOf[ocg] = title;
                }
            }
            Walk(order, null);
        }

        Dictionary<string, (string, string?)>? map = null;
        foreach (var key in props.Keys)
        {
            var target = reader.ResolveDict(props.Get(key));
            if (target is null) continue;
            // /OCMD selects one or more groups; the first names the region.
            if (target.GetName("Type") == "OCMD")
            {
                var ocgs = reader.Resolve(target.Get("OCGs"));
                target = ocgs is PdfArray a && a.Count > 0
                    ? reader.ResolveDict(a[0])
                    : reader.ResolveDict(target.Get("OCGs"));
                if (target is null) continue;
            }
            if (target.Get("Name") is not { } nameObj
                || reader.Resolve(nameObj) is not PdfString nameStr) continue;
            (map ??= new())[key] = (nameStr.ToText(),
                titleOf.TryGetValue(target, out var t) ? t : null);
        }
        return map;
    }

    /// <summary>Decoded FontFile (Adobe Type 1) program plus the /Length1 and
    /// /Length2 split its parser needs, or null when the font carries none.</summary>
    private static (byte[] Data, int Length1, int Length2)? GetEmbeddedType1(
        PdfDictionary font, PdfReader reader)
    {
        try
        {
            var descriptor = reader.ResolveDict(font.Get("FontDescriptor"));
            if (descriptor is null && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da && da.Count > 0)
            {
                var descFont = reader.ResolveDict(da[0]);
                descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
            }
            var fontFile = descriptor is not null ? reader.ResolveStream(descriptor.Get("FontFile")) : null;
            if (fontFile is null) return null;
            var bytes = reader.DecodeStream(fontFile);
            if (bytes.Length < 16) return null;
            var len1 = (int)DescriptorNumber(reader.Resolve(fontFile.Dict?.Get("Length1")));
            var len2 = (int)DescriptorNumber(reader.Resolve(fontFile.Dict?.Get("Length2")));
            return (bytes, len1, len2);
        }
        catch { return null; }
    }

    private string RenderPage(Page page, int pageNumber)
    {
        var mb = page.MediaBox;
        var width = page.Width;
        var height = page.Height;

        var sb = new StringBuilder();
        sb.AppendLine($"<div class=\"pdf-page\" data-page=\"{pageNumber}\" " +
            $"style=\"{PageDivStyle}width:{F(width)}pt;height:{F(height)}pt;\">");

        var reader = page.Reader;
        var fonts = ResolveFonts(page.Dict, reader, substitutors: _substitutors);
        var imageXObjects = ResolveImageXObjects(page.Dict, reader);

        RenderContentToHtml(ConcatContentStreams(page.Dict, reader), fonts, imageXObjects, reader, sb, height, width,
            // The plain pdf-page preview dialect carries no save options of its own and
            // keeps its long-standing shape: every shown run reaches the markup.
            saveTransparentTexts: true,
            substitutors: _substitutors,
            resources: reader.ResolveDict(page.Dict.Get("Resources")));

        // Render link annotations
        RenderLinkAnnotations(page.Dict, reader, sb, height);

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    // ── Infrastructure ──────────────────────────────────────────────────

    /// <summary>Naive CMYK→RGB (the same additive mapping the render devices use
    /// for k/K without an ICC profile).</summary>
    private static (double, double, double) CmykToRgb(double c, double m, double y, double k) =>
        ((1 - Math.Clamp(c, 0, 1)) * (1 - Math.Clamp(k, 0, 1)),
         (1 - Math.Clamp(m, 0, 1)) * (1 - Math.Clamp(k, 0, 1)),
         (1 - Math.Clamp(y, 0, 1)) * (1 - Math.Clamp(k, 0, 1)));

    /// <summary>
    /// Registry of rotated-text CSS classes for one HTML document. Each distinct
    /// rotation angle (CSS degrees, rounded to 0.01°) gets one class whose rule is
    /// emitted into the document's &lt;style&gt; block — rotation does not ride on the
    /// span's inline style; the vendor-prefixed
    /// transform lines belong in the stylesheet.
    /// </summary>
    /// <summary>One text line held open while the content stream draws elsewhere.
    /// A producer may emit a single visual line in FRAGMENTS scattered through the
    /// stream and interleaved with its other lines (one observed page draws three
    /// baselines across 53 shows, changing baseline 18 times). Closing the line at
    /// every jump would give one positioned div per fragment; instead each line is
    /// parked by its baseline and resumed when the producer returns to it, so the
    /// solver still sees whole lines. Carries exactly the grouping state
    /// <see cref="RenderContentToHtml"/> keeps for the line it is building.</summary>
    private sealed class StlLinePark
    {
        public List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)> Segs = new();
        public List<StlLineGlyph>? Glyphs;
        public List<StlRunStyle>? Styles;
        public bool Ok = true;
        public int StyleIdx = -1;
        public bool Pinned = true;
        public double EndX, PenX, TextPenX;
        public double X, Y, FontSize = 12, Rise, Angle, RawRise;
        public bool IsType3;
        public string Family = "sans-serif", CssFamily = "sans-serif",
            Weight = "normal", Style = "normal", DeclStyle = "normal";
        public bool FauxBold;
        public double R, G, B;
        public bool Transparent;
        public double Ascent = 1.0, LineHeight;
        public int Z, McSeq;
        public double TjNum;
        public int Chars;
        /// <summary>Text of the line's last merged show — an overstrike re-strokes
        /// its tail, so the drop test is a suffix match against this.</summary>
        public string LastShowText = "";
        /// <summary>Closed lines keep their place in the first-use emission order
        /// but can no longer be resumed — the column split severs a line exactly
        /// as it always did, without letting the severed half jump the queue.</summary>
        public bool Closed;
    }

    private sealed class RotationRegistry
    {
        private readonly Dictionary<double, string> _classes = new();

        /// <summary>(class name, CSS degrees) in first-use order.</summary>
        public List<(string Cls, double Deg)> Rules { get; } = new();

        public string Class(double cssDeg)
        {
            var key = Math.Round(cssDeg, 2);
            if (_classes.TryGetValue(key, out var cls)) return cls;
            cls = "pdf-rot" + (_classes.Count + 1);
            _classes[key] = cls;
            Rules.Add((cls, key));
            return cls;
        }

        /// <summary>The stylesheet rules: one class per angle, each transform
        /// property on its own line, vendor prefixes first (-o-, -webkit-, -moz-,
        /// then the standard property).</summary>
        public string Css()
        {
            var sb = new StringBuilder();
            foreach (var (cls, deg) in Rules)
            {
                sb.Append('.').Append(cls).Append(" {\n");
                sb.Append("-o-transform: rotate(").Append(F(deg)).Append("deg);\n");
                sb.Append("-webkit-transform: rotate(").Append(F(deg)).Append("deg);\n");
                sb.Append("-moz-transform: rotate(").Append(F(deg)).Append("deg);\n");
                sb.Append("transform: rotate(").Append(F(deg)).Append("deg);\n");
                sb.Append("transform-origin: left bottom;\n");
                sb.Append("}\n");
            }
            return sb.ToString();
        }
    }

    [System.ThreadStatic] private static Dictionary<string, bool>? _sysResolvable;

    [ThreadStatic]
    private static Dictionary<string, Text.GlyphOutlineParser?>? _substituteParsers;

    private static Dictionary<string, HtmlFontRecord> ResolveFonts(PdfDictionary pageDict, PdfReader reader,
        bool preferFontCmap = false, Dictionary<int, LigatureSubstitutor>? substitutors = null,
        string? defaultFontName = null, bool friendlyFamilies = false)
        => ResolveFontsFromResources(reader.ResolveDict(pageDict.Get("Resources")), reader,
            preferFontCmap, substitutors, defaultFontName, friendlyFamilies);

    private static Dictionary<string, ImageXObject> ResolveImageXObjects(
        PdfDictionary pageDict, PdfReader reader)
    {
        var result = new Dictionary<string, ImageXObject>(StringComparer.Ordinal);
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null) return result;
        var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is null) return result;

        foreach (var key in xobjectDict.Keys)
        {
            var obj = reader.ResolveStream(xobjectDict.Get(key));
            if (obj is not null && obj.Dict.GetName("Subtype") == "Image")
            {
                result[key] = new ImageXObject(key, obj, reader);
            }
        }

        return result;
    }

    private static (string family, string weight, string style) MapFont(string baseFont)
    {
        var name = baseFont;
        // Strip subset prefix (e.g., "ABCDEF+Helvetica")
        if (name.Length > 7 && name[6] == '+')
            name = name[7..];

        var family = name switch
        {
            var n when n.Contains("Helvetica") => "Helvetica, Arial, sans-serif",
            var n when n.Contains("Times") => "'Times New Roman', Times, serif",
            var n when n.Contains("Courier") => "'Courier New', Courier, monospace",
            var n when n.Contains("Symbol") => "Symbol, serif",
            var n when n.Contains("ZapfDingbats") => "ZapfDingbats, serif",
            _ => "sans-serif",
        };

        var weight = name switch
        {
            var n when n.Contains("Bold") => "bold",
            _ => "normal",
        };

        var style = name switch
        {
            var n when n.Contains("Italic") || n.Contains("Oblique") => "italic",
            _ => "normal",
        };

        return (family, weight, style);
    }

    /// <summary>Text-decoration for a run starting at (x, baselineY), both in device
    /// space (y-up): a same-colour hairline just below (or at) the baseline reads as
    /// an underline, one through the x-height as a line-through. The rule must
    /// horizontally cover the run's start.</summary>
    private static string? FindDecoration(
        List<(double Y, double X0, double X1, double Thick, double R, double G, double B)>? rules,
        double x, double baselineY, double fontSize, double r, double g, double b)
    {
        if (rules is null || rules.Count == 0) return null;
        bool under = false, through = false;
        foreach (var rule in rules)
        {
            if (rule.X0 > x + 0.6 || rule.X1 < x + 0.6) continue;
            if (Math.Abs(rule.R - r) > 0.02 || Math.Abs(rule.G - g) > 0.02
                || Math.Abs(rule.B - b) > 0.02) continue;
            var below = baselineY - rule.Y; // positive: rule sits below the baseline
            if (below >= -0.06 * fontSize && below <= 0.3 * fontSize) under = true;
            else if (below < -0.1 * fontSize && below >= -0.55 * fontSize) through = true;
        }
        return (under, through) switch
        {
            (true, true) => "underline line-through",
            (true, false) => "underline",
            (false, true) => "line-through",
            _ => null,
        };
    }

    /// <summary>A page's /Contents parts joined into the single logical stream the
    /// spec defines: graphics state (the CTM in particular — a page-wide scale/flip
    /// `cm` routinely sits in part 1 of many) carries across part boundaries, so the
    /// parts must be parsed as one stream, separated by whitespace.</summary>
    private static byte[] ConcatContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var parts = GetContentStreams(pageDict, reader);
        if (parts.Count == 1) return parts[0];
        var total = 0;
        foreach (var p in parts) total += p.Length + 1;
        var joined = new byte[total];
        var at = 0;
        foreach (var p in parts)
        {
            System.Array.Copy(p, 0, joined, at, p.Length);
            at += p.Length;
            joined[at++] = (byte)'\n';
        }
        return joined;
    }

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private static double NumFromObj(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    /// <summary>Turn a link annotation's /A action dictionary into an href string, or
    /// null if it carries no navigable target. Handles URI (web/absolute), JavaScript
    /// (<c>javascript:</c> scheme), and Launch (open a file) actions.</summary>
    private static string? ResolveLinkHref(PdfDictionary? actionDict, PdfReader reader)
    {
        if (actionDict is null) return null;
        switch (actionDict.GetName("S"))
        {
            case "URI":
                return actionDict.Get("URI") switch
                {
                    PdfString s => Encoding.Latin1.GetString(s.Value),
                    PdfName n => n.Value,
                    _ => null,
                };
            case "JavaScript":
                var js = ActionTextValue(actionDict.Get("JS"), reader);
                return js is null ? null : "javascript:" + TranslateAcrobatJs(js);
            case "Launch":
                // /F may be a bare path string or a file-specification dict.
                var f = actionDict.Get("F");
                string? path = f is PdfString fs
                    ? Encoding.Latin1.GetString(fs.Value)
                    : reader.ResolveDict(f)?.Get("F") is PdfString fes
                        ? Encoding.Latin1.GetString(fes.Value)
                        : null;
                return path?.Replace('\\', '/');
            default:
                return null;
        }
    }

    /// <summary>Map the handful of Acrobat viewer scripts that push-buttons commonly
    /// carry to their browser equivalents (print / close / full-screen toggle); any
    /// other script is passed through unchanged.</summary>
    private static string TranslateAcrobatJs(string js)
    {
        var launch = System.Text.RegularExpressions.Regex.Match(js, @"app\.launchURL\(\s*""([^""]*)""");
        if (launch.Success) return $"window.open('{launch.Groups[1].Value}')";
        if (js.Contains("getPrintParams") || js.Contains(".print(")) return "window.print()";
        if (js.Contains("closeDoc")) return "window.close()";
        if (js.Contains("isFullScreen"))
            return "if (window.document.fullscreenElement || window.document.webkitFullscreenElement) " +
                "{ window.document.exitFullscreen ? window.document.exitFullscreen() : " +
                "window.document.webkitExitFullscreen(); } else " +
                "{ window.document.documentElement.requestFullscreen ? " +
                "window.document.documentElement.requestFullscreen() : " +
                "window.document.documentElement.webkitRequestFullscreen(); }";
        return js;
    }

    /// <summary>Read a JavaScript action's /JS entry, which may be a literal string or
    /// a stream.</summary>
    private static string? ActionTextValue(PdfObject? obj, PdfReader reader)
    {
        if (reader.Resolve(obj) is PdfString s)
            return Encoding.Latin1.GetString(s.Value);
        try
        {
            var stream = reader.ResolveStream(obj);
            if (stream is not null) return Encoding.Latin1.GetString(reader.DecodeStream(stream));
        }
        catch { /* undecodable JS stream — treat as no target */ }
        return null;
    }

    private static string F(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

    /// <summary>Tint→RGB map for a named /ColorSpace resource. Separation (and
    /// single-input DeviceN) spaces with an exponential (Type 2) tint transform map
    /// the scn tint through C0 + t^N·(C1−C0) into the alternate space (gray, RGB or
    /// CMYK by component count — ICCBased behaves as its /N says). Anything else —
    /// plain component spaces, sampled/calculator transforms — returns null and the
    /// component-count mapping applies.</summary>
    private static Func<double, (double r, double g, double b)>? TryBuildTintMap(
        PdfDictionary? resources, string csName, PdfReader reader)
    {
        var csDict = resources is not null ? reader.ResolveDict(resources.Get("ColorSpace")) : null;
        if (csDict is null || reader.Resolve(csDict.Get(csName)) is not PdfArray arr || arr.Count < 4)
            return null;
        var kind = (reader.Resolve(arr[0]) as PdfName)?.Value;
        if (kind != "Separation" && kind != "DeviceN") return null;

        // Alternate-space component count.
        var alt = reader.Resolve(arr[2]);
        var comps = 3;
        var altName = (alt as PdfName)?.Value
            ?? (alt is PdfArray aa && aa.Count > 0 ? (reader.Resolve(aa[0]) as PdfName)?.Value : null);
        if (altName is "DeviceGray" or "CalGray") comps = 1;
        else if (altName == "DeviceCMYK") comps = 4;
        else if (altName == "ICCBased" && alt is PdfArray icc && icc.Count > 1
                 && reader.ResolveStream(icc[1]) is { } iccStream
                 && reader.Resolve(iccStream.Dict.Get("N")) is PdfInteger iccN)
            comps = (int)iccN.Value;

        // Exponential tint transform only (the common Separation shape).
        var fn = reader.Resolve(arr[3]);
        var fnDict = fn as PdfDictionary ?? (fn as PdfStream)?.Dict;
        if (fnDict is null || reader.Resolve(fnDict.Get("FunctionType")) is not PdfInteger ft
            || ft.Value != 2)
            return null;
        double[] ReadArr(string k, double dflt)
        {
            if (reader.Resolve(fnDict.Get(k)) is PdfArray pa)
            {
                var vals = new double[pa.Count];
                for (var i = 0; i < pa.Count; i++) vals[i] = Num(reader.Resolve(pa[i])!);
                return vals;
            }
            var d = new double[comps];
            Array.Fill(d, dflt);
            return d;
        }
        var c0 = ReadArr("C0", 0.0);
        var c1 = ReadArr("C1", 1.0);
        var n = reader.Resolve(fnDict.Get("N")) is { } nObj ? Num(nObj) : 1.0;
        if (c0.Length < comps || c1.Length < comps) return null;

        return t =>
        {
            var f = Math.Pow(Math.Clamp(t, 0, 1), n);
            double C(int i) => c0[i] + f * (c1[i] - c0[i]);
            return comps switch
            {
                1 => (C(0), C(0), C(0)),
                4 => CmykToRgb(C(0), C(1), C(2), C(3)),
                _ => (C(0), C(1), C(2)),
            };
        };
    }

    /// <summary>RGB from an sc/scn/SC/SCN operand list: 1 numeric = gray,
    /// 3 = RGB, 4 = CMYK; anything else (e.g. a /Pattern name) is not a colour.</summary>
    private static bool TryColorComponents(List<PdfObject> operands,
        out double r, out double g, out double b)
    {
        r = g = b = 0;
        var nums = new List<double>(4);
        foreach (var o in operands)
        {
            if (o is PdfInteger or PdfReal) nums.Add(Num(o));
            else return false;
        }
        switch (nums.Count)
        {
            case 1: r = g = b = nums[0]; return true;
            case 3: r = nums[0]; g = nums[1]; b = nums[2]; return true;
            case 4:
                r = (1 - Math.Min(1, nums[0] + nums[3]));
                g = (1 - Math.Min(1, nums[1] + nums[3]));
                b = (1 - Math.Min(1, nums[2] + nums[3]));
                return true;
            default: return false;
        }
    }

    private static void SkipInlineImage(PdfLexer lexer)
    {
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
        }

        var pos = lexer.Position + 1;
        var len = lexer.Length;

        while (pos < len - 2)
        {
            var b = lexer.ByteAt(pos);
            if (b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20 &&
                lexer.ByteAt(pos + 1) == (byte)'E' &&
                lexer.ByteAt(pos + 2) == (byte)'I')
            {
                var after = pos + 3;
                if (after >= len || lexer.ByteAt(after) is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
                {
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len;
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}
