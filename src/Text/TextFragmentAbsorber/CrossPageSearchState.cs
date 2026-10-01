using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CrossPageSearchState
{
    // Concatenate text from all pages with \r\n between pages
    public System.Text.StringBuilder fullText = null!;
    // Track: for each char position, which page and which run within that page
    public List<(int pageIdx, int runIdx)> charMap = null!;
    public List<List<int>> pageRunStartChars = null!;
    public string concatenated = null!;
    public System.Text.RegularExpressions.MatchCollection matches = null!;
    public List<(Page page, List<RawTextRun> runs)> allPageRuns = default!;
}
}
