using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms;

/// <summary>
/// Creates new interactive form fields and adds them to a PDF document.
/// </summary>
public sealed class FormFieldBuilder
{
    private readonly Document _document;

    public FormFieldBuilder(Document document)
    {
        _document = document;
    }

    /// <summary>
    /// Add a text field to a page.
    /// </summary>
    /// <param name="page">The page to add the field to.</param>
    /// <param name="name">The field name.</param>
    /// <param name="rect">The field rectangle on the page.</param>
    /// <param name="defaultValue">Optional initial value.</param>
    /// <param name="fontSize">Font size for the field text (default 12).</param>
    /// <returns>The page index (0-based) where the field was added.</returns>
    public void AddTextField(Page page, string name, Rectangle rect,
        string? defaultValue = null, double fontSize = 12)
    {
        var fieldDict = new PdfDictionary();
        fieldDict.Set("Type", new PdfName("Annot"));
        fieldDict.Set("Subtype", new PdfName("Widget"));
        fieldDict.Set("FT", new PdfName("Tx"));
        fieldDict.Set("T", new PdfString(Encoding.Latin1.GetBytes(name)));
        fieldDict.Set("Rect", MakeRectArray(rect));

        // Default appearance: Helvetica, given font size, black
        fieldDict.Set("DA", new PdfString(Encoding.Latin1.GetBytes($"/Helv {F(fontSize)} Tf 0 g")));

        if (defaultValue is not null)
        {
            fieldDict.Set("V", new PdfString(Encoding.Latin1.GetBytes(defaultValue)));
            fieldDict.Set("DV", new PdfString(Encoding.Latin1.GetBytes(defaultValue)));
        }

        // Generate a simple appearance stream
        var apStream = BuildTextAppearance(rect, defaultValue ?? "", fontSize);
        SetAppearance(fieldDict, apStream, rect);

        // Add widget annotation to the page
        AddAnnotToPage(page, fieldDict);

        // Add field to AcroForm
        AddToAcroForm(fieldDict);
    }

    /// <summary>
    /// Add a checkbox field to a page.
    /// </summary>
    public void AddCheckBox(Page page, string name, Rectangle rect, bool isChecked = false)
    {
        var fieldDict = new PdfDictionary();
        fieldDict.Set("Type", new PdfName("Annot"));
        fieldDict.Set("Subtype", new PdfName("Widget"));
        fieldDict.Set("FT", new PdfName("Btn"));
        fieldDict.Set("T", new PdfString(Encoding.Latin1.GetBytes(name)));
        fieldDict.Set("Rect", MakeRectArray(rect));

        var value = isChecked ? "Yes" : "Off";
        fieldDict.Set("V", new PdfName(value));
        fieldDict.Set("AS", new PdfName(value));

        // Build appearance dictionaries for Yes and Off states
        var apDict = new PdfDictionary();
        var nDict = new PdfDictionary();

        var yesAp = BuildCheckmarkAppearance(rect, true);
        var offAp = BuildCheckmarkAppearance(rect, false);

        nDict.Set("Yes", MakeAppearanceStream(yesAp, rect));
        nDict.Set("Off", MakeAppearanceStream(offAp, rect));
        apDict.Set("N", nDict);
        fieldDict.Set("AP", apDict);

        AddAnnotToPage(page, fieldDict);
        AddToAcroForm(fieldDict);
    }

    /// <summary>
    /// Add a choice field (dropdown/combo box) to a page.
    /// </summary>
    public void AddComboBox(Page page, string name, Rectangle rect,
        string[] options, string? selectedValue = null, double fontSize = 12)
    {
        var fieldDict = new PdfDictionary();
        fieldDict.Set("Type", new PdfName("Annot"));
        fieldDict.Set("Subtype", new PdfName("Widget"));
        fieldDict.Set("FT", new PdfName("Ch"));
        fieldDict.Set("Ff", new PdfInteger(1 << 17)); // Combo flag
        fieldDict.Set("T", new PdfString(Encoding.Latin1.GetBytes(name)));
        fieldDict.Set("Rect", MakeRectArray(rect));
        fieldDict.Set("DA", new PdfString(Encoding.Latin1.GetBytes($"/Helv {F(fontSize)} Tf 0 g")));

        // Options array
        var optArr = new PdfArray();
        foreach (var opt in options)
            optArr.Add(new PdfString(Encoding.Latin1.GetBytes(opt)));
        fieldDict.Set("Opt", optArr);

        if (selectedValue is not null)
            fieldDict.Set("V", new PdfString(Encoding.Latin1.GetBytes(selectedValue)));

        var apStream = BuildTextAppearance(rect, selectedValue ?? "", fontSize);
        SetAppearance(fieldDict, apStream, rect);

        AddAnnotToPage(page, fieldDict);
        AddToAcroForm(fieldDict);
    }

