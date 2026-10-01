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
        var svgDoc = BuildSvgDocument(xd.svgPaths.ToString(),
            xd.page.Width, xd.page.Height, ++xd.imageSink.SvgBodyCounter, xd.inlineSvg,
            xd.inlineSvg ? xd.namer.Cls("04") : null);
        if (xd.inlineSvg)
        {
            // A fully self-contained save carries the page graphics as INLINE SVG
            // markup: a base64 <object> would hide the vector content from anything
            // reading the HTML, and there is no sidecar to reference. The element
            // takes the positioning class and the explicit page size the <object>
            // carried, so re-importing the markup still lays it out as the page's
            // backdrop rather than as a default-sized inline image.
            xd.sb.AppendLine($"<div class=\"{xd.namer.Cls("03")}\">{svgDoc}</div>");
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
            xd.sb.AppendLine($"<div class=\"{xd.namer.Cls("03")}\"><object data=\"{svgUrl}\" " +
                $"type=\"image/svg+xml\" class=\"{xd.namer.Cls("04")}\">" +
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
        // A save asking for every part inside the one HTML file keeps its text
        // as the spans in whatever letter-positioning dialect: its background is
        // text-free too, or a round trip through that file draws every glyph twice.
        var textFreeBg = xd.embedResources || emGridBg
            || xd.options?.PartsEmbeddingMode == HtmlSaveOptions.PartsEmbeddingModes.EmbedAllIntoHtml;
        if (xd.embedResources || emGridBg)
        {
            // Untouched ImageResolution frames the self-contained background at
            // CSS pixels (96 dpi) — the data-URI page raster comes out
            // 793×1121 for a 595.5×841.9 page.
            var dpi = xd.options?.ImageResolution is > 0 and var res ? (int)res : 96;
            var pw = (int)System.Math.Round(System.Math.Floor(xd.page.Width) * dpi / 72.0);
            var ph = (int)System.Math.Round(System.Math.Floor(xd.page.Height) * dpi / 72.0);
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
            if (textFreeBg)
            {
                try
                {
                    Aspose.Pdf.Devices.PageRenderFlags.SuppressText = true;
                    device.Process(xd.page, ms);
                }
                finally { Aspose.Pdf.Devices.PageRenderFlags.SuppressText = false; }
            }
            else
            {
                device.Process(xd.page, ms);
            }
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
        xd.sb.AppendLine($"<div class=\"{xd.namer.Cls("03")}\"><img src=\"{url}\" " +
            $"class=\"{xd.namer.Cls("04")}\" style=\"width:100%;height:100%;\" /></div>");
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
        xd.pageResources = xd.reader.ResolveDict(xd.page.Dict.Get("Resources"));
        xd.imageSink.CurrentPdfPage = xd.i;

        xd.pageTurnedOver = xd.page.Rotate == Rotation.on180;
        xd.textBuf = new StringBuilder();
        xd.svgPaths = new StringBuilder();
        xd.destAnchors = DestAnchorsFor(xd.doc);
        xd.linkTargets = CollectLinkTargets(xd.page.Dict, xd.reader, xd.doc, xd.destAnchors);
        xd.mb = xd.page.MediaBox;
        xd.zCounter = xd.options?.UseZOrder == true ? new ZCounter() : null;
        xd.content = ConcatContentStreams(xd.page.Dict, xd.reader);
        xd.pageHasPaint = HasVectorPaintOps(xd.content);
        xd.emitPngBackground = xd.pngBackground && xd.pageHasPaint;
        xd.hasBackdrop = xd.emitPngBackground || xd.pageHasPaint;
        xd.styleReg.EnsureBase(xd.hasBackdrop ? 7 : 5);
        RenderContentToHtml(xd.content, xd.fonts, xd.imageXObjects, xd.reader, xd.textBuf,
            xd.page.Height, xd.page.Width,
            saveTransparentTexts: xd.options?.SaveTransparentTexts == true,
            emCompensation: xd.options?.LettersPositioningMethod
                == HtmlSaveOptions.LettersPositioningMethods.UseEmUnitsAndCompensationOfRoundingErrorsInCss,
            textOnly: xd.pngBackground,
            externalSvgPaths: xd.pngBackground ? null : xd.svgPaths,
            imageSink: xd.pngBackground ? null : xd.imageSink,
            styleReg: xd.styleReg, classNamer: xd.namer, linkTargets: xd.linkTargets,
            resources: xd.pageResources, preferFontCmap: xd.preferFontCmap,
            substitutors: _substitutors,
            cssTextDecorations: xd.options?.TrySaveTextUnderliningAndStrikeoutingInCss == true,
            pageLLX: xd.mb.LLX, yTopRef: xd.mb.LLY + Math.Floor(xd.mb.URY - xd.mb.LLY),
            zCounter: xd.zCounter,
            defaultFontName: xd.effectiveDefaultFont, authoredPathShape: xd.inlineSvg,
            ocLayers: xd.options?.ConvertMarkedContentToLayers == true
                ? BuildOcLayerMap(xd.pageResources, xd.reader) : null,
            pageTurnedOver: xd.pageTurnedOver);
    }
}
