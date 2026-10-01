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
