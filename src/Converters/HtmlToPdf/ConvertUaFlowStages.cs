using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The calibrated UA flow sets the metric page box: the sheet's own column width where the document declares one, else the probed body box, with the body margin folded into the page edges.</summary>
    private static void ResolveUaFlowWidth(ConvertState cv)
    {
        // The Word-filtered text column the flow justifies to (see the
        // margin override below: sheet = 96 + column + 96), and the drop of its
        // broken-image placeholders under the 72 pt top margin (both measured).
        // The UA paragraph margin (1.12 em of the 12pt base), as the metric flow's
        // own <p> blocks carry it - probed on the plain-serif document ladder:
        // p-to-p pitch 26.94 = 13.5 line box + this margin, collapsed pairwise.
        const double MsoTextColumnWPt = 529.8;
        if (cv.uaFlow)
        {
            cv.profile.metricFlow = true;
            // The UA serif, unless the body rule pinned its own face at the UA base.
            cv.profile.metricFace = cv.profile.uaStdSerif && cv.uaBodyFace is not null ? cv.uaBodyFace : "Times New Roman";
            cv.bodyMarT = 6.0;
            // Edge-to-edge sheets: the first paragraph's UA margin-top collapses
            // with the body margin — the content opens max(6, 12) below the top
            // margin, plus the engine's measured first-line seat (baseline lands
            // at 96.2: 72 + 15.35 + the metric drop).
            if (cv.edgeToEdgeDoc) cv.bodyMarT = 15.35;
            // Per-side-touched defaults: an untouched side keeps the renderer
            // default (the caller authored only the sides they set).
            var uaPerSide = cv.marginsExplicit && (cv.pageMargin?.IsTouched ?? false)
                && cv.pageMargin!.HtmlPerSideDefaults;
            // a zero body margin keeps the bare page margin — no 6pt body inset
            if (cv.profile.bodyZeroMargin && !cv.profile.bodySideOnlyZero) cv.bodyMarT = 0.0;
            cv.marginLeft = (cv.marginsExplicit
                ? (uaPerSide && !cv.pageMargin!.LeftTouched ? 90.0 : cv.pageMargin!.Left) : 90.0)
                + (cv.profile.bodyZeroMargin ? 0.0 : 6.0);
            cv.marginRight = (cv.marginsExplicit
                ? (uaPerSide && !cv.pageMargin!.RightTouched ? 90.0 : cv.pageMargin!.Right) : 90.0)
                // Edge-to-edge sheets get the UA body margin on the RIGHT too — a
                // width:100% table ends exactly one body margin short of the edge.
                + (cv.edgeToEdgeDoc ? 6.0 : 0.0);
            cv.marginTop = cv.marginsExplicit
                ? (uaPerSide && !cv.pageMargin!.TopTouched ? 72.0 : cv.pageMargin!.Top) : 72.0;
            cv.marginBottom = cv.marginsExplicit
                ? (uaPerSide && !cv.pageMargin!.BottomTouched ? 72.0 : cv.pageMargin!.Bottom) : 72.0;
            // The body tag's OWN top and side margins, when its style attribute is the flow's
            // typography (MEASURED, the no-doctype evaluation form: `margin-top:1in; margin-right:1in`
            // on the tag - the H1 box opens at 72 + 72, the grids and the HR span 96..W-162; the
            // bottom margin is the document's, not the page's - page 1 fills to the page margin).
            cv.bodyMarginRightPt = UaBodyMarginPt;
            if (cv.uaBodyFaceFromAttr && !cv.marginsExplicit && !cv.profile.bodyZeroMargin
                && BodyTagStyleAttr(cv.html) is { } bodyAttrStyle)
            {
                var bodyEm = cv.uaBodyFontPt > 0 ? cv.uaBodyFontPt : UaDefaultFontPt;
                if (BodyAttrMarginPt(bodyAttrStyle, "top", bodyEm) is { } attrTop) cv.bodyMarT = attrTop;
                if (BodyAttrMarginPt(bodyAttrStyle, "right", bodyEm) is { } attrRight)
                { cv.bodyMarginRightPt = attrRight; cv.marginRight = 90.0 + attrRight; }
                if (BodyAttrMarginPt(bodyAttrStyle, "left", bodyEm) is { } attrLeft)
                { cv.bodyMarginLeftPt = attrLeft; cv.marginLeft = 90.0 + attrLeft; }
            }
            // Fieldset worksheet: the body's own margin + padding ARE the content
            // offsets (no UA 6pt inset), and that padding blocks the doc-top
            // margin collapse — the first heading keeps its full margin.
            if (cv.fieldsetDoc)
            {
                cv.marginLeft = 90.0 + cv.fsBodyChromePt;
                cv.marginTop = 72.0 + cv.fsBodyChromePt;
                cv.bodyMarT = 0.0;
            }
            // Word-filtered pages lay on a SYMMETRIC 96 pt inset over the
            // measured 529.8 pt text column — the sheet is
            // 96 + 529.8 + 96 = 721.75, and the justified lines stretch to
            // exactly that column (measured on the filtered-page output).
            // That sheet is a GROWN one: the filtered page it was measured on carries an
            // absolutely positioned 817 px banner whose ink stands past the A4 content box,
            // and the sheet a filtered page ends on is one page margin past its widest
            // painted ink like any other (MEASURED, stable across releases: the filtered
            // e-mail export whose every line wraps inside the box stays on plain A4
            // 595 x 842 with its text column at 96..499). So a filtered page with nothing
            // wider than the default content box keeps the default sheet.
            if (cv.profile.msoFilteredDoc && !cv.marginsExplicit && !(cv.pageInfo?.WidthAssigned ?? false))
            {
                // The symmetric inset holds on every filtered page (MEASURED: the e-mail export's
                // own text box is 96..499 on the plain A4 sheet); only the GROWN sheet needs a
                // box wider than the content box to grow for.
                cv.marginRight = 90.0 + UaBodyMarginPt;
                if (cv.profile.msoFilteredGrownSheet)
                    cv.pageWidth = cv.marginLeft + MsoTextColumnWPt + cv.marginRight;
            }
            // The custom-font report keeps the UA body margin on BOTH sides of
            // its explicit zero margins — its reference wraps at exactly
            // page − 2×6 (a 602 pt line breaks out of the 600 box).
            if (cv.customFontFaceDoc && cv.marginsExplicit)
                cv.marginRight += UaBodyMarginPt;
            // A width:100% BODY spans the bare page margins instead of losing
            // the 6 pt UA body inset to its width — the sheet grows by that
            // inset so the offset box still fits (measured:
            // MediaBox 601 = 595 + 6, body box 96..517 = 421 wide).
            cv.profile.bodyWidthFullDoc = !(cv.pageInfo?.WidthAssigned ?? false)
                && Regex.Match(cv.html, @"<body\b[^>]*style\s*=\s*(['""])[^'""]*?(?<![-\w])width\s*:\s*100%[^'""]*\1",
                    RegexOptions.IgnoreCase).Success;
            if (cv.profile.bodyWidthFullDoc)
            {
                cv.pageWidth += UaBodyMarginPt;
                cv.marginRight -= UaBodyMarginPt;
            }
        }
    }
}
