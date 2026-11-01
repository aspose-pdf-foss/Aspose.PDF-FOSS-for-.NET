using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// The appearance stream an annotation shows and where it goes: the normal appearance picked by the
/// appearance state, and the matrix that fits its box into the annotation's rectangle
/// (PDF 32000-1 §12.5.5, algorithm 8.1). Shared by the exporters that draw a page the way a viewer shows it.
/// </summary>
internal static class AppearanceStreams
{
    private static readonly double[] Identity = { 1, 0, 0, 1, 0, 0 };

    /// <summary>The /AP /N stream: given directly, or picked from the appearance-state
    /// dictionary by the annotation's /AS (check boxes, radio buttons).</summary>
    internal static PdfStream? Normal(PdfDictionary annot, PdfReader reader)
    {
        var ap = reader.ResolveDict(annot.Get("AP"));
        if (ap is null) return null;
        var normal = reader.Resolve(ap.Get("N"));
        if (normal is PdfStream direct) return direct;
        if (normal is not PdfDictionary states) return null;
        var state = annot.GetName("AS");
        return string.IsNullOrEmpty(state) ? null : reader.ResolveStream(states.Get(state));
    }

    /// <summary>
    /// The matrix that maps the appearance stream, after its own /Matrix, onto the annotation's
    /// /Rect: the transformed /BBox corners are fitted to the rectangle. Null when either box has no area.
    /// </summary>
    internal static double[]? FitToRect(PdfDictionary annot, PdfStream form, PdfReader reader)
    {
        if (reader.Resolve(annot.Get("Rect")) is not PdfArray rectArray || rectArray.Count < 4) return null;
        var rect = Rectangle.FromPdfArray(rectArray, reader);
        var rMinX = Math.Min(rect.LLX, rect.URX);
        var rMinY = Math.Min(rect.LLY, rect.URY);
        var rW = Math.Abs(rect.URX - rect.LLX);
        var rH = Math.Abs(rect.URY - rect.LLY);
        if (rW <= 0 || rH <= 0) return null;

        if (reader.Resolve(form.Dict.Get("BBox")) is not PdfArray bbox || bbox.Count < 4) return null;
        var matrix = Identity;
        if (reader.Resolve(form.Dict.Get("Matrix")) is PdfArray m && m.Count >= 6)
            matrix = new[] { Num(reader, m[0]), Num(reader, m[1]), Num(reader, m[2]), Num(reader, m[3]), Num(reader, m[4]), Num(reader, m[5]) };
        var bx1 = Num(reader, bbox[0]); var by1 = Num(reader, bbox[1]);
        var bx2 = Num(reader, bbox[2]); var by2 = Num(reader, bbox[3]);
        double tMinX = double.PositiveInfinity, tMinY = double.PositiveInfinity;
        double tMaxX = double.NegativeInfinity, tMaxY = double.NegativeInfinity;
        foreach (var (cx, cy) in new[] { (bx1, by1), (bx2, by1), (bx2, by2), (bx1, by2) })
        {
            var tx = matrix[0] * cx + matrix[2] * cy + matrix[4];
            var ty = matrix[1] * cx + matrix[3] * cy + matrix[5];
            tMinX = Math.Min(tMinX, tx); tMaxX = Math.Max(tMaxX, tx);
            tMinY = Math.Min(tMinY, ty); tMaxY = Math.Max(tMaxY, ty);
        }
        var tW = tMaxX - tMinX;
        var tH = tMaxY - tMinY;
        if (tW <= 0 || tH <= 0) return null;

        var sx = rW / tW;
        var sy = rH / tH;
        return new[] { sx, 0, 0, sy, rMinX - tMinX * sx, rMinY - tMinY * sy };
    }

    private static double Num(PdfReader reader, PdfObject obj) => reader.Resolve(obj) switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };
}
