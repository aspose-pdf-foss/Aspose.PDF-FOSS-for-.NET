using System.Globalization;
using System.Xml;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

internal static partial class XmlBinding
{
    private static List<float> ParseFloatList(string? value)
    {
        var coords = new List<float>();
        foreach (var p in (value ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                coords.Add(v);
        return coords;
    }

    /// <summary>The XML templates spell the id attribute both <c>id</c> and
    /// <c>Id</c> (templates use <c>Id</c> on Image and <c>id</c> on Page).</summary>
    private static string? GetId(XmlNode node)
        => GetAttr(node, "id") ?? GetAttr(node, "Id");

    private static bool HasElementChild(XmlNode node, string localName)
    {
        foreach (XmlNode child in node.ChildNodes)
            if (child.NodeType == XmlNodeType.Element && child.LocalName == localName)
                return true;
        return false;
    }

    private static XmlNode? FirstElementChild(XmlNode node, string localName)
    {
        foreach (XmlNode child in node.ChildNodes)
            if (child.NodeType == XmlNodeType.Element && child.LocalName == localName)
                return child;
        return null;
    }

    // Parse a <TextState>/<DefaultCellTextState> element into a new TextState,
    // seeded from an existing one so unspecified properties are preserved.
    private static TextState ParseTextState(XmlNode node, TextState? seed)
    {
        var ts = seed ?? new TextState();
        ApplyTextState(node, ts);
        return ts;
    }

    private static void ApplyTextState(XmlNode node, TextState ts)
    {
        var font = GetAttr(node, "Font");
        if (!string.IsNullOrEmpty(font))
        {
            ts.FontName = font;
            if (font.Contains("Bold", StringComparison.OrdinalIgnoreCase)) ts.IsBold = true;
        }
        var size = GetAttrLength(node, "FontSize");
        if (size > 0) ts.FontSize = (float)size;
        var fg = ParseColorValue(GetAttr(node, "ForegroundColor"));
        if (fg is not null) ts.ForegroundColor = fg;
        var ls = GetAttrLength(node, "LineSpacing", -1);
        if (ls >= 0) ts.LineSpacing = (float)ls;
    }

    /// <summary>Horizontal alignment attribute: the schema takes both the enum
    /// NAMES and their numeric values (templates author Alignment="3" = Right).</summary>
    private static HorizontalAlignment? ParseHAlign(string? value) => value switch
    {
        "Left" or "1" => HorizontalAlignment.Left,
        "Center" or "2" => HorizontalAlignment.Center,
        "Right" or "3" => HorizontalAlignment.Right,
        "Justify" or "4" => HorizontalAlignment.Justify,
        "FullJustify" or "5" => HorizontalAlignment.FullJustify,
        _ => null,
    };

    private static VerticalAlignment? ParseVerticalAlignment(string? value) => value switch
    {
        "Center" => VerticalAlignment.Center,
        "Bottom" => VerticalAlignment.Bottom,
        "Top" => VerticalAlignment.Top,
        _ => null,
    };

    private static string ExtractTextFromFragment(XmlNode fragNode)
    {
        var sb = new System.Text.StringBuilder();
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "TextSegment")
            {
                // Get text content (non-element text nodes)
                foreach (XmlNode textChild in child.ChildNodes)
                {
                    if (textChild.NodeType == XmlNodeType.Text ||
                        textChild.NodeType == XmlNodeType.CDATA)
                        sb.Append(DecodeXmlEntities(textChild.Value ?? ""));
                }
            }
        }
        // Normalise display whitespace the way the layout engine does: an
        // XSLT-produced literal (e.g. "\n   *Gauge placeholder*\n ") carries the
        // stylesheet's indentation, which would otherwise render as a leading
        // blank line / trailing space line and knock the cell text out of
        // vertical alignment with its neighbours. Collapse runs to one space.
        // Text authored WITHOUT line structure keeps its spacing verbatim — a
        // segment's deliberate trailing space ("Table of content ") survives.
        var raw = sb.ToString();
        if (raw.IndexOfAny(new[] { '\n', '\r', '\t' }) < 0) return raw;
        return System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ").Trim();
    }

