using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BuildFragmentsState
{
    // Index the fill rects once by vertical midpoint so the per-run decoration probes
    // below query a small baseline-local slice instead of rescanning the whole list.
    public Aspose.Pdf.Text.TextFragmentAbsorber.FillRectIndex? fillIndex;
    public int runIndex;
    public List<RawTextRun> rawFragments = default!;
    public Rectangle? searchRect = null;
    public Page? sourcePage = null;
    public XForm? sourceForm = null;
    public int pageIndex = 0;
    public List<RawFillRect>? fillRects = null;
    public List<RawCoverRect>? coverRects = null;
    public bool occludedByLaterText;
    public double upX_;
    public double upY_;
    public double tmScale;
    public double effectiveFontSize;
    public Aspose.Pdf.Text.TextState textState = null!;
    // Tz-scaled advances really are wider on the page (a column-fit TOC
    // leader's rect ends where its stretched glyphs end).
    public double width;
    public double rectStartX;
    public double rectStartY;
    public double endX;
    public double endY;
    public double llx;
    public double lly;
    public double urx;
    public double ury;
    public Rectangle rect = null!;
    public double px;
    public double py;
    public string clipText = null!;
    public double clipX;
    public double clipY;
    public double clipWidth;
    public double descentOffset;
    public double ascentHeight;
    public double tdx;
    public double tdy;
    public double? rotDeg;
    // SearchForTextRelatedGraphics: when a fill rect collected from the content stream
    // contains the fragment's text origin, copy its color to the TextState as the
    // background. Search the most recently emitted rect first — later draw order wins
    // for overlapping rects, matching the visible z-order on the page.
    // Assigning to the TextState's backing field directly avoids triggering the
    // save-time rect-injection registration that the public setter performs.
    public RawFillRect? capturedUl;
    public RawFillRect? capturedBg;
}
}
