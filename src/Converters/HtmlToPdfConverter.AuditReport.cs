using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The cloud infrastructure-assessment report. Every block in it is a
// `float: left` div whose width is a PERCENTAGE of the sheet, so the whole
// document hangs off one number - the content width between the sheet's 96 pt
// margins. `.auditReportSubSubHeadingImageDiv` pins it: its 3% width over a
// 30px padding puts the sub-heading at 157.46, which only resolves at a
// content width of 1048.59.
//
// The flow itself is ordinary: text blocks split line by line across sheets
// while tables and charts move whole. The one thing worth stating is that each
// section wrapper has to CLEAR its own icon float - the 32px arrow in its
// 15px/5px padded box is taller than the 19pt heading beside it, so the body
// text below opens from the icon's bottom, not the heading's.
internal static partial class HtmlToPdfConverter
{
    private const double CtPxPt = 0.75;             // 96 dpi: one CSS pixel
    private const double CtLineF = 1.171875;        // Roboto's normal line box
    private const double CtAscEm = 0.9277;          // its hhea ascent / upem
    private const double CtMarginXPt = 96.0;        // the sheet's own margins
    private const double CtMarginYPt = 72.0;
    private const double CtContentWPt = 1048.59;    // the width every percentage resolves against
    private const double CtSheetHPt = 842.0;

    // the blocks' own paddings, converted at 0.75
    private const double CtHeadPadTopPt = 18.75;    // .auditReportHeading padding: 25px 0 10px
    private const double CtHeadPadBotPt = 7.5;
    private const double CtHeadMarginPt = 11.25;    // …over its 15px margin-bottom
    private const double CtMainPadTopPt = 15.0;     // .auditReportHeadingMain padding: 20px 0 5px
    private const double CtMainPadBotPt = 3.75;
    private const double CtSubHeadPadTopPt = 9.0;   // .auditReportSubHeading padding: 12px 0 5px
    private const double CtSubHeadPadBotPt = 3.75;
    private const double CtSubSubPadTopPt = 12.75;  // .auditReportSubSubHeading padding: 17px 0 0
    private const double CtTextPadTopPt = 11.25;    // .auditReportText padding: 15px 0 5px 1%
    private const double CtTextPadBotPt = 3.75;
    private const double CtSubTextPadBotPt = 3.75;  // .auditReportSubText padding: 0 0 5px 65px
    private const double CtRowMarginPt = 7.5;       // .auditReportTwoColumnDiv margin-top: 10px
    private const double CtRowPadPt = 7.5;          // .auditReportRowLabel padding: 10px 0

    // the icon floats, which are what the text beside them has to clear
    private const double CtIconBoxPt = 40.93;       // the 32px arrow in its 15px/5px box
    private const double CtSubIconBoxPt = 34.97;    // the 25x22 arrow in its 22px/30px box
    private const double CtSubIconPadTopPt = 16.5;  // …that box's own 22px padding-top

    // the flow's left edges, all derived from the content width
    private const double CtTextIndentFrac = 0.01;   // .auditReportText padding-left: 1%
    private const double CtSubTextIndentPt = 48.75; // .auditReportSubText padding-left: 65px
    private const double CtSubHeadIndentPt = 43.99; // past the 32px icon float
    private const double CtSubSubIndentPt = 61.46;  // 30px + 3% + 10px
    private const double CtRowIndentFrac = 0.10;    // .auditReportTwoColumnDiv margin-left: 10%
    private const double CtRowWidthFrac = 0.80;
    private const double CtRowLabelFrac = 0.68;     // .auditReportRowLabel width: 68%
    private const double CtRowValueFrac = 0.30;
    private const double CtRowPadLeftFrac = 0.02;   // …over its 2% padding-left

    // the wrap boxes: .auditReportText declares width 98%, while .auditReportSubText
    // declares none at all - a float with no width takes the container's own,
    // measuring from its 65px indent rather than inside it
    private const double CtTextWrapFrac = 0.98;
    private const double CtTextWrapPt = CtContentWPt * CtTextWrapFrac;
    private const double CtSubTextWrapPt = CtContentWPt;

