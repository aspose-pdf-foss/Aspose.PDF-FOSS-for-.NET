using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class InlineImageSkipState
{
    // Consume tokens until the ID keyword (image data start), capturing the
    // dictionary keys needed to size the data.
    public int imgW;
    public int imgH;
    public int imgBpc;
    public int imgColors;
    public string? key;
    public string? firstFilter;
    public long dataStart0;
    public long lenAll;
    // After ID, spec mandates one whitespace byte before raw data.
    // Scan raw bytes for 'E' 'I' followed by whitespace/EOF.
    // Many real-world PDFs don't have whitespace BEFORE "EI" (the image data
    // ends immediately before the E), so we check both patterns:
    //   1. Standard: whitespace + EI + whitespace (spec-compliant)
    //   2. Relaxed: any-byte + EI + whitespace (common in practice)
    public long pos;
    public int len;
    public PdfLexer lexer = default!;
    public bool imgFlate;
}
}
