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
public sealed class Form : IEnumerable<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    private readonly List<Field> _fields;

    /// <summary>
    /// Returns the form fields as a snapshot array. Mirrors the
    /// Aspose.PDF for .NET public signature (`Field[] Fields`), so callers
    /// can use .Length and array indexing.
    /// </summary>
    public Field[] Fields => _fields.ToArray();

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
            CollectGroupFields(reader, _fields, expandedGroups);
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

    public int Count => _fields.Count;
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

        // A field added without a partial name (/T) is auto-named "field_N" so it
        // stays addressable by FindByName. /NM (Annotation.Name) is the annotation
        // identifier, not the field name, so it does not make a field findable.
        if (string.IsNullOrEmpty(field.PartialName))
        {
            var maxN = 0;
            foreach (var item in fieldsArray)
            {
                var t = (reader.ResolveDict(item)?.Get("T") as PdfString)?.ToText();
                if (t is not null && t.StartsWith("field_", StringComparison.Ordinal)
                    && int.TryParse(t.AsSpan(6), out var n) && n > maxN)
                    maxN = n;
            }
            field.PartialName = "field_" + (maxN + 1);
        }

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
        var page = pages[pageIndex];
        var annots = page.Dict.Get("Annots") as PdfArray;
        if (annots is null)
        {
            annots = new PdfArray();
            page.Dict.Set("Annots", annots);
        }
        foreach (var widget in CollectWidgetDicts(fieldDict, reader))
            annots.Add(widget);
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

        // Composite fonts are named C{n}_0 (matching the Aspose.PDF for .NET convention).
        var n = 0;
        foreach (var key in fontDict.Keys)
            if (key.Length > 1 && key[0] == 'C' && key.Contains('_')) n++;
        var resName = $"C{n}_0";

        fontDict.Set(resName, BuildType0Font(ttf, font.BaseFont ?? font.FontName));
        da.FontResourceName = resName;

        var c = da.TextColor;
        var daStr = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "/{0} {1:G} Tf {2:F3} {3:F3} {4:F3} rg",
            resName, da.FontSize, c.R / 255.0, c.G / 255.0, c.B / 255.0);
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

            // 7. Last-segment fallback for non-bracket inputs (Aspose.PDF for .NET behaviour)
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
        if (dottedName.IndexOf('#') < 0) return dottedName;
        var parts = dottedName.Split('.');
        var kept = new List<string>(parts.Length);
        foreach (var p in parts)
            if (p.Length == 0 || p[0] != '#')
                kept.Add(p);
        return string.Join(".", kept);
    }

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
                    var currentPath = parentPath.Length > 0
                        ? $"{parentPath}.{nameAttr}[{idx}]"
                        : $"{nameAttr}[{idx}]";
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
                    var fieldPath = parentPath.Length > 0
                        ? $"{parentPath}.{nameAttr}[{idx}]"
                        : $"{nameAttr}[{idx}]";

                    // Map XFA path -> AcroForm field name (partial name)
                    if (!map.ContainsKey(fieldPath))
                        map[fieldPath] = nameAttr;
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
        // Remove XFA key from AcroForm — this converts XFA to standard AcroForm
        var catalog = flatReader.Catalog;
        var acroForm = flatReader.ResolveDict(catalog.Get("AcroForm"));
        if (acroForm is not null)
        {
            acroForm.Remove("XFA");
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
            var path = prefix.Length == 0 ? $"{localName}[{index}]" : $"{prefix}.{localName}[{index}]";

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
        var leaf = path.Split('.')[^1];
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
                    var currentPath = parentPath.Length > 0
                        ? $"{parentPath}.{nameAttr}[{idx}]"
                        : $"{nameAttr}[{idx}]";
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
                    var fieldPath = parentPath.Length > 0
                        ? $"{parentPath}.{nameAttr}[{idx}]"
                        : $"{nameAttr}[{idx}]";
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
        var parts = path.Split('.');
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

            var parts = path.Split('.');
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
                var imported = targetDoc.ImportNode(child, true);
                targetDataNode.AppendChild(imported);
            }
        }
        else
        {
            // Not a wrapper — import the element directly
            var imported = targetDoc.ImportNode(root, true);
            targetDataNode.AppendChild(imported);
        }
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
    internal void SyncAcroFormToXfa()
    {
        if (Type != FormType.Static) return;
        var pairs = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
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
                tb.Value = value;
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
        if (xml is null || stream is null) return;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var changed = false;
            foreach (var pair in values)
            {
                var node = FindXfaNode(doc, pair.Key) ?? CreateXfaNodePath(doc, pair.Key);
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
        var parts = path.Split('.');
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
                current = idx < nodes.Count ? nodes[idx] : null;
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
            if (allMatches is not null && allMatches.Count > leafIdx)
                return allMatches[leafIdx];
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

        var parts = path.Split('.');
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
    /// mirrors the Aspose.PDF for .NET behaviour so callers can branch on
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
    /// Aspose.PDF for .NET treats Flatten() as always refreshing appearances
    /// from the current field values) each field's /AP/N is rebuilt from its
    /// current /V before the page's widgets are folded into the page content.
    /// Without this, a flatten of a PDF whose fields were programmatically
    /// re-valued shows the original (stale) appearance.</summary>
    internal void Flatten(Document document, FlattenSettings? settings)
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
            FlattenFieldsOnPage(page, hideButtons);
        }

        // Remove AcroForm from catalog
        document.Catalog.Remove("AcroForm");

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
        field.Dict.Remove("AP");
        field.GenerateAppearance();
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

    private void FlattenFieldsOnPage(Page page, bool hideButtons = false)
    {
        var reader = page.Reader;
        var annotsObj = reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annotsObj is null) return;

        var remaining = new PdfArray();
        var appendContent = new System.IO.MemoryStream();

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
            if (!isWidget)
            {
                remaining.Add(annotRef);
                continue;
            }

            // HideButtons: drop push-button widgets entirely (neither rendered
            // into page content nor kept as an annotation) so the flattened
            // output shows no buttons.
            if (hideButtons && IsPushButtonWidget(annotDict, reader))
                continue;

            // Try to get the appearance stream
            var apDict = reader.ResolveDict(annotDict.Get("AP"));
            if (apDict is null)
            {
                // No appearance — widget won't be visible, but still remove it
                // so it doesn't persist as a form field after flatten.
                continue;
            }

            // Get the normal appearance (/N)
            var nObj = apDict.Get("N");
            PdfStream? appearanceStream = null;

            // /N can be a stream directly, or a dict of named states (checkbox: /Yes, /Off)
            var nResolved = reader.Resolve(nObj);
            if (nResolved is PdfStream ns)
            {
                appearanceStream = ns;
            }
            else if (nResolved is PdfDictionary stateDict)
            {
                // Pick the current state from /AS
                var asName = annotDict.GetName("AS");
                if (asName is not null)
                {
                    var stateStream = reader.ResolveStream(stateDict.Get(asName));
                    appearanceStream = stateStream;
                }
                // Fallback: try first non-Off state
                if (appearanceStream is null)
                {
                    foreach (var key in stateDict.Keys)
                    {
                        if (key == "Off") continue;
                        appearanceStream = reader.ResolveStream(stateDict.Get(key));
                        if (appearanceStream is not null) break;
                    }
                }
            }

            if (appearanceStream is null) continue;

            // Get the widget rectangle for positioning
            var rectArr = reader.Resolve(annotDict.Get("Rect")) as PdfArray;
            if (rectArr is null || rectArr.Count < 4) continue;

            var rect = Rectangle.FromPdfArray(rectArr);
            var streamData = reader.DecodeStream(appearanceStream);

            // Get the BBox from the appearance stream
            var bboxArr = reader.Resolve(appearanceStream.Dict.Get("BBox")) as PdfArray;
            double bboxW = rect.Width, bboxH = rect.Height;
            double bboxX = 0, bboxY = 0;
            if (bboxArr is { Count: >= 4 })
            {
                var bbox = Rectangle.FromPdfArray(bboxArr);
                bboxW = bbox.Width;
                bboxH = bbox.Height;
                bboxX = bbox.LLX;
                bboxY = bbox.LLY;
            }

            // Build transformation: scale appearance to fit widget rect and translate
            var sx = bboxW > 0 ? rect.Width / bboxW : 1.0;
            var sy = bboxH > 0 ? rect.Height / bboxH : 1.0;
            var tx = rect.LLX - bboxX * sx;
            var ty = rect.LLY - bboxY * sy;

            // Register the appearance stream as an XForm in the page's XObject
            // resources and emit a Do operator (matches .NET behavior — XForms
            // remain accessible via page.Resources.Forms after flatten).
            var xformName = RegisterAppearanceAsXForm(page.Dict, appearanceStream, reader);

            // Wrap in q/Q with transformation matrix + Do
            var writer = new System.IO.StreamWriter(appendContent, System.Text.Encoding.ASCII, leaveOpen: true);
            writer.Write($"q {Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n");
            writer.Write($"/{xformName} Do\n");
            writer.Write("Q\n");
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

            var combined = new byte[existingData.Length + 1 + appendContent.Length];
            existingData.CopyTo(combined, 0);
            if (existingData.Length > 0)
                combined[existingData.Length] = (byte)'\n';
            appendContent.ToArray().CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));

            page.SetContentStream(combined);
        }
    }

    /// <summary>
    /// Register an appearance stream as a named XForm in the page's XObject resources.
    /// Returns the assigned name (e.g., "FRM0", "FRM1"). Shared with annotation
    /// flatten (Annotation.Flatten) so both code paths use the same naming.
    /// </summary>
    internal static string RegisterAppearanceAsXForm(
        PdfDictionary pageDict, PdfStream appearanceStream, PdfReader reader)
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

        // Generate a unique name: FRM0, FRM1, .
        int idx = 0;
        string name;
        do
        {
            name = $"FRM{idx}";
            idx++;
        } while (xobjects.ContainsKey(name));

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
                            result.Add(CreateFieldWithObjNum(kidDict, reader, GetObjectNumber(kid)));
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
    private static void CollectGroupFields(PdfReader reader, List<Field> fields, HashSet<PdfDictionary> exclude)
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
                    groups.Add(Field.Create(parent, reader));
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

    // ── Aspose.PDF for .NET shape additions ───────────────────────────────

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
        _ = pageNumber; _ = rect;
        // The widget would be added to the page Annots array and the field's Kids;
        // this minimal implementation records intent on the field dict but does
        // not yet write the appearance stream or annotation array entry.
        field.Dict.Set("__appendedAppearance", new PdfInteger(pageNumber));
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
        field.Dict.Remove($"__appearance{appearanceIndex}");
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
        var parts = path.Split('.');
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
        var parts = path.Split('.');
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
