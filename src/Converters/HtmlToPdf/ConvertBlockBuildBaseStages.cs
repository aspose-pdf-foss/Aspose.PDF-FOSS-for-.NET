using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The flow's base line box and margins: the UA grid sheet's body pitch, the quirks chain sheet's and the Word export's own.</summary>
    private static void ResolveFlowBaseTypography(ConvertState cv)
    {
        if (cv.profile.uaGridSheet && cv.profile.bodyLineHeightPt <= 0 && cv.profile.bodyCssFontPt > 0
            && cv.css.TryGetValue("body", out var uaBodyRule) && uaBodyRule.TryGetValue("line-height", out var uaBodyLh)
            && MetricControlLineHeightPt(uaBodyLh.Trim(), cv.profile.bodyCssFontPt) is { } uaBodyLhPt && uaBodyLhPt > 0)
        {
            cv.profile.bodyLineHeightPt = uaBodyLhPt;
            if (uaBodyLh.Trim().EndsWith('%') || double.TryParse(uaBodyLh.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                cv.profile.bodyLineHeightFactor = uaBodyLhPt / cv.profile.bodyCssFontPt;
        }
        cv.profile.sheetTypographyDoc = ((cv.profile.docChainRules is not null && !cv.profile.docChainCellRulesOnly) || cv.profile.wordExportDoc || (cv.profile.uaGridSheet)) && !cv.uaFlow
            && !cv.printCoverDoc && !cv.profile.chartCardDoc && !cv.profile.floatBothSidesDoc;
        // …and the line-box seating only where the flat flow has no dialect calibration of its
        // own: the metric flow already seats inside CSS boxes, and a sheet that pads every div
        // builds its bands from container padding the box flow does not model.
        cv.profile.sheetBoxFlow = cv.profile.sheetTypographyDoc && !cv.profile.metricFlow && !SheetPadsDivs(cv.css);
        // The chain-dialect sheet lays its body box the UA way: the content opens one UA body
        // margin under the page's top margin and keeps that inset on the right as on the left
        // (measured: a UA body's first <br/> box starts at 78 and a bordered wrapper div spans
        // 96..499 on the A4 sheet). A sheet that grows to its widest grid re-settles its right
        // margin when it grows.
        // (the DOCTYPE-less legacy export only: a transitional DTD reads in quirks mode too, but the
        // one green such document - a 14-grid report with an external sheet - was calibrated without
        // these laws and went red under them; both references carry no doctype at all)
        var ancestorCells = _quirksRowStrut && !Regex.IsMatch(cv.html, @"<!doctype", RegexOptions.IgnoreCase)
            && SheetScopesCellsThroughContainer(cv);
        _quirksChainSheet = cv.profile.sheetTypographyDoc && ancestorCells;
        _ancestorGridSheet = ancestorCells;
        if (_quirksChainSheet && !cv.marginsExplicit && !cv.profile.bodyZeroMargin)
        {
            cv.marginTop = 72.0 + UaBodyMarginPt;
            if (!(cv.pageInfo?.WidthAssigned ?? false)) cv.marginRight = 90.0 + UaBodyMarginPt;
        }
        // A sheet that scopes its cells through a container and states nothing else tree-addressed
        // opens its content one UA body margin under the page's 72 pt top, like every other quirks
        // sheet that lays its body box the UA way - but it keeps the calibrated flow's right margin,
        // which the sheet it grows to is measured from.
        if (cv.profile.docChainCellRulesOnly && _quirksRowStrut
            && !cv.marginsExplicit && !cv.profile.bodyZeroMargin)
            cv.marginTop = 72.0 + UaBodyMarginPt;
        // The Word export lays on the UA page box: the content opens one UA body margin under the
        // 72 pt top and its lines end 90 from the right (probed: h1 baseline 100.2, a 12 pt line
        // wraps at 505), where the calibrated flow seats 89 / 72.
        if (cv.profile.wordExportDoc && !cv.marginsExplicit)
        {
            cv.marginTop = 72.0 + UaBodyMarginPt;
            if (!(cv.pageInfo?.WidthAssigned ?? false)) cv.marginRight = 90.0;
        }
    }
}
