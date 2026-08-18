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
    /// <summary>Build the page-content fragment that folds a widget annotation's /AP /N appearance
    /// into the page, registering it as the XForm <paramref name="frmName"/>. Resolves the current
    /// state for a multi-state (/AS) appearance, places the appearance per PDF 32000-1 §12.5.5
    /// (transform /BBox by /Matrix → axis-aligned bounds → map onto /Rect, leaving the form's own
    /// /Matrix on the XObject), and returns the <c>q … cm /FRMn Do Q</c> string. Null when there
    /// is no usable appearance or rectangle.</summary>
    private static string? BuildWidgetFlattenFragment(
        PdfDictionary pageDict, PdfDictionary annotDict, PdfReader reader, string frmName)
    {
        var apDict = reader.ResolveDict(annotDict.Get("AP"));
        if (apDict is null) return null;
        // Appearance selection for a flatten is STRICTLY state-driven (probed against the
        // reference flatten): when the widget carries an /AS, only /N[/AS] counts — a state
        // the appearance dictionary does not define draws NOTHING and the widget is skipped
        // (an unselected checkbox whose /AP/N holds only its on-state is the common case).
        // A state dictionary with no /AS to select from likewise draws nothing; only a bare
        // /N stream is used unconditionally. There is no "first non-Off state" fallback —
        // that fallback rendered unselected boxes as selected.
        var nResolved = reader.Resolve(apDict.Get("N"));
        PdfStream? appearanceStream;
        var asName = annotDict.GetName("AS");
        if (asName is not null)
            appearanceStream = nResolved is PdfDictionary asStates
                ? reader.ResolveStream(asStates.Get(asName))
                : null;
        else
            appearanceStream = nResolved as PdfStream;
        if (appearanceStream is null) return null;

        if (reader.Resolve(annotDict.Get("Rect")) is not PdfArray rectArr || rectArr.Count < 4)
            return null;
        var rect = Rectangle.FromPdfArray(rectArr);

        double tllx = 0, tlly = 0, tw = rect.Width, th = rect.Height;
        if (reader.Resolve(appearanceStream.Dict.Get("BBox")) is PdfArray bboxArr && bboxArr.Count >= 4)
        {
            var bbox = Rectangle.FromPdfArray(bboxArr);
            double[] m = ReadAppearanceMatrix(appearanceStream.Dict, reader);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (px, py) in new[] { (bbox.LLX, bbox.LLY), (bbox.URX, bbox.LLY), (bbox.URX, bbox.URY), (bbox.LLX, bbox.URY) })
            {
                double qx = m[0] * px + m[2] * py + m[4];
                double qy = m[1] * px + m[3] * py + m[5];
                if (qx < minX) minX = qx; if (qx > maxX) maxX = qx;
                if (qy < minY) minY = qy; if (qy > maxY) maxY = qy;
            }
            tllx = minX; tlly = minY; tw = maxX - minX; th = maxY - minY;
        }

        var sx = tw > 0 ? rect.Width / tw : 1.0;
        var sy = th > 0 ? rect.Height / th : 1.0;
        var tx = rect.LLX - tllx * sx;
        var ty = rect.LLY - tlly * sy;
        var xformName = RegisterAppearanceAsXForm(pageDict, appearanceStream, reader, frmName);
        return $"q {Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n/{xformName} Do\nQ\n";
    }

    /// <summary>Flatten a single field: fold each of its widget annotations into the owning page's
    /// content (as an FRM XObject placed at the widget /Rect), remove those widgets from the page
    /// /Annots, and drop the field from the AcroForm. Used by <see cref="Field.Flatten()"/>.</summary>
    internal void FlattenField(Field field)
    {
        var reader = _reader ?? OwnerDocument?.Reader;
        var doc = OwnerDocument;
        if (reader is null || doc is null) return;

        // The widget dicts this field contributes: a merged single-widget field is its own dict;
        // otherwise each kid widget.
        var widgets = new List<PdfDictionary>();
        if (field.Dict.ContainsKey("Rect")) widgets.Add(field.Dict);
        foreach (var kid in field.AllKids())
            if (kid.ContainsKey("Rect") && !ReferenceEquals(kid, field.Dict)) widgets.Add(kid);
        if (widgets.Count == 0) return;

        // Refresh the appearance unless it carries a non-identity /Matrix (see
        // RegenerateAppearanceForFlatten).
        RegenerateAppearanceForFlatten(field);

        foreach (var page in doc.Pages)
        {
            if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) continue;
            var append = new System.IO.MemoryStream();
            var remaining = new PdfArray();
            int frm = NextFreeFrmIndex(page.Dict, reader);
            var writer = new System.IO.StreamWriter(append, System.Text.Encoding.ASCII, leaveOpen: true);
            bool changed = false;
            foreach (var annotRef in annots)
            {
                var annotDict = reader.ResolveDict(annotRef);
                if (annotDict is not null && widgets.Exists(w => ReferenceEquals(w, annotDict)))
                {
                    var frag = BuildWidgetFlattenFragment(page.Dict, annotDict, reader, $"FRM{frm}");
                    if (frag is not null) { writer.Write(frag); frm++; changed = true; continue; }
                }
                remaining.Add(annotRef);
            }
            writer.Flush();
            if (!changed) continue;
            if (remaining.Count > 0) page.Dict.Set("Annots", remaining); else page.Dict.Remove("Annots");
            if (append.Length > 0)
            {
                var existing = page.GetContentStreamBytes() ?? [];
                var combined = new byte[existing.Length + (existing.Length > 0 ? 1 : 0) + append.Length];
                existing.CopyTo(combined, 0);
                if (existing.Length > 0) combined[existing.Length] = (byte)'\n';
                append.ToArray().CopyTo(combined, existing.Length + (existing.Length > 0 ? 1 : 0));
                page.SetContentStream(combined);
            }
        }

        // Drop the field from the AcroForm /Fields and the cached list.
        var acro = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (reader.Resolve(acro?.Get("Fields")) is PdfArray fields)
        {
            var kept = new PdfArray();
            foreach (var fr in fields)
                if (!ReferenceEquals(reader.ResolveDict(fr), field.Dict)) kept.Add(fr);
            acro!.Set("Fields", kept);
        }
        _fields.Remove(field);
    }

    /// <summary>The next unused FRM{n} index in the page's XObject resources (so a single-field
    /// flatten doesn't collide with already-registered forms).</summary>
    private static int NextFreeFrmIndex(PdfDictionary pageDict, PdfReader reader)
    {
        var res = reader.ResolveDict(pageDict.Get("Resources"));
        var xobj = res is null ? null : reader.ResolveDict(res.Get("XObject"));
        int n = 0;
        if (xobj is not null)
            while (xobj.ContainsKey("FRM" + n)) n++;
        return n;
    }

    /// <summary>
    /// Register an appearance stream as a named XForm in the page's XObject resources.
    /// Returns the assigned name (e.g., "FRM0", "FRM1"). Shared with annotation
    /// flatten (Annotation.Flatten) so both code paths use the same naming.
    /// </summary>
    /// <summary>Read an appearance stream's /Matrix entry as [a b c d e f], defaulting to the
    /// identity matrix when absent or malformed.</summary>
    private static double[] ReadAppearanceMatrix(PdfDictionary apDict, PdfReader reader)
    {
        var m = new double[] { 1, 0, 0, 1, 0, 0 };
        if (reader.Resolve(apDict.Get("Matrix")) is PdfArray arr && arr.Count >= 6)
            for (int i = 0; i < 6; i++)
                m[i] = arr[i] switch { PdfReal r => r.Value, PdfInteger n => n.Value, _ => m[i] };
        return m;
    }

    internal static string RegisterAppearanceAsXForm(
        PdfDictionary pageDict, PdfStream appearanceStream, PdfReader reader, string? preferredName = null)
    {
        var resources = EnsureOwnPageResources(pageDict, reader);

        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null)
        {
            xobjects = new PdfDictionary();
            resources.Set("XObject", xobjects);
        }

        // Use the caller's preferred name when free (field flatten assigns 1-based FRM{n}
        // in /Annots order); otherwise generate a unique FRM0, FRM1, … (annotation flatten).
        string name;
        if (!string.IsNullOrEmpty(preferredName) && !xobjects.ContainsKey(preferredName!))
        {
            name = preferredName!;
        }
        else
        {
            int idx = 0;
            do
            {
                name = $"FRM{idx}";
                idx++;
            } while (xobjects.ContainsKey(name));
        }

        // Ensure the appearance stream has /Type /XObject and /Subtype /Form
        appearanceStream.Dict.Set("Type", new PdfName("XObject"));
        appearanceStream.Dict.Set("Subtype", new PdfName("Form"));

        xobjects.Set(name, appearanceStream);
        return name;
    }

    internal static void MergeAnnotResources(PdfDictionary pageDict, PdfDictionary apDict, PdfReader reader)
    {
        var apResources = reader.ResolveDict(apDict.Get("Resources"));
        if (apResources is null) return;

        var pageResources = EnsureOwnPageResources(pageDict, reader);

        // Merge each resource category (Font, XObject, ExtGState, etc.)
        foreach (var category in apResources.Keys)
        {
            var apCatDict = reader.ResolveDict(apResources.Get(category));
            if (apCatDict is null) continue;

            var pageCatDict = reader.ResolveDict(pageResources.Get(category));
            if (pageCatDict is null)
            {
                pageCatDict = new PdfDictionary();
                pageResources.Set(category, pageCatDict);
            }

            foreach (var key in apCatDict.Keys)
            {
                if (!pageCatDict.ContainsKey(key))
                {
                    var val = apCatDict.Get(key);
                    if (val is not null) pageCatDict.Set(key, val);
                }
            }
        }
    }

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

    /// <summary>Serialize the form's fields to JSON via the supplied stream.</summary>
    public IEnumerable<FieldSerializationResult> ExportToJson(Stream stream)
        => ExportToJson(stream, null);

    /// <summary>Serialize the form's fields to JSON in a file.</summary>
    public IEnumerable<FieldSerializationResult> ExportToJson(string fileName)
        => ExportToJson(fileName, null);

    /// <summary>Serialize the form's fields to JSON via the supplied stream.</summary>
    public IEnumerable<FieldSerializationResult> ExportToJson(Stream stream, ExportFieldsToJsonOptions? options)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var indent = options?.WriteIndented ?? false;
        var reader = _reader ?? OwnerDocument?.Reader;
        // Serialize ROOT fields only (one entry per AcroForm /Fields entry, matching
        // Count): a group/subform or radio group is a single entry whose descendants
        // the exporter nests under /ChildFields — not one flat entry per terminal.
        var roots = RootFields(reader);
        var entries = new List<FieldExportingData>(roots.Count + 1);
        var results = new List<FieldSerializationResult>(roots.Count);
        foreach (var f in roots)
        {
            entries.Add(FieldJsonExporter.BuildField(f));
            results.Add(new FieldSerializationResult
            {
                FieldFullName = f.FullName ?? f.PartialName ?? string.Empty,
                FieldSerializationStatus = FieldSerializationStatus.Success,
            });
        }
        // Append a single entry carrying the form-level AcroForm dictionary data.
        entries.Add(FieldJsonExporter.BuildAcroForm(ResolveAcroForm(), reader));
        FieldJsonExporter.Write(stream, entries, indent);
        return results;
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

    /// <summary>Serialize the form's fields to JSON in a file.</summary>
    public IEnumerable<FieldSerializationResult> ExportToJson(string fileName, ExportFieldsToJsonOptions? options)
    {
        using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        return ExportToJson(fs, options);
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

    /// <summary>Read form-field values from a JSON stream and apply them.</summary>
    public IEnumerable<FieldSerializationResult> ImportFromJson(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var results = new List<FieldSerializationResult>();
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null) return results;
        try
        {
            using var jdoc = System.Text.Json.JsonDocument.Parse(stream);
            var root = jdoc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var entry in root.EnumerateArray())
                    ImportFieldEntry(entry, reader, null, results);
            }
            else if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                ImportFieldEntry(root, reader, null, results);
            }
        }
        catch
        {
            // parse failure → whatever was reconstructed so far
        }
        return results;
    }

    /// <summary>Reconstruct a field (and its child fields) from a FieldExportingData
    /// JSON entry. Top-level fields are added to the form; child fields are wired
    /// as /Kids of their parent (so a group field contributes a single Form entry).
    /// Returns the built dictionary for use as a parent's kid.</summary>
    private PdfDictionary? ImportFieldEntry(
        System.Text.Json.JsonElement entry, PdfReader reader,
        PdfDictionary? parent, List<FieldSerializationResult> results)
    {
        if (entry.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        var hasFieldType = entry.TryGetProperty("FieldType", out var ftEl)
            && ftEl.ValueKind == System.Text.Json.JsonValueKind.String;
        var hasAcroForm = entry.TryGetProperty("AcroFormData", out var acroEl)
            && acroEl.ValueKind != System.Text.Json.JsonValueKind.Null;
        // The single form-level AcroForm entry carries no field — apply its
        // dictionary data (/DA, /NeedAppearances, /DR) to the target form instead.
        if (parent is null && !hasFieldType && hasAcroForm)
        {
            ImportAcroFormData(acroEl, reader);
            return null;
        }

        var name = entry.TryGetProperty("Name", out var n)
            && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null;
        if (string.IsNullOrEmpty(name)) return null;

        var dict = new PdfDictionary();
        // A child's partial name is the last dotted segment; a top-level field uses its full name.
        var partial = parent is not null && name!.Contains('.') ? name.Substring(name.LastIndexOf('.') + 1) : name!;
        dict.Set("T", new PdfString(Encoding.Latin1.GetBytes(partial)));
        if (hasFieldType)
        {
            var ftName = ftEl.GetString();
            if (MapFieldTypeToFt(ftName) is { } ft)
                dict.Set("FT", new PdfName(ft));
            // Restore the field-flag bits that distinguish the concrete /Btn and
            // /Ch subtypes (radio / push-button / combo) so the field rebuilds as
            // the right type — the flat export carries only the FieldType name.
            var ff = FieldTypeFlags(ftName);
            if (ff != 0) dict.Set("Ff", new PdfInteger(ff));
        }
        if (entry.TryGetProperty("Value", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
            dict.Set("V", new PdfString(Encoding.Latin1.GetBytes(v.GetString()!)));
        if (entry.TryGetProperty("Flags", out var fl) && fl.ValueKind == System.Text.Json.JsonValueKind.Number
            && fl.TryGetInt32(out var flv) && flv != 0)
            dict.Set("F", new PdfInteger(flv));
        if (parent is not null) dict.Set("Parent", parent);

        // Carry the field's /DA so the appearance generator picks up the original
        // font + size (without it the default 12pt /Helv clips the value text).
        if (entry.TryGetProperty("DefaultAppearance", out var daEl)
            && daEl.ValueKind == System.Text.Json.JsonValueKind.String)
            dict.Set("DA", new PdfString(Encoding.Latin1.GetBytes(daEl.GetString()!)));

        // Choice-field options (/Opt) — required for a listbox to show all items,
        // and for a combo-box appearance that needs the display value list.
        if (entry.TryGetProperty("Options", out var optEl)
            && optEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var oa = new PdfArray();
            foreach (var o in optEl.EnumerateArray())
                if (o.ValueKind == System.Text.Json.JsonValueKind.String)
                    oa.Add(new PdfString(Encoding.Latin1.GetBytes(o.GetString()!)));
            if (oa.Count > 0) dict.Set("Opt", oa);
        }

        // Push-button normal caption (/MK /CA) — ButtonField.GenerateAppearance
        // centres this string on the button face.
        if (entry.TryGetProperty("NormalCaption", out var ncEl)
            && ncEl.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var mk = new PdfDictionary();
            mk.Set("CA", new PdfString(Encoding.Latin1.GetBytes(ncEl.GetString()!)));
            dict.Set("MK", mk);
        }

        // Restore each radio/check widget's identity within its group: /AS picks
        // the visible variant; the appearance generator uses /AS (when not "Off")
        // as the on-name for the widget's /AP/N, so the field's /V selects the
        // right widget visually after round-trip.
        if (entry.TryGetProperty("AppearanceState", out var asEl)
            && asEl.ValueKind == System.Text.Json.JsonValueKind.String)
            dict.Set("AS", new PdfName(asEl.GetString()!));

        // A widget carries a /Rect; reconstruct it so the field renders in place.
        if (entry.TryGetProperty("Rect", out var rc)
            && rc.ValueKind == System.Text.Json.JsonValueKind.Array && rc.GetArrayLength() >= 4)
        {
            var ra = new PdfArray();
            var idx = 0;
            foreach (var num in rc.EnumerateArray())
            {
                if (idx++ >= 4) break;
                ra.Add(new PdfReal(num.GetDouble()));
            }
            dict.Set("Rect", ra);
            dict.Set("Type", new PdfName("Annot"));
            dict.Set("Subtype", new PdfName("Widget"));
        }

        var pageIndex = 1;
        if (entry.TryGetProperty("Page", out var pg) && pg.ValueKind == System.Text.Json.JsonValueKind.Number
            && pg.TryGetInt32(out var pv) && pv > 0)
            pageIndex = pv;

        var childApRoundTripped = false;
        if (entry.TryGetProperty("ChildFields", out var kids) && kids.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var kidsArr = new PdfArray();
            foreach (var kid in kids.EnumerateArray())
            {
                var kidDict = ImportFieldEntry(kid, reader, dict, results);
                if (kidDict is not null)
                {
                    kidsArr.Add(kidDict);
                    // A radio option widget carries its own round-tripped /AP; its
                    // presence means the group must NOT be regenerated below (the
                    // radio generator overwrites kid appearances unconditionally).
                    if (kidDict.ContainsKey("AP")) childApRoundTripped = true;
                }
            }
            if (kidsArr.Count > 0) dict.Set("Kids", kidsArr);
        }

        // Restore the widget's /AP from captured stream bytes, when present.
        // /AP precedence over GenerateAppearance keeps Acrobat's pre-baked
        // appearance pixel-identical through the round-trip (the per-type
        // generator's first line short-circuits when /AP is populated).
        var apRoundTripped = ImportAppearances(entry, dict);

        if (parent is null)
        {
            var field = Field.Create(dict, reader);
            field.OwnerDocument = OwnerDocument;
            _fields.Add(field);
            AddToAcroFormFields(reader, dict);
            // Draw the field's appearance and place its widget(s) on the page so
            // the imported form renders, mirroring Form.Add for a created field.
            // Skip the generator pass when /AP was round-tripped from JSON --
            // the per-type generators check for /AP themselves but radio's path
            // overwrites kids unconditionally; skipping at this level is safer.
            if (!apRoundTripped && !childApRoundTripped) field.GenerateAppearance();
            PlaceFieldWidgets(dict, reader, pageIndex);
            results.Add(new FieldSerializationResult
            {
                FieldFullName = name,
                FieldSerializationStatus = FieldSerializationStatus.Success,
            });
        }
        return dict;
    }

    /// <summary>Rebuild the widget's /AP dict from the JSON Appearances block,
    /// reconstructing each variant's Form-XObject stream verbatim. Returns true
    /// when at least one stream was attached -- the caller skips
    /// GenerateAppearance so the per-type generator can't overwrite the
    /// round-tripped content.</summary>
    private static bool ImportAppearances(System.Text.Json.JsonElement entry, PdfDictionary widgetDict)
    {
        if (!entry.TryGetProperty("Appearances", out var apsEl)
            || apsEl.ValueKind != System.Text.Json.JsonValueKind.Object)
            return false;

        var apDict = new PdfDictionary();
        var any = false;
        foreach (var variant in apsEl.EnumerateObject())
        {
            if (variant.Value.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
            // Single-stream variant: one entry with null state -> /AP/<v> = stream.
            // State-dict variant: many entries -> /AP/<v> = { state: stream, ... }.
            PdfDictionary? states = null;
            PdfStream? singleStream = null;
            var count = variant.Value.GetArrayLength();
            foreach (var stateEl in variant.Value.EnumerateArray())
            {
                if (stateEl.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var hasState = stateEl.TryGetProperty("State", out var sEl)
                    && sEl.ValueKind == System.Text.Json.JsonValueKind.String;
                var stateName = hasState ? sEl.GetString() : null;
                var s = BuildAppearanceStream(stateEl);
                if (s is null) continue;
                if (count == 1 && !hasState)
                {
                    singleStream = s;
                }
                else
                {
                    states ??= new PdfDictionary();
                    states.Set(stateName ?? "On", s);
                }
            }
            if (singleStream is not null)
            {
                apDict.Set(variant.Name, singleStream);
                any = true;
            }
            else if (states is not null)
            {
                apDict.Set(variant.Name, states);
                any = true;
            }
        }

        if (!any) return false;
        widgetDict.Set("AP", apDict);
        return true;
    }

    /// <summary>Build a Form XObject /AP stream from one Appearances entry --
    /// Base64-decode the content bytes, restore /BBox + /Matrix, and rebuild
    /// /Resources with Standard-14 fonts under the captured aliases.</summary>
    private static PdfStream? BuildAppearanceStream(System.Text.Json.JsonElement stateEl)
    {
        if (!stateEl.TryGetProperty("Content", out var cEl)
            || cEl.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;
        byte[] bytes;
        try { bytes = System.Convert.FromBase64String(cEl.GetString()!); }
        catch { return null; }

        var sd = new PdfDictionary();
        sd.Set("Type", new PdfName("XObject"));
        sd.Set("Subtype", new PdfName("Form"));

        if (stateEl.TryGetProperty("BBox", out var bbEl)
            && bbEl.ValueKind == System.Text.Json.JsonValueKind.Array && bbEl.GetArrayLength() >= 4)
        {
            var bb = new PdfArray();
            var i = 0;
            foreach (var n in bbEl.EnumerateArray())
            {
                if (i++ >= 4) break;
                bb.Add(new PdfReal(n.GetDouble()));
            }
            sd.Set("BBox", bb);
        }
        if (stateEl.TryGetProperty("Matrix", out var mxEl)
            && mxEl.ValueKind == System.Text.Json.JsonValueKind.Array && mxEl.GetArrayLength() >= 6)
        {
            var mx = new PdfArray();
            var i = 0;
            foreach (var n in mxEl.EnumerateArray())
            {
                if (i++ >= 6) break;
                mx.Add(new PdfReal(n.GetDouble()));
            }
            sd.Set("Matrix", mx);
        }

        // Rebuild /Resources/Font as Standard-14 entries keyed by the original
        // aliases (Helv, HeBo, ZaDb, ...) so the content's Tf operator resolves.
        var fontDict = new PdfDictionary();
        if (stateEl.TryGetProperty("Fonts", out var fontsEl)
            && fontsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var fEl in fontsEl.EnumerateArray())
            {
                if (fEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                var alias = fEl.GetString();
                if (string.IsNullOrEmpty(alias)) continue;
                var fontEntry = new PdfDictionary();
                fontEntry.Set("Type", new PdfName("Font"));
                fontEntry.Set("Subtype", new PdfName("Type1"));
                fontEntry.Set("BaseFont", new PdfName(MapStandardAlias(alias!)));
                fontDict.Set(alias!, fontEntry);
            }
        }
        var resources = new PdfDictionary();
        if (fontDict.Count > 0) resources.Set("Font", fontDict);
        sd.Set("Resources", resources);

        return new PdfStream(sd, bytes);
    }

    private static string MapStandardAlias(string alias) => alias switch
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

    private static string? MapFieldTypeToFt(string? fieldType) => fieldType switch
    {
        "Text" => "Tx",
        "Button" or "CheckBox" or "RadioButton" or "Radio" => "Btn",
        "Choice" or "ListBox" or "ComboBox" => "Ch",
        "Signature" => "Sig",
        _ => null,
    };

    /// <summary>The /Ff bits that mark a concrete button/choice subtype, so an
    /// imported field rebuilds as the right type (radio / push-button / combo).</summary>
    private static int FieldTypeFlags(string? fieldType) => fieldType switch
    {
        "RadioButton" or "Radio" => 1 << 15, // Radio
        "Button" => 1 << 16,                 // Pushbutton
        "ComboBox" => 1 << 17,               // Combo
        _ => 0,
    };

    private static PdfDictionary EnsureAcroForm(PdfReader reader)
    {
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null)
        {
            acroForm = new PdfDictionary();
            reader.Catalog.Set("AcroForm", acroForm);
        }
        return acroForm;
    }

    private static void AddToAcroFormFields(PdfReader reader, PdfDictionary fieldDict)
    {
        var acroForm = EnsureAcroForm(reader);
        var fields = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fields is null)
        {
            fields = new PdfArray();
            acroForm.Set("Fields", fields);
        }
        fields.Add(fieldDict);
    }

    /// <summary>Apply the form-level AcroForm data (/DA, /NeedAppearances, /DR) from
    /// a FieldExportingData AcroForm entry to the target document's AcroForm.</summary>
    private void ImportAcroFormData(System.Text.Json.JsonElement acro, PdfReader reader)
    {
        var acroForm = EnsureAcroForm(reader);
        if (acro.TryGetProperty("NeedAppearances", out var na)
            && (na.ValueKind == System.Text.Json.JsonValueKind.True || na.ValueKind == System.Text.Json.JsonValueKind.False))
            acroForm.Set("NeedAppearances", na.GetBoolean() ? PdfBoolean.True : PdfBoolean.False);
        if (acro.TryGetProperty("DefaultAppearance", out var da) && da.ValueKind == System.Text.Json.JsonValueKind.String)
            acroForm.Set("DA", new PdfString(Encoding.Latin1.GetBytes(da.GetString()!)));
        if (acro.TryGetProperty("DefaultResources", out var dr) && dr.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var drDict = new PdfDictionary();
            if (dr.TryGetProperty("Fonts", out var fonts) && fonts.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var fontDict = new PdfDictionary();
                foreach (var fn in fonts.EnumerateArray())
                    if (fn.ValueKind == System.Text.Json.JsonValueKind.String)
                        fontDict.Set(fn.GetString()!, new PdfDictionary());
                drDict.Set("Font", fontDict);
            }
            acroForm.Set("DR", drDict);
            DefaultResources = new Aspose.Pdf.Resources(drDict, reader);
        }
    }

    /// <summary>Read form-field values from a JSON file and apply them.</summary>
    public IEnumerable<FieldSerializationResult> ImportFromJson(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
        return ImportFromJson(fs);
    }

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
