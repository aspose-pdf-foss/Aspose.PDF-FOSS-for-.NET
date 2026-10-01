using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A JSON-escaped export writes every attribute quote as <c>\"</c>, so a value
    /// arrives still wrapped in them. A value that is READ — a control's type, its size /
    /// cols / rows — must lose the wrappers; a value that is DRAWN keeps them:
    /// <c>\"Simon\"</c> typesets literally.</summary>
    private static string UnescapeAttrValue(string? v)
    {
        var s = v ?? "";
        return s.Length >= 4 && s.StartsWith("\\\"", StringComparison.Ordinal)
            && s.EndsWith("\\\"", StringComparison.Ordinal)
            ? s.Substring(2, s.Length - 4)
            : s;
    }

    /// <summary>The sheet's declarations for one control, weakest selector first so that a
    /// later one overrides: the element name, then each of its classes, then its id. Returned
    /// as a declaration string for the same reader that parses a style attribute.</summary>
    private static string? SheetControlBox(
        IReadOnlyDictionary<string, Dictionary<string, string>>? sheet,
        string? tag, Dictionary<string, string>? attrs)
    {
        if (sheet is null || string.IsNullOrEmpty(tag)) return null;
        var selectors = new List<string> { tag!.ToLowerInvariant() };
        if (attrs is not null && attrs.TryGetValue("class", out var classes))
            foreach (var c in classes.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries))
                selectors.Add("." + c);
        if (attrs is not null && attrs.TryGetValue("id", out var id) && id.Length > 0)
            selectors.Add("#" + id);

        var box = new StringBuilder();
        foreach (var sel in selectors)
        {
            if (!sheet.TryGetValue(sel, out var rule)) continue;
            foreach (var prop in new[] { "width", "height" })
                if (rule.TryGetValue(prop, out var v)) box.Append(prop).Append(':').Append(v).Append(';');
        }
        return box.Length > 0 ? box.ToString() : null;
    }

    /// <summary>Build a Block describing an &lt;input> control: its value and any CSS
    /// width/height, so layout can emit a TextBoxField of the right size.</summary>
    private static Block BuildInputBlock(Dictionary<string, string>? attrs, BlockStyle style,
        bool controlBoxes = false, bool multiline = false, string? innerText = null,
        IReadOnlyDictionary<string, Dictionary<string, string>>? sheet = null, string? tag = null)
    {
        string? value = null, styleAttr = null, name = null, id = null;
        attrs?.TryGetValue("value", out value);
        attrs?.TryGetValue("style", out styleAttr);
        attrs?.TryGetValue("name", out name);
        attrs?.TryGetValue("id", out id);
        var (w, h) = ParseInputSize(styleAttr);
        // A control sized by the sheet rather than by a style attribute: `textarea { width:
        // 950px }` is the only place many documents state the box, and it sizes the control
        // exactly as the attribute would. The attribute still wins where both speak.
        if (w <= 0 || h <= 0)
        {
            var (sheetW, sheetH) = ParseInputSize(SheetControlBox(sheet, tag, attrs));
            if (w <= 0) w = sheetW;
            if (h <= 0) h = sheetH;
        }
        double advance = 0;
        if (controlBoxes)
        {
            var (iw, ih, iadv) = IntrinsicControlBox(attrs, multiline);
            if (w <= 0) w = iw;
            if (h <= 0) h = ih;
            advance = iadv;
        }
        // A textarea occupies its intrinsic box in EVERY flow, not only the control-box
        // dialect: the reference draws a bare <textarea> as its rows x cols grid (two rows
        // of 11.25 under a 15.75 first row = 27 pt, 21 columns = 123 pt) and a declared width
        // replaces only the columns. Without this the plain flow gave it a one-line box.
        else if (multiline)
        {
            var (iw, ih, _) = IntrinsicControlBox(attrs, multiline);
            if (w <= 0) w = iw;
            if (h <= 0) h = ih;
        }
        if (multiline && !string.IsNullOrEmpty(innerText)) value = innerText;
        // A disabled or readonly input maps to a ReadOnly AcroForm field.
        var readOnly = attrs is not null && (attrs.ContainsKey("disabled") || attrs.ContainsKey("readonly"));
        // AcroForm field name: prefer the HTML name attribute, fall back to id.
        var fieldName = !string.IsNullOrEmpty(name) ? name : id;
        return new Block
        {
            IsInputField = true,
            InputValue = DecodeEntities(value ?? ""),
            InputName = string.IsNullOrEmpty(fieldName) ? null : fieldName,
            InputWidth = w,
            InputHeight = h,
            InputMultiline = multiline,
            InputReadOnly = readOnly,
            // A control the flow draws as a box shows its own value inside it: a text
            // input in the UI face, a textarea in the typewriter face.
            InputDrawValue = controlBoxes,
            InputValueMono = multiline,
            InputAdvance = advance,
            FontSize = style.FontSize,
            FontRes = style.FontRes,
            LeftIndent = style.LeftIndent,
            // The intrinsic box carries its own leading in the advance; the legacy
            // 1/2 pt padding is the fallback path's calibration, not this one's.
            MarginTop = controlBoxes ? 0 : 1,
            MarginBottom = controlBoxes ? 0 : 2,
        };
    }

    /// <summary>Turn HTML into a list of Block records. The parser is a
    /// small hand-rolled tokeniser (no external DOM): it tracks the stack
    /// of open block elements to decide font + margins for each text run.</summary>
    /// <summary>Resolve a container-chrome CSS length (px/pt/rem/em; for a border
    /// shorthand, its first length term) to POINTS. rem/em resolve at the 16px
    /// root — this feeds class-rule chrome on structural divs, which carry no
    /// authored font size of their own in these documents.</summary>
    private static double BoxChromeLen(string value)
    {
        var m = Regex.Match(value, @"([\d.]+)\s*(px|pt|rem|em)", RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return 0;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "pt" => n,
            "px" => n * 0.75,
            _ => n * 16 * 0.75,
        };
    }
}
