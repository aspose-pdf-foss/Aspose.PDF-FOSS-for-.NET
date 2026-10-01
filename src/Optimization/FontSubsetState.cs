using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Optimization;

internal static partial class FontSubsetter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FontSubsetState
{
    public Dictionary<Aspose.Pdf.Core.PdfDictionary, HashSet<int>> directCodes = null!;
    public Dictionary<int, HashSet<int>> usedCodes = null!;
    // Simple fonts, also grouped by FontFile2 stream: the PDF/A embedder shares one
    // program object between identical faces referenced by several font dictionaries,
    // and subsetting it per-dictionary would drop the other dictionaries' glyphs.
    public Dictionary<Aspose.Pdf.Core.PdfStream, (HashSet<int> subsetCodes, List<(Aspose.Pdf.Core.PdfDictionary fontDict, Aspose.Pdf.Core.PdfDictionary descriptor, string baseFont, HashSet<int> ownCodes)> fonts, bool isPending)> byProgram = null!;
    public PdfReader reader = default!;
    public Func<int, PdfStream?>? resolveNewStream = null;
    public bool newlyEmbeddedOnly = false;
}
}
