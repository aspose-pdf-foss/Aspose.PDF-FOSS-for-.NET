using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private const double PxPt = 0.75;
    // Styled ("<family> Bold"/" Italic") face bytes for the mixed-size cell
    // advances only — the plain PosFace deliberately misses these full names
    // (the calibrated column models measured on that behaviour), but a run
    // advance measured with the fallback em under-spaces the pen.
    // Fixtures convert in parallel: every lazy face/metric cache below must take
    // concurrent writers (a plain Dictionary corrupts and then throws for good).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Text.GlyphOutlineParser? parser, double upm)>
        _styledMeasureCache = new(StringComparer.Ordinal);

    // ── CSS-faithful metric flow helpers ────────────────────────────────────────
    // Line model: a line box is
    // round(sizePx · (winAscent+winDescent)/em) px tall, and the baseline sits at
    // halfLeading + ascent below the box top, halfLeading = (box − size·(wa+wd)/em)/2.

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (double asc, double sum)?> _winMetricsCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double?> _xHeightCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double?> _hheaLineSumCache =
        new(StringComparer.OrdinalIgnoreCase);


    /// <summary>A non-breaking space is CONTENT, whatever char.IsWhiteSpace says of it.</summary>
    private const char NoBreakSpaceChar = (char)0xA0;

    /// <summary>Nothing but collapsible whitespace has reached the open cell yet — the markup's
    /// indentation before its first real content.</summary>
    private static bool MetricTextIsBlank(StringBuilder text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (IsRunMark(text[i])) continue;
            if (!char.IsWhiteSpace(text[i]) || text[i] == NoBreakSpaceChar) return false;
        }
        return true;
    }

    private static void RenderMetricTable(Document doc, FlowPosition cursor,
        string tableHtml, IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double marginLeft, double contentWidth, double pageWidth, double pageHeight,
        double marginTop, double marginBottom, string face, (double asc, double sum) fm,
        Core.PdfDictionary docFontDict, bool stdSerif = false, double baseFontSize = 11,
        bool wrapperStacks = false, double symInsetPt = UaBodyMarginPt, bool rtl = false,
        bool paragraphCells = false, bool serifReportCells = false,
        HtmlLoadOptions? loadOptions = null,
        List<CssSiblingCellRule>? siblingCellRules = null, bool uaBlockCells = false,
        bool uaFormCells = false, (double x, double y)? pageMargin = null, double continuationInsetPt = 0,
        double hostCellPadPt = 0, bool keepRightInset = false, string[]? hostBlockClasses = null, bool ptFormCells = false)
    {

        // Wrapper stacks (legacy nested-table markup): a table whose every row is
        // a single td holding only tables contributes CHROME, not a grid — its
        // children stack inside insets of (2 x border) + cellspacing + cellpadding,
        // and a border=1 wrapper draws the browser's two beveled 1px frames
        // (outset: #555 top+left over black bottom+right; inset the reverse)
        // around the stacked extent. Measured: margin 96 -> 96.75
        // (plain wrapper, p=1px) -> 98.25+0.75 = 99 through a bordered one.
        // the page margin box the absolutely positioned content offsets from: the outer table's
        // flow origin less the body inset on its left (the top margin carries no inset: the body
        // margin is spent by the flow), carried into every nested grid
        var pageOrigin = pageMargin ?? (marginLeft - symInsetPt, marginTop);
        if (!TryRenderStackedWrapper(wrapperStacks, doc, cursor, tableHtml, css, face, docFontDict, loadOptions, serifReportCells, symInsetPt, marginLeft, contentWidth, pageWidth, pageHeight, marginTop, marginBottom, fm, stdSerif, baseFontSize, paragraphCells, siblingCellRules, uaBlockCells, uaFormCells, pageOrigin, hostBlockClasses, ptFormCells)) return;

        var mt = ParseMetricTable(doc, tableHtml, css, marginLeft, contentWidth, pageWidth, pageHeight,
            marginTop, marginBottom, face, fm, docFontDict, stdSerif, baseFontSize, wrapperStacks,
            symInsetPt, rtl, paragraphCells, serifReportCells, loadOptions, siblingCellRules, uaBlockCells,
            uaFormCells, pageOrigin, continuationInsetPt, hostCellPadPt, keepRightInset, hostBlockClasses, ptFormCells);
        mt.cursor = cursor;
        if (!SolveMetricTable(mt))
        {
            // (a pt form table with no rows still stands its two border-spacings tall - probed:
            //  the empty header table advances the flow 1.5)
            if (mt.ptFormCells) cursor.y -= 2 * mt.s;
            return;
        }
        PaintMetricTable(mt);
    }

    /// <summary>A metric table's state with its style read and its markup parsed into rows - the
    /// render solves and paints it, a nested max-content measure only reads its rows.</summary>
    private static MetricTableState ParseMetricTable(Document doc, string tableHtml,
        IReadOnlyDictionary<string, Dictionary<string, string>> css, double marginLeft, double contentWidth,
        double pageWidth, double pageHeight, double marginTop, double marginBottom, string face,
        (double asc, double sum) fm, Core.PdfDictionary docFontDict, bool stdSerif, double baseFontSize,
        bool wrapperStacks, double symInsetPt, bool rtl, bool paragraphCells, bool serifReportCells,
        HtmlLoadOptions? loadOptions, List<CssSiblingCellRule>? siblingCellRules, bool uaBlockCells = false,
        bool uaFormCells = false, (double x, double y)? pageMargin = null, double continuationInsetPt = 0,
        double hostCellPadPt = 0, bool keepRightInset = false, string[]? hostBlockClasses = null, bool ptFormCells = false)
    {
        var mt = new MetricTableState();
        mt.hostCellPadPt = hostCellPadPt;
        mt.keepRightInset = keepRightInset;
        mt.doc = doc;
        mt.tableHtml = tableHtml;
        mt.css = stdSerif ? ScopedTableCss(css, tableHtml) : css;
        mt.marginLeft = marginLeft;
        mt.contentWidth = contentWidth;
        mt.pageWidth = pageWidth;
        mt.pageHeight = pageHeight;
        mt.marginTop = marginTop;
        mt.marginBottom = marginBottom;
        mt.face = face;
        mt.fm = fm;
        mt.docFontDict = docFontDict;
        mt.stdSerif = stdSerif;
        mt.baseFontSize = baseFontSize;
        mt.wrapperStacks = wrapperStacks;
        mt.symInsetPt = symInsetPt;
        mt.rtl = rtl;
        mt.paragraphCells = paragraphCells;
        mt.serifReportCells = serifReportCells;
        mt.loadOptions = loadOptions;
        mt.siblingCellRules = siblingCellRules;
        // (the pt form flag is read by the table style init: its class frame and padding)
        mt.ptFormCells = ptFormCells;
        InitMetricTableStyle(mt);
        mt.mps.uaBlockCells = uaBlockCells;
        mt.mps.uaFormCells = uaFormCells;
        mt.mps.ptFormCells = ptFormCells;
        mt.mps.hostBlockClasses = hostBlockClasses;
        mt.mps.continuationInsetPt = continuationInsetPt;
        (mt.mps.pageMarginLeft, mt.mps.pageMarginTop) = pageMargin ?? (marginLeft - symInsetPt, marginTop);
        InitMetricRowState(mt);
        ParseMetricTokens(mt);
        return mt;
    }

    /// <summary>One cell of a collapsed-grid table (see <see cref="RenderBodyBoxGridTable"/>).</summary>
    private sealed partial class GridCell
    {
        public int ColSpan = 1;
        public double WidthPct;                               // width="40%" attribute
        public bool BorderLeftZero, BorderRightZero;          // style border-left/right: 0px
        public HorizontalAlignment Align = HorizontalAlignment.Left;
        public List<(string Text, bool Bold, bool Italic)> Runs = new();
        public string? ImgB64;                                // data-URI PNG payload
        public double ImgPct;                                 // img width="N%" attribute
        public List<List<(string Text, bool Bold, bool Italic)>> Lines = new();
        public int Col;                                       // first column index
    }

    private sealed partial class Token
    {
        public TokenKind Kind;
        public string? Tag;
        public bool IsClose;
        public bool IsSelfClosing;
        public Dictionary<string, string>? Attributes;
        public string Value = "";
        // Source span of this token in the tokenized string (element extraction).
        public int SrcIndex;
        public int SrcEnd;
    }

    /// <summary>A lightweight DOM node built from the tokenizer: enough tree structure
    /// (tag, attributes, children, source span) to resolve descendant CSS and extract
    /// styled-run rows. Tag == "" marks a text node.</summary>
    private sealed partial class HtmlNode
    {
        public string Tag = "";
        public string Text = "";
        public Dictionary<string, string>? Attrs;
        public List<HtmlNode> Children = new();
        public HtmlNode? Parent;
        public int SrcIndex;
        public int SrcEnd;

        public IEnumerable<HtmlNode> Descendants()
        {
            foreach (var c in Children)
            {
                yield return c;
                foreach (var d in c.Descendants()) yield return d;
            }
        }
    }


    /// <summary>Paints the table: the collapse frame, the rows, the border grid and the background underlay, advancing y.</summary>
    private static void PaintMetricTable(MetricTableState mt)
    {
        mt.cbFrameTopY = mt.cursor.y;
        mt.cbFramePage = mt.cursor.page;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MROW") == "1")
            Console.WriteLine("[mpaint] top=" + (mt.pageHeight - mt.cursor.y).ToString("0.##") + " collapseBoxW=" + mt.collapseBoxW + " frameW=" + mt.frameW + " s=" + mt.s + " rows=" + mt.rows.Count);
        if (mt.collapseBoxW > 0) mt.cursor.y -= mt.collapseBoxW;
        if (mt.frameW > 0) mt.cursor.y -= mt.frameW;
        mt.cursor.y -= mt.mps.tablePadTopPt;
        // The pt form's centred grid: its box (frame, table padding, spacings and column boxes) stands
        // midway on the sheet (probed: 96 + (544.609 - 533.717) / 2 = 101.446 for its 98 % tables; a
        // grid at its min-content, wider than the body, stays at the body's left).
        if (mt.mps.ptFormCells && mt.mps.centerTable && mt.nCols > 0)
        {
            var formBoxW = PtFormSideFramesWidthPt(mt) + 2 * mt.mps.tablePadLeftPt + (mt.nCols + 1) * mt.s;
            foreach (var w in mt.colW) formBoxW += w + 2 * mt.p;
            mt.tableX = Math.Max(mt.marginLeft, (mt.pageWidth - formBoxW) / 2);
        }
        PaintMetricCaption(mt);
        ComputeRowSpanBands(mt);
        RenderMetricRows(mt.mps, mt.rows, mt.colW, mt.nCols, mt.availW, mt.s, mt.lineH, mt.face, mt.boldFace, mt.hheaSum, mt.fm, mt.p, mt.pageWidth, mt.pageHeight, mt.marginTop, mt.marginBottom, mt.tableWpt, mt.tablePct, mt.baseFontSize, mt.paragraphCells, mt.tableHtml, mt.symInsetPt, mt.css, mt.doc, mt.docFontDict, mt.loadOptions, mt.invc, mt.flatRes, mt.reportCells, mt.serifReportCells, mt.stdSerif, mt.wrapperStacks, mt.collapseBoxW, mt.rowSpanExtra, mt.tableHasText, mt.tableRuleFace, mt.tableX + mt.frameW, mt.marginLeft, mt.contentWidth, mt.siblingCellRules, mt.cursor);
        mt.cursor.y -= mt.s;   // trailing cellspacing closes the table box
        mt.cursor.y -= mt.mps.tablePadBottomPt;
        // The inline-styled frame closes under the last row's spacing and strokes
        // around the whole box, centred half a width inside it.
        if (mt.frameW > 0)
        {
            mt.cursor.y -= mt.frameW;
            if (ReferenceEquals(mt.cbFramePage, mt.cursor.page))
            {
                double frBoxW = 2 * mt.frameW + (mt.nCols + 1) * mt.s;
                foreach (var w in mt.colW) frBoxW += w + 2 * mt.p;
                var frHalf = mt.frameW / 2;
                mt.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mt.invc,
                    $"q {mt.frameColor.R / 255.0:0.###} {mt.frameColor.G / 255.0:0.###} {mt.frameColor.B / 255.0:0.###} RG {mt.frameW:0.##} w ")
                    + RoundedRectPath(mt.tableX + frHalf, mt.cursor.y + frHalf, frBoxW - mt.frameW,
                        mt.cbFrameTopY - mt.cursor.y - mt.frameW, mt.frameRadius - frHalf, mt.invc) + " S Q\n"));
            }
        }
        // the sheet's table margin-bottom paces stacked grids (measured: 20px
        // between the collapse-grid boxes, measured pitch 55.875)
        if (mt.elemCollapseGrid && mt.css.TryGetValue("table", out var mbRule)
            && mbRule.TryGetValue("margin-bottom", out var mbV)
            && TryParseLength(mbV.Trim()) is { } mbPt && mbPt > 0)
            mt.cursor.y -= mbPt;
        // Outer-frame collapse grid: close the box below the last row and stroke
        // the frame around the whole table (stroke centred half a width inside).
        if (mt.collapseBoxW > 0)
        {
            mt.cursor.y -= mt.collapseBoxW;
            if (ReferenceEquals(mt.cbFramePage, mt.cursor.page))
            {
                double cbBoxW2 = 2 * mt.collapseBoxW + (mt.nCols + 1) * mt.s;
                foreach (var w in mt.colW) cbBoxW2 += w + 2 * mt.p;
                var cbHalf = mt.collapseBoxW / 2;
                mt.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mt.invc,
                    $"q {mt.mps.borderColor.R / 255.0:0.###} {mt.mps.borderColor.G / 255.0:0.###} {mt.mps.borderColor.B / 255.0:0.###} RG " +
                    $"{mt.collapseBoxW:0.##} w {mt.tableX + cbHalf:F2} {mt.cursor.y + cbHalf:F2} " +
                    $"{cbBoxW2 - mt.collapseBoxW:F2} {mt.cbFrameTopY - mt.cursor.y - mt.collapseBoxW:F2} re S Q\n")));
            }
        }

        // Paint the deferred band UNDER everything the rows drew, over the box's
        // REAL extent (sub-grids included). Same-page tables only — a paginated
        // band keeps whatever its rows drew.
        if (mt.tableBgUnderlay && mt.mps.tableBg is { } tbgcU
            && ReferenceEquals(mt.tableBgPage, mt.cursor.page) && mt.tableBgStartY > mt.cursor.y)
        {
            var bandW = (mt.nCols + 1) * mt.s;
            foreach (var w in mt.colW) bandW += w + 2 * mt.p;
            mt.tableBgPage.InsertContentStreamAt(mt.tableBgStartIdx,
                Encoding.ASCII.GetBytes(Compat.Format(mt.invc,
                    $"q {tbgcU.R / 255.0:0.###} {tbgcU.G / 255.0:0.###} {tbgcU.B / 255.0:0.###} rg " +
                    $"{mt.tableX:F2} {mt.cursor.y:F2} {bandW:F2} {mt.tableBgStartY - mt.cursor.y:F2} re f Q\n")));
        }
    }

    /// <summary>Closes the last row, solves the column widths and reserves the table's background; false when the table has no rows.</summary>
    /// <summary>A rule stating its own height stands that tall plus a border a side (probed on the
    /// Words letter: `height:2px` rules stand 3 pt in a 15 pt row - 1.5 + the 12 of UA margins);
    /// one declaring `width:100%` overflows its box by both borders, 127.5 across a 126 pt grid.</summary>
    private static void ReadMetricHrBox(MetricCell cell, Token tok)
    {
        if (tok.Attributes is not { } attrs || !attrs.TryGetValue("style", out var st) || st is null) return;
        if (Regex.Match(st, @"(?<![-\w])height\s*:\s*([\d.]+\s*(?:px|pt))", RegexOptions.IgnoreCase) is { Success: true } hm
            && TryParseLength(hm.Groups[1].Value.Replace(" ", "")) is { } hPt && hPt > 0)
            cell.HrBoxPt = hPt + 2 * HrGrooveW;
        if (Regex.IsMatch(st, @"(?<![-\w])width\s*:\s*100\s*%", RegexOptions.IgnoreCase))
            cell.HrOutsetPt = 2 * HrGrooveW;
    }

    private static bool SolveMetricTable(MetricTableState mt)
    {
        CloseRow(mt.mps, mt.rows, mt.text, mt.reportCells, mt.stdSerif);
        if (mt.rows.Count == 0) return false;
        // The table's own class typography sets the strut its rows pitch on: the size it
        // states, in the face it states (the line box was seeded from the flow base before
        // the tag was read - a 10 pt Arial grid banded its rows on the 12 pt serif line).
        if (mt.mps.tableClassFont && mt.mps.tableClassAbsoluteSize)
        {
            var strutSum = mt.mps.tableClassFace is { } tcf && WinMetricsFor(tcf) is { } tcfm ? tcfm.sum : mt.fm.sum;
            mt.lineH = MetricLineHeight(mt.mps.fontSize, strutSum <= 1.0 ? 1.2 : strutSum);
        }
        // …and the sheet's td rule line-height IS the strut every row pitches on, its blank <br> rows
        // included (MEASURED, the evaluation form: `TD { font-size: .75em; line-height: 150% }` bands
        // every row of its grids 13.5, the rows holding nothing but a <br> or a 1 px image too)
        if (mt.stdSerif && ElementRule(mt.css, "td") is { } strutTdRule
            && strutTdRule.TryGetValue("line-height", out var strutLh)
            && CellRuleLineHeightPt(strutLh, mt.mps.fontSize) is { } strutLhPt && strutLhPt > 0)
            mt.lineH = strutLhPt;
        SolveMetricColumns(mt);

        mt.tableHasText = false;
        foreach (var r in mt.rows)
            foreach (var mc in r)
                if (mc.Text.Length > 0) { mt.tableHasText = true; break; }

        // page-break-inside: avoid on the sheet's table rule — a table that cannot
        // finish in the space left on this page starts whole on a fresh one (and
        // still paginates row-at-a-time if it outgrows that full page). A table
        // already sitting at the page top has nothing to gain from breaking.
        if (mt.css.TryGetValue("table", out var pbiRule)
            && pbiRule.TryGetValue("page-break-inside", out var pbiV)
            && pbiV.Contains("avoid", StringComparison.OrdinalIgnoreCase)
            && mt.cursor.y < mt.pageHeight - mt.marginTop - 1e-6)
        {
            var tableH = mt.s;
            for (var ri = 0; ri < mt.rows.Count; ri++)
            {
                double rch = mt.tableHasText ? mt.lineH : 0;
                foreach (var mc in mt.rows[ri]) rch = Math.Max(rch, mc.ContentH);
                var rbh = rch + 2 * mt.p;
                if (ri < mt.mps.rowHeights.Count && mt.mps.rowHeights[ri] > rbh) rbh = mt.mps.rowHeights[ri];
                tableH += mt.s + rbh;
            }
            if (mt.cursor.y - tableH < mt.marginBottom)
            {
                mt.cursor.page = mt.doc.Pages.Add(mt.pageWidth, mt.pageHeight);
                EnsureFonts(mt.cursor.page, mt.docFontDict);
                mt.cursor.y = mt.pageHeight - mt.marginTop;
            }
        }

        // Bordered draw: outer border box, per-cell border boxes on the 2px
        // border-spacing grid, text at border+padding insets. Cell box heights =
        // content + padding + borders; strokes centred half a width inside.
        if (mt.mps.bordered) { RenderBorderedGrid(mt.mps, mt.rows, mt.colW, mt.nCols, mt.availW, mt.s, mt.bw, mt.lineH, mt.face, mt.boldFace, mt.hheaSum, mt.fm, mt.p, mt.pageWidth, mt.pageHeight, mt.marginTop, mt.marginBottom, mt.tableWpt, mt.tablePct, mt.baseFontSize, mt.paragraphCells, mt.tableHtml, mt.symInsetPt, mt.tableFills, mt.css, mt.doc, mt.docFontDict, mt.loadOptions, mt.invc, mt.stdSerif, mt.wrapperStacks, mt.rmtAnchorColor, mt.marginLeft, mt.contentWidth, mt.tableX, mt.cursor); return false; }

        mt.flatRes = new Dictionary<string, string>(StringComparer.Ordinal);
        // a page-less solve (a nested grid measured for its height) paints nothing
        if (mt.cursor.page is null) return true;
        mt.tableBgUnderlay = mt.mps.tableBg is not null && !mt.stdSerif && mt.wrapperStacks;
        mt.tableBgPage = mt.cursor.page;
        mt.tableBgStartIdx = mt.tableBgUnderlay ? mt.cursor.page.ContentStreamCount : 0;
        mt.tableBgStartY = mt.cursor.y;
        // (a UA form grid's class background paints page by page once its rows have drawn)
        if (mt.mps.tableBg is { } tbgc0 && !mt.tableBgUnderlay && !mt.mps.uaFormCells)
        {
            var bandH = mt.s;
            for (var ri = 0; ri < mt.rows.Count; ri++)
            {
                double rch = mt.tableHasText ? mt.lineH : 0;
                foreach (var mc in mt.rows[ri]) rch = Math.Max(rch, mc.ContentH);
                var rbh = rch + 2 * mt.p;
                if (ri < mt.mps.rowHeights.Count && mt.mps.rowHeights[ri] > rbh) rbh = mt.mps.rowHeights[ri];
                bandH += mt.s + rbh;
            }
            var bandW = (mt.nCols + 1) * mt.s;
            foreach (var w in mt.colW) bandW += w + 2 * mt.p;
            mt.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mt.invc,
                $"q {tbgc0.R / 255.0:0.###} {tbgc0.G / 255.0:0.###} {tbgc0.B / 255.0:0.###} rg " +
                $"{mt.tableX:F2} {mt.cursor.y - bandH:F2} {bandW:F2} {bandH:F2} re f Q\n")));
        }
        return true;
    }

    /// <summary>The sheet as this table reads it: the rules scoped to the table's own classes
    /// (`table.list`, `.list`, `table.list td`, `.list th`) folded over the bare `table` / `td` / `th`
    /// element rules, so every reader of an element rule sees the cascade the cells stand in
    /// (measured on the lab report: `table.list td { padding: 5px; border: thin solid }` pads and
    /// rules its cells, `table.list th { text-align: left }` seats its heads left).</summary>
    private static IReadOnlyDictionary<string, Dictionary<string, string>> ScopedTableCss(
        IReadOnlyDictionary<string, Dictionary<string, string>> css, string tableHtml)
    {
        var tag = Regex.Match(tableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!tag.Success) return css;
        var cm = Regex.Match(tag.Value, @"\bclass\s*=\s*[""']?([^""'>]+)", RegexOptions.IgnoreCase);
        if (!cm.Success) return css;
        // (the probed shape: a class rule that COLLAPSES the table's borders - `table.list { border-collapse:
        // collapse }` - and dresses its cells; a class-styled grid whose class collapses nothing keeps the
        // calibrated element rules its greens were measured on)
        // (a document-level idiom: any table class of the sheet collapsing its borders folds every
        // classed table's rules - the lab report's `table.internal td` inside its `table.list`)
        var collapsing = false;
        foreach (var kv in css)
        {
            var k = kv.Key.Trim();
            if ((k.StartsWith("table.", StringComparison.OrdinalIgnoreCase) || k.StartsWith('.')) && k.IndexOf(' ') < 0
                && kv.Value.TryGetValue("border-collapse", out var tbc) && tbc.Contains("collapse", StringComparison.OrdinalIgnoreCase))
            { collapsing = true; break; }
        }
        if (!collapsing) return css;
        Dictionary<string, Dictionary<string, string>>? scoped = null;
        foreach (var c in cm.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            foreach (var to in new[] { "table", "td", "th" })
                foreach (var from in new[] { "table." + c + (to == "table" ? "" : " " + to), "." + c + (to == "table" ? "" : " " + to) })
                {
                    if (!css.TryGetValue(from, out var rule)) continue;
                    // (copied entry by entry: netstandard2.0 / net48 have no constructor from a read-only map)
                    if (scoped is null)
                    {
                        scoped = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in css) scoped[kv.Key] = kv.Value;
                    }
                    var merged = scoped.TryGetValue(to, out var have)
                        ? new Dictionary<string, string>(have, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in rule) merged[kv.Key] = kv.Value;
                    scoped[to] = merged;
                }
        return scoped ?? css;
    }

    /// <summary>Walks the table markup token by token into rows, cells and styled segments.</summary>
    private static void ParseMetricTokens(MetricTableState mt)
    {
        foreach (var tok in Tokenize(StripNonContent(mt.tableHtml)))
        {
            if (!ParseMetricToken(mt, tok)) break;
        }
    }

    /// <summary>The parse cursor's row, cell, segment and span state before the first token.</summary>
    private static void InitMetricRowState(MetricTableState mt)
    {
        // (the sheet's `table td` rule reaches the cells as its bare `td` rule does)
        // (…or the table rule's collapse, inherited by the cells that carry the borders)
        ResolveMetricRowBoxWidth(mt);
        mt.rows = new List<List<MetricCell>>();
        // a CLASS height paces its row EXACTLY (the boleto's h13/h12 grid rows:
        // label 9.75 + value 9 measured as the pitch, content fitted inside);
        // a STYLE height keeps the calibrated raise-only behaviour
        mt.mps.pendingRowHExact = false;
        // Row-group ordering: thead rows render first and tfoot rows LAST regardless
        // of source order (a tfoot authored before the tbody still closes the table).
        mt.mps.curSection = 1;
        // Modern nesting (the UA-serif corpus): a table inside a CELL renders
        // as its own grid within that cell (extracted here, recursed at draw
        // time); the flat merge stays for the calibrated legacy dialects.
        if (mt.wrapperStacks)
        {
            // (a declared width is the grid's MINIMUM: a nested declared box wider than it, with
            //  that box's own padding and border, widens the grid - probed on the e-mail cards)
            if (mt.stdSerif) mt.nestedDeclaredFloorPt = InCellDeclaredTableIntrinsicPt(mt.tableHtml, mt.css, selfInCell: mt.symInsetPt == 0 && mt.wrapperStacks);
            (mt.tableHtml, mt.mps.nestedTables, mt.mps.nestedTableHostClasses) = ExtractNestedTablesWithHosts(mt.tableHtml);
        }
        mt.mps.cell = null;
        mt.mps.row = null;
        mt.text = new StringBuilder();
        // Per-effective-size text segments of the current cell — a new segment
        // opens when an inline span changes the size mid-cell. Kept only when
        // two sizes really meet (SizedRuns).
        mt.mps.boldDepth = 0;
        // b/strong transitions at raw-text positions — CloseCell rebuilds the
        // cell's interleaved Flow runs from them.
        mt.mps.sawTable = false;
        mt.mps.rowFs = null;
        mt.mps.rowAlign = null;
        mt.mps.rowBg = null;
        mt.mps.rowFace = null;
        mt.mps.rowFsFromClass = false;
        mt.mps.rowBold = false;
        mt.mps.rowFore = null;
        mt.mps.rowVTop = false;
        mt.mps.rowVBottom = false;
        mt.mps.tableBg = null;
        mt.mps.pendingRowH = 0;
        mt.mps.curSeg = null;
        mt.mps.pendingAbsLeftFrac = -1.0;
        mt.whiteSpans = new Stack<bool>();
        mt.floatSpans = new Stack<bool>();
        mt.rmtAnchorColor = null;
        if (mt.css.TryGetValue("a", out var rmtARule)
            && rmtARule.TryGetValue("color", out var rmtACol))
            mt.rmtAnchorColor = ParseCssColor(rmtACol);
        mt.mps.whiteDepth = 0;
        mt.spanSaves = new Stack<(double? fs, string? fc, bool b, Color? fo)>();
        mt.reportCells = mt.paragraphCells && (!mt.stdSerif || mt.serifReportCells) && mt.wrapperStacks;
        mt.mps.segBoldChars = 0;
        mt.mps.segPlainChars = 0;
        mt.mps.cellBoldChars = 0;
        mt.mps.cellPlainChars = 0;
        mt.mps.segFs = null;
        mt.mps.segFace = null;
        mt.mps.segFore = null;
        mt.mps.segInkSeen = false;
        mt.mps.leadFs = null;
        mt.mps.leadFace = null;
        mt.mps.leadFore = null;
        mt.mps.leadBold = false;
        mt.mps.leadSeen = false;
        mt.mps.nestDepth = 0;
        mt.mps.pendingNestSpan = 0;

        // Class-skin resolution (the boleto micro-framework): the metric grid
        // honours class typography, geometry and per-side borders on rows and
        // cells. A declared family only sticks when it RESOLVES — 'arial narrow'
        // falls back to the flow face exactly like the junk-family idiom.
        mt.mps.hiddenDepth = 0;
        mt.mps.hiddenTag = null;
    }

    /// <summary>The table's font size, faces, borders, collapse box and grid defaults from its CSS rules.</summary>
    private static void InitMetricTableStyle(MetricTableState mt)
    {
        mt.mps = new MetricParseState();
        mt.mps.ptFormCells = mt.ptFormCells;
        mt.mps.fontSize = mt.baseFontSize;
        // (the sheet lays the cells' paragraphs inline - `table td p { display: inline }` - and the
        // `p` rule's line-height then paces the cell lines: probed on the state analysis, 10 pt
        // cells pitch 15 = 1.5 em under `p { line-height: 1.5em }`)
        if (mt.stdSerif)
            foreach (var pKey in new[] { "table td p", "td p", "table th p", "th p", "p" })
                if (mt.css.TryGetValue(pKey, out var pInl) && pInl.TryGetValue("display", out var pDisp)
                    && pDisp.Trim().Equals("inline", StringComparison.OrdinalIgnoreCase))
                {
                    mt.mps.pInlineCells = true;
                    if (mt.css.TryGetValue("p", out var pRule) && pRule.TryGetValue("line-height", out var pLh)
                        && Regex.Match(pLh.Trim(), @"^([\d.]+)\s*em$", RegexOptions.IgnoreCase) is { Success: true } pLhM)
                        mt.mps.pInlineLineHeightEm = double.Parse(pLhM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                }
        mt.mps.tableClassFont = false;
        mt.mps.widthClassTable = false;
        // MEASURED (quirks table font reset): in a quirks document the table element does not inherit
        // its size - it resets to the UA 12 pt, a nested table again - so a table/td rule's em or
        // percent resolves against that base (the evaluation form's `TD { font-size: .75em }` is 9
        // whatever the body's 11) and a rule-less quirks cell is 12.
        if (mt.stdSerif && _quirksRowStrut) mt.mps.fontSize = UaDefaultFontPt;
        if (QuirksResetRuleFontPt(mt, "table") is { } qtfs) mt.mps.fontSize = qtfs;
        else if (QuirksResetRuleFontPt(mt, "td") is { } qdfs) mt.mps.fontSize = qdfs;
        else if (TryGetCssLength(mt.css, "table", "font-size") is { } tfs) mt.mps.fontSize = tfs;
        else if (TryGetCssLength(mt.css, "td", "font-size") is { } dfs) mt.mps.fontSize = dfs;
        // (…or a `table td { font-size }` descendant rule, which reaches every cell the same way)
        else if (mt.stdSerif && ElementRule(mt.css, "td") is { } tdElemRule && tdElemRule.TryGetValue("font-size", out var tdElemFs)
            && TryParseCssFontSize(tdElemFs.Trim()) is { } tdElemPt) mt.mps.fontSize = tdElemPt;

        mt.tableRuleFace = false;
        ApplySerifTableRuleTypography(mt);
        mt.tableFills = mt.css.TryGetValue("table", out var tblWr)
            && tblWr.TryGetValue("width", out var tblWv) && tblWv.Trim() == "100%";
        mt.mps.layoutFixed = false;
        mt.mps.borderHugs = false;
        mt.mps.centerTable = false;
        mt.mps.collapsedGrid = false;
        mt.mps.collapsedCol = Color.FromArgb(193, 193, 193);
        mt.mps.collapsedLineH = 0.0;
        mt.mps.attrCollapse = false;
        mt.mps.wtInlineGrid = false;
        mt.mps.inlineStatementGrid = false;
        mt.mps.wtPadV = -1;
        mt.mps.wtBw = 0;
        mt.mps.wtPMarginB = 0;
        mt.mps.wtPadH = -1;
        mt.mps.wtPadB = 0;
        mt.mps.wtPMarginDefaulted = false;
        mt.mps.wtBwBottom = 0;

        mt.s = 1.5;
        mt.p = 0.75;
        mt.indent = 0;
        mt.tablePct = 0;
        mt.tableWpt = 0;
        mt.mps.tableHeightPt = 0;
        mt.mps.tableStyleHPt = 0;
        mt.mps.tableStyleBg = null;
        if (mt.collapseBoxW > 0) mt.s = 0;                  // collapse zeroes the spacing
        // (…and so does a sheet's `table { border-spacing: 0 }`: such a sheet authors its cell boxes -
        //  the change-control page's th/td paddings and rules - which the grid then reads whole)
        if (mt.stdSerif && mt.css.TryGetValue("table", out var bsRule) && bsRule.TryGetValue("border-spacing", out var bsV)
            && IsZeroLength(bsV.Trim()))
        { mt.s = 0; mt.mps.sheetSpacingZero = true; }
        mt.elemCollapseGrid = false;
        InitMetricInlineFrame(mt);
    }

    /// <summary>The table tag's own inline `border-style` (solid / double / dashed, not
    /// none or hidden, and not a collapsed grid) frames the grid: the declared
    /// border-width, else 1 pt (probed: `border-style: solid` alone strokes 1 pt), in
    /// the declared border-color, its corners rounded by `border-radius` (10px -> 7.5).
    /// The cells sit inside the frame by its width (probed: "Cell1" at 99.25 = 96 + 1
    /// + the 1.5 spacing + the 0.75 padding).</summary>
    private static void InitMetricInlineFrame(MetricTableState mt)
    {
        mt.frameW = 0;
        mt.frameRadius = 0;
        if (mt.collapseBoxW > 0) return;
        var tag = Regex.Match(mt.tableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!tag.Success) return;
        // (the attribute may stand unquoted: `style=border-style:Solid;border-collapse:collapse`)
        var sm = Regex.Match(tag.Value, @"\bstyle\s*=\s*(?:[""'](?<s>[^""']*)[""']|(?<s>[^\s>""']+))", RegexOptions.IgnoreCase);
        if (!sm.Success) return;
        var decl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match d in StyleDeclRx.Matches(sm.Groups["s"].Value))
            decl[d.Groups[1].Value.Trim()] = d.Groups[2].Value.Trim();
        // (the `border: 1px solid rgb(204, 204, 204)` shorthand states the same three longhands)
        if (!decl.ContainsKey("border-style") && decl.TryGetValue("border", out var shorthand))
            ExpandBorderShorthand(shorthand, decl);
        if (!decl.TryGetValue("border-style", out var bs) || Regex.IsMatch(bs, "none|hidden", RegexOptions.IgnoreCase)) return;
        // (a collapsed inline frame is the collapse box: one outer rule, no spacing - measured on the
        // enterprise summary's `cellspacing=5 ... border-style:Solid; border-collapse:collapse` grids,
        // framed 0.75 with their rows 10.5 apart)
        if (decl.TryGetValue("border-collapse", out var bc) && bc.Contains("collapse", StringComparison.OrdinalIgnoreCase))
        {
            if (!mt.stdSerif) return;
            mt.collapseBoxW = decl.TryGetValue("border-width", out var cbw) && TryParseLength(cbw) is { } cbwPt && cbwPt > 0 ? cbwPt : 0.75;
            if (decl.TryGetValue("border-color", out var cbc) && ParseCssColor(cbc) is { } cbcV) mt.mps.borderColor = cbcV;
            mt.s = 0;
            return;
        }
        mt.frameW = decl.TryGetValue("border-width", out var bw) && TryParseLength(bw) is { } bwPt && bwPt > 0 ? bwPt : 1.0;
        if (decl.TryGetValue("border-color", out var bcol) && ParseCssColor(bcol) is { } fc) mt.frameColor = fc;
        if (decl.TryGetValue("border-radius", out var br) && TryParseLength(br) is { } brPt && brPt > 0) mt.frameRadius = brPt;
    }

    /// <summary>A rectangle path with rounded corners, as the four-arc stroke the frame
    /// takes (the caller passes the stroke's mid-line radius: the declared radius less
    /// half the width, probed 7.5 -> 7 at 1 pt); a zero radius is the plain rectangle.</summary>
    private static string RoundedRectPath(double x, double y, double w, double h, double r, System.Globalization.CultureInfo invc)
    {
        if (r <= 0) return Compat.Format(invc, $"{x:F2} {y:F2} {w:F2} {h:F2} re");
        r = Math.Min(r, Math.Min(w, h) / 2);
        var k = r * ArcKappa;
        return Compat.Format(invc,
            $"{x + r:F2} {y:F2} m {x + w - r:F2} {y:F2} l " +
            $"{x + w - r + k:F2} {y:F2} {x + w:F2} {y + r - k:F2} {x + w:F2} {y + r:F2} c " +
            $"{x + w:F2} {y + h - r:F2} l {x + w:F2} {y + h - r + k:F2} {x + w - r + k:F2} {y + h:F2} {x + w - r:F2} {y + h:F2} c " +
            $"{x + r:F2} {y + h:F2} l {x + r - k:F2} {y + h:F2} {x:F2} {y + h - r + k:F2} {x:F2} {y + h - r:F2} c " +
            $"{x:F2} {y + r:F2} l {x:F2} {y + r - k:F2} {x + r - k:F2} {y:F2} {x + r:F2} {y:F2} c h");
    }

    /// <summary>The Bezier control distance of a quarter arc, as a share of the radius.</summary>
    private const double ArcKappa = 0.5523;

    /// <summary>A wrapper table's presentational chrome: cellspacing and cellpadding in pt, the 1 px bevel of a border attribute, and its bgcolor.</summary>
    private static (double s, double p, double bw, Color? bg) WrapperAttrChrome(string wAttrs)
    {
        double wS = 1.5, wP = 0.75, wBw = 0;
        Color? wBg = null;
        var wcs = Regex.Match(wAttrs, @"cellspacing\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
        if (wcs.Success && PresentationalLengthPt(wcs.Groups[1].Value) is { } wcsPt) wS = wcsPt;
        var wcp = Regex.Match(wAttrs, @"cellpadding\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
        if (wcp.Success && PresentationalLengthPt(wcp.Groups[1].Value) is { } wcpPt) wP = wcpPt;
        var wbm = Regex.Match(wAttrs, @"\bborder\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (wbm.Success && double.TryParse(wbm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wbv) && wbv > 0)
            wBw = 0.75;
        var wbgm = Regex.Match(wAttrs, @"bgcolor\s*=\s*[""']?([#0-9a-zA-Z]+)", RegexOptions.IgnoreCase);
        if (wbgm.Success)
            wBg = ParseCssColor(wbgm.Groups[1].Value.StartsWith('#') ? wbgm.Groups[1].Value : "#" + wbgm.Groups[1].Value);
        return (wS, wP, wBw, wBg);
    }

    /// <summary>A wrapper-stacks document renders its table through the stacked-wrapper path; false when that path drew it.</summary>
    private static bool TryRenderStackedWrapper(bool wrapperStacks, Document doc, FlowPosition cursor, string tableHtml, IReadOnlyDictionary<string, Dictionary<string, string>> css, string face, Core.PdfDictionary docFontDict, HtmlLoadOptions? loadOptions, bool serifReportCells, double symInsetPt, double marginLeft, double contentWidth, double pageWidth, double pageHeight, double marginTop, double marginBottom, (double asc, double sum) fm, bool stdSerif, double baseFontSize, bool paragraphCells, List<CssSiblingCellRule>? siblingCellRules, bool uaBlockCells = false, bool uaFormCells = false, (double x, double y)? pageMargin = null, string[]? hostBlockClasses = null, bool ptFormCells = false)
    {
        if (!wrapperStacks
            || TrySplitWrapperStack(tableHtml) is not (var wAttrs, var wChildren)) return true;

        var (wS, wP, wBw, wBg) = WrapperAttrChrome(wAttrs);
        var wCentred = WrapperCellCentred(tableHtml);

        var wInset = 2 * wBw + wS + wP;
        var wPage0 = cursor.page;
        var wStreamMark = cursor.page.ContentStreamCount;
        var wX0 = marginLeft;
        var wTopTd = pageHeight - cursor.y;
        // CSS borders on the wrapper and its cell are chrome of their own: the children
        // stand inside them, and each box strokes its declared sides afterwards.
        var wChrome = WrapperCssChrome(tableHtml, wAttrs);
        var wChromeL = wChrome.table[3].W + wChrome.cell[3].W;
        var wChromeR = wChrome.table[1].W + wChrome.cell[1].W;
        cursor.y -= wInset + wChrome.table[0].W + wChrome.cell[0].W;
        var wAvail = contentWidth - symInsetPt;
        // (a wrapper declaring its own px width is that box, not its host's: its children lay
        // out inside it, and a wider child overflows it at its left edge, uncentred - measured
        // on the enterprise summary: the 650 px wrapper's 650 px child seats 97.5)
        var wDeclared = DeclaredTableWidthPt(wAttrs);
        if (stdSerif && wDeclared > 0 && wDeclared < wAvail) wAvail = wDeclared;
        var wRight = wX0 + wAvail;
        double wKidX0 = double.NaN, wKidX1 = double.NaN;
        var wFirst = true;
        var wPrevRendered = false;
        var wPrevBordered = false;
        foreach (var (childHtml, childNewCell) in wChildren)
        {
            // Each wrapper ROW pads its cell (bottom + top cellpadding);
            // SAME-CELL siblings sit the measured 1.2 pt apart — and an
            // EMPTY table (no cells) is fully transparent: no gap of its
            // own, and its neighbours share a single gap across it.
            var childRenders = Regex.IsMatch(childHtml, @"<td\b", RegexOptions.IgnoreCase);
            var childBordered = Regex.IsMatch(childHtml,
                @"^\s*<table\b[^>]*\bborder\s*=\s*[""']?[1-9]", RegexOptions.IgnoreCase);
            if (!wFirst && childRenders && wPrevRendered)
                cursor.y -= childNewCell ? 2 * wP
                    : childBordered || wPrevBordered ? WrapperSiblingGapPt : 0;
            wFirst = false;
            if (childRenders) { wPrevRendered = true; wPrevBordered = childBordered; }
            cursor.lastTableX0 = double.NaN;
            var wChildAvail = wAvail - 2 * wInset - wChromeL - wChromeR;
            var wChildInset = WrapperChildInset(childHtml, wCentred, wChildAvail);
            var wChildW = wChildAvail - wChildInset;
            // (a child centring ITSELF - `align=center` on its own tag - stands centred at the
            //  wider of its declared box and its content's intrinsic width: probed on the
            //  e-mail cards, the 755 px wrapper grows to its 767 px card and centres in the
            //  775 px body)
            if (stdSerif && wChildInset == 0 && CentredSubTableBox(childHtml, css, 0, wChildAvail) is var (selfInset, selfW) && selfInset > 0)
            { wChildInset = selfInset; wChildW = selfW; }
            RenderMetricTable(doc, cursor, childHtml, css,
                wX0 + wInset + wChromeL + wChildInset, wChildW, pageWidth, pageHeight,
                marginTop, marginBottom, face, fm, docFontDict,
                stdSerif, baseFontSize, wrapperStacks: true, symInsetPt: 0,
                paragraphCells: paragraphCells, serifReportCells: serifReportCells,
                loadOptions: loadOptions, siblingCellRules: siblingCellRules, uaBlockCells: uaBlockCells,
                uaFormCells: uaFormCells, pageMargin: pageMargin, hostCellPadPt: wP, hostBlockClasses: hostBlockClasses, ptFormCells: ptFormCells);
            if (!double.IsNaN(cursor.lastTableX0))
            {
                wKidX0 = double.IsNaN(wKidX0) ? cursor.lastTableX0 : Math.Min(wKidX0, cursor.lastTableX0);
                wKidX1 = double.IsNaN(wKidX1) ? cursor.lastTableX1 : Math.Max(wKidX1, cursor.lastTableX1);
            }
        }
        cursor.y -= wInset + wChrome.table[2].W + wChrome.cell[2].W;
        var wBotTd = pageHeight - cursor.y;
        if (!double.IsNaN(wKidX0) && ReferenceEquals(cursor.page, wPage0))
        {
            PaintWrapperCssFrames(cursor.page, wChrome, wKidX0, wKidX1, pageHeight - wTopTd, cursor.y,
                System.Globalization.CultureInfo.InvariantCulture);
            cursor.lastTableX0 = wKidX0 - wChromeL;
            cursor.lastTableX1 = wKidX1 + wChromeR;
        }
        if (wBw > 0) PaintWrapperOutsetFrame(cursor.page, wX0, wRight, wTopTd, wBotTd, pageHeight);
        PaintWrapperBand(wPage0, wStreamMark, wBg, wX0, wRight, wTopTd, wBotTd, pageHeight,
            ReferenceEquals(cursor.page, wPage0));
        return false;
    }

    /// <summary>The wrapper's own two-ply frame: an OUTSET outer frame (#555 top+left, black
    /// bottom+right) and, one border width inside it, the inset frame with those sides
    /// swapped.</summary>
    private static void PaintWrapperOutsetFrame(Page page, double wX0, double wRight,
        double wTopTd, double wBotTd, double pageHeight)
    {
        var wInv = System.Globalization.CultureInfo.InvariantCulture;
        var dark = "0 0 0 RG";
        var gray = "0.333 0.333 0.333 RG";
        var wsb = new StringBuilder("q 0.75 w ");
        void WLine(string col, double lx0, double ly0d, double lx1, double ly1d)
            => wsb.Append(Compat.Format(wInv,
                $"{col} {lx0:F2} {pageHeight - ly0d:F2} m {lx1:F2} {pageHeight - ly1d:F2} l S "));
        // outset frame: #555 top+left, black bottom+right
        WLine(gray, wX0, wTopTd + 0.375, wRight, wTopTd + 0.375);
        WLine(gray, wX0 + 0.375, wTopTd, wX0 + 0.375, wBotTd);
        WLine(dark, wX0, wBotTd - 0.375, wRight, wBotTd - 0.375);
        WLine(dark, wRight - 0.375, wTopTd, wRight - 0.375, wBotTd);
        // inset frame, one border width inside: black top+left, #555 bottom+right
        WLine(dark, wX0 + 0.75, wTopTd + 1.125, wRight - 0.75, wTopTd + 1.125);
        WLine(dark, wX0 + 1.125, wTopTd + 0.75, wX0 + 1.125, wBotTd - 0.75);
        WLine(gray, wX0 + 0.75, wBotTd - 1.125, wRight - 0.75, wBotTd - 1.125);
        WLine(gray, wRight - 1.125, wTopTd + 0.75, wRight - 1.125, wBotTd - 0.75);
        wsb.Append("Q\n");
        page.AddContentStream(Encoding.ASCII.GetBytes(wsb.ToString()));
    }

    /// <summary>The wrapper's bgcolor paints the whole band BENEATH its children: the fill is
    /// inserted at the stream position the wrapper opened at, so it underlays everything the
    /// children appended after it.</summary>
    private static void PaintWrapperBand(Page wPage0, int wStreamMark, Color? wBg,
        double wX0, double wRight, double wTopTd, double wBotTd, double pageHeight,
        bool samePage)
    {
        if (wBg is not { } wBand || !samePage) return;
        var wbInv = System.Globalization.CultureInfo.InvariantCulture;
        wPage0.InsertContentStreamAt(wStreamMark, Encoding.ASCII.GetBytes(Compat.Format(wbInv,
            $"q {wBand.R / 255.0:0.###} {wBand.G / 255.0:0.###} {wBand.B / 255.0:0.###} rg " +
            $"{wX0:F2} {pageHeight - wBotTd:F2} {wRight - wX0:F2} {wBotTd - wTopTd:F2} re f Q\n")));
    }

    /// <summary>Routes one markup token: text into the current segment, hidden elements skipped, tags to their open or close arm.</summary>
    private static bool ParseMetricToken(MetricTableState mt, Token tok)
    {
        if (tok.Kind == TokenKind.Text) { CollectMetricText(mt, tok); return true; }
        var tag = tok.Tag!.ToLowerInvariant();
        // inside a captured absolute div only its text and its own div nesting count
        if (mt.mps.absCapture is not null && tag != "div") return true;
        // display:none subtree (a hidden pager <select>, a state-carrier <input>):
        // none of its content reaches the cell text.
        if (SkipHiddenMarkup(mt, tok, tag)) return true;
        // The inline CONTAINERS open around the cell's blocks: a span-wrapped <p> is not the cell's
        // direct child (measured: it keeps its margins), while a bare formatting tag (b/i/u) around
        // the paragraph leaves it the cell's own paragraph (measured: no margins in quirks mode).
        if (tag is "span" or "font" or "a")
            mt.mps.inlineWrapDepth = Math.Max(0, mt.mps.inlineWrapDepth + (tok.IsClose ? -1 : 1));
        if (tok.IsClose) { CloseMetricTag(mt, tag); return true; }
        OpenMetricTokenTag(mt, tok, tag);
        return true;
    }

    /// <summary>Tokens inside a hidden element are skipped until its close; true when this token was one.</summary>
    private static bool SkipHiddenMarkup(MetricTableState mt, Token tok, string tag)
    {
        if (mt.mps.hiddenDepth > 0)
        {
            if (tag == mt.mps.hiddenTag)
            {
                if (tok.IsClose) { if (--mt.mps.hiddenDepth == 0) mt.mps.hiddenTag = null; }
                else if (!tok.IsSelfClosing) mt.mps.hiddenDepth++;
            }
            return true;
        }
        if (!tok.IsClose && IsHiddenElement(tag, tok.Attributes, mt.css))
        {
            if (!tok.IsSelfClosing && !VoidTags.Contains(tag))
            {
                mt.mps.hiddenTag = tag;
                mt.mps.hiddenDepth = 1;
            }
            return true;
        }
        return false;
    }
}