    // the grids. Each cell is its declared percentage of the table's 96% box,
    // plus the table's own default 2px cell spacing - which is what puts every
    // column boundary exactly where the reference has it.
    private const double CtTableMarginFrac = 0.02;  // .auditReportTableDiv margin-left: 2%
    private const double CtTableWidthFrac = 0.96;   // …over its width: 96%
    private const double CtTableMarginTopPt = 7.5;  // …and its 10px margin-top
    private const double CtCellSpacePt = 1.5;       // the table's 2px cellspacing
    private const double CtRulePt = 0.75;           // every border is 1px
    private const double CtHeadRowPt = 32.25;       // a 10px-padded 12pt header row
    private const double CtValueRowPt = 34.5;       // a 12px-padded 12pt value row
    private const double CtNineHeadRowPt = 22.5;    // a 6px-padded 9pt header row
    private const double CtNineValueRowPt = 25.5;   // …over its 12pt value rows
    private const double CtHeadDropPt = 9.2;        // the header caption inside its row
    private const double CtValueDropPt = 9.95;
    private const double CtNineHeadDropPt = 6.06;
    private const double CtNineValueDropPt = 5.45;
    private const double CtCellPadLeftPt = 7.5;     // the left column's 10px text indent
    private const double CtNineCellPadLeftPt = 3.75;
    private const int CtChartLookbackChars = 300;   // far enough to see a chart column's float

    // the cost plate that rides in a section's narrow column
    private const double CtCostColPadTopPt = 30.0;  // the column's own 40px padding-top
    private const double CtCostMarginFrac = 0.10;   // .auditReportCostBoxHeader margin-left: 10%
    private const double CtCostWidthFrac = 0.80;    // …over its width: 80%
    private const double CtCostHeaderPt = 37.5;     // its 42px plate over an 8px pad
    private const double CtCostHeaderPadPt = 6.0;   // …with an 8px padding-top
    private const double CtCostBodyPt = 120.0;      // .auditReportCostBoxValue min-height: 160px
    private const double CtCostGap1Pt = 15.0;       // the plate's inner 20px paddings
    private const double CtCostGap2Pt = 1.5;
    private const double CtIconColFrac = 0.06;      // the narrow column's icon div: width 6%
    private const double CtIconGapPt = 7.5;         // …over its 10px gutter
    private const int CtCostBoxScanChars = 1400;    // the plate's own markup span
    private const int CtLeadScanChars = 260;        // enough to reach a section's icon div

    // the title block, which opens the document under its 300px margin
    private const double CtTitleTopPt = 352.32;
    private const double CtTitleLeftPt = 15.0;      // its 20px margin-left
    private const double CtTitlePadTopPt = 18.75;
    private const double CtTitlePadBotPt = 7.5;
    private const double CtTitleMarginPt = 3.75;
    private const double CtTitleSubMarginPt = 30.0;
    private const double CtBreakDivPt = 0.75;       // the 1px page-break spacer

    private const int CtLayerFill = 0;
    private const int CtLayerRule = 1;
    private const int CtLayerText = 2;

    private static readonly Color CtInk = Color.FromRgb(0x66, 0x73, 0x79);
    private static readonly Color CtHeadInk = Color.FromRgb(0x3B, 0x41, 0x44);
    private static readonly Color CtBlue = Color.FromRgb(0x00, 0xA6, 0xFF);
    private static readonly Color CtRowBg = Color.FromRgb(0xD6, 0xE6, 0xF2);
    private static readonly Color CtWhite = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color CtBlack = Color.FromRgb(0, 0, 0);
    private static readonly Color CtCellBg = Color.FromRgb(0xED, 0xED, 0xED);
    private static readonly Color CtDotRule = Color.FromRgb(0xB5, 0xB5, 0xB5);

    private sealed class CtItem
    {
        public int Sheet;
        public double Y;          // line-box top
        public double X;
        public double Size;
        public string Text = "";
        public Color Ink = CtInk;
    }

    /// <summary>Roboto's line box, rounded to a whole device pixel.</summary>
    private static double CtLineH(double pt)
        => Math.Round(pt / CtPxPt * CtLineF, MidpointRounding.AwayFromZero) * CtPxPt;

    private static double CtHalf(double pt) => (CtLineH(pt) - pt * CtLineF) / 2.0;

