using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.IO;

/// <summary>
/// Managed baseline TIFF decoder. Walks every IFD (frame) of a single- or
/// multi-frame TIFF and decodes it to a PNG (via <see cref="PngEncoder"/>),
/// so TIFF handling does not depend on a platform image codec.
///
/// Supported per frame: strip and tile layouts; compressions None, CCITT
/// G3-1D/G3-2D/G4, LZW (with horizontal predictor), Deflate, PackBits;
/// photometrics WhiteIsZero, BlackIsZero, RGB (with alpha extra sample),
/// Palette and CMYK; 1/2/4/8/16 bits per sample; both byte orders; FillOrder 2;
/// PlanarConfiguration 2 for 8-bit samples.
///
/// A frame that is unsupported or corrupt (e.g. strip offsets beyond the end
/// of the file) is skipped; the remaining frames still decode — matching the
/// decoder, which paginates only the decodable frames of a damaged
/// multi-frame file. Reduced-resolution (thumbnail) subfiles are skipped too.
/// </summary>
internal static class TiffDecoder
{
    internal static bool IsTiff(byte[] d) =>
        d is { Length: >= 8 } &&
        ((d[0] == 0x49 && d[1] == 0x49 && d[2] == 42 && d[3] == 0) ||
         (d[0] == 0x4D && d[1] == 0x4D && d[2] == 0 && d[3] == 42));

    /// <summary>Decode every decodable frame to PNG bytes. Returns null when the
    /// data is not TIFF or no frame could be decoded.</summary>
    internal static List<byte[]>? DecodeFramesAsPng(byte[] d)
    {
        if (!IsTiff(d)) return null;
        var le = d[0] == 0x49;
        var frames = new List<byte[]>();
        var seen = new HashSet<long>();      // IFD offsets — guards cyclic chains
        long ifd = U32(d, 4, le);
        while (ifd != 0 && seen.Add(ifd) && seen.Count <= 1024)
        {
            if (ifd < 0 || ifd + 2 > d.Length) break;
            int n = U16(d, (int)ifd, le);
            var chainEnd = ifd + 2 + n * 12L;
            if (chainEnd + 4 > d.Length) break;
            try
            {
                var png = DecodeIfd(d, le, (int)ifd, n);
                if (png is not null) frames.Add(png);
            }
            catch
            {
                // corrupt or unsupported frame — skip it, keep walking the chain
            }
            ifd = U32(d, (int)chainEnd, le);
        }
        return frames.Count > 0 ? frames : null;
    }

