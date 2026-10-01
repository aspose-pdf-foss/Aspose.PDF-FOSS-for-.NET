using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class LetterTrackedGapsState
{
    public bool[] tracked = null!;
    public int i;
    public List<RawTextRun> runs = default!;
    // Gather one line: consecutive content runs sharing a baseline. Compare each run
    // to the PREVIOUS run's Y (not the line's first) so a slightly sloped/italic
    // glyph-by-glyph baseline (a small consistent per-glyph drift) stays one line
    // instead of fragmenting — matching the guard site's adjacent deltaY check.
    public List<int> line = null!;
    public double prevLineY;
    public int j;
    public double lineY;
    public double[] gaps = null!;
    public double[] subWord = null!;
    // A gap bordered by a MULTI-WORD run (a real space glyph among other
    // characters) is a WORD boundary, never intra-word letter tracking:
    // tracking splits one word into letter runs ("M","ARK"), while a
    // justified line drawn word-per-Tm has uniform ~space-sized gaps between
    // whole phrases ("…to 24"|"MAR"|"2013. During…") that must keep their
    // word spaces. A run that IS whitespace (an explicit space-glyph run
    // between tracked letters — 'M'|'ARK'|' '|'A.') stays trackable: such
    // lines mark their word breaks with the space runs themselves.
    // ★ Tracking is a LATIN phenomenon: it spreads the letters of one word,
    // which is why suppressing its gaps keeps the word whole. A CJK glyph is
    // not a letter of a word — a name set with its ideographs spread evenly
    // across a fixed column width looks identical to tracked text, and the
    // gaps there are real: a page that draws '監 察 監 督 官' glyph by glyph
    // reads back with a space at every gap. So a gap facing an ideograph or
    // kana is never claimed as tracking.
    public bool[] cjky = null!;
    public bool[] wordy = null!;
    public int k0;
}
}
