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
        var bytes = Encoding.UTF8.GetBytes(css);
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

    /// <summary>Append the font sidecar files for the saved pages in the formats
    /// <paramref name="fontMode"/> selects (one GUID-named file per format per font)
    /// and return the collected fonts. Each file is first offered to the caller's
    /// <see cref="HtmlSaveOptions.CustomResourceSavingStrategy"/>; a returned URL
    /// replaces the sidecar (recorded in <see cref="EmbeddedFont.Hrefs"/> for the
    /// @font-face src). <c>DontSave</c> emits nothing.</summary>
    private static List<EmbeddedFont> EmitFontSidecars(Document doc, int[] pageList,
        List<SidecarFile> sidecars, HtmlSaveOptions.FontSavingModes fontMode,
        HtmlSaveOptions? options)
    {
        if (fontMode == HtmlSaveOptions.FontSavingModes.DontSave) return new List<EmbeddedFont>();
        var fonts = CollectEmbeddedFonts(doc, pageList, options);
        foreach (var font in fonts)
        {
            var woff = fontMode is HtmlSaveOptions.FontSavingModes.AlwaysSaveAsWOFF
                or HtmlSaveOptions.FontSavingModes.SaveInAllFormats;
            var ttf = fontMode is HtmlSaveOptions.FontSavingModes.AlwaysSaveAsTTF
                or HtmlSaveOptions.FontSavingModes.SaveInAllFormats;
            var eot = fontMode is HtmlSaveOptions.FontSavingModes.AlwaysSaveAsEOT
                or HtmlSaveOptions.FontSavingModes.SaveInAllFormats;
            if (woff) EmitFontFile(sidecars, options, font, ".woff", font.Woff);
            if (ttf) EmitFontFile(sidecars, options, font, ".ttf", font.Ttf);
            if (eot) EmitFontFile(sidecars, options, font, ".eot", Text.EotWriter.Wrap(font.Ttf, font.Family));
        }
        return fonts;
    }

    /// <summary>Offer one font file to the resource strategy, falling back to a
    /// sidecar file when there is no strategy or it cancelled.</summary>
    private static void EmitFontFile(List<SidecarFile> sidecars, HtmlSaveOptions? options,
        EmbeddedFont font, string ext, byte[] bytes)
    {
        var name = font.BaseName + ext;
        if (options?.CustomResourceSavingStrategy is { } strategy)
        {
            var info = new SaveOptions.ResourceSavingInfo
            {
                ResourceType = SaveOptions.NodeLevelResourceType.Font,
                SupposedFileName = name,
                ContentStream = new System.IO.MemoryStream(bytes),
                ContentStreamData = bytes,
            };
            string? url = null;
            try { url = strategy(info); }
            catch { /* a failing caller callback must not abort the save */ }
            if (url != null && url.IndexOfAny(ForbiddenResourcePathChars) >= 0)
                throw new System.ArgumentException(
                    "Custom resource saving method returned resource path that contains char(s) forbidden in that context (('\"' or ''' or '\n' or '\r')).");
            if (!info.CustomProcessingCancelled && !string.IsNullOrEmpty(url))
            {
                font.Hrefs[ext] = url;
                return;
            }
        }
        sidecars.Add(new SidecarFile { Name = name, Content = bytes });
    }

    /// <summary>One @font-face rule for <paramref name="font"/> in the given saving
    /// mode. <paramref name="fontUrlPrefix"/> rebases the src URLs when the CSS does
    /// not live next to the font files (e.g. embedded into the HTML).</summary>
    private static string FontFaceCss(EmbeddedFont font, string fontUrlPrefix,
        HtmlSaveOptions.FontSavingModes fontMode)
    {
        // Strategy-supplied URLs (absolute) win over the default prefix+name form.
        string U(string ext) => font.Hrefs.TryGetValue(ext, out var u)
            ? u : fontUrlPrefix + font.BaseName + ext;
        var src = fontMode switch
        {
            HtmlSaveOptions.FontSavingModes.AlwaysSaveAsTTF =>
                $"\tsrc:url(\"{U(".ttf")}\") format(\"truetype\");\n",
            HtmlSaveOptions.FontSavingModes.AlwaysSaveAsEOT =>
                $"\tsrc:url(\"{U(".eot")}\");\n",
             // the "bulletproof" shape: plain EOT for old IE, then the format list
            HtmlSaveOptions.FontSavingModes.SaveInAllFormats =>
                $"\tsrc:url(\"{U(".eot")}\");\n" +
                $"\tsrc:url(\"{U(".eot")}?#iefix\") format(\"embedded-opentype\"),\n" +
                $"\turl(\"{U(".woff")}\") format(\"woff\"),\n" +
                $"\turl(\"{U(".ttf")}\") format(\"truetype\");\n",
            _ => $"\tsrc:url(\"{U(".woff")}\") format(\"woff\");\n",
        };
        return $"@font-face {{\n\tfont-family:\"{font.Family}\";\n{src}}}\n";
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

    /// <summary>An embedded TrueType font of the document, ready to emit as a sidecar.
    /// Sidecar files take GUID-shaped names (<see cref="BaseName"/> + a format
    /// extension); <see cref="Objects"/> lists the
    /// PDF font object(s) this file serves, for per-page CSS splitting.</summary>
    private sealed class EmbeddedFont
    {
        public string Family = "";
        public string BaseName = System.Guid.NewGuid().ToString();
        public byte[] Ttf = System.Array.Empty<byte>();
        public byte[] Woff = System.Array.Empty<byte>();
        public readonly List<PdfObject> Objects = new();
        public string? DedupKey;
        /// <summary>Per-format (".woff"/".ttf"/".eot") URLs supplied by the caller's
        /// resource strategy; a format with no entry uses the default sidecar name.</summary>
        public readonly Dictionary<string, string> Hrefs = new();
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

    /// <summary>True when <see cref="HtmlSaveOptions.ExcludeFontNameList"/> names this
    /// font, so the save ships neither its program nor an <c>@font-face</c> for it and
    /// its text falls back to the configured default family. The list names the FACE,
    /// so a six-letter subset tag (<c>ABCDEF+ArialMT</c>) is matched with and without
    /// its prefix.</summary>
    private static bool IsFontExcluded(HtmlSaveOptions? options, string baseFont)
    {
        if (options?.ExcludeFontNameList is not { Length: > 0 } list) return false;
        var bare = baseFont;
        if (bare.Length > 7 && bare[6] == '+') bare = bare.Substring(7);
        foreach (var name in list)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (string.Equals(name, bare, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, baseFont, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static List<EmbeddedFont> CollectEmbeddedFonts(Document doc, int[]? pages = null,
        HtmlSaveOptions? options = null)
    {
        var result = new List<EmbeddedFont>();
        var seen = new System.Collections.Generic.HashSet<string>();
        var seenObjs = new System.Collections.Generic.HashSet<PdfObject>();

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

        // Pass 1: which font objects actually show visible glyphs on the saved pages.
        var used = new System.Collections.Generic.HashSet<PdfObject>();
        foreach (var i in pageList)
            ScanUsedFontObjectsOnPage(doc, i, used);

        // Pass 2: walk the resource dictionaries and emit the used fonts in
        // encounter order.
        foreach (var i in pageList)
        {
            var page = doc.Pages[i];
            var reader = page.Reader;
            if (reader is null) continue;
            var resources = reader.ResolveDict(page.Dict.Get("Resources"));
            CollectFontsFromResources(resources, reader, seen, seenObjs, used, result, new System.Collections.Generic.HashSet<PdfObject>(), options);
        }
        return result;
    }

    /// <summary>Add every font object showing a visible glyph on page
    /// <paramref name="pageNum"/> to <paramref name="used"/>.</summary>
    private static void ScanUsedFontObjectsOnPage(Document doc, int pageNum,
        System.Collections.Generic.HashSet<PdfObject> used)
    {
        var page = doc.Pages[pageNum];
        var reader = page.Reader;
        if (reader is null) return;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        foreach (var stream in GetContentStreams(page.Dict, reader))
            ScanVisiblyUsedFonts(stream, resources, reader, used, new System.Collections.Generic.HashSet<PdfObject>());
    }

    /// <summary>Walk a content stream marking every font object that a text-showing
    /// operator gives at least one visible (non-whitespace) glyph, recursing into
    /// Form XObjects drawn by <c>Do</c> (their text uses their own resources).</summary>
    private static void ScanVisiblyUsedFonts(byte[] streamBytes, PdfDictionary? resources,
        PdfReader reader, System.Collections.Generic.HashSet<PdfObject> used,
        System.Collections.Generic.HashSet<PdfObject> visitedForms)
    {
        if (resources is null) return;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        var xobjects = reader.ResolveDict(resources.Get("XObject"));

        static bool Visible(PdfString s)
        {
            foreach (var b in s.Value)
                if (b is not (0x00 or 0x09 or 0x0A or 0x0D or 0x20)) return true;
            return false;
        }

        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        PdfDictionary? currentFont = null;
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;
            switch (token.Kind)
            {
                case TokenKind.Integer: operands.Add(new PdfInteger(token.IntValue)); break;
                case TokenKind.Real: operands.Add(new PdfReal(token.RealValue)); break;
                case TokenKind.LiteralString: operands.Add(new PdfString(token.BytesValue!)); break;
                case TokenKind.HexString: operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                case TokenKind.Name: operands.Add(new PdfName(token.StringValue!)); break;
                case TokenKind.ArrayStart: operands.Add(ParseArray(lexer)); break;
                case TokenKind.Keyword:
                    switch (token.StringValue)
                    {
                        case "Tf":
                            currentFont = operands.Count >= 1 && operands[0] is PdfName fn && fontDict is not null
                                ? reader.ResolveDict(fontDict.Get(fn.Value)) : null;
                            break;
                        case "Tj" or "'" or "\"":
                            if (currentFont is not null)
                                foreach (var o in operands)
                                    if (o is PdfString ps && Visible(ps)) { used.Add(currentFont); break; }
                            break;
                        case "TJ":
                            if (currentFont is not null && operands.Count >= 1 && operands[^1] is PdfArray arr)
                                foreach (var item in arr)
                                    if (item is PdfString ts && Visible(ts)) { used.Add(currentFont); break; }
                            break;
                        case "Do":
                            if (operands.Count >= 1 && operands[0] is PdfName xn && xobjects is not null
                                && reader.ResolveStream(xobjects.Get(xn.Value)) is { } form
                                && form.Dict.GetName("Subtype") == "Form" && visitedForms.Add(form))
                            {
                                byte[]? body = null;
                                try { body = reader.DecodeStream(form); } catch { }
                                if (body is not null)
                                    ScanVisiblyUsedFonts(body, reader.ResolveDict(form.Dict.Get("Resources")),
                                        reader, used, visitedForms);
                            }
                            break;
                        case "BI":
                            SkipInlineImage(lexer);
                            break;
                    }
                    operands.Clear();
                    break;
                default:
                    operands.Clear();
                    break;
            }
            if (operands.Count > 16) operands.Clear(); // stray tokens between keywords
        }
    }

    /// <summary>Harvest fonts from a resource dictionary's /Font entries and,
    /// recursively, from the /Resources of any Form XObject it references (fonts used
    /// only inside a form live in the form's own resource dict, not the page's).
    /// Only fonts in <paramref name="used"/> (visibly shown somewhere) are emitted,
    /// each font OBJECT once. The <paramref name="visitedForms"/> set guards against
    /// resource-graph cycles.</summary>
    private static void CollectFontsFromResources(PdfDictionary? resources, PdfReader reader,
        System.Collections.Generic.HashSet<string> seen,
        System.Collections.Generic.HashSet<PdfObject> seenObjs,
        System.Collections.Generic.HashSet<PdfObject> used,
        List<EmbeddedFont> result,
        System.Collections.Generic.HashSet<PdfObject> visitedForms,
        HtmlSaveOptions? options = null)
    {
        if (resources is null) return;

        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is not null)
        {
            foreach (var key in fontDict.Keys)
            {
                var font = reader.ResolveDict(fontDict.Get(key));
                var baseFont = font?.GetName("BaseFont");
                if (font is null || string.IsNullOrEmpty(baseFont)) continue;
                // An excluded face never claims a slot: the caller asked for its
                // program to stay out of the output entirely.
                if (IsFontExcluded(options, baseFont)) continue;
                if (!used.Contains(font) || !seenObjs.Add(font)) continue;
                var ttf = GetEmbeddedTtf(font, reader) ?? GetEmbeddedOpenType(font, reader);
                // A CID-keyed subset ships without a cmap (glyphs are addressed by GID;
                // the char mapping lives in /ToUnicode + /CIDToGIDMap), and a simple
                // TrueType subset commonly ships only a byte cmap over its re-encoded
                // content-stream codes (a (1,0) format-0 table — the CJK newspaper
                // workflow). Either face is useless to the HTML consumer — the spans
                // carry Unicode text — so synthesize a (3,1) cmap from the PDF's own
                // mapping before shipping.
                if (ttf is not null && font.GetName("Subtype") is "Type0" or "TrueType")
                    ttf = EnsureUnicodeCmap(ttf, font, reader) ?? ttf;
                if (ttf is null && GetEmbeddedBareCff(font, reader) is { } cff)
                {
                    // Bare CFF (Type1C): synthesize a TrueType sfnt so the glyphs
                    // survive into a WOFF like any other embedded program.
                    try { ttf = Text.CffToTrueType.Convert(cff); } catch { }
                }
                if (ttf is null && GetEmbeddedType1(font, reader) is { } t1)
                {
                    // Adobe Type 1 (FontFile): same treatment — without it the face
                    // never ships, and a round-trip through the HTML loses the
                    // weight, slant and ligature glyphs it carried. The dict's
                    // /Differences + /ToUnicode supplement the synthesized cmap so
                    // a glyph whose NAME has no codepoint (/equalx) is still
                    // reachable at the char the text decodes to.
                    try
                    {
                        ttf = Text.CffToTrueType.ConvertType1(t1.Data, t1.Length1, t1.Length2,
                            RawDifferencesNames(font, reader), SingleCharToUnicode(font, reader));
                    }
                    catch { }
                }
                string? dedupKey = null;
                string? substituteFamily = null;
                if (ttf is null)
                {
                    // Non-embedded font (no FontFile at all): resolve a system face for the
                    // BaseFont name and ship it like an embedded program. Deduped by NAME,
                    // not content — two BaseFonts resolving to the same host file still get
                    // separate files.
                    if (HasEmbeddedProgram(font, reader)) continue;
                    dedupKey = "name:" + baseFont;
                    if (!seen.Add(dedupKey))
                    {
                        // A later font object folded into an existing file still counts
                        // as a user of that file (per-page CSS needs to know).
                        result.Find(f => f.DedupKey == dedupKey)?.Objects.Add(font);
                        continue;
                    }
                    try { ttf = Text.SystemFontResolver.Resolve(baseFont); } catch { }
                    // A CJK font with no installed face by name serves a SUBSET
                    // of the substitute face instead (shipped as "TAG+SimSun"
                    // programs for these).
                    if (ttf is null)
                    {
                        try { ttf = BuildCjkSubstituteSubset(font, reader, out substituteFamily); }
                        catch { }
                    }
                    if (ttf is null) continue;
                }
                byte[] woff;
                try { woff = TtfToWoff(ttf); }
                catch { continue; /* an unparseable sfnt is skipped rather than aborting */ }
                var tag = substituteFamily ?? CssFaceFamily(baseFont);
                var emitted = new EmbeddedFont { Family = tag, Ttf = ttf, Woff = woff, DedupKey = dedupKey };
                emitted.Objects.Add(font);
                result.Add(emitted);
            }
        }

        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys)
        {
            var xobj = reader.ResolveStream(xobjects.Get(key));
            if (xobj is null || xobj.Dict.GetName("Subtype") != "Form" || !visitedForms.Add(xobj)) continue;
            CollectFontsFromResources(reader.ResolveDict(xobj.Dict.Get("Resources")), reader, seen, seenObjs, used, result, visitedForms, options);
        }
    }

    /// <summary>Build unicode→GID from /ToUnicode (code→text) composed with the
    /// code→GID mapping — the descendant's /CIDToGIDMap (Identity when
    /// absent/named) for a Type0 font, the program's own byte cmap for a simple
    /// TrueType subset — and patch a (3,1) format-4 cmap into
    /// <paramref name="ttf"/>. Null when the program already maps Unicode or no
    /// mapping can be derived.</summary>
    private static byte[]? EnsureUnicodeCmap(byte[] ttf, PdfDictionary font, PdfReader reader)
    {
        try
        {
            var toUni = Text.TextAbsorber.ParseToUnicodeFromDict(font, reader);
            if (toUni is not { Count: > 0 }) return null;

            var isType0 = font.GetName("Subtype") == "Type0";
            byte[]? cid2gid = null;
            if (isType0
                && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da2 && da2.Count > 0
                && reader.ResolveDict(da2[0]) is { } cidFont
                && cidFont.Get("CIDToGIDMap") is { } mapObj and not PdfName)
            {
                var mapStream = reader.ResolveStream(mapObj);
                if (mapStream is not null)
                    try { cid2gid = reader.DecodeStream(mapStream); } catch { }
            }
            Dictionary<int, int>? programCmap = null;
            if (!isType0)
            {
                try { programCmap = new Text.GlyphOutlineParser(ttf).CMap; }
                catch { return null; }
                if (programCmap.Count == 0) return null;
            }

            var uniToGid = new Dictionary<int, int>();
            foreach (var kv in toUni)
            {
                var (code, text) = (kv.Key, kv.Value);
                if (string.IsNullOrEmpty(text)) continue;
                int uni = text[0];
                if (char.IsHighSurrogate(text[0])) continue;   // format 4 is BMP-only
                int gid;
                if (isType0)
                {
                    gid = code;
                    if (cid2gid is not null)
                    {
                        var off = code * 2;
                        gid = off + 1 < cid2gid.Length ? (cid2gid[off] << 8) | cid2gid[off + 1] : 0;
                    }
                }
                else if (!programCmap!.TryGetValue(code, out gid))
                    continue;
                if (gid > 0 && !uniToGid.ContainsKey(uni)) uniToGid[uni] = gid;
            }
            return Text.CffToTrueType.TryAddUnicodeCmap(ttf, uniToGid);
        }
        catch { return null; }
    }

    /// <summary>Decoded FontFile3 program when it is a bare CFF (Type1C /
    /// CIDFontType0C — no sfnt wrapper), or null.</summary>
    private static byte[]? GetEmbeddedBareCff(PdfDictionary font, PdfReader reader)
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
            if (bytes.Length < 4) return null;
            var tag = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            // An sfnt-wrapped program is handled by GetEmbeddedOpenType; bare CFF
            // starts with the 1.x header (major version 1, header size 4).
            return tag is 0x4F54544F or 0x00010000 or 0x74727565 ? null : bytes;
        }
        catch { return null; }
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

    /// <summary>The font's /Encoding /Differences as raw code → glyph NAME (no
    /// resolution to unicode), or null when it carries none.</summary>
    private static Dictionary<int, string>? RawDifferencesNames(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var enc = reader.ResolveDict(font.Get("Encoding"));
            if (reader.Resolve(enc?.Get("Differences")) is not PdfArray diffs) return null;
            var map = new Dictionary<int, string>();
            var code = 0;
            foreach (var item in diffs)
            {
                var v = reader.Resolve(item);
                if (v is Core.PdfInteger pi) code = (int)pi.Value;
                else if (v is Core.PdfReal pr) code = (int)pr.Value;
                else if (v is PdfName pn) map[code++] = pn.Value;
            }
            return map.Count > 0 ? map : null;
        }
        catch { return null; }
    }

    /// <summary>The font's /ToUnicode as code → single codepoint, skipping
    /// multi-char expansions and the U+FFFF "unknown" sentinel. Null when the
    /// font has no usable entries.</summary>
    private static Dictionary<int, int>? SingleCharToUnicode(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var tou = Text.TextAbsorber.ParseToUnicodeFromDict(font, reader);
            if (tou is null) return null;
            var map = new Dictionary<int, int>();
            foreach (var (code, dst) in tou)
            {
                if (dst.Length == 0 || Text.TextAbsorber.IsUnknownToUnicodeDst(dst)) continue;
                if (CodePointCount(dst) != 1) continue;
                map[code] = char.ConvertToUtf32(dst, 0);
            }
            return map.Count > 0 ? map : null;
        }
        catch { return null; }
    }

    /// <summary>(/Ascent + |/Descent|) / 1000 from the font's descriptor — the
    /// line-height for a program that carries no sfnt of its own (a bare CFF /
    /// Type1C or Type1 subset has neither hhea nor OS/2). 0 when unreadable.</summary>
    private static double DescriptorLineHeightFactor(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var descriptor = reader.ResolveDict(font.Get("FontDescriptor"));
            if (descriptor is null && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da && da.Count > 0)
            {
                var descFont = reader.ResolveDict(da[0]);
                descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
            }
            if (descriptor is null) return 0;
            var ascent = DescriptorNumber(reader.Resolve(descriptor.Get("Ascent")));
            var descent = DescriptorNumber(reader.Resolve(descriptor.Get("Descent")));
            var lh = (ascent + Math.Abs(descent)) / 1000.0;
            return lh > 0 ? lh : 0;
        }
        catch { return 0; }
    }

    private static double DescriptorNumber(object? value) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>True when the font (or its descendant CID font) carries any embedded
    /// program — FontFile (Type1), FontFile2 (TrueType) or FontFile3 (CFF/OpenType).</summary>
    private static bool HasEmbeddedProgram(PdfDictionary font, PdfReader reader)
    {
        try
        {
            var descriptor = reader.ResolveDict(font.Get("FontDescriptor"));
            if (descriptor is null && reader.Resolve(font.Get("DescendantFonts")) is PdfArray da && da.Count > 0)
            {
                var descFont = reader.ResolveDict(da[0]);
                descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
            }
            if (descriptor is null) return false;
            return descriptor.Get("FontFile") is not null
                || descriptor.Get("FontFile2") is not null
                || descriptor.Get("FontFile3") is not null;
        }
        catch { return false; }
    }

    /// <summary>Wrap an sfnt (TrueType) font program in a WOFF 1.0 container, zlib-
    /// compressing each table when that shrinks it. Structure per the W3C WOFF spec:
    /// 44-byte header, a 20-byte directory entry per table, then 4-byte-aligned
    /// (optionally compressed) table data.</summary>
    private static byte[] TtfToWoff(byte[] sfnt)
    {
        uint U32(int o) => (uint)((sfnt[o] << 24) | (sfnt[o + 1] << 16) | (sfnt[o + 2] << 8) | sfnt[o + 3]);
        ushort U16(int o) => (ushort)((sfnt[o] << 8) | sfnt[o + 1]);

        var flavor = U32(0);
        var numTables = U16(4);

        var entries = new List<byte[]>();
        var blocks = new List<byte[]>();
        var woffHeader = 44;
        var dirSize = numTables * 20;
        var offset = woffHeader + dirSize;
        uint totalSfntSize = (uint)(12 + numTables * 16);

        for (var i = 0; i < numTables; i++)
        {
            var p = 12 + i * 16;
            var tag = U32(p);
            var checksum = U32(p + 4);
            var tblOff = (int)U32(p + 8);
            var tblLen = (int)U32(p + 12);
            var orig = new byte[tblLen];
            System.Array.Copy(sfnt, tblOff, orig, 0, tblLen);
            var comp = ZlibCompress(orig);
            var data = comp.Length < orig.Length ? comp : orig;

            var e = new byte[20];
            WriteU32(e, 0, tag);
            WriteU32(e, 4, (uint)offset);
            WriteU32(e, 8, (uint)data.Length);
            WriteU32(e, 12, (uint)tblLen);
            WriteU32(e, 16, checksum);
            entries.Add(e);
            blocks.Add(data);

            offset += data.Length;
            offset = (offset + 3) & ~3;
            totalSfntSize += (uint)((tblLen + 3) & ~3);
        }

        var ms = new System.IO.MemoryStream();
        var header = new byte[44];
        WriteU32(header, 0, 0x774F4646);       // 'wOFF'
        WriteU32(header, 4, flavor);
        WriteU32(header, 8, (uint)offset);      // total WOFF length
        WriteU16(header, 12, numTables);
        WriteU32(header, 16, totalSfntSize);
        WriteU16(header, 20, 1);                // majorVersion
        ms.Write(header);
        foreach (var e in entries) ms.Write(e);
        foreach (var block in blocks)
        {
            ms.Write(block);
            while (ms.Length % 4 != 0) ms.WriteByte(0);
        }
        return ms.ToArray();
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new System.IO.MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static void WriteU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static void WriteU16(byte[] b, int o, ushort v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }

    /// <summary>The stylesheet emitted alongside external HTML. Carries the fixed
    /// structural rules the layout relies on (positioned text, page box, sup/sub).</summary>
    private static string BuildStyleCss() =>
        ".stl_01 { position: absolute; white-space: nowrap; }\n" +
        ".stl_02 { font-size: 1em; line-height: 0.0em; border-style: none; display: block; margin: 0em; }\n" +
        ".pdf-page { position: relative; margin: 10px auto; border: 1px solid #ccc; overflow: hidden; }\n" +
        ".pdf-text { position: absolute; white-space: pre; }\n" +
        ".pdf-link { position: absolute; display: block; }\n" +
        ".stl_ sup { vertical-align: baseline; position: relative; top: -0.4em; }\n" +
        ".stl_ sub { vertical-align: baseline; position: relative; top: 0.4em; }\n" +
        ".stl_ a:link { text-decoration:none; }\n" +
        ".stl_ a:visited { text-decoration:none; }\n";

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

    private sealed class FontInfo
    {
        public string Family { get; init; } = "sans-serif";
        /// <summary>Human-readable single family for fixed-layout CSS rules
        /// ("Century Gothic", "Calibri") — the BaseFont with subset prefix and
        /// style suffix removed and camel-case words re-spaced. The flow-layout
        /// path keeps <see cref="Family"/>'s generic fallback stack instead.</summary>
        public string CssFamily { get; init; } = "sans-serif";
        public string Weight { get; init; } = "normal";
        public string Style { get; init; } = "normal";
        public Func<byte[], string>? ToUnicode { get; init; }
        /// <summary>Base-encoding decode for fonts without a ToUnicode CMap
        /// (named encodings, /Differences glyph names, embedded cmap/post).</summary>
        public Func<byte[], string>? BaseDecode { get; init; }
        /// <summary>Whether the embedded program's cmap covers a codepoint;
        /// null when no embedded program (full coverage assumed).</summary>
        public Func<int, bool>? SubsetHas { get; init; }
        /// <summary>Whether a shown CHARACTER CODE resolves to a glyph the
        /// embedded program's own cmap can address (a GID-only variant glyph
        /// renders in the CSS fallback face instead). Null = always mapped.</summary>
        public Func<int, bool>? GlyphMapped { get; init; }
        /// <summary>The embedded program's advance for a shown CHARACTER CODE,
        /// milli-em — the browser-model metric when the subset itself is the
        /// served face. Null func or null result = measure by the resolved
        /// installed face instead.</summary>
        public Func<int, double?>? EmbeddedAdvMilli { get; init; }
        /// <summary>The embedded program's own hmtx advance for a shown CHARACTER
        /// CODE (cid → gid → hmtx/upm), milli-em, unquantized. The em-compensation
        /// dialect solves its spacing against exactly this basis: the line is
        /// measured with the glyph advances of the program being re-served,
        /// so a ligature code weighs its LIGATURE advance and every /W-vs-face
        /// rounding residue stays in the word-spacing numerator. Null when the
        /// font embeds no parsable TrueType program.</summary>
        public Func<int, double?>? ProgramAdvMilli { get; init; }
        /// <summary>Embedded program advance by CHARACTER (reverse ToUnicode →
        /// code → gid → hmtx), for ligature-component measuring.</summary>
        public Func<int, double?>? ProgramCharAdvMilli { get; init; }
        public bool IsCidFont { get; init; }
        /// <summary>A Type3 face: its glyphs are content-stream procedures, not a
        /// program a browser can be handed, so the text drawn with it is only ever a
        /// best-effort transcription.</summary>
        public bool IsType3 { get; init; }
        /// <summary>OS/2 usWinAscent / unitsPerEm of the embedded font program —
        /// the ascent fraction the fixed-layout `top` subtracts (not the
        /// FontDescriptor /Ascent). 1.0 when no embedded sfnt provides it.</summary>
        public double AscentFactor { get; init; } = 1.0;
        /// <summary>hhea (asc+|desc|)/upm — the stl_ line-height class value; 0 = no program.</summary>
        public double LineHeightEm { get; init; }
        /// <summary>Advance of one character code in em fractions (1000-unit widths
        /// / 1000), from /Widths (simple) or /W + /DW (CID); null = no width data.</summary>
        public Func<int, double>? AdvanceOf { get; init; }
        /// <summary>The font serves a SUBSTITUTE face's subset (SimSun standing in
        /// for a non-embedded, non-installed CJK font).</summary>
        public bool SubstituteFace { get; init; }
    }

    /// <summary>Naive CMYK→RGB (the same additive mapping the render devices use
    /// for k/K without an ICC profile).</summary>
    private static (double, double, double) CmykToRgb(double c, double m, double y, double k) =>
        ((1 - Math.Clamp(c, 0, 1)) * (1 - Math.Clamp(k, 0, 1)),
         (1 - Math.Clamp(m, 0, 1)) * (1 - Math.Clamp(k, 0, 1)),
         (1 - Math.Clamp(y, 0, 1)) * (1 - Math.Clamp(k, 0, 1)));

    /// <summary>Build a code → advance (em fraction) lookup for <paramref name="font"/>:
    /// simple fonts from /FirstChar + /Widths (+ /MissingWidth), Type0 from the
    /// descendant's /W ranges with /DW as the default. Falls back to the embedded
    /// program's hmtx (through its cmap for simple fonts, CID→GID for composites)
    /// when the dictionary carries no widths; null when nothing is available.</summary>
    private static Func<int, double>? BuildAdvanceMap(PdfDictionary font, PdfReader reader, bool isCid)
    {
        try
        {
            if (!isCid)
            {
                var widths = reader.Resolve(font.Get("Widths")) as PdfArray;
                if (widths is { Count: > 0 })
                {
                    var first = (reader.Resolve(font.Get("FirstChar")) as PdfInteger)?.Value ?? 0;
                    var desc = reader.ResolveDict(font.Get("FontDescriptor"));
                    var missing = desc is not null
                        && reader.Resolve(desc.Get("MissingWidth")) is PdfInteger mw ? mw.Value : 0;
                    var arr = new double[widths.Count];
                    for (var i = 0; i < widths.Count; i++)
                        arr[i] = widths[i] is PdfInteger wi ? wi.Value
                            : widths[i] is PdfReal wr ? wr.Value : 0;
                    return code => code >= first && code - first < arr.Length
                        ? arr[code - first] / 1000.0 : missing / 1000.0;
                }
            }
            else
            {
                var descArr = reader.Resolve(font.Get("DescendantFonts")) as PdfArray;
                var descFont = descArr is { Count: > 0 } ? reader.ResolveDict(descArr[0]) : null;
                if (descFont is not null)
                {
                    double dw = reader.Resolve(descFont.Get("DW")) is PdfInteger d ? d.Value : 1000;
                    var map = new Dictionary<int, double>();
                    if (reader.Resolve(descFont.Get("W")) is PdfArray w)
                    {
                        var i = 0;
                        double NumAt(PdfObject? o) => o is PdfInteger pi ? pi.Value
                            : o is PdfReal pr ? pr.Value : 0;
                        while (i < w.Count)
                        {
                            if (i + 1 < w.Count && reader.Resolve(w[i]) is PdfInteger c0
                                && reader.Resolve(w[i + 1]) is PdfArray ws)
                            {
                                for (var k = 0; k < ws.Count; k++) map[(int)c0.Value + k] = NumAt(ws[k]);
                                i += 2;
                            }
                            else if (i + 2 < w.Count && reader.Resolve(w[i]) is PdfInteger ca
                                && reader.Resolve(w[i + 1]) is PdfInteger cb)
                            {
                                var val = NumAt(reader.Resolve(w[i + 2]));
                                for (var c = (int)ca.Value; c <= cb.Value && c - ca.Value < 65536; c++) map[c] = val;
                                i += 3;
                            }
                            else i++;
                        }
                    }
                    if (map.Count > 0 || dw != 1000)
                        return code => map.TryGetValue(code, out var v) ? v / 1000.0 : dw / 1000.0;
                }
            }

            // No dictionary widths: try the embedded program's own advances.
            var ttf = GetEmbeddedTtf(font, reader);
            if (ttf is not null)
            {
                var parser = new Text.GlyphOutlineParser(ttf);
                var upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000.0;
                if (!isCid)
                    return code => parser.CMap.TryGetValue(code, out var gid) && gid != 0
                        ? parser.GetAdvanceWidth(gid) / upm : 0.5;
                return code => // Identity CID: the code is (usually) the glyph id
                    parser.GetAdvanceWidth(code) is var adv && adv > 0 ? adv / upm : 0.5;
            }
        }
        catch { /* fall through to null: extent pinning simply stays off */ }
        return null;
    }

    /// <summary>
    /// Per-font output-character registry for ligature and unmapped-code handling.
    /// A char code whose ToUnicode sequence cannot be rendered from component glyphs
    /// (the embedded font has no cmap entries for them) is emitted as ONE character:
    /// the standard Unicode ligature char when the sequence has one, else the
    /// sequence's first character. When that character is already owned by a
    /// different char code of the same font, a fresh code is minted from U+A880
    /// upward instead. Identity-encoded CID codes with no unicode mapping at all
    /// mint directly — their char code is a glyph id, not text.
    /// </summary>
    private sealed class LigatureSubstitutor
    {
        private readonly Dictionary<int, string> _codeToText = new();
        private readonly HashSet<char> _owned = new();
        private char _mint = '\uA880';

        /// <summary>Register a collapsed ligature code with its preferred character.</summary>
        public string Register(int code, char desired)
        {
            if (_codeToText.TryGetValue(code, out var existing)) return existing;
            var ch = desired;
            while (_owned.Contains(ch)) ch = _mint++;
            _owned.Add(ch);
            var text = ch.ToString();
            _codeToText[code] = text;
            return text;
        }

        /// <summary>Register a code that has no derivable unicode at all.</summary>
        public string Mint(int code)
        {
            if (_codeToText.TryGetValue(code, out var existing)) return existing;
            var ch = _mint++;
            while (_owned.Contains(ch)) ch = _mint++;
            _owned.Add(ch);
            var text = ch.ToString();
            _codeToText[code] = text;
            return text;
        }
    }

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

    /// <summary>CSS family name for an embedded font's class and @font-face: the FULL
    /// BaseFont with the subset prefix kept and name/style separators normalized to
    /// spaces ("ACMJVR+Arial,Bold" → "ACMJVR+Arial Bold").</summary>
    internal static string CssFaceFamily(string baseFont)
    {
        var s = baseFont.Replace(',', ' ').Replace('-', ' ');
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    [System.ThreadStatic] private static Dictionary<string, bool>? _sysResolvable;

    /// <summary>Whether the system resolver can supply a program for this BaseFont —
    /// the sidecar emitter will then embed it as an @font-face, so the class family
    /// must use the same BaseFont-derived name. Cached per thread.</summary>
    private static bool SystemResolvable(string baseFont)
    {
        var cache = _sysResolvable ??= new Dictionary<string, bool>(StringComparer.Ordinal);
        if (cache.TryGetValue(baseFont, out var known)) return known;
        bool ok;
        try { ok = Text.SystemFontResolver.Resolve(baseFont) is not null; }
        catch { ok = false; }
        return cache[baseFont] = ok;
    }

    /// <summary>The substitute face family served for a CJK font that neither
    /// embeds a program nor resolves to an installed face by name —
    /// SimSun subsets are shipped for such fonts (emitted as
    /// "TAG+SimSun" @font-face programs). Null when the font is
    /// not such a case.</summary>
    private static string? CjkSubstituteFamily(PdfDictionary font, PdfReader reader)
    {
        if (HasEmbeddedProgram(font, reader)) return null;
        var baseFont = font.GetName("BaseFont") ?? "";
        if (baseFont.Length == 0 || SystemResolvable(baseFont)) return null;
        // A registry-decoded CID font declares its script outright.
        if (CidOrderingOf(font, reader) == "GB1") return "SimSun";
        Dictionary<int, string>? toUni = null;
        try { toUni = Text.TextAbsorber.ParseToUnicodeFromDict(font, reader); }
        catch { }
        if (toUni is null) return null;
        foreach (var dst in toUni.Values)
            if (dst.Length > 0 && HtmlToPdfConverter.StlIdeograph(dst[0]))
                return "SimSun";
        return null;
    }

    /// <summary>CIDSystemInfo /Ordering of a Type0 font's descendant ("GB1",
    /// "Japan1", …), null for simple fonts or absent info.</summary>
    private static string? CidOrderingOf(PdfDictionary font, PdfReader reader)
    {
        try
        {
            if (font.GetName("Subtype") != "Type0") return null;
            if (reader.Resolve(font.Get("DescendantFonts")) is not PdfArray da || da.Count == 0) return null;
            var cidFont = reader.ResolveDict(da[0]);
            if (cidFont is null) return null;
            var csi = reader.ResolveDict(cidFont.Get("CIDSystemInfo"));
            if (csi is null) return null;
            var registry = reader.Resolve(csi.Get("Registry")) is PdfString rs ? rs.ToText() : null;
            if (registry != "Adobe") return null;
            return reader.Resolve(csi.Get("Ordering")) is PdfString os ? os.ToText() : null;
        }
        catch { return null; }
    }

    /// <summary>Code → Unicode for a substituted font: the single-char ToUnicode
    /// when the font carries one, else the registry decode over the /W-declared
    /// CIDs (an Identity-H newspaper font names its used set there).</summary>
    private static Dictionary<int, int>? SubstituteCodeToUnicode(PdfDictionary font, PdfReader reader)
    {
        if (SingleCharToUnicode(font, reader) is { } tu) return tu;
        var ordering = CidOrderingOf(font, reader);
        if (ordering is null) return null;
        var map = new Dictionary<int, int>();
        foreach (var cid in DeclaredWidthCids(font, reader))
            if (Text.AdobeCidTables.LookupCid(ordering, cid) is { } uni and > 0)
                map.TryAdd(cid, uni);
        return map.Count > 0 ? map : null;
    }

    /// <summary>The CIDs a Type0 font's /W array declares widths for — the
    /// producer's own used-glyph set.</summary>
    private static IEnumerable<int> DeclaredWidthCids(PdfDictionary font, PdfReader reader)
    {
        if (reader.Resolve(font.Get("DescendantFonts")) is not PdfArray da || da.Count == 0) yield break;
        if (reader.Resolve(reader.ResolveDict(da[0])?.Get("W")) is not PdfArray w) yield break;
        for (var i = 0; i < w.Count - 1;)
        {
            var start = reader.Resolve(w[i]) switch { PdfInteger n => n.Value, PdfReal r => (long)r.Value, _ => -1L };
            if (start < 0) yield break;
            if (reader.Resolve(w[i + 1]) is PdfArray arr)
            {
                for (var k = 0; k < arr.Count; k++) yield return (int)start + k;
                i += 2;
            }
            else
            {
                if (i + 2 >= w.Count) yield break;
                var end = reader.Resolve(w[i + 1]) switch { PdfInteger n => n.Value, PdfReal r => (long)r.Value, _ => start - 1 };
                for (var cid = start; cid <= end && cid - start < 65536; cid++) yield return (int)cid;
                i += 3;
            }
        }
    }

    /// <summary>Deterministic six-letter subset tag for a substituted font,
    /// derived from the BaseFont name — the same "ABCDEF+" shape an embedder's
    /// subset carries, but stable across runs so the emitted markup is
    /// reproducible.</summary>
    private static string SubstituteTag(string baseFont)
    {
        var h = 5381u;
        foreach (var c in baseFont) h = unchecked(h * 33 + c);
        var tag = new char[6];
        for (var i = 0; i < 6; i++) { tag[i] = (char)('A' + (int)(h % 26)); h /= 26; }
        return new string(tag);
    }

    [ThreadStatic]
    private static Dictionary<string, Text.GlyphOutlineParser?>? _substituteParsers;

    /// <summary>The installed substitute face's outline parser, cached per
    /// family (the TTC face extraction and glyf parse are paid once).</summary>
    private static Text.GlyphOutlineParser? ResolveSubstituteParser(string family)
    {
        var cache = _substituteParsers ??= new Dictionary<string, Text.GlyphOutlineParser?>(StringComparer.Ordinal);
        if (cache.TryGetValue(family, out var p)) return p;
        Text.GlyphOutlineParser? parser = null;
        try
        {
            var bytes = Text.SystemFontResolver.Resolve(family);
            if (bytes is not null) parser = new Text.GlyphOutlineParser(bytes);
        }
        catch { }
        return cache[family] = parser;
    }

    /// <summary>Builds the shipped subset program for a substituted CJK font:
    /// the substitute face's glyphs behind the font's ToUnicode destinations
    /// (multi-char destinations contribute each component). Null when the font
    /// is not a substitute case or nothing maps.</summary>
    private static byte[]? BuildCjkSubstituteSubset(PdfDictionary font, PdfReader reader, out string? family)
    {
        family = null;
        var subName = CjkSubstituteFamily(font, reader);
        if (subName is null) return null;
        var parser = ResolveSubstituteParser(subName);
        if (parser is null) return null;
        var codeToUni = SubstituteCodeToUnicode(font, reader);
        if (codeToUni is null) return null;
        var uniToGid = new Dictionary<int, int>();
        foreach (var uni in codeToUni.Values)
            if (uni <= 0xFFFF && !uniToGid.ContainsKey(uni)
                && parser.CMap.TryGetValue(uni, out var g) && g > 0)
                uniToGid[uni] = g;
        if (parser.CMap.TryGetValue(' ', out var gSp) && gSp > 0) uniToGid.TryAdd(' ', gSp);
        var ttf = Text.CffToTrueType.BuildSubset(parser, uniToGid);
        if (ttf is null) return null;
        family = SubstituteTag(font.GetName("BaseFont") ?? "font") + "+" + subName;
        return ttf;
    }

    /// <summary>Any right-to-left-script codepoint (Hebrew/Arabic blocks and their
    /// presentation forms).</summary>
    private static bool HasRtlCodepoint(string s)
    {
        foreach (var c in s)
            if (c is (>= (char)0x0590 and <= (char)0x08FF)
                or (>= (char)0xFB1D and <= (char)0xFDFF)
                or (>= (char)0xFE70 and <= (char)0xFEFF))
                return true;
        return false;
    }

    /// <summary>The single character standing in for a multi-char ligature sequence.</summary>
    private static char StandardLigatureChar(string seq) => seq switch
    {
        "ff" => '\uFB00',
        "fi" => '\uFB01',
        "fl" => '\uFB02',
        "ffi" => '\uFB03',
        "ffl" => '\uFB04',
        "ft" => '\uFB05',
        "st" => '\uFB06',
        "ue" => '\u1D6B',
        "Th" => '\uE000',
        _ => seq[0],
    };

    /// <summary>A ToUnicode dst of tab+CR+space+nbsp marks the inter-word space glyph.</summary>
    private const string SpaceLigature = "\u0009\u000D\u0020\u00A0";

    private static int CodePointCount(string s)
    {
        var n = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            n++;
        }
        return n;
    }

    private static bool AllInCmap(string s, HashSet<int> cmapChars)
    {
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            if (!cmapChars.Contains(cp)) return false;
        }
        return true;
    }

    private static Dictionary<string, FontInfo> ResolveFonts(PdfDictionary pageDict, PdfReader reader,
        bool preferFontCmap = false, Dictionary<int, LigatureSubstitutor>? substitutors = null,
        string? defaultFontName = null, bool friendlyFamilies = false)
        => ResolveFontsFromResources(reader.ResolveDict(pageDict.Get("Resources")), reader,
            preferFontCmap, substitutors, defaultFontName, friendlyFamilies);

    /// <summary>Resolve the /Font entries of one resource dictionary (a page's or a
    /// Form XObject's own) into decode-ready <see cref="FontInfo"/> records.</summary>
    private static Dictionary<string, FontInfo> ResolveFontsFromResources(PdfDictionary? resources,
        PdfReader reader, bool preferFontCmap = false,
        Dictionary<int, LigatureSubstitutor>? substitutors = null,
        string? defaultFontName = null, bool friendlyFamilies = false)
    {
        var result = new Dictionary<string, FontInfo>(StringComparer.Ordinal);
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;

        foreach (var key in fontDict.Keys)
        {
            var fontRef = fontDict.Get(key);
            var font = reader.ResolveDict(fontRef);
            if (font is null) continue;

            var baseFont = font.GetName("BaseFont") ?? "sans-serif";
            var (family, weight, style) = MapFont(baseFont);
            // HtmlSaveOptions.DefaultFontName substitutes the requested face for every
            // source font — embedded ones included: the emitted font classes carry it
            // as the family, so the text both displays and round-trips in that face.
            if (!string.IsNullOrEmpty(defaultFontName)) family = defaultFontName;

            // Otherwise a font that ships as an @font-face program is named by its
            // FULL BaseFont there — subset prefix kept, separator-normalized
            // ("ACMJVR+Arial,Bold" → "ACMJVR+Arial Bold"); the class must reference
            // the SAME family, or the consumer substitutes a host face whose cmap
            // disagrees with a custom-encoded subset (an invoice re-imported through
            // such classes painted every run with a sibling font's garble), and a
            // bold sibling must get its own class rather than folding into the
            // regular face's. With FontSavingMode.DontSave nothing is embedded, so
            // there is no @font-face to match and the class instead keeps the font's
            // FRIENDLY family name (CssFamily below) — the raw BaseFont's style tail
            // ("Calibri-Bold") would otherwise leak into the CSS, which should
            // show the plain family ("Calibri").
            else if (!friendlyFamilies
                     && (HasEmbeddedProgram(font, reader) || SystemResolvable(baseFont)))
            {
                // System-resolvable fonts count too: the sidecar emitter embeds the
                // resolved face as an @font-face under the same BaseFont-derived name.
                var famTag = CssFaceFamily(baseFont);
                if (famTag.Length > 0) family = famTag;
            }
            else if (!friendlyFamilies && CjkSubstituteFamily(font, reader) is { } subFam)
            {
                // A substituted CJK font's class must reference the shipped
                // subset's @font-face name, exactly like an embedded program's.
                family = SubstituteTag(baseFont) + "+" + subFam;
            }

            // Parse ToUnicode CMap
            var toUnicodeMap = Text.TextAbsorber.ParseToUnicodeFromDict(font, reader);

            // A U+FFFF/U+FFFE destination is the producer's "unicode unknown"
            // (pdfTeX writes it for ligature glyphs). The /Differences glyph
            // name resolves where the CMap could not (/f_i → U+FB01); a code
            // neither can name drops back to the base decode.
            if (toUnicodeMap is not null)
            {
                List<int>? unknown = null;
                foreach (var (code, dst) in toUnicodeMap)
                    if (Text.TextAbsorber.IsUnknownToUnicodeDst(dst))
                        (unknown ??= new List<int>()).Add(code);
                if (unknown is not null)
                {
                    var rawNames = RawDifferencesNames(font, reader);
                    foreach (var code in unknown)
                    {
                        var resolved = rawNames is not null && rawNames.TryGetValue(code, out var nm)
                            ? Text.TextAbsorber.ResolveGlyphName(nm)
                            : null;
                        if (resolved is { Length: > 0 }) toUnicodeMap[code] = resolved;
                        else toUnicodeMap.Remove(code);
                    }
                }
            }

            // Check if this is a CID font (Type0)
            var fontSubtype = font.GetName("Subtype");
            var isCid = fontSubtype == "Type0";

            // FontEncodingRules.DecreaseToUnicodePriorityLevel: the font program's own
            // cmap subtable outranks the /ToUnicode CMap. Exporters that pre-compose
            // text sometimes map a combining-mark CID to a space (or another filler)
            // in /ToUnicode while the embedded cmap still carries the real codepoint
            // (e.g. Thai NIKHAHIT U+0E4D) — copy/paste from the HTML then loses the
            // character unless the cmap wins. Identity CIDs are glyph ids, so the
            // reverse cmap (gid → unicode) applies directly.
            Dictionary<int, int>? reverseCmap = null;
            if (preferFontCmap && isCid)
            {
                var descArr = reader.Resolve(font.Get("DescendantFonts")) as PdfArray;
                var descFont = descArr is { Count: > 0 } ? reader.ResolveDict(descArr[0]) : null;
                var descriptor = descFont is not null ? reader.ResolveDict(descFont.Get("FontDescriptor")) : null;
                var fontFile = descriptor is not null ? reader.ResolveStream(descriptor.Get("FontFile2")) : null;
                if (fontFile is not null)
                {
                    try
                    {
                        var parser = new Text.GlyphOutlineParser(reader.DecodeStream(fontFile));
                        reverseCmap = new Dictionary<int, int>();
                        foreach (var (ch, gid) in parser.CMap)
                            if (!reverseCmap.ContainsKey(gid)) reverseCmap[gid] = ch;
                    }
                    catch { reverseCmap = null; }
                }
            }

            // The ligature/unmapped-code model needs the embedded font program's
            // cmap coverage: a multi-char ToUnicode sequence stays expanded only
            // when the font can actually render its component characters, and an
            // Identity-encoded code with no unicode mapping at all is a bare glyph
            // id whose text form must be minted (U+A880 upward), exactly one new
            // character per glyph, shared across the pages of one conversion.
            var isIdentity = font.GetName("Encoding") is "Identity-H" or "Identity-V";
            var hasMultiDst = false;
            if (toUnicodeMap is not null)
                foreach (var dst in toUnicodeMap.Values)
                    if (CodePointCount(dst) > 1) { hasMultiDst = true; break; }

            // With Identity ENCODING the 2-byte code is the CID, but the CID is the
            // glyph id only under an Identity /CIDToGIDMap. A CIDToGIDMap STREAM
            // (packed big-endian uint16 per CID) marks the codes as true CIDs.
            PdfStream? c2gStream = null;
            if (isCid && isIdentity)
            {
                var descArr2 = reader.Resolve(font.Get("DescendantFonts")) as PdfArray;
                var descFont2 = descArr2 is { Count: > 0 } ? reader.ResolveDict(descArr2[0]) : null;
                var c2gObj = descFont2?.Get("CIDToGIDMap");
                c2gStream = c2gObj is not null ? reader.ResolveStream(c2gObj) : null;
            }

            HashSet<int>? cmapChars = null;
            Dictionary<int, int>? gidToUnicode = null;
            Func<int, bool>? glyphMapped = null;
            Func<int, double?>? embeddedAdvMilli = null;
            Func<int, double?>? programAdvMilli = null;
            Func<int, double?>? programCharAdvMilli = null;
            if (hasMultiDst || (isCid && isIdentity))
            {
                var ttf = GetEmbeddedTtf(font, reader);
                if (ttf is not null)
                {
                    try
                    {
                        var parser = new Text.GlyphOutlineParser(ttf);
                        cmapChars = new HashSet<int>(parser.CMap.Keys);
                        if (isCid && isIdentity)
                        {
                            gidToUnicode = new Dictionary<int, int>();
                            foreach (var (ch, gid) in parser.CMap)
                                if (!gidToUnicode.ContainsKey(gid)) gidToUnicode[gid] = ch;

                            // Thread the CIDToGIDMap stream so the map is keyed by the
                            // CODE the content stream actually shows (cid → gid → unicode).
                            byte[]? cgBytes = null;
                            if (c2gStream is not null)
                            {
                                var cg = reader.DecodeStream(c2gStream);
                                var cidToUnicode = new Dictionary<int, int>();
                                for (int cid = 0; cid * 2 + 1 < cg.Length; cid++)
                                {
                                    int gid = (cg[cid * 2] << 8) | cg[cid * 2 + 1];
                                    if (gid != 0 && gidToUnicode.TryGetValue(gid, out var u))
                                        cidToUnicode[cid] = u;
                                }
                                gidToUnicode = cidToUnicode;
                                cgBytes = cg;
                            }

                            // The program's own advance per shown code, for the
                            // em-compensation basis (cid → gid via the map, else
                            // Identity).
                            var upmProg = (double)parser.UnitsPerEm;
                            if (upmProg > 0)
                            {
                                var parserProg = parser;
                                var cgProg = cgBytes;
                                programAdvMilli = code =>
                                {
                                    var gid = cgProg is null
                                        ? code
                                        : code * 2 + 1 < cgProg.Length
                                            ? (cgProg[code * 2] << 8) | cgProg[code * 2 + 1]
                                            : 0;
                                    if (gid <= 0) return null;
                                    var w = parserProg.GetAdvanceWidth(gid);
                                    return w > 0 ? w * 1000.0 / upmProg : null;
                                };
                                // By CHARACTER: reverse the font's single-char
                                // ToUnicode so a ligature's components measure by
                                // the program's own f/t/i advances.
                                if (SingleCharToUnicode(font, reader) is { } uniOf)
                                {
                                    var u2code = new Dictionary<int, int>();
                                    foreach (var (code2, uni2) in uniOf)
                                        u2code.TryAdd(uni2, code2);
                                    var pam = programAdvMilli;
                                    programCharAdvMilli = ch =>
                                        u2code.TryGetValue(ch, out var c2) ? pam(c2) : null;
                                }
                            }

                            // A subset can carry several glyph VARIANTS of one
                            // character (duplicate instances from merged runs).
                            // The re-encoded face holds one glyph per character:
                            // the FIRST variant shown claims the slot, and later
                            // occurrences through a different variant (or through
                            // a multi-char ligature glyph) render in the CSS
                            // fallback face.
                            if (toUnicodeMap is not null)
                            {
                                int GidOf(int code) => cgBytes is null
                                    ? code
                                    : code * 2 + 1 < cgBytes.Length
                                        ? (cgBytes[code * 2] << 8) | cgBytes[code * 2 + 1]
                                        : 0;
                                var touForMapped = toUnicodeMap;
                                var slotWinner = new Dictionary<int, int>();
                                var cmapUnis = new HashSet<int>(parser.CMap.Keys);
                                var cmapGidSet = new HashSet<int>(parser.CMap.Values);
                                // With a DefaultFontName substitution the substituted
                                // face serves every character — the subset's variant
                                // structure is irrelevant and the machinery stays off.
                                var anyVariant = false;
                                if (string.IsNullOrEmpty(defaultFontName))
                                {
                                    foreach (var (cid0, txt0) in touForMapped)
                                    {
                                        if (txt0.Length == 0 || CodePointCount(txt0) != 1) continue;
                                        var g0 = GidOf(cid0);
                                        if (g0 != 0 && !cmapGidSet.Contains(g0)) { anyVariant = true; break; }
                                    }
                                }
                                // The whole variant/fallback machinery only exists for
                                // subsets that actually carry GID-only variant glyphs;
                                // ordinary subsets (or substituted fonts whose
                                // ToUnicode merely exceeds the cmap) keep the plain
                                // single-face model.
                                glyphMapped = !anyVariant ? null : code =>
                                {
                                    if (!touForMapped.TryGetValue(code, out var txt) || txt.Length == 0)
                                        return true;
                                    if (CodePointCount(txt) != 1)
                                    {
                                        // A ligature glyph whose expansion the subset can
                                        // render from component cmap glyphs stays in the
                                        // main face (the text is already expanded); only
                                        // an expansion with an uncovered component falls
                                        // to the fallback face.
                                        for (var ei = 0; ei < txt.Length; )
                                        {
                                            var cpt = char.ConvertToUtf32(txt, ei);
                                            if (!cmapUnis.Contains(cpt)) return false;
                                            ei += char.IsSurrogatePair(txt, ei) ? 2 : 1;
                                        }
                                        return true;
                                    }
                                    var gid = GidOf(code);
                                    if (gid == 0) return true;
                                    var uni = char.ConvertToUtf32(txt, 0);
                                    // A character the subset's cmap does not know at
                                    // all can only render from the fallback face.
                                    if (!cmapUnis.Contains(uni)) return false;
                                    if (!slotWinner.TryGetValue(uni, out var w))
                                    {
                                        slotWinner[uni] = gid;
                                        return true;
                                    }
                                    return w == gid;
                                };

                                // A subset carrying GID-only variant glyphs can
                                // only serve as itself (re-encoded with its own
                                // metrics), so the browser model measures such a
                                // font's glyphs by the embedded program's
                                // advances. A fully cmap-addressable subset is
                                // swapped for the resolved installed face instead
                                // and keeps the face-metric model.
                                var hasVariantGlyphs = anyVariant;
                                var parserForAdv = parser;
                                var upmForAdv = (double)parser.UnitsPerEm;
                                if (hasVariantGlyphs && upmForAdv > 0)
                                    embeddedAdvMilli = code =>
                                    {
                                        var gid = GidOf(code);
                                        return gid == 0
                                            ? null
                                            : parserForAdv.GetAdvanceWidth(gid) * 1000.0 / upmForAdv;
                                    };
                            }
                        }
                    }
                    catch { cmapChars = null; gidToUnicode = null; glyphMapped = null; }
                }

                // Subset programs often carry no cmap at all; a component character
                // still counts as renderable when the font's own ToUnicode maps some
                // char code to it — that code's glyph is in the subset. (This is what
                // separates an expandable "ti" from one that must collapse: a subset
                // with no single-char 't' mapping has no component glyphs for its
                // t-side ligatures to expand into.)
                if (cmapChars is not null && toUnicodeMap is not null)
                {
                    foreach (var dst in toUnicodeMap.Values)
                        if (dst.Length > 0 && CodePointCount(dst) == 1)
                            cmapChars.Add(char.ConvertToUtf32(dst, 0));
                }
            }

            LigatureSubstitutor? substitutor = null;
            if (cmapChars is not null && (hasMultiDst || (isCid && isIdentity)))
            {
                if (substitutors is not null)
                {
                    var objNum = fontRef is Core.PdfIndirectRef ir ? ir.ObjectNumber : -1;
                    if (objNum >= 0)
                    {
                        if (!substitutors.TryGetValue(objNum, out substitutor))
                            substitutors[objNum] = substitutor = new LigatureSubstitutor();
                    }
                    else substitutor = new LigatureSubstitutor();
                }
                else substitutor = new LigatureSubstitutor();
            }

            Func<byte[], string>? toUnicodeFunc = null;
            if (toUnicodeMap is not null || reverseCmap is not null
                || (isCid && isIdentity && substitutor is not null))
            {
                var map = toUnicodeMap ?? new Dictionary<int, string>();
                var subst = substitutor;
                var cmap = cmapChars;
                var gidUni = gidToUnicode;
                var identity = isIdentity;
                var cidNotGid = c2gStream is not null;
                toUnicodeFunc = (byte[] bytes) => ApplyToUnicode(bytes,
                    map, isCid, reverseCmap, cmap, subst, identity, gidUni, cidNotGid);
            }

            var ascentFactor = 1.0;
            var lineHeightEm = 0.0;
            var ascSfnt = GetEmbeddedTtf(font, reader) ?? GetEmbeddedOpenType(font, reader);
            if (ascSfnt is null && !friendlyFamilies)
                try { ascSfnt = Text.SystemFontResolver.Resolve(baseFont); } catch { }
            if (ascSfnt is not null)
            {
                var wa = SfntWinAscentFactor(ascSfnt);
                if (wa > 0) ascentFactor = wa;
                lineHeightEm = SfntLineHeightFactor(ascSfnt);
            }
            // A bare CFF (Type1C) or Type1 subset carries no sfnt at all, so neither
            // hhea nor OS/2 is reachable; the descriptor's own ascent/descent is the
            // only face metric on hand. A font with no embedded program at all still
            // measures by whatever face the browser substitutes, not by the descriptor.
            if (lineHeightEm <= 0 && HasEmbeddedProgram(font, reader))
                lineHeightEm = DescriptorLineHeightFactor(font, reader);

            // Without a ToUnicode CMap the show bytes still decode through the
            // font's base encoding (MacRoman/WinAnsi/Standard, /Differences with
            // glyph names, embedded-program cmap/post) rather than raw Latin1 —
            // a MacRomanEncoding font's quotes (0xD2/0xD5) otherwise render as
            // Ò/Õ mojibake.
            var fontForDecode = font;
            Func<byte[], string>? baseDecode = toUnicodeFunc is not null
                ? null
                : bytes => Text.TextAbsorber.DecodeStringPublic(bytes, null, fontForDecode, reader);

            // The EMBEDDED program's character coverage: a rendered char the
            // subset cannot map falls to the CSS fallback face, which cuts a
            // span and switches the measuring metrics.
            Func<int, bool>? subsetHas = null;
            var subsetSfnt = GetEmbeddedTtf(font, reader) ?? GetEmbeddedOpenType(font, reader);
            if (subsetSfnt is not null)
            {
                try
                {
                    var subsetParser = new Text.GlyphOutlineParser(subsetSfnt);
                    if (subsetParser.CMap.Count > 0)
                        subsetHas = cp => subsetParser.CMap.TryGetValue(cp, out var gg) && gg != 0;
                }
                catch { /* unparsable program: assume full coverage */ }
            }
            else if (GetEmbeddedType1(font, reader) is { } t1Cov)
            {
                // A Type 1 program serves through the synthesized sfnt whose cmap
                // is the glyph names + the dict's Differences/ToUnicode supplement
                // — the same coverage governs which chars the face can render (a
                // TeX ligature-only subset covers ﬁ but NOT the letters f and i).
                try
                {
                    var t1Src = Text.Type1GlyphSource.TryLoad(t1Cov.Data, t1Cov.Length1, t1Cov.Length2);
                    if (t1Src is not null && t1Src.CMap.Count > 0)
                    {
                        var t1Cmap = new Dictionary<int, int>(t1Src.CMap);
                        var names = RawDifferencesNames(font, reader);
                        var unis = SingleCharToUnicode(font, reader);
                        if (unis is not null)
                            foreach (var (code, uni) in unis)
                            {
                                var gid = names is not null && names.TryGetValue(code, out var nm)
                                    ? t1Src.GidForName(nm) : 0;
                                if (gid > 0) t1Cmap.TryAdd(uni, gid);
                            }
                        subsetHas = cp => t1Cmap.TryGetValue(cp, out var gg) && gg != 0;
                    }
                }
                catch { /* unparsable program: assume full coverage */ }
            }

            // A bare CFF (Type1C) — or an Adobe Type 1 program (FontFile) — is
            // shipped as a TrueType sfnt synthesized from the charstrings, so the
            // served face's advances are the program's own — the same values the
            // font dict's /Widths carries. Handing the browser model those
            // advances leaves each glyph's error as exactly its Tc/Tw and TJ
            // kern contribution, which is what the letter-spacing solves against.
            var advanceMap = BuildAdvanceMap(font, reader, isCid);
            if (embeddedAdvMilli is null && ascSfnt is null && advanceMap is not null
                && (GetEmbeddedBareCff(font, reader) is not null
                    || GetEmbeddedType1(font, reader) is not null))
            {
                var advForCff = advanceMap;
                embeddedAdvMilli = code =>
                {
                    var w = advForCff(code) * 1000.0;   // em fraction -> milli-em
                    return w > 0 ? w : null;
                };
            }

            // By-char program advance for ligature-component measuring: reverse
            // the single-char ToUnicode onto whichever per-code advance source
            // this font serves through.
            if (programCharAdvMilli is null && (programAdvMilli ?? embeddedAdvMilli) is { } advAny
                && SingleCharToUnicode(font, reader) is { } uniAll)
            {
                var u2codeAll = new Dictionary<int, int>();
                foreach (var (c3, u3) in uniAll) u2codeAll.TryAdd(u3, c3);
                programCharAdvMilli = ch =>
                    u2codeAll.TryGetValue(ch, out var cc2) ? advAny(cc2) : null;
            }

            // A substituted CJK font serves the substitute face's subset, so the
            // browser-model metrics are that face's own advances — the same
            // basis the shipped @font-face program carries, which is what the
            // em-compensation solve and the re-import both measure against.
            var substituteFace = false;
            if (embeddedAdvMilli is null && subsetSfnt is null
                && CjkSubstituteFamily(font, reader) is { } subFam3
                && ResolveSubstituteParser(subFam3) is { } subParser
                && SubstituteCodeToUnicode(font, reader) is { } subUniMap)
            {
                substituteFace = true;
                double subUpm = subParser.UnitsPerEm <= 0 ? 1000 : subParser.UnitsPerEm;
                double? AdvOfUni(int u) => subParser.CMap.TryGetValue(u, out var g5) && g5 > 0
                    ? subParser.GetAdvanceWidth(g5) * 1000.0 / subUpm
                    : null;
                var subCode2Uni = subUniMap;
                embeddedAdvMilli = code => subCode2Uni.TryGetValue(code, out var u5) ? AdvOfUni(u5) : null;
                programCharAdvMilli = ch => AdvOfUni(ch);
                // SubsetHas stays null: the built subset covers every glyph the
                // font's own /W or ToUnicode declares, and its contract keys by
                // CODEPOINT while an Identity-H font shows CIDs — a unicode-keyed
                // coverage probe here split runs at effectively random chars.
            }

            result[key] = new FontInfo
            {
                Family = family,
                // stl_ CSS font-family: the standard-14 names keep their generic
                // fallback chain; anything else (embedded/system faces) is the bare
                // friendly family name.
                CssFamily = family != "sans-serif" ? family : FriendlyFontFamily(baseFont),
                Weight = weight,
                Style = style,
                ToUnicode = toUnicodeFunc,
                BaseDecode = baseDecode,
                IsCidFont = isCid,
                IsType3 = fontSubtype == "Type3",
                AscentFactor = ascentFactor,
                LineHeightEm = lineHeightEm,
                AdvanceOf = advanceMap,
                SubsetHas = subsetHas,
                GlyphMapped = glyphMapped,
                EmbeddedAdvMilli = embeddedAdvMilli,
                ProgramAdvMilli = programAdvMilli,
                ProgramCharAdvMilli = programCharAdvMilli,
                SubstituteFace = substituteFace,
            };
        }
        return result;
    }

    /// <summary>OS/2 usWinAscent / head unitsPerEm from an sfnt (TrueType or OTTO),
    /// or 0 when either table is missing/short.</summary>
    private static double SfntWinAscentFactor(byte[] sfnt)
    {
        try
        {
            if (sfnt.Length < 12) return 0;
            int U16(int at) => (sfnt[at] << 8) | sfnt[at + 1];
            var numTables = U16(4);
            int os2 = 0, head = 0;
            for (var t = 0; t < numTables; t++)
            {
                var rec = 12 + t * 16;
                if (rec + 16 > sfnt.Length) return 0;
                var tag = System.Text.Encoding.ASCII.GetString(sfnt, rec, 4);
                var off = (sfnt[rec + 8] << 24) | (sfnt[rec + 9] << 16) | (sfnt[rec + 10] << 8) | sfnt[rec + 11];
                if (tag == "OS/2") os2 = off;
                else if (tag == "head") head = off;
            }
            if (os2 == 0 || head == 0) return 0;
            if (os2 + 76 > sfnt.Length || head + 20 > sfnt.Length) return 0;
            var upm = U16(head + 18);
            var winAscent = U16(os2 + 74);
            return upm > 0 ? winAscent / (double)upm : 0;
        }
        catch { return 0; }
    }

    /// <summary>hhea (ascender + |descender|) / unitsPerEm — the
    /// line-height class value for a font (1.117188 for Arial); 0 when unreadable.</summary>
    private static double SfntLineHeightFactor(byte[] sfnt)
    {
        try
        {
            if (sfnt.Length < 12) return 0;
            int U16(int at) => (sfnt[at] << 8) | sfnt[at + 1];
            int S16(int at) { var v = U16(at); return v >= 0x8000 ? v - 0x10000 : v; }
            var numTables = U16(4);
            int hhea = 0, head = 0, os2 = 0;
            for (var t = 0; t < numTables; t++)
            {
                var rec = 12 + t * 16;
                if (rec + 16 > sfnt.Length) return 0;
                var tag = System.Text.Encoding.ASCII.GetString(sfnt, rec, 4);
                var off = (sfnt[rec + 8] << 24) | (sfnt[rec + 9] << 16) | (sfnt[rec + 10] << 8) | sfnt[rec + 11];
                if (tag == "hhea") hhea = off;
                else if (tag == "head") head = off;
                else if (tag == "OS/2") os2 = off;
            }
            if (head == 0 || head + 20 > sfnt.Length) return 0;
            var upm = U16(head + 18);
            if (upm <= 0) return 0;
            var hheaLh = 0.0;
            if (hhea != 0 && hhea + 8 <= sfnt.Length)
            {
                var ascender = S16(hhea + 4);
                var descender = S16(hhea + 6);
                hheaLh = (ascender + Math.Abs(descender)) / (double)upm;
            }
            // A subset whose hhea is the degenerate 1-em placeholder (1536/-512
            // at 2048) still carries the real face metrics in OS/2
            // usWinAscent/usWinDescent; a live hhea stays authoritative.
            if (hheaLh != 0.0 && hheaLh != 1.0) return hheaLh;
            if (os2 > 0 && os2 + 78 <= sfnt.Length)
            {
                var winA = U16(os2 + 74);
                var winD = U16(os2 + 76);
                if (winA + winD > 0) return (winA + winD) / (double)upm;
            }
            return hheaLh;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Apply a ToUnicode CMap to raw string bytes.
    /// For CID fonts (Type0), character codes are 2 bytes each.
    /// For simple fonts, character codes are 1 byte each.
    /// </summary>
    private static string ApplyToUnicode(byte[] bytes, Dictionary<int, string> map, bool isCid,
        Dictionary<int, int>? reverseCmap = null, HashSet<int>? cmapChars = null,
        LigatureSubstitutor? substitutor = null, bool isIdentity = false,
        Dictionary<int, int>? gidToUnicode = null, bool cidCodeIsNotGid = false)
    {
        var sb = new StringBuilder();

        if (isCid)
        {
            // 2-byte character codes
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                // A reverse font-cmap entry (gid → unicode) outranks /ToUnicode when
                // the caller asked for cmap priority (see ResolveFonts).
                if (reverseCmap is not null && reverseCmap.TryGetValue(code, out var cmapCh))
                    sb.Append(char.ConvertFromUtf32(cmapCh));
                else if (map.TryGetValue(code, out var unicode))
                    sb.Append(MapDst(code, unicode, cmapChars, substitutor));
                else if (isIdentity && gidToUnicode is not null
                         && gidToUnicode.TryGetValue(code, out var uniCh))
                    sb.Append(char.ConvertFromUtf32(uniCh));
                else if (isIdentity && cidCodeIsNotGid)
                    // A CIDToGIDMap STREAM marks the code as a true CID, not a bare
                    // glyph id — nothing to mint. Fall back to the CID
                    // as a raw character (producers commonly assign CID = Unicode).
                    sb.Append((char)code);
                else if (isIdentity && substitutor is not null)
                    sb.Append(substitutor.Mint(code));
                else
                    sb.Append('?');
            }
        }
        else
        {
            // 1-byte character codes
            foreach (var b in bytes)
            {
                if (map.TryGetValue(b, out var unicode))
                    sb.Append(MapDst(b, unicode, cmapChars, substitutor));
                else
                    sb.Append((char)b);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The output text for one char code's ToUnicode sequence. Multi-char sequences
    /// stay expanded when the font's cmap can render every component character;
    /// otherwise the sequence collapses to a single stand-in registered with the
    /// font's substitutor (see <see cref="LigatureSubstitutor"/>).
    /// </summary>
    private static string MapDst(int code, string dst, HashSet<int>? cmapChars,
        LigatureSubstitutor? substitutor)
    {
        if (CodePointCount(dst) <= 1)
        {
            // A single-char dst naming an UNASSIGNED Unicode code point (category
            // Cn — e.g. a custom-encoded font whose identity ToUnicode lands raw
            // char codes in the U+FFDD / U+FFF0–FFF8 reserved gaps) is not real
            // text: each such char CODE is replaced with a
            // fresh minted character (U+A880 upward, in first-use order across the
            // conversion) so distinct glyphs keep distinct text. Assigned chars —
            // including private-use — pass through untouched.
            if (substitutor is not null && dst.Length == 1
                && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(dst[0])
                    == System.Globalization.UnicodeCategory.OtherNotAssigned)
                return substitutor.Mint(code);
            return dst;
        }
        if (dst == SpaceLigature) return " ";
        if (cmapChars is null || substitutor is null || AllInCmap(dst, cmapChars)) return dst;
        return substitutor.Register(code, StandardLigatureChar(dst));
    }

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

    /// <summary>The human-readable CSS family for a /BaseFont name: subset prefix
    /// ("ABCDEF+") and style suffix (the first "-"/"," segment and any trailing
    /// Bold/Italic/Oblique words) stripped, then glued camel-case words re-spaced —
    /// "CenturyGothic" → "Century Gothic", "Calibri-Bold" → "Calibri",
    /// "TimesNewRomanPSMT" → "Times New Roman".</summary>
    internal static string FriendlyFontFamily(string baseFont)
    {
        var name = baseFont;
        if (name.Length > 7 && name[6] == '+') name = name[7..];
        var cut = name.IndexOfAny(new[] { '-', ',' });
        if (cut > 0) name = name[..cut];
        // PostScript naming tails that are not part of the family.
        foreach (var tail in new[] { "PSMT", "PS", "MT" })
            if (name.Length > tail.Length && name.EndsWith(tail, StringComparison.Ordinal))
            { name = name[..^tail.Length]; break; }
        foreach (var styleWord in new[] { "BoldItalic", "BoldOblique", "Bold", "Italic", "Oblique" })
            if (name.Length > styleWord.Length && name.EndsWith(styleWord, StringComparison.Ordinal))
            { name = name[..^styleWord.Length]; break; }
        if (name.Length == 0) return "sans-serif";
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])
                && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
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
