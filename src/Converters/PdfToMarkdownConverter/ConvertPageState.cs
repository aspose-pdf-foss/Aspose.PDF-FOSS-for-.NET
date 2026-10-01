using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToMarkdownConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ConvertPageState
{
    public System.Text.StringBuilder sb = null!;
    // Collect link annotations
    public List<Aspose.Pdf.Converters.PdfToMarkdownConverter.LinkInfo> links = null!;
    // Detect horizontal rules from content stream
    public List<double> horizontalRules = null!;
    // Resolve base font names from page resources
    public Dictionary<string, string> baseFontNames = null!;
    // Fall back to plain text extraction with font-size-based heading detection
    public Aspose.Pdf.Text.TextFragmentAbsorber fragmentAbsorber = null!;
    // Track which links have been matched to text
    public HashSet<int> matchedLinks = null!;
    // Sort horizontal rule Y positions descending (PDF Y is bottom-up, process top-to-bottom)
    public List<double> ruleYPositions = null!;
    public int nextRuleIndex;
    public Page page = default!;
}
}
