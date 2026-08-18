using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public class ChoiceField : Field
{
    internal ChoiceField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public ChoiceField(Document doc)
        : base(BuildChoiceDict(new Rectangle(0, 0, 0, 0)), doc.Pages[1].Reader)
    {
    }

    public ChoiceField(Document doc, Rectangle rect)
        : base(BuildChoiceDict(rect), doc.Pages[1].Reader)
    {
    }

    public ChoiceField(Page page, Rectangle rect)
        : base(BuildChoiceDict(rect), page.Reader)
    {
    }

    private static PdfDictionary BuildChoiceDict(Rectangle rect)
    {
        var d = new PdfDictionary();
        d.Set("Type", new PdfName("Annot"));
        d.Set("Subtype", new PdfName("Widget"));
        d.Set("FT", new PdfName("Ch"));
        d.Set("Rect", MakeRectArray(rect));
        return d;
    }

    /// <summary>Draw the field appearance — the selected value for a combo box,
    /// or the full /Opt list (with a highlight rect on the selected entry) for a
    /// list box, clipped to the field rectangle, with any /MK background and
    /// border. Radio buttons override this with a per-option disc.</summary>
    internal override void GenerateAppearance()
    {
        if (Reader.ResolveDict(Dict.Get("AP")) is not null) return;
        if (!TryWidgetSize(out var w, out var h)) return;
        ParseDefaultAppearance(out var fontName, out var fontSize);
        // Colour operator from the field /DA (falls back to black fill).
        var daText = Reader.Resolve(Dict.Get("DA")) is PdfString daStr ? daStr.ToText() : "/Helv 12 Tf 0 g";
        var colorOp = ExtractDaColor(daText);
        if (string.IsNullOrWhiteSpace(colorOp)) colorOp = "0 g";

        var sb = new System.Text.StringBuilder();
        sb.Append("/Tx BMC q ");
        sb.Append(BuildMkBackgroundAndBorder(w, h));
        // Clip to the interior so long values don't overflow the widget box.
        sb.Append($"1 1 {FmtNum(w - 2)} {FmtNum(h - 2)} re W n ");

        if (IsCombo)
        {
            // Combo box: render only the selected value (single visible line).
            var text = Value ?? string.Empty;
            if (text.Length > 0)
            {
                var escaped = EscapePdfText(text);
                sb.Append($"BT /{fontName} {FmtNum(fontSize)} Tf {colorOp} " +
                          $"2 {FmtNum(h / 2 - fontSize * 0.3)} Td ({escaped}) Tj ET ");
            }
        }
        else
        {
            // List box: each /Opt entry on its own line, top-to-bottom; selected
            // entries get a light-blue highlight rectangle behind their text.
            var opts = ReadOptionDisplayValues();
            if (opts.Count > 0)
            {
                var selected = new System.Collections.Generic.HashSet<string>(SelectedValues);
                // Line height tracks the Acrobat default — slightly more than the
                // glyph nominal so descenders don't clip on the row below.
                var lineHeight = fontSize * 1.15;
                // Acrobat draws the first option at the top of the box.
                for (var i = 0; i < opts.Count; i++)
                {
                    var top = h - 1 - i * lineHeight;
                    var rowY = top - lineHeight;
                    if (rowY < 1) break; // stop once we're past the bottom edge
                    if (selected.Contains(opts[i]))
                    {
                        // Acrobat's selection-highlight blue (#5BA9E1-ish at 50%).
                        sb.Append($"0.6 0.74 0.86 rg 1 {FmtNum(rowY)} {FmtNum(w - 2)} {FmtNum(lineHeight)} re f ");
                    }
                    var escaped = EscapePdfText(opts[i]);
                    var textY = rowY + (lineHeight - fontSize) / 2 + fontSize * 0.25;
                    sb.Append($"BT /{fontName} {FmtNum(fontSize)} Tf 0 g " +
                              $"2 {FmtNum(textY)} Td ({escaped}) Tj ET ");
                }
            }
        }
        sb.Append("Q EMC");

        var ap = new PdfDictionary();
        ap.Set("N", MakeApXObject(sb.ToString(), w, h, BuildChoiceAppearanceResources(fontName)));
        Dict.Set("AP", ap);
    }

    /// <summary>Font resources for a choice-field appearance. When the /DA names an
    /// embedded resource (the <c>C{n}_{m}</c> alias produced by
    /// <see cref="Form.EmbedDefaultAppearanceFont"/>), reference that actual font
    /// from the AcroForm /DR so the glyph program is present; otherwise synthesise a
    /// Standard-14 entry under the name.</summary>
    private PdfDictionary BuildChoiceAppearanceResources(string fontName)
    {
        if (fontName.Length > 1 && fontName[0] == 'C' && fontName.Contains('_'))
        {
            try
            {
                var acroForm = Reader.ResolveDict(Reader.Catalog.Get("AcroForm"));
                var dr = acroForm is null ? null : Reader.ResolveDict(acroForm.Get("DR"));
                var drFonts = dr is null ? null : Reader.ResolveDict(dr.Get("Font"));
                if (drFonts is not null && drFonts.Get(fontName) is { } embedded)
                {
                    var fonts = new PdfDictionary();
                    fonts.Set(fontName, embedded);
                    var res = new PdfDictionary();
                    res.Set("Font", fonts);
                    return res;
                }
            }
            catch (System.InvalidOperationException) { /* detached field */ }
        }
        return MakeStandardFontResources(fontName);
    }

    /// <summary>Read the /Opt entries as a flat list of display strings.
    /// Each entry may be a string (display = export) or a [export, display] pair.</summary>
    private System.Collections.Generic.List<string> ReadOptionDisplayValues()
    {
        var list = new System.Collections.Generic.List<string>();
        if (Reader.Resolve(Dict.Get("Opt")) is not PdfArray arr) return list;
        foreach (var item in arr)
        {
            var resolved = Reader.Resolve(item);
            if (resolved is PdfString s) list.Add(s.ToText());
            else if (resolved is PdfArray pair && pair.Count >= 2
                     && Reader.Resolve(pair[1]) is PdfString display)
                list.Add(display.ToText());
        }
        return list;
    }

    public bool IsCombo => (FieldFlags & (1 << 17)) != 0;
    public bool IsEditable => (FieldFlags & (1 << 18)) != 0;
    public bool IsMultiSelect => (FieldFlags & (1 << 21)) != 0;
    public bool IsSorted => (FieldFlags & (1 << 19)) != 0;

    /// <summary>Get/set MultiSelect (/Ff bit 22 — 0-based 21).</summary>
    public bool MultiSelect
    {
        get => IsMultiSelect;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 21);
            else f &= ~(1 << 21);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Get/set CommitOnSelChange (/Ff bit 27 — 0-based 26). When true,
    /// each selection change immediately commits / fires events.</summary>
    public bool CommitImmediately
    {
        get => (FieldFlags & (1 << 26)) != 0;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 26);
            else f &= ~(1 << 26);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>0-based selected-option indices (public-API shape int[] sibling
    /// of <see cref="SelectedIndices"/>; setter rewrites /V from the matching
    /// options).</summary>
    public int[] SelectedItems
    {
        get => SelectedIndices.ToArray();
        set
        {
            if (value is null || value.Length == 0)
            {
                SelectedValues = [];
                return;
            }
            var opts = Options;
            var picked = new List<string>();
            foreach (var i in value)
                if (i >= 0 && i < opts.Count) picked.Add(opts[i + 1].Value);
            SelectedValues = picked;
        }
    }

    /// <summary>
    /// The currently selected values. For single-select, returns an array with one element.
    /// For multi-select (when /V is an array), returns all selected values.
    /// Returns empty array if no selection.
    /// Setter writes the new selection to the field's /V entry.
    /// </summary>
    public IReadOnlyList<string> SelectedValues
    {
        get
        {
            var v = Reader.Resolve(Dict.Get("V"));
            if (v is null or PdfNull) return [];

            if (v is PdfArray arr)
            {
                var result = new List<string>();
                foreach (var item in arr)
                {
                    var resolved = Reader.Resolve(item);
                    var text = resolved switch
                    {
                        PdfString s => s.ToText(),
                        PdfName n => n.Value,
                        _ => null,
                    };
                    if (text is not null)
                        result.Add(text);
                }
                return result;
            }

            var single = v switch
            {
                PdfString s => s.ToText(),
                PdfName n => n.Value,
                _ => null,
            };
            return single is not null ? [single] : [];
        }
        set
        {
            if (value is null || value.Count == 0)
                Dict.Set("V", PdfNull.Instance);
            else if (value.Count == 1)
                Dict.Set("V", new PdfString(System.Text.Encoding.UTF8.GetBytes(value[0])));
            else
            {
                // Multiple selected values → /V array
                var arr = new PdfArray();
                foreach (var val in value)
                    arr.Add(new PdfString(System.Text.Encoding.UTF8.GetBytes(val)));
                Dict.Set("V", arr);
            }
            // Mark the field dirty so an incremental save persists the selection change.
            if (OwnerDocument is not null && ObjectNumber >= 0)
                OwnerDocument.MarkDirty(ObjectNumber, Dict);
        }
    }

    /// <summary>
    /// The 0-based indices of selected values within the Options list.
    /// </summary>
    public IReadOnlyList<int> SelectedIndices
    {
        get
        {
            var selected = new HashSet<string>(SelectedValues);
            if (selected.Count == 0) return [];

            var result = new List<int>();
            int i = 0;
            foreach (var opt in Options)
            {
                if (selected.Contains(opt.ExportValue))
                    result.Add(i);
                i++;
            }
            return result;
        }
    }

    /// <summary>
    /// The collection of choice options. Each option has an export value and display name.
    /// Mutations on the returned collection (Add / Remove / Clear) write through to
    /// the field's /Opt array.
    /// </summary>
    public virtual OptionCollection Options => new(this);

    /// <summary>
    /// 1-based index of the currently selected option, or -1 when no option is selected
    /// or the selection cannot be matched against the option list.
    /// Setting -1 (or any value outside [1..Options.Count]) clears the selection.
    /// </summary>
    public virtual int Selected
    {
        get
        {
            var values = SelectedValues;
            if (values.Count == 0) return -1;
            int i = 1;
            foreach (var opt in Options)
            {
                if (opt.Value == values[0]) return i;
                i++;
            }
            return -1;
        }
        set
        {
            var opts = Options;
            if (value >= 1 && value <= opts.Count)
                SelectedValues = [opts[value].Value];
            else
                SelectedValues = [];
        }
    }

    /// <summary>
    /// The field value (/V). Override declared for the public API shape; behavior delegates
    /// to <see cref="Field.Value"/>.
    /// </summary>
    public override string? Value
    {
        get => base.Value;
        set => base.Value = value;
    }

    /// <summary>Add an option to the choice field.</summary>
    public void AddOption(string optionName)
    {
        var opt = Dict.Get("Opt") as PdfArray;
        if (opt is null)
        {
            opt = new PdfArray();
            Dict.Set("Opt", opt);
        }
        opt.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(optionName)));
    }

    /// <summary>Add an option with separate export and display values.</summary>
    public void AddOption(string export, string name)
    {
        var opt = Dict.Get("Opt") as PdfArray;
        if (opt is null)
        {
            opt = new PdfArray();
            Dict.Set("Opt", opt);
        }
        var pair = new PdfArray();
        pair.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(export)));
        pair.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
        opt.Add(pair);
    }

    /// <summary>Remove the first option matching <paramref name="optionName"/>
    /// (either as a bare PdfString or as the display half of an [export, name] pair).</summary>
    public void DeleteOption(string optionName)
    {
        // Remove the /Opt list entry (display/export pair).
        if (Dict.Get("Opt") is PdfArray opt)
        {
            for (var i = 0; i < opt.Count; i++)
            {
                var resolved = Reader.Resolve(opt[i]);
                var match = resolved switch
                {
                    PdfString s => s.ToText() == optionName,
                    PdfArray pair when pair.Count >= 2 => GetText(pair[1]) == optionName
                                                         || GetText(pair[0]) == optionName,
                    _ => false,
                };
                if (match)
                {
                    opt.RemoveAt(i);
                    break;
                }
            }
        }

        // Radio-button options also carry a kid widget annotation keyed by the
        // option's on-state name (AP/N key); without removing it the field's kid
        // count — which is what Count reports — keeps the deleted option.
        if (Reader.Resolve(Dict.Get("Kids")) is PdfArray kids)
        {
            for (var i = 0; i < kids.Count; i++)
            {
                if (Reader.Resolve(kids[i]) is PdfDictionary kid
                    && KidOnStateName(kid) == optionName)
                {
                    kids.RemoveAt(i);
                    break;
                }
            }
        }
    }

    /// <summary>The on (non-"Off") appearance-state name of a widget kid — the
    /// key under /AP/N that isn't "Off", falling back to /AS. Identifies which
    /// radio option the kid renders.</summary>
    private string? KidOnStateName(PdfDictionary kid)
    {
        if (Reader.ResolveDict(kid.Get("AP")) is { } ap
            && Reader.ResolveDict(ap.Get("N")) is { } n)
        {
            foreach (var key in n.Keys)
                if (key != "Off") return key;
        }
        var asName = kid.GetName("AS");
        return asName != "Off" ? asName : null;
    }

    internal static string GetText(PdfObject obj) => obj switch
    {
        PdfString s => s.ToText(),
        PdfName n => n.Value,
        _ => "",
    };

    /// <summary>
    /// Materialise the option list backing the <see cref="Options"/> collection.
    /// Default implementation reads the /Opt array. Subclasses (e.g. RadioButtonField)
    /// override to derive options from the field's appearance state dictionaries.
    /// </summary>
    protected internal virtual List<Option> MaterializeOptions()
    {
        var arr = Reader.Resolve(Dict.Get("Opt")) as PdfArray;
        if (arr is null) return new List<Option>();

        var selectedValues = new HashSet<string>(SelectedValues);
        var result = new List<Option>();
        foreach (var item in arr)
        {
            var resolved = Reader.Resolve(item);
            Option? option = null;
            if (resolved is PdfArray pair && pair.Count >= 2)
            {
                option = new Option(GetText(pair[0]), GetText(pair[1]));
            }
            else if (resolved is PdfString s)
            {
                var text = s.ToText();
                option = new Option(text, text);
            }
            else if (resolved is PdfName n)
            {
                option = new Option(n.Value, n.Value);
            }

            if (option is not null)
            {
                option.Index = result.Count + 1;
                option.Selected = selectedValues.Contains(option.Value);
                result.Add(option);
            }
        }
        return result;
    }
}

