using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TextMatrixOpState
{
    public double newTmY;
    public double tmBraw;
    public double tmCraw;
    public double tmDraw;
    public double tmEraw;
    public double tmFraw;
    // Effective text direction = Tm × CTM. A page that rotates
    // content via `cm` (deskewed scan, landscape form) keeps an
    // identity Tm; only the composed matrix shows it sideways.
    public double cEa;
    public double cEb;
    public double cEc;
    public double cEd;
    public double cEe;
    public double cEf;
    // Emit newline when Tm repositions to a different Y line.
    // Compare against lastRenderedY (where the previous Tj/'/"
    // actually PUT ink) rather than just prevTmY — the tracking Y
    // can differ from the rendered Y by a full 'leading' when the
    // previous BT/ET block used the '/(") operator to step down.
    // Only do this for upright text (tmD > 0). Rotated text (tmD ≈ 0,
    // e.g. 90° rotation [0 fs -fs 0 e f]) has meaningless f-value
    // differences that would generate false line breaks.
    public double tmYThreshold;
    // refY = where the last text landed. lastRenderedY / prevTmY are
    // per-content-stream locals, so they reset to NaN when text
    // continues inside a Form XObject drawn via Do (a common way to
    // place a diagram/overlay). Fall back to the instance-level
    // _currentLineY (which survives the recursion) so the XObject's
    // first positioned run still line-breaks against the outer text
    // instead of gluing onto it (e.g. floor-plan letters merging into
    // the paragraph line above them).
    public double refY;
    // A line the bounds/rectangle filter drops contributes NOTHING —
    // no break, no line/gap tracking. The stream then reads as if the
    // filtered block never existed, so a later run back on the open
    // line's baseline CONTINUES that line (the extractor joins
    // "3 Tl-base-11pcs" + "11-delig basiskookset" this way when
    // the rows between them fall outside the page bounds).
    // UPRIGHT text only — sideways pages keep the original
    // always-track flow (their per-column Tm churn is not line
    // structure). A same-row Tm INHERITS the line's verdict: the
    // filter decides once per line at its first run, so a later
    // bigger-size run on a kept line near the window's top edge
    // stays kept (a kept date) and a dropped line stays dropped.
    public bool tmFiltered;
    public ExtractState xs = default!;
    public bool tmSameRow;
}
}
