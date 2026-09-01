using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
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