    // The Aspose XML schema stores an HtmlFragment's body two ways: as a
    // direct text/CDATA child of <HtmlFragment>, or wrapped in a nested
    // <HtmlContent>…</HtmlContent> element. Either shape is valid input;
    // we flatten both into a single string that the Table/HtmlFragment
    // rendering path can strip via HtmlFragment.StripHtmlTags.
    private static string ExtractHtmlContent(XmlNode fragNode)
    {
        var sb = new System.Text.StringBuilder();
        foreach (XmlNode child in fragNode.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Text || child.NodeType == XmlNodeType.CDATA)
            {
                sb.Append(DecodeXmlEntities(child.Value ?? ""));
            }
            else if (child.NodeType == XmlNodeType.Element && child.LocalName == "HtmlContent")
            {
                foreach (XmlNode tc in child.ChildNodes)
                {
                    if (tc.NodeType == XmlNodeType.Text || tc.NodeType == XmlNodeType.CDATA)
                        sb.Append(DecodeXmlEntities(tc.Value ?? ""));
                }
            }
        }
        return sb.ToString();
    }

    private static string DecodeXmlEntities(string text)
    {
        // Decode Aspose-style XML character references: _x0027_ → '
        return System.Text.RegularExpressions.Regex.Replace(
            text, @"_x([0-9A-Fa-f]{4})_",
            m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString());
    }

    private static MarginInfo ParseMargin(XmlNode node)
    {
        return new MarginInfo
        {
            Left = GetAttrLength(node, "Left"),
            Right = GetAttrLength(node, "Right"),
            Top = GetAttrLength(node, "Top"),
            Bottom = GetAttrLength(node, "Bottom"),
        };
    }

    // Parse a <Border>/<DefaultCellBorder> element. Its children name the sides
    // (All/Box or Top/Bottom/Left/Right), each carrying a LineWidth and optional
    // Color. The result feeds BorderInfo.Side (which sides to draw) plus per-side
    // widths when the sides differ.
    private static BorderInfo ParseBorder(XmlNode node)
    {
        var border = new BorderInfo { Side = BorderSide.None };
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var width = GetAttrLength(child, "LineWidth", 1);
            var color = ParseColorValue(GetAttr(child, "Color"));
            // A doubled side draws a second rule outside the box and claims the room
            // for it, so the flag is part of the border's geometry and must round-trip.
            var doubled = string.Equals(GetAttr(child, "IsDoubled"), "true",
                StringComparison.OrdinalIgnoreCase);
            switch (child.LocalName)
            {
                case "All":
                case "Box":
                    border.Side |= BorderSide.Box;
                    border.Width = width;
                    if (color is not null) border.Color = color;
                    break;
                case "Top":
                    border.Side |= BorderSide.Top;
                    border.Width = width;
                    border.Top = new GraphInfo { LineWidth = (float)width, Color = color, IsDoubled = doubled };
                    if (color is not null) border.Color = color;
                    break;
                case "Bottom":
                    border.Side |= BorderSide.Bottom;
                    border.Width = width;
                    border.Bottom = new GraphInfo { LineWidth = (float)width, Color = color, IsDoubled = doubled };
                    if (color is not null) border.Color = color;
                    break;
                case "Left":
                    border.Side |= BorderSide.Left;
                    border.Width = width;
                    border.Left = new GraphInfo { LineWidth = (float)width, Color = color, IsDoubled = doubled };
                    if (color is not null) border.Color = color;
                    break;
                case "Right":
                    border.Side |= BorderSide.Right;
                    border.Width = width;
                    border.Right = new GraphInfo { LineWidth = (float)width, Color = color, IsDoubled = doubled };
                    if (color is not null) border.Color = color;
                    break;
            }
        }
        return border;
    }

    // Parse a colour attribute: a #rrggbb / #aarrggbb hex string, or a named
    // colour (Aspose XML also allows names like "Black"/"Gray"). Returns null
    // when the attribute is absent so callers leave the property unset.
    private static Color? ParseColorValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith('#'))
            return Color.Parse(value);
        try
        {
            var named = System.Drawing.Color.FromName(value);
            if (named.A != 0 || string.Equals(value, "Transparent", StringComparison.OrdinalIgnoreCase))
                return Color.FromRgbBytes(named.R, named.G, named.B);
        }
        catch { /* fall through */ }
        return null;
    }

    private static string? GetAttr(XmlNode node, string name)
        => node.Attributes?[name]?.Value;

    private static double GetAttrDouble(XmlNode node, string name, double defaultValue = 0)
    {
        var val = GetAttr(node, name);
        return val is not null && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : defaultValue;
    }

    // Length attribute parser that honours Aspose XML unit suffixes
    // (cm/mm/in/pt/px). A bare number is points. Used for page/table dimensions
    // and margins so e.g. Top="2.6cm" converts to 73.7 pt rather than parsing
    // to 0 (double.TryParse fails on the suffix).
    private static double GetAttrLength(XmlNode node, string name, double defaultValue = 0)
        => ParseLength(GetAttr(node, name), defaultValue);

    private static double ParseLength(string? val, double defaultValue = 0)
    {
        if (string.IsNullOrWhiteSpace(val)) return defaultValue;
        val = val.Trim();
        double factor = 1.0; // points
        string num = val;
        // Longest suffix first: "inch" must match before its "in" prefix would.
        // cm/mm use the classic XML binder's own factor of 28.7 pt/cm, not the
        // exact 72/2.54 — measured (a 1.5cm page
        // margin plus an 85.04 pt fragment margin puts the runs at x = 128.09
        // = 1.5 × 28.7 + 85.04; 1.8cm confirms → 51.66).
        foreach (var (suffix, f) in new[] { ("inch", 72.0), ("cm", 28.7), ("mm", 2.87), ("in", 72.0), ("pt", 1.0), ("px", 1.0) })
        {
            if (val.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                factor = f;
                num = val[..^suffix.Length].Trim();
                break;
            }
        }
        return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d * factor
            : defaultValue;
    }
}
