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
    // width and box height - where the reader reports the text, in the frame
    // it was read in - plus the metric height (raw-Tf units, which is
    // what a local frame measures in); the framing decides where the
    // numbers actually land. A band measured back through the run's matrix
    // is drawn by EmitRunSpaceBox instead.
    private static void EmitBg(BgFragmentState fb, Aspose.Pdf.Color color, double pageX, double pageY,
        double pageW, double metricH, double pageH)
    {
        SaveDecorationState(fb.builder, BoxMatrix(fb));
        if (fb.quarterTurn)
        {
            // The box in the CONTENT STREAM's own device space.
#pragma warning disable S8969 // the compiler reports a possible null without it (CS8602/CS8603)
            var (qx1, qy1) = fb.ctm!.InverseTransformPoint(pageX, pageY);
#pragma warning restore S8969
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
        else if (fb.unrotate is { } unrotate)
        {
            // Placed in the turned view the text was read in: laid back in the page's own space.
            var box = unrotate.Transform(new Rectangle(pageX, pageY, pageX + pageW, pageY + metricH));
            fb.builder.SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
            fb.builder.Rectangle(box.LLX, box.LLY, box.Width, box.Height);
        }
        else
        {
            fb.builder.SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
            fb.builder.Rectangle(pageX, pageY, pageW, metricH);
        }
        fb.builder.Fill();
        fb.builder.RestoreState();
    }

    /// <summary>The matrix a box at the reader's numbers opens with, by the space the framing writes them in:
    /// taken back through the run's matrix by the quarter turn, the run's; laid back from a turned view, the
    /// page's; as they are, or under the run's own frame, the frame the text was read in.</summary>
    private static Matrix? BoxMatrix(BgFragmentState fb)
    {
        if (fb.quarterTurn) return fb.inRunSpace;
        return fb.frame is null && fb.unrotate is not null ? fb.inPageSpace : fb.inReadFrame;
    }

    /// <summary>Draw one highlight box whose numbers were measured back through the run's matrix, in the run's space.</summary>
    private static void EmitRunSpaceBox(BgFragmentState fb, Aspose.Pdf.Color color, double x, double y, double w, double h)
    {
        SaveDecorationState(fb.builder, fb.inRunSpace);
        fb.builder.SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        fb.builder.Rectangle(x, y, w, h);
        fb.builder.Fill();
        fb.builder.RestoreState();
    }
}
