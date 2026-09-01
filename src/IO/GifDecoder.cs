namespace Aspose.Pdf.IO;

/// <summary>
/// Managed GIF decoder for the FIRST frame of a GIF87a/GIF89a stream, so a GIF
/// reaches a PDF the same way on every platform. GIF was the last raster format
/// the library could only read through the Windows image codecs, which meant a
/// plain <c>new ImageStamp("logo.gif")</c> threw off Windows.
///
/// Handles the global and local colour tables, interlaced frames and the Graphic
/// Control Extension's transparent index. Animation is out of scope: a PDF image
/// XObject shows one frame, and the first is the frame every other decoder here
/// would also present.
/// </summary>
internal static class GifDecoder
{
    /// <summary>True when the bytes begin with a GIF87a / GIF89a signature.</summary>
    internal static bool IsGif(byte[] data) =>
        data is { Length: >= 6 } && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F'
        && data[3] == (byte)'8' && (data[4] == (byte)'7' || data[4] == (byte)'9') && data[5] == (byte)'a';

    /// <summary>
    /// Decode the first frame to interleaved RGB plus a parallel 8-bit alpha plane
    /// (255 opaque). Returns false for anything this decoder does not recognise, so
    /// the caller stays on its own error path rather than being handed invented pixels.
    /// </summary>
    internal static bool TryDecode(byte[] data, out byte[] rgb, out byte[] alpha, out int width, out int height)
    {
        rgb = System.Array.Empty<byte>();
        alpha = System.Array.Empty<byte>();
        width = height = 0;
        if (!IsGif(data)) return false;
        try { return Decode(data, out rgb, out alpha, out width, out height); }
        catch { return false; }
    }

    private static bool Decode(byte[] d, out byte[] rgb, out byte[] alpha, out int width, out int height)
    {
        rgb = System.Array.Empty<byte>();
        alpha = System.Array.Empty<byte>();
        width = height = 0;

        // Logical Screen Descriptor: width(2) height(2) packed(1) background(1) aspect(1).
        if (d.Length < 13) return false;
        var p = 6;
        var packed = d[p + 4];
        p += 7;

        byte[]? globalTable = null;
        if ((packed & 0x80) != 0)
        {
            var n = 2 << (packed & 0x07);
            if (p + n * 3 > d.Length) return false;
            globalTable = new byte[n * 3];
            System.Array.Copy(d, p, globalTable, 0, n * 3);
            p += n * 3;
        }

        // The transparent index lives in the Graphic Control Extension that PRECEDES
        // the frame, so it has to be carried across the block walk.
        var transparentIndex = -1;

        while (p < d.Length)
        {
            var block = d[p++];
            if (block == 0x3B) return false;               // trailer before any frame
            if (block == 0x21)                             // extension
            {
                if (p >= d.Length) return false;
                var label = d[p++];
                if (label == 0xF9 && p + 4 < d.Length && d[p] >= 4)
                {
                    var gcePacked = d[p + 1];
                    if ((gcePacked & 0x01) != 0) transparentIndex = d[p + 4];
                }
                p = SkipSubBlocks(d, p);
                continue;
            }
            if (block != 0x2C) return false;               // not an Image Descriptor

            // Image Descriptor: left(2) top(2) width(2) height(2) packed(1).
            if (p + 9 > d.Length) return false;
            var iw = d[p + 4] | (d[p + 5] << 8);
            var ih = d[p + 6] | (d[p + 7] << 8);
            var iPacked = d[p + 8];
            p += 9;
            if (iw <= 0 || ih <= 0) return false;

            var table = globalTable;
            if ((iPacked & 0x80) != 0)
            {
                var n = 2 << (iPacked & 0x07);
                if (p + n * 3 > d.Length) return false;
                table = new byte[n * 3];
                System.Array.Copy(d, p, table, 0, n * 3);
                p += n * 3;
            }
            if (table is null) return false;

            if (p >= d.Length) return false;
            var minCodeSize = d[p++];
            var indices = Unpack(d, ref p, minCodeSize, iw * ih);
            if (indices is null) return false;

            if ((iPacked & 0x40) != 0) Deinterlace(indices, iw, ih);

            width = iw;
            height = ih;
            rgb = new byte[iw * ih * 3];
            alpha = new byte[iw * ih];
            var colours = table.Length / 3;
            for (var i = 0; i < indices.Length; i++)
            {
                int idx = indices[i];
                if (idx >= colours) idx = 0;
                rgb[i * 3] = table[idx * 3];
                rgb[i * 3 + 1] = table[idx * 3 + 1];
                rgb[i * 3 + 2] = table[idx * 3 + 2];
                alpha[i] = (byte)(idx == transparentIndex ? 0 : 255);
            }
            return true;
        }
        return false;
    }

