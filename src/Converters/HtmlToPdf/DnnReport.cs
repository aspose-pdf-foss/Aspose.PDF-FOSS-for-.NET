using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The DNN sectioned-report dialect: a DotNetNuke portal page (skinmaster box,
// ModuleContainer with a header band, ModuleSubHeader section bands and
// float-label field rows). The site's skin stylesheets sit behind
// <link>+@import chains — the flow engine never sees them — so the dialect
// draws the skin's box model directly. Every constant below is either derived
// from the skin CSS (the sheet and rule are named) or measured off the
// expected render of the enrollment-summary fixture where the rule lives in
// the portal's module stylesheet (a WebResource.axd bundle that no longer
// resolves).
internal static partial class HtmlToPdfConverter
{
    // skin.css DNN.css: .skinmaster { width: 984px; border: 1px #7994cb } —
    // the bordered box is 986 px = 739.5 pt; the sheet widens to hold it
    // between the 90 pt margins.
    private const double DnnSkinBoxPt = 986 * 0.75;
    // Base.css: td, th { font-size: 8pt; font-family: Verdana } — the module
    // content all sits in skin table cells.
    private const double DnnCellFontPt = 8.0;
    // DNN.css: body { font-size: 11px } — the breadcrumb date line.
    private const double DnnBodyFontPt = 11 * 0.75;
    // Module header/subheader bands are 24 px deep: min-height 18px +
    // 2px padding top/bottom + 1px borders (DNN.css .ModuleHeaderContainer).
    private const double DnnBandPt = 24 * 0.75;
    // .ModuleHeaderContainer / .ModuleBodyContainer pad content 10 px left.
    private const double DnnBodyPadPt = 10 * 0.75;
    // Verdana metrics (asc 2059/desc 430/upm 2048): the baseline seat below a
    // text line's bbox top, and the bbox height, at 8 pt.
    private const double DnnAscEm = 2059.0 / 2048.0;
    private const double DnnDescEm = 430.0 / 2048.0;
    // Module box left/right inset from the skinmaster content edge: the module
    // stylesheet (dead axd) sizes the centred pane to 953 px — measured
    // 103.1 / 817.9 on the expected render.
    private const double DnnModuleInsetPt = 13.5;
    // Header zone (measured; the skin composes it from skinheader padding 3px,
    // the logo's inline padding 25px/10px and the breadcrumb rows):
    private const double DnnLogoTopPt = 15.0;          // content top -> logo top
    private const double DnnLogoLeftPt = 21.75;        // border+3px+25px pads
    private const double DnnDateTopPt = 52.7;          // content top -> date bbox top
    // .Breadcrumb { padding: 0 15px } + 1px skinmaster border = right inset.
    private const double DnnDateRightPt = 12.0;
    // Content top -> module box top (skin rows between ruler and pane table).
    private const double DnnModuleTopPt = 117.4;
    // A field row's line box (13 px at 8pt Verdana) and the measured seat of
    // the text bbox below the line-box top (float/baseline slack, 2.4 px).
    private const double DnnRowLinePt = 13 * 0.75;
    private const double DnnRowSeatPt = 2.25;
    // Band text bbox tops below the band fill top (measured: the header
    // centres its 2px-padded 18px min-height; the subheader seats deeper).
    private const double DnnHeaderTextSeatPt = 4.1;
    private const double DnnSubTextSeatPt = 5.6;

    private interface IDnnBlock { }

    private sealed class DnnBand : IDnnBlock          // header or subheader
    {
        public string Text = "";
        public bool Header;                          // teal header vs DFEEF7 sub
    }

    private sealed class DnnFieldRow : IDnnBlock
    {
        // (label text, label border-box width pt, value text, x offset of the
        //  pair from the module body left). Label text right-aligns 7.5 pt
        //  inside the box right edge; the value sits AT the box right edge.
        public List<(string Label, double LabelW, string Value, double PairX)> Pairs = new();
        public double IndentPx;                      // ModuleBodyContainer pad-left
        public bool Bare;                            // no RowPad wrapper: taller line
    }

    private sealed class DnnParaLine : IDnnBlock      // stray plain-text line
    {
        public string Text = "";
#pragma warning disable S3218 // data-only nested type: its members are always reached through an instance
        public double Pitch = 23 * 0.75;
#pragma warning restore S3218
    }

    private sealed class DnnGap : IDnnBlock
    {
        public double H;
    }

    // A DataList grid: the header row of th cells, a record's summary row of
    // td cells (both at the scaled column grid), and a record's background
    // fill emitted BEFORE its content blocks with the pre-measured height.
    private sealed class DnnGridHeader : IDnnBlock
    {
#pragma warning disable S3218 // data-only nested type: its members are always reached through an instance
        public List<(string Text, double Wpx)> Cells = new();
#pragma warning restore S3218
    }

    private sealed class DnnGridRow : IDnnBlock
    {
#pragma warning disable S3218 // data-only nested type: its members are always reached through an instance
        public List<(string Text, double Wpx)> Cells = new();
#pragma warning restore S3218
    }

    private sealed class DnnGridFill : IDnnBlock
    {
        public double H;
        public bool White;                           // GridRowOdd records
        public bool FullWidth;                       // header-less list grids
    }

    /// <summary>Render a DNN portal report (see the class comment). Null when
    /// the document does not carry the skin's fingerprint.</summary>
    private static Document? TryRenderDnnReport(string html,
        double pageHIn)
    {
        if (!SafeRegex.IsMatch(html, @"class\s*=\s*[""']skinmaster[""']", RegexOptions.IgnoreCase)
            || html.IndexOf("ModuleHeaderContainer", StringComparison.OrdinalIgnoreCase) < 0
            || html.IndexOf("ContainerFieldLabelHoriz", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        var dn = new DnnReportState();
        if (!TryParseDnnReport(dn, pageHIn, html)) return null;

        DrawDnnHeader(dn);
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (var b in dn.blocks)
#pragma warning restore S3267
        {
            if (!RenderDnnBlock(dn, b)) break;
        }
        dn.moduleBotPerPage[dn.page] = dn.y;
        Touch(dn, dn.page, dn.y);

        dn.borderBlue = (1.0 * 0x79 / 255, 1.0 * 0x94 / 255, 1.0 * 0xCB / 255);
        dn.nearWhite = (1.0 * 0xFE / 255, 1.0 * 0xFE / 255, 1.0 * 0xFE / 255);
        for (var p = 0; p < dn.pageOps.Count; p++)
        {
            if (!AssembleDnnPage(dn, p)) break;
        }
        return dn.doc;
    }

    // ── parsing ────────────────────────────────────────────────────────────

    /// <summary>Inner HTML of the div opening at <paramref name="openIdx"/>.</summary>
    private static string? DnnInnerDiv(string html, int openIdx)
    {
        var end = html.IndexOf('>', openIdx);
        if (end < 0) return null;
        var depth = 1;
        foreach (Match m in SafeRegex.Matches(html[(end + 1)..], @"<(/?)div\b[^>]*>",
                     RegexOptions.IgnoreCase))
        {
            depth += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (depth == 0) return html.Substring(end + 1, m.Index);
        }
        return null;
    }

    /// <summary>Linear walk of a module's markup into layout blocks. Wrapper
    /// divs are entered, not skipped, so sibling CellLeft float groups merge
    /// into one field row; a RowPad, band, body-container or grid boundary
    /// closes the open row. Vertical rhythm: every RowPad opening contributes
    /// its padding pair (Controls.css .RowPad { padding: 5px 0 } — 7.5 pt
    /// between rows, verified by the measured 17.25 pt row pitch), and a
    /// ModuleBodyContainer brackets its children with its 5px/10px paddings
    /// and indents them by its 10px padding-left.</summary>
    /// <returns>True when the walk's last content element was a RowPad
    /// subtree (the caller then collapses its own bottom pad).</returns>
    private static bool DnnWalk(string inner, double indentPx, List<IDnnBlock> blocks, bool inRowPad = false)
    {
        var dw = new DnnWalkState();
        dw.inner = inner;
        dw.indentPx = indentPx;
        dw.blocks = blocks;
        dw.inRowPad = inRowPad;
        dw.lastRowPad = false;
        dw.pos = 0;
        dw.row = null;
        dw.runX = 0;
        dw.brRun = false;
        while (true)
        {
            if (!DnnWalkStep(dw)) break;
        }
        FlushDnnRow(dw);
        return dw.lastRowPad;
    }

    // Grid metrics (all measured on the expected render's Addresses and
    // Specialties DataLists): row heights, text seats below the row top, and
    // the 4px ListCell padding that joins each declared column width.
    private const double DnnGridHeaderHPt = 19.9;
    private const double DnnGridSummaryHPt = 20.3;
    private const double DnnGridTextSeatPt = 5.2;
    private const double DnnGridCellPadPx = 8.0;
    // Header/summary cell text inset from the cell's left edge (measured
    // 145.0 for the Address Type header at column x 133).
    private const double DnnGridCellTextPadPt = 12.0;

    /// <summary>A GridContainer DataList: GridRowHeader th cells, then
    /// GridRowEven/Odd records — a summary row of ListCell tds plus a details
    /// panel of ordinary field rows. Column x's scale the declared px widths
    /// (+4px padding each side) onto the grid width.</summary>
    private static void DnnParseGrid(string grid, double indentPx, List<IDnnBlock> blocks)
    {
        var hasHeader = grid.Contains("GridRowHeader", StringComparison.OrdinalIgnoreCase);
        foreach (Match td in SafeRegex.Matches(grid,
                     @"<td\b[^>]*class\s*=\s*[""'](GridRowHeader|GridRowEven|GridRowOdd)[""'][^>]*>",
                     RegexOptions.IgnoreCase))
        {
            var kind = td.Groups[1].Value;
            var end = DnnTagEnd(grid, td.Index, "td");
            if (end < 0) continue;
            var cell = grid[(grid.IndexOf('>', td.Index) + 1)..end];

            if (kind.Equals("GridRowHeader", StringComparison.OrdinalIgnoreCase))
            {
                var header = new DnnGridHeader();
                foreach (var groups in SafeRegex.Matches(cell, @"<th\b([^>]*)>([\s\S]*?)</th>",
                             RegexOptions.IgnoreCase).Select(th => th.Groups))
                {
                    var wm = SafeRegex.Match(groups[1].Value, @"width\s*:\s*([\d.]+)px",
                        RegexOptions.IgnoreCase);
                    header.Cells.Add((DnnPlainText(groups[2].Value),
                        wm.Success ? DtpNum(wm.Groups[1].Value) : 100));
                }
                if (header.Cells.Count > 0) blocks.Add(header);
                continue;
            }

            // record: summary ItemTable row + details panel
            var content = new List<IDnnBlock>();
            var itemTr = SafeRegex.Match(cell,
                @"<table\b[^>]*_ItemTable[^>]*>[\s\S]*?<tr\b[^>]*>([\s\S]*?)</tr>",
                RegexOptions.IgnoreCase);
            var rest = cell;
            if (itemTr.Success)
            {
                var summary = new DnnGridRow();
                foreach (var tcGroups in SafeRegex.Matches(itemTr.Groups[1].Value,
                             @"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase).Select(tc => tc.Groups))
                {
                    var wm = SafeRegex.Match(tcGroups[1].Value, @"width\s*:\s*([\d.]+)px",
                        RegexOptions.IgnoreCase);
                    summary.Cells.Add((DnnPlainText(DnnStripHidden(tcGroups[2].Value)),
                        wm.Success ? DtpNum(wm.Groups[1].Value) : 100));
                }
                content.Add(summary);
                var tblEnd = DnnTagEnd(cell,
                    SafeRegex.Match(cell, @"<table\b[^>]*_ItemTable[^>]*>", RegexOptions.IgnoreCase).Index,
                    "table");
                if (tblEnd >= 0) rest = cell[(cell.IndexOf('>', tblEnd) + 1)..];
            }
            DnnWalk(rest, indentPx, content);
            double h = 0;
            foreach (var b in content)
                h += b switch
                {
                    DnnGap g => g.H,
                    DnnFieldRow r => DnnRowLinePt + (r.Bare ? 5 * 0.75 : 0),
                    DnnBand => DnnBandPt,
                    DnnGridRow => DnnGridSummaryHPt,
                    DnnParaLine p => p.Pitch,
                    _ => 0.0,
                };
            blocks.Add(new DnnGridFill
            {
                H = h,
                White = kind.Equals("GridRowOdd", StringComparison.OrdinalIgnoreCase),
                FullWidth = !hasHeader,
            });
            blocks.AddRange(content);
        }
    }

    /// <summary>Remove PrintHidden / display:none subtrees.</summary>
    private static string DnnStripHidden(string html)
    {
        for (var guard = 0; guard < 32; guard++)
        {
            var m = SafeRegex.Match(html,
                @"<(div|span|a)\b[^>]*(?:PrintHidden|display\s*:\s*none)[^>]*>",
                RegexOptions.IgnoreCase);
            if (!m.Success) break;
            var end = DnnTagEnd(html, m.Index, m.Groups[1].Value);
            if (end < 0) break;
            var close = html.IndexOf('>', end);
            html = html.Remove(m.Index, close + 1 - m.Index);
        }
        return html;
    }

    /// <summary>Index just before the matching close tag of the element opening
    /// at <paramref name="openIdx"/>.</summary>
    private static int DnnTagEnd(string html, int openIdx, string tag)
    {
        var end = html.IndexOf('>', openIdx);
        if (end < 0) return -1;
        var depth = 1;
        var rx = new Regex(@"<(/?)" + tag + @"\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);
        var scan = end + 1;
        while (depth > 0)
        {
            var m = SafeRegex.Match(rx, html, scan);
            if (!m.Success) return -1;
            depth += m.Groups[1].Value.Length > 0 ? -1 : 1;
            if (depth == 0) return m.Index;
            scan = m.Index + m.Length;
        }
        return -1;
    }

    /// <summary>Visible text of a control cell: selected option of a select,
    /// value of a text input, the checked radio's label, or the inline text;
    /// an empty control renders as an underscore.</summary>
    private static string DnnControlValue(string inner)
    {
        var sel = SafeRegex.Match(inner,
            @"<option[^>]*\bselected\b[^>]*>([^<]*)</option>", RegexOptions.IgnoreCase);
        if (sel.Success) return EdgarHtmlRenderer.DecodeEntities(sel.Groups[1].Value).Trim();
        var chk = SafeRegex.Match(inner,
            @"<input[^>]*\bchecked\b[^>]*>\s*<label[^>]*>([^<]*)</label>", RegexOptions.IgnoreCase);
        if (chk.Success) return EdgarHtmlRenderer.DecodeEntities(chk.Groups[1].Value).Trim();
        var inp = SafeRegex.Match(inner, @"<input[^>]*>", RegexOptions.IgnoreCase);
        if (inp.Success && DtpAttr(inp.Value, "type") is not "hidden")
        {
            var v = DtpAttr(inp.Value, "value");
            if (!string.IsNullOrWhiteSpace(v)) return EdgarHtmlRenderer.DecodeEntities(v).Trim();
        }
        var t = DnnPlainText(inner);
        return t.Length > 0 ? t : "_";
    }

    /// <summary>Tag-stripped, entity-decoded, whitespace-collapsed text.</summary>
    private static string DnnPlainText(string inner)
    {
        var t = SafeRegex.Replace(inner, @"<[^>]+>", " ");
        t = EdgarHtmlRenderer.DecodeEntities(t);
        return SafeRegex.Replace(t, @"\s+", " ").Trim();
    }
}
