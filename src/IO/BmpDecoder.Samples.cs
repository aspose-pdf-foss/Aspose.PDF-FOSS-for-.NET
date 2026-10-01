namespace Aspose.Pdf.IO;

/// <summary>
/// A BMP read into samples instead of PNG: palette images keep their indices (for a caller that
/// writes an Indexed colour space), direct-colour images become 8-bit RGB, and RLE8 / RLE4
/// bitmaps are decoded. Rows come out top row first whatever the file's order.
/// </summary>
internal static partial class BmpDecoder
{
    /// <summary>Why a bitmap could not be read into samples.</summary>
    internal enum BmpFailure
    {
        None,
        NotBmp,
        UnsupportedHeader,
        UnsupportedCompression,
        UnsupportedDepth,
        Truncated,
    }

    /// <summary>A bitmap's header and samples.</summary>
    internal sealed class BmpSamples
    {
        public int Width;
        public int Height;
        public int BitCount;
        public int HeaderSize;
        public int Compression;
        public bool TopDown;
        public int XPelsPerMeter;
        public int YPelsPerMeter;

        /// <summary>RGB triplets, for a palette bitmap.</summary>
        public byte[]? Palette;

        /// <summary>Palette bitmaps: packed index rows of (width * bits + 7) / 8 bytes. Direct
        /// colour: RGB rows of width * 3 bytes.</summary>
        public byte[] Samples = System.Array.Empty<byte>();
    }

    private const int FileHeaderSize = 14;
    private const int CoreHeaderSize = 12;
    private const int InfoHeaderSize = 40;
    private const int BiRgb = 0;
    private const int BiRle8 = 1;
    private const int BiRle4 = 2;
    private const int BiBitFields = 3;

    /// <summary>
    /// Reads a bitmap's samples. <paramref name="noHeader"/> means the bytes start at the info
    /// header, with the palette and pixels straight after it. The palette holds the entries the
    /// header counts (all 2^bits when it counts none), but never more than fit before the pixels.
    /// </summary>
    internal static BmpSamples? DecodeSamples(byte[] d, bool noHeader, out BmpFailure failure)
    {
        try
        {
            return DecodeSamplesCore(d, noHeader, out failure);
        }
        catch (System.IndexOutOfRangeException)
        {
            failure = BmpFailure.Truncated;
            return null;
        }
    }

    private static BmpSamples? DecodeSamplesCore(byte[] d, bool noHeader, out BmpFailure failure)
    {
        failure = BmpFailure.None;
        var start = noHeader ? 0 : FileHeaderSize;
        if (!noHeader && !(d is { Length: >= FileHeaderSize + 4 } && d[0] == 0x42 && d[1] == 0x4D))
        {
            failure = BmpFailure.NotBmp;
            return null;
        }
        var dataOffset = noHeader ? -1 : (int)U32(d, 10);
        var s = new BmpSamples { HeaderSize = (int)U32(d, start) };
        int clrUsed = 0, entry;
        if (s.HeaderSize == CoreHeaderSize)
        {
            s.Width = U16(d, start + 4);
            s.Height = (short)U16(d, start + 6);
            s.BitCount = U16(d, start + 10);
            entry = 3;
        }
        else if (s.HeaderSize >= InfoHeaderSize)
        {
            s.Width = (int)U32(d, start + 4);
            s.Height = (int)U32(d, start + 8);
            s.BitCount = U16(d, start + 14);
            s.Compression = (int)U32(d, start + 16);
            s.XPelsPerMeter = (int)U32(d, start + 24);
            s.YPelsPerMeter = (int)U32(d, start + 28);
            clrUsed = (int)U32(d, start + 32);
            entry = 4;
        }
        else
        {
            failure = BmpFailure.UnsupportedHeader;
            return null;
        }
        if (s.Compression is not (BiRgb or BiRle8 or BiRle4 or BiBitFields))
        {
            failure = BmpFailure.UnsupportedCompression;
            return null;
        }
        if (s.BitCount is not (1 or 4 or 8 or 16 or 24 or 32))
        {
            failure = BmpFailure.UnsupportedDepth;
            return null;
        }
        s.TopDown = s.Height < 0;
        if (s.TopDown) s.Height = -s.Height;

        // Bit fields: inside a V4/V5 header, else the three words after a plain one.
        var at = start + s.HeaderSize;
        uint mR, mG, mB;
        if (s.Compression == BiBitFields && s.HeaderSize >= 52)
        {
            mR = U32(d, start + 40); mG = U32(d, start + 44); mB = U32(d, start + 48);
        }
        else if (s.Compression == BiBitFields)
        {
            mR = U32(d, at); mG = U32(d, at + 4); mB = U32(d, at + 8);
            at += 12;
        }
        else if (s.BitCount == 16) { mR = 0x7C00; mG = 0x03E0; mB = 0x001F; }
        else { mR = 0x00FF0000; mG = 0x0000FF00; mB = 0x000000FF; }

        if (s.BitCount <= 8)
        {
            var count = clrUsed > 0 ? clrUsed : 1 << s.BitCount;
            if (dataOffset > 0) count = System.Math.Min(count, (dataOffset - at) / entry);
            if (count <= 0 || at + count * entry > d.Length)
            {
                failure = BmpFailure.Truncated;
                return null;
            }
            s.Palette = new byte[count * 3];
            for (var i = 0; i < count; i++)
            {
                s.Palette[i * 3] = d[at + i * entry + 2];
                s.Palette[i * 3 + 1] = d[at + i * entry + 1];
                s.Palette[i * 3 + 2] = d[at + i * entry];
            }
            at += count * entry;
        }
        var pixels = dataOffset > 0 ? dataOffset : at;

        if (s.Compression is BiRle8 or BiRle4)
        {
            if (s.BitCount != (s.Compression == BiRle8 ? 8 : 4))
            {
                failure = BmpFailure.UnsupportedCompression;
                return null;
            }
            s.Samples = PackIndices(DecodeRle(d, pixels, s.Compression == BiRle8, s.Width, s.Height), s, bottomUpSource: true);
            return s;
        }

        var stride = (s.Width * s.BitCount + 31) / 32 * 4;
        if ((long)pixels + (long)stride * s.Height > d.Length)
        {
            failure = BmpFailure.Truncated;
            return null;
        }
        if (s.BitCount <= 8)
        {
            var rowBytes = (s.Width * s.BitCount + 7) / 8;
            s.Samples = new byte[rowBytes * s.Height];
            for (var y = 0; y < s.Height; y++)
            {
                var source = pixels + (s.TopDown ? y : s.Height - 1 - y) * stride;
                System.Array.Copy(d, source, s.Samples, y * rowBytes, rowBytes);
            }
            return s;
        }

        s.Samples = new byte[s.Width * 3 * s.Height];
        var bytesPer = s.BitCount / 8;
        for (var y = 0; y < s.Height; y++)
        {
            var source = pixels + (s.TopDown ? y : s.Height - 1 - y) * stride;
            for (var x = 0; x < s.Width; x++)
            {
                var p = source + x * bytesPer;
                var to = (y * s.Width + x) * 3;
                if (bytesPer == 3)
                {
                    s.Samples[to] = d[p + 2];
                    s.Samples[to + 1] = d[p + 1];
                    s.Samples[to + 2] = d[p];
                    continue;
                }
                var px = bytesPer == 2 ? (uint)(d[p] | (d[p + 1] << 8)) : U32(d, p);
                s.Samples[to] = Channel(px, mR);
                s.Samples[to + 1] = Channel(px, mG);
                s.Samples[to + 2] = Channel(px, mB);
            }
        }
        return s;
    }

