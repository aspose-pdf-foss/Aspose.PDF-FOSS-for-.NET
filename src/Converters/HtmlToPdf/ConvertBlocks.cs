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
                var m = SafeRegex.Match(tag, prop + @"\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                if (!m.Success)
                    m = SafeRegex.Match(tag, "\\b" + prop + @"\s*=\s*[""']?([\d.]+)", RegexOptions.IgnoreCase);
                return m.Success && double.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            }

    /// <summary>The size the block parser seeds its root style with: a sheet-less body attribute's size on the UA
    /// flow, the print grid's base, the docs-site root, the redline base, the default body size of the body-box grid
    /// and scale-to-width documents, the chain dialect's body rule (what its percent heading rules resolve against
    /// and what its plain text draws at - the unwrap flow never took that seed), else the form document's body size
    /// (0 = the flow's own default).</summary>
    private static double FlowRootFontPt(ConvertState cv, bool sheetBodyRule = true)
    {
        if (cv.profile.uaStdSerif && cv.uaBodyFontPt > 0) return cv.uaBodyFontPt;
        if (cv.profile.printGrid) return cv.profile.printGridBase;
        if (cv.articleFlow) return CssRootFontPt;
        if (cv.profile.redlineDiffDoc) return RedlineBaseFontPt;
        if (cv.profile.bodyBoxGridDoc || cv.profile.scaleToPageWidth) return DefaultBodyFontPt;
        if (sheetBodyRule && cv.profile.sheetTypographyDoc && cv.profile.bodyCssFontPt > 0) return cv.profile.bodyCssFontPt;
        // …and a chain-dialect sheet that sizes no body draws its plain text at the UA base, the
        // size its line boxes and em margins resolve against (measured: a UA body's <br/> is 13.5).
        if (sheetBodyRule && _quirksChainSheet && !cv.css.ContainsKey("body")) return DefaultBodyFontPt;
        // A universal rule sizes every element, the root among them: where no other seed owns the
        // flow, its font-size is the size the plain text draws at (the flow already takes its face
        // from the same rule).
        if (sheetBodyRule && cv.profile.formBodyFontPt <= 0
            && cv.css.TryGetValue("*", out var universal) && universal.TryGetValue("font-size", out var universalFs)
            && TryParseCssFontSize(StripImportant(universalFs)) is { } universalPt && universalPt > 0)
            return universalPt;
        return cv.profile.formBodyFontPt;
    }

    /// <summary>The table's visible controls are all button-family inputs (button, submit, reset): a layout grid
    /// whose buttons draw as their labels (probed: a submit in a th draws its value centred, the grid keeps its
    /// columns), not a form the flat path emits fields for.</summary>
    private static bool ButtonFamilyControlsOnly(string markup)
    {
        var any = false;
        foreach (Match fim in SafeRegex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (SafeRegex.IsMatch(HiddenInlineRx, fim.Value)) continue;
            if (!fim.Groups[1].Value.Equals("input", StringComparison.OrdinalIgnoreCase)) return false;
            var tyM = SafeRegex.Match(fim.Value, @"type\s*=\s*[""']?([A-Za-z]+)", RegexOptions.IgnoreCase);
            var ty = tyM.Success ? tyM.Groups[1].Value.ToLowerInvariant() : "text";
            if (ty is "hidden") continue;
            if (ty is not ("button" or "submit" or "reset")) return false;
            any = true;
        }
        return any;
    }

    /// <summary>Whether the markup holds a form control a reader sees: one neither hidden by an inline style nor an
    /// input of type hidden, which carries a value for the form's submission and draws nothing.</summary>
    private static bool HasVisibleFormControl(string markup)
    {
        return SafeRegex.Matches(markup, @"<\s*(input|select|textarea)\b[^>]*>",
                     RegexOptions.IgnoreCase).Any(fim => !SafeRegex.IsMatch(HiddenInlineRx, fim.Value)
                         && !SafeRegex.IsMatch(fim.Value, @"^<\s*input\b[^>]*\btype\s*=\s*[""']?hidden\b", RegexOptions.IgnoreCase));
    }

    private static bool IsSingleColumnWrapperTable(bool sheetChromesCells, string seg)
    {
        // Cells the SHEET borders or fills are a visible grid, not layout chrome.
        if (sheetChromesCells) return false;
        var open = SafeRegex.Match(seg, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return false;
        if (SafeRegex.IsMatch(open.Value, @"\bborder\s*=\s*['""]?[1-9]", RegexOptions.IgnoreCase)
            || SafeRegex.IsMatch(open.Value, @"\b(cellspacing|cellpadding)\s*=", RegexOptions.IgnoreCase))
            return false;
        if (SafeRegex.IsMatch(seg, @"\bbgcolor\s*=|background(-color)?\s*:|border\s*:\s*(?!none|0)",
                RegexOptions.IgnoreCase))
            return false;
        // A class on the structural tags can carry stylesheet chrome (the
        // width-class table skins) — only structurally BARE tables unwrap.
        if (SafeRegex.Matches(seg, @"<(table|tr|td|tbody|thead|tfoot)\b[^>]*>",
                     RegexOptions.IgnoreCase).Any(st => SafeRegex.IsMatch(st.Value, @"\bclass\s*=", RegexOptions.IgnoreCase)))
            return false;
        if (SafeRegex.Matches(seg, @"<table\b", RegexOptions.IgnoreCase).Count > 1
            || SafeRegex.IsMatch(seg, @"<th\b", RegexOptions.IgnoreCase))
            return false;
        var anyTd = false;
        foreach (Match tr in SafeRegex.Matches(seg, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>",
                     RegexOptions.IgnoreCase))
        {
            if (SafeRegex.Matches(tr.Groups[1].Value, @"<td\b", RegexOptions.IgnoreCase).Count != 1)
                return false;
            anyTd = true;
        }
        return anyTd;
    }

    private static List<Block> BuildStructuredBlocks(ConvertState cv, bool absSpanLedger, List<BeforeMarker> beforeMarkers, string? elementGridFace, bool htmlHasFormInput, bool inlineBlockColRules, bool perTableFormGate, List<Block> rowBlocks, bool sheetChromesCells, string frag, int depth, double availPt = 0)
    {
        var structured = SafeRegex.IsMatch(frag, @"float\s*:\s*left|border\s*:\s*solid", RegexOptions.IgnoreCase)
            || (cv.profile.printGrid && SafeRegex.IsMatch(frag, @"col-xs-|infobox", RegexOptions.IgnoreCase));
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
        var fam = string.IsNullOrEmpty(b.FontFamily) ? "Times New Roman" : b.FontFamily;
        if ((b.FontRes == "F2" || b.EmBold) && PosFace(fam + " Bold").ttf is not null)
            return fam + " Bold";
        return PosFace(fam).ttf is not null ? fam : "Arial";
    }

    private static bool IsBlankFlowBlock(Block b) =>
        string.IsNullOrWhiteSpace(b.Text) && string.IsNullOrEmpty(b.ImageSrc) && !b.IsTable;
}
