using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.IO;

internal static partial class TiffDecoder
{
    /// <summary>The decoded samples become a PNG by their photometric interpretation: bilevel, gray, RGB, palette or separated.</summary>
    private static byte[]? EncodeTiffPhotometric(TiffIfdState tf)
    {
        switch (tf.photometric)
        {
            case 0: // WhiteIsZero
            case 1: // BlackIsZero
            {
                var gray = new byte[(long)tf.w * tf.h];
                for (long i = 0, p = 0; i < gray.Length; i++, p += tf.spp)
                    gray[i] = tf.photometric == 0 ? (byte)(255 - tf.samples[p]) : tf.samples[p];
                return PngEncoder.Encode(gray, tf.w, tf.h, colorType: 0);
            }
            case 2: // RGB (+ optional alpha extra sample)
            {
                if (tf.spp < 3) return null;
                var hasAlpha = tf.spp >= 4 && tf.extraSamples is { Length: > 0 } && tf.extraSamples[0] is 1 or 2;
                var bpp = hasAlpha ? 4 : 3;
                var px = new byte[(long)tf.w * tf.h * bpp];
                for (long i = 0, p = 0; i < (long)tf.w * tf.h; i++, p += tf.spp)
                {
                    px[i * bpp] = tf.samples[p];
                    px[i * bpp + 1] = tf.samples[p + 1];
                    px[i * bpp + 2] = tf.samples[p + 2];
                    if (hasAlpha) px[i * bpp + 3] = tf.samples[p + 3];
                }
                return PngEncoder.Encode(px, tf.w, tf.h, colorType: hasAlpha ? 6 : 2);
            }
            case 3: // Palette
            {
                var mapLen = 1 << tf.bps;
                if (tf.colorMap is null || tf.colorMap.Length < mapLen * 3) return null;
                var px = new byte[(long)tf.w * tf.h * 3];
                for (long i = 0, p = 0; i < (long)tf.w * tf.h; i++, p += tf.spp)
                {
                    // ColorMap entries are 16-bit; indexed samples were scaled to
                    // 0..255 by ExpandTo8Bit, so recover the palette index first.
                    var idx = tf.bps == 8 ? tf.samples[p] : tf.samples[p] * (mapLen - 1) / 255;
                    px[i * 3] = (byte)(tf.colorMap[idx] >> 8);
                    px[i * 3 + 1] = (byte)(tf.colorMap[mapLen + idx] >> 8);
                    px[i * 3 + 2] = (byte)(tf.colorMap[2 * mapLen + idx] >> 8);
                }
                return PngEncoder.Encode(px, tf.w, tf.h, colorType: 2);
            }
            case 5: // CMYK
            {
                if (tf.spp < 4) return null;
                var px = new byte[(long)tf.w * tf.h * 3];
                for (long i = 0, p = 0; i < (long)tf.w * tf.h; i++, p += tf.spp)
                {
                    int c = tf.samples[p], m = tf.samples[p + 1], y = tf.samples[p + 2], k = tf.samples[p + 3];
                    px[i * 3] = (byte)((255 - c) * (255 - k) / 255);
                    px[i * 3 + 1] = (byte)((255 - m) * (255 - k) / 255);
                    px[i * 3 + 2] = (byte)((255 - y) * (255 - k) / 255);
                }
                return PngEncoder.Encode(px, tf.w, tf.h, colorType: 2);
            }
        }
        return null;
    }