    private static byte[]? DecodeIfd(byte[] d, bool le, int ifd, int entryCount)
    {
        long width = 0, height = 0, compression = 1, photometric = -1, fillOrder = 1;
        long samplesPerPixel = 1, rowsPerStrip = long.MaxValue, planarConfig = 1, predictor = 1;
        long newSubfileType = 0, t4Options = 0, tileWidth = 0, tileLength = 0;
        long[] bitsPerSample = { 1 };
        long[]? stripOffsets = null, stripCounts = null, tileOffsets = null, tileCounts = null;
        long[]? colorMap = null, extraSamples = null;

        for (var e = 0; e < entryCount; e++)
        {
            var eo = ifd + 2 + e * 12;
            int tag = U16(d, eo, le);
            int type = U16(d, eo + 2, le);
            long count = U32(d, eo + 4, le);
            switch (tag)
            {
                case 254: newSubfileType = ReadValues(d, le, eo, type, count)[0]; break;
                case 256: width = ReadValues(d, le, eo, type, count)[0]; break;
                case 257: height = ReadValues(d, le, eo, type, count)[0]; break;
                case 258: bitsPerSample = ReadValues(d, le, eo, type, count); break;
                case 259: compression = ReadValues(d, le, eo, type, count)[0]; break;
                case 262: photometric = ReadValues(d, le, eo, type, count)[0]; break;
                case 266: fillOrder = ReadValues(d, le, eo, type, count)[0]; break;
                case 273: stripOffsets = ReadValues(d, le, eo, type, count); break;
                case 277: samplesPerPixel = ReadValues(d, le, eo, type, count)[0]; break;
                case 278: rowsPerStrip = ReadValues(d, le, eo, type, count)[0]; break;
                case 279: stripCounts = ReadValues(d, le, eo, type, count); break;
                case 284: planarConfig = ReadValues(d, le, eo, type, count)[0]; break;
                case 292: t4Options = ReadValues(d, le, eo, type, count)[0]; break;
                case 317: predictor = ReadValues(d, le, eo, type, count)[0]; break;
                case 320: colorMap = ReadValues(d, le, eo, type, count); break;
                case 322: tileWidth = ReadValues(d, le, eo, type, count)[0]; break;
                case 323: tileLength = ReadValues(d, le, eo, type, count)[0]; break;
                case 324: tileOffsets = ReadValues(d, le, eo, type, count); break;
                case 325: tileCounts = ReadValues(d, le, eo, type, count); break;
                case 338: extraSamples = ReadValues(d, le, eo, type, count); break;
            }
        }

        // Reduced-resolution subfile (thumbnail) — not a page.
        if ((newSubfileType & 1) != 0) return null;
        if (width <= 0 || height <= 0 || width > 65500 || height > 65500) return null;
        if (width * height > 268_435_456) return null;                 // 256M px sanity cap
        var spp = (int)Math.Max(1, samplesPerPixel);
        if (spp > 5) return null;
        var bps = (int)bitsPerSample[0];
        foreach (var b in bitsPerSample)
            if (b != bps && photometric != 6) return null;             // heterogeneous depths unsupported
        if (bps is not (1 or 2 or 4 or 8 or 16)) return null;
        if (compression is 6 or 7) return null;                        // JPEG-in-TIFF — codec fallback handles it
        if (photometric is not (0 or 1 or 2 or 3 or 5)) return null;
        if (planarConfig == 2 && bps != 8) return null;

        var w = (int)width;
        var h = (int)height;

        // Decode strip/tile payloads into full-resolution sample rows (still at
        // the source bit depth, chunky order).
        byte[] raster;                                                  // packed rows, rowBytes each
        int rowBytes;
        if (tileOffsets is not null && tileWidth > 0 && tileLength > 0)
        {
            if (planarConfig == 2) return null;                        // planar tiles unsupported
            var tw = (int)tileWidth;
            var th = (int)tileLength;
            var tilesAcross = (w + tw - 1) / tw;
            var tilesDown = (h + th - 1) / th;
            if (tileOffsets.Length < tilesAcross * tilesDown) return null;
            var tileRowBytes = (tw * bps * spp + 7) / 8;
            rowBytes = (w * bps * spp + 7) / 8;
            raster = new byte[(long)rowBytes * h];
            for (var ty = 0; ty < tilesDown; ty++)
            for (var tx = 0; tx < tilesAcross; tx++)
            {
                var ti = ty * tilesAcross + tx;
                var expected = tileRowBytes * th;
                var tile = DecodeSegment(d, le, stripOffset: tileOffsets[ti],
                    stripCount: tileCounts is not null && ti < tileCounts.Length ? tileCounts[ti] : -1,
                    compression, tw, th, expected, t4Options, predictor, spp, bps, fillOrder);
                // Blit the tile rows into the raster, clipping the right/bottom edges.
                for (var r = 0; r < th && ty * th + r < h; r++)
                {
                    var dstRow = (long)(ty * th + r) * rowBytes;
                    var srcRow = (long)r * tileRowBytes;
                    // Whole-byte copy is exact when tx*tw*bps*spp is byte-aligned,
                    // which holds because tile widths are multiples of 16 (spec).
                    var dstBit = (long)tx * tw * bps * spp;
                    var copyBits = Math.Min((long)tw, w - (long)tx * tw) * bps * spp;
                    var copyBytes = (int)((copyBits + 7) / 8);
                    Array.Copy(tile, srcRow, raster, dstRow + dstBit / 8, copyBytes);
                }
            }
        }
        else
        {
            if (stripOffsets is null) return null;
            var rps = rowsPerStrip == long.MaxValue || rowsPerStrip <= 0 ? h : (int)Math.Min(rowsPerStrip, h);
            var stripsPerPlane = (h + rps - 1) / rps;
            var planes = planarConfig == 2 ? spp : 1;
            if (stripOffsets.Length < stripsPerPlane * planes) return null;
            var samplesPerRow = planarConfig == 2 ? 1 : spp;
            rowBytes = (w * bps * samplesPerRow + 7) / 8;
            var planeBytes = (long)rowBytes * h;
            var packed = new byte[planeBytes * planes];
            for (var pl = 0; pl < planes; pl++)
            {
                long rowsDone = 0;
                for (var s = 0; s < stripsPerPlane; s++)
                {
                    var stripRows = (int)Math.Min(rps, h - rowsDone);
                    var expected = rowBytes * stripRows;
                    var si = pl * stripsPerPlane + s;
                    var strip = DecodeSegment(d, le, stripOffsets[si],
                        stripCounts is not null && si < stripCounts.Length ? stripCounts[si] : -1,
                        compression, w, stripRows, expected, t4Options, predictor, samplesPerRow, bps, fillOrder);
                    Array.Copy(strip, 0, packed, pl * planeBytes + rowsDone * rowBytes, expected);
                    rowsDone += stripRows;
                }
            }
            if (planes > 1)
            {
                // Interleave planar samples into chunky order (8-bit only, checked above).
                var chunkyRowBytes = w * spp;
                var chunky = new byte[(long)chunkyRowBytes * h];
                for (long px = 0; px < (long)w * h; px++)
                    for (var c = 0; c < spp; c++)
                        chunky[px * spp + c] = packed[c * planeBytes + px];
                raster = chunky;
                rowBytes = chunkyRowBytes;
            }
            else
                raster = packed;
        }

        // Expand to 8-bit samples in chunky order.
        var samples = ExpandTo8Bit(raster, w, h, rowBytes, bps, spp, le);

        // Map to PNG gray / RGB / RGBA.
        switch (photometric)
        {
            case 0: // WhiteIsZero
            case 1: // BlackIsZero
            {
                var gray = new byte[(long)w * h];
                for (long i = 0, p = 0; i < gray.Length; i++, p += spp)
                    gray[i] = photometric == 0 ? (byte)(255 - samples[p]) : samples[p];
                return PngEncoder.Encode(gray, w, h, colorType: 0);
            }
            case 2: // RGB (+ optional alpha extra sample)
            {
                if (spp < 3) return null;
                var hasAlpha = spp >= 4 && extraSamples is { Length: > 0 } && extraSamples[0] is 1 or 2;
                var bpp = hasAlpha ? 4 : 3;
                var px = new byte[(long)w * h * bpp];
                for (long i = 0, p = 0; i < (long)w * h; i++, p += spp)
                {
                    px[i * bpp] = samples[p];
                    px[i * bpp + 1] = samples[p + 1];
                    px[i * bpp + 2] = samples[p + 2];
                    if (hasAlpha) px[i * bpp + 3] = samples[p + 3];
                }
                return PngEncoder.Encode(px, w, h, colorType: hasAlpha ? 6 : 2);
            }
            case 3: // Palette
            {
                var mapLen = 1 << bps;
                if (colorMap is null || colorMap.Length < mapLen * 3) return null;
                var px = new byte[(long)w * h * 3];
                for (long i = 0, p = 0; i < (long)w * h; i++, p += spp)
                {
                    // ColorMap entries are 16-bit; indexed samples were scaled to
                    // 0..255 by ExpandTo8Bit, so recover the palette index first.
                    var idx = bps == 8 ? samples[p] : samples[p] * (mapLen - 1) / 255;
                    px[i * 3] = (byte)(colorMap[idx] >> 8);
                    px[i * 3 + 1] = (byte)(colorMap[mapLen + idx] >> 8);
                    px[i * 3 + 2] = (byte)(colorMap[2 * mapLen + idx] >> 8);
                }
                return PngEncoder.Encode(px, w, h, colorType: 2);
            }
            case 5: // CMYK
            {
                if (spp < 4) return null;
                var px = new byte[(long)w * h * 3];
                for (long i = 0, p = 0; i < (long)w * h; i++, p += spp)
                {
                    int c = samples[p], m = samples[p + 1], y = samples[p + 2], k = samples[p + 3];
                    px[i * 3] = (byte)((255 - c) * (255 - k) / 255);
                    px[i * 3 + 1] = (byte)((255 - m) * (255 - k) / 255);
                    px[i * 3 + 2] = (byte)((255 - y) * (255 - k) / 255);
                }
                return PngEncoder.Encode(px, w, h, colorType: 2);
            }
        }
        return null;
    }

