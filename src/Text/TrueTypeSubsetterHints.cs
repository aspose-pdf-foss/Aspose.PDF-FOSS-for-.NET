namespace Aspose.Pdf.Text;

/// <summary>
/// A subset leaves the font's hint programs behind (fpgm, prep and cvt): neither the library's renderers nor
/// the common PDF rasterizers run them, and they cost several kilobytes per face. Its glyphs and its maxp then
/// go without hints too - a glyph that keeps its instructions calls functions and reads control values the
/// subset no longer has, and a rasterizer that does run hints (a PostScript Type 42 interpreter, a PCL XL
/// printer) rejects such a font as invalid.
/// </summary>
internal sealed partial class TrueTypeSubsetter
{
    // glyf: numberOfContours, xMin, yMin, xMax, yMax.
    private const int GlyphHeaderLength = 10;
    // Composite glyph component flags (OpenType glyf table).
    private const int ArgsAreWords = 0x0001;
    private const int HasScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int HasXAndYScale = 0x0040;
    private const int HasTwoByTwo = 0x0080;
    private const int HasInstructions = 0x0100;
    // maxp version 1.0: the offsets of maxZones and of the hint limits after it, and its length.
    private const int MaxpZonesOffset = 14;
    private const int MaxpHintLimitsEnd = 28;
    private const int MaxpVersion1Length = 32;

    /// <summary>The glyph's outline with its instructions taken out; the glyph itself when it has none.</summary>
    private static byte[] WithoutInstructions(byte[] glyph)
    {
        if (glyph.Length < GlyphHeaderLength) return glyph;
        var contours = (short)((glyph[0] << 8) | glyph[1]);
        return contours >= 0 ? SimpleWithoutInstructions(glyph, contours) : CompositeWithoutInstructions(glyph);
    }

    // A simple glyph: the end points of its contours, the instruction length and the instructions, then its
    // flags and coordinates. The length becomes 0 and the instructions go.
    private static byte[] SimpleWithoutInstructions(byte[] glyph, int contours)
    {
        var at = GlyphHeaderLength + contours * 2;
        if (at + 2 > glyph.Length) return glyph;
        var length = (glyph[at] << 8) | glyph[at + 1];
        if (length == 0 || at + 2 + length > glyph.Length) return glyph;
        var result = new byte[glyph.Length - length];
        Array.Copy(glyph, 0, result, 0, at);
        Array.Copy(glyph, at + 2 + length, result, at + 2, glyph.Length - at - 2 - length);
        return result;
    }

    // A composite glyph: its components, the instructions after the last one when a component's flags say so.
    // The flags lose the bit and the glyph ends with its last component.
    private static byte[] CompositeWithoutInstructions(byte[] glyph)
    {
        var end = GlyphHeaderLength;
        var instructed = false;
        while (end + 4 <= glyph.Length)
        {
            var flags = (glyph[end] << 8) | glyph[end + 1];
            instructed |= (flags & HasInstructions) != 0;
            end += 4 + ComponentTail(flags);
            if ((flags & MoreComponents) == 0) break;
        }
        if (!instructed || end > glyph.Length) return glyph;
        var result = new byte[end];
        Array.Copy(glyph, result, end);
        var at = GlyphHeaderLength;
        while (at + 4 <= result.Length)
        {
            var flags = ((result[at] << 8) | result[at + 1]) & ~HasInstructions;
            result[at] = (byte)(flags >> 8);
            result[at + 1] = (byte)flags;
            at += 4 + ComponentTail(flags);
            if ((flags & MoreComponents) == 0) break;
        }
        return result;
    }

    /// <summary>The bytes of a component after its flags and glyph index: its arguments and its transform.</summary>
    private static int ComponentTail(int flags)
    {
        var tail = (flags & ArgsAreWords) != 0 ? 4 : 2;
        if ((flags & HasScale) != 0) tail += 2;
        else if ((flags & HasXAndYScale) != 0) tail += 4;
        else if ((flags & HasTwoByTwo) != 0) tail += 8;
        return tail;
    }

    /// <summary>A maxp table of version 1.0 declaring no hints: one zone, and no twilight points, storage,
    /// functions, instruction definitions, stack or instructions.</summary>
    private static void ClearHintLimits(byte[] maxp)
    {
        if (maxp.Length < MaxpVersion1Length || maxp[0] != 0 || maxp[1] != 1) return;
        maxp[MaxpZonesOffset] = 0;
        maxp[MaxpZonesOffset + 1] = 1;
        for (var i = MaxpZonesOffset + 2; i < MaxpHintLimitsEnd; i++) maxp[i] = 0;
    }
}
