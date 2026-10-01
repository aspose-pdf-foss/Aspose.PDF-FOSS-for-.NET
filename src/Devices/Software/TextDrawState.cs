using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TextDrawState
{
    public double[] tm = null!;
    public double[] ctm = null!;
    public double[] trm = null!;
    // PDF allows a negative /Tf font size — the text matrix encodes the direction
    // (e.g. mirrored text in some XFA-derived forms). Use |fontSize|
    // for the rasterizer's pixel-size budget; horizontal direction is already
    // baked into the text matrix via Tm × CTM.
    public double fontSize;
    public double effectiveSize;
    // The glyph size above is the text matrix's VERTICAL magnitude. A matrix that
    // scales the two axes differently - a stamp squeezed to fit its box writes
    // `0.163 0 0 10 cm` - then draws the glyphs at their vertical size in both
    // directions, so a band of crushed text came out as inch-high letters running off
    // the page. Carry the ratio of the two axes as a horizontal factor; the rotated
    // path below builds its own matrix from trm and already has it.
    public double trmXLen;
    public double trmYLen;
    public double anisotropy;
    public double x;
    public double y;
    // Convert to pixel coords
    public double px;
    public double py;
    public byte r;
    public byte g;
    public byte b;
    public byte a;
    public double hScale;
    // PDF 32000 §9.4.4: the horizontal scaling Th stretches the glyphs AND every
    // horizontal displacement - the advance, Tc and Tw all carry the same factor. The
    // renderer already folded Th's SIGN into the glyph axes but deliberately left its
    // magnitude out, so a run set to 150% or 66% drew at 100% and three lines that
    // differ only in Tz came out the same width. The font's own horizontal scale is the
    // one factor every glyph and advance is already multiplied by, so Th rides with it.
    public double textHScale;
    // A text matrix that rotates or skews cannot be drawn by the axis-aligned glyph
    // path: it rasterises upright and steps the pen along the raster’s x, so rotated
    // text came out as diagonal smears of upright glyphs. Hand the run its own
    // font-unit-to-device map and let the pen travel along the real baseline.
    // Also when the run is MIRRORED. A 180-degree page turn leaves trm axis-aligned but
    // with both diagonals negative, and the upright rasteriser can express neither: it
    // drew the glyphs the right way up and walked the pen rightwards, so the line landed
    // one full text-width away from where it belongs and read forwards instead of back.
    // PDF 32000 §9.4.4: the text rendering matrix is [Tfs·Th, 0; 0, Tfs] × Tm × CTM, so a
    // NEGATIVE /Tf size flips BOTH glyph axes and a negative Tz flips the x axis. Those
    // signs are NOT in trm (which is only Tm × CTM), and using |Tfs| threw them away: a
    // generator that writes "1 0 0 -1 0 H cm" for top-down coordinates and cancels it with
    // "-10 Tf" / "-100 Tz" then rendered its whole page upside down and mirrored.
    // Only the SIGNS are folded in here — Th's magnitude stays out, as before, so the
    // calibrated advance/spacing behaviour of every other document is untouched.
    public double thSign;
    public double fsSign;
    public Aspose.Pdf.Text.IGlyphOutlineSource? parser;
    public Aspose.Pdf.Text.FontMetrics? fontMetrics;
    public Aspose.Pdf.Text.CidFontInfo? cidInfo;
    // Two code paths: CID (Type0 with 2-byte encoding) walks rawBytes to produce CIDs and
    // resolves GIDs via /CIDToGIDMap; simple fonts use the decoded Unicode string and the
    // TTF's own cmap. Subset CID fonts routinely strip their TTF cmap, so going via the
    // CID→GID map is the only path that produces correct glyphs.
    // Tc/Tw are in unscaled text-space units (PDF 32000 §9.3.3). They get multiplied by
    // the full text-to-device chain: |Tm × CTM| to land in PDF points, then × ctx.Scale
    // for pixels. Multiplying only by ctx.Scale (as before) over-counted by 1/|CTM| and
    // produced huge inter-word gaps on content streams with a sub-unit `cm` scaling
    // (ACORD forms use `0.12 cm`, making the over-count 8× too wide).
    public double textSpaceScale;
    public double charSpacingPx;
    public double wordSpacingPx;
    public RenderContext ctx = default!;
    public string text = default!;
    public byte[] rawBytes = default!;
    public GraphicsState state = default!;
}
}
