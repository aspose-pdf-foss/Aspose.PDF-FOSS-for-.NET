using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>A horizontal gap between two runs on the same line becomes a separator space. Returning early is what the enclosing loop used to reach by jumping to its skipSpaceInsert label.</summary>
    private bool InsertSeparatorSpaceBetweenRuns(List<RawTextRun> rawFragments, int i, StringBuilder fullText, List<int> charToRun, int[] runStartChar, bool ignoreShadow, ref string? lastKeptText, ref double lastKeptX, ref double lastKeptY, bool[] letterTracked, bool[] squeezedGap)
    {
        // Detect horizontal gaps between consecutive runs on the same line.
        // Skip \r\n sentinels to find the real previous run — BT/ET boundaries
        // inject \r\n but runs in adjacent BT blocks at the same Y are same-line text.
        if (!(i > 0 && rawFragments[i].Text != "\r\n")) return false;
        int prevIdx = i - 1;
        while (prevIdx >= 0 && rawFragments[prevIdx].Text == "\r\n") prevIdx--;
        if (prevIdx < 0) return false;
        var prev = rawFragments[prevIdx];
        // Compare baselines in PAGE space: producers that position each run's line
        // via a per-block cm translation keep text-space Y at 0 for every line, so a
        // raw-Y comparison would fuse the whole page into one line. For the common
        // identity-CTM case page space equals text space, so behaviour is unchanged.
        // HORIZONTAL runs only — rotated/curved glyphs drift in page-Y along the
        // line, so page-space comparison would split them; those keep the raw test.
        // Rotation anywhere (Tm OR CTM, see IsUprightCtm) keeps the raw test too.
        double deltaY;
        if (Math.Abs(rawFragments[i].TmB) <= Math.Abs(rawFragments[i].TmA)
            && Math.Abs(prev.TmB) <= Math.Abs(prev.TmA)
            && IsUprightCtm(rawFragments[i]) && IsUprightCtm(prev))
        {
            var (_, curPageY) = ApplyCtm(rawFragments[i].X, rawFragments[i].Y, rawFragments[i].Ctm);
            var (_, prevPageY) = ApplyCtm(prev.X, prev.Y, prev.Ctm);
            deltaY = Math.Abs(curPageY - prevPageY);
        }
        else
            deltaY = Math.Abs(rawFragments[i].Y - prev.Y);
        if (deltaY < 2.0) // same line
        {
            if (InsertSameLineGapSpace(rawFragments, i, prevIdx, prev, deltaY, fullText, charToRun, runStartChar, ignoreShadow, ref lastKeptText, ref lastKeptX, ref lastKeptY, letterTracked, squeezedGap)) return true;
        }
        return false;
    }
}
