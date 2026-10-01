using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PngDecodeState
{
    public int width;
    public int height;
    public byte bitDepth;
    public byte colorType;
    // Collect all IDAT chunks, plus the PLTE palette and the tRNS per-index
    // alpha table for indexed-colour PNGs.
    public System.IO.MemoryStream idatData = null!;
    public byte[]? palette;
    public byte[]? trns;
    public int pos;
    // Decompress (skip 2-byte zlib header)
    public byte[] compressed = null!;
    public byte[] rawScanlines = null!;
    // Determine bytes per pixel and extract RGB data
    public int channels;
    // Indexed PNGs pack the sample at the image bit depth (1/2/4/8); every other
    // supported colour type here is byte-per-channel.
    public int stride;
    public byte[] rgb = null!;
    // Alpha channel for the truecolour/grayscale+alpha types is split out into a
    // DeviceGray soft mask so transparent pixels show the page behind instead of
    // rendering as black. Built only when the source actually carries alpha:
    // an alpha channel (4/6), or an indexed image's tRNS per-index table —
    // without which a transparent palette entry would paint its PLTE colour
    // (typically black) over the page.
    public bool hasAlphaChannel;
    public bool indexedAlpha;
    public byte[]? alpha;
    public bool anyTransparent;
    // Reverse PNG filtering and extract RGB
    public byte[] prevRow = null!;
    public byte[] curRow = null!;
    public int scanPos;
    public ImageStamp stamp = null!;
    public byte[] pngData = default!;
}
}
