using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public partial class ImageXObject
{
// The stages of the pixel source: the indexed, 1-bit and per-component getters.

    /// <summary>The indexed-colour source: palette lookups per packed sample, or null when the
    /// image carries no palette.</summary>
    private static IO.PixelGetter? IndexedSource(PixelSourceState ps, int paletteSize)
    {
        if (ps.indexedPalette is null) return null;
        return (int x, int y) =>
        {
            var idx = ReadPackedIndex(ps.decoded, x, y, ps.w, ps.bpc);
            if (idx >= paletteSize) idx = paletteSize - 1;
            var src = idx * 3;
            return (ps.indexedPalette[src], ps.indexedPalette[src + 1], ps.indexedPalette[src + 2]);
        };
    }

    /// <summary>The packed rows of a 1-bit gray image and whether a 1 bit is black, as the pixel
    /// source reads them (see <see cref="OneBitSource"/>), for a caller that measures the image
    /// a byte of eight pixels at a time rather than one delegate call a pixel. Null for any other
    /// image, which the pixel source reads another way.</summary>
    internal (byte[] Rows, bool BlackIs1)? OneBitGrayRows()
    {
        if (!IsOneBitGray || IsJpeg || IsJpeg2000 || ResolveIndexedPalette().result is not null) return null;
        return (_reader.DecodeStream(_stream), BlackIs1());
    }

    /// <summary>The 1-bit source, honouring /Decode and /BlackIs1; null unless the image is 1 bpc.</summary>
    private IO.PixelGetter? OneBitSource(PixelSourceState ps)
    {
        if (ps.bpc != 1) return null;
        var blackIs1 = BlackIs1();
        var srcBytesPerRow = (ps.w + 7) / 8;
        return (int x, int y) =>
        {
            var byteIdx = (long)y * srcBytesPerRow + (x / 8);
            var bitIdx = 7 - (x % 8);
            var bit = (byteIdx < ps.decoded.Length) ? (ps.decoded[byteIdx] >> bitIdx) & 1 : 0;
            byte v;
            if (blackIs1) v = bit == 1 ? (byte)0 : (byte)255;
            else v = bit == 1 ? (byte)255 : (byte)0;
            return (v, v, v);
        };
    }

    /// <summary>Whether a 1 bit means black: /Decode [1 0] says so, and a /BlackIs1 in the
    /// decode parameters has the last word.</summary>
    private bool BlackIs1()
    {
        var blackIs1 = false;
        if (_reader.Resolve(_stream.Dict.Get("Decode")) is PdfArray { Count: >= 2 } da)
        {
            var first = da[0] switch { PdfInteger i => i.Value, PdfReal r => (long)r.Value, _ => 0 };
            blackIs1 = first == 1;
        }
        if (_reader.ResolveDict(_stream.Dict.Get("DecodeParms"))?.Get("BlackIs1") is PdfBoolean b)
            blackIs1 = b.Value;
        return blackIs1;
    }

    /// <summary>A huge 1-bit image box-averaged to <paramref name="tw"/> x <paramref name="th"/>
    /// straight from its packed rows - the boxes, and the gray each averages to, are those of
    /// <see cref="CapHugeImage"/> over the 1-bit source, which read every sample through a
    /// delegate call. Null for any other image.</summary>
    private (IO.PixelGetter getter, int width, int height)? ReducedOneBit(int tw, int th)
    {
        if (BitsPerComponent != 1 || IsJpeg || IsJpeg2000 || ResolveIndexedPalette().result is not null) return null;
        int w = Width, h = Height;
        var decoded = _reader.DecodeStream(_stream);
        var blackIs1 = BlackIs1();
        var rowBytes = (w + 7) / 8;
        var colOf = BoxOf(w, tw, rowBytes * 8);
        var rowOf = BoxOf(h, th, h);
        var colWidth = new int[tw];
        for (var x = 0; x < w; x++) colWidth[colOf[x]]++;
        var (runBox, runMask, runStart) = ByteRuns(colOf, rowBytes);
        var gray = new byte[tw * th];
        var ones = new int[tw];
        var ty = 0;
        var rows = 0;
        var padding = (byte)(0xFF << (rowBytes * 8 - w));   // the last byte's bits past the width
        for (var y = 0; y < h; y++)
        {
            if (rowOf[y] != ty) { EmitReducedRow(gray, ty * tw, ones, colWidth, rows, blackIs1); ty = rowOf[y]; rows = 0; }
            rows++;
            var row = (long)y * rowBytes;
            for (var bx = 0; bx < rowBytes && row + bx < decoded.Length; bx++)
            {
                var b = decoded[row + bx];
                if (bx == rowBytes - 1) b &= padding;
                if (b == 0) continue;
                for (var r = runStart[bx]; r < runStart[bx + 1]; r++)
                    ones[runBox[r]] += Compat.PopCount((byte)(b & runMask[r]));
            }
        }
        EmitReducedRow(gray, ty * tw, ones, colWidth, rows, blackIs1);
        return ((int x, int y) => { var v = gray[y * tw + x]; return (v, v, v); }, tw, th);
    }

    /// <summary>The boxes each byte of a packed row spans, as runs of (box, mask of the byte's
    /// bits in it): byte bx's runs are start[bx] up to start[bx + 1]. A byte is then counted
    /// into its few boxes by population count rather than bit by bit.</summary>
    private static (int[] box, byte[] mask, int[] start) ByteRuns(int[] colOf, int rowBytes)
    {
        var box = new List<int>(rowBytes * 2);
        var mask = new List<byte>(rowBytes * 2);
        var start = new int[rowBytes + 1];
        for (var bx = 0; bx < rowBytes; bx++)
        {
            start[bx] = box.Count;
            for (var k = 0; k < 8; k++)
            {
                var bit = (byte)(0x80 >> k);
                var b = colOf[bx * 8 + k];
                if (box.Count > start[bx] && box[^1] == b) mask[^1] |= bit;
                else { box.Add(b); mask.Add(bit); }
            }
        }
        start[rowBytes] = box.Count;
        return (box.ToArray(), mask.ToArray(), start);
    }

    /// <summary>The box each of <paramref name="src"/> samples falls in when they are averaged
    /// into <paramref name="dst"/> (fewer): box b takes samples b*src/dst up to (b+1)*src/dst.
    /// Entries past <paramref name="src"/>, up to <paramref name="length"/>, name the last box.</summary>
    private static int[] BoxOf(int src, int dst, int length)
    {
        var boxOf = new int[length];
        for (var b = 0; b < dst; b++)
        {
            var from = (int)((long)b * src / dst);
            var to = Math.Max(from + 1, (int)((long)(b + 1) * src / dst));
            for (var i = from; i < to && i < src; i++) boxOf[i] = b;
        }
        for (var i = src; i < length; i++) boxOf[i] = dst - 1;
        return boxOf;
    }

    /// <summary>One reduced row: each box's share of white samples as gray, 255 for all white,
    /// over <paramref name="rows"/> source rows of <paramref name="colWidth"/> samples each; the
    /// counts are cleared for the next row.</summary>
    private static void EmitReducedRow(byte[] gray, int offset, int[] ones, int[] colWidth, int rows, bool blackIs1)
    {
        for (var tx = 0; tx < ones.Length; tx++)
        {
            var count = colWidth[tx] * rows;
            var white = blackIs1 ? count - ones[tx] : ones[tx];
            gray[offset + tx] = count == 0 ? (byte)0 : (byte)(255L * white / count);
            ones[tx] = 0;
        }
    }

    /// <summary>A 1-bit image as a 1 bpp bitmap of its own rows, black and white as /Decode and
    /// /BlackIs1 read them - a scanned page handed over without a pass through PNG, which
    /// spread every bit to a byte and deflated the lot. Null for any other image, a stencil
    /// mask (its colour is the fill's), or rows the data does not cover.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal System.Drawing.Bitmap? OneBitBitmap()
    {
        if (BitsPerComponent != 1 || IsJpeg || IsJpeg2000 || ResolveIndexedPalette().result is not null) return null;
        if (_stream.Dict.Get("ImageMask") is PdfBoolean { Value: true }) return null;
        int w = Width, h = Height;
        var decoded = _reader.DecodeStream(_stream);
        var rowBytes = (w + 7) / 8;
        if (w <= 0 || h <= 0 || (long)rowBytes * h > decoded.Length) return null;
        var blackIs1 = BlackIs1();
        var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format1bppIndexed);
        var palette = bmp.Palette;
        palette.Entries[0] = blackIs1 ? System.Drawing.Color.White : System.Drawing.Color.Black;
        palette.Entries[1] = blackIs1 ? System.Drawing.Color.Black : System.Drawing.Color.White;
        bmp.Palette = palette;
        var bits = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format1bppIndexed);
        try
        {
            for (var y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(decoded, y * rowBytes, bits.Scan0 + y * bits.Stride, rowBytes);
        }
        finally { bmp.UnlockBits(bits); }
        return bmp;
    }

    /// <summary>The per-component sources: gray, DeviceCMYK and RGB samples; null for any other layout.</summary>
    private IO.PixelGetter? ComponentSource(PixelSourceState ps)
    {
        ps.components = ComponentCount;
        if (ps.components == 1)
        {
            return (int x, int y) =>
            {
                var idx = (long)y * ps.w + x;
                var v = idx < ps.decoded.Length ? ps.decoded[idx] : (byte)0;
                return (v, v, v);
            };
        }
        if (ps.components == 4 && ColorSpace is "DeviceCMYK")
        {
            // The renderers' CMYK conversion, so a saved or exported image keeps the colours its page shows.
            var colours = new Devices.CmykToRgbLut.Memo();
            return (int x, int y) =>
            {
                var idx = ((long)y * ps.w + x) * 4;
                if (idx + 3 >= ps.decoded.Length) return (0, 0, 0);
                return colours.Convert(ps.decoded[idx], ps.decoded[idx + 1], ps.decoded[idx + 2], ps.decoded[idx + 3]);
            };
        }
        if (ps.components == 3)
        {
            return (int x, int y) =>
            {
                var idx = ((long)y * ps.w + x) * 3;
                if (idx + 2 >= ps.decoded.Length) return (0, 0, 0);
                return (ps.decoded[idx], ps.decoded[idx + 1], ps.decoded[idx + 2]);
            };
        }
        return null;
    }
}