/// <summary>
/// Class represents option of choice field.
/// </summary>
public sealed class Option
{
    public Option(string value, string name)
    {
        ExportValue = value;
        DisplayValue = name;
    }

    /// <summary>1-based position of this option within its owning collection.</summary>
    public int Index { get; internal set; }

    /// <summary>Display name shown in the choice UI.</summary>
    public string Name { get => DisplayValue; set => DisplayValue = value; }

    /// <summary>Whether this option is currently selected on the owning field.</summary>
    public bool Selected { get; set; }

    /// <summary>Export value written to the PDF when this option is selected.</summary>
    public string Value { get => ExportValue; set => ExportValue = value; }

    internal string ExportValue { get; set; }
    internal string DisplayValue { get; set; }
}

/// <summary>
/// Mutable collection of <see cref="Option"/> values backed by the owning
/// <see cref="ChoiceField"/>'s /Opt entry. Implements ICollection&lt;Option&gt;
/// as part of the public API shape.
/// </summary>
public sealed class OptionCollection : ICollection<Option>
{
    private readonly ChoiceField _field;

    internal OptionCollection(ChoiceField field) => _field = field;

    /// <summary>Number of options on the owning field.</summary>
    public int Count => Materialize().Count;

    /// <summary>Always false — the collection is mutable.</summary>
    public bool IsReadOnly => false;

