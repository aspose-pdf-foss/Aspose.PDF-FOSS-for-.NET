using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the RTL SVG diagram layout: the text draw, the figure and one legend entry.

    /// <summary>x</summary>
    private static void PlaceRtlSvgFigure(RtlSvgDiagramState rd)
    {
        // The figure keeps its viewBox aspect at the styled height and
        // centers in the canvas (letterboxed, not stretched).
        var (figBytes, figNatW, figNatH) = ImageRasterizer.RasterizeSvgWithSize(rd.inlineSvgs[rd.dg.MainSvgIdx]);
        if (figBytes is not null)
        {
            var drawW = figNatW > 0 && figNatH > 0 ? rd.figH * figNatW / figNatH : rd.dg.MainSvgWPx * PxPt;
            var figX = rd.canvasLeft + (rd.canvasW - drawW) / 2 - 10.3 * PxPt;
            try
            {
                rd.cv.flow.page.AddImage(figBytes, new Rectangle(figX, rd.cv.flow.y - rd.figH, figX + drawW, rd.cv.flow.y));
            }
            catch { }
        }
    }

    /// <summary>x</summary>
    private static void DrawRtlSvgLegendEntry(RtlSvgDiagramState rd, int k)
    {
        var (svgIdx, label) = rd.dg.Legend[k];
        var boxLeft = rd.canvasLeft + rd.dg.LegendXFrac[k] * rd.canvasW;
        var boxW = rd.dg.LegendWFrac[k] * rd.canvasW;
        if (svgIdx >= 0 && svgIdx < rd.inlineSvgs.Count && boxLeft + boxW > 0)
        {
            var sw = ImageRasterizer.RasterizeSvg(rd.inlineSvgs[svgIdx]);
            if (sw is not null)
                try
                {
                    rd.cv.flow.page.AddImage(sw, new Rectangle(boxLeft, rd.cv.flow.y - rd.legendBoxH,
                        boxLeft + boxW, rd.cv.flow.y));
                }
                catch { }
        }
        if (label.Length > 0)
            DrawRtlText(rd, label, rd.canvasLeft + rd.dg.LegendLabelRightFrac[k] * rd.canvasW,
                rd.cv.flow.y - rd.legendBoxH - 16 * PxPt, rd.dg.LabelFontPx * PxPt);
    }

    private static void DrawRtlText(RtlSvgDiagramState rd, string text, double rightX, double baseline, double fontPt, bool centerCanvas = false)
    {
        if (rd.fontDictD is null || rd.arialD.ttf is null || text.Length == 0) return;
        var visual = IsPureRtl(text) ? ToVisualRtl(text)
            : Text.BidiReorderer.ContainsRtl(text) ? VisualizeMixedRtl(text) : text;
        var tw = MeasureFaceText("Arial", visual, fontPt);
        var tx = centerCanvas ? (rd.canvasLeft + rd.canvasRight - tw) / 2 : rightX - tw;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(rd.fontDictD, rd.arialD.ttf, "Arial",
            visual, stripSpacesInBaseFont: true);
        var t = new StringBuilder();
        t.Append("BT 0 0 0 rg ");
        t.Append($"/{rn} {fontPt.ToString("F1", rd.invd)} Tf ");
        t.Append($"1 0 0 1 {tx.ToString("F2", rd.invd)} {baseline.ToString("F2", rd.invd)} Tm ");
        t.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ET ");
        rd.cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(t.ToString()));
    }
}
