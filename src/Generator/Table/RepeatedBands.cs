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
    /// <summary>Number of rows at the END of the table that form its FOOTER
    /// band. They are drawn under the last row each page holds -- on every page
    /// the table reaches, not once at its end -- and their height is held back
    /// from each page's budget so the body always leaves them room. The mirror
    /// of <see cref="RepeatingRowsCount"/>, and what a printed table's foot band
    /// asks for.</summary>
    public int RepeatingFooterRowsCount { get; set; }

    /// <summary>Makes the footer band a CARRIED-FORWARD band: it closes every
    /// page the table runs onto except the one the table ENDS on, and that last
    /// page keeps the room the band would have taken. A table that needs three
    /// pages with the band under all of them fits on two when the last one
    /// skips it.</summary>
    public bool RepeatingFooterSkipsLastPage { get; set; }

    /// <summary>Makes the repeating rows a CONTINUED-FROM band: the table opens
    /// with its body and <see cref="RepeatingRowsCount"/> rows stand only at the
    /// top of the pages it runs ONTO, so the first page gains the room the band
    /// would have taken.</summary>
    public bool RepeatingRowsSkipFirstPage { get; set; }

    /// <summary>The height a whole planned row takes as one slice of a REPEATED
    /// band -- a header re-emitted at a page's top or a footer standing at its
    /// foot. Its own MinRowHeight floors it, so a repeated row keeps the pitch
    /// the body rows are laid on rather than shrinking to its natural height.</summary>
    private double BandSliceHeight(RowPlan plan)
    {
        var contentH = plan.LineCount == 0
            ? plan.MinBlankHeight
            : plan.CssContentH > 0
                ? CssRowContentH(plan)
                : (plan.LineCount - 1) * plan.LineHeight + plan.TightLine;
        var sliceH = plan.LineCount == 0 || plan.IsBlankRow ? contentH : contentH + plan.VertPadding;
        var floor = MinRowFloor(plan);
        return floor > sliceH ? floor : sliceH;
    }

    /// <summary>Splits the planned rows into the body the pages are filled with
    /// and the footer band that closes every one of them, and holds the band's
    /// height back from the page budget: the body then breaks a whole band
    /// earlier, which is exactly the room the footer stands in.</summary>
    private void PlanFooterBand(MultiPageBuildState mp)
    {
        var band = Math.Max(0, Math.Min(RepeatingFooterRowsCount, mp.rowPlans.Count - mp.repeatCount));
        mp.bodyRowCount = mp.rowPlans.Count - band;
        mp.footerReserve = 0;
        mp.footerReleased = false;
        for (var r = mp.bodyRowCount; r < mp.rowPlans.Count; r++)
            mp.footerReserve += BandSliceHeight(mp.rowPlans[r]) + mp.rowGap;
        mp.pageBottom += mp.footerReserve;
    }

    /// <summary>Hands a CARRIED-FORWARD band's reserve back when the page that
    /// has just filled up is the one the table ends on: every body row left fits
    /// in the room the band was holding, so there is no page after this to carry
    /// anything to. True when the reserve was released, and the caller then lays
    /// the row it was about to defer on this page after all.</summary>
    private bool ReleaseCarriedForwardBand(MultiPageBuildState mp, int row)
    {
        if (!RepeatingFooterSkipsLastPage || mp.footerReserve <= 0) return false;
        var need = 0.0;
        for (var r = row; r < mp.bodyRowCount; r++) need += RowPlanHeight(mp.rowPlans[r]) + mp.rowGap;
        if (mp.currentY - need < mp.pageBottom - mp.footerReserve - 1e-3) return false;
        mp.pageBottom -= mp.footerReserve;
        mp.footerReserve = 0;
        mp.footerReleased = true;
        return true;
    }

    /// <summary>Lays the footer band under the last row the current page took.
    /// It joins the page's slices like any other row -- so the grid rules it
    /// off, fills it and closes beneath it exactly as it does the body -- and
    /// stands on every page the table reaches, the last one included, unless the
    /// band is a CARRIED-FORWARD one: that band belongs to the pages the table
    /// runs PAST, and the page it ends on closes with its own last row.</summary>
    private void EmitFooterBand(MultiPageBuildState mp, bool tableEnds)
    {
        // A page whose reserve was handed back has no room for the band even if
        // something breaks the page after all: it was released as the last one.
        if (mp.footerReleased) return;
        if (tableEnds && RepeatingFooterSkipsLastPage) return;
        for (var r = mp.bodyRowCount; r < mp.rowPlans.Count; r++)
        {
            var fp = mp.rowPlans[r];
            var sliceH = BandSliceHeight(fp);
            mp.slices.Add(new RowSlice
            {
                Plan = fp,
                LineStart = 0,
                LineCount = fp.LineCount,
                TopY = mp.currentY,
                Height = sliceH,
                RowIndex = r,
            });
            mp.currentY -= sliceH + mp.rowGap;
        }
    }
}
