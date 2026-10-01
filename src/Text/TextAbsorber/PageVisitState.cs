using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PageVisitState
{
    public IO.PdfReader reader = null!;
    public List<byte[]> contentStreams = null!;
    // Track starting positions for this page
    public int textStart;
    public int yStart;
    // Pure mode: size the per-page character grid up front (see the column-model note
    // on the fields). Raw and MemorySaving modes keep the single-space-per-gap
    // behaviour (cellWidth 0) — MemorySaving output separates
    // column-gapped runs with ONE space, never grid pads.
    public bool pureLayout;
    // A page's content streams concatenate into ONE logical stream sharing one
    // graphics/text state (ISO 32000-1 §7.8.2). Parsing them separately reset the
    // whole text matrix at every boundary — a producer that splits mid-text-object
    // (Acrobat touch-up) had its post-boundary lines tracked at raw Td offsets
    // (tmY ≈ −1.2 instead of the page Y), so the search-rectangle and page-bounds
    // filters dropped them. Join with newline separators and parse once.
    public byte[] combined = null!;
    public Page page = default!;
}
}
