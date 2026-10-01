
namespace Aspose.Pdf.IO;

internal static partial class BmpDecoder
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BmpDecodeState
{
    public int dataOffset;
    public int dibSize;
    public int width;
    public int height;
    public int bitCount;
    public int compression;
    public int clrUsed;
    public int paletteEntry;
    // A negative height means the rows are stored TOP-DOWN instead of the usual
    // bottom-up order.
    public bool topDown;
    public byte[]? palette;
    public int rowBytes;
    public byte[] rgb = null!;
    public byte[] d = default!;
}
}
