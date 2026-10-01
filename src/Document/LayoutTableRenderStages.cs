using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Renders one layout-table row: each cell's box at its column, its paragraphs flowed inside the padding, the row height as the tallest cell, the cursor stepping down.</summary>
    private bool RenderLayoutTableRow(LayoutTableRenderState rl, Row lrow)
    {
        rl.cellCount = lrow.Cells.Count;
        if (rl.cellCount == 0) return true;
        rl.widths = new double[rl.cellCount];
        rl.declared = 0.0;
        rl.undeclared = 0;
        for (var c = 0; c < rl.cellCount; c++)
        {
            var w = lrow.Cells.At(c).Width;
            rl.widths[c] = w > 0 ? w : 0;
            if (w > 0) rl.declared += w; else rl.undeclared++;
        }
        if (rl.undeclared > 0)
        {
            var share = Math.Max(0, rl.boxW - rl.declared) / rl.undeclared;
            for (var c = 0; c < rl.cellCount; c++)
                if (rl.widths[c] <= 0) rl.widths[c] = share;
        }

        // how tall is this row? measure before placing, so a row
        // that no longer fits can move to a fresh page whole
        PlaceLayoutTableRowBoxes(rl, lrow);

        rl.rowTop = rl.y - rl.lpadTop;
        rl.rowAdvance = 0.0;
        rl.cx = rl.originLeft;
        for (var c = 0; c < rl.cellCount; c++)
        {
            if (!RenderLayoutTableCell(rl, lrow, c)) break;
        }
        rl.y -= rl.lpadTop + rl.rowAdvance + rl.lpadBottom;
        return true;
    }

    /// <summary>Renders one cell of the row: its column box and background, then its paragraphs flowed inside the padding, the cell's height kept for the row.</summary>
    private bool RenderLayoutTableCell(LayoutTableRenderState rl, Row lrow, int c)
    {
        var lcell = lrow.Cells.At(c);
        var cy = rl.rowTop;
        foreach (var lp in lcell.Paragraphs)
        {
            if (lp is Table lin)
            {
                if (!rl.measureOnly) rl.renderedTables.Add(lin);
                lin.HtmlEngineMetrics = true;
                lin.HtmlLayoutWrap = true;
                // a table that itself only places other tables keeps placing them
                if (HasNestedTables(lin))
                {
                    cy -= RenderLayoutTable(lin, rl.cx, rl.widths[c], cy, rl.flow, rl.marginLeft, rl.renderedTables, rl.measureOnly);   // nested: no paging
                    continue;
                }
                // a floated cell resolves its percentage a second
                // time: the region is half the cell, hung on its
                // right edge, and the table may overflow past it
                var region = lcell.Alignment == HorizontalAlignment.Right
                    ? rl.widths[c] / 2 : rl.widths[c];
                Converters.HtmlToPdfConverter.ApplyAutoWidths(lin, region, fill: !lin.HtmlAutoWidth);
                Converters.HtmlToPdfConverter.ApplyAutoRowHeights(lin);
                var lx = lcell.Alignment == HorizontalAlignment.Right
                    ? rl.cx + rl.widths[c] - region : rl.cx;
                lin.FlowLeftOffset = lx;
                var lcontents = lin.BuildMultiPage(rl.flow.CurrentPage, cy, rl.flow.BottomMargin,
                    measureOnly: rl.measureOnly);
                if (!rl.measureOnly)
                {
                    if (lcontents.Count > 0) rl.flow.InjectContentAtCursor(lcontents[0]);
                    if (lin.LastGraphDraws.Count > 0)
                        foreach (var gc in lin.LastGraphDraws[0])
                            rl.flow.InjectContentAtCursor(gc);
                    if (!rl.flow.HasOverflowed && lin.LastImageDraws.Count > 0)
                        foreach (var (data, rect) in lin.LastImageDraws[0])
                            rl.flow.CurrentPage.AddImage(data, rect);
                }
                cy -= lin.LastRenderedHeight;
            }
            else if (lp is Text.TextFragment ltf
                     && !string.IsNullOrWhiteSpace(ltf.Text))
            {
                var lfs = ltf.TextState.FontSize > 0 ? ltf.TextState.FontSize : 12;
                var lface = ltf.TextState.IsBold ? "Helvetica-Bold" : "Helvetica";
                if (!rl.measureOnly)
                {
                    var lres = Table.RegisterFont(rl.flow.CurrentPage, lface);
                    var lb = new Content.ContentStreamBuilder();
                    lb.SaveState();
                    lb.BeginText().SetFont(lres, lfs)
                      .MoveTextPosition(rl.cx, cy - lfs)
                      .ShowText(ltf.Text!).EndText();
                    lb.RestoreState();
                    rl.flow.InjectContentAtCursor(lb.Build());
                }
                cy -= Converters.HtmlToPdfConverter.FaceLineHeight(lface, lfs);
            }
            else if (lp is Text.TextFragment lws)
                // A blank (or &nbsp;-only) cell is still a line box:
                // it takes its own font's line height, not a nominal one.
                cy -= Converters.HtmlToPdfConverter.FaceLineHeight("Helvetica",
                    lws.TextState.FontSize > 0 ? lws.TextState.FontSize : 12);
        }
        rl.rowAdvance = Math.Max(rl.rowAdvance, rl.rowTop - cy);
        rl.cx += rl.widths[c];
        return true;
    }

    /// <summary>Under a real render (not a measure), draws the row's cell boxes: the shared border and each cell's fill at its column and the row height.</summary>
    private void PlaceLayoutTableRowBoxes(LayoutTableRenderState rl, Row lrow)
    {
        if (rl.originX < 0 && !rl.measureOnly)
        {
            var need = 0.0;
            for (var c = 0; c < rl.cellCount; c++)
            {
                var mcell = lrow.Cells.At(c);
                var mh = 0.0;
                foreach (var mp in mcell.Paragraphs)
                {
                    if (mp is Table mt)
                    {
                        // a cell holding a table of tables is as tall as
                        // placing it would make it — measure it the same way
                        if (HasNestedTables(mt))
                        {
                            mh += RenderLayoutTable(mt, rl.originLeft, rl.widths[c], rl.y, rl.flow, rl.marginLeft, rl.renderedTables, measureOnly: true);
                            continue;
                        }
                        mt.FlowLeftOffset = rl.originLeft;
                        mt.BuildMultiPage(rl.flow.CurrentPage, rl.y, rl.flow.BottomMargin, measureOnly: true);
                        mh += mt.LastRenderedHeight;
                    }
                    else if (mp is Text.TextFragment mtf)
                        mh += Converters.HtmlToPdfConverter.FaceLineHeight("Helvetica",
                            mtf.TextState.FontSize > 0 ? mtf.TextState.FontSize : 12);
                }
                need = Math.Max(need, mh);
            }
            if (need > 0) need += rl.lpadTop + rl.lpadBottom;
            if (need > 0 && rl.y - need < rl.flow.BottomMargin
                && need <= rl.flow.ContentTop - rl.flow.BottomMargin)
            {
                rl.flow.ForceNewPage();
                rl.y = rl.flow.CurrentY;
            }
        }
    }
}
