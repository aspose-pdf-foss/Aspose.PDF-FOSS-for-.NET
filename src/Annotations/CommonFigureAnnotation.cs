using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Common base for square and circle annotations — a figure drawn
/// inside a rectangle, optionally inset by /RD (PDF 32000 §12.5.6.8).</summary>
public abstract partial class CommonFigureAnnotation : MarkupAnnotation
{
    internal CommonFigureAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    protected CommonFigureAnnotation(Page page, Rectangle rect) : base(page, rect) { }
    protected CommonFigureAnnotation(Document document, Rectangle rect) : base(document, rect) { }

    /// <summary>The drawn figure rectangle — the annotation rectangle inset by
    /// the /RD (rectangle differences) entry. Equal to <see cref="Annotation.Rect"/>
    /// when /RD is absent.</summary>
    public Rectangle Frame
    {
        get
        {
            var r = Rect ?? new Rectangle(0, 0, 0, 0);
            var rd = InternalReader.Resolve(Dict.Get("RD")) as PdfArray;
            if (rd is null || rd.Count < 4) return new Rectangle(r.LLX, r.LLY, r.URX, r.URY);
            double left = N(rd[0]), top = N(rd[1]), right = N(rd[2]), bottom = N(rd[3]);
            return new Rectangle(r.LLX + left, r.LLY + bottom, r.URX - right, r.URY - top);
        }
        set
        {
            var r = Rect;
            if (value is null || r is null) { Dict.Remove("RD"); return; }
            var rd = new PdfArray();
            rd.Add(new PdfReal(value.LLX - r.LLX)); // left
            rd.Add(new PdfReal(r.URY - value.URY)); // top
            rd.Add(new PdfReal(r.URX - value.URX)); // right
            rd.Add(new PdfReal(value.LLY - r.LLY)); // bottom
            Dict.Set("RD", rd);
        }
    }

    private static double N(PdfObject o) => o is PdfReal r ? r.Value : o is PdfInteger i ? i.Value : 0;

    /// <summary>Generate the normal appearance for a Square or Circle annotation
    /// (PDF 32000 §12.5.6.8): stroke the figure with the border colour, width and
    /// dash from /BS, optionally fill the interior with /IC. The figure is inset by
    /// half the border width so the stroke stays within the annotation rectangle.</summary>
    public override void UpdateAppearances()
    {
        var rect = Rect;
        if (rect is null) return;
        var frame = Frame;
        if (frame.Width <= 0 || frame.Height <= 0) return;

        // Border width and dash pattern from /BS (the modern border-style dict),
        // falling back to the legacy /Border array's third element for the width.
        double bw = -1;
        double[]? dash = null;
        var bs = InternalReader.ResolveDict(Dict.Get("BS"));
        if (bs is not null)
        {
            if (bs.Get("W") is PdfReal wr) bw = wr.Value;
            else if (bs.Get("W") is PdfInteger wi) bw = wi.Value;
            if (bs.GetName("S") == "D" && InternalReader.Resolve(bs.Get("D")) is PdfArray da && da.Count > 0)
            {
                dash = new double[da.Count];
                for (var i = 0; i < da.Count; i++) dash[i] = N(da[i]);
            }
        }
        if (bw < 0 && InternalReader.Resolve(Dict.Get("Border")) is PdfArray bd && bd.Count >= 3)
            bw = N(bd[2]);
        if (bw < 0) bw = 1.0; // neither /BS nor /Border specified a width

        var stroke = Color;
        var fill = InteriorColor;

        // Nothing visible (no border colour with a non-zero width, no interior fill):
        // leave /AP absent so the figure stays invisible, matching a viewer that paints
        // a Square/Circle only when it has a colour. Squares used purely as text anchors
        // (/Border [0 0 0], no /C) must not sprout an opaque outline on flatten.
        bool doStroke = stroke is not null && bw > 0;
        bool doFill = fill is not null;
        if (!doStroke && !doFill) return;

        // Inset by half the line width; if that collapses the figure, stroke the frame as-is.
        double half = bw / 2.0;
        double x = frame.LLX + half, y = frame.LLY + half, w = frame.Width - bw, h = frame.Height - bw;
        if (w <= 0 || h <= 0) { x = frame.LLX; y = frame.LLY; w = frame.Width; h = frame.Height; }

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        if (doFill) b.SetFillColor(fill!);
        if (doStroke)
        {
            b.SetStrokeColor(stroke!);
            b.SetLineWidth(bw);
            if (dash is not null) { b.SetLineCap(1); b.SetDashPattern(dash); }
        }

        if (Dict.GetName("Subtype") == "Circle")
        {
            // Ellipse approximated by four cubic Béziers (kappa = 4/3·(√2−1)).
            const double k = 0.5522847498;
            double cx = x + w / 2, cy = y + h / 2, rx = w / 2, ry = h / 2;
            b.MoveTo(cx + rx, cy);
            b.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
            b.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
            b.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
            b.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
            b.ClosePath();
        }
        else
        {
            b.Rectangle(x, y, w, h);
        }

        if (doFill && doStroke) b.FillAndStroke();
        else if (doFill) b.Fill();
        else b.Stroke();
        b.RestoreState();
        SetNormalAppearance(b.Build(), rect);
    }

    /// <summary>Resize-with-normalization helper (PdfFileEditor.ResizeContents): regenerate
    /// the figure's /N appearance, and when <see cref="UpdateAppearances"/> draws nothing —
    /// a colourless figure or a collapsed rectangle (e.g. a zero-area Square used as a text
    /// anchor) — still emit a minimal valid but empty appearance form so the annotation
    /// carries a normalized /N instead of a degenerate/absent one. Flatten and normal
    /// rendering keep the visibility-gated <see cref="UpdateAppearances"/> behaviour.</summary>
    internal void EnsureNormalizedAppearance()
    {
        UpdateAppearances();
        var na = NormalAppearance;
        if (na is not null && na.Contents.Count > 0) return;

        var r = Rect ?? new Rectangle(0, 0, 0, 0);
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.RestoreState();
        SetNormalAppearance(b.Build(), r);
    }
}
