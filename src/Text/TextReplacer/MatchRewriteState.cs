using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MatchRewriteState
{
    public Aspose.Pdf.Text.TextReplacer.CrossTextOp fo = null!;
    public Aspose.Pdf.Text.TextReplacer.CrossTextOp lo = null!;
    public string prefixText = null!;
    public int matchedLastLen;
    public string suffixText = null!;
    public byte[] prefixBytes = null!;
    public byte[] suffixBytes = null!;
    public byte[] matchedLastBytes = null!;
    // What the matched glyphs ADVANCED, summed through the ops' own fonts
    // (and so through their own codes). A Td-chained line places every later
    // glyph off the chain rather than off these advances, so nothing moves
    // the tail when the replacement is wider - see the follower shift below.
    public double advMatched;
    // Replacement: re-encoded into the source font when its glyphs map
    // (source-metric width, keeps the face); otherwise the font-switch path.
    public double advRepl;
    // advRepl is a width guessed from standard metrics: the replacement went into
    // another face whose real advance this rewrite does not know.
    public bool advReplEstimated;
    public double advPrefix;
    public double advMatchedLast;
    public double oldSuffixTmX;
    public double newSuffixTmX;
    // Same-line reflow: the runs after the match on this line move by the width
    // delta so words split across runs stay joined (the follower pass after the
    // rewrite seats both Tm- and Td-positioned runs).
    public double delta;
}
}
