using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public class CheckboxField : Field
{
    internal CheckboxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Default-construct an unbound checkbox field. Add it to a
    /// document later via <c>form.Add(field, page)</c>.</summary>
    public CheckboxField() : base(BuildCheckboxDict(), PdfReader.Empty) { }

    /// <summary>Construct a checkbox bound to <paramref name="doc"/>.</summary>
    public CheckboxField(Document doc) : base(BuildCheckboxDict(), doc?.Reader ?? PdfReader.Empty) { }

    /// <summary>Construct a checkbox bound to <paramref name="doc"/> with
    /// the given widget <paramref name="rect"/>.</summary>
    public CheckboxField(Document doc, Rectangle rect)
        : base(BuildCheckboxDict(rect), doc?.Reader ?? PdfReader.Empty) { }

    /// <summary>Construct a checkbox on <paramref name="page"/> at the
    /// given <paramref name="rect"/>.</summary>
    public CheckboxField(Page page, Rectangle rect)
        : base(BuildCheckboxDict(rect), page?.Reader ?? PdfReader.Empty) { }

    private static PdfDictionary BuildCheckboxDict(Rectangle? rect = null)
    {
        var d = new PdfDictionary();
        d.Set("FT", new PdfName("Btn"));
        if (rect is not null)
        {
            var arr = new PdfArray();
            arr.Add(new PdfReal(rect.LLX));
            arr.Add(new PdfReal(rect.LLY));
            arr.Add(new PdfReal(rect.URX));
            arr.Add(new PdfReal(rect.URY));
            d.Set("Rect", arr);
        }
        return d;
    }

    /// <summary>Visual style of the rendered checkbox glyph (/MK /CA):
    /// <see cref="BoxStyle.Cross"/> draws an X, <see cref="BoxStyle.Check"/> a
    /// check mark; other styles fall back to an X. Honoured by
    /// <see cref="GenerateAppearance"/> when the checkbox is added to a form.
    /// Setting it after the field is placed rebuilds the /AP so this style — and
    /// any border/background (/MK) set alongside it — reaches the appearance
    /// (Form.Add generates the /AP once; a later style/colour change would
    /// otherwise be stranded behind GenerateAppearance's already-has-/AP guard).</summary>
    public BoxStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            if (Reader.ResolveDict(Dict.Get("AP")) is not null)
            {
                Dict.Remove("AP");
                GenerateAppearance();
            }
        }
    }
    private BoxStyle _style = BoxStyle.Check;

    /// <summary>Move this checkbox's widget to <paramref name="rect"/> (page space) and
    /// ensure it is registered in the page /Annots. Used by the generator to position a
    /// checkbox laid out inside a table cell; the widget's /AP draws the box and check.</summary>
    internal void PlaceWidget(Page page, Rectangle rect)
    {
        Dict.Set("Rect", MakeRectArray(rect));
        var annots = Reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annots is null) { annots = new PdfArray(); page.Dict.Set("Annots", annots); }
        foreach (var a in annots)
            if (ReferenceEquals(Reader.Resolve(a), Dict)) return; // already present
        annots.Add(Dict);
    }

    /// <summary>Generate the /AP Normal + Down appearance streams for this
    /// checkbox: the glyph matching <see cref="Style"/> for the on state
    /// (<see cref="OnValue"/>) and an empty box for "Off". No-op when the field
    /// has no /Rect or already carries an /AP (e.g. loaded from an existing PDF).</summary>
    internal override void GenerateAppearance()
    {
        if (Reader.ResolveDict(Dict.Get("AP")) is not null) return;
        if (Reader.Resolve(Dict.Get("Rect")) is not PdfArray rectArr || rectArr.Count < 4) return;
        var r = Rectangle.FromPdfArray(rectArr);
        double w = r.Width, h = r.Height;
        if (w <= 0 || h <= 0) return;

        var on = OnValue;
        var ap = new PdfDictionary();
        ap.Set("N", BuildStateDict(on, w, h));
        ap.Set("D", BuildStateDict(on, w, h));
        Dict.Set("AP", ap);
        if (Dict.GetName("AS") is null) Dict.Set("AS", new PdfName("Off"));
    }

    private PdfDictionary BuildStateDict(string onValue, double w, double h)
    {
        var states = new PdfDictionary();
        states.Set(onValue, BuildBoxStream(w, h, drawGlyph: true));
        states.Set("Off", BuildBoxStream(w, h, drawGlyph: false));
        return states;
    }

    private static string Fmt(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private PdfStream BuildBoxStream(double w, double h, bool drawGlyph)
    {
        var sb = new System.Text.StringBuilder();
        // Wrap in q/Q so the drawing is graphics-state isolated; the trailing Q
        // is the final operator, with the glyph strokes just before it.
        sb.Append("q 1 w ");
        // Interior fill only when the widget declares a background colour (/MK /BG);
        // a checkbox with no background stays transparent (an explicit empty
        // background must not paint a fill). The black border is always stroked.
        var mkDict = Reader.ResolveDict(Dict.Get("MK"));
        if (mkDict is not null && MkColorOperator(mkDict.Get("BG"), fill: true) is { } bgFill)
            sb.Append($"{bgFill} 0 0 {Fmt(w)} {Fmt(h)} re f ");
        // The box outline strokes only when the widget declares a border colour
        // (/MK /BC) — a default (colourless) unchecked checkbox stays invisible;
        // callers that want visible boxes set Characteristics.Border.
        if (mkDict is not null && MkColorOperator(mkDict.Get("BC"), fill: false) is { } bcStroke)
            sb.Append($"{bcStroke} 0 0 {Fmt(w)} {Fmt(h)} re S ");
        if (drawGlyph)
        {
            var inset = System.Math.Min(w, h) * 0.2;
            sb.Append("2 w ");
            if (Style == BoxStyle.Check)
            {
                // Check mark: down-stroke then up-stroke.
                sb.Append($"{Fmt(w * 0.2)} {Fmt(h * 0.5)} m {Fmt(w * 0.4)} {Fmt(h * 0.25)} l {Fmt(w * 0.8)} {Fmt(h * 0.78)} l S ");
            }
            else
            {
                // Cross (and fallback for other styles): an X of two diagonals.
                sb.Append($"{Fmt(inset)} {Fmt(inset)} m {Fmt(w - inset)} {Fmt(h - inset)} l S ");
                sb.Append($"{Fmt(inset)} {Fmt(h - inset)} m {Fmt(w - inset)} {Fmt(inset)} l S ");
            }
        }
        sb.Append("Q ");

        var stream = new PdfStream(new PdfDictionary(), System.Text.Encoding.Latin1.GetBytes(sb.ToString()));
        stream.Dict.Set("Type", new PdfName("XObject"));
        stream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        stream.Dict.Set("BBox", bbox);
        stream.Dict.Set("Resources", new PdfDictionary());
        return stream;
    }

    /// <summary>The export value emitted into FDF/XFDF on form submit.
    /// Defaults to <see cref="OnValue"/>.</summary>
    public string ExportValue
    {
        get => OnValue;
        set
        {
            // Renaming the export value re-keys the widget's /AP/N (and /AP/D) on-state
            // entry from the current on-value to the requested value, and updates /AS and
            // /V where they referenced the old name, so the new export value survives save.
            if (string.IsNullOrEmpty(value)) return;
            var oldOn = OnValue;
            if (value == oldOn) return;
            if (HasWidgetKids)
                foreach (var kid in AllKids()) RenameOnState(kid, oldOn, value);
            else
            {
                RenameOnState(Dict, oldOn, value);
                // A checkbox constructed without an /AP yet (export value set before the appearance
                // is generated) has no on-state to rename — establish one so the value is recorded.
                EnsureCheckboxOnState(Dict, value);
            }
            if (Dict.GetName("V") == oldOn) Dict.Set("V", new PdfName(value));
            if (Dict.GetName("AS") == oldOn) Dict.Set("AS", new PdfName(value));
            MarkCheckboxDirty();
        }
    }

    /// <summary>Re-key a widget's non-"Off" appearance state from <paramref name="oldOn"/>
    /// to <paramref name="newOn"/> across /AP/N and /AP/D, and follow it in the widget /AS.</summary>
    private bool RenameOnState(PdfDictionary widget, string oldOn, string newOn)
    {
        var ap = Reader.ResolveDict(widget.Get("AP"));
        if (ap is null) return false;
        bool renamed = false;
        foreach (var apKey in new[] { "N", "D" })
        {
            if (Reader.Resolve(ap.Get(apKey)) is not PdfDictionary states) continue;
            string? key = null;
            foreach (var k in states.Keys) if (k != "Off") { key = k; break; }
            if (key is null || key == newOn) continue;
            var stream = states.Get(key);
            if (stream is null) continue;
            states.Remove(key);
            states.Set(newOn, stream);
            renamed = true;
        }
        if (widget.GetName("AS") is { } asName && asName != "Off")
            widget.Set("AS", new PdfName(newOn));
        return renamed;
    }

    /// <summary>Ensure <paramref name="widget"/> declares an /AP/N on-state named
    /// <paramref name="onValue"/> (plus "Off") when it has no on-state yet. No-op when the widget
    /// already carries some non-"Off" on-state (that case is handled by renaming).</summary>
    private void EnsureCheckboxOnState(PdfDictionary widget, string onValue)
    {
        var ap = Reader.ResolveDict(widget.Get("AP"));
        if (ap is null) { ap = new PdfDictionary(); widget.Set("AP", ap); }
        var n = Reader.ResolveDict(ap.Get("N"));
        if (n is null) { n = new PdfDictionary(); ap.Set("N", n); }
        bool hasOn = false;
        foreach (var k in n.Keys) if (k != "Off") { hasOn = true; break; }
        if (!hasOn) n.Set(onValue, new PdfDictionary());
        if (!n.ContainsKey("Off")) n.Set("Off", new PdfDictionary());
    }

    /// <summary>True when this checkbox is a grouped field with explicit widget
    /// kids (each kid carrying its own /AP/N on-state), as opposed to a single
    /// leaf widget. Grouped checkboxes track their selection per kid.</summary>
    private bool HasWidgetKids => AllKids().Any();

    /// <summary>The /AP/N on-state name (the non-"Off" key) of a widget kid,
    /// or null when the kid declares no appearance states.</summary>
    private string? KidOnValue(PdfDictionary kid)
    {
        var ap = Reader.ResolveDict(kid.Get("AP"));
        var n = ap is null ? null : Reader.ResolveDict(ap.Get("N"));
        if (n is not null)
            foreach (var k in n.Keys)
                if (k != "Off") return k;
        return null;
    }

    /// <summary>The currently-selected export value of a grouped checkbox: the
    /// /AS of the first kid that is not "Off", or "Off" when none is selected.</summary>
    private string SelectedKidState()
    {
        foreach (var kid in AllKids())
        {
            var asName = kid.GetName("AS");
            if (asName is not null && asName != "Off") return asName;
        }
        return "Off";
    }

    /// <summary>Distinct on-state values declared across all widget kids.</summary>
    private System.Collections.Generic.List<string> KidOnValues()
    {
        var result = new System.Collections.Generic.List<string>();
        foreach (var kid in AllKids())
        {
            var ov = KidOnValue(kid);
            if (ov is not null && !result.Contains(ov)) result.Add(ov);
        }
        return result;
    }

    /// <summary>Select <paramref name="value"/> on a grouped checkbox: set the
    /// field /V and /AS, and set each kid's /AS to its own on-value when it
    /// matches, otherwise "Off".</summary>
    private void ApplyGroupedValue(string value)
    {
        Dict.Set("V", new PdfName(value));
        Dict.Set("AS", new PdfName(value));
        foreach (var kid in AllKids())
        {
            var ov = KidOnValue(kid);
            kid.Set("AS", new PdfName(ov == value ? value : "Off"));
        }
        MarkCheckboxDirty();
    }

    /// <summary>Mark the field dict and every widget-kid dict dirty so an
    /// incremental save persists a checkbox value/state change.</summary>
    private void MarkCheckboxDirty()
    {
        if (OwnerDocument is null) return;
        if (ObjectNumber >= 0) OwnerDocument.MarkDirty(ObjectNumber, Dict);
        foreach (var kid in AllKids())
        {
            var n = OwnerDocument.FindObjectNumber(kid);
            if (n >= 0) OwnerDocument.MarkDirty(n, kid);
        }
    }

    /// <summary>Currently selected appearance state. For a grouped checkbox this
    /// is the on-value of the selected kid; for a single checkbox the
    /// <see cref="OnValue"/> or "Off".</summary>
    public new string ActiveState
    {
        get => HasWidgetKids ? SelectedKidState() : (Dict.GetName("AS") ?? "Off");
        set
        {
            if (value is null) { Dict.Remove("AS"); return; }
            if (HasWidgetKids) ApplyGroupedValue(value);
            else { Dict.Set("AS", new PdfName(value)); MarkCheckboxDirty(); }
        }
    }

    /// <summary>All appearance-state keys declared under /AP/N. Includes
    /// "Off" plus the <see cref="OnValue"/>.</summary>
    public System.Collections.Generic.List<string> AllowedStates
    {
        get
        {
            var result = new System.Collections.Generic.List<string>();
            // Grouped checkbox: aggregate "Off" plus each on-state. When the field dict is
            // itself a visual widget (its own /Rect + /AP/N on-state) alongside the kid widgets,
            // its on-state is an allowed state too — include it, not just the kids'.
            if (HasWidgetKids)
            {
                result.Add("Off");
                if (Dict.ContainsKey("Rect"))
                {
                    var selfOn = KidOnValue(Dict);
                    if (selfOn is not null && !result.Contains(selfOn)) result.Add(selfOn);
                }
                foreach (var kid in AllKids())
                {
                    var ov = KidOnValue(kid);
                    if (ov is not null && !result.Contains(ov)) result.Add(ov);
                }
                return result;
            }
            var ap = Reader.ResolveDict(Dict.Get("AP"));
            var n = ap is null ? null : Reader.ResolveDict(ap.Get("N"));
            if (n is not null) foreach (var k in n.Keys) result.Add(k);
            return result;
        }
    }

    /// <summary>The checkbox value (/V entry, name-typed). For a grouped
    /// checkbox, reflects the selected kid's on-value.</summary>
    public new string Value
    {
        get => HasWidgetKids ? SelectedKidState() : (Dict.GetName("V") ?? "Off");
        set => SetValue(value);
    }

    /// <summary>Map an incoming value to a legal appearance-state name: null or
    /// "Off" (case-insensitive) → "Off"; an exact declared on-state (field /AP/N
    /// key or widget-kid on-value) → itself; any other non-empty value → the
    /// field's <see cref="OnValue"/>.</summary>
    private string NormalizeOnValue(string? value)
    {
        if (string.IsNullOrEmpty(value) || string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase))
            return "Off";
        if (AllowedStates.Contains(value)) return value;
        if (HasWidgetKids && KidOnValues().Contains(value)) return value;
        // A recognised "on" alias selects the field's real on-state; any other
        // unrecognised value (e.g. an FDF "No"/"false"/"0" for an unchecked box) is
        // treated as Off rather than forced on — fixes FDF imports that carry a
        // non-on string for a checkbox that should be cleared.
        if (IsOnAlias(value)) return OnValue;
        return "Off";
    }

    /// <summary>True when <paramref name="value"/> names one of this checkbox's
    /// declared "on" states (an /AP/N key other than "Off", or a grouped widget
    /// kid's on-value) — i.e. selecting it would check the box. "Off", empty, and
    /// any value matching no declared state return false. Used by data import
    /// (FDF/XFDF) where the incoming value is an export value to be matched
    /// against the real states, not coerced to the on-state.</summary>
    internal bool IsDeclaredOnState(string? value)
    {
        if (string.IsNullOrEmpty(value) || string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var s in AllowedStates)
            if (s != "Off" && string.Equals(s, value, StringComparison.Ordinal)) return true;
        return HasWidgetKids && KidOnValues().Contains(value);
    }

    private static bool IsOnAlias(string value) =>
        value.Equals("Yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("On", StringComparison.OrdinalIgnoreCase)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value == "1";

    /// <summary>Append <paramref name="optionName"/> as a new selectable
    /// state on this checkbox (used for grouped checkboxes
    /// that behave like radio buttons). Stored only; FOSS treats every
    /// checkbox as a single-state Yes/Off toggle.</summary>
    public void AddOption(string optionName) => AddOption(optionName, new Rectangle(0, 0, 0, 0));

    /// <summary>Append an option with an explicit widget rectangle. Turns a single
    /// checkbox into a grouped one (radio-style): the new option becomes a kid widget
    /// carrying its own /AP/N {optionName, Off} state, so the value is selectable and
    /// <see cref="AllowedStates"/> reports it.</summary>
    public void AddOption(string optionName, Rectangle rect)
    {
        if (string.IsNullOrEmpty(optionName)) return;

        var kid = new PdfDictionary();
        kid.Set("Type", new PdfName("Annot"));
        kid.Set("Subtype", new PdfName("Widget"));
        kid.Set("Rect", MakeRectArray(rect));
        kid.Set("AS", new PdfName("Off"));
        kid.Set("F", new PdfInteger(4));
        kid.Set("Parent", Dict);

        var ap = new PdfDictionary();
        if (rect.Width > 0 && rect.Height > 0)
        {
            // Draw a real check-glyph appearance for the option's on-state so the
            // widget renders a visible box + check (matching a single checkbox).
            ap.Set("N", BuildStateDict(optionName, rect.Width, rect.Height));
            ap.Set("D", BuildStateDict(optionName, rect.Width, rect.Height));
        }
        else
        {
            var n = new PdfDictionary();
            n.Set(optionName, new PdfDictionary());
            n.Set("Off", new PdfDictionary());
            ap.Set("N", n);
        }
        kid.Set("AP", ap);

        var kids = Reader.Resolve(Dict.Get("Kids")) as PdfArray;
        if (kids is null)
        {
            kids = new PdfArray();
            Dict.Set("Kids", kids);
        }
        kids.Add(kid);
        MarkCheckboxDirty();
    }

    /// <summary>Append an option targeting the given <paramref name="page"/>
    /// number plus widget rectangle. The option's widget is placed on that page
    /// (rather than the field's page) when the field is added to the form.</summary>
    public void AddOption(string optionName, int page, Rectangle rect)
    {
        if (string.IsNullOrEmpty(optionName)) return;
        AddOption(optionName, rect);
        // Tag the just-added kid so Form placement routes it to `page`.
        if (Reader.Resolve(Dict.Get("Kids")) is PdfArray kids && kids.Count > 0
            && Reader.Resolve(kids[kids.Count - 1]) is PdfDictionary kid && page >= 1)
            kid.Set("_PlacePage", new PdfInteger(page));
    }

    /// <summary>Shallow clone of the field. Returns a new
    /// <see cref="CheckboxField"/> wrapping the same backing dictionary
    /// (matching the <c>object Clone()</c> contract).</summary>
    public new object Clone() => new CheckboxField(Dict, Reader);

    /// <summary>
    /// Gets the "On" state name for this checkbox (the appearance state key that is not "Off").
    /// Typically "Yes" but can be any custom name.
    /// </summary>
    public string OnValue
    {
        get
        {
            // Grouped checkbox: the on-value is taken from the kids. Prefer the
            // conventional "Yes" when present, otherwise the first kid on-value.
            if (HasWidgetKids)
            {
                var vals = KidOnValues();
                if (vals.Contains("Yes")) return "Yes";
                if (vals.Count > 0) return vals[0];
            }
            // Look at AP/N dictionary keys for the non-Off state
            var apDict = Reader.ResolveDict(Dict.Get("AP"));
            if (apDict is not null)
            {
                var nObj = Reader.Resolve(apDict.Get("N"));
                if (nObj is PdfDictionary nDict)
                {
                    foreach (var key in nDict.Keys)
                    {
                        if (key != "Off") return key;
                    }
                }
            }
            return "Yes"; // default
        }
    }

    public bool IsChecked
    {
        get
        {
            if (HasWidgetKids) return SelectedKidState() == OnValue;
            var v = Value;
            return v is not null && v != "Off";
        }
        set
        {
            if (HasWidgetKids)
            {
                string target;
                if (value) target = OnValue;
                else
                {
                    // Unchecking a grouped checkbox selects the single remaining
                    // (non-on) export value when there is exactly one — e.g. a
                    // "Yes"/"No" pair unchecks to "No"; otherwise plain "Off".
                    var others = KidOnValues();
                    others.Remove(OnValue);
                    target = others.Count == 1 ? others[0] : "Off";
                }
                ApplyGroupedValue(target);
                return;
            }
            var newValue = value ? OnValue : "Off";
            Dict.Set("V", new PdfName(newValue));
            Dict.Set("AS", new PdfName(newValue));
            MarkCheckboxDirty();
        }
    }

    public bool Checked
    {
        get => IsChecked;
        set => IsChecked = value;
    }

    /// <summary>The last <b>arbitrary non-state</b> string assigned through the
    /// public <see cref="Value"/> setter (e.g. <c>"1234"</c>), or null. An XFA
    /// checkbox can legitimately carry such a value in the XFA datasets even though
    /// the AcroForm appearance state normalises to "Off"; the static-XFA sync
    /// (<c>Form.SyncAcroFormToXfa</c>) persists this verbatim. Recognised state
    /// tokens ("Off", "0", an on-state, an on-alias) leave this null so the normal
    /// FDF/XFDF import path (<see cref="NormalizeOnValue"/>) stays unaffected.
    /// Keyed by the field DICT, not this instance: the value may be assigned
    /// through a transient child Field materialised by hierarchy enumeration,
    /// while the sync reads through the form's tracked instance — both wrap the
    /// same dictionary.</summary>
    internal string? RawNonStateValue
    {
        get => _rawNonState.TryGetValue(Dict, out var v) ? v : null;
        private set
        {
            _rawNonState.Remove(Dict);
            if (value is not null) _rawNonState.Add(Dict, value);
        }
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, string> _rawNonState = new();

    /// <summary>True when <paramref name="value"/> is neither empty, "Off", "0",
    /// a declared on-state / kid on-value, nor an on-alias — i.e. a value the
    /// checkbox appearance cannot represent but XFA still stores literally.</summary>
    private bool IsArbitraryNonState(string? value)
        => !string.IsNullOrEmpty(value)
           && !value!.Equals("Off", StringComparison.OrdinalIgnoreCase)
           && value != "0"
           && !AllowedStates.Contains(value)
           && !(HasWidgetKids && KidOnValues().Contains(value))
           && !IsOnAlias(value);

    protected override void SetValue(string? value)
    {
        // For checkboxes, /V is a name (not a string), and its only legal values
        // are "Off" and the field's declared on-state(s). Normalise generic
        // on-values ("Yes"/"On"/…) to the real on-state so importing e.g. "Yes"
        // into a checkbox whose on-state is "Y" stores "Y" (parity).
        var name = NormalizeOnValue(value);
        // Remember an arbitrary (non-state) assignment so a static XFA form can
        // persist it into the datasets verbatim — the AcroForm appearance still
        // normalises to "Off" below.
        RawNonStateValue = IsArbitraryNonState(value) ? value : null;
        if (HasWidgetKids) { ApplyGroupedValue(name); return; }
        Dict.Set("V", new PdfName(name));
        Dict.Set("AS", new PdfName(name));
        MarkCheckboxDirty();
    }
}

public class RadioButtonField : ChoiceField
{
    internal RadioButtonField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>
    /// Creates a new radio-button field bound to the supplied page's reader.
    /// Use <see cref="AddOption(string, Rectangle)"/> to populate kid widgets,
    /// then add the field via <c>Form.Add</c>.
    /// </summary>
    public RadioButtonField(Page page) : base(BuildRadioFieldDict(), page.Reader) { }

    /// <summary>Construct with the field-level /Rect set. Callers that
    /// build a radio group via <c>new RadioButtonField(page, rect)</c> followed
    /// by per-option <c>RadioButtonOptionField</c> kids expect the parent dict
    /// to carry a /Rect; the kids each carry their own widget rects.</summary>
    public RadioButtonField(Page page, Rectangle rect) : base(BuildRadioFieldDict(), page.Reader)
    {
        if (rect is null) return;
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        Dict.Set("Rect", arr);
    }

    public RadioButtonField(Document doc) : base(BuildRadioFieldDict(), doc?.Reader ?? PdfReader.Empty) { }

    private static PdfDictionary BuildRadioFieldDict()
    {
        var dict = new PdfDictionary();
        dict.Set("FT", new PdfName("Btn"));
        dict.Set("Ff", new PdfInteger(1 << 15)); // bit 16: Radio flag
        return dict;
    }

    /// <summary>
    /// Adds an option with the given appearance-state name at the supplied
    /// rectangle. Writes both an /Opt entry (so <see cref="Options"/> reflects
    /// it) and a kid widget annotation under /Kids (so <c>this[i].Rect</c>
    /// surfaces the option's position).
    /// </summary>
    public void AddOption(string optionName, Rectangle rect)
    {
        base.AddOption(optionName); // /Opt entry (inherited from ChoiceField)
        AddOptionKid(optionName, rect);
    }

    /// <summary>Add a radio option by name only; shadows the inherited ChoiceField
    /// version so reflection surfaces it on this type. Besides the /Opt entry, this
    /// also registers a kid widget whose /AP/N on-state is <paramref name="optionName"/>
    /// so <see cref="Selected"/> / <see cref="Value"/> resolve the option (a radio
    /// group's selection is carried by the kids' appearance states, not /Opt). When
    /// no per-option rectangle is supplied the field's own /Rect is reused.</summary>
    public new void AddOption(string optionName)
    {
        base.AddOption(optionName);
        var rect = Reader.Resolve(Dict.Get("Rect")) is PdfArray ra && ra.Count >= 4
            ? Rectangle.FromPdfArray(ra)
            : new Rectangle(0, 0, 16, 16);
        AddOptionKid(optionName, rect);
    }

    /// <summary>Build and register a radio kid widget annotation carrying
    /// <paramref name="optionName"/> as its sole on-state (plus the universal "Off").</summary>
    private void AddOptionKid(string optionName, Rectangle rect)
    {
        var kid = new PdfDictionary();
        kid.Set("Type", new PdfName("Annot"));
        kid.Set("Subtype", new PdfName("Widget"));
        kid.Set("Rect", MakeRectArray(rect));
        kid.Set("AS", new PdfName("Off"));

        var ap = new PdfDictionary();
        var n = new PdfDictionary();
        n.Set(optionName, new PdfDictionary());
        n.Set("Off", new PdfDictionary());
        ap.Set("N", n);
        kid.Set("AP", ap);

        var kids = Dict.Get("Kids") as PdfArray;
        if (kids is null)
        {
            kids = new PdfArray();
            Dict.Set("Kids", kids);
        }
        kids.Add(kid);
    }

    /// <summary>
    /// Add a configured <see cref="RadioButtonOptionField"/> as a kid widget of this
    /// radio group. Builds the widget annotation from the option's rectangle, name,
    /// and border characteristics, registers it under /Kids (so <c>Form.Add</c>
    /// places it on the page's /Annots), and links the option to the new widget so
    /// <see cref="RadioButtonOptionField.Rect"/> tracks it.
    /// </summary>
    public void Add(RadioButtonOptionField option)
    {
        if (option is null) throw new ArgumentNullException(nameof(option));

        var rect = option.PendingRect
            ?? new Rectangle(0, 0, option.Width, option.Height);
        var kidCount = 0;
        if (Dict.Get("Kids") is PdfArray existing) kidCount = existing.Count;
        var onState = string.IsNullOrEmpty(option.OptionName)
            ? $"Option{kidCount + 1}" : option.OptionName!;

        base.AddOption(onState); // /Opt entry on the parent field

        var kid = new PdfDictionary();
        kid.Set("Type", new PdfName("Annot"));
        kid.Set("Subtype", new PdfName("Widget"));
        kid.Set("Rect", MakeRectArray(rect));
        kid.Set("AS", new PdfName("Off"));
        kid.Set("Parent", Dict);

        var ap = new PdfDictionary();
        var n = new PdfDictionary();
        n.Set(onState, new PdfDictionary());
        n.Set("Off", new PdfDictionary());
        ap.Set("N", n);
        kid.Set("AP", ap);

        // /MK appearance characteristics: border colour (and background when set).
        var mk = new PdfDictionary();
        mk.Set("BC", ColorComponents(option.Characteristics.Border));
        if (option.Characteristics.Background.A != 0)
            mk.Set("BG", ColorComponents(option.Characteristics.Background));
        kid.Set("MK", mk);

        // /BS border width when the option carries a border.
        if (option.Border is { Width: > 0 } border)
        {
            var bs = new PdfDictionary();
            bs.Set("W", new PdfReal(border.Width));
            kid.Set("BS", bs);
        }

        var kids = Dict.Get("Kids") as PdfArray;
        if (kids is null) { kids = new PdfArray(); Dict.Set("Kids", kids); }
        kids.Add(kid);

        option.KidDict = kid;
        option.KidReader = Reader;
        option.OwnerRadio = this;
    }

    /// <summary>Position <paramref name="option"/>'s widget at <paramref name="rect"/> (page
    /// space) and register it in <paramref name="page"/>'s /Annots, so a radio option laid out
    /// by the generator round-trips as an interactive widget rather than just a drawn glyph.
    /// Refreshes the option's on/off appearance using its border and marker colours.</summary>
    internal void PlaceOptionWidget(RadioButtonOptionField option, Page page, Rectangle rect)
    {
        var kid = option.KidDict;
        if (kid is null) return;
        kid.Set("Rect", MakeRectArray(rect));
        // An option whose glyph the table render drew into the page content keeps
        // an /AP-less widget (form-grid option widgets ship without their own
        // appearance stream) — a widget appearance here would paint the circle twice.
        if (!option.InlineGlyphDrawn)
            WriteOptionAppearance(option, kid, rect.Width, rect.Height);

        var annots = page.Dict.Get("Annots") as PdfArray;
        if (annots is null) { annots = new PdfArray(); page.Dict.Set("Annots", annots); }
        foreach (var a in annots) if (ReferenceEquals(a, kid)) return; // already placed
        annots.Add(kid);
    }

    /// <summary>Build the option widget's /AP /N appearance for its on-state and "Off": the
    /// border-coloured disc outline followed by the marker-coloured centre, emitted as two
    /// non-stroking <c>rg</c> colour operators (border first, marker second).</summary>
    private static void WriteOptionAppearance(RadioButtonOptionField option, PdfDictionary kid, double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        var onName = "Off";
        if (kid.Get("AP") is PdfDictionary apd && apd.Get("N") is PdfDictionary nd0)
            foreach (var key in nd0.Keys) if (key != "Off") { onName = key; break; }
        if (onName == "Off") onName = string.IsNullOrEmpty(option.OptionName) ? "On" : option.OptionName!;

        var border = option.Characteristics.Border;
        var marker = option.DefaultAppearance?.TextColor ?? System.Drawing.Color.Black;
        double cx = w / 2, cy = h / 2, rad = System.Math.Min(w, h) / 2 - 1;
        if (rad <= 0) rad = System.Math.Min(w, h) / 2;

        var on = new System.Text.StringBuilder();
        on.Append("q ");
        on.Append($"{FmtNum(border.R / 255.0)} {FmtNum(border.G / 255.0)} {FmtNum(border.B / 255.0)} rg ");
        on.Append(CirclePath(cx, cy, rad)).Append("f ");
        on.Append($"{FmtNum(marker.R / 255.0)} {FmtNum(marker.G / 255.0)} {FmtNum(marker.B / 255.0)} rg ");
        on.Append(CirclePath(cx, cy, rad * 0.5)).Append("f Q ");

        var off = new System.Text.StringBuilder();
        off.Append("q ");
        off.Append($"{FmtNum(border.R / 255.0)} {FmtNum(border.G / 255.0)} {FmtNum(border.B / 255.0)} rg ");
        off.Append(CirclePath(cx, cy, rad)).Append("S Q ");

        var n = new PdfDictionary();
        n.Set(onName, MakeApXObject(on.ToString(), w, h));
        n.Set("Off", MakeApXObject(off.ToString(), w, h));
        var ap = new PdfDictionary();
        ap.Set("N", n);
        kid.Set("AP", ap);
        if (kid.GetName("AS") is null) kid.Set("AS", new PdfName("Off"));
    }

    /// <summary>Convert a System.Drawing colour to a PDF DeviceRGB component array.</summary>
    private static PdfArray ColorComponents(System.Drawing.Color color)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(color.R / 255.0));
        arr.Add(new PdfReal(color.G / 255.0));
        arr.Add(new PdfReal(color.B / 255.0));
        return arr;
    }

    /// <summary>Build a filled-disc "on" glyph and an empty "off" glyph for every
    /// kid widget, keyed by the kid's existing on-state name. Lets a freshly-built
    /// radio group render without a viewer's NeedAppearances pass.</summary>
    internal override void GenerateAppearance()
    {
        // Targets are the kid widgets carrying a /Rect; with none, the field's
        // own dict is the widget (a single-widget radio, e.g. one rebuilt from a
        // flat import).
        var targets = new System.Collections.Generic.List<PdfDictionary>();
        if (Reader.Resolve(Dict.Get("Kids")) is PdfArray kids)
            foreach (var k in kids)
                if (Reader.Resolve(k) is PdfDictionary kd && Reader.Resolve(kd.Get("Rect")) is PdfArray)
                    targets.Add(kd);
        if (targets.Count == 0 && Reader.Resolve(Dict.Get("Rect")) is PdfArray)
            targets.Add(Dict);

        // The field-level /V selects which widget is shown filled. Without it
        // the appearance is uniformly "off" — that's how a single-widget radio
        // gets toggled on on a flat import.
        var fieldOn = Reader.Resolve(Dict.Get("V")) switch
        {
            PdfName pn => pn.Value,
            PdfString ps => ps.ToText(),
            _ => null,
        };

        foreach (var kid in targets)
        {
            if (Reader.Resolve(kid.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var r = Rectangle.FromPdfArray(ra);
            double w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) continue;

            // The on-state name is the non-"Off" key already present in /AP/N;
            // failing that, /AS itself (if not "Off"); failing that, the field's
            // /V (so a flat-imported single-widget radio adopts the selected
            // name); finally fall back to "On".
            var onName = "On";
            if (Reader.ResolveDict(kid.Get("AP")) is { } apd &&
                Reader.ResolveDict(apd.Get("N")) is { } nd)
            {
                foreach (var key in nd.Keys)
                    if (key != "Off") { onName = key; break; }
            }
            else if (kid.GetName("AS") is { } existingAs && existingAs != "Off")
                onName = existingAs;
            else if (!string.IsNullOrEmpty(fieldOn) && fieldOn != "Off")
                onName = fieldOn!;

            var n = new PdfDictionary();
            n.Set(onName, BuildRadioStream(w, h, filled: true));
            n.Set("Off", BuildRadioStream(w, h, filled: false));
            var ap = new PdfDictionary();
            ap.Set("N", n);
            kid.Set("AP", ap);
            // /AS picks the visible state. Honor an existing /AS, otherwise
            // turn this widget on iff its on-name matches the field's /V.
            if (kid.GetName("AS") is null)
            {
                var asName = !string.IsNullOrEmpty(fieldOn) && fieldOn == onName ? onName : "Off";
                kid.Set("AS", new PdfName(asName));
            }
        }
    }

    private static PdfStream BuildRadioStream(double w, double h, bool filled)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("q 0.5 w 0 0 0 RG ");
        double cx = w / 2, cy = h / 2, rad = System.Math.Min(w, h) / 2 - 1;
        if (rad <= 0) rad = System.Math.Min(w, h) / 2;
        sb.Append(CirclePath(cx, cy, rad)).Append("S ");
        if (filled)
        {
            sb.Append("0 0 0 rg ");
            sb.Append(CirclePath(cx, cy, rad * 0.5)).Append("f ");
        }
        sb.Append("Q ");
        return MakeApXObject(sb.ToString(), w, h);
    }

    /// <summary>Approximate a circle with four cubic Béziers (kappa = 0.5523).</summary>
    private static string CirclePath(double cx, double cy, double r)
    {
        var kr = 0.5523 * r;
        return $"{FmtNum(cx + r)} {FmtNum(cy)} m " +
               $"{FmtNum(cx + r)} {FmtNum(cy + kr)} {FmtNum(cx + kr)} {FmtNum(cy + r)} {FmtNum(cx)} {FmtNum(cy + r)} c " +
               $"{FmtNum(cx - kr)} {FmtNum(cy + r)} {FmtNum(cx - r)} {FmtNum(cy + kr)} {FmtNum(cx - r)} {FmtNum(cy)} c " +
               $"{FmtNum(cx - r)} {FmtNum(cy - kr)} {FmtNum(cx - kr)} {FmtNum(cy - r)} {FmtNum(cx)} {FmtNum(cy - r)} c " +
               $"{FmtNum(cx + kr)} {FmtNum(cy - r)} {FmtNum(cx + r)} {FmtNum(cy - kr)} {FmtNum(cx + r)} {FmtNum(cy)} c ";
    }

    /// <summary>Move the field's widget rectangle to <paramref name="point"/>, preserving the
    /// current width/height. Stored on the field dictionary's /Rect.</summary>
    public new void SetPosition(Aspose.Pdf.Point point)
    {
        if (point is null) throw new ArgumentNullException(nameof(point));
        var rectArr = Reader.Resolve(Dict.Get("Rect")) as PdfArray;
        double w = 0, h = 0;
        if (rectArr is { Count: >= 4 })
        {
            var r = Rectangle.FromPdfArray(rectArr);
            w = r.URX - r.LLX;
            h = r.URY - r.LLY;
        }
        Dict.Set("Rect", MakeRectArray(new Rectangle(point.X, point.Y, point.X + w, point.Y + h)));
    }

    /// <summary>/Ff bit 15 (NoToggleToOff): when true the user can't clear the
    /// selected radio button by clicking it again.</summary>
    public bool NoToggleToOff
    {
        get => (FieldFlags & (1 << 14)) != 0;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 14);
            else f &= ~(1 << 14);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Visual style of the radio glyph (/MK /CA). Stored only — the FOSS
    /// appearance generator emits a filled disc regardless of this value.</summary>
    public BoxStyle Style { get; set; } = BoxStyle.Circle;

    /// <summary>Outer border shape of the radio-button widgets. Persisted on the field so
    /// it survives a save/reload round-trip; defaults to <see cref="BoxShape.Circle"/>.</summary>
    public BoxShape Shape
    {
        get => Dict.Get("AsposeBoxShape") is Aspose.Pdf.Core.PdfInteger pi
            ? (BoxShape)pi.Value
            : BoxShape.Circle;
        set => Dict.Set("AsposeBoxShape", new Aspose.Pdf.Core.PdfInteger((int)value));
    }

    /// <summary>1-based page index of the field's first widget. Shadows the inherited
    /// <see cref="Field.PageIndex"/> with a get-only public-API shape signature.</summary>
    public new int PageIndex => base.PageIndex;

    /// <summary>The radio-button option collection. Shadows the inherited
    /// <see cref="ChoiceField.Options"/> so DeclaredOnly reflection surfaces the
    /// public-API shape return type directly on RadioButtonField.</summary>
    public new OptionCollection Options => base.Options;

    /// <summary>
    /// 1-based access to the kid widget annotations on this radio field.
    /// Each entry exposes the per-option /Rect.
    /// </summary>
    public new Field this[int index]
    {
        get
        {
            var kids = Reader.Resolve(Dict.Get("Kids")) as PdfArray;
            if (kids is null || index < 1 || index > kids.Count)
                throw new System.ArgumentOutOfRangeException(nameof(index),
                    $"Index {index} is out of range. Must be between 1 and {kids?.Count ?? 0}.");
            var kidDict = Reader.ResolveDict(kids[index - 1]);
            if (kidDict is null)
                throw new System.InvalidOperationException("Kid widget reference does not resolve to a dictionary.");
            return new Field(kidDict, Reader);
        }
    }

    /// <summary>Whether this is a radio button (always true for RadioButtonField).</summary>
    public bool IsRadio => true;

    /// <summary>Whether this is a pushbutton (always false for RadioButtonField).</summary>
    public bool IsPushbutton => false;

    /// <summary>
    /// Currently selected value, or null if nothing is selected.
    /// For radio buttons, the value is the /V entry of the field or its parent.
    /// </summary>
    public string? SelectedValue
    {
        get
        {
            var v = Value;
            return v is not null && v != "Off" ? v : null;
        }
    }

    /// <summary>
    /// Gets or sets the 1-based index of the selected radio button option among
    /// the kid widget /AP/N appearance states (excluding the universal "Off"
    /// state). Setting a value outside [1..N] (or -1) writes /V = "Off",
    /// matching the PDF radio-button "no selection" convention.
    /// </summary>
    public override int Selected
    {
        get
        {
            var raw = base.Value;
            if (string.IsNullOrEmpty(raw) || raw == "Off") return -1;
            var states = CollectKidStates();
            if (IsOptIndexed(states, MaterializeOptions().Count)
                && int.TryParse(raw, out var idx))
                return idx + 1;
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] == raw) return i + 1;
            }
            return -1;
        }
        set
        {
            var states = CollectKidStates();
            string optValue;
            if (IsOptIndexed(states, MaterializeOptions().Count))
                optValue = value >= 1 && value <= states.Count
                    ? (value - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "Off";
            else
                optValue = value >= 1 && value <= states.Count ? states[value - 1] : "Off";
            ApplyRadioState(optValue);
        }
    }

    /// <summary>Drive the group /V and every kid widget's /AS for the given
    /// appearance-state name ("Off" clears the selection), marking the touched
    /// objects dirty so an incremental save persists the change. The selection
    /// of a radio group is carried by the widget kids' /AS (the chosen kid
    /// shows its on-state, every other shows "Off") and the group /V (a name) —
    /// drive both so it renders and round-trips.</summary>
    private void ApplyRadioState(string optValue)
    {
        Dict.Set("V", new PdfName(optValue));
        // Track each touched kid with its object number (taken from the /Kids
        // indirect ref — robust, unlike a reference-equality xref scan).
        var dirtyKids = new List<(int num, PdfDictionary dict)>();
        if (Reader.Resolve(Dict.Get("Kids")) is PdfArray kids)
        {
            foreach (var kidRef in kids)
            {
                if (Reader.ResolveDict(kidRef) is not PdfDictionary kid) continue;
                var on = RadioKidOnState(kid);
                kid.Set("AS", new PdfName(on is not null && on == optValue ? on : "Off"));
                var kn = kidRef is PdfIndirectRef kr ? kr.ObjectNumber
                    : OwnerDocument?.FindObjectNumber(kid) ?? -1;
                dirtyKids.Add((kn, kid));
            }
        }
        else
        {
            Dict.Set("AS", new PdfName(optValue));
        }
        var parentRef = Dict.Get("Parent");
        var parent = Reader.ResolveDict(parentRef);
        parent?.Set("V", new PdfName(optValue));
        // Mark the group (and kids/parent) dirty so an incremental save persists it.
        if (OwnerDocument is not null)
        {
            var gnum = ObjectNumber > 0 ? ObjectNumber : OwnerDocument.FindObjectNumber(Dict);
            if (gnum > 0) OwnerDocument.MarkDirty(gnum, Dict);
            foreach (var (kn, kid) in dirtyKids)
                if (kn > 0) OwnerDocument.MarkDirty(kn, kid);
            if (parent is not null)
            {
                var pn = parentRef is PdfIndirectRef pr ? pr.ObjectNumber
                    : OwnerDocument.FindObjectNumber(parent);
                if (pn > 0) OwnerDocument.MarkDirty(pn, parent);
            }
        }
    }

    /// <summary>The /AP/N on-state name (the non-"Off" key) of a radio widget kid.</summary>
    private string? RadioKidOnState(PdfDictionary kid)
    {
        var ap = Reader.ResolveDict(kid.Get("AP"));
        var n = ap is null ? null : Reader.ResolveDict(ap.Get("N"));
        if (n is not null)
            foreach (var k in n.Keys)
                if (k != "Off") return k;
        return null;
    }

    /// <summary>True when this is the /Opt-indexed radio model (PDF 32000
    /// §12.7.4.2.3): the group carries an /Opt array of export values and every
    /// kid widget's on-state is the decimal index of its /Opt entry. In that
    /// model the public <see cref="Value"/> is the /Opt EXPORT value, while /V
    /// and the kid appearance states hold the index string.</summary>
    private bool IsOptIndexed(List<string> states, int optCount)
    {
        if (optCount == 0 || states.Count == 0) return false;
        foreach (var s in states)
            if (!int.TryParse(s, out var i) || i < 0 || i >= optCount) return false;
        return true;
    }

    /// <summary>
    /// Override of <see cref="Field.Value"/>. For an /Opt-indexed group the
    /// value is the selected option's export value; otherwise it is the /AP/N
    /// appearance-state name. Setting a value that resolves to no option lands
    /// on "Off" — the canonical unselected sentinel for radio buttons.
    /// </summary>
    public override string? Value
    {
        get
        {
            var raw = base.Value;
            if (string.IsNullOrEmpty(raw) || raw == "Off") return raw;
            var opts = MaterializeOptions();
            if (IsOptIndexed(CollectKidStates(), opts.Count)
                && int.TryParse(raw, out var idx) && idx >= 0 && idx < opts.Count)
                return opts[idx].Value;
            return raw;
        }
        set
        {
            var states = CollectKidStates();
            string? state = value;
            var opts = MaterializeOptions();
            if (value is not null && IsOptIndexed(states, opts.Count))
            {
                state = null;
                for (int i = 0; i < opts.Count; i++)
                {
                    if (opts[i].Value == value)
                    {
                        state = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    }
                }
                // Compatibility: accept a raw index-state name as well.
                if (state is null && states.Contains(value)) state = value;
            }
            ApplyRadioState(state is not null && states.Contains(state) ? state : "Off");
        }
    }

    private List<string> CollectKidStates()
    {
        var values = new List<string>();
        var seen = new HashSet<string>();

        void AddFromDict(PdfDictionary dict)
        {
            var apDict = Reader.ResolveDict(dict.Get("AP"));
            if (apDict is null) return;
            var nObj = Reader.Resolve(apDict.Get("N"));
            if (nObj is PdfDictionary nDict)
            {
                foreach (var key in nDict.Keys)
                {
                    if (key != "Off" && seen.Add(key))
                        values.Add(key);
                }
            }
        }

        var kidsObj = Reader.Resolve(Dict.Get("Kids"));
        if (kidsObj is PdfArray kids)
        {
            foreach (var kidRef in kids)
            {
                var kidDict = Reader.ResolveDict(kidRef);
                if (kidDict is not null)
                    AddFromDict(kidDict);
            }
        }

        if (values.Count == 0)
        {
            var parentDict = Reader.ResolveDict(Dict.Get("Parent"));
            if (parentDict is not null)
            {
                var parentKids = Reader.Resolve(parentDict.Get("Kids")) as PdfArray;
                if (parentKids is not null)
                {
                    foreach (var kidRef in parentKids)
                    {
                        var kidDict = Reader.ResolveDict(kidRef);
                        if (kidDict is not null)
                            AddFromDict(kidDict);
                    }
                }
            }
        }

        if (values.Count == 0)
            AddFromDict(Dict);

        return values;
    }

    /// <summary>Whether this button field's selectable buttons live on separate kid
    /// widgets — i.e. it is a real radio group — as opposed to a single merged-widget
    /// button that only carries its own /AP/N on-state. Only the former surfaces
    /// synthesized <see cref="Options"/>.</summary>
    private bool HasRadioKidWidgets()
    {
        if (Reader.Resolve(Dict.Get("Kids")) is PdfArray kids && kids.Count > 0)
            return true;
        if (Reader.ResolveDict(Dict.Get("Parent")) is PdfDictionary parent &&
            Reader.Resolve(parent.Get("Kids")) is PdfArray parentKids && parentKids.Count > 0)
            return true;
        return false;
    }

    /// <summary>
    /// A radio group's option list is carried by the kid widgets' /AP/N on-states,
    /// not (usually) by an /Opt array, so the base /Opt reading yields nothing.
    /// Fall back to one option per distinct kid appearance state so
    /// <see cref="Options"/> reflects the selectable buttons. Reads /V straight from
    /// the dict to mark the selected option (calling <see cref="Value"/> would recurse
    /// back into this method).
    /// </summary>
    protected internal override List<Option> MaterializeOptions()
    {
        var baseOpts = base.MaterializeOptions();
        if (baseOpts.Count > 0) return baseOpts;

        // Only a genuine radio GROUP — whose buttons are separate kid widgets — surfaces
        // synthesized options. A single merged-widget button that carries its own /AP/N
        // on-state (no /Kids of its own, no shared parent group) reports no
        // options. CollectKidStates would otherwise fall back to the
        // field's own /AP/N (correct for resolving Value/Selected) and invent a phantom
        // option here.
        if (!HasRadioKidWidgets()) return baseOpts;

        var raw = Reader.Resolve(Dict.Get("V")) switch
        {
            PdfName n => n.Value,
            PdfString s => s.ToText(),
            _ => null,
        };
        var result = new List<Option>();
        foreach (var state in CollectKidStates())
        {
            var option = new Option(state, state)
            {
                Index = result.Count + 1,
                Selected = raw is not null && raw == state,
            };
            result.Add(option);
        }
        return result;
    }
}
