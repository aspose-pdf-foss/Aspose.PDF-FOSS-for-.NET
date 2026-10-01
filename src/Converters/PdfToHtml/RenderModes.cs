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

}
