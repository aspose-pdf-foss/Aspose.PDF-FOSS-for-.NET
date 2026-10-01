using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms.Xfa;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    // A static XFA form's barcode field is a bare /Tx widget (no /AP): its only appearance
    // is the XFA <ui><barcode> the template paints. The Standard conversion draws that
    // symbol so the field survives the conversion. Geometry follows the reference's own
    // conversion (probed on a 183.43 x 28.81 pt code3Of9 field, ratio 3, seven digits):
    // the bars fill the field height, the narrow module is the field width over the symbol's
    // module count plus one trailing quiet module, and the caption is knocked out of the
    // bars at the bottom, centred on the symbol.
    private const string Code39XfaType = "code3Of9";
    private const double Code39DefaultWideNarrowRatio = 3.0;
    /// <summary>The caption em as a fraction of the field height (6.95 pt on 28.81 pt).</summary>
    private const double BarcodeCaptionEmOfHeight = 0.2412;
    /// <summary>The caption baseline's height above the field bottom, in caption ems.</summary>
    private const double BarcodeCaptionBaselineEm = 0.1922;
    /// <summary>The caption knockout reaches this far above the baseline: Courier New's ascent.</summary>
    private const double BarcodeCaptionAscentEm = 0.8325;
    /// <summary>Courier's fixed advance, used when the caption font carries no /Widths.</summary>
    private const double BarcodeCaptionAdvanceEm = 0.6;
    private const string BarcodeCaptionFallbackFont = "Courier";

    /// <summary>Give every widget whose XFA field is a Code 39 barcode, and which has no
    /// appearance of its own, an appearance stream drawing the symbol for its value.</summary>
    private void GenerateXfaBarcodeAppearances(PdfReader reader, PdfDictionary acroForm)
    {
        if (GetXfaTemplateDocument()?.DocumentElement is not { } root) return;
        foreach (var field in Fields)
        {
            if (reader.ResolveDict(field.Dict.Get("AP")) is not null) continue;
            if (field.FullName is not { Length: > 0 } name) continue;
            if (WalkTemplateBySomPath(root, name) is not XmlElement fieldEl) continue;
            if (XfaFormEngine.FindUiControl(fieldEl) is not XmlElement ui
                || ui.LocalName != "barcode" || ui.GetAttribute("type") != Code39XfaType) continue;
            if (reader.Resolve(field.Dict.Get("Rect")) is not PdfArray rectArr || rectArr.Count < 4) continue;
            var rect = Rectangle.FromPdfArray(rectArr, reader);
            if (rect is null || rect.Width <= 0 || rect.Height <= 0) continue;

            var framed = Code39Symbol.Frame(ResolveXfaBarcodeData(field, fieldEl, name));
            if (framed.Length <= 2) continue;
            double ratio = double.TryParse(ui.GetAttribute("wideNarrowRatio"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var r) && r > 0 ? r : Code39DefaultWideNarrowRatio;
            bool caption = ui.GetAttribute("textLocation") != "none";

            var fontName = field.DefaultAppearanceFontName;
            var (fontRef, fontDict) = ResolveBarcodeCaptionFont(reader, acroForm, fontName);
            var content = BuildCode39Appearance(framed, ratio, caption, rect.Width, rect.Height,
                fontName, ch => GlyphAdvanceEm(reader, fontDict, ch));

            var stream = new PdfStream(new PdfDictionary(), Aspose.Pdf.Text.Cp1252.GetBytes(content));
            stream.Dict.Set("Type", new PdfName("XObject"));
            stream.Dict.Set("Subtype", new PdfName("Form"));
            var bbox = new PdfArray();
            bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
            bbox.Add(new PdfReal(rect.Width)); bbox.Add(new PdfReal(rect.Height));
            stream.Dict.Set("BBox", bbox);
            var fonts = new PdfDictionary();
            fonts.Set(fontName, fontRef);
            var resources = new PdfDictionary();
            resources.Set("Font", fonts);
            stream.Dict.Set("Resources", resources);
            var ap = new PdfDictionary();
            ap.Set("N", stream);
            field.Dict.Set("AP", ap);
        }
    }

    /// <summary>The data the symbol encodes: the datasets value bound to the field, else the
    /// template's literal value, else the widget's /V, else its /DV.</summary>
    private string ResolveXfaBarcodeData(Field field, XmlElement fieldEl, string somPath)
    {
        if (GetXfaFieldValue(somPath) is { Length: > 0 } bound) return bound;
        if (fieldEl.SelectSingleNode("*[local-name()='value']/*[local-name()='text']") is { } text
            && text.InnerText is { Length: > 0 } literal) return literal;
        if (field.Value is { Length: > 0 } value) return value;
        return field.Dict.Get("DV") is PdfString dv ? dv.ToText() : string.Empty;
    }

    /// <summary>The caption's font: the /DA font from the AcroForm default resources (the
    /// resource entry as stored, so the appearance shares the object, plus its dictionary),
    /// else a Standard-14 Courier.</summary>
    private static (PdfObject entry, PdfDictionary dict) ResolveBarcodeCaptionFont(
        PdfReader reader, PdfDictionary acroForm, string fontName)
    {
        var drFonts = reader.ResolveDict(reader.ResolveDict(acroForm.Get("DR"))?.Get("Font"));
        if (drFonts?.Get(fontName) is { } entry && reader.ResolveDict(entry) is { } drFont) return (entry, drFont);
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(BarcodeCaptionFallbackFont));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        return (font, font);
    }

    /// <summary>A glyph's advance in ems from the font's /Widths (Courier's fixed advance
    /// when the font carries none).</summary>
    private static double GlyphAdvanceEm(PdfReader reader, PdfDictionary font, char ch)
    {
        if (reader.Resolve(font.Get("Widths")) is not PdfArray widths) return BarcodeCaptionAdvanceEm;
        int first = reader.Resolve(font.Get("FirstChar")) is PdfInteger fc ? (int)fc.Value : 0;
        int index = ch - first;
        if (index < 0 || index >= widths.Count) return BarcodeCaptionAdvanceEm;
        return reader.Resolve(widths[index]) switch
        {
            PdfInteger i => i.Value / 1000.0,
            PdfReal d => d.Value / 1000.0,
            _ => BarcodeCaptionAdvanceEm,
        };
    }

    /// <summary>The appearance content: the bars, then (with a caption) a white box knocked
    /// out of their bottom carrying the data in the /DA font.</summary>
    private static string BuildCode39Appearance(string framed, double ratio, bool caption,
        double w, double h, string fontName, System.Func<char, double> advanceEm)
    {
        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        double module = w / Code39Symbol.TotalModules(framed, ratio);
        var sb = new StringBuilder("q\n0 g\n");
        foreach (var (left, width) in Code39Symbol.Bars(framed, ratio))
            sb.Append(F(left * module)).Append(" 0 ").Append(F(width * module)).Append(' ').Append(F(h)).Append(" re\n");
        sb.Append("f\nQ\n");
        if (!caption) return sb.ToString();

        var text = framed.Substring(1, framed.Length - 2);
        double em = h * BarcodeCaptionEmOfHeight;
        double textWidth = 0;
        foreach (char ch in text) textWidth += advanceEm(ch) * em;
        double symbolWidth = (Code39Symbol.TotalModules(framed, ratio) - 1) * module;
        double x = (symbolWidth - textWidth) / 2;
        double baseline = em * BarcodeCaptionBaselineEm;
        sb.Append("q\n1 g\n").Append(F(x)).Append(" 0 ").Append(F(textWidth)).Append(' ')
          .Append(F(baseline + em * BarcodeCaptionAscentEm)).Append(" re\nf\nQ\n");
        var escaped = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        sb.Append("BT\n0 g\n/").Append(fontName).Append(' ').Append(F(em)).Append(" Tf\n")
          .Append(F(x)).Append(' ').Append(F(baseline)).Append(" Td\n(").Append(escaped).Append(") Tj\nET\n");
        return sb.ToString();
    }
}
