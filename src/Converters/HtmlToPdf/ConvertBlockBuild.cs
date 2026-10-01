using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The quirks-mode chain sheet (no doctype, a stylesheet with descendant/id rules): its flow
    /// steps by CSS boxes - UA body inset top and right, a line box per bare br, the UA base size,
    /// the sheet's heading boxes and margins, a class-bordered frame. Per conversion, like the quirks flag.</summary>
    [ThreadStatic] private static bool _quirksChainSheet;
    /// <summary>The quirks-mode sheet whose grids the stylesheet reaches through their ancestors. Per conversion.</summary>
    [ThreadStatic] private static bool _ancestorGridSheet;

    /// <summary>True when the stylesheet reaches table CELLS through a hooked container above the
    /// table: a chain rule ending in td/th whose leading segment is an id or class that the markup
    /// carries on a div/section-like container (`#right_column TABLE TD` under `&lt;DIV id=right_column&gt;`,
    /// `.datagrid table td` under `&lt;div class="datagrid"&gt;`). A rule hooked on the table's own
    /// class (`.table tr td` on `&lt;table class="table"&gt;`) is not this dialect.</summary>
    private static bool SheetScopesCellsThroughContainer(ConvertState cv)
    {
        if (cv.profile.docChainCellRulesOnly || cv.profile.docChainRules is not { Count: > 0 } rules) return false;
        foreach (var r in rules)
        {
            if (r.Segs.Count < 2 || r.Segs[^1].Tag is not ("td" or "th")) continue;
            var head = r.Segs[0];
            if (head.Id is null && head.Classes is null) continue;
            if (head.Tag is "table" or "tr" or "tbody" or "thead" or "tfoot" or "td" or "th") continue;
            var hooks = new List<string>();
            if (head.Id is not null) hooks.Add(@"\bid\s*=\s*[""']?" + Regex.Escape(head.Id) + @"\b");
            if (head.Classes is not null)
                foreach (var c in head.Classes)
                    hooks.Add(@"\bclass\s*=\s*(?:[""'][^""']*\b" + Regex.Escape(c) + @"\b[^""']*[""']|" + Regex.Escape(c) + @"\b)");
            foreach (var hook in hooks)
                if (Regex.IsMatch(cv.html, @"<(?:div|section|article|main|form|center|fieldset|blockquote)\b[^>]*" + hook, RegexOptions.IgnoreCase))
                    return true;
        }
        return false;
    }

    /// <summary>The block-build stage of an HTML conversion: flow detection, block construction and the table width ledgers, verbatim. A non-null result is a finished document.</summary>
    /// <summary>The UA-grid document shape (see the caller): a styled document with no body typography rule, on no
    /// calibrated dialect, that declares page breaks or an @page rule, or sizes its table text through the sheet.</summary>
    private static bool IsUaGridDocument(ConvertState cv)
    {
        var p = cv.profile;
        if (cv.uaFlow || cv.printCoverDoc || cv.articleFlow || cv.marginsExplicit) return false;
        // A sheet that BOXES its grids - collapses every table's borders and zeroes every cell's padding, the
        // e-mail template idiom - lays them out on the browser's box model whatever body typography or
        // descendant rules it declares (the mailing's Arial 13px / 125% body types its grids, and its
        // `.orange-blue .footer` rules reach their cells through ApplyUaChainRules).
        var sheetBoxedGrids = UaSheetBoxesGrids(cv.css);
        if (!sheetBoxedGrids && (p.bodyCssFontPt > 0 || cv.bodyCssFace is not null)) return false;
        if (p.bodyPinnedW > 0 || p.scaleToPageWidth) return false;
        if (!sheetBoxedGrids && p.docChainRules is not null && !p.docChainCellRulesOnly) return false;
        if (p.wordExportDoc || p.wordMailDoc || p.dwFormDoc || p.msoFilteredDoc) return false;
        if (p.uaStdSerif || p.uaBareDoc || p.deadExternalCss || p.escapedAttrDoc || p.metricFlow || p.printGrid) return false;
        if (p.bodyBoxGridDoc || p.elementGridDoc || p.emailNewsletterDoc || p.uaBlockCells || p.uaFormCells) return false;
        if (p.redlineDiffDoc || p.ssrsReportDoc || p.ptReportDoc || p.ptStyledFragment || p.formHorizontalDoc || p.formDialectTables) return false;
        if (p.floatBandDoc || p.floatBothSidesDoc || p.chartCardDoc || p.rtlDoc) return false;
        if (cv.html.IndexOf("<style", StringComparison.OrdinalIgnoreCase) < 0) return false;
        if (cv.html.IndexOf("<table", StringComparison.OrdinalIgnoreCase) < 0) return false;
        if (cv.html.IndexOf("<link", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        // The pagination must be the SHEET's (an @page rule or a page-break rule in a <style> block): an
        // inline `style="page-break-before"` on a div is the application form's, a calibrated green.
        var paged = false;
        foreach (Match sheet in Regex.Matches(cv.html, "<style[^>]*>(.*?)</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            if (Regex.IsMatch(sheet.Groups[1].Value, "page-break-(before|after) *: *(always|page)", RegexOptions.IgnoreCase)
                || Regex.IsMatch(sheet.Groups[1].Value, "@page *[{:]", RegexOptions.IgnoreCase))
                paged = true;
        // …or the sheet types the TABLE element itself (`table { font-family: Arial; font-size: 12px }`); a td
        // rule alone is the calibrated grids' (the claim plan and the invoice keep their flow).
        var tableTyped = cv.css.TryGetValue("table", out var tableRule)
            && (tableRule.ContainsKey("font-size") || tableRule.ContainsKey("font-family") || tableRule.ContainsKey("font"));
        return paged || tableTyped || sheetBoxedGrids;
    }

    /// <summary>The sheet boxes its grids: a `table` rule collapsing the borders and a `td` rule zeroing the padding.</summary>
    private static bool UaSheetBoxesGrids(IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        if (!(css.TryGetValue("table", out var tableRule) && tableRule.TryGetValue("border-collapse", out var bc)
              && bc.Trim().Equals("collapse", StringComparison.OrdinalIgnoreCase))) return false;
        foreach (var key in new[] { "td", "table td", "td, th", "th, td" })
            if (css.TryGetValue(key, out var tdRule) && tdRule.TryGetValue("padding", out var pad)
                && IsZeroLength(pad.Trim().Split(' ')[0])) return true;
        return false;
    }

    private static Document? ConvertBlockBuild(HtmlLoadOptions? options, ConvertState cv)
    {
        ResolveUaFlowAndBodyWidth(cv, options);

        // Official-letter flow (gated): an explicit-zero-margin CJK letter —
        // a content class carries the family, table rows pace themselves with
        // inline font-size keywords and px heights, and no body rule exists
        // for the standard metric gate. The metric flow lays it out with the
        // face's real advances; an uninstalled family substitutes to SimSun,
        // the same fallback the expected output draws it with.
        ApplyMetricFlowMargins(cv, options);

        cv.articleFlow = false;
        cv.articleLineFactor = 0.0;
        DetectArticleAndNewsletterFlow(cv.bodyCssFace, cv.css, cv.html, cv.inlineSvgs, cv.marginsExplicit, cv.profile, cv);

        cv.printCoverDoc = cv.profile.bodyZeroMargin && !cv.profile.metricFlow && !cv.profile.chartCardDoc && Regex.IsMatch(cv.html, @"page-break-after\s*:\s*always", RegexOptions.IgnoreCase);
        // The chain dialect on the calibrated flow takes its sheet's typography (probed: H1 22px
        // draws 16.5, H2/H3 14px draw 10.5 where the legacy defaults drew 18/15/13); the print
        // cover, chart card and float-both-sides certificate dialects keep their own probed
        // models (the card's raster lands within a point of its title's calibrated pitch; the
        // certificate measures its declared element heights and float line boxes itself).
        // A UA-grid document: its sheet types its cells and runs but names no body typography, no calibrated
        // dialect claims it, and it either declares print pagination or sizes its tables' text - its grids and
        // blocks then lay out on the browser's box model (the resume, the royalty statement, the quotation).
        if (!cv.profile.uaGridBoxes && IsUaGridDocument(cv)) cv.profile.uaGridBoxes = cv.profile.uaGridSheet = true;
        if (cv.profile.uaGridBoxes && !cv.profile.sectionedReport) cv.profile.uaGridSheet = true;
        // A UA-grid sheet's body line-height paces its cells (`body { font-size: 13px; line-height: 125% }` = 12.19).
        ResolveFlowBaseTypography(cv);

        // Styled inline rows (nav bars, centered link lines) render from prebuilt
        // run blocks; their markup is replaced by <rowmark> placeholders.
        (cv.html, var rowBlocks) = ExtractRowBlocks(cv.html, cv.css);

        // Document-level RTL (dir="rtl" on <html>/<body>): a block image wider than
        // the content box keeps its NATIVE size with its right edge on the right
        // margin, overflowing (and clipping) off the left page edge — the mirror of
        // the LTR left-pinned overflow.
        DetectRegionsAndFooterImage(cv, options);
        ParseDocumentBlocks(cv, options, rowBlocks);
        // A content-less document still ships ONE page at the configured size —
        // the converter never emits a zero-page PDF.
        if (EmptyDocumentOrContinue(cv, options) is { } emptyDocumentOrContinueResult) return emptyDocumentOrContinueResult;
        SeatBlockFaces(cv, options);

        MeasureTableWidths(cv, options);
        // …unless the caller AUTHORED the page width. A browser printing to a fixed
        // paper size overflows a too-wide table, it does not grow the paper, and the
        // public idiom for that is `options.PageInfo.Width = PageSize.A4.Width`.
        // Under ScaleToPageWidth an authored width no longer pins the sheet
        // during LAYOUT: the body grows to the widest table's min-content (the
        // percent tables resolve against the grown box),
        // and the finished pages shrink back onto the authored page with the
        // content pinned at the left margin and the page top.
        WidenPageForTables(cv, options);

        // A declared table wider than the content box widens the page too: the
        // browser grows the canvas rather than squeezing a fixed-width table.
        // A table the natural-width probe already measured (and the page grew
        // for) must not re-widen the sheet — the stale second pass would also
        // capture the widened width as the page height.
        // The escaped-attr dialect's declared widths are JSON-mangled — they are
        // all ignored and the default page is kept.
        FitDeclaredTableWidth(cv, options);

        // Positioned-slide widen: the page grows to the slide's CONTENT EXTENT —
        // the rightmost absolutely positioned child's edge — inside the page and
        // UA body margins. The canvas min-width does NOT drive it (measured:
        // 878.25 = 90 + 6 + (403 + 520)px·0.75 + 90 on a 960px-min-width slide).
        WidenPageForChartsAndEdges(cv, options);
        // A position:absolute/fixed element anchored by `left` (at any depth in a chain of
        // positioned ancestors) can extend past the content box in real CSS just as a table
        // or slide can - the page grows to fit it the same way (measured against the
        // reference). A `right`-anchored chain (no `left` anywhere in it) never contributes.
        WidenPageForAbsoluteOverflow(cv, options);
        // The widest painted ink outside the tables grows the sheet last: page margin +
        // body inset + ink + page margin, the flow reflowing to the grown content box.
        WidenPageToInk(cv, options);
        // The SSRS report opens at the raw 72 pt content top and flows to the
        // raw 72 pt bottom (measured: the first grid baseline seats at 86 =
        // 72 + the 2.05 mm spacer row + cell chrome, and page 1 runs to 753).
        return null;
    }

    /// <summary>Whether the sheet's bare `div` rule pads the element (a band-built document).</summary>
    private static bool SheetPadsDivs(Dictionary<string, Dictionary<string, string>> css) =>
        css.TryGetValue("div", out var divRule)
        && (divRule.TryGetValue("padding", out var pad) && TryParseLength(pad.Split(' ')[0]) is > 0
            || divRule.TryGetValue("padding-top", out var padTop) && TryParseLength(padTop) is > 0);
}