    /// <summary>
    /// Add a radio button group with multiple options to a page.
    /// </summary>
    /// <param name="page">The page to add the field to.</param>
    /// <param name="name">The field name.</param>
    /// <param name="optionRects">Rectangles for each radio button option.</param>
    /// <param name="optionValues">Value names for each option.</param>
    /// <param name="selectedIndex">Index of the initially selected option (default 0).</param>
    public void AddRadioButton(Page page, string name, Rectangle[] optionRects,
        string[] optionValues, int selectedIndex = 0)
    {
        if (optionRects.Length != optionValues.Length)
            throw new ArgumentException("optionRects and optionValues must have the same length.");
        if (optionRects.Length == 0)
            throw new ArgumentException("At least one option is required.");

        // Parent field (non-terminal) — carries /FT, /T, /V, /Ff
        var parentDict = new PdfDictionary();
        parentDict.Set("FT", new PdfName("Btn"));
        parentDict.Set("Ff", new PdfInteger(1 << 15)); // Bit 16: Radio flag (PDF spec Table 226)
        parentDict.Set("T", new PdfString(Encoding.Latin1.GetBytes(name)));

        var selectedValue = (selectedIndex >= 0 && selectedIndex < optionValues.Length)
            ? optionValues[selectedIndex]
            : "Off";
        parentDict.Set("V", new PdfName(selectedValue));

        // Kids array — one widget per option
        var kids = new PdfArray();
        for (int i = 0; i < optionValues.Length; i++)
        {
            var optValue = optionValues[i];
            var optRect = optionRects[i];
            var isSelected = i == selectedIndex;

            var widgetDict = new PdfDictionary();
            widgetDict.Set("Type", new PdfName("Annot"));
            widgetDict.Set("Subtype", new PdfName("Widget"));
            widgetDict.Set("Rect", MakeRectArray(optRect));
            widgetDict.Set("AS", new PdfName(isSelected ? optValue : "Off"));

            // Appearance dict with named states
            var apDict = new PdfDictionary();
            var nDict = new PdfDictionary();
            nDict.Set(optValue, MakeAppearanceStream(BuildRadioButtonAppearance(optRect, true), optRect));
            nDict.Set("Off", MakeAppearanceStream(BuildRadioButtonAppearance(optRect, false), optRect));
            apDict.Set("N", nDict);
            widgetDict.Set("AP", apDict);

            kids.Add(widgetDict);
            AddAnnotToPage(page, widgetDict);
        }

        parentDict.Set("Kids", kids);
        AddToAcroForm(parentDict);
    }

    /// <summary>
    /// Add a list box field to a page.
    /// </summary>
    /// <param name="page">The page to add the field to.</param>
    /// <param name="name">The field name.</param>
    /// <param name="rect">The field rectangle on the page.</param>
    /// <param name="options">The list of option strings.</param>
    /// <param name="selectedValue">Optional initially selected value.</param>
    /// <param name="fontSize">Font size for the option text (default 12).</param>
    public void AddListBox(Page page, string name, Rectangle rect,
        string[] options, string? selectedValue = null, double fontSize = 12)
    {
        var fieldDict = new PdfDictionary();
        fieldDict.Set("Type", new PdfName("Annot"));
        fieldDict.Set("Subtype", new PdfName("Widget"));
        fieldDict.Set("FT", new PdfName("Ch"));
        // No Combo flag — Ff = 0 means list box
        fieldDict.Set("T", new PdfString(Encoding.Latin1.GetBytes(name)));
        fieldDict.Set("Rect", MakeRectArray(rect));
        fieldDict.Set("DA", new PdfString(Encoding.Latin1.GetBytes($"/Helv {F(fontSize)} Tf 0 g")));

        // Options array
        var optArr = new PdfArray();
        foreach (var opt in options)
            optArr.Add(new PdfString(Encoding.Latin1.GetBytes(opt)));
        fieldDict.Set("Opt", optArr);

        if (selectedValue is not null)
            fieldDict.Set("V", new PdfString(Encoding.Latin1.GetBytes(selectedValue)));

        var apStream = BuildListBoxAppearance(rect, options, selectedValue, fontSize);
        SetAppearance(fieldDict, apStream, rect);

        AddAnnotToPage(page, fieldDict);
        AddToAcroForm(fieldDict);
    }

