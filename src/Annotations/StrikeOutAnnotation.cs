using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>StrikeOut text markup annotation.</summary>
public partial class StrikeOutAnnotation : TextMarkupAnnotation
{
    internal StrikeOutAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public StrikeOutAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("StrikeOut"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.StrikeOut;

    /// <summary>Regenerate the normal appearance (/AP /N): ONE line across the
    /// annotation rectangle at its mid-height in the annotation colour, 1 pt wide
    /// — the strike-out is drawn as exactly `0 h/2 m w h/2 l S` in a [0 0 w h] box,
    /// whether the strikeout carries quads or only a /Rect.</summary>
    public override void UpdateAppearances()
    {
        var r = Rect;
        if (r is null) { base.UpdateAppearances(); return; }
        var w = r.URX - r.LLX;
        var h = r.URY - r.LLY;
        if (w <= 0 || h <= 0) { base.UpdateAppearances(); return; }
        var color = Color;
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(color);
        b.SetFillColor(color);
        b.SetLineWidth(1);
        b.MoveTo(0, h / 2);
        b.LineTo(w, h / 2);
        b.Stroke();
        b.RestoreState();
        SetLocalBoxAppearance(b.Build(), w, h);
    }

    // Build the /AP /N form XObject with a LOCAL [0 0 w h] BBox (the flatten
    // placement translates it to the rectangle's lower-left corner).
    private void SetLocalBoxAppearance(byte[] content, double w, double h)
    {
        var form = new PdfDictionary();
        form.Set("Type", new PdfName("XObject"));
        form.Set("Subtype", new PdfName("Form"));
        form.Set("FormType", new PdfInteger(1));
        var bb = new PdfArray();
        bb.Add(new PdfReal(0)); bb.Add(new PdfReal(0));
        bb.Add(new PdfReal(w)); bb.Add(new PdfReal(h));
        form.Set("BBox", bb);
        form.Set("Length", new PdfInteger(content.Length));
        var ap = InternalReader.ResolveDict(Dict.Get("AP")) ?? new PdfDictionary();
        ap.Set("N", new PdfStream(form, content));
        Dict.Set("AP", ap);
    }
}
