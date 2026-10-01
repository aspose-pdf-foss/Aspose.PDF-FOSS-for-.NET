using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
// Markup text extraction, row parsing, kerned measure and wrap, and the run and flow-text emitters behind the escaped-newline footer.
    // Tag-free text of a markup span, entities decoded, whitespace-only → "".
    private static string TextOf(string mk)
    {
        var t = DecodeHtmlEntities(AnyTagRegex.Replace(mk, ""));
        return t.Trim().Length == 0 ? "" : t;
    }

    private static List<string> CellLines(string inner)
    {
        var outLines = new List<string>();
        foreach (var piece in EscNlBrRegex.Split(inner))
        {
            var t = DecodeHtmlEntities(AnyTagRegex.Replace(piece, "")).Trim(' ');
            if (t.Length > 0) outLines.Add(t);
        }
        return outLines;
    }

    private static void AddRow(EscNlFooterState ef, string rowMk)
    {
        var cells = new List<(List<string>, bool)>();
        foreach (Match cm in EscNlCellRegex.Matches(rowMk))
            cells.Add((CellLines(cm.Groups["c"].Value),
                cm.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase)));
        if (cells.Count > 0) ef.rows.Add(cells);
    }

    private static double Measure(EscNlFooterState ef, string s, bool bold) =>
        MeasureWidthKerned(s, EscNlFontSize, bold ? _serifBoldTtf! : _serifTtf!);

    // ── wrap the cells at their content widths ──
    private static List<string> Wrap(EscNlFooterState ef, List<string> cellLines, bool bold, double contentW)
    {
        var res = new List<string>();
        foreach (var ln in cellLines)
        {
            var cur = "";
            foreach (var w in ln.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var cand = cur.Length == 0 ? w : cur + " " + w;
                if (cur.Length == 0 || Measure(ef, cand, bold) <= contentW + 0.01) cur = cand;
                else { res.Add(cur); cur = w; }
            }
            if (cur.Length > 0) res.Add(cur);
        }
        return res;
    }

    private static void EmitRun(EscNlFooterState ef, string text, bool bold, double x, double baseline)
    {
        var ttf = bold ? _serifBoldTtf! : _serifTtf!;
        var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
            ef.fontDict, ttf, bold ? "Times New Roman Bold" : "Times New Roman",
            text, stripSpacesInBaseFont: true);
        ef.b.BeginText();
        ef.b.SetFont(resName, EscNlFontSize);
        ef.b.MoveTextPosition(x, baseline);
        if (KernAdjustments(text, ttf) is { } kern) ef.b.ShowTextHexKerned(hex, kern);
        else ef.b.ShowTextHex(hex);
        ef.b.EndText();
    }

    private static void EmitFlowText(EscNlFooterState ef, string text, bool centred)
    {
        if (text.Length == 0) return;
        var cur = "";
        void Flush()
        {
            if (cur.Length == 0) return;
            var w = Measure(ef, cur, false);
            var x = centred ? ef.bandLeft + Math.Max(0, (ef.bandW - w) / 2) : ef.bandLeft;
            // A flow line whose baseline falls below the page bottom is not
            // drawn (measured: the after-</center> run renders at baseline
            // 3.61 on the taller band and vanishes at −0.39 on the shorter).
            var flowBase = ef.topCursor - ef.baseDrop;
            if (flowBase >= 0) EmitRun(ef, cur, false, x, flowBase);
            ef.topCursor -= ef.rootBox;
            cur = "";
        }
        foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var cand = cur.Length == 0 ? w : cur + " " + w;
            if (cur.Length == 0 || Measure(ef, cand, false) <= ef.bandW + 0.01) cur = cand;
            else { Flush(); cur = w; }
        }
        Flush();
    }
}
