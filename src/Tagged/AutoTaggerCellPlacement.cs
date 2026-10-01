using System;
using System.Collections.Generic;
using System.Linq;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // Two cells of a row stand as far from an edge of it when their distances differ by at most this (points).
    private const double SameEdgeTolerance = 1.0;
    // A rule runs along a row's edge when it stands within this many points of it.
    private const double RowRuleTolerance = 1.5;
    // The share of its size a line's text reaches over its baseline, and under it (the box of a run of text).
    private const double TextBoxAscent = 0.8;
    private const double TextBoxDescent = 0.2;

    /// <summary>Where a cell's text stands in its box: how it is set from top to bottom, the room it leaves on
    /// each side (before, after, start, end), and whether it is centred across.</summary>
    private readonly record struct CellPlacement(LS.AttributeName Block, double Before, double After, double Start, double End, bool Centred);

    /// <summary>Whether every edge of a table's rows runs along a rule: the rows are as tall as they are drawn
    /// (a table with no rules between its rows has its row edges midway between its lines of text).</summary>
    private static bool RowsAreRuled(List<PageContentScan.Rule> edgeRules, List<double> rowY)
        => rowY.Count >= 2 && rowY.All(y => edgeRules.Any(r => r.Horizontal && Math.Abs(r.At - y) <= RowRuleTolerance));

    /// <summary>Where the text of each cell of a ruled table stands in its box. A row whose cells hold text of
    /// different heights shows how the table sets them: all as far from the top is set from the top, all as far
    /// from the bottom from the bottom, neither but each midway is centred. A row that shows nothing is set as
    /// most of the table's rows are, from the top when none shows. A cell of one column is centred across when
    /// its text stands as far from both sides, further in than the column's cells set from the left - or in a
    /// column whose cells all stand so, not all starting alike (its widest cell is centred as the others are).</summary>
    private static Dictionary<(int Row, int Col), CellPlacement> CellPlacements(List<Line> lines, (int Row, int Col)[,]? cells,
        List<double> colX, List<double> rowY, int rows, int cols)
    {
        var boxes = new Dictionary<(int Row, int Col), (int RowSpan, int ColSpan, double Before, double After, double Start, double End)>();
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                if (cells is not null && cells[r, c] != (r, c)) continue;
                var (rowSpan, colSpan) = cells is not null ? CellSpan(cells, r, c) : (1, 1);
                if (c + colSpan >= colX.Count || rows - r - rowSpan < 0 || rows - r >= rowY.Count) continue;
                double left = colX[c], right = colX[c + colSpan], bottom = rowY[rows - r - rowSpan], top = rowY[rows - r];
                var frags = lines.Where(l => l.Baseline > bottom && l.Baseline <= top).SelectMany(l => l.Frags)
                    .Where(f => !string.IsNullOrWhiteSpace(f.Text) && (f.X + f.R) / 2 >= left && (f.X + f.R) / 2 <= right).ToList();
                if (frags.Count == 0) continue;
                boxes[(r, c)] = (rowSpan, colSpan, top - frags.Max(f => f.Base + TextBoxAscent * f.Size),
                    frags.Min(f => f.Base - TextBoxDescent * f.Size) - bottom, frags.Min(f => f.X) - left, right - frags.Max(f => f.R));
            }

        var shown = new Dictionary<int, LS.AttributeName>();
        foreach (var row in boxes.Where(b => b.Value.RowSpan == 1).GroupBy(b => b.Key.Row))
        {
            var set = row.Select(b => b.Value).ToList();
            if (set.Count < 2) continue;
            var sameBefore = set.Max(b => b.Before) - set.Min(b => b.Before) <= SameEdgeTolerance;
            var sameAfter = set.Max(b => b.After) - set.Min(b => b.After) <= SameEdgeTolerance;
            if (sameBefore && !sameAfter) shown[row.Key] = LS.AttributeName.BlockAlign_Before;
            else if (sameAfter && !sameBefore) shown[row.Key] = LS.AttributeName.BlockAlign_After;
            else if (!sameBefore && set.All(b => Math.Abs(b.Before - b.After) <= 2 * SameEdgeTolerance)) shown[row.Key] = LS.AttributeName.BlockAlign_Middle;
        }
        var usual = shown.Values.GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
                    ?? LS.AttributeName.BlockAlign_Before;

        var pads = boxes.Where(b => b.Value.ColSpan == 1).GroupBy(b => b.Key.Col).ToDictionary(g => g.Key, g => g.Min(b => b.Value.Start));
        var midway = boxes.Where(b => b.Value.ColSpan == 1).GroupBy(b => b.Key.Col)
            .Where(g => g.All(b => Math.Abs(b.Value.Start - b.Value.End) <= AlignTolerance)
                        && g.Max(b => b.Value.Start) - g.Min(b => b.Value.Start) > AlignTolerance)
            .Select(g => g.Key).ToHashSet();
        var result = new Dictionary<(int Row, int Col), CellPlacement>();
        foreach (var pair in boxes)
        {
            var (at, box) = (pair.Key, pair.Value);
            var centred = box.ColSpan == 1 && Math.Abs(box.Start - box.End) <= AlignTolerance
                          && pads.TryGetValue(at.Col, out var pad) && (box.Start > pad + AlignTolerance || midway.Contains(at.Col));
            result[at] = new CellPlacement(shown.TryGetValue(at.Row, out var block) ? block : usual,
                Math.Max(0, box.Before), Math.Max(0, box.After), Math.Max(0, box.Start), Math.Max(0, box.End), centred);
        }
        return result;
    }

    /// <summary>State where a cell's text stands in its box: its /BlockAlign and the /Padding round it (before,
    /// after, start, end; the Layout attributes, ISO 32000-1 §14.8.5.4), and /TextAlign Center when it is
    /// centred across and states no alignment.</summary>
    private static void StatePlacement(LS.StructureElement cell, CellPlacement placement)
    {
        var layout = cell.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout);
        var block = new LS.StructureAttribute(LS.AttributeKey.BlockAlign);
        block.SetNameValue(placement.Block);
        layout.SetAttribute(block);
        var padding = new LS.StructureAttribute(LS.AttributeKey.Padding);
        padding.SetArrayNumberValue([Math.Round(placement.Before, 1), Math.Round(placement.After, 1), Math.Round(placement.Start, 1), Math.Round(placement.End, 1)]);
        layout.SetAttribute(padding);
        if (placement.Centred && layout.GetAttribute(LS.AttributeKey.TextAlign) is null) StateTextAlign(cell, LS.AttributeName.TextAlign_Center);
    }
}
