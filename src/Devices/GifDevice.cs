using System.Collections.Generic;

namespace Aspose.Pdf.Devices;

/// <summary>
/// Renders PDF document pages into GIF image format. The page is rasterised to
/// RGB, reduced to a 256-colour palette (median-cut quantisation) and encoded as
/// a GIF89a image with LZW compression.
/// </summary>
public sealed class GifDevice : ImageDevice
{
    public GifDevice(IPageRenderer renderer) : base(renderer) { }
    public GifDevice(IPageRenderer renderer, Resolution resolution) : base(renderer, resolution) { }
    public GifDevice() : base() { }
    public GifDevice(Resolution resolution) : base(resolution) { }
    public GifDevice(int width, int height) : base(width, height) { }
    public GifDevice(int width, int height, Resolution resolution) : base(width, height, resolution) { }
    public GifDevice(Aspose.Pdf.PageSize pageSize) : base(pageSize) { }
    public GifDevice(Aspose.Pdf.PageSize pageSize, Resolution resolution) : base(pageSize, resolution) { }

    /// <inheritdoc />
    public override void Process(Page page, Stream output)
    {
        var rgba = RenderPage(page);
        var gif = EncodeGif(rgba.Data, rgba.Width, rgba.Height);
        output.Write(gif, 0, gif.Length);
    }

    private static byte[] EncodeGif(byte[] rgba, int width, int height)
    {
        // Composite onto white and reduce to ≤256 colours. The index map holds one
        // palette index per pixel (row-major, top-to-bottom).
        var indices = Quantize(rgba, width, height, out var palette, out var colorCount);

        // GIF colour table size must be a power of two (2..256); the LZW minimum
        // code size is its log2, never below 2.
        int tableSize = 2;
        int minCodeSize = 1;
        while (tableSize < colorCount) { tableSize <<= 1; minCodeSize++; }
        if (minCodeSize < 2) { minCodeSize = 2; tableSize = 4; }

        using var ms = new MemoryStream();

        // Header: "GIF89a"
        ms.Write(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' }, 0, 6);

        // Logical screen descriptor.
        WriteLE16(ms, width);
        WriteLE16(ms, height);
        // Packed: global colour table flag (1), colour resolution (7 → 0b111<<4),
        // sort flag (0), size of global colour table (minCodeSize-1).
        ms.WriteByte((byte)(0x80 | 0x70 | (minCodeSize - 1)));
        ms.WriteByte(0); // background colour index
        ms.WriteByte(0); // pixel aspect ratio

        // Global colour table (tableSize entries, RGB).
        for (int i = 0; i < tableSize; i++)
        {
            if (i < colorCount)
            {
                ms.WriteByte(palette[i * 3]);
                ms.WriteByte(palette[i * 3 + 1]);
                ms.WriteByte(palette[i * 3 + 2]);
            }
            else { ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0); }
        }

        // Image descriptor.
        ms.WriteByte(0x2C);
        WriteLE16(ms, 0); WriteLE16(ms, 0); // left, top
        WriteLE16(ms, width); WriteLE16(ms, height);
        ms.WriteByte(0); // no local colour table, not interlaced

        // LZW-compressed image data.
        ms.WriteByte((byte)minCodeSize);
        var lzw = LzwCompress(indices, minCodeSize);
        // Emit as sub-blocks of at most 255 bytes.
        int offset = 0;
        while (offset < lzw.Length)
        {
            int n = System.Math.Min(255, lzw.Length - offset);
            ms.WriteByte((byte)n);
            ms.Write(lzw, offset, n);
            offset += n;
        }
        ms.WriteByte(0); // block terminator

        ms.WriteByte(0x3B); // trailer
        return ms.ToArray();
    }

    private static void WriteLE16(Stream s, int v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
    }

    // ── Quantisation (median cut) ───────────────────────────────────────────