    /// <summary>The raster assembled from the file's tiles or strips, each decompressed by the IFD's compression.</summary>
    private static bool ReadTiffRaster(TiffIfdState tf, byte[] d, bool le)
    {
        if (tf.tileOffsets is not null && tf.tileWidth > 0 && tf.tileLength > 0)
        {
            if (tf.planarConfig == 2) return false;                        // planar tiles unsupported
            var tw = (int)tf.tileWidth;
            var th = (int)tf.tileLength;
            var tilesAcross = (tf.w + tw - 1) / tw;
            var tilesDown = (tf.h + th - 1) / th;
            if (tf.tileOffsets.Length < tilesAcross * tilesDown) return false;
            var tileRowBytes = (tw * tf.bps * tf.spp + 7) / 8;
            tf.rowBytes = (tf.w * tf.bps * tf.spp + 7) / 8;
            tf.raster = new byte[(long)tf.rowBytes * tf.h];
            for (var ty = 0; ty < tilesDown; ty++)
            for (var tx = 0; tx < tilesAcross; tx++)
            {
                var ti = ty * tilesAcross + tx;
                var expected = tileRowBytes * th;
                var tile = DecodeSegment(d, le, stripOffset: tf.tileOffsets[ti],
                    stripCount: tf.tileCounts is not null && ti < tf.tileCounts.Length ? tf.tileCounts[ti] : -1,
                    tf.compression, tw, th, expected, tf.t4Options, tf.predictor, tf.spp, tf.bps, tf.fillOrder, tf.strictGroup4);
                // Blit the tile rows into the raster, clipping the right/bottom edges.
                for (var r = 0; r < th && ty * th + r < tf.h; r++)
                {
                    var dstRow = (long)(ty * th + r) * tf.rowBytes;
                    var srcRow = (long)r * tileRowBytes;
                    // Whole-byte copy is exact when tx*tw*bps*spp is byte-aligned,
                    // which holds because tile widths are multiples of 16 (spec).
                    var dstBit = (long)tx * tw * tf.bps * tf.spp;
                    var copyBits = Math.Min((long)tw, tf.w - (long)tx * tw) * tf.bps * tf.spp;
                    var copyBytes = (int)((copyBits + 7) / 8);
                    Array.Copy(tile, srcRow, tf.raster, dstRow + dstBit / 8, copyBytes);
                }
            }
        }
        else
        {
            if (tf.stripOffsets is null) return false;
            var rps = tf.rowsPerStrip == long.MaxValue || tf.rowsPerStrip <= 0 ? tf.h : (int)Math.Min(tf.rowsPerStrip, tf.h);
            var stripsPerPlane = (tf.h + rps - 1) / rps;
            var planes = tf.planarConfig == 2 ? tf.spp : 1;
            if (tf.stripOffsets.Length < stripsPerPlane * planes) return false;
            var samplesPerRow = tf.planarConfig == 2 ? 1 : tf.spp;
            tf.rowBytes = (tf.w * tf.bps * samplesPerRow + 7) / 8;
            var planeBytes = (long)tf.rowBytes * tf.h;
            var packed = new byte[planeBytes * planes];
            for (var pl = 0; pl < planes; pl++)
            {
                long rowsDone = 0;
                for (var s = 0; s < stripsPerPlane; s++)
                {
                    var stripRows = (int)Math.Min(rps, tf.h - rowsDone);
                    var expected = tf.rowBytes * stripRows;
                    var si = pl * stripsPerPlane + s;
                    byte[] strip;
                    try
                    {
                        strip = DecodeSegment(d, le, tf.stripOffsets[si],
                            tf.stripCounts is not null && si < tf.stripCounts.Length ? tf.stripCounts[si] : -1,
                            tf.compression, tf.w, stripRows, expected, tf.t4Options, tf.predictor, samplesPerRow, tf.bps, tf.fillOrder, tf.strictGroup4);
                    }
                    catch (Exception) when (tf.blankUnreadable)
                    {
                        strip = new byte[expected];
                    }
                    Array.Copy(strip, 0, packed, pl * planeBytes + rowsDone * tf.rowBytes, expected);
                    rowsDone += stripRows;
                }
            }
            if (planes > 1)
            {
                // Interleave planar samples into chunky order (8-bit only, checked above).
                var chunkyRowBytes = tf.w * tf.spp;
                var chunky = new byte[(long)chunkyRowBytes * tf.h];
                for (long px = 0; px < (long)tf.w * tf.h; px++)
                    for (var c = 0; c < tf.spp; c++)
                        chunky[px * tf.spp + c] = packed[c * planeBytes + px];
                tf.raster = chunky;
                tf.rowBytes = chunkyRowBytes;
            }
            else
                tf.raster = packed;
        }
        return true;
    }

