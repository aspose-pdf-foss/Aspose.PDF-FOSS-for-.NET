using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Quantize a 24-bit RGB buffer to a 16-colour palette for TIFF
    /// <see cref="ColorDepth.Format4bpp"/>. Returns one palette index per
    /// pixel (each 0..15) and a 3 × 16-short /ColorMap in TIFF 6.0 layout
    /// (<c>R0..R15 G0..G15 B0..B15</c>).
    ///
    /// Like the 8bpp path this is adaptive: when the image uses ≤ 16 distinct
    /// colours (typical for vector PDFs) the palette captures every one with
    /// zero loss. Otherwise the 16 most frequent colours become the palette
    /// and every other pixel maps to its nearest entry by squared RGB
    /// distance.
    /// </summary>
    public static (byte[] indices, ushort[] colorMap) QuantizeTo4bpp(byte[] rgb, int width, int height)
    {
        var total = width * height;

        // Tally colour frequencies (24-bit RGB key fits in int).
        var freq = new Dictionary<int, int>();
        var src = 0;
        for (var i = 0; i < total; i++, src += 3)
        {
            var key = (rgb[src] << 16) | (rgb[src + 1] << 8) | rgb[src + 2];
            freq.TryGetValue(key, out var c);
            freq[key] = c + 1;
        }

        // Palette: every colour if ≤ 16 (lossless), else the 16 most frequent.
        var palette = freq.Count <= 16
            ? freq.Keys.ToList()
            : freq.OrderByDescending(k => k.Value).Take(16).Select(k => k.Key).ToList();

        // /ColorMap is 3 × 16 shorts; unused slots stay 0 (black) but are never
        // indexed. Scale each 0..255 sample to 0..65535 by ×257 so it round-trips.
        var colorMap = new ushort[3 * 16];
        for (var p = 0; p < palette.Count; p++)
        {
            var key = palette[p];
            colorMap[p] = (ushort)(((key >> 16) & 0xFF) * 257);       // R block
            colorMap[p + 16] = (ushort)(((key >> 8) & 0xFF) * 257);   // G block
            colorMap[p + 32] = (ushort)((key & 0xFF) * 257);          // B block
        }

        // Exact lookup for palette colours; nearest-match (cached) for the rest.
        var exact = new Dictionary<int, byte>(palette.Count);
        for (var p = 0; p < palette.Count; p++) exact[palette[p]] = (byte)p;

        var indices = new byte[total];
        var nearestCache = new Dictionary<int, byte>();
        src = 0;
        for (var i = 0; i < total; i++, src += 3)
        {
            var key = (rgb[src] << 16) | (rgb[src + 1] << 8) | rgb[src + 2];
            if (exact.TryGetValue(key, out var idx))
            {
                indices[i] = idx;
                continue;
            }
            if (!nearestCache.TryGetValue(key, out idx))
            {
                idx = NearestIndex(palette, rgb[src], rgb[src + 1], rgb[src + 2]);
                nearestCache[key] = idx;
            }
            indices[i] = idx;
        }
        return (indices, colorMap);
    }

    private static byte NearestIndex(List<int> palette, byte r, byte g, byte b)
    {
        var best = 0;
        var bestDist = int.MaxValue;
        for (var p = 0; p < palette.Count; p++)
        {
            var key = palette[p];
            var dr = r - ((key >> 16) & 0xFF);
            var dg = g - ((key >> 8) & 0xFF);
            var db = b - (key & 0xFF);
            var d = dr * dr + dg * dg + db * db;
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return (byte)best;
    }
}
