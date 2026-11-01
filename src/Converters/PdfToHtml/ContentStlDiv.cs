using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>Solve one line div's glyphs into spans and emit them.
    /// Returns false when the data cannot be solved (caller falls back).</summary>
    private static bool EmitStlSolvedDiv(StringBuilder sb, List<StlLineGlyph> glyphs,
        List<StlRunStyle> styles, StyleRegistry styleReg, ClassNamer classNamer,
        string divCls, string zStyle, double pageLLX, double yTop, double baselineY,
        Func<double, double, LinkTarget?>? linkFor, List<(string Label, string Href)>? popupItems,
        double turnedOverShiftLeftEm = 0, double turnedOverShiftTopEm = 0,
        bool emGrid = false, bool perGlyph = false, TextLayerModel? model = null, int seq = 0,
        Func<char, bool>? isMintedStandIn = null)
    {
        // A prefix-joined line arrives with its fragments in DRAW order (body
        // first, title behind it): the em-compensation solve reads the line
        // left-to-right, so a real inversion re-orders by pen position. Kern
        // jitter under a point is left alone; other dialects never prefix-join.
        if (emGrid)
            for (var q = 1; q < glyphs.Count; q++)
                if (glyphs[q].StartX < glyphs[q - 1].StartX - 1.0)
                {
                    glyphs = glyphs.OrderBy(x => x.StartX).ToList();
                    break;
                }

        var sd = new StlDivState();
        sd.sb = sb;
        sd.glyphs = glyphs;
        sd.styles = styles;
        sd.styleReg = styleReg;
        sd.classNamer = classNamer;
        sd.divCls = divCls;
        sd.zStyle = zStyle;
        sd.pageLLX = pageLLX;
        sd.yTop = yTop;
        sd.baselineY = baselineY;
        sd.linkFor = linkFor;
        sd.popupItems = popupItems;
        sd.turnedOverShiftLeftEm = turnedOverShiftLeftEm;
        sd.turnedOverShiftTopEm = turnedOverShiftTopEm;
        sd.emGrid = emGrid;
        sd.perGlyph = perGlyph;
        sd.model = model;
        sd.seq = seq;
        sd.isMintedStandIn = isMintedStandIn;
        sd.lo = 0;
        if (TrimAndClassifyStlLine(sd) is { } trimAndClassifyStlLineResult) return trimAndClassifyStlLineResult;
        while (sd.i <= sd.hi)
        {
            if (!EmitStlGlyph(sd)) break;
        }
        if (EmitStlPopupsOrEarlyLine(sd) is { } emitStlPopupsOrEarlyLineResult) return emitStlPopupsOrEarlyLineResult;
        EmitStlPart(sd, sd.items);
        return true;

    }

    private const double RowTol = 0.05;   // divs within this top distance share a row
    private const double LaneTol = 0.1;   // lefts within this distance share a column

    /// <summary>Two lefts within <see cref="LaneTol"/> share a column.</summary>
    private static bool Near(double a, double b) => Math.Abs(a - b) <= LaneTol;

    /// <summary>A LEADER region: every row is a label column plus a title cell whose text
    /// runs out in a dot leader. Consecutive leader regions form a CHAIN.</summary>
    private static bool IsLeaderRegion(List<List<(double L, double T, string Html)>> region)
    {
        foreach (var row in region)
        {
            if (row.Count < 2) return false;
            var rightmost = row[0];
            foreach (var d in row) if (d.L > rightmost.L) rightmost = d;
            if (!SafeRegex.IsMatch(rightmost.Html, @"\.{8,}"))
                return false;
        }
        return true;
    }

    /// <summary>A chain of leader regions emits as: the first region's label cells plus the HEAD
    /// row's label of a deeper second region, then every title cell in row order, then the
    /// remaining label cells in row order.</summary>
    private static void EmitStlLeaderChain(List<string> order,
        List<List<List<(double L, double T, string Html)>>> regions, int ri, int chainLen)
    {
        var chain = regions.GetRange(ri, chainLen);
        double LabelLeft(List<List<(double L, double T, string Html)>> region)
        {
            var min = double.MaxValue;
            foreach (var row in region) foreach (var d in row) min = Math.Min(min, d.L);
            return min;
        }
        var deeperSecond = LabelLeft(chain[1]) > LabelLeft(chain[0]) + LaneTol;
        var labelsFirst = new List<string>();
        var titles = new List<string>();
        var labelsRest = new List<string>();
        for (var ci = 0; ci < chain.Count; ci++)
        {
            for (var rowIdx = 0; rowIdx < chain[ci].Count; rowIdx++)
            {
                var row = chain[ci][rowIdx];
                var rightmost = row[0];
                foreach (var d in row) if (d.L > rightmost.L) rightmost = d;
                var leading = ci == 0 || (ci == 1 && rowIdx == 0 && deeperSecond);
                foreach (var html in row.Select(d => d.Html))
                {
                    if (ReferenceEquals(html, rightmost.Html)) titles.Add(html);
                    else if (leading) labelsFirst.Add(html);
                    else labelsRest.Add(html);
                }
            }
        }
        foreach (var h in labelsFirst) order.Add(h);
        foreach (var h in titles) order.Add(h);
        foreach (var h in labelsRest) order.Add(h);
    }

    /// <summary>Whether the run's rows, chained into regions as below, hold a dot-leader region.</summary>
    private static bool HasLeaderRegion(List<(double L, double T, string Html)> run)
        => BuildStlRegions(BuildStlRows(run)).Exists(IsLeaderRegion);

    private static List<List<(double L, double T, string Html)>> BuildStlRows(List<(double L, double T, string Html)> run)
    {
        var rows = new List<List<(double L, double T, string Html)>>();
        foreach (var d in run.OrderBy(x => x.T).ToList())
        {
            if (rows.Count > 0 && Math.Abs(rows[^1][0].T - d.T) <= RowTol) rows[^1].Add(d);
            else rows.Add(new List<(double L, double T, string Html)> { d });
        }
        return rows;
    }

    private static List<List<List<(double L, double T, string Html)>>> BuildStlRegions(List<List<(double L, double T, string Html)>> rows)
    {
        var regions = new List<List<List<(double L, double T, string Html)>>>();
        List<double>? regionLanes = null;
        foreach (var row in rows)
        {
            var rowLanes = new List<double>();
            foreach (var d in row.Where(d => !rowLanes.Exists(x => Near(x, d.L))))
                rowLanes.Add(d.L);
            var chains = regionLanes is not null
                && (rowLanes.TrueForAll(l => regionLanes.Exists(r => Near(l, r)))
                    || regionLanes.TrueForAll(r => rowLanes.Exists(l => Near(l, r))));
            if (!chains)
            {
                regions.Add(new List<List<(double L, double T, string Html)>>());
                regionLanes = new List<double>();
            }
            regions[^1].Add(row);
#pragma warning disable S3267 // the if's null test narrows the item for the body - a Where predicate does not carry that null state into the loop
            foreach (var l in rowLanes)
#pragma warning restore S3267
                if (!regionLanes!.Exists(r => Near(r, l))) regionLanes.Add(l);
        }
        return regions;
    }

    private static List<string> OrderStlRun(List<(double L, double T, string Html)> run, List<LineBox>? boxes)
    {
        var order = new List<string>();
        if (run.Count <= 1)
        {
            foreach (var d in run) order.Add(d.Html);
            return order;
        }
        // With the lines' boxes known, a run that is not a dot-leader chain reads block by
        // block (see EmitBlockOrder); the row/lane model below stays for the leader chains
        // and for a layer whose boxes are not available.
        if (boxes is not null && !HasLeaderRegion(run))
        {
            EmitBlockOrder(run, boxes, Enumerable.Range(0, run.Count).ToList(), order);
            return order;
        }
        var regions = BuildStlRegions(BuildStlRows(run));
        // A chain of leader regions emits in label/title order; anything else emits
        // region-major: lanes left-to-right, columns top-down.
        var ri = 0;
        while (ri < regions.Count)
        {
            var chainLen = 0;
            while (ri + chainLen < regions.Count && IsLeaderRegion(regions[ri + chainLen])) chainLen++;
            if (chainLen >= 2)
            {
                EmitStlLeaderChain(order, regions, ri, chainLen);
                ri += chainLen;
                continue;
            }
            var region = regions[ri];
            var lanes = new List<double>();
            foreach (var row in region)
                foreach (var d in row.Where(d => !lanes.Exists(x => Near(x, d.L))))
                    lanes.Add(d.L);
            lanes.Sort();
            foreach (var lane in lanes)
                foreach (var row in region)
                    foreach (var d in row.Where(d => Near(d.L, lane)))
                        order.Add(d.Html);
            ri++;
        }
        return order;
    }
}
