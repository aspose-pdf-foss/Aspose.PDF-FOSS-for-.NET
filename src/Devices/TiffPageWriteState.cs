
namespace Aspose.Pdf.Devices;

public sealed partial class TiffDevice
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TiffPageWriteState
{
    public bool isPalette;
    public bool is4bpp;
    public bool isBilevel;
    // Default depth keeps the source alpha channel — emits 32bpp RGBA. Explicit
    // Format24bpp drops alpha. The default is 32bpp ARGB; it reads
    // back as PixelFormat.Format32bppArgb.
    public bool isAlpha;
    // Pick the strip layout per requested depth:
    //   1bpp: 1 packed bit per pixel, MSB-first, /Photometric=WhiteIsZero so
    //         CCITT-style templates and our output share the same convention.
    //         The default brightness halftones the render's luminance; a raised
    //         brightness thresholds it.
    //   8bpp: indexed palette — adaptive (≤256 unique colours, lossless)
    //         falling back to 3-3-2 uniform.
    //   4bpp: indexed palette — adaptive ≤16 colours (lossless) else the
    //         16 most frequent, packed 2 indices/byte (high nibble first).
    //  32bpp: RGBA straight through with ExtraSamples=2 (unassociated alpha).
    //   else: 24-bit RGB (alpha stripped).
    public byte[] stripInput = null!;
    public int stripSize;
    // Write strip data first
    public uint stripOffset;
    // Patch previous IFD offset to point here
    public uint ifdOffset;
    public long currentPos;
    // Photometric: 0=WhiteIsZero (bilevel min-is-white), 2=RGB(/RGBA), 3=Palette.
    public uint photometric;
    public uint samplesPerPixel;
    // Next IFD offset placeholder (0 for last page; caller patches when
    // writing the next page).
    public long nextIfdPos;
    public BinaryWriter bw = default!;
    public Stream output = default!;
    public byte[] rgba = default!;
    public int w = 0;
    public int h = 0;
    public long ifdOffsetPos = 0;
    public CompressionType compression = default!;
    public ColorDepth depth = default!;
    public ushort[]? colorMap;
}
}
