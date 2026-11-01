namespace Aspose.Pdf.IO;

internal static partial class TiffDecoder
{
    private const byte MarkerStart = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte BaselineFrame = 0xC0;
    private const byte ExtendedFrame = 0xC1;
    private const byte StartOfScan = 0xDA;
    private const byte EndOfImage = 0xD9;
    private const byte DefineRestartInterval = 0xDD;
    private const byte FirstRestartMarker = 0xD0;
    private const int RestartMarkerCount = 8;
    private const int BlockSize = 8;

    /// <summary>One JPEG frame from a new-style JPEG TIFF whose frame is stored in several strips, each an abbreviated
    /// stream of its own band of rows (the layout libtiff writes by default): the strips' entropy-coded data joined
    /// into one scan of the whole frame, a restart marker where each strip ends. A strip starts its coding afresh, as
    /// a scan does after a restart marker, so the joined stream decodes to the strips' rows - and it embeds as the DCT
    /// stream it is. Null when the strips are not coded alike, or carry restart intervals of their own.</summary>
    private static byte[]? JoinJpegStrips(byte[] d, long[] stripOffsets, long[] stripCounts, byte[]? jpegTables, long height)
    {
        var strips = new List<byte[]>(stripOffsets.Length);
        for (var i = 0; i < stripOffsets.Length; i++)
        {
            if (stripOffsets[i] < 0 || stripCounts[i] <= 0 || stripOffsets[i] + stripCounts[i] > d.Length) return null;
            var body = new byte[stripCounts[i]];
            System.Array.Copy(d, stripOffsets[i], body, 0, stripCounts[i]);
            strips.Add(SpliceJpegTables(jpegTables, body));
        }
        if (ReadJpegStripLayout(strips[0]) is not { } first) return null;
        var (mcuW, mcuH) = first.McuSize;
        if (first.Height % mcuH != 0) return null;
        long interval = (first.Width + mcuW - 1) / mcuW * (first.Height / mcuH);
        if (interval is <= 0 or > ushort.MaxValue) return null;

        using var joined = new MemoryStream();
        var head = strips[0];
        // Everything before the scan from the first strip, its frame header made the whole frame's height.
        joined.Write(head, 0, first.SofHeightAt);
        joined.WriteByte((byte)(height >> BitsPerByte));
        joined.WriteByte((byte)height);
        joined.Write(head, first.SofHeightAt + 2, first.SosAt - first.SofHeightAt - 2);
        var restartInterval = new byte[] { MarkerStart, DefineRestartInterval, 0, 4, (byte)(interval >> BitsPerByte), (byte)interval };
        joined.Write(restartInterval, 0, restartInterval.Length);
        joined.Write(head, first.SosAt, first.DataAt - first.SosAt);
        long rows = 0;
        for (var i = 0; i < strips.Count; i++)
        {
            var layout = i == 0 ? first : ReadJpegStripLayout(strips[i]);
            if (layout is not { } s || !SameHeaders(head, first, strips[i], s)) return null;
            // Every strip but the last fills whole restart intervals.
            if (i < strips.Count - 1 && s.Height != first.Height) return null;
            if (i > 0)
            {
                joined.WriteByte(MarkerStart);
                joined.WriteByte((byte)(FirstRestartMarker + (i - 1) % RestartMarkerCount));
            }
            joined.Write(strips[i], s.DataAt, s.DataEnd - s.DataAt);
            rows += s.Height;
        }
        if (rows < height) return null;
        joined.WriteByte(MarkerStart);
        joined.WriteByte(EndOfImage);
        return joined.ToArray();
    }

    private const int BitsPerByte = 8;

    /// <summary>Whether two strips are coded alike: the same tables, frame and scan headers, the height of their band
    /// aside.</summary>
    private static bool SameHeaders(byte[] a, JpegStripLayout la, byte[] b, JpegStripLayout lb)
    {
        if (la.DataAt != lb.DataAt || la.SofHeightAt != lb.SofHeightAt) return false;
        for (var i = 0; i < la.DataAt; i++)
            if (a[i] != b[i] && i != la.SofHeightAt && i != la.SofHeightAt + 1) return false;
        return true;
    }

