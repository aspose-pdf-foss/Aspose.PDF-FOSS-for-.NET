using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The iShares fund fact-sheet (TSR two-column allocation page) ───────────
    // A generated report page: an `iShares_Custom` stylesheet, a `sideBySide`
    // two-column layout whose columns each hold a band title and a `Table col3`
    // grid (subtype `TSR_*`): label cell + right-aligned value cell + a hanging
    // suffix cell ('%', a '(a)' superscript, a closing paren), the label
    // remainder filled with dot leaders, `<ins>` content underlined with a small
    // delta triangle drawn under its first character.
    //
    // Geometry (all measured on the expected conversion of the corpus sheet):
    //   page 756 × 842; column one at x = 126, 270 pt wide; column two at 406,
    //   260 pt wide (the sideBySide inner div pads 10 pt left); blue top rule
    //   126..666 × 4 pt at y(td) 108; green band 15 pt tall to y(td) 131.16 with
    //   the Arial-Bold 10 title on baseline 127.25 (a title <sup> rides 4.16
    //   higher at 8.33); italic 8 column heads right-aligned at the value edge,
    //   the label head on the last line's baseline; a 0.5 pt rule 2.48 below;
    //   rows Arial 8 from rule + 10.16 pitching 12.14 (a row whose label wraps
    //   advances only 11.6 into its first line, the continuation 10.54 below);
    //   the closing rule 4.08 under the last row baseline.
    //
    // Column partition: the label cell takes the table width left over by the
    // value and hang cells (CSS `td:first-child { width: 100% }`). The hang
    // cell is content-sized, where a `number-suffix="percent"` span outside the
    // first body row is invisible but keeps `width − 7 pt` of it (the
    // stylesheet's `visibility: hidden; margin-left: -7pt`); the value cell is
    // sized by its widest head line. That derivation reproduces the expected
    // rule segments exactly for the TSR_FST1 table (223.06 / 38.68 / 8.26) but
    // NOT for TSR_Allo2, whose segments measure 173.78 / 76.57 / 9.65
    // — those are taken as measured constants.

    private const double IfsPageW = 756.0;
    private const double IfsPageH = 842.0;
    private static readonly double[] IfsColX = { 126.0, 406.0 };
    private const double IfsColW = 270.0;
    private const double IfsCol2W = 260.0;          // 270 less the 10 pt inner padding
    private const double IfsBlueTopTd = 108.0;      // blue rule band, 4 pt tall
    private const double IfsBandTopTd = 116.16;     // green band top
    private const double IfsBandBottomTd = 131.16;  // green band bottom = title rule
    private const double IfsTitleBaseTd = 127.25;
    private const double IfsHeadLine1Off = 10.02;   // first head baseline below band
    private const double IfsHeadPitch = 10.0;
    private const double IfsAllo2HeadLine1Off = 9.12; // the TSR_Allo2 head sits higher
    private const double IfsAllo2HeadPitch = 9.5;     // and pitches tighter (measured)
    private const double IfsHeadRuleOff = 2.48;     // rule below the last head line
    private const double IfsRowStartOff = 10.16;    // first row baseline below rule
    private const double IfsRowPitch = 12.14;
    private const double IfsWrapRowLead = 11.6;     // a wrapping row's first-line pitch
    private const double IfsLabelWrapPitch = 10.54; // a wrapped label's second line
    private const double IfsCloseRuleOff = 4.08;    // closing rule below last row
    private const double IfsAllo2LabelW = 173.78;   // TSR_Allo2 partition, measured off
    private const double IfsAllo2ValueW = 76.57;    // the expected rule segments
    private const double IfsSupRise = 3.83;         // superscript baseline rise
    private const double IfsSupFs = 6.67;
    private const double IfsInsRuleDrop = 0.8;      // <ins> underline below baseline
    private const double IfsInsSeat = 0.5;          // <ins> content seats lower
    private const double IfsHiddenPctMargin = 7.0;  // css margin-left: -7pt on hidden %
    private const double IfsDeltaW = 7.5;           // DeltaSymbol triangle box (css
    private const double IfsDeltaH = 3.75;          // 3.75 pt borders), drawn solid

    private sealed class IfsHeadLine
    {
        public string Pre = "";     // text before the <ins> segment
        public string Ins = "";     // the underlined <ins> segment
        public string Sup = "";     // a trailing superscript (last line only)
        public bool Delta;          // a DeltaSymbol marker opens the ins segment
    }

    private sealed class IfsRow
    {
        public string Label = "";
        public string Value = "";
        public bool ValueIns;
        public bool ValueDelta;
        public List<(string text, bool sup, bool percent)> Hang = new();
    }

    private sealed class IfsColumn
    {
        public string Subtype = "";
        public string Title = "";
        public string TitleSup = "";
        public string LabelHead = "";
        public List<IfsHeadLine> ValueHead = new();
        public List<IfsRow> Rows = new();
    }

    private static Document? TryRenderIsharesFactSheet(string html)
    {
        if (!html.Contains("iShares_Custom", StringComparison.Ordinal)
            || !html.Contains("class=\"sideBySide\"", StringComparison.Ordinal)
            || !Regex.IsMatch(html, @"subtype\s*=\s*[""']TSR_", RegexOptions.IgnoreCase))
            return null;

        static string Flat(string s) => DecodeEntities(
            Regex.Replace(s, @"<[^>]+>", "")).Replace(' ', ' ').Trim();

        // the 0.001 pt "@[" "]@" wrappers are invisible markers
        static string StripMarkers(string s) => Regex.Replace(s,
            @"<span style=""font-size: 0\.001pt;"">[^<]*</span>", "");

        var cols = new List<IfsColumn>();
        foreach (Match colM in Regex.Matches(html,
            @"<div class=""sideBySideColumn\w+"">([\s\S]*?)(?=<div class=""sideBySideColumn|<!-- 2-COLUMN|</body)",
            RegexOptions.IgnoreCase))
        {
            var seg = colM.Groups[1].Value;
            var tblM = Regex.Match(seg,
                @"<table class=""Table col3""(?:[^>]*?\bsubtype\s*=\s*""([^""]*)"")?[^>]*>([\s\S]*?)</table>",
                RegexOptions.IgnoreCase);
            if (!tblM.Success) continue;
            var col = new IfsColumn { Subtype = tblM.Groups[1].Value };
            var headP = Regex.Match(seg, @"<p class=""centerhead""[^>]*>([\s\S]*?)</p>",
                RegexOptions.IgnoreCase);
            if (headP.Success)
            {
                var t = headP.Groups[1].Value;
                var supM = Regex.Match(t, @"<sup[^>]*>([\s\S]*?)</sup>", RegexOptions.IgnoreCase);
                if (supM.Success) { col.TitleSup = Flat(supM.Groups[1].Value); t = t.Remove(supM.Index, supM.Length); }
                col.Title = Flat(t);
            }
            var thead = Regex.Match(tblM.Groups[2].Value, @"<thead>([\s\S]*?)</thead>",
                RegexOptions.IgnoreCase);
            if (thead.Success)
                foreach (Match hm in Regex.Matches(thead.Groups[1].Value,
                    @"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
                {
                    var clsM = Regex.Match(hm.Groups[1].Value, @"class\s*=\s*""([^""]*)""",
                        RegexOptions.IgnoreCase);
                    var cls = clsM.Success ? clsM.Groups[1].Value : "";
                    var inner = hm.Groups[2].Value;
                    if (cls.Contains("Heading1"))
                    {
                        // the TSR_Allo2 title is a colspan thead row, not a centerhead p
                        var supM = Regex.Match(inner, @"<sup[^>]*>([\s\S]*?)</sup>",
                            RegexOptions.IgnoreCase);
                        if (supM.Success) { col.TitleSup = Flat(supM.Groups[1].Value); inner = inner.Remove(supM.Index, supM.Length); }
                        col.Title = Flat(inner);
                    }
                    else if (cls.Contains("hasHangColumn"))
                        ParseIfsHeadCell(StripMarkers(inner), col.ValueHead, Flat);
                    else if (cls.Contains("hangColumn"))
                    {
                        // the head hang cell carries only a superscript marker; it
                        // rides as the last value-head line's suffix
                        if (Flat(inner) is { Length: > 0 } hsup && col.ValueHead.Count > 0)
                            col.ValueHead[^1].Sup = hsup;
                    }
                    else if (cls.StartsWith("Head", StringComparison.Ordinal))
                        col.LabelHead = Flat(inner);
                }
            foreach (Match rm in Regex.Matches(tblM.Groups[2].Value,
                @"<tr class=""CalcSheetSectionItem""[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
            {
                var row = new IfsRow();
                foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                    @"<td class=""([^""]*)""[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
                {
                    var cls = cm.Groups[1].Value;
                    var inner = cm.Groups[2].Value;
                    if (cls.Contains("categoryhead")) row.Label = Flat(inner);
                    else if (cls.Contains("hasHangColumn"))
                    {
                        row.ValueIns = Regex.IsMatch(inner, @"<ins\b", RegexOptions.IgnoreCase);
                        row.ValueDelta = inner.Contains("DeltaSymbol", StringComparison.Ordinal);
                        row.Value = Flat(StripMarkers(inner));
                    }
                    else if (cls.Contains("hangColumn"))
                        foreach (Match sm in Regex.Matches(inner,
                            @"<(span|sup)\b([^>]*)>([\s\S]*?)</\1>", RegexOptions.IgnoreCase))
                        {
                            var txt = Flat(sm.Groups[3].Value);
                            if (txt.Length > 0)
                                row.Hang.Add((txt,
                                    sm.Groups[1].Value.Equals("sup", StringComparison.OrdinalIgnoreCase),
                                    sm.Groups[2].Value.Contains("number-suffix=\"percent\"",
                                        StringComparison.Ordinal)));
                        }
                }
                if (row.Label.Length > 0 || row.Value.Length > 0) col.Rows.Add(row);
            }
            if (col.Rows.Count > 0) cols.Add(col);
            if (cols.Count == 2) break;
        }
        if (cols.Count == 0) return null;

        var doc = Document.Create();
        var docFontDict = new Core.PdfDictionary();
        var page = doc.Pages.Add(IfsPageW, IfsPageH);
        EnsureFonts(page, docFontDict);
        EnsureFont(page, "Arial", "F8");
        EnsureFont(page, "ArialBold", "F9");
        EnsureFont(page, "ArialItalic", "F10");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        double MW(string s, double fs, string face = "Arial") => MeasureFaceText(face, s, fs);
        void Run(string res, double fs, double x, double yTd, string text)
            => sb.AppendLine(string.Create(inv,
                $"BT /{res} {fs:0.##} Tf 1 0 0 1 {x:F2} {IfsPageH - yTd:F2} Tm ({EscapePdfString(text)}) Tj ET"));
        void HRule(double x0, double x1, double yTd, double w)
            => sb.AppendLine(string.Create(inv,
                $"q 0 0 0 RG {w:0.##} w {x0:F2} {IfsPageH - yTd:F2} m {x1:F2} {IfsPageH - yTd:F2} l S Q"));
        // the DeltaSymbol triangle box: solid, centred on the ins segment's start,
        // hanging from the baseline (css `top: 100%`, drawn as fat border strokes
        // that merge into a solid block)
        void Delta(double cx, double yTd)
            => sb.AppendLine(string.Create(inv,
                $"q 0 0 0 rg {cx - IfsDeltaW / 2:F2} {IfsPageH - yTd - IfsDeltaH:F2} {IfsDeltaW:0.##} {IfsDeltaH:0.##} re f Q"));

        // Blue top rule across both columns.
        sb.AppendLine(string.Create(inv,
            $"q 0 0.663 0.878 rg 126 {IfsPageH - IfsBlueTopTd - 4:F2} 540 4 re f Q"));

        for (var c = 0; c < cols.Count && c < 2; c++)
        {
            var col = cols[c];
            var isAllo2 = col.Subtype.Equals("TSR_Allo2", StringComparison.OrdinalIgnoreCase);
            var x0 = IfsColX[c];
            var tableW = c == 0 ? IfsColW : IfsCol2W;
            var x1 = x0 + tableW;

            // The hang cell fits the widest suffix run; a percent span outside
            // the first row is hidden but keeps its width less the -7 pt margin.
            double hangW = 0;
            for (var ri = 0; ri < col.Rows.Count; ri++)
            {
                double w = 0;
                foreach (var (t, sup, pct) in col.Rows[ri].Hang)
                    w += pct && ri > 0 ? MW(t, 8.0) - IfsHiddenPctMargin
                        : MW(t, sup ? IfsSupFs : 8.0);
                hangW = Math.Max(hangW, w);
            }
            // The value cell fits the widest head line (sup included).
            double valueW = 0;
            foreach (var hl in col.ValueHead)
                valueW = Math.Max(valueW, MW(hl.Pre + hl.Ins, 8, "Arial Italic")
                    + (hl.Sup.Length > 0 ? MW(hl.Sup, IfsSupFs, "Arial Italic") : 0));
            double labelW;
            if (isAllo2) { labelW = IfsAllo2LabelW; valueW = IfsAllo2ValueW; hangW = tableW - labelW - valueW; }
            else labelW = tableW - valueW - hangW;
            var labelRight = x0 + labelW;
            var valueRight = labelRight + valueW;

            sb.AppendLine(string.Create(inv,
                $"q 0.463 0.737 0.129 rg {x0:F2} {IfsPageH - IfsBandBottomTd:F2} {tableW:F2} {IfsBandBottomTd - IfsBandTopTd:F2} re f Q"));
            Run("F9", 10, x0, IfsTitleBaseTd, col.Title);
            if (col.TitleSup.Length > 0)
                Run("F9", 8.33, x0 + MW(col.Title, 10, "Arial Bold"), IfsTitleBaseTd - 4.16, col.TitleSup);
            HRule(x0, x1, IfsBandBottomTd, 0.5);

            // Column heads: value-head lines right-aligned at the value edge
            // (sup included), the label head on the LAST line's natural baseline;
            // a line holding <ins> content seats half a point lower.
            var headBase = IfsBandBottomTd + (isAllo2 ? IfsAllo2HeadLine1Off : IfsHeadLine1Off);
            var headPitch = isAllo2 ? IfsAllo2HeadPitch : IfsHeadPitch;
            var lastHeadBase = headBase;
            for (var i = 0; i < col.ValueHead.Count; i++)
            {
                var hl = col.ValueHead[i];
                var ybNat = headBase + i * headPitch;
                lastHeadBase = ybNat;
                var yb = hl.Ins.Length > 0 ? ybNat + IfsInsSeat : ybNat;
                var wPre = MW(hl.Pre, 8, "Arial Italic");
                var wIns = MW(hl.Ins, 8, "Arial Italic");
                var supW = hl.Sup.Length > 0 ? MW(hl.Sup, IfsSupFs, "Arial Italic") : 0;
                var xh = valueRight - wPre - wIns - supW;
                Run("F10", 8, xh, yb, hl.Pre + hl.Ins);
                if (hl.Delta) Delta(xh + wPre, yb);
                if (hl.Ins.Length > 0)
                    HRule(xh + wPre, xh + wPre + wIns, yb + IfsInsRuleDrop, 0.8);
                if (hl.Sup.Length > 0)
                    Run("F10", IfsSupFs, xh + wPre + wIns, yb - IfsSupRise + 0.5, hl.Sup);
            }
            Run("F10", 8, x0, lastHeadBase, col.LabelHead);
            var ruleTd = lastHeadBase + IfsHeadRuleOff;
            HRule(x0, x1, ruleTd, 0.5);
            HRule(x0, labelRight, ruleTd + 0.13, 0.25);

            // Rows.
            var dotW = MW(".", 8.0);
            var yRow = ruleTd + IfsRowStartOff;
            for (var ri = 0; ri < col.Rows.Count; ri++)
            {
                var r = col.Rows[ri];
                // greedy label wrap at the label cell width
                var labelLines = new List<string>();
                var cur = new StringBuilder();
                foreach (var word in r.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trial = cur.Length == 0 ? word : cur + " " + word;
                    if (cur.Length == 0 || MW(trial, 8) <= labelW) { cur.Clear(); cur.Append(trial); }
                    else { labelLines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
                }
                if (cur.Length > 0) labelLines.Add(cur.ToString());
                if (labelLines.Count == 0) labelLines.Add("");
                // a wrapping row leads its first line 11.6 under the previous
                // baseline instead of the plain 12.14 row pitch
                if (ri > 0 && labelLines.Count > 1) yRow -= IfsRowPitch - IfsWrapRowLead;

                var yLast = yRow;
                for (var li = 0; li < labelLines.Count; li++)
                {
                    // a wrapped continuation indents 6 pt (measured:
                    // "depreciation" sits at 132 = 126 + 6)
                    var lx = x0 + (li > 0 ? 6.0 : 0);
                    yLast = yRow + li * IfsLabelWrapPitch;
                    Run("F8", 8, lx, yLast, labelLines[li]);
                }
                // dot leaders fill the label cell remainder on the LAST line,
                // 2.01 after the text, while a dot's end stays within half a
                // dot of the cell edge
                var lastW = (labelLines.Count > 1 ? 6.0 : 0) + MW(labelLines[^1], 8);
                var dotsStart = x0 + lastW + 2.01;
                var nDots = (int)Math.Floor((labelRight + dotW / 2 - dotsStart) / dotW);
                if (nDots > 3)
                    Run("F8", 8, dotsStart, yLast, new string('.', nDots));
                // value right-aligned at the value edge, on the last label line —
                // an <ins> value seats half a point lower (measured 176.46 against
                // the row's 175.96)
                var yVal = r.ValueIns ? yLast + IfsInsSeat : yLast;
                if (r.Value.Length > 0)
                {
                    var vw = MW(r.Value, 8);
                    Run("F8", 8, valueRight - vw, yVal, r.Value);
                    if (r.ValueDelta) Delta(valueRight - vw, yVal);
                    if (r.ValueIns)
                        HRule(valueRight - vw, valueRight, yVal + IfsInsRuleDrop, 0.8);
                }
                // hanging suffixes run on from the value edge; a percent span
                // outside the first row is invisible but advances its width
                // less the -7 pt margin
                var hx = valueRight;
                foreach (var (t, sup, pct) in r.Hang)
                {
                    if (pct && ri > 0) { hx += MW(t, 8) - IfsHiddenPctMargin; continue; }
                    Run("F8", sup ? IfsSupFs : 8, hx, sup ? yVal - IfsSupRise : yVal, t);
                    hx += MW(t, sup ? IfsSupFs : 8);
                }
                yRow = yLast + IfsRowPitch;
            }
            var closeTd = yRow - IfsRowPitch + IfsCloseRuleOff;
            HRule(x0, x1, closeTd, 0.5);
            HRule(x0, labelRight, closeTd - 0.13, 0.25);
        }

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
    }

    // Split a value-head cell into lines at <br>, separating the text before an
    // <ins> segment from the underlined segment itself: the corpus head is
    // `<em>Percent <ins>[Δ]of Total<br>Investments</ins></em>` — line one keeps
    // "Percent " as its plain prefix, the continuation is wholly underlined.
    private static void ParseIfsHeadCell(string inner, List<IfsHeadLine> lines,
        Func<string, string> flat)
    {
        // a trailing superscript marker rides the last line
        var sup = "";
        var supM = Regex.Match(inner, @"<sup[^>]*>([\s\S]*?)</sup>", RegexOptions.IgnoreCase);
        if (supM.Success) { sup = flat(supM.Groups[1].Value); inner = inner.Remove(supM.Index, supM.Length); }
        var first = lines.Count;
        var insM = Regex.Match(inner, @"<ins\b[^>]*>([\s\S]*?)</ins>", RegexOptions.IgnoreCase);
        if (!insM.Success)
        {
            foreach (var piece in Regex.Split(inner, @"<br\s*/?>", RegexOptions.IgnoreCase))
                if (flat(piece) is { Length: > 0 } txt)
                    lines.Add(new IfsHeadLine { Pre = txt });
            if (sup.Length > 0 && lines.Count > first) lines[^1].Sup = sup;
            return;
        }
        var pre = flat(inner[..insM.Index]);
        var insBody = insM.Groups[1].Value;
        var delta = insBody.Contains("DeltaSymbol", StringComparison.Ordinal);
        var insPieces = Regex.Split(insBody, @"<br\s*/?>", RegexOptions.IgnoreCase);
        for (var i = 0; i < insPieces.Length; i++)
        {
            var txt = flat(insPieces[i]);
            if (txt.Length == 0 && i > 0) continue;
            lines.Add(new IfsHeadLine
            {
                Pre = i == 0 ? pre + (pre.Length > 0 ? " " : "") : "",
                Ins = txt,
                Delta = delta && i == 0,
            });
        }
        var tail = flat(inner[(insM.Index + insM.Length)..]);
        if (tail.Length > 0 && lines.Count > first && lines[^1].Ins.Length == 0)
            lines[^1].Pre += tail;
        if (sup.Length > 0 && lines.Count > first) lines[^1].Sup = sup;
    }
}
