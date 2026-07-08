using System.Text;
using System.Xml;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for form field manipulation including XML import/export.
/// This is the Facades API (file-path/stream-based) counterpart to
/// <see cref="Aspose.Pdf.Forms.Form"/> (DOM-based).
/// </summary>
public sealed class Form : IDisposable
{
    private Document? _doc;
    private readonly bool _ownsDoc;
    private string? _destPath;
    private FormImportResult[] _importResult = Array.Empty<FormImportResult>();

    /// <summary>Per-field outcomes from the most recent ImportFdf/
    /// ImportXfdf/ImportXml call. Empty when no import has been
    /// performed yet.</summary>
    public FormImportResult[] ImportResult => _importResult;

    /// <summary>Outcome of importing a single field's value.</summary>
    public enum ImportStatus
    {
        /// <summary>The field was found in the bound form and its value was set.</summary>
        Success,
        /// <summary>The field name in the source data does not match any field in the form.</summary>
        FieldNotFound,
    }

    /// <summary>Per-field result of a form-data import operation.</summary>
    public sealed class FormImportResult
    {
        /// <summary>The field name from the imported data.</summary>
        public string FieldName { get; internal set; } = string.Empty;
        /// <summary>The outcome of trying to apply the value to the bound form.</summary>
        public ImportStatus Status { get; internal set; }
    }

    /// <summary>Create an unbound Form facade.</summary>
    public Form() { }

    /// <summary>Create a Form facade bound to a document.</summary>
    public Form(Document document)
    {
        _doc = document;
    }

    /// <summary>Bind a Document and pre-configure an output stream for the parameterless Save.</summary>
    public Form(Document document, Stream destStream)
    {
        _doc = document;
        DestStream = destStream;
    }

    /// <summary>Bind a Document and pre-configure an output file path for the parameterless Save.</summary>
    public Form(Document document, string destFileName)
    {
        _doc = document;
        _destPath = destFileName;
    }

    /// <summary>Create a Form facade from input and output file paths.</summary>
    public Form(string srcFileName, string destFileName)
    {
        _doc = Document.Open(srcFileName);
        _ownsDoc = true;
        _destPath = destFileName;
        DestStream = new FileStream(destFileName, FileMode.Create, FileAccess.Write);
    }

    /// <summary>Create a Form facade from an input file, writing to a stream on Save.</summary>
    public Form(string srcFileName, Stream destStream)
    {
        _doc = Document.Open(srcFileName);
        _ownsDoc = true;
        DestStream = destStream;
    }

    /// <summary>Create a Form facade from an input file path.</summary>
    public Form(string srcFileName)
    {
        _doc = Document.Open(srcFileName);
        _ownsDoc = true;
    }

    /// <summary>Create a Form facade from a stream.</summary>
    public Form(Stream srcStream)
    {
        // Buffer the source into an independent array so the facade never owns or
        // mutates the caller's stream (Form must not dispose the
        // source stream on Save). A seekable source is read from offset 0 —
        // Aspose.Pdf semantics, so `doc.Save(ms); new Form(ms)` works without
        // the caller rewinding — and the caller's position is restored afterwards so
        // the same source stream can be reused across several Form instances in a loop.
        long origPos = srcStream.CanSeek ? srcStream.Position : -1;
        if (srcStream.CanSeek) srcStream.Position = 0;
        using var ms = new MemoryStream();
        srcStream.CopyTo(ms);
        _doc = Document.Open(ms.ToArray());
        _ownsDoc = true;
        if (origPos >= 0)
            srcStream.Position = origPos;
    }

    /// <summary>Create a Form facade from a source stream, writing to a destination stream on Save.</summary>
    public Form(Stream srcStream, Stream destStream)
        : this(srcStream)
    {
        DestStream = destStream;
    }

    /// <summary>Create a Form facade from a source stream, writing to a destination file on Save.</summary>
    public Form(Stream srcStream, string destFileName)
        : this(srcStream)
    {
        _destPath = destFileName;
    }

    /// <summary>The underlying document.</summary>
    public Document? Document => _doc;

    /// <summary>Bind a PDF file to this facade.</summary>
    public void BindPdf(string path)
    {
        _doc = Pdf.Document.Open(path);
    }

    /// <summary>Bind a document to this facade.</summary>
    public void BindPdf(Document doc)
    {
        _doc = doc;
    }

    /// <summary>Bind a PDF stream to this facade.</summary>
    public void BindPdf(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _doc = Document.Open(ms.ToArray());
    }

    /// <summary>
    /// Get the maximum character count of a text field by name.
    /// Returns 0 when the field is not a TextBoxField or has no /MaxLen.
    /// </summary>
    public int GetFieldLimit(string fieldName)
    {
        if (_doc?.Form is null) return 0;
        var field = _doc.Form.FindByName(fieldName);
        return field is TextBoxField tb ? tb.MaxLen : 0;
    }

    /// <summary>Get a field value by name.</summary>
    public string? GetField(string fieldName)
    {
        if (_doc is null) return null;
        // For XFA forms, read from XFA datasets
        if (_doc.Form.IsXfa)
        {
            var val = _doc.Form.GetXfaFieldValue(fieldName);
            if (val is not null) return val;
        }
        var field = _doc.Form.FindByName(fieldName);
        if (field is null) return null;
        // An existing field with no value (e.g. an unsigned signature field) reads as an
        // empty string, not null — callers expect GetField on a known field to be non-null.
        return field.Value ?? string.Empty;
    }

    /// <summary>Fill a field with a value.</summary>
    public bool FillField(string fieldName, string fieldValue)
    {
        if (_doc is null) return false;
        // For XFA forms, update the XFA datasets XML directly
        if (_doc.Form.IsXfa)
        {
            // Only fill a path that resolves to a genuine XFA template field — a non-matching
            // (e.g. wrong-container) or partial (leaf-only) path returns false, matching
            // Aspose.Pdf, rather than silently creating a stray datasets node.
            if (!_doc.Form.XfaTemplateFieldExists(fieldName)) return false;
            _doc.Form.SetXfaFieldValue(fieldName, fieldValue);
            // Also try AcroForm field if it exists
            var field = _doc.Form.FindByName(fieldName);
            if (field is not null) field.Value = fieldValue;
            return true;
        }
        else
        {
            var field = _doc.Form.FindByName(fieldName);
            if (field is null) return false;
            field.Value = fieldValue;
            return true;
        }
    }

    /// <summary>Fill a field with a boolean value (for checkboxes/radio buttons).</summary>
    public bool FillField(string fieldName, bool beChecked)
    {
        return FillField(fieldName, beChecked ? "Yes" : "Off");
    }

    /// <summary>Get the type of a field by name.</summary>
    public FieldType GetFieldType(string fieldName)
    {
        if (_doc is null) return FieldType.InvalidNameOrType;
        var field = _doc.Form.FindByName(fieldName);
        if (field is not null)
        {
            // An image field is a push button laid out for an icon (/MK /TP 1) or
            // one whose appearance draws an image XObject (e.g. after FillImageField).
            // Detect that before the generic FT mapping, which reports PushButton.
            if (field is ButtonField && IsImageButton(field)) return FieldType.Image;
            return MapToFacadeFieldType(field.Type);
        }
        // Dynamic XFA forms carry no AcroForm twin for their fields, so resolve the
        // type from the XFA template's <ui> widget instead.
        if (_doc.Form.IsXfa)
            return MapXfaUiKindToFieldType(_doc.Form.GetXfaFieldUiKind(fieldName));
        return FieldType.InvalidNameOrType;
    }

    /// <summary>Returns the XFA template node for the named field (or null when
    /// the document has no XFA / the field is absent). Delegates to the
    /// document-level form's XFA template lookup.</summary>
    public System.Xml.XmlNode? GetFieldTemplate(string fieldName)
        => _doc?.Form?.XFA?.GetFieldTemplate(fieldName);

    /// <summary>True when a button field is an image (icon) button: it carries the
    /// icon-only layout (/MK /TP 1) or its normal appearance draws an image XObject.</summary>
    private bool IsImageButton(Forms.Field field)
    {
        var reader = _doc!.Reader;
        var widgets = field.AllKids().ToList();
        if (widgets.Count == 0) widgets.Add(field.Dict);
        foreach (var w in widgets)
        {
            var mk = reader.ResolveDict(w.Get("MK"));
            if (mk is not null && mk.ContainsKey("TP") && mk.GetInt("TP") == 1) return true;

            var ap = reader.ResolveDict(w.Get("AP"));
            var n = ap is null ? null : reader.ResolveStream(ap.Get("N"));
            var res = n is null ? null : reader.ResolveDict(n.Dict.Get("Resources"));
            var xobj = res is null ? null : reader.ResolveDict(res.Get("XObject"));
            if (xobj is not null)
                foreach (var k in xobj.Keys)
                    if (reader.ResolveStream(xobj.Get(k)) is { } x && x.Dict.GetName("Subtype") == "Image")
                        return true;
        }
        return false;
    }

    private static FieldType MapXfaUiKindToFieldType(string? uiKind) => uiKind switch
    {
        "textEdit" => FieldType.Text,
        "passwordEdit" => FieldType.Text,
        "numericEdit" => FieldType.Numeric,
        "dateTimeEdit" => FieldType.DateTime,
        "choiceList" => FieldType.ComboBox,
        "choiceListMulti" => FieldType.ListBox,
        "button" => FieldType.PushButton,
        "checkButton" => FieldType.CheckBox,
        "exclGroup" => FieldType.Radio,
        "signature" => FieldType.Signature,
        "imageEdit" => FieldType.Image,
        "barcode" => FieldType.Barcode,
        _ => FieldType.InvalidNameOrType,
    };

