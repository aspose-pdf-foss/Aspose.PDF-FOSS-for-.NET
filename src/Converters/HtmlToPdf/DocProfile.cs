// What KIND of document this is. Every value here is decided once, from the source
// and the caller's options, before any block is laid out, and only read afterwards:
// which dialect the markup belongs to, what the body says about fonts and colour, and
// the few measurements those imply. Held together so a block-layout method can take
// "the document" instead of two score separate flags.

using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The document-shape facts the layout reads, settled before the flow starts.</summary>
    private sealed class HtmlDocProfile
    {
        /// <summary>The document escapes its attribute quotes; a dialect with its own table geometry.</summary>
        public bool escapedAttrDoc;
        /// <summary>Explicit page margins with a zero top margin.</summary>
        public bool hasZeroTopMargin;
        public bool rtlDoc;
        /// <summary>No usable authored font: the user-agent serif flow.</summary>
        public bool uaStdSerif;
        public bool uaBareDoc;
        /// <summary>A stylesheet was referenced but could not be reached.</summary>
        public bool deadExternalCss;
        public bool quirksCssRun;
        public bool metricFlow;
        public string metricFace = "";
        public double metricLineSum;
        /// <summary>A print-media bootstrap grid report.</summary>
        public bool printGrid;
        public double printGridBase;
        public bool bodyBoxGridDoc;
        public bool elementGridDoc;
        public bool overDeclaredGridDoc;
        public bool emailNewsletterDoc;
        public bool redlineDiffDoc;
        public bool sectionedReport;
        public bool ssrsReportDoc;
        public bool ptReportDoc;
        public bool ptStyledFragment;
        public double ptTableFontPt;
        /// <summary>A Word-filtered export.</summary>
        public bool msoFilteredDoc;
        public bool chartCardDoc;
        public bool floatBandDoc;
        public bool floatImageDoc;
        public bool floatBothSidesDoc;
        public bool formHorizontalDoc;
        public bool formDialectTables;
        public double formBodyFontPt;
        public bool bodyWidthFullDoc;
        public bool bodyZeroMargin;
        public double bodyPinnedW;
        public double bodyCssFontPt;
        public Color? bodyCssColor;
        public double bodyLineHeightPt;
        public double fsBoxW;
        /// <summary>The caller asked for the page to scale to content width.</summary>
        public bool scaleToPageWidth;
        public List<CssChainRule>? docChainRules;
        public Dictionary<string, int> gridRadioCounts = new();
        public Dictionary<string, Aspose.Pdf.Forms.RadioButtonField> gridRadioGroups = new();
        public List<(Aspose.Pdf.Forms.RadioButtonField rbf, Page page)> gridRadioPages = new();
        /// <summary>A DataWorks form export; its own font and border conventions.</summary>
        public bool dwFormDoc;
    }

    /// <summary>Decides whether the document declares more grid columns than it fills, and measures the widest table that settles it.</summary>
    /// <remarks>Lifted verbatim out of the document analysis in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void DetectOverDeclaredGrid(string? bodyCssFace, double availContentW, List<Block> blocks, Dictionary<string, Dictionary<string, string>> css, List<byte[]> inlineSvgs, HtmlLoadOptions? options, HtmlDocProfile profile, ref bool preGrownGridDoc, ref bool widestIsPctMin, ref double widestTable)
    {
    foreach (var b in blocks)
    {
        // A wrapper-stack table lays out through the recursive metric path,
        // whose children fit the symmetric content frame — the flat probe
        // would measure the merged monster and widen a sheet the render
        // never fills.
        if (b.IsTable && profile.uaStdSerif && !profile.deadExternalCss
            && TrySplitWrapperStack(b.TableHtml ?? "", out _, out _))
            continue;
        if (b.IsTable && BuildTableFromHtml(b.TableHtml ?? "", availContentW, out var natW, options, inlineSvgs, css,
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
                uaCellBoxes: profile.sectionedReport,
                // …which means the SAME lift setting: it also switches the whole
                // chain-selector dialect on, so a probe that lifts while the render
                // does not measures class-rule cell padding and borders the drawn
                // grid never gets, and widens the sheet to a grid nothing draws.
                liftNestedTables: true,
                ptCellWidths: profile.ptStyledFragment,
                // Only a bare full UA document probes the serif floors -
                // styled or fragment docs keep the legacy floors their
                // calibrated sheets were measured on.
                uaSerifMin: profile.uaBareDoc,
                redlineCells: profile.redlineDiffDoc,
                dwFormCells: profile.dwFormDoc,
                docElementGrid: profile.elementGridDoc,
                pinnedBodyGrid: profile.bodyPinnedW > 0,
                // The width probe measures CJK the way the layout draws it
                // — full-em advances, per-ideograph breaks.
                fullWidthCjkMin: true,
                chainRules: profile.docChainRules) is { } probedTable)
        {
            if (probedTable.HtmlOverDeclaredGrid) profile.overDeclaredGridDoc = true;
            if (probedTable.HtmlPreGrownGrid) preGrownGridDoc = true;
            if (natW > widestTable)
            {
                widestTable = natW;
                widestIsPctMin = probedTable.HtmlPctMinNatural;
            }
        }
    }
    }

    /// <summary>Applies the print-grid dialect's page geometry: its column base, margins and content width.</summary>
    /// <remarks>Lifted verbatim out of the document analysis in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void ApplyPrintGridBase(Dictionary<string, Dictionary<string, string>> css, HtmlDocProfile profile, ref string html, ref double marginLeft, ref double marginRight, ref double marginTop, ref double printGridLineFactor)
    {
    if (profile.printGrid)
    {
        // Wrapper chrome: a whole-content wrapper div's inline padding lands
        // inside the page margins on BOTH sides (the UA body margin is already
        // baked into the 96pt default; the right margin mirrors the left).
        double wrapPad = 0;
        var wpm = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*[""'][^""']*container[^""']*[""'][^>]*style\s*=\s*[""'][^""']*padding\s*:\s*(\d+(?:\.\d+)?)\s*px",
            RegexOptions.IgnoreCase);
        if (wpm.Success && double.TryParse(wpm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wrapPx))
            wrapPad = wrapPx * 0.75;
        marginLeft += wrapPad;
        marginRight = marginLeft;
        marginTop += wrapPad;
        if (css.TryGetValue("body", out var pgBody))
        {
            if (pgBody.TryGetValue("font-size", out var pgFs) && TryParseLength(pgFs, out var pgPt) && pgPt > 0)
                profile.printGridBase = pgPt;
            if (pgBody.TryGetValue("line-height", out var pgLh)
                && double.TryParse(pgLh, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pgLf)
                && pgLf is > 0.5 and < 3)
                printGridLineFactor = pgLf;
        }
        if (profile.printGridBase <= 0) profile.printGridBase = 12;
        // The first line box sits ~5pt lower than the legacy
        // first-baseline calibration under the metric model.
        marginTop += 5.0;
        // Heading bands: a ".cls hN { border-bottom: … }" descendant rule paints a
        // bar under headings inside a .cls div. The grid segmentation splits those
        // divs away from their headings, so resolve the ancestry HERE by
        // annotating each in-scope heading with a band="r,g,b|px|padpx" attribute.
        var bandKeys = new List<string>();
        foreach (var k in css.Keys) bandKeys.Add(k);
        foreach (var bandKey in bandKeys)
        {
            var bkm = Regex.Match(bandKey, @"^\.([\w-]+) (h[1-6])$");
            if (!bkm.Success || !css[bandKey].TryGetValue("border-bottom", out var bandDecl2)) continue;
            var bandCol = ParseCssColor(bandDecl2);
            if (bandCol is null) continue;
            var bwm = Regex.Match(bandDecl2, @"(\d+(?:\.\d+)?)\s*px");
            var bandPxV = bwm.Success ? bwm.Groups[1].Value : "1";
            var bandPadV = "0";
            if (css[bandKey].TryGetValue("padding-bottom", out var bandPadDecl))
            {
                var bpm = Regex.Match(bandPadDecl, @"(\d+(?:\.\d+)?)");
                if (bpm.Success) bandPadV = bpm.Groups[1].Value;
            }
            var attr = FormattableString.Invariant(
                $" band=\"{bandCol.R},{bandCol.G},{bandCol.B}|{bandPxV}|{bandPadV}\"");
            var hostRx = new Regex(@"<div\b[^>]*class\s*=\s*[""'][^""']*\b"
                + Regex.Escape(bkm.Groups[1].Value) + @"\b[^""']*[""'][^>]*>", RegexOptions.IgnoreCase);
            var hTag = bkm.Groups[2].Value;
            var hosts = new List<Match>();
            foreach (Match hm in hostRx.Matches(html)) hosts.Add(hm);
            for (var hi = hosts.Count - 1; hi >= 0; hi--)
            {
                var contentStart = hosts[hi].Index + hosts[hi].Length;
                if (FindDivEnd(html, contentStart, out var hostEnd) < 0) continue;
                var region = html[contentStart..hostEnd];
                region = Regex.Replace(region, "<" + hTag + @"\b", "<" + hTag + attr, RegexOptions.IgnoreCase);
                html = html[..contentStart] + region + html[hostEnd..];
            }
        }
    }
    }

    /// <summary>Reads the body's declared font and colour, and whether the page is an element grid.</summary>
    /// <remarks>Lifted verbatim out of the document analysis in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void DetectBodyCssAndGrid(Dictionary<string, Dictionary<string, string>> css, string html, HtmlDocProfile profile, ref string? bodyCssFace)
    {
    profile.bodyCssColor = null;
    if (profile.bodyZeroMargin && css.TryGetValue("body", out var bodyFontDecls)
        && bodyFontDecls.TryGetValue("font-size", out var bodyFontSize)
        && TryParseLength(bodyFontSize, out var bodyFontPt) && bodyFontPt > 0)
    {
        profile.bodyCssFontPt = bodyFontPt;
        // …its colour, which every block inherits (these pages set a soft grey where
        // our default is black — a visibly heavier ink)…
        if (bodyFontDecls.TryGetValue("color", out var bodyColorV))
            profile.bodyCssColor = ParseCssColor(bodyColorV);
        // …and the first INSTALLED face of the stack that rule names. It carries the
        // document's real `line-height: normal` box, and it marks the cell grids as
        // CSS line boxes so a run's own size governs its own pitch.
        if (bodyFontDecls.TryGetValue("font-family", out var bodyFontFam))
            foreach (var fam in bodyFontFam.Split(','))
            {
                var f = fam.Trim().Trim('"', '\'');
                if (f.Length > 0 && WinMetricsFor(f) is not null) { bodyCssFace = f; break; }
            }
    }

    // Quirks-mode CSS-run documents: a resolvable body face but NO <!DOCTYPE>
    // (CKEditor notes, Outlook/Teams exports). Two behaviours hang off this:
    // their tables render at the UA 16px cell base through the metric layouter
    // (the body rule's pixel font does not inherit into cells in quirks mode),
    // and their text honours inline-block title columns and dash-break
    // overflow wrapping (both measured on the references).
    profile.quirksCssRun = bodyCssFace is not null
        && !Regex.IsMatch(html, @"<!doctype", RegexOptions.IgnoreCase);

    // Element-styled fixed-grid document (quirks): the stylesheet sizes the
    // TABLE element itself and borders the cells by ELEMENT rule. Its
    // inter-table <br/>s keep their line boxes — each grid is separated
    // from the next by one.
    profile.elementGridDoc = !Regex.IsMatch(html, @"<!doctype", RegexOptions.IgnoreCase)
        && css.TryGetValue("table", out var egTbl) && egTbl.ContainsKey("width")
        && css.TryGetValue("td", out var egTd) && egTd.ContainsKey("border");
    }

    /// <summary>Decides whether the document is an article, a newsletter or a chart card, and settles the face and size its body text is measured in.</summary>
    /// <remarks>Lifted verbatim out of the document analysis in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void DetectArticleAndNewsletterFlow(string? bodyCssFace, Dictionary<string, Dictionary<string, string>> css, string html, List<byte[]> inlineSvgs, bool marginsExplicit, HtmlDocProfile profile, ref bool articleFlow, ref double articleLineFactor, ref double bodyMarT, ref double marginTop)
    {
    if (!profile.metricFlow && !marginsExplicit && profile.bodyZeroMargin && profile.bodyCssFontPt > 0
        && css.TryGetValue("body", out var artBody)
        && artBody.TryGetValue("font-size", out var artFs)
        && artFs.TrimEnd().EndsWith("rem", StringComparison.OrdinalIgnoreCase)
        && artBody.TryGetValue("line-height", out var artLh)
        && double.TryParse(artLh.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var artLhF)
        && artLhF is > 1.0 and < 2.5
        && bodyCssFace is not null && WinMetricsFor(bodyCssFace) is not null)
    {
        profile.metricFlow = true;
        articleFlow = true;
        profile.metricFace = bodyCssFace;
        articleLineFactor = artLhF;
    }

    // The pt-sized clinical REPORT: a BODY rule pinning a resolvable face at
    // an absolute pt size beside a TABLE rule carrying a family, on a
    // table-heavy sheet — the expected render lays it out as a metric flow
    // in that face (hhea line boxes), css class typography driving both the
    // flow blocks and the cell grids.
    profile.ptReportDoc = false;
    // the pt-report family's NEWSLETTER arm (inline-body-styled email):
    // in-cell paragraph segments, UA p margins and the quirks body margin
    // are ITS dialect — the NHS/boleto report greens keep the whole-cell model.
    profile.emailNewsletterDoc = false;
    profile.ptTableFontPt = 0.0;
    if (!profile.metricFlow && !marginsExplicit
        && css.TryGetValue("body", out var ptBody)
        && ptBody.TryGetValue("font-family", out var ptFam0)
        && FirstFontFamily(ptFam0) is { } ptFam && WinMetricsFor(ptFam) is not null
        && ptBody.TryGetValue("font-size", out var ptFs0)
        && Regex.IsMatch(ptFs0.Trim(), @"^[\d.]+\s*pt$", RegexOptions.IgnoreCase)
        && css.TryGetValue("table", out var ptTbl) && ptTbl.ContainsKey("font-family")
        && Regex.Matches(html, @"<table\b", RegexOptions.IgnoreCase).Count >= 5)
    {
        profile.ptReportDoc = true;
        profile.metricFlow = true;
        profile.metricFace = ptFam;
        profile.metricLineSum = HheaLineSumFor(ptFam) ?? 0;
        // The metric report opens at the raw 72 pt content top (the legacy
        // calibrated 89 belongs to the flow this document left).
        marginTop = 72.0;
        // The body rule authors MARGIN-TOP: 0cm — content opens at the page
        // margin with no UA body inset. (TryParseLength rejects an explicit
        // zero by design, so the zero idiom is matched first.)
        bodyMarT = ptBody.TryGetValue("margin-top", out var ptMt)
            ? Regex.IsMatch(ptMt.Trim(), @"^0(\.0+)?\s*(cm|mm|px|pt|em|in)?$")
                ? 0.0
                : TryParseLength(ptMt.Trim(), out var ptMtPt) ? ptMtPt : 6.0
            : 6.0;
        profile.formBodyFontPt = double.Parse(Regex.Match(ptFs0, @"[\d.]+").Value,
            System.Globalization.CultureInfo.InvariantCulture);
        if (ptTbl.TryGetValue("font-size", out var ptTfs)
            && TryParseCssFontSize(ptTfs.Trim(), out var ptTfsPt))
            profile.ptTableFontPt = ptTfsPt;
    }
    // …or the same declaration INLINE on the body tag: the NEWSLETTER shape
    // (an Arial px email with zero body margins whose whole layout is
    // table-built) renders through the same metric route.
    if (!profile.ptReportDoc && !profile.metricFlow && !marginsExplicit
        && Regex.Match(html, @"<body\b[^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase) is { Success: true } ebM
        && Regex.Match(ebM.Groups[1].Value, @"font-family\s*:\s*([^;]+)",
            RegexOptions.IgnoreCase) is { Success: true } ebFam0
        && FirstFontFamily(ebFam0.Groups[1].Value) is { } ebFam
        && WinMetricsFor(ebFam) is not null
        && Regex.Match(ebM.Groups[1].Value, @"font-size\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase) is { Success: true } ebFs
        && Regex.Matches(html, @"<table\b", RegexOptions.IgnoreCase).Count >= 5)
    {
        profile.ptReportDoc = true;
        profile.emailNewsletterDoc = true;
        profile.metricFlow = true;
        profile.metricFace = ebFam;
        profile.metricLineSum = HheaLineSumFor(ebFam) ?? 0;
        // page margin + the quirks body's default 8px margin (the inline
        // style declares no margins of its own)
        marginTop = 72.0 + UaBodyMarginPt;
        profile.formBodyFontPt = double.Parse(ebFs.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture) * 0.75;
    }

    // SSRS report export (the ReportingServices HTML renderer's
    // grow-rectangles wrapper): its cells run the paragraph-segment model
    // and its oversized data-URI JPEG widens the sheet (see the widen below).
    profile.ssrsReportDoc = html.Contains(
        "Microsoft_ReportingServices_HTMLRenderer", StringComparison.OrdinalIgnoreCase);

    // Chart-card documents: a body{margin:0} page whose visible content is an
    // inline-SVG chart in a padded widget card (the saved React/c3 report
    // shape). The container class chrome positions the blocks
    // (containerBoxIndents) and the page widens to the chart's natural size.
    // A metric/article-flow document keeps its own dialect even when it ships
    // decorative inline SVGs (a docs site's icons must not re-route it).
    profile.chartCardDoc = profile.bodyZeroMargin && !profile.metricFlow && inlineSvgs.Count > 0;
    }
}
