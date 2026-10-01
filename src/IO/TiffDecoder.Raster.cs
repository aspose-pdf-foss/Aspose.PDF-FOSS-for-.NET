namespace Aspose.Pdf.IO;

/// <summary>One TIFF page as its samples: decompressed, the predictor undone, fill order
/// normalised and planar samples interleaved, but still at the file's own depth, packed
/// MSB-first a row at a time, 16-bit samples big-endian. Group 4 is decoded exactly as T.6 lays
/// it out. Photometric, palette and extra samples are left for the caller to interpret.</summary>
internal sealed class TiffRaster
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int BitsPerSample { get; init; }
    public int SamplesPerPixel { get; init; }
    public int Photometric { get; init; }
    public int Compression { get; init; }
    public int FillOrder { get; init; }
    /// <summary>TIFF ColorMap: all reds, then greens, then blues, each 0..65535; null when absent.</summary>
    public long[]? ColorMap { get; init; }
    /// <summary>TIFF ExtraSamples, one entry per sample beyond the colour ones; null when absent.</summary>
    public long[]? ExtraSamples { get; init; }
    public int RowBytes { get; init; }
    public byte[] Samples { get; init; } = System.Array.Empty<byte>();
}

internal static partial class TiffDecoder
{
    private const int MaxIfdChain = 1024;
    private const long MaxRasterDimension = 65500;

    /// <summary>
    /// The samples of one page, counted from 0 along the IFD chain, thumbnails included. Null
    /// when the data is not TIFF, the page does not exist, or its layout or compression is not
    /// one this decoder reads. With <paramref name="blankUnreadable"/> a strip whose data
    /// cannot be decoded comes back blank (all zero) instead of losing the page.
    /// </summary>
    internal static TiffRaster? DecodeRaster(byte[] d, int page, bool blankUnreadable)
    {
        if (!IsTiff(d) || page < 0) return null;
        var le = d[0] == 0x49;
        var seen = new HashSet<long>();
        long ifd = U32(d, 4, le);
        for (var index = 0; ifd != 0 && seen.Add(ifd) && seen.Count <= MaxIfdChain; index++)
        {
            if (ifd + 2 > d.Length) return null;
            int n = U16(d, (int)ifd, le);
            var chainEnd = ifd + 2 + n * 12L;
            if (chainEnd + 4 > d.Length) return null;
            if (index == page)
            {
                try
                {
                    return RasterOf(d, le, (int)ifd, n, blankUnreadable);
                }
                catch (Exception)
                {
                    return null;
                }
            }
            ifd = U32(d, (int)chainEnd, le);
        }
        return null;
    }

    private static TiffRaster? RasterOf(byte[] d, bool le, int ifd, int entryCount, bool blankUnreadable)
    {
        var tf = new TiffIfdState();
        ResetTiffIfd(tf);
        ReadTiffTags(tf, d, le, ifd, entryCount);
        tf.blankUnreadable = blankUnreadable;
        tf.strictGroup4 = true;
        if (tf.width <= 0 || tf.height <= 0 || tf.width > MaxRasterDimension || tf.height > MaxRasterDimension) return null;
        tf.spp = (int)Math.Max(1, tf.samplesPerPixel);
        if (tf.spp > 5) return null;
        tf.bps = (int)tf.bitsPerSample[0];
        foreach (var b in tf.bitsPerSample)
            if (b != tf.bps) return null;
        if (tf.bps is not (1 or 2 or 4 or 8 or 16)) return null;
        if (tf.compression is 6 or 7) return null;
        if (tf.planarConfig == 2 && tf.bps != 8) return null;
        tf.w = (int)tf.width;
        tf.h = (int)tf.height;
        if (!ReadTiffRaster(tf, d, le)) return null;
        if (tf.bps == 16 && le)
            for (var i = 0; i + 1 < tf.raster.Length; i += 2)
                (tf.raster[i], tf.raster[i + 1]) = (tf.raster[i + 1], tf.raster[i]);
        return new TiffRaster
        {
            Width = tf.w,
            Height = tf.h,
            BitsPerSample = tf.bps,
            SamplesPerPixel = tf.spp,
            Photometric = (int)tf.photometric,
            Compression = (int)tf.compression,
            FillOrder = (int)tf.fillOrder,
            ColorMap = tf.colorMap,
            ExtraSamples = tf.extraSamples,
            RowBytes = tf.rowBytes,
            Samples = tf.raster,
        };
    }
}
