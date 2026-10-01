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
    /// <summary>Emits one table row: each cell wrapped at its content width, centred vertically in the row, th centred and td flush left; the cursor drops by the padded line stack and the spacing below.</summary>
    private static void EmitEscNlRow(EscNlFooterState ef, List<(List<string> lines, bool bold)> r)
    {
        var wrapped = new List<List<string>>();
        var rowLines = 1;
        for (var c = 0; c < r.Count; c++)
        {
            var lines2 = Wrap(ef, r[c].lines, r[c].bold, ef.colBox[c] - 2 * EscNlCellPadding);
            wrapped.Add(lines2);
            rowLines = Math.Max(rowLines, lines2.Count);
        }
        var rowTop = ef.topCursor - EscNlCellPadding;
        var cellX = ef.tableLeft + EscNlCellSpacing;
        for (var c = 0; c < r.Count; c++)
        {
            var (_, bold) = r[c];
            var contentX = cellX + EscNlCellPadding;
            var contentW = ef.colBox[c] - 2 * EscNlCellPadding;
            var vOff = (rowLines - wrapped[c].Count) * ef.rootBox / 2;
            for (var li = 0; li < wrapped[c].Count; li++)
            {
                var ln = wrapped[c][li];
                var x = bold
                    ? contentX + Math.Max(0, (contentW - Measure(ef, ln, bold)) / 2)
                    : contentX;
                EmitRun(ef, ln, bold, x, rowTop - vOff - li * ef.rootBox - ef.baseDrop);
            }
            cellX += ef.colBox[c] + EscNlCellSpacing;
        }
        ef.topCursor -= 2 * EscNlCellPadding + rowLines * ef.rootBox + EscNlCellSpacing;
    }

    /// <summary>Solves the column boxes with HTML default chrome over the full band: min/max content widths per column, slack shared in proportion when the band allows.</summary>
    private static void SolveEscNlColumns(EscNlFooterState ef)
    {
        ef.nCols = 0;
        foreach (var r in ef.rows) ef.nCols = Math.Max(ef.nCols, r.Count);
        ef.minBox = new double[ef.nCols];
        ef.maxBox = new double[ef.nCols];
        for (var c = 0; c < ef.nCols; c++) ef.minBox[c] = ef.maxBox[c] = 2 * EscNlCellPadding;
        foreach (var r in ef.rows)
            for (var c = 0; c < r.Count; c++)
            {
                var (cellLines, bold) = r[c];
                foreach (var ln in cellLines)
                {
                    ef.maxBox[c] = Math.Max(ef.maxBox[c], Measure(ef, ln, bold) + 2 * EscNlCellPadding);
                    foreach (var w in ln.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        ef.minBox[c] = Math.Max(ef.minBox[c], Measure(ef, w, bold) + 2 * EscNlCellPadding);
                }
            }
        ef.avail = ef.bandW - (ef.nCols + 1) * EscNlCellSpacing;
        ef.sumMin = 0;
        ef.sumSlack = 0;
        for (var c = 0; c < ef.nCols; c++) { ef.sumMin += ef.minBox[c]; ef.sumSlack += ef.maxBox[c] - ef.minBox[c]; }
        ef.colBox = new double[ef.nCols];
        for (var c = 0; c < ef.nCols; c++)
        {
            ef.colBox[c] = ef.minBox[c];
            if (ef.avail > ef.sumMin && ef.sumSlack > 1e-9)
                ef.colBox[c] = Math.Min(ef.maxBox[c],
                    ef.minBox[c] + (ef.avail - ef.sumMin) * (ef.maxBox[c] - ef.minBox[c]) / ef.sumSlack);
        }
    }

    /// <summary>Splits the pre- and post-table text at the center boundary (the fostered text glued to the centred part) and parses the table rows, bare th cells before the first tr forming their own row. False when the table has no rows.</summary>
    private static bool ParseEscNlFlowAndRows(EscNlFooterState ef)
    {
        ef.centerOpen = Regex.Match(ef.preMk, @"<center\b[^>]*>", RegexOptions.IgnoreCase);
        ef.preOutside = TextOf(ef.centerOpen.Success ? ef.preMk[..ef.centerOpen.Index] : ef.preMk);
        ef.preCenter = ef.centerOpen.Success ? TextOf(ef.preMk[ef.centerOpen.Index..]) : "";
        ef.fostered = TextOf(EscNlCellRegex.Replace(ef.tblMk, ""));
        ef.tableCentred = ef.centerOpen.Success;
        ef.preCentreRun = ef.preCenter + ef.fostered;
        ef.centerClose = Regex.Match(ef.postMk, @"</center\s*>", RegexOptions.IgnoreCase);
        ef.postCenter = TextOf(ef.centerClose.Success ? ef.postMk[..ef.centerClose.Index] : "");
        ef.postOutside = TextOf(ef.centerClose.Success ? ef.postMk[ef.centerClose.Index..] : ef.postMk);

        ef.rows = new List<List<(List<string> lines, bool bold)>>();
        ef.firstTr = EscNlRowRegex.Match(ef.tblMk);
        ef.headSpan = ef.firstTr.Success ? ef.tblMk[..ef.firstTr.Index] : ef.tblMk;
        if (EscNlCellRegex.IsMatch(ef.headSpan)) AddRow(ef, ef.headSpan);
        foreach (Match rm in EscNlRowRegex.Matches(ef.tblMk)) AddRow(ef, rm.Groups["r"].Value);
        if (ef.rows.Count == 0) return false;
        return true;
    }

    /// <summary>Strips the doctype and style, locates the table's markup span and the serif line box; false when the markup holds no table or the band is too narrow.</summary>
    private static bool LocateEscNlTable(EscNlFooterState ef)
    {
        ef.src = EscNlStyleRegex.Replace(EscNlDoctypeRegex.Replace(ef.html, ""), "");
        ef.tblOpen = Regex.Match(ef.src, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        ef.tblClose = Regex.Match(ef.src, @"</table\s*>", RegexOptions.IgnoreCase);
        if (!ef.tblOpen.Success || !ef.tblClose.Success || ef.tblClose.Index < ef.tblOpen.Index) return false;
        ef.preMk = ef.src[..ef.tblOpen.Index];
        ef.tblMk = ef.src[ef.tblOpen.Index..(ef.tblClose.Index + ef.tblClose.Length)];
        ef.postMk = ef.src[(ef.tblClose.Index + ef.tblClose.Length)..];

        ef.rootLine = SerifLineBox(EscNlFontSize);
        ef.rootBox = ef.rootLine.Box;
        ef.baseDrop = ef.rootLine.Drop;
        ef.desc = _serifDescFrac * EscNlFontSize;
        ef.bandW = ef.bandRight - ef.bandLeft;
        if (ef.bandW < 40) return false;
        return true;
    }
}
