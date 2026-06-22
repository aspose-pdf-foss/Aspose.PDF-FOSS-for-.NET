namespace Aspose.Pdf.Devices;

/// <summary>
/// Quantizes a 24-bit RGB image to an 8-bit palette-indexed image for TIFF
/// output with <see cref="ColorDepth.Format8bpp"/>.
///
/// Two paths:
/// - If the image has ≤ 256 unique colours (typical for vector PDFs —
///   text, charts, form fills), an adaptive palette captures every one
///   with zero loss. This is what Acrobat's TIFF export does for the
///   common case.
/// - Otherwise, fall back to a uniform 3-3-2 (R-G-B) colour cube.
/// </summary>
internal static class TiffPaletteQuantizer
{
    /// <summary>
    /// Build an adaptive palette from the image's unique RGB colours.
    /// Returns (indexed bytes, 768-short colormap) if the image fits in
    /// 256 colours; otherwise returns null so callers can fall back.
    /// </summary>
    public static (byte[] indexed, ushort[] colorMap)? TryQuantizeAdaptive(byte[] rgb, int width, int height)
    {
        var total = width * height;
        // Pack RGB into a 24-bit key. Dictionary&lt;int, byte&gt; is fine — the
        // 24-bit keys all fit in int.
        var table = new Dictionary<int, byte>(256);
        var indexed = new byte[total];
        var src = 0;
        for (var i = 0; i < total; i++, src += 3)
        {
            var key = (rgb[src] << 16) | (rgb[src + 1] << 8) | rgb[src + 2];
            if (!table.TryGetValue(key, out var idx))
            {
                if (table.Count >= 256) return null; // too many colours → caller falls back.
                idx = (byte)table.Count;
                table[key] = idx;
            }
            indexed[i] = idx;
        }

        var colorMap = new ushort[3 * 256];
        foreach (var (key, idx) in table)
        {
            var r = (byte)((key >> 16) & 0xFF);
            var g = (byte)((key >> 8) & 0xFF);
            var b = (byte)(key & 0xFF);
            colorMap[idx] = (ushort)(r * 257);            // R plane
            colorMap[idx + 256] = (ushort)(g * 257);      // G plane
            colorMap[idx + 512] = (ushort)(b * 257);      // B plane
        }
        return (indexed, colorMap);
    }

    /// <summary>
    /// Returns the 256-entry RGB palette expanded to 16-bit shorts, in
    /// <c>R0 R1 ... R255 G0 ... G255 B0 ... B255</c> order (TIFF 6.0
    /// /ColorMap layout). Use with <see cref="QuantizeRgbTo8bpp"/> — both
    /// share the same 3-3-2 index convention.
    /// </summary>
    public static ushort[] BuildColorMap332()
    {
        var map = new ushort[3 * 256];
        for (var i = 0; i < 256; i++)
        {
            var rBits = (i >> 5) & 0x07; // top 3 bits
            var gBits = (i >> 2) & 0x07; // middle 3 bits
            var bBits = i & 0x03;        // bottom 2 bits

            var r = (byte)((rBits * 255 + 3) / 7);
            var g = (byte)((gBits * 255 + 3) / 7);
            var b = (byte)((bBits * 255 + 1) / 3);

            // TIFF colormap is 16-bit unsigned per sample. Scale 0..255 to
            // 0..65535 by multiplying by 257 (=65535/255) so values
            // round-trip exactly.
            map[i] = (ushort)(r * 257);              // R channel block
            map[i + 256] = (ushort)(g * 257);        // G channel block
            map[i + 512] = (ushort)(b * 257);        // B channel block
        }
        return map;
    }

    /// <summary>
    /// Quantize an <c>rgb.Length = w * h * 3</c> buffer to a
    /// <c>w * h</c> palette-index buffer. Each index refers into the
    /// <see cref="BuildColorMap332"/> palette.
    /// </summary>
    public static byte[] QuantizeRgbTo8bpp(byte[] rgb, int width, int height)
    {
        var total = width * height;
        var indexed = new byte[total];
        var src = 0;
        for (var i = 0; i < total; i++, src += 3)
        {
            // Top 3 bits of R, top 3 bits of G, top 2 bits of B.
            var rBits = rgb[src] >> 5;
            var gBits = rgb[src + 1] >> 5;
            var bBits = rgb[src + 2] >> 6;
            indexed[i] = (byte)((rBits << 5) | (gBits << 2) | bBits);
        }
        return indexed;
    }
}
