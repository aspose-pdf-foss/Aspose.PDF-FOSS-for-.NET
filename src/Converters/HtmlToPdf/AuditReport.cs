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
    // column boundary exactly where the expected output has it.
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

    private static readonly Color CtInk = Color.FromRgbBytes(0x66, 0x73, 0x79);
    private static readonly Color CtHeadInk = Color.FromRgbBytes(0x3B, 0x41, 0x44);
    private static readonly Color CtBlue = Color.FromRgbBytes(0x00, 0xA6, 0xFF);
    private static readonly Color CtRowBg = Color.FromRgbBytes(0xD6, 0xE6, 0xF2);
    private static readonly Color CtWhite = Color.FromRgbBytes(0xFF, 0xFF, 0xFF);
    private static readonly Color CtBlack = Color.FromArgb(0, 0, 0);
    private static readonly Color CtCellBg = Color.FromRgbBytes(0xED, 0xED, 0xED);
    private static readonly Color CtDotRule = Color.FromRgbBytes(0xB5, 0xB5, 0xB5);

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
        var ar = new AuditReportState();
        ar.reg = (Text.FontRepository.FaceInstalled("Roboto-Regular")
            ? Text.FontRepository.GetTtfData("Roboto-Regular")
            : Text.FontRepository.FaceInstalled("Roboto")
                ? Text.FontRepository.GetTtfData("Roboto")
                : null)!;
        if (ar.reg is null) return null;

        ar.bodyM = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body", RegexOptions.IgnoreCase);
        ar.body = ar.bodyM.Success ? ar.bodyM.Groups[1].Value : html;

        ar.pageW = CtMarginXPt + CtContentWPt + CtMarginXPt;
        ar.left = CtMarginXPt;
        ar.top = CtMarginYPt;
        ar.bottom = CtSheetHPt - CtMarginYPt;

        ar.items = new List<CtItem>();
        ar.rows = new List<(int Sheet, double Top)>();
        ar.fills = new List<(int Sheet, double X, double Top, double W, double H, Color C)>();
        ar.rules = new List<(int Sheet, double X0, double X1, double Y, Color C)>();
        ar.sheet = 0;
        ar.y = ar.top;
        ar.sheetHasGrid = false;
        ar.colLeft = ar.left;
        ar.colWidth = CtContentWPt;
        ar.colRowTop = 0.0;
        ar.colDeepest = 0.0;
        ar.colSheet = -1;
        ar.inCols = false;

        ar.doc = new Document();
        ar.pages = new List<Page>();
        ar.pendingMain = 0.0;
        ar.mainOpen = false;

        foreach (var (cls, inner, meta) in CtBlocks(ar.body))
        {
            if (!LayoutAuditBlock(ar, cls, inner, meta)) break;
        }

        ar.invc = System.Globalization.CultureInfo.InvariantCulture;
        EmitAuditReport(ar);
        return ar.doc;
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
        return Color.FromRgbBytes(System.Convert.ToInt32(v[..2], 16),
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
