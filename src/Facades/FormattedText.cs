using System.Drawing;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Font style enumeration for FormattedText.
/// </summary>
public enum FontStyle
{
    /// <summary>Courier font.</summary>
    Courier,
    /// <summary>Courier Bold font.</summary>
    CourierBold,
    /// <summary>Courier Oblique font.</summary>
    CourierOblique,
    /// <summary>Courier Bold-Oblique font.</summary>
    CourierBoldOblique,
    /// <summary>Helvetica font.</summary>
    Helvetica,
    /// <summary>Helvetica Bold font.</summary>
    HelveticaBold,
    /// <summary>Helvetica Oblique font.</summary>
    HelveticaOblique,
    /// <summary>Helvetica Bold-Oblique font.</summary>
    HelveticaBoldOblique,
    /// <summary>Times Roman font.</summary>
    TimesRoman,
    /// <summary>Times Bold font.</summary>
    TimesBold,
    /// <summary>Times Italic font.</summary>
    TimesItalic,
    /// <summary>Times Bold-Italic font.</summary>
    TimesBoldItalic,
    /// <summary>Symbol font.</summary>
    Symbol,
    /// <summary>Zapf Dingbats font.</summary>
    ZapfDingbats,
    /// <summary>CJK font.</summary>
    CjkFont,
    /// <summary>Unknown / unmapped font style.</summary>
    Unknown,
}

/// <summary>
/// Encoding type enumeration for FormattedText.
/// </summary>
public enum EncodingType
{
    /// <summary>WinAnsi encoding.</summary>
    Winansi,
    /// <summary>Identity-H encoding (for CJK/Unicode).</summary>
    Identity_h,
    /// <summary>Identity-V encoding (for vertical CJK).</summary>
    Identity_v,
    /// <summary>Windows CP-1250 (Central European).</summary>
    Cp1250,
    /// <summary>Windows CP-1252 (Western European).</summary>
    Cp1252,
    /// <summary>Windows CP-1257 (Baltic).</summary>
    Cp1257,
    /// <summary>Mac Roman encoding.</summary>
    Macroman,
}

/// <summary>
/// Represents a font color using RGB components.
/// </summary>
public sealed class FontColor
{
    /// <summary>Red component (0-255).</summary>
    public int Red { get; set; }
    /// <summary>Green component (0-255).</summary>
    public int Green { get; set; }
    /// <summary>Blue component (0-255).</summary>
    public int Blue { get; set; }

    /// <summary>Default-constructed black colour.</summary>
    public FontColor() { }

    /// <summary>
    /// Create a new FontColor from RGB components.
    /// </summary>
    public FontColor(int r, int g, int b)
    {
        Red = r;
        Green = g;
        Blue = b;
    }

    /// <summary>Convert to a Color.</summary>
    internal Color ToColor() => Color.FromArgb(Red, Green, Blue);

    /// <summary>Implicit conversion from System.Drawing.Color.</summary>
    public static implicit operator FontColor(System.Drawing.Color c) =>
        new(c.R, c.G, c.B);
}

/// <summary>
/// Represents formatted text used in stamp and mend operations.
/// </summary>
public sealed class FormattedText
{
    private readonly List<TextLine> _lines = new();

    // The parameterless ctor seeds an empty placeholder line so Text/TextWidth behave;
    // the first AddNewLineText must replace it (not append after it), or stamps built
    // from `new FormattedText()` + AddNewLineText start with a spurious blank line.
    private bool _seedLineIsPlaceholder;

    /// <summary>The first line text content.</summary>
    public string Text => _lines.Count > 0 ? _lines[0].Text : "";

    /// <summary>Font size in points. Defaults to 10 to match the public
    /// simple-constructor default (e.g. <c>new FormattedText("text")</c>).</summary>
    public double FontSize { get; set; } = 10;

    /// <summary>Font name (PDF base font name).</summary>
    public string FontName { get; set; } = "Helvetica";