    /// <summary>Render the infrastructure-assessment report, or null when the
    /// document is not one.</summary>
    private static Document? TryRenderAuditReport(string html)
    {
        if (!html.Contains("auditReportSubSubHeadingImageDiv", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("auditReportTwoColumnDiv", StringComparison.OrdinalIgnoreCase)
            || !html.Contains("auditReportTitleMain", StringComparison.OrdinalIgnoreCase))
            return null;
        // Roboto ships WITH the document rather than the system, registered as a
        // folder source under its file name. Ask for the installed face by name -
        // a plain repository lookup answers with a substitute whose advances run
        // over a percent wide, which is enough to move a line break.
        var reg = Text.FontRepository.FaceInstalled("Roboto-Regular")
            ? Text.FontRepository.GetTtfData("Roboto-Regular")
            : Text.FontRepository.FaceInstalled("Roboto")
                ? Text.FontRepository.GetTtfData("Roboto")
                : null;
        if (reg is null) return null;

        var bodyM = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body", RegexOptions.IgnoreCase);
        var body = bodyM.Success ? bodyM.Groups[1].Value : html;

        var pageW = CtMarginXPt + CtContentWPt + CtMarginXPt;
        var left = CtMarginXPt;
        var top = CtMarginYPt;
        var bottom = CtSheetHPt - CtMarginYPt;

        var items = new List<CtItem>();
        var rows = new List<(int Sheet, double Top)>();
        var fills = new List<(int Sheet, double X, double Top, double W, double H, Color C)>();
        var rules = new List<(int Sheet, double X0, double X1, double Y, Color C)>();
        var sheet = 0;
        var y = top;
        var sheetHasGrid = false;
        // a section may split into floated columns; while one is open the
        // percentages resolve against IT rather than the sheet
        var colLeft = left;
        var colWidth = CtContentWPt;
        var colRowTop = 0.0;
        var colDeepest = 0.0;
        var colSheet = -1;
        var inCols = false;

        var doc = new Document();
        var pages = new List<Page>();
        Page PageAt(int i)
        {
            while (pages.Count <= i)
            {
                var p = doc.Pages.Add(pageW, CtSheetHPt);
                EnsureFonts(p);
                pages.Add(p);
            }
            return pages[i];
        }

        double Measure(byte[] ttf, string s, double size)
        {
            if (s.Length == 0) return 0;
            if (PageAt(0).Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * size * 0.5;
            return Text.Type0FontEmbedder.MeasureText(fd, ttf, "RobotoRegular", s, size,
                stripSpacesInBaseFont: true);
        }

        List<string> Wrap(string text, double width, double size)
        {
            var outp = new List<string>();
            var cur = "";
            foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = cur.Length == 0 ? w : cur + " " + w;
                if (cur.Length > 0 && Measure(reg, t, size) > width) { outp.Add(cur); cur = w; }
                else cur = t;
            }
            if (cur.Length > 0) outp.Add(cur);
            if (outp.Count == 0) outp.Add("");
            return outp;
        }

        // a splittable run of lines: each line moves to the next sheet on its own
        void Lines(IEnumerable<string> lines, double x, double size, Color ink)
        {
            foreach (var ln in lines)
            {
                if (y + CtLineH(size) > bottom) { sheet++; y = top; sheetHasGrid = false; }
                if (ln.Length > 0)
                    items.Add(new CtItem
                    {
                        Sheet = sheet, Y = y + CtHalf(size), X = x, Size = size,
                        Text = ln, Ink = ink,
                    });
                y += CtLineH(size);
            }
        }

        void Atomic(double h, double padTop, double x, double size, string text, Color ink)
        {
            if (y + h > bottom) { sheet++; y = top; sheetHasGrid = false; }
            if (text.Length > 0)
                items.Add(new CtItem
                {
                    Sheet = sheet, Y = y + padTop + CtHalf(size), X = x, Size = size,
                    Text = text, Ink = ink,
                });
            y += h;
        }

        double TextX() => colLeft + colWidth * CtTextIndentFrac;
        double TextWrap() => colWidth * CtTextWrapFrac;
        var pendingMain = 0.0;
        var mainOpen = false;

        // .auditReportHeadingMain's own 5px padding-bottom, paid once its
        // heading and body text have been placed
        void CloseMain()
        {
            if (!mainOpen) return;
            mainOpen = false;
            y += CtMainPadBotPt;
        }

        foreach (var (cls, inner, meta) in CtBlocks(body))
        {
            switch (cls)
            {
                case "auditReportTitleMain":
                    y = CtTitleTopPt;
                    Atomic(CtTitlePadTopPt + CtLineH(30) + CtTitlePadBotPt + CtTitleMarginPt,
                        CtTitlePadTopPt, left + CtTitleLeftPt, 30, CtFlat(inner), CtHeadInk);
                    break;
                case "auditReportTitleSub":
                    Atomic(CtLineH(16) + CtTitleSubMarginPt, 0, left + CtTitleLeftPt, 16,
                        CtFlat(inner), CtHeadInk);
                    y += CtBreakDivPt;
                    break;
                case "auditReportHeading":
                    CloseMain();
                    Atomic(CtHeadPadTopPt + CtLineH(27) + CtHeadPadBotPt + CtHeadMarginPt,
                        CtHeadPadTopPt, left, 27, CtFlat(inner), CtHeadInk);
                    break;
                case "auditReportHeadingMain":
                    CloseMain();
                    pendingMain = CtMainPadTopPt;
                    mainOpen = true;
                    break;
                case "auditReportSubHeading":
                {
                    y += pendingMain;
                    pendingMain = 0;
                    var wrapTop = y;
                    Atomic(CtSubHeadPadTopPt + CtLineH(19) + CtSubHeadPadBotPt,
                        CtSubHeadPadTopPt,
                        colLeft + (inCols
                            ? colWidth * CtTextIndentFrac + colWidth * CtIconColFrac + CtIconGapPt
                            : CtSubHeadIndentPt),
                        19, CtFlat(inner), CtBlue);
                    // the section's own icon float is taller than its heading
                    y = Math.Max(y, wrapTop + CtIconBoxPt);
                    break;
                }
                case "auditReportSubSubHeading":
                {
                    CloseMain();
                    var wrapTop = y;
                    // a section may shorten its own and its icon's padding inline
                    var pad = CtInlinePadTop(meta, "auditReportSubSubHeading", CtSubSubPadTopPt);
                    var iconPad = CtInlinePadTop(meta, "auditReportSubSubHeadingImageDiv",
                        CtSubIconPadTopPt);
                    Atomic(pad + CtLineH(16), pad, left + CtSubSubIndentPt, 16,
                        CtFlat(inner), CtBlue);
                    y = Math.Max(y, wrapTop + iconPad + CtSubIconBoxPt - CtSubIconPadTopPt);
                    break;
                }
                case "auditReportText":
                    y += CtTextPadTopPt;
                    Lines(CtSegments(inner).SelectMany(t => Wrap(t, TextWrap(), 12)),
                        TextX(), 12, CtInk);
                    y += CtTextPadBotPt;
                    break;
                case "auditReportSubText":
                    Lines(CtSegments(inner).SelectMany(t => Wrap(t, colWidth, 12)),
                        colLeft + CtSubTextIndentPt, 12, CtInk);
                    y += CtSubTextPadBotPt;
                    break;
                case "auditReportTableDiv":
                {
                    var tableLeft = left + CtContentWPt * CtTableMarginFrac;
                    var tableW = CtContentWPt * CtTableWidthFrac;
                    y += CtTableMarginTopPt;
                    foreach (var cells in CtTableRows(inner))
                    {
                        if (cells.Count == 0) continue;
                        // the nine- and three-column grids share the 6px cell
                        // padding that sets their row heights
                        var nine = cells[0].Cls.Contains("NineColumn", StringComparison.Ordinal)
                            || cells[0].Cls.Contains("ThreeColumn", StringComparison.Ordinal);
                        var small = cells[0].Cls.Contains("NineColumn", StringComparison.Ordinal);
                        // a header row is the one whose cells declare the label
                        // classes - the grid's own first cell says "Headerlabel"
                        // rather than "Toplabel", so both have to count
                        var head = cells.Exists(c =>
                            c.Cls.Contains("oplabel", StringComparison.Ordinal)
                            || c.Cls.Contains("Headerlabel", StringComparison.Ordinal));
                        var rowH = nine
                            ? (small && head ? CtNineHeadRowPt : CtNineValueRowPt)
                            : (head ? CtHeadRowPt : CtValueRowPt);
                        if (y + rowH > bottom) { sheet++; y = top; sheetHasGrid = false; }
                        var cx = tableLeft;
                        for (var ci = 0; ci < cells.Count; ci++)
                        {
                            var (ccls, scls, txt) = cells[ci];
                            var cw = tableW * CtColFrac(ccls);
                            var size = small && head ? 9.0 : 12.0;
                            var leftCol = ci == 0;
                            // the cell sits half a spacing in from its column
                            var bx = cx + CtCellSpacePt / 2;
                            // a highlighted row names its own colour on the cell
                            var inlineBg = CtInlineBg(ccls);
                            if (inlineBg is { } ib)
                                fills.Add((sheet, cx, y, cw + CtCellSpacePt,
                                    rowH - CtCellSpacePt, ib));
                            else if (leftCol)
                                fills.Add((sheet, cx, y, cw + CtCellSpacePt,
                                    rowH - CtCellSpacePt, CtCellBg));
                            var drop = nine
                                ? (small && head ? CtNineHeadDropPt : CtNineValueDropPt)
                                : (head ? CtHeadDropPt : CtValueDropPt);
                            if (txt.Length > 0)
                            {
                                var tw = Measure(reg, txt, size);
                                // the three-column grid's spans are 100% wide and
                                // centre their own text, first column included
                                var centred = !leftCol
                                    || ccls.Contains("ThreeColumn", StringComparison.Ordinal);
                                var tx = centred
                                    ? bx + (cw - tw) / 2
                                    : bx + (nine ? CtNineCellPadLeftPt : CtCellPadLeftPt);
                                items.Add(new CtItem
                                {
                                    Sheet = sheet, Y = y + drop, X = tx, Size = size,
                                    Text = txt,
                                    Ink = head ? (leftCol && !nine ? CtBlack : CtBlue) : CtBlack,
                                });
                            }
                            // the cell's own borders: a solid blue pair around a
                            // header, a dotted grey under a value row
                            var ruleC = head ? CtBlue : CtDotRule;
                            if (head)
                                rules.Add((sheet, cx, cx + cw,
                                    y + CtRulePt / 2,
                                    leftCol && !nine ? CtCellBg : CtBlue));
                            rules.Add((sheet, cx, cx + cw,
                                y + rowH - CtCellSpacePt - CtRulePt / 2, ruleC));
                            cx += cw + CtCellSpacePt;
                        }
                        y += rowH;
                        sheetHasGrid = true;
                    }
                    break;
                }
                case "col":
                {
                    var frac = CtColumnFrac(inner);
                    if (frac >= 1.0 || frac <= 0)
                    {
                        // the row closes on its deepest column, but only when
                        // that column ended on THIS sheet - a column that spilled
                        // has already carried the flow forward
                        if (inCols && colSheet == sheet) y = Math.Max(y, colDeepest);
                        inCols = false;
                        colLeft = left;
                        colWidth = CtContentWPt;
                        break;
                    }
                    if (!inCols)
                    {
                        colRowTop = y;
                        colDeepest = y;
                        colSheet = sheet;
                        inCols = true;
                        colLeft = left;
                    }
                    else if (colSheet == sheet)
                    {
                        colDeepest = Math.Max(colDeepest, y);
                        y = colRowTop;
                        colLeft += colWidth;
                    }
                    else
                    {
                        colRowTop = y;
                        colDeepest = y;
                        colSheet = sheet;
                        colLeft += colWidth;
                    }
                    colWidth = CtContentWPt * frac;
                    break;
                }
                case "costbox":
                {
                    var bx = colLeft + colWidth * CtCostMarginFrac;
                    var bw = colWidth * CtCostWidthFrac;
                    var by = y + CtCostColPadTopPt;
                    fills.Add((sheet, bx, by, bw, CtCostHeaderPt, CtBlue));
                    fills.Add((sheet, bx, by + CtCostHeaderPt, bw, CtCostBodyPt, CtRowBg));
                    var cy = by + CtCostHeaderPadPt;
                    var first = true;
                    foreach (var (txt, size, padTop) in CtCostLines(inner))
                    {
                        if (!first) cy += padTop;
                        var w = Measure(reg, txt, size);
                        items.Add(new CtItem
                        {
                            Sheet = sheet, Y = cy, X = bx + (bw - w) / 2, Size = size,
                            Text = txt, Ink = first ? CtWhite : CtBlack,
                        });
                        cy += CtLineH(size);
                        if (first) { cy = by + CtCostHeaderPt; first = false; }
                    }
                    y = by + CtCostHeaderPt + CtCostBodyPt;
                    break;
                }
                case "pagebreak":
                    // A `page-break-before` is taken only where it would part a
                    // finished grid or chart from the next analysis part; one
                    // that merely interrupts running prose is passed over, which
                    // is what the reference does with the five inside the
                    // narrative half of the report.
                    if (sheetHasGrid
                        && (inner.Contains("auditReportHeading\"", StringComparison.Ordinal)
                            || inner.Contains("data-highcharts-chart", StringComparison.Ordinal)))
                    {
                        CloseMain();
                        sheet++;
                        y = top;
                        sheetHasGrid = false;
                    }
                    break;
                case "chartpair":
                    break;
                case "chart":
                {
                    var h = CtChartHeight(inner);
                    if (h > 0)
                    {
                        if (y + h > bottom) { sheet++; y = top; sheetHasGrid = false; }
                        y += h;
                        sheetHasGrid = true;
                    }
                    break;
                }
                case "auditReportTwoColumnDiv":
                {
                    CloseMain();
                    var h = 2 * CtRowPadPt + CtLineH(14);
                    if (y + CtRowMarginPt + h > bottom) { sheet++; y = top; sheetHasGrid = false; }
                    y += CtRowMarginPt;
                    rows.Add((sheet, y));
                    var (lab, val) = CtRowPair(inner);
                    var rowL = left + CtContentWPt * CtRowIndentFrac;
                    var rowW = CtContentWPt * CtRowWidthFrac;
                    items.Add(new CtItem
                    {
                        Sheet = sheet, Y = y + CtRowPadPt + CtHalf(14),
                        X = rowL + rowW * CtRowPadLeftFrac, Size = 14, Text = lab, Ink = CtBlack,
                    });
                    var vw = Measure(reg, val, 14);
                    items.Add(new CtItem
                    {
                        Sheet = sheet, Y = y + CtRowPadPt + CtHalf(14),
                        X = rowL + rowW * (CtRowLabelFrac + CtRowPadLeftFrac)
                            + (rowW * CtRowValueFrac - vw) / 2,
                        Size = 14, Text = val, Ink = CtWhite,
                    });
                    y += h;
                    break;
                }
            }
        }

        // ── emit ────────────────────────────────────────────────────────────
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        var ops = new List<(int Sheet, int Layer, int Seq, string Text)>();
        var seq = 0;

        string Rgb(Color c, string op)
            => string.Create(invc,
                $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} {op} ");

        foreach (var (rs, rt) in rows)
        {
            PageAt(rs);
            var rowL = left + CtContentWPt * CtRowIndentFrac;
            var rowW = CtContentWPt * CtRowWidthFrac;
            var labW = rowW * (CtRowLabelFrac + CtRowPadLeftFrac);
            var h = 2 * CtRowPadPt + CtLineH(14);
            ops.Add((rs, CtLayerFill, seq++, string.Create(invc,
                $"q {Rgb(CtRowBg, "rg")}{rowL:0.##} {CtSheetHPt - rt - h:0.##} "
                + $"{labW:0.##} {h:0.##} re f Q")));
            ops.Add((rs, CtLayerFill, seq++, string.Create(invc,
                $"q {Rgb(CtBlue, "rg")}{rowL + labW:0.##} {CtSheetHPt - rt - h:0.##} "
                + $"{rowW * CtRowValueFrac:0.##} {h:0.##} re f Q")));
        }

        foreach (var (fs, fx, ft, fw, fh, fc) in fills)
        {
            PageAt(fs);
            ops.Add((fs, CtLayerFill, seq++, string.Create(invc,
                $"q {Rgb(fc, "rg")}{fx:0.##} {CtSheetHPt - ft - fh:0.##} "
                + $"{fw:0.##} {fh:0.##} re f Q")));
        }
        foreach (var (rs, x0, x1, ry, rc) in rules)
        {
            PageAt(rs);
            ops.Add((rs, CtLayerRule, seq++, string.Create(invc,
                $"q {Rgb(rc, "RG")}{CtRulePt:0.##} w {x0:0.##} {CtSheetHPt - ry:0.##} m "
                + $"{x1:0.##} {CtSheetHPt - ry:0.##} l S Q")));
        }

        foreach (var it in items)
        {
            var pg = PageAt(it.Sheet);
            if (pg.Dict.Get("Resources") is not Core.PdfDictionary res
                || res.Get("Font") is not Core.PdfDictionary fd) continue;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, reg, "RobotoRegular", it.Text,
                stripSpacesInBaseFont: true);
            var baseline = it.Y + it.Size * CtAscEm;
            ops.Add((it.Sheet, CtLayerText, seq++, string.Create(invc,
                $"BT {Rgb(it.Ink, "rg")}/{rn} {it.Size:0.##} Tf 1 0 0 1 {it.X:0.##} "
                + $"{CtSheetHPt - baseline:0.##} Tm ")
                + "<" + System.Convert.ToHexString(hex) + "> Tj ET"));
        }

        foreach (var g in ops.GroupBy(o => o.Sheet))
        {
            var sb = new StringBuilder();
            foreach (var o in g.OrderBy(o => o.Layer).ThenBy(o => o.Seq))
                sb.Append(o.Text).Append('\n');
            pages[g.Key].AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        }
        return doc;
    }

