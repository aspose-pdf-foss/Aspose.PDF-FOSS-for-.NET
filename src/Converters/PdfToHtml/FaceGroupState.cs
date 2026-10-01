using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FaceGroupState
{
    // Overlay segments in visual order; re-drawn whitespace-only
    // overlap segments are dropped.
    public List<(double X, System.Text.StringBuilder Text, double PenEnd, double GlyphEnd)> ordered = null!;
    public List<(string text, double startX, double glyphEnd)> emit = null!;
    public double coveredTo;
    // Re-cut segment boundaries at word boundaries: a span is
    // never split inside a word, but a justified line's
    // raw shows often break mid-word ("laborat" + "ory"). A
    // segment's leading word fragment moves into the previous
    // span, and the following span starts at the fragment's
    // measured end past its old device anchor.
    public double spaceAdv;
    public int lastLsNum;
}
}
