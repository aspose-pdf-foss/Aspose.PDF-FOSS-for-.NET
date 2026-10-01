using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>Render one page's <c>page_N</c> container (background graphics +
    /// stl_view text layer) into <paramref name="sb"/>, appending any page graphics
    /// files to the shared sidecar list.</summary>
    private void RenderPageExternalDiv(Document doc, int i, StringBuilder sb,
        ClassNamer namer, StyleRegistry styleReg, ExternalImageSink imageSink,
        List<SidecarFile> sidecars, string imagesUrl, bool pngBackground,
        int htmlPageNumber, HtmlSaveOptions? options, bool dispatchPngBackground,
        bool embedResources = false, bool inlineSvg = false)
    {
        var xd = new ExternalDivState();
        xd.doc = doc;
        xd.i = i;
        xd.sb = sb;
        xd.namer = namer;
        xd.styleReg = styleReg;
        xd.imageSink = imageSink;
        xd.sidecars = sidecars;
        xd.imagesUrl = imagesUrl;
        xd.pngBackground = pngBackground;
        xd.htmlPageNumber = htmlPageNumber;
        xd.options = options;
        xd.dispatchPngBackground = dispatchPngBackground;
        xd.embedResources = embedResources;
        xd.inlineSvg = inlineSvg;
        xd.page = xd.doc.Pages[xd.i];
        xd.reader = xd.page.Reader;
        RenderPageTextLayer(xd);

        // page_N container -> optional SVG background -> stl_view/stl_05/stl_06 text layer.
        // No inline style: the page box (width/height/margin/border) lives in the
        // structural stl_02 CSS class, and tests match the exact div markup.
        xd.sb.AppendLine($"<div id=\"page_{xd.i - 1}\" class=\"{xd.namer.PageCls()}\">");

        if (xd.emitPngBackground)
        {
            EmitPagePngBackground(xd);
        }
        else if (xd.svgPaths.Length > 0)
        {
            EmitPageSvgBackdrop(xd);
        }

        xd.layerCls = xd.hasBackdrop
            ? $"{xd.namer.Cls("05")} {xd.namer.Cls("06")}"
            : $"{xd.namer.Cls("03")} {xd.namer.Cls("04")}";
        xd.sb.AppendLine($"<div class=\"{xd.namer.Cls("view")}\"><div class=\"{xd.layerCls}\">");
        if (xd.pageTurnedOver)
            xd.sb.Append($"<div class=\"{xd.namer.Cls(xd.styleReg.PageRotation(180))}\">");
        xd.sb.Append(ReorderStlLineDivs(xd.textBuf.ToString(), xd.namer.Cls("01")));
        if (xd.pageTurnedOver) xd.sb.Append("</div>");
        // Internal-link destinations into THIS page materialize as positioned,
        // named anchors at the end of the text layer — the "#page_index" hrefs
        // land on them.
        if (xd.destAnchors.PageDests.TryGetValue(xd.i, out var pageDests))
        {
            var yTop = xd.mb.LLY + Math.Floor(xd.mb.URY - xd.mb.LLY);
            for (var di = 0; di < pageDests.Count; di++)
            {
                var (dx, dy) = pageDests[di];
                // The anchor sits a 10pt lead above the destination point so a
                // scrolled-to target line stays fully visible.
                xd.sb.AppendLine($"<a name=\"{xd.i}_{di}\" style=\"position:absolute;" +
                    $"left:{Em4T((dx - xd.mb.LLX) / 12.0)}em;top:{Em4T((yTop - dy - 10.0) / 12.0)}em;\">&nbsp;</a>");
            }
        }
        xd.sb.AppendLine("</div></div>");
        // Links whose rect covered no text still need a click surface: the
        // class-less overlay div goes after the text layer, as the page div's
        // last children.
        // The z-ordered variant never emits overlays — text runs under a link
        // rect already carry inline anchors, so no overlay is needed there.
        if (xd.options?.UseZOrder != true)
            EmitGrlinkOverlays(xd.linkTargets, xd.sb, xd.page.Height, xd.namer);
        xd.sb.AppendLine("</div>");
    }
}
