using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The INFOPATH FORM: an InfoPath-exported application form whose body is one fixed-layout
// `table.xdFormLayout` (every table `table-layout: fixed` with `<col>` px widths, borders
// collapsed) of three bands - the dark title band with the logo/title table and two lines of
// controls, the grey 12-column field grid holding nested xdLayout tables, repeating sections
// and msoUcTable grids, and the Submit band - in Verdana 10pt with InfoPath's control
// classes: xdComboBox selects, xdTextBox/xdRichTextBox inline-block boxes, xdDTPicker date
// pickers with their xdDTButton, xdHyperlinkBox, Boolean inputs, optionalPlaceholder lines.
// Nothing wraps: every block is one line of inline boxes, and a row is as tall as the
// tallest cell line. The reference converter reads the XHTML's self-closed `<span …/>` and
// `<font …/>` as OPEN tags (a self-closed textbox swallows its following siblings), paints
// every tr / tbody background with its right and bottom edges as width and height, ignores
// the malformed `border:1ptsolid#dcdcdc` of the boxes, and sizes each control by the laws
// below. Constants not read from the markup are measured on the licensed render.
internal static partial class HtmlToPdfConverter
{
    private const double IpPxPt = 0.75;
    private const double IpMarginX = 90.0;                  // the UA sheet's side margins
    private const double IpMarginTop = 72.0;
    private const double IpBodyMargin = UaBodyMarginPt;    // the UA body margin
    private const double IpFs = 10.0;                       // the sheet's 10pt body
    private const double IpCellPad = 0.75;                  // the border=1 default 1px cell padding
    private const double IpBorder = 1.0;                    // every 1pt collapsed cell border
    private const double IpHalfBorder = 0.5;
    private const double IpFaceAsc = 1.005;                 // Verdana OS/2 win ascent (em)
    private const double IpFaceSum = 1.215;                 // …ascent + descent
    private const double IpGothicAsc = 0.859;               // MS UI Gothic / MS Gothic
    private const double IpGothicSum = 1.0;
    private const double IpCjkAdvanceEm = 1.0;              // a full-width glyph advances one em
    private const double IpSpaceEm = 0.352;                 // the Verdana space
    private const double IpBoxMargin = 0.75;                // .xdTextBox / .xdComboBox / .xdDTPicker margin: 1px
    private const double IpBoxPad = 0.75;                   // .xdTextBox padding: 1px
    private const double IpSelectBoxH = 16.65;              // a select's inline box (12.15 content + 4.5)
    private const double IpSelectAbove = 12.3;              // …its baseline below the box top
    private const double IpSelectFillOutset = 0.25;         // the white fill overhangs the box top and bottom
    private const double IpSelectFillInset = 1.0;           // …and starts a px inside the pen (margin + 0.25)
    private const double IpSelectFillTrim = 0.5;            // the fill is half a pt narrower than the declared width
    private const double IpSelectStrokeInset = 0.5;         // the 1pt black rect inside the fill
    private const double IpSelectTextX = 2.0;               // the value's pen from the fill's left
    private const double IpSelectTextDrop = 11.73;          // …and its baseline from the box top
    private const double IpSelectCjkExtra = 0.72;           // a CJK value tallens the fill
    private const double IpSelectCjkDrop = 1.05;            // …and lowers its baseline
    private const double IpInputBoxSize = 9.75;                 // a radio / checkbox inline box (13px)
    private const double IpInputMarginTop = 2.25;           // the UA 3px input margins
    private const double IpInputMarginRight = 2.25;
    private const double IpRadioMarginLeft = 3.75;          // radio 5px
    private const double IpCheckMarginLeft = 3.0;           // checkbox 4px
    private const double IpCheckMarginBottom = 2.25;        // checkbox 3px (a radio has none)
    private const double IpCheckSquare = 7.75;              // the white square drawn inside the box
    private const double IpRadioRadius = 4.375;             // the 1pt ring (its path; the ink runs 3.875..4.875)
    private const double IpRadioDotRadius = 2.0;
    private const double IpButtonPadX = 6.0;                // a push button's text inset
    private const double IpButtonPadTop = 2.25;             // …and its line's top inset
    private const double IpButtonFillH = 16.5;              // an unsized button: the 12 line + 2 × 2.25
    private const double IpButtonRectX = 2.0;               // the 1pt black rect outside the fill, sides
    private const double IpButtonRectY = 1.5;               // …top and bottom
    private const double IpButtonBevelW = 1.5;              // the #a9a9a9 bevel strokes
    private const double IpButtonBevelInset = 0.75;
    private const string IpButtonFillRgb = "0.941 0.941 0.941";   // #f0f0f0
    private const string IpButtonBevelRgb = "0.663 0.663 0.663";  // #a9a9a9
    private const double IpDtButtonW = 15.0;                // .xdDTButton width: 20px
    private const double IpDtButtonH = 12.75;               // …height: 17px
    private const double IpDtButtonAbove = 10.5;            // its box above the baseline
    private const double IpDtButtonBelow = 2.25;
    private const double IpDtButtonGap = 0.75;              // the px before a sibling button
    private const double IpDtSwallowedW = 15.75;            // a self-closed xdDTText span that swallowed its button
    private const double IpHyperPad = 3.75;                 // .xdHyperlinkBox padding: 5px
    private const double IpHyperBorder = 1.0;               // …border: 1pt solid #dcdcdc
    private const string IpHyperRgb = "0.863 0.863 0.863";
    private const double IpPlaceholderPad = 15.0;           // .optionalPlaceholder padding-left: 20px
    private const double IpPlaceholderFs = 7.5;             // …font-size: x-small
    private const string IpPlaceholderRgb = "0.2 0.2 0.2";  // …color: #333333
    private const double IpBrokenIcon = 32.0;               // the broken-image icon
    private const double IpBrokenFrame = 1.0;               // …and the bevel frame round the declared box
    private const double IpBrokenIconInset = 1.0;
    private const string IpBevelDarkRgb = "0.333 0.333 0.333";
    private const string IpBevelLightRgb = "0.667 0.667 0.667";
    private const string IpVeryDarkTextRgb = "0.922 0.941 0.976";   // .primaryVeryDark color: #ebf0f9
    private const string IpFace = "Verdana";
    private const string IpBoldFace = "Verdana-Bold";
    private const string IpGothicFace = "MSUIGothic";
    private const string IpCjkFace = "MSGothic";
    private const string IpFaceRes = "F12";
    private const string IpBoldRes = "F13";
    private const string IpGothicRes = "F14";
    private const string IpCjkRes = "F15";
    private const double IpBoundary = 1e-6;

