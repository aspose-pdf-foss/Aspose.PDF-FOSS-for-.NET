namespace Aspose.Pdf.IO;

/// <summary>
/// Managed BMP decoder: reads a Windows bitmap into PNG bytes (via <see cref="PngEncoder"/>)
/// so BMP handling does not depend on a platform image codec. BMP is the one common raster
/// format with barely any compression to speak of, which is probably why it never got a
/// managed reader - and why a BMP silently vanished from a generated document anywhere the
/// System.Drawing codecs are absent.
///
/// Supported: BITMAPCOREHEADER and BITMAPINFOHEADER (and its longer V4/V5 variants);
/// 1/4/8 bit palette, 16/24/32 bit direct colour; BI_RGB and BI_BITFIELDS; bottom-up and
/// top-down row order. An RLE-compressed bitmap is declined (returns null) rather than
/// half-decoded. Any alpha channel is composited over WHITE, matching what the platform
/// codec path produces when it draws the image onto a cleared bitmap.
/// </summary>
internal static class BmpDecoder
{
    public static bool IsBmp(byte[] d) =>
        d is { Length: >= 26 } && d[0] == 0x42 && d[1] == 0x4D;

    /// <summary>Decode to PNG bytes, or null when the bytes are not a BMP this reader
    /// handles.</summary>
    public static byte[]? DecodeAsPng(byte[] d)
    {
        try { return DecodeCore(d); }
        catch { return null; }
    }

    private static byte[]? DecodeCore(byte[] d)
    {
        if (!IsBmp(d)) return null;
        var dataOffset = (int)U32(d, 10);
        var dibSize = (int)U32(d, 14);
        int width, height, bitCount, compression = 0, clrUsed = 0, paletteEntry;
        if (dibSize == 12)
        {
            width = U16(d, 18);
            height = U16(d, 20);
            bitCount = U16(d, 24);
            paletteEntry = 3;                       // BITMAPCOREHEADER palette is RGB triplets
        }
        else if (dibSize >= 40)
        {
            width = (int)U32(d, 18);
            height = (int)U32(d, 22);
            bitCount = U16(d, 28);
            compression = (int)U32(d, 30);
            clrUsed = (int)U32(d, 46);
            paletteEntry = 4;
        }
        else return null;

        // A negative height means the rows are stored TOP-DOWN instead of the usual
        // bottom-up order.
        var topDown = height < 0;
        if (topDown) height = -height;
        if (width <= 0 || height <= 0 || width > 65535 || height > 65535) return null;
        if ((long)width * height > 268_435_456) return null;          // 256M px sanity cap
        if (bitCount is not (1 or 4 or 8 or 16 or 24 or 32)) return null;
        if (compression is not (0 or 3)) return null;                 // RLE4/RLE8 not read here

        // Channel masks: BI_BITFIELDS states them, either inside a V4/V5 header or in the
        // three words right after a plain one. BI_RGB implies the classic packing.
        uint mR, mG, mB, mA = 0;
        if (compression == 3 && dibSize >= 52)
        {
            mR = U32(d, 54); mG = U32(d, 58); mB = U32(d, 62);
            if (dibSize >= 56) mA = U32(d, 66);
        }
        else if (compression == 3 && 14 + dibSize + 12 <= d.Length)
        {
            mR = U32(d, 14 + dibSize); mG = U32(d, 18 + dibSize); mB = U32(d, 22 + dibSize);
        }
        else if (bitCount == 16) { mR = 0x7C00; mG = 0x03E0; mB = 0x001F; }
        else { mR = 0x00FF0000; mG = 0x0000FF00; mB = 0x000000FF; }

        byte[]? palette = null;
        if (bitCount <= 8)
        {
            var count = clrUsed > 0 ? clrUsed : 1 << bitCount;
            var at = 14 + dibSize;
            if (count <= 0 || at + count * paletteEntry > d.Length) return null;
            palette = new byte[count * 3];
            for (var i = 0; i < count; i++)
            {
                // Stored BLUE, GREEN, RED (then a pad byte in the 4-byte form).
                palette[i * 3]     = d[at + i * paletteEntry + 2];
                palette[i * 3 + 1] = d[at + i * paletteEntry + 1];
                palette[i * 3 + 2] = d[at + i * paletteEntry];
            }
        }

        var rowBytes = (width * bitCount + 31) / 32 * 4;              // rows pad to 4 bytes
        if (dataOffset <= 0 || dataOffset >= d.Length) return null;
        if ((long)dataOffset + (long)rowBytes * height > d.Length) return null;

        var rgb = new byte[(long)width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var srcRow = dataOffset + (topDown ? y : height - 1 - y) * rowBytes;
            var dst = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;
                if (bitCount <= 8)
                {
                    var bitPos = x * bitCount;
                    var idx = bitCount switch
                    {
                        8 => d[srcRow + x],
                        4 => (d[srcRow + bitPos / 8] >> (bitPos % 8 == 0 ? 4 : 0)) & 0x0F,
                        _ => (d[srcRow + bitPos / 8] >> (7 - bitPos % 8)) & 0x01,
                    };
                    if (palette is null || idx * 3 + 2 >= palette.Length) { r = g = b = 0; }
                    else { r = palette[idx * 3]; g = palette[idx * 3 + 1]; b = palette[idx * 3 + 2]; }
                }
                else
                {
                    var bytesPer = bitCount / 8;
                    var at = srcRow + x * bytesPer;
                    uint px = bytesPer switch
                    {
                        2 => (uint)(d[at] | (d[at + 1] << 8)),
                        3 => (uint)(d[at] | (d[at + 1] << 8) | (d[at + 2] << 16)),
                        _ => (uint)(d[at] | (d[at + 1] << 8) | (d[at + 2] << 16) | (d[at + 3] << 24)),
                    };
                    r = Channel(px, mR); g = Channel(px, mG); b = Channel(px, mB);
                    if (mA != 0) a = Channel(px, mA);
                }
                if (a != 255)
                {
                    // Composite over white - the platform codec path draws onto a cleared
                    // bitmap, so a transparent BMP has always reached the page that way.
                    var inv = 255 - a;
                    r = (byte)((r * a + 255 * inv + 127) / 255);
                    g = (byte)((g * a + 255 * inv + 127) / 255);
                    b = (byte)((b * a + 255 * inv + 127) / 255);
                }
                rgb[dst + x * 3] = r; rgb[dst + x * 3 + 1] = g; rgb[dst + x * 3 + 2] = b;
            }
        }
        return PngEncoder.Encode(rgb, width, height, colorType: 2);
    }

    /// <summary>One channel out of a packed pixel, scaled up to a full byte so a 5-bit
    /// channel reaches 255 rather than 248.</summary>
    private static byte Channel(uint px, uint mask)
    {
        if (mask == 0) return 0;
        var shift = 0;
        while ((mask & (1u << shift)) == 0) shift++;
        var bits = 0;
        while (shift + bits < 32 && (mask & (1u << (shift + bits))) != 0) bits++;
        var v = (px & mask) >> shift;
        if (bits >= 8) return (byte)(v >> (bits - 8));
        var max = (1u << bits) - 1;
        return (byte)(max == 0 ? 0 : v * 255 / max);
    }

    private static int U16(byte[] d, int o) => d[o] | (d[o + 1] << 8);

    private static uint U32(byte[] d, int o) =>
        (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
}
