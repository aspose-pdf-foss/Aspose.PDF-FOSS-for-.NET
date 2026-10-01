using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CrossReplaceState
{
    // Advance (in text-space units) a byte string renders with an op's font state:
    // glyph widths + per-glyph Tc − TJ kerns (kern applied only when measuring the
    // op's own full bytes).
    public Dictionary<Aspose.Pdf.Core.PdfDictionary, Aspose.Pdf.Text.FontMetrics?> metricsCache = null!;
    // Gap-aware concatenation: like the absorber, insert a synthetic space between
    // two same-line ops separated by a word-sized positioning gap (text drawn
    // word-per-Tm with no space glyphs), so a spaced phrase can match across ops.
    // Synthetic chars map to op −1 and are trimmed off the match edges.
    public System.Text.StringBuilder allText = null!;
    public List<int> charToOp = null!;
    public string fullText = null!;
    // Byte-level patches (follower Tm x rewrites), applied while copying.
    public SortedList<int, (int end, byte[] text)> patches = null!;
    public System.IO.MemoryStream result = null!;
    public int lastWrite;
    public bool replaced;
    public byte[] streamBytes = null!;
    public string search = null!;
    public string replacement = null!;
    public PdfDictionary pageDict = null!;
    public PdfReader reader = null!;
    public string normalizedSearch = null!;
    public List<CrossTextOp> textOps = null!;
    public int searchIdx;
    public int searchLen;
}
}
