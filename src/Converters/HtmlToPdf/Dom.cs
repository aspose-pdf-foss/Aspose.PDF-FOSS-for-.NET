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

    private static void RenderMetricTable(Document doc, ref Page page, ref double y,
        string tableHtml, IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double marginLeft, double contentWidth, double pageWidth, double pageHeight,
        double marginTop, double marginBottom, string face, (double asc, double sum) fm,
        Core.PdfDictionary docFontDict, bool stdSerif = false, double baseFontSize = 11,
        bool wrapperStacks = false, double symInsetPt = UaBodyMarginPt, bool rtl = false,
        bool paragraphCells = false, bool serifReportCells = false,
        HtmlLoadOptions? loadOptions = null)
    {

        // Wrapper stacks (legacy nested-table markup): a table whose every row is
        // a single td holding only tables contributes CHROME, not a grid — its
        // children stack inside insets of (2 x border) + cellspacing + cellpadding,
        // and a border=1 wrapper draws the browser's two beveled 1px frames
        // (outset: #555 top+left over black bottom+right; inset the reverse)
        // around the stacked extent. Measured: margin 96 -> 96.75
        // (plain wrapper, p=1px) -> 98.25+0.75 = 99 through a bordered one.
        if (!TryRenderStackedWrapper(wrapperStacks, doc, ref page, ref y, tableHtml, css, face, docFontDict, loadOptions, serifReportCells, symInsetPt, marginLeft, contentWidth, pageWidth, pageHeight, marginTop, marginBottom, fm, stdSerif, baseFontSize, paragraphCells)) return;

        var mt = new MetricTableState();
        mt.doc = doc;
        mt.tableHtml = tableHtml;
        mt.css = css;
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
        InitMetricTableStyle(mt);
        InitMetricRowState(mt);
        ParseMetricTokens(mt);
        if (!SolveMetricTable(mt, ref page, ref y)) return;
        PaintMetricTable(mt, ref page, ref y);
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
    private static void PaintMetricTable(MetricTableState mt, ref Page page, ref double y)
    {
        mt.cbFrameTopY = y;
        mt.cbFramePage = page;
        if (mt.collapseBoxW > 0) y -= mt.collapseBoxW;
        mt.rowSpanExtra = new double[mt.rows.Count];
        if (mt.stdSerif)
            for (var ri0 = 0; ri0 < mt.rows.Count; ri0++)
                foreach (var mcSpan in mt.rows[ri0])
                    if (mcSpan.RowSpan > 1 && mcSpan.ContentH > 0)
                    {
                        var kSpan = Math.Min(mcSpan.RowSpan, mt.rows.Count - ri0);
                        if (kSpan <= 0) continue;
                        var have = (kSpan - 1) * (mt.s + 2 * mt.p);
                        for (var rj = ri0; rj < ri0 + kSpan; rj++)
                        {
                            var rjH = mt.tableHasText ? mt.lineH : 0;
                            foreach (var mc2 in mt.rows[rj])
                                if (mc2.RowSpan <= 1) rjH = Math.Max(rjH, mc2.ContentH);
                            have += rjH;
                        }
                        if (mcSpan.ContentH > have)
                        {
                            var addEach = (mcSpan.ContentH - have) / kSpan;
                            for (var rj = ri0; rj < ri0 + kSpan; rj++)
                                mt.rowSpanExtra[rj] = Math.Max(mt.rowSpanExtra[rj], addEach);
                        }
                    }
        RenderMetricRows(mt.mps, mt.rows, mt.colW, mt.nCols, mt.availW, mt.s, mt.lineH, mt.face, mt.boldFace, mt.hheaSum, mt.fm, mt.p, mt.pageWidth, mt.pageHeight, mt.marginTop, mt.marginBottom, mt.tableWpt, mt.tablePct, mt.baseFontSize, mt.paragraphCells, mt.tableHtml, mt.symInsetPt, mt.css, mt.doc, mt.docFontDict, mt.loadOptions, mt.invc, mt.flatRes, mt.reportCells, mt.serifReportCells, mt.stdSerif, mt.wrapperStacks, mt.collapseBoxW, mt.rowSpanExtra, mt.tableHasText, mt.tableRuleFace, mt.tableX, mt.marginLeft, mt.contentWidth, ref page, ref y);
        y -= mt.s;   // trailing cellspacing closes the table box
        // the sheet's table margin-bottom paces stacked grids (measured: 20px
        // between the collapse-grid boxes, measured pitch 55.875)
        if (mt.elemCollapseGrid && mt.css.TryGetValue("table", out var mbRule)
            && mbRule.TryGetValue("margin-bottom", out var mbV)
            && TryParseLength(mbV.Trim(), out var mbPt) && mbPt > 0)
            y -= mbPt;
        // Outer-frame collapse grid: close the box below the last row and stroke
        // the frame around the whole table (stroke centred half a width inside).
        if (mt.collapseBoxW > 0)
        {
            y -= mt.collapseBoxW;
            if (ReferenceEquals(mt.cbFramePage, page))
            {
                double cbBoxW2 = 2 * mt.collapseBoxW + (mt.nCols + 1) * mt.s;
                foreach (var w in mt.colW) cbBoxW2 += w + 2 * mt.p;
                var cbHalf = mt.collapseBoxW / 2;
                page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(mt.invc,
                    $"q {mt.mps.borderColor.R / 255.0:0.###} {mt.mps.borderColor.G / 255.0:0.###} {mt.mps.borderColor.B / 255.0:0.###} RG " +
                    $"{mt.collapseBoxW:0.##} w {mt.tableX + cbHalf:F2} {y + cbHalf:F2} " +
                    $"{cbBoxW2 - mt.collapseBoxW:F2} {mt.cbFrameTopY - y - mt.collapseBoxW:F2} re S Q\n")));
            }
        }

        // Paint the deferred band UNDER everything the rows drew, over the box's
        // REAL extent (sub-grids included). Same-page tables only — a paginated
        // band keeps whatever its rows drew.
        if (mt.tableBgUnderlay && mt.mps.tableBg is { } tbgcU
            && ReferenceEquals(mt.tableBgPage, page) && mt.tableBgStartY > y)
        {
            var bandW = (mt.nCols + 1) * mt.s;
            foreach (var w in mt.colW) bandW += w + 2 * mt.p;
            mt.tableBgPage.InsertContentStreamAt(mt.tableBgStartIdx,
                Encoding.ASCII.GetBytes(string.Create(mt.invc,
                    $"q {tbgcU.R / 255.0:0.###} {tbgcU.G / 255.0:0.###} {tbgcU.B / 255.0:0.###} rg " +
                    $"{mt.tableX:F2} {y:F2} {bandW:F2} {mt.tableBgStartY - y:F2} re f Q\n")));
        }
    }

    /// <summary>Closes the last row, solves the column widths and reserves the table's background; false when the table has no rows.</summary>
    private static bool SolveMetricTable(MetricTableState mt, ref Page page, ref double y)
    {
        CloseRow(mt.mps, mt.rows, mt.text, mt.reportCells, mt.stdSerif);
        if (mt.rows.Count == 0) return false;
        (mt.nCols, mt.colW, mt.availW, mt.tableX, mt.hheaSum, mt.invc) = SolveMetricColumns(mt.mps, ref mt.rows, mt.text, mt.css, mt.face, mt.boldFace, mt.fmSum, mt.lineH, mt.s, ref mt.p, mt.bw, mt.symInsetPt, mt.tableFills, mt.reportCells, mt.stdSerif, mt.wrapperStacks, mt.paragraphCells, mt.serifReportCells, mt.collapseBoxW, mt.elemCollapseGrid, mt.tableRuleFace, mt.indent, mt.tablePct, mt.tableWpt, mt.baseFontSize, mt.marginLeft, mt.marginTop, mt.marginBottom, mt.pageWidth, mt.pageHeight, mt.contentWidth, mt.rtl, mt.loadOptions, mt.tableHtml, mt.fm);

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
            && y < mt.pageHeight - mt.marginTop - 1e-6)
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
            if (y - tableH < mt.marginBottom)
            {
                page = mt.doc.Pages.Add(mt.pageWidth, mt.pageHeight);
                EnsureFonts(page, mt.docFontDict);
                y = mt.pageHeight - mt.marginTop;
            }
        }

        // Bordered draw: outer border box, per-cell border boxes on the 2px
        // border-spacing grid, text at border+padding insets. Cell box heights =
        // content + padding + borders; strokes centred half a width inside.
        if (mt.mps.bordered) { RenderBorderedGrid(mt.mps, mt.rows, mt.colW, mt.nCols, mt.availW, mt.s, mt.bw, mt.lineH, mt.face, mt.boldFace, mt.hheaSum, mt.fm, mt.p, mt.pageWidth, mt.pageHeight, mt.marginTop, mt.marginBottom, mt.tableWpt, mt.tablePct, mt.baseFontSize, mt.paragraphCells, mt.tableHtml, mt.symInsetPt, mt.tableFills, mt.css, mt.doc, mt.docFontDict, mt.loadOptions, mt.invc, mt.stdSerif, mt.wrapperStacks, mt.rmtAnchorColor, mt.marginLeft, mt.contentWidth, ref mt.tableX, ref page, ref y); return false; }

        mt.flatRes = new Dictionary<string, string>(StringComparer.Ordinal);
        mt.tableBgUnderlay = mt.mps.tableBg is not null && !mt.stdSerif && mt.wrapperStacks;
        mt.tableBgPage = page;
        mt.tableBgStartIdx = mt.tableBgUnderlay ? page.ContentStreamCount : 0;
        mt.tableBgStartY = y;
        if (mt.mps.tableBg is { } tbgc0 && !mt.tableBgUnderlay)
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
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(mt.invc,
                $"q {tbgc0.R / 255.0:0.###} {tbgc0.G / 255.0:0.###} {tbgc0.B / 255.0:0.###} rg " +
                $"{mt.tableX:F2} {y - bandH:F2} {bandW:F2} {bandH:F2} re f Q\n")));
        }
        return true;
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
        if (mt.stdSerif && mt.collapseBoxW == 0
            && mt.css.TryGetValue("td", out var egTd)
            && egTd.TryGetValue("border-collapse", out var egBc)
            && egBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)
            && egTd.TryGetValue("border", out var egB)
            && egB.Contains("solid", StringComparison.OrdinalIgnoreCase))
        {
            mt.mps.collapsedGrid = true;
            mt.elemCollapseGrid = true;
            mt.s = 0;
            if (ParseCssColor(egB) is { } egCol) mt.mps.collapsedCol = egCol;
        }
        // pt-report sheets (non-serif wrapper mode): the TABLE rule's
        // border-collapse zeroes the spacing and its padding: 0 the cell
        // padding — cell/table attributes still win below.
        if (!mt.stdSerif && mt.wrapperStacks && mt.css.TryGetValue("table", out var ptTblRule))
        {
            if (ptTblRule.TryGetValue("border-collapse", out var ptBc)
                && ptBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)) mt.s = 0;
            if (ptTblRule.TryGetValue("padding", out var ptPad)
                && Regex.IsMatch(ptPad.Trim(), @"^0(px)?$")) mt.p = 0;
        }
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
            mt.tableHtml = ExtractNestedTables(mt.tableHtml, out mt.mps.nestedTables);
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
        mt.mps.fontSize = mt.baseFontSize;
        mt.mps.tableClassFont = false;
        mt.mps.widthClassTable = false;
        if (TryGetCssLength(mt.css, "table", "font-size", out var tfs)) mt.mps.fontSize = tfs;
        else if (TryGetCssLength(mt.css, "td", "font-size", out var dfs)) mt.mps.fontSize = dfs;

        mt.tableRuleFace = false;
        if (mt.stdSerif && mt.css.TryGetValue("table", out var tblFontRule)
            && tblFontRule.TryGetValue("font", out var tblFontV))
        {
            var tfsh = Regex.Match(tblFontV, @"([\d.]+)\s*(pt|px)\s+(.+)$", RegexOptions.IgnoreCase);
            if (tfsh.Success && double.TryParse(tfsh.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var tfshV) && tfshV > 0)
            {
                mt.mps.fontSize = tfsh.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                    ? tfshV * 0.75 : tfshV;
                if (FirstFontFamily(tfsh.Groups[3].Value) is { Length: > 0 } tfshFam
                    && WinMetricsFor(tfshFam) is { } tfshFm)
                { mt.face = tfshFam; mt.fm = tfshFm; mt.tableRuleFace = true; }
            }
        }

        mt.fmSum = mt.fm.sum <= 1.0 ? 1.2 : mt.fm.sum;
        mt.lineH = MetricLineHeight(mt.mps.fontSize, mt.fmSum);
        mt.boldFace = mt.face + "-Bold";

        mt.mps.bordered = false;
        mt.mps.borderColor = Color.FromArgb(0, 0, 0);
        mt.bw = 0.75;
        if (mt.stdSerif && mt.css.TryGetValue("table", out var tblRule)
            && tblRule.TryGetValue("border", out var tblBv)
            && tblBv.Contains("solid", StringComparison.OrdinalIgnoreCase)
            && !(tblRule.TryGetValue("border-collapse", out var tblBc)
                 && tblBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)))
        {
            mt.mps.bordered = true;
            if (ParseCssColor(tblBv) is { } tblBcol) mt.mps.borderColor = tblBcol;
        }
        mt.collapseBoxW = 0.0;
        if (mt.stdSerif
            && mt.css.TryGetValue("table", out var cbRule)
            && cbRule.TryGetValue("border-collapse", out var cbC)
            && cbC.Contains("collapse", StringComparison.OrdinalIgnoreCase)
            && cbRule.TryGetValue("border-style", out var cbS)
            && cbS.Contains("solid", StringComparison.OrdinalIgnoreCase)
            && !(mt.css.TryGetValue("td", out var cbTd)
                 && (cbTd.ContainsKey("border") || cbTd.ContainsKey("border-style"))))
        {
            mt.collapseBoxW = cbRule.TryGetValue("border-width", out var cbW)
                && TryParseLength(cbW.Trim(), out var cbWPt) && cbWPt > 0 ? cbWPt : 0.75;
            if (cbRule.TryGetValue("border-color", out var cbCol)
                && ParseCssColor(cbCol) is { } cbColV) mt.mps.borderColor = cbColV;
        }
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
        mt.elemCollapseGrid = false;
    }

    /// <summary>A wrapper-stacks document renders its table through the stacked-wrapper path; false when that path drew it.</summary>
    private static bool TryRenderStackedWrapper(bool wrapperStacks, Document doc, ref Page page, ref double y, string tableHtml, IReadOnlyDictionary<string, Dictionary<string, string>> css, string face, Core.PdfDictionary docFontDict, HtmlLoadOptions? loadOptions, bool serifReportCells, double symInsetPt, double marginLeft, double contentWidth, double pageWidth, double pageHeight, double marginTop, double marginBottom, (double asc, double sum) fm, bool stdSerif, double baseFontSize, bool paragraphCells)
    {
        if (wrapperStacks
            && TrySplitWrapperStack(tableHtml, out var wAttrs, out var wChildren))
        {
            double wS = 1.5, wP = 0.75, wBw = 0;
            Color? wBg = null;
            var wcs = Regex.Match(wAttrs, @"cellspacing\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (wcs.Success) wS = double.Parse(wcs.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * PxPt;
            var wcp = Regex.Match(wAttrs, @"cellpadding\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (wcp.Success) wP = double.Parse(wcp.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * PxPt;
            var wbm = Regex.Match(wAttrs, @"\bborder\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (wbm.Success && double.TryParse(wbm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wbv) && wbv > 0)
                wBw = 0.75;
            var wbgm = Regex.Match(wAttrs, @"bgcolor\s*=\s*[""']?([#0-9a-zA-Z]+)", RegexOptions.IgnoreCase);
            if (wbgm.Success)
                wBg = ParseCssColor(wbgm.Groups[1].Value.StartsWith('#')
                    ? wbgm.Groups[1].Value : "#" + wbgm.Groups[1].Value);

            var wInset = 2 * wBw + wS + wP;
            var wPage0 = page;
            var wStreamMark = page.ContentStreamCount;
            var wX0 = marginLeft;
            var wTopTd = pageHeight - y;
            y -= wInset;
            var wAvail = contentWidth - symInsetPt;
            var wRight = wX0 + wAvail;
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
                    y -= childNewCell ? 2 * wP
                        : childBordered || wPrevBordered ? WrapperSiblingGapPt : 0;
                wFirst = false;
                if (childRenders) { wPrevRendered = true; wPrevBordered = childBordered; }
                RenderMetricTable(doc, ref page, ref y, childHtml, css,
                    wX0 + wInset, wAvail - 2 * wInset, pageWidth, pageHeight,
                    marginTop, marginBottom, face, fm, docFontDict,
                    stdSerif, baseFontSize, wrapperStacks: true, symInsetPt: 0,
                    paragraphCells: paragraphCells, serifReportCells: serifReportCells,
                    loadOptions: loadOptions);
            }
            y -= wInset;
            var wBotTd = pageHeight - y;
            if (wBw > 0)
            {
                var wInv = System.Globalization.CultureInfo.InvariantCulture;
                var dark = "0 0 0 RG";
                var gray = "0.333 0.333 0.333 RG";
                var wsb = new StringBuilder("q 0.75 w ");
                void WLine(string col, double lx0, double ly0d, double lx1, double ly1d)
                    => wsb.Append(string.Create(wInv,
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
            // The wrapper's bgcolor paints the whole band BENEATH its children:
            // the fill is inserted at the stream position the wrapper opened at,
            // so it underlays everything the children appended after it.
            if (wBg is { } wBand && ReferenceEquals(page, wPage0))
            {
                var wbInv = System.Globalization.CultureInfo.InvariantCulture;
                wPage0.InsertContentStreamAt(wStreamMark, Encoding.ASCII.GetBytes(string.Create(wbInv,
                    $"q {wBand.R / 255.0:0.###} {wBand.G / 255.0:0.###} {wBand.B / 255.0:0.###} rg " +
                    $"{wX0:F2} {pageHeight - wBotTd:F2} {wRight - wX0:F2} {wBotTd - wTopTd:F2} re f Q\n")));
            }
            return false;
        }
        return true;
    }

    /// <summary>Routes one markup token: text into the current segment, hidden elements skipped, tags to their open or close arm.</summary>
    private static bool ParseMetricToken(MetricTableState mt, Token tok)
    {
        if (tok.Kind == TokenKind.Text) { CollectMetricText(mt, tok); return true; }
        var tag = tok.Tag!.ToLowerInvariant();
        // display:none subtree (a hidden pager <select>, a state-carrier <input>):
        // none of its content reaches the cell text.
        if (SkipHiddenMarkup(mt, tok, tag)) return true;
        if (tok.IsClose) { CloseMetricTag(mt, tag); return true; }
        switch (tag)
        {
            case "table" when mt.mps.sawTable:
                OpenMetricTable(mt);
                break;
            case "table" when !mt.mps.sawTable:
                HandleMetricTableOpen(mt.mps, tok, mt.text, mt.css, mt.stdSerif, mt.wrapperStacks, mt.paragraphCells, mt.serifReportCells, mt.reportCells, mt.baseFontSize, mt.rows, mt.face, mt.boldFace, mt.fm, ref mt.indent, mt.loadOptions, ref mt.p, mt.rtl, ref mt.s, mt.tableHtml, ref mt.tablePct, ref mt.tableWpt, tag);
                break;
            case "tr":
                OpenMetricRow(mt, tok);
                break;
            case "td":
            case "th":
                HandleMetricCellOpen(mt.mps, tok, tag, mt.text, mt.css, mt.stdSerif, mt.wrapperStacks, mt.paragraphCells, mt.serifReportCells, mt.reportCells, mt.baseFontSize, mt.rows, mt.face, mt.boldFace, mt.fm, mt.indent, mt.loadOptions, mt.p, mt.rtl, mt.s, mt.tableHtml, mt.tablePct, mt.tableWpt);
                break;
            case "b":
            case "strong":
                mt.mps.boldDepth++;
                if (mt.mps.cell is not null) mt.mps.cellBoldMarks.Add((mt.text.Length, true));
                // report cells account bold per RUN (boldDepth); the
                // whole-cell flag stays a legacy-flow behaviour
                if (mt.mps.cell is not null && !mt.reportCells) mt.mps.cell.Bold = true;
                break;
            case "hr":
                // An <hr> inside a cell is drawn content, not a block break:
                // the grid gives it a line box and strokes the rule in it.
                if (mt.mps.cell is not null) mt.mps.cell.HrRule = true;
                break;
            case "i":
            case "em":
                // …and <i>/<em> italicises it, the same whole-cell flag an
                // inline `font-style: italic` sets. Without this a cell's
                // emphasis is simply lost: the grid drew <b> and nothing else.
                if (mt.mps.cell is not null) mt.mps.cell.Italic = true;
                break;
            case "font":
                OpenMetricFont(mt, tok, tag);
                break;
            case "a":
                OpenMetricAnchor(mt, tok);
                break;
            case "span":
                OpenMetricSpan(mt, tok);
                break;
            case "div":
                OpenMetricDiv(mt, tok);
                break;
            case "img":
                HandleMetricImgOpen(mt.mps, tok, mt.text, mt.css, mt.stdSerif, mt.wrapperStacks, mt.reportCells, mt.rows, mt.face, mt.boldFace, mt.fm, mt.indent, mt.loadOptions, mt.p, mt.rtl, mt.s, mt.tableHtml, mt.tablePct, mt.tableWpt, tag);
                break;
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                OpenMetricHeading(mt, tag);
                break;
            case "p":
                HandleMetricParaOpen(mt.mps, tok, mt.text, mt.css, mt.stdSerif, mt.wrapperStacks, mt.reportCells, mt.rows, mt.face, mt.boldFace, mt.fm, mt.indent, mt.loadOptions, mt.p, mt.rtl, mt.s, mt.tableHtml, mt.tablePct, mt.tableWpt, tag);
                break;
            case "br":
                // a <br> inside a cell is a hard line break (the letter's
                // item list stacks its items with them)
                if (mt.mps.cell is not null)
                    (mt.mps.curSeg is not null ? mt.mps.divText : mt.text).Append('\u0001');
                break;
            case "thead":
            case "tbody":
            case "tfoot":
                if (mt.mps.nestDepth == 0)
                    mt.mps.curSection = tag == "thead" ? 0 : tag == "tfoot" ? 2 : 1;
                break;
        }
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
