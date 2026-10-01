using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The over-declared grid keeps its own columns and takes the sheet the ink asks for.</summary>
    private static void ConfigureOverDeclaredGridTable(TableBlockState tb, HtmlDocProfile profile, Table table, HtmlFlowCursor flow, double marginLeft, double pageWidth)
    {
        if (profile.overDeclaredGridDoc)
        {
            table.UsableWidthOverride = flow.contentWidth - UaBodyMarginPt
                - OverDeclaredHostChromePt;
            // The shipped-era reference stops its bands a left-margin's
            // width short of the right edge (probed off the template:
            // 96.75 → pageW−96 at every section).
            table.HtmlBandBleedRightPt = pageWidth - marginLeft;
            // <table align="center"> centres its NATURAL box in the
            // standard box (the 物業地點 address card sits mid-page).
            if (tb.renderNatW > 0 && tb.renderNatW < table.UsableWidthOverride
                && Regex.IsMatch(
                    Regex.Match(tb.fhTableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase).Value,
                    @"align\s*=\s*[""']?center", RegexOptions.IgnoreCase))
            {
                table.FlowLeftOffset = marginLeft
                    + (table.UsableWidthOverride - tb.renderNatW) / 2;
                table.UsableWidthOverride = tb.renderNatW;
            }
        }
    }
}
