using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// THE COLUMN-ROW DOCUMENT: the float-box formatter's fourth claim - a page on the plain flow whose
// stylesheet seats sibling boxes side by side: the items of a flex row, table-cell boxes, floated
// siblings with percentage widths. The plain flow stacks every one of them as a
// full-width block; the formatter lays the row out as CSS does - each sibling in its own column
// width, the row as tall as its tallest cell - under the print media context, the full cascade and
// the UA defaults of the block tags, on the caller's own page box.
internal static partial class HtmlToPdfConverter
{
    private const int FbMinRowSiblings = 2;                   // a row seats at least two boxes side by side

    // The UA defaults of the block tags, the least specific rules of the cascade: the sample sheet of
    // CSS 2.1 appendix D, whose margins the reference converter's flow was probed to hold (a heading
    // of the fourth level and a paragraph stand 1.12 em off their neighbours).
    private const string FbUaSheetCss =
        "h1{font-size:2em;margin:.67em 0;font-weight:bold}h2{font-size:1.5em;margin:.75em 0;font-weight:bold}"
        + "h3{font-size:1.17em;margin:.83em 0;font-weight:bold}h4{margin:1.12em 0;font-weight:bold}"
        + "h5{font-size:.83em;margin:1.5em 0;font-weight:bold}h6{font-size:.75em;margin:1.67em 0;font-weight:bold}"
        + "p,dl,ul,ol,pre{margin:1.12em 0}blockquote{margin:1.12em 40px}dd{margin-left:40px}ul,ol{padding-left:40px}"
        + "ul ul,ul ol,ol ul,ol ol{margin:0}hr{border-top:1px solid #808080;margin:.5em 0}center{text-align:center}"
        + "fieldset{border:2px solid #c0c0c0;padding:.35em .75em .625em;margin:0 2px}a:link{color:#0000ee}";

