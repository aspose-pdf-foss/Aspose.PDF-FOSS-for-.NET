using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A built table whose declared column widths overrun its own declared box marks the over-declared grid dialect.</summary>
    private static void DetectOverDeclaredGridFromTable(Block b, ConvertState cv, HtmlDocProfile profile, Dictionary<string, Dictionary<string, string>> css, List<byte[]> inlineSvgs, HtmlLoadOptions? options, double availContentW, string? bodyCssFace)
    {
        if (b.IsTable && BuildTableFromHtml(b.TableHtml ?? "", availContentW, options, inlineSvgs, css,
                widenProbe: profile.floatBandDoc,
                // A scaled layout measures at the UA base size — the shrink
                // factor multiplies it back to the final text size.
                defaultCellFontPt: profile.dwFormDoc ? 12.0
                    : profile.scaleToPageWidth ? DefaultBodyFontPt
                    : profile.printGrid ? profile.printGridBase
                    // UA-serif documents measure at the UA 16px base in the
                    // serif face — the 11pt Helvetica default under-measures
                    // the min-content the sheet widens for.
                    : profile.uaStdSerif && !profile.deadExternalCss && profile.bodyCssFontPt <= 0 ? 12
                    : profile.bodyCssFontPt,
                tightExtras: profile.printGrid,
                cssRunFace: bodyCssFace ?? (profile.uaStdSerif && !profile.deadExternalCss ? "Times New Roman" : null),
                // …and when the run-face is dropped (no class styles the runs) the
                // probe must still measure in the face the flow DRAWS, on the UA box
                // model it draws with. Measuring the serif grid in the default
                // Helvetica over-states every column by its width difference, and the
                // sheet is then widened to a table nothing lays out. Only a BARE
                // grid — no class on the table or its cells, so the UA supplies all
                // of its typography — holding an UNSIZED image measures this way:
                // the image's intrinsic pixels are what force the widen, so its
                // text columns must be measured coherently with the drawn face. Any
                // classed grid keeps the legacy probe it was calibrated on — a
                // class-styled report re-measured in the UA face mis-sizes columns
                // its own classes style, and the UA cell walk is far slower on a
                // large data grid.
                defaultCellFace: profile.dwFormDoc ? "Times New Roman"
                    : profile.uaStdSerif && !profile.deadExternalCss
                    && Regex.IsMatch(b.TableHtml ?? "",
                        "<img(?![^>]*(width|height)[ ]*=)", RegexOptions.IgnoreCase)
                    && !Regex.IsMatch(b.TableHtml ?? "",
                        "class[ ]*=", RegexOptions.IgnoreCase)
                    ? "Times New Roman" : null,
                // The probe must measure the same cell boxes the render will build,
                // or the page is sized off a grid nothing draws.
                uaCellBoxes: profile.uaGridBoxes,
                // …which means the SAME lift setting: it also switches the whole
                // chain-selector dialect on, so a probe that lifts while the render
                // does not measures class-rule cell padding and borders the drawn
                // grid never gets, and widens the sheet to a grid nothing draws.
                liftNestedTables: true,
                ptCellWidths: profile.ptStyledFragment,
                // Only a bare full UA document probes the serif floors -
                // styled or fragment docs keep the legacy floors their
                // calibrated sheets were measured on.
                uaSerifMin: profile.uaBareDoc || profile.uaNoWrapCellRule,
                redlineCells: profile.redlineDiffDoc,
                dwFormCells: profile.dwFormDoc,
                wordMailCells: profile.wordMailDoc,
                docElementGrid: profile.elementGridDoc,
                pinnedBodyGrid: profile.bodyPinnedW > 0,
                // The width probe measures CJK the way the layout draws it
                // — full-em advances, per-ideograph breaks.
                fullWidthCjkMin: true,
                chainRules: profile.docChainRules,
                // …through the same ancestor chain the render resolves the grid on, or the
                // probe measures columns the scoped cell rules never dressed.
                cssAncestors: b.CssAncestors) is ({ } probedTable, var natW))
        {
            ReadProbedGridIntoProfile(probedTable, natW, b, cv, profile, availContentW, options);
        }
    }
}
