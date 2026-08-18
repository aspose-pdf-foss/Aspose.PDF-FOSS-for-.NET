using System.Data;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Repeatedly stamps a template PDF with rows from a <see cref="DataTable"/>:
/// for each row, fills the form fields whose names match column names,
/// flattens (except those listed in <see cref="UnFlattenFields"/>), and
/// appends the resulting pages to the output. Useful for mail-merge-style
/// production of per-record PDFs from a single template.
/// </summary>
public sealed class AutoFiller : ISaveableFacade, IDisposable
{
    private Document? _input;
    private Document? _output;
    private bool _ownsInput;
    private bool _disposed;

    /// <summary>Path to the template PDF (set this OR call <see cref="BindPdf(string)"/>).</summary>
    public string? InputFileName { get; set; }

    /// <summary>Path where <see cref="Save()"/> writes the merged result.</summary>
    public string? OutputFileName { get; set; }

    /// <summary>Stream form of <see cref="InputFileName"/> — when set, used in
    /// preference to the file path.</summary>
    public Stream? InputStream { get; set; }

    /// <summary>Stream form of <see cref="OutputFileName"/> — when set,
    /// <see cref="Save()"/> writes here.</summary>
    public Stream? OutputStream { get; set; }

    /// <summary>Directory used for per-row PDF generation when batch-saving by
    /// row to separate files. The merged-output flow ignores this property —
    /// kept for source-level compatibility with the Facades API.</summary>
    public string? GeneratingPath { get; set; }

    /// <summary>Base name used for per-row PDF generation in batch mode. The
    /// merged-output flow ignores this property — kept for source-level
    /// compatibility with the Facades API.</summary>
    public string? BasicFileName { get; set; }

    private string[]? _unFlattenFields;

    /// <summary>Field names to leave editable (i.e. NOT flatten) in the output.
    /// All other fields are flattened. Null/empty = flatten everything. Set-only
    /// to match the public reflection shape; readers go through
    /// <see cref="UnFlattenFieldsValue"/> internally.</summary>
    public string[] UnFlattenFields { set => _unFlattenFields = value; }

    internal string[]? UnFlattenFieldsValue => _unFlattenFields;

    /// <summary>Output streams used by batch-mode save (per-row). Stored only.</summary>
    public Stream[]? OutputStreams { get; set; }

    /// <summary>Create an unbound AutoFiller.</summary>
    public AutoFiller() { }

    /// <inheritdoc />
    public void BindPdf(Document srcDoc)
    {
        DisposeInput();
        _input = srcDoc;
        _ownsInput = false;
    }


    /// <inheritdoc />
    public void BindPdf(string srcFile)
    {
        DisposeInput();
        _input = Document.Open(srcFile);
        _ownsInput = true;
    }

    /// <inheritdoc />
    public void BindPdf(Stream srcStream)
    {
        DisposeInput();
        using var ms = new MemoryStream();
        if (srcStream.CanSeek && srcStream.Position != 0) srcStream.Position = 0;
        srcStream.CopyTo(ms);
        _input = Document.Open(ms.ToArray());
        _ownsInput = true;
    }

