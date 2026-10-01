using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One case table: its column widths, header band, rows with wrapped cells and rules, breaking the page as needed.</summary>
    private static bool DrawCaseTable(CaseLetterState ce, ClCaseTable ct)
    {
        ce.nCols = 0;
        if (SizeCaseColumns(ce, ct)) return true;
        MeasureCaseRows(ce, ct);
        for (var ri = 0; ri < ct.Rows.Count; ri++)
        {
            var row = ct.Rows[ri];
            HLine(ce, ClCaseGridLeftPt, ClCaseGridRightPt, ce.rowTop);
            var ci = 0;
            var x = ClCaseGridLeftPt;
            VLine(ce, ClCaseGridLeftPt, ce.rowTop, ce.rowTop + ce.rowHs[ri]);
            foreach (var cl in row)
            {
                double boxW = 0;
                for (var k = ci; k < System.Math.Min(ci + cl.ColSpan, ce.nCols); k++) boxW += ce.widths[k];
                var lines = cl.Lines!;
                var block = lines.Count * ClLine9Pt;
                var y0 = ce.rowTop + (ce.rowHs[ri] - block) / 2 + ClAscSeatPt;
                for (var li = 0; li < lines.Count; li++)
                {
                    var lx = x + ClCellPadXPt;
                    foreach (var run in lines[li])
                    {
                        if (run.Text.Length > 0)
                            T(ce, 9, lx, y0 + li * ClLine9Pt, run.Text, run.Bold);
                        lx += W(ce, run.Text, 9, run.Bold) + (run.Bold ? W(ce, "  ", 9, true) : 0);
                    }
                }
                x += boxW;
                VLine(ce, x, ce.rowTop, ce.rowTop + ce.rowHs[ri]);
                ci += cl.ColSpan;
            }
            ce.rowTop += ce.rowHs[ri];
        }
        HLine(ce, ClCaseGridLeftPt, ClCaseGridRightPt, ce.rowTop);
        ce.caseTop = ce.rowTop + ClInterCaseGapPt;
        return true;
    }

    /// <summary>The letter head, address block, date, request line, paragraph, please-provide line and bullet list.</summary>
    private static void DrawCaseProse(CaseLetterState ce)
    {
        ce.doc = new Document();
        ce.page = ce.doc.Pages.Add(ClPageW, ClPageH);
        EnsureFonts(ce.page);
        ce.resByFace = new Dictionary<string, string>(System.StringComparer.Ordinal);
        ce.strokes = new StringBuilder();
        // Page 1 prose ladder (all seats probed). The heading draws in darkgray.
        ce.page.AddContentStream(Encoding.ASCII.GetBytes("q 0.663 0.663 0.663 rg\n"));
        T(ce, 20, ClHeadingXPt, ClHeadingBaselinePt, ce.headBold, bold: true);
        if (ce.headRest.Length > 0)
            T(ce, 20, ClHeadingXPt + W(ce, ce.headBold, 20, bold: true), ClHeadingBaselinePt, " " + ce.headRest);
        ce.page.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));

        for (var i = 0; i < ce.addrLines.Count; i++)
            T(ce, 11, ClAddrXPt, ClAddrBaselinePt + i * ClLine11Pt, ce.addrLines[i]);
        if (ce.dateText.Length > 0)
            T(ce, 9, ClLeftPt + 472.5 - ClCellChromePt - W(ce, ce.dateText, 9), ClDateBaselinePt, ce.dateText);

        ce.contentX = ClLeftPt + ClCellChromePt;
        ce.proseBox = 472.5 - 2 * ClCellChromePt;
        T(ce, 9, ce.contentX, ClRequestBaselinePt, ce.requestText, bold: true);
        {
            var pl = Wrap(ce, ce.paraText, ce.proseBox, 9);
            for (var i = 0; i < pl.Count; i++)
                T(ce, 9, ce.contentX, ClParaBaselinePt + i * ClLine9Pt, pl[i]);
        }
        T(ce, 9, ce.contentX, ClPleaseBaselinePt, ce.pleaseText, bold: true);
        ce.y = ClBulletBaselinePt;
        foreach (var (text, nested) in ce.bullets)
        {
            var wl = Wrap(ce, text, ClLeftPt + 472.5 - ClCellChromePt - ClBulletTextXPt, 9);
            T(ce, 9, ClBulletMarkerXPt, ce.y, "•");
            for (var i = 0; i < wl.Count; i++)
            {
                T(ce, 9, ClBulletTextXPt, ce.y, wl[i]);
                ce.y += ClLine9Pt;
            }
            foreach (var n in nested)
            {
                T(ce, 9, ClBulletNestedXPt, ce.y, "◦" + n);
                ce.y += ClLine9Pt;
            }
        }

    }

    /// <summary>Parse the case tables between the page breaks; a letter without any is not this dialect.</summary>
    private static bool TryParseCaseTables(CaseLetterState ce, string html)
    {
        ce.caseTables = new List<ClCaseTable>();
        ce.breakPositions = new List<int>();
        foreach (Match bm in Regex.Matches(html, @"<div\s+class=""break""", RegexOptions.IgnoreCase))
            ce.breakPositions.Add(bm.Index);
        foreach (Match om in Regex.Matches(html,
            @"<table[^>]*width:630px[^>]*>\s*<tr>\s*<td[^>]*>\s*(?<mk>\d+\.)\s*</td>\s*<td[^>]*>\s*(?<tb><table[^>]*blackBorder[\s\S]*?</table>)\s*</td>",
            RegexOptions.IgnoreCase))
        {
            var ct = new ClCaseTable { Marker = om.Groups["mk"].Value };
            // BreakBefore: a break div sits between the previous case table and this one.
            var prevEnd = ce.caseTables.Count > 0 ? ce.caseTables[^1].SourceEnd : 0;
            foreach (var bp in ce.breakPositions)
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
                        ct.ColPct.Add(double.Parse(wm.Groups[1].Value, ce.inv));
                    // Runs: bold segments stay bold; an uppercase-transform div upper-cases.
                    var content = td.Groups["c"].Value;
                    var upper = Regex.IsMatch(content, @"TEXT-TRANSFORM\s*:\s*uppercase", RegexOptions.IgnoreCase);
                    foreach (Match seg in Regex.Matches(content, @"<b>(?<b>[\s\S]*?)</b>|(?<p>(?:(?!<b>)[\s\S])+?)(?=<b>|$)", RegexOptions.IgnoreCase))
                    {
                        var bold = seg.Groups["b"].Success;
                        var text = Clean(ce, bold ? seg.Groups["b"].Value : seg.Groups["p"].Value);
                        if (text.Length == 0) continue;
                        if (upper) text = text.ToUpperInvariant();
                        cell.Runs.Add(new ClCellRun { Text = text, Bold = bold });
                    }
                    row.Add(cell);
                }
                if (row.Count > 0) ct.Rows.Add(row);
            }
            if (ct.Rows.Count > 0) ce.caseTables.Add(ct);
        }
        if (ce.caseTables.Count == 0) return false;

        ce.closeM = Regex.Match(html,
            @"<td[^>]*>\s*(?<t>If you have any questions[\s\S]*?)</td", RegexOptions.IgnoreCase);
        ce.closeText = ce.closeM.Success ? Clean(ce, ce.closeM.Groups["t"].Value) : "";
        return true;
    }

    /// <summary>Parse the bullet list and its nested items.</summary>
    private static void ParseCaseBullets(CaseLetterState ce, string html)
    {
        ce.bullets = new List<(string Text, List<string> Nested)>();
        ce.ulM = Regex.Match(html, @"<ul>(?<b>[\s\S]*?)</ul>\s*</td", RegexOptions.IgnoreCase);
        if (ce.ulM.Success)
        {
            // Cut the NESTED lists out first (their <li>s would end the outer
            // item's match early), then walk the top-level items.
            var body = ce.ulM.Groups["b"].Value;
            var nestedLists = new List<List<string>>();
            body = Regex.Replace(body, @"<ul[^>]*>(?<n>[\s\S]*?)</ul>", nm =>
            {
                var items = new List<string>();
                foreach (Match nli in Regex.Matches(nm.Groups["n"].Value, @"<li[^>]*>(?<t>[\s\S]*?)</li>", RegexOptions.IgnoreCase))
                    items.Add(Clean(ce, nli.Groups["t"].Value));
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
                var text = Clean(ce, c);
                if (text.Length > 0 || nested.Count > 0) ce.bullets.Add((text, nested));
            }
        }

    }

    /// <summary>Parse the class rule, heading, address, date, request and paragraph texts; a page without the rule is not this dialect.</summary>
    private static bool TryParseCaseHead(CaseLetterState ce, string html)
    {
        ce.clsRule = Regex.Match(html,
            @"\.blackBorder\s*\{[^}]*border-top\s*:[^;}]*;[^}]*border-bottom\s*:[^;}]*;[^}]*border-left\s*:[^;}]*;[^}]*border-right\s*:[^;}]*;",
            RegexOptions.IgnoreCase);
        if (!ce.clsRule.Success) return false;

        ce.inv = System.Globalization.CultureInfo.InvariantCulture;
        ce.headM = Regex.Match(html,
            @"id=""PageHeading""[^>]*>\s*<b>(?<b>[\s\S]*?)</b>(?<r>[\s\S]*?)</td",
            RegexOptions.IgnoreCase);
        ce.headBold = ce.headM.Success ? Clean(ce, ce.headM.Groups["b"].Value) : "HSBC";
        ce.headRest = ce.headM.Success ? Clean(ce, ce.headM.Groups["r"].Value) : "";

        ce.addrM = Regex.Match(html,
            @"<table[^>]*height:120px[^>]*>(?<b>[\s\S]*?)</table\s*>", RegexOptions.IgnoreCase);
        ce.addrLines = new List<string>();
        ce.dateText = "";
        if (ce.addrM.Success)
        {
            var tds = Regex.Matches(ce.addrM.Groups["b"].Value, @"<td[^>]*>(?<c>[\s\S]*?)</td\s*>", RegexOptions.IgnoreCase);
            if (tds.Count > 0)
                foreach (var ln in Regex.Split(tds[0].Groups["c"].Value, @"<br\s*/?>", RegexOptions.IgnoreCase))
                {
                    // NBSP survives the whitespace collapse (the "GB   LE1 7BB"
                    // line keeps its gap), then reads back as a plain space.
                    var t = Regex.Replace(DecodeEntities(Regex.Replace(ln, "<[^>]+>", " ")),
                        "[ \t\r\n]+", " ").Trim();
                    t = t.Replace(' ', ' ');
                    if (t.Trim().Length > 0) ce.addrLines.Add(t.Trim().ToUpperInvariant());
                }
            if (tds.Count > 1) ce.dateText = Clean(ce, tds[1].Groups["c"].Value).ToUpperInvariant();
        }

        ce.reqM = Regex.Match(html, @"<b>\s*(?<t>REQUEST[^<]*)</b>", RegexOptions.IgnoreCase);
        ce.requestText = ce.reqM.Success ? Clean(ce, ce.reqM.Groups["t"].Value) : "REQUEST FOR INFORMATION";
        ce.paraM = Regex.Match(html,
            @"<td[^>]*>\s*(?<t>This is a request[\s\S]*?)</td", RegexOptions.IgnoreCase);
        ce.paraText = ce.paraM.Success ? Clean(ce, ce.paraM.Groups["t"].Value) : "";
        ce.pleaseM = Regex.Match(html, @"<b>\s*(?<t>PLEASE PROVIDE[^<]*)</b>", RegexOptions.IgnoreCase);
        ce.pleaseText = ce.pleaseM.Success ? Clean(ce, ce.pleaseM.Groups["t"].Value) : "";
        return true;
    }

    /// <summary>Measure the row heights from the wrapped cells, break the page when the table does not fit, and set the marker.</summary>
    private static void MeasureCaseRows(CaseLetterState ce, ClCaseTable ct)
    {
        for (var ri = 0; ri < ct.Rows.Count; ri++)
        {
            var maxLines = 1;
            var ci = 0;
            foreach (var cl in ct.Rows[ri])
            {
                double boxW = 0;
                for (var k = ci; k < System.Math.Min(ci + cl.ColSpan, ce.nCols); k++) boxW += ce.widths[k];
                boxW -= 2 * ClCellPadXPt;
                var joined = new StringBuilder();
                foreach (var run in cl.Runs) { if (joined.Length > 0) joined.Append(' '); joined.Append(run.Text); }
                var bold0 = cl.Runs.Count > 0 && cl.Runs[0].Bold;
                var wl = Wrap(ce, joined.ToString(), System.Math.Max(boxW, 8), 9, bold0);
                cl.Lines = new List<List<ClCellRun>>();
                foreach (var ln in wl)
                    cl.Lines.Add(new List<ClCellRun> { new() { Text = ln, Bold = bold0 } });
                // A two-run reason cell keeps its bold prefix on one line.
                if (cl.Runs.Count == 2 && !cl.Runs[1].Bold && cl.Runs[0].Bold && wl.Count == 1)
                    cl.Lines[0] = new List<ClCellRun> { cl.Runs[0], cl.Runs[1] };
                if (cl.Lines.Count > maxLines) maxLines = cl.Lines.Count;
                ci += cl.ColSpan;
            }
            ce.rowHs[ri] = maxLines * ClLine9Pt + ClRowPadPt + ClRowExtraLinePt * (maxLines - 1);
        }
        ce.tableH = 0; foreach (var rh in ce.rowHs) ce.tableH += rh;

        // Page placement.
        if (ct.BreakBefore || ce.caseTop + ce.tableH > ClBottomPt)
        {
            FlushStrokes(ce);
            ce.page = ce.doc.Pages.Add(ClPageW, ClPageH);
            EnsureFonts(ce.page);
            ce.resByFace.Clear();
            ce.caseTop = ClBreakPageTopPt;
            ce.firstOnPage = true;
        }
        _ = ce.firstOnPage;

        T(ce, 9, ce.contentX, ce.caseTop + ClAscSeatPt, ct.Marker);

        ce.rowTop = ce.caseTop;
    }

    /// <summary>Resolve the column count and widths from the declared percentages or the cells' needs; an empty table is skipped.</summary>
    private static bool SizeCaseColumns(CaseLetterState ce, ClCaseTable ct)
    {
        foreach (var r in ct.Rows) { var c = 0; foreach (var cl in r) c += cl.ColSpan; if (c > ce.nCols) ce.nCols = c; }
        if (ce.nCols == 0) return true;
        ce.shares = new double[ce.nCols];
        if (ct.ColPct.Count == ce.nCols)
        {
            double sum = 0; foreach (var p2 in ct.ColPct) sum += p2;
            for (var i = 0; i < ce.nCols; i++) ce.shares[i] = ce.innerBox * ct.ColPct[i] / System.Math.Max(sum, 1);
        }
        else for (var i = 0; i < ce.nCols; i++) ce.shares[i] = ce.innerBox / ce.nCols;
        ce.mins = new double[ce.nCols];
        foreach (var r in ct.Rows)
        {
            var ci = 0;
            foreach (var cl in r)
            {
                if (cl.ColSpan == 1 && ci < ce.nCols)
                    foreach (var run in cl.Runs)
                        foreach (var tok in run.Text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                            ce.mins[ci] = System.Math.Max(ce.mins[ci], W(ce, tok, 9, run.Bold) + 2 * ClCellPadXPt);
                ci += cl.ColSpan;
            }
        }
        ce.need = 0;
        ce.slack = 0;
        for (var i = 0; i < ce.nCols; i++)
        {
            if (ce.mins[i] > ce.shares[i]) ce.need += ce.mins[i] - ce.shares[i];
            else ce.slack += ce.shares[i] - ce.mins[i];
        }
        ce.widths = new double[ce.nCols];
        if (ce.need > 0 && ce.slack > 0)
        {
            var c = System.Math.Min(1.0, ce.need / ce.slack);
            for (var i = 0; i < ce.nCols; i++)
                ce.widths[i] = ce.mins[i] > ce.shares[i] ? ce.mins[i] : ce.shares[i] - c * (ce.shares[i] - ce.mins[i]);
        }
        else for (var i = 0; i < ce.nCols; i++) ce.widths[i] = ce.shares[i];

        ce.rowHs = new double[ct.Rows.Count];
        return false;
    }
}
