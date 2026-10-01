using System.Numerics;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

internal static partial class PdfAValidator
{
    /// <summary>The font requirements PDF/UA-1 clause 7.21 states about the programs a page
    /// draws with. Each font is examined once, on the first page that uses it.</summary>
    /// <remarks>The embedding requirement is reported only when the report describes a
    /// CONVERSION. A conversion embeds every face it can reach, so the log names what the
    /// source was missing; validation leaves it alone, because a document authored through
    /// this library draws through faces that are embedded when it is saved.</remarks>
    private static void CheckUaFonts(Document document, UaReport report, bool forConversion)
    {
        var visited = new HashSet<PdfDictionary>();
        foreach (var page in document.Pages)
            foreach (var (objectNumber, font) in document.PageFonts(page, visited))
            {
                if (forConversion)
                    CheckUaFontEmbedding(document, font, objectNumber, page.Number, report);
                CheckUaSymbolicCmap(document, font, page.Number, report);
                CheckUaCidSet(document, font, objectNumber, page.Number, report);
            }
    }

    /// <summary>
    /// PDF/UA-1 clause 7.21.4.1: every font must travel with the file. A reader that
    /// substitutes a face it happens to have renders different glyph shapes and widths, and
    /// assistive technology reading the file elsewhere sees something else again.
    /// </summary>
    private static void CheckUaFontEmbedding(Document document, PdfDictionary font,
        int objectNumber, int pageNumber, UaReport report)
    {
        var descriptor = document.DescriptorOf(font);
        if (descriptor is null) return;
        if (descriptor.Get("FontFile") is not null || descriptor.Get("FontFile2") is not null
            || descriptor.Get("FontFile3") is not null) return;
        report.Add(UaProblems.FontNotEmbedded, "FontEmbedding",
            $"Font '{font.GetName("BaseFont") ?? "Unknown"}' is not embedded",
            pageNumber, objectNumber == 0 ? null : objectNumber.ToString());
    }

    /// <summary>
    /// PDF/UA-1 clause 7.21.4.2: a CIDFont that ships a /CIDSet must account for the whole
    /// embedded program in it — the set carries one bit per CID and the number of bits SET
    /// has to equal the program's glyph count. A short set means the file claims fewer
    /// glyphs than it actually embeds, so an assistive reader cannot tell which glyphs are
    /// real.
    /// </summary>
    /// <remarks>A font with no /CIDSet at all is not checked: the entry is optional and its
    /// absence is a different (unreported) matter. The count is the program's raw glyph
    /// count — not the glyphs that carry outlines, and not the CIDs the content uses.</remarks>
    private static void CheckUaCidSet(Document document, PdfDictionary font, int objectNumber,
        int pageNumber, UaReport report)
    {
        var reader = document.Reader;
        if (font.GetName("Subtype") != "Type0") return;
        if (reader.Resolve(font.Get("DescendantFonts")) is not PdfArray { Count: > 0 } descendants)
            return;

        var cidFont = reader.ResolveDict(descendants[0]);
        var descriptor = cidFont is null ? null : reader.ResolveDict(cidFont.Get("FontDescriptor"));
        if (descriptor is null) return;
        var cidSetStream = reader.ResolveStream(descriptor.Get("CIDSet"));
        if (cidSetStream is null) return;

        var program = reader.ResolveStream(descriptor.Get("FontFile2"))
            ?? reader.ResolveStream(descriptor.Get("FontFile3"));
        if (program is null) return;

        byte[] cidSet, programBytes;
        try
        {
            cidSet = reader.DecodeStream(cidSetStream);
            programBytes = reader.DecodeStream(program);
        }
        catch { return; }

        var glyphs = EmbeddedGlyphCount(programBytes);
        if (glyphs <= 0) return;

        var marked = 0;
        foreach (var b in cidSet) marked += Compat.PopCount(b);
        if (marked == glyphs) return;

        // The problem names the CIDFont dictionary, not the Type0 wrapper.
        var cidFontObject = (descendants[0] as PdfIndirectRef)?.ObjectNumber ?? objectNumber;
        var baseFont = cidFont?.GetName("BaseFont") ?? font.GetName("BaseFont") ?? "Unknown";
        report.Add(UaProblems.CidSetIncomplete, "FontCidSet",
            $"CIDSet is missing or incomplete for font '{baseFont}'",
            pageNumber, cidFontObject == 0 ? null : cidFontObject.ToString());
    }

