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
private sealed class CidTextDrawState
{
    // Non-embedded predefined CJK fonts (HYGoThic, STSong, KozMin, etc.)
    // have no /FontFile*, so parser is null. PDF 32000 §9.6.6 says the
    // reader should supply a system font that matches /CIDSystemInfo.
    // CjkFallbackFont loads a broad-coverage TTF (Arial Unicode on macOS,
    // Noto CJK on Linux, etc.) once per process and reroutes glyph
    // resolution via CID → Unicode (Adobe tables) → fallback cmap.
    public IGlyphOutlineSource? fallback;
    // A "-V" CMap stacks glyphs DOWN a column instead of along x (PDF 32000 §9.7.4.3).
    // Only the legacy national-charset path handled this; the main CID path always
    // advanced px, so every vertical run smeared sideways and columns overlapped.
    public bool vertical;
    public double penY;
    // 1-byte custom CMaps (codespace <00> <FF>) show one CID per byte.
    public int step;
    // The pen along the run; each glyph advances it.
    public double px;
    public RenderContext ctx = default!;
    public byte[] rawBytes = default!;
    public CidFontInfo cidInfo = default!;
    public IGlyphOutlineSource? parser = null;
    public FontMetrics? fontMetrics = null;
    public double py = 0;
    public double effectiveSize = 0;
    public double hScale = 0;
    public double charSpacingPx = 0;
    public double wordSpacingPx = 0;
    public byte r = 0;
    public byte g = 0;
    public byte b = 0;
    public byte a = 0;
}
}
