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
            if (!string.IsNullOrEmpty(Facade.CustomFont) && !IsLoadableFormFont(Facade.CustomFont))
                throw new ArgumentException($"Could not load specified font : {Facade.CustomFont}");
            string fontName;
            if (!string.IsNullOrEmpty(Facade.CustomFont))
            {
                fontName = Facade.CustomFont;
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
                return;
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
            Aspose.Pdf.Forms.Form.RegisterDefaultAppearanceFont(field);
            field.DropWidgetAppearanceStrings();
            // A size the facade names is the field's own, as one handed over in a DefaultAppearance
            // is: it is pinned against the STATIC TextBoxField auto-fit clamps, which another
            // document in the process may have set (a field asked for 20 pt came back at 18.26).
            if (fontSize > 0) field.DaFontSizePinned = true;
            // A field that already carries a value keeps showing the OLD font until
            // its appearance is rebuilt — regenerate it under the new /DA.
            if (IsTextLike(field)) RedrawTextAppearance(field);
    }

    private static bool IsTextLike(Field field) => field.Type is Forms.FieldType.Text or Forms.FieldType.Choice
        or Forms.FieldType.ComboBox or Forms.FieldType.ListBox;

    private static void RedrawTextAppearance(Field field)
    {
        field.ResetDefaultAppearanceCache();
        field.Dict.Remove("AP");
        foreach (var kid in field.AllKids())
            kid.Remove("AP");
        field.GenerateAppearance();
    }

    /// <summary>The facade's background, horizontal alignment and - when no font is set, which carries the colour
    /// itself - text colour on a text or choice field: /MK /BG on each widget, /Q on the field, the colour in the
    /// /DA. True when any was written, so the appearance is drawn again.</summary>
    private bool ApplyFacadeTextStyle(Field field, bool fontApplied)
    {
        var widgets = field.AllKids().Where(kid => !kid.ContainsKey("T")).ToList();
        if (widgets.Count == 0) widgets.Add(field.Dict);
        var styled = false;
        if (Facade.BackgroundColor.A != 0)
        {
            foreach (var widget in widgets) WriteMk(widget, "BG", Facade.BackgroundColor);
            styled = true;
        }
        if (QuaddingFor(Facade.Alignment) is { } quadding)
        {
            field.Dict.Set("Q", new PdfInteger(quadding));
            // A widget's own /Q would override the field's.
            foreach (var widget in widgets.Where(w => !ReferenceEquals(w, field.Dict))) widget.Remove("Q");
            styled = true;
        }
        if (!fontApplied && Facade.TextColor.A != 0)
        {
            var color = Aspose.Pdf.Color.FromRgb(Facade.TextColor);
            var acroDa = (_document?.Reader.ResolveDict(_document.Reader.Catalog.Get("AcroForm"))?.Get("DA") as PdfString)?.ToText();
            var da = field.InheritedAppearanceString ?? acroDa ?? DefaultTextAppearanceString;
            field.Dict.Set("DA", new PdfString(Compat.Latin1.GetBytes(Field.WithTextColor(da, color))));
            foreach (var widget in widgets.Where(w => !ReferenceEquals(w, field.Dict)))
            {
                if (widget.Get("DA") is PdfString own)
                    widget.Set("DA", new PdfString(Compat.Latin1.GetBytes(Field.WithTextColor(own.ToText(), color))));
            }
            styled = true;
        }
        return styled;
    }

    /// <summary>The appearance string a text field is drawn with when the form gives it none.</summary>
    private const string DefaultTextAppearanceString = "/Helv 12 Tf 0 g";

    /// <summary>The /Q quadding of a facade horizontal alignment (the facade numbers left, centre and right
    /// from 1, /Q from 0); null for no alignment or a vertical one.</summary>
    internal static int? QuaddingFor(int alignment) => alignment switch
    {
        FormFieldFacade.AlignLeft => 0,
        FormFieldFacade.AlignCenter => 1,
        FormFieldFacade.AlignRight => 2,
        _ => null,
    };
}
