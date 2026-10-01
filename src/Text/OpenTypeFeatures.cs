using System.Collections.Generic;

namespace Aspose.Pdf.Text;

/// <summary>
/// The OpenType features a font carries, and what a run of text becomes once they are
/// applied: `liga` turns "fi" into the single glyph a good face draws it with, `onum`
/// draws digits that sit on the old-style line, `ss01` picks a face's first stylistic set.
///
/// None of this happens unless it is asked for. A Latin run is one glyph per character
/// by default, which is what the rest of this library assumes and what the layout engines
/// built on top of it measure against.
///
/// ⭐ A caller that lays text out itself needs the SAME glyph run twice: once to measure
/// it, because a ligature is narrower than the characters it replaced, and once to draw
/// it. Asking here for the first and naming the same features on the operator for the
/// second keeps one implementation behind both.
/// </summary>
public static class OpenTypeFeatures
{
    /// <summary>
    /// The glyphs <paramref name="text"/> becomes in <paramref name="fontProgram"/> once
    /// <paramref name="features"/> have been applied, each with the index of the first
    /// character it stands for.
    ///
    /// Returns null when nothing applied — the font has no such feature, or none of its
    /// rules matched — and the caller should then map characters to glyphs as usual.
    /// </summary>
    /// <param name="fontProgram">The TrueType/OpenType font program.</param>
    /// <param name="text">The run, in logical order.</param>
    /// <param name="features">Feature tags, such as "liga" or "onum".</param>
    public static IReadOnlyList<(ushort Glyph, int FirstCharacter)>? Apply(
        byte[] fontProgram, string text, IReadOnlyList<string> features)
    {
        if (fontProgram is null || string.IsNullOrEmpty(text)) return null;
        if (features is null || features.Count == 0) return null;

        try
        {
            var parser = new GlyphOutlineParser(fontProgram);
            var shaped = OpenType.TextShaper.ShapeFeatures(
                fontProgram, text, cp => (ushort)parser.GlyphIdOrLookAlike(cp), features);

            return shaped is null ? null : System.Array.ConvertAll(shaped, g => (g.Glyph, g.Cluster));
        }
        catch
        {
            // A malformed layout table must never cost the caller its text.
            return null;
        }
    }
}
