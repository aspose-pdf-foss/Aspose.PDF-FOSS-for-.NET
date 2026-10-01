using System.IO;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

public sealed partial class FormEditor
{
    /// <summary>The stages of the facade apply: the font and text-colour appearance.</summary>
    private void ApplyFacadeFont(Field field)
    {
            // A caller-specified CustomFont must be loadable: a standard PDF font
            // abbreviation, or a font name FontRepository can resolve (Standard-14,
            // a registered source, or a host system font). An unknown family is an
            // error rather than a silent fall-through to the default font.
            if (!string.IsNullOrEmpty(Facade.CustomFont) && !IsLoadableFormFont(Facade.CustomFont!))
                throw new ArgumentException($"Could not load specified font : {Facade.CustomFont}");
            string fontName;
            if (!string.IsNullOrEmpty(Facade.CustomFont))
            {
                fontName = Facade.CustomFont!;
            }
            else if (Facade.Font == FontStyle.CjkFont)
            {
                // The CJK facade font is a real embeddable face (Bitstream CyberCJK):
                // route through the DefaultAppearance setter so the composite font is
                // embedded into /DR and the /DA re-pointed at it.
                var cjk = Aspose.Pdf.Text.FontRepository.TryFindFont("BitstreamCyberCJK");
                if (cjk is null)
                    throw new ArgumentException("Could not load specified font : BitstreamCyberCJK");
                cjk.IsEmbedded = true; // composite face goes into the form's /DR
                var tcCjk = Facade.TextColor;
                var cjkColor = tcCjk.A != 0 ? tcCjk : System.Drawing.Color.Black;
                field.DefaultAppearance = new Aspose.Pdf.Annotations.DefaultAppearance(
                    cjk, Facade.FontSize > 0 ? Facade.FontSize : 12, cjkColor);
                goto daDone;
            }
            else
            {
                // Facade.Font (the base-14 enum) maps to a standard /DA
                // abbreviation registered in the AcroForm /DR so the /DA resolves.
                var (abbr, baseFont, subtype) = DaFontFor(Facade.Font);
                fontName = abbr;
                _document?.Form?.RegisterDefaultResourceFont(abbr, baseFont, subtype);
            }
            var fontSize = Facade.FontSize > 0 ? Facade.FontSize : 0f;
            // The facade text colour rides in the /DA operation (yellow → "1 1 0 rg").
            var tc = Facade.TextColor;
            var colorOp = tc.A != 0
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.###} {1:0.###} {2:0.###} rg", tc.R / 255.0, tc.G / 255.0, tc.B / 255.0)
                : "0 g";
            var da = $"/{fontName} {fontSize.ToString("G", System.Globalization.CultureInfo.InvariantCulture)} Tf {colorOp}";
            field.Dict.Set("DA", new PdfString(System.Text.Encoding.UTF8.GetBytes(da)));
            // A size the facade names is the field's own, as one handed over in a DefaultAppearance
            // is: it is pinned against the STATIC TextBoxField auto-fit clamps, which another
            // document in the process may have set (a field asked for 20 pt came back at 18.26).
            if (fontSize > 0) field.DaFontSizePinned = true;
            // A field that already carries a value keeps showing the OLD font until
            // its appearance is rebuilt — regenerate it under the new /DA.
            if (field.Type is Forms.FieldType.Text or Forms.FieldType.Choice
                or Forms.FieldType.ComboBox or Forms.FieldType.ListBox)
            {
                field.ResetDefaultAppearanceCache();
                field.Dict.Remove("AP");
                foreach (var kid in field.AllKids())
                    kid.Remove("AP");
                field.GenerateAppearance();
            }
        daDone: ;
    }
}
