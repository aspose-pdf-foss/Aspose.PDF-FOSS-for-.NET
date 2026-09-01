using System.IO;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for form operations: fill fields, flatten forms, import/export data.
/// </summary>
public sealed partial class FormEditor : IDisposable
{
    private Document? _document;
    private string? _srcFileName;
    private Stream? _srcStream;
    private string? _destFileName;
    private Stream? _destStream;
    private PdfFormat? _convertTo;

    /// <summary>The document bound to this editor, exposed so it can be
    /// chained into another facade.</summary>
    public Document Document => _document ?? throw new InvalidOperationException("No document bound.");

    /// <summary>Default constructor for stateless byte[]-based API.</summary>
    public FormEditor() { }

    /// <summary>Bind the editor to a document for in-place operations.</summary>
    public FormEditor(Document document)
    {
        _document = document;
    }

    /// <summary>Bind a document and pre-configure an output stream for the parameterless Save.</summary>
    public FormEditor(Document document, Stream destStream)
    {
        _document = document;
        _destStream = destStream;
    }

    /// <summary>Bind a document and pre-configure an output file path for the parameterless Save.</summary>
    public FormEditor(Document document, string destFileName)
    {
        _document = document;
        _destFileName = destFileName;
    }

    /// <summary>Open from an input stream, writing to an output stream on Save.</summary>
    public FormEditor(Stream srcStream, Stream destStream)
    {
        _srcStream = srcStream;
        _destStream = destStream;
        using var ms = new MemoryStream();
        if (srcStream.CanSeek) srcStream.Position = 0;
        srcStream.CopyTo(ms);
        _document = Document.Open(ms.ToArray());
    }

    /// <summary>Open from an input file, writing to an output file on Save.</summary>
    public FormEditor(string srcFileName, string destFileName)
    {
        _srcFileName = srcFileName;
        _destFileName = destFileName;
        _document = Document.Open(File.ReadAllBytes(srcFileName));
        // Binding to a destination path CLAIMS that path immediately: the file exists
        // (empty) from the moment the editor is constructed, and Save fills it in. The
        // source is read first, so binding a document onto its own path is still safe.
        CreateDestinationPlaceholder(destFileName);
    }

