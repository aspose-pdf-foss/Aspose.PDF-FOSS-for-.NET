using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One stylesheet property decides whether it is the declaration that ends the layout-free flow.</summary>
    private static bool ScanRulePropertyForLayout(ConvertState cv, KeyValuePair<string, Dictionary<string, string>> kv, string prop)
    {
        if (PropertyCannotPullFlowOntoGeometry(kv, prop)) return true;
        // `margin: 0 auto` (any mix of zeros and autos) authors no flow
        // geometry — auto centres a box no wider than the content band,
        // zero is the reset.
        if (prop == "margin"
            && Regex.IsMatch(kv.Value[prop].Trim(), @"^(?:(?:0(?:px|pt|em|in|cm|mm)?|auto)\s+)*(?:0(?:px|pt|em|in|cm|mm)?|auto)$",
                RegexOptions.IgnoreCase)) return true;
        // …and a PULL-UP shorthand - a negative top value with a zero or auto left - states a
        // stated margin the flow reads off the shorthand itself (the e-mail cards' `.header_sub
        // h2 { margin: -15px 3px 0 0 }` pulls the heading up inside its card; the reference
        // renders the document in the flow with it). A positive vertical shorthand keeps the
        // calibrated disqualification.
        if (prop == "margin" && MarginShorthandPullsUp(kv.Value[prop])) return true;
        // …and on a sheet scoped under the body's own class, a shorthand whose SIDES are zero
        // states the vertical margins the UA flow reads off the rule (the change-control print
        // sheet's `h1 { margin: 0.75em 0 }` and `pre { margin: 0.25em 0 0 0 }`)
        if (prop == "margin" && cv.bodyClassSheet && MarginShorthandSidesZero(kv.Value[prop])) return true;
        // Vertical margins on a rule the block-margin override applies
        // (the h1/p margin resets of the order-ticket family) render in
        // the flow — they do not disqualify it. A class margin-left
        // indents its block in the flow the same way.
        if (prop is "margin-top" or "margin-bottom" or "margin-left") return true;
        // A line-height rule is typography the UA flow paces its lines by, not a layout it
        // leaves the flow for (probed: `p { line-height: 3.9em }` paces the UA serif paragraph at
        // 46.8 pt under the UA h1; `p { line-height: 1.5em }` at 18; a bold 14 pt span's 16 pt
        // box rides its UA line).
        if (prop == "line-height") return true;
        // A line-height rule is typography the UA flow paces its lines by, not a layout it
        // leaves the flow for (probed: `p { line-height: 3.9em }` paces the UA serif paragraph at
        // 46.8 pt under the UA h1; `p { line-height: 1.5em }` at 18; a bold 14 pt span's 16 pt
        // box rides its UA line).
        if (prop == "line-height") return true;
        // …and a class PADDING longhand likewise. PROBED (against the reference): a typography
        // class carrying `font-weight; font-size; padding-top:15px` leaves the paragraphs
        // around it at the UA pitch, so the reference renders such a rule without leaving
        // its flow for it.
        if (prop is "padding" or "padding-top" or "padding-bottom" or "padding-left" or "padding-right")
            return true;
        // An inline-block class rule with a PERCENT width is a column the
        // flow renders (the title-column dialect): the label/input form's
        // 32 % label beside its 63 % control keeps the UA flow.
        if (prop is "width" or "min-width" && IsInlineBlockColumnRule(kv.Value)) return true;
        // A wrapper that opens directly with a table is the grid's HOST: its declared width is
        // the box the grid lays out in (Block.HostWidthPt), which the flow renders (probed:
        // `#divTable { width: 2000px }` round a 20-column grid pages 96 + 1500 - 1.5 + 90).
        if (prop == "width" && IsGridHostSelector(cv.html, kv.Key)) return true;
        // A max-width at or beyond the UA content band cannot clamp
        // anything on this sheet — it is inert for the flow.
        if (prop == "max-width" && TryParseLength(kv.Value[prop].Trim()) is { } mwInert
            && mwInert >= cv.pageWidth - 96.0 - 72.0) return true;
        // A form control's rule (`textarea { width: 950px }`, its border, padding, font)
        // dresses the control alone - the sheet-box lookup already sizes the box - and
        // says nothing about the flow around it (probed: the reference keeps its flow and
        // draws the 712.5 x 27 pt control at the cell's content corner).
        if (IsControlSelector(kv.Key)) return true;
        // A DEAD-SHEET document's residual head rules: a text-direction or scripting property
        // lays nothing out (`unicode-bidi: isolate` on a wiki's edit links, an IE `behavior:`
        // url) - the flow keeps its box model. (A live-sheet document keeps the full test: the
        // PDF-export's `.ie .stl_N` letter-spacing hacks name no element either, yet that
        // document is no UA page - it lost a page as one.)
        if (IsDeadExternalCssDoc(cv) && prop is "unicode-bidi" or "behavior") return true;
        // ...and a rule whose selector names a class the markup never carries (a script
        // widget's `.suggestions`, a tooltip's `.referencetooltip li`) styles no element on the
        // sheet, so it cannot take the document out of the flow either.
        if (IsDeadExternalCssDoc(cv) && !SelectorNamesMarkup(MarkupSansScripts(cv), kv.Key)) return true;
        cv.cssLayoutFreeBrokenBy ??= kv.Key.Trim() + " { " + prop + ": " + kv.Value[prop] + " }";
        cv.cssLayoutFree = false;
        // the trace wants EVERY breaker, not the first: keep scanning under it
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PROFILE") == "1")
        { (cv.cssLayoutFreeBreakers ??= new List<string>()).Add(kv.Key.Trim() + " { " + prop + ": " + kv.Value[prop] + " }"); return true; }
        return false;
    }
}
