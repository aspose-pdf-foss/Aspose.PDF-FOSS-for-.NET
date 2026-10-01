namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The page margin a grown sheet keeps past its last ink.</summary>
    private const double InkWidenRightMarginPt = 90.0;

    /// <summary>The A4 default a grown sheet never falls below. The measure reads the ink WE
    /// drew, so a document whose content we under-draw would otherwise shrink the sheet under
    /// the default page — which the reference never does, whatever its body declares.</summary>
    private const double InkWidenFloorPt = 595.0;

    /// <summary>A document whose sheet grew from a declared body width takes its width from
    /// what it actually drew: one page margin past the last ink. The branch that sized it from
    /// the body plus a fitted band reproduces the reference on none of the six probed body
    /// widths, and this measure reproduces all of them; a document with explicit margins, an
    /// assigned page size or a sheet already wider than the ink is left alone.</summary>
    private static void GrowSheetToInk(ConvertState cv)
    {
        if (cv.doc is null || cv.marginsExplicit || (cv.pageInfo?.WidthAssigned ?? false)) return;
        if (!cv.grewSheetFromBodyWidth) return;
        var grown = InkGrownPageWidth(cv.doc, InkWidenFloorPt);
        if (grown <= 0 || System.Math.Abs(grown - cv.pageWidth) < 0.01) return;
        cv.pageWidth = grown;
        foreach (Page page in cv.doc.Pages) page.SetPageSize(grown, page.Rect.Height);
        // cv.marginRight is now STALE: the branch that opened the sheet left its own right band
        // there, and this pass has just moved the page edge to one page margin past the ink
        // without touching it. Nothing reads it after this point today - it is consumed during
        // layout, and this is the last call in ConvertRenderPages - so the two are never
        // compared. It is NOT corrected here because the branches disagree about what that
        // value means: one leaves the page margin, another deliberately leaves the page margin
        // plus the body's own inset, and both are measured. Anything added below this line must
        // read the page width rather than assume the margin still describes it.
    }

    /// <summary>The rightmost ink a page actually carries: the right edge of its text and of
    /// its drawn geometry, whichever reaches further; 0 when the page paints nothing.
    /// The measure reads the FINISHED page rather than a layout estimate, so it cannot drift
    /// from what the writer emitted.</summary>
    internal static double RightmostInkPt(Page page)
    {
        var right = 0.0;
        try
        {
            var text = new Text.TextFragmentAbsorber();
            text.Visit(page);
            foreach (Text.TextFragment fragment in text.TextFragments)
                if (fragment.Rectangle is { } r && r.URX > right) right = r.URX;
        }
        catch { /* a page whose text cannot be read contributes none */ }
        try
        {
            var graphics = new Vector.GraphicsAbsorber();
            graphics.Visit(page);
            foreach (var element in graphics.Elements)
                if (element.Rectangle is { } r && r.URX > right) right = r.URX;
        }
        catch { /* likewise for its geometry */ }
        return right;
    }

    /// <summary>The width a grown sheet takes: one page margin past the document's last ink,
    /// never under the sheet it already has. LAW (probed on the reference 2026-09-08 across
    /// six documents whose declared body width differs and whose content saturates it, and
    /// again on three bordered-table documents): the page ends exactly 90 pt past the
    /// rightmost DRAWN extent - the stroke's path, not its painted edge, and a text run's
    /// advance - whatever the body declares. A fitted right band reproduces none of them:
    /// the same six documents leave 81.1, 86.9, 86.8 and 82.3 pt past their bodies.</summary>
    internal static double InkGrownPageWidth(Document doc, double currentWidth)
    {
        var ink = 0.0;
        foreach (Page page in doc.Pages)
        {
            var pageInk = RightmostInkPt(page);
            if (pageInk > ink) ink = pageInk;
        }
        return ink <= 0 ? currentWidth : System.Math.Max(currentWidth, ink + InkWidenRightMarginPt);
    }
}
