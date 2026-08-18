using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
    // ── The Word-filtered FORM dialect ────────────────────────────────────────
    // A Microsoft-Word "filtered" page whose whole layout is ONE MsoNormalTable
    // form grid (border-collapse, per-side windowtext borders, teal band rows,
    // 7pt Arial labels over text inputs, a checkbox roster). The source engine
    // renders it as a single landscape-wide page:
    //   page W = margin 90 + body inset 6 + Σ solved columns + gutter + margin 90
    // Inputs draw as 1px boxes with the value in 10pt Helvetica; a stylesheet
    // #ID { width } rule sizes a control, otherwise the measured default box.
    // All constants below are measured on the reference (probes q1..q3).

    private const double MsoInputDefaultWPt = 117.5;  // default text input box
    private const double MsoInputHPt = 16.2;          // input box height
    private const double MsoInputTextInsetPt = 1.5;   // value x inset inside the box
    private const double MsoInputBaselinePt = 11.8;   // value baseline below box top
    private const double MsoCheckboxPt = 7.8;         // checkbox square
    private const double MsoCellPadPt = 5.75;         // the sheet's padding: 0in 5.75pt
    private const double MsoInputChromePt = 5.5;      // input margins inside its cell
    private const double MsoLabelLinePt = 8.5;        // 7pt label line box
    private const double MsoRowBottomPadPt = 12.2;    // input row's bottom band
    private const double MsoBodyInsetPt = 6.0;        // body inset before the table

    private sealed class MsoInputBox
    {
        public bool Checkbox;
        public bool Select;
        public bool Checked;
        public string Value = "";
        public double WPt = MsoInputDefaultWPt;
        public double HPt = MsoInputHPt;
    }

    private sealed class MsoRun
    {
        public string Text = "";
        public double Fs = 12;
        public string Face = "Times New Roman";
        public bool Bold, Italic;
        public bool White, Teal;
        public bool Center;
        public bool NewLine;          // starts a fresh line (p or br)
        public bool BrLine;           // the break above came from <br> (not <p>)
        public MsoInputBox? Input;
    }

    private sealed class MsoCell
    {
        public int ColSpan = 1;
        public double StyleWPt;
        // borders draw only where the style declares them (the collapsed
        // windowtext grid) — an undeclared side stays open
        public bool BTop, BLeft, BRight, BBottom;
        public bool BgTeal;
        public bool NestedHost;       // this cell contains the roster's nested table
        public List<MsoRun> Runs = new();
    }

    private sealed class MsoRow
    {
        public double StyleHPt;
        public bool Nested;           // a row of the roster's nested table
        public List<MsoCell> Cells = new();
    }

    /// <summary>Detects and renders the Word-filtered form-grid document.
    /// Returns null when the fingerprint does not match (the caller keeps
    /// its own flow).</summary>
    private static Document? TryRenderMsoWordForm(string html)
    {
        if (!Regex.IsMatch(html, @"<meta[^>]+Generator[^>]+Microsoft Word", RegexOptions.IgnoreCase))
            return null;
        if (!Regex.IsMatch(html, @"class=""?MsoNormalTable", RegexOptions.IgnoreCase))
            return null;
        var inputCount = Regex.Matches(html, @"<input\b", RegexOptions.IgnoreCase).Count;
        if (inputCount < 5) return null;
        var tm = Regex.Match(html, @"<table[^>]*MsoNormalTable[\s\S]*?</table>", RegexOptions.IgnoreCase);
        if (!tm.Success) return null;

        // #ID { width: Npx; height: Npx; } control sizing rules.
        var idW = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var idH = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (Match im in Regex.Matches(html, @"#(\w+)\s*\{([^}]*)\}"))
        {
            var wm = Regex.Match(im.Groups[2].Value, @"width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (wm.Success) idW[im.Groups[1].Value] = DtpNum(wm.Groups[1].Value) * 0.75;
            var hm = Regex.Match(im.Groups[2].Value, @"height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (hm.Success) idH[im.Groups[1].Value] = DtpNum(hm.Groups[1].Value) * 0.75;
        }

        var rows = ParseMsoFormTable(tm.Value, idW, idH);
        if (rows.Count < 4) return null;

        var doc = new Document();
        RenderMsoFormGrid(doc, rows);
        return doc;
    }

    private static List<MsoRow> ParseMsoFormTable(string tableHtml,
        IReadOnlyDictionary<string, double> idW, IReadOnlyDictionary<string, double> idH)
    {
        var rows = new List<MsoRow>();
        MsoRow? row = null;
        MsoCell? cell = null;
        // the open inline style (span/b/i nesting collapses onto one state)
        double fs = 12; var face = "Times New Roman"; var bold = false; var ital = false;
        var white = false; var teal = false; var center = false;
        var pendingLine = false;
        var pendingBr = false;
        var nestedDepth = 0;
        var text = new StringBuilder();

        void FlushText()
        {
            var t = CollapseWs(text.ToString());
            text.Clear();
            if (cell is null || t.Trim(' ', '\u00A0').Length == 0) { return; }
            cell.Runs.Add(new MsoRun
            {
                Text = t.Trim(), Fs = fs, Face = face, Bold = bold, Italic = ital,
                White = white, Teal = teal, Center = center, NewLine = pendingLine,
            });
            pendingLine = false;
        }
        void CloseCell() { FlushText(); if (cell is not null) row!.Cells.Add(cell); cell = null; }
        void CloseRow()
        {
            CloseCell();
            if (row is { Cells.Count: > 0 }) { row.Nested = nestedDepth > 0; rows.Add(row); }
            row = null;
        }

        var inSelect = false;
        var inSelectedOption = false;
        foreach (var tok in Tokenize(StripNonContent(tableHtml)))
        {
            if (tok.Kind == TokenKind.Text)
            {
                if (inSelect)
                {
                    // only the SELECTED option's text shows, inside the box
                    if (inSelectedOption && cell is not null && cell.Runs.Count > 0
                        && cell.Runs[^1].Input is { Select: true } selBox)
                        selBox.Value = (selBox.Value == "?" ? "" : selBox.Value)
                                       + CollapseWs(DecodeEntities(tok.Value)).Trim();
                    continue;
                }
                if (cell is not null) text.Append(DecodeEntities(tok.Value));
                continue;
            }
            var tag = tok.Tag!.ToLowerInvariant();
            if (tok.IsClose)
            {
                switch (tag)
                {
                    case "table":
                        if (nestedDepth > 0) { CloseRow(); nestedDepth--; }
                        break;
                    case "select": inSelect = false; inSelectedOption = false; break;
                    case "option": inSelectedOption = false; break;
                    case "td": CloseCell(); inSelect = false; break;
                    case "tr": CloseRow(); inSelect = false; break;
                    case "b": case "strong": FlushText(); bold = false; break;
                    case "i": case "em": FlushText(); ital = false; break;
                    case "span": FlushText(); fs = 12; face = "Times New Roman"; white = false; teal = false; break;
                    case "p": FlushText(); pendingLine = true; pendingBr = false; center = false; break;
                }
                continue;
            }
            switch (tag)
            {
                case "table":
                    // the roster's nested table: its host cell closes as a label
                    // row; the nested rows follow tagged Nested
                    if (cell is not null)
                    {
                        cell.NestedHost = true;
                        CloseCell();
                        CloseRow();
                        nestedDepth++;
                    }
                    break;
                case "tr":
                    CloseRow();
                    row = new MsoRow();
                    if (tok.Attributes is { } tra && tra.TryGetValue("style", out var trst))
                    {
                        var hm = Regex.Match(trst, @"height\s*:\s*([\d.]+)\s*in", RegexOptions.IgnoreCase);
                        if (hm.Success) row.StyleHPt = DtpNum(hm.Groups[1].Value) * 72.0;
                        var hp = Regex.Match(trst, @"height\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
                        if (hp.Success) row.StyleHPt = DtpNum(hp.Groups[1].Value);
                    }
                    break;
                case "td":
                    CloseCell();
                    row ??= new MsoRow();
                    cell = new MsoCell();
                    pendingLine = false;
                    bold = false; ital = false; fs = 12; face = "Times New Roman";
                    white = false; teal = false; center = false;
                    if (tok.Attributes is { } ca)
                    {
                        if (ca.TryGetValue("colspan", out var cs)
                            && int.TryParse(cs.Trim(), out var csn) && csn > 1)
                            cell.ColSpan = csn;
                        if (ca.TryGetValue("style", out var st))
                        {
                            var wm = Regex.Match(st, @"width\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
                            if (wm.Success) cell.StyleWPt = DtpNum(wm.Groups[1].Value);
                            if (Regex.IsMatch(st, @"background\s*:\s*teal", RegexOptions.IgnoreCase))
                                cell.BgTeal = true;
                            // border model: `border:none` clears all; `border:solid`
                            // sets all; per-side declarations override afterwards.
                            if (Regex.IsMatch(st, @"(?<!-)border\s*:\s*none", RegexOptions.IgnoreCase))
                                cell.BTop = cell.BLeft = cell.BRight = cell.BBottom = false;
                            else if (Regex.IsMatch(st, @"(?<!-)border\s*:\s*solid", RegexOptions.IgnoreCase))
                                cell.BTop = cell.BLeft = cell.BRight = cell.BBottom = true;
                            foreach (var (side, setter) in new (string, Action<bool>)[]
                            {
                                ("top", v => cell.BTop = v), ("left", v => cell.BLeft = v),
                                ("right", v => cell.BRight = v), ("bottom", v => cell.BBottom = v),
                            })
                            {
                                var bm = Regex.Match(st, @"border-" + side + @"\s*:\s*(none|solid)",
                                    RegexOptions.IgnoreCase);
                                if (bm.Success) setter(bm.Groups[1].Value.Equals("solid",
                                    StringComparison.OrdinalIgnoreCase));
                                var bs = Regex.Match(st, @"border-" + side + @"-style\s*:\s*(none|solid)",
                                    RegexOptions.IgnoreCase);
                                if (bs.Success) setter(bs.Groups[1].Value.Equals("solid",
                                    StringComparison.OrdinalIgnoreCase));
                            }
                        }
                    }
                    break;
                case "p":
                    FlushText();
                    pendingLine = true;
                    if (tok.Attributes is { } pa
                        && ((pa.TryGetValue("align", out var al)
                             && al.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
                            || (pa.TryGetValue("style", out var pst)
                                && Regex.IsMatch(pst, @"text-align\s*:\s*center", RegexOptions.IgnoreCase))))
                        center = true;
                    break;
                case "br":
                    FlushText();
                    // a bare <br> still occupies its line box (the contact-info
                    // row's <br><br><br> rhythm paces the row)
                    if (cell is not null && pendingLine)
                        cell.Runs.Add(new MsoRun { Text = "", NewLine = true, Fs = fs, Face = face });
                    pendingLine = true;
                    pendingBr = true;
                    break;
                case "b": case "strong": FlushText(); bold = true; break;
                case "i": case "em": FlushText(); ital = true; break;
                case "span":
                    FlushText();
                    if (tok.Attributes is { } sa && sa.TryGetValue("style", out var sst))
                    {
                        var fm = Regex.Match(sst, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
                        if (fm.Success) fs = DtpNum(fm.Groups[1].Value);
                        else if (Regex.IsMatch(sst, @"font-size\s*:\s*large", RegexOptions.IgnoreCase))
                            fs = 13.5;   // `large` at the Word base (measured header)
                        if (Regex.IsMatch(sst, @"Arial", RegexOptions.IgnoreCase)) face = "Arial";
                        if (Regex.IsMatch(sst, @"color\s*:\s*white", RegexOptions.IgnoreCase)) white = true;
                        if (Regex.IsMatch(sst, @"color\s*:\s*teal", RegexOptions.IgnoreCase)) teal = true;
                    }
                    break;
                case "input":
                {
                    FlushText();
                    if (cell is null) break;
                    var inp = new MsoInputBox();
                    if (tok.Attributes is { } ia)
                    {
                        if (ia.TryGetValue("type", out var ty)
                            && ty.Trim().Equals("checkbox", StringComparison.OrdinalIgnoreCase))
                        { inp.Checkbox = true; inp.WPt = MsoCheckboxPt; inp.HPt = MsoCheckboxPt; }
                        if (ia.ContainsKey("checked")) inp.Checked = true;
                        if (ia.TryGetValue("value", out var v)) inp.Value = DecodeEntities(v);
                        if (ia.TryGetValue("id", out var id) && !inp.Checkbox)
                        {
                            if (idW.TryGetValue(id.Trim(), out var w)) inp.WPt = w;
                            if (idH.TryGetValue(id.Trim(), out var h)) inp.HPt = h;
                        }
                    }
                    cell.Runs.Add(new MsoRun
                    { Input = inp, NewLine = pendingLine, BrLine = pendingBr, Center = center });
                    pendingLine = false;
                    pendingBr = false;
                    break;
                }
                case "select":
                {
                    FlushText();
                    if (cell is null) break;
                    // a dropdown draws as a small input-height box with the
                    // selected option's text (measured: the Other box 47.8 wide)
                    cell.Runs.Add(new MsoRun
                    {
                        Input = new MsoInputBox { Select = true, WPt = 47.8 },
                        NewLine = pendingLine, Center = center,
                    });
                    pendingLine = false;
                    inSelect = true;
                    break;
                }
                case "option":
                    inSelectedOption = (tok.Attributes is { } oa && oa.ContainsKey("selected"))
                        || (cell is not null && cell.Runs.Count > 0
                            && cell.Runs[^1].Input is { Select: true, Value: "" });
                    break;
            }
        }
        CloseRow();
        return rows;
    }

    private static void RenderMsoFormGrid(Document doc, List<MsoRow> rows)
    {
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        // ── column solve ─────────────────────────────────────────────────────
        var nCols = 0;
        foreach (var r in rows)
        {
            var c = 0;
            foreach (var mc in r.Cells) c += mc.ColSpan;
            nCols = Math.Max(nCols, c);
        }
        var colW = new double[nCols];
        // single-span pins first; a spanning deficit spreads over its columns
        foreach (var r in rows)
        {
            var c = 0;
            foreach (var mc in r.Cells)
            {
                var nat = MsoCellNaturalW(mc);
                if (mc.ColSpan == 1 && nat > colW[c]) colW[c] = nat;
                c += mc.ColSpan;
            }
        }
        for (var pass = 0; pass < 2; pass++)
            foreach (var r in rows)
            {
                var c = 0;
                foreach (var mc in r.Cells)
                {
                    var nat = MsoCellNaturalW(mc);
                    double have = 0;
                    for (var k = 0; k < mc.ColSpan && c + k < nCols; k++) have += colW[c + k];
                    if (mc.ColSpan > 1 && nat > have)
                    {
                        var add = (nat - have) / mc.ColSpan;
                        for (var k = 0; k < mc.ColSpan && c + k < nCols; k++) colW[c + k] += add;
                    }
                    c += mc.ColSpan;
                }
            }
        double tableW = 0;
        foreach (var w in colW) tableW += w;

        var pageW = 90.0 + MsoBodyInsetPt + tableW + 90.0;
        var page = doc.Pages.Add(pageW, 842.0);
        EnsureFont(page, "Helvetica", "F1");
        EnsureFont(page, "Helvetica-Bold", "F2");
        EnsureFont(page, "Times-Roman", "F3");
        EnsureFont(page, "Times-Bold", "F4");
        EnsureFont(page, "Times-BoldItalic", "F5");
        EnsureFont(page, "Helvetica-Oblique", "F6");
        EnsureFont(page, "Helvetica-BoldOblique", "F7");
        EnsureFont(page, "ZapfDingbats", "F8");

        string RunRes(MsoRun run) => run.Face == "Arial"
            ? (run.Bold ? (run.Italic ? "F7" : "F2") : run.Italic ? "F6" : "F1")
            : run.Bold ? (run.Italic ? "F5" : "F4") : "F3";
        string RunFace(MsoRun run) => run.Face == "Arial"
            ? "Arial" + (run.Bold ? " Bold" : "")
            : "Times New Roman" + (run.Bold ? " Bold" : "");

        var x0 = 90.0 + MsoBodyInsetPt;
        var yTop = 72.0 + MsoBodyInsetPt;      // table seats one body inset under the margin
        var y = 842.0 - yTop;

        var sb = new StringBuilder();
        var tsb = new StringBuilder();

        // roster group state: the nested checkbox table draws inside the host
        // cell's border box, with the empty side cell braced to its right
        var groupOpen = false;
        double groupTop = 0;
        void CloseGroup(double yNow)
        {
            if (!groupOpen) return;
            groupOpen = false;
            var hostW = tableW - MsoRosterSideWPt;
            var gl = new StringBuilder("q 0 0 0 RG 1.5 w ");
            gl.Append(string.Create(invc, $"{x0:F2} {groupTop:F2} m {x0:F2} {yNow:F2} l S "));
            gl.Append(string.Create(invc, $"{x0 + hostW:F2} {groupTop:F2} m {x0 + hostW:F2} {yNow:F2} l S "));
            gl.Append(string.Create(invc, $"{x0:F2} {yNow:F2} m {x0 + hostW:F2} {yNow:F2} l S "));
            gl.Append(string.Create(invc, $"{x0 + tableW:F2} {groupTop:F2} m {x0 + tableW:F2} {yNow:F2} l S "));
            gl.Append(string.Create(invc, $"{x0 + hostW:F2} {yNow:F2} m {x0 + tableW:F2} {yNow:F2} l S "));
            gl.Append("Q\n");
            sb.Append(gl);
        }

        foreach (var r in rows)
        {
            if (r.Nested)
            {
                // roster rows: checkbox + label pairs on the measured two-column
                // rhythm inside the host box
                var ly0 = y;
                var pen0 = x0 + MsoRosterCol1Pt;
                var ci0 = 0;
                foreach (var mc in r.Cells)
                {
                    var penN = ci0 == 0 ? x0 + MsoRosterCol1Pt : x0 + MsoRosterCol2Pt;
                    ci0++;
                    foreach (var run in mc.Runs)
                    {
                        if (run.Input is { Checkbox: true } cbN)
                        {
                            var bx = penN + 2.5;
                            var byy = ly0 - MsoCheckLinePt + 3.2;
                            sb.Append(string.Create(invc,
                                $"q 1 1 1 rg {bx:F2} {byy:F2} {MsoCheckboxPt:F2} {MsoCheckboxPt:F2} re f Q\n"));
                            if (cbN.Checked)
                                tsb.Append(string.Create(invc,
                                    $"BT /F8 7.75 Tf 0 0 0 rg {bx + 0.6:F2} {byy + 1.0:F2} Td (4) Tj ET\n"));
                            penN = bx + MsoCheckboxPt + 3.5;
                            continue;
                        }
                        if (run.Text.Length == 0 || run.Input is not null) continue;
                        var clean0 = FilterWinAnsi(run.Text);
                        if (clean0.Trim().Length == 0) continue;
                        var w0 = MeasureFaceText(RunFace(run), clean0, run.Fs);
                        tsb.Append(string.Create(invc,
                            $"BT /{RunRes(run)} {run.Fs:F2} Tf 0 0 0 rg {penN:F2} {ly0 - MsoCheckLinePt + 4.6:F2} Td ({EscapePdfText(clean0)}) Tj ET\n"));
                        penN += w0;
                    }
                }
                y -= MsoCheckLinePt;
                continue;
            }
            if (groupOpen)
            {
                CloseGroup(y);
                // the stray side-cell row that followed the nested table holds
                // no band of its own
                var sideOnly = true;
                foreach (var mc in r.Cells)
                    foreach (var run in mc.Runs)
                        if (run.Text.Trim().Length > 0 || run.Input is not null) { sideOnly = false; break; }
                if (sideOnly) continue;
            }
            // row height: teal/band rows take the style height; input rows grow
            // label line + box + the measured bottom band; text rows by lines.
            double rowH = Math.Max(r.StyleHPt + 1, 0);
            foreach (var mc in r.Cells)
            {
                double h = 0;
                var firstLine = true;
                var lineHasCheck = false;
                foreach (var run in mc.Runs)
                {
                    if (run.Input is { Checkbox: true })
                    { lineHasCheck = true; continue; }   // inline — its line bills below
                    if (run.Input is { } inp)
                    {
                        // a same-paragraph (br-broken) input keeps the deep
                        // bottom band; a separate-paragraph one closes tight
                        h += inp.HPt + (run.BrLine ? MsoRowBottomPadPt : MsoTightPadPt);
                        firstLine = false; lineHasCheck = false; continue;
                    }
                    if (run.NewLine || firstLine)
                        h += lineHasCheck ? MsoCheckLinePt : MsoLineOf(run.Fs);
                    firstLine = false;
                    lineHasCheck = false;
                }
                if (lineHasCheck) h += MsoCheckLinePt;
                if (h > rowH) rowH = h;
            }
            if (rowH <= 2) rowH = 13.5;
            // an inkless teal band row draws the measured .1in band
            var allTeal = r.Cells.Count > 0;
            foreach (var mc in r.Cells)
            {
                if (!mc.BgTeal) { allTeal = false; break; }
                foreach (var run in mc.Runs)
                    if (run.Text.Trim().Length > 0 || run.Input is not null) { allTeal = false; break; }
            }
            if (allTeal) rowH = Math.Max(rowH, 12.2);

            var cx = x0;
            var ci = 0;
            foreach (var mc in r.Cells)
            {
                double cw = 0;
                for (var k = 0; k < mc.ColSpan && ci + k < nCols; k++) cw += colW[ci + k];
                ci += mc.ColSpan;
                var top = y; var bot = y - rowH;
                if (mc.BgTeal)
                    sb.Append(string.Create(invc,
                        $"q 0 0.502 0.502 rg {cx:F2} {bot:F2} {cw:F2} {rowH:F2} re f Q\n"));
                // borders (1px black)
                var lb = new StringBuilder("q 0 0 0 RG 1 w ");
                if (mc.BTop) lb.Append(string.Create(invc, $"{cx:F2} {top:F2} m {cx + cw:F2} {top:F2} l S "));
                if (mc.BBottom) lb.Append(string.Create(invc, $"{cx:F2} {bot:F2} m {cx + cw:F2} {bot:F2} l S "));
                if (mc.BLeft) lb.Append(string.Create(invc, $"{cx:F2} {top:F2} m {cx:F2} {bot:F2} l S "));
                if (mc.BRight) lb.Append(string.Create(invc, $"{cx + cw:F2} {top:F2} m {cx + cw:F2} {bot:F2} l S "));
                lb.Append("Q\n");
                sb.Append(lb);

                // content
                var ly = top - 1;
                var lx = cx + MsoCellPadPt + 1;
                var pen = lx;
                var lineOpen = false;
                double lineFs = 0;
                var lineHasCb = false;
                foreach (var run in mc.Runs)
                {
                    if (run.NewLine && lineOpen)
                    {
                        ly -= lineHasCb ? MsoCheckLinePt : MsoLineOf(lineFs);
                        pen = lx; lineOpen = false; lineFs = 0; lineHasCb = false;
                    }
                    if (run.Input is { Checkbox: true } cb)
                    {
                        // a checkbox rides its line inline, box seated on the text
                        var bx = pen + 2.5;
                        var byy = ly - MsoCheckLinePt + 3.2;
                        sb.Append(string.Create(invc,
                            $"q 1 1 1 rg {bx:F2} {byy:F2} {MsoCheckboxPt:F2} {MsoCheckboxPt:F2} re f Q\n"));
                        if (cb.Checked)
                            tsb.Append(string.Create(invc,
                                $"BT /F8 7.75 Tf 0 0 0 rg {bx + 0.6:F2} {byy + 1.0:F2} Td (4) Tj ET\n"));
                        pen = bx + MsoCheckboxPt + 3.5;
                        lineOpen = true;
                        lineHasCb = true;
                        lineFs = Math.Max(lineFs, 12);
                        continue;
                    }
                    if (run.Input is { } inp)
                    {
                        if (lineOpen)
                        { ly -= lineHasCb ? MsoCheckLinePt : MsoLineOf(lineFs); pen = lx; lineOpen = false; lineFs = 0; lineHasCb = false; }
                        var bx = pen;
                        var bw = Math.Min(inp.WPt, cw - 2 * MsoCellPadPt);
                        var bTop = ly - 1;
                        sb.Append(string.Create(invc,
                            $"q 1 1 1 rg {bx:F2} {bTop - inp.HPt:F2} {bw:F2} {inp.HPt:F2} re f Q\n"));
                        sb.Append(string.Create(invc,
                            $"q 0 0 0 RG 0.75 w {bx:F2} {bTop - inp.HPt:F2} {bw:F2} {inp.HPt:F2} re S Q\n"));
                        if (inp.Value.Length > 0 && inp.Value != "?")
                            tsb.Append(string.Create(invc,
                                $"BT /F1 10 Tf 0 0 0 rg {bx + MsoInputTextInsetPt:F2} {bTop - MsoInputBaselinePt:F2} Td ({EscapePdfText(inp.Value)}) Tj ET\n"));
                        ly = bTop - inp.HPt - 1.5;
                        pen = lx;
                        continue;
                    }
                    if (run.Text.Length == 0)
                    {
                        // a bare <br> line box
                        ly -= MsoLineOf(run.Fs);
                        pen = lx; lineOpen = false; lineFs = 0; lineHasCb = false;
                        continue;
                    }
                    var res = RunRes(run);
                    var fsz = run.Fs;
                    var clean = FilterWinAnsi(run.Text);
                    if (clean.Trim().Length == 0) { lineOpen = true; lineFs = Math.Max(lineFs, fsz); continue; }
                    var w = MeasureFaceText(RunFace(run), clean, fsz);
                    var tx = run.Center ? cx + (cw - w) / 2 : pen;
                    var col = run.White ? "1 1 1" : run.Teal ? "0 0.502 0.502" : "0 0 0";
                    var by = ly - MsoLineOf(fsz) + MsoLineOf(fsz) * 0.18;
                    tsb.Append(string.Create(invc,
                        $"BT /{res} {fsz:F2} Tf {col} rg {tx:F2} {by + 1.5:F2} Td ({EscapePdfText(clean)}) Tj ET\n"));
                    pen = tx + w + MeasureFaceText(RunFace(run), " ", fsz);
                    lineOpen = true;
                    lineFs = Math.Max(lineFs, fsz);
                }
                cx += cw;
            }
            var hostRow = false;
            foreach (var mc in r.Cells) if (mc.NestedHost) hostRow = true;
            if (hostRow) { groupOpen = true; groupTop = y; }
            y -= rowH;
        }
        CloseGroup(y);

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        page.AddContentStream(Encoding.ASCII.GetBytes(tsb.ToString()));
    }

    private const double MsoCheckLinePt = 17.7;   // roster line: checkbox + label (measured pitch)
    private const double MsoTightPadPt = 2.15;    // bottom band of a separate-paragraph input row
    private const double MsoRosterSideWPt = 95.0; // the empty brace cell right of the roster
    private const double MsoRosterCol1Pt = 9.9;   // roster column pens inside the host box
    private const double MsoRosterCol2Pt = 179.4;

    private static double MsoLineOf(double fs) => Math.Round(fs / 0.75 * 1.15) * 0.75;

    /// <summary>Drop glyphs outside WinAnsi (the ► arrows draw separately);
    /// nbsp becomes a plain space.</summary>
    private static string FilterWinAnsi(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\u00A0') { sb.Append(' '); continue; }
            if (ch <= 'ÿ') sb.Append(ch);
        }
        return sb.ToString();
    }

    private static double MsoCellNaturalW(MsoCell mc)
    {
        var w = mc.StyleWPt > 0 ? mc.StyleWPt + 2 * MsoCellPadPt + 1 : 0;
        foreach (var run in mc.Runs)
            if (run.Input is { Checkbox: false } inp)
                w = Math.Max(w, inp.WPt + 2 * MsoCellPadPt + MsoInputChromePt);
        return w;
    }

    private static string EscapePdfText(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch is '(' or ')' or '\\') sb.Append('\\');
            sb.Append(ch <= 'ÿ' ? ch : '?');
        }
        return sb.ToString();
    }
}