    /// <summary>Always false — the collection is not thread-safe.</summary>
    public bool IsSynchronized => false;

    /// <summary>Synchronization root for the collection.</summary>
    public object SyncRoot => this;

    /// <summary>
    /// Gets the option at the given 1-based index.
    /// Throws <see cref="ArgumentOutOfRangeException"/> when the index falls outside [1..Count].
    /// </summary>
    public Option this[int index]
    {
        get
        {
            var list = Materialize();
            if (index < 1 || index > list.Count)
                throw new System.ArgumentOutOfRangeException(nameof(index),
                    $"Index {index} is out of range. Must be between 1 and {list.Count}.");
            return list[index - 1];
        }
    }

    /// <summary>Gets the option whose display name matches the given key, or null when not found.</summary>
    public Option? this[string name]
    {
        get
        {
            foreach (var opt in Materialize())
            {
                if (opt.Name == name) return opt;
            }
            return null;
        }
    }

    /// <summary>Method-form indexer (the public surface uses a lowercase `get` method).</summary>
    public Option get(int index) => this[index];

    /// <summary>Method-form lookup by display name.</summary>
    public Option? get(string name) => this[name];


    /// <summary>Appends an option to the field's /Opt array.</summary>
    public void Add(Option item)
    {
        if (item is null) throw new System.ArgumentNullException(nameof(item));
        var arr = _field.Dict.Get("Opt") as PdfArray;
        if (arr is null)
        {
            arr = new PdfArray();
            _field.Dict.Set("Opt", arr);
        }
        if (item.Value == item.Name)
        {
            arr.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(item.Value)));
        }
        else
        {
            var pair = new PdfArray();
            pair.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(item.Value)));
            pair.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(item.Name)));
            arr.Add(pair);
        }
    }

    /// <summary>Removes all options from the field.</summary>
    public void Clear() => _field.Dict.Set("Opt", new PdfArray());

    /// <summary>Returns true when an option with the same Value+Name exists in the collection.</summary>
    public bool Contains(Option item)
    {
        if (item is null) return false;
        foreach (var opt in Materialize())
        {
            if (opt.Value == item.Value && opt.Name == item.Name) return true;
        }
        return false;
    }

    /// <summary>Copies the materialised options into the supplied array starting at the given offset.</summary>
    public void CopyTo(Option[] array, int index) => Materialize().CopyTo(array, index);

    /// <summary>
    /// Removes the first option whose Value+Name match the supplied option. Returns true if a
    /// matching entry was removed.
    /// </summary>
    public bool Remove(Option item)
    {
        if (item is null) return false;
        var arr = _field.Reader.Resolve(_field.Dict.Get("Opt")) as PdfArray;
        if (arr is null) return false;
        for (int i = 0; i < arr.Count; i++)
        {
            var resolved = _field.Reader.Resolve(arr[i]);
            string ev, dv;
            if (resolved is PdfArray pair && pair.Count >= 2)
            {
                ev = ChoiceField.GetText(pair[0]);
                dv = ChoiceField.GetText(pair[1]);
            }
            else if (resolved is PdfString s)
            {
                ev = dv = s.ToText();
            }
            else if (resolved is PdfName n)
            {
                ev = dv = n.Value;
            }
            else continue;

            if (ev == item.Value && dv == item.Name)
            {
                arr.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Enumerates the options on the field at call time.</summary>
    public IEnumerator<Option> GetEnumerator() => Materialize().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private List<Option> Materialize() => _field.MaterializeOptions();
}

/// <summary>Combo box (drop-down) form field — a ChoiceField with the Combo flag set.</summary>
public class ComboBoxField : ChoiceField
{
    internal ComboBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Parameterless ctor (caller binds to a page later).</summary>
    public ComboBoxField() : base(BuildComboFieldDict(new Rectangle(0, 0, 0, 0)), PdfReader.Empty) { }

    /// <summary>Document-bound ctor; rectangle defaults to empty.</summary>
    public ComboBoxField(Document doc) : base(BuildComboFieldDict(new Rectangle(0, 0, 0, 0)), doc.Pages[1].Reader) { }

    /// <summary>Document-bound ctor with an explicit rectangle.</summary>
    public ComboBoxField(Document doc, Rectangle rect)
        : base(BuildComboFieldDict(rect), doc.Pages[1].Reader)
    {
    }

    /// <summary>
    /// Creates a new combo box field on the specified page with the given rectangle.
    /// </summary>
    public ComboBoxField(Page page, Rectangle rect)
        : base(BuildComboFieldDict(rect), page.Reader)
    {
    }

    /// <summary>Whether the user can type a free-form value (/Ff bit 19, Edit).</summary>
    public bool Editable
    {
        get => (FieldFlags & (1 << 18)) != 0;
        set => Dict.Set("Ff", new PdfInteger(value ? FieldFlags | (1 << 18) : FieldFlags & ~(1 << 18)));
    }

    /// <summary>Whether spell-check is enabled (/Ff bit 23, DoNotSpellCheck inverted).</summary>
    public bool SpellCheck
    {
        get => (FieldFlags & (1 << 22)) == 0;
        set => Dict.Set("Ff", new PdfInteger(value ? FieldFlags & ~(1 << 22) : FieldFlags | (1 << 22)));
    }

    private static PdfDictionary BuildComboFieldDict(Rectangle rect)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Ch"));
        dict.Set("Ff", new PdfInteger(1 << 17)); // Combo flag
        dict.Set("Rect", MakeRectArray(rect));
        dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes("/Helv 12 Tf 0 g")));
        return dict;
    }
}

