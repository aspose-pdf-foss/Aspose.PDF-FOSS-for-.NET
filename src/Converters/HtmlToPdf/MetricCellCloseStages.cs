using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The stages of the metric cell close: interleaving the nested grids with the text, and splitting the sized segments.</summary>
    private static void SplitSizedCellSegments(MetricParseState mps)
    {
        if (mps.sizedSegs.Count > 1 && mps.cell!.SubTables is not { Count: > 0 })
        {
            var distinctFs = new HashSet<double>();
            foreach (var (sb2, fs2) in mps.sizedSegs)
                if (sb2.ToString().AsSpan().Trim().Length > 0)
                    distinctFs.Add(fs2 ?? 0);
            if (distinctFs.Count > 1)
            {
                mps.cell.SizedRuns = new List<(string, double)>();
                foreach (var (sb2, fs2) in mps.sizedSegs)
                {
                    var st2 = CollapseWs(sb2.ToString().Replace('\u0001', ' '));
                    // a whitespace-only segment still advances the pen at
                    // ITS size (the 10 pt space between '23 May' and the
                    // 9 pt parenthetical)
                    if (st2.Length == 0 && sb2.Length > 0) st2 = " ";
                    if (st2.Length > 0)
                        mps.cell.SizedRuns.Add((st2, fs2 ?? 0));
                }
            }
        }
    }

    /// <summary></summary>
    private static bool InterleaveNestedGrids(MetricParseState mps, StringBuilder text, bool brAfterGridCounts = false)
    {
        var raw = text.ToString();
        var mms = Regex.Matches(raw, "\u0002(\\d+)\u0003");
        var inkAfterMarker = false;
        var brAfterMarker = false;
        if (mms.Count > 0)
        {
            var afterFirst = raw[(mms[0].Index + mms[0].Length)..];
            afterFirst = Regex.Replace(afterFirst, "\u0002\\d+\u0003", "");
            brAfterMarker = afterFirst.IndexOf('\u0001') >= 0;
            foreach (var ch in afterFirst)
                if (!char.IsWhiteSpace(ch) && ch is not ('\u0001' or '\u00A0'))
                { inkAfterMarker = true; break; }
        }
        // …and under the UA flow a line break AFTER the grid keeps its place too: each br past
        // a block is a whole line box (probed: two brs after a nested table hold 27 pt under it,
        // three 40.5), which the stacked draw had spent ABOVE the grid.
        if (inkAfterMarker || (brAfterGridCounts && brAfterMarker))
        {
            bool BoldAt(int at)
            {
                var on = false;
                foreach (var (mp, mo) in mps.cellBoldMarks)
                { if (mp > at) break; on = mo; }
                return on;
            }
            var flow = new List<(string? TableHtml, string Text, bool Bold)>();
            void AddRuns(int from, int to)
            {
                var runStart = from;
                var runBold = BoldAt(from);
                for (var ci = from + 1; ci <= to; ci++)
                {
                    var b2 = ci < to ? BoldAt(ci) : !runBold;
                    if (b2 == runBold && ci < to) continue;
                    var chunk = CollapseWs(raw[runStart..ci]);
                    if (chunk.Length > 0) flow.Add((null, chunk, runBold));
                    runStart = ci; runBold = b2;
                }
            }
            var fpos = 0;
            foreach (Match fmm in mms)
            {
                if (fmm.Index > fpos) AddRuns(fpos, fmm.Index);
                if (int.TryParse(fmm.Groups[1].Value, out var fti)
                    && fti < mps.nestedTables!.Count)
                    flow.Add((mps.nestedTables[fti], "", false));
                fpos = fmm.Index + fmm.Length;
            }
            if (fpos < raw.Length) AddRuns(fpos, raw.Length);
            mps.cell!.Flow = flow;
            mps.cell.Bold = false;   // bold lives on the runs now
        }
        return true;
    }

    /// <summary>The plain-serif cell's first ink sets the floor its closing height is measured from.</summary>
    private static void ApplySerifCellInkFloor(MetricParseState mps, bool stdSerif)
    {
        if (stdSerif && mps.firstInkSeen)
        {
            if (mps.cell!.DivSegs is null && mps.cell.SubTables is null && (BuildRunLineSegments(mps) || BuildSizedLineSegments(mps)))
            { mps.cell.Text = ""; mps.cell.Runs = null; }
            else
            {
                if (mps.cell.SubTables is { Count: > 0 } || (mps.cell.DivSegs?.Exists(s => s.NestedTable >= 0) ?? false))
                { mps.cell.GridFontSize = mps.cell.FontSize; mps.cell.GridFace = mps.cell.Face; mps.cell.GridTypoSaved = true; }
                mps.cell.FontSize = mps.firstInkFs;
                mps.cell.Face = mps.firstInkFace;
                mps.cell.Fore = mps.firstInkFore;
            }
        }
    }

    /// <summary>A cell holding nested tables splits its text at the table marks, so each segment seats against its own table.</summary>
    private static void SplitNestedTableCellSegments(MetricParseState mps, bool stdSerif)
    {
        if (mps.nestedTables is not null && mps.cell!.Text.IndexOf('\u0002') >= 0)
        {
            foreach (Match nm in Regex.Matches(mps.cell.Text, "\u0002(\\d+)\u0003"))
                if (int.TryParse(nm.Groups[1].Value, out var nti) && nti < mps.nestedTables.Count)
                {
                    (mps.cell.SubTables ??= new List<string>()).Add(mps.nestedTables[nti]);
                    (mps.cell.SubTableHostClasses ??= new List<string[]?>()).Add(
                        mps.nestedTableHostClasses is { } nhc && nti < nhc.Count ? nhc[nti] : null);
                }
            // (…and the breaks after the last grid, with no ink after them, are lines under it)
            if (stdSerif && mps.cell.DivSegs is null)
            {
                var afterLast = mps.cell.Text[(mps.cell.Text.LastIndexOf('\u0003') + 1)..];
                if (afterLast.Trim(' ', '\u0001').Length == 0)
                    foreach (var ch in afterLast) if (ch == '\u0001') mps.cell.TrailingBreakLines++;
            }
            // text that only follows the last grid is a trailing paragraph, not a line above them
            var firstGrid = mps.cell.Text.IndexOf('\u0002');
            mps.cell.TextTrailsGrids = firstGrid >= 0 && mps.cell.Text[..firstGrid].Trim(' ', '\t', '\r', '\n').Length == 0;   // an nbsp AHEAD of a grid is content
            mps.cell.Text = Regex.Replace(mps.cell.Text, "\u0002\\d+\u0003", " ");
            mps.cell.Text = CollapseWs(mps.cell.Text);
        }
    }
}
