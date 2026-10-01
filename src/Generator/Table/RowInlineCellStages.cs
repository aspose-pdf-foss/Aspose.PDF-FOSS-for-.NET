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
// Inline cell layout helpers: the row pitch, alignment notes, the row flush and a fragment's alignment.
    private static double RowH(InlineCellLayoutState il, double fs) => il.generatorPitch ? fs : fs * 1.2;

    private static void NoteAlign(InlineCellLayoutState il, HorizontalAlignment a)
    {
        il.rowItemSources++;
        if (a == HorizontalAlignment.Right) il.rowRightCount++;
    }

    private static void FlushInlineRow(InlineCellLayoutState il, HorizontalAlignment cellAlign)
    {
        if (il.current.Count > 0)
        {
            // Right-aligned inline row: paragraph order packs
            // from the RIGHT content edge (first paragraph rightmost), so an
            // image + joined right-aligned text renders [text][image] against
            // the cell's right padding edge.
            if (il.rowItemSources > 0 && il.rowRightCount == il.rowItemSources && il.contentW < double.MaxValue)
            {
                var xr = il.contentW;
                foreach (var it in il.current) { it.X = xr - it.Width; xr -= it.Width; }
            }
            // …otherwise the CELL's own alignment places the row: a centred cell
            // centres its inline line the way it centres a plain wrapped one.
            else if (il.contentW < double.MaxValue
                     && cellAlign is HorizontalAlignment.Center or HorizontalAlignment.Right)
            {
                double rowW = 0;
                foreach (var it in il.current) rowW = Math.Max(rowW, it.X + it.Width);
                var slack = il.contentW - rowW;
                if (slack > 0)
                {
                    var shift = cellAlign == HorizontalAlignment.Center ? slack / 2 : slack;
                    foreach (var it in il.current) it.X += shift;
                }
            }
            il.rows.Add(il.current);
            il.current = new List<InlineItem>();
        }
        il.x = 0;
        il.lineHasText = false;
        il.rowItemSources = 0;
        il.rowRightCount = 0;
    }

    private static HorizontalAlignment FragAlign(Aspose.Pdf.Text.TextFragment f)
    {
        if (f.TextState.HorizontalAlignment == HorizontalAlignment.Right)
            return HorizontalAlignment.Right;
        foreach (var s in f.Segments)
            if (s.TextState.HorizontalAlignment == HorizontalAlignment.Right)
                return HorizontalAlignment.Right;
        return f.TextState.HorizontalAlignment;
    }

    /// <summary>Lay out one paragraph of an inline cell: graphs, text fragments (segment by segment, word-wrapped at the content width) and inline images become items of the current row.</summary>
    private bool LayoutInlineCellParagraph(InlineCellLayoutState il, BaseParagraph para, double defaultFontSize, Aspose.Pdf.Text.TextState? cellTextState, HorizontalAlignment cellAlign)
    {
        if (para is Aspose.Pdf.Drawing.Graph g)
        {
            if (!g.IsInLineParagraph) FlushInlineRow(il, cellAlign);
            var marginL = g.Margin?.Left ?? 0;
            if (il.current.Count > 0 && il.x + marginL + g.Width > il.contentW) FlushInlineRow(il, cellAlign);
            il.x += marginL;
            il.current.Add(new InlineItem { Graph = g, X = il.x, Width = g.Width, Height = g.Height });
            il.x += g.Width;
            if (g.Height > il.maxH) il.maxH = g.Height;
            if (!g.IsInLineParagraph) FlushInlineRow(il, cellAlign);
        }
        else if (para is Aspose.Pdf.Text.TextFragment tf)
        {
            LayoutInlineFragment(il, tf, defaultFontSize, cellTextState, cellAlign);
        }
        else if (para is Image inlineImg)
        {
            // An Image among inline paragraphs joins the line as a fixed box;
            // following IsInLineParagraph text continues on the same line
            // (so the row does NOT flush after the image).
            var bytes = ReadImageBytes(inlineImg);
            if (bytes is null) return true;
            if (!inlineImg.IsInLineParagraph) FlushInlineRow(il, cellAlign);
            double dispW, dispH;
            if (inlineImg.FixWidth > 0 && inlineImg.FixHeight > 0)
            {
                dispW = inlineImg.FixWidth;
                dispH = inlineImg.FixHeight;
            }
            else if (TryGetCellImageSizePt(bytes) is (var nw, var nh) && nw > 0 && nh > 0)
            {
                dispW = il.contentW < double.MaxValue && nw > il.contentW ? il.contentW : nw;
                dispH = nh;
            }
            else
            {
                dispW = dispH = 24;
            }
            if (il.current.Count > 0 && il.x + dispW > il.contentW) FlushInlineRow(il, cellAlign);
            il.current.Add(new InlineItem { ImageData = bytes, X = il.x, Width = dispW, Height = dispH });
            il.x += dispW;
            if (dispH > il.maxH) il.maxH = dispH;
            NoteAlign(il, inlineImg.HorizontalAlignment);
        }
        // Other paragraph kinds inside a graph cell are not laid out inline.
        return true;
    }

    /// <summary>A text fragment of an inline cell: its segments become items, a multi-segment fragment run by run and a single one word-wrapped at the content width.</summary>
    private void LayoutInlineFragment(InlineCellLayoutState il, Aspose.Pdf.Text.TextFragment tf, double defaultFontSize, Aspose.Pdf.Text.TextState? cellTextState, HorizontalAlignment cellAlign)
    {
        il.marginL = tf.Margin?.Left ?? 0;
        if (!tf.IsInLineParagraph) FlushInlineRow(il, cellAlign);

        il.segs = System.Linq.Enumerable.ToList(tf.Segments);
        il.textCount = System.Linq.Enumerable.Count(il.segs, s => !string.IsNullOrEmpty(s.Text));
        if (il.textCount > 1)
        {
            LayoutSegmentedFragment(il, tf, defaultFontSize, cellTextState, cellAlign);
        }
        else
        {
            LayoutSingleSegmentFragment(il, tf, defaultFontSize, cellTextState, cellAlign);
        }
        NoteAlign(il, FragAlign(tf));
        // The row stays open: a following IsInLineParagraph fragment joins
        // this one's line (probed: a plain " | " fragment followed by an
        // inline "Aspose URL" draws as one line, the link pen-chained at the
        // first run's measured end). A following block paragraph flushes
        // the row itself on entry.
    }
}
