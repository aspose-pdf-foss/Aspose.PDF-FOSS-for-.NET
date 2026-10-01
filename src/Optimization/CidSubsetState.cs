using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Optimization;

internal static partial class FontSubsetter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CidSubsetState
{
    // FontFile2 stream → (union of used GIDs, participating font dicts)
    public Dictionary<Aspose.Pdf.Core.PdfStream, (HashSet<int> gids, List<(Aspose.Pdf.Core.PdfDictionary type0, Aspose.Pdf.Core.PdfDictionary descriptor)> fonts)> byProgram = null!;
    // CIDToGIDMap stream → union of used CIDs across the fonts sharing it.
    public Dictionary<Aspose.Pdf.Core.PdfStream, (HashSet<int> cids, byte[] data)> byMap = null!;
    // Indirect fonts (keyed by object number) and direct inline ones (the form
    // /DA embed nests its Type0 graph in the resources; the writer hoists it at
    // save time, so at optimize time it exists only as an instance).
    public List<(Aspose.Pdf.Core.PdfDictionary fontDict, HashSet<int> charCodes)> candidates = null!;
    public PdfReader reader = default!;
    public Dictionary<int, HashSet<int>> usedCodes = default!;
    public Dictionary<PdfDictionary, HashSet<int>> directCodes = default!;
    public Func<int, PdfStream?>? resolveNewStream = null;
    public bool newlyEmbeddedOnly = false;
}
}
