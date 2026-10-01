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
    // 7pt Arial labels over text inputs, a checkbox roster). The expected render
    // renders it as a single landscape-wide page:
    //   page W = margin 90 + body inset 6 + Σ solved columns + gutter + margin 90
    // Inputs draw as 1px boxes with the value in 10pt Helvetica; a stylesheet
    // #ID { width } rule sizes a control, otherwise the measured default box.
    // All constants below are measured values (probes q1..q3).

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
