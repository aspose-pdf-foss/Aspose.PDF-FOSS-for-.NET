namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Encoder
{
    /// <summary>
    /// The JBIG2 stream of <paramref name="layout"/> written again so it decodes to
    /// <paramref name="painted"/> (packed rows, 1 = black), which differs from the stream's page only
    /// inside <paramref name="changed"/>. What lies clear of that box is copied as it was - symbol
    /// dictionaries, and every region that does not touch it - so a scan keeps its size. A text
    /// region touching the box is written again without the symbol instances that touch it (those
    /// refined in place move to the residual region below). A generic region touching the box goes
    /// into one residual generic region with the box cleared; any other region touching it is left
    /// out. One last generic region then replaces the box, and the places the dropped instances and
    /// regions covered, with the painted pixels. Nothing that touched the box is left in the stream.
    /// <para>Null when the stream cannot be rewritten so (a page of unknown size, intermediate
    /// regions), or when the result does not decode to <paramref name="painted"/> exactly - the caller
    /// then encodes the painted page whole.</para>
    /// </summary>
    public static byte[]? Rewrite(Jbig2Layout layout, byte[]? globals, byte[] painted,
        (int X, int Y, int Width, int Height) changed)
    {
        var data = layout.Data;
        var stride = (layout.PageWidth + 7) / 8;
        var patch = changed;
        var residual = new byte[painted.Length];
        (int X, int Y, int Width, int Height)? residualBox = null;
        int? residualCombination = null;
        var pageNumber = 1;
        var closed = false;
        using var output = new MemoryStream();

        // The residual region takes the layers it replaces combined as they were (OR or XOR); layers
        // combined differently cannot share it.
        bool AddResidual(int x, int y, int width, int height, byte[] bitmap, int combination)
        {
            if (combination is not (0 or 2) || residualCombination is { } known0 && known0 != combination) return false;
            residualCombination = combination;
            Combine(residual, stride, bitmap, x, y, width, height, combination == 2);
            var box = (x, y, width, height);
            residualBox = residualBox is { } known ? Union(known, box) : box;
            return true;
        }

        void Close()
        {
            var number = layout.Segments.Max(s => s.Number);
            if (residualBox is { } box && Clip(box, layout.PageWidth, layout.PageHeight) is { Width: > 0, Height: > 0 } kept)
            {
                Clear(residual, stride, Clip(changed, layout.PageWidth, layout.PageHeight));
                WriteSegment(output, ++number, ImmediateLosslessGenericRegion, pageNumber,
                    GenericRegion(residual, stride, kept, residualCombination ?? 0));
            }
            if (Clip(patch, layout.PageWidth, layout.PageHeight) is { Width: > 0, Height: > 0 } patched)
                WriteSegment(output, ++number, ImmediateLosslessGenericRegion, pageNumber,
                    GenericRegion(painted, stride, patched, Replace));
            closed = true;
        }

        foreach (var segment in layout.Segments)
        {
            var header = Slice(data, segment.HeaderStart, segment.DataStart);
            var body = Slice(data, segment.DataStart, segment.DataStart + segment.DataLength);
            switch (segment.Type)
            {
                case PageInformation:
                    pageNumber = PageOf(header);
                    body[16] |= CombinationOverridden;
                    Write(output, header, body);
                    continue;
                case EndOfPage or EndOfFile when !closed:
                    Close();
                    break;
            }
            if (segment.Region is not { } region || !Touches(region, changed))
            {
                Write(output, header, body);
                continue;
            }
            if (segment.Type is 4 or 36 or 40) return null;   // intermediate: feeds a later region
            if (segment.Type is ImmediateTextRegion or 7
                && layout.TextRegions.TryGetValue(segment.Number, out var text) && text.Reencodable
                && text.SymbolCombination == 0 && text.RegionCombination is 0 or 2)
            {
                var plain = new List<Jbig2Placement>();
                foreach (var instance in text.Instances)
                {
                    if (Touches((instance.X, instance.Y, instance.Width, instance.Height), changed))
                        patch = Union(patch, (instance.X, instance.Y, instance.Width, instance.Height));
                    else if (instance.Refined is { } bitmap)
                    {
                        if (!AddResidual(instance.X, instance.Y, instance.Width, instance.Height, bitmap, text.RegionCombination))
                            return null;
                    }
                    else plain.Add(instance);
                }
                // A region left with no instances goes; its black background, if it paints one,
                // comes back through the patch.
                if (plain.Count > 0) Write(output, header, TextRegion(region, text, plain));
                else if (text.DefaultPixel) patch = Union(patch, region);
                continue;
            }
            if (segment.Type is 38 or ImmediateLosslessGenericRegion
                && layout.GenericRegions.TryGetValue(segment.Number, out var generic))
            {
                // A generic region with no black pixel in the box holds nothing of it: it stays as it was.
                if (!InkInside(generic, changed)) Write(output, header, body);
                else if (!AddResidual(generic.X, generic.Y, generic.Width, generic.Height, generic.Bitmap, generic.Combination))
                    patch = Union(patch, region);
                continue;
            }
            patch = Union(patch, region);
        }
        if (!closed) Close();

        var rewritten = output.ToArray();
        var decoded = Jbig2Decoder.Decode(rewritten, globals);
        return decoded.AsSpan().SequenceEqual(painted) ? rewritten : null;
    }

    /// <summary>Whether a region's bitmap has a black pixel inside <paramref name="box"/> (page space).</summary>
    private static bool InkInside(Jbig2RegionBitmap region, (int X, int Y, int Width, int Height) box)
    {
        var rowBytes = (region.Width + 7) / 8;
        int left = Math.Max(box.X, region.X) - region.X, right = Math.Min(box.X + box.Width, region.X + region.Width) - region.X;
        int top = Math.Max(box.Y, region.Y) - region.Y, bottom = Math.Min(box.Y + box.Height, region.Y + region.Height) - region.Y;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
                if (((region.Bitmap[y * rowBytes + (x >> 3)] >> (7 - (x & 7))) & 1) != 0) return true;
        return false;
    }

    /// <summary>OR (or XOR) a bitmap (packed rows of its own width) into a page-wide one at (x, y).</summary>
    private static void Combine(byte[] page, int stride, byte[] bitmap, int x, int y, int width, int height, bool xor)
    {
        var rowBytes = (width + 7) / 8;
        var pageWidth = stride * 8;
        var pageHeight = page.Length / stride;
        for (var row = 0; row < height; row++)
        {
            var py = y + row;
            if ((uint)py >= (uint)pageHeight) continue;
            for (var column = 0; column < width; column++)
            {
                var px = x + column;
                if ((uint)px >= (uint)pageWidth) continue;
                if (((bitmap[row * rowBytes + (column >> 3)] >> (7 - (column & 7))) & 1) == 0) continue;
                if (xor) page[py * stride + (px >> 3)] ^= (byte)(0x80 >> (px & 7));
                else page[py * stride + (px >> 3)] |= (byte)(0x80 >> (px & 7));
            }
        }
    }

    private static void Clear(byte[] page, int stride, (int X, int Y, int Width, int Height) box)
    {
        for (var py = box.Y; py < box.Y + box.Height; py++)
            for (var px = box.X; px < box.X + box.Width; px++)
                page[py * stride + (px >> 3)] &= (byte)~(0x80 >> (px & 7));
    }

    /// <summary>The data of an immediate text region placing <paramref name="kept"/>: arithmetic
    /// coded, in strips of <see cref="StripRows"/> rows, each instance by its top-left corner, sorted by
    /// strip then column; every strip ends with an out-of-band IADS (T.88 §6.4.5).</summary>
    private static byte[] TextRegion((int X, int Y, int Width, int Height) region, Jbig2TextRegionPlacements text,
        List<Jbig2Placement> kept)
    {
        var coder = new MqEncoder();
        var dt = new IntegerEncoder(coder);
        var fs = new IntegerEncoder(coder);
        var ds = new IntegerEncoder(coder);
        var it = new IntegerEncoder(coder);
        var id = new IaidEncoder(coder, SymbolCodeLength(text.SymbolCount));

        dt.Encode(0);
        int stripT = 0, firstS = 0;
        foreach (var strip in kept.GroupBy(i => FloorDiv(i.Y - region.Y, StripRows)).OrderBy(g => g.Key))
        {
            dt.Encode(strip.Key - stripT);
            stripT = strip.Key;
            var curS = 0;
            var first = true;
            foreach (var instance in strip.OrderBy(i => i.X))
            {
                var s = instance.X - region.X;
                if (first)
                {
                    fs.Encode(s - firstS);
                    firstS = s;
                    first = false;
                }
                else ds.Encode(s - curS);
                it.Encode(instance.Y - region.Y - strip.Key * StripRows);
                id.Encode(instance.SymbolId);
                curS = s + instance.Width - 1;
            }
            ds.Encode(null);
        }
        var coded = coder.Finish();

        // SBFLAGS: arithmetic, no refinement, strips of StripRows rows, top-left reference corner, not
        // transposed, the region's own symbol combination and default pixel, no S offset.
        const int TopLeft = 1;
        var flags = (Log2StripRows << 2) | (TopLeft << 4) | (text.SymbolCombination << 7) | (text.DefaultPixel ? 1 << 9 : 0);
        var data = RegionInfo(region, text.RegionCombination, 2 + 4 + coded.Length);
        data[RegionInfoLength] = (byte)(flags >> 8);
        data[RegionInfoLength + 1] = (byte)flags;
        WriteInt(data, RegionInfoLength + 2, kept.Count);
        Array.Copy(coded, 0, data, RegionInfoLength + 6, coded.Length);
        return data;
    }

    // Text region strips: this many rows (a power of two), coded as its logarithm.
    private const int Log2StripRows = 3;
    private const int StripRows = 1 << Log2StripRows;

    private static int FloorDiv(int value, int divisor) => (int)Math.Floor((double)value / divisor);

    /// <summary>SBSYMCODELEN: the bits a symbol ID takes (at least one).</summary>
    private static int SymbolCodeLength(int count)
    {
        var bits = 0;
        while ((1 << bits) < count) bits++;
        return Math.Max(1, bits);
    }

    /// <summary>The page a segment header associates its segment with.</summary>
    private static int PageOf(byte[] header)
    {
        var wide = (header[4] & 0x40) != 0;
        return wide
            ? (header[header.Length - 8] << 24) | (header[header.Length - 7] << 16)
              | (header[header.Length - 6] << 8) | header[header.Length - 5]
            : header[header.Length - 5];
    }

    /// <summary>Write a segment: its header with the data length set to <paramref name="body"/>'s.</summary>
    private static void Write(Stream output, byte[] header, byte[] body)
    {
        WriteInt(header, header.Length - 4, body.Length);
        output.Write(header, 0, header.Length);
        output.Write(body, 0, body.Length);
    }

    private static byte[] Slice(byte[] data, int from, int to)
    {
        var slice = new byte[to - from];
        Array.Copy(data, from, slice, 0, slice.Length);
        return slice;
    }

    private static bool Touches((int X, int Y, int Width, int Height) a, (int X, int Y, int Width, int Height) b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static (int X, int Y, int Width, int Height) Union((int X, int Y, int Width, int Height) a,
        (int X, int Y, int Width, int Height) b)
    {
        int left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y);
        int right = Math.Max(a.X + a.Width, b.X + b.Width), bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return (left, top, right - left, bottom - top);
    }

    private static (int X, int Y, int Width, int Height) Clip((int X, int Y, int Width, int Height) box, int width,
        int height)
    {
        int left = Math.Max(0, box.X), top = Math.Max(0, box.Y);
        int right = Math.Min(width, box.X + box.Width), bottom = Math.Min(height, box.Y + box.Height);
        return (left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
