using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using System.Globalization;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ParagraphContentState
{
    public double startX;
    public double? clipWidth;
    public Content.ContentStreamBuilder builder = null!;
    public Aspose.Pdf.Text.TextFormattingOptions.WordWrapMode wrapMode;
    public bool needsWrap;
    // Build the visual lines. Each visual line is a horizontal sequence of
    // runs ("chunks") that share a baseline: a fragment's segments flow onto
    // the same line until a hard '\n' or a word-wrap boundary starts a new
    // one, and each fragment begins a fresh line. Word-wrap measures across
    // segment boundaries via a per-character logical buffer so a break can
    // land inside a later segment ("the" Arial 30 + " quick brown…" MSGothic
    // 10 keeps "the quick" together then wraps the rest).
    public List<List<(string text, Aspose.Pdf.Text.TextState ts)>> visualLines = null!;
    // The block: each line advances by its own font size plus the LineSpacing
    // of the line above it (a line's spacing opens the gap BELOW it; the last
    // line's spacing is not part of the block).
    public double blockHeight;
    public bool hasRotation;
    public double startY;
    public Page page = default!;
    public Func<string, string> ensureFont = default!;
    public Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont = null;
}
}
