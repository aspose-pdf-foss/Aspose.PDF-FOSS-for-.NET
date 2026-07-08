using System.IO.Compression;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Decodes Flate (zlib/deflate) compressed data (PDF 32000 §7.4.4).
/// Uses a self-contained managed inflater so behavior is independent of
/// the host's native zlib; falls back to the BCL's ZLibStream/DeflateStream
/// only if the managed path throws.
/// PNG / TIFF prediction filters are applied after decompression per the
/// stream's DecodeParms dictionary.
/// </summary>
internal static class FlateDecodeFilter
{
    internal static byte[] Decode(byte[] data, PdfDictionary? parms)
    {
        var decompressed = Inflate(data);

        if (parms is not null)
        {
            var predictor = (int)parms.GetInt("Predictor", 1);
            if (predictor > 1)
            {
                var columns = (int)parms.GetInt("Columns", 1);
                var colors = (int)parms.GetInt("Colors", 1);
                var bitsPerComponent = (int)parms.GetInt("BitsPerComponent", 8);
                decompressed = RemovePredictor(decompressed, predictor, columns, colors, bitsPerComponent);
            }
        }

        return decompressed;
    }

    /// <summary>Inflate only the leading <paramref name="maxBytes"/> of the stream so a
    /// caller that needs just a header (e.g. a security content-sniff) doesn't fully
    /// materialise a multi-hundred-MB payload. A stream carrying a predictor is decoded
    /// fully then sliced, since a predictor must reconstruct whole rows in order.</summary>
    internal static byte[] DecodePrefix(byte[] data, PdfDictionary? parms, int maxBytes)
    {
        if (maxBytes <= 0 || data.Length == 0) return System.Array.Empty<byte>();

        if (parms is not null && parms.GetInt("Predictor", 1) > 1)
        {
            var full = Decode(data, parms);
            return full.Length <= maxBytes ? full : full[..maxBytes];
        }

        // Stream-inflate and stop once maxBytes are produced. Try the same wrapper
        // variants Inflate's BCL fallback uses (zlib, raw, raw+2-byte skip).
        foreach (var (useZlib, offset) in new[] { (true, 0), (false, 0), (false, 2) })
        {
            if (offset >= data.Length) continue;
            if (TryInflatePrefix(data, useZlib, offset, maxBytes, out var prefix) && prefix.Length > 0)
                return prefix;
        }

        // Last resort: full inflate then slice.
        var all = Inflate(data);
        return all.Length <= maxBytes ? all : all[..maxBytes];
    }

    private static bool TryInflatePrefix(byte[] data, bool useZlib, int offset, int maxBytes, out byte[] result)
    {
        var output = new MemoryStream();
        try
        {
            using var input = new MemoryStream(data, offset, data.Length - offset);
            using var decompressor = useZlib
                ? (Stream)new ZLibStream(input, CompressionMode.Decompress)
                : new DeflateStream(input, CompressionMode.Decompress);
            var buf = new byte[Math.Min(4096, maxBytes)];
            while (output.Length < maxBytes)
            {
                int n;
                try { n = decompressor.Read(buf, 0, buf.Length); }
                catch { break; }
                if (n == 0) break;
                output.Write(buf, 0, (int)Math.Min(n, maxBytes - output.Length));
            }
        }
        catch { /* partial output already captured */ }

        result = output.ToArray();
        return result.Length > 0;
    }

    private static byte[] Inflate(byte[] data)
    {
        if (data.Length == 0) return data;

        // Primary path: pure-managed inflater. Always produces the same output
        // for the same input, on any host.
        try { return ManagedInflater.InflateZlib(data); }
        catch { /* fall through to BCL fallbacks for edge cases the managed path doesn't handle */ }

        // BCL fallback: System.IO.Compression. Native zlib is fast and well-tested,
        // but its behavior varies across hosts (some Win Server 2022 .NET 8 builds
        // mis-decode certain streams) — that's why we try the managed path first.
        if (TryBclDecompress(data, useZlib: true, offset: 0, out var result) && result.Length > 0)
            return result;
        if (TryBclDecompress(data, useZlib: false, offset: 0, out result) && result.Length > 0)
            return result;
        if (data.Length > 2 && TryBclDecompress(data, useZlib: false, offset: 2, out result) && result.Length > 0)
            return result;

        // Some PDFs ship raw deflate without the zlib wrapper.
        try { return ManagedInflater.InflateRaw(data); }
        catch { }

        // Give up: return the input untouched rather than crashing.
        return data;
    }

