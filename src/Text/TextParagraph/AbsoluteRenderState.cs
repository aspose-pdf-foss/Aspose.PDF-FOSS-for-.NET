using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using System.Globalization;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class AbsoluteRenderState
{
    // Precompute max line width for bg rects (all lines get uniform width).
    public double maxLineWidth;
    public bool anyBg;
    public double textY;
    public double minY;
    public ContentStreamBuilder builder = default!;
    public List<List<(string text, TextState ts)>> visualLines = default!;
    public double startX = 0;
    public double startY = 0;
    public Func<string, string> ensureFont = default!;
    public Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont = null;
    public Page page = default!;
    public List<(string text, Aspose.Pdf.Text.TextState ts)> line = null!;
    public double lineFs;
    // First-line / subsequent-line indent shifts the line's left edge.
    public double lineStartX;
    public double lineWidth;
    // Background / underline use the line's first chunk as the representative
    // state (single-segment lines — the common case — are unchanged).
    public Aspose.Pdf.Text.TextState firstTs = null!;
    // A Position-anchored block paints each line's background on its glyph
    // box (bottom at baseline − descent, so the last line's rect bottom is
    // exactly Position.YIndent); the Rectangle path keeps the historical
    // baseline + fontSize seat its op-level tests pin.
    // A lifted run (embedded face) already has its layout baseline at the
    // box bottom, so its box starts at textY itself.
    public Aspose.Pdf.Text.FontData? firstFd;
    public bool firstLifted;
    public double bgRectY;
    public Color? bg;
    // Draw the line's chunks left-to-right, sharing the baseline textY.
    public double penX;
}
}
