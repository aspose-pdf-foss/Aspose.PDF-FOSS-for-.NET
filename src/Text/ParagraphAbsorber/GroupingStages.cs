using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
    /// <summary>The stages of the line grouping: one fragment into its line, then one line split by gaps.</summary>
    private static void SplitGroupedLine(GroupLinesState gl, Aspose.Pdf.Text.ParagraphAbsorber.TextLine line)
    {
        if (line.Fragments.Count <= 1)
        {
            gl.splitLines.Add(line);
            return;
        }

        // Detect column gaps adaptively:
        // Collect all horizontal gaps within this line, then look for a natural break point.
        // If there's a clear gap between word-level spacing and column-level spacing, use it.
        var gaps = new List<double>();
        for (var i = 1; i < line.Fragments.Count; i++)
        {
            var prev = line.Fragments[i - 1];
            var curr = line.Fragments[i];
            var prevRight = prev.Rectangle?.URX ?? GetX(prev);
            var currLeft = curr.Rectangle?.LLX ?? GetX(curr);
            var gap = currLeft - prevRight;
            if (gap > 0) gaps.Add(gap);
        }

        double gapThreshold;
        if (gaps.Count > 2)
        {
            gaps.Sort();
            var medianGap = gaps[gaps.Count / 2];
            // Column gaps should be significantly larger than word gaps
            gapThreshold = Math.Max(medianGap * 3, line.AvgFontSize * 2);
        }
        else
        {
            gapThreshold = line.AvgFontSize * 3;
        }
        var currentSplit = new TextLine();
        currentSplit.Fragments.Add(line.Fragments[0]);

        for (var i = 1; i < line.Fragments.Count; i++)
        {
            var prev = line.Fragments[i - 1];
            var curr = line.Fragments[i];
            var prevRight = prev.Rectangle?.URX ?? GetX(prev);
            var currLeft = curr.Rectangle?.LLX ?? GetX(curr);
            var gap = currLeft - prevRight;

            if (gap > gapThreshold)
            {
                currentSplit.Recalc();
                gl.splitLines.Add(currentSplit);
                currentSplit = new TextLine();
            }
            currentSplit.Fragments.Add(curr);
        }

        currentSplit.Recalc();
        gl.splitLines.Add(currentSplit);
    }

    /// <summary>The stages of the line grouping: one fragment into its line, then one line split by gaps.</summary>
    private static void GroupFragmentIntoLine(GroupLinesState gl, Aspose.Pdf.Text.TextFragment frag)
    {
        if (frag.Rectangle is null) return;
        var fragMidY = (frag.Rectangle.LLY + frag.Rectangle.URY) / 2;
        var fragHeight = frag.Rectangle.Height;
        var tolerance = Math.Max(fragHeight * 0.5, 1.0);

        var found = false;
        for (var i = gl.lines.Count - 1; i >= 0; i--)
        {
            // The join window is the LINE's own half-height, not only the joining
            // fragment's: a superscript citation run (fs 5.9 riding high on a
            // 10.3 pt CJK line) must land on the line whose band covers it — the
            // reference keys each line by (median, half-height) of the line.
            // The fragment-based tolerance stays as the floor so nothing that
            // joined before stops joining.
            var lineTol = Math.Max((gl.lines[i].MaxY - gl.lines[i].MinY) * 0.5, tolerance);
            if (Math.Abs(gl.lines[i].MidY - fragMidY) <= lineTol)
            {
                gl.lines[i].Fragments.Add(frag);
                gl.lines[i].Recalc();
                found = true;
                break;
            }
        }

        if (!found)
        {
            var line = new TextLine();
            line.Fragments.Add(frag);
            line.Recalc();
            gl.lines.Add(line);
        }
    }
}