    private static FieldType MapToFacadeFieldType(Forms.FieldType t)
    {
        return t switch
        {
            Forms.FieldType.Text => FieldType.Text,
            Forms.FieldType.Button => FieldType.PushButton,
            Forms.FieldType.CheckBox => FieldType.CheckBox,
            Forms.FieldType.RadioButton => FieldType.Radio,
            Forms.FieldType.Choice => FieldType.ListBox,
            Forms.FieldType.ListBox => FieldType.ListBox,
            Forms.FieldType.ComboBox => FieldType.ComboBox,
            Forms.FieldType.Signature => FieldType.Signature,
            Forms.FieldType.Barcode => FieldType.Barcode,
            Forms.FieldType.Numeric => FieldType.Numeric,
            Forms.FieldType.DateTime => FieldType.DateTime,
            _ => FieldType.InvalidNameOrType,
        };
    }

    /// <summary>Get the names of submit buttons in the form.</summary>
    public string[] FormSubmitButtonNames
    {
        get
        {
            if (_doc is null) return [];
            var names = new List<string>();
            foreach (var f in _doc.Form.Fields)
            {
                // A submit button is a button field whose activation action is a
                // SubmitForm action — not every push button (e.g. a logo button
                // with a different or no action) qualifies.
                if (f.Type == Forms.FieldType.Button
                    && f.OnActivated?.Type == Annotations.ActionType.SubmitForm)
                {
                    var name = f.FullName ?? f.PartialName;
                    if (name is not null) names.Add(name);
                }
            }
            return names.ToArray();
        }
    }

    /// <summary>
    /// Get visual facade properties for a field.
    /// Returns null if the field is not found.
    /// </summary>
    public FormFieldFacade? GetFieldFacade(string fieldName)
    {
        if (_doc is null) return null;
        var field = _doc.Form.FindByName(fieldName);

        // For XFA forms, field may not exist in AcroForm — build facade from XFA template
        if (field is null && _doc.Form.IsXfa)
        {
            var xfaCap = _doc.Form.GetXfaFieldCaption(fieldName);
            return new FormFieldFacade { Caption = xfaCap ?? "" };
        }

        if (field is null) return null;

        // For XFA forms, caption always comes from the XFA template (not /TU which is the description)
        string? fieldCaption = null;
        if (_doc.Form.IsXfa)
            fieldCaption = _doc.Form.GetXfaFieldCaption(fieldName);
        fieldCaption ??= field.AlternateName;

        var fieldRect = field.Rect;
        var facade = new FormFieldFacade { Caption = fieldCaption };
        if (fieldRect is not null)
        {
            facade.Box = new System.Drawing.Rectangle(
                (int)fieldRect.LLX,
                (int)fieldRect.LLY,
                (int)(fieldRect.URX - fieldRect.LLX),
                (int)(fieldRect.URY - fieldRect.LLY));
            // Position is the 1-based [page, llx, lly, urx, ury] rectangle tuple the
            // facade exposes; element 0 carries the owning page number.
            facade.Position = new float[]
            {
                System.Math.Max(0, field.PageIndex),
                (float)fieldRect.LLX, (float)fieldRect.LLY,
                (float)fieldRect.URX, (float)fieldRect.URY,
            };
        }

        // Try to extract font info from DA (default appearance)
        var da = GetDaString(field.Dict) ?? GetInheritedDa(field.Dict);
        if (da is not null)
        {
            // DA format: "/FontName fontSize Tf" or "0 g /FontName fontSize Tf"
            var parts = da.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "Tf" && i >= 2)
                {
                    facade.CustomFont = parts[i - 2].TrimStart('/');
                    if (float.TryParse(parts[i - 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var size))
                        facade.FontSize = size;
                }
            }
        }

        // Page number: prefer the field's own PageIndex — it resolves a parent field's
        // page via its widget kids / inherited /P, whereas the annotation scan below only
        // matches a field dict that is itself in a page's /Annots.
        if (field.PageIndex >= 1)
        {
            facade.PageNumber = field.PageIndex;
        }
        else
        {
            for (int p = 1; p <= _doc.PageCount; p++)
            {
                var page = _doc.Pages.At(p);
                foreach (var annot in page.Annotations)
                {
                    if (annot.Dict == field.Dict)
                    {
                        facade.PageNumber = p;
                        break;
                    }
                }
                if (facade.PageNumber > 0) break;
            }
        }

        // Position mirrors FormFieldFacade.Position: [page, llx, lly, urx, ury].
        if (fieldRect is not null)
            facade.Position = new[]
            {
                (float)facade.PageNumber,
                (float)fieldRect.LLX, (float)fieldRect.LLY,
                (float)fieldRect.URX, (float)fieldRect.URY,
            };

