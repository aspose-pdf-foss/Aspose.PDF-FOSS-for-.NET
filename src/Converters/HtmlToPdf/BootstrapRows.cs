using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The container-less Bootstrap ROWS page (the routing-slip shape): body-level
// .row grids of col-xs-N label/value columns, panel-success cards whose heading
// rows carry bold labels over values, hr rules and centred type — laid out on
// this box model: 90/72 pt margins, the Site.css 20px body side
// padding, .row negative margins expanding 15px back into the padding, columns
// at their percent of the expanded row plus the 15px column padding, adjacent
// block margins MAX-collapsing, and the Bootstrap panel chrome (10px/15px
// heading padding, 15px body padding, the success palette). All constants
// are measured values; the grid positions reproduce the expected output exactly
// (columns at 117 / 264.1 / 411.2 / 558.4 on the 800 pt sheet).
internal static partial class HtmlToPdfConverter
{
    private const double BrMarginX = 90.0;
    private const double BrMarginY = 72.0;
    private const double BrBodyPadX = 15.0;        // Site.css body padding 20px
    private const double BrLineH = 15.0;           // 14px × 1.42857 = 20px
    private const double BrFontPt = 10.5;          // 14px
    private const double BrH3FontPt = 18.0;        // 24px
    private const double BrH3LineH = 19.8;         // 24px × 1.1 = 26.4px
    private const double BrH3MarginTop = 15.0;     // 20px
    private const double BrH3MarginBottom = 7.5;   // 10px
    private const double BrHrMargin = 15.0;        // hr margin 20px 0
    private const double BrRowExpand = 11.25;      // .row margin -15px
    private const double BrColPad = 11.25;         // column padding 15px
    private const double BrPanelMb = 15.0;         // .panel margin-bottom 20px
    private const double BrHeadPadY = 7.5;         // panel-heading padding 10px
    private const double BrBodyPad = 11.25;        // panel-body padding 15px
    private const double BrPMb = 7.5;              // p margin-bottom 10px
    // the Bootstrap success palette + chrome inks (theme constants)
    private static readonly Color BrText = Color.FromRgbBytes(0x33, 0x33, 0x33);
    private static readonly Color BrLink = Color.FromRgbBytes(0x33, 0x7a, 0xb7);
    private static readonly Color BrHrInk = Color.FromRgbBytes(0xee, 0xee, 0xee);
    private static readonly Color BrPanelBorder = Color.FromRgbBytes(0xd6, 0xe9, 0xc6);
    private static readonly Color BrHeadBg = Color.FromRgbBytes(0xdf, 0xf0, 0xd8);
    private static readonly Color BrHeadFg = Color.FromRgbBytes(0x3c, 0x76, 0x3d);

    // ── the EDGE-TO-EDGE SEGOE ALERT sheet (the `.top_label/.grid_header`
    // class namespace at zero margins) ──
    // A Segoe UI e-mail alert: the banner line and its UA hr, two label/value
    // panels (9pt grey right-aligned labels against 10.5pt bold values on a
    // shared baseline; the right panel's label centres on its wrapped value
    // block), the broken vehicle-image frame with the browser placeholder, and
    // the sensor grid — six measured columns, grey centred headers over their
    // 2px underline, bold centred values, the 45px red highlight cells with
    // white ink, and the red 'No Sensor' rows. Every constant measured on the
    // reference.
    private const double SaBodyX = 6.0;            // the UA body margin at zero margins
    private const double SaBannerBaselinePt = 18.85;
    private const double SaHrY1 = 28.1;            // the UA hr's black edge…
    private const double SaHrY2 = 28.9;            // …over its #555 shadow
    private const double SaLeftLabelRight = 303.0;
    private const double SaLeftValueX = 310.5;
    private const double SaRightLabelRight = 733.3;
    private const double SaRightValueX = 740.8;
    private const double SaPanelBase0 = 57.6;      // first shared baseline
    private const double SaPanelPitch = 17.45;     // left-panel row pitch
    private const double SaValuePitch = 15.95;     // wrapped right-value pitch
    private const double SaValueWrapW = 145.0;     // the value column wrap box (200px − pads)
    private const double SaGridHeadTop = 342.0;    // header glyph top
    private const double SaGridRuleY = 358.5;      // the 2px header underline
    private const double SaGridRow0Top = 370.1;    // first value glyph top
    private const double SaGridRowPitch1 = 35.3;   // rows 1-2 (highlight rows)
    private const double SaGridRowPitch2 = 33.7;   // the sensor-less rows
    private const double SaRedTop0 = 359.2;        // first highlight fill top
    private const double SaRedH = 33.8;            // 45px cell
    private const double SegoeAscEm = 1.079;       // Segoe UI hhea ascent

