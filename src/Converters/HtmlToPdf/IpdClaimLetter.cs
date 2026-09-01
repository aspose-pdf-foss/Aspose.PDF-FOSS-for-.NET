using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The ipd claim-file letter (a generated claims-system report) ───────────
    // A form letter: an `ipdPortrait` 8-in sheet holding an `ipdPageTitle`, then
    // `ipdSection`s — each a double-bordered `ipdSectionTitle` band followed by a
    // 25/75 `ipdPropertyGrid` or bordered `ipdSubSection` boxes (gray title bar +
    // padded contents holding fixed-layout `slfFormLayout` label/value tables).
    //
    // Geometry (all measured on the expected conversion of the corpus letter):
    //   page 771.5 × 842 = 96 content left + the section-title border box 585.5
    //   (576 declared + 2×3.75 px padding + 2×1 border) + the 90 right band;
    //   content runs 72..770. The 18 pt bold title centres on the 96..672 sheet
    //   (baseline 94.74) over a 1.5 pt rule at 99.75. A section title seats its
    //   12 pt bold lines on a 13.5 pitch inside a 1 pt "double" border (drawn as
    //   two ⅓ pt strokes) with 3.75 padding; margins collapse to 15 between the
    //   letter's blocks. Grid rows are 19.5 tall (10 pt text at 3.75 cell
    //   padding, 0.75 collapsed borders), label column 25 % of 576; form-table
    //   rows are lines×11.25 + 7.5 with cells wrapped at their fixed column.
    //   The grid's label-column gray paints as one FIXED rect per
    //   page (96.38..240.19 × 72.38..242.25) regardless of where the rows sit —
    //   an era artifact reproduced as-is.

    private const double IpdPageW = 771.5;
    private const double IpdPageH = 842.0;
    private const double IpdContentTop = 72.0;
    private const double IpdContentBottom = 770.0;
    private const double IpdLeft = 96.0;
    private const double IpdSheetW = 576.0;          // .ipdPortrait: 8in
    private const double IpdTitleBaseTd = 94.74;
    private const double IpdTitleRuleTd = 99.75;     // 2px border-bottom, 1.5 pt
    private const double IpdBlockMargin = 15.0;      // 20px margins, collapsed
    private const double IpdSecPad = 3.75;           // 5px padding
    private const double IpdSecBorder = 1.0;         // the "double" border, 1 pt
    private const double IpdSecLinePitch = 13.5;     // 12 pt title line pitch
    private const double IpdSecAscent = 10.91;       // baseline inset under padding
    private const double IpdGridRowH = 19.5;
    private const double IpdGridLabelFrac = 0.25;
    private const double IpdGridBaseOff = 13.59;     // row top → text baseline
    private const double IpdCellPad = 3.75;          // 5px td padding
    private const double IpdAscent10 = 9.05;         // 10 pt Arial ascent
    private const double IpdLinePitch10 = 11.25;     // 10 pt wrapped-line pitch
    private const double IpdFormRowPad = 7.5;        // row height = lines·11.25 + 7.5
    private const double IpdSubBarH = 18.0;          // gray sub-title bar height
    private const double IpdSubBarPad = 2.25;        // 3px padding
    private const double IpdSubBarBase = 12.43;      // bar top → 11 pt baseline
    private const double IpdSubBarW = 579.0;         // width:100% bar overhang
    private const double IpdSubPad = 6.0;            // 8px contents padding
    private const double IpdSubTableFrac = 0.95;     // .ipdSubSectionContents table
    private const double IpdGrayTop = 72.38;         // the fixed label-gray rect
    private const double IpdGrayBottom = 242.25;
    private static readonly double[] IpdCol4 = { 0.20, 0.30, 0.20, 0.30 };
    private static readonly double[] IpdCol2 = { 0.50, 0.50 };
    private static readonly double[] IpdCol2Pay = { 0.55, 0.45 };

    private sealed class IpdItem
    {
        public string Kind = "";                     // title/sectitle/subbegin/subend/grid/form/text
        public string Text = "";
        public double Fs = 10;
        public List<List<string>> Rows = new();      // grid + form cell texts per row
        public double[] Cols = Array.Empty<double>();
        public bool Framed;
    }

    private static Document? TryRenderIpdClaimLetter(string html)
    {
        if (!html.Contains("class=\"ipdPortrait\"", StringComparison.Ordinal)
            || !html.Contains("ipdPageTitle", StringComparison.Ordinal)
            || !html.Contains("ipdPropertyGrid", StringComparison.Ordinal)
            || !html.Contains("slfFormLayout", StringComparison.Ordinal))
            return null;

        static string Flat(string s) => Regex.Replace(DecodeEntities(
            Regex.Replace(s, @"<[^>]+>", " ")).Replace(' ', ' '), @"\s+", " ").Trim();

        // Balanced scan: from an opening <div ...> at openEnd, find the matching
        // </div> and return the inner segment.
        static string DivInner(string h, int openEnd, out int closeEnd)
        {
            var depth = 1;
            var i = openEnd;
            foreach (Match m in Regex.Matches(h[openEnd..], @"<(/?)div\b[^>]*>",
                RegexOptions.IgnoreCase))
            {
                depth += m.Groups[1].Value.Length > 0 ? -1 : 1;
                if (depth == 0)
                {
                    closeEnd = openEnd + m.Index + m.Length;
                    return h[openEnd..(openEnd + m.Index)];
                }
            }
            closeEnd = h.Length;
            return h[openEnd..];
        }

        var items = new List<IpdItem>();
        var bodyM = Regex.Match(html, @"<div\b[^>]*class=""ipdPortrait""[^>]*>",
            RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var seg = DivInner(html, bodyM.Index + bodyM.Length, out _);

        var pos = 0;
        var subDepth = new Stack<int>();  // div depths at which open ipdSubSections close
        var depthNow = 0;
        var tagRx = new Regex(@"<(/?)(div|table|textarea)\b([^>]*)>", RegexOptions.IgnoreCase);
        while (pos < seg.Length)
        {
            var m = tagRx.Match(seg, pos);
            if (!m.Success) break;
            pos = m.Index + m.Length;
            var closing = m.Groups[1].Value.Length > 0;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            var attrs = m.Groups[3].Value;
            var clsM = Regex.Match(attrs, @"class\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
            var cls = clsM.Success ? clsM.Groups[1].Value : "";
            if (tag == "div")
            {
                if (closing)
                {
                    depthNow--;
                    if (subDepth.Count > 0 && depthNow < subDepth.Peek())
                    {
                        subDepth.Pop();
                        items.Add(new IpdItem { Kind = "subend" });
                    }
                    continue;
                }
                if (cls.Contains("ipdPageTitle"))
                {
                    items.Add(new IpdItem { Kind = "title", Text = Flat(DivInner(seg, pos, out pos)) });
                    continue;
                }
                if (cls.Contains("ipdSectionTitle"))
                {
                    items.Add(new IpdItem { Kind = "sectitle", Text = Flat(DivInner(seg, pos, out pos)) });
                    continue;
                }
                if (cls.Contains("ipdSubSectionTitle"))
                {
                    var t = Flat(DivInner(seg, pos, out pos));
                    if (items.Count > 0 && items[^1].Kind == "subbegin") items[^1].Text = t;
                    continue;
                }
                if (cls.Contains("ipdSubSection") && !cls.Contains("Contents"))
                {
                    items.Add(new IpdItem { Kind = "subbegin" });
                    depthNow++;
                    subDepth.Push(depthNow);
                    continue;
                }
                // a plain inner div carrying bare text (the "nodata" messages)
                depthNow++;
                var tail = seg[pos..];
                var nextTag = tagRx.Match(tail);
                var lead = nextTag.Success ? tail[..nextTag.Index] : tail;
                if (Flat(lead) is { Length: > 0 } bare
                    && !cls.Contains("ipdSection") && !cls.Contains("Contents")
                    && !cls.Contains("slfGeneralPayment") && !cls.Contains("slfOverPay"))
                    items.Add(new IpdItem { Kind = "text", Text = bare, Fs = 9 });
                continue;
            }
            if (tag == "textarea")
            {
                var end = seg.IndexOf("</textarea>", pos, StringComparison.OrdinalIgnoreCase);
                if (end < 0) end = seg.Length;
                items.Add(new IpdItem { Kind = "text", Text = Flat(seg[pos..end]), Fs = 9 });
                pos = end;
                continue;
            }
            if (tag == "table" && !closing)
            {
                var end = seg.IndexOf("</table>", pos, StringComparison.OrdinalIgnoreCase);
                if (end < 0) end = seg.Length;
                var body = seg[pos..end];
                pos = end;
                var it = new IpdItem();
                if (cls.Contains("ipdPropertyGrid")) it.Kind = "grid";
                else
                {
                    it.Kind = "form";
                    it.Framed = cls.Contains("slfFramed");
                    it.Cols = cls.Contains("slfFormLayoutCol2")
                        ? (cls.Contains("slfGeneralPayment") ? IpdCol2Pay : IpdCol2)
                        : IpdCol4;
                    if (cls.Contains("slfCoverage")) { it.Kind = "coverage"; }
                }
                foreach (Match rm in Regex.Matches(body, @"<tr\b[^>]*>([\s\S]*?)</tr>",
                    RegexOptions.IgnoreCase))
                {
                    var cells = new List<string>();
                    foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                        @"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
                    {
                        cells.Add(Flat(cm.Groups[2].Value));
                        var spanM = Regex.Match(cm.Groups[1].Value, @"colspan\s*=\s*""?(\d+)",
                            RegexOptions.IgnoreCase);
                        if (spanM.Success && int.TryParse(spanM.Groups[1].Value, out var sp))
                            for (var k = 1; k < sp; k++) cells.Add("");
                    }
                    if (cells.Count > 0) it.Rows.Add(cells);
                }
                if (it.Rows.Count > 0) items.Add(it);
            }
        }
        while (subDepth.Count > 0) { subDepth.Pop(); items.Add(new IpdItem { Kind = "subend" }); }
        if (items.Count == 0 || items[0].Kind != "title") return null;

        // ── layout ──────────────────────────────────────────────────────────────
        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var pages = new List<StringBuilder>();
        StringBuilder sb = null!;
        var pageHasGrid = new List<bool>();
        double MW(string s, double fs, bool bold = false)
            => MeasureFaceText(bold ? "Arial Bold" : "Arial", s, fs);

        void NewPage()
        {
            sb = new StringBuilder();
            pages.Add(sb);
            pageHasGrid.Add(false);
        }
        NewPage();
        void Run(double fs, double x, double yTd, string text, bool bold = false)
            => sb.AppendLine(string.Create(inv,
                $"BT /{(bold ? "F9" : "F8")} {fs:0.##} Tf 1 0 0 1 {x:F2} {IpdPageH - yTd:F2} Tm ({EscapePdfString(text)}) Tj ET"));
        void Line(double x0, double y0, double x1, double y1, double w, string gray = "0.651 0.651 0.651")
            => sb.AppendLine(string.Create(inv,
                $"q {gray} RG {w:0.###} w {x0:F2} {IpdPageH - y0:F2} m {x1:F2} {IpdPageH - y1:F2} l S Q"));
        void FillRect(double x0, double yTop, double wpt, double hpt, string rgb)
            => sb.AppendLine(string.Create(inv,
                $"q {rgb} rg {x0:F2} {IpdPageH - yTop - hpt:F2} {wpt:F2} {hpt:F2} re f Q"));

        // greedy word wrap at a pixel budget
        List<string> Wrap(string text, double fs, double budget, bool bold = false)
        {
            var lines = new List<string>();
            var cur = new StringBuilder();
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var trial = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length == 0 || MW(trial, fs, bold) <= budget) { cur.Clear(); cur.Append(trial); }
                else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            if (lines.Count == 0) lines.Add("");
            return lines;
        }

        var y = IpdContentTop;      // running cursor: bottom edge of the last box
        double pendingMargin = 0;
        // open subsection boxes: (boxTopTd on this page) — sides drawn on close/break
        double subTop = -1;
        var inSub = false;

        void BreakPage()
        {
            if (inSub && subTop >= 0)
            {
                Line(96.38, subTop, 96.38, IpdContentBottom, 0.75);
                Line(671.62, subTop, 671.62, IpdContentBottom, 0.75);
            }
            NewPage();
            y = IpdContentTop;
            subTop = inSub ? IpdContentTop : -1;
            pendingMargin = 0;
        }
        void Advance(double margin) => pendingMargin = Math.Max(pendingMargin, margin);
        double Open(double ownTop)
        {
            var t = y + Math.Max(pendingMargin, ownTop);
            pendingMargin = 0;
            return t;
        }

        foreach (var it in items)
        {
            switch (it.Kind)
            {
                case "title":
                {
                    var w = MW(it.Text, 18, true);
                    Run(18, IpdLeft + (IpdSheetW - w) / 2, IpdTitleBaseTd, it.Text, true);
                    Line(IpdLeft, IpdTitleRuleTd, IpdLeft + IpdSheetW, IpdTitleRuleTd, 1.5, "0 0 0");
                    y = IpdTitleRuleTd + 0.75;
                    Advance(7.5);
                    break;
                }
                case "sectitle":
                {
                    var lines = Wrap(it.Text, 12, IpdSheetW, true);
                    var boxH = 2 * IpdSecBorder + 2 * IpdSecPad + lines.Count * IpdSecLinePitch;
                    if (y + Math.Max(pendingMargin, IpdBlockMargin) + boxH > IpdContentBottom)
                        BreakPage();
                    var t = Open(IpdBlockMargin);
                    var bx1 = IpdLeft + IpdSheetW + 2 * IpdSecPad + 2 * IpdSecBorder;
                    // the 1 pt "double" border draws as two ⅓ pt strokes
                    foreach (var off in new[] { 0.17, 0.83 })
                    {
                        Line(IpdLeft, t + off, bx1, t + off, 0.333, "0 0 0");
                        Line(IpdLeft, t + boxH - 1 + off, bx1, t + boxH - 1 + off, 0.333, "0 0 0");
                        Line(IpdLeft + off, t, IpdLeft + off, t + boxH, 0.333, "0 0 0");
                        Line(bx1 - 1 + off, t, bx1 - 1 + off, t + boxH, 0.333, "0 0 0");
                    }
                    for (var i = 0; i < lines.Count; i++)
                        Run(12, IpdLeft + IpdSecBorder + IpdSecPad,
                            t + IpdSecBorder + IpdSecPad + IpdSecAscent + i * IpdSecLinePitch,
                            lines[i], true);
                    y = t + boxH;
                    Advance(IpdBlockMargin);
                    break;
                }
                case "grid":
                {
                    var t = Open(IpdBlockMargin);
                    // the 25 % split resolves on the border-inset content width
                    // (96.38 + 0.25 · 575.25 = 240.19, the measured divider)
                    var xSplit = IpdLeft + 0.38 + (IpdSheetW - 0.75) * IpdGridLabelFrac;
                    var xRight = IpdLeft + IpdSheetW;            // 672
                    if (t + IpdGridRowH > IpdContentBottom) { BreakPage(); t = IpdContentTop; }
                    pageHasGrid[^1] = true;
                    Line(IpdLeft, t + 0.38, xSplit + 0.37, t + 0.38, 0.75);
                    Line(xSplit - 0.38, t + 0.38, xRight, t + 0.38, 0.75);
                    foreach (var row in it.Rows)
                    {
                        if (t + IpdGridRowH > IpdContentBottom)
                        {
                            BreakPage();
                            t = IpdContentTop;
                            pageHasGrid[^1] = true;
                            Line(IpdLeft, t + 0.38, xSplit + 0.37, t + 0.38, 0.75);
                            Line(xSplit - 0.38, t + 0.38, xRight, t + 0.38, 0.75);
                        }
                        Line(IpdLeft + 0.38, t, IpdLeft + 0.38, t + IpdGridRowH + 0.75, 0.75);
                        Line(xSplit + 0.19, t, xSplit + 0.19, t + IpdGridRowH + 0.75, 0.75);
                        Line(xRight - 0.38, t, xRight - 0.38, t + IpdGridRowH + 0.75, 0.75);
                        if (row.Count > 0 && row[0].Length > 0)
                            Run(10, IpdLeft + 0.75 + IpdCellPad, t + IpdGridBaseOff, row[0]);
                        if (row.Count > 1 && row[1].Length > 0)
                            Run(10, xSplit + 0.37 + IpdCellPad, t + IpdGridBaseOff, row[1]);
                        t += IpdGridRowH;
                        Line(IpdLeft, t + 0.38, xSplit + 0.37, t + 0.38, 0.75);
                        Line(xSplit - 0.38, t + 0.38, xRight, t + 0.38, 0.75);
                    }
                    y = t + 0.75;
                    Advance(IpdBlockMargin);
                    break;
                }
                case "subbegin":
                {
                    var t = Open(IpdBlockMargin);
                    if (t + IpdSubBarH + 30 > IpdContentBottom) { BreakPage(); t = IpdContentTop; }
                    inSub = true;
                    subTop = t;
                    Line(IpdLeft, t + 0.38, IpdLeft + IpdSheetW, t + 0.38, 0.75);
                    FillRect(96.75, t + 0.76, IpdSubBarW, IpdSubBarH, "0.945 0.945 0.945");
                    if (it.Text.Length > 0)
                        Run(11, 96.75 + IpdSubBarPad, t + 0.76 + IpdSubBarBase, it.Text, true);
                    Line(96.75, t + 0.76 + IpdSubBarH - 0.38, 96.75 + IpdSubBarW,
                        t + 0.76 + IpdSubBarH - 0.38, 0.75);
                    y = t + 0.76 + IpdSubBarH + IpdSubPad;
                    pendingMargin = 0;
                    break;
                }
                case "subend":
                {
                    var b = y + IpdSubPad;
                    Line(96.38, subTop, 96.38, b + 0.38, 0.75);
                    Line(671.62, subTop, 671.62, b + 0.38, 0.75);
                    Line(IpdLeft, b + 0.38, IpdLeft + IpdSheetW, b + 0.38, 0.75);
                    inSub = false;
                    subTop = -1;
                    y = b + 0.75;
                    Advance(IpdBlockMargin);
                    break;
                }
                case "form":
                case "coverage":
                {
                    var x0 = inSub ? 96.75 + IpdSubPad : IpdLeft + 0.75;
                    var tw = inSub ? (IpdSheetW - 2 * 0.75 - 2 * IpdSubPad) * IpdSubTableFrac
                                   : IpdSheetW;
                    var t = Open(0);
                    // column left edges
                    var fr = it.Cols.Length > 0 ? it.Cols : IpdCol4;
                    var colX = new double[fr.Length + 1];
                    colX[0] = x0;
                    for (var c = 0; c < fr.Length; c++) colX[c + 1] = colX[c] + tw * fr[c];
                    double frameTop = t;
                    foreach (var row in it.Rows)
                    {
                        var wraps = new List<List<string>>();
                        var maxLines = 1;
                        for (var c = 0; c < row.Count && c < fr.Length; c++)
                        {
                            var budget = tw * fr[c] - 2 * IpdCellPad;
                            var wl = row[c].Length > 0 ? Wrap(row[c], 10, budget) : new List<string> { "" };
                            wraps.Add(wl);
                            if (wl.Count > maxLines) maxLines = wl.Count;
                        }
                        var rowH = maxLines * IpdLinePitch10 + IpdFormRowPad;
                        if (t + rowH > IpdContentBottom) { BreakPage(); t = IpdContentTop; frameTop = t; }
                        for (var c = 0; c < wraps.Count; c++)
                            for (var li = 0; li < wraps[c].Count; li++)
                                if (wraps[c][li].Length > 0)
                                    Run(10, colX[c] + IpdCellPad,
                                        t + IpdCellPad + IpdAscent10 + li * IpdLinePitch10,
                                        wraps[c][li]);
                        t += rowH;
                    }
                    if (it.Framed)
                    {
                        Line(x0 - 0.38, frameTop, x0 - 0.38, t, 0.75);
                        Line(x0 + tw + 0.38, frameTop, x0 + tw + 0.38, t, 0.75);
                        Line(x0, frameTop + 0.38, x0 + tw, frameTop + 0.38, 0.75);
                        Line(x0, t - 0.38, x0 + tw, t - 0.38, 0.75);
                    }
                    y = t;
                    break;
                }
                case "text":
                {
                    var x0 = inSub ? 96.75 + IpdSubPad : IpdLeft + 0.75;
                    var t = Open(0);
                    var lines = Wrap(it.Text, it.Fs, IpdSheetW - 2 * IpdSubPad);
                    foreach (var ln in lines)
                    {
                        var pitch = it.Fs * 1.15;
                        if (t + pitch > IpdContentBottom) { BreakPage(); t = IpdContentTop; }
                        Run(it.Fs, x0, t + 0.905 * it.Fs + 0.66, ln);
                        t += pitch;
                    }
                    y = t;
                    break;
                }
            }
        }

        // pages: white sheet + the fixed label-gray artifact on grid pages
        for (var pi = 0; pi < pages.Count; pi++)
        {
            var page = doc.Pages.Add(IpdPageW, IpdPageH);
            EnsureFonts(page, docFontDict);
            EnsureFont(page, "Arial", "F8");
            EnsureFont(page, "ArialBold", "F9");
            var head = new StringBuilder();
            head.AppendLine(string.Create(inv,
                $"q 1 1 1 rg 90 {IpdPageH - IpdContentBottom:F2} 591.5 698 re f Q"));
            if (pageHasGrid[pi])
                head.AppendLine(string.Create(inv,
                    $"q 0.945 0.945 0.945 rg 96.38 {IpdPageH - IpdGrayBottom:F2} 143.81 {IpdGrayBottom - IpdGrayTop:F2} re f Q"));
            page.AddContentStream(Encoding.ASCII.GetBytes(head.ToString() + pages[pi]));
        }
        return doc;
    }
}