    /// <summary>Decode one strip/tile to exactly <paramref name="expected"/> bytes of
    /// packed rows. Throws when the payload is out of bounds, truncated or malformed —
    /// the caller skips the frame.</summary>
    private static byte[] DecodeSegment(byte[] d, bool le, long stripOffset, long stripCount,
        long compression, int widthPx, int rowCount, int expected, long t4Options,
        long predictor, int sppChunky, int bps, long fillOrder)
    {
        if (stripOffset < 0 || stripOffset > d.Length)
            throw new InvalidOperationException("strip offset out of bounds");
        if (stripCount < 0) stripCount = d.Length - stripOffset;
        if (stripOffset + stripCount > d.Length)
            throw new InvalidOperationException("strip extends past end of file");
        var src = new byte[stripCount];
        Array.Copy(d, stripOffset, src, 0, stripCount);

        // CCITT bit order: FillOrder 2 stores bits LSB-first; the fax decoder
        // expects MSB-first, so reverse before decoding. Other compressions are
        // byte-oriented and FillOrder applies to the decoded bits (handled by
        // the caller's bit expansion for bps < 8).
        if (fillOrder == 2 && compression is 2 or 3 or 4)
            for (var i = 0; i < src.Length; i++) src[i] = ReverseBits(src[i]);

        byte[] outBytes;
        switch (compression)
        {
            case 1:
                outBytes = src;
                break;
            case 2: // CCITT modified Huffman: G3 1D, every row byte-aligned
                outBytes = CcittDecode(src, widthPx, rowCount, k: 0, encodedByteAlign: true);
                break;
            case 3: // G3, optionally 2D per T4Options bit 0; fill bits per bit 2
                outBytes = CcittDecode(src, widthPx, rowCount,
                    k: (t4Options & 1) != 0 ? 4 : 0, encodedByteAlign: (t4Options & 4) != 0);
                break;
            case 4: // G4
                outBytes = CcittDecode(src, widthPx, rowCount, k: -1, encodedByteAlign: false);
                break;
            case 5:
            {
                var parms = new PdfDictionary();
                parms.Set("EarlyChange", new PdfInteger(1));
                if (predictor > 1)
                {
                    parms.Set("Predictor", new PdfInteger(predictor));
                    parms.Set("Colors", new PdfInteger(sppChunky));
                    parms.Set("BitsPerComponent", new PdfInteger(bps));
                    parms.Set("Columns", new PdfInteger(widthPx));
                }
                outBytes = LzwDecodeFilter.Decode(src, parms);
                break;
            }
            case 8:
            case 32946:
            {
                PdfDictionary? parms = null;
                if (predictor > 1)
                {
                    parms = new PdfDictionary();
                    parms.Set("Predictor", new PdfInteger(predictor));
                    parms.Set("Colors", new PdfInteger(sppChunky));
                    parms.Set("BitsPerComponent", new PdfInteger(bps));
                    parms.Set("Columns", new PdfInteger(widthPx));
                }
                outBytes = FlateDecodeFilter.Decode(src, parms);
                break;
            }
            case 32773:
                outBytes = PackBitsDecode(src, expected);
                break;
            default:
                throw new InvalidOperationException($"unsupported TIFF compression {compression}");
        }

        if (outBytes.Length < expected)
            throw new InvalidOperationException("strip decoded short");
        if (outBytes.Length != expected)
        {
            var trimmed = new byte[expected];
            Array.Copy(outBytes, trimmed, expected);
            outBytes = trimmed;
        }
        // FillOrder 2 on byte-oriented compressions: pixel bits are stored
        // LSB-first within each byte; normalise so the bit expansion (MSB-first)
        // reads them correctly. CCITT input was already reversed pre-decode.
        if (fillOrder == 2 && bps < 8 && compression is not (2 or 3 or 4))
            for (var i = 0; i < outBytes.Length; i++) outBytes[i] = ReverseBits(outBytes[i]);
        return outBytes;
    }