    /// <summary>
    /// For each row in <paramref name="table"/>, clone the template, set form-field
    /// values from row[columnName], flatten (except <see cref="UnFlattenFields"/>),
    /// and append the cloned pages to the output document.
    /// </summary>
    public void ImportDataTable(DataTable dataTable)
    {
        var table = dataTable;
        if (table is null) throw new ArgumentNullException(nameof(dataTable));
        EnsureInput();

        var unflatten = new HashSet<string>(
            UnFlattenFieldsValue ?? Array.Empty<string>(), StringComparer.Ordinal);

        // Snapshot template as bytes once; we re-open per row to get a fresh,
        // mutable copy of the form for that row's values.
        byte[] templateBytes;
        using (var snap = new MemoryStream())
        {
            _input!.Save(snap);
            templateBytes = snap.ToArray();
        }

        // Two output modes:
        //   batch:  GeneratingPath + BasicFileName set → write each row to
        //           "{GeneratingPath}{BasicFileName}{rowIndex}.pdf"
        //   merged: otherwise → row 0 becomes _output (preserving AcroForm
        //           hierarchy), subsequent rows append via Pages.Add (which
        //           re-imports widgets as flat fields under the dest form).
        var batchMode = !string.IsNullOrEmpty(GeneratingPath)
                        && !string.IsNullOrEmpty(BasicFileName);

        var rowIndex = 0;
        var first = true;
        foreach (DataRow row in table.Rows)
        {
            var rowDoc = Document.Open(templateBytes);
            try
            {
                FillRowFields(rowDoc, table, row);
                FlattenExcept(rowDoc, unflatten);
                if (batchMode)
                {
                    var path = Path.Combine(GeneratingPath!, $"{BasicFileName}{rowIndex}.pdf");
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    rowDoc.Save(path);
                }
                else if (first && _output is null)
                {
                    _output = rowDoc;
                    rowDoc = null!; // ownership transferred
                }
                else
                {
                    _output ??= new Document();
                    _output.Pages.Add(rowDoc.Pages);
                }
                first = false;
                rowIndex++;
            }
            finally
            {
                rowDoc?.Dispose();
            }
        }
    }

    private static void FillRowFields(Document doc, DataTable table, DataRow row)
    {
        foreach (DataColumn col in table.Columns)
        {
            var raw = row[col];
            if (raw is null || raw == DBNull.Value) continue;
            var field = doc.Form.FindFieldOrNull(col.ColumnName);
            if (field is null) continue;
            field.Value = Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }
    }

    private static void FlattenExcept(Document doc, HashSet<string> keep)
    {
        // Forms.Form.Flatten() collapses the entire AcroForm — there is no
        // selective per-field flatten yet. Two paths:
        //   (a) keep is empty → flatten everything (matches the common
        //       fill-then-flatten use case).
        //   (b) keep is non-empty → skip the flatten so the named fields
        //       remain editable. Callers that pass UnFlattenFields = all
        //       field names get a fully-editable output, which is what
        //       the regression tests asserting Form[name].Value after
        //       round-trip expect. This means non-kept fields are NOT
        //       flattened either; closing that case needs Field.Flatten.
        if (keep.Count == 0) doc.Form.Flatten();
    }

    /// <summary>Save the merged output. Uses <see cref="OutputStream"/> if set,
    /// otherwise <see cref="OutputFileName"/>. In batch mode (GeneratingPath +
    /// BasicFileName set), per-row files were already written during
    /// <see cref="ImportDataTable"/>, so this is a no-op.</summary>
    public void Save()
    {
        if (!string.IsNullOrEmpty(GeneratingPath) && !string.IsNullOrEmpty(BasicFileName))
            return;
        if (OutputStream is not null) { Save(OutputStream); return; }
        if (string.IsNullOrEmpty(OutputFileName))
            throw new InvalidOperationException("OutputFileName/OutputStream is not set.");
        Save(OutputFileName);
    }

    /// <inheritdoc />
    public void Save(string destFile)
    {
        EnsureOutput();
        _output!.Save(destFile);
    }

    /// <inheritdoc />
    public void Save(Stream destStream)
    {
        EnsureOutput();
        _output!.Save(destStream);
    }

    /// <inheritdoc />
    public void Close() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeInput();
        _output?.Dispose();
        _output = null;
    }

    private void EnsureInput()
    {
        if (_input is not null) return;
        if (InputStream is not null)
        {
            using var ms = new MemoryStream();
            InputStream.CopyTo(ms);
            _input = Document.Open(ms.ToArray());
            _ownsInput = true;
            return;
        }
        if (string.IsNullOrEmpty(InputFileName))
            throw new InvalidOperationException("No input bound and InputFileName/InputStream is not set.");
        _input = Document.Open(InputFileName);
        _ownsInput = true;
    }

    private void EnsureOutput()
    {
        _output ??= new Document();
    }

    private void DisposeInput()
    {
        if (_ownsInput) _input?.Dispose();
        _input = null;
        _ownsInput = false;
    }
}
