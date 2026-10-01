using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // Flex-grid block parsing helpers: a CSS percent as a fraction, and whether a
    // border side is drawn.
    private static double PctFrac(string? v)
    {
        if (v is null) return 0;
        var m = Regex.Match(v, @"([\d.]+)\s*%");
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var p) ? p / 100.0 : 0;
    }

    private static bool BorderSideDrawn(HtmlNode n, string side,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css2)
    {
        var v = DomDecl(n, side, css2);
        return v is not null && !v.Contains("none", StringComparison.OrdinalIgnoreCase)
               && !v.TrimStart().StartsWith("0", StringComparison.Ordinal);
    }

    /// <summary>A definition's dd: a full-width value keeps its pixel padding and its left- and right-floated spans, else the plain text.</summary>
    private static void ParseFlexCellValue(FlexGridCell fc, HtmlNode c, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        fc.HasDd = true;
        if (DomDecl(c, "width", css)?.Trim() == "100%")
        {
            fc.ValueWide = true;
            var pm = Regex.Match(DomDecl(c, "padding", css) ?? "",
                @"([\d.]+)\s*px");
            if (pm.Success && double.TryParse(pm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var vpp)) fc.ValuePadPx = vpp;
            foreach (var t in c.Children)
            {
                if (t.Tag != "span") continue;
                var fl = DomDecl(t, "float", css)?.Trim();
                if (fl?.Equals("left", StringComparison.OrdinalIgnoreCase) == true)
                    fc.ValueLeft = DomText(t, css);
                else if (fl?.Equals("right", StringComparison.OrdinalIgnoreCase) == true)
                {
                    fc.ValueRight = DomText(t, css);
                    fc.ValueRightMrFrac = PctFrac(DomDecl(t, "margin-right", css));
                }
            }
        }
        else fc.Value = DomText(c, css);
    }

    /// <summary>A definition's dt: its text, with a right-floated span kept as the right label and its margin.</summary>
    private static void ParseFlexCellTerm(FlexGridCell fc, HtmlNode c, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var lbl = new StringBuilder();
        foreach (var t in c.Children)
        {
            if (t.Tag.Length == 0) lbl.Append(DecodeEntities(t.Text));
            else if (t.Tag == "span"
                && DomDecl(t, "float", css)?.Trim()
                    .Equals("right", StringComparison.OrdinalIgnoreCase) == true)
            {
                fc.LabelRight = DomText(t, css);
                fc.LabelRightMrFrac = PctFrac(DomDecl(t, "margin-right", css));
            }
            else lbl.Append(DomText(t, css));
        }
        fc.Label = CollapseWs(lbl.ToString()).Trim();
    }

    /// <summary>Parses one flex cell: its width and padding fractions, alignment and drawn border sides, then either its plain text or its definition list.</summary>
    private static bool ParseFlexGridCell(FlexGridRow fr, HtmlNode cell, IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        if (cell.Tag is not ("div" or "td")) return true;
        var fc = new FlexGridCell
        {
            WFrac = PctFrac(DomDecl(cell, "width", css)),
            PadFrac = PctFrac(DomDecl(cell, "padding-left", css)),
            Center = DomDecl(cell, "text-align", css)?.Contains("center",
                StringComparison.OrdinalIgnoreCase) == true,
        };
        if (fc.WFrac <= 0) return true;
        fc.BL = BorderSideDrawn(cell, "border-left", css);
        fc.BR = BorderSideDrawn(cell, "border-right", css);
        fc.BT = BorderSideDrawn(cell, "border-top", css);
        fc.BB = BorderSideDrawn(cell, "border-bottom", css);
        HtmlNode? dl = null;
        foreach (var c in cell.Children) if (c.Tag == "dl") { dl = c; break; }
        if (dl is null)
        {
            fc.PlainWrap = true;
            fc.Label = DomText(cell, css);
        }
        else
        {
            foreach (var c in dl.Children)
            {
                if (c.Tag == "dt")
                {
                    ParseFlexCellTerm(fc, c, css);
                }
                else if (c.Tag == "dd")
                {
                    ParseFlexCellValue(fc, c, css);
                }
            }
        }
        fr.Cells.Add(fc);
        return true;
    }
}