    private static string CtFlat(string frag)
    {
        var t = Regex.Replace(frag, @"</?(?:b|i|u|span|a|strong|em)\b[^>]*>", "",
            RegexOptions.IgnoreCase);
        return Regex.Replace(DecodeEntities(Regex.Replace(t, "<[^>]+>", " ")), @"\s+", " ").Trim();
    }

    /// <summary>A block's own lines: `&lt;br&gt;` splits them, and two in a row
    /// leave a blank line behind.</summary>
    private static List<string> CtSegments(string frag)
    {
        var outp = new List<string>();
        foreach (var part in Regex.Split(frag, @"<br\s*/?>", RegexOptions.IgnoreCase))
            outp.Add(CtFlat(part));
        while (outp.Count > 0 && outp[^1].Length == 0) outp.RemoveAt(outp.Count - 1);
        return outp;
    }

    /// <summary>A chart reserves the height its own Highcharts container
    /// declares, converted at 96 dpi.</summary>
    private static double CtChartHeight(string inner)
    {
        var m = Regex.Match(inner, @"height\s*:\s*(\d+)px", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var px) ? px * CtPxPt : 0.0;
    }

    /// <summary>A padding-top the block states inline for the named class,
    /// falling back to the class's own.</summary>
    private static double CtInlinePadTop(string markup, string cls, double dflt)
    {
        var m = Regex.Match(markup,
            Regex.Escape(cls) + @"[""'][^>]*padding-top\s*:\s*(\d+)px", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) * CtPxPt : dflt;
    }