    private static byte[] CcittDecode(byte[] src, int widthPx, int rowCount, int k, bool encodedByteAlign)
    {
        var parms = new PdfDictionary();
        parms.Set("K", new PdfInteger(k));
        parms.Set("Columns", new PdfInteger(widthPx));
        parms.Set("Rows", new PdfInteger(rowCount));
        if (encodedByteAlign) parms.Set("EncodedByteAlign", PdfBoolean.True);
        // BlackIs1: decoded bit 1 = black, no inversion — TIFF fax frames carry
        // PhotometricInterpretation 0 (WhiteIsZero), so bit 1 = black = max sample,
        // which the photometric-0 mapping then inverts to black.
        parms.Set("BlackIs1", PdfBoolean.True);
        return CcittFaxDecodeFilter.Decode(src, parms);
    }

    private static byte[] PackBitsDecode(byte[] src, int expected)
    {
        var output = new byte[expected];
        int op = 0, ip = 0;
        while (op < expected)
        {
            if (ip >= src.Length) throw new InvalidOperationException("PackBits truncated");
            var n = (sbyte)src[ip++];
            if (n >= 0)
            {
                var len = n + 1;
                if (ip + len > src.Length || op + len > expected)
                    throw new InvalidOperationException("PackBits literal overrun");
                Array.Copy(src, ip, output, op, len);
                ip += len; op += len;
            }
            else if (n != -128)
            {
                var len = 1 - n;
                if (ip >= src.Length || op + len > expected)
                    throw new InvalidOperationException("PackBits run overrun");
                var v = src[ip++];
                for (var i = 0; i < len; i++) output[op++] = v;
            }
        }
        return output;
    }

