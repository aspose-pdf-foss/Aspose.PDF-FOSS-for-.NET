using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class TextBoxField : Field
{
    internal TextBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Static default applied to all newly-created TextBoxFields'
    /// auto-fit appearance. Stored only; tests set this to clamp the
    /// max font size for the whole form.</summary>
    public static new double MaxFontSize { get; set; }

    /// <summary>Static default applied to all newly-created TextBoxFields'
    /// auto-fit appearance — the lower bound for the auto-sized font. Stored
    /// only; together with <see cref="MaxFontSize"/> a caller can pin the
    /// appearance font size for the whole form.</summary>
    public static new double MinFontSize { get; set; }

    /// <summary>Static default applied to all newly-created TextBoxFields'
    /// FitIntoRectangle flag. Stored only.</summary>
    public static new bool FitIntoRectangle { get; set; }

    protected override void SetValue(string? value)
        => SetValue(value, validateFormat: true);

    /// <summary>Set the field value, optionally bypassing the /AA/F built-in
    /// format validation. The Facades Form.FillField path fills raw — a value
    /// the field's number/date formatter cannot parse is still stored and
    /// rendered verbatim — while the DOM Value setter keeps the
    /// reject-invalid semantics.</summary>
    internal void SetValue(string? value, bool validateFormat)
    {
        // A value that the field's built-in format action cannot accept (e.g. a
        // non-date string assigned to an AFDate-formatted field) is rejected,
        // leaving the previous value in place.
        if (validateFormat && !FieldFormatScript.IsValueValid(Dict, Reader, value ?? string.Empty))
            return;
        // A /MaxLen-limited text field stores at most that many characters —
        // an over-long assignment is truncated, exactly as typing past the
        // limit would be.
        if (value is not null
            && Reader.Resolve(Dict.Get("MaxLen")) is Aspose.Pdf.Core.PdfInteger ml
            && ml.Value > 0 && value.Length > ml.Value)
            value = value.Substring(0, (int)ml.Value);
        base.SetValue(value);
        // /V holds the raw typed value (per PDF convention). The visible
        // appearance uses what the field's /AA/F Format JavaScript would
        // produce — for the small set of built-in Acrobat formatters
        // (AFDate_FormatEx, AFNumber_Format, AFPercent_Format, ...) we
        // pattern-match the call and apply the equivalent transform.
        // Falls through to the raw value when no Format action is set or
        // when the script isn't a recognised built-in.
        var displayValue = FieldFormatScript.Apply(Dict, Reader, value ?? "");
        RegenerateAppearance(displayValue);

        // Multi-widget field: refresh each extra widget kid's /AP so every visual
        // widget shows the updated value (a bare widget kid carries no /T or /FT).
        foreach (var kid in AllKids())
        {
            if (kid.ContainsKey("T") || kid.ContainsKey("FT")) continue;
            if (Reader.Resolve(kid.Get("Rect")) is not PdfArray kr || kr.Count < 4) continue;
            var kidRect = Rectangle.FromPdfArray(kr);
            var kidAp = BuildWidgetApDict(kidRect.Width, kidRect.Height, kid);
            if (kidAp is not null) kid.Set("AP", kidAp);
        }

        // A value change fires the form's calculate event (Acrobat semantics).
        TriggerRecalculation();
    }

    /// <summary>Persist a calculated result into this field's /V and refresh its
    /// appearance (formatted through any /AA/F action), without re-entering the
    /// calculation trigger. Used by the form recalculation pass.</summary>
    internal void ApplyCalculatedValue(string rawValue)
    {
        base.SetValue(rawValue);
        // The calculation pass is not a user assignment — the field stays
        // eligible for future recalculation.
        ClearExplicitAssignment(Dict);
        var displayValue = FieldFormatScript.Apply(Dict, Reader, rawValue);
        RegenerateAppearance(displayValue);
        foreach (var kid in AllKids())
        {
            if (kid.ContainsKey("T") || kid.ContainsKey("FT")) continue;
            if (Reader.Resolve(kid.Get("Rect")) is not PdfArray kr || kr.Count < 4) continue;
            var kidRect = Rectangle.FromPdfArray(kr);
            var kidAp = BuildWidgetApDict(kidRect.Width, kidRect.Height, kid);
            if (kidAp is not null) kid.Set("AP", kidAp);
        }
    }

    /// <summary>When the widget rectangle changes, regenerate the /AP/N appearance so the
    /// value re-lays-out inside the new box. Only fires when
    /// the field already carries an appearance and a value — construction and empty fields
    /// are left untouched.</summary>
    internal override void OnRectChanged()
    {
        if (Dict.Get("AP") is not null)
        {
            var v = Value;
            if (!string.IsNullOrEmpty(v))
            {
                var displayValue = FieldFormatScript.Apply(Dict, Reader, v!);
                RegenerateAppearance(displayValue);
            }
        }

        // Static-XFA form: mirror the new widget geometry into the XFA template and
        // re-render the page's static form content at the new positions. Guarded so a
        // malformed XFA packet can never break a plain AcroForm rectangle move.
        try { OwnerDocument?.Form.SyncXfaWidgetGeometry(this); } catch { /* keep the /Rect change */ }
    }

    /// <summary>
    /// Rebuild the /AP/N appearance stream to display the current value text.
    /// Uses the font/size from the /DA (default appearance) string.
    /// </summary>
    /// <summary>True when the current /AP was generated by this session (Value
    /// setter / rect change / Form.Add) rather than loaded from the document.
    /// Such appearances are safe to rebuild when later property changes
    /// (Multiline, alignment, border) invalidate them.</summary>
    private bool _apAutoGenerated;

    // True once the generator placed this widget (see ApplyGeneratorDefaultAppearance).
    private bool _generatorPlaced;

    /// <summary>Set while a Unicode (embedded Type0) appearance is being generated:
    /// the face whose advances the shown CID run will carry, so measurement of
    /// beyond-WinAnsi chars agrees with the paint exactly.</summary>
    private byte[]? _uniAppearanceTtf;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], Aspose.Pdf.Text.GlyphOutlineParser>
        _uniWidthParsers = new();

    private protected static string Format(double v) =>
        v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Creates a new text box field on the specified page with the given rectangle.
    /// </summary>
    public TextBoxField(Page page, Rectangle rect)
        : base(BuildTextFieldDict(rect), page.Reader)
    {
    }

    /// <summary>
    /// Creates a new text box field associated with the document (page assigned via Form.Add).
    /// </summary>
    public TextBoxField(Document doc, Rectangle rect)
        : base(BuildTextFieldDict(rect), doc.Pages[1].Reader)
    {
    }

    /// <summary>Document-bound text box without a rectangle. Caller sets
    /// <see cref="Width"/> / <see cref="Height"/> or attaches via Form.Add.</summary>
    public TextBoxField(Document doc)
        : base(BuildTextFieldDict(new Rectangle(0, 0, 0, 0)), doc.Pages[1].Reader)
    {
    }

    /// <summary>Multi-widget text field — first rectangle is used for the widget
    /// dictionary; additional rectangles are stored on /Kids per the
    /// multi-widget contract.</summary>
    public TextBoxField(Page page, Rectangle[] rects)
        : base(BuildTextFieldDict(rects is { Length: > 0 } ? rects[0] : new Rectangle(0, 0, 0, 0)),
               page.Reader)
    {
        if (rects is null || rects.Length <= 1) return;
        var kids = new Aspose.Pdf.Core.PdfArray();
        for (var i = 1; i < rects.Length; i++)
        {
            var kid = new PdfDictionary();
            kid.Set("Subtype", new PdfName("Widget"));
            kid.Set("Rect", MakeRectArray(rects[i]));
            kids.Add(kid);
        }
        Dict.Set("Kids", kids);
    }

    /// <summary>
    /// Parameterless ctor — creates an unbound text box field. Layout
    /// dimensions come from <see cref="Width"/> / <see cref="Height"/>;
    /// the field is later attached to a page (via Form.Add) or to a layout
    /// container that places it (via <c>cell.Paragraphs.Add(field)</c>).
    /// </summary>
    public TextBoxField()
        : base(BuildTextFieldDict(new Rectangle(0, 0, 0, 0)), IO.PdfReader.Empty)
    {
        _daIsCtorDefault = true;
    }

    // True while the /DA is still the parameterless ctor's placeholder: a field
    // the generator places with that /DA auto-sizes its value ("/Helv 0 Tf"
    // is written for it); any caller-set DefaultAppearance wins.
    private bool _daIsCtorDefault;

    /// <summary>Switch a still-default /DA to auto-size (0 Tf) before the
    /// generator places the widget. No-op once a DefaultAppearance was set.</summary>
    internal void ApplyGeneratorDefaultAppearance()
    {
        _generatorPlaced = true;
        if (!_daIsCtorDefault) return;
        var da = Reader.Resolve(Dict.Get("DA")) is PdfString ps ? ps.ToText() : "";
        if (da != "/Helv 12 Tf 0 g") return;
        Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes("/Helv 0 Tf 0 g")));
        _daIsCtorDefault = false;
    }

    private static PdfDictionary BuildTextFieldDict(Rectangle rect)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Tx"));
        dict.Set("Rect", MakeRectArray(rect));
        dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes("/Helv 12 Tf 0 g")));
        return dict;
    }

    /// <summary>Layout width when the field is placed inside a layout
    /// container (table cell, paragraph). Stored only; the actual widget
    /// rectangle is set when the field is attached to a page.</summary>
    public new double Width { get; set; }

    /// <summary>Layout height when the field is placed inside a layout
    /// container. On a page-bound field (a real widget rectangle exists) setting it
    /// also resizes the rectangle like an annotation — bottom edge anchored — so a
    /// caller-supplied Height genuinely shrinks/grows the widget box.</summary>
    public new double Height
    {
        get => _layoutHeight > 0 ? _layoutHeight : base.Height;
        set
        {
            _layoutHeight = value;
            if (Rect is { } r && r.Width > 0 && r.Height > 0)
                base.Height = value;
        }
    }
    private double _layoutHeight;

    internal override (double w, double h) GeneratorBlockSize() => (Width, Height);

    /// <summary>Multiline flag (bit 13 of /Ff). Setter mirrors the
    /// existing read-only <see cref="IsMultiline"/>.</summary>
    public bool Multiline
    {
        get => IsMultiline;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 12);
            else f &= ~(1 << 12);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    public int MaxLen
    {
        get => (int)Dict.GetInt("MaxLen");
        set => Dict.Set("MaxLen", new Aspose.Pdf.Core.PdfInteger(value));
    }
    public bool IsMultiline => (FieldFlags & (1 << 12)) != 0;
    public bool IsPassword => (FieldFlags & (1 << 13)) != 0;

    /// <summary>
    /// Vertical alignment of the text inside the field's appearance.
    /// Stored in-memory; persisted to /AP regeneration
    /// when the appearance pipeline reads this hint.
    /// </summary>
    public VerticalAlignment TextVerticalAlignment { get; set; } = VerticalAlignment.None;

    /// <summary>True when the Tx field has the rich-text flag (bit 26 of /Ff) set.</summary>
    public bool IsRichText => (FieldFlags & (1 << 25)) != 0;

    /// <summary>
    /// Text justification: 0 = Left, 1 = Center, 2 = Right.
    /// </summary>
    public int Justification => (int)Dict.GetInt("Q");

    /// <summary>Bit 25 of /Ff (Comb): when set, the field is divided into
    /// MaxLen equal-width combs. Requires Multiline=false and MaxLen>0.</summary>
    public bool ForceCombs
    {
        get => (FieldFlags & (1 << 24)) != 0;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 24);
            else f &= ~(1 << 24);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Inverse of bit 24 of /Ff (DoNotScroll). When false the field
    /// will not scroll horizontally / vertically beyond its visible region.</summary>
    public bool Scrollable
    {
        get => (FieldFlags & (1 << 23)) == 0;
        set
        {
            var f = FieldFlags;
            if (value) f &= ~(1 << 23);
            else f |= (1 << 23);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Inverse of bit 23 of /Ff (DoNotSpellCheck).</summary>
    public bool SpellCheck
    {
        get => (FieldFlags & (1 << 22)) == 0;
        set
        {
            var f = FieldFlags;
            if (value) f &= ~(1 << 22);
            else f |= (1 << 22);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>The text value stored in /V.</summary>
    public override string? Value
    {
        get
        {
            // Auto-calculate: a field with an /AA/C calculate action reports its
            // recomputed value (Acrobat auto-calculates on read). An explicitly
            // assigned value wins over the calculation.
            if (!IsExplicitlyAssigned(Dict) && Dict.Get("AA") is not null
                && FieldCalculateScript.ComputeValue(Dict, Reader) is { } computed)
                return computed;
            if (Reader.Resolve(Dict.Get("V")) is PdfString s) return s.ToText();
            // /V is inheritable, but only a PURE widget kid (no /T of its own)
            // reads it up the /Parent chain — a NAMED child field's missing /V
            // stays null (an ancestor's value is not this field's; inheriting
            // one would push bogus values into e.g. the XFA datasets sync).
            if (!Dict.ContainsKey("T"))
            {
                var parent = Reader.ResolveDict(Dict.Get("Parent"));
                while (parent is not null)
                {
                    if (Reader.Resolve(parent.Get("V")) is PdfString ps) return ps.ToText();
                    parent = Reader.ResolveDict(parent.Get("Parent"));
                }
            }
            return null;
        }
        set => SetValue(value);
    }

    /// <summary>Encode <paramref name="code"/> as a placeholder barcode in the
    /// field value. The FOSS pipeline does not currently render a glyph form;
    /// the string is stored verbatim so callers can round-trip the value.</summary>
    public void AddBarcode(string code) => SetValue(code);

    /// <summary>Stub for image overlay support. Cross-platform — no GDI
    /// rasterization happens.</summary>
    public void AddImage(System.Drawing.Image image) { _ = image; }
}
