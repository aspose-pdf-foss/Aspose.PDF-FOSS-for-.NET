using System.Text;
using System.Xml;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for form field manipulation including XML import/export.
/// This is the Facades API (file-path/stream-based) counterpart to
/// <see cref="Aspose.Pdf.Forms.Form"/> (DOM-based).
/// </summary>
public sealed partial class Form : IDisposable
{
    private Document? _doc;
    private readonly bool _ownsDoc;
    private string? _destPath;
    private bool _ownsDestStream;
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
        // Callers reach for DestStream directly, so it exists from construction. The handle
        // is shared and released on Dispose: several facades are routinely pointed at one
        // destination, and an exclusive handle held for the facade's lifetime would fail the
        // second binding — and any reader of the destination — with a sharing violation.
        DestStream = new FileStream(destFileName, FileMode.Create, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        _ownsDestStream = true;
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
        // so `doc.Save(ms); new Form(ms)` works without
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
        if (stream.CanSeek && stream.Position != 0) stream.Position = 0;
        stream.CopyTo(ms);
        _doc = Document.Open(ms.ToArray());
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
            // logical field name a single time.
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
    /// Flatten a specific field by name — render it as static content and remove interactive state.
    /// Currently flattens the entire form (all fields) since per-field flattening is not implemented.
    /// </summary>
    public void FlattenField(string fieldName)
    {
        if (_doc is null) return;
        var field = _doc.Form.FindFieldOrNull(fieldName);
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
            if (_doc.Form.Type == FormType.Dynamic)
            {
                _doc.Form.SetXfaFieldsReadOnly();
                return;
            }
            // Static XFA: the widgets carry real /AP + /Rect, so fold them into the page
            // content as-authored (no appearance regen — the /AP already reflects the
            // filled value, and regenerating would repaint with this library's default
            // text layout instead of the authored one). Standard-14 fonts referenced by
            // the folded appearances are subset-embedded at save so the flattened text
            // keeps an embedded font.
            _doc.EmbedStandardFonts = true;
            _doc.Form.Flatten(_doc,
                settings: new Forms.Form.FlattenSettings { HideButtons = true, UpdateAppearances = false },
                frmStartIndex: 1, flattenNonWidgets: true);
            return;
        }
        // The facade flatten numbers its flattened FRM{n} XObjects from 1 (document/form flatten
        // numbers from 0) and folds every annotation — including markup such as FreeText — into
        // the page content so the FRM index lines up with /Annots. Push buttons carry no
        // persistent value and are UI affordances, so flattening drops them entirely rather than
        // stamping their (often coloured) appearance as static ink.
        _doc.Form.Flatten(_doc,
            settings: new Forms.Form.FlattenSettings { HideButtons = true, UpdateAppearances = true },
            frmStartIndex: 1, flattenNonWidgets: true);
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
            // A stream this facade opened itself is closed here: the contract is that Save()
            // returns with the destination fully written and unlocked, so callers can
            // immediately do `new Document(destPath)`. A caller-supplied stream is left
            // open — closing it would be the caller's call, not ours.
            if (_ownsDestStream)
            {
                DestStream.Dispose();
                DestStream = null;
                _ownsDestStream = false;
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
        if (_ownsDestStream)
        {
            DestStream?.Dispose();
            DestStream = null;
            _ownsDestStream = false;
        }
    }

    // ── AcroForm XML Import ─────────────────────────────────────────

    // ── AcroForm XML Export ─────────────────────────────────────────

    // ── XFA XML Import/Export ────────────────────────────────────────

    // ── XFA FDF / XFDF Import/Export ──────────────────────────────────────
    //
    // A dynamic XFA form carries no AcroForm widget fields, so the AcroForm
    // FDF/XFDF exporters emit an empty /Fields set. These XFA-aware variants
    // build the data tree from the template (with values from the datasets
    // packet, if any), the same source ExportXmlXfa uses, and render it as
    // FDF (flat /T(leaf[0]) entries) or XFDF (nested xfdf:fields). Import
    // resolves the entries back to XFA data paths and persists them through
    // the same ReplaceXfaDatasets path ImportXml uses.

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

    // ── API-shape additions ───────────────────────────────────────────────

    /// <summary>Rename a form field.</summary>
    public void RenameField(string fieldName, string newFieldName)
    {
        if (_doc?.Form is null) return;
        _doc.Form.FindFieldOrNull(fieldName)?.SetPartialName(newFieldName);
    }

}
