using System;
using System.Collections.Generic;
using System.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Per-call working state of the form-area render. One instance per
    /// invocation; never shared.</summary>
    private sealed class FormAreasState
    {
        public System.Globalization.CultureInfo invc = null!;
        public Document doc = null!;
        public List<Page> pages = new();
        public List<StringBuilder> streams = new();     // one content stream per page
        public (double asc, double sum) tm;              // Times New Roman's OS/2 win metrics
        public double pageW, pageH, marginT, contentL;
        public double sliceH;                            // the sheet's content height: one page of document space
        public List<FaArea> areas = new();
    }

    private static double FaLineH(FormAreasState fa) => MetricLineHeight(FaFs, fa.tm.sum);

    private static double FaDrop(FormAreasState fa) => MetricBaselineDrop(FaFs, FaLineH(fa), fa.tm);

    private static double FaRowH(FormAreasState fa) => FaLineH(fa) + 2 * FaCellPad;

    private static double FaMeasure(string s, bool bold) => s.Length == 0 ? 0 : MeasureFaceText(bold ? FaBoldFace : FaFace, s, FaFs);

    private static Document FaRender(FormAreasState fa)
    {
        fa.doc = new Document();
        FaPageAt(fa, 0);
        foreach (var area in fa.areas)
            foreach (var ctl in area.Controls)
            {
                if (ctl.Table is { } t) FaPlaceTable(fa, t, fa.contentL + ctl.Left, area.Top + ctl.Top);
                else FaPlaceLabel(fa, ctl, fa.contentL + ctl.Left, area.Top + ctl.Top);
            }
        for (var i = 0; i < fa.pages.Count; i++)
            if (fa.streams[i].Length > 0)
                fa.pages[i].AddContentStream(Encoding.ASCII.GetBytes(fa.streams[i].ToString()));
        return fa.doc;
    }

    /// <summary>The page holding slice <paramref name="index"/> of the document space, opened
    /// with every earlier one when the flow has not reached it yet.</summary>
    private static Page FaPageAt(FormAreasState fa, int index)
    {
        while (fa.pages.Count <= index)
        {
            var page = fa.doc.Pages.Add(fa.pageW, fa.pageH);
            EnsureFonts(page);
            EnsureFont(page, FaFontName, FaRes);
            EnsureFont(page, FaBoldFontName, FaBoldRes);
            fa.pages.Add(page);
            fa.streams.Add(new StringBuilder());
        }
        return fa.pages[index];
    }

    /// <summary>The page index and page-space top of a document-space top.</summary>
    private static (int page, double top) FaPlace(FormAreasState fa, double docTop)
    {
        var index = Math.Max(0, (int)Math.Floor((docTop + FaBoundary) / fa.sliceH));
        return (index, fa.marginT + docTop - index * fa.sliceH);
    }

    /// <summary>A label at its declared offset: its words wrapped at the min-content width
    /// (the widest word), each line on the page its own top falls in.</summary>
    private static void FaPlaceLabel(FormAreasState fa, FaControl ctl, double x, double docTop)
    {
        if (ctl.Text.Length == 0) return;
        var lineH = FaLineH(fa);
        var y = docTop;
        foreach (var line in FaWrapMin(ctl.Text))
        {
            var (page, top) = FaPlace(fa, y);
            FaEmit(fa, page, line, false, x, top + FaDrop(fa));
            y += lineH;
        }
    }

    /// <summary>Greedy lines of a text at its min-content width: a line takes the next word
    /// while the widest word's advance still holds it.</summary>
    private static List<string> FaWrapMin(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double width = 0;
        foreach (var w in words) width = Math.Max(width, FaMeasure(w, false));
        var lines = new List<string>();
        var cur = "";
        foreach (var w in words)
        {
            var next = cur.Length == 0 ? w : cur + " " + w;
            if (cur.Length > 0 && FaMeasure(next, false) > width + FaBoundary)
            {
                lines.Add(cur);
                cur = w;
            }
            else cur = next;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    /// <summary>The table at its declared offset: the header row, then the body rows one
    /// after another, a row that cannot end above the page's content bottom opening the
    /// next page under the repeated header row.</summary>
    private static void FaPlaceTable(FormAreasState fa, FaTable t, double x, double docTop)
    {
        t.ColW = FaColumns(t);
        var rowH = FaRowH(fa);
        var limit = fa.marginT + fa.sliceH;               // the page's content bottom
        var (page, top) = FaPlace(fa, docTop);
        var y = top + FaSpacing;
        FaDrawRow(fa, page, t, t.Head, true, x, y);
        y += rowH + FaSpacing;
        foreach (var row in t.Rows)
        {
            if (y + rowH > limit + FaBoundary)
            {
                page++;
                y = fa.marginT + FaSpacing;
                FaDrawRow(fa, page, t, t.Head, true, x, y);
                y += rowH + FaSpacing;
            }
            FaDrawRow(fa, page, t, row, false, x, y);
            y += rowH + FaSpacing;
        }
    }

    /// <summary>The columns: a declared th width is the content box, so the column box is it
    /// plus the cell inset, floored at the column's min-content; the excess over the width
    /// the spacings leave is taken back in proportion to each column's slack, a shortfall
    /// widening every column in proportion to its box.</summary>
    private static double[] FaColumns(FaTable t)
    {
        var n = t.Head.Count;
        var inset = 2 * FaCellPad;
        var avail = t.W - (n + 1) * FaSpacing;
        var w = new double[n];
        var min = new double[n];
        for (var i = 0; i < n; i++)
        {
            min[i] = FaMinContent(t.Head[i], true) + inset;
            foreach (var row in t.Rows)
                if (i < row.Count) min[i] = Math.Max(min[i], FaMinContent(row[i], false) + inset);
            w[i] = Math.Max((i < t.HeadW.Count ? t.HeadW[i] : 0) + inset, min[i]);
        }
        double sum = 0, slack = 0;
        for (var i = 0; i < n; i++) { sum += w[i]; slack += w[i] - min[i]; }
        if (sum > avail + FaBoundary)
        {
            var excess = sum - avail;
            for (var i = 0; i < n; i++)
                w[i] = excess >= slack - FaBoundary ? min[i] : w[i] - excess * (w[i] - min[i]) / slack;
        }
        else if (sum < avail - FaBoundary && sum > 0)
            for (var i = 0; i < n; i++) w[i] *= avail / sum;
        return w;
    }

    /// <summary>A cell's min-content: its widest space-separated word.</summary>
    private static double FaMinContent(string text, bool bold)
    {
        double w = 0;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            w = Math.Max(w, FaMeasure(word, bold));
        return w;
    }

    /// <summary>One row: a th centred in its content box in bold, a td at its left pad.</summary>
    private static void FaDrawRow(FormAreasState fa, int page, FaTable t, List<string> cells, bool head, double x, double top)
    {
        var baseTd = top + FaCellPad + FaDrop(fa);
        var cx = x + FaSpacing;
        for (var i = 0; i < t.ColW.Length; i++)
        {
            var text = i < cells.Count ? cells[i] : "";
            var contentW = t.ColW[i] - 2 * FaCellPad;
            var tx = cx + FaCellPad + (head ? (contentW - FaMeasure(text, true)) / 2 : 0);
            FaEmit(fa, page, text, head, tx, baseTd);
            cx += t.ColW[i] + FaSpacing;
        }
    }

    private static void FaEmit(FormAreasState fa, int page, string text, bool bold, double x, double baseTd)
    {
        if (text.Length == 0) return;
        FaPageAt(fa, page);
        fa.streams[page].Append(Compat.Format(fa.invc,
            $"BT 0 0 0 rg /{(bold ? FaBoldRes : FaRes)} {FaFs:0.###} Tf 1 0 0 1 {x:0.###} {fa.pageH - baseTd:0.###} Tm ({EscapePdfString(text)}) Tj ET\n"));
    }
}