        return facade;
    }

    /// <summary>
    /// Get the current value of a button (radio button) field.
    /// </summary>
    public string? GetButtonOptionCurrentValue(string fieldName)
    {
        if (_doc is null) return null;
        var field = _doc.Form.FindByName(fieldName);
        if (field is not null)
        {
            var v = field.Value;
            return v is not null ? "/" + v.TrimStart('/') : null;
        }
        // XFA fallback: read current value from XFA datasets
        if (_doc.Form.IsXfa)
        {
            var v = _doc.Form.GetXfaFieldValue(fieldName);
            if (v is not null) return "/" + v.TrimStart('/');
            // No value set → Off
            return "/Off";
        }
        return null;
    }

    /// <summary>
    /// Get the option values of a button (radio button) field.
    /// Returns a dictionary of option export values to display labels.
    /// </summary>
    public Dictionary<string, string>? GetButtonOptionValues(string fieldName)
    {
        if (_doc is null) return null;
        var field = _doc.Form.FindByName(fieldName);

        var result = new Dictionary<string, string>();
        if (field is not null)
        {
            CollectButtonOptions(field.Dict, result);

            // Also check parent's Kids for radio groups
            if (result.Count == 0)
            {
                var parent = _doc.Reader.ResolveDict(field.Dict.Get("Parent"));
                if (parent is not null)
                {
                    var kids = _doc.Reader.Resolve(parent.Get("Kids")) as Core.PdfArray;
                    if (kids is not null)
                    {
                        foreach (var kidRef in kids)
                        {
                            var kidDict = _doc.Reader.ResolveDict(kidRef);
                            if (kidDict is not null) CollectButtonOptions(kidDict, result);
                        }
                    }
                }
            }
        }

        // XFA forms: template items are authoritative — replace AcroForm results
        if (_doc.Form.IsXfa)
        {
            var items = _doc.Form.GetXfaRadioButtonItems(fieldName);
            if (items is not null)
            {
                result.Clear();
                foreach (var item in items)
                    result[item] = item;
            }
        }

        return result.Count > 0 ? result : null;
    }

    private void CollectButtonOptions(Core.PdfDictionary dict, Dictionary<string, string> result)
    {
        var apDict = _doc!.Reader.ResolveDict(dict.Get("AP"));
        if (apDict is null) return;
        var nObj = _doc.Reader.Resolve(apDict.Get("N"));
        if (nObj is Core.PdfDictionary nDict)
        {
            foreach (var key in nDict.Keys)
            {
                if (key != "Off" && !result.ContainsKey(key))
                    result[key] = key;
            }
        }
    }

    private static string? GetDaString(Core.PdfDictionary dict)
    {
        var obj = dict.Get("DA");
        return obj is Core.PdfString s ? s.ToText() : null;
    }

    private string? GetInheritedDa(Core.PdfDictionary dict)
    {
        var parent = _doc!.Reader.ResolveDict(dict.Get("Parent"));
        while (parent is not null)
        {
            var val = GetDaString(parent);
            if (val is not null) return val;
            parent = _doc.Reader.ResolveDict(parent.Get("Parent"));
        }
        return null;
    }

    private string? _srcFileName;
    private Stream? _srcStream;

    /// <summary>Source file name. Setting eagerly opens the document.</summary>
    public string? SrcFileName
    {
        get => _srcFileName;
        set
        {
            _srcFileName = value;
            if (value is not null) _doc = Pdf.Document.Open(value);
        }
    }

    /// <summary>Source stream. Setting eagerly opens the document.</summary>
    public Stream? SrcStream
    {
        get => _srcStream;
        set
        {
            _srcStream = value;
            if (value is not null)
            {
                using var ms = new MemoryStream();
                if (value.CanSeek) value.Position = 0;
                value.CopyTo(ms);
                _doc = Document.Open(ms.ToArray());
            }
        }
    }

    private Aspose.Pdf.PdfFormat? _convertTo;
    /// <summary>Target PDF/A or PDF version for the saved output. Stored only; the save path emits plain PDF.</summary>
    public Aspose.Pdf.PdfFormat ConvertTo
    {
        set => _convertTo = value;
    }

    /// <summary>Get all field names in the form.</summary>
    public string[] FieldNames
    {
        get
        {
            if (_doc is null) return [];
            // For XFA forms, enumerate field names from the XFA template
            if (_doc.Form.IsXfa)
            {
                var xfaNames = _doc.Form.GetXfaFieldNames();
                if (xfaNames.Length > 0)
                {
                    return xfaNames;
                }
            }
            // Each field name is reported once. A radio/checkbox group whose widgets
            // are split across several /Kids (or that resolves through more than one
            // enumerated entry) must not inflate the count — the facade lists the
            // logical field name a single time, matching the reference.
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in _doc.Form.Fields)
            {
                var name = f.FullName ?? f.PartialName;
                if (name is not null && seen.Add(name)) names.Add(name);
            }
            return names.ToArray();
        }
    }

    /// <summary>
    /// Get the full qualified field name. Returns the field's FullName (dotted path for nested fields),
    /// or the PartialName if FullName is not available, or the input name as fallback.
    /// </summary>
    public string GetFullFieldName(string fieldName)
    {
        if (_doc is null) return fieldName;
        var field = _doc.Form.FindByName(fieldName);
        return field?.FullName ?? field?.PartialName ?? fieldName;
    }

    /// <summary>
    /// Flatten a specific field by name — render it as static content and remove interactive state.
    /// Currently flattens the entire form (all fields) since per-field flattening is not implemented.
    /// </summary>
    public void FlattenField(string fieldName)
    {
        if (_doc is null) return;
        var field = _doc.Form.FindByName(fieldName);
        if (field is null) return;
        _doc.Form.Flatten(_doc);
    }

    /// <summary>
    /// Flatten all form fields in the document — render them as static content and remove the AcroForm.
    /// </summary>
    public void FlattenAllFields()
    {
        if (_doc is null) return;
        if (!_doc.HasForm) return;
        // Flattening a dynamic XFA form locks its template fields (access="readOnly"); the
        // /XFA packet + datasets must survive, so do NOT run the AcroForm flatten below — it
        // strips the whole /AcroForm dict (which carries /XFA). Rendering a dynamic XFA
        // template into a flattened static page is a separate, unimplemented feature.
        if (_doc.Form.IsXfa)
        {
            _doc.Form.SetXfaFieldsReadOnly();
            return;
        }
        // The facade flatten numbers its flattened FRM{n} XObjects from 1 (document/form flatten
        // numbers from 0) and folds every annotation — including markup such as FreeText — into
        // the page content so the FRM index lines up with /Annots. Matches Aspose.Pdf.
        _doc.Form.Flatten(_doc, settings: null, frmStartIndex: 1, flattenNonWidgets: true);
    }

    /// <summary>
    /// Import form field values from an XML stream.
    /// For AcroForm: expects XML with field names as element names, values as text content.
    /// For XFA: replaces the XFA datasets.
    /// </summary>
    public void ImportXml(Stream inputXmlStream, bool IgnoreFormTemplateChanges)
    {
        var xmlStream = inputXmlStream;
        var ignoreFormTemplateChanges = IgnoreFormTemplateChanges;
        if (_doc is null) throw new InvalidOperationException("No document bound.");

        var xml = new XmlDocument();
        // Aspose.Pdf reads the XML through the Windows default codepage unless
        // the stream carries a BOM or an explicit <?xml encoding=...?> declaration, so
        // UTF-8 bytes in a bare XML arrive as Windows-1252 characters. Field values
        // written that way round-trip through the XFA datasets verbatim; decode the
        // same way so imported values match.
        using (var buffer = new MemoryStream())
        {
            xmlStream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                || bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF);
            bool hasDeclaredEncoding = false;
            if (!hasBom && bytes.Length >= 5 && bytes[0] == (byte)'<' && bytes[1] == (byte)'?')
            {
                var declEnd = System.Array.IndexOf(bytes, (byte)'>', 0, Math.Min(bytes.Length, 256));
                if (declEnd > 0)
                    hasDeclaredEncoding = System.Text.Encoding.ASCII
                        .GetString(bytes, 0, declEnd).Contains("encoding=", StringComparison.OrdinalIgnoreCase);
            }
            if (hasBom || hasDeclaredEncoding)
                xml.Load(new MemoryStream(bytes));
            else
                xml.LoadXml(Aspose.Pdf.Text.Cp1252.GetString(bytes));
        }

        // Check if this is an XFA form
        if (_doc.Form.IsXfa)
        {
            ImportXmlXfa(xml);
            // XFA-side import doesn't go through FindByName; skip per-field
            // tracking. Callers can still inspect form-level success via the
            // imported XFA datasets state.
            _importResult = Array.Empty<FormImportResult>();
            return;
        }

        // AcroForm: walk XML elements and fill fields
        var names = new List<string>();
        if (xml.DocumentElement is not null)
        {
            CollectXmlFieldNames(xml.DocumentElement, parentPath: null, names);
            // If no <field name="..."> elements found, fall back to XFA-datasets
            // format: each leaf element is a field, dotted path = nested element names.
            if (names.Count == 0)
                CollectXfaDatasetsLeafNames(xml.DocumentElement, parentPath: null, names);
        }
        ImportXmlAcroForm(xml.DocumentElement!);
        _importResult = TrackResults(names);
    }

    private static void CollectXmlFieldNames(XmlNode node, string? parentPath, List<string> result)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is not XmlElement el || el.Name != "field") continue;
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name)) continue;
            var path = parentPath is null ? name : $"{parentPath}.{name}";
            // <field><fields><field name="..."> nesting → dotted child path
            var nestedFields = el.SelectSingleNode("fields");
            if (nestedFields is not null)
                CollectXmlFieldNames(nestedFields, path, result);
            else
                result.Add(path);
        }
    }

    private static void CollectXfaDatasetsLeafNames(XmlNode node, string? parentPath, List<string> result)
    {
        // For XFA <xfa:data> XML, the root element is the data wrapper. Skip it
        // and use its first child (the form root, e.g. <form1>) as the implicit
        // top of the dotted path so the names match XFA's "form1[0]..." pattern.
        if (parentPath is null && node.LocalName == "data")
        {
            foreach (XmlNode firstLevel in node.ChildNodes)
            {
                if (firstLevel is XmlElement)
                {
                    CollectXfaDatasetsLeafNames(firstLevel, parentPath: null, result);
                    return;
                }
            }
            return;
        }

        var elementChildren = new List<XmlElement>();
        foreach (XmlNode c in node.ChildNodes)
            if (c is XmlElement ec) elementChildren.Add(ec);

        if (elementChildren.Count == 0)
        {
            // Leaf — emit the path
            if (parentPath is not null) result.Add(parentPath);
            return;
        }

        foreach (var child in elementChildren)
        {
            var seg = child.LocalName;
            var path = parentPath is null ? seg : $"{parentPath}.{seg}";
            CollectXfaDatasetsLeafNames(child, path, result);
        }
    }

    /// <summary>
    /// Import form field data from an XFDF stream.
    /// </summary>
    public void ImportXfdf(Stream inputXfdfStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        using var reader = new StreamReader(inputXfdfStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true, bufferSize: 4096);
        var xfdfXml = reader.ReadToEnd();
        if (_doc.Form.IsXfa)
        {
            // Persist into the XFA datasets, but still report per-field import
            // status (TrackResults resolves names against the XFA field paths).
            var xfaNames = ParseXfdfFieldNames(xfdfXml);
            ImportXfdfXfa(xfdfXml);
            _importResult = TrackResults(xfaNames);
            return;
        }
        var names = ParseXfdfFieldNames(xfdfXml);
        _doc.Form.ImportXfdf(xfdfXml);
        _importResult = TrackResults(names);
    }

    /// <summary>
    /// Import form field data from an FDF stream.
    /// </summary>
    public void ImportFdf(Stream inputFdfStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        using var ms = new MemoryStream();
        inputFdfStream.CopyTo(ms);
        var bytes = ms.ToArray();
        if (_doc.Form.IsXfa)
        {
            var xfaNames = ParseFdfFieldNames(bytes);
            ImportFdfXfa(bytes);
            _importResult = TrackResults(xfaNames);
            return;
        }
        var names = ParseFdfFieldNames(bytes);
        _doc.Form.ImportFdf(bytes);
        _importResult = TrackResults(names);
    }

    private FormImportResult[] TrackResults(IEnumerable<string> fieldNames)
    {
        if (_doc is null) return Array.Empty<FormImportResult>();
        var form = _doc.Form;
        // For XFA forms, AcroForm FindByName won't match XFA dotted paths;
        // resolve names against the set of XFA field paths instead.
        // For XFA forms, AcroForm FindByName won't match XFA dotted paths. Resolve
        // names against the XFA field paths, index-insensitively: a flat leaf name
        // (from a flat-FDF /T(Employee[0])) matches the full path form1[0]…Employee[0],
        // and a full dotted path matches exactly. Only genuine template fields are in
        // the set, so a field named in the import but absent from the form (e.g.
        // 33618's TextFieldX) reports FieldNotFound.
        List<string>? xfaPathsNorm = XfaFieldPathsNorm();

        var list = new List<FormImportResult>();
        foreach (var name in fieldNames)
        {
            bool found = xfaPathsNorm is not null
                ? IsKnownXfaField(name, xfaPathsNorm)
                : form.FindByName(name) is not null;
            var status = found ? ImportStatus.Success : ImportStatus.FieldNotFound;
            list.Add(new FormImportResult { FieldName = name, Status = status });
        }
        return list.ToArray();
    }

    /// <summary>The XFA template's field paths with per-segment <c>[n]</c> indices stripped,
    /// or null when the form is not XFA.</summary>
    private List<string>? XfaFieldPathsNorm()
    {
        var form = _doc?.Form;
        if (form is null || !form.IsXfa || form.XFA is null) return null;
        return form.XFA.FieldNames.Select(StripPathIndices).ToList();
    }

    /// <summary>True when <paramref name="name"/> (a flat leaf like <c>Employee[0]</c> or a full
    /// dotted path) resolves to a genuine XFA template field — index-insensitively, matched as a
    /// full path or a trailing segment-suffix of one.</summary>
    private static bool IsKnownXfaField(string name, List<string> normPaths)
    {
        var n = StripPathIndices(name);
        return normPaths.Any(p => p == n || p.EndsWith("." + n, StringComparison.Ordinal));
    }

    private static List<string> ParseXfdfFieldNames(string xfdfXml)
    {
        var result = new List<string>();
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xfdfXml);
            var ns = doc.Root?.GetDefaultNamespace() ?? System.Xml.Linq.XNamespace.None;
            var fields = doc.Root?.Element(ns + "fields");
            if (fields is null) return result;
            CollectXfdfFieldNames(fields, ns, parentPath: null, result);
        }
        catch { /* malformed XFDF — leave list empty */ }
        return result;
    }

    private static void CollectXfdfFieldNames(System.Xml.Linq.XElement fieldsContainer,
        System.Xml.Linq.XNamespace ns, string? parentPath, List<string> result)
    {
        foreach (var fieldEl in fieldsContainer.Elements(ns + "field"))
        {
            var name = fieldEl.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;
            var path = parentPath is null ? name : $"{parentPath}.{name}";
            // <field><fields><field name="..."> nesting → recurse
            var nestedFields = fieldEl.Element(ns + "fields");
            if (nestedFields is not null)
                CollectXfdfFieldNames(nestedFields, ns, path, result);
            else
                result.Add(path);
        }
    }

    private static List<string> ParseFdfFieldNames(byte[] fdfData)
    {
        // FDF /Fields encodes a tree of dicts: each entry has an optional /T
        // (partial name), an optional /V (value), and an optional /Kids (array
        // of child entries). Leaves are entries without /Kids; their full
        // field name is the dotted join of /T values from the root.
        //
        // Recursive-descent parser handles nested <<...>> dicts and [...]
        // arrays without flattening to bare /T scans.
        var result = new List<string>();
        var text = Encoding.Latin1.GetString(fdfData);
        var fieldsIdx = text.IndexOf("/Fields", StringComparison.Ordinal);
        if (fieldsIdx < 0) return result;
        var pos = text.IndexOf('[', fieldsIdx);
        if (pos < 0) return result;
        pos++; // step past '['
        ParseFdfFieldsArray(text, ref pos, parentPath: null, result);
        return result;
    }

    private static void ParseFdfFieldsArray(string t, ref int pos, string? parentPath, List<string> result)
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

    private static void ParseFdfFieldDict(string t, ref int pos, string? parentPath, List<string> result)
    {
        string? partialName = null;
        int kidsStart = -1;

        while (pos < t.Length)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) return;
            if (pos + 1 < t.Length && t[pos] == '>' && t[pos + 1] == '>')
            {
                pos += 2;
                break;
            }
            if (t[pos] != '/') { pos++; continue; }
            pos++; // step past '/'
            int kStart = pos;
            while (pos < t.Length && !IsFdfDelimOrWS(t[pos])) pos++;
            var key = t.Substring(kStart, pos - kStart);
            FdfSkipWS(t, ref pos);
            if (key == "T" && pos < t.Length && t[pos] == '(')
            {
                partialName = FdfReadStringLiteral(t, ref pos);
            }
            else if (key == "Kids" && pos < t.Length && t[pos] == '[')
            {
                kidsStart = pos + 1; // remember; consume below
                FdfSkipArray(t, ref pos);
            }
            else
            {
                FdfSkipValue(t, ref pos);
            }
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
            result.Add(fullPath);
        }
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
            if (pos >= t.Length) break;
            char c = t[pos];
            if (c == '[') { pos++; depth++; }
            else if (c == ']') { pos++; depth--; }
            else if (c == '<' && pos + 1 < t.Length && t[pos + 1] == '<') FdfSkipDict(t, ref pos);
            else if (c == '(') FdfReadStringLiteral(t, ref pos);
            else FdfSkipValue(t, ref pos);
        }
    }

    private static void FdfSkipDict(string t, ref int pos)
    {
        pos += 2; // step past '<<'
        int depth = 1;
        while (pos < t.Length && depth > 0)
        {
            FdfSkipWS(t, ref pos);
            if (pos >= t.Length) break;
            char c = t[pos];
            if (pos + 1 < t.Length && c == '<' && t[pos + 1] == '<') { pos += 2; depth++; }
            else if (pos + 1 < t.Length && c == '>' && t[pos + 1] == '>') { pos += 2; depth--; }
            else if (c == '(') FdfReadStringLiteral(t, ref pos);
            else if (c == '[') FdfSkipArray(t, ref pos);
            else FdfSkipValue(t, ref pos);
        }
    }

    /// <summary>
    /// Export form field values to an XML stream.
    /// </summary>
    public void ExportXml(Stream outputXmlStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");

        if (_doc.Form.IsXfa)
        {
            ExportXmlXfa(outputXmlStream);
        }
        else
        {
            ExportXmlAcroForm(outputXmlStream);
        }

        // Reset stream position to beginning for caller convenience (API parity)
        if (outputXmlStream.CanSeek)
            outputXmlStream.Position = 0;
    }

    /// <summary>
    /// Export form field values as FDF to a stream.
    /// </summary>
    public void ExportFdf(Stream outputFdfStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (_doc.Form.IsXfa)
        {
            ExportFdfXfa(outputFdfStream);
            if (outputFdfStream.CanSeek) outputFdfStream.Position = 0;
            return;
        }
        var bytes = _doc.Form.ExportFdf();
        outputFdfStream.Write(bytes, 0, bytes.Length);
        if (outputFdfStream.CanSeek)
            outputFdfStream.Position = 0;
    }

    /// <summary>
    /// Export form field values as XFDF to a stream.
    /// </summary>
    public void ExportXfdf(Stream outputXfdfStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (_doc.Form.IsXfa)
        {
            ExportXfdfXfa(outputXfdfStream);
            if (outputXfdfStream.CanSeek) outputXfdfStream.Position = 0;
            return;
        }
        var xml = _doc.Form.ExportXfdf();
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        outputXfdfStream.Write(bytes, 0, bytes.Length);
        if (outputXfdfStream.CanSeek)
            outputXfdfStream.Position = 0;
    }

    /// <summary>
    /// Exports the contents of all fields in the document into a JSON stream.
    /// Button-field values are not exported.
    /// </summary>
    /// <param name="outputJsonStream">The output JSON stream.</param>
    /// <param name="indented">Whether the JSON output should be indented.</param>
    public void ExportJson(Stream outputJsonStream, bool indented = true)
    {
        if (outputJsonStream is null)
            throw new ArgumentNullException(nameof(outputJsonStream));
        if (_doc is null)
            throw new InvalidOperationException("No document bound.");

        var fields = FormJsonSerializer.BuildFieldData(_doc);
        var json = System.Text.Json.JsonSerializer.Serialize(fields, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        outputJsonStream.Write(bytes, 0, bytes.Length);
        if (outputJsonStream.CanSeek)
            outputJsonStream.Position = 0;
    }

    /// <summary>
    /// Imports field values from a JSON stream into the bound document, matching
    /// fields by their full names.
    /// </summary>
    public void ImportJson(Stream inputJsonStream)
    {
        if (inputJsonStream is null)
            throw new ArgumentNullException(nameof(inputJsonStream));
        if (_doc is null)
            throw new InvalidOperationException("No document bound.");

        FormJsonSerializer.ImportFieldData(_doc, inputJsonStream);
    }

    /// <summary>True if a field with the given full name exists in the bound form.</summary>
    public bool HasField(string fullName)
    {
        if (_doc is null) return false;
        return _doc.Form.FindByName(fullName) is not null;
    }

    /// <summary>Destination file name for saving.</summary>
    public string? DestFileName
    {
        get => _destPath;
        set => _destPath = value;
    }

    /// <summary>Destination stream for saving.</summary>
    public Stream? DestStream { get; set; }

    /// <summary>Applies the requested <see cref="ConvertTo"/> target to the bound
    /// document. For the plain PDF version formats (v_1_x / v_2_0) this sets the
    /// document version so the saved file carries the requested header.</summary>
    private void ApplyConvertTo()
    {
        if (_doc is null || _convertTo is null) return;
        var name = _convertTo.Value.ToString();
        if (name.StartsWith("v_", StringComparison.Ordinal))
            _doc.SetVersion(name.Substring(2).Replace('_', '.'));
    }

    /// <summary>Save the modified document.</summary>
    public void Save()
    {
        if (_doc is null) return;
        ApplyConvertTo();
        var bytes = _doc.ToArray();
        if (DestStream is not null)
        {
            // The destination may be the very stream the document was read from
            // (Form(fs, fs) — an in-place save), which leaves the position at EOF.
            // Reset to the start and truncate so the written bytes replace the file
            // rather than being appended after the original (which corrupts it).
            if (DestStream.CanSeek)
            {
                DestStream.Seek(0, SeekOrigin.Begin);
                DestStream.SetLength(0);
            }
            DestStream.Write(bytes, 0, bytes.Length);
            // Form(srcPath, destPath) opens DestStream itself in the ctor; the
            // contract is that Save() returns with the destination
            // file fully written and unlocked, so callers can immediately do
            // `new Document(destPath)`. Without this dispose, the FileStream
            // opened in the (string,string) ctor stayed open for the lifetime
            // of the Form facade and locked the output for any reader.
            if (_destPath is not null)
            {
                DestStream.Dispose();
                DestStream = null;
            }
            else
            {
                DestStream.Flush();
            }
        }
        else if (_destPath is not null)
        {
            File.WriteAllBytes(_destPath, bytes);
        }
    }

    /// <summary>Save to a specific path.</summary>
    public void Save(string destFile)
    {
        if (_doc is null) return;
        ApplyConvertTo();
        File.WriteAllBytes(destFile, _doc.ToArray());
    }

    /// <summary>Save the modified document to a stream.</summary>
    public void Save(Stream destStream)
    {
        if (_doc is null) return;
        ApplyConvertTo();
        var bytes = _doc.ToArray();
        destStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Save the modified document and return bytes.</summary>
    public byte[] SaveBytes()
    {
        if (_doc is null) return [];
        ApplyConvertTo();
        return _doc.ToArray();
    }

    /// <summary>Close the facade.</summary>
    public void Close() => Dispose();

    public void Dispose()
    {
        if (_ownsDoc) _doc?.Dispose();
        _doc = null;
    }

    // ── AcroForm XML Import ─────────────────────────────────────────

    private void ImportXmlAcroForm(XmlElement root)
    {
        var form = _doc!.Form;
        var editor = new FormEditor(_doc);

        foreach (XmlNode node in root.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;

            // Support both formats:
            // 1. <field name="FieldName"><value>val</value></field> (Aspose .NET format)
            // 2. <FieldName>val</FieldName> (simple format)
            var nameAttr = node.Attributes?["name"]?.Value;
            var fieldName = nameAttr ?? node.LocalName;

            // Extract value: check for <value> child element first. Strip the
            // newline characters introduced by pretty-printed (indented) XML —
            // Acrobat/Aspose keep the value's spaces but drop the layout newlines,
            // so "\n            Product\n        " imports as "            Product        ".
            var valueNode = node.SelectSingleNode("value");
            var fieldValue = (valueNode?.InnerText ?? node.InnerText).Replace("\r", "").Replace("\n", "");

            var field = form.FindByName(fieldName);
            if (field is not null)
            {
                editor.SetField(fieldName, fieldValue);
            }
            else
            {
                // Try with full path from nested elements
                ImportXmlNode(node, "", editor, form);
            }
        }
    }

    private static void ImportXmlNode(XmlNode node, string prefix, FormEditor editor, Forms.Form form)
    {
        var name = string.IsNullOrEmpty(prefix) ? node.LocalName : prefix + "." + node.LocalName;

        if (node.HasChildNodes && node.FirstChild!.NodeType == XmlNodeType.Text)
        {
            var field = form.FindByName(name);
            if (field is not null)
                editor.SetField(name, node.InnerText);
            return;
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element)
                ImportXmlNode(child, name, editor, form);
        }
    }

    // ── AcroForm XML Export ─────────────────────────────────────────

    private void ExportXmlAcroForm(Stream output)
    {
        var form = _doc!.Form;
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
        };

        using var writer = XmlWriter.Create(output, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("fields");

        var fieldsArray = form.Fields;
        for (int i = 0; i < fieldsArray.Length; i++)
        {
            var field = fieldsArray[i];
            writer.WriteStartElement("field");
            writer.WriteAttributeString("name", field.FullName ?? $"field_{i + 1}");
            writer.WriteStartElement("value");
            writer.WriteString(field.Value ?? "");
            writer.WriteEndElement(); // </value>
            writer.WriteEndElement(); // </field>
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    // ── XFA XML Import/Export ────────────────────────────────────────

    private void ImportXmlXfa(XmlDocument xml)
    {
        // For XFA forms, replace the entire datasets with the imported XML
        var xfaForm = _doc!.Form;
        xfaForm.ReplaceXfaDatasets(xml);
        // Push the imported values into the AcroForm widgets (static XFA) so the two
        // representations agree — otherwise the save-time AcroForm→XFA sync writes the
        // stale widget state back over the imported datasets.
        xfaForm.SyncXfaToAcroForm();
    }

    private static void ImportXfaNode(XmlNode node, string path, Forms.Form form)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;

            var childPath = string.IsNullOrEmpty(path) ? child.LocalName : path + "." + child.LocalName;

            if (child.HasChildNodes && child.ChildNodes.Count == 1 && child.FirstChild!.NodeType == XmlNodeType.Text)
            {
                // Leaf value — try to set XFA field
                try { form.SetXfaFieldValue(childPath, child.InnerText); } catch { }
            }
            else
            {
                ImportXfaNode(child, childPath, form);
            }
        }
    }

    private void ExportXmlXfa(Stream output)
    {
        var form = _doc!.Form;
        var datasetsXml = form.GetXfaDatasetsXml();
        // If the datasets carry real form data (from an import/fill), export it directly.
        if (datasetsXml is not null)
        {
            try
            {
                var dsDoc = new XmlDocument();
                dsDoc.LoadXml(datasetsXml);

                // Locate a GENUINE XFA data root: the <data> element inside <datasets>
                // (xfa-data namespace), or a bare <data>/<datasets> root. GetXfaDatasetsXml
                // can return the whole XDP (preamble/config/template/. concatenated) when the
                // form has no datasets packet — that is presentation metadata, NOT form data,
                // so we must only take the rich path when a real <datasets>/<data> node exists;
                // otherwise we fall through to building the export from the template below.
                XmlNode? datasetsNode = dsDoc.SelectSingleNode("//*[local-name()='datasets']");
                XmlNode? dataNode = datasetsNode?.SelectSingleNode("*[local-name()='data']")
                    ?? (dsDoc.DocumentElement?.LocalName == "data" ? dsDoc.DocumentElement : null);

                XmlElement? dataRoot = null;
                if (dataNode is not null)
                {
                    foreach (XmlNode c in dataNode.ChildNodes)
                        if (c is XmlElement el) { dataRoot = el; break; }
                }

                // Only export the datasets verbatim when they carry a RICH, foreign
                // structure (an imported document root the template can't reproduce,
                // e.g. <us-request> with many nested elements). A sparse data root —
                // a few filled fields (FillField) or a form-shaped import (13347) —
                // must instead be merged onto the template below so the export carries
                // the COMPLETE field structure (all fields, empty ones included) and
                // any CDATA/text values (collected via InnerText) survive.
                int dataElementCount = 0;
                if (dataRoot is not null)
                    foreach (var _ in DescendantElements(dataRoot)) { if (++dataElementCount > 5) break; }

                if (dataRoot is not null && dataElementCount > 5)
                {
                    // Export with xfa:data wrapper
                    var settings = new XmlWriterSettings
                    {
                        Indent = true,
                        OmitXmlDeclaration = false,
                        Encoding = Encoding.UTF8
                    };
                    using var writer = XmlWriter.Create(output, settings);
                    writer.WriteStartElement("xfa", "data",
                        "http://www.xfa.org/schema/xfa-data/1.0/");
                    // Write the data root element (typically the imported XML's root,
                    // e.g. <us-request>). Preserves the document structure expected by callers.
                    ExportXfaNodeClean(dataRoot, writer);
                    writer.WriteEndElement(); // xfa:data
                    writer.Flush();
                    return;
                }
            }
            catch { }
        }

        // Datasets are sparse/empty — build complete XML from template + dataset values
        var templateXml = form.GetXfaTemplateXml();
        if (templateXml is null) { ExportXmlAcroForm(output); return; }

        var dataValues = new Dictionary<string, string>(StringComparer.Ordinal);
        if (datasetsXml is not null)
        {
            try
            {
                var dsDoc = new XmlDocument();
                dsDoc.LoadXml(datasetsXml);
                CollectDataValues(dsDoc.DocumentElement, "", dataValues);
            }
            catch { }
        }

        try
        {
            var tmplDoc = new XmlDocument();
            tmplDoc.LoadXml(templateXml);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = false,
                Encoding = Encoding.UTF8
            };
            using var writer = XmlWriter.Create(output, settings);
            BuildXfaExportXml(tmplDoc.DocumentElement!, "", writer, dataValues);
            writer.Flush();
        }
        catch
        {
            ExportXmlAcroForm(output);
        }
    }

    private static IEnumerable<XmlElement> DescendantElements(XmlElement root)
    {
        foreach (XmlNode child in root.ChildNodes)
        {
            if (child is XmlElement el)
            {
                yield return el;
                foreach (var desc in DescendantElements(el))
                    yield return desc;
            }
        }
    }

    private static void ExportXfaNodeClean(XmlElement element, XmlWriter writer)
    {
        // Force the empty namespace so children of <xfa:data> don't inherit
        // the xfa prefix — XPath callers expect plain element names like
        // //form1/TextField1, not //xfa:form1/xfa:TextField1.
        writer.WriteStartElement(element.LocalName, string.Empty);
        foreach (XmlAttribute attr in element.Attributes)
        {
            if (attr.Prefix == "xmlns" || attr.Name == "xmlns") continue;
            writer.WriteAttributeString(attr.LocalName, attr.Value);
        }
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement childElement)
                ExportXfaNodeClean(childElement, writer);
            else if (child is XmlCharacterData cdata
                     && (child.NodeType == XmlNodeType.Text || child.NodeType == XmlNodeType.CDATA))
                writer.WriteString(cdata.Value ?? "");
        }
        writer.WriteEndElement();
    }

    /// <summary>Collect all leaf values from the datasets XML into a path→value map.</summary>
    private static void CollectDataValues(XmlNode? node, string path, Dictionary<string, string> values)
    {
        if (node is null) return;

        // Skip the datasets/data wrapper — descend to the data content root
        if (node.LocalName is "datasets")
        {
            foreach (XmlNode c in node.ChildNodes)
                if (c.NodeType == XmlNodeType.Element && c.LocalName == "data")
                    { CollectDataValues(c, path, values); return; }
            return;
        }

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var childPath = string.IsNullOrEmpty(path) ? child.LocalName : $"{path}/{child.LocalName}";

            bool hasElementChildren = false;
            foreach (XmlNode gc in child.ChildNodes)
                if (gc.NodeType == XmlNodeType.Element) { hasElementChildren = true; break; }

            if (hasElementChildren)
                CollectDataValues(child, childPath, values);
            else
                values[childPath] = child.InnerText ?? "";
        }
    }

    /// <summary>Build XFA export XML by walking the template and writing elements for each subform/field.</summary>
    private static void BuildXfaExportXml(XmlNode templateNode, string dataPath,
        XmlWriter writer, Dictionary<string, string> dataValues)
    {
        // Find the root subform (e.g. <subform name="form1">)
        foreach (XmlNode child in templateNode.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element) continue;
            var localName = child.LocalName;
            var nameAttr = child.Attributes?["name"]?.Value;

            if (localName is "subform" or "subformSet" or "area")
            {
                if (nameAttr is not null)
                {
                    var childPath = string.IsNullOrEmpty(dataPath) ? nameAttr : $"{dataPath}/{nameAttr}";
                    writer.WriteStartElement(nameAttr);
                    BuildXfaExportXml(child, childPath, writer, dataValues);
                    writer.WriteEndElement();
                }
                else
                {
                    BuildXfaExportXml(child, dataPath, writer, dataValues);
                }
            }
            else if (localName == "field"
                || (localName == "draw" && nameAttr is not null
                    && templateNode.Attributes?["layout"]?.Value is not "row" and not "table"))
            {
                if (nameAttr is not null)
                {
                    var fieldPath = string.IsNullOrEmpty(dataPath) ? nameAttr : $"{dataPath}/{nameAttr}";
                    writer.WriteStartElement(nameAttr);
                    if (dataValues.TryGetValue(fieldPath, out var val))
                        writer.WriteString(val);
                    writer.WriteEndElement();
                }
            }
            else if (localName is "exclGroup")
            {
                if (nameAttr is not null)
                {
                    var fieldPath = string.IsNullOrEmpty(dataPath) ? nameAttr : $"{dataPath}/{nameAttr}";
                    writer.WriteStartElement(nameAttr);
                    if (dataValues.TryGetValue(fieldPath, out var val))
                        writer.WriteString(val);
                    writer.WriteEndElement();
                }
            }
            else
            {
                // Recurse into other template elements (e.g., pageArea, contentArea)
                BuildXfaExportXml(child, dataPath, writer, dataValues);
            }
        }
    }

    // ── XFA FDF / XFDF Import/Export ──────────────────────────────────────
    //
    // A dynamic XFA form carries no AcroForm widget fields, so the AcroForm
    // FDF/XFDF exporters emit an empty /Fields set. These XFA-aware variants
    // build the data tree from the template (with values from the datasets
    // packet, if any), the same source ExportXmlXfa uses, and render it as
    // FDF (flat /T(leaf[0]) entries) or XFDF (nested xfdf:fields). Import
    // resolves the entries back to XFA data paths and persists them through
    // the same ReplaceXfaDatasets path ImportXml uses.

    /// <summary>Build the XFA data tree (root e.g. &lt;form1&gt; with one child per
    /// field/subform) from the template, filling values from the datasets packet.
    /// Returns null when no XFA template is present.</summary>
    private XmlDocument? BuildXfaDataDocument()
    {
        var form = _doc!.Form;
        var templateXml = form.GetXfaTemplateXml();
        if (templateXml is null) return null;

        var dataValues = new Dictionary<string, string>(StringComparer.Ordinal);
        var datasetsXml = form.GetXfaDatasetsXml();
        if (datasetsXml is not null)
        {
            try
            {
                var ds = new XmlDocument();
                ds.LoadXml(datasetsXml);
                CollectDataValues(ds.DocumentElement, "", dataValues);
            }
            catch { }
        }

        try
        {
            var tmpl = new XmlDocument();
            tmpl.LoadXml(templateXml);
            using var ms = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = true,
                Encoding = new UTF8Encoding(false),
            };
            using (var w = XmlWriter.Create(ms, settings))
            {
                BuildXfaExportXml(tmpl.DocumentElement!, "", w, dataValues);
                w.Flush();
            }
            var dataDoc = new XmlDocument();
            dataDoc.LoadXml(Encoding.UTF8.GetString(ms.ToArray()));
            return dataDoc;
        }
        catch { return null; }
    }

    private static List<XmlElement> ElementChildren(XmlNode node)
    {
        var list = new List<XmlElement>();
        foreach (XmlNode c in node.ChildNodes)
            if (c is XmlElement el) list.Add(el);
        return list;
    }

    private void ExportFdfXfa(Stream output)
    {
        var dataDoc = BuildXfaDataDocument();
        var fields = XfaFieldPathsNorm();
        var sb = new StringBuilder();
        sb.Append("%FDF-1.2\n1 0 obj\n<< /FDF << /Fields [\n");
        if (dataDoc?.DocumentElement is not null)
            EmitFdfLeaves(dataDoc.DocumentElement, sb, fields);
        sb.Append("] >> >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
        var bytes = Encoding.Latin1.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Emit a flat <c>/T(leafName[0]) /V(value)</c> FDF entry for each
    /// leaf field (an element with no element children). When <paramref name="fields"/>
    /// is non-null, a leaf that isn't a genuine XFA field (an empty subform/draw the
    /// template build left behind) is skipped so the export carries only bound fields.</summary>
    private static void EmitFdfLeaves(XmlElement element, StringBuilder sb, List<string>? fields)
    {
        var children = ElementChildren(element);
        if (children.Count == 0)
        {
            if (fields is not null && !IsKnownXfaField(element.LocalName, fields)) return;
            sb.Append("  << /T(");
            sb.Append(EscapeFdf(element.LocalName + "[0]"));
            sb.Append(") /V(");
            sb.Append(EscapeFdf(element.InnerText));
            sb.Append(") >>\n");
            return;
        }
        foreach (var child in children)
            EmitFdfLeaves(child, sb, fields);
    }

    private static string EscapeFdf(string s)
        => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private void ExportXfdfXfa(Stream output)
    {
        var dataDoc = BuildXfaDataDocument();
        const string ns = "http://ns.adobe.com/xfdf/";
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
        };
        using var w = XmlWriter.Create(output, settings);
        w.WriteStartDocument();
        w.WriteStartElement("xfdf", ns);
        w.WriteStartElement("fields", ns);
        if (dataDoc?.DocumentElement is not null)
            EmitXfdfField(dataDoc.DocumentElement, w, ns, XfaFieldPathsNorm());
        w.WriteEndElement(); // fields
        w.WriteEndElement(); // xfdf
        w.WriteEndDocument();
    }

    /// <summary>Emit a nested <c>&lt;field name="elem[0]"&gt;</c> element. A container
    /// holds a <c>&lt;fields&gt;</c> with its children; a leaf holds a <c>&lt;value&gt;</c>.
    /// A leaf that isn't a genuine XFA field (when <paramref name="fields"/> is non-null)
    /// is skipped so only bound fields carry a value entry.</summary>
    private static void EmitXfdfField(XmlElement element, XmlWriter w, string ns, List<string>? fields)
    {
        var children = ElementChildren(element);
        // A known XFA field is a value leaf; anything else (including an empty
        // subform such as a table header row) is a container that still exports a
        // <field><fields/></field> wrapper.
        bool isField = fields is not null && IsKnownXfaField(element.LocalName, fields);

        w.WriteStartElement("field", ns);
        w.WriteAttributeString("name", element.LocalName + "[0]");

        if (isField)
        {
            // Value field: emit <value> only when filled; an unfilled field is
            // self-closing (<field name="X[0]" />).
            var value = element.InnerText;
            if (!string.IsNullOrEmpty(value))
            {
                w.WriteStartElement("value", ns);
                w.WriteString(value);
                w.WriteEndElement(); // value
            }
        }
        else
        {
            // Container subform: always wrap children in <fields> (empty → <fields />).
            w.WriteStartElement("fields", ns);
            foreach (var child in children)
                EmitXfdfField(child, w, ns, fields);
            w.WriteEndElement(); // fields
        }
        w.WriteEndElement(); // field
    }

    private void ImportFdfXfa(byte[] bytes)
    {
        var dataDoc = BuildXfaDataDocument();
        if (dataDoc?.DocumentElement is null) return;
        var text = Encoding.Latin1.GetString(bytes);
        foreach (var (name, value) in ParseFdfTV(text))
            SetDataDocValue(dataDoc.DocumentElement, name, value);
        _doc!.Form.ReplaceXfaDatasets(dataDoc);
    }

    private void ImportXfdfXfa(string xfdfXml)
    {
        var dataDoc = BuildXfaDataDocument();
        if (dataDoc?.DocumentElement is null) return;
        XmlDocument xfdf;
        try { xfdf = new XmlDocument(); xfdf.LoadXml(xfdfXml); }
        catch { return; }
        var fields = xfdf.DocumentElement?.SelectSingleNode("*[local-name()='fields']");
        if (fields is null) return;
        foreach (var (path, value) in CollectXfdfValues(fields, parentPath: null))
            SetDataDocValue(dataDoc.DocumentElement, path, value);
        _doc!.Form.ReplaceXfaDatasets(dataDoc);
    }

    /// <summary>Walk nested xfdf:field elements, yielding (dotted-path, value) for
    /// each leaf that carries a &lt;value&gt;. Names keep their [n] index, joined with '.'.</summary>
    private static IEnumerable<(string path, string value)> CollectXfdfValues(XmlNode container, string? parentPath)
    {
        foreach (XmlNode child in container.ChildNodes)
        {
            if (child is not XmlElement fieldEl || fieldEl.LocalName != "field") continue;
            var name = fieldEl.GetAttribute("name");
            if (string.IsNullOrEmpty(name)) continue;
            var path = parentPath is null ? name : parentPath + "." + name;

            var valueEl = fieldEl.SelectSingleNode("*[local-name()='value']");
            var nestedFields = fieldEl.SelectSingleNode("*[local-name()='fields']");
            if (nestedFields is not null)
            {
                foreach (var pair in CollectXfdfValues(nestedFields, path))
                    yield return pair;
            }
            else if (valueEl is not null)
            {
                yield return (path, valueEl.InnerText);
            }
        }
    }

    /// <summary>Set a value in the XFA data document by a dotted path whose segments may
    /// carry [n] indices and may or may not include the data-root element. A bare leaf
    /// name (FDF) is matched anywhere in the tree.</summary>
    private static void SetDataDocValue(XmlElement root, string dottedName, string value)
    {
        var segments = dottedName.Split('.');
        // Drop a leading segment that names the data root itself (e.g. "form1[0]").
        int start = 0;
        if (segments.Length > 1 && StripIndex(segments[0]) == root.LocalName)
            start = 1;

        if (segments.Length - start == 1)
        {
            // Single segment: locate the leaf anywhere under the root by local name.
            var leaf = StripIndex(segments[start]);
            var node = leaf == root.LocalName ? root : FindDescendantByLocalName(root, leaf);
            if (node is not null) node.InnerText = value;
            return;
        }

        XmlElement? current = root;
        for (int i = start; i < segments.Length && current is not null; i++)
        {
            var seg = StripIndex(segments[i]);
            XmlElement? next = null;
            foreach (var c in ElementChildren(current))
                if (c.LocalName == seg) { next = c; break; }
            current = next;
        }
        if (current is not null) current.InnerText = value;
    }

    private static XmlElement? FindDescendantByLocalName(XmlElement root, string localName)
    {
        foreach (var c in ElementChildren(root))
        {
            if (c.LocalName == localName) return c;
            var found = FindDescendantByLocalName(c, localName);
            if (found is not null) return found;
        }
        return null;
    }

    private static string StripIndex(string name)
    {
        var br = name.IndexOf('[');
        return br < 0 ? name : name.Substring(0, br);
    }

    /// <summary>Strip the <c>[n]</c> occurrence index from every dotted segment
    /// (<c>form1[0].P1[0].Employee[0]</c> → <c>form1.P1.Employee</c>).</summary>
    private static string StripPathIndices(string path)
        => string.Join('.', path.Split('.').Select(StripIndex));

    /// <summary>Normalise an XFA SOM path for a full-path match: strip every <c>[n]</c>
    /// occurrence index and drop anonymous <c>#</c>-container segments (e.g.
    /// <c>form1[0].#subform[0].TextField1[0]</c> → <c>form1.TextField1</c>), so a caller's
    /// container-collapsed path lines up with the enumerated field name.</summary>
    /// <summary>Parse flat <c>/T(name) /V(value)</c> pairs from FDF text, honouring
    /// <c>\(</c>/<c>\)</c>/<c>\\</c> escapes.</summary>
    private static IEnumerable<(string name, string value)> ParseFdfTV(string fdf)
    {
        int pos = 0;
        while (true)
        {
            int tIdx = fdf.IndexOf("/T", pos, StringComparison.Ordinal);
            if (tIdx < 0) yield break;
            int open = fdf.IndexOf('(', tIdx);
            if (open < 0) yield break;
            var name = ReadFdfLiteral(fdf, open, out int afterName);
            // Look for an immediately following /V (allow whitespace between).
            int vIdx = fdf.IndexOf("/V", afterName, StringComparison.Ordinal);
            string value = "";
            int next = afterName;
            if (vIdx >= 0 && fdf.IndexOf("/T", afterName, StringComparison.Ordinal) is var nextT
                && (nextT < 0 || vIdx < nextT))
            {
                int vOpen = fdf.IndexOf('(', vIdx);
                if (vOpen >= 0)
                {
                    value = ReadFdfLiteral(fdf, vOpen, out next);
                }
            }
            yield return (name, value);
            pos = next;
        }
    }

    /// <summary>Read a PDF/FDF string literal beginning at <paramref name="open"/> (the '(').</summary>
    private static string ReadFdfLiteral(string s, int open, out int afterClose)
    {
        var sb = new StringBuilder();
        int depth = 0;
        int i = open;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length) { sb.Append(s[++i]); continue; }
            if (c == '(') { if (depth > 0) sb.Append(c); depth++; continue; }
            if (c == ')') { depth--; if (depth == 0) { i++; break; } sb.Append(c); continue; }
            if (depth > 0) sb.Append(c);
        }
        afterClose = i;
        return sb.ToString();
    }

    // ── API-shape additions ───────────────────────────────────────────────

    /// <summary>Extract the document's XFA datasets XML to a stream.</summary>
    public void ExtractXfaData(Stream outputXmlStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (!_doc.Form.IsXfa)
            throw new InvalidOperationException("Document does not contain an XFA form.");
        var xml = _doc.Form.GetXfaDatasetsXml();
        if (xml is null) return;
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        outputXmlStream.Write(bytes, 0, bytes.Length);
        if (outputXmlStream.CanSeek) outputXmlStream.Position = 0;
    }

    /// <summary>Replace the document's XFA datasets XML from a stream.</summary>
    public void SetXfaData(Stream inputXmlStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (!_doc.Form.IsXfa) return;
        using var ms = new MemoryStream();
        if (inputXmlStream.CanSeek) inputXmlStream.Position = 0;
        inputXmlStream.CopyTo(ms);
        ImportXml(new MemoryStream(ms.ToArray()));
    }

    /// <summary>Fill a barcode field by name with the given data.</summary>
    public bool FillBarcodeField(string fieldName, string data)
        => FillField(fieldName, data);

    /// <summary>Fill a text field with the supplied value, optionally fitting font size to the box.</summary>
    public bool FillField(string fieldName, string value, bool fitFontSize)
    {
        _ = fitFontSize; // fit-to-box not currently honoured; value still gets set.
        return FillField(fieldName, value);
    }

    /// <summary>Fill a choice or radio field by selected-option index (0-based).</summary>
    public bool FillField(string fieldName, int index)
    {
        if (_doc?.Form is null) return false;
        var field = _doc.Form.FindByName(fieldName);
        // A radio button is selected by widget index. Its appearance on-state can
        // differ from its export value (the /Opt entry) — "index and value do not
        // match" — so drive the selection by index (RadioButtonField.Selected is
        // 1-based) and, for an XFA-backed form, write the option's export value into
        // the XFA datasets directly. We deliberately bypass the reverse
        // XFA->acro-field sync here: re-applying the export value would re-match it
        // against an unrelated widget on-state and move the selection.
        if (field is RadioButtonField rb)
        {
            if (index < 0 || index >= rb.Options.Count) return false;
            rb.Selected = index + 1; // RadioButtonField.Selected is 1-based
            _doc.Form.SetXfaFieldValue(fieldName, rb.Options[index + 1].Value);
            return true;
        }
        if (field is ChoiceField cf)
        {
            if (index >= 0 && index < cf.Options.Count)
            {
                cf.Selected = index;
                return true;
            }
        }
        return false;
    }

    /// <summary>Fill a list-box field with the supplied multi-select values.</summary>
    public void FillField(string fieldName, string[] fieldValues)
    {
        if (_doc?.Form is null || fieldValues is null) return;
        var field = _doc.Form.FindByName(fieldName);
        if (field is null) return;
        field.Value = string.Join(",", fieldValues);
    }

    /// <summary>
    /// Apply multiple field values and return the resulting PDF as a stream.
    /// Returns true when every name was found.
    /// </summary>
    public bool FillFields(string[] fieldNames, string[] fieldValues, out Stream output)
    {
        output = new MemoryStream();
        if (_doc is null || fieldNames is null || fieldValues is null) return false;
        bool allOk = true;
        for (var i = 0; i < fieldNames.Length && i < fieldValues.Length; i++)
        {
            if (!FillField(fieldNames[i], fieldValues[i])) allOk = false;
        }
        // A signed document must be updated incrementally so the original bytes
        // (and thus the existing signature's /ByteRange) survive; a full rewrite
        // would invalidate the signature.
        var bytes = _doc.Form is { SignaturesExist: true }
            ? _doc.ToArrayIncremental()
            : _doc.ToArray();
        output.Write(bytes, 0, bytes.Length);
        output.Position = 0;
        return allOk;
    }

    /// <summary>Fill an image field by embedding the image as the field widget's
    /// normal appearance (/AP/N), scaled to fill each widget's rectangle. When the
    /// field name is shared by several widgets (e.g. repeated on multiple pages)
    /// every widget receives the image.</summary>
    public void FillImageField(string fieldName, Stream imageStream)
    {
        if (_doc?.Form is null || imageStream is null) return;

        using var ms = new MemoryStream();
        imageStream.CopyTo(ms);
        var imageBytes = ms.ToArray();
        if (imageBytes.Length == 0) return;

        // XFA image fields carry their picture as a base64 datasets value tagged with a
        // contentType; record it so it round-trips through XFA.GetFieldNode. A dynamic
        // XFA field has no AcroForm widget (FindByName is null), so this is the only sink.
        if (_doc.Form.IsXfa)
            _doc.Form.SetXfaFieldImage(fieldName, Convert.ToBase64String(imageBytes),
                DetectImageContentType(imageBytes));

        var field = _doc.Form.FindByName(fieldName);
        if (field is null) return;

        // A field is either a terminal widget (its own dict carries /Rect + /AP) or
        // a parent with one /Kids widget per placement. Target every widget.
        var widgets = field.AllKids().ToList();
        if (widgets.Count == 0) widgets.Add(field.Dict);

        foreach (var widget in widgets)
            SetImageAppearance(widget, imageBytes);

        // Surface the field as an image push-button: filling a (text or button) field
        // with an image converts it to a push button whose icon is the image, so a
        // reloaded document reports FieldType.Image and the field is
        // a ButtonField carrying the image appearance.
        field.Dict.Set("FT", new Core.PdfName("Btn"));
        field.Dict.Set("Ff", new Core.PdfInteger(65536)); // push button
        field.Dict.Remove("V");
        field.Dict.Remove("DV");
    }

    /// <summary>Fill an image field from a file path.</summary>
    public void FillImageField(string fieldName, string imageFileName)
    {
        if (string.IsNullOrEmpty(imageFileName) || !File.Exists(imageFileName)) return;
        using var fs = File.OpenRead(imageFileName);
        FillImageField(fieldName, fs);
    }

    /// <summary>MIME type for an XFA image field's datasets <c>contentType</c>, from the
    /// image's magic bytes. Uses <c>image/jpg</c> (XFA's spelling, not image/jpeg).</summary>
    private static string DetectImageContentType(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 3 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return "image/gif";
        if (b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4D) return "image/bmp";
        return "image/jpg";
    }

    /// <summary>Build an image XObject from <paramref name="imageBytes"/> and write
    /// it as the widget's normal appearance, scaled to fill the widget rectangle.</summary>
    private void SetImageAppearance(Core.PdfDictionary widget, byte[] imageBytes)
    {
        var reader = _doc!.Reader;
        if (reader.Resolve(widget.Get("Rect")) is not Core.PdfArray rectArr || rectArr.Count < 4)
            return;
        var rect = Rectangle.FromPdfArray(rectArr, reader);
        double w = rect.Width, h = rect.Height;
        if (w <= 0 || h <= 0) return;

        // Decode (PNG/JPEG/GDI+) and build a standalone image XObject (with /SMask
        // for transparency), reusing the page-image pipeline.
        using var imgSrc = new MemoryStream(imageBytes, writable: false);
        var stamp = new ImageStamp(imgSrc);
        var imgStream = stamp.BuildImageXObject();

        var xobjects = new Core.PdfDictionary();
        xobjects.Set("Im0", imgStream);
        var resources = new Core.PdfDictionary();
        resources.Set("XObject", xobjects);

        // Honour the widget's icon rotation (/MK /R, in degrees). For 90°/270° the
        // appearance is drawn in a coordinate space with width/height swapped and
        // mapped back into the widget rect by the form /Matrix.
        int rot = 0;
        var mk = reader.ResolveDict(widget.Get("MK"));
        if (mk is not null && mk.ContainsKey("R"))
            rot = (int)(((mk.GetInt("R") % 360) + 360) % 360);
        bool swap = rot == 90 || rot == 270;
        double boxW = swap ? h : w;   // appearance-space box dimensions
        double boxH = swap ? w : h;

        // Proportional fit, centered, inset by a 2-unit icon margin on each side —
        // the standard Acrobat push-button icon fit (/MK /IF default). The image is
        // scaled to fit inside the (inset) box preserving its aspect ratio, then
        // centered; stretch-to-fill would distort non-matching aspect ratios.
        const double inset = 2.0;
        double availW = Math.Max(0, boxW - 2 * inset);
        double availH = Math.Max(0, boxH - 2 * inset);
        double dw = availW, dh = availH, tx = inset, ty = inset;
        if (stamp.PixelWidth > 0 && stamp.PixelHeight > 0 && availW > 0 && availH > 0)
        {
            double scale = Math.Min(availW / stamp.PixelWidth, availH / stamp.PixelHeight);
            dw = stamp.PixelWidth * scale;
            dh = stamp.PixelHeight * scale;
            tx = inset + (availW - dw) / 2.0;
            ty = inset + (availH - dh) / 2.0;
        }
        var content = Encoding.ASCII.GetBytes(
            $"q {Fmt(dw)} 0 0 {Fmt(dh)} {Fmt(tx)} {Fmt(ty)} cm /Im0 Do Q");

        var apN = new Core.PdfDictionary();
        apN.Set("Type", new Core.PdfName("XObject"));
        apN.Set("Subtype", new Core.PdfName("Form"));
        apN.Set("FormType", new Core.PdfInteger(1));
        apN.Set("BBox", MakeRectArray(0, 0, boxW, boxH));
        if (rot != 0)
            apN.Set("Matrix", IconRotationMatrix(rot, w, h));
        apN.Set("Resources", resources);
        apN.Set("Length", new Core.PdfInteger(content.Length));
        var apStream = new Core.PdfStream(apN, content);

        var ap = reader.ResolveDict(widget.Get("AP")) ?? new Core.PdfDictionary();
        ap.Set("N", apStream);
        widget.Set("AP", ap);

        // Mark the widget as an icon (image) button so GetFieldType reports Image after
        // a reload, and expose the icon via /MK /I.
        if (mk is null) { mk = new Core.PdfDictionary(); widget.Set("MK", mk); }
        mk.Set("TP", new Core.PdfInteger(1));
        mk.Set("I", apStream);
    }

    /// <summary>The /Matrix that maps an icon appearance drawn in the (rotated)
    /// appearance space back into the widget rectangle for an /MK /R rotation.</summary>
    private static Core.PdfArray IconRotationMatrix(int rot, double w, double h)
    {
        var (a, b, c, d, e, f) = rot switch
        {
            90  => (0.0, 1.0, -1.0, 0.0, h, 0.0),
            180 => (-1.0, 0.0, 0.0, -1.0, w, h),
            270 => (0.0, -1.0, 1.0, 0.0, 0.0, w),
            _   => (1.0, 0.0, 0.0, 1.0, 0.0, 0.0),
        };
        var arr = new Core.PdfArray();
        foreach (var v in new[] { a, b, c, d, e, f }) arr.Add(new Core.PdfReal(v));
        return arr;
    }

    private static Core.PdfArray MakeRectArray(double llx, double lly, double urx, double ury)
    {
        var arr = new Core.PdfArray();
        arr.Add(new Core.PdfReal(llx));
        arr.Add(new Core.PdfReal(lly));
        arr.Add(new Core.PdfReal(urx));
        arr.Add(new Core.PdfReal(ury));
        return arr;
    }

    private static string Fmt(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Get the PropertyFlag set on a field (ReadOnly / Required / NoExport).</summary>
    public PropertyFlag GetFieldFlag(string fieldName)
    {
        if (_doc?.Form is null) return PropertyFlag.InvalidFlag;
        var f = _doc.Form.FindByName(fieldName);
        if (f is null) return PropertyFlag.InvalidFlag;
        int ff = (int)f.Dict.GetInt("Ff");
        if ((ff & 1) != 0) return PropertyFlag.ReadOnly;
        if ((ff & 2) != 0) return PropertyFlag.Required;
        if ((ff & 4) != 0) return PropertyFlag.NoExport;
        return PropertyFlag.InvalidFlag;
    }

    /// <summary>Return the field's rich-text contents (the /RV entry) or null when absent.</summary>
    public string? GetRichText(string fieldName)
    {
        if (_doc?.Form is null) return null;
        var f = _doc.Form.FindByName(fieldName);
        if (f is null) return null;
        var rv = f.Dict.Get("RV");
        return rv is Core.PdfString s ? s.ToText() : null;
    }

    /// <summary>Return the SubmitFormFlag recorded on a submit-button field, read back
    /// from its /A submit-form action's /Flags bitmask.</summary>
    public SubmitFormFlag GetSubmitFlags(string fieldName)
    {
        if (_doc?.Form is null) return SubmitFormFlag.Fdf;
        var f = _doc.Form.FindByName(fieldName);
        if (f is null) return SubmitFormFlag.Fdf;
        var action = _doc.Reader.ResolveDict(f.Dict.Get("A"));
        var flags = action?.Get("Flags") is Core.PdfInteger n ? (int)n.Value : 0;
        const int xfdf = Annotations.SubmitFormAction.Xfdf;
        const int comments = Annotations.SubmitFormAction.IncludeAnnotations;
        return flags switch
        {
            Annotations.SubmitFormAction.SubmitPdf => SubmitFormFlag.Pdf,
            xfdf | comments => SubmitFormFlag.XfdfWithComments,
            xfdf => SubmitFormFlag.Xfdf,
            Annotations.SubmitFormAction.ExportFormat => SubmitFormFlag.Html,
            comments => SubmitFormFlag.FdfWithComments,
            _ => SubmitFormFlag.Fdf,
        };
    }

    /// <summary>True when the field has the Required flag set.</summary>
    public bool IsRequiredField(string fieldName)
    {
        if (_doc?.Form is null) return false;
        var f = _doc.Form.FindByName(fieldName);
        if (f is null) return false;
        return ((int)f.Dict.GetInt("Ff") & 2) != 0;
    }

    /// <summary>Rename a form field.</summary>
    public void RenameField(string fieldName, string newFieldName)
    {
        if (_doc?.Form is null) return;
        _doc.Form.FindByName(fieldName)?.SetPartialName(newFieldName);
    }

    /// <summary>
    /// Import form field values from an XML stream (parameterless overload — same as
    /// <see cref="ImportXml(Stream, bool)"/> with <c>ignoreFormTemplateChanges=false</c>).
    /// </summary>
    public void ImportXml(Stream inputXmlStream) => ImportXml(inputXmlStream, IgnoreFormTemplateChanges: false);
}