    /// <summary>
    /// Add an empty signature field placeholder to a page.
    /// </summary>
    /// <param name="page">The page to add the field to.</param>
    /// <param name="name">The field name.</param>
    /// <param name="rect">The field rectangle on the page.</param>
    public void AddSignatureField(Page page, string name, Rectangle rect)
    {
        var fieldDict = new PdfDictionary();
        fieldDict.Set("Type", new PdfName("Annot"));
        fieldDict.Set("Subtype", new PdfName("Widget"));
        fieldDict.Set("FT", new PdfName("Sig"));
        fieldDict.Set("T", new PdfString(Encoding.Latin1.GetBytes(name)));
        fieldDict.Set("Rect", MakeRectArray(rect));

        var apStream = BuildSignatureAppearance(rect);
        SetAppearance(fieldDict, apStream, rect);

        AddAnnotToPage(page, fieldDict);
        AddToAcroForm(fieldDict);
    }

    // ── Internal helpers ─────────────────────────────────────────────

    private void AddToAcroForm(PdfDictionary fieldDict)
    {
        var catalog = _document.Catalog;
        var reader = _document.Reader;
        // /AcroForm (and its /Fields) are frequently indirect references, so they must
        // be resolved before use — a plain `as PdfDictionary`/`as PdfArray` on the raw
        // entry returns null for an indirect ref, which would silently create a SECOND
        // AcroForm and orphan the real one (losing the existing fields from lookups).
        var acroForm = reader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null)
        {
            acroForm = new PdfDictionary();
            catalog.Set("AcroForm", acroForm);
        }