    /// <summary>Composite RGBA over white, build the colour histogram, reduce to
    /// at most 256 colours and return one palette index per pixel.</summary>
    private static byte[] Quantize(byte[] rgba, int width, int height,
        out byte[] palette, out int colorCount)
    {
        int pixelCount = width * height;
        // Composite over white and pack RGB into an int key.
        var packed = new int[pixelCount];
        var histogram = new Dictionary<int, int>();
        for (int p = 0; p < pixelCount; p++)
        {
            int si = p * 4;
            int a = rgba[si + 3];
            int r = rgba[si], g = rgba[si + 1], b = rgba[si + 2];
            if (a < 255)
            {
                r = (r * a + 255 * (255 - a)) / 255;
                g = (g * a + 255 * (255 - a)) / 255;
                b = (b * a + 255 * (255 - a)) / 255;
            }
            int key = (r << 16) | (g << 8) | b;
            packed[p] = key;
            histogram[key] = histogram.TryGetValue(key, out var c) ? c + 1 : 1;
        }

        // Unique colours, each with its population.
        var colors = new List<(int rgb, int count)>(histogram.Count);
        foreach (var kv in histogram) colors.Add((kv.Key, kv.Value));

        const int maxColors = 256;
        List<(int rgb, int count)>[] boxes;
        if (colors.Count <= maxColors)
        {
            // One box per colour so each maps to itself exactly.
            boxes = new List<(int rgb, int count)>[colors.Count];
            for (int i = 0; i < colors.Count; i++)
                boxes[i] = new List<(int rgb, int count)> { colors[i] };
        }
        else
        {
            boxes = MedianCut(colors, maxColors);
        }

        colorCount = boxes.Length;
        palette = new byte[colorCount * 3];
        // Map a packed colour to its palette index. Colours that landed in a box
        // resolve directly; everything else falls back to nearest-palette search.
        var exact = new Dictionary<int, int>();
        for (int i = 0; i < colorCount; i++)
        {
            long sr = 0, sg = 0, sb = 0, sw = 0;
            foreach (var (rgb, count) in boxes[i])
            {
                sr += ((rgb >> 16) & 0xFF) * (long)count;
                sg += ((rgb >> 8) & 0xFF) * (long)count;
                sb += (rgb & 0xFF) * (long)count;
                sw += count;
                exact[rgb] = i;
            }
            if (sw == 0) sw = 1;
            palette[i * 3] = (byte)(sr / sw);
            palette[i * 3 + 1] = (byte)(sg / sw);
            palette[i * 3 + 2] = (byte)(sb / sw);
        }

        // Build the per-pixel index map, caching nearest-colour lookups by colour.
        var cache = new Dictionary<int, byte>(exact.Count);
        var indices = new byte[pixelCount];
        for (int p = 0; p < pixelCount; p++)
        {
            int key = packed[p];
            if (!cache.TryGetValue(key, out var idx))
            {
                idx = exact.TryGetValue(key, out var ei) ? (byte)ei : Nearest(palette, colorCount, key);
                cache[key] = idx;
            }
            indices[p] = idx;
        }
        return indices;
    }

    private static byte Nearest(byte[] palette, int colorCount, int rgb)
    {
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        int best = 0; long bestDist = long.MaxValue;
        for (int i = 0; i < colorCount; i++)
        {
            int dr = r - palette[i * 3], dg = g - palette[i * 3 + 1], db = b - palette[i * 3 + 2];
            long d = (long)dr * dr + (long)dg * dg + (long)db * db;
            if (d < bestDist) { bestDist = d; best = i; if (d == 0) break; }
        }
        return (byte)best;
    }

