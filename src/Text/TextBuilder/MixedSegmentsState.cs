using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextBuilder
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MixedSegmentsState
{
    public Color? fg;
    public Color? sc;
    // A STROKING render mode also needs its pen: the default line width is a
    // full point, which at text sizes floods the glyphs into blobs. Producers
    // that fake a bold weight this way — filling and stroking the same regular
    // face rather than switching to a bold one — set the pen to a fraction of
    // the size, and the replacement has to keep that relationship for the run to
    // come back the same weight.
    public Aspose.Pdf.Text.TextRenderingMode mode;
    public bool strokes;
    // \n inside the fragment text needs explicit T* breaks: PDF's Tj
    // operator renders the whole string on one line (newline chars are
    // either dropped or drawn as .notdef). Without splitting, a multi-
    // line input collapses to a single overflowing line.
    public string normalised = null!;
    public bool hasNewlines;
    // An explicit LineSpacing is extra leading on top of the glyph height
    // (line pitch = fontSize + LineSpacing, matching the generator paginator);
    // otherwise fall back to the default 1.2x leading.
    public double lineHeight;
    // A non-zero TextState.Rotation rotates the run about its position via a
    // text matrix; the descent shift (applied straight down in the unrotated
    // case) is rotated to stay perpendicular to the rotated baseline.
    public double rotation;
    public FragmentWriteState fw = default!;
    public TextFragment fragment = default!;
}
}
