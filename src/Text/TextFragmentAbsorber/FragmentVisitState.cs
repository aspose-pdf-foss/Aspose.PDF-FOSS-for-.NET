using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FragmentVisitState
{
    public IO.PdfReader reader = null!;
    public List<byte[]> contentStreams = null!;
    // Font keys named by Tf but absent from Resources, reported to the page
    // notification log when the document enables it.
    public List<string>? missingFontKeys;
    public List<Aspose.Pdf.Text.TextFragmentAbsorber.RawTextRun> rawFragments = null!;
    // Collect filled rects when the caller asked for graphics-related results, or when
    // ToAttemptGetUnderlineFromSource is set (so source underlines can be captured and,
    // if the fragment's underline is later toggled off, removed at save time).
    // Always collect fill rects: strikeout detection runs by default (no option).
    // In default mode only thin decoration-candidate rects are kept (see ExtractRuns)
    // so the extra bookkeeping stays cheap; the background/underline consumers below
    // remain gated behind their options.
    public List<Aspose.Pdf.Text.TextFragmentAbsorber.RawFillRect> fillRects = null!;
    // Occlusion candidates for hidden-text detection (always on; rect-only paths).
    public List<Aspose.Pdf.Text.TextFragmentAbsorber.RawCoverRect> coverRects = null!;
    // Apply page rotation CTM so fragment coordinates are in the viewer's
    // natural coordinate system (same as the public API behaviour).
    public Aspose.Pdf.Text.TextFragmentAbsorber.Matrix? rotCtm;
    public Rectangle? searchRect;
    public Page page = default!;
    public bool tolerantFonts = false;
    public HashSet<object>? seenForms = null;
}
}