    /// <summary>Walk a chain of length-prefixed sub-blocks to the terminating zero byte.</summary>
    private static int SkipSubBlocks(byte[] d, int p)
    {
        while (p < d.Length)
        {
            var len = d[p++];
            if (len == 0) break;
            p += len;
        }
        return p;
    }

    /// <summary>
    /// GIF's variable-width LZW: codes start at minCodeSize+1 bits and grow by one
    /// whenever the dictionary fills, the stream carries its own Clear and end-of-information
    /// codes, and the codes arrive packed LEAST-significant bit first inside
    /// length-prefixed sub-blocks.
    /// </summary>
    private static byte[]? Unpack(byte[] d, ref int p, int minCodeSize, int expected)
    {
        if (minCodeSize is < 2 or > 8) return null;
        var clearCode = 1 << minCodeSize;
        var eoiCode = clearCode + 1;

        // Dictionary as parallel arrays: each entry is a prefix code plus one suffix
        // byte, walked backwards onto a stack to emit. 4096 is the format's ceiling.
        var prefix = new int[4096];
        var suffix = new byte[4096];
        var stack = new byte[4096];
        for (var i = 0; i < clearCode; i++) suffix[i] = (byte)i;

        var output = new byte[expected];
        var outPos = 0;

        var codeSize = minCodeSize + 1;
        var nextCode = eoiCode + 1;
        var previous = -1;
        var bitBuffer = 0;
        var bitCount = 0;
        var blockLen = 0;
        var blockPos = 0;

        while (true)
        {
            while (bitCount < codeSize)
            {
                if (blockPos >= blockLen)
                {
                    if (p >= d.Length) return output;
                    blockLen = d[p++];
                    if (blockLen == 0) return output;
                    if (p + blockLen > d.Length) return output;
                    blockPos = 0;
                }
                bitBuffer |= d[p + blockPos] << bitCount;
                blockPos++;
                bitCount += 8;
                if (blockPos >= blockLen) { p += blockLen; blockLen = 0; blockPos = 0; }
            }

            var code = bitBuffer & ((1 << codeSize) - 1);
            bitBuffer >>= codeSize;
            bitCount -= codeSize;

            if (code == clearCode)
            {
                codeSize = minCodeSize + 1;
                nextCode = eoiCode + 1;
                previous = -1;
                continue;
            }
            if (code == eoiCode) return output;

            var stackTop = 0;
            int current;
            if (code < nextCode && code != eoiCode)
            {
                current = code;
            }
            else if (previous >= 0)
            {
                // The KwKwK case: the encoder used the code it is defining right now, so
                // the string is the previous one plus its own first byte.
                current = previous;
                stack[stackTop++] = FirstByte(prefix, suffix, previous, clearCode);
            }
            else
            {
                return output;
            }

            while (current > eoiCode)
            {
                if (stackTop >= stack.Length) return output;
                stack[stackTop++] = suffix[current];
                current = prefix[current];
            }
            if (stackTop >= stack.Length) return output;
            stack[stackTop++] = suffix[current];

            while (stackTop > 0)
            {
                if (outPos >= output.Length) return output;
                output[outPos++] = stack[--stackTop];
            }

            if (previous >= 0 && nextCode < 4096)
            {
                prefix[nextCode] = previous;
                suffix[nextCode] = FirstByte(prefix, suffix, code < nextCode ? code : previous, clearCode);
                nextCode++;
                if (nextCode < 4096 && nextCode == (1 << codeSize) && codeSize < 12) codeSize++;
            }
            previous = code;
        }
    }

    private static byte FirstByte(int[] prefix, byte[] suffix, int code, int clearCode)
    {
        var guard = 0;
        while (code > clearCode + 1 && guard++ < 4096) code = prefix[code];
        return suffix[code];
    }

    /// <summary>
    /// Reorder the four interlace passes (rows 0/8, 4/8, 2/4, 1/2) into row order.
    /// </summary>
    private static void Deinterlace(byte[] indices, int w, int h)
    {
        var src = (byte[])indices.Clone();
        var row = 0;
        var starts = new[] { 0, 4, 2, 1 };
        var steps = new[] { 8, 8, 4, 2 };
        for (var pass = 0; pass < 4; pass++)
        {
            for (var y = starts[pass]; y < h; y += steps[pass])
            {
                if (row >= h) break;
                System.Array.Copy(src, row * w, indices, y * w, w);
                row++;
            }
        }
    }
}