    /// <summary>Expand packed rows to one byte per sample (chunky), scaling
    /// sub-byte depths to 0..255 and taking the high byte of 16-bit samples.</summary>
    private static byte[] ExpandTo8Bit(byte[] raster, int w, int h, int rowBytes, int bps, int spp, bool le)
    {
        var samplesPerRow = (long)w * spp;
        var output = new byte[samplesPerRow * h];
        if (bps == 8)
        {
            for (var r = 0; r < h; r++)
                Array.Copy(raster, (long)r * rowBytes, output, r * samplesPerRow, samplesPerRow);
            return output;
        }
        if (bps == 16)
        {
            for (var r = 0; r < h; r++)
                for (long s = 0; s < samplesPerRow; s++)
                    output[r * samplesPerRow + s] = raster[(long)r * rowBytes + s * 2 + (le ? 1 : 0)];
            return output;
        }
        var max = (1 << bps) - 1;
        for (var r = 0; r < h; r++)
        {
            long bit = 0;
            var rowBase = (long)r * rowBytes;
            for (long s = 0; s < samplesPerRow; s++)
            {
                var byteIdx = rowBase + bit / 8;
                var shift = 8 - bps - (int)(bit % 8);
                var v = (raster[byteIdx] >> shift) & max;
                output[r * samplesPerRow + s] = (byte)(v * 255 / max);
                bit += bps;
            }
        }
        return output;
    }

    private static byte ReverseBits(byte b)
    {
        b = (byte)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
        b = (byte)((b & 0xCC) >> 2 | (b & 0x33) << 2);
        return (byte)((b & 0xAA) >> 1 | (b & 0x55) << 1);
    }

    /// <summary>Read an entry's values (SHORT/LONG/BYTE), resolving the inline-vs-offset
    /// storage rule. RATIONALs and other types return their first LONG as a best effort.</summary>
    private static long[] ReadValues(byte[] d, bool le, int entryOffset, int type, long count)
    {
        var size = type switch { 1 => 1, 3 => 2, 4 => 4, _ => 4 };
        if (count <= 0 || count > 262144) throw new InvalidOperationException("bad tag count");
        var total = size * count;
        var at = total <= 4 ? entryOffset + 8 : (long)U32(d, entryOffset + 8, le);
        if (at < 0 || at + total > d.Length) throw new InvalidOperationException("tag value out of bounds");
        var vals = new long[count];
        for (long i = 0; i < count; i++)
        {
            vals[i] = type switch
            {
                1 => d[at + i],
                3 => U16(d, (int)(at + i * 2), le),
                _ => U32(d, (int)(at + i * 4), le),
            };
        }
        return vals;
    }

    private static int U16(byte[] d, int o, bool le) =>
        le ? d[o] | (d[o + 1] << 8) : (d[o] << 8) | d[o + 1];

    private static uint U32(byte[] d, int o, bool le) =>
        le ? (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24))
           : (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
}
