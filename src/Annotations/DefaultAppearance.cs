using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Represents the default appearance of a free text annotation.
/// Encapsulates font name, size, and text color used to generate the /DA string.
/// </summary>
public class DefaultAppearance
{
    /// <summary>Create a default appearance with font, size, and color.</summary>
    public DefaultAppearance(string fontName, double fontSize, System.Drawing.Color textColor)
    {
        FontName = fontName ?? "Helvetica";
        FontSize = fontSize;
        TextColor = textColor;
    }

    /// <summary>Create a default appearance with just a font name and size.</summary>
    public DefaultAppearance(string fontName, double fontSize)
        : this(fontName, fontSize, System.Drawing.Color.Black) { }

    /// <summary>Create a default appearance from a typed Font instance plus
    /// size and color. The font name is taken from the Font's normalized name
    /// (subset prefix and comma-separated style stripped); the Font is retained so
    /// an embeddable face can be written into the form when the owning field is
    /// added.</summary>
    public DefaultAppearance(Aspose.Pdf.Text.Font font, double fontSize, System.Drawing.Color textColor)
        : this(font?.FontName ?? "Helvetica", fontSize, textColor)
    {
        EmbeddedFont = font;
    }

    /// <summary>Create with default values (Helvetica 12pt black).</summary>
    public DefaultAppearance()
        : this("Helvetica", 12, System.Drawing.Color.Black) { }

    /// <summary>Font name (e.g. "Arial", "Helvetica").</summary>
    public string FontName { get; set; }

    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; }

    /// <summary>Text color.</summary>
    public System.Drawing.Color TextColor { get; set; }

    /// <summary>PDF resource name used to reference this font in /DA. Defaults to
    /// the font name with spaces stripped; once the font is embedded into the form
    /// (a composite face), this is the generated resource name (e.g. "C0_0").</summary>
    public string FontResourceName
    {
        get => _fontResourceName ?? FontName.Replace(" ", "");
        set => _fontResourceName = value;
    }
    private string? _fontResourceName;

    /// <summary>The typed font this appearance was built from (when constructed from
    /// a Font); null otherwise. Retained so the field can embed an embeddable face.</summary>
    public Aspose.Pdf.Text.Font? Font => EmbeddedFont;

    /// <summary>Backing font supplied to the typed constructor.</summary>
    internal Aspose.Pdf.Text.Font? EmbeddedFont { get; set; }

    /// <summary>Raw /DA appearance string.</summary>
    public string Text => ToAppearanceString();

    /// <summary>Generate the PDF /DA appearance string (e.g. "/Helv 12 Tf 0 0 0 rg").</summary>
    internal string ToAppearanceString()
    {
        var r = TextColor.R / 255.0;
        var g = TextColor.G / 255.0;
        var b = TextColor.B / 255.0;
        // Use a resource name derived from the font name (strip spaces, prefix with /)
        var resName = "/" + FontName.Replace(" ", "");
        return $"{resName} {FontSize:G} Tf {r:F3} {g:F3} {b:F3} rg";
    }
}
