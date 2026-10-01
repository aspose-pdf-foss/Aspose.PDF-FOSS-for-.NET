using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render an OutSystems bill-of-lading export at natural width and
    /// shrink it onto the authored sheet, or null without the fingerprint.</summary>
    private static Document? TryRenderOutSystemsExport(string html,
        double pageWidth, double pageHeight, double mL, double mR)
    {
        var os = new OutSystemsState();
        os.html = html;
        os.pageWidth = pageWidth;
        os.pageHeight = pageHeight;
        os.mL = mL;
        os.mR = mR;
        if (!os.html.Contains("aspNetHidden", StringComparison.OrdinalIgnoreCase)
            || !os.html.Contains("OSFillParent", StringComparison.Ordinal)
            || !os.html.Contains("ThemeGrid", StringComparison.Ordinal))
            return null;
        if (WinMetricsFor("Arial") is not { } am
            || WinMetricsFor("Times New Roman") is not { } tm)
            return null;

        os.elems = OsTopLevel(os.html);
        if (os.elems.Count(e => e.Kind == 't') != 8) return null;

        os.contentX = os.mL + OsUaBody;
        os.contentW = os.pageWidth - os.mL - os.mR - 2 * OsUaBody;
        os.q4 = os.contentW / 4;
        os.drop12 = MetricBaselineDrop(12.0, OsLine, am);
        os.dropTnr = MetricBaselineDrop(12.0, OsLine, tm);
        os.pMargin = 12.0 * am.sum;

        os.doc = new Document();
        os.page = os.doc.Pages.Add(os.pageWidth, os.pageHeight);
        EnsureFonts(os.page);
        EnsureFont(os.page, "Arial", "F8");
        EnsureFont(os.page, "ArialBold", "F9");
        EnsureFont(os.page, "TimesNewRoman", "F10");
        os.inv = System.Globalization.CultureInfo.InvariantCulture;

        os.y = OsUaBody;
        os.pendingGap = 0;
        os.natRight = os.contentX + os.contentW;
        os.tableIdx = 0;
        foreach (var el in os.elems)
        {
            switch (el.Kind)
            {
                case 'p':
                {
                    (var runs, _, _) = OsParseRuns(el.Frag);
                    var lines = OsWrap(runs, os.contentW);
                    if (lines.Count == 0)
                    {
                        // an empty paragraph: its margins collapse straight through
                        os.pendingGap = Math.Max(os.pendingGap, os.pMargin);
                        break;
                    }
                    os.y += Math.Max(os.pendingGap, os.pMargin);
                    for (var i = 0; i < lines.Count; i++)
                        EmitSegs(os, lines[i], os.contentX, os.y + i * OsLine + os.drop12);
                    os.y += lines.Count * OsLine;
                    os.pendingGap = os.pMargin;
                    break;
                }
                case 'h':
                {
                    os.y += Math.Max(os.pendingGap, os.pMargin);
                    HLine(os, os.contentX, os.contentX + os.contentW, os.y + OsBorder / 2, OsBorder);
                    HLine(os, os.contentX, os.contentX + os.contentW, os.y + OsHrH - OsBorder / 2, OsBorder);
                    os.y += OsHrH;
                    os.pendingGap = OsHrMarginBottom;
                    break;
                }
                case 't':
                {
                    os.y += os.pendingGap;
                    os.pendingGap = 0;
                    var rows = OsParseRows(el.Frag);
                    switch (os.tableIdx++)
                    {
                        case 0: os.y = OsHeader(os, rows, os.y); break;
                        case 1: os.y = OsGrid(os, rows, OsFromCols(os, rows), os.y); break;
                        case 2: os.y = OsBand(os, rows, os.y, centered: false); break;
                        case 3:
                        case 4: os.y = OsGrid(os, rows, OsEvenCols(os), os.y); break;
                        case 5: os.y = OsBand(os, rows, os.y, centered: true); break;
                        case 6: (os.y, os.natRight) = OsSignature(os, rows, os.y); break;
                        case 7: os.y = OsDate(os, rows, os.y); break;
                    }
                    break;
                }
            }
        }

        os.s = (os.pageWidth - os.mL - os.mR) / (os.natRight - os.mL);
        if (os.s is > 0 and < 1)
        {
            os.page.PrependContentStream(Encoding.ASCII.GetBytes(Compat.Format(os.inv,
                $"q {os.s:F5} 0 0 {os.s:F5} {os.mL * (1 - os.s):F2} {os.pageHeight * (1 - os.s):F2} cm\n")));
            Stream(os, "Q\n");
        }
        return os.doc;

    }
}
