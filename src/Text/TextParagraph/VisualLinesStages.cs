using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
    /// <summary>Record one built visual line against the fragment it came from.</summary>
    private void Emit(VisualLinesState vl, List<(string, TextState)> line) { vl.result.Add(line); _visualLineFragments.Add(vl.current!); }

    /// <summary>Emit a single run (one TextState) as one-or-more visual lines via the
    /// historical per-run wrap, which also handles discretionary hyphenation.</summary>
    private void AddSingleRun(VisualLinesState vl, TextState ts, string runText)
    {
        if (vl.wrap)
        {
            foreach (var hardLine in runText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                foreach (var wl in WrapText(hardLine, ts.Font, ts.FontSize, vl.maxWidth, vl.wrapMode))
                    Emit(vl, new List<(string, TextState)> { (wl, ts) });
        }
        else
        {
            // Even with wrapping off (NoWrap / no clip width), an explicit hard
            // newline (\r, \n, \r\n) in the run text is a line break — split on it
            // so a replacement string that embeds Environment.NewLine renders on
            // multiple lines instead of one run.
            foreach (var hardLine in runText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                Emit(vl, new List<(string, TextState)> { (hardLine, ts) });
        }
    }
}
