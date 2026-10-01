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
    /// <summary>The escaped-newline footer's serif size: the HTML cell default.</summary>
    private const double EscNlFontSize = HtmlCellFontSize;

    /// <summary>Render the escaped-newline footer fragment (see the block comment
    /// in HtmlCells.cs) top-down from <paramref name="bandTop"/> across the band
    /// [<paramref name="bandLeft"/>, <paramref name="bandRight"/>]. Returns the
    /// content stream, or null when the markup is not this dialect's
    /// (no literal "\n" text or no table). <c>consumedH</c> is the
    /// stack height consumed below <paramref name="bandTop"/>.</summary>
    internal static (byte[]? result, double consumedH) DrawEscapedNewlineFooterHtml(Page page, string? html, double bandLeft, double bandRight, double bandTop)
    {
        double consumedH = default;
        consumedH = 0;
        if (page is null || string.IsNullOrEmpty(html)) return (null, consumedH);
        if (!html.Contains("\\n", StringComparison.Ordinal)) return (null, consumedH);
        var ef = new EscNlFooterState();
        ef.page = page;
        ef.html = html;
        ef.bandLeft = bandLeft;
        ef.bandRight = bandRight;
        ef.bandTop = bandTop;
        var content = DrawEscapedNewlineFooterCore(ef);
        consumedH = ef.consumedH;
        return (content, consumedH);
    }

    /// <summary>The body of the escaped-newline footer render, on its own state.</summary>
    private static byte[]? DrawEscapedNewlineFooterCore(EscNlFooterState ef)
    {
        if (BoldSerifTtf() is null || _serifTtf is null || _serifBoldTtf is null) return null;

        if (!LocateEscNlTable(ef)) return null;

        if (!ParseEscNlFlowAndRows(ef)) return null;

        SolveEscNlColumns(ef);
        ef.fontDict = ResolvePageFontDict(ef.page);
        ef.b = new ContentStreamBuilder();
        ef.topCursor = ef.bandTop - ef.desc;

        EmitFlowText(ef, ef.preOutside, centred: false);
        EmitFlowText(ef, ef.preCentreRun, centred: ef.tableCentred);

        ef.tableW = (ef.nCols + 1) * EscNlCellSpacing;
        for (var c = 0; c < ef.nCols; c++) ef.tableW += ef.colBox[c];
        ef.tableLeft = ef.tableCentred
            ? Math.Max(ef.bandLeft, ef.bandLeft + (ef.bandW - 2 * EscNlCenterSideMargin - ef.tableW) / 2)
            : ef.bandLeft;
        ef.topCursor -= EscNlCellSpacing;
        foreach (var r in ef.rows)
        {
            EmitEscNlRow(ef, r);
        }

        EmitFlowText(ef, ef.postCenter, centred: ef.tableCentred);
        EmitFlowText(ef, ef.postOutside, centred: false);

        ef.consumedH = ef.bandTop - ef.topCursor;
        return ef.b.Build();
    }
}
