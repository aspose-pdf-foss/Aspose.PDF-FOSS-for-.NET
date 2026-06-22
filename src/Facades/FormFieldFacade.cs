namespace Aspose.Pdf.Facades;

/// <summary>
/// Represents the visual appearance attributes of a form field.
/// Returned by <see cref="Form.GetFieldFacade"/>.
/// </summary>
public sealed class FormFieldFacade
{
    // ── Alignment constants ───────────────────────────────────────────────
    public const int AlignUndefined = 0;
    public const int AlignLeft = 1;
    public const int AlignCenter = 2;
    public const int AlignRight = 3;
    public const int AlignTop = 4;
    public const int AlignMiddle = 5;
    public const int AlignBottom = 6;
    public const int AlignJustified = 7;

    // ── Border-style constants ────────────────────────────────────────────
    public const int BorderStyleUndefined = 0;
    public const int BorderStyleSolid = 1;
    public const int BorderStyleDashed = 2;
    public const int BorderStyleBeveled = 3;
    public const int BorderStyleInset = 4;
    public const int BorderStyleUnderline = 5;

    // ── Border-width constants ────────────────────────────────────────────
    public const float BorderWidthUndefined = 0f;
    /// <summary>Alias for <see cref="BorderWidthUndefined"/>. Retained for API compatibility.</summary>
    public const float BorderWidthUndified = 0f;
    public const float BorderWidthThin = 1f;
    public const float BorderWidthMedium = 2f;
    public const float BorderWidthThick = 3f;

    // ── Check-box style constants ─────────────────────────────────────────
    public const int CheckBoxStyleUndefined = 0;
    public const int CheckBoxStyleCheck = 1;
    public const int CheckBoxStyleCircle = 2;
    public const int CheckBoxStyleCross = 3;
    public const int CheckBoxStyleDiamond = 4;
    public const int CheckBoxStyleSquare = 5;
    public const int CheckBoxStyleStar = 6;

    /// <summary>The caption (alternate name / tooltip / TU entry) of the field.</summary>
    public string? Caption { get; set; }

    /// <summary>Font family used by the field. Defaults to <see cref="FontStyle.Helvetica"/>.</summary>
    public FontStyle Font { get; set; } = FontStyle.Helvetica;

    /// <summary>Optional override for the font name, used when <see cref="Font"/> does not name a known family.</summary>
    public string? CustomFont { get; set; }

    /// <summary>The font size of the field.</summary>
    public float FontSize { get; set; }

    /// <summary>Border width in points. See <see cref="BorderWidthThin"/> etc.</summary>
    public float BorderWidth { get; set; }

    /// <summary>Border style. See <see cref="BorderStyleSolid"/> etc.</summary>
    public int BorderStyle { get; set; }

    /// <summary>Border color.</summary>
    public System.Drawing.Color BorderColor { get; set; }

    /// <summary>Background color.</summary>
    public System.Drawing.Color BackgroundColor { get; set; }

    /// <summary>Background color. Retained alongside <see cref="BackgroundColor"/> for API compatibility (note the spelling).</summary>
    public System.Drawing.Color BackgroudColor { get; set; }

    /// <summary>Text color.</summary>
    public System.Drawing.Color TextColor { get; set; }

    /// <summary>The field's position rectangle, in PDF integer points.</summary>
    public System.Drawing.Rectangle Box { get; set; }

    /// <summary>The page number where the field appears (1-based).</summary>
    public int PageNumber { get; set; }

    /// <summary>The field alignment. See <see cref="AlignLeft"/> etc.</summary>
    public int Alignment { get; set; }

    /// <summary>Rotation in degrees (0, 90, 180, 270).</summary>
    public int Rotation { get; set; }

    /// <summary>Button visual style — combination of <see cref="CheckBoxStyleCheck"/> etc.</summary>
    public int ButtonStyle { get; set; }

    /// <summary>List of items for choice fields (combobox / listbox).</summary>
    public string[]? Items { get; set; }

    /// <summary>Export-value pairs for items, paired with <see cref="Items"/>.</summary>
    public string[][]? ExportItems { get; set; }

    /// <summary>Field position as a 4-element array [llx, lly, urx, ury] in points.</summary>
    public float[]? Position { get; set; }

    /// <summary>Text encoding used by the field's font.</summary>
    public EncodingType TextEncoding { get; set; } = EncodingType.Winansi;

    /// <summary>Reset every property to its default value.</summary>
    public void Reset()
    {
        Caption = null;
        Font = FontStyle.Helvetica;
        CustomFont = null;
        FontSize = 0f;
        BorderWidth = 0f;
        BorderStyle = BorderStyleUndefined;
        BorderColor = default;
        BackgroundColor = default;
        BackgroudColor = default;
        TextColor = default;
        Box = default;
        PageNumber = 0;
        Alignment = AlignUndefined;
        Rotation = 0;
        ButtonStyle = CheckBoxStyleUndefined;
        Items = null;
        ExportItems = null;
        Position = null;
        TextEncoding = EncodingType.Winansi;
    }
}
