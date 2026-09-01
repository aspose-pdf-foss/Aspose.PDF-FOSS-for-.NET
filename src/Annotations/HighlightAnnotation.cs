using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>Highlight text markup annotation.</summary>
public partial class HighlightAnnotation : TextMarkupAnnotation
{
    internal HighlightAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public HighlightAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Highlight"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.Highlight;

    /// <summary>Regenerate the normal appearance (/AP /N): each /QuadPoints quad is
    /// painted as a filled-and-stroked rectangle in the annotation colour, under a
    /// Multiply blend graphics state so the highlighted text stays legible underneath.</summary>
    public override void UpdateAppearances()
    {
        var r = Rect;
        var quads = QuadPoints;
        if (r is null || quads.Length < 4) { base.UpdateAppearances(); return; }
        var color = Color;
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetExtGState("TransGs");
        b.SetFillColor(color);
        b.SetStrokeColor(color);
        for (int i = 0; i + 3 < quads.Length; i += 4)
        {
            double minX = Math.Min(Math.Min(quads[i].X, quads[i + 1].X), Math.Min(quads[i + 2].X, quads[i + 3].X));
            double maxX = Math.Max(Math.Max(quads[i].X, quads[i + 1].X), Math.Max(quads[i + 2].X, quads[i + 3].X));
            double minY = Math.Min(Math.Min(quads[i].Y, quads[i + 1].Y), Math.Min(quads[i + 2].Y, quads[i + 3].Y));
            double maxY = Math.Max(Math.Max(quads[i].Y, quads[i + 1].Y), Math.Max(quads[i + 2].Y, quads[i + 3].Y));
            b.MoveTo(minX, minY);
            b.LineTo(minX, maxY);
            b.LineTo(maxX, maxY);
            b.LineTo(maxX, minY);
            b.ClosePath();
            b.FillAndStroke();
        }
        b.RestoreState();
        SetHighlightAppearance(b.Build(), r);
    }

    // Build the /AP /N form XObject carrying the /TransGs ExtGState (Multiply blend)
    // referenced by the appearance content.
    private void SetHighlightAppearance(byte[] content, Rectangle bbox)
    {
        var form = new PdfDictionary();
        form.Set("Type", new PdfName("XObject"));
        form.Set("Subtype", new PdfName("Form"));
        form.Set("FormType", new PdfInteger(1));
        var bb = new PdfArray();
        bb.Add(new PdfReal(bbox.LLX)); bb.Add(new PdfReal(bbox.LLY));
        bb.Add(new PdfReal(bbox.URX)); bb.Add(new PdfReal(bbox.URY));
        form.Set("BBox", bb);

        // The blend rides the annotation's own /CA (a highlight authored at 40%
        // opacity is written with CA = ca = 0.399994); a
        // highlight with no /CA keeps full strength.
        var alpha = Dict.Get("CA") switch
        {
            PdfReal r2 => r2.Value,
            PdfInteger i2 => i2.Value,
            _ => 1.0,
        };
        var gs = new PdfDictionary();
        gs.Set("Type", new PdfName("ExtGState"));
        gs.Set("BM", new PdfName("Multiply"));
        gs.Set("ca", new PdfReal(alpha));
        gs.Set("CA", new PdfReal(alpha));
        var extg = new PdfDictionary();
        extg.Set("TransGs", gs);
        var res = new PdfDictionary();
        res.Set("ExtGState", extg);
        form.Set("Resources", res);
        form.Set("Length", new PdfInteger(content.Length));

        var ap = InternalReader.ResolveDict(Dict.Get("AP")) ?? new PdfDictionary();
        ap.Set("N", new PdfStream(form, content));
        Dict.Set("AP", ap);
    }
}
