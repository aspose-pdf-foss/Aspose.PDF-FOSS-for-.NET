namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Lossless JBIG2 encoding for bitonal images, written as a PDF <c>/JBIG2Decode</c> stream. A PDF
/// stream carries no file header and no end-of-page segment (PDF 32000 §7.4.7). Bitmaps are packed
/// rows, MSB first, 1 = black - the JBIG2 convention, the inverse of the PDF samples a
/// <c>/JBIG2Decode</c> filter produces.
/// </summary>
internal static partial class Jbig2Encoder
{
    private const byte PageInformation = 48;
    private const byte ImmediateTextRegion = 6;
    private const byte ImmediateLosslessGenericRegion = 39;
    private const byte EndOfPage = 49;
    private const byte EndOfFile = 51;
    // Generic region flags: arithmetic coding (MMR off), template 0, typical prediction on.
    private const byte TemplateZeroWithTypicalPrediction = 0x08;
    // Page flags: the page is eventually lossless; and regions may use a combination operator of
    // their own instead of the page's.
    private const byte LosslessPage = 0x01;
    private const byte CombinationOverridden = 0x40;
    // Region combination operators (T.88 §7.4.1.5).
    private const byte Replace = 4;
    // The pseudo-pixel context the per-row typical-prediction bit is coded in (template 0).
    private const int TypicalRowContext = 0x9B25;
    // Template 0's adaptive pixels, at their nominal places.
    private static readonly (int Dx, int Dy)[] Adaptive = [(3, -1), (-3, -1), (2, -2), (-2, -2)];
    // Rows are unpacked with this margin of white pixels round them, so a context never reads
    // outside its array.
    private const int Margin = 4;
    // The region segment information field: width, height, x, y, combination operator.
    private const int RegionInfoLength = 17;

    /// <summary>A whole bitmap as one page: a page-information segment and one immediate lossless
    /// generic region, arithmetic coded with template 0 and typical prediction (T.88 §6.2, §7.4).</summary>
    public static byte[] Encode(byte[] packed, int width, int height, int stride)
    {
        using var output = new MemoryStream();
        var page = new byte[19];
        WriteInt(page, 0, width);
        WriteInt(page, 4, height);
        page[16] = LosslessPage;
        WriteSegment(output, 0, PageInformation, 1, page);
        WriteSegment(output, 1, ImmediateLosslessGenericRegion, 1,
            GenericRegion(packed, stride, (0, 0, width, height), 0));
        return output.ToArray();
    }

    /// <summary>A segment with no referred-to segments: its number, its type, its page, its data.</summary>
    private static void WriteSegment(Stream output, int number, byte type, int page, byte[] data)
    {
        var wide = page > byte.MaxValue;
        var header = new byte[wide ? 14 : 11];
        WriteInt(header, 0, number);
        header[4] = (byte)(type | (wide ? 0x40 : 0));
        header[5] = 0;
        if (wide) WriteInt(header, 6, page);
        else header[6] = (byte)page;
        WriteInt(header, header.Length - 4, data.Length);
        output.Write(header, 0, header.Length);
        output.Write(data, 0, data.Length);
    }

    private static void WriteInt(byte[] buffer, int at, int value)
    {
        buffer[at] = (byte)(value >> 24);
        buffer[at + 1] = (byte)(value >> 16);
        buffer[at + 2] = (byte)(value >> 8);
        buffer[at + 3] = (byte)value;
    }

    /// <summary>A region segment information field for <paramref name="box"/>.</summary>
    private static byte[] RegionInfo((int X, int Y, int Width, int Height) box, int combination, int extra)
    {
        var data = new byte[RegionInfoLength + extra];
        WriteInt(data, 0, box.Width);
        WriteInt(data, 4, box.Height);
        WriteInt(data, 8, box.X);
        WriteInt(data, 12, box.Y);
        data[16] = (byte)combination;
        return data;
    }

    /// <summary>The data of an immediate lossless generic region holding <paramref name="box"/> of the
    /// bitmap: per row, whether it repeats the row above (typical prediction), then - when it does
    /// not - each pixel in its template-0 context (two rows above, four to the left, and the four
    /// adaptive pixels).</summary>
    private static byte[] GenericRegion(byte[] packed, int stride, (int X, int Y, int Width, int Height) box,
        int combination)
    {
        var (left, top, width, height) = box;
        var span = width + 2 * Margin;
        // Two white rows above the region, one pixel per byte.
        var pixels = new byte[(height + 2) * span];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                int px = left + x, py = top + y;
                pixels[(y + 2) * span + Margin + x] = (byte)((packed[py * stride + (px >> 3)] >> (7 - (px & 7))) & 1);
            }

        var coder = new MqEncoder();
        var contexts = new ContextSet(1 << 16);
        var typical = false;
        for (var y = 0; y < height; y++)
        {
            var row = (y + 2) * span;
            var repeats = SameRow(pixels, row, row - span, span);
            coder.Encode(contexts, TypicalRowContext, repeats != typical);
            typical = repeats;
            if (repeats) continue;
            for (var x = 0; x < width; x++)
            {
                var at = row + Margin + x;
                coder.Encode(contexts, Context(pixels, at, span), pixels[at] != 0);
            }
        }
        var coded = coder.Finish();

        var data = RegionInfo(box, combination, 1 + 2 * Adaptive.Length + coded.Length);
        data[RegionInfoLength] = TemplateZeroWithTypicalPrediction;
        for (var i = 0; i < Adaptive.Length; i++)
        {
            data[RegionInfoLength + 1 + 2 * i] = unchecked((byte)(sbyte)Adaptive[i].Dx);
            data[RegionInfoLength + 2 + 2 * i] = unchecked((byte)(sbyte)Adaptive[i].Dy);
        }
        Array.Copy(coded, 0, data, RegionInfoLength + 1 + 2 * Adaptive.Length, coded.Length);
        return data;
    }

    private static bool SameRow(byte[] pixels, int row, int above, int span)
    {
        for (var i = 0; i < span; i++)
            if (pixels[row + i] != pixels[above + i]) return false;
        return true;
    }

    /// <summary>Template 0's 16-bit context, in the order the decoder builds it.</summary>
    private static int Context(byte[] p, int at, int span)
    {
        int up2 = at - 2 * span, up1 = at - span;
        int c = p[up2 - 1];
        c = (c << 1) | p[up2];
        c = (c << 1) | p[up2 + 1];
        c = (c << 1) | p[up1 - 2];
        c = (c << 1) | p[up1 - 1];
        c = (c << 1) | p[up1];
        c = (c << 1) | p[up1 + 1];
        c = (c << 1) | p[up1 + 2];
        c = (c << 1) | p[at - 4];
        c = (c << 1) | p[at - 3];
        c = (c << 1) | p[at - 2];
        c = (c << 1) | p[at - 1];
        foreach (var (dx, dy) in Adaptive) c = (c << 1) | p[at + dy * span + dx];
        return c & 0xFFFF;
    }
}
