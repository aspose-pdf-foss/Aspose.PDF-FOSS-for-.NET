using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The UBL invoice-frame dialect (`#invoice-frame` + `.invoice-viewer` + the UBL
// class namespace): a Danish e-invoice export whose PRINT media rules put the
// tables in Verdana at 14px while the frame's own stack resolves to Arial for
// bare divs (the VARER headline). It lays on SYMMETRIC 96 pt side
// margins one UA body margin below the 72 pt top, paces table cells on the
// sheet's 20px line-height with the cell content inset one collapsed-border
// step (0.75) below the row top, sizes the item table's columns at each
// heading's width plus the 10px column padding (the 40% description column
// declared), floats the totals table right, and collapses the supplier block's
// 50px margin-top with the spacer's 20px bottom margin. Constants not read from
// the stylesheet are measured on the expected render.
internal static partial class HtmlToPdfConverter
{
    private const double UblMarginX = 96.0;
    private const double UblTop = 78.0;            // 72 pt margin + the UA 6 pt body margin
    private const double UblCellInset = 0.75;      // cell content sits one collapsed border below the row top
    private const double UblRefLinePt = 14.25;     // the floated references table's 19px line (measured)

    /// <summary>Render a UBL invoice-frame export, or null when the page does
    /// not carry the dialect's fingerprint.</summary>
    private static Document? TryRenderUblInvoice(string html,
        double pageWidth, double pageHeight)
    {
        var ub = new UblInvoiceState();
        ub.html = html;
        ub.pageWidth = pageWidth;
        ub.pageHeight = pageHeight;
        if (!Regex.IsMatch(ub.html, @"id\s*=\s*[""']invoice-frame[""']", RegexOptions.IgnoreCase)
            || ub.html.IndexOf("invoice-viewer", StringComparison.OrdinalIgnoreCase) < 0
            || ub.html.IndexOf("UBLInvoiceLine", StringComparison.OrdinalIgnoreCase) < 0)
            return null;
        if (WinMetricsFor("Verdana") is not { } vm || WinMetricsFor("Arial") is not { } am)
            return null;
        ub.am = am;
        ub.vm = vm;

        ub.cellFs = 10.5;
        ub.lineH = 15.0;
        ub.cellDrop = MetricBaselineDrop(ub.cellFs, ub.lineH, ub.vm);

        if (!ParseUblInvoice(ub)) return null;

        ub.doc = new Document();
        ub.page = ub.doc.Pages.Add(ub.pageWidth, ub.pageHeight);
        EnsureFonts(ub.page);
        EnsureFont(ub.page, "Verdana", "FV");
        EnsureFont(ub.page, "Verdana-Bold", "FW");
        ub.invc = System.Globalization.CultureInfo.InvariantCulture;
        ub.contentW = ub.pageWidth - 2 * UblMarginX;
        ub.rightEdge = UblMarginX + ub.contentW;

        DrawUblHeader(ub);

        DrawUblItems(ub);

        DrawUblFooter(ub);
        return ub.doc;
    }

    /// <summary>The inline FAKTURA h2 wraps at the address cell's width — the
    /// sheet's runs break after the heading's first word (measured: two lines).</summary>
    private static List<string> WrapH2Lines(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1) return new List<string> { text };
        return new List<string> { words[0] + " ", string.Join(" ", words[1..]) };
    }
}
