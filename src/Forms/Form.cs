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
public sealed partial class Form : ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    private readonly List<Field> _fields;

    // Non-terminal (group/parent) field dicts surfaced into _fields by
    // CollectGroupFields so FindByName can resolve a group by its full name. They are
    // NOT terminal fields, so the public Fields array (and its leaf count) excludes
    // them — Form.Fields returns terminal fields only.
    private readonly HashSet<PdfDictionary> _groupFieldDicts = new();

    /// <summary>
    /// Returns the form fields as a snapshot array. Mirrors the
    /// public signature (`Field[] Fields`), so callers
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
    /// skipped). The numbering scheme: an unnamed field is based on
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
            // The layout pass may already have bound this widget to the page's
            // /Annots (a footer table placing the control at its laid-out cell
            // rectangle) — don't add the same dict twice.
            var annotsArr = AnnotsFor(target);
            var present = false;
            foreach (var a in annotsArr)
                if (ReferenceEquals(reader.Resolve(a), widget)) { present = true; break; }
            if (!present) annotsArr.Add(widget);
            // Record the owning page (/P) so PageIndex resolves without an
            // /Annots identity scan (which can't see through a re-serialized file).
            widget.Set("P", pages[target].Dict);
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

        // Composite fonts are named C{n}_0.
        var n = 0;
        foreach (var key in fontDict.Keys)
            if (key.Length > 1 && key[0] == 'C' && key.Contains('_')) n++;
        var resName = $"C{n}_0";

        // Prefer the face's PostScript name (name table id 6) for /BaseFont —
        // "BitstreamCyberCJK-Roman", not the family "Bitstream CyberCJK".
        string? psName = null;
        try
        {
            var ttp = new Aspose.Pdf.Text.TrueTypeParser(ttf);
            ttp.Parse();
            psName = ttp.PostScriptName;
        }
        catch { /* fall back to the family-derived name */ }
        if (psName is null or "" or "Unknown") psName = font.BaseFont ?? font.FontName;
        fontDict.Set(resName, BuildType0Font(ttf, psName));
        da.FontResourceName = resName;

        var c = da.TextColor;
        // Colour components: whole values as integers ("1"/"0"), fractions at full precision
        // (128/255 -> "0.5019607843"), not a fixed 3-decimal rounding.
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
        Widgetize(FindFieldOrNull(fullName) ?? throw new ArgumentException(
            $"Form field not found : {fullName}"));

    /// <summary>Look up a field by name; returns null when not found.
    /// camelCase 'findField' alias for the public surface.</summary>
    public Field? findField(string fullName) => FindFieldOrNull(fullName);

    /// <summary>PascalCase alias of <see cref="findField"/>.</summary>
    public Field? FindField(string fullName) => FindFieldOrNull(fullName);

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
    public bool HasField(string fieldName) => FindFieldOrNull(fieldName) is not null;

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
    /// Returns null for a missing name on a standard AcroForm (callers count
    /// misses); on an XFA-backed form a miss THROWS (the XFA path resolver
    /// reports the unmatched name).
    /// </summary>
    public Field? FindByName(string fullName) =>
        FindFieldOrNull(fullName)
        ?? (IsXfa || _fields.Count == 0
            ? throw new ArgumentException($"Form field not found : {fullName}")
            : null);
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