    /// <summary>A background colour a cell names inline, or null.</summary>
    private static Color? CtInlineBg(string cellCls)
    {
        var m = Regex.Match(cellCls, @"background-color\s*:\s*#([0-9a-fA-F]{6})",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var v = m.Groups[1].Value;
        return Color.FromRgb(System.Convert.ToInt32(v[..2], 16),
            System.Convert.ToInt32(v.Substring(2, 2), 16),
            System.Convert.ToInt32(v.Substring(4, 2), 16));
    }

    /// <summary>A floated column's declared share of its container.</summary>
    private static double CtColumnFrac(string tag)
    {
        var m = Regex.Match(tag, @"width\s*:\s*(\d+)%", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var pc) ? pc / 100.0 : 1.0;
    }

    /// <summary>The cost plate's caption and its stacked values, each with the
    /// size its own span declares.</summary>
    private static List<(string Text, double Size, double PadTop)> CtCostLines(string frag)
    {
        var outp = new List<(string, double, double)>();
        var head = Regex.Match(frag, @"CostBoxSpanHeader[""'][^>]*>([^<]*)<", RegexOptions.IgnoreCase);
        if (head.Success) outp.Add((CtFlat(head.Groups[1].Value), 17.0, 0.0));
        // each value sits in its own div, and THAT div's padding-top is the gap
        foreach (Match m in Regex.Matches(frag,
                     @"<div\b[^>]*style\s*=\s*[""']([^""']*)[""'][^>]*>\s*<span\b[^>]*"
                     + @"CostBoxSpanValue[""']\s*style\s*=\s*[""']font-size:\s*([\d.]+)pt[^""']*[""'][^>]*>([^<]*)<",
                     RegexOptions.IgnoreCase))
        {
            var t = CtFlat(m.Groups[3].Value);
            if (t.Length == 0) continue;
            if (!double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var sz)) continue;
            var pt = Regex.Match(m.Groups[1].Value, @"padding-top\s*:\s*(\d+)px",
                RegexOptions.IgnoreCase);
            outp.Add((t, sz, pt.Success ? int.Parse(pt.Groups[1].Value) * CtPxPt : 0.0));
        }
        return outp;
    }