    private static bool TryBclDecompress(byte[] data, bool useZlib, int offset, out byte[] result)
    {
        var output = new MemoryStream();
        try
        {
            using var input = new MemoryStream(data, offset, data.Length - offset);
            using var decompressor = useZlib
                ? (Stream)new ZLibStream(input, CompressionMode.Decompress)
                : new DeflateStream(input, CompressionMode.Decompress);
            // Chunked reads with per-Read try/catch so any bytes produced before
            // a corruption error are still captured in `output` (CopyTo with a
            // large buffer would discard partial output on a mid-buffer fault).
            var buf = new byte[4096];
            while (true)
            {
                int n;
                try { n = decompressor.Read(buf, 0, buf.Length); }
                catch { break; }
                if (n == 0) break;
                output.Write(buf, 0, n);
            }
        }
        catch { /* partial output already captured */ }

        result = output.ToArray();
        return result.Length > 0;
    }

    // Shared with LzwDecodeFilter: both filters carry the same /Predictor DecodeParms.
    internal static byte[] RemovePredictor(byte[] data, int predictor, int columns, int colors, int bpc)
    {
        if (predictor == 2)
            return RemoveTiffPredictor(data, columns, colors, bpc);

        // PNG predictors (10-15)
        if (predictor >= 10)
            return RemovePngPredictor(data, columns, colors, bpc);

        return data;
    }

    private static byte[] RemovePngPredictor(byte[] data, int columns, int colors, int bpc)
    {
        var bytesPerPixel = Math.Max(1, colors * bpc / 8);
        var rowBytes = columns * colors * bpc / 8;
        var srcRowBytes = rowBytes + 1; // +1 for filter type byte

        if (data.Length == 0) return data;

        var rows = data.Length / srcRowBytes;
        var output = new byte[rows * rowBytes];
        var prevRow = new byte[rowBytes];

        for (var row = 0; row < rows; row++)
        {
            var srcOffset = row * srcRowBytes;
            var dstOffset = row * rowBytes;
            var filterType = data[srcOffset];

            for (var col = 0; col < rowBytes; col++)
            {
                var raw = data[srcOffset + 1 + col];
                byte a = col >= bytesPerPixel ? output[dstOffset + col - bytesPerPixel] : (byte)0;
                byte b = prevRow[col];
                byte c = col >= bytesPerPixel ? prevRow[col - bytesPerPixel] : (byte)0;

                output[dstOffset + col] = filterType switch
                {
                    0 => raw,                              // None
                    1 => (byte)(raw + a),                  // Sub
                    2 => (byte)(raw + b),                  // Up
                    3 => (byte)(raw + ((a + b) / 2)),      // Average
                    4 => (byte)(raw + PaethPredictor(a, b, c)), // Paeth
                    _ => raw,
                };
            }

            Array.Copy(output, dstOffset, prevRow, 0, rowBytes);
        }

        return output;
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static byte[] RemoveTiffPredictor(byte[] data, int columns, int colors, int bpc)
    {
        if (bpc != 8) return data; // only handle 8-bit TIFF predictor for now

        var rowBytes = columns * colors;
        var rows = data.Length / rowBytes;
        var output = new byte[data.Length];

        for (var row = 0; row < rows; row++)
        {
            var offset = row * rowBytes;
            for (var col = 0; col < rowBytes; col++)
            {
                if (col < colors)
                {
                    output[offset + col] = data[offset + col];
                }
                else
                {
                    output[offset + col] = (byte)(data[offset + col] + output[offset + col - colors]);
                }
            }
        }

        return output;
    }
}
