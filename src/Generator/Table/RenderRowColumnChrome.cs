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
    /// <summary>The chrome stage of a column render: the generator-cell finish, borders and the cursor advance, verbatim.</summary>
    private void RenderRowColumnChrome(RowColumnState rc, int col, ref double cellX, ContentStreamBuilder builder, RowSlice slice,
        double[] colWidths, string fontName, int[] cellMap,
        List<(Rectangle rect, Hyperlink link)>? links, List<(byte[] data, Rectangle rect)>? imageSink,
        List<(Aspose.Pdf.Forms.RadioButtonOptionField opt, Rectangle rect)>? optionSink, List<byte[]>? graphSink,
        List<(Aspose.Pdf.Forms.CheckboxField cbf, Rectangle rect)>? checkboxSink, Page? page,
        List<(Note note, double x, double baseline, double size)>? footnoteSink)
    {
        if (rc.generatorCell)
        {
            // Mixed-size HTML-engine cell: the single-size clip model reads the
            // smaller-size lines as sub/superscript satellites of the biggest one
            // and crops them away — bound such a cell by every line's own box.
            var clipMixedEngine = false;
            double clipEngSz = -1;
            for (var li = 0; rc.cellLines is not null && li < rc.cellLines.Count && !clipMixedEngine; li++)
            {
                var l = rc.cellLines[li];
                if (!l.HtmlEngine || l.FontSize <= 0) continue;
                if (clipEngSz < 0) clipEngSz = l.FontSize;
                else if (Math.Abs(l.FontSize - clipEngSz) > 0.5) clipMixedEngine = true;
            }
            EmitCellTextClip(builder, rc.clipMark, cellX + rc.borderInsetLeft + rc.clipPadL, rc.cellWidth - _columnPitch - rc.clipPadL - rc.clipPadR, rc.cellDescentEm, rc.cellFace, clipMixedEngine);
        }

        // Image content — recorded once, at the row's top slice, for the caller to blit
        // onto the materialised page (overflow pages don't exist yet during the build).
        // The image is collected as a page-space rect; the cell border drawn above into
        // builder frames it once both content streams land on the page.
        if (imageSink is not null
            && slice.Plan.CellImages is { } imgs && imgs.TryGetValue(col, out var colImgs))
            foreach (var ci in colImgs)
            {
                // Each image belongs to the slice holding ITS line, and is seated from
                // THAT slice's top — an absolute line offset would push a continuation
                // slice's image off the bottom of the page it was never drawn on.
                // Identity on an unsplit row: LineStart is 0 and every line is in range.
                if (ci.LineOffset < slice.LineStart
                    || ci.LineOffset >= slice.LineStart + slice.LineCount) continue;
                var rel = ci.LineOffset - slice.LineStart;
                var padRight = rc.padding?.Right ?? rc.dp;
                var imgX = cellX + rc.padLeft + ci.XOffset;
                if (ci.Align == HorizontalAlignment.Center)
                    imgX = cellX + Math.Max(0, (rc.cellWidth - ci.Width) / 2);
                else if (ci.Align == HorizontalAlignment.Right)
                    imgX = cellX + Math.Max(0, rc.cellWidth - padRight - ci.Width);
                // Cell VerticalAlignment centres/bottoms the image within the row's
                // content band (the row is usually taller than the image because its
                // height is reserved in whole text lines). With SEVERAL images stacked
                // in one cell there is no single band to centre in, so they sit where
                // their lines put them.
                var imgVaOffset = 0.0;
                if (colImgs.Count == 1 && rc.effVA is VerticalAlignment.Center or VerticalAlignment.Bottom)
                {
                    var availH = slice.Height - rc.padTop - rc.padBot - rel * slice.Plan.LineHeight;
                    if (availH > ci.Height)
                        imgVaOffset = rc.effVA == VerticalAlignment.Center
                            ? (availH - ci.Height) / 2
                            : availH - ci.Height;
                }
                // Seat the image below any text lines that precede it in the cell (e.g. a
                // title line above a centred logo) rather than at the cell top.
                var imgTopY = slice.TopY - rc.padTop - rel * slice.Plan.LineHeight - imgVaOffset
                    // Generator dialect: the image sits inside the cell's top rule.
                    - (rc.generatorCell ? rc.borderInsetTop : 0);
                imageSink.Add((ci.Data, new Rectangle(imgX, imgTopY - ci.Height, imgX + ci.Width, imgTopY)));
            }

        // Nested tables — each inner grid renders in place on the slice holding its
        // reserved lines (the cell-image window precedent), as its own page-space
        // content stream via graphSink so the outer table's stream stays untouched.
        if (!_measureOnly && _buildPage is not null
            && slice.Plan.CellTables is { } ctabs && ctabs.TryGetValue(col, out var colTabs))
            foreach (var ct in colTabs)
            {
                // The reserve may span several slices (the host row splits at its
                // reserve-line boundaries); this slice participates when their
                // line windows overlap.
                if (ct.LineOffset >= slice.LineStart + slice.LineCount
                    || ct.LineOffset + ct.LineCount <= slice.LineStart) continue;
                var innerT = ct.Table;
                if (ct.Slices is null)
                {
                    // First covering slice: build the grid ONCE against the same
                    // page bounds as this host build, so it breaks where the page
                    // really ends and its continuation slices are positioned for
                    // the same fresh-page top the host's own continuation uses.
                    var ctRel = ct.LineOffset - slice.LineStart;
                    // Lines preceding the reserve advance by the same ruler the
                    // draw walk uses: an earlier grid's reserve lines at their
                    // own FontSize, everything else at the uniform pitch.
                    var ctLead = 0.0;
                    for (var pli = slice.LineStart;
                         pli < ct.LineOffset && rc.cellLines is not null && pli < rc.cellLines.Count; pli++)
                        ctLead += rc.cellLines[pli].ImgReserve && rc.cellLines[pli].FontSize > 0
                            ? rc.cellLines[pli].FontSize
                            // A generator cell holding a grid is an EXACT stack, and
                            // its text is drawn by the per-line walk -- measuring the
                            // lead on the row's uniform pitch instead put the grid
                            // above the heading it belongs under.
                            : rc.generatorCell
                                ? (rc.cellLines[pli].BoxH > 0
                                    ? rc.cellLines[pli].BoxH
                                    : rc.cellLines[pli].FontSize + rc.cellLines[pli].Leading)
                            : slice.Plan.LineHeight;
                    var tabTopY = slice.TopY - rc.padTop - ctLead
                        // Generator dialect: the grid starts inside the host's border.
                        - (rc.generatorCell ? rc.borderInsetTop : 0);
                    // vertical-align: middle — a nested grid shorter than its row
                    // centres in the cell band (the cell-image precedent). Only
                    // when the reserve is the cell's LAST content: interleaved
                    // lines after the grid occupy the rest of the slice, so the
                    // band available to the grid is its own reserve window.
                    if (rc.effVA == VerticalAlignment.Center && colTabs.Count == 1
                        && (rc.cellLines is null || ct.LineOffset + ct.LineCount >= rc.cellLines.Count))
                    {
                        var ctAvail = slice.Height - rc.padTop - rc.padBot - ctRel * slice.Plan.LineHeight;
                        if (ctAvail > ct.HeightPt) tabTopY -= (ctAvail - ct.HeightPt) / 2;
                    }
                    // The reserved box holds the capsule's wrapper; the grid itself
                    // starts one outset inside it on both axes.
                    tabTopY -= innerT.HtmlCapsuleOutsetVPt + innerT.HtmlMarginTopPt;
                    innerT.FlowLeftOffset = cellX + rc.padLeft + innerT.HtmlCapsuleOutsetHPt
                        + innerT.HtmlListIndentPt
                        // DataWorks results grid: the draw pen sits past the
                        // reference's full widget footprints while the width
                        // model keeps the smaller reserves (see DwNestedDrawShiftPt).
                        + (DwFormCells && innerT.HtmlDwGapReservePt > 0
                            ? Converters.HtmlToPdfConverter.DwNestedDrawShiftPt : 0);
                    // Generator dialect: a grid inside a FIXED-height host row is bounded
                    // by the host cell's inner bottom — rows that do not fit there go
                    // to a continuation slice nobody consumes (a 35.1 pt
                    // second logo row never draws inside the 27 pt host row).
                    var gridBottom = rc.generatorCell && rc.row.FixedRowHeight > 0
                        ? Math.Max(_curPageBottom, slice.TopY - slice.Height
                            + (rc.pitchBorder is not null ? SideInsets(rc.pitchBorder, half: false).b : 0))
                        : _curPageBottom;
                    try
                    {
                        ct.Slices = innerT.BuildMultiPage(_buildPage, tabTopY,
                            gridBottom, _curFreshTopMargin);
                    }
                    catch { ct.Slices = new List<byte[]>(); }
                    ct.Consumed = 0;
                }
                if (ct.Slices.Count > ct.Consumed)
                {
                    // Splice the grid's next page slice into THIS stream, right
                    // where its cell draws, instead of appending it to the page as
                    // a separate stream: the page is then laid out in document
                    // order for anything that reads the operators back (a text
                    // absorber walks streams in order, and a whole grid arriving
                    // early shifts every fragment index after it).
                    var s = ct.Consumed++;
                    builder.AppendStream(ct.Slices[s]);
                    if (graphSink is not null && innerT.LastGraphDraws.Count > s)
                        foreach (var ig in innerT.LastGraphDraws[s]) graphSink.Add(ig);
                    if (imageSink is not null && innerT.LastImageDraws.Count > s)
                        foreach (var im in innerT.LastImageDraws[s]) imageSink.Add(im);
                }
            }

        // Inline graph/text content (legend swatches, bar graphs): drawn once at the
        // cell top on the first slice. Text is shown in the table stream; each graph
        // is emitted as its own page-space content stream via graphSink.
        if (slice.LineStart == 0 &&
            slice.Plan.CellInline is { } inlineMap && inlineMap.TryGetValue(col, out var inlineRows))
        {
            var inlineMark = builder.Mark;
            builder.ResetTextExtent();
            // Generator dialect: the block starts inside the border like any other
            // cell text, and seats one face descent above the full-em drop.
            // …only for a face the fragment names itself (or the default Helvetica): a
            // multi-segment line drawn in the TABLE default face keeps the full-em drop
            // (probed by the absorber positions of an Arial-default sub/superscript cell:
            // sub at top − fs − 0.245 fs, main at top − fs).
            var inlineCellFromGraphOnly = CellInlineFromGraphOnly(rc.cell);
            var inlineLift = rc.generatorCell && (rc.cellFragmentFace || inlineCellFromGraphOnly)
                ? rc.cellDescentEm : 0;
            var inlineStack = 0.0;
            for (var ri = 0; ri < inlineRows.Count; ri++)
            {
                // Generator cells honour the cell's vertical alignment for
                // inline rows too (a two-line cell centres in its 102 pt row).
                var lineTop = slice.TopY - rc.padTop - (rc.generatorCell ? rc.borderInsetTop + rc.vaOffset : 0)
                    - (rc.generatorCell ? inlineStack : ri * slice.Plan.LineHeight);
                if (rc.generatorCell) inlineStack += InlineRowHeight(inlineRows[ri], slice.Plan.LineHeight);
                foreach (var item in inlineRows[ri])
                {
                    var ix = cellX + rc.padLeft + item.X;
                    if (item.ImageData is { } inlineImgData)
                        imageSink?.Add((inlineImgData,
                            new Rectangle(ix, lineTop - item.Height, ix + item.Width, lineTop)));
                    else if (item.Graph is { } g)
                        // The graph's local origin is its box's BOTTOM-left corner,
                        // which sits its own declared height below the line top —
                        // NOT one text line below it.
                        graphSink?.Add(g.Build(null, ix, lineTop - item.Height));
                    else if (item.Empty)
                    {
                        // Emit an explicit empty run so the generator's leading empty
                        // segment round-trips as an (empty) text fragment.
                        builder.BeginText();
                        builder.SetFont(fontName, item.FontSize);
                        builder.MoveTextPosition(ix, lineTop - item.FontSize);
                        builder.ShowText("");
                        builder.EndText();
                    }
                    else if (item.Text is { Length: > 0 } t)
                    {
                        // Seat the run on the line baseline (the pre-shrink size), then apply any
                        // sub/superscript shift — so a reduced-size super/subscript glyph still
                        // hangs off the common baseline rather than its own smaller box.
                        var baseSize = item.BaseFontSize > 0 ? item.BaseFontSize : item.FontSize;
                        // Generator dialect: the line's baseline is the fragment size's
                        // face-descent seat, and a smaller segment bottom-aligns on its
                        // own descent (probed: a 13 pt run beside 15 pt Calibri seats
                        // 0.5 pt lower); sub/superscripts shift off the segment's seat.
                        var lineFs = rc.generatorCell && item.LineFontSize > 0 ? item.LineFontSize : baseSize;
                        var seatY = lineTop - lineFs * (1 - inlineLift) - (lineFs - baseSize) * inlineLift
                            + item.BaselineShift;
                        // Per-run embedded font (e.g. NotoSans / NotoSansArabic): embed it as a
                        // Type0/CID font and emit the run as hex glyph IDs.
                        if (item.Ttf is not null && page is not null)
                        {
                            var fontDict = ResolvePageFontDict(page);
                            var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                                fontDict, item.Ttf, item.FontName ?? "Font", t, stripSpacesInBaseFont: true);
                            builder.BeginText();
                            builder.SetFont(resName, item.FontSize);
                            ApplyColor(builder, item.Color);
                            builder.MoveTextPosition(ix, seatY);
                            builder.ShowTextHex(hex);
                            builder.EndText();
                            continue;
                        }
                        // The run draws in ITS OWN face: a segment that asked for
                        // bold/italic gets the matching Standard-14 face, not the
                        // table's default one.
                        var itemFont = fontName;
                        if ((item.Bold || item.Italic) && page is not null)
                            itemFont = RegisterFont(page, item.Bold && item.Italic
                                ? "Helvetica-BoldOblique"
                                : item.Bold ? "Helvetica-Bold" : "Helvetica-Oblique");
                        builder.BeginText();
                        builder.SetFont(itemFont, item.FontSize);
                        ApplyColor(builder, item.Color);
                        builder.MoveTextPosition(ix, seatY);
                        builder.ShowText(t);
                        builder.EndText();
                        // …and its own underline, one rule under the run at the
                        // link-underline seat.
                        if (item.Underline && item.Width > 0)
                        {
                            builder.SaveState();
                            if (item.Color is { } uc)
                                builder.SetStrokeColor(uc.R / 255.0, uc.G / 255.0, uc.B / 255.0);
                            builder.SetLineWidth(LinkUnderlineWPt * item.FontSize / LinkProbeBasePt);
                            var uy = seatY - LinkUnderlineDropPt * item.FontSize / LinkProbeBasePt;
                            builder.MoveTo(ix, uy).LineTo(ix + item.Width, uy).Stroke();
                            builder.RestoreState();
                        }
                    }
                }
            }
            if (rc.generatorCell)
                EmitCellTextClip(builder, inlineMark, cellX + rc.borderInsetLeft + rc.clipPadL, rc.cellWidth - _columnPitch - rc.clipPadL - rc.clipPadR, rc.cellDescentEm, rc.cellFace);
        }
        cellX += rc.cellWidth;
    }
}
