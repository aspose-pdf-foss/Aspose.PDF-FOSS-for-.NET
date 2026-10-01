using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The block builders, lifted out of ConvertFromHtml: each takes the
// conversion state and the pre-scan flags it reads. Bodies are verbatim.
    private static         double PxOf(string tag, string prop)
            {
                // CSS style wins; a bare width/height attribute is the fallback.
                var m = Regex.Match(tag, prop + @"\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                if (!m.Success)
                    m = Regex.Match(tag, "\\b" + prop + @"\s*=\s*[""']?([\d.]+)", RegexOptions.IgnoreCase);
                return m.Success && double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            }

    /// <summary>The size the block parser seeds its root style with: a sheet-less body attribute's size on the UA
    /// flow, the print grid's base, the docs-site root, the redline base, the default body size of the body-box grid
    /// and scale-to-width documents, the chain dialect's body rule (what its percent heading rules resolve against
    /// and what its plain text draws at - the unwrap flow never took that seed), else the form document's body size
    /// (0 = the flow's own default).</summary>
    private static double FlowRootFontPt(ConvertState cv, bool sheetBodyRule = true) =>
        cv.profile.uaStdSerif && cv.uaBodyFontPt > 0 ? cv.uaBodyFontPt
        : cv.profile.printGrid ? cv.profile.printGridBase
        : cv.articleFlow ? CssRootFontPt
        : cv.profile.redlineDiffDoc ? RedlineBaseFontPt
        : cv.profile.bodyBoxGridDoc || cv.profile.scaleToPageWidth ? DefaultBodyFontPt
        : sheetBodyRule && cv.profile.sheetTypographyDoc && cv.profile.bodyCssFontPt > 0 ? cv.profile.bodyCssFontPt
        // …and a chain-dialect sheet that sizes no body draws its plain text at the UA base, the
        // size its line boxes and em margins resolve against (measured: a UA body's <br/> is 13.5).
        : sheetBodyRule && _quirksChainSheet && !cv.css.ContainsKey("body") ? DefaultBodyFontPt
        : cv.profile.formBodyFontPt;

    /// <summary>The table's visible controls are all button-family inputs (button, submit, reset): a layout grid
    /// whose buttons draw as their labels (probed: a submit in a th draws its value centred, the grid keeps its
    /// columns), not a form the flat path emits fields for.</summary>
    private static bool ButtonFamilyControlsOnly(string markup)
    {
        var any = false;
        foreach (Match fim in Regex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (HiddenInlineRx.IsMatch(fim.Value)) continue;
            if (!fim.Groups[1].Value.Equals("input", StringComparison.OrdinalIgnoreCase)) return false;
            var tyM = Regex.Match(fim.Value, @"type\s*=\s*[""']?([A-Za-z]+)", RegexOptions.IgnoreCase);
            var ty = tyM.Success ? tyM.Groups[1].Value.ToLowerInvariant() : "text";
            if (ty is "hidden") continue;
            if (ty is not ("button" or "submit" or "reset")) return false;
            any = true;
        }
        return any;
    }

    private static bool HasVisibleFormControl(string markup)
    {
        foreach (Match fim in Regex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>",
                     RegexOptions.IgnoreCase))
            if (!HiddenInlineRx.IsMatch(fim.Value)) return true;
        return false;
    }

    private static bool IsSingleColumnWrapperTable(bool sheetChromesCells, string seg)
    {
        // Cells the SHEET borders or fills are a visible grid, not layout chrome.
        if (sheetChromesCells) return false;
        var open = Regex.Match(seg, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return false;
        if (Regex.IsMatch(open.Value, @"\bborder\s*=\s*['""]?[1-9]", RegexOptions.IgnoreCase)
            || Regex.IsMatch(open.Value, @"\b(cellspacing|cellpadding)\s*=", RegexOptions.IgnoreCase))
            return false;
        if (Regex.IsMatch(seg, @"\bbgcolor\s*=|background(-color)?\s*:|border\s*:\s*(?!none|0)",
                RegexOptions.IgnoreCase))
            return false;
        // A class on the structural tags can carry stylesheet chrome (the
        // width-class table skins) — only structurally BARE tables unwrap.
        foreach (Match st in Regex.Matches(seg, @"<(table|tr|td|tbody|thead|tfoot)\b[^>]*>",
                     RegexOptions.IgnoreCase))
            if (Regex.IsMatch(st.Value, @"\bclass\s*=", RegexOptions.IgnoreCase))
                return false;
        if (Regex.Matches(seg, @"<table\b", RegexOptions.IgnoreCase).Count > 1
            || Regex.IsMatch(seg, @"<th\b", RegexOptions.IgnoreCase))
            return false;
        var anyTd = false;
        foreach (Match tr in Regex.Matches(seg, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>",
                     RegexOptions.IgnoreCase))
        {
            if (Regex.Matches(tr.Groups[1].Value, @"<td\b", RegexOptions.IgnoreCase).Count != 1)
                return false;
            anyTd = true;
        }
        return anyTd;
    }

    private static List<Block> BuildStructuredBlocks(ConvertState cv, bool absSpanLedger, List<BeforeMarker> beforeMarkers, string? elementGridFace, bool htmlHasFormInput, bool inlineBlockColRules, bool perTableFormGate, List<Block> rowBlocks, bool sheetChromesCells, string frag, int depth, double availPt = 0)
    {
        var structured = Regex.IsMatch(frag, @"float\s*:\s*left|border\s*:\s*solid", RegexOptions.IgnoreCase)
            || (cv.profile.printGrid && Regex.IsMatch(frag, @"col-xs-|infobox", RegexOptions.IgnoreCase));
        // A pinned-body report is a single column by AUTHORING (margin:auto
        // on a fixed body) — the floats its cells carry are in-cell layout,
        // and the div segmenter would cut its tables apart.
        if (depth > 6 || !structured || cv.profile.bodyPinnedW > 0)
            return BuildFlowBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, frag);
        // px-width float columns convert to fractions of the box they live IN —
        // the page content at the top level, the enclosing column when nested.
        var segs = SegmentDivStructures(frag, cv.profile.printGrid ? cv.css : null,
            availPt > 0 ? availPt : cv.pageWidth - cv.marginLeft - cv.marginRight,
            allowPxCols: cv.profile.formHorizontalDoc);
        if (segs.Count == 0) return BuildFlowBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, frag);
        var list = new List<Block>();
        foreach (var seg in segs)
        {
            switch (seg.Kind)
            {
                case DivSeg.Col:
                    list.Add(new Block { ColScopeStart = true, FloatWidthFrac = seg.WidthFrac, ColPadPt = seg.ColPadPt });
                    list.AddRange(BuildStructuredBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, seg.Html, depth + 1));
                    list.Add(new Block { ColScopeEnd = true });
                    break;
                case DivSeg.Band when seg.Cols is { Count: > 0 }:
                    list.Add(new Block { FloatBandStart = true });
                    foreach (var (inner, startFrac, widthFrac, padTopPt) in seg.Cols)
                    {
                        list.Add(new Block
                        {
                            FloatColStart = true,
                            FloatStartFrac = startFrac,
                            FloatWidthFrac = widthFrac,
                            FloatPadTopPt = padTopPt,
                        });
                        list.AddRange(BuildStructuredBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, inner, depth + 1,
                            widthFrac * (availPt > 0 ? availPt : cv.pageWidth - cv.marginLeft - cv.marginRight)));
                    }
                    list.Add(new Block { FloatBandEnd = true });
                    break;
                case DivSeg.Box:
                {
                    // A sized box whose children declare themselves table cells holds an
                    // anonymous table row: the cells become the box's columns.
                    var cellCols = seg.BoxWidthPt > 0
                        ? TableCellColumns(seg.Html, seg.BoxWidthPt) : null;
                    var innerBlocks = cellCols is not null
                        ? BuildTableCellBandBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput,
                            inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, cellCols, seg.BoxWidthPt, depth)
                        : BuildStructuredBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, seg.Html, depth + 1, availPt);
                    var firstTextSize = 0.0;
                    foreach (var ib in innerBlocks)
                    {
                        if (ib.IsTable || ib.IsImage) break;
                        if (!string.IsNullOrEmpty(ib.Text)) { firstTextSize = ib.FontSize; break; }
                    }
                    list.Add(new Block
                    {
                        BoxStart = true, BoxBorderPt = seg.BorderPt, BoxPadTopPt = seg.PadTopPt,
                        // A box drawn at its declared size opens at the flow cursor: its top
                        // border is the box's own edge, not a line of ink to clear.
                        BoxAscentPt = cv.profile.printGrid || seg.BoxHeightPt > 0 ? 0 : firstTextSize * 0.9,
                        BoxPadSidePt = cv.profile.printGrid ? seg.PadSidePt : 0,
                        BoxBorderGray = seg.BorderGray,
                        BoxWidthPt = seg.BoxWidthPt, BoxHeightPt = seg.BoxHeightPt,
                    });
                    list.AddRange(innerBlocks);
                    list.Add(new Block
                    {
                        BoxEnd = true, BoxPadBottomPt = seg.PadBottomPt, BoxMarginBottomPt = seg.MarginBottomPt,
                        BoxWidthPt = seg.BoxWidthPt, BoxHeightPt = seg.BoxHeightPt,
                    });
                    break;
                }
                default:
                    list.AddRange(BuildFlowBlocks(cv, absSpanLedger, beforeMarkers, elementGridFace, htmlHasFormInput, inlineBlockColRules, perTableFormGate, rowBlocks, sheetChromesCells, seg.Html));
                    break;
            }
        }
        return list;
    }

    private static string BandMeasureFace(Block b)
    {
        var fam = string.IsNullOrEmpty(b.FontFamily) ? "Times New Roman" : b.FontFamily!;
        if ((b.FontRes == "F2" || b.EmBold) && PosFace(fam + " Bold").ttf is not null)
            return fam + " Bold";
        return PosFace(fam).ttf is not null ? fam : "Arial";
    }

    private static bool IsBlankFlowBlock(ConvertState cv, Block b) =>
        string.IsNullOrWhiteSpace(b.Text) && string.IsNullOrEmpty(b.ImageSrc) && !b.IsTable;
}
