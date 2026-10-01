using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
// The stages of the tag tree build: the element emit, the paragraph flush and one block.
    // Each leaf holds a marked-content reference to the page content it stands for; a
    // heading and a body run are an H/P with one MCR, an image is a Figure holding the MCR.
    // Every MCR is registered as a slot the marking pass fills with the content's MCIDs.

    private static LogicalStructure.MCRElement NewSlot(TagTreeBuildState tg, Slot slot)
    {
        var mcr = new LogicalStructure.MCRElement();
        slot.El = mcr;
        tg.run.Slots.Add(slot);
        return mcr;
    }

    private static LogicalStructure.StructureElement FigureEl(TagTreeBuildState tg, int blockId, int figOp)
    {
        var fig = tg.tc.CreateFigureElement();
        // The figure states where it stands (Layout /BBox, PDF 32000-1 table 345): a reader that
        // cannot use its content - a drawing of paths - can still show the page there.
        if (tg.run.PageOfBlock.TryGetValue(blockId, out var pw) && pw.Figures.Any(f => f.op == figOp))
        {
            var f = pw.Figures.First(x => x.op == figOp);
            var box = new LogicalStructure.StructureAttribute(LogicalStructure.AttributeKey.BBox);
            box.SetArrayNumberValue([Math.Round(f.x, 2), Math.Round(f.y, 2), Math.Round(f.x + f.w, 2), Math.Round(f.y + f.h, 2)]);
            fig.Attributes.GetAttributes(LogicalStructure.AttributeOwnerStandard.Layout).SetAttribute(box);
        }
        fig.AppendChild(NewSlot(tg, new Slot { Kind = SlotKind.Figure, BlockId = blockId, Fig = figOp, Tag = "Figure" }));
        return fig;
    }

    // A heading carries its text (MCR) plus any inline heading-icon figures absorbed
    // into it, in reading order: an icon left of the heading text stands before the
    // MCR, one to its right after it (probed: "## ![icon] *Title*" tags [Figure, MCR],
    // "## *Title* ![icon]" tags [MCR, Figure]).
    private static LogicalStructure.StructureElement MakeHeaderEl(TagTreeBuildState tg, Block b)
    {
        var h = tg.tc.CreateHeaderElement(b.Level);
        StateLayout(h, b.Layout);
        if (b.RuleGapBelow is { } gap) StateRuleBelow(h, gap, b.RuleBox, b.RuleThickness);
        StateRuleAbove(h, b);
        var icons = b.HeaderFigs ?? new List<(double X, int Fig)>();
        foreach (var icon in icons.Where(i => i.X < b.TextMinX).OrderBy(i => i.X))
            h.AppendChild(FigureEl(tg, b.Id, icon.Fig));
        h.AppendChild(NewSlot(tg, new Slot { Kind = SlotKind.Text, BlockId = b.Id, Tag = $"H{b.Level}" }));
        foreach (var icon in icons.Where(i => i.X >= b.TextMinX).OrderBy(i => i.X))
            h.AppendChild(FigureEl(tg, b.Id, icon.Fig));
        return h;
    }

    private static LogicalStructure.StructureElement MakeBody(TagTreeBuildState tg, Block b)
    {
        LogicalStructure.StructureElement p = b.Note ? tg.tc.CreateNoteElement() : b.Formula ? tg.tc.CreateFormulaElement()
            : b.Toc ? tg.tc.CreateTOCIElement() : tg.tc.CreateParagraphElement();
        StateLayout(p, b.Layout);
        if (b.RuleGapBelow is { } gap) StateRuleBelow(p, gap, b.RuleBox, b.RuleThickness);
        StateRuleAbove(p, b);
        if (b.Formula && b.Lines is { Count: > 0 } formulaLines)
        {
            // Where the formula stands (Layout /BBox): a reader can render the page there.
            var box = new LogicalStructure.StructureAttribute(LogicalStructure.AttributeKey.BBox);
            box.SetArrayNumberValue(LinesBox(formulaLines).Select(v => (double?)v).ToArray());
            p.Attributes.GetAttributes(LogicalStructure.AttributeOwnerStandard.Layout).SetAttribute(box);
        }
        var tag = b.Note ? "Note" : b.Formula ? "Formula" : b.Toc ? "TOCI" : "P";
        LogicalStructure.MCRElement Segment(int s) =>
            NewSlot(tg, new Slot { Kind = SlotKind.Text, BlockId = b.Id, Segment = s, Tag = tag });
        if (b.Links is { Count: > 0 } links)
        {
            // Interleave content runs with inline links: a leading MCR, then each link
            // (Link → OBJR per annotation + MCR) followed by another content MCR. A link
            // wrapped over lines is one Link holding each of its annotations.
            p.AppendChild(Segment(0));
            var groups = b.LinkGroups ?? Enumerable.Range(0, links.Count).ToList();
            for (var g = 0; g <= groups[^1]; g++)
            {
                p.AppendChild(MakeLink(tg, b, Enumerable.Range(0, links.Count).Where(k => groups[k] == g), g));
                p.AppendChild(Segment(g + 1));
            }
            // In-line images of a paragraph with links stand after its text.
            foreach (var fig in b.InlineFigs ?? new List<int>())
                p.AppendChild(FigureEl(tg, b.Id, fig));
            AppendJoined(tg, p, b);
            return p;
        }
        // An in-line image splits the paragraph's content around itself:
        // [MCR, Figure, MCR] (probed: "text ![img] text" in one source paragraph).
        p.AppendChild(Segment(0));
        var inline = b.InlineFigs ?? new List<int>();
        for (var i = 0; i < inline.Count; i++)
        {
            p.AppendChild(FigureEl(tg, b.Id, inline[i]));
            p.AppendChild(Segment(i + 1));
        }
        AppendJoined(tg, p, b);
        return p;
    }

    /// <summary>The rest of a paragraph a page break cut: its content on the following page(s).</summary>
    private static void AppendJoined(TagTreeBuildState tg, LogicalStructure.StructureElement p, Block b)
    {
        foreach (var joined in b.Joined ?? new List<int>())
            p.AppendChild(NewSlot(tg, new Slot { Kind = SlotKind.Text, BlockId = joined, Tag = "P" }));
    }

    private static LogicalStructure.StructureElement MakeFigure(TagTreeBuildState tg, Block b)
    {
        // One rendered row of images = one paragraph holding every figure of the row.
        var p = tg.tc.CreateParagraphElement();
        StateLayout(p, b.Layout);
        foreach (var fig in b.Figs ?? new List<int>())
            p.AppendChild(FigureEl(tg, b.Id, fig));
        return p;
    }

    private static LogicalStructure.StructureElement MakeTable(TagTreeBuildState tg, Block b)
    {
        var table = tg.tc.CreateTableElement();
        StateLayout(table, b.Layout);
        // The table states where it stands on its first page (Layout /BBox, PDF 32000-1 table 345): its edges.
        if (tg.run.PageOfBlock.TryGetValue(b.Id, out var pw) && b.TableIndex < pw.Tables.Count)
        {
            var r = pw.Tables[b.TableIndex].region;
            var box = new LogicalStructure.StructureAttribute(LogicalStructure.AttributeKey.BBox);
            box.SetArrayNumberValue([Math.Round(r.LLX, 2), Math.Round(r.LLY, 2), Math.Round(r.URX, 2), Math.Round(r.URY, 2)]);
            table.Attributes.GetAttributes(LogicalStructure.AttributeOwnerStandard.Layout).SetAttribute(box);
        }
        AddTableRows(tg, table, b, continued: false);
        // The parts of the table the following pages go on with.
        foreach (var joined in b.Joined ?? new List<int>())
            if (tg.run.PageOfBlock.TryGetValue(joined, out var next) && next.Blocks.FirstOrDefault(x => x.Id == joined) is { Kind: BlockKind.Table } part)
                AddTableRows(tg, table, part, continued: true);
        return table;
    }

    /// <summary>The rows of one page's part of a table: a TR per grid row holding a cell per
    /// table cell that starts in it (a merged cell states its /RowSpan and /ColSpan). A part
    /// that goes on with a table from the page before takes no header rows of its own; the
    /// header rows it repeats are no content (they are marked as artifacts).</summary>
    /// <summary>The rules a table's cells may be ruled by: the page's, less those on another table's region - outside this
    /// one's, or on its edge when no rule of its own touches it: the bottom rule of a table right over a table read from
    /// its text is that table's edge, not this one's top. (Two parts of one ruled table share the rule between them.)</summary>
    private static List<PageContentScan.Rule> OwnRules(PageWork pw, int index)
    {
        var own = pw.Tables[index].region;
        bool On(Aspose.Pdf.Rectangle region, PageContentScan.Rule rule)
        {
            var (low, high, from, to) = rule.Horizontal ? (region.LLY, region.URY, region.LLX, region.URX) : (region.LLX, region.URX, region.LLY, region.URY);
            return rule.At >= low - GridLineTolerance && rule.At <= high + GridLineTolerance && Math.Min(rule.To, to) - Math.Max(rule.From, from) > 0;
        }
        bool Others(PageContentScan.Rule rule) => pw.Tables.Where((_, k) => k != index).Any(t => On(t.region, rule));
        var mine = pw.Rules.Where(rule => On(own, rule)).ToList();
        var sharedOnly = mine.Count > 0 && mine.All(Others);
        return pw.Rules.Where(rule => !Others(rule) || On(own, rule) && !sharedOnly).ToList();
    }

    private static void AddTableRows(TagTreeBuildState tg, LogicalStructure.StructureElement table, Block b, bool continued)
    {
        var headers = tg.run.PageOfBlock.TryGetValue(b.Id, out var pw) && b.TableIndex < pw.TableHeaders.Count
            ? pw.TableHeaders[b.TableIndex] : (Rows: 0, Col: false);
        var cells = pw is not null && b.TableIndex < pw.TableCells.Count ? pw.TableCells[b.TableIndex] : null;
        var colX = pw is not null && b.TableIndex < pw.Tables.Count ? pw.Tables[b.TableIndex].colX : null;
        var rowY = pw is not null && b.TableIndex < pw.Tables.Count ? pw.Tables[b.TableIndex].rowY : null;
        var first = continued && pw is not null && pw.RepeatedHeaders.TryGetValue(b.Id, out var repeated) ? repeated : 0;
        var edgeRules = pw is not null && colX is not null && rowY is not null ? SnapRules(OwnRules(pw, b.TableIndex), colX, rowY) : null;
        var (flushRight, figureEnds) = pw is not null && colX is not null && rowY is not null && rowY.Count == b.Rows + 1
            ? FlushRightColumns(pw.Lines, cells, colX, rowY, b.Rows, headers.Rows) : (null, null);
        var widthEdges = flushRight is not null ? WidthEdges(colX!, flushRight, figureEnds!, edgeRules) : colX;
        var centredColumns = flushRight is not null ? CentredColumns(pw!.Lines, cells, colX!, rowY!, b.Rows) : null;
        var columnStarts = flushRight is not null ? ColumnStarts(pw!.Lines, cells, colX!, rowY!, b.Rows, headers.Rows) : null;
        var placements = edgeRules is not null && rowY!.Count == b.Rows + 1 && RowsAreRuled(edgeRules, rowY)
            ? CellPlacements(pw!.Lines, cells, colX!, rowY, b.Rows, b.Cols) : null;
        for (var r = first; r < Math.Max(1, b.Rows); r++)
        {
            var tr = tg.tc.CreateTableTRElement();
            var rowSlots = new List<Slot>();
            for (var c = 0; c < Math.Max(1, b.Cols); c++)
            {
                if (cells is not null && r < cells.GetLength(0) && c < cells.GetLength(1) && cells[r, c] != (r, c)) continue;
                var (rowSpan, colSpan) = cells is not null && r < cells.GetLength(0) && c < cells.GetLength(1)
                    ? CellSpan(cells, r, c) : (1, 1);
                // A header row's cells head their columns, a header column's cells their rows.
                var scope = r < headers.Rows && !continued ? "Column" : headers.Col && c == 0 ? "Row" : null;
                var cell = scope is null ? tg.tc.CreateTableTDElement() : HeaderCell(tg, scope);
                var slot = new Slot { Kind = SlotKind.Cell, BlockId = b.Id, Row = r, Col = c, Tag = scope is null ? "TD" : "TH" };
                rowSlots.Add(slot);
                cell.AppendChild(NewSlot(tg, slot));
                cell.SetTableSpan(LogicalStructure.AttributeKey.RowSpan, rowSpan);
                cell.SetTableSpan(LogicalStructure.AttributeKey.ColSpan, colSpan);
                if (widthEdges is not null && c + colSpan < widthEdges.Count)
                    StateWidth(cell, widthEdges[c + colSpan] - widthEdges[c]);
                // The row edges run bottom up: row r spans rowY[rows - 1 - r] .. rowY[rows - r].
                if (rowY is not null && b.Rows - r < rowY.Count && b.Rows - r - rowSpan >= 0)
                    StateHeight(cell, rowY[b.Rows - r] - rowY[b.Rows - r - rowSpan]);
                if (edgeRules is not null && c + colSpan < colX!.Count && b.Rows - r < rowY!.Count && b.Rows - r - rowSpan >= 0)
                    StateBorders(cell, edgeRules, colX[c], colX[c + colSpan], rowY[b.Rows - r], rowY[b.Rows - r - rowSpan]);
                if (pw is not null && colX is not null && rowY is not null && c + colSpan < colX.Count && b.Rows - r < rowY.Count && b.Rows - r - rowSpan >= 0)
                    StateBackground(cell, pw, new Aspose.Pdf.Rectangle(colX[c], rowY[b.Rows - r - rowSpan], colX[c + colSpan], rowY[b.Rows - r]),
                        pw.Tables[b.TableIndex].region);
                if (flushRight is not null && c + colSpan < colX!.Count && b.Rows - r - rowSpan >= 0)
                {
                    var align = CellAlign(pw!.Lines, flushRight, c, colSpan, colX[c], colX[c + colSpan], rowY![b.Rows - r - rowSpan], rowY[b.Rows - r],
                        scope == "Column" ? pw.Rules : null, (colX[0], colX[^1]), centredColumns);
                    StateTextAlign(cell, align);
                    if (align == LogicalStructure.AttributeName.TextAlign_End && colSpan == 1 && r >= headers.Rows && flushRight[c])
                        StateEndIndent(cell, widthEdges![c + 1] - (TextIn(pw.Lines, colX[c], colX[c + 1], rowY![b.Rows - r - rowSpan], rowY[b.Rows - r])?.R ?? figureEnds![c]));
                }
                if (placements is not null && placements.TryGetValue((r, c), out var placement)) StatePlacement(cell, placement);
                // A label set flush left in its cell states how far in it starts; a cell set any other way states nothing.
                if (columnStarts is not null && colSpan == 1 && r >= headers.Rows && c + 1 < colX!.Count && b.Rows - r - rowSpan >= 0
                    && cell.Attributes.GetAttributes(LogicalStructure.AttributeOwnerStandard.Layout).GetAttribute(LogicalStructure.AttributeKey.TextAlign) is null)
                    StateCellIndent(cell, pw!.Lines, columnStarts[c], colX[c], colX[c + 1], rowY![b.Rows - r - rowSpan], rowY[b.Rows - r]);
                tr.AppendChild(cell);
            }
            table.AppendChild(tr);
            if (continued) tg.run.ContinuedRows.Add((tr, rowSlots));
        }
    }

    /// <summary>A Link element: an object reference per annotation of its group (a link wrapped over lines is several
    /// annotations), then the slot its text goes to.</summary>
    private static LogicalStructure.StructureElement MakeLink(TagTreeBuildState tg, Block b, IEnumerable<int> members, int group)
    {
        var link = tg.tc.CreateLinkElement();
        foreach (var k in members)
        {
            var objr = new LogicalStructure.OBJRElement();
            objr.SetObj(b.Links![k]);
            link.AppendChild(objr);
            tg.run.LinkSlots.Add(new LinkSlot { Objr = objr, Link = link, Annot = b.Links[k], BlockId = b.Id });
        }
        link.AppendChild(NewSlot(tg, new Slot { Kind = SlotKind.Link, BlockId = b.Id, Link = group, Tag = "Link" }));
        return link;
    }

    // A list is L → LI per item → Lbl (the bullet or number) + LBody (the item's text, split around its links as a
    // paragraph's is).
    private static LogicalStructure.StructureElement MakeList(TagTreeBuildState tg, Block b)
    {
        var list = tg.tc.CreateListElement();
        StateLayout(list, b.Layout);
        for (var i = 0; i < (b.Items?.Count ?? 0); i++)
        {
            var li = tg.tc.CreateListLIElement();
            var lbl = tg.tc.CreateListLblElement();
            lbl.AppendChild(NewSlot(tg, new Slot { Kind = SlotKind.Label, BlockId = b.Id, Row = i, Tag = "Lbl" }));
            li.AppendChild(lbl);
            var body = tg.tc.CreateListLBodyElement();
            LogicalStructure.MCRElement Segment(int s) => NewSlot(tg, new Slot { Kind = SlotKind.Body, BlockId = b.Id, Row = i, Segment = s, Tag = "LBody" });
            body.AppendChild(Segment(0));
            var groups = ItemLinks(b, i).Select(k => b.LinkGroups![k]).Distinct().OrderBy(g => g).ToList();
            for (var s = 0; s < groups.Count; s++)
            {
                body.AppendChild(MakeLink(tg, b, ItemLinks(b, i).Where(k => b.LinkGroups![k] == groups[s]), groups[s]));
                body.AppendChild(Segment(s + 1));
            }
            li.AppendChild(body);
            list.AppendChild(li);
        }
        return list;
    }

    private static LogicalStructure.StructureElement MakeContent(TagTreeBuildState tg, Block b) => b.Kind switch
    {
        BlockKind.List => MakeList(tg, b),
        BlockKind.Figure => MakeFigure(tg, b),
        BlockKind.Table => MakeTable(tg, b),
        BlockKind.Heading => MakeHeaderEl(tg, b),
        _ => MakeBody(tg, b),
    };
}
