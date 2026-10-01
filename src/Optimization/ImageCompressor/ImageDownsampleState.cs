using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Optimization;

internal static partial class ImageCompressor
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ImageDownsampleState
{
    public int width;
    public int height;
    public string? filterName;
    // Determine components per pixel from ColorSpace
    public int components;
    public int bpc;
    // Estimate DPI: assume the image is displayed at full page width (612pt = 8.5in)
    public double estimatedDpiX;
    public double estimatedDpiY;
    public double estimatedDpi;
    public double scaleFactor;
    public int newWidth;
    public int newHeight;
    // Decode the image data
    public byte[] decoded = null!;
    public int expectedSize;
    // Downsample using box filter
    public byte[] downsampled = null!;
    // Compress
    public byte[] compressed = null!;
    public PdfStream stream = default!;
    public PdfReader reader = default!;
    public int maxDpi = 0;
    public int quality = 0;
}
}
