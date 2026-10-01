using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

internal sealed partial class ContentStreamParser
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TextShownState
{
    public string text = null!;
    // Advance the text matrix by the total string displacement.
    // Per PDF spec §9.4.4: tx = ((w0 - Tj/1000) * Tf + Tc + Tw) for each character.
    // For CID fonts bytes pair into 2-byte codes, and widths are CID-keyed — walking the
    // decoded Unicode string would use wrong width entries and miscount character boundaries.
    public double fontSize;
    public double charSpacing;
    public double wordSpacing;
    public double hScaling;
    public double totalWidth;
    public byte[] bytes = default!;
    public Dictionary<int, string>? toUnicode = null;
    // When a caller listens for code advances: after each code, the byte offset just past it
    // and the displacement from the string's start to its end.
    public List<(int ByteEnd, double Advance)>? codeEnds;
}
}
