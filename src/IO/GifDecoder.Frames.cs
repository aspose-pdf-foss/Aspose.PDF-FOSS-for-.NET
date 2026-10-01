namespace Aspose.Pdf.IO;

/// <summary>
/// Every frame of a GIF as the file holds it: palette indices rather than colours, for a caller
/// that writes the image as an Indexed colour space instead of RGB, or that wants a later frame
/// of an animation.
/// </summary>
internal static partial class GifDecoder
{
    /// <summary>One frame: where it sits on the logical screen, its indices in row order
    /// (interlacing undone), the colour table it uses (local, else global) exactly as stored, and
    /// the transparent index its own Graphic Control Extension names (-1 for none -- the
    /// extension governs only the image that follows it).</summary>
    internal sealed class GifFrame
    {
        public int Left;
        public int Top;
        public int Width;
        public int Height;
        public byte[] Indices = System.Array.Empty<byte>();
        public byte[] ColourTable = System.Array.Empty<byte>();
        public int ColourTableBits;
        public bool LocalTable;
        public bool Interlaced;
        public int TransparentIndex = -1;
        public int DelayCentiseconds;
    }

    /// <summary>The logical screen and the frames read, in file order.</summary>
    internal sealed class GifFrames
    {
        public int LogicalWidth;
        public int LogicalHeight;
        public int GlobalTableBits;
        public byte[]? GlobalTable;
        public readonly System.Collections.Generic.List<GifFrame> Frames = new();
    }

    /// <summary>
    /// Reads the logical screen and up to <paramref name="maxFrames"/> frames. Returns null when
    /// the bytes are not a GIF or its header or global colour table is cut short; a frame that
    /// cannot be read ends the walk, so the frames returned are the ones the file really holds.
    /// </summary>
    internal static GifFrames? DecodeFrames(byte[] data, int maxFrames = int.MaxValue)
    {
        if (!IsGif(data) || data.Length < 13) return null;
        var result = new GifFrames
        {
            LogicalWidth = data[6] | (data[7] << 8),
            LogicalHeight = data[8] | (data[9] << 8),
        };
        var packed = data[10];
        var p = 13;
        if ((packed & 0x80) != 0)
        {
            result.GlobalTableBits = (packed & 0x07) + 1;
            var n = 1 << result.GlobalTableBits;
            if (p + n * 3 > data.Length) return null;
            result.GlobalTable = new byte[n * 3];
            System.Array.Copy(data, p, result.GlobalTable, 0, n * 3);
            p += n * 3;
        }

        var transparentIndex = -1;
        var delay = 0;
        while (p < data.Length && result.Frames.Count < maxFrames)
        {
            var block = data[p++];
            if (block == 0x21)
            {
                if (p >= data.Length) break;
                var label = data[p++];
                if (label == 0xF9 && p + 4 < data.Length && data[p] >= 4)
                {
                    transparentIndex = (data[p + 1] & 0x01) != 0 ? data[p + 4] : -1;
                    delay = data[p + 2] | (data[p + 3] << 8);
                }
                p = SkipSubBlocks(data, p);
                continue;
            }
            if (block != 0x2C) break;
            var frame = ReadFrame(data, ref p, result.GlobalTable, result.GlobalTableBits);
            if (frame is null) break;
            frame.TransparentIndex = transparentIndex;
            frame.DelayCentiseconds = delay;
            result.Frames.Add(frame);
            transparentIndex = -1;
            delay = 0;
        }
        return result;
    }

    // An Image Descriptor, its colour table and its image data; null when any is cut short.
    private static GifFrame? ReadFrame(byte[] d, ref int p, byte[]? globalTable, int globalBits)
    {
        if (p + 9 > d.Length) return null;
        var frame = new GifFrame
        {
            Left = d[p] | (d[p + 1] << 8),
            Top = d[p + 2] | (d[p + 3] << 8),
            Width = d[p + 4] | (d[p + 5] << 8),
            Height = d[p + 6] | (d[p + 7] << 8),
        };
        var packed = d[p + 8];
        p += 9;
        frame.Interlaced = (packed & 0x40) != 0;
        if ((packed & 0x80) != 0)
        {
            frame.LocalTable = true;
            frame.ColourTableBits = (packed & 0x07) + 1;
            var n = 1 << frame.ColourTableBits;
            if (p + n * 3 > d.Length) return null;
            frame.ColourTable = new byte[n * 3];
            System.Array.Copy(d, p, frame.ColourTable, 0, n * 3);
            p += n * 3;
        }
        else
        {
            if (globalTable is null) return null;
            frame.ColourTable = globalTable;
            frame.ColourTableBits = globalBits;
        }
        if (p >= d.Length || frame.Width <= 0 || frame.Height <= 0) return null;
        var minCodeSize = d[p++];
        var indices = Unpack(d, p, minCodeSize, frame.Width * frame.Height);
        if (indices is null) return null;
        if (frame.Interlaced) Deinterlace(indices, frame.Width, frame.Height);
        frame.Indices = indices;
        p = SkipSubBlocks(d, p);
        return frame;
    }
}