    /// <summary>Every IFD entry read into its field: geometry, compression, layout, the strip and tile tables, the palette.</summary>
    private static void ReadTiffTags(TiffIfdState tf, byte[] d, bool le, int ifd, int entryCount)
    {
        for (var e = 0; e < entryCount; e++)
        {
            var eo = ifd + 2 + e * 12;
            int tag = U16(d, eo, le);
            int type = U16(d, eo + 2, le);
            long count = U32(d, eo + 4, le);
            switch (tag)
            {
                case 254: tf.newSubfileType = ReadValues(d, le, eo, type, count)[0]; break;
                case 256: tf.width = ReadValues(d, le, eo, type, count)[0]; break;
                case 257: tf.height = ReadValues(d, le, eo, type, count)[0]; break;
                case 258: tf.bitsPerSample = ReadValues(d, le, eo, type, count); break;
                case 259: tf.compression = ReadValues(d, le, eo, type, count)[0]; break;
                case 262: tf.photometric = ReadValues(d, le, eo, type, count)[0]; break;
                case 266: tf.fillOrder = ReadValues(d, le, eo, type, count)[0]; break;
                case 273: tf.stripOffsets = ReadValues(d, le, eo, type, count); break;
                case 277: tf.samplesPerPixel = ReadValues(d, le, eo, type, count)[0]; break;
                case 278: tf.rowsPerStrip = ReadValues(d, le, eo, type, count)[0]; break;
                case 279: tf.stripCounts = ReadValues(d, le, eo, type, count); break;
                case 284: tf.planarConfig = ReadValues(d, le, eo, type, count)[0]; break;
                case 292: tf.t4Options = ReadValues(d, le, eo, type, count)[0]; break;
                case 317: tf.predictor = ReadValues(d, le, eo, type, count)[0]; break;
                case 320: tf.colorMap = ReadValues(d, le, eo, type, count); break;
                case 322: tf.tileWidth = ReadValues(d, le, eo, type, count)[0]; break;
                case 323: tf.tileLength = ReadValues(d, le, eo, type, count)[0]; break;
                case 324: tf.tileOffsets = ReadValues(d, le, eo, type, count); break;
                case 325: tf.tileCounts = ReadValues(d, le, eo, type, count); break;
                case 338: tf.extraSamples = ReadValues(d, le, eo, type, count); break;
                case 347: tf.jpegTables = RawBytes(d, le, eo, type, count); break;
                case 513: tf.jpegIfOffset = ReadValues(d, le, eo, type, count)[0]; break;
                case 514: tf.jpegIfLength = ReadValues(d, le, eo, type, count)[0]; break;
            }
        }
    }

    /// <summary>The IFD's defaults before its entries are read.</summary>
    private static void ResetTiffIfd(TiffIfdState tf)
    {
        tf.width = 0;
        tf.height = 0;
        tf.compression = 1;
        tf.photometric = -1;
        tf.fillOrder = 1;
        tf.samplesPerPixel = 1;
        tf.rowsPerStrip = long.MaxValue;
        tf.planarConfig = 1;
        tf.predictor = 1;
        tf.newSubfileType = 0;
        tf.t4Options = 0;
        tf.tileWidth = 0;
        tf.tileLength = 0;
        tf.jpegIfOffset = 0;
        tf.jpegIfLength = 0;
        tf.jpegTables = null;
        tf.bitsPerSample = new long[] { 1 };
        tf.stripOffsets = null;
        tf.stripCounts = null;
        tf.tileOffsets = null;
        tf.tileCounts = null;
        tf.colorMap = null;
        tf.extraSamples = null;
    }
}
