using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class FontMetrics
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SimpleMetricsState
{
    public int firstChar;
    public int lastChar;
    // Type 3 fonts express glyph widths in glyph space; they become text-space
    // advances only after the horizontal component of /FontMatrix is applied
    // (PDF 32000 §9.6.5). The shared advance formula divides the stored width
    // by 1000, so pre-scale Type 3 widths by FontMatrix[0]·1000 to land in the
    // same 1/1000-text-space unit the formula expects. Other simple fonts already
    // store /Widths in 1/1000 units, so they keep a unit scale.
    public bool isType3;
    public double widthScale;
    public int[]? widths;
    // A /Widths entry written as a real is the face's own advance: a face drawn on
    // a 2048-unit em has advances that are not whole 1000ths, and dropping the
    // fraction shortens a re-measured paragraph by a fifth of a point. Keep those
    // entries alongside the integer table, which every all-integer array — very
    // nearly every real document — leaves exactly as it was.
    public Dictionary<int, double>? exactWidths;
    public Aspose.Pdf.Core.PdfObject? widthsObj;
    public int defaultWidth;
    public Aspose.Pdf.Text.FontMetrics metrics = null!;
    // MacRoman-encoded Standard-14 fonts remap codes before the (WinAnsi-shaped)
    // built-in width lookup — see MacRomanToWinAnsiCode.
    public Aspose.Pdf.Core.PdfObject? encObj;
    public PdfDictionary fontDict = default!;
    public PdfReader reader = default!;
    public string normalizedBase = default!;
    public bool isStandard14 = false;
}
}
