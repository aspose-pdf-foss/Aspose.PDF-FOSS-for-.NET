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
        // source stream on Save). Preserve the caller's position so the same source
        // stream can be reused across several Form instances in a loop.
        long origPos = srcStream.CanSeek ? srcStream.Position : -1;
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
            var names = new List<string>();
            foreach (var f in _doc.Form.Fields)
            {
                var name = f.FullName ?? f.PartialName;
                if (name is not null) names.Add(name);
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
        _doc.Form.Flatten(_doc);
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
        xml.Load(xmlStream);

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
        HashSet<string>? xfaPaths = null;
        if (form.IsXfa && form.XFA is not null)
        {
            xfaPaths = new HashSet<string>(form.XFA.FieldNames, StringComparer.Ordinal);
        }

        var list = new List<FormImportResult>();
        foreach (var name in fieldNames)
        {
            bool found = xfaPaths is not null
                ? xfaPaths.Contains(name)
                : form.FindByName(name) is not null;
            var status = found ? ImportStatus.Success : ImportStatus.FieldNotFound;
            list.Add(new FormImportResult { FieldName = name, Status = status });
        }
        return list.ToArray();
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

            // Extract value: check for <value> child element first
            var valueNode = node.SelectSingleNode("value");
            var fieldValue = valueNode?.InnerText ?? node.InnerText;

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
        // If the datasets contain rich content (from import/fill), extract and export directly
        if (datasetsXml is not null)
        {
            try
            {
                var dsDoc = new XmlDocument();
                dsDoc.LoadXml(datasetsXml);
                // Find the <data> node inside <datasets>
                XmlNode? dataNode = null;
                if (dsDoc.DocumentElement?.LocalName == "datasets")
                {
                    foreach (XmlNode c in dsDoc.DocumentElement.ChildNodes)
                        if (c.NodeType == XmlNodeType.Element && c.LocalName == "data")
                            { dataNode = c; break; }
                }
                else
                    dataNode = dsDoc.DocumentElement;

                var dataRoot = dataNode?.FirstChild as XmlElement ?? dsDoc.DocumentElement as XmlElement;
                if (dataRoot is not null)
                {
                    // Count data elements — if >5, datasets have real content, use them directly
                    int elementCount = 0;
                    foreach (var _ in IterateElements(dataRoot)) { elementCount++; if (elementCount > 5) break; }

                    if (elementCount > 5)
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

    private static IEnumerable<XmlElement> IterateElements(XmlElement root)
    {
        foreach (XmlNode child in root.ChildNodes)
        {
            if (child is XmlElement el)
            {
                yield return el;
                foreach (var desc in IterateElements(el))
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
            else if (child is XmlText text)
                writer.WriteString(text.Value ?? "");
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

    /// <summary>Fill a choice field by selected-option index.</summary>
    public bool FillField(string fieldName, int index)
    {
        if (_doc?.Form is null) return false;
        if (_doc.Form.FindByName(fieldName) is ChoiceField cf)
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
        var bytes = _doc.ToArray();
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
        var field = _doc.Form.FindByName(fieldName);
        if (field is null) return;

        using var ms = new MemoryStream();
        imageStream.CopyTo(ms);
        var imageBytes = ms.ToArray();
        if (imageBytes.Length == 0) return;

        // A field is either a terminal widget (its own dict carries /Rect + /AP) or
        // a parent with one /Kids widget per placement. Target every widget.
        var widgets = field.AllKids().ToList();
        if (widgets.Count == 0) widgets.Add(field.Dict);

        foreach (var widget in widgets)
            SetImageAppearance(widget, imageBytes);
    }

    /// <summary>Fill an image field from a file path.</summary>
    public void FillImageField(string fieldName, string imageFileName)
    {
        if (string.IsNullOrEmpty(imageFileName) || !File.Exists(imageFileName)) return;
        using var fs = File.OpenRead(imageFileName);
        FillImageField(fieldName, fs);
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

        // Proportional fit, centered — the standard Acrobat icon fit (/MK /IF). The
        // image is scaled to fit inside the widget rectangle preserving its aspect
        // ratio, then centered. This matches the AddImage placement and
        // how independent renderers (pdf.js / MuPDF) draw button icons; stretch-to-
        // fill would distort images whose aspect ratio differs from the widget.
        double dw = w, dh = h, tx = 0, ty = 0;
        if (stamp.PixelWidth > 0 && stamp.PixelHeight > 0)
        {
            double imgAspect = (double)stamp.PixelWidth / stamp.PixelHeight;
            double rectAspect = w / h;
            if (imgAspect > rectAspect) { dw = w; dh = dw / imgAspect; }
            else                        { dh = h; dw = dh * imgAspect; }
            tx = (w - dw) / 2.0;
            ty = (h - dh) / 2.0;
        }
        var content = Encoding.ASCII.GetBytes(
            $"q {Fmt(dw)} 0 0 {Fmt(dh)} {Fmt(tx)} {Fmt(ty)} cm /Im0 Do Q");

        var apN = new Core.PdfDictionary();
        apN.Set("Type", new Core.PdfName("XObject"));
        apN.Set("Subtype", new Core.PdfName("Form"));
        apN.Set("FormType", new Core.PdfInteger(1));
        apN.Set("BBox", MakeRectArray(0, 0, w, h));
        apN.Set("Resources", resources);
        apN.Set("Length", new Core.PdfInteger(content.Length));
        var apStream = new Core.PdfStream(apN, content);

        var ap = reader.ResolveDict(widget.Get("AP")) ?? new Core.PdfDictionary();
        ap.Set("N", apStream);
        widget.Set("AP", ap);
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
