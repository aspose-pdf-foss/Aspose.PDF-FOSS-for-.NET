namespace Aspose.Pdf.Text;

/// <summary>
/// Rounds a charstring glyph outline's vertices to whole font units — how the glyph
/// renders after its program is converted to a TrueType, whose glyf records store
/// integer coordinates. The <see cref="Aspose.Pdf.RenderingOptions.ConvertFontsToUnicodeTTF"/>
/// render path applies this via the <c>QuantizeToFontUnits</c> mode on
/// <see cref="CffGlyphSource"/> / <see cref="Type1GlyphSource"/> (a mode rather than a
/// wrapper, because the renderers type-test the concrete source for CID-keyed lookups).
/// </summary>
internal static class GlyphOutlineQuantizer
{
    /// <summary>Grid the converted TrueType stores its coordinates on. The conversion
    /// targets a fine TrueType em so its render stays within the "minimal changes" band
    /// the conversion contract promises: measured against the unconverted render, no
    /// pixel moves by more than one anti-aliasing step (the converter's
    /// on/off deltas cap at ~35 of 255).</summary>
    private const double ConvertedUnitsPerEm = 65536.0;

    public static GlyphOutline Quantize(GlyphOutline outline, int sourceUnitsPerEm)
    {
        var scale = sourceUnitsPerEm > 0 ? ConvertedUnitsPerEm / sourceUnitsPerEm : 1.0;
        double Q(double v) => Math.Round(v * scale) / scale;
        var contours = new ContourPoint[outline.Contours.Length][];
        for (var c = 0; c < contours.Length; c++)
        {
            var src = outline.Contours[c];
            var dst = new ContourPoint[src.Length];
            for (var i = 0; i < src.Length; i++)
                dst[i] = new ContourPoint(Q(src[i].X), Q(src[i].Y), src[i].OnCurve);
            contours[c] = dst;
        }
        return new GlyphOutline(contours,
            Q(outline.XMin), Q(outline.YMin), Q(outline.XMax), Q(outline.YMax));
    }
}
