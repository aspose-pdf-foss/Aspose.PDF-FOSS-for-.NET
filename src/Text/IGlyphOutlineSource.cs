namespace Aspose.Pdf.Text;

/// <summary>
/// Common contract for anything the renderer can ask for a glyph's outline in the
/// font's own em-square units. Implemented by both <see cref="GlyphOutlineParser"/>
/// (TrueType <c>glyf</c> table, <c>/FontFile2</c>) and <see cref="CffParser"/>
/// (Adobe CFF / <c>/FontFile3</c> with <c>/Type1C</c> or <c>/CIDFontType0C</c>).
/// </summary>
internal interface IGlyphOutlineSource
{
    /// <summary>Font em-square in design units. Glyph coords are divided by this
    /// when mapping to pixel space.</summary>
    int UnitsPerEm { get; }

    /// <summary>
    /// Character → glyph-ID mapping for simple (non-CID) fonts. CID fonts carry
    /// their own <c>/CIDToGIDMap</c> in the PDF font dictionary so this is unused
    /// by the CID text-drawing path and may be empty.
    /// </summary>
    Dictionary<int, int> CMap { get; }

    /// <summary>Resolve a glyph outline by glyph ID. Returns null for empty or
    /// out-of-range glyphs; implementations are expected to handle malformed
    /// charstrings gracefully rather than throwing.</summary>
    GlyphOutline? GetOutline(int glyphId);

    /// <summary>Advance width for the given glyph id, in font units (same unit
    /// system as <see cref="UnitsPerEm"/>). Used by the renderer to advance the
    /// pen between glyphs when the font dict's /Widths array doesn't cover the
    /// requested code — e.g. a /Differences-mapped Polish/Czech glyph decoded
    /// to a Unicode code point above 255. Returns 0 for unknown glyph ids;
    /// callers should treat that as "use the existing fallback".</summary>
    int GetAdvanceWidth(int glyphId) => 0;

    /// <summary>Resolve a glyph id from a PostScript glyph name (e.g. a /Differences
    /// entry like "G42"). Returns 0 when the font exposes no name table or the name is
    /// unknown — callers treat 0 as "not found". Lets the renderer draw fonts whose PDF
    /// /Encoding maps codes to custom glyph names with no Unicode equivalent.</summary>
    int GidForName(string name) => 0;
}
