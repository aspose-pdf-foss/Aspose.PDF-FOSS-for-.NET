using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for adding stamps, page numbers, headers, footers, and watermarks.
/// Supports both stateless (byte[]-in / byte[]-out) and stateful (constructor with paths / AddStamp / Close) modes.
/// </summary>
public sealed partial class PdfFileStamp : System.IDisposable
{
    // ── Page-position constants (1..8 layout) ─────────────────────────────────
    public const int PosUpperLeft = 1;
    public const int PosUpperMiddle = 2;
    public const int PosUpperRight = 3;
    public const int PosSidesLeft = 4;
    public const int PosSidesRight = 5;
    public const int PosBottomLeft = 6;
    public const int PosBottomMiddle = 7;
    public const int PosBottomRight = 8;

    private Document? _document;
    private string? _outputPath;
    private byte[]? _inputData;
    private Stream? _inputStream;
    private Stream? _outputStream;

    /// <summary>
    /// Default constructor for stateless mode.
    /// </summary>
    public PdfFileStamp()
    {
    }

    /// <summary>
    /// Bind an already-loaded Document. Save target stays unset until OutputFile/OutputStream is configured.
    /// </summary>
    public PdfFileStamp(Document document)
    {
        _document = document;
    }

    /// <summary>
    /// Bind a Document and pre-configure an output stream for the parameterless Save.
    /// </summary>
    public PdfFileStamp(Document document, Stream outputStream)
    {
        _document = document;
        _outputStream = outputStream;
    }

    /// <summary>
    /// Bind a Document and pre-configure an output file path for the parameterless Save.
    /// </summary>
    public PdfFileStamp(Document document, string outputFile)
    {
        _document = document;
        _outputPath = outputFile;
    }

    /// <summary>
    /// Open from an input stream, writing to an output stream on Save.
    /// </summary>
    public PdfFileStamp(Stream inputStream, Stream outputStream)
        : this(inputStream, outputStream, keepSecurity: false)
    {
    }

    /// <summary>
    /// Open from an input stream, writing to an output stream on Save. The keepSecurity flag is recorded
    /// on <see cref="KeepSecurity"/> for callers to inspect.
    /// </summary>
    public PdfFileStamp(Stream inputStream, Stream outputStream, bool keepSecurity)
    {
        _inputStream = inputStream;
        _outputStream = outputStream;
        KeepSecurity = keepSecurity;
        _inputData = ReadAll(inputStream);
        _document = Document.Open(_inputData);
    }

    /// <summary>
    /// Open from an input file, writing to an output file on Save.
    /// </summary>
    public PdfFileStamp(string inputFile, string outputFile)
        : this(inputFile, outputFile, keepSecurity: false)
    {
    }

    /// <summary>
    /// Open from an input file, writing to an output file on Save. The keepSecurity flag is recorded
    /// on <see cref="KeepSecurity"/> for callers to inspect.
    /// </summary>
    public PdfFileStamp(string inputFile, string outputFile, bool keepSecurity)
    {
        _inputFile = inputFile;
        _outputPath = outputFile;
        KeepSecurity = keepSecurity;
        _inputData = File.ReadAllBytes(inputFile);
        _document = Document.Open(_inputData);
    }

    /// <summary>
    /// Create a PdfFileStamp for given input bytes, saving to the given output file on Close().
    /// </summary>
    public PdfFileStamp(byte[] inputData, string outputPath)
    {
        _inputData = inputData;
        _document = Document.Open(inputData);
        _outputPath = outputPath;
    }

    /// <summary>
    /// Bind a PDF document from a file path for stateful processing.
    /// </summary>
    public void BindPdf(string path)
    {
        _inputData = File.ReadAllBytes(path);
        _document = Document.Open(_inputData);
    }

    /// <summary>
    /// Bind a PDF document from a stream for stateful processing.
    /// </summary>
    public void BindPdf(Stream inputStream)
    {
        if (inputStream.CanSeek) inputStream.Seek(0, SeekOrigin.Begin);
        _inputData = ReadAll(inputStream);
        _document = Document.Open(_inputData);
    }

    /// <summary>
    /// Bind a PDF document for stateful processing.
    /// </summary>
    public void BindPdf(Document doc)
    {
        _document = doc;
    }

    private static byte[] ReadAll(Stream s)
    {
        if (s is MemoryStream ms) return ms.ToArray();
        using var copy = new MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Save the modified document to the specified path.
    /// </summary>
    public void Save(string destFile)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        PruneOrphans();
        _document.Save(destFile);
    }

    // Stamping replaces a page's /Contents with a combined stream (existing + stamp),
    // leaving the original content stream orphaned in the xref. The default save serialises
    // every in-use entry, so the output keeps growing by the old content size on each stamp.
    // This pure reachability prune (RemoveUnusedObjects only) drops the orphans without
    // rewriting or recompressing live content; the shared stamp form stays reachable.
    private void PruneOrphans()
    {
        _document?.OptimizeResources(new Aspose.Pdf.Optimization.OptimizationOptions
        {
            RemoveUnusedObjects = true,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });
    }

