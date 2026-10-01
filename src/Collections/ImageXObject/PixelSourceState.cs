using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public partial class ImageXObject
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PixelSourceState
{
    public int bpc;
    public int w;
    public byte[] decoded = null!;
    // Indexed colour space — see ToPng() for the rationale.
    public byte[]? indexedPalette;
    public int components;
}
}
