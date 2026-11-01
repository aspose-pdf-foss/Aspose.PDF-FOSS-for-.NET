using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private const int BitsPerByte = 8;
    private const int HighBit = 0x80;
    private const int BytesPerGlyphId = 2;

    /// <summary>
    /// ISO 19005-1 clause 6.3.5, PDF/UA-1 clause 7.21.4.2: a CIDFont's /CIDSet names every CID
    /// its program holds. A set that says otherwise is rewritten; a subset that states none gets
    /// one when <paramref name="addMissing"/> is set (part 1 requires it of every subset).
    /// </summary>
    private void WriteSubsetCidSet(PdfDictionary font, bool addMissing)
    {
        if (ProgramCidSet(font) is not var (descriptor, existing, held)) return;
        if (existing is null && (!addMissing || DescendantOf(font)?.GetName("BaseFont") is not { } name
            || name.Length <= SubsetTagLength || name[SubsetTagLength] != '+')) return;
        try
        {
            if (existing is not null && SameBits(_reader.DecodeStream(existing), held)) return;
        }
        catch { return; }
        descriptor.Set("CIDSet", new PdfStream(new PdfDictionary(), held));
    }

    /// <summary>ISO 19005-2/-3 clause 6.2.11.4.2: a /CIDSet, which these parts do not require,
    /// names every CID the program holds when the file states one. A set that names others is
    /// dropped rather than restated.</summary>
    private void DropMisstatedCidSet(PdfDictionary font)
    {
        if (ProgramCidSet(font) is not var (descriptor, existing, held) || existing is null) return;
        try
        {
            if (SameBits(_reader.DecodeStream(existing), held)) return;
        }
        catch { return; }
        descriptor.Remove("CIDSet");
    }

    private PdfDictionary? DescendantOf(PdfDictionary font) =>
        _reader.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } descendants
            ? _reader.ResolveDict(descendants[0])
            : null;

    /// <summary>The descriptor of a composite font's TrueType CIDFont, the CIDSet it states, and
    /// the one its program calls for: the CIDs that reach a glyph of the program - under an
    /// Identity /CIDToGIDMap each glyph id, under a map stream each CID the stream maps to a
    /// glyph the program has. Null for a CFF program, or one that cannot be read.</summary>
    private (PdfDictionary Descriptor, PdfStream? Existing, byte[] Held)? ProgramCidSet(PdfDictionary font)
    {
        var cidFont = DescendantOf(font);
        if (cidFont?.GetName("Subtype") != "CIDFontType2") return null;
        var descriptor = _reader.ResolveDict(cidFont.Get("FontDescriptor"));
        if (descriptor is null || ResolveFontProgramStream(descriptor.Get("FontFile2")) is not { } program) return null;
        try
        {
            var glyphs = Optimization.PdfAValidator.EmbeddedGlyphCount(_reader.DecodeStream(program));
            if (glyphs <= 0) return null;
            var held = _reader.Resolve(cidFont.Get("CIDToGIDMap")) switch
            {
                PdfStream map => MappedCidSet(_reader.DecodeStream(map), glyphs),
                null or PdfName { Value: "Identity" } => MappedCidSet(null, glyphs),
                _ => null,
            };
            return held is null ? null : (descriptor, _reader.ResolveStream(descriptor.Get("CIDSet")), held);
        }
        catch { return null; }
    }

    /// <summary>The CIDSet bits of the CIDs that reach a glyph of a program with
    /// <paramref name="glyphs"/> glyphs: through the big-endian glyph ids of a map stream, or
    /// every glyph id when there is no map (Identity).</summary>
    private static byte[] MappedCidSet(byte[]? map, int glyphs)
    {
        var cids = map is null ? glyphs : map.Length / BytesPerGlyphId;
        var bits = new byte[(cids + BitsPerByte - 1) / BitsPerByte];
        for (var cid = 0; cid < cids; cid++)
        {
            var glyph = map is null ? cid : (map[cid * BytesPerGlyphId] << BitsPerByte) | map[cid * BytesPerGlyphId + 1];
            if (glyph < glyphs) bits[cid / BitsPerByte] |= (byte)(HighBit >> (cid % BitsPerByte));
        }
        return bits;
    }

    /// <summary>True when two CIDSets name the same CIDs past CID 0, the .notdef every program
    /// holds whether a set names it or not (trailing zero bytes name none).</summary>
    private static bool SameBits(byte[] a, byte[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var mask = i == 0 ? ~HighBit : ~0;
            if (((i < a.Length ? a[i] : 0) & mask) != ((i < b.Length ? b[i] : 0) & mask)) return false;
        }
        return true;
    }
}
