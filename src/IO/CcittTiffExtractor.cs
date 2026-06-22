namespace Aspose.Pdf.IO;

/// <summary>
/// Extracts the pre-encoded CCITT Group 4 (T.6) strips from a bilevel TIFF so they can
/// be embedded in a PDF as CCITTFaxDecode XObjects without re-encoding. Only the common
/// fax/scan shape is handled — every frame must be compression 4 (G4) with a single
/// strip; anything else yields null so the caller falls back to a normal raster embed.
/// </summary>
internal static class CcittTiffExtractor
{
    internal readonly record struct G4Frame(byte[] Data, int Width, int Height, bool BlackIs1);

    /// <summary>Return one entry per TIFF page (each a directly-embeddable G4 strip), or
    /// null when the file is not a TIFF or any frame isn't single-strip G4.</summary>
    internal static System.Collections.Generic.List<G4Frame>? TryExtract(byte[] data)
    {
        if (data is null || data.Length < 8) return null;
        bool little;
        if (data[0] == 0x49 && data[1] == 0x49) little = true;       // 'II' little-endian
        else if (data[0] == 0x4D && data[1] == 0x4D) little = false; // 'MM' big-endian
        else return null;
        if (ReadU16(data, 2, little) != 42) return null;

        var frames = new System.Collections.Generic.List<G4Frame>();
        long ifd = ReadU32(data, 4, little);
        var guard = 0;
        while (ifd > 0 && ifd + 2 <= data.Length && guard++ < 4096)
        {
            int count = ReadU16(data, (int)ifd, little);
            long entry = ifd + 2;
            if (entry + (long)count * 12 + 4 > data.Length) return null;

            long width = 0, height = 0, compression = 0, photometric = 0, fillOrder = 1;
            long rowsPerStrip = long.MaxValue, stripOffset = 0, stripCount = 0, stripOffsetsN = 0;
            for (var i = 0; i < count; i++)
            {
                var e = entry + i * 12;
                int tag = ReadU16(data, (int)e, little);
                int type = ReadU16(data, (int)e + 2, little);
                long n = ReadU32(data, (int)e + 4, little);
                switch (tag)
                {
                    case 256: width = TagValue(data, e, type, little); break;
                    case 257: height = TagValue(data, e, type, little); break;
                    case 259: compression = TagValue(data, e, type, little); break;
                    case 262: photometric = TagValue(data, e, type, little); break;
                    case 266: fillOrder = TagValue(data, e, type, little); break;
                    case 273: stripOffset = TagValue(data, e, type, little); stripOffsetsN = n; break;
                    case 278: rowsPerStrip = TagValue(data, e, type, little); break;
                    case 279: stripCount = TagValue(data, e, type, little); break;
                }
            }

            if (compression != 4 || width <= 0 || height <= 0) return null;       // not G4 → bail
            if (stripOffsetsN != 1 || rowsPerStrip < height) return null;          // multi-strip → bail
            if (stripOffset <= 0 || stripCount <= 0 || stripOffset + stripCount > data.Length) return null;

            var strip = new byte[stripCount];
            System.Array.Copy(data, (int)stripOffset, strip, 0, (int)stripCount);
            if (fillOrder == 2) ReverseBits(strip);   // PDF CCITT expects MSB-first

            frames.Add(new G4Frame(strip, (int)width, (int)height, BlackIs1: photometric == 1));

            ifd = ReadU32(data, (int)(entry + (long)count * 12), little);
        }
        return frames.Count > 0 ? frames : null;
    }

    // A scalar TIFF tag value: SHORT (type 3) or LONG (type 4), read from the entry's
    // inline value field (the first component is enough for the single-valued tags here).
    private static long TagValue(byte[] d, long entry, int type, bool little)
        => type == 3 ? ReadU16(d, (int)entry + 8, little) : ReadU32(d, (int)entry + 8, little);

    private static int ReadU16(byte[] d, int o, bool little)
        => little ? d[o] | (d[o + 1] << 8) : (d[o] << 8) | d[o + 1];

    private static long ReadU32(byte[] d, int o, bool little)
        => little
            ? (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24))
            : (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);

    private static readonly byte[] BitReverse = BuildBitReverse();
    private static byte[] BuildBitReverse()
    {
        var t = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            int v = i, r = 0;
            for (var b = 0; b < 8; b++) { r = (r << 1) | (v & 1); v >>= 1; }
            t[i] = (byte)r;
        }
        return t;
    }
    private static void ReverseBits(byte[] buf)
    {
        for (var i = 0; i < buf.Length; i++) buf[i] = BitReverse[buf[i]];
    }
}
