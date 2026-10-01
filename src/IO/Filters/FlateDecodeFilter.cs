using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Decodes Flate (zlib/deflate) compressed data (PDF 32000 §7.4.4).
/// Uses the library's own inflater so behavior is independent of the host's
/// native zlib; a stream it refuses whole is salvaged as zlib, as raw deflate
/// and as raw deflate past a two-byte header, keeping what each still yields.
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

        // Inflate no further than maxBytes, trying the readings Inflate's salvage uses
        // (zlib, raw, raw past a two-byte header).
        if (HasZlibHeader(data) && ManagedInflater.InflatePrefix(data, ZlibHeaderLength, maxBytes) is { Length: > 0 } zlibPrefix)
            return zlibPrefix;
        foreach (var offset in new[] { 0, ZlibHeaderLength })
        {
            if (offset >= data.Length) continue;
            if (ManagedInflater.InflatePrefix(data, offset, maxBytes) is { Length: > 0 } prefix)
                return prefix;
        }

        // Last resort: full inflate then slice.
        var all = Inflate(data);
        return all.Length <= maxBytes ? all : all[..maxBytes];
    }

    private const int ZlibHeaderLength = 2;
    private const int CompressionMethodDeflate = 8;
    private const int MaxWindowInfo = 7;
    private const int HeaderCheckDivisor = 31;
    private const int PresetDictionaryFlag = 0x20;

    /// <summary>A zlib header a reader would accept: deflate, a window of at most 32K, the check
    /// bits right and no preset dictionary.</summary>
    private static bool HasZlibHeader(byte[] data)
        => data.Length >= ZlibHeaderLength
            && (data[0] & 0x0F) == CompressionMethodDeflate
            && data[0] >> 4 <= MaxWindowInfo
            && (data[0] * 256 + data[1]) % HeaderCheckDivisor == 0
            && (data[1] & PresetDictionaryFlag) == 0;

    private static byte[] Inflate(byte[] data)
    {
        if (data.Length == 0) return data;

        // Primary path: the whole stream as zlib. Always produces the same output
        // for the same input, on any host.
        try { return ManagedInflater.InflateZlib(data); }
        catch { /* refused whole: salvage what the other readings yield */ }

        // A stream refused whole (a broken header, a fault before any output) may still
        // hold data: read it as zlib, as raw deflate, and as raw deflate past its header.
        if (ManagedInflater.Salvage(data, 0, zlibWrapper: true) is { } zlibResult)
            return zlibResult;
        if (ManagedInflater.Salvage(data, 0, zlibWrapper: false) is { } rawResult)
            return rawResult;
        if (data.Length > ZlibHeaderLength && ManagedInflater.Salvage(data, ZlibHeaderLength, zlibWrapper: false) is { } skippedResult)
            return skippedResult;

        // Some PDFs ship raw deflate without the zlib wrapper.
        try { return ManagedInflater.InflateRaw(data); }
        catch { }

        // Give up: return the input untouched rather than crashing.
        return data;
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

        // Samples filtered through a stride other than the image's own row (a soft mask
        // whose producer kept its parent's /Colors) end in a partial row: one filter byte
        // and a short run of samples. Those samples are real image data, so the partial
        // row decodes like the others instead of being dropped.
        var rows = data.Length / srcRowBytes;
        var tailBytes = data.Length - rows * srcRowBytes - 1;
        var totalRows = tailBytes > 0 ? rows + 1 : rows;
        var output = new byte[rows * rowBytes + Math.Max(0, tailBytes)];
        var prevRow = new byte[rowBytes];

        for (var row = 0; row < totalRows; row++)
        {
            var srcOffset = row * srcRowBytes;
            var dstOffset = row * rowBytes;
            var filterType = data[srcOffset];
            var rowLength = row < rows ? rowBytes : tailBytes;

            for (var col = 0; col < rowLength; col++)
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

            Array.Copy(output, dstOffset, prevRow, 0, rowLength);
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