    /// <summary>
    /// Save the modified document to a stream.
    /// </summary>
    public void Save(Stream destStream)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        PruneOrphans();
        var data = _document.ToArray();
        destStream.Write(data);
    }

    /// <summary>
    /// Save the modified document to a byte array.
    /// </summary>
    public byte[] Save()
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        ApplyConvertTo();
        PruneOrphans();
        return _document.ToArray();
    }

    /// <summary>Applies the requested <see cref="ConvertTo"/> target to the bound
    /// document. For the plain PDF version formats (v_1_x / v_2_0) this sets the
    /// document version so the saved file carries the requested header.</summary>
    private void ApplyConvertTo()
    {
        if (_document is null || _convertTo is null) return;
        var name = _convertTo.Value.ToString();
        if (name.StartsWith("v_", StringComparison.Ordinal))
            _document.SetVersion(name.Substring(2).Replace('_', '.'));
    }

    /// <summary>The bound document.</summary>
    public Document Document => _document ?? throw new InvalidOperationException("No document bound.");

    /// <summary>
    /// Input file path. Setting binds the PDF lazily so that subsequent
    /// page-dimension queries reflect the input.
    /// </summary>
    public string? InputFile
    {
        get => _inputFile;
        set
        {
            _inputFile = value;
            if (value is not null) BindPdf(value);
        }
    }
    private string? _inputFile;

    /// <summary>
    /// Output file path. Save targets this when the parameterless Save is later wired up.
    /// </summary>
    public string? OutputFile
    {
        get => _outputPath;
        set => _outputPath = value;
    }

    /// <summary>
    /// Width of the first page in the bound document. Defaults to A4 (595)
    /// when no document is bound.
    /// </summary>
    public float PageWidth =>
        _document is null || _document.Pages.Count == 0
            ? 595f
            : (float)_document.Pages[1].MediaBox.Width;

    /// <summary>
    /// Height of the first page in the bound document. Defaults to A4 (842)
    /// when no document is bound.
    /// </summary>
    public float PageHeight =>
        _document is null || _document.Pages.Count == 0
            ? 842f
            : (float)_document.Pages[1].MediaBox.Height;

    /// <summary>
    /// Input stream — setting binds the PDF eagerly so page-dimension queries reflect the new input.
    /// </summary>
    public Stream? InputStream
    {
        get => _inputStream;
        set
        {
            _inputStream = value;
            if (value is not null)
            {
                _inputData = ReadAll(value);
                _document = Document.Open(_inputData);
            }
        }
    }

    /// <summary>
    /// Output stream — Save() writes here when no explicit destination is passed.
    /// </summary>
    public Stream? OutputStream
    {
        get => _outputStream;
        set => _outputStream = value;
    }

    /// <summary>
    /// If true, the source document's security (encryption, permissions) should be preserved on Save.
    /// Recorded for callers to inspect; the current save path does not re-apply source encryption.
    /// </summary>
    public bool KeepSecurity { get; set; }

    /// <summary>
    /// Numbering style for AddPageNumber. Defaults to <see cref="Aspose.Pdf.NumberingStyle.Decimal"/>.
    /// </summary>
    public NumberingStyle NumberingStyle { get; set; } = NumberingStyle.Decimal;

    /// <summary>
    /// Hint: optimize the saved output for size. Stored for callers to inspect; not currently honoured by Save.
    /// </summary>
    public bool OptimizeSize { get; set; }

    /// <summary>
    /// Rotation (degrees) applied to AddPageNumber stamps.
    /// </summary>
    public float PageNumberRotation { get; set; }

    /// <summary>
    /// Stamp identifier embedded as a content-stream comment by AddStamp.
    /// </summary>
    public int StampId { get; set; }

    /// <summary>
    /// Starting number used by AddPageNumber. Defaults to 1.
    /// </summary>
    public int StartingNumber { get; set; } = 1;

    /// <summary>
    /// PDF/A or PDF version target for the saved output. Stored for callers to inspect; the current
    /// Save path emits plain PDF regardless of this value.
    /// </summary>
    public PdfFormat ConvertTo { set => _convertTo = value; }
    private PdfFormat? _convertTo;

    /// <summary>
    /// Close the document and save to the output path.
    /// </summary>
    public void Close()
    {
        if (_document is null) return;
        ApplyConvertTo();
        PruneOrphans();
        if (_outputPath is not null)
        {
            _document.Save(_outputPath);
        }
        else if (_outputStream is not null)
        {
            // Stream-bound stamp (e.g. PdfFileStamp(inputStream, outputStream)):
            // flush the stamped document to the configured output stream, mirroring
            // Save(Stream). Without this the output stream stays empty and any
            // subsequent reader rejects it as a headerless file.
            var data = _document.ToArray();
            _outputStream.Write(data, 0, data.Length);
        }
        _document.Dispose();
        _document = null;
    }

    /// <summary>IDisposable implementation; delegates to <see cref="Close"/>.</summary>
    public void Dispose() => Close();

    // ── Stateful header / footer / page-number API ──────────────────────────

    private static (HorizontalAlignment h, VerticalAlignment v, bool top) ResolvePosition(int position) => position switch
    {
        PosUpperLeft => (HorizontalAlignment.Left, VerticalAlignment.Top, true),
        PosUpperMiddle => (HorizontalAlignment.Center, VerticalAlignment.Top, true),
        PosUpperRight => (HorizontalAlignment.Right, VerticalAlignment.Top, true),
        PosSidesLeft => (HorizontalAlignment.Left, VerticalAlignment.Center, false),
        PosSidesRight => (HorizontalAlignment.Right, VerticalAlignment.Center, false),
        PosBottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom, false),
        PosBottomRight => (HorizontalAlignment.Right, VerticalAlignment.Bottom, false),
        _ => (HorizontalAlignment.Center, VerticalAlignment.Bottom, false), // PosBottomMiddle (default)
    };

    // ── Stateless API (existing) ──────────────────────────────────────────────

}
