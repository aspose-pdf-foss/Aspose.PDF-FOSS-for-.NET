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
        // A generator checkbox is registered on its paragraph's page before the
        // table lays it out; a cell that spills to a later page moves the widget
        // there, so drop it from every other page it was listed on.
        if (OwnerDocument is { } doc)
            for (var pi = 1; pi <= doc.Pages.Count; pi++)
            {
                var other = doc.Pages[pi];
                if (ReferenceEquals(other, page) || ReferenceEquals(other.Dict, page.Dict)) continue;
                if (Reader.Resolve(other.Dict.Get("Annots")) is not PdfArray oa) continue;
                for (var i = oa.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(Reader.Resolve(oa[i]), Dict)) oa.RemoveAt(i);
            }
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
    /// <summary>Select the kid widget whose on-value is <paramref name="state"/> (all
    /// others go to "Off") and record <paramref name="storedValue"/> as the field's /V.
    /// The two differ when the assigned value names no declared state: the value is kept
    /// verbatim while no kid is selected (see <see cref="SetValue"/>).</summary>
    private void ApplyGroupedValue(string state, string? storedValue = null)
    {
        Dict.Set("V", new PdfName(storedValue ?? state));
        Dict.Set("AS", new PdfName(state));
        foreach (var kid in AllKids())
        {
            var ov = KidOnValue(kid);
            kid.Set("AS", new PdfName(ov == state ? state : "Off"));
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
        // /V is the value, verbatim, for both shapes; a grouped box with no /V of its
        // own still answers from whichever kid widget is showing its on-state.
        get => Dict.GetName("V") ?? (HasWidgetKids ? SelectedKidState() : "Off");
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
            // Checked follows the APPEARANCE state, not /V: /V is stored verbatim
            // (see SetValue), so a box whose value is the data's own "False" is
            // unchecked. A document that carries no /AS at all still answers from /V.
            if (Dict.GetName("AS") is string state) return state != "Off";
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
        // LAW (probed with a box whose declared on-state is "True"):
        // the assigned string is stored in /V VERBATIM — "False", "Bogus" and even ""
        // survive — while the APPEARANCE state /AS is the normalised one: the declared
        // on-state when the value names one, otherwise "Off". So a data import can carry
        // its own vocabulary in the value while the widget still renders on/off correctly.
        var name = NormalizeOnValue(value);
        // Remember an arbitrary (non-state) assignment so a static XFA form can
        // persist it into the datasets verbatim — the AcroForm appearance still
        // normalises to "Off" below.
        RawNonStateValue = IsArbitraryNonState(value) ? value : null;
        if (HasWidgetKids) { ApplyGroupedValue(name, value ?? "Off"); return; }
        Dict.Set("V", new PdfName(value ?? "Off"));
        Dict.Set("AS", new PdfName(name));
        MarkCheckboxDirty();
    }
}
