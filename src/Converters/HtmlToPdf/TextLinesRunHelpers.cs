using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The emphasis writer's run helpers: whether a position lies in a face run, and the colour run covering it.
    // Mixed-emphasis line in a real face: consecutive
    // embedded-face segments (regular / Bold / Italic
    // variants of the block family), the text position
    // advancing naturally between them. The runs carry
    // the emphasis truth even when a leading <b>
    // promoted the whole block's FontRes.
    private static bool InFaceRuns(System.Collections.Generic.List<(int Start, int Length)>? runs,
        int p, ref int upTo)
    {
        var inside = false;
        if (runs is not null)
            foreach (var (rs, rl) in runs)
            {
                var re = rs + rl;
                if (p >= rs && p < re) { inside = true; upTo = Math.Min(upTo, re); }
                else if (rs > p) upTo = Math.Min(upTo, rs);
            }
        return inside;
    }

    // Span-scoped colour runs: the run's ink applies to
    // its own segments only (the saved email's red bold
    // phrases on a black line).
    private static Color? ColorInRuns(BlockTextState bt, int p2, ref int upTo)
    {
        Color? found = null;
        if (bt.block.ColorRuns is not null)
            foreach (var (rs, rl, rc) in bt.block.ColorRuns)
            {
                var re = rs + rl;
                if (p2 >= rs && p2 < re) { found = rc; upTo = Math.Min(upTo, re); }
                else if (rs > p2) upTo = Math.Min(upTo, rs);
            }
        return found;
    }
}
