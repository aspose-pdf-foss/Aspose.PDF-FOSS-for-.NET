using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>Per-font runtime options (currently only the font-embedding error toggle).</summary>
public interface IFontOptions
{
    bool NotifyAboutFontEmbeddingError { get; set; }
}

/// <summary>Thin engine-font view exposed for public-API compatibility (Font.iPdfFont).
/// Stripped down to the members the test corpus actually reads.</summary>
public sealed class PdfFontView
{
    private readonly Font _font;
    internal PdfFontView(Font font) { _font = font; }

    /// <summary>The PDF /BaseFont name with the subset prefix removed but the full font name
    /// (including any style suffix) preserved — e.g. "ABCDEF+TimesNewRomanPS-BoldMT" becomes
    /// "TimesNewRomanPS-BoldMT" and "Helvetica-Bold" stays "Helvetica-Bold". The 6-letter
    /// subset tag (per PDF §9.6.4) is the only part stripped.</summary>
    public string BaseFontNameOnly
    {
        get
        {
            var bf = _font.BaseFont ?? string.Empty;
            var plus = bf.IndexOf('+');
            if (plus >= 0 && plus < bf.Length - 1) bf = bf.Substring(plus + 1);
            return bf;
        }
    }
}
