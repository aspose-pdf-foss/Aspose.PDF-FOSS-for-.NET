using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms;

public sealed partial class RichTextBoxField
{
    /// <summary>The style a piece of rich text is set in; a copy is made for every element opened.</summary>
    private sealed class RichStyle
    {
        public string Family = "Helvetica";
        public double Size = DefaultRichSize;
        public bool Bold;
        public bool Italic;
        /// <summary>The fill colour operator ("r g b rg"), or null for the field's /DA colour.</summary>
        public string? Color;
        public string Align = "left";
        public RichStyle Copy() => (RichStyle)MemberwiseClone();
    }

    /// <summary>A piece of text in one style, and the hard lines the markup's blocks and breaks make of them.</summary>
    private sealed record RichPiece(string Text, RichStyle Style);

    private sealed class RichLine
    {
        public readonly List<RichPiece> Pieces = new();
        public string Align = "left";
        public string Text => string.Concat(Pieces.Select(p => p.Text));
    }

    private const double DefaultRichSize = 12;
    /// <summary>The text starts this far inside the box on both sides, as the plain appearance's does.</summary>
    private const double RichSideInset = 2;
    /// <summary>Line pitch as a share of the type size, and the share of it a baseline sits above the line's bottom.</summary>
    private const double RichLinePitchEm = 1.15;
    private const double RichDescentEm = 0.207;
    /// <summary>Elements that start a new line; a cell is set off from the one before it by a space.</summary>
    private static readonly string[] RichBlockTags = { "p", "div", "li", "tr", "ul", "ol", "table", "tbody", "thead", "tfoot", "h1", "h2", "h3", "h4", "h5", "h6", "body" };
    private static readonly string[] RichVoidTags = { "br", "hr", "img" };
    /// <summary>The point sizes the HTML font size scale 1..7 names.</summary>
    private static readonly double[] HtmlFontScalePt = { 8, 10, 12, 14, 18, 24, 36 };
    private const double PxToPt = 0.75;

    /// <summary>True for a value that is markup rather than literal text: a rich-text body or document, or a
    /// fragment holding a tag of one of the elements rich text is written in.</summary>
    internal static bool LooksLikeRichMarkup(string value) =>
        SafeRegex.IsMatch(value, @"<(\?xml|/?(body|p|div|b|i|u|strong|em|span|font|ul|ol|li|table|tr|td|br))\b[^<>]*>", RegexOptions.IgnoreCase);

