using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Forms;

public sealed partial class Form : ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    private static string Format(double v) =>
        v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    public IEnumerator<Aspose.Pdf.Annotations.WidgetAnnotation> GetEnumerator()
    {
        // Yield the direct /AcroForm/Fields entries — the top level of the field tree.
        // Callers descend via each Field's own enumerator/Count (foreach (Field f in
        // doc.Form) with a recursive fill/print helper walks group nodes down to every
        // leaf — a recursive fill still reaches nested XFA leaves that way), so
        // groups must NOT be pre-flattened here: a hierarchical form yields its named
        // roots exactly once, where the flattened yield made every terminal surface
        // once per named ancestor on top of its own turn (a deep XFA hierarchy
        // multiply-counted that way — 457 surfaced for 130 leaves).
        //
        // Skip only the un-named entries: a radio/checkbox group's kid option-widgets have no
        // /T (PartialName == null) and must not surface as separate widgets — yielding them
        // hands callers a field whose PartialName is null, throwing NRE in name-matching
        // loops. The public Field[] Fields property is unchanged (flattened
        // terminals).
        if (_reader is not null && _acroForm is not null
            && _reader.Resolve(_acroForm.Get("Fields")) is PdfArray topArray && topArray.Count > 0)
        {
            // Reuse the tracked Field instances (they carry unsaved in-memory state);
            // materialise a fresh Field only for top-level dicts _fields never surfaced
            // directly (e.g. an expanded radio group node or an unnamed-container root
            // that was flattened through at collect time).
            var byDict = new Dictionary<PdfDictionary, Field>();
            foreach (var f in _fields)
                if (!byDict.ContainsKey(f.Dict)) byDict[f.Dict] = f;
            foreach (var item in topArray)
            {
                if (_reader.ResolveDict(item) is not PdfDictionary dict) continue;
                if (!byDict.TryGetValue(dict, out var field))
                {
                    field = Field.Create(dict, _reader);
                    field.OwnerDocument = OwnerDocument;
                }
                if (field.PartialName is not null)
                    yield return Widgetize(field);
            }
            yield break;
        }
        // No backing /Fields array (programmatic or widget-reconstructed form): the
        // tracked list is flat, so yielding it IS the top level.
        foreach (var f in _fields)
            if (f.PartialName is not null)
                yield return Widgetize(f);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ICollection<WidgetAnnotation> — a read-only view over the form's fields (mutation goes
    // through Add(Field)/Remove/Flatten, not this interface). Lets callers pass the form where
    // an ICollection<WidgetAnnotation> is expected (e.g. a bulk field-fill helper).
    bool ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.IsReadOnly => true;

    void ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.Add(Aspose.Pdf.Annotations.WidgetAnnotation item)
        => throw new NotSupportedException("Add fields via Form.Add(Field).");

    void ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.Clear()
        => throw new NotSupportedException("Clear the form via Flatten or field removal.");

    bool ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.Contains(Aspose.Pdf.Annotations.WidgetAnnotation item)
    {
        foreach (var w in this) if (ReferenceEquals(w, item)) return true;
        return false;
    }

    void ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.CopyTo(Aspose.Pdf.Annotations.WidgetAnnotation[] array, int arrayIndex)
    {
        foreach (var w in this) array[arrayIndex++] = w;
    }

    bool ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>.Remove(Aspose.Pdf.Annotations.WidgetAnnotation item)
        => throw new NotSupportedException("Remove fields via Flatten or field removal.");

    private static int GetObjectNumber(PdfObject item)
    {
        return item is PdfIndirectRef iref ? iref.ObjectNumber : -1;
    }

    private static Field CreateFieldWithObjNum(PdfDictionary dict, PdfReader reader, int objNum)
    {
        var field = Field.Create(dict, reader);
        field.ObjectNumber = objNum;
        return field;
    }

    private static void CollectFields(PdfArray fieldsArray, PdfReader reader, List<Field> result, HashSet<PdfDictionary> expandedGroups, int depth = 0)
    {
        foreach (var item in fieldsArray)
        {
            var dict = reader.ResolveDict(item);
            if (dict is null) continue;
            var objNum = GetObjectNumber(item);

            // If this node has /Kids that are NOT widget annotations, recurse
            var kids = reader.Resolve(dict.Get("Kids")) as PdfArray;
            if (kids is not null && !HasWidgetKids(kids, reader))
            {
                CollectFields(kids, reader, result, expandedGroups, depth + 1);
            }
            else if (kids is not null && !dict.ContainsKey("Rect"))
            {
                // Field has widget kids but no /Rect of its own.
                var ft = dict.GetName("FT");
                if (ft is null)
                {
                    var p = reader.ResolveDict(dict.Get("Parent"));
                    while (p is not null && ft is null) { ft = p.GetName("FT"); p = reader.ResolveDict(p.Get("Parent")); }
                }

                // Container node with no field type (e.g. XFA subform node):
                // expand each kid as an individual field so typed widgets are collected.
                if (ft is null)
                {
                    // Container node with no field type — always recurse so that
                    // deeply nested field hierarchies are fully traversed.
                    CollectFields(kids, reader, result, expandedGroups, depth + 1);
                    continue;
                }

                // Radio button groups (Btn + Radio flag) with proper /Parent back-refs on kids
                // are expanded to individual widgets so callers can access per-option rects.
                // All other cases (Text, Choice, Sig, or FormFieldBuilder radio without /Parent)
                // return the parent field, which carries /T, /V, and type info.
                var ff = (long)(dict.ContainsKey("Ff") ? dict.GetInt("Ff") : 0);
                bool isRadioGroup = ft == "Btn" && (ff & (1 << 15)) != 0 && (ff & (1 << 16)) == 0;

                bool kidsHaveParent = false;
                if (isRadioGroup)
                {
                    foreach (var kid in kids)
                    {
                        var kidDict = reader.ResolveDict(kid);
                        if (kidDict?.ContainsKey("Parent") == true) { kidsHaveParent = true; break; }
                    }
                }

                if (isRadioGroup && kidsHaveParent)
                {
                    // Real PDF radio group: expand to per-option widgets with their Rects.
                    // Record the group node so CollectGroupFields doesn't also surface it as
                    // a separate field — it's already represented by its expanded option widgets.
                    expandedGroups.Add(dict);
                    foreach (var kid in kids)
                    {
                        var kidDict = reader.ResolveDict(kid);
                        if (kidDict is not null)
                        {
                            // Surface each radio option as a RadioButtonOptionField (carrying its
                            // own /Rect and /MK characteristics) — the option type the public API
                            // expects on Form.Fields. The parent group is still surfaced as a
                            // RadioButtonField by CollectGroupFields / FindByName.
                            var opt = new RadioButtonOptionField(kidDict, reader)
                            { ObjectNumber = GetObjectNumber(kid) };
                            result.Add(opt);
                        }
                    }
                }
                else
                {
                    result.Add(CreateFieldWithObjNum(dict, reader, objNum));
                }
            }
            else
            {
                result.Add(CreateFieldWithObjNum(dict, reader, objNum));
            }
        }
    }

    /// <summary>After terminal fields are collected, also surface the named
    /// non-terminal (group) fields sitting in their /Parent ancestry, so callers
    /// can find a group by its full name and enumerate its child fields. Group
    /// dicts are de-duplicated when several terminals share the same parent.</summary>
    private static void CollectGroupFields(PdfReader reader, List<Field> fields,
        HashSet<PdfDictionary> exclude, HashSet<PdfDictionary> groupDicts)
    {
        var seen = new HashSet<PdfDictionary>();
        foreach (var f in fields) seen.Add(f.Dict);
        // Radio groups already expanded into option widgets must not be re-surfaced
        // as standalone group fields (Form.Fields counts the widgets only).
        foreach (var e in exclude) seen.Add(e);

        var groups = new List<Field>();
        var terminalCount = fields.Count;
        for (var i = 0; i < terminalCount; i++)
        {
            var parent = reader.ResolveDict(fields[i].Dict.Get("Parent"));
            while (parent is not null)
            {
                if (parent.ContainsKey("T") && seen.Add(parent))
                {
                    groups.Add(Field.Create(parent, reader));
                    groupDicts.Add(parent);
                }
                parent = reader.ResolveDict(parent.Get("Parent"));
            }
        }
        fields.AddRange(groups);
    }

    private static bool HasWidgetKids(PdfArray kids, PdfReader reader)
    {
        // If any kid has /Subtype /Widget, these are widget annotations (terminal field)
        foreach (var kid in kids)
        {
            var kidDict = reader.ResolveDict(kid);
            if (kidDict is null) continue;
            var subtype = kidDict.GetName("Subtype");
            if (subtype == "Widget") return true;
            // Also check if kid has /T (partial name) — means it's a field node, not widget
            if (kidDict.ContainsKey("T")) return false;
        }
        return true;
    }

    // ── Public-API shape additions ───────────────────────────────

    /// <summary>How widgets dependent on signature appearance are rendered when the form is converted.</summary>
    public enum SignDependentElementsRenderingModes
    {
        /// <summary>Render the form as if every signature is valid and signed.</summary>
        RenderFormAsSigned = 0,
        /// <summary>Render the form as if no signatures are present.</summary>
        RenderFormAsUnsigned = 1,
    }

    /// <summary>Strategy applied to widgets whose appearance depends on signed-state at conversion time.</summary>
    public SignDependentElementsRenderingModes SignDependentElementsRenderingModeWhenConverted;

    /// <summary>True when the form contains an XFA part.</summary>
    public bool HasXfa => IsXfa;

    /// <summary>When true, recalculate calculated fields whenever a dependency changes. Stored only; recalculation is not currently driven by this flag.</summary>
    public bool AutoRecalculate { get; set; }

    // True when this form's fields were reconstructed from page widgets because
    // the AcroForm /Fields entry was missing or empty.
    private bool _autoRestored;

    private bool _autoRestoreForm = true;

    /// <summary>Whether the form reconstructs its fields from page widgets when the
    /// AcroForm /Fields entry is missing/empty (default true). Setting it to false
    /// drops any such auto-restored fields, leaving only the real /Fields entries.</summary>
    public bool AutoRestoreForm
    {
        get => _autoRestoreForm;
        set
        {
            _autoRestoreForm = value;
            if (!value && _autoRestored)
            {
                _fields.Clear();
                _autoRestored = false;
            }
        }
    }

    /// <summary>The field order used to drive calculation passes. Stored only.</summary>
    public IEnumerable<Field>? CalculatedFields { set => _calculatedFields = value; }

    private IEnumerable<Field>? _calculatedFields;

    /// <summary>Default appearance applied to fields that don't carry their own /DA.</summary>
    public Aspose.Pdf.Annotations.DefaultAppearance DefaultAppearance { get; set; } = new();

    /// <summary>Default font / colorspace resources used by the form. Returns null until a backing Resources instance is wired through.</summary>
    public Aspose.Pdf.Resources? DefaultResources { get; internal set; }

    /// <summary>Whether the underlying collection is thread-safe. Always false.</summary>
    public bool IsSynchronized => false;

    /// <summary>True when the form has the NeedsAppearances flag set.</summary>
    public bool NeedsRendering => !IgnoreNeedsRendering;

    /// <summary>When true, the document's permission bits are stripped before saving. Stored only.</summary>
    public bool RemovePermission { get; set; }

    /// <summary>When true, the writer ignores signature-bypassing modifications. Stored only.</summary>
    public bool SignaturesAppendOnly { get; set; }

    /// <summary>Synchronization root object for <see cref="IsSynchronized"/>. Always returns <c>this</c>.</summary>
    public object SyncRoot => this;

    /// <summary>
    /// Copy <paramref name="field"/> into the form under a new partial name,
    /// binding its widget(s) to <paramref name="pageNumber"/>. The source field
    /// and its widget kids are deep-copied (one level), so later edits to the
    /// returned field — including per-widget rectangles — do not affect the
    /// original. Returns the newly added field.
    /// </summary>
    public Field Add(Field field, string partialName, int pageNumber)
    {
        if (field is null) throw new ArgumentNullException(nameof(field));
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null)
            throw new InvalidOperationException("Cannot add fields to an empty form.");

        // Copy the source field dict (except /Kids, rebuilt below) and rename it.
        var newDict = new PdfDictionary();
        foreach (var key in field.Dict.Keys)
        {
            if (key == "Kids") continue;
            var val = field.Dict.Get(key);
            if (val is not null) newDict.Set(key, val);
        }
        newDict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(partialName)));

        // Rebuild /Kids as independent copies of each source widget so that
        // editing a copied widget's /Rect does not mutate the original.
        var srcKids = reader.Resolve(field.Dict.Get("Kids")) as PdfArray;
        var newKids = new PdfArray();
        var copiedWidgets = new List<PdfDictionary>();
        if (srcKids is not null)
        {
            foreach (var k in srcKids)
            {
                if (reader.Resolve(k) is not PdfDictionary srcKid) continue;
                var newKid = new PdfDictionary();
                foreach (var kk in srcKid.Keys)
                {
                    var kv = srcKid.Get(kk);
                    if (kv is not null) newKid.Set(kk, kv);
                }
                newKids.Add(newKid);
                copiedWidgets.Add(newKid);
            }
        }
        if (newKids.Count > 0) newDict.Set("Kids", newKids);

        var newField = Field.Create(newDict, reader);
        newField.OwnerDocument = OwnerDocument;

        // Register in the AcroForm /Fields array.
        var catalog = reader.Catalog;
        var acroForm = reader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null)
        {
            acroForm = new PdfDictionary();
            catalog.Set("AcroForm", acroForm);
        }
        var fieldsArray = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fieldsArray is null)
        {
            fieldsArray = new PdfArray();
            acroForm.Set("Fields", fieldsArray);
        }
        fieldsArray.Add(newDict);
        acroForm.Set("NeedAppearances", PdfBoolean.True);
        EnsureDefaultResources(acroForm);

        // Bind the widget(s) to the target page's /Annots.
        var pages = new PageCollection(reader);
        PdfDictionary? pageDict = null;
        if (pageNumber >= 1 && pageNumber <= pages.Count)
        {
            pageDict = pages[pageNumber].Dict;
            var annots = pageDict.Get("Annots") as PdfArray;
            if (annots is null)
            {
                annots = new PdfArray();
                pageDict.Set("Annots", annots);
            }

            if (copiedWidgets.Count > 0)
            {
                foreach (var widget in copiedWidgets)
                {
                    widget.Set("P", pageDict);
                    widget.Set("Parent", newDict);
                    annots.Add(widget);
                }
            }
            else
            {
                // Single-widget field merged into the field dict.
                newDict.Set("P", pageDict);
                annots.Add(newDict);
            }
        }

        _fields.Add(newField);

        // Mark dirty so incremental save persists the new field/annots.
        var doc = OwnerDocument;
        if (doc is not null)
        {
            var acroFormObjNum = doc.FindObjectNumber(acroForm);
            if (acroFormObjNum > 0)
                doc.MarkDirty(acroFormObjNum, acroForm);
            if (pageDict is not null)
            {
                var pageObjNum = doc.FindObjectNumber(pageDict);
                if (pageObjNum > 0)
                    doc.MarkDirty(pageObjNum, pageDict);
            }
        }

        return newField;
    }

    /// <summary>Append a fresh widget appearance to a field on a specific page within a rectangle.</summary>
    public void AddFieldAppearance(Field field, int pageNumber, Rectangle rect)
    {
        if (field is null) return;
        var doc = OwnerDocument;
        var reader = _reader ?? doc?.Reader;
        if (doc is null || reader is null) return;

        // Build an extra widget for this field: its own /Rect on the target page and an
        // /AP/N rendering the field value. The field keeps its own /Rect+/AP (the first
        // visual widget) and grows a /Kids array of additional widgets.
        var kid = new PdfDictionary();
        kid.Set("Type", new PdfName("Annot"));
        kid.Set("Subtype", new PdfName("Widget"));
        kid.Set("Rect", Field.MakeRectArray(rect));
        kid.Set("F", new PdfInteger(4));
        kid.Set("Parent", field.Dict);
        var ap = field.BuildWidgetApDict(rect.Width, rect.Height);
        if (ap is not null) kid.Set("AP", ap);

        var kids = reader.Resolve(field.Dict.Get("Kids")) as PdfArray;
        if (kids is null)
        {
            kids = new PdfArray();
            field.Dict.Set("Kids", kids);
        }
        kids.Add(kid);

        if (pageNumber >= 1 && pageNumber <= doc.Pages.Count)
        {
            var page = doc.Pages[pageNumber];
            var annots = reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
            if (annots is null)
            {
                annots = new PdfArray();
                page.Dict.Set("Annots", annots);
            }
            annots.Add(kid);
        }
    }

    /// <summary>Replace the XFA datasets from the given XmlDocument.</summary>
    public void AssignXfa(XmlDocument xml)
    {
        if (xml?.OuterXml is null) return;
        var reader = _reader ?? OwnerDocument?.Reader;
        var doc = OwnerDocument;
        if (reader is null || doc is null) return;

        // Ensure the catalog carries an AcroForm dictionary (a freshly created document
        // has none), then store the whole XDP as a single indirect /XFA stream. After
        // save+reload the catalog AcroForm contains /XFA, so IsXfa/HasXfa report true.
        var acro = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acro is null)
        {
            acro = new PdfDictionary();
            reader.Catalog.Set("AcroForm", acro);
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(xml.OuterXml);
        var streamDict = new PdfDictionary();
        streamDict.Set("Length", new PdfInteger(bytes.Length));
        var xfaStream = new PdfStream(streamDict, bytes);
        var objNum = doc.AllocateObjectNumber();
        doc.AddNewObject(objNum, xfaStream, registerOverlay: true);
        acro.Set("XFA", new PdfIndirectRef(objNum, 0));
        _acroForm = acro;
    }

    /// <summary>Copy fields into the supplied array starting at <paramref name="index"/>.</summary>
    public void CopyTo(Field[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        _fields.CopyTo(array, index);
    }

    /// <summary>Remove the supplied field from the form.</summary>
    public void Delete(Field field)
    {
        if (field is null) return;
        _fields.Remove(field);

        // Keep the AcroForm /Fields array in sync with the cached field list — Count reads
        // /Fields, so a cache-only removal left the deleted field still counted (and saved).
        // Rebuild /Fields from the surviving top-level fields (the deleted one is already gone
        // from _fields), preserving each field's original /Fields item where it is still present.
        var reader = _reader ?? OwnerDocument?.Reader;
        var af = _acroForm ?? (reader is not null ? reader.ResolveDict(reader.Catalog?.Get("AcroForm")) : null);
        if (reader is not null && af is not null && reader.Resolve(af.Get("Fields")) is PdfArray fields)
        {
            var survivors = new System.Collections.Generic.HashSet<PdfDictionary>(
                System.Collections.Generic.ReferenceEqualityComparer.Instance);
            foreach (var f in _fields) survivors.Add(f.Dict);
            var kept = new PdfArray();
            foreach (var item in fields)
                if (reader.ResolveDict(item) is { } d && survivors.Contains(d))
                    kept.Add(item);
            af.Set("Fields", kept);
        }
    }

    /// <summary>The root form fields — one per resolvable entry of the AcroForm
    /// <c>/Fields</c> array (so the count matches <see cref="Count"/>). A group/subform
    /// or radio group is a single root; its child fields are nested by the exporter,
    /// not surfaced as separate roots. Falls back to the flattened field list for
    /// page-widget-only forms that carry no <c>/Fields</c> array.</summary>
    private List<Field> RootFields(PdfReader? reader)
    {
        if (reader is not null)
        {
            var fieldsArray = _acroForm is not null
                ? reader.Resolve(_acroForm.Get("Fields")) as PdfArray
                : null;
            fieldsArray ??= reader.Resolve(
                reader.ResolveDict(reader.Catalog?.Get("AcroForm"))?.Get("Fields")) as PdfArray;
            if (fieldsArray is not null)
            {
                var roots = new List<Field>();
                foreach (var item in fieldsArray)
                    if (reader.ResolveDict(item) is { } d)
                    {
                        var field = Field.Create(d, reader);
                        field.OwnerDocument = OwnerDocument;
                        roots.Add(field);
                    }
                return roots;
            }
        }
        return _fields;
    }

    /// <summary>Return the fields that intersect the supplied rectangle.</summary>
    public Field[] GetFieldsInRect(Rectangle rect)
    {
        if (rect is null) return Array.Empty<Field>();
        var list = new List<Field>();
        foreach (var f in _fields)
        {
            var r = f.Rect;
            if (r is null) continue;
            if (r.URX < rect.LLX || r.LLX > rect.URX) continue;
            if (r.URY < rect.LLY || r.LLY > rect.URY) continue;
            list.Add(f);
        }
        return list.ToArray();
    }

    /// <summary>True when the supplied field belongs to this form.</summary>
    public bool HasField(Field field) => field is not null && _fields.Contains(field);

    /// <summary>Detach form annotations from the field tree so they can be moved between pages independently.</summary>
    public void MakeFormAnnotationsIndependent(Page page)
    {
        _ = page;
        // Records intent; the widget-annotation hierarchy is not yet rewritten.
    }

    /// <summary>Remove a specific appearance entry from a field.</summary>
    public void RemoveFieldAppearance(Field field, int appearanceIndex)
    {
        if (field is null) return;
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null) return;

        // The field's visual widgets, 1-based in /Kids order: the field's own merged widget first
        // (when it has one), then each /Kids entry. appearanceIndex must be in [1..Count].
        int count = field.Count;
        if (appearanceIndex < 1 || appearanceIndex > count)
            throw new IndexOutOfRangeException(
                $"childIndex should be in the range [1..{count}] where n equals to the field count");

        var widgets = new System.Collections.Generic.List<PdfDictionary>();
        if (field.HasMergedSelfWidget) widgets.Add(field.Dict);
        var kids = reader.Resolve(field.Dict.Get("Kids")) as PdfArray;
        if (kids is not null)
            foreach (var k in kids)
                if (reader.Resolve(k) is PdfDictionary kd) widgets.Add(kd);

        var target = widgets[appearanceIndex - 1];
        widgets.RemoveAt(appearanceIndex - 1);
        if (!ReferenceEquals(target, field.Dict)) DetachAnnotFromPages(target, reader);

        if (widgets.Count <= 1)
        {
            // Removal leaves a single widget: collapse back to a merged leaf so Count returns to 0.
            // The survivor's appearance lives on the field dict; drop the now-empty /Kids array.
            if (widgets.Count == 1 && !ReferenceEquals(widgets[0], field.Dict))
            {
                var survivor = widgets[0];
                foreach (var key in new[] { "AP", "Rect", "AS", "MK", "F", "DA" })
                    if (survivor.Get(key) is { } v) field.Dict.Set(key, v);
                DetachAnnotFromPages(survivor, reader);
            }
            field.Dict.Remove("Kids");
            return;
        }

        // Two or more widgets remain. If the field's own merged widget was the one removed, strip its
        // widget keys so the field dict becomes a pure parent; keep the survivors in /Kids.
        if (ReferenceEquals(target, field.Dict))
            foreach (var key in new[] { "Rect", "AP", "AS", "MK" }) field.Dict.Remove(key);
        var newKids = new PdfArray();
        foreach (var w in widgets)
            if (!ReferenceEquals(w, field.Dict)) newKids.Add(w);
        field.Dict.Set("Kids", newKids);
    }

    /// <summary>Remove a widget-annotation dictionary from every page's /Annots array.</summary>
    private void DetachAnnotFromPages(PdfDictionary widget, PdfReader reader)
    {
        var doc = OwnerDocument;
        if (doc is null) return;
        for (int p = 1; p <= doc.Pages.Count; p++)
        {
            if (reader.Resolve(doc.Pages[p].Dict.Get("Annots")) is not PdfArray annots) continue;
            for (int i = annots.Count - 1; i >= 0; i--)
                if (ReferenceEquals(reader.Resolve(annots[i]), widget)) annots.RemoveAt(i);
        }
    }
}
