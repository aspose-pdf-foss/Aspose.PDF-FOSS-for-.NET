using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>Pdf to html: the page's vector backdrop emitted as an svg document.</summary>
    private void EmitPageSvgBackdrop(ExternalDivState xd)
    {
        // A trimmed page's SVG keeps the page's coordinates and is cut at the band's bottom; the
        // holder lifts it by the band's top.
        var svgDoc = BuildSvgDocument(xd.svgPaths.ToString(),
            xd.pageWidth, xd.pageHeight, ++xd.imageSink.SvgBodyCounter, xd.inlineSvg,
            xd.inlineSvg ? xd.namer.Cls("04") : null, xd.mb.LLX, xd.mb.LLY,
            visibleHeightPt: xd.band is { } cut ? cut.BottomEm * 12.0 : double.NaN,
            quarterTurn: xd.page.RotationQuarter is 90 or 270 ? xd.page.RotationQuarter : 0,
            frameWidthPt: Math.Floor(xd.mb.Width), frameHeightPt: Math.Floor(xd.mb.Height));
        var holder = HolderStyleAttr(xd);
        if (xd.inlineSvg)
        {
            // A fully self-contained save carries the page graphics as INLINE SVG
            // markup: a base64 <object> would hide the vector content from anything
            // reading the HTML, and there is no sidecar to reference. The element
            // takes the positioning class and the explicit page size the <object>
            // carried, so re-importing the markup still lays it out as the page's
            // backdrop rather than as a default-sized inline image.
            xd.sb.AppendLine($"<div class=\"{xd.namer.Cls("03")}\"{holder}>{svgDoc}</div>");
        }
        else
        {
            var svgName = $"img_{++xd.imageSink.Counter:00}.svg";
            var svgUrl = Ref(xd.imagesUrl, svgName);
            xd.sidecars.Add(new SidecarFile
            {
                Name = svgName,
                Content = Encoding.UTF8.GetBytes(svgDoc),
                IsImage = true,
            });
            // The object of a trimmed page states the size its cut document shows at.
            var objectStyle = xd.band is { } band
                ? $" style=\"position:absolute; width:{EmG7(Math.Floor(xd.pageWidth) / 12.0)}; height:{Em4T(band.BottomEm)}em;\""
                : "";
            xd.sb.AppendLine($"<div class=\"{xd.namer.Cls("03")}\"{holder}><object data=\"{svgUrl}\" " +
                $"type=\"image/svg+xml\" class=\"{xd.namer.Cls("04")}\"{objectStyle}>" +
                $"<embed src=\"{svgUrl}\" type=\"image/svg+xml\" /></object></div>");
        }
    }

    /// <summary>Pdf to html: the page's graphics flattened to one background png.</summary>
    private void EmitPagePngBackground(ExternalDivState xd)
    {
        // The page's full graphics flattened to one background PNG. The caller's
        // resource strategy (split saves) may take over writing it and supply the
        // URL; otherwise it becomes a sidecar file with the default name — or,
        // for a fully self-contained save (EmbedAllIntoHtml), a base64 data URI
        // rendered at ImageResolution with the truncated page box as the pixel
        // frame (595.5pt → 595pt → 793px at 96dpi).
        var pngName = $"img_{++xd.imageSink.Counter:00}.png";
        byte[] png;
        Aspose.Pdf.Devices.PngDevice device;
        // The em-compensation dialect's background is IMAGES-ONLY at CSS
        // pixels in the sidecar save too, not just the self-contained one —
        // the sidecar raster is a text-free 793×1123 page image. A
        // text-carrying backdrop under the (substitute-basis) text layer
        // double-strikes every glyph at slightly different metrics.
        var emGridBg = xd.options?.LettersPositioningMethod
            == HtmlSaveOptions.LettersPositioningMethods.UseEmUnitsAndCompensationOfRoundingErrorsInCss;
        if (xd.embedResources || emGridBg)
        {
            // Untouched ImageResolution frames the self-contained background at
            // CSS pixels (96 dpi) — the data-URI page raster comes out
            // 793×1121 for a 595.5×841.9 page.
            var dpi = xd.options?.ImageResolution is > 0 and var res ? res : 96;
            var pw = (int)System.Math.Round(System.Math.Floor(xd.pageWidth) * dpi / 72.0);
            var ph = (int)System.Math.Round(System.Math.Floor(xd.pageHeight) * dpi / 72.0);
            device = new Aspose.Pdf.Devices.PngDevice(pw, ph, new Aspose.Pdf.Devices.Resolution(dpi));
        }
        else
        {
            device = new Aspose.Pdf.Devices.PngDevice(new Aspose.Pdf.Devices.Resolution(150));
        }
        using (var ms = new System.IO.MemoryStream())
        {
            // The background raster carries the page GRAPHICS only, embedded or
            // saved beside the HTML: the text lives on as the visible HTML spans, so
            // text ink in the PNG would draw every glyph twice, a hair apart.
            try
            {
#pragma warning disable S2696 // [ThreadStatic] flag, set and reset around the call
                Aspose.Pdf.Devices.PageRenderFlags.SuppressText = true;
#pragma warning restore S2696
                device.Process(xd.page, ms);
            }
            finally { Aspose.Pdf.Devices.PageRenderFlags.SuppressText = false; }
            png = ms.ToArray();
        }
        WritePngIntermediate(xd.options?.PngIntermediateFileIfAny, xd.page, xd.htmlPageNumber);
        string url;
        if (xd.embedResources)
        {
            url = "data:image/png;base64," + System.Convert.ToBase64String(png);
        }
        else
        {
            var strategyUrl = xd.dispatchPngBackground
                ? DispatchImageResourceCallback(xd.options, png, pngName, xd.i, xd.htmlPageNumber)
                : null;
            if (strategyUrl is null)
            {
                xd.sidecars.Add(new SidecarFile { Name = pngName, Content = png, IsImage = true });
                url = Ref(xd.imagesUrl, pngName);
            }
            else
            {
                url = EscapeHrefAmpersands(strategyUrl);
            }
        }
        xd.sb.AppendLine($"<div class=\"{xd.namer.Cls("03")}\"{HolderStyleAttr(xd)}><img src=\"{url}\" " +
            $"class=\"{xd.namer.Cls("04")}\" style=\"width:100%;\" /></div>");
    }

    /// <summary>Whether any page after the one being rendered draws vector paint, so needs
    /// the backdrop layout. Reads the pages in order and stops at the first that does.</summary>
    private static bool LaterPageHasPaint(ExternalDivState xd)
    {
        for (var i = xd.i + 1; i <= xd.doc.PageCount; i++)
        {
            var page = xd.doc.Pages[i];
            if (HasVectorPaintOps(ConcatContentStreams(page.Dict, page.Reader)))
                return true;
        }
        return false;
    }

    /// <summary>Pdf to html: the page's fonts resolved and its content rendered into the text buffer.</summary>
    private void RenderPageTextLayer(ExternalDivState xd)
    {
        xd.preferFontCmap = xd.options?.FontEncodingStrategy
            == HtmlSaveOptions.FontEncodingRules.DecreaseToUnicodePriorityLevel;
        xd.fontsNotSaved = xd.options?.FontSavingMode == HtmlSaveOptions.FontSavingModes.DontSave;
        xd.effectiveDefaultFont = xd.fontsNotSaved ? null : xd.options?.DefaultFontName;
        xd.fonts = ResolveFonts(xd.page.Dict, xd.reader,
            preferFontCmap: xd.preferFontCmap,
            substitutors: _substitutors,
            defaultFontName: xd.effectiveDefaultFont,
            friendlyFamilies: xd.fontsNotSaved);
        xd.imageXObjects = ResolveImageXObjects(xd.page.Dict, xd.reader);
        xd.pageResources = PageResources(xd.page.Dict, xd.reader);
        (xd.mb, xd.pageWidth, xd.pageHeight) = VisiblePageBox(xd.page);
        xd.model = new TextLayerModel();
        // A line drawn wholly outside the box the page shows is not on the page. The box
        // is laid out in the unrotated grid, so only an unrotated page is measured.
        if (xd.page.RotationQuarter == 0) xd.model.PageExtentEm = (xd.pageWidth / 12.0, xd.pageHeight / 12.0);
        xd.imageSink.CurrentPdfPage = xd.i;

        xd.pageTurnedOver = xd.page.Rotate == Rotation.on180;
        xd.textBuf = new StringBuilder();
        xd.svgPaths = new StringBuilder();
        xd.destAnchors = DestAnchorsFor(xd.doc);
        xd.destAnchors.CurrentPage = xd.i;
        xd.linkTargets = CollectLinkTargets(xd.page.Dict, xd.reader, xd.doc, xd.destAnchors);
        xd.zCounter = xd.options?.UseZOrder == true ? new ZCounter() : null;
        xd.content = ConcatContentStreams(xd.page.Dict, xd.reader);
        // The form's widgets show through their appearances like the rest of the page; their
        // boxes and borders are paint, so a page carrying any counts as painted.
        var widgets = WidgetLayer(xd.page.Dict, xd.reader);
        xd.pageHasPaint = HasVectorPaintOps(xd.content) || widgets is not null;
        xd.emitPngBackground = xd.pngBackground && xd.pageHasPaint;
        xd.hasBackdrop = xd.emitPngBackground || xd.pageHasPaint;
        // The class layout is pinned by the first page and shared by the whole
        // document, so a paint-free first page must not pin the layout without a
        // backdrop when a later page needs one: that page's wrapper and text layer
        // would reuse the numbers the stylesheet gave to the first fonts.
        if (!xd.hasBackdrop && !xd.styleReg.BasePinned && LaterPageHasPaint(xd))
            xd.hasBackdrop = true;
        xd.styleReg.EnsureBase(xd.hasBackdrop ? 7 : 5);
        void Render(byte[] content, PdfDictionary? resources) =>
            RenderContentToHtml(content, xd.fonts, xd.imageXObjects, xd.reader, xd.textBuf,
                xd.pageHeight, xd.pageWidth,
                saveTransparentTexts: xd.options?.SaveTransparentTexts == true,
                emCompensation: xd.options?.LettersPositioningMethod
                    == HtmlSaveOptions.LettersPositioningMethods.UseEmUnitsAndCompensationOfRoundingErrorsInCss,
                textOnly: xd.pngBackground,
                externalSvgPaths: xd.pngBackground ? null : xd.svgPaths,
                imageSink: xd.pngBackground ? null : xd.imageSink,
                styleReg: xd.styleReg, classNamer: xd.namer, linkTargets: xd.linkTargets,
                resources: resources, preferFontCmap: xd.preferFontCmap,
                substitutors: _substitutors,
                cssTextDecorations: xd.options?.TrySaveTextUnderliningAndStrikeoutingInCss == true,
                pageLLX: xd.mb.LLX, yTopRef: xd.mb.LLY + Math.Floor(xd.mb.URY - xd.mb.LLY),
                zCounter: xd.zCounter,
                defaultFontName: xd.effectiveDefaultFont, authoredPathShape: xd.inlineSvg,
                ocLayers: xd.options?.ConvertMarkedContentToLayers == true
                    ? BuildOcLayerMap(xd.pageResources, xd.reader) : null,
                pageTurnedOver: xd.pageTurnedOver,
                perGlyphDivs: xd.options?.PreventGlyphsGrouping == true,
                model: xd.model,
                frameWidth: xd.page.RotationQuarter is 90 or 270 ? xd.pageHeight : xd.pageWidth);
        Render(xd.content, xd.pageResources);
        if (widgets is not null) Render(widgets.Value.Content, widgets.Value.Resources);
    }

    /// <summary>The box a page shows, as a viewer presents it: the crop box inside the media
    /// box, with the width and height the page has once a quarter turn is applied. Everything
    /// the export lays out - the page size, the text grid, the background - is measured
    /// from this box, so what lies beyond it is off the page.</summary>
    private static (Rectangle Box, double Width, double Height) VisiblePageBox(Page page)
    {
        var box = Devices.SoftwarePageRenderer.EffectiveCropRect(page);
        var quarterTurned = page.RotationQuarter is 90 or 270;
        return (box, quarterTurned ? box.Height : box.Width, quarterTurned ? box.Width : box.Height);
    }
}
