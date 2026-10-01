using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FontEmbedState
{
    // Companion font entries are an A-LEVEL (tagged) conversion behaviour: the
    // 1A/2A outputs carry the original entry plus the embedded
    // companion (a pair = 4 entries), while the B/U-level outputs stay
    // within the corpus's size budgets (output <= input + 10%) - so no
    // companions there.
    public bool makeCompanions;
    // Records (once per BaseFont) that the source left a glyph-bearing font
    // unembedded — a PDF/A violation that this pass then fixes by embedding.
    public HashSet<string> reported = null!;
    public HashSet<Aspose.Pdf.Core.PdfDictionary> done = null!;
    // Shared across every dictionary so identical font programs are embedded once.
    public Dictionary<string, (int objNum, string embedName)> fontFileCache = null!;
    public HashSet<Aspose.Pdf.Core.PdfDictionary> visitedRes = null!;
    // The conversion leaves TWO resource entries per fixed font: the
    // slot the content references (embedded in place) AND a companion - for a
    // real face, the same face embedded in full under its bare name; for a
    // Standard-14 replacement, the untouched original (non-embedded, legal
    // because nothing references it and the embedding check is usage-based).
    // Measured: an ArialNarrow pair -> 4 entries (2 embedded + 2 subset
    // copies); a Helvetica pair -> 4 (originals + embedded Arials).
    public Dictionary<Aspose.Pdf.Core.PdfDictionary, Aspose.Pdf.Core.PdfDictionary> companions = null!;
    public PdfFormatConversionOptions? options = null;
    public bool includeStandard14 = false;
}
}
