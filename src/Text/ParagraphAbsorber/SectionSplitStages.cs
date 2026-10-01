using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
// The stages of the horizontal section split: one region's recursive split.
    private void SplitRegion(SectionSplitState sh, List<TextFragment> frags, bool byRows, int depth)
    {
        if (frags.Count == 0) return;
        List<double> cuts = new();
        if (byRows)
        {
            CollectRowCuts(sh, frags, cuts);
        }
        else
        {
            // The column floor rides the PAGE-wide average font size — the
            // reference computes it once per markup, so a band whose own average
            // is dragged down by superscript-citation runs keeps the page floor
            // (the 9.5 pt gap before an author's citation digit must NOT
            // split at the page's 10 pt floor, while the 12.4 pt gap between a
            // heading number and its text still does).
            var hRun = Math.Max((int)Math.Round(sh.pageW * sh.hOv, MidpointRounding.ToEven) + 2,
                                (int)Math.Round(0.8 * (sh.avgFontSize + 2), MidpointRounding.ToEven));
            var cols = new HashSet<int>();
            int colMin = int.MaxValue, colMax = int.MinValue;
            foreach (var f in frags)
            {
                var r = f.Rectangle;
                if (r is null) continue;
                for (var c = (int)Math.Floor(r.LLX); c < r.URX; c++)
                {
                    if (c + 1 <= r.LLX) continue;
                    cols.Add(c);
                    if (c < colMin) colMin = c;
                    if (c > colMax) colMax = c;
                }
            }
            if (colMin <= colMax)
            {
                var emptyStart = int.MinValue; // rows/columns may be negative in a text frame
                for (var c = colMin; c <= colMax + 1; c++)
                {
                    var empty = c <= colMax && !cols.Contains(c);
                    if (empty && emptyStart == int.MinValue) emptyStart = c;
                    else if (!empty && emptyStart != int.MinValue)
                    {
                        if (c - emptyStart >= hRun)
                            cuts.Add(emptyStart + (c - emptyStart) / 2.0);
                        emptyStart = int.MinValue;
                    }
                }
            }
        }

        if (GridDebug) Console.Error.WriteLine($"[grid] byRows={byRows} depth={depth} cuts={string.Join(",", cuts)}");
        if (cuts.Count == 0)
        {
            // Try the other axis before emitting (rows -> columns -> rows ...); a
            // two-column page with no full-width band splits on its gutter first.
            if (byRows) { SplitRegion(sh, frags, byRows: false, depth); return; }
            var lines = GroupIntoLines(frags);
            lines.Sort(TopToBottomThenLeft);
            if (lines.Count > 0)
                sh.sections.Add(BuildSection(lines, sh.pageBodyRight));
            return;
        }

        var groups = new Dictionary<int, List<TextFragment>>();
        foreach (var f in frags)
        {
            var key = byRows ? (f.PositionOrNull?.YIndent ?? f.Rectangle?.LLY ?? 0)
                             : (f.Rectangle?.LLX ?? 0);
            var g = 0;
            if (byRows) { foreach (var c in cuts) if (key < c) g++; }
            else { foreach (var c in cuts) if (key >= c) g++; }
            if (!groups.TryGetValue(g, out var list)) groups[g] = list = [];
            list.Add(f);
        }
        foreach (var kv in groups)
            SplitRegion(sh, kv.Value, byRows: !byRows, depth + 1);
    }

    /// <summary></summary>
    private void CollectRowCuts(SectionSplitState sh, List<TextFragment> frags, List<double> cuts)
    {
        var rows = new HashSet<int>();
        int rowMin = int.MaxValue, rowMax = int.MinValue;
        foreach (var f in frags)
        {
            var b = f.PositionOrNull?.YIndent ?? f.Rectangle?.LLY ?? 0;
            var fs = f.FontSize > 0 ? f.FontSize : 12;
            var top = b + 1.1 * fs;
            for (var r = (int)Math.Floor(b); r < top; r++)
            {
                if (r + 1 <= b) continue;
                rows.Add(r);
                if (r < rowMin) rowMin = r;
                if (r > rowMax) rowMax = r;
            }
        }
        if (GridDebug) Console.Error.WriteLine($"[grid] rows {rowMin}..{rowMax} n={frags.Count} vRun={sh.vRun} filled={rows.Count}");
        if (rowMin <= rowMax)
        {
            var emptyStart = int.MinValue; // rows/columns may be negative in a text frame
            for (var r = rowMin; r <= rowMax + 1; r++)
            {
                var empty = r <= rowMax && !rows.Contains(r);
                if (empty && emptyStart == int.MinValue) emptyStart = r;
                else if (!empty && emptyStart != int.MinValue)
                {
                    if (r - emptyStart >= sh.vRun)
                        cuts.Add(emptyStart + (r - emptyStart) / 2.0);
                    emptyStart = int.MinValue;
                }
            }
        }
    }
}
