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
        /// <summary>A UA document whose sheet keeps every cell on one line (`table td { white-space: nowrap }`): its grids measure their serif floors as a bare document's do.</summary>
        public bool uaNoWrapCellRule;
        /// <summary>A stylesheet was referenced but could not be reached.</summary>
        public bool deadExternalCss;
        /// <summary>No sheet at all, and the grid cells author their own typography.</summary>
        public bool cellAuthoredTypography;
        /// <summary>An inline span declared BOTH its family and its own size.</summary>
        public bool inlineSpanTypography;
        // The document reached this flow because its SIZED spans name faces the flow
        // draws (see InlineFamiliesDisqualify): those faces are the run faces its
        // lines wrap on, where a sheet-driven document keeps the flow's measure face.
        public bool inlineRunFaces;
        public bool quirksCssRun;
        // the legacy leftmargin/marginwidth=0 body: zero SIDE margins, the top keeps its inset
        public bool bodySideOnlyZero;
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
        /// <summary>A UA-serif document whose table cells hold BLOCK children with typography of
        /// their own (`&lt;p style="font-size:12px">`, headings): the cells lay each block out as a
        /// paragraph segment at its own size, bold per line, a right float on the right.</summary>
        public bool uaBlockCells;
        /// <summary>A UA-serif document whose table cells hold text-like form controls (the
        /// worksheet shape: `&lt;td>&lt;input type="text">` beside its labels): every table draws as a
        /// metric grid with the controls as replaced boxes - a text input at its intrinsic or
        /// percent width, a checkbox after its own margin - the rows pacing on their ink and
        /// banding on declared heights.</summary>
        public bool uaFormCells;
        public bool redlineDiffDoc;
        public bool sectionedReport;
        /// <summary>The document's grids lay out on the browser's own cell box model (the sectioned
        /// report, and the table-carried resume and statement shapes).</summary>
        public bool uaGridBoxes;
        /// <summary>…claimed by the UA-grid document shape itself (not a sectioned report): its BLOCKS lay out on the
        /// browser's model too.</summary>
        public bool uaGridSheet;
        public bool ssrsReportDoc;
        public bool ptReportDoc;
        /// <summary>The pt report's FORM shape (see SheetCellClassCarriesBodyFace): its grids solve on
        /// the probed pt-form column rule and a grid wider than the body at min-content grows the sheet.</summary>
        public bool ptFormDoc;
        public bool ptStyledFragment;
        public double ptTableFontPt;
        /// <summary>A Word-filtered export.</summary>
        public bool msoFilteredDoc;
        /// <summary>A Word mail: a WordSection1 body of MsoNormal paragraphs whose header rows tab their values with mso-tab-count spans.</summary>
        public bool wordMailDoc;
        // A Word 2000-2003 export (Generator "Microsoft Word N", div.Section1, mso-list paragraphs): the
        // Word-mail dialect under its own sheet's element typography, on the UA page box.
        public bool wordExportDoc;
        public bool chartCardDoc;
        public bool floatBandDoc;
        public bool floatImageDoc;
        public bool floatBothSidesDoc;
        /// <summary>The caller keeps font embedding on (HtmlLoadOptions.IsEmbedFonts): the UA
        /// serif's family-free text draws the real face; off, it keeps the Standard-14 one.</summary>
        public bool embedFonts = true;
        public bool formHorizontalDoc;
        public bool formDialectTables;
        public double formBodyFontPt;
        public bool bodyWidthFullDoc;
        public bool bodyZeroMargin;
        /// <summary>The sheet was sized to a declared table's box inside a host cell: the content box already
        /// reaches the overflowing right edge, so the grids take no further body inset on the right.</summary>
        public bool inCellSheet;
        /// <summary>The ink width the sheet grew to hold (see WidenPageToInk); an image up to
        /// this wide draws at its declared size past the text box instead of shrinking.</summary>
        public double inkWidenPt;
        /// <summary>A min-floor grid's ink sized the sheet: an auto-width table keeps the body inset
        /// on its right of that grown sheet, the grid itself standing at its floors past it (measured:
        /// the holdings grid fills 449.75 + its frame on the 455.91 box the returns grid's 455.75 of
        /// floors overrun; a percent table's box is the inset span already).</summary>
        public bool minFloorSheet;
        public double bodyPinnedW;
        public double bodyCssFontPt;
    // A PERCENT body size, in points of the 16 px UA root (`body { font-size: 62.5% }` = 7.5): the
    // flow's root size only - it flips none of the dialect gates the px/pt body size does.
    public double bodyPctFontPt;
    /// <summary>The size the body's own tag or class states on the UA flow (0 = none): its cells inherit it.</summary>
    public double bodyOwnFontPt;
    // The field-list dialect (MEASURED, the change-control print page): a sheet whose `div > span:first-child`
    // rule makes the first span of each field row an inline-block LABEL column - bold, a stated share of the
    // fields box wide, a `:after` suffix - with the value span seated beside it on the label's last line.
    public bool fieldListDoc;
    public double fieldLabelFrac;
    public string fieldLabelSuffix = "";
    public string? fieldsClass;
    public double fieldsInsetPt;
        public Color? bodyCssColor;
        public double bodyLineHeightPt;
        // The body line-height as a FACTOR of the running size where the rule states a percent or a bare
        // number (inherited as factors: a 12 px run under `line-height: 125%` steps 15 px); 0 for a length or none.
        public double bodyLineHeightFactor;
        /// <summary>The body's ABSOLUTE CSS line-height (px/pt/cm/mm/in): the line box every
        /// UA-flow line and break takes, whatever its font size (measured: a body
        /// `line-height: 1.5pt` piles the dunning letter's 10 pt lines 1.5 pt apart).</summary>
        public double bodyLineBoxPt;
        public double fsBoxW;
        /// <summary>The caller asked for the page to scale to content width.</summary>
        public bool scaleToPageWidth;
        /// <summary>The document's CSS3 paged-media page NAMES, selector -> name, read from the raw
        /// style blocks: a `div.WordSection1 { page: WordSection1 }` names the page its elements want.
        /// Read raw because the flows that need it are the ones whose rule map is dropped whole.</summary>
        public Dictionary<string, string>? namedPageRules;
        /// <summary>A Word-FILTERED page that carries a box wider than the default content box -
        /// the shape the arm's grown 721.75 pt sheet and its 1.00 em paragraph margin were both
        /// calibrated on. A filtered page without one keeps plain A4 and the UA 1.12 em.</summary>
        public bool msoFilteredGrownSheet;
        public List<CssChainRule>? docChainRules;
        /// <summary>The sheet's only tree-addressed rules are DESCENDANT CELL rules. They dress the cells
        /// of its grids, and nothing else about the document changes: a sheet that states one of these and
        /// nothing more never had a chain cascade to be calibrated against.</summary>
        public bool docChainCellRulesOnly;
        /// <summary>The sheet's adjacent-sibling CELL rules (see <see cref="CssSiblingCellRule"/>):
        /// a cell dressed by the cell that closed before it.</summary>
        public List<CssSiblingCellRule>? docSiblingCellRules;
        /// <summary>A chain-dialect document on the calibrated flow: its sheet's element and
        /// class rules size, face and pace the blocks, its emphasis and coloured spans draw as
        /// runs in the sheet's face, and its floats keep the sheet's own margins.</summary>
        public bool sheetTypographyDoc;
        /// <summary>The sheet-typography flow's line-box seating (the flat calibrated flow only:
        /// the metric flow already seats its baselines inside CSS line boxes).</summary>
        public bool sheetBoxFlow;
        public Dictionary<string, int> gridRadioCounts = new();
        public Dictionary<string, Aspose.Pdf.Forms.RadioButtonField> gridRadioGroups = new();
        /// <summary>UA fieldset boxes: a table inside the frame fills the frame's content box (see uaFieldsetBoxes).</summary>
        public bool uaFieldsetContent;
        public List<(Aspose.Pdf.Forms.RadioButtonField rbf, Page page)> gridRadioPages = new();
        /// <summary>A DataWorks form export; its own font and border conventions.</summary>
        public bool dwFormDoc;
    }

    /// <summary>The field-list dialect: recognised from the sheet's `span:first-child` label rule (kept by the
    /// body-class chain flattening) on a document whose body class seeds the UA flow.</summary>
    private static void DetectFieldListDoc(ConvertState cv)
    {
        var p = cv.profile;
        p.fieldListDoc = false;
        if (!cv.uaBodyFaceFromAttr || !cv.css.TryGetValue("span:first-child", out var labelRule)) return;
        if (!labelRule.TryGetValue("display", out var disp) || !disp.Trim().Equals("inline-block", StringComparison.OrdinalIgnoreCase)) return;
        var frac = labelRule.TryGetValue("width", out var w) ? PercentFraction(w) : 0;
        if (frac <= 0 && labelRule.TryGetValue("min-width", out var mw)) frac = PercentFraction(mw);
        if (frac <= 0) return;
        p.fieldListDoc = true;
        p.fieldLabelFrac = frac;
        p.fieldLabelSuffix = cv.css.TryGetValue("span:first-child:after", out var afterRule)
            && afterRule.TryGetValue("content", out var content) ? content.Trim().Trim('"', '\'') : "";
        p.fieldsClass = null;
        p.fieldsInsetPt = 0;
        var em = cv.uaBodyFontPt > 0 ? cv.uaBodyFontPt : UaDefaultFontPt;
        foreach (var kv in cv.css)
            if (Regex.Match(kv.Key, @"^div\.([\w-]+)$", RegexOptions.IgnoreCase) is { Success: true } dm
                && kv.Value.TryGetValue("padding", out var pad)
                && Regex.Matches(cv.html, @"class\s*=\s*[""'][^""']*\b" + Regex.Escape(dm.Groups[1].Value) + @"\b", RegexOptions.IgnoreCase).Count == 1)
            {
                p.fieldsClass = dm.Groups[1].Value;
                p.fieldsInsetPt = ChainPadPt(pad, em).L;
                break;
            }
    }

    /// <summary>Decides whether the document declares more grid columns than it fills, and measures the widest table that settles it.</summary>
    /// <remarks>Lifted verbatim out of the document analysis in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void DetectOverDeclaredGrid(string? bodyCssFace, double availContentW, List<Block> blocks, Dictionary<string, Dictionary<string, string>> css, List<byte[]> inlineSvgs, HtmlLoadOptions? options, HtmlDocProfile profile, ConvertState cv)
    {
    foreach (var b in blocks)
    {
        // A wrapper-stack table lays out through the recursive metric path,
        // whose children fit the symmetric content frame — the flat probe
        // would measure the merged monster and widen a sheet the render
        // never fills.
        if (b.IsTable && profile.uaStdSerif && !profile.deadExternalCss
            && TrySplitWrapperStack(b.TableHtml ?? "") is (_, _))
            continue;
        // An unpainted wrapper's declared box is not ink: the sheet follows the table it wraps
        // (its own declared width is what the width scan below still counts).
        if (profile.wordMailDoc && b.IsTable && b.TableHtml is { } wrapHtml
            && IsUnpaintedWrapperTable(wrapHtml, Math.Max(0, wrapHtml.IndexOf("<table", StringComparison.OrdinalIgnoreCase))))
            continue;
        DetectOverDeclaredGridFromTable(b, cv, profile, css, inlineSvgs, options, availContentW, bodyCssFace);
    }
    }

    /// <summary>Applies the print-grid dialect's page geometry: its column base, margins and content width.</summary>
    /// <remarks>Lifted verbatim out of the document analysis in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void ApplyPrintGridBase(Dictionary<string, Dictionary<string, string>> css, HtmlDocProfile profile, ConvertState cv)
    {
    if (profile.printGrid)
    {
        // Wrapper chrome: a whole-content wrapper div's inline padding lands
        // inside the page margins on BOTH sides (the UA body margin is already
        // baked into the 96pt default; the right margin mirrors the left).
        double wrapPad = 0;
        var wpm = Regex.Match(cv.html,
            @"<div\b[^>]*class\s*=\s*[""'][^""']*container[^""']*[""'][^>]*style\s*=\s*[""'][^""']*padding\s*:\s*(\d+(?:\.\d+)?)\s*px",
            RegexOptions.IgnoreCase);
        if (wpm.Success && double.TryParse(wpm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wrapPx))
            wrapPad = wrapPx * 0.75;
        cv.marginLeft += wrapPad;
        cv.marginRight = cv.marginLeft;
        cv.marginTop += wrapPad;
        if (css.TryGetValue("body", out var pgBody))
        {
            if (pgBody.TryGetValue("font-size", out var pgFs) && TryParseLength(pgFs) is { } pgPt && pgPt > 0)
                profile.printGridBase = pgPt;
            if (pgBody.TryGetValue("line-height", out var pgLh)
                && double.TryParse(pgLh, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pgLf)
                && pgLf is > 0.5 and < 3)
                cv.printGridLineFactor = pgLf;
        }
        if (profile.printGridBase <= 0) profile.printGridBase = 12;
        // The first line box sits ~5pt lower than the legacy
        // first-baseline calibration under the metric model.
        cv.marginTop += 5.0;
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
            foreach (Match hm in hostRx.Matches(cv.html)) hosts.Add(hm);
            for (var hi = hosts.Count - 1; hi >= 0; hi--)
            {
                var contentStart = hosts[hi].Index + hosts[hi].Length;
                var (divEnd, hostEnd) = FindDivEnd(cv.html, contentStart);
                if (divEnd < 0) continue;
                var region = cv.html[contentStart..hostEnd];
                region = Regex.Replace(region, "<" + hTag + @"\b", "<" + hTag + attr, RegexOptions.IgnoreCase);
                cv.html = cv.html[..contentStart] + region + cv.html[hostEnd..];
            }
        }
    }
    }

    /// <summary>The UA root font size a percent body size resolves against (16 px).</summary>
    private const double UaRootFontPt = 12.0;

    /// <summary>A body `font-size: N%` in points of the UA root; null for any other spelling (probed on the
    /// hospital letter: `62.5%` sizes its 1.02em runs 7.65 pt, the same as `10px`; without it 12.24).</summary>
    private static double? BodyPercentFontPt(string value)
    {
        var m = System.Text.RegularExpressions.Regex.Match(value.Trim(), @"^([0-9.]+)\s*%$");
        return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct > 0
            ? UaRootFontPt * pct / 100.0 : null;
    }

    /// <summary>Reads the body's declared font and colour, and whether the page is an element grid.</summary>
    /// <remarks>Lifted verbatim out of the document analysis in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void DetectBodyCssAndGrid(Dictionary<string, Dictionary<string, string>> css, string html, HtmlDocProfile profile, ConvertState cv)
    {
    profile.bodyCssColor = null;
    SheetEmBasePt = 0;
    if (css.TryGetValue("body", out var bodyPctDecls) && bodyPctDecls.TryGetValue("font-size", out var bodyPctV)
        && BodyPercentFontPt(bodyPctV) is { } bodyPctPt) { profile.bodyPctFontPt = bodyPctPt; SheetEmBasePt = bodyPctPt; }
    if (profile.bodyZeroMargin && css.TryGetValue("body", out var bodyFontDecls)
        && bodyFontDecls.TryGetValue("font-size", out var bodyFontSize)
        && TryParseLength(bodyFontSize) is { } bodyFontPt && bodyFontPt > 0)
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
                if (f.Length > 0 && WinMetricsFor(f) is not null) { cv.bodyCssFace = f; break; }
            }
    }

    // Quirks-mode CSS-run documents: a resolvable body face but NO <!DOCTYPE>
    // (CKEditor notes, Outlook/Teams exports). Two behaviours hang off this:
    // their tables render at the UA 16px cell base through the metric layouter
    // (the body rule's pixel font does not inherit into cells in quirks mode),
    // and their text honours inline-block title columns and dash-break
    // overflow wrapping (both measured on the references).
    profile.quirksCssRun = cv.bodyCssFace is not null
        && !Regex.IsMatch(html, @"<!doctype", RegexOptions.IgnoreCase);

    // Element-styled fixed-grid document (quirks): the stylesheet sizes the
    // TABLE element itself and borders the cells by ELEMENT rule. Its
    // inter-table <br/>s keep their line boxes — each grid is separated
    // from the next by one.
    profile.elementGridDoc = !Regex.IsMatch(html, @"<!doctype", RegexOptions.IgnoreCase)
        && css.TryGetValue("table", out var egTbl) && egTbl.ContainsKey("width")
        && css.TryGetValue("td", out var egTd) && egTd.ContainsKey("border");
    }

    /// <summary>The form shape of the pt-sized report: a class rule naming the body's own face
    /// is worn by a table cell, and a cell seats a text input (the helpdesk request form's
    /// `.Form-table-cell-label { font-family: Tahoma }` beside `BODY { font-family: Tahoma }`).</summary>
    private static bool SheetCellClassCarriesBodyFace(Dictionary<string, Dictionary<string, string>> css, string html, string bodyFace)
    {
        if (!Regex.IsMatch(html, @"<t[dh]\b[^>]*>(?:(?!</t[dh]\b).)*?<input\b[^>]*type\s*=\s*[""']?text\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            return false;
        foreach (var (sel, rule) in css)
        {
            if (sel.Length < 2 || sel[0] != '.' || !rule.TryGetValue("font-family", out var fam)) continue;
            if (FirstFontFamily(fam) is not { } face || !face.Equals(bodyFace, StringComparison.OrdinalIgnoreCase)) continue;
            if (Regex.IsMatch(html, @"<t[dh]\b[^>]*\bclass\s*=\s*[""']?" + Regex.Escape(sel[1..]) + @"\b", RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Decides whether the document is an article, a newsletter or a chart card, and settles the face and size its body text is measured in.</summary>
    /// <remarks>Lifted verbatim out of the document analysis in
    /// <see cref="ConvertFromHtml"/>.</remarks>
    private static void DetectArticleAndNewsletterFlow(string? bodyCssFace, Dictionary<string, Dictionary<string, string>> css, string html, List<byte[]> inlineSvgs, bool marginsExplicit, HtmlDocProfile profile, ConvertState cv)
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
        cv.articleFlow = true;
        profile.metricFace = bodyCssFace;
        cv.articleLineFactor = artLhF;
    }

    // The pt-sized clinical REPORT: a BODY rule pinning a resolvable face at
    // an absolute pt size beside a TABLE rule carrying a family, on a
    // table-heavy sheet — the expected render lays it out as a metric flow
    // in that face (hhea line boxes), css class typography driving both the
    // flow blocks and the cell grids.
    DetectPtReportAndNewsletterFlow(cv, profile, css, html, marginsExplicit);

    // UA cells holding TEXT INPUTS (the worksheet's `<td><input type="text">` rows): the
    // form-cell model - the controls are replaced boxes inside metric grids (measured on the
    // worksheet: its 90 % inputs sit in a 35 %-labelled nested grid, the checkbox column beside
    // a 936 px column; the flat control path had put every cell on its own line).
    // The pt-sized report arm takes it too: a form whose sheet sizes the body in points and
    // seats its inputs in cells is the same UA grid in its own face (measured on the test
    // request: Verdana 8 pt labels beside 284 px inputs, every table gridded inside a
    // fieldset, whatever the sheet's dead external links).
    profile.uaFormCells = ((profile.uaStdSerif && !profile.deadExternalCss) || profile.ptReportDoc)
        && Regex.IsMatch(html, @"<t[dh]\b[^>]*>(?:(?!</t[dh]\b).)*?<input\b[^>]*type\s*=\s*[""']?text\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

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
