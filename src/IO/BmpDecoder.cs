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
internal static partial class BmpDecoder
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
        var bd = new BmpDecodeState();
        bd.d = d;
        if (!IsBmp(bd.d)) return null;
        bd.dataOffset = (int)U32(bd.d, 10);
        bd.dibSize = (int)U32(bd.d, 14);
        bd.compression = 0;
        bd.clrUsed = 0;
        if (bd.dibSize == 12)
        {
            bd.width = U16(bd.d, 18);
            bd.height = U16(bd.d, 20);
            bd.bitCount = U16(bd.d, 24);
            bd.paletteEntry = 3;                       // BITMAPCOREHEADER palette is RGB triplets
        }
        else if (bd.dibSize >= 40)
        {
            bd.width = (int)U32(bd.d, 18);
            bd.height = (int)U32(bd.d, 22);
            bd.bitCount = U16(bd.d, 28);
            bd.compression = (int)U32(bd.d, 30);
            bd.clrUsed = (int)U32(bd.d, 46);
            bd.paletteEntry = 4;
        }
        else return null;

        bd.topDown = bd.height < 0;
        if (bd.topDown) bd.height = -bd.height;
        if (bd.width <= 0 || bd.height <= 0 || bd.width > 65535 || bd.height > 65535) return null;
        if ((long)bd.width * bd.height > 268_435_456) return null;          // 256M px sanity cap
        if (bd.bitCount is not (1 or 4 or 8 or 16 or 24 or 32)) return null;
        if (bd.compression is 1 or 2) return RleAsPng(bd.d);
        if (bd.compression is not (0 or 3)) return null;

        // Channel masks: BI_BITFIELDS states them, either inside a V4/V5 header or in the
        // three words right after a plain one. BI_RGB implies the classic packing.
        uint mR;
        uint mG;
        uint mB;
        uint mA = 0;
        if (bd.compression == 3 && bd.dibSize >= 52)
        {
            mR = U32(bd.d, 54); mG = U32(bd.d, 58); mB = U32(bd.d, 62);
            if (bd.dibSize >= 56) mA = U32(bd.d, 66);
        }
        else if (bd.compression == 3 && 14 + bd.dibSize + 12 <= bd.d.Length)
        {
            mR = U32(bd.d, 14 + bd.dibSize); mG = U32(bd.d, 18 + bd.dibSize); mB = U32(bd.d, 22 + bd.dibSize);
        }
        else if (bd.bitCount == 16) { mR = 0x7C00; mG = 0x03E0; mB = 0x001F; }
        else { mR = 0x00FF0000; mG = 0x0000FF00; mB = 0x000000FF; }

        bd.palette = null;
        if (bd.bitCount <= 8)
        {
            var count = bd.clrUsed > 0 ? bd.clrUsed : 1 << bd.bitCount;
            var at = 14 + bd.dibSize;
            if (count <= 0 || at + count * bd.paletteEntry > bd.d.Length) return null;
            bd.palette = new byte[count * 3];
            for (var i = 0; i < count; i++)
            {
                // Stored BLUE, GREEN, RED (then a pad byte in the 4-byte form).
                bd.palette[i * 3]     = bd.d[at + i * bd.paletteEntry + 2];
                bd.palette[i * 3 + 1] = bd.d[at + i * bd.paletteEntry + 1];
                bd.palette[i * 3 + 2] = bd.d[at + i * bd.paletteEntry];
            }
        }

        bd.rowBytes = (bd.width * bd.bitCount + 31) / 32 * 4;              // rows pad to 4 bytes
        if (bd.dataOffset <= 0 || bd.dataOffset >= bd.d.Length) return null;
        if ((long)bd.dataOffset + (long)bd.rowBytes * bd.height > bd.d.Length) return null;

        bd.rgb = new byte[(long)bd.width * bd.height * 3];
        for (var y = 0; y < bd.height; y++)
        {
            DecodeBmpRow(bd, y, mR, mG, mB, mA);
        }
        return PngEncoder.Encode(bd.rgb, bd.width, bd.height, colorType: 2);
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
