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
    /// <summary>Parse an HtmlFragment whose markup uses only the b/strong/small/div/br
    /// family into HTML-engine cell lines: serif runs (bold via b/strong, 10pt via small
    /// — nested smalls do NOT compound), greedy kerned wrap at <paramref name="availWidth"/>,
    /// div/small as block boundaries, br as a forced (possibly empty) line. Returns null
    /// when the markup falls outside the family (legacy path) or the faces are missing.</summary>
    private static List<CellLine>? ParseHtmlEngineCell(string? html, double availWidth,
        double baseSize = HtmlCellFontSize, bool breakWords = false, bool plainText = false)
    {
        var hc = new HtmlEngineCellState();
        hc.html = html;
        hc.availWidth = availWidth;
        hc.baseSize = baseSize;
        hc.breakWords = breakWords;
        hc.plainText = plainText;
        if (string.IsNullOrEmpty(hc.html)) return null;
        // Markup-free text is the legacy (inherited-face) path unless the caller's
        // dialect sets every HtmlFragment through the engine.
        if (hc.html.IndexOf('<') < 0 && !hc.plainText) return null;
        if (BoldSerifTtf() is null) return null;
        if (hc.baseSize <= 0) hc.baseSize = HtmlCellFontSize;
        hc.smallSize = hc.baseSize * HtmlSmallFontSize / HtmlCellFontSize;
        hc.rootLine = SerifLineBox(hc.baseSize);
        hc.rootBox = hc.rootLine.Box;
        hc.baseDrop = hc.rootLine.Drop;
        // Every tag present must belong to the allowed family.
        foreach (Match any in AnyTagRegex.Matches(hc.html))
            if (!HtmlEngineTagRegex.IsMatch(any.Value)) return null;
        if (!EngineAcceptsStructure(hc.html)) return null;

        hc.lines = new List<CellLine>();
        hc.curRuns = new List<HtmlRun>();
        hc.curX = 0;
        hc.boldDepth = 0;
        hc.smallDepth = 0;
        hc.anyText = false;
        hc.anchors = new Stack<string>();
        hc.spans = new List<(Color? Color, double Size, bool Underline)>();
        hc.lists = new List<(bool Ordered, int Counter)>();
        hc.lineIndent = 0.0;
        hc.pendingMarker = null;

        hc.pos = 0;
        foreach (Match m in HtmlEngineTagRegex.Matches(hc.html))
        {
            AbsorbHtmlEngineTag(hc, m);
        }
        if (hc.pos < hc.html.Length) EmitText(hc, hc.html.Substring(hc.pos));
        CommitFloats(hc);
        FlushLine(hc, force: false);

        if (!hc.anyText || hc.lines.Count == 0) return null;
        hc.lastLine = hc.lines[^1];
        // An inline-family block ends at its last baseline + descent; a LIST ITEM's last
        // line keeps its full line box, as a CSS block does (probed 2026-09-07: a cell of
        // one two-line <li> at 12 pt is 27.0 tall inside its padding - two 13.5 boxes -
        // where the descent rule would give 26.89).
        if (!hc.lastLine.InListItem && !hc.lastLine.InBlock)
            hc.lastLine.BoxH = hc.lastLine.BaseOff + _serifDescFrac * hc.lastLine.FontSize;
        return hc.lines;
    }
}