    /// <summary>The font name exactly as the caller passed it, before
    /// <see cref="NormalizeFontName"/> folded it to a Standard-14 base name
    /// (e.g. "Arial" → Helvetica). Consumers that measure with the real system
    /// face (facade stamp form BBox) need the original name.</summary>
    internal string? RequestedFontName { get; private set; }

    /// <summary>Foreground (text) color.</summary>
    public Color ForegroundColor { get; set; } = Color.Black;

    /// <summary>Background color.</summary>
    public Color BackgroundColor { get; set; } = Color.Empty;

    /// <summary>Whether the text is embedded (for non-standard fonts).</summary>
    public bool IsEmbedded { get; set; }

    /// <summary>Custom font file path (for TrueType font embedding).</summary>
    public string? CustomFontFile { get; set; }

    /// <summary>Advance width of the text at the current font and size, in points. CJK
    /// fonts (e.g. the MS-Gothic style mapped from <see cref="FontStyle.CjkFont"/>) and
    /// CJK codepoints advance a full em per glyph; Latin glyphs use the Standard-14
    /// advance widths.</summary>
    public float TextWidth
    {
        get
        {
            var text = Text;
            if (string.IsNullOrEmpty(text)) return 0f;
            bool cjkFont = FontName.Contains("Gothic", StringComparison.OrdinalIgnoreCase)
                || FontName.Contains("Mincho", StringComparison.OrdinalIgnoreCase);
            double total = 0;
            foreach (var ch in text)
            {
                if (cjkFont || IsWideCodepoint(ch))
                {
                    total += FontSize;
                    continue;
                }
                int w = ch <= 0xFF ? Aspose.Pdf.Text.Standard14Fonts.GetWidth("Helvetica", ch) : 0;
                if (w <= 0) w = Aspose.Pdf.Text.Standard14Fonts.GetDefaultWidth("Helvetica");
                total += w * FontSize / 1000.0;
            }
            return (float)total;
        }
    }

    private static bool IsWideCodepoint(char ch) =>
        ch is >= 'ᄀ' and <= 'ᅟ'   // Hangul Jamo
        || ch is >= '⺀' and <= '꓏' // CJK radicals … Yi
        || ch is >= '가' and <= '힣' // Hangul syllables
        || ch is >= '豈' and <= '﫿' // CJK compatibility ideographs
        || ch is >= '＀' and <= '｠' // fullwidth forms
        || ch is >= '￠' and <= '￦';

    /// <summary>All text lines.</summary>
    internal IReadOnlyList<TextLine> Lines => _lines;

    /// <summary>Line spacing in points (default uses 1.15× font size).</summary>
    internal double DefaultLineSpacing => FontSize * 1.15;

    /// <summary>
    /// Create a new FormattedText with empty text.
    /// </summary>
    public FormattedText()
    {
        _lines.Add(new TextLine("", 0));
        _seedLineIsPlaceholder = true;
    }

    /// <summary>
    /// Create a new FormattedText with the specified text.
    /// </summary>
    public FormattedText(string text)
    {
        _lines.Add(new TextLine(text, 0));
    }

    /// <summary>
    /// Create a FormattedText with text and foreground/background colors.
    /// </summary>
    public FormattedText(string text, Color foregroundColor, Color backgroundColor)
    {
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
        _lines.Add(new TextLine(text, 0));
    }

    /// <summary>
    /// Create a FormattedText with text and FontColor foreground/background.
    /// </summary>
    public FormattedText(string text, FontColor foregroundColor)
    {
        ForegroundColor = foregroundColor.ToColor();
        _lines.Add(new TextLine(text, 0));
    }

    /// <summary>
    /// Create a FormattedText with text and FontColor foreground/background.
    /// </summary>
    public FormattedText(string text, FontColor foregroundColor, FontColor backgroundColor)
    {
        ForegroundColor = foregroundColor.ToColor();
        BackgroundColor = backgroundColor.ToColor();
        _lines.Add(new TextLine(text, 0));
    }