    /// <summary>Lays the /RV markup out as the field's appearance and derives the plain /V from it: every block
    /// and break starts a line, a line too wide for the box wraps at its spaces, and each piece is drawn in the
    /// face, size and colour its elements and styles give it.</summary>
    private void ApplyRichMarkup(string rv)
    {
        var lines = ParseRichLines(rv);
        if (lines.Count == 0) return;
        var plain = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) plain.Append('\n');
            plain.Append(lines[i].Text);
        }
        Dict.Set("V", EncodePdfTextString(plain.ToString()));
        RegenerateMarkupAppearance(lines);
    }

    /// <summary>Reads the markup into hard lines of styled pieces. The reader is lenient as a viewer is: tags
    /// may be unclosed or closed without an opener, attribute values may stand unquoted, and a void tag needs no
    /// slash. Whitespace that only formats the markup between tags is dropped.</summary>
    private static List<RichLine> ParseRichLines(string markup)
    {
        var lines = new List<RichLine>();
        var line = new RichLine();
        var styles = new Stack<(string Tag, RichStyle Style)>();
        var style = new RichStyle();
        var pos = 0;
        void Break(bool always)
        {
            if (!always && line.Pieces.Count == 0) { line.Align = style.Align; return; }
            lines.Add(line);
            line = new RichLine { Align = style.Align };
        }
        while (pos < markup.Length)
        {
            var lt = markup.IndexOf('<', pos);
            var textEnd = lt < 0 ? markup.Length : lt;
            if (textEnd > pos) AddRichText(line, System.Net.WebUtility.HtmlDecode(markup.Substring(pos, textEnd - pos)), style);
            if (lt < 0) break;
            pos = SkipRichTag(markup, lt, out var name, out var attrs, out var closing, out var selfClosing);
            if (name.Length == 0) continue;
            var block = Array.IndexOf(RichBlockTags, name) >= 0;
            if (closing)
            {
                if (block) Break(always: false);
                while (styles.Count > 0)
                {
                    var (tag, outer) = styles.Pop();
                    style = outer;
                    if (tag == name) break;
                }
                if (block) line.Align = style.Align;
                continue;
            }
            if (name == "br") { Break(always: true); continue; }
            if (name is "td" or "th" && line.Pieces.Count > 0) AddRichText(line, " ", style);
            if (block) Break(always: false);
            if (selfClosing || Array.IndexOf(RichVoidTags, name) >= 0) continue;
            styles.Push((name, style));
            style = RichStyleOf(name, attrs, style.Copy());
            if (block) line.Align = style.Align;
        }
        if (line.Pieces.Count > 0 || lines.Count == 0) lines.Add(line);
        return lines;
    }

    private static void AddRichText(RichLine line, string text, RichStyle style)
    {
        // Formatting whitespace between tags is not content; a break inside text is a space, as a viewer shows it.
        if (text.IndexOf('\n') >= 0 && text.Trim().Length == 0) return;
        text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        if (text.Length > 0) line.Pieces.Add(new RichPiece(text, style));
    }

    /// <summary>Reads the tag at <paramref name="start"/> and returns the index after it. A comment, a processing
    /// instruction and a doctype are skipped and return an empty name.</summary>
    private static int SkipRichTag(string markup, int start, out string name, out string attrs, out bool closing, out bool selfClosing)
    {
        name = string.Empty; attrs = string.Empty; closing = false; selfClosing = false;
        if (start + 1 >= markup.Length) return markup.Length;
        var next = markup[start + 1];
        if (next == '!' || next == '?')
        {
            var closer = markup.StartsWith("<!--", StringComparison.Ordinal) ? "-->" : ">";
            var end = markup.IndexOf(closer, start, StringComparison.Ordinal);
            return end < 0 ? markup.Length : end + closer.Length;
        }
        var gt = markup.IndexOf('>', start);
        if (gt < 0) gt = markup.Length;
        var body = markup.Substring(start + 1, gt - start - 1).Trim();
        if (body.StartsWith("/", StringComparison.Ordinal)) { closing = true; body = body.Substring(1).Trim(); }
        if (body.EndsWith("/", StringComparison.Ordinal)) { selfClosing = true; body = body.Substring(0, body.Length - 1); }
        var m = SafeRegex.Match(body, @"^([A-Za-z][A-Za-z0-9]*)");
        if (!m.Success) return gt + 1;
        name = m.Groups[1].Value.ToLowerInvariant();
        attrs = body.Substring(m.Length);
        return gt + 1;
    }

    /// <summary>The style inside an element: its tag's own meaning (bold, italic), its font attributes and its
    /// style declarations, laid over the style outside it.</summary>
    private static RichStyle RichStyleOf(string name, string attrs, RichStyle style)
    {
        if (name is "b" or "strong") style.Bold = true;
        if (name is "i" or "em") style.Italic = true;
        var a = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in SafeRegex.Matches(attrs, @"([A-Za-z][\w-]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))"))
            a[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
        if (a.TryGetValue("face", out var face) && face.Trim().Length > 0) style.Family = FirstFamily(face);
        if (a.TryGetValue("color", out var color) && ColorOperator(color) is { } op) style.Color = op;
        if (a.TryGetValue("size", out var sizeText) && double.TryParse(sizeText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0)
            style.Size = size <= HtmlFontScalePt.Length ? HtmlFontScalePt[(int)Math.Round(size) - 1] : size; // the 1..7 scale, else points
        if (a.TryGetValue("align", out var align)) style.Align = align.Trim().ToLowerInvariant();
        if (a.TryGetValue("style", out var css)) ApplyRichCss(css, style);
        return style;
    }

    private static void ApplyRichCss(string css, RichStyle style)
    {
        foreach (var declaration in css.Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0) continue;
            var property = declaration.Substring(0, colon).Trim().ToLowerInvariant();
            var value = declaration.Substring(colon + 1).Trim();
            switch (property)
            {
                case "font-family": style.Family = FirstFamily(value); break;
                case "font-weight": style.Bold = value.Equals("bold", StringComparison.OrdinalIgnoreCase) || value.Equals("bolder", StringComparison.OrdinalIgnoreCase) || (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) && weight >= 600); break;
                case "font-style": style.Italic = value.StartsWith("italic", StringComparison.OrdinalIgnoreCase) || value.StartsWith("oblique", StringComparison.OrdinalIgnoreCase); break;
                case "color": if (ColorOperator(value) is { } op) style.Color = op; break;
                case "text-align": style.Align = value.ToLowerInvariant(); break;
                case "font-size":
                    var m = SafeRegex.Match(value, @"^([\d.]+)\s*(pt|px)?");
                    if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0)
                        style.Size = m.Groups[2].Value == "px" ? size * PxToPt : size;
                    break;
                default: break;
            }
        }
    }

    private static string FirstFamily(string list)
    {
        var first = list.Split(',')[0].Trim().Trim('\'', '"').Trim();
        return first.Length == 0 ? "Helvetica" : first;
    }

    /// <summary>The fill operator for a colour written as #rgb, #rrggbb, #rrggbbaa or rgb(r, g, b); null otherwise.</summary>
    private static string? ColorOperator(string value)
    {
        value = value.Trim();
        int r, g, b;
        var rgb = SafeRegex.Match(value, @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");
        if (rgb.Success)
        {
            r = int.Parse(rgb.Groups[1].Value, CultureInfo.InvariantCulture);
            g = int.Parse(rgb.Groups[2].Value, CultureInfo.InvariantCulture);
            b = int.Parse(rgb.Groups[3].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var hex = SafeRegex.Match(value, @"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");
            if (!hex.Success) return null;
            var digits = hex.Groups[1].Value;
            if (digits.Length == 3) digits = string.Concat(digits.Select(c => new string(c, 2)));
            r = Convert.ToInt32(digits.Substring(0, 2), 16);
            g = Convert.ToInt32(digits.Substring(2, 2), 16);
            b = Convert.ToInt32(digits.Substring(4, 2), 16);
        }
        return string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} rg", r / 255.0, g / 255.0, b / 255.0);
    }
}
