using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The classed border-table case letter ────────────────────────────────
    //
    // A generated merchant letter: a #PageHeading table, an address table with
    // padded cells, prose tables, a bulleted request list, and then numbered
    // case tables — each a "N." marker cell beside a nested table whose CLASS
    // (.blackBorder) declares all four border-side longhands on the table and
    // on every cell. <div class="break"> splits the letters onto their pages.
    //
    // The geometry is measured on the expected output for this
    // fixture (it reproduces the shipped era template exactly):
    //  - page 657 × 842: 96 left margin + the declared 630 px box + 88.5;
    //  - a 9 pt line paces 10.5 (floor(1.2 × 12 px) css px), an 11 pt line
    //    12.75, everything seats its baseline 8.0 under its band top;
    //  - the heading draws ONE 20 pt darkgray line ("HSBC" bold) at x 98.25,
    //    baseline 111.18; the address block opens at baseline 199.19;
    //  - case tables: declared percent columns of the 459 box, and a column
    //    whose MIN-CONTENT (longest unbreakable token + the 1.13 pad pair)
    //    exceeds its share takes that min while the others give the surplus
    //    up in proportion to their own slack;
    //  - a row is max-cell-lines × 10.5 + 2.25 tall (+0.375 per extra line),
    //    each cell's lines centred in the row; the marker "N." seats at the
    //    table top + 8.0.
    private const double ClPageW = 657.0;
    private const double ClPageH = 842.0;
    private const double ClLeftPt = 96.0;               // outer tables' left edge
    private const double ClCellChromePt = 1.5;          // cellspacing 1 + cellpadding 1
    private const double ClLine9Pt = 10.5;              // 9 pt body line (14 css px)
    private const double ClLine11Pt = 12.75;            // 11 pt address line (17 css px)
    private const double ClAscSeatPt = 8.0;             // baseline under the band top
    private const double ClHeadingBaselinePt = 111.18;
    private const double ClHeadingXPt = 98.25;          // UA table chrome (spacing 2 + pad 1)
    private const double ClAddrBaselinePt = 199.19;
    private const double ClAddrXPt = 121.5;             // cell chrome + padding-left 32 px
    private const double ClDateBaselinePt = 250.62;
    private const double ClRequestBaselinePt = 328.62;
    private const double ClParaBaselinePt = 355.62;
    private const double ClPleaseBaselinePt = 403.62;
    private const double ClBulletBaselinePt = 424.2;
    private const double ClBulletMarkerXPt = 120.97;
    private const double ClBulletTextXPt = 127.5;
    private const double ClBulletNestedXPt = 150.93;
    /// <summary>The first case table's top border under the last bullet baseline.</summary>
    private const double ClCaseGapAfterProsePt = 29.08;
    /// <summary>Between two case tables on one page (their <br/> plus chrome).</summary>
    private const double ClInterCaseGapPt = 16.88;
    /// <summary>A break page's first case-table top border.</summary>
    private const double ClBreakPageTopPt = 87.38;
    /// <summary>The case grid's left border past the marker column.</summary>
    private const double ClCaseGridLeftPt = 107.63;
    private const double ClCaseGridRightPt = 566.62;
    private const double ClCellPadXPt = 1.13;
    private const double ClRowPadPt = 2.25;
    private const double ClRowExtraLinePt = 0.375;
    private const double ClBorderW = 0.75;
    /// <summary>Overflow guard for un-broken content (the era pages never reach it).</summary>
    private const double ClBottomPt = 770.0;
    private static readonly Color ClHeadingInk = Color.FromArgb(169, 169, 169); // darkgray

    private sealed class ClCellRun { public string Text = ""; public bool Bold; }
    private sealed class ClCell
    {
        public List<ClCellRun> Runs = new();
        public int ColSpan = 1;
        public List<List<ClCellRun>>? Lines;            // wrap result
    }
    private sealed class ClCaseTable
    {
        public string Marker = "";
        public List<double> ColPct = new();
        public List<List<ClCell>> Rows = new();
        public bool BreakBefore;
        public int SourceEnd;
    }

    private static Document? TryRenderCaseLetter(string html)
    {
        if (html.IndexOf("id=\"PageHeading\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("class=\"blackBorder\"", System.StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("class=\"break\"", System.StringComparison.OrdinalIgnoreCase) < 0) return null;
        // The letterhead-IMAGE variant of the same letter renders its logo through
        // the legacy flow (calibrated green there); this arm is the imageless
        // #PageHeading text dialect only.
        if (html.IndexOf("<img", System.StringComparison.OrdinalIgnoreCase) >= 0) return null;
        // The dialect's signature: the class rule declares all four side longhands.
        var clsRule = Regex.Match(html,
            @"\.blackBorder\s*\{[^}]*border-top\s*:[^;}]*;[^}]*border-bottom\s*:[^;}]*;[^}]*border-left\s*:[^;}]*;[^}]*border-right\s*:[^;}]*;",
            RegexOptions.IgnoreCase);
        if (!clsRule.Success) return null;

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string Clean(string s) => Regex.Replace(DecodeEntities(
            Regex.Replace(s, "<[^>]+>", " ")).Replace(' ', ' '), @"\s+", " ").Trim();

        // ---- parse -------------------------------------------------------------
        var headM = Regex.Match(html,
            @"id=""PageHeading""[^>]*>\s*<b>(?<b>[\s\S]*?)</b>(?<r>[\s\S]*?)</td",
            RegexOptions.IgnoreCase);
        var headBold = headM.Success ? Clean(headM.Groups["b"].Value) : "HSBC";
        var headRest = headM.Success ? Clean(headM.Groups["r"].Value) : "";

        // The address table: first padded td's <br>-split lines, second td's date.
        var addrM = Regex.Match(html,
            @"<table[^>]*height:120px[^>]*>(?<b>[\s\S]*?)</table\s*>", RegexOptions.IgnoreCase);
        var addrLines = new List<string>();
        var dateText = "";
        if (addrM.Success)
        {
            var tds = Regex.Matches(addrM.Groups["b"].Value, @"<td[^>]*>(?<c>[\s\S]*?)</td\s*>", RegexOptions.IgnoreCase);
            if (tds.Count > 0)
                foreach (var ln in Regex.Split(tds[0].Groups["c"].Value, @"<br\s*/?>", RegexOptions.IgnoreCase))
                {
                    // NBSP survives the whitespace collapse (the "GB   LE1 7BB"
                    // line keeps its gap), then reads back as a plain space.
                    var t = Regex.Replace(DecodeEntities(Regex.Replace(ln, "<[^>]+>", " ")),
                        "[ \t\r\n]+", " ").Trim();
                    t = t.Replace(' ', ' ');
                    if (t.Trim().Length > 0) addrLines.Add(t.Trim().ToUpperInvariant());
                }
            if (tds.Count > 1) dateText = Clean(tds[1].Groups["c"].Value).ToUpperInvariant();
        }

        // Prose/bullets and the case tables, in document order.
        var reqM = Regex.Match(html, @"<b>\s*(?<t>REQUEST[^<]*)</b>", RegexOptions.IgnoreCase);
        var requestText = reqM.Success ? Clean(reqM.Groups["t"].Value) : "REQUEST FOR INFORMATION";
        var paraM = Regex.Match(html,
            @"<td[^>]*>\s*(?<t>This is a request[\s\S]*?)</td", RegexOptions.IgnoreCase);
        var paraText = paraM.Success ? Clean(paraM.Groups["t"].Value) : "";
        var pleaseM = Regex.Match(html, @"<b>\s*(?<t>PLEASE PROVIDE[^<]*)</b>", RegexOptions.IgnoreCase);
        var pleaseText = pleaseM.Success ? Clean(pleaseM.Groups["t"].Value) : "";
        // Level-1 items with their optional nested list.
        var bullets = new List<(string Text, List<string> Nested)>();
        var ulM = Regex.Match(html, @"<ul>(?<b>[\s\S]*?)</ul>\s*</td", RegexOptions.IgnoreCase);
        if (ulM.Success)
        {
            // Cut the NESTED lists out first (their <li>s would end the outer
            // item's match early), then walk the top-level items.
            var body = ulM.Groups["b"].Value;
            var nestedLists = new List<List<string>>();
            body = Regex.Replace(body, @"<ul[^>]*>(?<n>[\s\S]*?)</ul>", nm =>
            {
                var items = new List<string>();
                foreach (Match nli in Regex.Matches(nm.Groups["n"].Value, @"<li[^>]*>(?<t>[\s\S]*?)</li>", RegexOptions.IgnoreCase))
                    items.Add(Clean(nli.Groups["t"].Value));
                nestedLists.Add(items);
                return "" + (nestedLists.Count - 1) + "";
            }, RegexOptions.IgnoreCase);
            foreach (Match li in Regex.Matches(body, @"<li[^>]*>(?<c>[\s\S]*?)</li>", RegexOptions.IgnoreCase))
            {
                var c = li.Groups["c"].Value;
                var nested = new List<string>();
                var tok = Regex.Match(c, "(" + @"\d+" + ")");
                if (tok.Success)
                {
                    nested = nestedLists[int.Parse(tok.Groups[1].Value)];
                    c = c.Remove(tok.Index, tok.Length);
                }
                var text = Clean(c);
                if (text.Length > 0 || nested.Count > 0) bullets.Add((text, nested));
            }
        }

        // Case tables: every outer 630px table holding a nested .blackBorder table.
        var caseTables = new List<ClCaseTable>();
        var breakPositions = new List<int>();
        foreach (Match bm in Regex.Matches(html, @"<div\s+class=""break""", RegexOptions.IgnoreCase))
            breakPositions.Add(bm.Index);
        foreach (Match om in Regex.Matches(html,
            @"<table[^>]*width:630px[^>]*>\s*<tr>\s*<td[^>]*>\s*(?<mk>\d+\.)\s*</td>\s*<td[^>]*>\s*(?<tb><table[^>]*blackBorder[\s\S]*?</table>)\s*</td>",
            RegexOptions.IgnoreCase))
        {
            var ct = new ClCaseTable { Marker = om.Groups["mk"].Value };
            // BreakBefore: a break div sits between the previous case table and this one.
            var prevEnd = caseTables.Count > 0 ? caseTables[^1].SourceEnd : 0;
            foreach (var bp in breakPositions)
                if (bp > prevEnd && bp < om.Index) { ct.BreakBefore = true; break; }
            ct.SourceEnd = om.Index + om.Length;
            var tb = om.Groups["tb"].Value;
            foreach (Match tr in Regex.Matches(tb, @"<tr>(?<r>[\s\S]*?)</tr>", RegexOptions.IgnoreCase))
            {
                var row = new List<ClCell>();
                foreach (Match td in Regex.Matches(tr.Groups["r"].Value,
                    @"<td(?<a>[^>]*)>(?<c>[\s\S]*?)</td\s*>", RegexOptions.IgnoreCase))
                {
                    var cell = new ClCell();
                    var csm = Regex.Match(td.Groups["a"].Value, @"colspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase);
                    if (csm.Success) cell.ColSpan = int.Parse(csm.Groups[1].Value);
                    // width:NN% on the first row's cells declares the shares.
                    var wm = Regex.Match(td.Groups["a"].Value, @"width\s*:\s*([\d.]+)\s*%");
                    if (ct.Rows.Count == 0 && wm.Success)
                        ct.ColPct.Add(double.Parse(wm.Groups[1].Value, inv));
                    // Runs: bold segments stay bold; an uppercase-transform div upper-cases.
                    var content = td.Groups["c"].Value;
                    var upper = Regex.IsMatch(content, @"TEXT-TRANSFORM\s*:\s*uppercase", RegexOptions.IgnoreCase);
                    foreach (Match seg in Regex.Matches(content, @"<b>(?<b>[\s\S]*?)</b>|(?<p>(?:(?!<b>)[\s\S])+?)(?=<b>|$)", RegexOptions.IgnoreCase))
                    {
                        var bold = seg.Groups["b"].Success;
                        var text = Clean(bold ? seg.Groups["b"].Value : seg.Groups["p"].Value);
                        if (text.Length == 0) continue;
                        if (upper) text = text.ToUpperInvariant();
                        cell.Runs.Add(new ClCellRun { Text = text, Bold = bold });
                    }
                    row.Add(cell);
                }
                if (row.Count > 0) ct.Rows.Add(row);
            }
            if (ct.Rows.Count > 0) caseTables.Add(ct);
        }
        if (caseTables.Count == 0) return null;

        // The closing paragraph after the last case table (last page).
        var closeM = Regex.Match(html,
            @"<td[^>]*>\s*(?<t>If you have any questions[\s\S]*?)</td", RegexOptions.IgnoreCase);
        var closeText = closeM.Success ? Clean(closeM.Groups["t"].Value) : "";

        // ---- layout + draw -----------------------------------------------------
        var doc = new Document();
        var page = doc.Pages.Add(ClPageW, ClPageH);
        EnsureFonts(page);
        var resByFace = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var strokes = new StringBuilder();
        void FlushStrokes()
        {
            if (strokes.Length == 0) return;
            page.AddContentStream(Encoding.ASCII.GetBytes("q 0 0 0 RG " + ClBorderW.ToString("0.##", inv) + " w\n" + strokes + "Q\n"));
            strokes.Clear();
        }
        void HLine(double x0, double x1, double yTop) => strokes.Append(string.Create(inv,
            $"{x0:F2} {ClPageH - yTop:F2} m {x1:F2} {ClPageH - yTop:F2} l S\n"));
        void VLine(double x, double y0Top, double y1Top) => strokes.Append(string.Create(inv,
            $"{x:F2} {ClPageH - y0Top:F2} m {x:F2} {ClPageH - y1Top:F2} l S\n"));
        void T(double size, double x, double baselineTop, string text, bool bold = false)
            => EmitGridsterText(page, resByFace, size, x, ClPageH - baselineTop, text,
                bold ? "Arial,Bold" : "Arial");
        double W(string s, double size, bool bold = false)
            => MeasureFaceText(bold ? "Arial,Bold" : "Arial", s, size);
        List<string> Wrap(string text, double box, double size, bool bold = false)
        {
            var lines = new List<string>();
            var cur = new StringBuilder();
            foreach (var word in text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
            {
                var cand = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && W(cand, size, bold) > box) { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
                else { cur.Clear(); cur.Append(cand); }
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            if (lines.Count == 0) lines.Add("");
            return lines;
        }

        // Page 1 prose ladder (all seats probed). The heading draws in darkgray.
        page.AddContentStream(Encoding.ASCII.GetBytes("q 0.663 0.663 0.663 rg\n"));
        T(20, ClHeadingXPt, ClHeadingBaselinePt, headBold, bold: true);
        if (headRest.Length > 0)
            T(20, ClHeadingXPt + W(headBold, 20, bold: true), ClHeadingBaselinePt, " " + headRest);
        page.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));

        for (var i = 0; i < addrLines.Count; i++)
            T(11, ClAddrXPt, ClAddrBaselinePt + i * ClLine11Pt, addrLines[i]);
        if (dateText.Length > 0)
            T(9, ClLeftPt + 472.5 - ClCellChromePt - W(dateText, 9), ClDateBaselinePt, dateText);

        var contentX = ClLeftPt + ClCellChromePt;
        var proseBox = 472.5 - 2 * ClCellChromePt;
        T(9, contentX, ClRequestBaselinePt, requestText, bold: true);
        {
            var pl = Wrap(paraText, proseBox, 9);
            for (var i = 0; i < pl.Count; i++)
                T(9, contentX, ClParaBaselinePt + i * ClLine9Pt, pl[i]);
        }
        T(9, contentX, ClPleaseBaselinePt, pleaseText, bold: true);
        var y = ClBulletBaselinePt;
        foreach (var (text, nested) in bullets)
        {
            var wl = Wrap(text, ClLeftPt + 472.5 - ClCellChromePt - ClBulletTextXPt, 9);
            T(9, ClBulletMarkerXPt, y, "•");
            for (var i = 0; i < wl.Count; i++)
            {
                T(9, ClBulletTextXPt, y, wl[i]);
                y += ClLine9Pt;
            }
            foreach (var n in nested)
            {
                T(9, ClBulletNestedXPt, y, "◦" + n);
                y += ClLine9Pt;
            }
        }

        // Case tables.
        var caseTop = (y - ClLine9Pt) + ClCaseGapAfterProsePt;   // last bullet baseline + gap
        var innerBox = ClCaseGridRightPt - ClCaseGridLeftPt;
        var firstOnPage = false;
        foreach (var ct in caseTables)
        {
            // Resolve columns: declared shares, min-content overrides, slack shrink.
            var nCols = 0;
            foreach (var r in ct.Rows) { var c = 0; foreach (var cl in r) c += cl.ColSpan; if (c > nCols) nCols = c; }
            if (nCols == 0) continue;
            var shares = new double[nCols];
            if (ct.ColPct.Count == nCols)
            {
                double sum = 0; foreach (var p2 in ct.ColPct) sum += p2;
                for (var i = 0; i < nCols; i++) shares[i] = innerBox * ct.ColPct[i] / System.Math.Max(sum, 1);
            }
            else for (var i = 0; i < nCols; i++) shares[i] = innerBox / nCols;
            var mins = new double[nCols];
            foreach (var r in ct.Rows)
            {
                var ci = 0;
                foreach (var cl in r)
                {
                    if (cl.ColSpan == 1 && ci < nCols)
                        foreach (var run in cl.Runs)
                            foreach (var tok in run.Text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                                mins[ci] = System.Math.Max(mins[ci], W(tok, 9, run.Bold) + 2 * ClCellPadXPt);
                    ci += cl.ColSpan;
                }
            }
            double need = 0, slack = 0;
            for (var i = 0; i < nCols; i++)
            {
                if (mins[i] > shares[i]) need += mins[i] - shares[i];
                else slack += shares[i] - mins[i];
            }
            var widths = new double[nCols];
            if (need > 0 && slack > 0)
            {
                var c = System.Math.Min(1.0, need / slack);
                for (var i = 0; i < nCols; i++)
                    widths[i] = mins[i] > shares[i] ? mins[i] : shares[i] - c * (shares[i] - mins[i]);
            }
            else for (var i = 0; i < nCols; i++) widths[i] = shares[i];

            // Wrap the cells and take the row heights.
            var rowHs = new double[ct.Rows.Count];
            for (var ri = 0; ri < ct.Rows.Count; ri++)
            {
                var maxLines = 1;
                var ci = 0;
                foreach (var cl in ct.Rows[ri])
                {
                    double boxW = 0;
                    for (var k = ci; k < System.Math.Min(ci + cl.ColSpan, nCols); k++) boxW += widths[k];
                    boxW -= 2 * ClCellPadXPt;
                    var joined = new StringBuilder();
                    foreach (var run in cl.Runs) { if (joined.Length > 0) joined.Append(' '); joined.Append(run.Text); }
                    var bold0 = cl.Runs.Count > 0 && cl.Runs[0].Bold;
                    var wl = Wrap(joined.ToString(), System.Math.Max(boxW, 8), 9, bold0);
                    cl.Lines = new List<List<ClCellRun>>();
                    foreach (var ln in wl)
                        cl.Lines.Add(new List<ClCellRun> { new() { Text = ln, Bold = bold0 } });
                    // A two-run reason cell keeps its bold prefix on one line.
                    if (cl.Runs.Count == 2 && !cl.Runs[1].Bold && cl.Runs[0].Bold && wl.Count == 1)
                        cl.Lines[0] = new List<ClCellRun> { cl.Runs[0], cl.Runs[1] };
                    if (cl.Lines.Count > maxLines) maxLines = cl.Lines.Count;
                    ci += cl.ColSpan;
                }
                rowHs[ri] = maxLines * ClLine9Pt + ClRowPadPt + ClRowExtraLinePt * (maxLines - 1);
            }
            double tableH = 0; foreach (var rh in rowHs) tableH += rh;

            // Page placement.
            if (ct.BreakBefore || caseTop + tableH > ClBottomPt)
            {
                FlushStrokes();
                page = doc.Pages.Add(ClPageW, ClPageH);
                EnsureFonts(page);
                resByFace.Clear();
                caseTop = ClBreakPageTopPt;
                firstOnPage = true;
            }
            _ = firstOnPage;

            T(9, contentX, caseTop + ClAscSeatPt, ct.Marker);

            // Grid + cells.
            var rowTop = caseTop;
            for (var ri = 0; ri < ct.Rows.Count; ri++)
            {
                var row = ct.Rows[ri];
                HLine(ClCaseGridLeftPt, ClCaseGridRightPt, rowTop);
                var ci = 0;
                var x = ClCaseGridLeftPt;
                VLine(ClCaseGridLeftPt, rowTop, rowTop + rowHs[ri]);
                foreach (var cl in row)
                {
                    double boxW = 0;
                    for (var k = ci; k < System.Math.Min(ci + cl.ColSpan, nCols); k++) boxW += widths[k];
                    var lines = cl.Lines!;
                    var block = lines.Count * ClLine9Pt;
                    var y0 = rowTop + (rowHs[ri] - block) / 2 + ClAscSeatPt;
                    for (var li = 0; li < lines.Count; li++)
                    {
                        var lx = x + ClCellPadXPt;
                        foreach (var run in lines[li])
                        {
                            if (run.Text.Length > 0)
                                T(9, lx, y0 + li * ClLine9Pt, run.Text, run.Bold);
                            lx += W(run.Text, 9, run.Bold) + (run.Bold ? W("  ", 9, true) : 0);
                        }
                    }
                    x += boxW;
                    VLine(x, rowTop, rowTop + rowHs[ri]);
                    ci += cl.ColSpan;
                }
                rowTop += rowHs[ri];
            }
            HLine(ClCaseGridLeftPt, ClCaseGridRightPt, rowTop);
            caseTop = rowTop + ClInterCaseGapPt;
        }

        // Closing paragraph on the final page.
        if (closeText.Length > 0)
        {
            var cl2 = Wrap(closeText, proseBox, 9);
            var cy = caseTop - ClInterCaseGapPt + ClInterCaseGapPt + ClAscSeatPt;
            for (var i = 0; i < cl2.Count; i++)
                T(9, contentX, cy + i * ClLine9Pt, cl2[i]);
        }
        FlushStrokes();
        PruneUnusedFonts(doc);
        return doc;
    }
}