/// <summary>List box form field — a ChoiceField without the Combo flag.</summary>
public class ListBoxField : ChoiceField
{
    internal ListBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Parameterless ctor (caller binds to a page later).</summary>
    public ListBoxField() : base(BuildListFieldDict(new Rectangle(0, 0, 0, 0)), PdfReader.Empty) { }

    /// <summary>
    /// Creates a new list box field on the specified page with the given rectangle.
    /// </summary>
    public ListBoxField(Page page, Rectangle rect)
        : base(BuildListFieldDict(rect), page.Reader)
    {
    }

    /// <summary>
    /// Creates a new list box field associated with the document (page assigned via Form.Add).
    /// </summary>
    public ListBoxField(Document doc, Rectangle rect)
        : base(BuildListFieldDict(rect), doc.Pages[1].Reader)
    {
    }

    /// <summary>Set-only selected-items array.</summary>
    public new int[] SelectedItems
    {
        set => base.SelectedItems = value;
    }

    /// <summary>1-based index of the selected option (-1 if none); setting it
    /// selects that single option. Delegates to the 1-based base setter so it
    /// stays consistent with the getter and <see cref="ChoiceField.Options"/>
    /// (the int[] SelectedItems API is index-based and handled separately).</summary>
    public new int Selected
    {
        get => base.Selected;
        set => base.Selected = value;
    }

    /// <summary>0-based index of the first visible option when the list is scrolled. Stored only.</summary>
    public int TopIndex { get; set; }

    private static PdfDictionary BuildListFieldDict(Rectangle rect)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Ch"));
        // No Combo flag — list box
        dict.Set("Rect", MakeRectArray(rect));
        dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes("/Helv 12 Tf 0 g")));
        return dict;
    }
}
