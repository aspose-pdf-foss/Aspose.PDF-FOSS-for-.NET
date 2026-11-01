using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// Represents a form field. Implements IEnumerable&lt;Field&gt; to allow
/// iterating over child fields (Kids array in the PDF dictionary).
/// </summary>
public partial class Field : Aspose.Pdf.Annotations.WidgetAnnotation, ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;

    /// <summary>Explicit multiline line pitch (pt) from a rich-text field's style string
    /// (<c>line-height: Npt</c>); null keeps the default 1.15× font-size pitch.</summary>
    internal double? StyleLineHeightPt;

    /// <summary>Set when a <c>Style</c> string NAMED the /DA face (<c>font: 'Tahoma' 10pt</c>).
    /// Such a field paces its multiline appearance by the face's own line pitch rather than
    /// by the head-bbox model a loaded /DR face uses - see TextBoxField.StyleFacePitchEm.</summary>
    internal bool StyleNamedFace;

    /// <summary>Set when the <c>Style</c> string asked for BOLD. Only the Standard-14
    /// Courier paces differently bold (1199 against 1203 per mille); every other face
    /// measured paces the same either way.</summary>
    internal bool StyleFaceBold;

    /// <summary>Set when the CALLER named the /DA font size - by assigning a whole
    /// DefaultAppearance or through a Style string. The global TextBoxField
    /// MinFontSize / MaxFontSize clamps bound the field auto-fit only, so a pinned size
    /// is left alone; measured: "Tf 10" is written for a 10 pt
    /// field even with the clamps pinned to 15/15.</summary>
    internal bool DaFontSizePinned;

    /// <summary>Multiline pitch persisted in the field's /DS style string — unlike
    /// <see cref="StyleLineHeightPt"/> it survives the field being re-wrapped from
    /// its dictionary (Flatten re-wraps before stamping).</summary>
    private protected double? DsLineHeightPt()
    {
        var ds = _dict.Get("DS") is PdfString dsStr
            ? Compat.Latin1.GetString(dsStr.Value)
            : null;
        if (string.IsNullOrEmpty(ds)) return null;
        var m = SafeRegex.Match(ds,
            @"line-height\s*:\s*([\d.]+)\s*pt",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
            ? v : null;
    }

    internal Field(PdfDictionary dict, PdfReader reader) : base(dict, reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>Detached ctor — a document-less field used as a configuration holder
    /// (e.g. a generator <see cref="RadioButtonOptionField"/> before it is placed into a
    /// real form). Backed by a fresh empty dict + the shared empty reader.</summary>
    private protected Field() : base()
    {
        _dict = Dict;
        _reader = InternalReader;
    }

    /// <summary>Create a field bound to a document. The field is not yet attached to the document's form — use <see cref="Form.Add(Field)"/> after configuring.</summary>
    public Field(Document doc) : base(doc)
    {
        if (doc is null) throw new ArgumentNullException(nameof(doc));
        _dict = Dict;
        _reader = InternalReader;
        OwnerDocument = doc;
    }


    /// <summary>Low-level view of the field's underlying PDF dictionary, surfacing raw
    /// stored bytes (see <see cref="FieldDictionaryView"/>).</summary>
    internal new FieldDictionaryView DictionaryView => FieldDictionaryView.For(_dict, _reader ?? Aspose.Pdf.IO.PdfReader.Empty);

    /// <summary>The owning document (for dirty tracking during incremental save).</summary>
    internal Document? OwnerDocument { get; set; }

    /// <summary>The field type.</summary>
    public FieldType Type => DetermineType();

    /// <summary>The fully qualified field name.</summary>
    public new string? FullName => BuildFullName();

    /// <summary>Text-specific horizontal alignment override applied to
    /// the field's widget appearance. Stored only — mirrors
    /// Annotation.TextHorizontalAlignment.</summary>
    public new Aspose.Pdf.HorizontalAlignment TextHorizontalAlignment
    {
        get => _textHorizontalAlignment;
        set { _textHorizontalAlignment = value; TextHorizontalAlignmentSet = true; }
    }

    private Aspose.Pdf.HorizontalAlignment _textHorizontalAlignment = Aspose.Pdf.HorizontalAlignment.Left;

    /// <summary>True once a caller set <see cref="TextHorizontalAlignment"/>: its Left default asks for nothing.</summary>
    internal bool TextHorizontalAlignmentSet { get; private set; }

    /// <summary>
    /// 1-based index of the page that owns this field's widget. Resolved by
    /// walking the document's page tree and matching the dict referenced by
    /// the field's (or a parent's) /P entry, or by scanning each page's
    /// /Annots when /P is absent. Returns -1 when no owning page is found.
    /// </summary>
    public new int PageIndex
    {
        get
        {
            var idx = ResolvePageIndexFor(_dict);
            if (idx > 0) return idx;
            // Multi-widget terminal fields (e.g. a checkbox whose options live in
            // separate widget kids) keep /P on each widget, not on the field dict.
            // Fall back to the first widget kid's owning page.
            foreach (var kidDict in AllKids())
            {
                idx = ResolvePageIndexFor(kidDict);
                if (idx > 0) return idx;
            }
            return -1;
        }
    }

    /// <summary>1-based page index resolved from this field's OWN dict only
    /// (its /P or /Annots membership), without the multi-widget kid fallback that
    /// <see cref="PageIndex"/> applies. Returns -1 for a group/non-terminal field
    /// that has no widget of its own. Used to distinguish "on a page" from
    /// "merely has children on a page" in the form JSON export.</summary>
    internal int OwnPageIndex => ResolvePageIndexFor(_dict);

    /// <summary>The partial field name (/T).</summary>
    public string? PartialName
    {
        get => GetString("T");
        set
        {
            if (value is null)
                _dict.Remove("T");
            else
                _dict.Set("T", EncodePdfTextString(value));
        }
    }

    /// <summary>
    /// The field's "Normal" appearance (the /AP /N entry). Returns an
    /// <see cref="XForm"/> wrapping the appearance stream so callers can
    /// inspect content-stream operators via <c>NormalAppearance.Contents</c>.
    ///
    /// Resolution order: this field's /AP/N first; if absent, walk into the
    /// single widget kid's /AP/N (terminal fields with one widget). When
    /// /AP/N is a state-keyed dict (checkbox /Yes vs /Off), the first
    /// non-Off stream entry is returned.
    /// </summary>
    public new XForm? NormalAppearance
    {
        get
        {
            var stream = ResolveNormalAppearanceStream(_dict);
            if (stream is null)
            {
                var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
                if (kids is { Count: 1 })
                {
                    var kidDict = _reader.ResolveDict(kids[0]);
                    if (kidDict is not null)
                        stream = ResolveNormalAppearanceStream(kidDict);
                }
            }
            return stream is null ? null : new XForm(stream, _reader);
        }
    }

    /// <summary>The field widget's appearance streams (the /AP entry), keyed by
    /// appearance name. State-keyed sub-dictionaries (e.g. checkbox /N with
    /// /Yes vs /Off) are flattened to compound keys like <c>"N.Yes"</c> /
    /// <c>"D.Off"</c>. Resolves this field's own /AP, falling back to a single
    /// widget kid's, matching <see cref="Aspose.Pdf.Annotations.Annotation.Appearance"/>.</summary>
    public new Aspose.Pdf.Annotations.AppearanceDictionary Appearance
    {
        get
        {
            var result = new Aspose.Pdf.Annotations.AppearanceDictionary();
            var ap = _reader.ResolveDict(_dict.Get("AP"));
            if (ap is null)
            {
                var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
                if (kids is { Count: 1 } && _reader.ResolveDict(kids[0]) is { } kidDict)
                    ap = _reader.ResolveDict(kidDict.Get("AP"));
            }
            if (ap is null) return result;

            foreach (var key in ap.Keys)
            {
                var obj = _reader.Resolve(ap.Get(key));
                if (obj is PdfStream stream)
                {
                    result[key] = new XForm(stream, _reader);
                }
                else if (obj is PdfDictionary stateDict)
                {
                    foreach (var stateKey in stateDict.Keys)
                        if (_reader.ResolveStream(stateDict.Get(stateKey)) is { } stStream)
                            result[key + "." + stateKey] = new XForm(stStream, _reader);
                }
            }
            return result;
        }
    }

    /// <summary>The action triggered when this field is activated (/A entry).
    /// Reads from the field dict; if absent and the field has a single
    /// widget kid, falls back to the kid's /A.</summary>
    public new Annotations.PdfAction? OnActivated
    {
        get
        {
            var actionDict = _reader.ResolveDict(_dict.Get("A"));
            if (actionDict is null)
            {
                // Fall back to the single widget kid (common case for non-radio fields)
                var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
                if (kids is { Count: 1 })
                {
                    var kid = _reader.ResolveDict(kids[0]);
                    actionDict = _reader.ResolveDict(kid?.Get("A"));
                }
            }
            return actionDict is not null ? Annotations.PdfAction.Create(actionDict, _reader) : null;
        }
        set
        {
            if (value is null) _dict.Remove("A");
            else _dict.Set("A", value.Dict);
        }
    }

    /// <summary>The field value (/V). Inherits from parent if not set on this dict.</summary>
    public virtual string? Value
    {
        get
        {
            // Auto-calculate: a field carrying an /AA/C calculate action reports its
            // recomputed value (Acrobat auto-calculates on read). Only
            // text/choice fields calculate; guard is cheap when no /AA is present.
            // An explicitly assigned value wins over the calculation.
            if (!IsExplicitlyAssigned(_dict) && _dict.Get("AA") is not null
                && FieldCalculateScript.ComputeValue(_dict, _reader) is { } computed)
                return computed;

            var v = _reader.Resolve(_dict.Get("V"));
            if (v is null or PdfNull)
            {
                // Inherit /V from parent (e.g. radio button widget kids)
                var parent = _reader.ResolveDict(_dict.Get("Parent"));
                while (parent is not null)
                {
                    v = _reader.Resolve(parent.Get("V"));
                    if (v is not null and not PdfNull) break;
                    parent = _reader.ResolveDict(parent.Get("Parent"));
                }
            }
            return v switch
            {
                PdfString s => s.ToText(),
                PdfName n => n.Value,
                _ => null,
            };
        }
        set
        {
            SetValue(value);
        }
    }

    [System.ThreadStatic] private static bool _recalculating;

    // Fields whose /V was EXPLICITLY assigned through the API in this session.
    // An explicit assignment wins over the form's /CO auto-calculation: the
    // recalculation pass must not overwrite it, and the value getter reports
    // the assigned value rather than the recomputed one. (An assigned
    // value is kept verbatim on calculated fields.)
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, object> _explicitlyAssigned = new();

    private protected static void MarkExplicitlyAssigned(PdfDictionary dict)
        => _explicitlyAssigned.AddOrUpdate(dict, string.Empty);

    private protected static bool IsExplicitlyAssigned(PdfDictionary dict)
        => _explicitlyAssigned.TryGetValue(dict, out _);

    private protected static void ClearExplicitAssignment(PdfDictionary dict)
        => _explicitlyAssigned.Remove(dict);

    // ── Child field iteration (Kids array) ────────────────────────────

    /// <summary>
    /// Number of kids in this field's /Kids array — either child fields
    /// (hierarchy nodes) or widget annotations (a terminal field with
    /// multiple visual widgets, such as a grouped checkbox/radio). Returns
    /// 0 if this is a single-widget leaf field with no /Kids.
    /// </summary>
    public int Count
    {
        get
        {
            int count = HasMergedSelfWidget ? 1 : 0;
            foreach (var _ in AllKids()) count++;
            return count;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── ICollection<WidgetAnnotation> (public-surface compatibility: a field IS a
    // collection of its visual widgets / child fields; the corpus casts a
    // group field to ICollection<WidgetAnnotation> and reads Count). The
    // mutating members are not part of any pinned behaviour — Add/Remove of
    // widgets goes through Form.AddFieldAppearance / RemoveFieldAppearance.
    bool ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.IsReadOnly => false;

    void ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.Add(Aspose.Pdf.Annotations.WidgetAnnotation item)
        => throw new NotSupportedException("Add a widget through Form.AddFieldAppearance.");

    void ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.Clear()
        => throw new NotSupportedException("Remove widgets through Form.RemoveFieldAppearance.");

    bool ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.Contains(Aspose.Pdf.Annotations.WidgetAnnotation item)
    {
        if (item is null) return false;
        return this.Any(w => ReferenceEquals(w, item) || ReferenceEquals(w.Dict, item.Dict));
    }

    void ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.CopyTo(
        Aspose.Pdf.Annotations.WidgetAnnotation[] array, int arrayIndex)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        foreach (var w in this) array[arrayIndex++] = w;
    }

    bool ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.Remove(Aspose.Pdf.Annotations.WidgetAnnotation item)
        => throw new NotSupportedException("Remove widgets through Form.RemoveFieldAppearance.");

#pragma warning disable S2365 // public API: a fresh copy per read is the published contract
    /// <summary>Field-typed child kids of this field as a snapshot
    /// array — convenience accessor mirroring
    /// <see cref="Form.Fields"/> on the form. Each entry is a freshly
    /// materialised <see cref="Field"/> over the matching /Kids entry.</summary>
    public Field[] Fields => FieldKids().ToArray();
#pragma warning restore S2365

    // A Field is now itself a WidgetAnnotation, so the former implicit
    // Field -> WidgetAnnotation conversion is unnecessary (and illegal to a
    // base type); the inheritance relationship supplies it directly.

    /// <summary>
    /// Gets the widget annotation for the kid at the 1-based index.
    /// </summary>
    public Aspose.Pdf.Annotations.WidgetAnnotation this[int index]
    {
        get
        {
            // The same order as the enumerator and Count: a merged self widget beside grown kids is the first.
            if (HasMergedSelfWidget && index == 1)
                return new Aspose.Pdf.Annotations.WidgetAnnotation(_dict, _reader);
            int i = HasMergedSelfWidget ? 2 : 1;
            foreach (var kidDict in AllKids())
            {
                if (i == index)
                {
                    // A kid that is itself a field node comes back as the typed child
                    // field (so `field[1] as TextBoxField` works, matching the
                    // enumerator); a pure widget kid stays a plain WidgetAnnotation.
                    if (kidDict.ContainsKey("T") || kidDict.ContainsKey("FT"))
                    {
                        var child = Field.Create(kidDict, _reader);
                        child.OwnerDocument = OwnerDocument;
                        child.Parent = this;
                        return child;
                    }
                    return new Aspose.Pdf.Annotations.WidgetAnnotation(kidDict, _reader);
                }
                i++;
            }
            // A terminal field with no /Kids is merged with its single widget: the
            // field dict itself is that widget, addressable at index 1.
            if (i == 1 && index == 1 &&
                (_dict.GetName("Subtype") == "Widget" || _dict.ContainsKey("Rect")))
                return new Aspose.Pdf.Annotations.WidgetAnnotation(_dict, _reader);
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Index {index} is out of range. Must be between 1 and {Count}.");
        }
    }

    /// <summary>
    /// Gets the widget annotation for the kid whose partial name matches.
    /// Throws ArgumentException if not found.
    /// </summary>
    public Aspose.Pdf.Annotations.WidgetAnnotation this[string name]
    {
        get
        {
            var child = FieldKids().FirstOrDefault(child => string.Equals(child.PartialName, name, StringComparison.Ordinal));
            if (child is not null)
                return new Aspose.Pdf.Annotations.WidgetAnnotation(child.Dict, child.Reader);
            throw new ArgumentException($"Kid field not found: {name}");
        }
    }

    // ── Properties ──────────────────────────────────────────────────────

    /// <summary>The default value (/DV).</summary>
    public string? DefaultValue => GetString("DV");

    /// <summary>The alternate field name / tooltip (/TU entry).</summary>
    public string? AlternateName
    {
        get => GetString("TU");
        set
        {
            if (value is null)
                Dict.Remove("TU");
            else
                Dict.Set("TU", EncodePdfTextString(value));
        }
    }



    /// <summary>The widget annotation flags (/F entry), e.g. Print / Hidden /
    /// ReadOnly. Matches the <c>Field.Flags</c> surface (which exposes
    /// the annotation flags, not the field /Ff flags).</summary>
    public new Aspose.Pdf.Annotations.AnnotationFlags Flags
    {
        get => (Aspose.Pdf.Annotations.AnnotationFlags)(int)_dict.GetInt("F");
        set => _dict.Set("F", new Aspose.Pdf.Core.PdfInteger((int)value));
    }

    /// <summary>The form-field flags (/Ff entry) — Required, Multiline, Combo,
    /// etc. Used internally to derive the typed Is* properties.</summary>
    internal int FieldFlags => (int)_dict.GetInt("Ff");

    /// <summary>Bit 21 of /Ff: the text field holds a file path and offers a file-select
    /// dialog (PDF 32000-1, table 228).</summary>
    internal const int FileSelectFlag = 1 << 20;

    /// <summary>
    /// Tab order index for this field. Returns -1 if not specified.
    /// The tab order is derived from the field's position in the page's /Annots array
    /// combined with the /Tabs entry on the page dictionary.
    /// When no page association can be determined, falls back to the /StructParent
    /// or the internal form index.
    /// </summary>
    public int TabOrder
    {
        set => _dict.Set("TI", new PdfInteger(value));
        get
        {
            // /TI (Tab Index) is a non-standard but common extension
            if (_dict.ContainsKey("TI"))
                return (int)_dict.GetInt("TI");

            // /StructParent gives a unique ordering index per annotation
            if (_dict.ContainsKey("StructParent"))
                return (int)_dict.GetInt("StructParent");

            // Walk the page's Annots array for the field's widget: its own dict, or a widget kid of its
            // own (1-based to match the public API contract).
            var widgets = new List<PdfDictionary> { _dict };
            widgets.AddRange(AllKids());
            var pageDict = FindPageDict() ?? widgets.Skip(1).Select(kid => _reader.ResolveDict(kid.Get("P"))).FirstOrDefault(p => p is not null);
            if (pageDict is not null)
            {
                var annotsObj = _reader.Resolve(pageDict.Get("Annots"));
                if (annotsObj is PdfArray annots)
                {
                    for (int i = 0; i < annots.Count; i++)
                    {
                        var resolved = _reader.ResolveDict(annots[i]);
                        if (widgets.Any(widget => ReferenceEquals(resolved, widget)))
                            return i + 1;
                    }

                    // Fallback: match by partial name
                    var partialName = PartialName;
                    if (partialName is not null)
                    {
                        for (int i = 0; i < annots.Count; i++)
                        {
                            var resolved = _reader.ResolveDict(annots[i]);
                            if (resolved is null) continue;
                            var t = resolved.Get("T");
                            string? name = t switch
                            {
                                PdfString s => s.ToText(),
                                PdfName n => n.Value,
                                _ => null,
                            };
                            if (name == partialName)
                                return i + 1;
                        }
                    }
                }
            }

            return -1;
        }
    }

    /// <summary>Whether the field is read-only.</summary>
    public bool IsReadOnly => (FieldFlags & 1) != 0;

    /// <summary>Whether the field is read-only (settable alias for API compatibility).</summary>
    public new bool ReadOnly
    {
        get => IsReadOnly;
        set
        {
            var flags = (int)_dict.GetInt("Ff");
            if (value)
                flags |= 1;
            else
                flags &= ~1;
            _dict.Set("Ff", new PdfInteger(flags));
        }
    }

    /// <summary>Whether the field is required.</summary>
    public bool IsRequired => (FieldFlags & 2) != 0;

    /// <summary>
    /// Appearance characteristics (border / background colors, widget rotation).
    /// Delegates to the annotation-level instance, which seeds from the widget's
    /// /MK dictionary and writes colour changes back through to it.
    /// </summary>
    public new Aspose.Pdf.Annotations.Characteristics Characteristics
        => base.Characteristics;

    private Aspose.Pdf.Annotations.DefaultAppearance? _defaultAppearance;

    /// <summary>The appearance object handed to or out of this field, without parsing the /DA for one.</summary>
    internal Aspose.Pdf.Annotations.DefaultAppearance? DefaultAppearanceIfSet => _defaultAppearance;

    /// <summary>
    /// Default appearance (font, size, colour) of the field — the field-level
    /// counterpart of <see cref="Aspose.Pdf.Annotations.WidgetAnnotation"/>.
    /// Lazily allocated; mutations on the returned instance are visible to
    /// subsequent reads through the same Field.
    /// </summary>
    public new Aspose.Pdf.Annotations.DefaultAppearance DefaultAppearance
    {
        get
        {
            if (_defaultAppearance is not null) return _defaultAppearance;
            var parsed = ParseDefaultAppearance();
            var handedSize = parsed.FontSize;
            // The appearance read from a field of the file stays that field's: a size, face
            // or colour set on it is written like a whole appearance assigned. A field made in
            // code writes the change to its /DA, and redraws an appearance it already has; only
            // a size set on it pins the size.
            void Write()
            {
                parsed.Changed = null; // the write itself may settle the face name
                try
                {
                    if (IsNumberedObject) DefaultAppearance = parsed;
                    else WriteChangedAppearance(parsed, sizeSet: !parsed.FontSize.Equals(handedSize));
                }
                finally { parsed.Changed = Write; }
            }
            parsed.Changed = Write;
            return _defaultAppearance = parsed;
        }
        set
        {
            _defaultAppearance = value;
            // A caller handing over a whole DefaultAppearance has PINNED its size, which
            // takes the field out of the global auto-fit clamps (see TextBoxField).
            if (value is not null && value.FontSize > 0) DaFontSizePinned = true;
            // Persist the /DA string immediately so an appearance regenerated
            // before the field is attached to a form (e.g. setting Value first)
            // already reflects this font/size/colour.
            if (value is not null)
                Dict.Set("DA", new PdfString(
                    Compat.Latin1.GetBytes(value.ToAppearanceString())));
            // When the field is already part of a form, embed an embeddable font
            // immediately (the font may be set after Form.Add); this re-points /DA
            // at the embedded resource. A no-op otherwise.
            Form.EmbedDefaultAppearanceFont(this);
            DropWidgetAppearanceStrings();
            // Drop any appearance generated with the previous /DA (e.g. built with
            // the default /Helv during Form.Add) and rebuild it with the new
            // font/size/colour.
            Dict.Remove("AP");
            GenerateAppearance();
        }
    }

    /// <summary>A change to the handed-out appearance of a field made in code: the /DA follows it, a font
    /// the form can embed is embedded once the field is on a form, and an appearance already drawn is drawn
    /// again with the new font, size and colour.</summary>
    private void WriteChangedAppearance(Aspose.Pdf.Annotations.DefaultAppearance appearance, bool sizeSet)
    {
        if (sizeSet && appearance.FontSize > 0) DaFontSizePinned = true;
        Dict.Set("DA", new PdfString(Compat.Latin1.GetBytes(appearance.ToAppearanceString())));
        Form.EmbedDefaultAppearanceFont(this);
        DropWidgetAppearanceStrings();
        if (Dict.Get("AP") is null) return;
        Dict.Remove("AP");
        GenerateAppearance();
    }

    /// <summary>The field rectangle.</summary>
    public new Rectangle? Rect
    {
        get
        {
            var arr = _reader.Resolve(_dict.Get("Rect")) as PdfArray;
            if (arr is { Count: >= 4 }) return Rectangle.FromPdfArray(arr, _reader);
            // Field whose geometry lives on widget kids: return the bounding box (union)
            // of all descendant widget /Rects, so a multi-widget field reports its full
            // extent.
            return UnionKidRect(_dict, 0);
        }
        set
        {
            if (value is null)
            {
                _dict.Remove("Rect");
            }
            else
            {
                var arr = new PdfArray();
                arr.Add(new PdfReal(value.LLX));
                arr.Add(new PdfReal(value.LLY));
                arr.Add(new PdfReal(value.URX));
                arr.Add(new PdfReal(value.URY));
                _dict.Set("Rect", arr);
            }
            OnRectChanged();
        }
    }

    // Field border is inherited from WidgetAnnotation.Border, which lazily
    // resolves a non-null Border from the widget's /BS dictionary (and writes
    // back through the setter). The previous `new Border? { get; set; }` shadow
    // here defaulted to null, so `field.Border.Width = 1` threw NRE.

    /// <summary>The field color (stored in /DA default appearance string as RGB).</summary>
    public new Color Color
    {
        get
        {
            var da = GetString("DA");
            if (da is null) return Color.Black;
            // Parse "r g b rg" or "r g b RG" from DA string. The colour operator
            // is normally the last token, so the scan must include the final index
            // (the match only reads tokens before i, so there's no overrun).
            var parts = da.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if ((parts[i] == "rg" || parts[i] == "RG") && i >= 3 && double.TryParse(parts[i - 3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) &&
                    double.TryParse(parts[i - 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g) &&
                    double.TryParse(parts[i - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b))
                    return Color.FromRgb(r, g, b);
            }
            return Color.Black;
        }
        set => _dict.Set("DA", new PdfString(Compat.Latin1.GetBytes(WithTextColor(GetString("DA") ?? "", value))));
    }

    /// <summary>An appearance string with its colour operators replaced by an RGB fill of this colour, the font
    /// and size kept.</summary>
    internal static string WithTextColor(string da, Color value)
    {
        var r = (value.R / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var g = (value.G / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var b = (value.B / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        // Remove existing color operators from DA
        var parts = da.Split(' ').ToList();
        int i = parts.Count - 1;
        while (i >= 0)
        {
            if ((parts[i] == "rg" || parts[i] == "RG" || parts[i] == "g" || parts[i] == "G") && i >= 1)
            {
                int numArgs = parts[i] == "rg" || parts[i] == "RG" ? 3 : 1;
                int start = Math.Max(0, i - numArgs);
                parts.RemoveRange(start, i - start + 1);
                i = start;
            }
            i--;
        }
        var newDa = string.Join(" ", parts).Trim();
        if (newDa.Length > 0) newDa += " ";
        newDa += $"{r} {g} {b} rg";
        return newDa;
    }

    internal new PdfDictionary Dict => _dict;
    internal PdfReader Reader => _reader;

    // ── Public-API shape additions ───────────────────────────────

    private int _annotationIndex = 1;

    /// <summary>1-based tab position of this field's widget within its page's /Annots
    /// array — the array order is the page tab order. Reading it returns the widget's
    /// current position; setting it moves the widget to that slot, swapping it with the
    /// widget that previously occupied the slot. Falls back
    /// to a stored value when the field has no widget on a page.</summary>
    public int AnnotationIndex
    {
        get
        {
            var (_, idx) = LocateWidgetInAnnots();
            return idx > 0 ? idx : _annotationIndex;
        }
        set
        {
            _annotationIndex = value;
            var (annots, idx) = LocateWidgetInAnnots();
            if (annots is null || idx <= 0) return;
            if (value < 1 || value > annots.Count || value == idx) return;
            // Swap the widget into the requested tab slot; the widget previously there
            // takes this field's old slot. Swap the raw array entries (indirect refs) so
            // the new /Annots order persists on save.
            var moved = annots[idx - 1];
            var displaced = annots[value - 1];
            annots.ReplaceAt(idx - 1, displaced);
            annots.ReplaceAt(value - 1, moved);
        }
    }

    /// <summary>Find the page /Annots array that holds this field's widget and the
    /// widget's 1-based position in it. Considers the field's own dict (single-widget
    /// leaf) and each /Kids widget. Returns (null, -1) when no widget is on a page.</summary>
    private (Core.PdfArray? annots, int index) LocateWidgetInAnnots()
    {
        var candidates = new List<PdfDictionary> { _dict };
        foreach (var kid in AllKids()) candidates.Add(kid);

        var pages = new PageCollection(_reader);
        for (var p = 1; p <= pages.Count; p++)
        {
            if (_reader.Resolve(pages[p].Dict.Get("Annots")) is not Core.PdfArray annots) continue;
            for (var i = 0; i < annots.Count; i++)
            {
                var ad = _reader.ResolveDict(annots[i]);
                if (ad is null) continue;
                if (candidates.Any(cand => ReferenceEquals(ad, cand))) return (annots, i + 1);
            }
        }
        return (null, -1);
    }

    /// <summary>Global flag: when true, a field's value is auto-resized to fit its
    /// widget rectangle. Static to match the public surface (a process-wide default);
    /// one flag for every field type, read on the single-line and the multiline path alike.</summary>
    public static bool FitIntoRectangle { get; set; }

#pragma warning disable S2325 // public API: the published instance member
    /// <summary>Whether the collection is thread-safe. Always false.</summary>
    public bool IsSynchronized => false;
#pragma warning restore S2325

    /// <summary>Synchronization root; returns this instance.</summary>
    public object SyncRoot => this;

    /// <summary>Maximum font size used by the field's appearance auto-fit. Stored only.</summary>
    public double MaxFontSize { get; set; }

    /// <summary>Minimum font size used by the field's appearance auto-fit. Stored only.</summary>
    public double MinFontSize { get; set; }

    /// <summary>Mapping name used by external scripts to identify the field. Stored only.</summary>
    public string? MappingName
    {
        get
        {
            var obj = Dict.Get("TM");
            return obj is PdfString s ? s.ToText() : null;
        }
        set
        {
            if (value is null) Dict.Remove("TM");
            else Dict.Set("TM", EncodePdfTextString(value));
        }
    }

#pragma warning disable S2325 // public API: the published instance member
    /// <summary>Execute the supplied JavaScript action against this field. Stored intent only — the JS interpreter is not invoked.</summary>
    public void ExecuteFieldJavaScript(Aspose.Pdf.Annotations.JavascriptAction javaScriptAction)
#pragma warning restore S2325
    {
        _ = javaScriptAction;
    }

    /// <summary>Flatten this field — turn its value into static page content. Currently no-op; per-field flattening is not implemented.</summary>
    public new void Flatten()
    {
        // Flatten just this field: fold its widget appearance(s) into the owning page content and
        // remove it from the AcroForm. Delegates to the form so the placement (§12.5.5) and FRM
        // registration match the document/form flatten path.
        OwnerDocument?.Form.FlattenField(this);
    }

#pragma warning disable S2325, S3400 // public API: the published instance member
    /// <summary>Recalculate the field's value from its calculation script. Currently no-op; returns false.</summary>
    public bool Recalculate() => false;
#pragma warning restore S2325, S3400

    /// <summary>The block size the generator reserves for a field placed through
    /// Paragraphs: the layout Width/Height a field type stores (text box, button)
    /// else the widget rectangle's size.</summary>
    internal virtual (double w, double h) GeneratorBlockSize()
        => (base.Width, base.Height);

    /// <summary>A field an HTML page sizes as a percentage of its table cell: the share of the cell's
    /// content box its layout width takes (0: the field keeps its own width).</summary>
    internal double CellWidthFraction { get; set; }

    /// <summary>Resize the field's layout box to a new width, keeping its height.</summary>
    internal virtual void SetGeneratorBlockWidth(double width)
        => Rect = new Rectangle(0, 0, width, base.Height);

}
