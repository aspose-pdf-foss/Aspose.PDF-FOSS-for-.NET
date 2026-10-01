using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.IO;

internal static partial class TiffDecoder
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TiffIfdState
{
    public long width;
    public long height;
    public long compression;
    public long photometric;
    public long fillOrder;
    public long samplesPerPixel;
    public long rowsPerStrip;
    public long planarConfig;
    public long predictor;
    public long newSubfileType;
    public long t4Options;
    public long tileWidth;
    public long tileLength;
    public long jpegIfOffset;
    public long jpegIfLength;
    public byte[]? jpegTables;
    public long[] bitsPerSample = null!;
    public long[]? stripOffsets;
    public long[]? stripCounts;
    public long[]? tileOffsets;
    public long[]? tileCounts;
    public long[]? colorMap;
    public long[]? extraSamples;
    public int spp;
    public int bps;
    public int w;
    public int h;
    // Decode strip/tile payloads into full-resolution sample rows (still at
    // the source bit depth, chunky order).
    public byte[] raster = null!;
    public int rowBytes;
    // A strip that cannot be decoded is left blank instead of failing the frame.
    public bool blankUnreadable;
    // Group 4 decoded exactly as T.6 lays it out, without the one-column shift used for display.
    public bool strictGroup4;
    // Expand to 8-bit samples in chunky order.
    public byte[] samples = null!;
}
}