    /// <summary>Where a strip's frame header keeps its height, where its scan header starts and its entropy-coded
    /// data runs, and the frame's width, height and MCU size.</summary>
    private readonly record struct JpegStripLayout(int SofHeightAt, int SosAt, int DataAt, int DataEnd,
        int Width, int Height, (int W, int H) McuSize);

    /// <summary>The layout of one strip's stream, or null when it has no baseline frame header and scan, or has a
    /// restart interval of its own.</summary>
    private static JpegStripLayout? ReadJpegStripLayout(byte[] s)
    {
        if (s.Length < 4 || s[0] != MarkerStart || s[1] != StartOfImage) return null;
        var i = 2;
        var sofHeightAt = -1;
        int width = 0, height = 0;
        (int, int) mcu = (BlockSize, BlockSize);
        while (i + 4 <= s.Length)
        {
            if (s[i] != MarkerStart) return null;
            var marker = s[i + 1];
            if (marker == MarkerStart) { i++; continue; }
            var length = (s[i + 2] << BitsPerByte) | s[i + 3];
            if (length < 2 || i + 2 + length > s.Length) return null;
            if (marker == DefineRestartInterval) return null;
            if (marker is BaselineFrame or ExtendedFrame)
            {
                if (length < 8) return null;
                sofHeightAt = i + 5;
                height = (s[i + 5] << BitsPerByte) | s[i + 6];
                width = (s[i + 7] << BitsPerByte) | s[i + 8];
                mcu = McuOf(s, i + 9, length - 8);
            }
            if (marker == StartOfScan)
            {
                if (sofHeightAt < 0 || width <= 0 || height <= 0) return null;
                var dataAt = i + 2 + length;
                var dataEnd = s.Length >= dataAt + 2 && s[^2] == MarkerStart && s[^1] == EndOfImage ? s.Length - 2 : s.Length;
                return new JpegStripLayout(sofHeightAt, i, dataAt, dataEnd, width, height, mcu);
            }
            i += 2 + length;
        }
        return null;
    }

    /// <summary>The MCU of a frame: one block for a single component, else the largest sampling factors' blocks.</summary>
    private static (int W, int H) McuOf(byte[] s, int componentsAt, int available)
    {
        var count = s[componentsAt];
        if (count <= 1 || available < 1 + count * 3) return (BlockSize, BlockSize);
        int maxH = 1, maxV = 1;
        for (var c = 0; c < count; c++)
        {
            var factors = s[componentsAt + 2 + c * 3];
            maxH = System.Math.Max(maxH, factors >> 4);
            maxV = System.Math.Max(maxV, factors & 0x0F);
        }
        return (BlockSize * maxH, BlockSize * maxV);
    }

    /// <summary>A frame whose JPEG strips cannot be joined, as one PNG of the strips decoded one by one and stacked;
    /// null when a strip does not decode or differs from the frame's width or colour.</summary>
    private static byte[]? StackJpegStrips(byte[] d, long[] stripOffsets, long[] stripCounts, byte[]? jpegTables,
        long width, long height)
    {
        byte[]? raster = null;
        var components = 0;
        long rows = 0;
        for (var i = 0; i < stripOffsets.Length && rows < height; i++)
        {
            if (stripOffsets[i] < 0 || stripCounts[i] <= 0 || stripOffsets[i] + stripCounts[i] > d.Length) return null;
            var body = new byte[stripCounts[i]];
            System.Array.Copy(d, stripOffsets[i], body, 0, stripCounts[i]);
            (byte[] pixels, int w, int h, int c) strip;
            try { strip = Filters.JpegDecoder.Decode(SpliceJpegTables(jpegTables, body)); }
            catch (Exception) { return null; }
            if (strip.w != width || strip.c is not (1 or 3) || (components != 0 && strip.c != components)) return null;
            components = strip.c;
            raster ??= new byte[width * height * components];
            var take = (int)System.Math.Min(strip.h, height - rows);
            System.Array.Copy(strip.pixels, 0, raster, rows * width * components, (long)take * width * components);
            rows += take;
        }
        if (raster is null || rows < height) return null;
        return PngEncoder.Encode(raster, (int)width, (int)height, colorType: components == 1 ? 0 : 2);
    }
}