    private static readonly Regex FbRowTriggerRx = new(@"display\s*:\s*(?:-webkit-|-ms-)?(?:inline-)?(?:flex|flexbox|box|table-cell)\b|float\s*:\s*(?:left|right)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);
    // (form controls keep the plain flow, whose fields and control lines the page was drawn with)
    private static readonly Regex FbUnlaidContentRx = new(@"<(?:svg|canvas|frameset|math|input|textarea|select|button)\b|\browspan\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>Render the column-row document, or null when the claim declines it (the plain flow
    /// then lays it out as before). <paramref name="html"/> is the markup before the styled rows
    /// were lifted out of it.</summary>
    private static Document? TryRenderColumnRowDocument(ConvertState cv, HtmlLoadOptions? options, string html, List<Block> rowBlocks)
    {
        var trace = Environment.GetEnvironmentVariable("ASPOSE_TRACE_FB") == "1";
        if (FbColumnsDecline(cv, options, html, rowBlocks) is { } why)
        {
            if (trace) Console.Error.WriteLine($"[fb] columns declined: {why}");
            return null;
        }
        var fb = new FbState { invc = System.Globalization.CultureInfo.InvariantCulture, columns = true, options = options, inlineSvgs = cv.inlineSvgs, pageH = cv.pageHeight };
        fb.css = cv.css ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (cv.marginsExplicit)
        {
            fb.marginLeft = cv.marginLeft; fb.marginRight = cv.marginRight;
            fb.marginTop = cv.marginTop; fb.marginBottom = cv.marginBottom;
        }
        var sheet = FbReadSheet(html, options, (cv.pageWidth - fb.marginLeft - fb.marginRight) / FbPxPt, (cv.pageHeight - fb.marginTop - fb.marginBottom) / FbPxPt, keepsUniversal: true);
        FbAddUaRules(sheet);
        fb.sheet = sheet;
        var cleaned = SafeRegex.Replace(html, @"<(script|style|head)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        cleaned = SafeRegex.Replace(cleaned, @"<!--[\s\S]*?-->|<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        try
        {
            var dom = ParseDom(cleaned);
            fb.body = FbFindTag(dom, "body") ?? dom;
            FbAdoptStrayText(dom, fb.body);
            if (!FbHasColumnRow(fb, fb.body))
            {
                if (trace) Console.Error.WriteLine("[fb] columns declined: no row of side-by-side boxes");
                return null;
            }
            fb.bodyMarginPt = FbBodyMarginPt(fb, fb.body);
            // (a caller's own page box is the sheet; the default sheet grows to its ink like the other claims')
            FbRenderSheets(fb, cv.pageWidth, growToInk: !cv.marginsExplicit);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException)
        {
            // a construct the formatter cannot lay out: the plain flow keeps the document
            if (trace) Console.Error.WriteLine($"[fb] columns declined: {ex.GetType().Name} {ex.Message}");
            return null;
        }
        if (trace) Console.Error.WriteLine($"[fb] sheet={fb.pageW:0.###} pages={fb.pageObjs.Count} claim=columns");
        // (a tagged import keeps its tag tree whichever formatter lays the page out)
        if (options?.CreateLogicalStructure == true) BuildLogicalStructure(fb.doc, html);
        return fb.doc;
    }

    /// <summary>Text written ahead of the document element belongs to the body, as its first content.</summary>
    private static void FbAdoptStrayText(HtmlNode dom, HtmlNode body)
    {
        if (ReferenceEquals(dom, body)) return;
        var at = 0;
        foreach (var c in dom.Children)
        {
            if (c.Tag.Length > 0) break;
            if (c.Text.Trim().Length == 0) continue;
            c.Parent = body;
            body.Children.Insert(at++, c);
        }
    }

    /// <summary>Why the column claim declines a document, or null: a document a calibrated dialect
    /// or flow lays out keeps it, as does one whose content the formatter has no box for.</summary>
    private static string? FbColumnsDecline(ConvertState cv, HtmlLoadOptions? options, string html, List<Block> rowBlocks)
    {
        if (!SafeRegex.IsMatch(FbRowTriggerRx, html)) return "no row declaration";
        if (options is { HtmlMediaType: HtmlMediaType.Screen }) return "screen media";
        if (cv.singlePage) return "single page";
        if (cv.uaFlow) return "ua flow";
        if (cv.articleFlow || cv.printCoverDoc || cv.wikiExportDoc) return "a calibrated flow";
        if (FbCalibratedProfile(cv.profile) is { } flag) return "profile " + flag;
        // (a row the plain flow already lifts out as a styled row keeps its calibrated drawing)
        if (rowBlocks.Count > 0) return "styled rows";
        if (SafeRegex.Match(FbUnlaidContentRx, html) is { Success: true } unlaid) return "content without a box: " + unlaid.Value;
        foreach (var ch in html)
            if (ch > 0xFF && char.IsLetter(ch) && Aspose.Pdf.Text.Cp1252.TryGetByte(ch) is null) return "text outside the built-in faces";
        return null;
    }

    /// <summary>The profile flag of a calibrated dialect the document is on, or null on the plain flow.</summary>
    private static string? FbCalibratedProfile(HtmlDocProfile p)
    {
        var flags = new (bool on, string name)[]
        {
            (p.wordExportDoc, nameof(p.wordExportDoc)), (p.wordMailDoc, nameof(p.wordMailDoc)), (p.dwFormDoc, nameof(p.dwFormDoc)),
            (p.msoFilteredDoc, nameof(p.msoFilteredDoc)), (p.uaStdSerif, nameof(p.uaStdSerif)), (p.uaBareDoc, nameof(p.uaBareDoc)),
            (p.deadExternalCss, nameof(p.deadExternalCss)), (p.escapedAttrDoc, nameof(p.escapedAttrDoc)), (p.metricFlow, nameof(p.metricFlow)),
            (p.printGrid, nameof(p.printGrid)), (p.bodyBoxGridDoc, nameof(p.bodyBoxGridDoc)), (p.elementGridDoc, nameof(p.elementGridDoc)),
            (p.overDeclaredGridDoc, nameof(p.overDeclaredGridDoc)), (p.emailNewsletterDoc, nameof(p.emailNewsletterDoc)),
            (p.uaBlockCells, nameof(p.uaBlockCells)), (p.uaFormCells, nameof(p.uaFormCells)), (p.redlineDiffDoc, nameof(p.redlineDiffDoc)),
            (p.ssrsReportDoc, nameof(p.ssrsReportDoc)), (p.ptReportDoc, nameof(p.ptReportDoc)), (p.ptFormDoc, nameof(p.ptFormDoc)),
            (p.ptStyledFragment, nameof(p.ptStyledFragment)), (p.formHorizontalDoc, nameof(p.formHorizontalDoc)),
            (p.formDialectTables, nameof(p.formDialectTables)), (p.floatBandDoc, nameof(p.floatBandDoc)),
            (p.floatBothSidesDoc, nameof(p.floatBothSidesDoc)), (p.chartCardDoc, nameof(p.chartCardDoc)), (p.rtlDoc, nameof(p.rtlDoc)),
            (p.fieldListDoc, nameof(p.fieldListDoc)), (p.uaGridBoxes, nameof(p.uaGridBoxes)), (p.uaGridSheet, nameof(p.uaGridSheet)),
            (p.sectionedReport, nameof(p.sectionedReport)), (p.scaleToPageWidth, nameof(p.scaleToPageWidth)), (p.bodyPinnedW > 0, nameof(p.bodyPinnedW)),
        };
        foreach (var (on, name) in flags) if (on) return name;
        return null;
    }

    /// <summary>The UA rules enter the cascade below every author rule: no specificity, ahead in source order.</summary>
    private static void FbAddUaRules(FbSheet sheet)
    {
        var ua = new FbSheet();
        var order = 0;
        FbReadCssBlock(ua, FbUaSheetCss, ref order, 0, 0);
        order++;                                                // (every UA rule sorts ahead of a bare `*` rule)
        foreach (var rule in ua.Rules)
        {
            rule.Spec = 0;
            rule.Order -= order;
        }
        sheet.Rules.InsertRange(0, ua.Rules);
    }

    /// <summary>Does the tree seat boxes side by side: a flex row of two items or more, or adjacent
    /// siblings that are table cells or floats with percentage widths?</summary>
    private static bool FbHasColumnRow(FbState fb, HtmlNode node)
    {
        var kids = new List<HtmlNode>();
        foreach (var c in node.Children)
            if (c.Tag.Length > 0 && c.Tag is not ("script" or "style" or "head" or "br") && FbDisplayOf(fb, c) != "none") kids.Add(c);
        if (kids.Count >= FbMinRowSiblings)
        {
            if (node.Tag.Length > 0 && FbDisplayOf(fb, node) is "flex" or "inline-flex" && !FbFlexColumn(fb, node)) return true;
            var run = 0;
            foreach (var c in kids)
            {
                run = FbSeatsBeside(fb, c) ? run + 1 : 0;
                if (run >= FbMinRowSiblings) return true;
            }
        }
        foreach (var c in kids)
            if (FbHasColumnRow(fb, c)) return true;
        return false;
    }

    /// <summary>A sibling that stands beside its neighbours: a table-cell box that is no table's cell,
    /// or a float of a percentage width.</summary>
    private static bool FbSeatsBeside(FbState fb, HtmlNode el)
    {
        var display = FbDisplayOf(fb, el);
        if (display == "table-cell") return el.Tag is not ("td" or "th");
        // (inline blocks of percentage widths stay with the plain flow, which stacks them)
        var floated = (FbDecl(fb, el, "float") ?? "").Trim().ToLowerInvariant() is "left" or "right";
        return floated && (FbDecl(fb, el, "width") ?? "").Trim().EndsWith('%');
    }

    /// <summary>The element's display keyword, a prefixed flex spelling read as the standard one.</summary>
    private static string FbDisplayOf(FbState fb, HtmlNode el) => FbDisplayKeyword(FbDecl(fb, el, "display"));

    private static string FbDisplayKeyword(string? value)
    {
        var d = (value ?? "").Trim().ToLowerInvariant();
        if (d.StartsWith("-webkit-", StringComparison.Ordinal)) d = d[8..];
        else if (d.StartsWith("-ms-", StringComparison.Ordinal)) d = d[4..];
        return d switch { "flexbox" or "box" => "flex", "inline-flexbox" or "inline-box" => "inline-flex", _ => d };
    }

    private static bool FbFlexColumn(FbState fb, HtmlNode el)
    {
        var dir = FbDecl(fb, el, "flex-direction") ?? FbDecl(fb, el, "flex-flow") ?? "";
        return dir.TrimStart().StartsWith("column", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The display kinds the claim lays out as block boxes beside the block tags.</summary>
    private static bool FbIsColumnBlockDisplay(string display) => display is "flex" or "list-item" or "table-row" or "table-cell";

    /// <summary>The box properties only the column claim reads: the display's standard spelling, a
    /// max-width, the border-box sizing (the declared sizes then hold the chrome) and absolute
    /// positioning.</summary>
    private static void FbColumnBoxProps(FbState fb, HtmlNode el, FbBoxProps p, double cbW, double emPx)
    {
        p.Display = FbDisplayKeyword(p.Display);
        var maxW = FbDecl(fb, el, "max-width");
        if (!string.IsNullOrEmpty(maxW) && FbSizePt(maxW, cbW, emPx) is { } mw) p.MaxWidthPt = mw;
        if ((FbDecl(fb, el, "box-sizing") ?? "").Trim().Equals("border-box", StringComparison.OrdinalIgnoreCase))
        {
            if (!p.WidthAuto) p.WidthPt = Math.Max(0, p.WidthPt - p.ChromeH);
            if (!p.HeightAuto) p.HeightPt = Math.Max(0, p.HeightPt - p.ChromeV);
            if (p.MaxWidthPt > 0) p.MaxWidthPt = Math.Max(FbEpsilon, p.MaxWidthPt - p.ChromeH);
        }
        p.Positioned = (FbDecl(fb, el, "position") ?? "").Trim().ToLowerInvariant() is "relative" or "absolute" or "fixed";
        p.OutOfFlow = p.Positioned && !(FbDecl(fb, el, "position") ?? "").Trim().Equals("relative", StringComparison.OrdinalIgnoreCase);
    }
}