    /// <summary>Create (or truncate) the bound destination file so it exists as soon as the
    /// editor is constructed. Failures are ignored - an unwritable path surfaces on Save,
    /// which is where the caller expects the write to happen.</summary>
    private static void CreateDestinationPlaceholder(string destFileName)
    {
        if (string.IsNullOrEmpty(destFileName)) return;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(destFileName);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.Create(destFileName).Dispose();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ── Stateful destination/source properties ────────────────────────────

    /// <summary>Source file path used by the parameterless ctor + Save flow.</summary>
    public string? SrcFileName
    {
        get => _srcFileName;
        set
        {
            _srcFileName = value;
            if (value is not null) _document = Document.Open(File.ReadAllBytes(value));
        }
    }

    /// <summary>Source stream used by the parameterless ctor + Save flow.</summary>
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
                _document = Document.Open(ms.ToArray());
            }
        }
    }

    /// <summary>Destination file path written by the parameterless <see cref="Save()"/>.</summary>
    public string? DestFileName
    {
        get => _destFileName;
        set => _destFileName = value;
    }

    /// <summary>Destination stream written by the parameterless <see cref="Save()"/>.</summary>
    public Stream? DestStream
    {
        get => _destStream;
        set => _destStream = value;
    }

    /// <summary>
    /// Target PDF/A or PDF version for the saved output. Stored only; the save path emits plain PDF.
    /// </summary>
    public PdfFormat ConvertTo
    {
        set => _convertTo = value;
    }

    /// <summary>Visual appearance applied by <see cref="AddField"/> and <see cref="DecorateField()"/>.</summary>
    public FormFieldFacade Facade { get; set; } = new FormFieldFacade();

    /// <summary>Default list of items used by <see cref="AddField"/> when creating ListBox / ComboBox fields.</summary>
    public string[]? Items { get; set; }

    /// <summary>Export-value pairs paired with <see cref="Items"/>.</summary>
    public string[][]? ExportItems { get; set; }

    /// <summary>Radio-button item edge length (in points) used when laying out a radio group.</summary>
    public double RadioButtonItemSize { get; set; } = 12.0;

    /// <summary>Gap (in points) between radio-button items in a group.</summary>
    public float RadioGap { get; set; } = 4f;

    /// <summary>When true, radio-button items are laid out horizontally; otherwise vertically.</summary>
    public bool RadioHoriz { get; set; }

    /// <summary>Submit format used by <see cref="AddSubmitBtn"/> and <see cref="SetSubmitFlag"/>.</summary>
    public SubmitFormFlag SubmitFlag { get; set; } = SubmitFormFlag.Fdf;

    // ── BindPdf overloads ─────────────────────────────────────────────────

    /// <summary>Bind a PDF file by path for editing.</summary>
    public void BindPdf(string path)
    {
        _document = Document.Open(File.ReadAllBytes(path));
    }

    /// <summary>Bind a PDF from a byte buffer.</summary>
    public void BindPdf(byte[] pdfData)
    {
        _document = Document.Open(pdfData);
    }

    /// <summary>Bind a PDF from a stream.</summary>
    public void BindPdf(Stream stream)
    {
        // Bind the WHOLE stream, not the tail past its current position: callers
        // routinely hand over a MemoryStream they just saved a document into, which
        // leaves the position at the end (a copy from there yields no bytes at all).
        using var ms = new MemoryStream();
        if (stream.CanSeek) stream.Position = 0;
        stream.CopyTo(ms);
        _document = Document.Open(ms.ToArray());
    }

    /// <summary>Bind a PDF from an already-open <see cref="Document"/>.</summary>
    public void BindPdf(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    // ── Save overloads ────────────────────────────────────────────────────

    /// <summary>Save the bound document to a file path.</summary>
    public void Save(string path)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        _document.Save(path);
    }

    /// <summary>Save the bound document to a stream.</summary>
    public void Save(Stream destStream)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        _document.Save(destStream);
    }

    /// <summary>
    /// Save the bound document to the configured destination (<see cref="DestFileName"/>
    /// or <see cref="DestStream"/>). Throws when neither is configured.
    /// </summary>
    public void Save()
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        if (_destFileName is null && _destStream is null)
            throw new InvalidOperationException("No destination configured. Set DestFileName or DestStream, or call Save(string)/Save(Stream).");
        ApplyConvertTo();
        var bytes = _document.ToArray();
        if (_destFileName is not null) File.WriteAllBytes(_destFileName, bytes);
        if (_destStream is not null) _destStream.Write(bytes);
    }

    /// <summary>Apply a <see cref="ConvertTo"/> target PDF version to the bound document
    /// so the saved file header carries it. Only the plain <c>v_X_Y</c> versions are
    /// honoured here; PDF/A and other conformance formats need a full conversion pipeline
    /// that this facade does not implement.</summary>
    private void ApplyConvertTo()
    {
        if (_document is null || _convertTo is null) return;
        var version = _convertTo switch
        {
            PdfFormat.v_1_0 => "1.0",
            PdfFormat.v_1_1 => "1.1",
            PdfFormat.v_1_2 => "1.2",
            PdfFormat.v_1_3 => "1.3",
            PdfFormat.v_1_4 => "1.4",
            PdfFormat.v_1_5 => "1.5",
            PdfFormat.v_1_6 => "1.6",
            PdfFormat.v_1_7 => "1.7",
            PdfFormat.v_2_0 => "2.0",
            _ => null,
        };
        if (version is not null) _document.SetVersion(version);
    }

    /// <summary>Close and release the bound document.</summary>
    public void Close()
    {
        _document?.Dispose();
        _document = null;
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    // ── Field-set helpers ─────────────────────────────────────────────────

    /// <summary>Set a field value by name. Returns false if the field was not found.</summary>
    public bool SetField(string fieldName, string value)
    {
        if (_document?.Form is null) return false;
        var field = _document.Form.FindFieldOrNull(fieldName);
        if (field is null) return false;
        field.Value = value;
        return true;
    }

    /// <summary>Check a checkbox field by name. Returns false if the field was not found or is not a checkbox.</summary>
    public bool CheckField(string fieldName)
    {
        if (_document?.Form is null) return false;
        var field = _document.Form.FindFieldOrNull(fieldName);
        if (field is not CheckboxField cb) return false;
        cb.IsChecked = true;
        return true;
    }

    /// <summary>Uncheck a checkbox field by name. Returns false if the field was not found or is not a checkbox.</summary>
    public bool UncheckField(string fieldName)
    {
        if (_document?.Form is null) return false;
        var field = _document.Form.FindFieldOrNull(fieldName);
        if (field is not CheckboxField cb) return false;
        cb.IsChecked = false;
        return true;
    }

    // ── Field-add convenience helpers ────────────────────────────────────

    /// <summary>Add a text field to the bound document.</summary>
    public void AddTextField(string fieldName, int pageNumber, double llx, double lly, double urx, double ury)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var page = _document.Pages[pageNumber];
        var builder = new FormFieldBuilder(_document);
        builder.AddTextField(page, fieldName, new Rectangle(llx, lly, urx, ury));
    }

    /// <summary>Add a checkbox field to the bound document.</summary>
    public void AddCheckBox(string fieldName, int pageNumber, double llx, double lly, double urx, double ury, bool isChecked = false)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var page = _document.Pages[pageNumber];
        var builder = new FormFieldBuilder(_document);
        builder.AddCheckBox(page, fieldName, new Rectangle(llx, lly, urx, ury), isChecked);
    }

    // ── AddField ─────────────────────────────────────────────────────────

    // ── List-item helpers ────────────────────────────────────────────────

    // ── Submit-button + scripts ──────────────────────────────────────────

    // ── Copy / move / rename ─────────────────────────────────────────────

    /// <summary>Map the facade font enum to its standard /DA resource abbreviation,
    /// /BaseFont name and font subtype for the AcroForm /DR registration.</summary>
    private static (string abbr, string baseFont, string subtype) DaFontFor(FontStyle style) => style switch
    {
        FontStyle.Courier => ("Cour", "Courier", "Type1"),
        FontStyle.CourierBold => ("CoBo", "Courier-Bold", "Type1"),
        FontStyle.CourierOblique => ("CoOb", "Courier-Oblique", "Type1"),
        FontStyle.CourierBoldOblique => ("CoBO", "Courier-BoldOblique", "Type1"),
        FontStyle.HelveticaBold => ("HeBo", "Helvetica-Bold", "Type1"),
        FontStyle.HelveticaOblique => ("HeOb", "Helvetica-Oblique", "Type1"),
        FontStyle.HelveticaBoldOblique => ("HeBO", "Helvetica-BoldOblique", "Type1"),
        FontStyle.TimesRoman => ("TiRo", "Times-Roman", "Type1"),
        FontStyle.TimesBold => ("TiBo", "Times-Bold", "Type1"),
        FontStyle.TimesItalic => ("TiIt", "Times-Italic", "Type1"),
        FontStyle.TimesBoldItalic => ("TiBI", "Times-BoldItalic", "Type1"),
        FontStyle.Symbol => ("Symb", "Symbol", "Type1"),
        FontStyle.ZapfDingbats => ("ZaDb", "ZapfDingbats", "Type1"),
        FontStyle.CjkFont => ("CJKF", "BitstreamCyberCJK-Roman", "TrueType"),
        _ => ("Helv", "Helvetica", "Type1"),
    };

    private static FieldType MapToFacadeType(Forms.FieldType t)
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

    /// <summary>Reset every property on <see cref="Facade"/>.</summary>
    public void ResetFacade() => Facade.Reset();

    /// <summary>Alias for <see cref="ResetFacade"/> covering nested-facade callers.</summary>
    public void ResetInnerFacade() => Facade.Reset();

    // ── Setters that return success ──────────────────────────────────────

    // ── Stateless (byte[]-in / byte[]-out) helpers ───────────────────────

    /// <summary>Fill form fields with the given values (field name → value).</summary>
    public byte[] FillFields(byte[] input, Dictionary<string, string> fieldValues)
    {
        using var doc = Document.Open(input);
        if (doc.Form is null)
            throw new InvalidOperationException("Document has no form.");

        foreach (var (name, value) in fieldValues)
        {
            var field = doc.Form.FindFieldOrNull(name);
            if (field is not null)
                field.Value = value;
        }

        return doc.ToArray();
    }

    /// <summary>Fill a single form field by name.</summary>
    public byte[] FillField(byte[] input, string fieldName, string value)
        => FillFields(input, new Dictionary<string, string> { [fieldName] = value });

    /// <summary>Flatten the form — render fields as static content and remove the AcroForm.</summary>
    public byte[] FlattenForm(byte[] input)
    {
        using var doc = Document.Open(input);
        if (!doc.HasForm)
            return input;
        doc.Form.Flatten(doc);
        return doc.ToArray();
    }

    /// <summary>Get all field names in the document's form.</summary>
    public string[] GetFieldNames(byte[] input)
    {
        using var doc = Document.Open(input);
        if (!doc.HasForm) return [];

        var names = new List<string>();
        foreach (var field in doc.Form.Fields)
        {
            if (field.FullName is not null)
                names.Add(field.FullName);
        }
        return names.ToArray();
    }

    /// <summary>Get the value of a specific field.</summary>
    public string? GetFieldValue(byte[] input, string fieldName)
    {
        using var doc = Document.Open(input);
        return doc.Form?.FindFieldOrNull(fieldName)?.Value;
    }

    /// <summary>Get the type of a specific field.</summary>
    public Forms.FieldType? GetFieldType(byte[] input, string fieldName)
    {
        using var doc = Document.Open(input);
        return doc.Form?.FindFieldOrNull(fieldName)?.Type;
    }

    /// <summary>Check if the document has a form.</summary>
    public bool HasForm(byte[] input)
    {
        using var doc = Document.Open(input);
        return doc.HasForm;
    }

    /// <summary>Get the total number of form fields.</summary>
    public int GetFieldCount(byte[] input)
    {
        using var doc = Document.Open(input);
        return doc.Form?.Count ?? 0;
    }

    /// <summary>Export form data as a dictionary (field name → value).</summary>
    public Dictionary<string, string> ExportFormData(byte[] input)
    {
        using var doc = Document.Open(input);
        var result = new Dictionary<string, string>();
        if (doc.Form is null) return result;

        foreach (var field in doc.Form.Fields)
        {
            if (field.Value is not null && field.FullName is not null)
                result[field.FullName] = field.Value;
        }
        return result;
    }

    /// <summary>Import form data from a dictionary.</summary>
    public byte[] ImportFormData(byte[] input, Dictionary<string, string> data)
        => FillFields(input, data);

    /// <summary>
    /// Rename a form field in the supplied bytes. Returns the updated PDF bytes and
    /// whether the field was found. Stateless variant of <see cref="RenameField(string,string)"/>.
    /// </summary>
    public (byte[] Pdf, bool Found) RenameField(byte[] input, string oldName, string newPartialName)
    {
        using var doc = Document.Open(input);
        if (doc.Form is null) return (input, false);

        var field = doc.Form.FindFieldOrNull(oldName);
        if (field is null) return (input, false);

        field.SetPartialName(newPartialName);
        return (doc.ToArray(), true);
    }
}