        var fields = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fields is null)
        {
            fields = new PdfArray();
            acroForm.Set("Fields", fields);
        }

        fields.Add(fieldDict);

        // Set NeedAppearances to false (we provide our own appearances)
        acroForm.Set("NeedAppearances", PdfBoolean.False);

        // Ensure default resources include Helvetica
        EnsureDefaultResources(acroForm, reader);
    }

    private static void EnsureDefaultResources(PdfDictionary acroForm, IO.PdfReader reader)
    {
        var dr = reader.ResolveDict(acroForm.Get("DR"));
        if (dr is null)
        {
            dr = new PdfDictionary();
            acroForm.Set("DR", dr);
        }

        var fontDict = reader.ResolveDict(dr.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            dr.Set("Font", fontDict);
        }

        if (!fontDict.ContainsKey("Helv"))
        {
            var helvetica = new PdfDictionary();
            helvetica.Set("Type", new PdfName("Font"));
            helvetica.Set("Subtype", new PdfName("Type1"));
            helvetica.Set("BaseFont", new PdfName("Helvetica"));
            helvetica.Set("Encoding", new PdfName("WinAnsiEncoding"));
            fontDict.Set("Helv", helvetica);
        }
    }

    private static void AddAnnotToPage(Page page, PdfDictionary annotDict)
    {
        // /Annots is commonly an indirect reference; resolve it before appending so
        // we extend the real array rather than orphan it behind a fresh one.
        var annots = page.Reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annots is null)
        {
            annots = new PdfArray();
            page.Dict.Set("Annots", annots);
        }
        annots.Add(annotDict);
    }

    private static byte[] BuildTextAppearance(Rectangle rect, string text, double fontSize)
    {
        var w = rect.Width;
        var h = rect.Height;
        var sb = new StringBuilder();

        // Border
        sb.Append($"1 w 0.7 0.7 0.7 rg 0 0 {F(w)} {F(h)} re f ");
        sb.Append($"0 0 0 RG 0 0 {F(w)} {F(h)} re S ");

        // Text
        if (!string.IsNullOrEmpty(text))
        {
            var y = (h - fontSize) / 2; // vertical center
            sb.Append($"BT /Helv {F(fontSize)} Tf 2 {F(y)} Td ({EscapePdf(text)}) Tj ET");
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] BuildCheckmarkAppearance(Rectangle rect, bool isChecked)
    {
        var w = rect.Width;
        var h = rect.Height;
        var sb = new StringBuilder();

        // Background
        sb.Append($"1 w 0.9 0.9 0.9 rg 0 0 {F(w)} {F(h)} re f ");
        sb.Append($"0 0 0 RG 0 0 {F(w)} {F(h)} re S ");

        if (isChecked)
        {
            // Draw an X
            var inset = Math.Min(w, h) * 0.2;
            sb.Append($"2 w ");
            sb.Append($"{F(inset)} {F(inset)} m {F(w - inset)} {F(h - inset)} l S ");
            sb.Append($"{F(inset)} {F(h - inset)} m {F(w - inset)} {F(inset)} l S ");
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void SetAppearance(PdfDictionary fieldDict, byte[] streamBytes, Rectangle rect)
    {
        var apStream = MakeAppearanceStream(streamBytes, rect);

        var apDict = new PdfDictionary();
        apDict.Set("N", apStream);
        fieldDict.Set("AP", apDict);
    }

    private static PdfStream MakeAppearanceStream(byte[] streamBytes, Rectangle rect)
    {
        var apDict = new PdfDictionary();
        apDict.Set("Type", new PdfName("XObject"));
        apDict.Set("Subtype", new PdfName("Form"));
        apDict.Set("BBox", MakeRectArray(new Rectangle(0, 0, rect.Width, rect.Height)));
        apDict.Set("Length", new PdfInteger(streamBytes.Length));

        // Add Helvetica font resource for text appearances
        var fontDict = new PdfDictionary();
        var helvetica = new PdfDictionary();
        helvetica.Set("Type", new PdfName("Font"));
        helvetica.Set("Subtype", new PdfName("Type1"));
        helvetica.Set("BaseFont", new PdfName("Helvetica"));
        fontDict.Set("Helv", helvetica);
        var resources = new PdfDictionary();
        resources.Set("Font", fontDict);
        apDict.Set("Resources", resources);

        return new PdfStream(apDict, streamBytes);
    }

    private static PdfArray MakeRectArray(Rectangle rect)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        return arr;
    }

    private static string EscapePdf(string text) =>
        text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static byte[] BuildRadioButtonAppearance(Rectangle rect, bool isSelected)
    {
        var w = rect.Width;
        var h = rect.Height;
        var sb = new StringBuilder();

        var cx = w / 2;
        var cy = h / 2;
        var r = Math.Min(cx, cy) * 0.8;

        // Draw circle outline using Bézier curves (approximation)
        var k = r * 0.5523; // magic constant for circle approximation
        sb.Append("0.9 0.9 0.9 rg 0 0 0 RG 1 w ");
        sb.Append($"{F(cx + r)} {F(cy)} m ");
        sb.Append($"{F(cx + r)} {F(cy + k)} {F(cx + k)} {F(cy + r)} {F(cx)} {F(cy + r)} c ");
        sb.Append($"{F(cx - k)} {F(cy + r)} {F(cx - r)} {F(cy + k)} {F(cx - r)} {F(cy)} c ");
        sb.Append($"{F(cx - r)} {F(cy - k)} {F(cx - k)} {F(cy - r)} {F(cx)} {F(cy - r)} c ");
        sb.Append($"{F(cx + k)} {F(cy - r)} {F(cx + r)} {F(cy - k)} {F(cx + r)} {F(cy)} c ");
        sb.Append("B "); // fill and stroke

        if (isSelected)
        {
            // Draw filled inner circle
            var ir = r * 0.5;
            var ik = ir * 0.5523;
            sb.Append("0 0 0 rg ");
            sb.Append($"{F(cx + ir)} {F(cy)} m ");
            sb.Append($"{F(cx + ir)} {F(cy + ik)} {F(cx + ik)} {F(cy + ir)} {F(cx)} {F(cy + ir)} c ");
            sb.Append($"{F(cx - ik)} {F(cy + ir)} {F(cx - ir)} {F(cy + ik)} {F(cx - ir)} {F(cy)} c ");
            sb.Append($"{F(cx - ir)} {F(cy - ik)} {F(cx - ik)} {F(cy - ir)} {F(cx)} {F(cy - ir)} c ");
            sb.Append($"{F(cx + ik)} {F(cy - ir)} {F(cx + ir)} {F(cy - ik)} {F(cx + ir)} {F(cy)} c ");
            sb.Append("f ");
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] BuildListBoxAppearance(Rectangle rect, string[] options,
        string? selected, double fontSize)
    {
        var w = rect.Width;
        var h = rect.Height;
        var sb = new StringBuilder();

        // Background
        sb.Append($"1 w 0.95 0.95 0.95 rg 0 0 {F(w)} {F(h)} re f ");
        sb.Append($"0 0 0 RG 0 0 {F(w)} {F(h)} re S ");

        // Draw each option
        var lineHeight = fontSize * 1.2;
        var y = h - lineHeight;
        foreach (var opt in options)
        {
            if (y < -lineHeight) break; // beyond visible area

            // Highlight selected option
            if (opt == selected)
            {
                sb.Append($"0.6 0.75 0.95 rg 0 {F(y)} {F(w)} {F(lineHeight)} re f ");
                sb.Append("0 0 0 rg ");
            }

            var textY = y + (lineHeight - fontSize) / 2;
            sb.Append($"BT /Helv {F(fontSize)} Tf 2 {F(textY)} Td ({EscapePdf(opt)}) Tj ET ");

            y -= lineHeight;
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] BuildSignatureAppearance(Rectangle rect)
    {
        var w = rect.Width;
        var h = rect.Height;
        var sb = new StringBuilder();

        // Dashed border rectangle
        sb.Append($"0.95 0.95 0.95 rg 0 0 {F(w)} {F(h)} re f ");
        sb.Append($"0.5 0.5 0.5 RG [4 2] 0 d 1 w 0 0 {F(w)} {F(h)} re S ");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string F(double v) =>
        v.ToString("G6", CultureInfo.InvariantCulture);
}
