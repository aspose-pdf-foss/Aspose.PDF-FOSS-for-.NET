
namespace Aspose.Pdf.IO;

internal static partial class BmpDecoder
{
    /// <summary>The stages of the BMP decode: the palette read and one image row at a time.</summary>
    private static void DecodeBmpRow(BmpDecodeState bd, int y, uint mR, uint mG, uint mB, uint mA)
    {
        var srcRow = bd.dataOffset + (bd.topDown ? y : bd.height - 1 - y) * bd.rowBytes;
        var dst = y * bd.width * 3;
        for (var x = 0; x < bd.width; x++)
        {
            byte r, g, b, a = 255;
            if (bd.bitCount <= 8)
            {
                var bitPos = x * bd.bitCount;
                var idx = bd.bitCount switch
                {
                    8 => bd.d[srcRow + x],
                    4 => (bd.d[srcRow + bitPos / 8] >> (bitPos % 8 == 0 ? 4 : 0)) & 0x0F,
                    _ => (bd.d[srcRow + bitPos / 8] >> (7 - bitPos % 8)) & 0x01,
                };
                if (bd.palette is null || idx * 3 + 2 >= bd.palette.Length) { r = g = b = 0; }
                else { r = bd.palette[idx * 3]; g = bd.palette[idx * 3 + 1]; b = bd.palette[idx * 3 + 2]; }
            }
            else
            {
                var bytesPer = bd.bitCount / 8;
                var at = srcRow + x * bytesPer;
                uint px = bytesPer switch
                {
                    2 => (uint)(bd.d[at] | (bd.d[at + 1] << 8)),
                    3 => (uint)(bd.d[at] | (bd.d[at + 1] << 8) | (bd.d[at + 2] << 16)),
                    _ => (uint)(bd.d[at] | (bd.d[at + 1] << 8) | (bd.d[at + 2] << 16) | (bd.d[at + 3] << 24)),
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
            bd.rgb[dst + x * 3] = r; bd.rgb[dst + x * 3 + 1] = g; bd.rgb[dst + x * 3 + 2] = b;
        }
    }
}