    private static readonly double[] SaColEdges =
        { 57.8, 133.5, 307.7, 482.0, 656.2, 830.4, 1005.4 };

    private static Document? TryRenderSegoeAlert(string html, IReadOnlyDictionary<string, Dictionary<string, string>> css, double pageWidth, double pageHeight)
    {
        var sg = new SegoeAlertRenderState();
        sg.html = html;
        sg.css = css;
        sg.pageWidth = pageWidth;
        sg.pageHeight = pageHeight;
        if (!sg.css.ContainsKey(".top_label") || !sg.css.ContainsKey(".grid_header")
            || !sg.css.ContainsKey(".grid_highlight_value")
            || !Regex.IsMatch(sg.html, @"class\s*=\s*[""']left_panel[""']", RegexOptions.IgnoreCase))
            return null;
        sg.segoe = Text.SystemFontResolver.Resolve("Segoe UI");
        // the resolver maps PDF-style names — the bold face answers to the
        // hyphenated form
        sg.segoeBold = Text.SystemFontResolver.Resolve("SegoeUI-Bold")
            ?? Text.SystemFontResolver.Resolve("Segoe UI Bold");
        if (sg.segoe is null || sg.segoeBold is null) return null;
        sg.invc = System.Globalization.CultureInfo.InvariantCulture;
        sg.grey = Color.FromRgbBytes(0x59, 0x59, 0x59);
        sg.red = Color.FromRgbBytes(0xCF, 0x31, 0x35);
        sg.white = Color.FromArgb(255, 255, 255);
        sg.black = Color.FromArgb(0, 0, 0);

        sg.doc = new Document();
        sg.page = sg.doc.Pages.Add(sg.pageWidth, sg.pageHeight);
        EnsureFonts(sg.page);

        sg.bodyM = Regex.Match(sg.html, @"<body\b[^>]*>\s*(?<t>[^<]+)", RegexOptions.IgnoreCase);
        if (sg.bodyM.Success && SegoeFlat(sg, sg.bodyM.Groups["t"].Value).Length > 0)
            SegoeRun(sg, false, 12, SaBodyX, SaBannerBaselinePt, SegoeFlat(sg, sg.bodyM.Groups["t"].Value), sg.black);
        SegoeLine(sg, SaBodyX, sg.pageWidth - SaBodyX, SaHrY1, 0.75, sg.black);
        SegoeLine(sg, SaBodyX, sg.pageWidth - SaBodyX, SaHrY2, 0.75, Color.FromRgbBytes(0x55, 0x55, 0x55));

        SegoePanel(sg, "left_panel", SaLeftLabelRight, SaLeftValueX);
        SegoePanel(sg, "right_panel", SaRightLabelRight, SaRightValueX);

        // The vehicle cid: image leaves NOTHING — neither a frame nor a
        // placeholder is drawn for it (the area is bare white on the
        // template), it only holds the vertical space.

        sg.gm = Regex.Match(sg.html,
            @"<table\b[^>]*border-collapse[^>]*>(?<b>[\s\S]*?)$", RegexOptions.IgnoreCase);
        if (sg.gm.Success)
        {
            RenderSegoeAlertGrid(sg);
        }
        return sg.doc;
    }

    /// <summary>Index of the matching close for the div whose open tag ends at
    /// <paramref name="afterOpen"/> (balanced by div depth).</summary>
    private static int FindDivClose(string html, int afterOpen)
    {
        var depth = 1;
        foreach (Match t in Regex.Matches(html[afterOpen..], @"<div\b|</div\s*>",
            RegexOptions.IgnoreCase))
        {
            depth += t.Value.StartsWith("</") ? -1 : 1;
            if (depth == 0) return afterOpen + t.Index;
        }
        return html.Length;
    }
}
