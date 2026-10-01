using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>Lay a graph-bearing cell out into left-to-right inline rows, wrapping at
    /// the cell's content width. Each row is positioned <see cref="InlineItem"/>s (a text
    /// run or a Graph) with x-offsets from the cell content-left; the render pass draws the
    /// text and blits each graph's content stream at the resolved position.</summary>
    private (List<List<InlineItem>> result, double lineHeight) BuildInlineCellLayout(Cell cell, double availWidth, double defaultFontSize, Aspose.Pdf.Text.TextState? cellTextState, HorizontalAlignment cellAlign)
    {
        double lineHeight = default;
        var il = new InlineCellLayoutState();
        il.rows = new List<List<InlineItem>>();
        il.current = new List<InlineItem>();
        il.x = 0;
        il.generatorPitch = GeneratorCellModel;
        il.maxH = il.generatorPitch ? 0 : defaultFontSize * 1.2;
        il.contentW = availWidth > 0 ? availWidth : double.MaxValue;
        il.faceCache = new Dictionary<string, (byte[]? ttf, string? name)>();

        il.lineHasText = false;
        il.rowItemSources = 0;
        il.rowRightCount = 0;

        foreach (var para in cell.Paragraphs)
        {
            if (!LayoutInlineCellParagraph(il, para, defaultFontSize, cellTextState, cellAlign)) break;
        }
        FlushInlineRow(il, cellAlign);
        if (il.rows.Count == 0) il.rows.Add(new List<InlineItem>());
        lineHeight = il.maxH > 0 ? il.maxH : defaultFontSize;
        return (il.rows, lineHeight);
    }
}
