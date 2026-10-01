using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Audit letter helpers: baseline drop, runs, measurement, fills, page breaks, the editor-paragraph parser, paragraph and label emitters and the evidence grid.
    private static double Drop(AuditLetterState al, double fs, double box) => (box - fs * al.wm.sum) / 2 + fs * al.wm.asc;

    private static void EmitRun(AuditLetterState al, string text, double fs, string variant, double x, double baseline, Color col)
    {
        if (text.Length == 0) return;
        var faceName = "Arial" + (variant.Length > 0 ? " " + variant : "");
        var (rn, hex) = Text.Type0FontEmbedder.Embed(
            (al.page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                ?? throw new InvalidOperationException(),
            PosFace(faceName).ttf ?? PosFace("Arial").ttf!,
            "Arial" + variant.Replace(" ", ""), text, stripSpacesInBaseFont: true);
        al.sb.AppendLine(Compat.Format(al.inv,
            $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
            $"BT /{rn} {fs:0.##} Tf 1 0 0 1 {x:F2} {al.pageHeight - baseline:F2} Tm " +
            $"<{Compat.ToHexString(hex)}> Tj ET Q"));
    }

    private static double MeasureAr(AuditLetterState al, string t, string variant, double fs)
        => MeasureFaceText("Arial" + (variant.Length > 0 ? " " + variant : ""), t, fs);

    private static void FillRect(AuditLetterState al, double x, double yTop, double w, double h, Color col)
        => al.sb.AppendLine(Compat.Format(al.inv,
            $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
                $"{x:F2} {al.pageHeight - yTop - h:F2} {w:F2} {h:F2} re f Q"));

    private static void NewPage(AuditLetterState al)
    {
        // close the rail on the finished page
        if (!double.IsNaN(al.railFrom))
            al.sb.AppendLine(Compat.Format(al.inv,
                $"q {al.railGray.R / 255.0:0.###} {al.railGray.G / 255.0:0.###} {al.railGray.B / 255.0:0.###} RG 1.5 w " +
                $"49.5 {al.pageHeight - al.railFrom:F2} m 49.5 {al.marginBottom:F2} l S Q"));
        al.page = al.doc.Pages.Add(al.pageWidth, al.pageHeight);
        EnsureFonts(al.page, al.docFontDict);
        al.sb = new StringBuilder();
        al.streams.Add((al.page, al.sb));
        al.y = al.marginTop;
        al.railFrom = al.marginTop;
    }

    private static List<List<(string t, bool it, Color col)>> ParseEditorParas(AuditLetterState al, string tdInner)
    {
        var paras = new List<List<(string, bool, Color)>>();
        List<(string, bool, Color)>? cur = null;
        var curAdded = false;                     // div paras add LAZILY on first ink
        var langDepth = 0; var langStack = new Stack<bool>();
        var colStack = new Stack<Color>();
        var abbrStack = new Stack<bool>(); var abbrDepth = 0;
        foreach (Match m in al.pRunRx.Matches(tdInner))
        {
            if (m.Groups[5].Success)
            {
                if (cur is null) continue;
                var txt = DecodeEntities(m.Groups[5].Value);
                if (txt.Length == 0) continue;
                // inter-tag whitespace never OPENS a lazy div paragraph —
                // but an &nbsp; (U+00A0, surviving the space trim) does
                if (!curAdded && txt.Trim(' ').Length == 0) continue;
                var col = colStack.Count > 0 ? colStack.Peek()
                    : abbrDepth > 0 ? al.inkAbbr : al.inkEditor;
                if (!curAdded) { paras.Add(cur); curAdded = true; }
                cur.Add((txt, langDepth > 0, col));
                continue;
            }
            var tag = m.Groups[2].Value.ToLowerInvariant();
            var closeT = m.Groups[1].Value == "/";
            var selfC = m.Groups[4].Value == "/";
            if (tag == "p")
            {
                if (!closeT)
                { cur = new List<(string, bool, Color)>(); paras.Add(cur); curAdded = true; }
                else cur = null;
                continue;
            }
            // a content div carries its own line when it holds direct text
            // (the manual-editor '&nbsp;' and 'test' blocks)
            if (tag == "div" && m.Groups[3].Value.Contains("editor-content-readonly",
                    StringComparison.Ordinal))
            {
                if (!closeT) { cur = new List<(string, bool, Color)>(); curAdded = false; }
                continue;
            }
            if (cur is null || selfC) continue;
            if (tag == "span")
            {
                if (!closeT)
                {
                    var hasLang = m.Groups[3].Value.Contains("lang=", StringComparison.OrdinalIgnoreCase);
                    var isAbbr = m.Groups[3].Value.Contains("abbrSpan", StringComparison.OrdinalIgnoreCase);
                    langStack.Push(hasLang); if (hasLang) langDepth++;
                    abbrStack.Push(isAbbr); if (isAbbr) abbrDepth++;
                }
                else
                {
                    if (langStack.Count > 0 && langStack.Pop()) langDepth--;
                    if (abbrStack.Count > 0 && abbrStack.Pop()) abbrDepth--;
                }
            }
            else if (tag == "font")
            {
                if (!closeT)
                {
                    var fcM = Regex.Match(m.Groups[3].Value, @"color\s*=\s*[""']?(#?\w+)");
                    colStack.Push(fcM.Success && ParseCssColor(fcM.Groups[1].Value) is { } fc
                        ? fc : al.inkEditor);
                }
                else if (colStack.Count > 0) colStack.Pop();
            }
        }
        return paras;
    }

    // wrapped emission of one paragraph's runs at the editor 12 pt line
    private static void EmitPara(AuditLetterState al, List<(string t, bool it, Color col)> runs)
    {
        const double fs = 12.0; const double lh = 13.5;
        var flat = new List<(string word, bool it, Color col)>();
        foreach (var (t, it, col) in runs)
            foreach (var piece in Regex.Split(t, @"(?<= )"))
                if (piece.Length > 0) flat.Add((piece, it, col));
        if (flat.Count == 0)
        {
            if (al.y + lh > al.pageHeight - al.marginBottom) NewPage(al);
            EmitRun(al, " ", fs, "", ArTdXPt, al.y + Drop(al, fs, lh), al.inkEditor);
            al.y += lh;
            return;
        }
        var lineRuns = new List<(string t, bool it, Color col)>();
        double lineW = 0;
        void FlushLine()
        {
            if (lineRuns.Count == 0) return;
            if (al.y + 13.5 > al.pageHeight - al.marginBottom) NewPage(al);
            var x = ArTdXPt;
            foreach (var (t, it, col) in lineRuns)
            {
                EmitRun(al, t, fs, it ? "Italic" : "", x, al.y + Drop(al, fs, lh), col);
                // Arial's italic advances match the upright — measure the
                // upright face so wrap and seats stay off the real widths
                x += MeasureAr(al, t, "", fs);
            }
            al.y += lh;
            lineRuns.Clear(); lineW = 0;
        }
        foreach (var (word, it, col) in flat)
        {
            var wWidth = MeasureAr(al, word, "", fs);
            // a word's TRAILING space hangs past the wrap edge — the fit
            // check measures the trimmed word, the advance keeps the space
            var wFit = MeasureAr(al, word.TrimEnd(' '), "", fs);
            if (lineW + wFit > al.contentRight - ArTdXPt && lineRuns.Count > 0
                && word.Trim().Length > 0)
                FlushLine();
            if (lineRuns.Count > 0 && lineRuns[^1].it == it
                && lineRuns[^1].col.Equals(col))
            {
                var last = lineRuns[^1];
                lineRuns[^1] = (last.t + word, it, col);
            }
            else lineRuns.Add((word, it, col));
            lineW += wWidth;
        }
        FlushLine();
    }

    // th labels wrap in their 50 pt column at the 10.5 pt label pitch
    private static void EmitLabel(AuditLetterState al, string label, double atY)
    {
        var savedY = al.y;
        al.y = atY;
        foreach (var ln in MeasuredWordWrap(label, ArThColWPt, "Arial Bold", 9.0))
        {
            EmitRun(al, ln, 9.0, "Bold", ArThXPt, al.y + Drop(al, 9.0, 10.5), al.inkBody);
            al.y += 10.5;
        }
        al.y = savedY;
    }

    // ── the Tespitler evidence grid: measured columns (the sheet's mixed
    // %-and-min-content solve, overflowing the card to the right) ──
    private static void EmitEvidenceGrid(AuditLetterState al, string tdInner)
    {
        double[] edges = { 123.8, 171.1, 219.4, 258.2, 310.7, 365.2, 413.7,
                           443.2, 497.2, 526.7, 706.6 };
        // the LAST column's TEXT wraps at its 15% share (~70 pt) even
        // though its band runs wider (measured: 'Giderilme / Durumu')
        double TextW(int i) => (i == edges.Length - 2 ? 597.2 : edges[i + 1]) - edges[i] - 13.5;
        // greedy wrap that also breaks AFTER hyphens (the sheet splits
        // '2016-01-11' at the hyphen inside its narrow date column)
        string[] GridWrap(string text, double availW, string variant)
        {
            var toks = new List<string>();
            foreach (var wpart in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var start = 0;
                for (var k = 0; k < wpart.Length - 1; k++)
                    if (wpart[k] == '-') { toks.Add(wpart[start..(k + 1)]); start = k + 1; }
                if (start < wpart.Length) toks.Add(wpart[start..]);
            }
            var outLines = new List<string>(); var curLn = "";
            foreach (var tok in toks)
            {
                var cand = curLn.Length == 0 || curLn.EndsWith('-') ? curLn + tok
                    : curLn + " " + tok;
                // the lira glyph measures as a plain cap in the sheet's
                // solve (our width table lacks it)
                if (curLn.Length > 0
                    && MeasureAr(al, cand.Replace('₺', 'T'), variant, 9.0) > availW)
                { outLines.Add(curLn); curLn = tok; }
                else curLn = cand;
            }
            if (curLn.Length > 0) outLines.Add(curLn);
            return outLines.Count > 0 ? outLines.ToArray() : new[] { "" };
        }
        var ths = new List<string>();
        foreach (Match thM in Regex.Matches(tdInner, @"<th[^>]*>(.*?)</th>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
            ths.Add(CollapseWs(DecodeEntities(Regex.Replace(thM.Groups[1].Value, "<[^>]+>", " "))).Trim());
        var rows = new List<List<string>>();
        var bodyIdx = tdInner.IndexOf("<tbody", StringComparison.OrdinalIgnoreCase);
        if (bodyIdx >= 0)
            foreach (Match trM in Regex.Matches(tdInner[bodyIdx..], @"<tr\b[^>]*>(.*?)</tr>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var cells = new List<string>();
                foreach (Match tdM in Regex.Matches(trM.Groups[1].Value, @"<td[^>]*>(.*?)</td>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    cells.Add(CollapseWs(DecodeEntities(
                        Regex.Replace(tdM.Groups[1].Value, "<[^>]+>", " "))).Trim());
                if (cells.Count > 0) rows.Add(cells);
            }

        var gridTop = al.y;
        var maxHeadLines = 1;
        for (var i = 0; i < ths.Count && i < edges.Length - 1; i++)
        {
            var lines = GridWrap(ths[i], TextW(i), "Bold");
            var yH = gridTop;
            foreach (var ln in lines)
            {
                if (yH + 10.5 > al.pageHeight - al.marginBottom) break;
                EmitRun(al, ln, 9.0, "Bold", edges[i] + 7.5, yH + Drop(al, 9.0, 10.5), al.inkBody);
                yH += 10.5;
            }
            maxHeadLines = Math.Max(maxHeadLines, lines.Length);
        }
        al.y = gridTop + maxHeadLines * 10.5 + 3.0;
        // the header underline, per column
        for (var i = 0; i + 1 < edges.Length; i++)
            al.sb.AppendLine(Compat.Format(al.inv,
                $"q 0.812 0.812 0.812 RG 0.75 w {edges[i]:F2} {al.pageHeight - al.y:F2} m " +
                $"{edges[i + 1]:F2} {al.pageHeight - al.y:F2} l S Q"));
        al.y += 3.0;
        foreach (var row in rows)
        {
            var rowTop2 = al.y;
            double maxLines = 1;
            for (var i = 0; i < row.Count && i < edges.Length - 1; i++)
            {
                var lines = GridWrap(row[i], TextW(i), "");
                var yV = rowTop2;
                foreach (var ln in lines)
                {
                    EmitRun(al, ln, 9.0, "", edges[i] + 7.5, yV + Drop(al, 9.0, 10.5), al.inkBody);
                    yV += 10.5;
                }
                maxLines = Math.Max(maxLines, lines.Length);
            }
            al.y = rowTop2 + maxLines * 10.5 + 3.0;
            al.sb.AppendLine(Compat.Format(al.inv,
                $"q {al.railGray.R / 255.0:0.###} {al.railGray.G / 255.0:0.###} {al.railGray.B / 255.0:0.###} RG 0.75 w " +
                $"{edges[0]:F2} {al.pageHeight - al.y:F2} m {edges[^1]:F2} {al.pageHeight - al.y:F2} l S Q"));
            al.y += 3.0;
        }
        // measured: the grid advances a small band past its last row rule
        // before the next label row opens
        al.y += 5.5;
    }

    /// <summary>One row of the report content area: its label cell and the editor paragraphs or evidence grid of its value cell, with page breaks as the rows fill the sheet.</summary>
    private static bool LayoutAuditLetterRow(AuditLetterState al, Match rowM)
    {
        var label = CollapseWs(DecodeEntities(rowM.Groups[1].Value)).Trim();
        var tdInner = BalancedInner(al.contentArea, rowM.Index + rowM.Length, "td") ?? "";
        if (al.y + 13.5 > al.pageHeight - al.marginBottom) NewPage(al);
        var rowTop = al.y;
        var rowPageCount = al.streams.Count;
        EmitLabel(al, label, rowTop);
        if (tdInner.Contains("gt-editor-content", StringComparison.Ordinal))
        {
            EmitRun(al, ":", 9.0, "", ArDotXPt, rowTop + Drop(al, 9.0, 10.5), al.inkBody);
            al.y = rowTop;                           // paragraphs seat on the row top
            foreach (var para in ParseEditorParas(al, tdInner)) EmitPara(al, para);
        }
        else if (tdInner.Contains("class=\"dtr\"", StringComparison.Ordinal))
        {
            EmitRun(al, ":", 9.0, "", ArDotXPt, rowTop + Drop(al, 9.0, 10.5), al.inkBody);
            al.y = rowTop + 7.5;
            EmitEvidenceGrid(al, tdInner);
        }
        else
        {
            // a plain value row: ': value' at the label size; a badge span
            // draws its pill; wrapped continuation re-seats at the td x
            var badgeM = Regex.Match(tdInner,
                @"<span class=""badge-grey[^""]*""[^>]*>([^<]*)</span>", RegexOptions.IgnoreCase);
            var valTxt = CollapseWs(DecodeEntities(
                Regex.Replace(Regex.Replace(tdInner,
                    @"<span class=""badge-grey.*?</span>", "", RegexOptions.Singleline),
                    "<[^>]+>", " "))).Trim();
            var linkVal = Regex.IsMatch(tdInner, @"<a\b", RegexOptions.IgnoreCase);
            var first = ": " + valTxt;
            var avail = al.contentRight - ArDotXPt;
            var lines = MeasuredWordWrap(first, avail, "Arial", 9.0);
            var yV = rowTop;
            for (var li = 0; li < lines.Length; li++)
            {
                EmitRun(al, lines[li], 9.0, "", li == 0 ? ArDotXPt : ArTdXPt,
                    yV + Drop(al, 9.0, 10.5), linkVal ? al.inkCode : al.inkBody);
                yV += 10.5;
            }
            if (badgeM.Success)
            {
                var bTxt = CollapseWs(DecodeEntities(badgeM.Groups[1].Value)).Trim();
                var bX = ArDotXPt + MeasureAr(al, ": " + valTxt, "", 9.0) + 7.5;
                var bW = MeasureAr(al, bTxt, "", ArBadgeFsPt) + 5.0;
                FillRect(al, bX, rowTop + 0.9, bW, 14.3,
                    ParseCssColor("#617778") ?? Color.FromArgb(97, 119, 120));
                EmitRun(al, bTxt, ArBadgeFsPt, "", bX + 2.2, rowTop + 10.9,
                    Color.FromArgb(255, 255, 255));
            }
            al.y = yV;
            // the badge pill paces its row past the single text line
            if (badgeM.Success) al.y = Math.Max(al.y, rowTop + 14.3);
        }
        // the th block may run deeper than the value — but only while the
        // row is still on the page it OPENED on (a split row's clamp would
        // re-apply the previous page's extent)
        if (al.streams.Count == rowPageCount)
        {
            var thLines = MeasuredWordWrap(label, ArThColWPt, "Arial Bold", 9.0).Length;
            al.y = Math.Max(al.y, rowTop + thLines * 10.5);
        }
        al.y += 15.72;                               // 20 px padding-bottom (measured
                                                  // block pitch: 137.22 per row)
        return true;
    }
}
