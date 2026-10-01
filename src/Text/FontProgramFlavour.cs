namespace Aspose.Pdf.Text;

/// <summary>
/// What kind of outlines a font program carries, read from the four bytes it opens with.
/// An sfnt container holds either TrueType outlines in a 'glyf' table - version 1.0 or
/// 'true' - or PostScript (CFF) ones in a 'CFF ' table, and then opens with 'OTTO'. The
/// two are embedded into a document in different ways (PDF 32000-1 §9.9, Table 126), so
/// the difference has to be told before a font is written out.
/// </summary>
internal static class FontProgramFlavour
{
    /// <summary>'OTTO': an OpenType container whose outlines are PostScript (CFF), not TrueType.</summary>
    private const uint OpenTypeCffTag = 0x4F54544F;

    /// <summary>Whether this font program is an OpenType container with CFF outlines.</summary>
    internal static bool IsOpenTypeCff(byte[]? program) =>
        program is { Length: >= 4 }
        && (uint)((program[0] << 24) | (program[1] << 16) | (program[2] << 8) | program[3]) == OpenTypeCffTag;
}
