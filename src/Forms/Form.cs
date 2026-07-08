using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// Represents the interactive form (AcroForm) of a PDF document.
/// </summary>
public sealed class Form : ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    private readonly List<Field> _fields;

    // Non-terminal (group/parent) field dicts surfaced into _fields by
    // CollectGroupFields so FindByName can resolve a group by its full name. They are
    // NOT terminal fields, so the public Fields array (and its leaf count) excludes
    // them — matching Aspose.Pdf, whose Form.Fields returns terminal fields only.
    private readonly HashSet<PdfDictionary> _groupFieldDicts = new();

    /// <summary>
    /// Returns the form fields as a snapshot array. Mirrors the
    /// Aspose.Pdf public signature (`Field[] Fields`), so callers
    /// can use .Length and array indexing. Only terminal fields are returned;
    /// intermediate group nodes (kept in <c>_fields</c> for name lookup) are excluded.
    /// </summary>
    public Field[] Fields
    {
        get
        {
            if (_groupFieldDicts.Count == 0) return _fields.ToArray();
            var terminals = new List<Field>(_fields.Count);
            foreach (var f in _fields)
                if (!_groupFieldDicts.Contains(f.Dict)) terminals.Add(f);
            return terminals.ToArray();
        }
    }

    private static Aspose.Pdf.Annotations.WidgetAnnotation Widgetize(Field f)
        => f;

    /// <summary>An empty form with no fields.</summary>
    internal static readonly Form Empty = new();

    /// <summary>
    /// When true, the form's NeedAppearances entry is suppressed so Acrobat won't
    /// rebuild widget appearances at open time. Stored flag — honored on save once
    /// the appearance regeneration pipeline lands.
    /// </summary>
    public bool IgnoreNeedsRendering { get; set; }

    /// <summary>When true, the form's flatten step adds a synthetic
    /// Required marker for XFA-style 'requiredGroups' whose
    /// at-least-one-required constraint is unmet. Stored only;
    /// required-group validation is not currently enforced. Note the
    /// 'Requierd' typo: it matches the public API spelling.</summary>
    public bool EmulateRequierdGroups { get; set; }

    private Form()
    {
        _fields = new List<Field>();
    }

    /// <summary>Construct a Form bound to <paramref name="document"/>.
    /// Parity for code that does
    /// <c>new Form(new Document())</c> before populating fields.</summary>
    public Form(Document document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        _fields = new List<Field>();
        OwnerDocument = document;
    }

    internal Form(PdfDictionary acroForm, PdfReader reader)
    {
        _reader = reader;
        _acroForm = acroForm;
        _fields = new List<Field>();
        var fieldsArray = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fieldsArray is not null)
        {
            var expandedGroups = new HashSet<PdfDictionary>();
            CollectFields(fieldsArray, reader, _fields, expandedGroups);
            CollectGroupFields(reader, _fields, expandedGroups, _groupFieldDicts);
        }
        // Surface the AcroForm default resources (/DR) so callers can read its fonts.
        var dr = reader.ResolveDict(acroForm.Get("DR"));
        if (dr is not null)
            DefaultResources = new Aspose.Pdf.Resources(dr, reader);
        // Propagate document ref after OwnerDocument is set (deferred)
    }

    /// <summary>Propagate OwnerDocument to all fields.</summary>
    private void PropagateOwnerToFields()
    {
        if (OwnerDocument is null) return;
        foreach (var field in _fields)
            field.OwnerDocument = OwnerDocument;
    }

    /// <summary>
    /// Fallback: build a form by scanning all page Widget annotations.
    /// Used when there is no /AcroForm in the catalog (PDF has widgets but no AcroForm).
    /// </summary>
    internal static Form FromPageWidgets(PdfDictionary catalog, PdfReader reader)
    {
        var list = new List<Field>();
        var pagesDict = reader.ResolveDict(catalog.Get("Pages"));
        if (pagesDict is null)
            return Empty;

        CollectWidgetsFromPagesNode(pagesDict, reader, list);
        // These fields were reconstructed from page widgets (the AcroForm /Fields
        // was missing/empty), so AutoRestoreForm=false can later drop them.
        return list.Count > 0 ? new Form(list) { _autoRestored = true } : Empty;
    }

    /// <summary>
    /// Build a fresh, per-document empty Form. Used by Document.Form when
    /// there is no AcroForm and no page widgets, so that Form.Add later
    /// mutates this document's private list — not the shared Empty
    /// singleton (which would bleed field counts across documents).
    /// </summary>
    internal static Form CreateEmptyForDocument(PdfReader reader) => new(new PdfDictionary(), reader);

    private Form(List<Field> fields)
    {
        _fields = fields;
    }

    private static void CollectWidgetsFromPagesNode(
        PdfDictionary node, PdfReader reader, List<Field> result)
    {
        var type = node.GetName("Type");
        if (type == "Page")
        {
            var annotsObj = reader.Resolve(node.Get("Annots")) as PdfArray;
            if (annotsObj is null) return;
            foreach (var annotRef in annotsObj)
            {
                var annot = reader.ResolveDict(annotRef);
                if (annot is null) continue;
                if (annot.GetName("Subtype") != "Widget") continue;
                if (!annot.ContainsKey("T") && !annot.ContainsKey("FT")) continue;
                result.Add(Field.Create(annot, reader));
            }
            return;
        }

        // Pages node — recurse into kids
        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) return;
        foreach (var kid in kids)
        {
            var kidDict = reader.ResolveDict(kid);
            if (kidDict is not null)
                CollectWidgetsFromPagesNode(kidDict, reader, result);
        }
    }

    /// <summary>
    /// Number of ROOT form fields — the entries of the AcroForm <c>/Fields</c> array
    /// (fields whose field-<c>/Parent</c> is null). A container/subform field (e.g. an
    /// XFA subform tree) or a radio-button group counts as a SINGLE field here; its
    /// descendants are not walked. This is a different view from <see cref="Fields"/>,
    /// which flattens the tree to terminal fields (and splits a radio group into its
    /// option fields). Falls back to the flattened count for forms with no AcroForm
    /// <c>/Fields</c> array (e.g. reconstructed from page widgets).
    /// </summary>
    public int Count
    {
        get
        {
            var reader = _reader ?? OwnerDocument?.Reader;
            if (reader is not null)
            {
                // Use the AcroForm this Form represents; fall back to the live catalog
                // AcroForm only when ours carries no /Fields (e.g. an initially-empty form
                // whose AcroForm was created later by Add()).
                var fieldsArray = _acroForm is not null
                    ? reader.Resolve(_acroForm.Get("Fields")) as PdfArray
                    : null;
                fieldsArray ??= reader.Resolve(
                    reader.ResolveDict(reader.Catalog?.Get("AcroForm"))?.Get("Fields")) as PdfArray;
                if (fieldsArray is not null)
                {
                    var n = 0;
                    foreach (var item in fieldsArray)
                        if (reader.ResolveDict(item) is not null) n++;
                    return n;
                }
            }
            return _fields.Count;
        }
    }
    /// <summary>Get the widget annotation for the form field at the 1-based index.</summary>
    public Aspose.Pdf.Annotations.WidgetAnnotation this[int index] => Widgetize(_fields[index - 1]);

    /// <summary>Append to the tracked field list any AcroForm /Fields entries that
    /// aren't represented yet. Used after a field is added by mutating the raw
    /// AcroForm dictionary directly (e.g. <c>FormFieldBuilder</c> via the facade
    /// <c>FormEditor.AddField</c>): unlike rebuilding the Form, this preserves the
    /// in-memory state (e.g. unsaved values set by FillField) of fields already
    /// tracked, while making the newly added ones visible to FindByName/GetFieldType.</summary>
    internal void SyncNewlyAddedFields()
    {
        if (_reader is null || _acroForm is null) return;
        if (_reader.Resolve(_acroForm.Get("Fields")) is not PdfArray fieldsArray) return;

        var known = new HashSet<PdfDictionary>();
        foreach (var f in _fields) known.Add(f.Dict);

        foreach (var item in fieldsArray)
        {
            var dict = _reader.ResolveDict(item);
            if (dict is null || known.Contains(dict)) continue;
            var field = Field.Create(dict, _reader);
            field.OwnerDocument = OwnerDocument;
            _fields.Add(field);
        }
    }

    /// <summary>Optional back-reference to the owning document (set by Document.Form getter).</summary>
    private Document? _ownerDocument;
    internal Document? OwnerDocument
    {
        get => _ownerDocument;
        set
        {
            _ownerDocument = value;
            PropagateOwnerToFields();
        }
    }

    /// <summary>
    /// Delete a form field by name.
    /// Removes every field with the matching fully-qualified name.
    /// </summary>
    public void Delete(string fieldName)
    {
        OwnerDocument?.RemoveFormField(fieldName);
    }

    /// <summary>
    /// Add a field to the form on the specified page.
    /// <summary>Add a field to the document's AcroForm and bind its widget
    /// to page 1 (the most common case). Use the (Field, int) overload to
    /// place the widget on a different page.</summary>
    public void Add(Field field) => Add(field, 1);

    /// </summary>
    /// <param name="field">The field to add.</param>
    /// <param name="pageNumber">1-based page number.</param>
    public void Add(Field field, int pageNumber)
    {
        var pageIndex = pageNumber;
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null)
            throw new InvalidOperationException("Cannot add fields to an empty form.");

        var catalog = reader.Catalog;

        // Ensure AcroForm exists
        var acroForm = reader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null)
        {
            acroForm = new PdfDictionary();
            catalog.Set("AcroForm", acroForm);
        }

        // Ensure /Fields array exists
        var fieldsArray = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fieldsArray is null)
        {
            fieldsArray = new PdfArray();
            acroForm.Set("Fields", fieldsArray);
        }

        // Add field dict to AcroForm /Fields
        fieldsArray.Add(field.Dict);

        // Disambiguate the field's name against the fields already on the form. An
        // unnamed field is auto-named off the "field_" base; a name that collides
        // with an existing field is suffixed. Checkboxes/radios that share a base
        // name form a group suffixed "#0","#1",… (0-based, the first bare member is
        // retroactively renamed to "#0"); every other collision (text fields, or a
        // button colliding with a non-button) appends a 1-based numeric suffix.
        field.PartialName = DisambiguateFieldName(field, fieldsArray, reader);

        // Set NeedAppearances so viewers generate appearances
        acroForm.Set("NeedAppearances", PdfBoolean.True);

        // Ensure default resources include Helvetica
        EnsureDefaultResources(acroForm);

        // Embed a custom font set via the field's DefaultAppearance into the form's
        // /DR as a composite (Type0) face, and point the field's /DA at it.
        EmbedDefaultAppearanceFont(field);

        // Generate a default appearance for freshly-created fields so the /AP is
        // present without relying on a viewer's NeedAppearances pass. Each field
        // type draws its own representation (value text, list value, radio disc,
        // button caption); the call is a no-op when an /AP already exists. Done
        // before page placement so the widget dictionaries below carry their /AP.
        field.GenerateAppearance();

        // Add the field's widget annotation(s) to the page's /Annots array.
        PlaceFieldWidgets(field.Dict, reader, pageIndex);

        // Add to internal fields list
        _fields.Add(field);

        // Back-reference the owner so the field's own operations (e.g. Sign)
        // can reach the document.
        if (OwnerDocument is not null) field.OwnerDocument = OwnerDocument;

        // Mark AcroForm and page dicts dirty so that incremental save persists the
        // /Fields and /Annots mutations. Without this, a document opened from a
        // writable stream takes the incremental path and drops the new field.
        var doc = OwnerDocument;
        if (doc is not null)
        {
            var acroFormObjNum = doc.FindObjectNumber(acroForm);
            if (acroFormObjNum > 0)
                doc.MarkDirty(acroFormObjNum, acroForm);

            var dirtyPages = OwnerDocument?.Pages ?? new PageCollection(reader);
            if (pageIndex >= 1 && pageIndex <= dirtyPages.Count)
            {
                var pageDict = dirtyPages[pageIndex].Dict;
                var pageObjNum = doc.FindObjectNumber(pageDict);
                if (pageObjNum > 0)
                    doc.MarkDirty(pageObjNum, pageDict);
            }
        }
    }

    /// <summary>Compute the disambiguated /T name for a field being added, against the
    /// names already present in <paramref name="fieldsArray"/> (the field's own dict is
    /// skipped). Reproduces the reference numbering: an unnamed field is based on
    /// "field_"; checkboxes/radios sharing a base form a "#N" group (0-based, the bare
    /// first member is retroactively renamed to "#N0"); all other collisions append a
    /// 1-based decimal. Returns the name to assign; performs the retroactive rename as a
    /// side effect when a button group forms.</summary>
    private string DisambiguateFieldName(Field field, PdfArray fieldsArray, PdfReader reader)
    {
        var empty = string.IsNullOrEmpty(field.PartialName);
        var baseName = empty ? "field_" : field.PartialName!;
        var isButton = field is CheckboxField || field is RadioButtonField;

        // Existing top-level names (excluding the field being added) with their dicts
        // and a button flag (a /FT of /Btn, inherited where absent on the kid).
        var names = new List<(string name, bool isButton, PdfDictionary dict)>();
        foreach (var item in fieldsArray)
        {
            var d = reader.ResolveDict(item);
            if (d is null || ReferenceEquals(d, field.Dict)) continue;
            if ((d.Get("T") as PdfString)?.ToText() is not { } t) continue;
            var ft = d.GetName("FT");
            names.Add((t, ft == "Btn", d));
        }

        var indexed = new Regex("^" + Regex.Escape(baseName) + @"#\d+$");
        bool Taken(string n) { foreach (var e in names) if (e.name == n) return true; return false; }
        bool HasButtonGroup() { foreach (var e in names) if (e.isButton && (e.name == baseName || indexed.IsMatch(e.name))) return true; return false; }
        bool BaseInUse() { if (Taken(baseName)) return true; foreach (var e in names) if (indexed.IsMatch(e.name)) return true; return false; }
        int CountIndexed() { var c = 0; foreach (var e in names) if (indexed.IsMatch(e.name)) c++; return c; }
        var baseHasIndexSuffix = Regex.IsMatch(baseName, @"#\d+$");

        if (isButton && (HasButtonGroup() || empty))
        {
            // Checkbox/radio "#N" group. Retroactively rename a bare same-base button
            // (one with no "#N" tail of its own) to "<base>#0" so the whole group is
            // suffixed; a base that already carries a "#N" tail is left as-is.
            var didRename = false;
            if (!baseHasIndexSuffix)
            {
                foreach (var e in names)
                {
                    if (e.isButton && e.name == baseName)
                    {
                        e.dict.Set("T", new PdfString(Encoding.Latin1.GetBytes(baseName + "#0")));
                        var num = OwnerDocument?.FindObjectNumber(e.dict) ?? -1;
                        if (num > 0) OwnerDocument!.MarkDirty(num, e.dict);
                        didRename = true;
                        break;
                    }
                }
            }
            // Index = existing "#N" members, plus the one we just renamed to "#0"
            // (the in-memory name list still reflects its pre-rename name).
            return baseName + "#" + (CountIndexed() + (didRename ? 1 : 0));
        }

        // Bare name when there is no collision at all (named fields only).
        if (!empty && !BaseInUse()) return baseName;

        // Numeric (1-based) append for every other collision.
        var k = 1;
        while (Taken(baseName + k)) k++;
        return baseName + k;
    }

    /// <summary>Add a field's widget annotation(s) to the 1-based target page's
    /// /Annots array. Uses the owning document's page collection — a freshly-built
    /// PageCollection(reader) doesn't see pages added via Document.Pages.Add() on a
    /// new document (the catalog page tree isn't rewritten until save), so the
    /// widget would never land on the page that actually renders. No-op when the
    /// page index is out of range.</summary>
    private void PlaceFieldWidgets(PdfDictionary fieldDict, PdfReader reader, int pageIndex)
    {
        var pages = OwnerDocument?.Pages ?? new PageCollection(reader);
        if (pageIndex < 1 || pageIndex > pages.Count) return;

        PdfArray AnnotsFor(int idx)
        {
            var pd = pages[idx].Dict;
            if (pd.Get("Annots") is PdfArray a) return a;
            var na = new PdfArray();
            pd.Set("Annots", na);
            return na;
        }

        foreach (var widget in CollectWidgetDicts(fieldDict, reader))
        {
            // A widget may carry a per-option page hint (CheckboxField.AddOption with
            // an explicit page); route it there, otherwise use the field's page.
            int target = pageIndex;
            if (reader.Resolve(widget.Get("_PlacePage")) is PdfInteger pp
                && pp.Value >= 1 && pp.Value <= pages.Count)
            {
                target = (int)pp.Value;
                widget.Remove("_PlacePage");
            }
            AnnotsFor(target).Add(widget);
            var pageObjNum = OwnerDocument?.FindObjectNumber(pages[target].Dict) ?? -1;
            if (pageObjNum > 0) OwnerDocument!.MarkDirty(pageObjNum, pages[target].Dict);
        }
    }

    /// <summary>The widget annotation dictionaries that a field contributes to a
    /// page's /Annots. A field whose own dict is the widget (single-widget text,
    /// checkbox, …) places that dict directly; a field with kid widgets (radio
    /// groups, multi-widget fields) places each kid that carries a /Rect.</summary>
    private static System.Collections.Generic.IEnumerable<PdfDictionary> CollectWidgetDicts(
        PdfDictionary fieldDict, PdfReader reader)
    {
        var widgets = new System.Collections.Generic.List<PdfDictionary>();
        if (reader.Resolve(fieldDict.Get("Kids")) is PdfArray kids && kids.Count > 0)
        {
            // A field that carries its own /Rect alongside /Kids is a merged self-widget
            // (multi-widget text field): the field dict is itself the first visual widget,
            // so it must be placed in /Annots too — not just the kids.
            if (reader.Resolve(fieldDict.Get("Rect")) is PdfArray)
                widgets.Add(fieldDict);
            foreach (var k in kids)
                if (reader.Resolve(k) is PdfDictionary kid &&
                    reader.Resolve(kid.Get("Rect")) is PdfArray)
                    widgets.Add(kid);
        }
        if (widgets.Count == 0)
            widgets.Add(fieldDict);
        return widgets;
    }

    private static void EnsureDefaultResources(PdfDictionary acroForm)
    {
        var dr = acroForm.Get("DR") as PdfDictionary;
        if (dr is null)
        {
            dr = new PdfDictionary();
            acroForm.Set("DR", dr);
        }

        var fontDict = dr.Get("Font") as PdfDictionary;
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

    /// <summary>Embed the embeddable font carried by a field's
    /// <see cref="Field.DefaultAppearance"/> into the AcroForm /DR as a composite
    /// (Type0) face under a generated <c>C{n}_0</c> resource name, repoint the
    /// field's /DA at it, and record the name on the DefaultAppearance. No-op when
    /// the field carries no embeddable default-appearance font.</summary>
    /// <summary>Embed a field's default-appearance font (when embeddable) into the
    /// form's /DR. Resolves the AcroForm from the field's reader, so it can be
    /// invoked both during <see cref="Add(Field,int)"/> (font set before the field
    /// was added) and from the DefaultAppearance setter (font set afterwards). A
    /// no-op when there is no AcroForm /DR yet, or no embeddable font.</summary>
    internal static void EmbedDefaultAppearanceFont(Field field)
    {
        var da = field.DefaultAppearance;
        if (da?.EmbeddedFont is not { } font || !font.IsEmbedded) return;
        var ttf = font.SourceFontData?.TtfData;
        if (ttf is null || ttf.Length == 0) return;

        var reader = field.Reader;
        PdfDictionary? acroForm;
        try { acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm")); }
        catch (InvalidOperationException) { return; } // detached field, no trailer
        if (acroForm is null) return;

        var dr = reader.ResolveDict(acroForm.Get("DR"));
        var fontDict = dr is null ? null : reader.ResolveDict(dr.Get("Font"));
        if (fontDict is null) return;

        // Already embedded for this appearance — don't add a duplicate.
        if (da.FontResourceName.Length > 1 && da.FontResourceName[0] == 'C'
            && da.FontResourceName.Contains('_') && fontDict.ContainsKey(da.FontResourceName))
            return;

        // Composite fonts are named C{n}_0 (matching the Aspose.Pdf convention).
        var n = 0;
        foreach (var key in fontDict.Keys)
            if (key.Length > 1 && key[0] == 'C' && key.Contains('_')) n++;
        var resName = $"C{n}_0";

        fontDict.Set(resName, BuildType0Font(ttf, font.BaseFont ?? font.FontName));
        da.FontResourceName = resName;

        var c = da.TextColor;
        // Colour components: whole values as integers ("1"/"0"), fractions at full precision
        // (128/255 -> "0.5019607843") like the reference, not a fixed 3-decimal rounding.
        static string Cc(double v) => v.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
        var daStr = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "/{0} {1:G} Tf {2} {3} {4} rg",
            resName, da.FontSize, Cc(c.R / 255.0), Cc(c.G / 255.0), Cc(c.B / 255.0));
        field.Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(daStr)));
    }

    /// <summary>Build a composite (Type0/CIDFontType2, Identity-H) font dictionary
    /// that embeds the supplied TrueType data as /FontFile2.</summary>
    private static PdfDictionary BuildType0Font(byte[] ttf, string baseFontName)
    {
        var psName = (baseFontName ?? "EmbeddedFont").Replace(" ", "");

        var descriptor = new PdfDictionary();
        descriptor.Set("Type", new PdfName("FontDescriptor"));
        descriptor.Set("FontName", new PdfName(psName));
        descriptor.Set("Flags", new PdfInteger(4)); // Symbolic
        descriptor.Set("Ascent", new PdfInteger(750));
        descriptor.Set("Descent", new PdfInteger(-250));
        descriptor.Set("ItalicAngle", new PdfInteger(0));
        descriptor.Set("CapHeight", new PdfInteger(700));
        descriptor.Set("StemV", new PdfInteger(80));
        var bbox = new PdfArray();
        bbox.Add(new PdfInteger(-200)); bbox.Add(new PdfInteger(-250));
        bbox.Add(new PdfInteger(1000)); bbox.Add(new PdfInteger(900));
        descriptor.Set("FontBBox", bbox);
        var fontFile = new PdfStream(new PdfDictionary(), ttf);
        fontFile.Dict.Set("Length1", new PdfInteger(ttf.Length));
        descriptor.Set("FontFile2", fontFile);

        var cidFont = new PdfDictionary();
        cidFont.Set("Type", new PdfName("Font"));
        cidFont.Set("Subtype", new PdfName("CIDFontType2"));
        cidFont.Set("BaseFont", new PdfName(psName));
        var sysInfo = new PdfDictionary();
        sysInfo.Set("Registry", new PdfString(System.Text.Encoding.ASCII.GetBytes("Adobe")));
        sysInfo.Set("Ordering", new PdfString(System.Text.Encoding.ASCII.GetBytes("Identity")));
        sysInfo.Set("Supplement", new PdfInteger(0));
        cidFont.Set("CIDSystemInfo", sysInfo);
        cidFont.Set("FontDescriptor", descriptor);
        cidFont.Set("DW", new PdfInteger(1000));
        cidFont.Set("CIDToGIDMap", new PdfName("Identity"));

        var type0 = new PdfDictionary();
        type0.Set("Type", new PdfName("Font"));
        type0.Set("Subtype", new PdfName("Type0"));
        type0.Set("BaseFont", new PdfName(psName));
        type0.Set("Encoding", new PdfName("Identity-H"));
        var descendants = new PdfArray();
        descendants.Add(cidFont);
        type0.Set("DescendantFonts", descendants);
        return type0;
    }

    /// <summary>
    /// All radio-button groups in the form.
    /// Radio buttons that share the same full field name are combined into one
    /// <see cref="RadioButtonGroup"/>.
    /// </summary>
    public IReadOnlyList<RadioButtonGroup> RadioGroups
    {
        get
        {
            var map = new Dictionary<string, List<RadioButtonField>>();
            foreach (var field in _fields)
            {
                if (field is not RadioButtonField rbf) continue;
                var key = rbf.FullName ?? rbf.PartialName ?? "";
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<RadioButtonField>();
                    map[key] = list;
                }
                list.Add(rbf);
            }
            return map.Values.Select(list => new RadioButtonGroup(list)).ToList();
        }
    }

    /// <summary>
    /// Get the widget annotation for the form field with the given full name.
    /// Throws ArgumentException if not found.
    /// </summary>
    public Aspose.Pdf.Annotations.WidgetAnnotation this[string fullName] =>
        Widgetize(FindByName(fullName) ?? throw new ArgumentException(
            $"Form field not found : {fullName}"));

    /// <summary>Look up a field by name; returns null when not found.
    /// camelCase 'findField' alias for the public surface.</summary>
    public Field? findField(string fullName) => FindByName(fullName);

    /// <summary>PascalCase alias of <see cref="findField"/>.</summary>
    public Field? FindField(string fullName) => FindByName(fullName);

    /// <summary>True if any signature field with a non-null /V signature
    /// dictionary exists in this form.</summary>
    public bool SignaturesExist
    {
        get
        {
            if (_signaturesExistOverride.HasValue) return _signaturesExistOverride.Value;
            foreach (var f in _fields)
            {
                if (f.Type == FieldType.Signature && f.Dict.ContainsKey("V"))
                    return true;
            }
            return false;
        }
        set => _signaturesExistOverride = value;
    }
    private bool? _signaturesExistOverride;

    /// <summary>
    /// Export all field values as a dictionary (field name → value).
    /// </summary>
    public Dictionary<string, string?> ToObject()
    {
        var result = new Dictionary<string, string?>();
        foreach (var field in _fields)
        {
            var name = field.FullName ?? field.PartialName;
            if (name is not null)
                result[name] = field.Value;
        }
        return result;
    }

    /// <summary>
    /// True if a field with the given full name exists in the AcroForm.
    /// For XFA paths (containing brackets), also accepts XFA-style names.
    /// </summary>
    public bool HasField(string fieldName) => FindByName(fieldName) is not null;

    /// <summary>
    /// True if a field with the given name exists. When <paramref name="searchChildren"/>
    /// is true and the form is XFA-backed, also searches the XFA template's field
    /// dictionary for names that exist only in the template (no AcroForm twin).
    /// </summary>
    public bool HasField(string fieldName, bool searchChildren)
    {
        if (HasField(fieldName)) return true;
        if (!searchChildren || !IsXfa) return false;
        foreach (var name in GetXfaFieldNames())
            if (string.Equals(name, fieldName, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// Find a field by its full name, or by XFA path for static XFA forms.
    /// </summary>
    public Field? FindByName(string fullName)
    {
        // A null/empty request (e.g. GetFieldType on a non-field annotation whose FullName
        // is null) matches no field — return null rather than dereferencing it downstream.
        if (string.IsNullOrEmpty(fullName)) return null;
        // 1. Named radio group reconstruction. A radio group is read back as its
        // individual option widgets (each /Parent → the group dict); when the group
        // itself is named, surface it as one RadioButtonField so callers can look it
        // up by name and read its options. This takes priority over the direct match
        // below because an option widget inherits the group's full name, and a caller
        // asking for that name wants the group (with its /Opt), not a single widget.
        var rdr = _reader ?? OwnerDocument?.Reader;
        if (rdr is not null)
            foreach (var field in _fields)
            {
                var parentRef = field.Dict.Get("Parent");
                var parent = rdr.ResolveDict(parentRef);
                if (parent is null || parent.GetName("FT") != "Btn") continue;
                var ff = parent.ContainsKey("Ff") ? parent.GetInt("Ff") : 0;
                if ((ff & (1 << 15)) == 0) continue; // not a radio group
                var group = new RadioButtonField(parent, rdr);
                // Carry the owning document and the group's real object number (the
                // kid's /Parent points at the group's indirect object) so value changes
                // on the reconstructed group can be marked dirty for incremental save.
                group.OwnerDocument = OwnerDocument;
                if (parentRef is PdfIndirectRef pref) group.ObjectNumber = pref.ObjectNumber;
                if (string.Equals(group.FullName, fullName, StringComparison.Ordinal))
                    return group;
            }

        // 1a. Direct match by AcroForm full name
        foreach (var field in _fields)
        {
            if (string.Equals(field.FullName, fullName, StringComparison.Ordinal))
                return field;
        }

        // 1b. Group/parent prefix match — if fullName is a parent path,
        // return the first child field whose name starts with it
        var dotPrefix = fullName + ".";
        foreach (var field in _fields)
        {
            if (field.FullName?.StartsWith(dotPrefix, StringComparison.Ordinal) == true)
                return field;
        }

        // 1c. Anonymous-container–insensitive match. An XFA SOM address omits
        // unnamed container segments, so the test/caller asks for
        // "form1[0].TextField1[0]" while the fully-qualified AcroForm name is
        // "form1[0].#subform[0].TextField1[0]". Compare both names with their
        // "#..."-segments stripped so either spelling resolves to the same field.
        // Runs after the exact (1a) and prefix (1b) matches so literal names win.
        var canonicalRequest = StripAnonymousContainers(fullName);
        foreach (var field in _fields)
        {
            var fn = field.FullName;
            if (fn is null) continue;
            if (string.Equals(StripAnonymousContainers(fn), canonicalRequest, StringComparison.Ordinal))
                return field;
        }

        // 2. XFA path resolution fallback — try for any path with bracket indices
        if (fullName.Contains('['))
        {
            var mapping = GetXfaPathMapping();
            if (mapping.TryGetValue(fullName, out var acroFieldName))
            {
                foreach (var field in _fields)
                {
                    if (string.Equals(field.FullName, acroFieldName, StringComparison.Ordinal))
                        return field;
                }
            }

            // 3. Group node prefix match: if the requested path is a non-terminal
            // group (subform), return the first child field whose XFA path starts with it.
            var groupPrefix = fullName + ".";
            foreach (var kvp in mapping)
            {
                if (kvp.Key.StartsWith(groupPrefix, StringComparison.Ordinal))
                {
                    foreach (var field in _fields)
                    {
                        if (string.Equals(field.FullName, kvp.Value, StringComparison.Ordinal))
                            return field;
                    }
                }
            }

            // 4. Strip [N] indices and try matching as dotted AcroForm name
            var stripped = System.Text.RegularExpressions.Regex.Replace(fullName, @"\[\d+\]", "");
            foreach (var field in _fields)
            {
                if (string.Equals(field.FullName, stripped, StringComparison.Ordinal))
                    return field;
                // Also check if field's full name starts with the stripped path (group node)
                if (field.FullName?.StartsWith(stripped + ".", StringComparison.Ordinal) == true)
                    return field;
            }

            // 5. Last-segment fallback: match last path segment (without index) to partial name
            var lastDot = fullName.LastIndexOf('.');
            var lastSegment = lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;
            // Strip [N] index from segment
            var bracketIdx = lastSegment.IndexOf('[');
            if (bracketIdx >= 0)
                lastSegment = lastSegment.Substring(0, bracketIdx);

            foreach (var field in _fields)
            {
                if (string.Equals(field.PartialName, lastSegment, StringComparison.Ordinal))
                    return field;
                if (string.Equals(field.FullName, lastSegment, StringComparison.Ordinal))
                    return field;
            }
        }

        // 6. Bracket-stripped XFA-style match (no '[' in input but fields have '[N]')
        // Strip [N] indices from each stored FullName/PartialName and compare.
        // Only return a hit if exactly one field's stripped name matches.
        if (!fullName.Contains('['))
        {
            static string StripIdx(string s) =>
                System.Text.RegularExpressions.Regex.Replace(s, @"\[\d+\]", "");

            Field? unique = null;
            foreach (var field in _fields)
            {
                if (field.FullName is null) continue;
                if (string.Equals(StripIdx(field.FullName), fullName, StringComparison.Ordinal))
                {
                    if (unique is not null) { unique = null; break; }
                    unique = field;
                }
            }
            if (unique is not null) return unique;

            // 7. Last-segment fallback for non-bracket inputs (Aspose.Pdf behaviour)
            var lastDot2 = fullName.LastIndexOf('.');
            var leaf = lastDot2 >= 0 ? fullName.Substring(lastDot2 + 1) : fullName;
            Field? leafMatch = null;
            foreach (var field in _fields)
            {
                var partial = field.PartialName is null ? null : StripIdx(field.PartialName);
                if (string.Equals(partial, leaf, StringComparison.Ordinal))
                {
                    if (leafMatch is not null) { leafMatch = null; break; }
                    leafMatch = field;
                }
            }
            if (leafMatch is not null) return leafMatch;
        }

        return null;
    }

    /// <summary>
    /// Remove anonymous XFA container segments ("#subform[0]", "#area[1]",
    /// "#exclGroup[0]", …) from a dotted field path. XFA SOM addresses omit
    /// these unnamed containers, whereas the fully-qualified AcroForm field
    /// name includes them. A segment is anonymous when it begins with '#'.
    /// </summary>
    private static string StripAnonymousContainers(string dottedName)
    {
        if (string.IsNullOrEmpty(dottedName) || dottedName.IndexOf('#') < 0) return dottedName;
        var parts = dottedName.Split('.');
        var kept = new List<string>(parts.Length);
        foreach (var p in parts)
            if (p.Length == 0 || p[0] != '#')
                kept.Add(p);
        return string.Join(".", kept);
    }

    /// <summary>
    /// Split an XFA SOM (Scripting Object Model) path into its segments on the
    /// <b>unescaped</b> '.' separators, un-escaping any <c>\.</c> inside a segment
    /// back to a literal '.'. XFA field (and AcroForm /T) names may legitimately
    /// contain a '.' — e.g. a leaf named <c>SRC.C_ACTION</c> — which the SOM syntax
    /// writes escaped as <c>SRC\.C_ACTION</c>. A naive <c>Split('.')</c> would split
    /// such a leaf into two bogus segments. For backward compatibility (and speed) a
    /// path with no backslash falls straight through to <c>Split('.')</c>.
    /// </summary>
    internal static string[] SplitSomPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path is null ? Array.Empty<string>() : new[] { path };
        if (path.IndexOf('\\') < 0) return path.Split('.');
        var segs = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];
            if (c == '\\' && i + 1 < path.Length && path[i + 1] == '.')
            {
                sb.Append('.');
                i++; // consume the escaped dot
            }
            else if (c == '.')
            {
                segs.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        segs.Add(sb.ToString());
        return segs.ToArray();
    }

    /// <summary>Escape a single SOM path segment (a leaf/subform name) so that a
    /// literal '.' inside it round-trips as <c>\.</c> when the segment is joined
    /// into a dotted SOM path. Mirrors <see cref="SplitSomPath"/>.</summary>
    internal static string EscapeSomSegment(string segment)
        => string.IsNullOrEmpty(segment) || segment.IndexOf('.') < 0
            ? segment
            : segment.Replace(".", "\\.");

    private Dictionary<string, string>? _xfaPathMap;

    /// <summary>
    /// Build a mapping from XFA template paths to AcroForm field names.
    /// Parses the XFA template XML and walks the subform/field hierarchy.
    /// </summary>
    private Dictionary<string, string> GetXfaPathMapping()
    {
        if (_xfaPathMap is not null) return _xfaPathMap;
        _xfaPathMap = new Dictionary<string, string>(StringComparer.Ordinal);

        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return _xfaPathMap;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is not null)
            {
                WalkXfaTemplate(doc.DocumentElement, "", _xfaPathMap);
            }
        }
        catch { /* malformed template — return empty map */ }

        return _xfaPathMap;
    }

    /// <summary>
    /// Recursively walk XFA template nodes to build path-to-field-name mapping.
    /// Subform nodes build up the path; field/draw/exclGroup nodes are leaf entries.
    /// </summary>
    private static void WalkXfaTemplate(XmlNode node, string parentPath, Dictionary<string, string> map)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;

            var localName = child.LocalName;
            var nameAttr = child.Attributes?["name"]?.Value;

            if (localName == "subform" || localName == "subformSet")
            {
                if (nameAttr is not null)
                {
                    // Count same-named siblings at this level to determine index
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var currentPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";
                    WalkXfaTemplate(child, currentPath, map);
                }
                else
                {
                    // Unnamed subform — pass through parent path
                    WalkXfaTemplate(child, parentPath, map);
                }
            }
            else if (localName == "field" || localName == "draw" || localName == "exclGroup")
            {
                if (nameAttr is not null)
                {
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var fieldPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";

                    // Map XFA path -> AcroForm field name (partial name). The value is
                    // matched against Field.FullName, which the AcroForm side escapes too.
                    if (!map.ContainsKey(fieldPath))
                        map[fieldPath] = escName;
                }

                // exclGroup can contain fields (radio buttons)
                if (localName == "exclGroup")
                    WalkXfaTemplate(child, parentPath, map);
            }
            else
            {
                // Other elements might contain subforms/fields — recurse
                WalkXfaTemplate(child, parentPath, map);
            }
        }
    }

    /// <summary>
    /// Count preceding siblings with the same local name and name attribute.
    /// </summary>
    private static int CountPrecedingSiblings(XmlNode node, string localName, string nameAttr)
    {
        int count = 0;
        var sibling = node.PreviousSibling;
        while (sibling is not null)
        {
            if (sibling.NodeType == XmlNodeType.Element &&
                sibling.LocalName == localName &&
                sibling.Attributes?["name"]?.Value == nameAttr)
            {
                count++;
            }
            sibling = sibling.PreviousSibling;
        }
        return count;
    }

    /// <summary>The form type (Standard, Static, Dynamic).</summary>
    public FormType Type
    {
        get
        {
            if (!IsXfa) return FormType.Standard;
            // Detect Static vs Dynamic from the config packet.
            // Dynamic XFA uses client-side rendering (<renderPolicy>client</renderPolicy>).
            var (_, configXml) = GetXfaPart("config");
            if (configXml is not null &&
                configXml.Contains("<renderPolicy") && configXml.Contains("client"))
                return FormType.Dynamic;
            // Fallback: check template for dynamicRender
            var templateXml = GetXfaTemplateXml();
            if (templateXml is not null && templateXml.Contains("dynamicRender"))
                return FormType.Dynamic;
            return FormType.Static;
        }
        set
        {
            if (value == FormType.Standard || value == FormType.Static)
            {
                FlattenXfa();
            }
        }
    }

    internal string? GetXfaTemplateXml()
    {
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null) return null;
        var catalog = reader.Catalog;
        var acroForm = reader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null) return null;
        var xfaObj = reader.Resolve(acroForm.Get("XFA"));
        if (xfaObj is PdfArray arr)
        {
            for (int i = 0; i < arr.Count - 1; i += 2)
            {
                if (arr[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == "template")
                {
                    var stream = reader.Resolve(arr[i + 1]) as PdfStream;
                    if (stream is not null)
                        return Encoding.UTF8.GetString(reader.DecodeStream(stream));
                }
            }
        }
        return null;
    }

    /// <summary>Replace the /XFA template part with the given XML. Used by
    /// Document.Flatten(FlattenSettings) to mark fields hidden before flatten.
    /// Drops the stream's /Filter entry and rewrites /Length so the new
    /// uncompressed bytes can be re-read after save without going through
    /// a decoder.</summary>
    internal void SetXfaTemplateXml(string xml)
    {
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null) return;
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return;
        var xfaObj = reader.Resolve(acroForm.Get("XFA"));
        if (xfaObj is PdfArray arr)
        {
            for (int i = 0; i < arr.Count - 1; i += 2)
            {
                if (arr[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == "template")
                {
                    if (reader.Resolve(arr[i + 1]) is PdfStream stream)
                    {
                        var bytes = Encoding.UTF8.GetBytes(xml);
                        stream.ReplaceData(bytes);
                        stream.Dict.Remove("Filter");
                        stream.Dict.Remove("DecodeParms");
                        stream.Dict.Set("Length", new PdfInteger(bytes.Length));
                    }
                    return;
                }
            }
        }
    }

    /// <summary>Write <paramref name="url"/> into the XFA template's
    /// <c>&lt;submit target&gt;</c> for the named button field and persist it back
    /// into the /XFA template stream. Returns false when the form has no XFA
    /// template, the field/submit node can't be located, or the write fails.
    /// Mirrors the AcroForm SubmitForm /F update so both stay in sync.</summary>
    internal bool SetXfaSubmitUrl(string fieldName, string url)
    {
        var xml = GetXfaTemplateXml();
        if (xml is null) return false;
        XmlDocument doc = new();
        try { doc.LoadXml(xml); } catch { return false; }
        if (doc.DocumentElement is null) return false;

        // Locate the field's template node by walking the leaf-name segments
        // (template nodes are named by leaf name only, no dotted path / [n] index).
        XmlNode current = doc.DocumentElement;
        foreach (var rawSeg in SplitSomPath(fieldName))
        {
            var seg = System.Text.RegularExpressions.Regex.Replace(rawSeg, @"\[\d+\]$", "");
            var next = FindNamedTemplateNode(current, seg);
            if (next is null) return false;
            current = next;
        }
        if (ReferenceEquals(current, doc.DocumentElement)) return false;

        // The <submit> element (xfa-template ns) is a descendant of the field node,
        // typically <field><event><submit target="…"/></event></field>.
        if (current.SelectSingleNode(".//*[local-name()='submit']") is not XmlElement submit)
            return false;

        submit.SetAttribute("target", url);
        SetXfaTemplateXml(doc.OuterXml);
        return true;
    }

    /// <summary>Return the XFA template XML from either a named "template" array part or,
    /// when the form's /XFA is a single-stream XDP (no named parts), the <c>&lt;template&gt;</c>
    /// element extracted from that XDP. <see cref="GetXfaTemplateXml"/> only handles the
    /// array form and returns null for a single-stream XDP.</summary>
    internal string? GetXfaTemplateXmlResolved()
    {
        var direct = GetXfaTemplateXml();
        if (direct is not null) return direct;
        var reader = _reader ?? OwnerDocument?.Reader;
        if (reader is null) return null;
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return null;
        if (reader.Resolve(acroForm.Get("XFA")) is not PdfStream single) return null;
        try
        {
            var xdp = StripBom(Encoding.UTF8.GetString(reader.DecodeStream(single)));
            var d = new XmlDocument();
            d.LoadXml(xdp);
            // Select the genuine xfa-template packet by namespace — NOT the config's
            // <common><template><base>. element (a different, xci namespace).
            var t = d.DocumentElement?.SelectSingleNode(
                "//*[local-name()='template' and contains(namespace-uri(),'xfa-template')]");
            return (t as XmlElement)?.OuterXml;
        }
        catch { return null; }
    }

    /// <summary>Strictly resolve a dotted SOM path against the XFA template — follow the
    /// container hierarchy segment by segment, skipping only anonymous (unnamed) subform
    /// wrappers, WITHOUT the lenient "any descendant with the leaf name" fallback that
    /// <see cref="FindXfaTemplateNode"/> applies. Returns true only when every segment
    /// resolves and the final node is a fillable field. Robust where the template-field
    /// enumeration is incomplete (some templates enumerate to zero fields).</summary>
    internal bool XfaTemplateFieldExists(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var xml = GetXfaTemplateXmlResolved();
        if (xml is null) return false;
        XmlDocument doc = new();
        try { doc.LoadXml(xml); } catch { return false; }
        if (doc.DocumentElement is null) return false;
        var node = ResolveXfaTemplateStrict(doc.DocumentElement, SplitSomPath(path), 0);
        return node is XmlElement el && (el.LocalName == "field" || el.LocalName == "exclGroup");
    }

    private static XmlNode? ResolveXfaTemplateStrict(XmlNode current, string[] parts, int idx)
    {
        if (idx >= parts.Length) return current;
        var seg = parts[idx];
        int occ = 0;
        var br = seg.IndexOf('[');
        var name = seg;
        if (br >= 0)
        {
            name = seg[..br];
            int.TryParse(seg[(br + 1)..seg.IndexOf(']')], out occ);
        }
        bool byLocal = name.StartsWith('#');
        var matchName = byLocal ? name[1..] : name;

        // Phase 1: a direct child matching this segment's @name (or #local-name).
        int count = 0;
        foreach (XmlNode child in current.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            bool matches = byLocal
                ? child.LocalName == matchName && child.Attributes?["name"] is null
                : child.Attributes?["name"]?.Value == matchName;
            if (!matches) continue;
            if (count == occ)
            {
                var r = ResolveXfaTemplateStrict(child, parts, idx + 1);
                if (r is not null) return r;
            }
            count++;
        }
        // Phase 2: descend transparently through containers the SOM data path collapses —
        // anonymous unnamed subforms, the always-structural pageSet/pageArea, AND named
        // subforms that don't bind data (bind match="none", e.g. a layout "page" subform
        // that hosts the real data subforms beneath it).
        foreach (XmlNode child in current.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            bool structural = child.LocalName is "pageArea" or "pageSet";
            bool named = !structural && child.Attributes?["name"] is not null;
            if (named && !HasBindNone(child)) continue;
            if (child.LocalName is "subform" or "subformSet" or "area" or "pageSet" or "pageArea")
            {
                var r = ResolveXfaTemplateStrict(child, parts, idx);
                if (r is not null) return r;
            }
        }
        return null;
    }

    /// <summary>Mark every XFA template field read-only (<c>access="readOnly"</c>) and persist
    /// it back into the /XFA template stream. Used by <c>Facades.Form.FlattenAllFields</c> to
    /// lock a dynamic XFA form's fields (which have no AcroForm widgets to flatten).</summary>
    internal void SetXfaFieldsReadOnly()
    {
        var xml = GetXfaTemplateXml();
        if (xml is null) return;
        XmlDocument doc = new();
        try { doc.LoadXml(xml); } catch { return; }
        if (doc.DocumentElement is null) return;
        var fields = doc.DocumentElement.SelectNodes(".//*[local-name()='field']");
        if (fields is null || fields.Count == 0) return;
        bool changed = false;
        foreach (XmlNode f in fields)
            if (f is XmlElement el) { el.SetAttribute("access", "readOnly"); changed = true; }
        if (changed) SetXfaTemplateXml(doc.DocumentElement.OuterXml);
    }

    /// <summary>First descendant (child-first) element whose @name equals
    /// <paramref name="name"/>, skipping anonymous wrapper subforms between levels.</summary>
    private static XmlNode? FindNamedTemplateNode(XmlNode parent, string name)
    {
        foreach (XmlNode child in parent.ChildNodes)
            if (child is XmlElement el && el.GetAttribute("name") == name) return el;
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is not XmlElement) continue;
            var found = FindNamedTemplateNode(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Walk the XFA template hierarchy along a dotted SOM path
    /// ("formulaire1[0].#subform[0].FIELD[0]"). Named segments descend via
    /// <see cref="FindNamedTemplateNode"/> (which skips anonymous wrapper subforms);
    /// anonymous class segments ("#subform[1]") resolve to the nth direct child of that
    /// XFA class. Returns null when a segment fails to resolve or the path never leaves
    /// the root.</summary>
    internal static XmlNode? WalkTemplateBySomPath(XmlNode templateRoot, string somPath)
    {
        XmlNode current = templateRoot;
        foreach (var rawSeg in SplitSomPath(somPath))
        {
            var m = Regex.Match(rawSeg, @"^(.*?)(?:\[(\d+)\])?$");
            var seg = m.Groups[1].Value;
            var idx = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            var next = seg.StartsWith('#')
                ? FindClassTemplateNode(current, seg[1..], idx)
                : FindNamedTemplateNode(current, seg);
            if (next is null) return null;
            current = next;
        }
        return ReferenceEquals(current, templateRoot) ? null : current;
    }

    /// <summary>Resolve an anonymous SOM class segment ("#subform") to the
    /// <paramref name="index"/>th direct child of that XFA class carrying no name of
    /// its own, falling back to any direct child of the class.</summary>
    private static XmlNode? FindClassTemplateNode(XmlNode parent, string className, int index)
    {
        int seen = 0;
        foreach (XmlNode child in parent.ChildNodes)
            if (child is XmlElement el && el.LocalName == className
                && el.GetAttribute("name").Length == 0 && seen++ == index)
                return el;
        seen = 0;
        foreach (XmlNode child in parent.ChildNodes)
            if (child is XmlElement el && el.LocalName == className && seen++ == index)
                return el;
        return null;
    }

    /// <summary>Mirror a moved widget's rectangle back into the static-XFA template —
    /// x/y/w/h rewritten in "px" (1px = 1pt), with the caption reserve and the
    /// contentArea origin folded out and the field's own insets ignored — then replace
    /// the page's content with a fresh render of its fields (border + caption) so the
    /// page shows the form at the new geometry. Matches Aspose.Pdf, which keeps
    /// the template and the designer-baked static render in sync when AcroForm field
    /// geometry changes on an XFA form. No-op for non-XFA documents and for fields
    /// without a template node.</summary>
    internal void SyncXfaWidgetGeometry(Field field)
    {
        if (!IsXfa || _reader is null) return;
        var fullName = field.FullName;
        if (string.IsNullOrEmpty(fullName)) return;
        if (_reader.Resolve(field.Dict.Get("Rect")) is not PdfArray ra || ra.Count < 4) return;
        var rect = Rectangle.FromPdfArray(ra, _reader);
        if (rect is null) return;

        var xml = GetXfaTemplateXml();
        if (xml is null) return;
        XmlDocument tdoc = new();
        try { tdoc.LoadXml(xml); } catch { return; }
        if (tdoc.DocumentElement is null) return;
        if (WalkTemplateBySomPath(tdoc.DocumentElement, fullName!) is not XmlElement fieldEl
            || fieldEl.LocalName != "field") return;

        var doc = OwnerDocument;
        var pageIndex = field.PageIndex;
        if (doc is null || pageIndex < 1 || pageIndex > doc.Pages.Count) return;
        var page = doc.Pages[pageIndex];
        double pageH = page.Rect.Height;

        var (reserve, placement) = GetCaptionReserve(fieldEl);
        var (caX, caY) = GetContentAreaOrigin(tdoc.DocumentElement);

        double x = rect.LLX - caX, y = pageH - rect.URY - caY;
        double w = rect.Width, h = rect.Height;
        switch (placement)
        {
            case "right": case "inline": w += reserve; break;
            case "top": y -= reserve; h += reserve; break;
            case "bottom": h += reserve; break;
            default: x -= reserve; w += reserve; break; // left — the XFA default
        }
        fieldEl.SetAttribute("x", PxAttr(x));
        fieldEl.SetAttribute("y", PxAttr(y));
        fieldEl.SetAttribute("w", PxAttr(w));
        fieldEl.SetAttribute("h", PxAttr(h));
        SetXfaTemplateXml(tdoc.DocumentElement.OuterXml);

        RegenerateXfaStaticPageContent(page, tdoc.DocumentElement);
    }

    /// <summary>Replace the page's content with a fresh static render of the XFA form
    /// fields on it — one stroked border rectangle per widget plus its caption text at
    /// the caption-reserve position — dropping the original designer-baked render, which
    /// still draws the fields at their pre-move positions.</summary>
    private void RegenerateXfaStaticPageContent(Page page, XmlElement templateRoot)
    {
        if (_reader is null) return;
        var sb = new StringBuilder();
        string? fontRes = null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => System.Math.Round(v, 3).ToString("0.###", ci);

        foreach (var f in Fields)
        {
            if (f.PageIndex != page.Number) continue;
            if (_reader.Resolve(f.Dict.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var r = Rectangle.FromPdfArray(ra, _reader);
            if (r is null) continue;

            double bw = 1;
            if (_reader.ResolveDict(f.Dict.Get("BS")) is { } bs
                && _reader.Resolve(bs.Get("W")) is { } wObj)
                bw = wObj switch { PdfInteger i => i.Value, PdfReal d => d.Value, _ => 1 };

            sb.Append("q\n0 G\n").Append(F(bw)).Append(" w\n")
              .Append(F(r.LLX)).Append(' ').Append(F(r.LLY)).Append(' ')
              .Append(F(r.Width)).Append(' ').Append(F(r.Height)).Append(" re\nS\nQ\n");

            var tplNode = f.FullName is { Length: > 0 } fn
                ? WalkTemplateBySomPath(templateRoot, fn) as XmlElement
                : null;
            var caption = tplNode is null ? null : GetCaptionText(tplNode);
            if (string.IsNullOrEmpty(caption)) continue;

            var (reserve, placement) = GetCaptionReserve(tplNode!);
            double fs = 10;
            double capX = placement switch
            {
                "right" or "inline" => r.URX,
                "top" or "bottom" => r.LLX,
                _ => r.LLX - reserve,
            };
            double capY = (r.LLY + r.URY) / 2 - fs / 2;
            fontRes ??= Annotations.RedactionAnnotation.RegisterOverlayFont(page);
            var esc = caption!.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            sb.Append("BT\n0 g\n/").Append(fontRes).Append(' ').Append(F(fs)).Append(" Tf\n")
              .Append(F(capX)).Append(' ').Append(F(capY)).Append(" Td\n(")
              .Append(esc).Append(") Tj\nET\n");
        }

        if (sb.Length == 0) return;
        page.SetContentStream(Encoding.Latin1.GetBytes(sb.ToString()));
        page.ResetContentsCache();
    }

    private static string PxAttr(double v) =>
        System.Math.Round(v, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "px";

    /// <summary>Caption reserve (in points) and placement for an XFA template field node.</summary>
    private static (double reserve, string placement) GetCaptionReserve(XmlElement fieldEl)
    {
        foreach (XmlNode ch in fieldEl.ChildNodes)
            if (ch is XmlElement el && el.LocalName == "caption")
                return (XfaMeasureToPt(el.GetAttribute("reserve")) ?? 0,
                        el.GetAttribute("placement") is { Length: > 0 } p ? p : "left");
        return (0, "left");
    }

    /// <summary>The caption's literal text (caption/value/text) for an XFA template field node.</summary>
    private static string? GetCaptionText(XmlElement fieldEl)
    {
        foreach (XmlNode ch in fieldEl.ChildNodes)
            if (ch is XmlElement { LocalName: "caption" } cap)
                return cap.SelectSingleNode(".//*[local-name()='text']")?.InnerText;
        return null;
    }

    /// <summary>Origin (in points) of the first contentArea in the template — the offset
    /// between template coordinates and page coordinates on a static XFA form.</summary>
    private static (double x, double y) GetContentAreaOrigin(XmlElement templateRoot)
    {
        if (templateRoot.SelectSingleNode(".//*[local-name()='contentArea']") is XmlElement ca)
            return (XfaMeasureToPt(ca.GetAttribute("x")) ?? 0, XfaMeasureToPt(ca.GetAttribute("y")) ?? 0);
        return (0, 0);
    }

    /// <summary>Parse an XFA measurement ("25mm", "0.25in", "10pt", "12px", bare number)
    /// to points; XFA "px" is treated as 1pt, matching the Aspose.Pdf write-back
    /// unit.</summary>
    internal static double? XfaMeasureToPt(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        foreach (var (u, f) in new[] { ("mm", 72.0 / 25.4), ("cm", 720.0 / 25.4), ("in", 72.0), ("pt", 1.0), ("px", 1.0) })
            if (v.EndsWith(u, StringComparison.Ordinal)
                && double.TryParse(v[..^u.Length], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d * f;
        return double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var raw) ? raw : null;
    }

    /// <summary>Settings for <see cref="Document.Flatten(FlattenSettings)"/>.</summary>
    public sealed class FlattenSettings
    {
        /// <summary>When true, button widgets (XFA &lt;button&gt; nodes) are
        /// marked presence="hidden" before the flatten step so they are not
        /// rasterised into the resulting page content.</summary>
        public bool HideButtons { get; set; }

        /// <summary>When true, JavaScript and other field events are run
        /// during flatten (e.g. computed-field formulas refresh before the
        /// flatten captures their value). Stored only; XFA scripts are
        /// not currently executed.</summary>
        public bool CallEvents { get; set; }

        /// <summary>When true, each field's appearance stream is regenerated
        /// from its current value before being flattened into page content.
        /// Stored only; appearances are not currently rebuilt.</summary>
        public bool UpdateAppearances { get; set; }

        /// <summary>When true, redaction annotations are applied during the flatten pass. Stored only.</summary>
        public bool ApplyRedactions { get; set; }
    }

    private void FlattenXfa()
    {
        var flatReader = ResolvedReader;
        if (flatReader is null || !IsXfa) return;
        var catalog = flatReader.Catalog;
        var acroForm = flatReader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is null) return;

        // A dynamic XFA form carries its fields only in the XFA template — the AcroForm
        // has no widget fields. When flattening it to a standard AcroForm, materialise one
        // flat field per template field so the fields survive as findable AcroForm fields
        // (/T = the full dotted SOM path, matching GetXfaFieldNames). A static XFA form
        // already owns AcroForm widget fields, so leave those untouched (no duplication).
        var existing = flatReader.Resolve(acroForm.Get("Fields")) as PdfArray;
        bool hasWidgets = existing is not null && existing.Count > 0;
        if (!hasWidgets)
        {
            GenerateFlatAcroFieldsFromXfaTemplate(flatReader, acroForm);
            RenderDynamicXfa();     // paint the form onto real pages (replaces the XFA fallback page)
        }

        // Remove XFA key from AcroForm — this converts XFA to standard AcroForm
        acroForm.Remove("XFA");
        MarkAcroFormDirty();
    }

    /// <summary>Materialise a flat AcroForm field for each RENDERED XFA field (only used when
    /// flattening a dynamic XFA form that has no AcroForm widgets). The rendered set is resolved
    /// by <see cref="Xfa.XfaFormEngine"/>, which walks the subform tree AND the master pages
    /// (pageSet/pageArea) and applies the template-decidable selection rules (static presence,
    /// barcode ui). Each field is a top-level /Fields entry whose /T is the entire dotted SOM
    /// path (so FullName == PartialName and FindByName resolves it), with /FT derived from the
    /// field's XFA &lt;ui&gt; control. Positions (/Rect) are not emitted — that needs the XFA
    /// layout engine and is not required to make the fields findable.</summary>
    /// <summary>Paint the dynamic-XFA form's content onto fresh PDF pages so a raster render shows
    /// the form rather than the XFA fallback page. Tolerant: a failure leaves pages untouched.</summary>
    private void RenderDynamicXfa()
    {
        var doc = OwnerDocument;
        if (doc is null) return;
        var xml = GetXfaTemplateXmlResolved() ?? GetXfaTemplateXml();
        if (string.IsNullOrEmpty(xml)) return;
        try
        {
            var tdoc = new XmlDocument();
            tdoc.LoadXml(xml);
            if (tdoc.DocumentElement is { } root)
                Xfa.XfaRenderer.Render(doc, root, GetXfaFieldValue);
        }
        catch { }
    }

    private void GenerateFlatAcroFieldsFromXfaTemplate(PdfReader reader, PdfDictionary acroForm)
    {
        var doc = OwnerDocument;
        if (doc is null) return;
        var engine = Xfa.XfaFormEngine.TryCreate(GetXfaTemplateXmlResolved() ?? GetXfaTemplateXml());
        if (engine is null) return;
        // Give the engine the XFA data-binding resolver so its scripts can read field rawValues.
        // The engine must never break a flatten — fall back to no generated fields on any failure.
        List<Xfa.XfaFlatField> fields;
        try { fields = engine.BuildRenderedFields(GetXfaFieldValue); }
        catch { return; }
        if (fields.Count == 0) return;

        var fieldsArr = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fieldsArr is null) { fieldsArr = new PdfArray(); acroForm.Set("Fields", fieldsArr); }

        foreach (var f in fields)
        {
            var fld = new PdfDictionary();
            fld.Set("T", new PdfString(Encoding.UTF8.GetBytes(f.Path)));
            fld.Set("FT", new PdfName(f.Ft));
            if (f.Ff != 0) fld.Set("Ff", new PdfInteger((int)f.Ff));
            // Carry the field's bound datasets value onto the flat field's /V so the flattened
            // dynamic-XFA form keeps its data values (Field.Value). Text and
            // choice fields take a text /V; leave value-less and button/signature fields untouched.
            if (!string.IsNullOrEmpty(f.Value) && (f.Ft == "Tx" || f.Ft == "Ch"))
                fld.Set("V", new PdfString(Encoding.UTF8.GetBytes(f.Value)));
            int num = doc.AllocateObjectNumber();
            doc.AddNewObject(num, fld, registerOverlay: true);
            fieldsArr.Add(new PdfIndirectRef(num, 0));
        }
    }

    /// <summary>Whether this form is an XFA form.</summary>
    public bool IsXfa
    {
        get
        {
            var reader = _reader ?? OwnerDocument?.Reader;
            return reader is not null &&
                reader.ResolveDict(reader.Catalog.Get("AcroForm")) is { } acro &&
                acro.ContainsKey("XFA");
        }
    }

    /// <summary>
    /// Get all interactive field names from the XFA template. Returns an empty array for non-XFA forms.
    /// Excludes draw (decorative) elements.
    /// </summary>
    internal string[] GetXfaFieldNames()
    {
        if (!IsXfa) return [];
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return [];
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            var names = new List<string>();
            if (doc.DocumentElement is not null)
                CollectXfaFieldNames(doc.DocumentElement, "", names);
            return names.ToArray();
        }
        catch { return []; }
    }

    /// <summary>Enumerate the actual XFA datasets leaves as (full dotted path, value)
    /// pairs. Unlike <see cref="GetXfaFieldNames"/> (which walks the template and so
    /// only yields the index-0 instance of each repeated field), this walks the
    /// datasets tree and yields every repeated instance with its real sibling index
    /// (e.g. <c>movies[0].movie[13].countries[0].country[1]</c>).</summary>
    internal List<KeyValuePair<string, string>> GetXfaDatasetsFields()
    {
        var result = new List<KeyValuePair<string, string>>();
        if (!IsXfa) return result;
        var xml = GetXfaDatasetsXml();
        if (string.IsNullOrEmpty(xml)) return result;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var data = FindDatasetsDataNode(doc);
            if (data is not null) WalkXfaDatasets(data, string.Empty, result);
        }
        catch { }
        return result;
    }

    private static XmlNode? FindDatasetsDataNode(XmlDocument doc)
    {
        var root = doc.DocumentElement;
        if (root is null) return null;
        var datasets = root.SelectSingleNode("//*[local-name()='datasets']");
        var data = datasets?.SelectSingleNode("*[local-name()='data']");
        if (data is not null) return data;
        var allData = root.SelectNodes("//*[local-name()='data']");
        if (allData is not null)
        {
            foreach (XmlNode d in allData)
                if (d.ParentNode?.LocalName == "datasets") return d;
            if (allData.Count > 0) return allData[0];
        }
        return root;
    }

    private static void WalkXfaDatasets(XmlNode node, string prefix, List<KeyValuePair<string, string>> result)
    {
        var counts = new Dictionary<string, int>();
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var localName = child.LocalName;
            var index = counts.TryGetValue(localName, out var c) ? c : 0;
            counts[localName] = index + 1;
            var escName = EscapeSomSegment(localName);
            var path = prefix.Length == 0 ? $"{escName}[{index}]" : $"{prefix}.{escName}[{index}]";

            var hasElementChild = false;
            foreach (XmlNode grand in child.ChildNodes)
                if (grand.NodeType == XmlNodeType.Element) { hasElementChild = true; break; }

            if (hasElementChild)
                WalkXfaDatasets(child, path, result);
            else
                result.Add(new KeyValuePair<string, string>(path, child.InnerText));
        }
    }

    /// <summary>True when the XFA template marks the field at the given path as a
    /// multi-line text edit (<c>&lt;textEdit multiLine="1"&gt;</c>). Non-multi-line
    /// fields normalise embedded newlines on import.</summary>
    internal bool IsXfaFieldMultiline(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (string.IsNullOrEmpty(templateXml)) return false;
        var leaf = SplitSomPath(path)[^1];
        var match = Regex.Match(leaf, @"^(.+)\[\d+\]$");
        var name = match.Success ? match.Groups[1].Value : leaf;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            var field = doc.SelectSingleNode($"//*[local-name()='field'][@name='{name}']");
            var attr = field?.SelectSingleNode(".//@*[local-name()='multiLine']");
            return attr is not null && attr.Value == "1";
        }
        catch { return false; }
    }

    private static void CollectXfaFieldNames(XmlNode node, string parentPath, List<string> names)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var localName = child.LocalName;
            var nameAttr = child.Attributes?["name"]?.Value;

            if (localName is "subform" or "subformSet" or "area")
            {
                if (nameAttr is not null)
                {
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var currentPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";
                    CollectXfaFieldNames(child, currentPath, names);
                }
                else
                    CollectXfaFieldNames(child, parentPath, names);
            }
            else if (localName is "field" or "exclGroup")
            {
                if (nameAttr is not null)
                {
                    int idx = CountPrecedingSiblings(child, localName, nameAttr);
                    var escName = EscapeSomSegment(nameAttr);
                    var fieldPath = parentPath.Length > 0
                        ? $"{parentPath}.{escName}[{idx}]"
                        : $"{escName}[{idx}]";
                    names.Add(fieldPath);
                }
                // Don't recurse into exclGroup — the group itself is the field,
                // individual options are not separate fields.
            }
            else
            {
                CollectXfaFieldNames(child, parentPath, names);
            }
        }
    }

    /// <summary>
    /// Get the XFA datasets XML, or null if not an XFA form.
    /// </summary>
    public string? GetXfaDatasetsXml()
    {
        // First try to get just the "datasets" part from an XFA array
        var (_, datasetsXml) = GetXfaPart("datasets");
        if (datasetsXml is not null) return datasetsXml;

        var reader = ResolvedReader;
        if (reader is null) return null;
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return null;

        var xfaObj = reader.Resolve(acroForm.Get("XFA"));

        // Single-stream XFA: entire XDP in one stream
        if (xfaObj is PdfStream xfaStream)
        {
            var data = reader.DecodeStream(xfaStream);
            return StripBom(Encoding.UTF8.GetString(data));
        }

        // XFA array without a named "datasets" part:
        // Concatenate all streams to reconstruct the full XDP
        if (xfaObj is PdfArray xfaArray)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < xfaArray.Count; i++)
            {
                var item = reader.Resolve(xfaArray[i]);
                if (item is PdfStream s)
                {
                    var data = reader.DecodeStream(s);
                    sb.Append(Encoding.UTF8.GetString(data));
                }
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
        return null;
    }

    /// <summary>
    /// Get a specific XFA part stream (e.g. "datasets") from the XFA array.
    /// </summary>
    private (PdfStream? stream, string? xml) GetXfaPart(string partName)
    {
        var reader = ResolvedReader;
        if (reader is null) return (null, null);
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return (null, null);
        var xfaObj = reader.Resolve(acroForm.Get("XFA"));
        if (xfaObj is PdfArray xfaArray)
        {
            for (int i = 0; i < xfaArray.Count - 1; i += 2)
            {
                if (xfaArray[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == partName)
                {
                    var stream = reader.Resolve(xfaArray[i + 1]) as PdfStream;
                    if (stream is not null)
                    {
                        var data = reader.DecodeStream(stream);
                        return (stream, Encoding.UTF8.GetString(data));
                    }
                }
            }
        }
        return (null, null);
    }

    /// <summary>
    /// <summary>
    /// Get the caption text for an XFA field by walking the template XML.
    /// Returns the text from &lt;caption&gt;&lt;value&gt;&lt;text&gt; inside the field element.
    /// </summary>
    internal string? GetXfaFieldCaption(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is null) return null;
            var fieldNode = FindXfaTemplateNode(doc.DocumentElement, path, 0);
            if (fieldNode is null) return null;

            // Look for <caption><value><text>.</text></value></caption>
            foreach (XmlNode child in fieldNode.ChildNodes)
            {
                if (child.LocalName == "caption")
                {
                    // Try <value><text> first
                    foreach (XmlNode vc in child.ChildNodes)
                    {
                        if (vc.LocalName == "value")
                        {
                            foreach (XmlNode tc in vc.ChildNodes)
                            {
                                if (tc.LocalName == "text")
                                    return tc.InnerText;
                            }
                        }
                    }
                    // Fallback: direct text content
                    return child.InnerText;
                }
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Resolve the XFA template UI widget kind for a field path — the local name of
    /// the element under the field's &lt;ui&gt; (e.g. "textEdit", "choiceList",
    /// "button"). A multi-select &lt;choiceList&gt; reports "choiceListMulti".
    /// Returns "exclGroup" for an exclusion (radio) group and "textEdit" for a
    /// &lt;field&gt; that declares no &lt;ui&gt; (the XFA default). Returns null when
    /// the path resolves to no template field node.
    /// </summary>
    internal string? GetXfaFieldUiKind(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is null) return null;
            var fieldNode = FindXfaTemplateNode(doc.DocumentElement, path, 0);
            if (fieldNode is null) return null;
            if (fieldNode.LocalName == "exclGroup") return "exclGroup";

            foreach (XmlNode child in fieldNode.ChildNodes)
            {
                if (child.LocalName != "ui") continue;
                foreach (XmlNode uiChild in child.ChildNodes)
                {
                    if (uiChild.NodeType != XmlNodeType.Element) continue;
                    if (uiChild.LocalName == "choiceList")
                    {
                        // XFA: open="always"/"multiSelect" is an expanded list box;
                        // "userControl"/"onEntry"/absent is a drop-down combo.
                        var open = uiChild.Attributes?["open"]?.Value;
                        return open is "always" or "multiSelect" ? "choiceListMulti" : "choiceList";
                    }
                    return uiChild.LocalName;
                }
            }
            // A <field> with no explicit <ui> renders as a plain text edit in XFA.
            return fieldNode.LocalName == "field" ? "textEdit" : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Get radio button option values from the XFA template.
    /// Looks for &lt;items&gt; children containing &lt;integer&gt; or &lt;text&gt; values.
    /// </summary>
    internal List<string>? GetXfaRadioButtonItems(string path)
    {
        var templateXml = GetXfaTemplateXml();
        if (templateXml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(templateXml);
            if (doc.DocumentElement is null) return null;
            var fieldNode = FindXfaTemplateNode(doc.DocumentElement, path, 0);
            if (fieldNode is null) return null;

            // Collect items from <items> children (direct or in child <field> elements)
            var result = new List<string>();
            CollectXfaItems(fieldNode, result);

            // For exclGroup: items are on child <field> elements, not the group itself
            if (result.Count == 0 && fieldNode.LocalName == "exclGroup")
            {
                foreach (XmlNode child in fieldNode.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element && child.LocalName == "field")
                        CollectXfaItems(child, result);
                }
            }
            return result.Count > 0 ? result : null;
        }
        catch { return null; }
    }

    private static void CollectXfaItems(XmlNode node, List<string> result)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.LocalName != "items") continue;
            foreach (XmlNode item in child.ChildNodes)
            {
                if (item.NodeType == XmlNodeType.Element)
                    result.Add(item.InnerText);
            }
        }
    }

    /// <summary>
    /// Walk the XFA template tree to find a node at the given dotted path.
    /// Path segments are "name[index]" or just "name". Unnamed containers
    /// (subforms without a name attribute) are transparently descended into.
    /// </summary>
    private static XmlNode? FindXfaTemplateNode(XmlNode root, string path, int startSegment)
    {
        var parts = SplitSomPath(path);
        return FindXfaTemplateNodeRecursive(root, parts, startSegment);
    }

    private static XmlNode? FindXfaTemplateNodeRecursive(XmlNode current, string[] parts, int partIndex)
    {
        if (partIndex >= parts.Length) return current;

        var seg = parts[partIndex];
        int idx = 0;
        var bracketPos = seg.IndexOf('[');
        string name;
        if (bracketPos >= 0)
        {
            name = seg[..bracketPos];
            int.TryParse(seg[(bracketPos + 1)..seg.IndexOf(']')], out idx);
        }
        else
        {
            name = seg;
        }

        // #-prefixed segments match unnamed elements by local name
        bool matchByLocalName = name.StartsWith('#');
        var matchName = matchByLocalName ? name[1..] : name;

        // Search direct children first
        int count = 0;
        foreach (XmlNode child in current.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            bool matches = matchByLocalName
                ? child.LocalName == matchName && child.Attributes?["name"] is null
                : child.Attributes?["name"]?.Value == matchName;
            if (matches)
            {
                if (count == idx)
                {
                    var result = FindXfaTemplateNodeRecursive(child, parts, partIndex + 1);
                    if (result is not null) return result;
                }
                count++;
            }
        }

        // If not found, descend into unnamed containers and structural elements
        // (pageArea/pageSet are always transparent in XFA paths even when named)
        foreach (XmlNode child in current.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            // pageArea and pageSet are structural — always descend even if named
            bool isStructural = child.LocalName is "pageArea" or "pageSet";
            if (!isStructural && child.Attributes?["name"] is not null) continue;
            if (child.LocalName is "subform" or "pageSet" or "pageArea" or "area" or "subformSet")
            {
                var result = FindXfaTemplateNodeRecursive(child, parts, partIndex);
                if (result is not null) return result;
            }
        }

        // Last-segment fallback: if strict walk fails, search for the final named segment
        // as a descendant. XFA paths from AcroForm may use different subform index numbering.
        if (partIndex < parts.Length && parts.Length > 1)
        {
            var lastSeg = parts[^1];
            var idxMatch = Regex.Match(lastSeg, @"^(.+)\[(\d+)\]$");
            var leafName = idxMatch.Success ? idxMatch.Groups[1].Value : lastSeg;
            var leafIdx = idxMatch.Success ? int.Parse(idxMatch.Groups[2].Value) : 0;
            // Search by name attribute (field, exclGroup, subform)
            var allMatches = current.SelectNodes($".//*[@name='{leafName}']");
            if (allMatches is not null && allMatches.Count > leafIdx)
                return allMatches[leafIdx];
        }

        return null;
    }

    /// <summary>
    /// Get an XFA field value by dotted path in the datasets XML.
    /// When direct lookup fails, resolves XFA template data-binding (bind match="dataRef")
    /// to map template field paths to their corresponding data nodes.
    /// </summary>
    public string? GetXfaFieldValue(string path)
    {
        var (_, xml) = GetXfaPart("datasets");
        // Fallback: single-stream XFA
        var reader = ResolvedReader;
        if (xml is null && reader is not null)
        {
            var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
            if (acroForm is not null)
            {
                var xfaObj = reader.Resolve(acroForm.Get("XFA"));
                if (xfaObj is PdfStream singleStream)
                {
                    var data = reader.DecodeStream(singleStream);
                    xml = Encoding.UTF8.GetString(data);
                }
            }
        }
        if (xml is null) return null;
        var result = FindXfaNodeValue(xml, path);
        if (!string.IsNullOrEmpty(result)) return result;

        // Template-based data binding resolution:
        // Walk the template to find the field, resolve <bind match="dataRef" ref="$.xxx"/>
        // and skip presentation-only subforms (those with <bind match="none"/>).
        try
        {
            var templateXml = GetXfaTemplateXml();
            if (templateXml is null) return result;

            var templateDoc = new XmlDocument();
            templateDoc.LoadXml(templateXml);
            if (templateDoc.DocumentElement is null) return result;

            var parts = SplitSomPath(path);
            if (parts.Length < 2) return result;

            // Walk the template by path segments to find the field node
            XmlNode? templateNode = templateDoc.DocumentElement;
            for (int i = 0; i < parts.Length && templateNode is not null; i++)
            {
                templateNode = FindTemplateChild(templateNode, parts[i]);
            }

            if (templateNode is null) return result;

            // Check for <bind match="dataRef" ref="$.xxx"/>
            var bindNode = FindBindElement(templateNode);
            string? bindRef = null;
            if (bindNode is not null)
            {
                var matchAttr = bindNode.Attributes?["match"];
                var refAttr = bindNode.Attributes?["ref"];
                if (matchAttr?.Value == "dataRef" && refAttr?.Value is { } r && r.StartsWith("$."))
                    bindRef = r.Substring(2); // strip "$."
            }

            // Build the data path by walking up, skipping bind="none" subforms
            var dataPathParts = new List<string>();
            for (int i = 0; i < parts.Length - 1; i++) // exclude the field itself
            {
                // Check if this subform is presentation-only (bind match="none")
                XmlNode? checkNode = templateDoc.DocumentElement;
                for (int j = 0; j <= i && checkNode is not null; j++)
                    checkNode = FindTemplateChild(checkNode, parts[j]);

                if (checkNode is not null && HasBindNone(checkNode))
                    continue; // skip presentation-only subform

                dataPathParts.Add(parts[i]);
            }

            // Append the resolved field name (from bind ref or original field name)
            dataPathParts.Add(bindRef ?? parts[^1]);

            var resolvedPath = string.Join(".", dataPathParts);
            if (resolvedPath != path)
            {
                var resolved = FindXfaNodeValue(xml, resolvedPath);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }
        }
        catch { /* template resolution failed — return original result */ }

        return result;
    }

    /// <summary>Resolve a SOM (template) field path to the corresponding XFA *datasets* path using
    /// the template's bind rules — honour a leaf <c>&lt;bind match="dataRef" ref="$.xxx"/&gt;</c> and
    /// skip presentation-only subforms (<c>&lt;bind match="none"/&gt;</c>). Returns the resolved
    /// dotted data path, or null when the template can't be walked or the path is unchanged. This
    /// mirrors the SOM→data mapping <see cref="GetXfaFieldValue"/> applies on READ; the WRITE path
    /// (<see cref="SetXfaFieldValues"/>) reuses it so a value lands on the same datasets node reading
    /// returns (e.g. a <c>filerName</c> field bound to a <c>&lt;sarx:FilerName&gt;</c> data node).</summary>
    private string? ResolveSomToDataPath(string path)
    {
        try
        {
            var templateXml = GetXfaTemplateXml();
            if (templateXml is null) return null;
            var templateDoc = new XmlDocument();
            templateDoc.LoadXml(templateXml);
            if (templateDoc.DocumentElement is null) return null;

            var parts = SplitSomPath(path);
            if (parts.Length < 2) return null;

            // The template's name attributes are UN-indexed (name="FilerNameSub"), while SOM parts
            // carry an occurrence index (FilerNameSub[0]); strip it for the template walk.
            static string Bare(string p)
            {
                var m = Regex.Match(p, @"^(.+)\[(\d+)\]$");
                return m.Success ? m.Groups[1].Value : p;
            }

            XmlNode? templateNode = templateDoc.DocumentElement;
            for (int i = 0; i < parts.Length && templateNode is not null; i++)
                templateNode = FindTemplateChild(templateNode, Bare(parts[i]));
            if (templateNode is null) return null;

            var bindNode = FindBindElement(templateNode);
            string? bindRef = null;
            if (bindNode is not null)
            {
                var matchAttr = bindNode.Attributes?["match"];
                var refAttr = bindNode.Attributes?["ref"];
                if (matchAttr?.Value == "dataRef" && refAttr?.Value is { } r && r.StartsWith("$."))
                    bindRef = r.Substring(2); // may itself be multi-segment, e.g. "FilingInstitutionInformation.FilerName"
            }

            var dataPathParts = new List<string>();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                XmlNode? checkNode = templateDoc.DocumentElement;
                for (int j = 0; j <= i && checkNode is not null; j++)
                    checkNode = FindTemplateChild(checkNode, Bare(parts[j]));
                if (checkNode is not null && HasBindNone(checkNode)) continue; // presentation-only subform: not in data
                dataPathParts.Add(parts[i]);
            }
            dataPathParts.Add(bindRef ?? parts[^1]);

            var resolvedPath = string.Join(".", dataPathParts);
            return resolvedPath != path ? resolvedPath : null;
        }
        catch { return null; }
    }

    /// <summary>Find a subform or field child by name in an XFA template node.</summary>
    private static XmlNode? FindTemplateChild(XmlNode parent, string name)
    {
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var ln = child.LocalName;
            if (ln is "subform" or "field" or "exclGroup" or "draw")
            {
                if (child.Attributes?["name"]?.Value == name)
                    return child;
            }
        }
        // Also search inside unnamed subforms
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            if (child.LocalName == "subform" && child.Attributes?["name"] is null)
            {
                var found = FindTemplateChild(child, name);
                if (found is not null) return found;
            }
        }
        return null;
    }

    /// <summary>Find a &lt;bind&gt; element within a template field/subform.</summary>
    private static XmlNode? FindBindElement(XmlNode node)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element && child.LocalName == "bind")
                return child;
        }
        return null;
    }

    /// <summary>Check if a template subform has bind match="none" (presentation-only).</summary>
    private static bool HasBindNone(XmlNode node)
    {
        var bind = FindBindElement(node);
        return bind?.Attributes?["match"]?.Value == "none";
    }

    /// <summary>
    /// Replace the entire XFA datasets stream with new XML content.
    /// Used by ImportXml to wholesale replace data rather than individual field updates.
    /// </summary>
    internal void ReplaceXfaDatasets(XmlDocument importedXml)
    {
        var (stream, existingXml) = GetXfaPart("datasets");

        if (stream is not null && existingXml is not null)
        {
            // Existing datasets part — merge imported data into it
            var existingDoc = new XmlDocument();
            existingDoc.LoadXml(existingXml);

            var dataNs = existingDoc.DocumentElement?.SelectSingleNode("//*[local-name()='data']");
            // If <data> doesn't exist (only <dataDescription>), create it
            if (dataNs is null && existingDoc.DocumentElement is not null)
            {
                var ns = existingDoc.DocumentElement.NamespaceURI;
                var prefix = existingDoc.DocumentElement.Prefix;
                dataNs = string.IsNullOrEmpty(prefix)
                    ? existingDoc.CreateElement("data", ns)
                    : existingDoc.CreateElement(prefix, "data", ns);
                existingDoc.DocumentElement.AppendChild(dataNs);
            }
            if (dataNs is null) return;

            dataNs.InnerXml = "";
            // Unwrap xfa:data / xfa:datasets wrapper if present in the imported XML,
            // so we don't double-nest (e.g. <data><xfa:data><form1>.)
            ImportDataChildren(importedXml, existingDoc, dataNs);

            using var ms = new MemoryStream();
            SaveXmlNoBom(existingDoc, ms);
            var newData = ms.ToArray();
            stream.ReplaceData(newData);
            stream.Dict.Set("Length", new PdfInteger(newData.Length));
            stream.Dict.Remove("Filter");
            MarkXfaStreamDirty(stream);
            return;
        }

        // No "datasets" part in the XFA array
        var rdr = ResolvedReader;
        if (rdr is null) return;
        var acroForm = rdr.ResolveDict(rdr.Catalog.Get("AcroForm"));
        if (acroForm is null) return;
        var xfaObj = rdr.Resolve(acroForm.Get("XFA"));

        // Single-stream XFA: the entire XDP is in one stream.
        // Parse it, find/create <datasets><data>, replace content, write back.
        if (xfaObj is PdfStream singleStream)
        {
            var xdpData = rdr.DecodeStream(singleStream);
            var xdpXml = Encoding.UTF8.GetString(xdpData);
            var xdpDoc = new XmlDocument();
            xdpDoc.LoadXml(xdpXml);

            // Find or create the <datasets> element
            var datasetsEl = xdpDoc.DocumentElement?.SelectSingleNode("//*[local-name()='datasets']");
            if (datasetsEl is null && xdpDoc.DocumentElement is not null)
            {
                // Create <xfa:datasets> element and insert before postamble
                const string xfaNs = "http://www.xfa.org/schema/xfa-data/1.0/";
                datasetsEl = xdpDoc.CreateElement("xfa", "datasets", xfaNs);
                // Try to insert before the closing </xdp:xdp> (last child or before postamble)
                xdpDoc.DocumentElement.AppendChild(datasetsEl);
            }
            if (datasetsEl is null) return;

            // Find or create the <data> element inside <datasets>
            var dataEl = datasetsEl.SelectSingleNode("*[local-name()='data']");
            if (dataEl is null)
            {
                var ns = datasetsEl.NamespaceURI;
                var prefix = datasetsEl.Prefix;
                dataEl = string.IsNullOrEmpty(prefix)
                    ? xdpDoc.CreateElement("data", ns)
                    : xdpDoc.CreateElement(prefix, "data", ns);
                datasetsEl.AppendChild(dataEl);
            }

            // Clear existing data and import the root element of the imported XML
            dataEl.InnerXml = "";
            ImportDataChildren(importedXml, xdpDoc, dataEl);

            // Write updated XDP back to the stream (no BOM)
            using var ms = new MemoryStream();
            SaveXmlNoBom(xdpDoc, ms);
            var newData = ms.ToArray();
            singleStream.ReplaceData(newData);
            singleStream.Dict.Set("Length", new PdfInteger(newData.Length));
            singleStream.Dict.Remove("Filter");
            MarkXfaStreamDirty(singleStream);
            return;
        }

        if (xfaObj is PdfArray xfaArray)
        {
            // XFA array without a named "datasets" part — create one
            const string xfaNs2 = "http://www.xfa.org/schema/xfa-data/1.0/";
            var datasetsDoc = new XmlDocument();
            var datasetsEl2 = datasetsDoc.CreateElement("xfa", "datasets", xfaNs2);
            datasetsDoc.AppendChild(datasetsEl2);
            var dataEl2 = datasetsDoc.CreateElement("xfa", "data", xfaNs2);
            datasetsEl2.AppendChild(dataEl2);

            ImportDataChildren(importedXml, datasetsDoc, dataEl2);

            using var ms = new MemoryStream();
            SaveXmlNoBom(datasetsDoc, ms);
            var newData = ms.ToArray();
            var newStream = new PdfStream(new PdfDictionary(), newData);
            newStream.Dict.Set("Length", new PdfInteger(newData.Length));

            // Insert "datasets" name + stream before "postamble" (or at end)
            int insertIdx = xfaArray.Count;
            for (int i = 0; i < xfaArray.Count - 1; i += 2)
            {
                if (xfaArray[i] is PdfString s &&
                    Encoding.Latin1.GetString(s.Value) == "postamble")
                {
                    insertIdx = i;
                    break;
                }
            }
            xfaArray.Insert(insertIdx, new PdfString(Encoding.Latin1.GetBytes("datasets")));
            xfaArray.Insert(insertIdx + 1, newStream);
        }
    }

    /// <summary>Ensure the /XFA array carries a "datasets" part, creating an empty
    /// <c>&lt;xfa:datasets&gt;&lt;xfa:data/&gt;&lt;/xfa:datasets&gt;</c> stream and wiring it into the
    /// array (before any "postamble") when absent. Marks the AcroForm dict dirty so the
    /// added array entry + stream are re-serialised on save. Returns the datasets stream,
    /// or null when the form's XFA is not an array (single-stream is handled elsewhere).</summary>
    private PdfStream? EnsureXfaDatasetsStreamInArray()
    {
        var rdr = ResolvedReader;
        if (rdr is null) return null;
        var acroForm = rdr.ResolveDict(rdr.Catalog.Get("AcroForm"));
        if (acroForm is null) return null;
        if (rdr.Resolve(acroForm.Get("XFA")) is not PdfArray xfaArray) return null;

        for (int i = 0; i + 1 < xfaArray.Count; i += 2)
            if (xfaArray[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == "datasets"
                && rdr.Resolve(xfaArray[i + 1]) is PdfStream existing)
                return existing;

        const string xfaNs = "http://www.xfa.org/schema/xfa-data/1.0/";
        var doc = new XmlDocument();
        var dsEl = doc.CreateElement("xfa", "datasets", xfaNs);
        doc.AppendChild(dsEl);
        dsEl.AppendChild(doc.CreateElement("xfa", "data", xfaNs));
        using var ms = new MemoryStream();
        SaveXmlNoBom(doc, ms);
        var bytes = ms.ToArray();
        var newStream = new PdfStream(new PdfDictionary(), bytes);
        newStream.Dict.Set("Length", new PdfInteger(bytes.Length));

        int insertIdx = xfaArray.Count;
        for (int i = 0; i < xfaArray.Count - 1; i += 2)
            if (xfaArray[i] is PdfString s && Encoding.Latin1.GetString(s.Value) == "postamble")
            { insertIdx = i; break; }
        xfaArray.Insert(insertIdx, new PdfString(Encoding.Latin1.GetBytes("datasets")));
        xfaArray.Insert(insertIdx + 1, newStream);

        // The array lives on the AcroForm dict — mark it dirty so the new "datasets"
        // entry (and its inline stream) are written out.
        MarkAcroFormDirty();
        return newStream;
    }

    /// <summary>Mark the catalog's /AcroForm object dirty so an in-place edit of its
    /// /XFA array (a newly-added datasets part) is re-serialised on save.</summary>
    private void MarkAcroFormDirty()
    {
        var rdr = ResolvedReader;
        if (OwnerDocument is null || rdr is null) return;
        if (rdr.Catalog.Get("AcroForm") is not PdfIndirectRef acroRef) return;
        var acroDict = rdr.ResolveDict(acroRef);
        if (acroDict is not null)
            OwnerDocument.MarkDirty(acroRef.ObjectNumber, acroDict);
    }

    /// <summary>
    /// Import data from an imported XML document into a target data node.
    /// Unwraps xfa:data and xfa:datasets wrappers so we don't double-nest
    /// (e.g. avoid &lt;data&gt;&lt;xfa:data&gt;&lt;form1&gt;.).
    /// </summary>
    private static void ImportDataChildren(
        XmlDocument importedXml, XmlDocument targetDoc, XmlNode targetDataNode)
    {
        if (importedXml.DocumentElement is null) return;

        // Unwrap: if root is xfa:datasets, drill into xfa:data child
        var root = importedXml.DocumentElement;
        if (root.LocalName == "datasets")
        {
            var dataChild = root.SelectSingleNode("*[local-name()='data']");
            if (dataChild is not null) root = (XmlElement)dataChild;
        }

        // Unwrap: if root is xfa:data, import its children (the actual form data)
        if (root.LocalName == "data" &&
            (root.NamespaceURI.Contains("xfa") || root.NamespaceURI == ""))
        {
            foreach (XmlNode child in root.ChildNodes)
            {
                var imported = ImportNodeStripNamespaces(targetDoc, child);
                if (imported is not null) targetDataNode.AppendChild(imported);
            }
        }
        else
        {
            // Not a wrapper — import the element directly
            var imported = ImportNodeStripNamespaces(targetDoc, root);
            if (imported is not null) targetDataNode.AppendChild(imported);
        }
    }

    /// <summary>Deep-copy an imported data node into <paramref name="targetDoc"/> with all
    /// namespaces stripped (element + attribute local names only, xmlns declarations dropped),
    /// preserving attributes, text and CDATA. The XFA data model ($data) is namespace-less, so
    /// foreign source XML (e.g. an <c>efile:</c>-namespaced e-file wrapper) must land as
    /// namespace-less nodes for the form's SOM/XPath to resolve them.</summary>
    private static XmlNode? ImportNodeStripNamespaces(XmlDocument targetDoc, XmlNode src)
    {
        switch (src.NodeType)
        {
            case XmlNodeType.Text: return targetDoc.CreateTextNode(src.Value ?? "");
            case XmlNodeType.CDATA: return targetDoc.CreateCDataSection(src.Value ?? "");
            case XmlNodeType.Element: break;
            default: return null; // drop comments / PIs / whitespace-only handled by children walk
        }
        var el = targetDoc.CreateElement(src.LocalName);
        if (src.Attributes is not null)
            foreach (XmlAttribute a in src.Attributes)
            {
                if (a.Prefix == "xmlns" || a.LocalName == "xmlns") continue; // drop ns declarations
                el.SetAttribute(a.LocalName, a.Value);
            }
        foreach (XmlNode c in src.ChildNodes)
        {
            var ic = ImportNodeStripNamespaces(targetDoc, c);
            if (ic is not null) el.AppendChild(ic);
        }
        return el;
    }

    /// <summary>
    /// Set an XFA field value by dotted path in the datasets XML.
    /// </summary>
    public void SetXfaFieldValue(string path, string value)
        => SetXfaFieldValues(new[] { new KeyValuePair<string, string>(path, value) });

    /// <summary>For a static XFA form, copy each AcroForm terminal field's current
    /// value into the XFA datasets, keyed by the field's fully-qualified name, so
    /// the datasets (and <see cref="XFA"/>[field]) stay in sync with values set
    /// through the typed field API. Called automatically before save. Dynamic XFA
    /// forms (whose data is driven by the template) are left untouched.</summary>
    /// <summary>Snapshot the current XFA datasets as a full-path → value map, for
    /// checkbox on/off-token preservation during <see cref="SyncAcroFormToXfa"/>.</summary>
    private Dictionary<string, string> BuildDatasetsValueMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in GetXfaDatasetsFields()) map[kv.Key] = kv.Value;
        return map;
    }

    /// <summary>Push every XFA datasets leaf value into its matching AcroForm field
    /// (static XFA forms only) so the widget representation reflects data that was
    /// replaced wholesale in the datasets (e.g. by <c>ImportXml</c>). Without this the
    /// AcroForm fields keep their old values and the save-time
    /// <see cref="SyncAcroFormToXfa"/> would push those stale values back over the
    /// freshly-imported datasets (notably clobbering checkbox "1"/"0" with "Off").</summary>
    internal void SyncXfaToAcroForm()
    {
        if (Type != FormType.Static) return;
        foreach (var kv in GetXfaDatasetsFields())
            ApplyXfaValueToAcroField(kv.Key, kv.Value);
    }

    internal void SyncAcroFormToXfa()
    {
        if (Type != FormType.Static) return;
        var pairs = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string>? existingDs = null;
        foreach (var field in _fields)
        {
            // Only terminal value-bearing fields map to a datasets leaf. Subform /
            // container nodes (the base Field hierarchy entries) carry no value, and
            // writing an empty value to a container path would wipe its whole subtree.
            // CheckboxField.Value is a `new` shadow → dispatch explicitly; the others
            // override Value and resolve through the base reference.
            string? val;
            switch (field)
            {
                case CheckboxField cb:
                    // An arbitrary non-state value assigned to an XFA checkbox
                    // (e.g. Field.Value = "1234") is stored VERBATIM in the datasets,
                    // even though the AcroForm appearance normalised it to "Off".
                    if (cb.RawNonStateValue is string rawCb)
                    {
                        val = rawCb;
                        break;
                    }
                    // Preserve the datasets' own on/off token (XFA forms conventionally
                    // bind "1"/"0") when the checkbox state already agrees with it — only
                    // overwrite on a genuine state change. Otherwise the AcroForm off
                    // export-name ("Off") would clobber an imported "0".
                    var cbName = field.FullName;
                    existingDs ??= BuildDatasetsValueMap();
                    if (cbName is not null && existingDs.TryGetValue(cbName, out var curVal))
                    {
                        bool curOn = !(string.IsNullOrEmpty(curVal) || curVal == "0"
                            || curVal.Equals("Off", StringComparison.OrdinalIgnoreCase));
                        if (curOn == cb.Checked) continue; // datasets token already matches → keep it
                    }
                    val = cb.Value;
                    break;
                case ChoiceField ch:
                    // Resolve the canonical group field (a radio kid instance carries
                    // no /Opt list) and use the selected option's export value — the
                    // field's own /V can lag the selection for radio groups.
                    var group = FindByName(ch.FullName ?? "") as ChoiceField ?? ch;
                    var sel = group.Selected;
                    val = sel >= 1 && sel <= group.Options.Count ? group.Options[sel].Value : group.Value;
                    break;
                case TextBoxField:
                    val = field.Value;
                    break;
                default:
                    val = null;
                    break;
            }
            if (val is null) continue;
            var name = field.FullName;
            if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
            pairs.Add(new KeyValuePair<string, string>(name, val));
        }
        if (pairs.Count > 0) SetXfaFieldValues(pairs);
    }

    /// <summary>For a static XFA form, push a value written to the XFA datasets
    /// (via <see cref="XFA"/>[field]) into the matching AcroForm field so the two
    /// representations stay in sync. Text fields take the value verbatim; choice
    /// fields select the option whose export value matches; a checkbox is checked
    /// unless the value is empty / "0" / "Off".</summary>
    internal void ApplyXfaValueToAcroField(string name, string value)
    {
        if (Type != FormType.Static || string.IsNullOrEmpty(name)) return;
        switch (FindByName(name))
        {
            case CheckboxField cb:
                cb.Checked = !(string.IsNullOrEmpty(value) || value == "0" || value == "Off");
                break;
            case ChoiceField ch:
                for (int i = 1; i <= ch.Options.Count; i++)
                {
                    if (ch.Options[i].Value == value) { ch.Selected = i; break; }
                }
                break;
            case TextBoxField tb:
                // Honour the field's /MaxLen: an imported value longer than the
                // field allows is truncated to fit (as a viewer would on entry).
                tb.Value = tb.MaxLen > 0 && value is not null && value.Length > tb.MaxLen
                    ? value.Substring(0, tb.MaxLen)
                    : value;
                break;
        }
    }

    /// <summary>Set several XFA field values in one datasets parse/serialise cycle.
    /// Much cheaper than calling <see cref="SetXfaFieldValue"/> per field when
    /// importing a whole form.</summary>
    public void SetXfaFieldValues(IReadOnlyList<KeyValuePair<string, string>> values)
    {
        if (values is null || values.Count == 0) return;
        var (stream, xml) = GetXfaPart("datasets");
        if (xml is null || stream is null)
        {
            // Fallback: XFA might be a single stream (not an array with named parts)
            var reader = ResolvedReader;
            if (reader is not null)
            {
                var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
                if (acroForm is not null && reader.Resolve(acroForm.Get("XFA")) is PdfStream singleStream)
                {
                    var data = reader.DecodeStream(singleStream);
                    xml = Encoding.UTF8.GetString(data);
                    stream = singleStream;
                }
            }
        }
        if (xml is null || stream is null)
        {
            // XFA is an array with no "datasets" part (a template-only dynamic form):
            // create an empty datasets packet and wire it into the /XFA array so the
            // value has somewhere to persist.
            stream = EnsureXfaDatasetsStreamInArray();
            if (stream is not null) xml = Encoding.UTF8.GetString(stream.RawData);
        }
        if (xml is null || stream is null) return;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var changed = false;
            foreach (var pair in values)
            {
                // Prefer the field's own SOM path; if that names no existing datasets node, fall
                // back to the template-resolved data path (honours a <bind match="dataRef"> + skips
                // bind="none" subforms — the SAME mapping the read path uses) BEFORE creating a new
                // node, so a bound field (e.g. filerName → <sarx:FilerName>) lands on the node the
                // reader returns rather than spawning a stray sibling.
                var node = FindXfaNode(doc, pair.Key);
                if (node is null && ResolveSomToDataPath(pair.Key) is { } resolved)
                    node = FindXfaNode(doc, resolved);
                node ??= CreateXfaNodePath(doc, pair.Key);
                if (node is null) continue;
                node.InnerText = pair.Value;
                changed = true;
            }
            if (!changed) return;

            // Write the modified XML back to the stream (uncompressed).
            using var ms = new MemoryStream();
            SaveXmlNoBom(doc, ms);
            var newData = ms.ToArray();
            stream.ReplaceData(newData);
            stream.Dict.Set("Length", new PdfInteger(newData.Length));
            stream.Dict.Remove("Filter");
            MarkXfaStreamDirty(stream);
        }
        catch { }
    }

    /// <summary>Write a base64 image into the XFA datasets node for
    /// <paramref name="path"/> and tag it with the given <paramref name="contentType"/>
    /// (e.g. <c>image/jpg</c>) — how XFA image fields carry their picture. Returns
    /// false when there is no datasets packet or the node can't be resolved.</summary>
    public bool SetXfaFieldImage(string path, string base64, string contentType)
    {
        var (stream, xml) = GetXfaPart("datasets");
        if (xml is null || stream is null)
        {
            // Fallback: XFA might be a single stream (not an array with named parts)
            var reader = ResolvedReader;
            if (reader is not null)
            {
                var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
                if (acroForm is not null && reader.Resolve(acroForm.Get("XFA")) is PdfStream singleStream)
                {
                    xml = Encoding.UTF8.GetString(reader.DecodeStream(singleStream));
                    stream = singleStream;
                }
            }
        }
        if (xml is null || stream is null) return false;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            if ((FindXfaNode(doc, path) ?? CreateXfaNodePath(doc, path)) is not XmlElement el)
                return false;
            el.InnerText = base64;
            el.SetAttribute("contentType", contentType);

            using var ms = new MemoryStream();
            SaveXmlNoBom(doc, ms);
            var newData = ms.ToArray();
            stream.ReplaceData(newData);
            stream.Dict.Set("Length", new PdfInteger(newData.Length));
            stream.Dict.Remove("Filter");
            MarkXfaStreamDirty(stream);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Mark an XFA stream as dirty so incremental save includes it.
    /// Scans the xref table to find the object number for the stream.
    /// </summary>
    private void MarkXfaStreamDirty(PdfStream stream)
    {
        var dirtyReader = ResolvedReader;
        if (OwnerDocument is null || dirtyReader is null) return;
        foreach (var entry in dirtyReader.XRefTable.Entries.Values)
        {
            var resolved = dirtyReader.Resolve(
                new PdfIndirectRef(entry.ObjectNumber, 0));
            if (ReferenceEquals(resolved, stream))
            {
                OwnerDocument.MarkDirty(entry.ObjectNumber, stream);
                return;
            }
            // Also check if the resolved object's Dict matches the stream's Dict
            if (resolved is PdfStream s && ReferenceEquals(s.Dict, stream.Dict))
            {
                OwnerDocument.MarkDirty(entry.ObjectNumber, stream);
                return;
            }
        }
    }

    private static XmlNode? FindXfaNode(XmlDocument doc, string path)
    {
        var parts = SplitSomPath(path);
        XmlNode? current = doc.DocumentElement;

        // First descend into the xfa:data element if present.
        // Prefer the <data> element inside <datasets> (not config's <data>).
        if (current is not null)
        {
            XmlNode? dataNode = null;
            // Try to find <datasets>/<data> first
            var datasetsNode = current.SelectSingleNode("//*[local-name()='datasets']");
            if (datasetsNode is not null)
                dataNode = datasetsNode.SelectSingleNode("*[local-name()='data']");
            // Fallback: find <data> that contains form-like children (not config <data>)
            if (dataNode is null)
            {
                var allData = current.SelectNodes("//*[local-name()='data']");
                if (allData is not null)
                {
                    foreach (XmlNode d in allData)
                    {
                        // Skip config <data> — it has adjustData, xsl etc. as children
                        // The real XFA data has form-field-like children
                        if (d.ParentNode is not null && d.ParentNode.LocalName == "datasets")
                        {
                            dataNode = d;
                            break;
                        }
                    }
                    // Last resort: use first <data> with child elements matching path start
                    if (dataNode is null && allData.Count > 0)
                    {
                        var firstPart = parts[0];
                        var partMatch = Regex.Match(firstPart, @"^(.+)\[(\d+)\]$");
                        var partName = partMatch.Success ? partMatch.Groups[1].Value : firstPart;
                        foreach (XmlNode d in allData)
                        {
                            if (FindChildrenByLocalName(d, partName).Count > 0)
                            {
                                dataNode = d;
                                break;
                            }
                        }
                    }
                    dataNode ??= allData.Count > 0 ? allData[0] : null;
                }
            }
            if (dataNode is not null) current = dataNode;
        }

        // Try strict path walk first
        var root = current;
        foreach (var part in parts)
        {
            if (current is null) break;
            var match = Regex.Match(part, @"^(.+)\[(\d+)\]$");
            if (match.Success)
            {
                var name = match.Groups[1].Value;
                var idx = int.Parse(match.Groups[2].Value);
                var nodes = FindChildrenByLocalName(current, name);
                // XFA occurrence binding: when the template repeats a field name
                // (Season[0], Season[1]) but the datasets carries fewer data nodes, the
                // surplus instances bind to the existing (often single) node rather than
                // resolving to nothing. Fall back to the last available node when the
                // requested index is out of range. An in-range index is unchanged, so
                // fields that DO carry one node per instance still resolve distinctly.
                if (idx < nodes.Count) current = nodes[idx];
                else current = nodes.Count > 0 ? nodes[nodes.Count - 1] : null;
            }
            else
            {
                var nodes = FindChildrenByLocalName(current, part);
                current = nodes.Count > 0 ? nodes[0] : null;
            }
        }

        if (current is not null)
            return current;

        // Fallback: XFA data XML may be flat (template path segments don't map to data hierarchy).
        // Search for the last segment as a descendant of the data root.
        if (root is not null && parts.Length > 0)
        {
            var lastPart = parts[^1];
            var idxMatch = Regex.Match(lastPart, @"^(.+)\[(\d+)\]$");
            var leafName = idxMatch.Success ? idxMatch.Groups[1].Value : lastPart;
            var leafIdx = idxMatch.Success ? int.Parse(idxMatch.Groups[2].Value) : 0;
            var allMatches = root.SelectNodes($".//*[local-name()='{leafName}']");
            if (allMatches is not null && allMatches.Count > 0)
                // XFA occurrence binding (see the strict walk above): a repeated template
                // instance whose datasets has fewer data nodes binds to the existing node
                // rather than resolving to nothing, so clamp an out-of-range index to the
                // last available match. An in-range index resolves distinctly as before.
                return allMatches[leafIdx < allMatches.Count ? leafIdx : allMatches.Count - 1];
        }

        return null;
    }

    /// <summary>
    /// Create the full path of nodes in the XFA data section.
    /// Used when setting a value on a node that doesn't exist yet.
    /// </summary>
    private static XmlNode? CreateXfaNodePath(XmlDocument doc, string path)
    {
        XmlNode? current = doc.DocumentElement;
        if (current is null) return null;

        // Find the correct <data> node (inside <datasets>, not config)
        XmlNode? dataNode = null;
        var datasetsNode = current.SelectSingleNode("//*[local-name()='datasets']");
        if (datasetsNode is not null)
        {
            dataNode = datasetsNode.SelectSingleNode("*[local-name()='data']");
            if (dataNode is null)
            {
                // Create <xfa:data> inside <datasets>
                var ns = datasetsNode.NamespaceURI;
                dataNode = doc.CreateElement("xfa", "data", ns);
                datasetsNode.AppendChild(dataNode);
            }
        }
        else
        {
            // Fallback: find any <data> whose parent is datasets
            var allData = current.SelectNodes("//*[local-name()='data']");
            if (allData is not null)
            {
                foreach (XmlNode d in allData)
                {
                    if (d.ParentNode?.LocalName == "datasets") { dataNode = d; break; }
                }
            }
            if (dataNode is null)
            {
                dataNode = current.SelectSingleNode("//*[local-name()='data']");
            }
        }
        if (dataNode is null) return null;
        current = dataNode;

        var parts = SplitSomPath(path);
        foreach (var part in parts)
        {
            var match = Regex.Match(part, @"^(.+)\[(\d+)\]$");
            var name = match.Success ? match.Groups[1].Value : part;
            var idx = match.Success ? int.Parse(match.Groups[2].Value) : 0;

            var children = FindChildrenByLocalName(current, name);
            // Create missing nodes up to the required index
            while (children.Count <= idx)
            {
                var newNode = doc.CreateElement(name);
                current.AppendChild(newNode);
                children.Add(newNode);
            }
            current = children[idx];
        }
        return current;
    }

    private static List<XmlNode> FindChildrenByLocalName(XmlNode parent, string localName)
    {
        var result = new List<XmlNode>();
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.LocalName == localName)
                result.Add(child);
        }
        // Also search all descendants if not found in direct children
        if (result.Count == 0)
        {
            var descendants = parent.SelectNodes($".//*[local-name()='{localName}']");
            if (descendants is not null)
                foreach (XmlNode d in descendants)
                    result.Add(d);
        }
        return result;
    }

    private static string StripBom(string s) =>
        s.Length > 0 && s[0] == '\uFEFF' ? s.Substring(1) : s;

    /// <summary>Save XmlDocument to a stream without BOM.</summary>
    private static void SaveXmlNoBom(XmlDocument doc, MemoryStream ms)
    {
        var settings = new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false
        };
        using var writer = System.Xml.XmlWriter.Create(ms, settings);
        doc.Save(writer);
    }

    private static string? FindXfaNodeValue(string xml, string path)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var node = FindXfaNode(doc, path);
            return node?.InnerText;
        }
        catch { return null; }
    }

    /// <summary>XFA accessor exposing the underlying XML packets (template,
    /// datasets, …). Returns <c>null</c> when the form has no XFA part —
    /// mirrors the Aspose.Pdf behaviour so callers can branch on
    /// <c>Form.XFA is null</c>.
    /// </summary>
    public XFA? XFA => IsXfa ? (_xfa ??= new XFA(this)) : null;
    private XFA? _xfa;

    private PdfReader? _reader;
    private PdfDictionary? _acroForm;
    internal void SetReader(PdfReader reader) => _reader = reader;

    /// <summary>Resolve the AcroForm dictionary backing this form (preferring the
    /// one captured at load time, then the document catalog's /AcroForm).</summary>
    private PdfDictionary? ResolveAcroForm()
    {
        if (_acroForm is not null) return _acroForm;
        var reader = _reader ?? OwnerDocument?.Reader;
        return reader is null ? null : reader.ResolveDict(reader.Catalog.Get("AcroForm"));
    }

    /// <summary>
    /// Resolve the PDF reader: prefer explicitly set reader, fall back to OwnerDocument's reader.
    /// </summary>
    private PdfReader? ResolvedReader => _reader ?? OwnerDocument?.Reader;

    /// <summary>
    /// Flatten all form fields — render their visual appearance into page content
    /// and remove the interactive form. After flattening, fields are no longer editable.
    /// Uses the owning document from the form dictionary.
    /// </summary>
    public void Flatten()
    {
        var doc = _ownerDocument ?? throw new InvalidOperationException("Form is not associated with a Document.");
        Flatten(doc);
    }

    /// <summary>
    /// Flatten all form fields — render their visual appearance into page content
    /// and remove the interactive form. After flattening, fields are no longer editable.
    /// </summary>
    public void Flatten(Document document)
    {
        Flatten(document, settings: null);
    }

    /// <summary>Settings-aware overload exposed to Document.Flatten(settings).</summary>
    internal void FlattenWithSettings(Document document, FlattenSettings? settings)
        => Flatten(document, settings);

    /// <summary>Internal entry point that honours <paramref name="settings"/>.
    /// When settings.UpdateAppearances is true (or the flag is unspecified —
    /// Aspose.Pdf treats Flatten() as always refreshing appearances
    /// from the current field values) each field's /AP/N is rebuilt from its
    /// current /V before the page's widgets are folded into the page content.
    /// Without this, a flatten of a PDF whose fields were programmatically
    /// re-valued shows the original (stale) appearance.</summary>
    internal void Flatten(Document document, FlattenSettings? settings)
        => Flatten(document, settings, frmStartIndex: 0, flattenNonWidgets: false);

    /// <summary>Flatten honouring <paramref name="settings"/>. <paramref name="frmStartIndex"/>
    /// bases the flattened-field FRM{n} numbering (0 for document/form flatten, 1 for the facade
    /// FlattenAllFields). <paramref name="flattenNonWidgets"/> also folds non-widget annotations
    /// (e.g. FreeText) into the page content so their FRM index lines up with /Annots — the facade
    /// FlattenAllFields does this; document/form flatten leaves them for the annotation path.
    /// Matches Aspose.Pdf.</summary>
    internal void Flatten(Document document, FlattenSettings? settings, int frmStartIndex,
        bool flattenNonWidgets)
    {
        // 1. Force each field's appearance to reflect the current value.
        //    The per-type GenerateAppearance short-circuits when /AP is present,
        //    so for text/choice/check/button/radio fields we delete the existing
        //    /AP and re-emit — that's the only way to capture an updated value.
        var refresh = settings is null || settings.UpdateAppearances;
        if (refresh)
        {
            // Multiple Field wrappers can share the same underlying PdfDictionary
            // when _fields collects a field both directly from /AcroForm/Fields
            // AND through a kid-widget walk that points back at the same dict.
            // Regenerating twice on the same dict can clobber a successful
            // first regen if the second regen (with a stale /V or zero-size
            // /Rect on a duplicate wrapper) early-exits after the
            // Dict.Remove("AP") step. Dedupe by dict-identity so each
            // dictionary's /AP is generated exactly once.
            var seenDicts = new System.Collections.Generic.HashSet<PdfDictionary>(
                System.Collections.Generic.ReferenceEqualityComparer.Instance);
            foreach (var f in _fields)
            {
                if (!seenDicts.Add(f.Dict)) continue;
                RegenerateAppearanceForFlatten(f);
            }
        }

        // 2. Hoist /AcroForm/DR fonts into each page's /Resources so the
        //    appearance Form-XObject's content stream (which references fonts by
        //    AcroForm-DR alias like /Helv) still resolves after step 4 strips
        //    the /AcroForm dict. /AP streams without their own /Resources
        //    fall back to the page's resources per PDF 32000-2 § 8.10.
        var acroForm = document.Reader.ResolveDict(document.Catalog.Get("AcroForm"));
        var drFonts = document.Reader.ResolveDict(document.Reader.ResolveDict(acroForm?.Get("DR"))?.Get("Font"));
        if (drFonts is not null)
        {
            foreach (var page in document.Pages)
                HoistDrFontsIntoPageResources(page, drFonts, document.Reader);
        }

        var hideButtons = settings is { HideButtons: true };
        foreach (var page in document.Pages)
        {
            FlattenFieldsOnPage(page, hideButtons, frmStartIndex, flattenNonWidgets);
        }

        // Remove AcroForm from catalog
        document.Catalog.Remove("AcroForm");

        // Empty the /Fields array on the (now-detached) AcroForm dict too: Count reads the
        // AcroForm's /Fields (this Form's cached _acroForm, or the catalog's) before falling
        // back to _fields, so without this a flattened form still reports its old field count.
        foreach (var af in new[] { acroForm, _acroForm })
            if (af is not null && af.ContainsKey("Fields"))
                af.Set("Fields", new PdfArray());

        // Clear cached field list so Count reflects the flattened state
        _fields.Clear();
    }

    /// <summary>Re-emit the field's /AP/N based on its current /V by dropping
    /// the existing /AP and re-invoking the per-type generator. Each generator
    /// short-circuits when /AP is present so the value-driven appearance was
    /// only ever written on initial Form.Add (opening a PDF whose
    /// fields already have stale /AP from a prior save and flattening shipped
    /// the stale visuals).
    ///
    /// Caller is expected to dedupe by dict identity since one Field wrapper
    /// commonly shares its underlying PdfDictionary with another wrapper
    /// reached through a Kids walk. Removing /AP unconditionally on every
    /// _fields entry without dedupe would clobber a successfully-regenerated
    /// dict when its second wrapper is processed.</summary>
    private static void RegenerateAppearanceForFlatten(Field field)
    {
        var oldAp = field.Dict.Get("AP");
        // Preserve an existing appearance that carries a non-identity /Matrix (e.g. a field on a
        // /Rotate page): the per-type generator re-emits appearances axis-aligned, which would drop
        // the /Matrix and mis-place the flattened form (and can split the value's words). The
        // existing /AP already reflects the value, so keep it verbatim.
        if (HasNonIdentityAppearanceMatrix(field, oldAp)) return;
        // Drop and re-emit so the appearance reflects the current value. If the per-type
        // generator can't produce a new /AP (e.g. an Off checkbox / button it doesn't
        // synthesise), keep the original so flatten can still render it.
        field.Dict.Remove("AP");
        field.GenerateAppearance();
        if (field.Dict.Get("AP") is null && oldAp is not null)
            field.Dict.Set("AP", oldAp);
    }

    /// <summary>True when the field's /AP /N appearance carries a /Matrix that isn't the identity
    /// (a rotated/skewed/scaled appearance, e.g. a widget on a /Rotate page).</summary>
    private static bool HasNonIdentityAppearanceMatrix(Field field, PdfObject? apObj)
    {
        var reader = field.Reader;
        var ap = reader.ResolveDict(apObj);
        if (ap is null) return false;
        var n = reader.Resolve(ap.Get("N"));
        var ns = n as PdfStream;
        if (ns is null && n is PdfDictionary states)
            foreach (var k in states.Keys) { ns = reader.ResolveStream(states.Get(k)); if (ns is not null) break; }
        if (ns is null || reader.Resolve(ns.Dict.Get("Matrix")) is not PdfArray)
            return false;
        var m = ReadAppearanceMatrix(ns.Dict, reader);
        return System.Math.Abs(m[0] - 1) > 1e-6 || System.Math.Abs(m[3] - 1) > 1e-6
            || System.Math.Abs(m[1]) > 1e-6 || System.Math.Abs(m[2]) > 1e-6;
    }

    /// <summary>Copy every font entry from the AcroForm /DR /Font dict into the
    /// page's /Resources/Font (without overwriting an existing entry of the
    /// same alias). Lets the appearance Form-XObject /Do reference resolve
    /// against page resources after AcroForm is stripped at flatten end.</summary>
    private static void HoistDrFontsIntoPageResources(Page page, PdfDictionary drFonts, PdfReader reader)
    {
        var pageResources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (pageResources is null)
        {
            pageResources = new PdfDictionary();
            page.Dict.Set("Resources", pageResources);
        }
        var pageFonts = reader.ResolveDict(pageResources.Get("Font"));
        if (pageFonts is null)
        {
            pageFonts = new PdfDictionary();
            pageResources.Set("Font", pageFonts);
        }
        foreach (var alias in drFonts.Keys)
        {
            if (pageFonts.ContainsKey(alias)) continue;
            var entry = drFonts.Get(alias);
            if (entry is not null) pageFonts.Set(alias, entry);
        }
    }

    /// <summary>
    /// Export form field data in FDF (Forms Data Format) per PDF spec §12.7.7.
    /// </summary>
    public byte[] ExportFdf()
    {
        var sb = new StringBuilder();
        sb.Append("%FDF-1.2\n");
        sb.Append("1 0 obj\n");
        sb.Append("<< /FDF << /Fields [\n");

        foreach (var field in _fields)
        {
            var name = field.FullName ?? field.PartialName;
            if (name is null) continue;
            var value = field.Value ?? "";
            sb.Append("  << /T (");
            sb.Append(EscapeFdfString(name));
            sb.Append(") /V (");
            sb.Append(EscapeFdfString(value));
            sb.Append(") >>\n");
        }

        sb.Append("] >> >>\n");
        sb.Append("endobj\n");
        sb.Append("trailer\n");
        sb.Append("<< /Root 1 0 R >>\n");
        sb.Append("%%EOF\n");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Import form field data from FDF bytes.
    /// </summary>
    public void ImportFdf(byte[] fdfData)
    {
        var text = Encoding.Latin1.GetString(fdfData);
        var pairs = ParseFdfFields(text);
        foreach (var (name, value) in pairs)
        {
            var field = FindByName(name);
            if (field is null) continue;
            if (field is CheckboxField cb)
            {
                // FDF carries a checkbox's export value verbatim: check the box only
                // when the value names a declared on-state. "Off", empty, and any
                // non-matching value (e.g. "No" on a Yes/Off box) leave it unchecked.
                // Routing through the generic Value setter would instead coerce every
                // non-"Off" value to the on-state and wrongly tick the box.
                if (cb.IsDeclaredOnState(value)) cb.Value = value;
                else cb.Checked = false;
            }
            else
                field.Value = value;
        }
    }

    /// <summary>
    /// Export form field data in XFDF (XML Forms Data Format).
    /// </summary>
    public string ExportXfdf()
    {
        var ns = XNamespace.Get("http://ns.adobe.com/xfdf/");
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "xfdf",
                new XElement(ns + "fields",
                    _fields
                        .Where(f => f.FullName is not null || f.PartialName is not null)
                        .Select(f => new XElement(ns + "field",
                            new XAttribute("name", f.FullName ?? f.PartialName!),
                            new XElement(ns + "value", f.Value ?? ""))))));

        return doc.Declaration + "\n" + doc;
    }

    /// <summary>
    /// Import form field data from XFDF XML string.
    /// </summary>
    public void ImportXfdf(string xfdfXml)
    {
        var doc = XDocument.Parse(xfdfXml);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var fields = doc.Root?.Element(ns + "fields");
        if (fields is null) return;
        ImportXfdfFields(fields, ns, parentPath: null);
    }

    /// <summary>
    /// Recursively import the <c>&lt;field&gt;</c> children of <paramref name="container"/>.
    /// Aspose-exported XFDF is flat (<c>&lt;field name="SA Datum.0"&gt;</c>), while
    /// Acrobat nests partial names (<c>&lt;field name="SA Datum"&gt;&lt;field name="0"&gt;</c>),
    /// so the full field name is the dotted join of the ancestor <c>name</c> attributes.
    /// A field element carries its value in a direct <c>&lt;value&gt;</c> child; nested
    /// <c>&lt;field&gt;</c> elements (or a defensive <c>&lt;fields&gt;</c> wrapper) describe
    /// child fields.
    /// </summary>
    private void ImportXfdfFields(XElement container, XNamespace ns, string? parentPath)
    {
        foreach (var fieldEl in container.Elements(ns + "field"))
        {
            var name = fieldEl.Attribute("name")?.Value;
            if (name is null) continue;
            var path = parentPath is null ? name : parentPath + "." + name;

            var value = fieldEl.Element(ns + "value")?.Value;
            if (value is not null)
            {
                var field = FindByName(path);
                if (field is not null)
                    field.Value = value;
            }

            // Recurse: Acrobat nests <field> directly; some writers wrap them in <fields>.
            ImportXfdfFields(fieldEl, ns, path);
            var nestedWrap = fieldEl.Element(ns + "fields");
            if (nestedWrap is not null)
                ImportXfdfFields(nestedWrap, ns, path);
        }
    }

    private static string EscapeFdfString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static List<(string name, string value)> ParseFdfFields(string fdf)
    {
        // FDF /Fields is a tree: each entry has an optional /T (partial name),
        // an optional /V (value), and an optional /Kids (child entries). The full
        // field name of a leaf is the dotted join of /T values from the root.
        // Acrobat exports hierarchical fields this way — e.g.
        //   <</Kids[<</T(0)/V(01-09-2010)>> ...]/T(SA Datum Sollicitatie)>>
        // resolves to "SA Datum Sollicitatie.0". A flat <</T(.)/V(.)>> scan would
        // mistake the kids' partial names ("0".."6") for full names.
        var result = new List<(string, string)>();
        var fieldsIdx = fdf.IndexOf("/Fields", StringComparison.Ordinal);
        if (fieldsIdx < 0) return result;
        var pos = fdf.IndexOf('[', fieldsIdx);
        if (pos < 0) return result;
        pos++; // step past '['
        ParseFdfFieldsArray(fdf, ref pos, parentPath: null, result);
        return result;
    }

    private static void ParseFdfFieldsArray(string t, ref int pos, string? parentPath,
        List<(string, string)> result)
    {
        while (pos < t.Length)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            if (t[pos] == ']') { pos++; return; }
            if (pos + 1 < t.Length && t[pos] == '<' && t[pos + 1] == '<')
            {
                pos += 2;
                ParseFdfFieldDict(t, ref pos, parentPath, result);
            }
            else
            {
                pos++; // tolerate stray bytes
            }
        }
    }

    private static void ParseFdfFieldDict(string t, ref int pos, string? parentPath,
        List<(string, string)> result)
    {
        string? partialName = null;
        string? value = null;
        int kidsStart = -1;

        while (pos < t.Length)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            if (pos + 1 < t.Length && t[pos] == '>' && t[pos + 1] == '>') { pos += 2; break; }
            if (t[pos] != '/') { pos++; continue; }
            pos++; // step past '/'
            int kStart = pos;
            while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++;
            var key = t.Substring(kStart, pos - kStart);
            FdfSkipWS(t, ref pos);
            if (key == "T" && pos < t.Length && t[pos] == '(')
                partialName = FdfReadStringLiteral(t, ref pos);
            else if (key == "V")
                value = FdfReadValue(t, ref pos);
            else if (key == "Kids" && pos < t.Length && t[pos] == '[')
            {
                kidsStart = pos + 1; // remember; consume below
                FdfSkipValue(t, ref pos);
            }
            else
                FdfSkipValue(t, ref pos);
        }

        var fullPath = (parentPath, partialName) switch
        {
            (null, null) => null,
            (null, _) => partialName,
            (_, null) => parentPath,
            _ => $"{parentPath}.{partialName}",
        };

        if (kidsStart >= 0)
        {
            int kp = kidsStart;
            ParseFdfFieldsArray(t, ref kp, fullPath, result);
        }
        else if (fullPath is not null)
        {
            result.Add((fullPath, value ?? ""));
        }
    }

    /// <summary>Read a /V value: either a string literal <c>(...)</c> or a name
    /// object <c>/Off</c> (checkbox states). Returns the decoded text; the name
    /// object's value is returned without the leading slash.</summary>
    private static string? FdfReadValue(string t, ref int pos)
    {
        FdfSkipWS(t, ref pos);
        if (pos >= t.Length) return null;
        if (t[pos] == '(') return FdfReadStringLiteral(t, ref pos);
        if (t[pos] == '/')
        {
            pos++; // step past '/'
            int s = pos;
            while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++;
            return t.Substring(s, pos - s);
        }
        FdfSkipValue(t, ref pos);
        return null;
    }

    private static void FdfSkipWS(string t, ref int pos)
    {
        while (pos < t.Length)
        {
            char c = t[pos];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\0') pos++;
            else if (c == '%') { while (pos < t.Length && t[pos] != '\n') pos++; }
            else break;
        }
    }

    private static bool IsFdfDelimOrWS(char c) =>
        c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\0'
        || c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']'
        || c == '/' || c == '%';

    private static string FdfReadStringLiteral(string t, ref int pos)
    {
        pos++; // step past '('
        var sb = new StringBuilder();
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            char c = t[pos++];
            if (c == '\\')
            {
                if (pos >= t.Length) break;
                char esc = t[pos++];
                switch (esc)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '(': sb.Append('('); break;
                    case ')': sb.Append(')'); break;
                    case '\\': sb.Append('\\'); break;
                    case '\n': break;
                    case '\r': if (pos < t.Length && t[pos] == '\n') pos++; break;
                    default: sb.Append(esc); break;
                }
            }
            else if (c == '(') { depth++; sb.Append(c); }
            else if (c == ')') { depth--; if (depth > 0) sb.Append(c); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static void FdfSkipValue(string t, ref int pos)
    {
        FdfSkipWS(t, ref pos);
        if (pos >= t.Length) return;
        char c = t[pos];
        if (c == '(') { FdfReadStringLiteral(t, ref pos); }
        else if (c == '[') { FdfSkipArray(t, ref pos); }
        else if (c == '<' && pos + 1 < t.Length && t[pos + 1] == '<') { FdfSkipDict(t, ref pos); }
        else if (c == '<') { pos++; while (pos < t.Length && t[pos] != '>') pos++; if (pos < t.Length) pos++; }
        else if (c == '/') { pos++; while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++; }
        else { while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++; }
    }

    private static void FdfSkipArray(string t, ref int pos)
    {
        pos++; // step past '['
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            char c = t[pos];
            if (c == '[') { depth++; pos++; }
            else if (c == ']') { depth--; pos++; }
            else if (c == '(') { FdfReadStringLiteral(t, ref pos); }
            else if (c == '<' && pos + 1 < t.Length && t[pos + 1] == '<') { FdfSkipDict(t, ref pos); }
            else pos++;
        }
    }

    private static void FdfSkipDict(string t, ref int pos)
    {
        pos += 2; // step past '<<'
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            if (pos + 1 < t.Length && t[pos] == '<' && t[pos + 1] == '<') { depth++; pos += 2; }
            else if (pos + 1 < t.Length && t[pos] == '>' && t[pos + 1] == '>') { depth--; pos += 2; }
            else if (t[pos] == '(') { FdfReadStringLiteral(t, ref pos); }
            else if (t[pos] == '[') { FdfSkipArray(t, ref pos); }
            else pos++;
        }
    }

    /// <summary>True when the widget belongs to a push-button field — field type
    /// /Btn with the Pushbutton flag (Ff bit 17, value 1&lt;&lt;16) set. /FT and /Ff
    /// are inherited, so walk up the /Parent chain when the widget itself omits them.</summary>
    private static bool IsPushButtonWidget(PdfDictionary widget, Aspose.Pdf.IO.PdfReader reader)
    {
        string? ft = null;
        long ff = 0;
        var dict = widget;
        var guard = 0;
        while (dict is not null && guard++ < 32)
        {
            ft ??= dict.GetName("FT");
            if (dict.Get("Ff") is { } ffObj && reader.Resolve(ffObj) is PdfInteger ffi && ff == 0)
                ff = ffi.Value;
            if (ft is not null && ff != 0) break;
            dict = reader.ResolveDict(dict.Get("Parent"));
        }
        return ft == "Btn" && (ff & (1L << 16)) != 0;
    }

    private void FlattenFieldsOnPage(Page page, bool hideButtons = false, int frmStartIndex = 0,
        bool flattenNonWidgets = false)
    {
        var reader = page.Reader;
        var annotsObj = reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annotsObj is null) return;

        var remaining = new PdfArray();
        var appendContent = new System.IO.MemoryStream();
        // Flattened field appearances are registered as FRM{n} in /Annots order so a caller can
        // look each one up by position. The base index is path-dependent (matches Aspose.PDF for
        // .NET): the document/form flatten numbers from FRM0, the facade FlattenAllFields from FRM1.
        int frmCounter = frmStartIndex;
        foreach (var annotRef in annotsObj)
        {
            var annotDict = reader.ResolveDict(annotRef);
            if (annotDict is null)
            {
                remaining.Add(annotRef);
                continue;
            }

            var subtype = annotDict.GetName("Subtype");
            // Merged field-widget dicts may omit /Subtype but still have field
            // properties (/FT, /T, or /Parent pointing to a field hierarchy).
            bool isWidget = subtype == "Widget"
                || (subtype is null && (annotDict.ContainsKey("FT") || annotDict.ContainsKey("T")
                    || annotDict.ContainsKey("Parent")));
            // Form-field flatten (document/form) folds only widgets into the page content and
            // leaves other annotation types (markup, line, link, …) for their own annotation
            // path. FlattenAllFields (flattenNonWidgets) instead flattens every annotation with
            // an appearance so the FRM{n} index lines up with the page /Annots order.
            if (!isWidget && !flattenNonWidgets)
            {
                remaining.Add(annotRef);
                continue;
            }

            // HideButtons: drop push-button widgets entirely (neither rendered
            // into page content nor kept as an annotation) so the flattened
            // output shows no buttons.
            if (isWidget && hideButtons && IsPushButtonWidget(annotDict, reader))
                continue;

            // Build the content fragment that folds this widget's appearance into the page
            // (registering it as FRM{n}). Null when the widget has no usable appearance — a
            // widget is then dropped (orphan field), a non-widget kept in /Annots.
            var fragment = BuildWidgetFlattenFragment(page.Dict, annotDict, reader, $"FRM{frmCounter}");
            if (fragment is null)
            {
                if (!isWidget) remaining.Add(annotRef);
                continue;
            }
            frmCounter++;
            var writer = new System.IO.StreamWriter(appendContent, System.Text.Encoding.ASCII, leaveOpen: true);
            writer.Write(fragment);
            writer.Flush();
        }

        // Update page annotations (remove flattened widgets)
        if (remaining.Count > 0)
            page.Dict.Set("Annots", remaining);
        else
            page.Dict.Remove("Annots");

        // Append the flattened content to the page. /Contents may be a single stream
        // OR an array of streams (PDF 32000-2 § 7.7.3.3) — Page.GetContentStreamBytes
        // concatenates the array case correctly; doing it inline only handled the
        // single-stream case, silently dropping the original page content for any
        // array-of-streams page.
        if (appendContent.Length > 0)
        {
            var existingData = page.GetContentStreamBytes() ?? [];

            // Bracket the original page content in a balanced q … Q before appending the
            // flattened field fragments. A page content stream may leave the CTM in a
            // non-default state — e.g. a leading global "0.12 0 0 0.12 0 0 cm" scale that
            // is never wrapped in q/Q (some form authoring tools draw the whole page in an
            // 8.33× coordinate space) — so appending fragments raw would draw every widget
            // appearance through that leftover transform (scaled + shoved into a page
            // corner). The outer q/Q restores the base CTM so each "q … cm /FRMn Do Q"
            // fragment is placed at its true /Rect. Harmless when the page content is
            // already clean (a no-op save/restore around it).
            byte[] pre = existingData.Length > 0 ? Encoding.ASCII.GetBytes("q\n") : [];
            byte[] mid = existingData.Length > 0 ? Encoding.ASCII.GetBytes("\nQ\n") : [];
            var frag = appendContent.ToArray();

            var combined = new byte[pre.Length + existingData.Length + mid.Length + frag.Length];
            int off = 0;
            pre.CopyTo(combined, off); off += pre.Length;
            existingData.CopyTo(combined, off); off += existingData.Length;
            mid.CopyTo(combined, off); off += mid.Length;
            frag.CopyTo(combined, off);

            page.SetContentStream(combined);
        }
    }

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
        var nResolved = reader.Resolve(apDict.Get("N"));
        var appearanceStream = nResolved as PdfStream;
        if (appearanceStream is null && nResolved is PdfDictionary stateDict)
        {
            var asName = annotDict.GetName("AS");
            // A widget explicitly in the Off state with no Off appearance of its own (e.g. an
            // unselected radio/checkbox kid whose /AP/N holds only its on-state) draws nothing —
            // skip it. Falling back to its on-state would render an unselected kid as selected.
            if (asName == "Off" && reader.ResolveStream(stateDict.Get("Off")) is null)
                return null;
            if (asName is not null) appearanceStream = reader.ResolveStream(stateDict.Get(asName));
            // Fallback: first non-Off state, then any state.
            if (appearanceStream is null)
                foreach (var key in stateDict.Keys)
                {
                    if (key == "Off") continue;
                    appearanceStream = reader.ResolveStream(stateDict.Get(key));
                    if (appearanceStream is not null) break;
                }
            if (appearanceStream is null)
                foreach (var key in stateDict.Keys)
                {
                    appearanceStream = reader.ResolveStream(stateDict.Get(key));
                    if (appearanceStream is not null) break;
                }
        }
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
        var resources = reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }

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

        var pageResources = reader.ResolveDict(pageDict.Get("Resources"));
        if (pageResources is null)
        {
            pageResources = new PdfDictionary();
            pageDict.Set("Resources", pageResources);
        }

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
        foreach (var f in _fields) yield return Widgetize(f);
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

    // ── Aspose.Pdf shape additions ───────────────────────────────

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
        var entries = new List<FieldExportingData>(_fields.Count + 1);
        var results = new List<FieldSerializationResult>(_fields.Count);
        foreach (var f in _fields)
        {
            entries.Add(FieldJsonExporter.BuildField(f));
            results.Add(new FieldSerializationResult
            {
                FieldFullName = f.FullName ?? f.PartialName ?? string.Empty,
                FieldSerializationStatus = FieldSerializationStatus.Success,
            });
        }
        // Append a single entry carrying the form-level AcroForm dictionary data.
        entries.Add(FieldJsonExporter.BuildAcroForm(ResolveAcroForm(), _reader ?? OwnerDocument?.Reader));
        FieldJsonExporter.Write(stream, entries, indent);
        return results;
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

        if (entry.TryGetProperty("ChildFields", out var kids) && kids.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var kidsArr = new PdfArray();
            foreach (var kid in kids.EnumerateArray())
            {
                var kidDict = ImportFieldEntry(kid, reader, dict, results);
                if (kidDict is not null) kidsArr.Add(kidDict);
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
            if (!apRoundTripped) field.GenerateAppearance();
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

/// <summary>
/// <summary>
/// Represents the type of a PDF form.
/// </summary>
public enum FormType
{
    /// <summary>Standard AcroForm (non-XFA).</summary>
    Standard,
    /// <summary>Static XFA form.</summary>
    Static,
    /// <summary>Dynamic XFA form.</summary>
    Dynamic,
}

/// Provides indexer access to XFA field values by path.
/// </summary>
public sealed class XfaAccessor
{
    // Standard XFA namespaces — used to resolve "tpl:", "xfa:", "datasets:"
    // prefixes in XPath queries against the template/datasets documents.
    private const string TemplateNs = "http://www.xfa.org/schema/xfa-template/2.6/";
    private const string XfaNs = "http://www.xfa.org/schema/xfa-data/1.0/";
    private const string DatasetsNs = "http://www.xfa.org/schema/xfa-datasets/2.6/";

    private readonly Form _form;
    private XmlDocument? _templateDoc;
    private XmlNamespaceManager? _nsManager;
    private string[]? _fieldNames;

    internal XfaAccessor(Form form) => _form = form;

    /// <summary>Get or set an XFA field value by dotted path.</summary>
    public string? this[string path]
    {
        get => _form.GetXfaFieldValue(path);
        set => _form.SetXfaFieldValue(path, value ?? "");
    }

    /// <summary>The owning Form.</summary>
    public Form Form => _form;

    /// <summary>The XFA template root element, or null when no template is
    /// present in the XFA package.</summary>
    public XmlNode? Template => GetTemplateDocument()?.DocumentElement;

    /// <summary>An XmlNamespaceManager pre-bound with the standard XFA
    /// prefixes ("tpl" → template, "xfa" → data, "datasets" → datasets).
    /// Use with XPath queries against <see cref="Template"/>.</summary>
    public XmlNamespaceManager NamespaceManager => GetOrBuildNamespaceManager();

    /// <summary>Method-form alias for <see cref="NamespaceManager"/>.</summary>
    public XmlNamespaceManager GetNamespaceManager() => NamespaceManager;

    /// <summary>Dotted-path field names for every <c>&lt;field&gt;</c>
    /// element in the XFA template, indexed by document order so each
    /// repeated subform/field gets its own <c>[N]</c> suffix.</summary>
    public string[] FieldNames => _fieldNames ??= EnumerateFieldNames();

    /// <summary>Get the template XmlNode for a single field by its dotted
    /// path (e.g. "form1[0].P1[0].SubmitButton[0]"), or null if not
    /// present.</summary>
    public XmlNode? GetFieldTemplate(string fieldName)
    {
        var template = GetTemplateDocument();
        if (template?.DocumentElement is null) return null;
        return WalkTemplateByPath(template.DocumentElement, fieldName);
    }

    /// <summary>All <c>&lt;tpl:field&gt;</c> nodes in the template.</summary>
    public XmlNodeList? GetFieldTemplates()
    {
        var template = GetTemplateDocument();
        return template?.DocumentElement?.SelectNodes("//tpl:field", NamespaceManager);
    }

    /// <summary>Get the XFA data node for a field by dotted path, or null.</summary>
    public XmlNode? GetFieldNode(string fieldName)
    {
        var datasets = Datasets;
        if (datasets is null) return null;
        return WalkDatasetsByPath(datasets, fieldName);
    }

    /// <summary>Marker that callers use after a batch of field updates to
    /// signal "no more updates pending". This implementation does not
    /// cache updates, so the call is a no-op kept for source compatibility.</summary>
    public void EndCachedUpdates() { }

    /// <summary>Get the XFA Datasets as an XmlElement.
    /// If the XFA has a separate "datasets" part, returns its root element.
    /// Otherwise, extracts the datasets element from the full XDP document.</summary>
    public System.Xml.XmlElement? Datasets
    {
        get
        {
            var xml = _form.GetXfaDatasetsXml();
            if (xml is null) return null;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                // If the root is already <xfa:datasets>, return it directly
                var root = doc.DocumentElement;
                if (root is not null && root.LocalName == "datasets")
                    return root;
                // Otherwise, search for the datasets element within the full XDP
                var datasetsNode = root?.SelectSingleNode("//*[local-name()='datasets']") as XmlElement;
                if (datasetsNode is not null) return datasetsNode;
                return root;
            }
            catch { return null; }
        }
    }

    private XmlDocument? GetTemplateDocument()
    {
        if (_templateDoc is not null) return _templateDoc;
        var xml = _form.GetXfaTemplateXml();
        if (xml is null) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            _templateDoc = doc;
            return doc;
        }
        catch { return null; }
    }

    private XmlNamespaceManager GetOrBuildNamespaceManager()
    {
        if (_nsManager is not null) return _nsManager;
        var doc = GetTemplateDocument();
        var nt = doc?.NameTable ?? new NameTable();
        var mgr = new XmlNamespaceManager(nt);
        mgr.AddNamespace("tpl", TemplateNs);
        mgr.AddNamespace("xfa", XfaNs);
        mgr.AddNamespace("datasets", DatasetsNs);
        _nsManager = mgr;
        return mgr;
    }

    private string[] EnumerateFieldNames() => _form.GetXfaFieldNames();

    private XmlNode? WalkTemplateByPath(XmlNode root, string path)
    {
        var parts = Form.SplitSomPath(path);
        XmlNode? current = root;
        foreach (var part in parts)
        {
            if (current is null) return null;
            // part is "name[N]" — find the Nth child element with that @name
            var match = System.Text.RegularExpressions.Regex.Match(part, @"^(.+)\[(\d+)\]$");
            string name; int idx;
            if (match.Success) { name = match.Groups[1].Value; idx = int.Parse(match.Groups[2].Value); }
            else { name = part; idx = 0; }
            current = FindNthNamedChild(current, name, idx);
        }
        return current;
    }

    private static XmlNode? FindNthNamedChild(XmlNode parent, string name, int targetIdx)
    {
        int seen = 0;
        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is not XmlElement el) continue;
            if (el.LocalName is not ("subform" or "field" or "exclGroup")) continue;
            if (el.GetAttribute("name") != name) continue;
            if (seen == targetIdx) return el;
            seen++;
        }
        return null;
    }

    private XmlNode? WalkDatasetsByPath(XmlNode root, string path)
    {
        var parts = Form.SplitSomPath(path);
        XmlNode? current = root;
        foreach (var part in parts)
        {
            if (current is null) return null;
            var match = System.Text.RegularExpressions.Regex.Match(part, @"^(.+)\[(\d+)\]$");
            string name; int idx;
            if (match.Success) { name = match.Groups[1].Value; idx = int.Parse(match.Groups[2].Value); }
            else { name = part; idx = 0; }
            int seen = 0; XmlNode? found = null;
            foreach (XmlNode child in current.ChildNodes)
            {
                if (child is not XmlElement el) continue;
                if (el.LocalName != name) continue;
                if (seen == idx) { found = el; break; }
                seen++;
            }
            current = found;
        }
        return current;
    }
}
