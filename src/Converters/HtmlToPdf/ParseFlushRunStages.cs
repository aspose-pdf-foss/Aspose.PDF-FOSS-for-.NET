using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The spaces inside an isolated span stay non-breaking, a run continuing after a control keeps the collapsed space before it, and a newline before a break survives as a trailing one.</summary>
    private static string ApplyIsolatesAndRunEdges(ParseBlocksState pb, string collapsed, System.Collections.Generic.List<int> rawOf, string raw, bool controlBoxes)
    {
        if (pb.isolateRanges.Count > 0 && collapsed.IndexOf(' ') >= 0)
        {
            var isoChars = collapsed.ToCharArray();
            for (var ci = 0; ci < isoChars.Length; ci++)
                if (isoChars[ci] == ' ' && ci < rawOf.Count)
                    foreach (var (isoS, isoE) in pb.isolateRanges)
                        if (rawOf[ci] >= isoS && rawOf[ci] < isoE) { isoChars[ci] = ' '; break; }
            collapsed = new string(isoChars);
        }
        pb.isolateRanges.Clear();
        // Text continuing an inline run after a control keeps the one collapsed
        // space the markup put between them — " State: " draws
        // with its leading space right at the control's edge.
        if (controlBoxes && pb.inlineRunId != 0 && pb.runPrevWasControl
            && collapsed.Length > 0 && raw.Length > 0 && char.IsWhiteSpace(raw[0]))
        {
            collapsed = " " + collapsed;
            rawOf.Insert(0, -1);
        }
        // A collapsed newline before a <br> survives as the fragment's trailing
        // space (quirks CSS-run docs — the flush the <br> triggers sets the
        // marker; asserted fragment values carry it).
        if (pb.keepTrailingSpace && collapsed.Length > 0 && raw.Length > 0
            && char.IsWhiteSpace(raw[^1]) && collapsed[^1] != ' ')
        {
            collapsed += " ";
            rawOf.Add(raw.Length - 1);
        }
        return collapsed;
    }
}