    /// <summary>Per-call working state of the InfoPath render. One instance per invocation; never shared.</summary>
    private sealed class InfoPathState
    {
        public System.Globalization.CultureInfo invc = null!;
        public Document doc = null!;
        public Page page = null!;
        public StringBuilder fills = new();                 // the backgrounds, painted first
        public StringBuilder sb = new();                    // borders, widgets and text after
        public double pageW, pageH;
        public IpNode body = null!;
        public Dictionary<IpNode, double> heights = new();  // measured content heights per block container
        public Dictionary<IpNode, IpLine> lines = new();    // built lines per line container
    }

    /// <summary>Render the InfoPath form, or null when the page does not carry the idiom's markup.</summary>
    private static Document? TryRenderInfoPathForm(ConvertState cv)
    {
        var html = cv.html;
        if (!Regex.IsMatch(html, "<table\\b[^>]*class\\s*=\\s*['\"][^'\"]*\\bxdFormLayout\\b", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, "\\bxdComboBox\\b", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, "\\bxdDTPicker\\b", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(html, "\\boptionalPlaceholder\\b", RegexOptions.IgnoreCase))
            return null;
        if (WinMetricsFor(IpFace) is null) return null;
        var bodyOpen = Regex.Match(html, "<body\\b[^>]*>", RegexOptions.IgnoreCase);
        var body = IpParse(bodyOpen.Success ? html[(bodyOpen.Index + bodyOpen.Length)..] : html);
        var form = IpFindForm(body);
        if (form is null) return null;
        var tableW = IpPx(form.Css("width"));
        if (tableW <= 0) return null;
        var ip = new InfoPathState
        {
            invc = System.Globalization.CultureInfo.InvariantCulture,
            body = body,
            pageW = IpMarginX + IpBodyMargin + tableW + IpMarginX,
            pageH = cv.pageHeight,
        };
        ip.doc = new Document();
        ip.page = ip.doc.Pages.Add(ip.pageW, ip.pageH);
        EnsureFonts(ip.page);
        EnsureFont(ip.page, IpFace, IpFaceRes);
        EnsureFont(ip.page, IpBoldFace, IpBoldRes);
        EnsureFont(ip.page, IpGothicFace, IpGothicRes);
        EnsureFont(ip.page, IpCjkFace, IpCjkRes);
        IpLayoutTable(ip, form, IpMarginX + IpBodyMargin, IpMarginTop + IpBodyMargin, tableW, true);
        ip.page.AddContentStream(Encoding.ASCII.GetBytes(ip.fills.ToString() + ip.sb.ToString()));
        return ip.doc;
    }

    private static IpNode? IpFindForm(IpNode node)
    {
        if (node.Tag == "table" && node.HasClass("xdFormLayout")) return node;
        foreach (var c in node.Children)
            if (!c.IsText && IpFindForm(c) is { } found) return found;
        return null;
    }

    /// <summary>A px length in pt; 0 when absent or not px.</summary>
    private static double IpPx(string value)
    {
        var m = Regex.Match(value, "^\\s*(-?\\d+(?:\\.\\d+)?)\\s*px\\s*$", RegexOptions.IgnoreCase);
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * IpPxPt : 0;
    }

    /// <summary>A percent width against a box; 0 when the value is not a percent.</summary>
    private static double IpPct(string value, double of)
    {
        var m = Regex.Match(value, "^\\s*(\\d+(?:\\.\\d+)?)\\s*%\\s*$");
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0 * of : 0;
    }

    /// <summary>The metric line box of a size in a face (px-rounded win height).</summary>
    private static double IpLineH(double fs, bool gothic = false) => MetricLineHeight(fs, gothic ? IpGothicSum : IpFaceSum);

    /// <summary>The strut's extent above the baseline: half-leading plus the ascent.</summary>
    private static double IpAbove(double fs, bool gothic = false)
        => MetricBaselineDrop(fs, IpLineH(fs, gothic), gothic ? (IpGothicAsc, IpGothicSum) : (IpFaceAsc, IpFaceSum));

    private static double IpBelow(double fs, bool gothic = false) => IpLineH(fs, gothic) - IpAbove(fs, gothic);

    private static double IpMeasure(string face, string text, double fs) => text.Length == 0 ? 0 : MeasureFaceText(IpMeasureFace(face), text, fs);

    private static string IpMeasureFace(string face) => face switch
    {
        IpBoldFace => "Verdana-Bold",
        IpGothicFace => "MS UI Gothic",
        IpCjkFace => "MS Gothic",
        _ => "Verdana",
    };

    private static string IpRes(string face) => face switch
    {
        IpBoldFace => IpBoldRes,
        IpGothicFace => IpGothicRes,
        IpCjkFace => IpCjkRes,
        _ => IpFaceRes,
    };

    private static bool IpHidden(IpNode el) => el.Css("display").Equals("none", StringComparison.OrdinalIgnoreCase);

    private static bool IpIsCjk(string text)
    {
        foreach (var ch in text) if (ch >= 0x2E80 && ch <= 0x9FFF || ch >= 0xFF00 && ch <= 0xFFEF) return true;
        return false;
    }

    /// <summary>A CSS colour as a PDF rgb triple, or null.</summary>
    private static string? IpRgb(string css)
    {
        if (ParseCssColor(css) is not { } c) return null;
        return Compat.Format(System.Globalization.CultureInfo.InvariantCulture, $"{c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###}");
    }

    /// <summary>The `font size=N` / CSS keyword size of an element in pt, or 0 when it sets none.</summary>
    private static double IpFontSizeOf(IpNode el)
    {
        var css = el.Css("font-size");
        if (css.Length > 0)
        {
            switch (css.ToLowerInvariant())
            {
                case "xx-small": return 6.75;
                case "x-small": return 7.5;
                case "small": return 9.75;
                case "medium": return 12.0;
                case "large": return 13.5;
            }
            if (IpPx(css) > 0) return IpPx(css);
            var pt = Regex.Match(css, "^(\\d+(?:\\.\\d+)?)\\s*pt$", RegexOptions.IgnoreCase);
            if (pt.Success) return double.Parse(pt.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (el.Tag == "font" && el.Attr("size") is { Length: > 0 } size)
            return size.Trim() switch { "1" => 7.5, "2" => 9.75, "3" => 12.0, "4" => 13.5, "5" => 18.0, "6" => 24.0, "7" => 36.0, _ => 0 };
        if (el.HasClass("optionalPlaceholder")) return IpPlaceholderFs;
        return 0;
    }
}