    /// <summary>
    /// RLE8 / RLE4 into one index per pixel, bottom row first as the file codes them: runs,
    /// absolute runs (padded to a word), end of line, a delta move, end of bitmap. Pixels a
    /// delta or an early end skips are index 0.
    /// </summary>
    private static byte[] DecodeRle(byte[] d, int p, bool eight, int width, int height)
    {
        var indices = new byte[width * height];
        int x = 0, y = 0;
        while (p + 1 < d.Length && y < height)
        {
            int count = d[p++];
            int value = d[p++];
            if (count > 0)
            {
                for (var i = 0; i < count; i++, x++)
                {
                    var v = eight ? value : i % 2 == 0 ? value >> 4 : value & 0x0f;
                    if (x < width) indices[y * width + x] = (byte)v;
                }
                continue;
            }
            switch (value)
            {
                case 0:
                    x = 0;
                    y++;
                    break;
                case 1:
                    return indices;
                case 2:
                    if (p + 1 >= d.Length) return indices;
                    x += d[p++];
                    y += d[p++];
                    break;
                default:
                    var bytes = eight ? value : (value + 1) / 2;
                    for (var i = 0; i < value && p + (eight ? i : i / 2) < d.Length; i++, x++)
                    {
                        var b = d[p + (eight ? i : i / 2)];
                        var v = eight ? b : i % 2 == 0 ? b >> 4 : b & 0x0f;
                        if (x < width && y < height) indices[y * width + x] = (byte)v;
                    }
                    p += bytes + (bytes & 1);
                    break;
            }
        }
        return indices;
    }

    /// <summary>One index per pixel packed at the bitmap's depth, top row first.</summary>
    private static byte[] PackIndices(byte[] indices, BmpSamples s, bool bottomUpSource)
    {
        var rowBytes = (s.Width * s.BitCount + 7) / 8;
        var packed = new byte[rowBytes * s.Height];
        for (var y = 0; y < s.Height; y++)
        {
            var target = bottomUpSource && !s.TopDown ? s.Height - 1 - y : y;
            for (var x = 0; x < s.Width; x++)
            {
                var v = indices[y * s.Width + x];
                if (s.BitCount == 8) packed[target * rowBytes + x] = v;
                else packed[target * rowBytes + x / 2] |= (byte)(x % 2 == 0 ? v << 4 : v & 0x0f);
            }
        }
        return packed;
    }

    /// <summary>An RLE bitmap as PNG: its indices looked up in its palette.</summary>
    private static byte[]? RleAsPng(byte[] d)
    {
        var s = DecodeSamples(d, false, out _);
        if (s?.Palette is null) return null;
        var rgb = new byte[(long)s.Width * s.Height * 3];
        var rowBytes = (s.Width * s.BitCount + 7) / 8;
        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++)
            {
                var b = s.Samples[y * rowBytes + (s.BitCount == 8 ? x : x / 2)];
                var idx = s.BitCount == 8 ? b : x % 2 == 0 ? b >> 4 : b & 0x0f;
                if (idx * 3 + 2 >= s.Palette.Length) continue;
                var to = (y * s.Width + x) * 3;
                rgb[to] = s.Palette[idx * 3];
                rgb[to + 1] = s.Palette[idx * 3 + 1];
                rgb[to + 2] = s.Palette[idx * 3 + 2];
            }
        }
        return PngEncoder.Encode(rgb, s.Width, s.Height, colorType: 2);
    }
}