    /// <summary>Median-cut colour quantisation: repeatedly split the box with the
    /// widest colour axis at the population median until the target colour count.</summary>
    private static List<(int rgb, int count)>[] MedianCut(List<(int rgb, int count)> colors, int maxColors)
    {
        var boxes = new List<List<(int rgb, int count)>> { colors };
        while (boxes.Count < maxColors)
        {
            // Pick the box with the largest extent on any channel.
            int bestBox = -1, bestAxis = 0; int bestRange = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                Extents(boxes[i], out var rr, out var gr, out var br);
                if (rr >= gr && rr >= br && rr > bestRange) { bestRange = rr; bestBox = i; bestAxis = 0; }
                else if (gr >= rr && gr >= br && gr > bestRange) { bestRange = gr; bestBox = i; bestAxis = 1; }
                else if (br > bestRange) { bestRange = br; bestBox = i; bestAxis = 2; }
            }
            if (bestBox < 0) break; // nothing splittable

            var box = boxes[bestBox];
            int shift = bestAxis == 0 ? 16 : bestAxis == 1 ? 8 : 0;
            box.Sort((a, b) => ((a.rgb >> shift) & 0xFF).CompareTo((b.rgb >> shift) & 0xFF));
            long total = 0; foreach (var c in box) total += c.count;
            long half = total / 2, acc = 0; int split = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                acc += box[i].count;
                if (acc >= half) { split = i + 1; break; }
            }
            var left = box.GetRange(0, split);
            var right = box.GetRange(split, box.Count - split);
            boxes[bestBox] = left;
            boxes.Add(right);
        }
        return boxes.ToArray();
    }

    private static void Extents(List<(int rgb, int count)> box, out int rRange, out int gRange, out int bRange)
    {
        int rMin = 255, rMax = 0, gMin = 255, gMax = 0, bMin = 255, bMax = 0;
        foreach (var (rgb, _) in box)
        {
            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            if (r < rMin) rMin = r; if (r > rMax) rMax = r;
            if (g < gMin) gMin = g; if (g > gMax) gMax = g;
            if (b < bMin) bMin = b; if (b > bMax) bMax = b;
        }
        rRange = rMax - rMin; gRange = gMax - gMin; bRange = bMax - bMin;
    }

    // ── LZW (GIF variant: LSB-first, variable code width) ───────────────────

    private static byte[] LzwCompress(byte[] indices, int minCodeSize)
    {
        int clearCode = 1 << minCodeSize;
        int eoiCode = clearCode + 1;

        var output = new List<byte>();
        int bitBuffer = 0, bitCount = 0;
        void Emit(int code, int codeWidth)
        {
            bitBuffer |= code << bitCount;
            bitCount += codeWidth;
            while (bitCount >= 8)
            {
                output.Add((byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }

        var dict = new Dictionary<int, int>();
        void ResetDict()
        {
            dict.Clear();
            // Entries 0..clearCode-1 are the literals; clearCode/eoiCode reserved.
        }

        int codeWidth = minCodeSize + 1;
        int nextCode = eoiCode + 1;
        ResetDict();
        Emit(clearCode, codeWidth);

        if (indices.Length == 0)
        {
            Emit(eoiCode, codeWidth);
            if (bitCount > 0) output.Add((byte)(bitBuffer & 0xFF));
            return output.ToArray();
        }

        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int k = indices[i];
            int key = (prefix << 8) | k;
            if (dict.TryGetValue(key, out var combined))
            {
                prefix = combined;
            }
            else
            {
                Emit(prefix, codeWidth);
                dict[key] = nextCode++;
                // Increase the code width once the table has grown past the current
                // width's capacity. The decoder builds its table one entry behind the
                // encoder, so the switch happens when the next free code reaches
                // 2^width + 1 (not 2^width) — the canonical GIF LZW off-by-one.
                if (nextCode == (1 << codeWidth) + 1 && codeWidth < 12)
                    codeWidth++;
                prefix = k;
                if (nextCode == 4096)
                {
                    Emit(clearCode, codeWidth);
                    ResetDict();
                    codeWidth = minCodeSize + 1;
                    nextCode = eoiCode + 1;
                }
            }
        }
        Emit(prefix, codeWidth);
        Emit(eoiCode, codeWidth);
        if (bitCount > 0) output.Add((byte)(bitBuffer & 0xFF));
        return output.ToArray();
    }
}
