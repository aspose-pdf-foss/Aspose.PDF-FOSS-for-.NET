using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// Represents a form field. Implements IEnumerable&lt;Field&gt; to allow
/// iterating over child fields (Kids array in the PDF dictionary).
/// </summary>
public class Field : Aspose.Pdf.Annotations.WidgetAnnotation, IEnumerable<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;

    /// <summary>Explicit multiline line pitch (pt) from a rich-text field's style string
    /// (<c>line-height: Npt</c>); null keeps the default 1.15× font-size pitch.</summary>
    internal double? StyleLineHeightPt;

    /// <summary>Multiline pitch persisted in the field's /DS style string — unlike
    /// <see cref="StyleLineHeightPt"/> it survives the field being re-wrapped from
    /// its dictionary (Flatten re-wraps before stamping).</summary>
    private protected double? DsLineHeightPt()
    {
        var ds = _dict.Get("DS") is PdfString dsStr
            ? System.Text.Encoding.Latin1.GetString(dsStr.Value)
            : null;
        if (string.IsNullOrEmpty(ds)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(ds,
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

    /// <summary>The PDF object number for this field's dictionary (-1 if unknown).</summary>
    internal int ObjectNumber { get; set; } = -1;

    /// <summary>Low-level view of the field's underlying PDF dictionary, surfacing raw
    /// stored bytes (see <see cref="FieldDictionaryView"/>).</summary>
    internal FieldDictionaryView EngineDict => new(_dict, _reader);

    /// <summary>The owning document (for dirty tracking during incremental save).</summary>
    internal Document? OwnerDocument { get; set; }

    /// <summary>The field type.</summary>
    public FieldType Type => DetermineType();

    /// <summary>The fully qualified field name.</summary>
    public new string? FullName => BuildFullName();

    /// <summary>Text-specific horizontal alignment override applied to
    /// the field's widget appearance. Stored only — parity
    /// with Annotation.TextHorizontalAlignment.</summary>
    public new Aspose.Pdf.HorizontalAlignment TextHorizontalAlignment { get; set; } = Aspose.Pdf.HorizontalAlignment.Left;

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

    /// <summary>Resolve the 1-based owning page index for a widget/field dict by
    /// its /P entry (walking /Parent), then by scanning each page's /Annots.
    /// Returns -1 when no owning page is found.</summary>
    private int ResolvePageIndexFor(PdfDictionary dict)
    {
        // The widget annotation dicts this field is drawn by: the field dict
        // itself (merged field+widget) plus any pure-widget /Kids (a field whose
        // visual widget is a separate object). A /Kids entry that carries its own
        // /T is a child *field*, not a widget of this field, so it is excluded —
        // a non-terminal (group) field is not itself on any single page.
        var widgets = new List<PdfDictionary> { dict };
        if (_reader.Resolve(dict.Get("Kids")) is Core.PdfArray kids)
            foreach (var kid in kids)
                if (_reader.ResolveDict(kid) is { } kidDict && kidDict.Get("T") is null)
                    widgets.Add(kidDict);

        var pageDict = _reader.ResolveDict(dict.Get("P"));
        // A missing /P on the field: try each widget's /P, then walk /Parent up.
        foreach (var w in widgets)
        {
            if (pageDict is not null) break;
            pageDict = _reader.ResolveDict(w.Get("P"));
        }
        if (pageDict is null)
        {
            var parent = _reader.ResolveDict(dict.Get("Parent"));
            while (parent is not null && pageDict is null)
            {
                pageDict = _reader.ResolveDict(parent.Get("P"));
                parent = _reader.ResolveDict(parent.Get("Parent"));
            }
        }
        var pages = new PageCollection(_reader);
        if (pageDict is not null)
        {
            for (var i = 1; i <= pages.Count; i++)
                if (ReferenceEquals(pages[i].Dict, pageDict)) return i;
        }
        for (var i = 1; i <= pages.Count; i++)
        {
            var annots = _reader.Resolve(pages[i].Dict.Get("Annots")) as Core.PdfArray;
            if (annots is null) continue;
            foreach (var item in annots)
            {
                var annotDict = _reader.ResolveDict(item);
                if (widgets.Any(w => ReferenceEquals(annotDict, w)))
                    return i;
            }
        }
        return -1;
    }

    /// <summary>The partial field name (/T).</summary>
    public string? PartialName
    {
        get => GetString("T");
        set
        {
            if (value is null)
                _dict.Remove("T");
            else
                _dict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(value)));
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

    private PdfStream? ResolveNormalAppearanceStream(PdfDictionary dict)
    {
        var ap = _reader.ResolveDict(dict.Get("AP"));
        if (ap is null) return null;
        var nObj = _reader.Resolve(ap.Get("N"));
        if (nObj is PdfStream direct) return direct;
        if (nObj is PdfDictionary stateDict)
        {
            // /AP/N is a state-keyed dict (e.g. /Yes /Off for checkboxes).
            // Pick the on-state stream — anything that isn't /Off — falling
            // back to the first stream we find.
            PdfStream? firstAny = null;
            foreach (var key in stateDict.Keys)
            {
                var resolved = _reader.ResolveStream(stateDict.Get(key));
                if (resolved is null) continue;
                firstAny ??= resolved;
                if (key != "Off") return resolved;
            }
            return firstAny;
        }
        return null;
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
            if (_dict.Get("AA") is not null
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

    /// <summary>Recompute every field listed in the AcroForm /CO (calculation
    /// order) whose /AA/C calculate action is a recognised built-in, persisting the
    /// result into each field's /V and appearance. Mirrors Acrobat's "calculate"
    /// event that fires whenever any field value changes. Re-entrancy guarded.</summary>
    private protected void TriggerRecalculation()
    {
        if (_recalculating) return;
        PdfDictionary? acroForm;
        try { acroForm = _reader.ResolveDict(_reader.Catalog?.Get("AcroForm")); }
        catch (System.InvalidOperationException) { return; }
        if (acroForm is null) return;
        if (_reader.Resolve(acroForm.Get("CO")) is not PdfArray co || co.Count == 0) return;

        void MarkFieldTreeDirty(PdfDictionary fieldDict)
        {
            if (OwnerDocument is null) return;
            var fn = OwnerDocument.FindObjectNumber(fieldDict);
            if (fn >= 0) OwnerDocument.MarkDirty(fn, fieldDict);
            if (_reader.Resolve(fieldDict.Get("Kids")) is PdfArray kids)
                foreach (var k in kids)
                    if (_reader.ResolveDict(k) is PdfDictionary kd)
                    {
                        var kn = k is PdfIndirectRef kr ? kr.ObjectNumber : OwnerDocument.FindObjectNumber(kd);
                        if (kn >= 0) OwnerDocument.MarkDirty(kn, kd);
                    }
        }

        _recalculating = true;
        try
        {
            foreach (var entry in co)
            {
                if (_reader.ResolveDict(entry) is not PdfDictionary fieldDict) continue;
                var computed = FieldCalculateScript.ComputeValue(fieldDict, _reader);
                if (computed is null) continue;
                new TextBoxField(fieldDict, _reader).ApplyCalculatedValue(computed);
                MarkFieldTreeDirty(fieldDict);
            }
        }
        finally { _recalculating = false; }
    }

    /// <summary>
    /// Set the field value. For text fields, sets /V as a PdfString.
    /// For checkboxes, sets /V and /AS as a PdfName.
    /// </summary>
    protected virtual void SetValue(string? value)
    {
        if (value is null)
        {
            // Clearing the value drops the /V key entirely — a field with no value
            // must not carry a /V (a null-valued /V still reports HasKey("V") == true).
            _dict.Remove("V");
        }
        else
        {
            _dict.Set("V", EncodePdfTextString(value));
        }
        // Mark dirty for incremental save
        if (OwnerDocument is not null && ObjectNumber >= 0)
            OwnerDocument.MarkDirty(ObjectNumber, _dict);
    }

    /// <summary>
    /// Encode a string as a PDF text string: Latin1 for ASCII-safe text,
    /// UTF-16BE with BOM for text containing non-Latin1 characters.
    /// </summary>
    internal static PdfString EncodePdfTextString(string value)
    {
        // Check if all characters fit in Latin1 (0x00–0xFF)
        bool needsUnicode = false;
        foreach (char c in value)
        {
            if (c > 0xFF) { needsUnicode = true; break; }
        }

        if (!needsUnicode)
            return new PdfString(System.Text.Encoding.Latin1.GetBytes(value));

        // UTF-16BE with BOM prefix (0xFE 0xFF)
        byte[] utf16 = System.Text.Encoding.BigEndianUnicode.GetBytes(value);
        byte[] withBom = new byte[utf16.Length + 2];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        utf16.CopyTo(withBom, 2);
        return new PdfString(withBom);
    }

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

    /// <summary>
    /// Internal: enumerate every kid in the /Kids array that resolves to a
    /// dictionary, in array order. Unlike <see cref="FieldKids"/>, this does
    /// not filter out pure widget annotations (kids without /T or /FT), so it
    /// surfaces the per-option widgets of a grouped checkbox/radio field.
    /// </summary>
    internal IEnumerable<PdfDictionary> AllKids()
    {
        var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
        if (kids is null) yield break;
        foreach (var kid in kids)
        {
            if (_reader.Resolve(kid) is PdfDictionary kidDict)
                yield return kidDict;
        }
    }

    /// <summary>
    /// Iterates widget annotations for this field's kids in the /Kids array.
    /// </summary>
    public IEnumerator<Aspose.Pdf.Annotations.WidgetAnnotation> GetEnumerator()
    {
        // A merged single-widget leaf that has also grown extra visual widgets (multi-
        // widget field via Form.AddFieldAppearance) keeps its own /Rect+/AP on the field
        // dict — yield that as the first widget so all visual widgets are enumerated.
        if (HasMergedSelfWidget)
            yield return new Aspose.Pdf.Annotations.WidgetAnnotation(_dict, _reader);

        // Walk every /Kids entry. A kid that is itself a field node (/T or /FT) is yielded
        // as the typed child field with its Parent wired back. An UNNAMED CONTAINER kid
        // (no /T, no /FT, but with /Kids of its own — an XFA #subform level) contributes
        // no name component and is flattened through: its named descendants surface as
        // this field's children, so caller recursion reaches every leaf. A pure widget
        // kid (no /T, no /Kids) is yielded as a plain WidgetAnnotation so callers can
        // read its per-widget appearance.
        foreach (var w in EnumerateKidsThrough(_dict, depth: 0))
            yield return w;
    }

    private IEnumerable<Aspose.Pdf.Annotations.WidgetAnnotation> EnumerateKidsThrough(
        PdfDictionary node, int depth)
    {
        if (depth > 10) yield break;
        var kids = _reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) yield break;
        foreach (var kid in kids)
        {
            if (_reader.Resolve(kid) is not PdfDictionary kidDict) continue;
            if (kidDict.ContainsKey("T") || kidDict.ContainsKey("FT"))
            {
                var child = Field.Create(kidDict, _reader);
                child.OwnerDocument = OwnerDocument;
                child.Parent = this;
                yield return child;
            }
            else if (_reader.Resolve(kidDict.Get("Kids")) is PdfArray)
            {
                foreach (var w in EnumerateKidsThrough(kidDict, depth + 1))
                    yield return w;
            }
            else
            {
                yield return new Aspose.Pdf.Annotations.WidgetAnnotation(kidDict, _reader);
            }
        }
    }

    /// <summary>True when this field is a merged single-widget leaf (its own /Rect)
    /// that has nonetheless grown extra widget kids — a multi-widget text field. The
    /// field dict itself is then the first visual widget alongside the /Kids widgets.</summary>
    internal bool HasMergedSelfWidget =>
        _dict.ContainsKey("Rect") &&
        _reader.Resolve(_dict.Get("Kids")) is PdfArray k && k.Count > 0;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Field-typed child kids of this field as a snapshot
    /// array — convenience accessor mirroring
    /// <see cref="Form.Fields"/> on the form. Each entry is a freshly
    /// materialised <see cref="Field"/> over the matching /Kids entry.</summary>
    public Field[] Fields => FieldKids().ToArray();

    /// <summary>
    /// Internal: enumerate the Field-typed child kids of this field. Skips pure
    /// widget annotations (kids without /T or /FT) and recreates each child via
    /// <see cref="Field.Create"/>.
    /// </summary>
    internal IEnumerable<Field> FieldKids()
    {
        var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
        if (kids is null) yield break;

        foreach (var kid in kids)
        {
            var kidDict = _reader.Resolve(kid) as PdfDictionary;
            if (kidDict is null) continue;
            if (!kidDict.ContainsKey("T") && !kidDict.ContainsKey("FT")) continue;

            var childField = Field.Create(kidDict, _reader);
            childField.OwnerDocument = OwnerDocument;
            yield return childField;
        }
    }

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
            int i = 1;
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
            foreach (var child in FieldKids())
            {
                if (string.Equals(child.PartialName, name, StringComparison.Ordinal))
                    return new Aspose.Pdf.Annotations.WidgetAnnotation(child.Dict, child.Reader);
            }
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

            // Walk the parent page's Annots array to find position
            var pageDict = FindPageDict();
            if (pageDict is not null)
            {
                var annotsObj = _reader.Resolve(pageDict.Get("Annots"));
                if (annotsObj is PdfArray annots)
                {
                    // Try by reference identity (1-based to match the public API contract).
                    for (int i = 0; i < annots.Count; i++)
                    {
                        var resolved = _reader.ResolveDict(annots[i]);
                        if (ReferenceEquals(resolved, _dict))
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

    private PdfDictionary? FindPageDict()
    {
        // The field's /P entry points to the page
        var pageRef = _reader.ResolveDict(_dict.Get("P"));
        if (pageRef is not null) return pageRef;

        // Walk parent chain looking for /P
        var parent = _reader.ResolveDict(_dict.Get("Parent"));
        while (parent is not null)
        {
            pageRef = _reader.ResolveDict(parent.Get("P"));
            if (pageRef is not null) return pageRef;
            parent = _reader.ResolveDict(parent.Get("Parent"));
        }

        return null;
    }

    /// <summary>Whether the field is read-only.</summary>
    public bool IsReadOnly => (FieldFlags & 1) != 0;

    /// <summary>Whether the field is read-only (settable alias for API parity).</summary>
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

    /// <summary>Drop the cached DefaultAppearance so the next read re-parses the
    /// (possibly rewritten) /DA string. Used by facades that edit /DA directly.</summary>
    internal void ResetDefaultAppearanceCache() => _defaultAppearance = null;
    /// <summary>
    /// Default appearance (font, size, colour) of the field — the field-level
    /// counterpart of <see cref="Aspose.Pdf.Annotations.WidgetAnnotation"/>.
    /// Lazily allocated; mutations on the returned instance are visible to
    /// subsequent reads through the same Field.
    /// </summary>
    public new Aspose.Pdf.Annotations.DefaultAppearance DefaultAppearance
    {
        get => _defaultAppearance ??= ParseDefaultAppearance();
        set
        {
            _defaultAppearance = value;
            // Persist the /DA string immediately so an appearance regenerated
            // before the field is attached to a form (e.g. setting Value first)
            // already reflects this font/size/colour.
            if (value is not null)
                Dict.Set("DA", new PdfString(
                    System.Text.Encoding.Latin1.GetBytes(value.ToAppearanceString())));
            // When the field is already part of a form, embed an embeddable font
            // immediately (the font may be set after Form.Add); this re-points /DA
            // at the embedded resource. A no-op otherwise.
            Form.EmbedDefaultAppearanceFont(this);
            // Drop any appearance generated with the previous /DA (e.g. built with
            // the default /Helv during Form.Add) and rebuild it with the new
            // font/size/colour.
            Dict.Remove("AP");
            GenerateAppearance();
        }
    }

    /// <summary>Build the DefaultAppearance from the field's stored /DA string
    /// (inherited through /Parent up to the AcroForm default). The FontName is the
    /// /DA RESOURCE name (e.g. "TiBI", "Helv") — callers resolve it against
    /// <see cref="Form.DefaultResources"/>. Falls back to Helvetica 12 when no /DA
    /// parses.</summary>
    private Aspose.Pdf.Annotations.DefaultAppearance ParseDefaultAppearance()
    {
        var da = FindInheritedDaString(_dict, 0);
        if (da is not null)
        {
            var m = System.Text.RegularExpressions.Regex.Match(da,
                @"/(\S+)\s+([\d.]+)\s+Tf");
            if (m.Success && double.TryParse(m.Groups[2].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var size))
            {
                var color = System.Drawing.Color.Black;
                var cm = System.Text.RegularExpressions.Regex.Match(da,
                    @"([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+rg");
                if (cm.Success)
                    color = System.Drawing.Color.FromArgb(
                        (int)(double.Parse(cm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 255),
                        (int)(double.Parse(cm.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * 255),
                        (int)(double.Parse(cm.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) * 255));
                // A "0 Tf" size means auto-size; keep the raw 0.
                return new Aspose.Pdf.Annotations.DefaultAppearance(m.Groups[1].Value, size, color);
            }
        }
        return new Aspose.Pdf.Annotations.DefaultAppearance();
    }

    private string? FindInheritedDaString(PdfDictionary dict, int depth)
    {
        if (depth > 8) return null;
        if (_reader.Resolve(dict.Get("DA")) is PdfString ps)
            return System.Text.Encoding.Latin1.GetString(ps.Value);
        if (_reader.ResolveDict(dict.Get("Parent")) is { } parent)
            return FindInheritedDaString(parent, depth + 1);
        return null;
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

    /// <summary>Bounding box (union) of all descendant widget /Rects (depth-limited), or null.</summary>
    private Rectangle? UnionKidRect(PdfDictionary dict, int depth)
    {
        if (depth > 2) return null;
        var kids = _reader.Resolve(dict.Get("Kids")) as PdfArray;
        if (kids is null) return null;
        Rectangle? acc = null;
        foreach (var k in kids)
        {
            var kid = _reader.ResolveDict(k);
            if (kid is null) continue;
            Rectangle? r = _reader.Resolve(kid.Get("Rect")) is PdfArray { Count: >= 4 } ra
                ? Rectangle.FromPdfArray(ra, _reader)
                : UnionKidRect(kid, depth + 1);
            if (r is null) continue;
            acc = acc is null
                ? r
                : new Rectangle(
                    System.Math.Min(acc.LLX, r.LLX), System.Math.Min(acc.LLY, r.LLY),
                    System.Math.Max(acc.URX, r.URX), System.Math.Max(acc.URY, r.URY));
        }
        return acc;
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
                if ((parts[i] == "rg" || parts[i] == "RG") && i >= 3)
                {
                    if (double.TryParse(parts[i - 3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) &&
                        double.TryParse(parts[i - 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g) &&
                        double.TryParse(parts[i - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b))
                        return Color.FromRgb(r, g, b);
                }
            }
            return Color.Black;
        }
        set
        {
            // Update /DA string with new color, preserving font info
            var da = GetString("DA") ?? "";
            var r = (value.R / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var g = (value.G / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var b = (value.B / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            // Remove existing color operators from DA
            var parts = da.Split(' ').ToList();
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if ((parts[i] == "rg" || parts[i] == "RG" || parts[i] == "g" || parts[i] == "G") && i >= 1)
                {
                    int numArgs = parts[i] == "rg" || parts[i] == "RG" ? 3 : 1;
                    int start = Math.Max(0, i - numArgs);
                    parts.RemoveRange(start, i - start + 1);
                    i = start;
                }
            }
            var newDa = string.Join(" ", parts).Trim();
            if (newDa.Length > 0) newDa += " ";
            newDa += $"{r} {g} {b} rg";
            _dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(newDa)));
        }
    }

    internal new PdfDictionary Dict => _dict;
    internal PdfReader Reader => _reader;

    /// <summary>Set the partial name (/T entry) of this field.</summary>
    internal void SetPartialName(string name)
    {
        _dict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
    }

    private FieldType DetermineType()
    {
        var ft = GetInheritedName("FT");
        var flags = (int)GetInheritedInt("Ff");

        // NB: Field now inherits Annotation.FieldType (a string /FT accessor), which
        // shadows the FieldType enum in expression position — so qualify the enum.
        return ft switch
        {
            "Tx" => Aspose.Pdf.Forms.FieldType.Text,
            "Btn" => (flags & (1 << 16)) != 0 ? Aspose.Pdf.Forms.FieldType.Button      // bit 17: Pushbutton
                   : (flags & (1 << 15)) != 0 ? Aspose.Pdf.Forms.FieldType.RadioButton // bit 16: Radio
                   : Aspose.Pdf.Forms.FieldType.CheckBox,
            // The /Ff combo bit (bit 18, 0-based 17) splits Ch into combo / list.
            // The form-JSON round-trip and facade callers rely on this so a re-imported
            // combo rebuilds as ComboBoxField, not ListBoxField.
            "Ch" => (flags & (1 << 17)) != 0 ? Aspose.Pdf.Forms.FieldType.ComboBox
                                              : Aspose.Pdf.Forms.FieldType.ListBox,
            "Sig" => Aspose.Pdf.Forms.FieldType.Signature,
            _ => Aspose.Pdf.Forms.FieldType.Unknown,
        };
    }

    private string? GetInheritedName(string key)
    {
        var name = _dict.GetName(key);
        if (name is not null) return name;

        var parent = _reader.ResolveDict(_dict.Get("Parent"));
        while (parent is not null)
        {
            name = parent.GetName(key);
            if (name is not null) return name;
            parent = _reader.ResolveDict(parent.Get("Parent"));
        }
        return null;
    }

    private long GetInheritedInt(string key)
    {
        if (_dict.ContainsKey(key)) return _dict.GetInt(key);

        var parent = _reader.ResolveDict(_dict.Get("Parent"));
        while (parent is not null)
        {
            if (parent.ContainsKey(key)) return parent.GetInt(key);
            parent = _reader.ResolveDict(parent.Get("Parent"));
        }
        return 0;
    }

    private string? BuildFullName()
    {
        var parts = new List<string>();
        var current = _dict;
        while (current is not null)
        {
            var t = current.Get("T");
            if (t is PdfString s)
                parts.Add(s.ToText());
            else if (t is PdfName n)
                parts.Add(n.Value);
            current = _reader.ResolveDict(current.Get("Parent"));
        }
        parts.Reverse();
        return parts.Count > 0 ? string.Join(".", parts) : null;
    }

    private new string? GetString(string key)
    {
        var obj = _reader.Resolve(_dict.Get(key));
        return obj switch
        {
            PdfString s => s.ToText(),
            PdfName n => n.Value,
            _ => null,
        };
    }

    /// <summary>Build a PdfArray for a rectangle.</summary>
    internal static PdfArray MakeRectArray(Rectangle rect)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        return arr;
    }

    internal static Field Create(PdfDictionary dict, PdfReader reader)
    {
        var ft = dict.GetName("FT");
        // Check parent for inherited FT
        if (ft is null)
        {
            var parent = reader.ResolveDict(dict.Get("Parent"));
            while (parent is not null && ft is null)
            {
                ft = parent.GetName("FT");
                parent = reader.ResolveDict(parent.Get("Parent"));
            }
        }

        return ft switch
        {
            "Tx" => CreateTextField(dict, reader),
            "Btn" => CreateButtonField(dict, reader),
            "Ch" => CreateChoiceField(dict, reader),
            "Sig" => new SignatureField(dict, reader),
            _ => new Field(dict, reader),
        };
    }

    private static TextBoxField CreateTextField(PdfDictionary dict, PdfReader reader)
    {
        if (BarcodeField.IsBarcode(dict, reader)) return new BarcodeField(dict, reader);
        var flags = (int)GetInheritedInt(dict, reader, "Ff");
        return (flags & (1 << 25)) != 0
            ? new RichTextBoxField(dict, reader)
            : new TextBoxField(dict, reader);
    }

    private static Field CreateButtonField(PdfDictionary dict, PdfReader reader)
    {
        var flags = (int)GetInheritedInt(dict, reader, "Ff");
        if ((flags & (1 << 16)) != 0) return new ButtonField(dict, reader); // bit 17: Pushbutton
        if ((flags & (1 << 15)) != 0) return new RadioButtonField(dict, reader); // bit 16: Radio
        return new CheckboxField(dict, reader);
    }

    private static ChoiceField CreateChoiceField(PdfDictionary dict, PdfReader reader)
    {
        var flags = (int)GetInheritedInt(dict, reader, "Ff");
        if ((flags & (1 << 17)) != 0) return new ComboBoxField(dict, reader); // bit 18: Combo
        return new ListBoxField(dict, reader);
    }

    private static long GetInheritedInt(PdfDictionary dict, PdfReader reader, string key)
    {
        if (dict.ContainsKey(key)) return dict.GetInt(key);
        var parent = reader.ResolveDict(dict.Get("Parent"));
        while (parent is not null)
        {
            if (parent.ContainsKey(key)) return parent.GetInt(key);
            parent = reader.ResolveDict(parent.Get("Parent"));
        }
        return 0;
    }

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
                foreach (var cand in candidates)
                    if (ReferenceEquals(ad, cand)) return (annots, i + 1);
            }
        }
        return (null, -1);
    }

    /// <summary>Global flag: when true, a field's value is auto-resized to fit its
    /// widget rectangle. Static to match the public surface (a process-wide default).
    /// Stored only.</summary>
    public static bool FitIntoRectangle { get; set; }

    /// <summary>Whether this field is a container/group of child fields.</summary>
    public bool IsGroup => Count > 0;

    private List<Field> GetKids()
    {
        var list = new List<Field>();
        foreach (var k in FieldKids()) list.Add(k);
        return list;
    }

    /// <summary>Whether this field belongs to a shared-fields group. Stored only.</summary>
    public bool IsSharedField { get; set; }

    /// <summary>Whether the collection is thread-safe. Always false.</summary>
    public bool IsSynchronized => false;

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
            else Dict.Set("TM", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Copy widget annotations into an array starting at <paramref name="index"/>.</summary>
    public void CopyTo(Aspose.Pdf.Annotations.WidgetAnnotation[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        if (OwnerDocument is null) return;
        var kids = GetKids();
        for (var i = 0; i < kids.Count; i++)
            array[index + i] = new Aspose.Pdf.Annotations.WidgetAnnotation(OwnerDocument);
    }

    /// <summary>Copy this field's kids (as Field instances) into an array starting at <paramref name="index"/>.</summary>
    public void CopyTo(Field[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        var kids = GetKids();
        for (var i = 0; i < kids.Count; i++)
            array[index + i] = kids[i];
    }

    /// <summary>Execute the supplied JavaScript action against this field. Stored intent only — the JS interpreter is not invoked.</summary>
    public void ExecuteFieldJavaScript(Aspose.Pdf.Annotations.JavascriptAction javaScriptAction)
    {
        _ = javaScriptAction;
    }

    /// <summary>Serialise this field to JSON (a single <see cref="FieldExportingData"/>
    /// object) via the supplied stream.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(Stream stream)
        => ExportToJson(stream, null);

    /// <summary>Serialise this field to a JSON file.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(string fileName)
        => ExportToJson(fileName, null);

    /// <summary>Serialise this field to JSON via the supplied stream.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(Stream stream, ExportFieldsToJsonOptions? options)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var data = FieldJsonExporter.BuildField(this);
        FieldJsonExporter.Write(stream, data, options?.WriteIndented ?? false);
        return new[]
        {
            new FieldSerializationResult
            {
                FieldFullName = FullName ?? PartialName ?? string.Empty,
                FieldSerializationStatus = FieldSerializationStatus.Success,
            },
        };
    }

    /// <summary>Serialise this field to a JSON file.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(string fileName, ExportFieldsToJsonOptions? options)
    {
        using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        return ExportToJson(fs, options);
    }

    /// <summary>Write the field's name, flags and value to a JSON stream. A
    /// dotted field name (an XFA path such as <c>form1[0].P1[0].Field[0]</c>)
    /// is emitted as nested <c>{ "Name", "ChildFields" }</c> objects, one per
    /// path segment, with the leaf carrying <c>Flags</c> and <c>Value</c>.</summary>
    public void ExportValueToJson(Stream outputJsonStream, bool indented)
    {
        if (outputJsonStream is null) throw new ArgumentNullException(nameof(outputJsonStream));
        var fullName = FullName ?? PartialName ?? string.Empty;
        var form = OwnerDocument?.Form;
        var value = (form is { IsXfa: true } ? form.GetXfaFieldValue(fullName) : Value) ?? Value ?? string.Empty;

        var segments = fullName.Length == 0 ? new[] { string.Empty } : fullName.Split('.');
        var node = new FieldValueNode { Name = segments[segments.Length - 1], Flags = (int)Flags, Value = value };
        for (var i = segments.Length - 2; i >= 0; i--)
            node = new FieldValueNode { Name = segments[i], ChildFields = new List<FieldValueNode> { node } };

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(node, options);
        outputJsonStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Nested field node used when (de)serialising a field-value path
    /// to/from JSON. Property order matters: <c>Name, Flags, Value, ChildFields</c>.</summary>
    private sealed class FieldValueNode
    {
        public string? Name { get; set; }
        public int? Flags { get; set; }
        public string? Value { get; set; }
        public List<FieldValueNode>? ChildFields { get; set; }
    }

    private static string JsonEscape(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t");

    /// <summary>Flatten this field — turn its value into static page content. Currently no-op; per-field flattening is not implemented.</summary>
    public new void Flatten()
    {
        // Flatten just this field: fold its widget appearance(s) into the owning page content and
        // remove it from the AcroForm. Delegates to the form so the placement (§12.5.5) and FRM
        // registration match the document/form flatten path.
        OwnerDocument?.Form.FlattenField(this);
    }

    /// <summary>Enumerator typed to <see cref="Aspose.Pdf.Annotations.WidgetAnnotation"/>.</summary>
    public IEnumerator<Aspose.Pdf.Annotations.WidgetAnnotation> GetWidgetEnumerator()
    {
        if (OwnerDocument is null) yield break;
        var kids = GetKids();
        for (var i = 0; i < kids.Count; i++)
            yield return new Aspose.Pdf.Annotations.WidgetAnnotation(OwnerDocument);
    }

    /// <summary>Read this field's value from a JSON stream. Returns true on success.</summary>
    public bool ImportValueFromJson(Stream inputJsonStream)
        => ImportValueFromJson(inputJsonStream, FullName ?? PartialName ?? string.Empty);

    /// <summary>Read a named field's value from a JSON stream — a single field
    /// object (possibly nested via <c>ChildFields</c>) or an array of them — and
    /// apply the value of the entry whose dotted path matches
    /// <paramref name="fieldFullNameInJSON"/>. For an XFA-backed form the value
    /// is written into the XFA datasets as well as the AcroForm field. Returns
    /// true on success.</summary>
    public bool ImportValueFromJson(Stream inputJsonStream, string fieldFullNameInJSON)
    {
        if (inputJsonStream is null) return false;
        try
        {
            using var reader = new StreamReader(inputJsonStream, System.Text.Encoding.UTF8, leaveOpen: true);
            using var doc = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
            var root = doc.RootElement;
            string? value = null;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                    if (TryFindValueByPath(element, string.Empty, fieldFullNameInJSON, out value))
                        break;
            }
            else
            {
                TryFindValueByPath(root, string.Empty, fieldFullNameInJSON, out value);
            }
            if (value is null) return false;
            ApplyImportedValue(value);
            return true;
        }
        catch
        {
            // parse failure → false
        }
        return false;
    }

    /// <summary>Search a field node (recursing through <c>ChildFields</c>) for a
    /// leaf whose accumulated dotted path equals <paramref name="target"/>.</summary>
    private static bool TryFindValueByPath(
        System.Text.Json.JsonElement element, string prefix, string target, out string? value)
    {
        value = null;
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
        var name = element.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var full = prefix.Length == 0 ? name : prefix + "." + name;
        // Children are nested under "ChildFields" (field-level export) or "Fields"
        // (form-level export); recurse whichever is present.
        if ((element.TryGetProperty("ChildFields", out var kids) ||
             element.TryGetProperty("Fields", out kids)) &&
            kids.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var kid in kids.EnumerateArray())
                if (TryFindValueByPath(kid, full, target, out value))
                    return true;
            return false;
        }
        if (full == target &&
            element.TryGetProperty("Value", out var v) &&
            v.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            value = v.GetString();
            return true;
        }
        return false;
    }

    private void ApplyImportedValue(string value)
    {
        Value = value;
        var form = OwnerDocument?.Form;
        if (form is { IsXfa: true })
            form.SetXfaFieldValue(FullName ?? string.Empty, value);
    }

    /// <summary>Recalculate the field's value from its calculation script. Currently no-op; returns false.</summary>
    public bool Recalculate() => false;

    /// <summary>
    /// Generate a default <c>/AP /N</c> appearance stream for this field's widget
    /// so it renders without relying on a viewer's NeedAppearances pass. Overridden
    /// per field type; the base implementation is a no-op (fields with no visible
    /// representation, or types that build their appearance elsewhere).
    /// </summary>
    internal virtual void GenerateAppearance() { }

    /// <summary>Build an /AP dictionary (with /N) rendering this field's current value
    /// for an extra widget of the given size — used by multi-widget construction
    /// (<see cref="Form.AddFieldAppearance"/>). The base implementation returns null
    /// (no value appearance); value-bearing field types override it.</summary>
    internal virtual PdfDictionary? BuildWidgetApDict(double w, double h) => null;

    /// <summary>Format a coordinate for a content stream, invariant culture.</summary>
    private protected static string FmtNum(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Escape a string for use as a PDF literal-string operand.</summary>
    private protected static string EscapePdfText(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>Resolve the widget rectangle's width/height. Returns false when
    /// there is no usable (positive-area) rectangle.</summary>
    private protected bool TryWidgetSize(out double w, out double h)
    {
        w = h = 0;
        if (Reader.Resolve(Dict.Get("Rect")) is not PdfArray rectArr || rectArr.Count < 4)
            return false;
        var r = Rectangle.FromPdfArray(rectArr);
        w = r.Width; h = r.Height;
        return w > 0 && h > 0;
    }

    /// <summary>Parse the field's <c>/DA</c> default-appearance string for a font
    /// resource name and size, defaulting to Helvetica 12. A size of 0 in /DA
    /// means auto-size — the caller decides what size to substitute.</summary>
    private protected void ParseDefaultAppearance(out string fontName, out double fontSize)
        => ParseDefaultAppearance(out fontName, out fontSize, defaultSize: 12);

    private protected void ParseDefaultAppearance(out string fontName, out double fontSize, double defaultSize)
    {
        fontName = "Helv";
        fontSize = defaultSize;
        if (Reader.Resolve(Dict.Get("DA")) is not PdfString daStr) return;
        var parts = daStr.ToText().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] != "Tf" || i < 2) continue;
            fontName = parts[i - 2].TrimStart('/');
            double.TryParse(parts[i - 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s);
            if (s > 0) fontSize = s;
        }
    }

    /// <summary>Extract the fill-colour operator (g/rg/k) from a /DA string,
    /// or <paramref name="fallback"/> when none is present.</summary>
    private protected static string ExtractDaColor(string da, string fallback = "0 g")
    {
        if (string.IsNullOrEmpty(da)) return fallback;
        var p = da.Split(new[] { ' ', '\n', '\t', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i] == "g" && i >= 1) return $"{p[i - 1]} g";
            if (p[i] == "rg" && i >= 3) return $"{p[i - 3]} {p[i - 2]} {p[i - 1]} rg";
            if (p[i] == "k" && i >= 4) return $"{p[i - 4]} {p[i - 3]} {p[i - 2]} {p[i - 1]} k";
        }
        return fallback;
    }

    /// <summary>Wrap a content string in a Form XObject appearance stream sized
    /// to the supplied bounding box.</summary>
    private protected static PdfStream MakeApXObject(string content, double w, double h,
        PdfDictionary? resources = null)
    {
        var stream = new PdfStream(new PdfDictionary(), System.Text.Encoding.Latin1.GetBytes(content));
        stream.Dict.Set("Type", new PdfName("XObject"));
        stream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        stream.Dict.Set("BBox", bbox);
        stream.Dict.Set("Resources", (PdfObject?)resources ?? new PdfDictionary());
        return stream;
    }

    /// <summary>Build a <c>/Font</c> resource dictionary mapping the given resource
    /// name to a Standard-14 base font, so an appearance stream that references it
    /// renders text (the renderer resolves a font only from declared resources).
    /// The /DR aliases written by Acrobat ("Helv" / "HeBo" / "ZaDb" / "TiRo" / ...)
    /// are translated to their PostScript base-font names; an unknown alias falls
    /// back to Helvetica.</summary>
    private protected static PdfDictionary MakeStandardFontResources(string fontName)
    {
        var alias = string.IsNullOrEmpty(fontName) ? "Helv" : fontName;
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(StandardBaseFont(alias)));
        var fonts = new PdfDictionary();
        fonts.Set(alias, font);
        var res = new PdfDictionary();
        res.Set("Font", fonts);
        return res;
    }

    /// <summary>The Acrobat /DR font aliases that name a Standard-14 face.</summary>
    private static readonly HashSet<string> StandardFontAliases = new(StringComparer.Ordinal)
    {
        "Helv", "HeBo", "HeOb", "HeBO", "TiRo", "TiBo", "TiIt", "TiBI",
        "Cour", "CoBo", "CoOb", "CoBO", "ZaDb", "Symb",
    };

    /// <summary>Build the <c>/Resources</c> dictionary for a regenerated text-field
    /// appearance so the <c>/{fontName} Tf</c> operator in the content stream
    /// resolves. Sibling fonts and non-font resources from
    /// <paramref name="existingRes"/> are carried over unchanged; when the
    /// <c>/DA</c> font name isn't already declared there, a Standard-14 entry under
    /// that name is synthesised. The AcroForm <c>/DR</c> face is deliberately not
    /// pulled in: a /DR font is often an embedded subset that only carries the
    /// field's original glyphs, so rendering a freshly-set value through it would
    /// drop characters the subset never included.</summary>
    private protected PdfDictionary BuildTextAppearanceResources(string fontName, PdfDictionary? existingRes,
        PdfDictionary? compositeFont = null)
    {
        var existingFonts = existingRes is null ? null : Reader.ResolveDict(existingRes.Get("Font"));

        var fonts = new PdfDictionary();
        if (existingFonts is not null)
            foreach (var key in existingFonts.Keys)
                fonts.Set(key, existingFonts.Get(key)!);

        if (compositeFont is not null)
            fonts.Set(fontName, compositeFont);
        else if (!fonts.ContainsKey(fontName))
            fonts.Set(fontName, MakeAppearanceFont(fontName));

        var res = new PdfDictionary();
        res.Set("Font", fonts);
        if (existingRes is not null)
            foreach (var key in existingRes.Keys)
                if (key != "Font")
                    res.Set(key, existingRes.Get(key)!);
        return res;
    }

    /// <summary>Synthesise a <c>/Font</c> dictionary for an appearance resource.
    /// A known Acrobat alias (Helv, TiRo, ...) maps to its Standard-14 PostScript
    /// base font. Any other name that resolves to an installed face is kept verbatim
    /// as the <c>/BaseFont</c> so a <c>/DA</c> naming a real font (e.g. "Arial")
    /// round-trips that name; a name that resolves to nothing — typically a bare
    /// subset tag whose <c>/DA</c> font isn't a usable family — falls back to
    /// Helvetica so the value still renders instead of vanishing.</summary>
    private static PdfDictionary MakeAppearanceFont(string fontName)
    {
        string baseFont;
        if (StandardFontAliases.Contains(fontName))
            baseFont = StandardBaseFont(fontName);
        else if (Aspose.Pdf.Text.SystemFontResolver.Resolve(fontName) is not null)
            baseFont = fontName;
        else
            baseFont = "Helvetica";
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFont));
        // Tag the encoding so the WinAnsi-encoded appearance text (see the appearance
        // content build) maps its 0x80-0x9F bytes to the right glyphs on both render and
        // text-extraction, rather than the font's default StandardEncoding.
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        return font;
    }

    /// <summary>Resolve a /DA font name against the AcroForm /DR /Font dictionary.
    /// Referencing the actual /DR font (instead of synthesising a Type1 stand-in)
    /// keeps embedded composite faces working in regenerated appearances.</summary>
    private protected PdfDictionary? ResolveDrFontDict(string fontName)
    {
        try
        {
            var acro = Reader.ResolveDict(Reader.Catalog.Get("AcroForm"));
            var dr = acro is null ? null : Reader.ResolveDict(acro.Get("DR"));
            var fontDict = dr is null ? null : Reader.ResolveDict(dr.Get("Font"));
            return fontDict is null ? null : Reader.ResolveDict(fontDict.Get(fontName));
        }
        catch { return null; }
    }

    /// <summary>For a composite (/Type0, Identity-H, CIDToGIDMap Identity) /DA font,
    /// load the embedded face's char→glyph cmap so a regenerated appearance can encode
    /// the value as 2-byte glyph ids. Null when the font isn't composite/embedded.</summary>
    private protected System.Collections.Generic.Dictionary<int, int>? LoadCompositeCmap(PdfDictionary? type0)
        => LoadCompositeParser(type0)?.CMap;

    private protected Aspose.Pdf.Text.TrueTypeParser? LoadCompositeParser(PdfDictionary? type0)
    {
        try
        {
            if (type0?.GetName("Subtype") != "Type0") return null;
            var desc = Reader.Resolve(type0.Get("DescendantFonts")) as PdfArray;
            var cid = desc is { Count: > 0 } ? Reader.ResolveDict(desc[0]) : null;
            var fd = cid is null ? null : Reader.ResolveDict(cid.Get("FontDescriptor"));
            var ff = fd is null ? null : Reader.ResolveStream(fd.Get("FontFile2"));
            if (ff is null) return null;
            var ttf = Reader.DecodeStream(ff);
            var parser = new Aspose.Pdf.Text.TrueTypeParser(ttf);
            parser.Parse();
            return parser;
        }
        catch { return null; }
    }

    /// <summary>The /DR font adapted for use in a regenerated value appearance. A
    /// composite face is referenced through a shallow clone whose /BaseFont is the
    /// FAMILY name ("BitstreamCyberCJK") — the fill-time subset embeds
    /// under the family name, while the /DR default keeps the PostScript name.</summary>
    private protected PdfDictionary? AppearanceFontFromDr(string fontName)
    {
        var dr = ResolveDrFontDict(fontName);
        // Simple fonts keep the synthesized WinAnsi Type1 stand-in (the appearance
        // text is Cp1252-encoded); only a composite face must be carried verbatim.
        if (dr is null || dr.GetName("Subtype") != "Type0") return null;
        var family = LoadCompositeParser(dr)?.FamilyName;
        if (string.IsNullOrEmpty(family) || family == "Unknown") return dr;
        var clone = new PdfDictionary();
        foreach (var k in dr.Keys) clone.Set(k, dr.Get(k)!);
        clone.Set("BaseFont", new PdfName(family!.Replace(" ", "")));
        return clone;
    }

    /// <summary>Map an Acrobat /DR alias to its Standard-14 PostScript base-font name.</summary>
    private protected static string StandardBaseFont(string alias) => alias switch
    {
        "Helv" => "Helvetica",
        "HeBo" => "Helvetica-Bold",
        "HeOb" => "Helvetica-Oblique",
        "HeBO" => "Helvetica-BoldOblique",
        "TiRo" => "Times-Roman",
        "TiBo" => "Times-Bold",
        "TiIt" => "Times-Italic",
        "TiBI" => "Times-BoldItalic",
        "Cour" => "Courier",
        "CoBo" => "Courier-Bold",
        "CoOb" => "Courier-Oblique",
        "CoBO" => "Courier-BoldOblique",
        "ZaDb" => "ZapfDingbats",
        "Symb" => "Symbol",
        _ => "Helvetica",
    };

    /// <summary>Coerce a resolved numeric PDF object (integer or real) to a double.</summary>
    private protected static double? AsNumber(PdfObject? obj) => obj switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => null,
    };

    /// <summary>Emit background-fill and/or border-stroke operators from the
    /// widget's <c>/MK</c> appearance-characteristics dictionary (<c>/BG</c>,
    /// <c>/BC</c>). Empty when neither colour is present.</summary>
    private protected string BuildMkBackgroundAndBorder(double w, double h)
    {
        var mk = Reader.ResolveDict(Dict.Get("MK"));
        if (mk is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        if (MkColorOperator(mk.Get("BG"), fill: true) is { } bg)
            sb.Append($"{bg} 0 0 {FmtNum(w)} {FmtNum(h)} re f ");
        if (MkColorOperator(mk.Get("BC"), fill: false) is { } bc)
            sb.Append($"{bc} 1 w 0.5 0.5 {FmtNum(w - 1)} {FmtNum(h - 1)} re S ");
        return sb.ToString();
    }

    private protected string? MkColorOperator(PdfObject? entry, bool fill)
    {
        if (Reader.Resolve(entry) is not PdfArray arr) return null;
        var vals = new System.Collections.Generic.List<double>();
        foreach (var it in arr)
            if (AsNumber(Reader.Resolve(it)) is { } d) vals.Add(d);
        if (vals.Count == 0) return null; // empty array = transparent / no paint
        var nums = string.Join(" ", vals.ConvertAll(FmtNum));
        return vals.Count switch
        {
            1 => $"{nums} {(fill ? "g" : "G")}",
            3 => $"{nums} {(fill ? "rg" : "RG")}",
            4 => $"{nums} {(fill ? "k" : "K")}",
            _ => null,
        };
    }

    /// <summary>Move the field's first widget rectangle to the supplied point (top-left corner).</summary>
    public void SetPosition(Aspose.Pdf.Point point)
    {
        if (point is null) return;
        var rect = Rect;
        if (rect is null) return;
        var width = rect.URX - rect.LLX;
        var height = rect.URY - rect.LLY;
        var newRect = new PdfArray();
        newRect.Add(new PdfReal(point.X));
        newRect.Add(new PdfReal(point.Y - height));
        newRect.Add(new PdfReal(point.X + width));
        newRect.Add(new PdfReal(point.Y));
        Dict.Set("Rect", newRect);
    }
}
