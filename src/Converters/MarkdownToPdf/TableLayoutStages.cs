using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class MarkdownToPdfConverter
{
    /// <summary>The stages of the markdown table layout: the column fit and one styled row.</summary>
    private static void LayoutStyledRow(MarkdownTableState mt, int r)
    {
        var cellLines = new List<List<List<Seg>>>();
        for (var c = 0; c < mt.cols; c++)
            cellLines.Add(WrapRuns(mt.styled[r][c], mt.size, mt.widths[c], mt.widths[c]));
        var maxLines = Math.Max(1, cellLines.Max(cl => cl.Count));

        if (r > 0) mt.firstBase += mt.lastMax * mt.size * LineHeightEm + RowPad;
        if (r > 0 && mt.firstBase + (maxLines - 1) * mt.size * LineHeightEm + DescentEm * mt.size > mt.flow.Limit)
        {
            mt.flow.NewPage();
            mt.firstBase = mt.flow.Top + RowPad + AscentEm * mt.size;
        }
        mt.lastMax = maxLines;

        for (var c = 0; c < mt.cols; c++)
        {
            var lines = cellLines[c];
            if (lines.Count == 0) continue;
            // A short cell centres vertically in its row.
            var cellBase = mt.firstBase + (maxLines - lines.Count) * mt.size * LineHeightEm / 2;
            for (var k = 0; k < lines.Count; k++)
            {
                var x = mt.colX[c];
                if (r == 0)
                {
                    // Header cells centre over their column.
                    var w = SegsWidth(lines[k], mt.size);
                    x = mt.colX[c] + (mt.widths[c] - w) / 2;
                }
                EmitRunLine(mt.flow, x, cellBase + k * mt.size * LineHeightEm, lines[k], mt.size);
            }
        }
    }

    /// <summary>The stages of the markdown table layout: the column fit and one styled row.</summary>
    private static void FitTableColumns(MarkdownTableState mt)
    {
        var flexible = Enumerable.Range(0, mt.cols).ToList();
        var remaining = mt.avail;
        bool changed = true;
        while (changed)
        {
            changed = false;
            var share = remaining / Math.Max(1, flexible.Count);
            for (var fi = flexible.Count - 1; fi >= 0; fi--)
            {
                var c = flexible[fi];
                if (mt.natural[c] <= share)
                {
                    mt.widths[c] = mt.natural[c];
                    remaining -= mt.natural[c];
                    flexible.RemoveAt(fi);
                    changed = true;
                }
            }
        }
        foreach (var c in flexible)
            mt.widths[c] = remaining / flexible.Count;
    }
}