    /// <summary>
    /// Create a FormattedText with full formatting using FontColor.
    /// </summary>
    public FormattedText(string text, FontColor fontColor, FontStyle fontStyle,
        EncodingType encodingType, bool embedded, float textSize)
    {
        ForegroundColor = fontColor.ToColor();
        FontName = GetPdfFontName(fontStyle);
        IsEmbedded = embedded;
        FontSize = textSize;
        _lines.Add(new TextLine(text, 0));
    }

    /// <summary>
    /// Create a FormattedText with full formatting using FontColor with foreground and background.
    /// </summary>
    public FormattedText(string text, FontColor textColor, FontColor backColor,
        FontStyle textFont, EncodingType textEncoding, bool embedded, float textSize)
    {
        ForegroundColor = textColor.ToColor();
        BackgroundColor = backColor.ToColor();
        FontName = GetPdfFontName(textFont);
        IsEmbedded = embedded;
        FontSize = textSize;
        _lines.Add(new TextLine(text, 0));
    }

    /// <summary>
    /// Create a FormattedText with full formatting using FontColor, with line spacing.
    /// </summary>
    public FormattedText(string text, FontColor fontColor, FontStyle textFont,
        EncodingType textEncoding, bool embedded, float textSize, float lineSpacing)
    {
        ForegroundColor = fontColor.ToColor();
        FontName = GetPdfFontName(textFont);
        IsEmbedded = embedded;
        FontSize = textSize;
        _lines.Add(new TextLine(text, lineSpacing));
    }

    /// <summary>
    /// Create a FormattedText with System.Drawing.Color foreground/background and font settings.
    /// </summary>
    public FormattedText(string text, Color textColor, Color backColor,
        string fontName, EncodingType textEncoding, bool embedded, float fontSize)
    {
        ForegroundColor = textColor;
        BackgroundColor = backColor;
        RequestedFontName = fontName;
        FontName = NormalizeFontName(fontName);
        IsEmbedded = embedded;
        FontSize = fontSize;
        _lines.Add(new TextLine(text, 0));
    }

    /// <summary>
    /// Create a FormattedText with System.Drawing.Color foreground/background and FontStyle.
    /// </summary>
    public FormattedText(string text, Color textColor, Color backColor,
        FontStyle textFont, EncodingType encoding, bool embedded, float textSize)
    {
        ForegroundColor = textColor;
        BackgroundColor = backColor;
        FontName = GetPdfFontName(textFont);
        IsEmbedded = embedded;
        FontSize = textSize;
        _lines.Add(new TextLine(text, 0));
    }

    public FormattedText(string text, Color textColor, Color backColor,
        FontStyle textFont, EncodingType textEncoding, bool embedded, float textSize, float lineSpacing)
    {
        ForegroundColor = textColor;
        BackgroundColor = backColor;
        FontName = GetPdfFontName(textFont);
        IsEmbedded = embedded;
        FontSize = textSize;
        _lines.Add(new TextLine(text, lineSpacing));
    }

    public FormattedText(string text, System.Drawing.Color textColor, System.Drawing.Color backColor,
        FontStyle textFont, EncodingType textEncoding, bool embedded, float textSize, float lineSpacing)
        : this(text, Color.FromRgb(textColor), Color.FromRgb(backColor),
               textFont, textEncoding, embedded, textSize, lineSpacing)
    {
    }

    public FormattedText(string text, FontColor textColor, FontColor backColor,
        FontStyle textFont, EncodingType textEncoding, bool embedded, float textSize, float lineSpacing)
        : this(text, textColor.ToColor(), backColor.ToColor(),
               textFont, textEncoding, embedded, textSize, lineSpacing)
    {
    }

    // ── System.Drawing.Color forwarders (the public API declares these as
    //    distinct overloads alongside the Aspose.Pdf.Color ones). ──────────

    /// <summary>System.Drawing.Color foreground + foreground/background pair.</summary>
    public FormattedText(string text, System.Drawing.Color textColor, System.Drawing.Color backColor)
        : this(text, Color.FromRgb(textColor), Color.FromRgb(backColor)) { }

    /// <summary>System.Drawing.Color foreground + FontStyle + EncodingType, no lineSpacing.</summary>
    public FormattedText(string text, System.Drawing.Color color, FontStyle textFont,
        EncodingType textEncoding, bool embedded, float textSize)
        : this(text, Color.FromRgb(color), textFont, textEncoding, embedded, textSize) { }

