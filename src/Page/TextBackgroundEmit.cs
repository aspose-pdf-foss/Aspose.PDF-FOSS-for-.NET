using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
// Text backgrounds: the background rectangle emitter.
    // Draw one highlight box. The caller supplies the PAGE-space anchor,
    // width and box height plus the metric height (raw-Tf units, which is
    // what a local frame measures in); the framing decides where the
    // numbers actually land.
    private static void EmitBg(BgFragmentState fb, Aspose.Pdf.Color color, double pageX, double pageY,
        double pageW, double metricH, double pageH)
    {
        fb.builder.SaveState();
        if (fb.quarterTurn)
        {
            // The box in the CONTENT STREAM's own device space.
            var (qx1, qy1) = fb.ctm!.InverseTransformPoint(pageX, pageY);
            var (qx2, qy2) = fb.ctm.InverseTransformPoint(pageX + pageW, pageY + pageH);
            fb.builder.SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
            fb.builder.Rectangle(Math.Min(qx1, qx2), Math.Min(qy1, qy2),
                Math.Abs(qx2 - qx1), Math.Abs(qy2 - qy1));
        }
        else if (fb.frame is not null)
        {
            // Replay the run's frame around the rectangle and write the rect
            // in that local space. cm FIRST, colour second, so the cm stays
            // immediately before the rectangle operands.
            var (lx, ly) = fb.frame.InverseTransformPoint(pageX, pageY);
            if (fb.frame.D < 0) ly -= metricH;
            fb.builder.SetMatrix(fb.frame.A, fb.frame.B, fb.frame.C, fb.frame.D, fb.frame.E, fb.frame.F);
            fb.builder.SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
            fb.builder.Rectangle(lx, ly, pageW / fb.frame.A, metricH);
        }
        else
        {
            fb.builder.SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
            fb.builder.Rectangle(pageX, pageY, pageW, metricH);
        }
        fb.builder.Fill();
        fb.builder.RestoreState();
    }
}
