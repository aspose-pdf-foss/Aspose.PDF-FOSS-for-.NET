using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileMend
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MendPngDecodeState
{
    public int pos;
    public int width;
    public int height;
    public int bitDepth;
    public int colorType;
    public byte[]? palette;
    public byte[]? trns;
    public System.IO.MemoryStream idatData = null!;
    // Decompress IDAT data (deflate inside zlib wrapper)
    public byte[] compressedData = null!;
    public byte[] decompressed = null!;
    public bool hasAlpha;
    public int channels;
    // Bytes per pixel used by the row filters is ceil(bitsPerPixel/8), min 1
    // (PNG spec §9.2). Stride is the packed scanline length; for 8/16-bit this
    // equals width*channels*(bitDepth/8) as before, and it also handles the
    // sub-byte (1/2/4-bit) palette/grayscale case.
    public int bpp;
    public int stride;
    // Unfilter scanlines
    public byte[] raw = null!;
    public byte[] prevRow = null!;
    public int srcPos;
    public byte[] png = default!;
}
}