    /// <summary>System.Drawing.Color foreground + FontStyle + EncodingType + lineSpacing.</summary>
    public FormattedText(string text, System.Drawing.Color textColor, FontStyle textFont,
        EncodingType textEncoding, bool embedded, float textSize, float lineSpacing)
        : this(text, Color.FromRgb(textColor), textFont, textEncoding, embedded, textSize, lineSpacing) { }

    /// <summary>System.Drawing.Color foreground/background + FontStyle.</summary>
    public FormattedText(string text, System.Drawing.Color textColor, System.Drawing.Color backColor,
        FontStyle textFont, EncodingType encoding, bool embedded, float textSize)
        : this(text, Color.FromRgb(textColor), Color.FromRgb(backColor), textFont, encoding, embedded, textSize) { }

    /// <summary>System.Drawing.Color foreground/background + font-name string.</summary>
    public FormattedText(string text, System.Drawing.Color textColor, System.Drawing.Color backColor,
        string fontName, EncodingType textEncoding, bool embedded, float fontSize)
        : this(text, Color.FromRgb(textColor), Color.FromRgb(backColor), fontName, textEncoding, embedded, fontSize) { }

    /// <summary>
    /// Create a FormattedText with System.Drawing.Color foreground and a font name or TrueType font file.
    /// </summary>
    public FormattedText(string text, System.Drawing.Color textColor, string fontName,
        EncodingType textEncoding, bool embedded, float fontSize)
        : this(text, Color.FromRgb(textColor), fontName, textEncoding, embedded, fontSize)
    {
    }

    /// <summary>
    /// Create a FormattedText with Aspose.Pdf.Color foreground and a font name or TrueType font file.
    /// </summary>
    public FormattedText(string text, Color textColor, string fontName,
        EncodingType textEncoding, bool embedded, float fontSize)
    {
        ForegroundColor = textColor;
        // Heuristic: treat extension-bearing strings as font files, plain names as font names.
        if (!string.IsNullOrEmpty(fontName) &&
            (fontName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
             || fontName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
             || System.IO.Path.IsPathRooted(fontName)))
        {
            CustomFontFile = fontName;
        }
        else
        {
            RequestedFontName = fontName;
            FontName = fontName;
        }
        IsEmbedded = embedded;
        FontSize = fontSize;
        _lines.Add(new TextLine(text, 0));
    }

    /// <summary>
    /// Create a FormattedText with FontStyle (enum) and System.Drawing.Color.
    /// </summary>
    public FormattedText(string text, Color color, FontStyle textFont,
        EncodingType textEncoding, bool embedded, float textSize)
    {
        ForegroundColor = color;
        FontName = GetPdfFontName(textFont);
        IsEmbedded = embedded;
        FontSize = textSize;
        _lines.Add(new TextLine(text, 0));
    }

    /// <summary>
    /// Create a FormattedText with Aspose.Pdf.Color foreground/background and a line spacing.
    /// (Matches the 7-arg <c>(text, textColor, textFont, textEncoding, embedded,
    /// textSize, lineSpacing)</c> overload.)
    /// </summary>
    public FormattedText(string text, Color textColor, FontStyle textFont,
        EncodingType textEncoding, bool embedded, float textSize, float lineSpacing)
    {
        ForegroundColor = textColor;
        FontName = GetPdfFontName(textFont);
        IsEmbedded = embedded;
        FontSize = textSize;
        _lines.Add(new TextLine(text, lineSpacing));
    }

    /// <summary>Height of the formatted text, in points. Matches the em height of a
    /// single line at the current font size (mirrors <see cref="TextWidth"/>, which
    /// measures the first line), so facade stamps built from a <see cref="FormattedText"/>
    /// get a non-zero background/logo box.</summary>
    public float TextHeight => string.IsNullOrEmpty(Text) ? 0f : (float)FontSize;