    /// <summary>PDF/UA-1 clause 7.21.4.2: a symbolic TrueType font program (one whose cmap
    /// carries a Windows-Symbol (3,0) subtable) must contain EXACTLY one cmap encoding.</summary>
    private static void CheckUaSymbolicCmap(Document document, PdfDictionary font,
        int pageNumber, UaReport report)
    {
        var reader = document.Reader;
        var descriptor = document.DescriptorOf(font);
        var ff = descriptor is null ? null : reader.ResolveStream(descriptor.Get("FontFile2"));
        if (ff is null) return;

        byte[] prog;
        try { prog = reader.DecodeStream(ff); } catch { return; }
        if (SfntTableOffset(prog, "cmap") is not { } table || table + 4 > prog.Length) return;

        int subtables = (prog[table + 2] << 8) | prog[table + 3];
        var hasSymbol = false;
        for (var j = 0; j < subtables; j++)
        {
            var entry = table + 4 + j * 8;
            if (entry + 8 > prog.Length) break;
            int platform = (prog[entry] << 8) | prog[entry + 1];
            int encoding = (prog[entry + 2] << 8) | prog[entry + 3];
            if (platform == 3 && encoding == 0) hasSymbol = true;
        }
        if (!hasSymbol || subtables == 1) return;

        var baseFont = font.GetName("BaseFont") ?? "Unknown";
        report.Add(UaProblems.CidSetIncomplete, "FontCmap",
            $"Symbolic TrueType font '{baseFont}' program cmap must contain exactly one encoding "
            + $"(PDF/UA-1 7.21.4.2), found {subtables}",
            pageNumber);
    }

    /// <summary>Number of glyphs an embedded font program defines: <c>numGlyphs</c> from an
    /// sfnt's <c>maxp</c> table, or the CharStrings count of a bare CFF.</summary>
    internal static int EmbeddedGlyphCount(byte[] program)
    {
        if (SfntTableOffset(program, "maxp") is { } maxp && maxp + 6 <= program.Length)
            return (program[maxp + 4] << 8) | program[maxp + 5];
        if (IsSfnt(program)) return 0;
        try
        {
            var cff = new Text.CffParser(program);
            cff.ExtractWidths();
            return cff.GlyphCount;
        }
        catch { return 0; }
    }

    /// <summary>Offset of a table in an sfnt font program, or null when the program is not an
    /// sfnt or carries no such table.</summary>
    private static int? SfntTableOffset(byte[] program, string tag)
    {
        if (!IsSfnt(program) || program.Length < 12) return null;
        int numTables = (program[4] << 8) | program[5];
        for (var i = 0; i < numTables; i++)
        {
            var entry = 12 + i * 16;
            if (entry + 16 > program.Length) return null;
            if (program[entry] != tag[0] || program[entry + 1] != tag[1]
                || program[entry + 2] != tag[2] || program[entry + 3] != tag[3]) continue;
            return (program[entry + 8] << 24) | (program[entry + 9] << 16)
                | (program[entry + 10] << 8) | program[entry + 11];
        }
        return null;
    }

    /// <summary>True when the program opens with one of the sfnt version tags — TrueType
    /// outlines (1.0 or 'true') or CFF outlines wrapped in an sfnt ('OTTO').</summary>
    private static bool IsSfnt(byte[] program) =>
        program.Length >= 4
        && ((program[0] == 0x00 && program[1] == 0x01 && program[2] == 0x00 && program[3] == 0x00)
            || (program[0] == 'O' && program[1] == 'T' && program[2] == 'T' && program[3] == 'O')
            || (program[0] == 't' && program[1] == 'r' && program[2] == 'u' && program[3] == 'e'));
}