    /// <summary>A cell's share of the grid, from the class it declares.</summary>
    private static double CtColFrac(string cls)
    {
        if (cls.Contains("NineColumn", StringComparison.Ordinal))
        {
            if (cls.Contains("TopLeftlabel", StringComparison.Ordinal)
                || cls.Contains("LeftValuelabel", StringComparison.Ordinal)) return 0.07;
            if (cls.Contains("Small", StringComparison.Ordinal)) return 0.04;
            return 0.14;
        }
        if (cls.Contains("ThreeColumn", StringComparison.Ordinal))
            return cls.Contains("TopLeftlabel", StringComparison.Ordinal)
                || cls.Contains("LeftValuelabel", StringComparison.Ordinal) ? 0.25 : 0.14;
        if (cls.Contains("LeftHeaderlabel", StringComparison.Ordinal)
            || cls.Contains("LeftValuelabel", StringComparison.Ordinal)) return 0.29;
        return 0.17;
    }

    /// <summary>A grid's rows, each cell carrying its own class, its span's
    /// class and its text.</summary>
    private static List<List<(string Cls, string SpanCls, string Text)>> CtTableRows(string frag)
    {
        var outp = new List<List<(string, string, string)>>();
        foreach (Match r in Regex.Matches(frag, @"<tr\b[^>]*>([\s\S]*?)</tr\s*>",
                     RegexOptions.IgnoreCase))
        {
            var cells = new List<(string, string, string)>();
            foreach (Match c in Regex.Matches(r.Groups[1].Value,
                         @"<td\b([^>]*)>([\s\S]*?)</td\s*>", RegexOptions.IgnoreCase))
            {
                var ccls = Regex.Match(c.Groups[1].Value, @"class\s*=\s*[""']([^""']*)",
                    RegexOptions.IgnoreCase);
                var style = Regex.Match(c.Groups[1].Value, @"style\s*=\s*[""']([^""']*)",
                    RegexOptions.IgnoreCase);
                var scls = Regex.Match(c.Groups[2].Value, @"<span\b[^>]*class\s*=\s*[""']([^""']*)",
                    RegexOptions.IgnoreCase);
                cells.Add((
                    (ccls.Success ? ccls.Groups[1].Value : "")
                        + (style.Success ? " " + style.Groups[1].Value : ""),
                    scls.Success ? scls.Groups[1].Value : "", CtFlat(c.Groups[2].Value)));
            }
            if (cells.Count > 0) outp.Add(cells);
        }
        return outp;
    }