    /// <summary>
    /// Add a new line of text.
    /// </summary>
    public void AddNewLineText(string newLineText)
    {
        AddNewLineText(newLineText, 0);
    }

    /// <summary>
    /// Add a new line of text with custom line spacing.
    /// </summary>
    public void AddNewLineText(string newLineText, float lineSpacing)
    {
        if (_seedLineIsPlaceholder)
        {
            _lines[0] = new TextLine(newLineText, lineSpacing);
            _seedLineIsPlaceholder = false;
            return;
        }
        _lines.Add(new TextLine(newLineText, lineSpacing));
    }

    /// <summary>
    /// Check if the text contains CJK characters.
    /// </summary>
    public bool IsCjk()
    {
        foreach (var line in _lines)
        {
            foreach (var ch in line.Text)
            {
                // CJK Unified Ideographs, Hiragana, Katakana, Hangul
                if (ch >= 0x3000 && ch <= 0x9FFF) return true;
                if (ch >= 0xAC00 && ch <= 0xD7AF) return true; // Hangul Syllables
                if (ch >= 0xFF00 && ch <= 0xFFEF) return true; // Fullwidth forms
                if (ch >= 0x2E80 && ch <= 0x2FFF) return true; // CJK Radicals
                if (ch >= 0xF900 && ch <= 0xFAFF) return true; // CJK Compatibility
            }
        }
        return false;
    }

    /// <summary>
    /// Set the font to a CJK-capable font (MS Gothic equivalent).
    /// </summary>
    public void SetCjkFontStyle()
    {
        FontName = "MS-Gothic";
    }

    /// <summary>
    /// Get the internal font reference for the formatted text.
    /// </summary>
    internal FormattedTextFont getFont() => new(FontName);

    /// <summary>Map FontStyle enum to PDF base font name.</summary>
    internal static string GetPdfFontName(FontStyle style) => style switch
    {
        FontStyle.Courier => "Courier",
        FontStyle.CourierBold => "Courier-Bold",
        FontStyle.CourierOblique => "Courier-Oblique",
        FontStyle.CourierBoldOblique => "Courier-BoldOblique",
        FontStyle.Helvetica => "Helvetica",
        FontStyle.HelveticaBold => "Helvetica-Bold",
        FontStyle.HelveticaOblique => "Helvetica-Oblique",
        FontStyle.HelveticaBoldOblique => "Helvetica-BoldOblique",
        FontStyle.TimesRoman => "Times-Roman",
        FontStyle.TimesBold => "Times-Bold",
        FontStyle.TimesItalic => "Times-Italic",
        FontStyle.TimesBoldItalic => "Times-BoldItalic",
        FontStyle.Symbol => "Symbol",
        FontStyle.ZapfDingbats => "ZapfDingbats",
        FontStyle.CjkFont => "MS-Gothic",
        // FontStyle.Unknown resolves to Times-Roman (a plain
        // Standard-14 Type1 with WinAnsi — the EncodingType is not honoured for it).
        FontStyle.Unknown => "Times-Roman",
        _ => "Helvetica",
    };

    /// <summary>Normalize user-friendly font names to PDF base font names.</summary>
    private static string NormalizeFontName(string name)
    {
        return name.Replace(" ", "") switch
        {
            "TimesNewRoman" => "Times-Roman",
            "TimesRoman" => "Times-Roman",
            "Times" => "Times-Roman",
            "Arial" => "Helvetica",
            "ArialBold" => "Helvetica-Bold",
            "CourierNew" => "Courier",
            _ => name,
        };
    }

    /// <summary>Represents a single line of text.</summary>
    internal sealed class TextLine
    {
        public string Text { get; }
        public double LineSpacing { get; }

        public TextLine(string text, double lineSpacing)
        {
            Text = text;
            LineSpacing = lineSpacing;
        }
    }
}

/// <summary>
/// Represents a font reference returned by FormattedText.getFont().
/// </summary>
public sealed class FormattedTextFont
{
    /// <summary>Font name.</summary>
    public string FontName { get; }

    internal FormattedTextFont(string fontName)
    {
        FontName = fontName;
    }
}
