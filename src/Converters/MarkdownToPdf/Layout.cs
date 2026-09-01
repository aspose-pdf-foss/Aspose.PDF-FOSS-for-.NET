using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class MarkdownToPdfConverter
{
    private sealed class Flow
    {
        public Document Doc = null!;
        public Page Page = null!;
        public StringBuilder Sb = new();
        public double PageW, PageH, Margin, MarginTop, MarginBottom;
        public double Top;            // top-down cursor: bottom edge of the last block's box
        public double PendingGap;     // bottom margin of the previous block (collapses via max)
        public bool PageEmpty = true; // nothing on the current page yet: no top margin applies

        public double ContentWidth => PageW - 2 * Margin;
        public double Limit => PageH - MarginBottom;

        public void FlushPage()
        {
            if (Sb.Length > 0)
            {
                Page.AddContentStream(Encoding.ASCII.GetBytes(Sb.ToString()));
                Sb.Clear();
            }
        }

        public void NewPage()
        {
            FlushPage();
            Page = Doc.Pages.Add(PageW, PageH);
            EnsureFonts(Page);
            Top = MarginTop;
            PendingGap = 0;
            PageEmpty = true;
        }

        /// <summary>Open a block: apply the collapsed inter-block gap (skipped at a page
        /// top) and return the block's top edge; the caller advances <see cref="Top"/>.</summary>
        public double OpenBlock(double ownMargin)
        {
            var top = PageEmpty ? Top : Top + Math.Max(PendingGap, ownMargin);
            PageEmpty = false;
            return top;
        }

        public void CloseBlock(double bottom, double ownMargin)
        {
            Top = bottom;
            PendingGap = ownMargin;
        }
    }

    private static Document LayoutBlocks(List<Blk> blocks, double pageW, double pageH,
        double margin, double marginTop, double marginBottom)
    {
        var doc = Document.Create();
        var flow = new Flow
        {
            Doc = doc,
            PageW = pageW,
            PageH = pageH,
            Margin = margin,
            MarginTop = marginTop,
            MarginBottom = marginBottom,
            Top = marginTop,
        };
        flow.Page = doc.Pages.Add(pageW, pageH);
        EnsureFonts(flow.Page);

        foreach (var blk in blocks)
        {
            switch (blk)
            {
                case HeadBlk h:
                    LayoutHeading(flow, h);
                    break;
                case ParaBlk p:
                    LayoutTextBlock(flow, p.HardLines, flow.Margin, flow.ContentWidth, ParaGap, BaseFontSize, forceStyle: 0);
                    break;
                case QuoteBlk q:
                    LayoutTextBlock(flow, q.HardLines, flow.Margin + QuoteIndent,
                        flow.ContentWidth - QuoteIndent, ParaGap, BaseFontSize, forceStyle: 0);
                    break;
                case ListBlk l:
                    LayoutList(flow, l);
                    break;
                case CodeBlk c:
                    LayoutCode(flow, c);
                    break;
                case TableBlk t:
                    LayoutTable(flow, t);
                    break;
                case HrBlk:
                    LayoutHr(flow);
                    break;
                case ImgBlk img:
                    LayoutImage(flow, img);
                    break;
            }
        }
        flow.FlushPage();
        return doc;
    }

    private static double HeadingSize(int level) => BaseFontSize * (level switch
    {
        1 => 2.0,
        2 => 1.5,
        3 => 1.17,
        4 => 1.0,
        5 => 0.83,
        _ => 0.67,
    });

    private static void LayoutHeading(Flow flow, HeadBlk h)
    {
        var size = HeadingSize(h.Level);
        var hm = Math.Max(ParaGap, HeadingMarginEm * size);
        var top = flow.OpenBlock(hm);
        var baseline = top + AscentEm * size;
        if (baseline + DescentEm * size > flow.Limit)
        {
            flow.NewPage();
            baseline = flow.Top + AscentEm * size;
        }
        // A heading is set bold; explicit italic markers survive.
        var runs = h.Runs.Select(r => r with { Style = (byte)(r.Style | 1) }).ToList();
        var lines = WrapRuns(runs, size, flow.ContentWidth, flow.ContentWidth);
        var y = baseline;
        for (var k = 0; k < lines.Count; k++)
        {
            EmitRunLine(flow, flow.Margin, y, lines[k], size);
            if (k < lines.Count - 1) y += size * LineHeightEm;
        }
        flow.CloseBlock(y + DescentEm * size, hm);
    }

    private static void LayoutTextBlock(Flow flow, List<List<Run>> hardLines, double x,
        double width, double gap, double size, byte forceStyle)
    {
        var top = flow.OpenBlock(gap);
        var baseline = top + AscentEm * size;
        var first = true;
        foreach (var hardLine in hardLines)
        {
            var runs = forceStyle == 0 ? hardLine
                : hardLine.Select(r => r with { Style = (byte)(r.Style | forceStyle) }).ToList();
            foreach (var segs in WrapRuns(runs, size, width, width))
            {
                if (!first) baseline += size * LineHeightEm;
                first = false;
                if (baseline + DescentEm * size > flow.Limit)
                {
                    flow.NewPage();
                    baseline = flow.Top + AscentEm * size;
                }
                EmitRunLine(flow, x, baseline, segs, size);
            }
        }
        flow.CloseBlock(first ? top : baseline + DescentEm * size, gap);
    }

    private static void LayoutList(Flow flow, ListBlk list)
    {
        var top = flow.OpenBlock(ListGap);
        var size = BaseFontSize;
        var baseline = top + AscentEm * size;
        var first = true;
        var width = flow.ContentWidth - ListContentIndent;
        foreach (var (marker, runs) in list.Items)
        {
            var lines = WrapRuns(runs, size, width, width);
            for (var k = 0; k < lines.Count; k++)
            {
                if (!first) baseline += size * LineHeightEm;
                first = false;
                if (baseline + DescentEm * size > flow.Limit)
                {
                    flow.NewPage();
                    baseline = flow.Top + AscentEm * size;
                }
                if (k == 0)
                    EmitRunLine(flow, flow.Margin + ListBulletIndent, baseline,
                        new List<Seg> { new(marker, 0, null) }, size);
                EmitRunLine(flow, flow.Margin + ListContentIndent, baseline, lines[k], size);
            }
        }
        flow.CloseBlock(first ? top : baseline + DescentEm * size, ListGap);
    }

    private static void LayoutCode(Flow flow, CodeBlk code)
    {
        var top = flow.OpenBlock(ParaGap);
        var size = code.Size;
        var baseline = top + AscentEm * size;
        var first = true;
        foreach (var line in code.Lines)
        {
            if (!first) baseline += BaseFontSize * LineHeightEm;
            first = false;
            if (baseline + DescentEm * size > flow.Limit)
            {
                flow.NewPage();
                baseline = flow.Top + AscentEm * size;
            }
            if (line.Length > 0)
                EmitRunLine(flow, flow.Margin, baseline, new List<Seg> { new(line, 4, null) }, size);
        }
        flow.CloseBlock(first ? top : baseline + DescentEm * size, ParaGap);
    }

    private static void LayoutHr(Flow flow)
    {
        // Rules pace on their own collapsed 6pt margin, and the first one on a page
        // seats directly at the body margin.
        var top = flow.PageEmpty ? flow.Top : flow.Top + Math.Max(flow.PendingGap, HrMargin);
        flow.PageEmpty = false;
        if (top + HrGrooveH > flow.Limit)
        {
            flow.NewPage();
            top = flow.Top;
        }
        EmitHrGroove(flow.Sb, flow.PageW, flow.PageH, top);
        flow.Top = top + HrGrooveH;
        flow.PendingGap = HrMargin;
    }

    private static void LayoutImage(Flow flow, ImgBlk img)
    {
        var top = flow.OpenBlock(ParaGap);
        if (top + img.H > flow.Limit)
        {
            flow.NewPage();
            top = flow.Top;
        }
        var x = img.Center ? (flow.PageW - img.W) / 2 : flow.Margin;
        var yTop = flow.PageH - top;
        try
        {
            flow.Page.AddImage(img.Data, new Rectangle(x, yTop - img.H, x + img.W, yTop));
        }
        catch
        {
            // An undecodable image leaves its space blank.
        }
        flow.CloseBlock(top + img.H, ParaGap);
    }

    private static void LayoutTable(Flow flow, TableBlk table)
    {
        var top = flow.OpenBlock(ParaGap);
        var size = BaseFontSize;
        var rows = table.Rows;
        var cols = rows.Max(r => r.Count);

        // Header runs render bold.
        var styled = new List<List<List<Run>>>();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new List<List<Run>>();
            for (var c = 0; c < cols; c++)
            {
                var runs = c < rows[r].Count ? rows[r][c] : new List<Run>();
                if (r == 0) runs = runs.Select(x2 => x2 with { Style = (byte)(x2.Style | 1) }).ToList();
                row.Add(runs);
            }
            styled.Add(row);
        }

        // Natural (unwrapped) column widths; columns whose natural width no longer
        // fits share the remaining span equally and wrap their cells.
        var natural = new double[cols];
        for (var c = 0; c < cols; c++)
            foreach (var row in styled)
                natural[c] = Math.Max(natural[c], MeasureRuns(row[c], size));
        var avail = flow.ContentWidth - 2 * CellPad - (cols - 1) * CellGutter;
        var widths = (double[])natural.Clone();
        if (natural.Sum() > avail)
        {
            var flexible = Enumerable.Range(0, cols).ToList();
            var remaining = avail;
            bool changed = true;
            while (changed)
            {
                changed = false;
                var share = remaining / Math.Max(1, flexible.Count);
                for (var fi = flexible.Count - 1; fi >= 0; fi--)
                {
                    var c = flexible[fi];
                    if (natural[c] <= share)
                    {
                        widths[c] = natural[c];
                        remaining -= natural[c];
                        flexible.RemoveAt(fi);
                        changed = true;
                    }
                }
            }
            foreach (var c in flexible)
                widths[c] = remaining / flexible.Count;
        }

        var colX = new double[cols];
        var xCursor = flow.Margin + CellPad;
        for (var c = 0; c < cols; c++)
        {
            colX[c] = xCursor;
            xCursor += widths[c] + CellGutter;
        }

        // Track the row's FIRST BASELINE directly: the probed uniform advance is
        // firstBase(r+1) − firstBase(r) = maxLines(r)·13.5 + 3 (the header seats its
        // baseline 2.21 + ascent under the table top).
        var firstBase = top + HeaderTopPad + AscentEm * size;
        var lastMax = 0;
        for (var r = 0; r < styled.Count; r++)
        {
            var cellLines = new List<List<List<Seg>>>();
            for (var c = 0; c < cols; c++)
                cellLines.Add(WrapRuns(styled[r][c], size, widths[c], widths[c]));
            var maxLines = Math.Max(1, cellLines.Max(cl => cl.Count));

            if (r > 0) firstBase += lastMax * size * LineHeightEm + RowPad;
            if (r > 0 && firstBase + (maxLines - 1) * size * LineHeightEm + DescentEm * size > flow.Limit)
            {
                flow.NewPage();
                firstBase = flow.Top + RowPad + AscentEm * size;
            }
            lastMax = maxLines;

            for (var c = 0; c < cols; c++)
            {
                var lines = cellLines[c];
                if (lines.Count == 0) continue;
                // A short cell centres vertically in its row.
                var cellBase = firstBase + (maxLines - lines.Count) * size * LineHeightEm / 2;
                for (var k = 0; k < lines.Count; k++)
                {
                    var x = colX[c];
                    if (r == 0)
                    {
                        // Header cells centre over their column.
                        var w = SegsWidth(lines[k], size);
                        x = colX[c] + (widths[c] - w) / 2;
                    }
                    EmitRunLine(flow, x, cellBase + k * size * LineHeightEm, lines[k], size);
                }
            }
        }
        flow.CloseBlock(firstBase - AscentEm * size + lastMax * size * LineHeightEm + RowPad, ParaGap);
    }

    private sealed record Seg(string Text, byte Style, string? Uri);

    private static double MeasureText(string text, byte style, double size)
    {
        var m = Measurers.GetOrAdd((style, size), static key =>
        {
            var name = (key.style & 4) != 0 ? "Courier" : (key.style & 3) switch
            {
                3 => "Times-BoldItalic",
                1 => "Times-Bold",
                2 => "Times-Italic",
                _ => "Times-Roman",
            };
            return Text.TextPaginator.CreateMeasurer(name, key.size, null);
        });
        return m(text);
    }

    private static double MeasureRuns(List<Run> runs, double size)
        => runs.Sum(r => MeasureText(r.Text, r.Style, size));

    private static double SegsWidth(List<Seg> segs, double size)
        => segs.Sum(s => MeasureText(s.Text, s.Style, size));

    /// <summary>Word-wrap styled runs into lines of segments. Split points are spaces;
    /// a word that alone exceeds the width still gets its own line.</summary>
    private static List<List<Seg>> WrapRuns(List<Run> runs, double size, double firstWidth, double width)
    {
        var result = new List<List<Seg>>();
        var line = new List<Seg>();
        double lineW = 0;
        var limit = firstWidth;

        void Flush()
        {
            // Trailing spaces do not count against the next line.
            while (line.Count > 0 && string.IsNullOrWhiteSpace(line[line.Count - 1].Text))
                line.RemoveAt(line.Count - 1);
            if (line.Count > 0) result.Add(line);
            line = new List<Seg>();
            lineW = 0;
            limit = width > 0 ? width : firstWidth;
        }

        foreach (var run in runs)
        {
            var tokens = Regex.Split(run.Text, "( )");
            foreach (var tok in tokens)
            {
                if (tok.Length == 0) continue;
                var w = MeasureText(tok, run.Style, size);
                if (tok != " " && lineW + w > limit && line.Any(s => !string.IsNullOrWhiteSpace(s.Text)))
                    Flush();
                if (tok == " " && line.Count == 0) continue; // no leading spaces
                line.Add(new Seg(tok, run.Style, run.Uri));
                lineW += w;
            }
        }
        Flush();

        // Merge adjacent same-style segments for compact emission.
        var merged = new List<List<Seg>>();
        foreach (var l in result)
        {
            var ml = new List<Seg>();
            foreach (var s in l)
            {
                if (ml.Count > 0 && ml[ml.Count - 1].Style == s.Style
                    && string.Equals(ml[ml.Count - 1].Uri, s.Uri, StringComparison.Ordinal))
                    ml[ml.Count - 1] = ml[ml.Count - 1] with { Text = ml[ml.Count - 1].Text + s.Text };
                else
                    ml.Add(s);
            }
            merged.Add(ml);
        }
        return merged;
    }

    /// <summary>Emit one laid-out line at <paramref name="baselineTop"/> (top-down).
    /// Links render blue with a per-word underline and carry a link annotation.</summary>
    private static void EmitRunLine(Flow flow, double x, double baselineTop, List<Seg> segs, double size)
    {
        var y = flow.PageH - baselineTop;
        var cursor = x;
        foreach (var seg in segs)
        {
            var w = MeasureText(seg.Text, seg.Style, size);
            if (!string.IsNullOrWhiteSpace(seg.Text))
            {
                var fontRes = (seg.Style & 4) != 0 ? Mono : (seg.Style & 3) switch
                {
                    3 => BoldItalic,
                    1 => Bold,
                    2 => Italic,
                    _ => Normal,
                };
                var color = seg.Uri != null ? "0 0 1 rg " : "";
                var reset = seg.Uri != null ? " 0 0 0 rg" : "";
                flow.Sb.Append($"{color}BT /{fontRes} {F(size)} Tf {F(cursor)} {F(y)} Td ({EscapePdf(seg.Text)}) Tj ET{reset}\n");

                if (seg.Uri != null)
                {
                    // Per-word underline: the space gaps stay open.
                    var ux = cursor;
                    var uy = flow.PageH - (baselineTop + UnderlineDrop);
                    foreach (var word in Regex.Split(seg.Text, "( )"))
                    {
                        if (word.Length == 0) continue;
                        var ww = MeasureText(word, seg.Style, size);
                        if (word != " ")
                            flow.Sb.Append($"q 0 0 1 RG {F(UnderlineW)} w {F(ux)} {F(uy)} m {F(ux + ww)} {F(uy)} l S Q\n");
                        ux += ww;
                    }
                    var link = new Aspose.Pdf.Annotations.LinkAnnotation(flow.Page,
                        new Rectangle(cursor, y - DescentEm * size, cursor + w, y + AscentEm * size))
                    {
                        Action = new Aspose.Pdf.Annotations.GoToURIAction(seg.Uri),
                    };
                    flow.Page.Annotations.Add(link);
                }
            }
            cursor += w;
        }
    }

    /// <summary>Draw the UA 3-D groove for a thematic break: four strokes seated half a
    /// width inside the 1.5pt box's edges — top/left black, bottom/right #555 — spanning
    /// the page inside the 6pt body margin. <paramref name="top"/> is the box top measured
    /// from the PAGE TOP; the emitted coordinates are bottom-up PDF space.</summary>
    private static void EmitHrGroove(StringBuilder sb, double pageWidth, double pageHeight, double top)
    {
        const string GrooveGray = "0.333333 0.333333 0.333333";
        var left = BodyMargin;
        var right = pageWidth - BodyMargin;
        var inset = HrStrokeW / 2;
        var yTop = pageHeight - (top + inset);
        var yBottom = pageHeight - (top + HrGrooveH - inset);
        var yBoxTop = pageHeight - top;
        var yBoxBottom = pageHeight - top - HrGrooveH;
        var xLeft = left + inset;
        var xRight = right - inset;
        sb.Append($"q 0 0 0 RG {F(HrStrokeW)} w {F(left)} {F(yTop)} m {F(right)} {F(yTop)} l S Q\n");
        sb.Append($"q {GrooveGray} RG {F(HrStrokeW)} w {F(xRight)} {F(yBoxBottom)} m {F(xRight)} {F(yBoxTop)} l S Q\n");
        sb.Append($"q {GrooveGray} RG {F(HrStrokeW)} w {F(left)} {F(yBottom)} m {F(right)} {F(yBottom)} l S Q\n");
        sb.Append($"q 0 0 0 RG {F(HrStrokeW)} w {F(xLeft)} {F(yBoxBottom)} m {F(xLeft)} {F(yBoxTop)} l S Q\n");
    }
}
