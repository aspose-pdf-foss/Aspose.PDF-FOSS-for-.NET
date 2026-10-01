using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the gridster dashboard render: the local helpers and one widget at a time.
    private static double GdPx(double v)
    => v * GridsterPxPt;

    /// <summary>The stages of the gridster dashboard render: the local helpers and one widget at a time.</summary>
    private static void RenderGridsterWidget(GridsterDashboardState gd, Aspose.Pdf.Converters.HtmlToPdfConverter.GridsterItem w)
    {
        var boxX = gd.originX + GdPx(w.LeftPx);
        var boxTop = gd.originY + GdPx(w.TopPx);
        var boxW = GdPx(w.WidthPx);

        var controls = ParseGridsterItems(w.Html);
        controls.RemoveAll(c => c.IsWidget);

        // The box hugs its content: caption band + the deepest control row +
        // the body's bottom padding.
        double deepestPx = 0;
        foreach (var c in controls) deepestPx = System.Math.Max(deepestPx, c.TopPx + c.HeightPx);
        var boxH = GdPx(GridsterCaptionBandPx + deepestPx + GridsterBodyPadBottomPx);

        // Border box (1 px, rgb(28,42,67)), stroked on its centre line.
        var bw = GdPx(GridsterBoxBorderPx);
        gd.sb.Append(Compat.Format(gd.inv,
            $"q {28 / 255.0:0.###} {42 / 255.0:0.###} {67 / 255.0:0.###} RG {bw:0.##} w " +
            $"{boxX + bw / 2:F2} {gd.pageHeight - boxTop - bw / 2:F2} " +
            $"{boxW - bw:F2} {-(boxH - bw):F2} re S Q\n"));

        // Caption: 18 px Arial Bold, 5 px in from the box, on the band.
        var caption = FirstClassText(w.Html, "pdf-widget-name");
        if (caption.Length > 0)
        {
            var capSize = GdPx(GridsterCaptionPx);
            var capX = boxX + GdPx(GridsterBoxBorderPx + 5.0);
            var capBase = boxTop + GdPx(GridsterBoxBorderPx + 5.0) + capSize;
            EmitGridsterText(gd.page, gd.resByFace, capSize, capX, gd.pageHeight - capBase,
                caption, "Arial,Bold");
        }

        var bodyTop = boxTop + GdPx(GridsterCaptionBandPx);
        foreach (var c in controls)
        {
            var cx = boxX + GdPx(GridsterBoxBorderPx + c.LeftPx);
            var cTop = bodyTop + GdPx(c.TopPx);
            var cW = GdPx(c.WidthPx);

            var label = FirstClassText(c.Html, "pdf-field-label");
            var value = FirstClassText(c.Html, "pdf-label-control");

            // The red control box fills the value half of the control.
            var valX = cx + GdPx(GridsterLabelColPx);
            var valW = cW - GdPx(GridsterLabelColPx);
            if (valW > 0)
                gd.sb.Append(Compat.Format(gd.inv,
                    $"q 1 0 0 rg {valX:F2} {gd.pageHeight - cTop - GdPx(c.HeightPx):F2} " +
                    $"{valW:F2} {GdPx(c.HeightPx):F2} re f Q\n"));

            // Label: 16 px Arial, its baseline at the row's own 3 px pad + ascent.
            var labSize = GdPx(GridsterLabelPx);
            var labBase = cTop + GdPx(3.0) + labSize * ArialAscentEm;
            if (label.Length > 0)
                EmitGridsterText(gd.page, gd.resByFace, labSize, cx, gd.pageHeight - labBase,
                    label, "Arial");

            // Value: 10.5 pt Arial, RIGHT-aligned inside the red box (probed:
            // every value's right edge lands on the box's right inset).
            if (value.Length > 0 && valW > 0)
            {
                var vw = MeasureFaceText("Arial", value, GridsterValuePt);
                var vx = valX + valW - vw;
                EmitGridsterText(gd.page, gd.resByFace, GridsterValuePt, vx,
                    gd.pageHeight - (labBase + GridsterValueDropPt), value, "Arial");
            }
        }
    }
}