    private static (string Label, string Value) CtRowPair(string frag)
    {
        var lab = Regex.Match(frag, @"class\s*=\s*[""']auditReportRowLabel[""'][^>]*>([\s\S]*?)</div",
            RegexOptions.IgnoreCase);
        var val = Regex.Match(frag, @"class\s*=\s*[""']auditReportRowValue[""'][^>]*>([\s\S]*?)</div",
            RegexOptions.IgnoreCase);
        return (lab.Success ? CtFlat(lab.Groups[1].Value) : "",
            val.Success ? CtFlat(val.Groups[1].Value) : "");
    }

    /// <summary>The report's blocks in document order, each with its own inner
    /// markup (the wrapper divs carry no geometry of their own).</summary>
    private static List<(string Cls, string Inner, string Meta)> CtBlocks(string body)
    {
        var outp = new List<(string, string, string)>();
        var rx = new Regex(
            @"<div\b[^>]*class\s*=\s*[""'](auditReportHeading|auditReportTitleMain|"
            + @"auditReportTitleSub|auditReportSubHeading|auditReportSubSubHeading|"
            + @"auditReportText|auditReportSubText|auditReportHeadingMain|"
            + @"auditReportTwoColumnDiv)[""'][^>]*>", RegexOptions.IgnoreCase);
        var tableRx = new Regex(
            @"<table\b[^>]*class\s*=\s*[""']auditReportTableDiv[""'][^>]*>",
            RegexOptions.IgnoreCase);
        var chartRx = new Regex(@"<div\b[^>]*data-highcharts-chart\s*=", RegexOptions.IgnoreCase);
        var breakRx = new Regex(@"<div\b[^>]*page-break-before\s*:\s*always[^>]*>",
            RegexOptions.IgnoreCase);
        var colRx = new Regex(
            @"<div\b[^>]*style\s*=\s*[""'][^""']*float\s*:\s*left\s*;\s*width\s*:\s*(\d+)%",
            RegexOptions.IgnoreCase);
        var costRx = new Regex(
            @"<div\b[^>]*class\s*=\s*[""']auditReportCostBoxHeader[""'][^>]*>",
            RegexOptions.IgnoreCase);
        var found = new List<(int At, string Cls, string Inner, string Meta)>();
        foreach (Match m in rx.Matches(body))
        {
            // the block's own tag and the markup just before it: a section may
            // override its class paddings inline, and its icon div sits in that
            // lead. Kept APART from the text - a lead starts mid-markup.
            var lead = body[Math.Max(0, m.Index - CtLeadScanChars)..m.Index];
            found.Add((m.Index, m.Groups[1].Value, VrDivAt(body, m.Index), lead + m.Value));
        }
        foreach (Match m in tableRx.Matches(body))
        {
            var end = body.IndexOf("</table", m.Index, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = body.Length;
            found.Add((m.Index, "auditReportTableDiv", body[m.Index..end], ""));
        }
        foreach (Match m in chartRx.Matches(body))
        {
            // charts come in pairs of 45% columns, one floated left and one
            // right - the right-hand one sits BESIDE its partner and so adds
            // no height of its own
            var lead = body[Math.Max(0, m.Index - CtChartLookbackChars)..m.Index];
            var paired = lead.Contains("float:right", StringComparison.OrdinalIgnoreCase)
                || lead.Contains("float: right", StringComparison.OrdinalIgnoreCase);
            found.Add((m.Index, paired ? "chartpair" : "chart", VrDivAt(body, m.Index), ""));
        }
        foreach (Match m in colRx.Matches(body))
            found.Add((m.Index, "col", m.Value, ""));
        foreach (Match m in costRx.Matches(body))
        {
            var to = body.IndexOf("auditReportTableDiv", m.Index, StringComparison.OrdinalIgnoreCase);
            if (to < 0) to = Math.Min(body.Length, m.Index + CtCostBoxScanChars);
            found.Add((m.Index, "costbox", body[m.Index..to], ""));
        }
        var breaks = breakRx.Matches(body);
        for (var bi = 0; bi < breaks.Count; bi++)
        {
            var from = breaks[bi].Index;
            var to = bi + 1 < breaks.Count ? breaks[bi + 1].Index : body.Length;
            found.Add((from, "pagebreak", body[from..to], ""));
        }
        found.Sort((a, b2) => a.At.CompareTo(b2.At));
        foreach (var f in found) outp.Add((f.Cls, f.Inner, f.Meta));
        return outp;
    }
}
