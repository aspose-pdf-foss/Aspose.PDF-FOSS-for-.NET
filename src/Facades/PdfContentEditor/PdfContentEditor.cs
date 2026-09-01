using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for content-level editing: text replacement, link creation, image operations.
/// Supports both stateless (byte[]-in / byte[]-out) and stateful (BindPdf / Save) modes.
/// </summary>
public sealed partial class PdfContentEditor : System.IDisposable
{
    // ── Document additional-action event-type constants ──
    // Values are the documented event names that
    // AddDocumentAdditionalAction accepts. They map to keys of the /AA dict in
    // the document catalog.
    public const string DocumentOpen       = "DO";
    public const string DocumentClose      = "WC";
    public const string DocumentWillSave   = "WS";
    public const string DocumentSaved      = "DS";
    public const string DocumentWillPrint  = "WP";
    public const string DocumentPrinted    = "DP";

    private Document? _document;
    private byte[]? _boundData;
    private bool _ownsDocument;

    /// <summary>
    /// The PDF document currently bound for editing.
    /// </summary>
    public Document Document => _document
        ?? throw new InvalidOperationException("No document bound. Call BindPdf first.");

    /// <summary>Default ctor — bind a PDF later via <see cref="BindPdf(string)"/>.</summary>
    public PdfContentEditor() { }

    /// <summary>Bind to an existing <see cref="Document"/> at construction.</summary>
    public PdfContentEditor(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _ownsDocument = false;
    }

    private TextReplaceOptions _textReplaceOptions = new TextReplaceOptions();
    private bool _textReplaceOptionsAssigned;

    /// <summary>Text-replacement options used by <see cref="ReplaceText(string,string)"/> family.</summary>
    public TextReplaceOptions TextReplaceOptions
    {
        get => _textReplaceOptions;
        set { _textReplaceOptions = value; _textReplaceOptionsAssigned = true; }
    }

    /// <summary>Text-edit options forwarded to the underlying replacement engine.</summary>
    public TextEditOptions TextEditOptions { get; set; } = new TextEditOptions(true);

    /// <summary>Text-search options used by the stateful text-replacement variants.</summary>
    public TextSearchOptions TextSearchOptions { get; set; } = new TextSearchOptions();

    // ── Stateful (BindPdf / Save) API ─────────────────────────────────────────

    /// <summary>
    /// Bind a PDF file for editing.
    /// </summary>
    public void BindPdf(byte[] pdfData)
    {
        _boundData = pdfData;
        _document = Document.Open(pdfData);
        _ownsDocument = true;
    }

    /// <summary>
    /// Bind a PDF file by path for editing.
    /// </summary>
    public void BindPdf(string inputFile)
    {
        _boundData = File.ReadAllBytes(inputFile);
        _document = Document.Open(_boundData);
        _ownsDocument = true;
    }

    /// <summary>
    /// Bind a PDF stream for editing.
    /// </summary>
    public void BindPdf(Stream inputStream)
    {
        using var ms = new MemoryStream();
        if (inputStream.CanSeek && inputStream.Position != 0) inputStream.Position = 0;
        inputStream.CopyTo(ms);
        _boundData = ms.ToArray();
        _document = Document.Open(_boundData);
        _ownsDocument = true;
    }

    /// <summary>
    /// Bind an in-memory PDF document for editing. The caller retains ownership of the document.
    /// </summary>
    public void BindPdf(Document srcDoc)
    {
        _document = srcDoc ?? throw new ArgumentNullException(nameof(srcDoc));
        _boundData = null;
        _ownsDocument = false;
    }

    /// <summary>
    /// Save the bound document.
    /// </summary>
    public byte[] Save()
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        // Editing operations (DeleteStamp/MoveStamp) replace a page's /Contents stream and
        // drop XObjects from /Resources, orphaning the previous content stream and the
        // removed image. Those orphans are still reachable in the source xref table, so
        // without a prune they are re-serialised and the file grows on every edit cycle.
        // Eliminate unreferenced objects (reachability sweep from the trailer) before
        // writing so the saved file shrinks after a stamp is removed.
        _document.OptimizeResources(new Aspose.Pdf.Optimization.OptimizationOptions
        {
            RemoveUnusedObjects = true,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });
        var result = _document.ToArray();
        // Re-open so further operations work on the saved state.
        // Skip re-open when caller owns the document — they hold the reference.
        if (_ownsDocument)
        {
            _boundData = result;
            _document.Dispose();
            _document = Document.Open(result);
        }
        return result;
    }

    /// <summary>
    /// Save the bound document to a file path.
    /// </summary>
    public void Save(string path)
    {
        var bytes = Save();
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// Save the bound document to a stream.
    /// </summary>
    public void Save(Stream stream)
    {
        var bytes = Save();
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Close the bound document.
    /// </summary>
    public void Close()
    {
        if (_ownsDocument)
            _document?.Dispose();
        _document = null;
        _boundData = null;
        _ownsDocument = false;
    }

    /// <summary>Releases the bound document (equivalent to <see cref="Close"/>).</summary>
    public void Dispose() => Close();

    // ── ViewerPreference (stateful) ─────────────────────────────────────────

    // ── Stamp operations (stateful) ───────────────────────────────────────────

    // ── Stamp parsing internals ───────────────────────────────────────────────

    /// <summary>Map a content-space point into the page's displayed coordinate system for a /Rotate value.</summary>
    private static (double x, double y) RotatePoint(double x, double y, int rotate, double w, double h)
        => (((rotate % 360) + 360) % 360) switch
        {
            90 => (y, w - x),
            180 => (w - x, h - y),
            270 => (h - y, x),
            _ => (x, y),
        };

    // IsStampBlock replaced by IsStampBlockContent — see ParseStamps and DeleteStamp.

    // ── Stateful ReplaceText ───────────────────────────────────────────────────

    /// <summary>
    /// Strategy controlling how <see cref="ReplaceText(string, string)"/> matches
    /// (literal vs. regex) and how many matches it substitutes per call.
    /// </summary>
    private ReplaceTextStrategy? _replaceTextStrategy;

    public ReplaceTextStrategy ReplaceTextStrategy
    {
        // Bound to this editor so its settings are live views over
        // TextSearchOptions / TextEditOptions / TextReplaceOptions (legacy API compatibility).
        get => _replaceTextStrategy ??= new ReplaceTextStrategy { Owner = this };
        set { _replaceTextStrategy = value; value?.BindTo(this); }
    }

    // ── Stateless API (existing) ──────────────────────────────────────────────

    // ──────────────────────────────────────────────────────────────────────────
    // Annotation, stamp, attachment, and document-action helpers. Most write a
    // single annotation dict into the page /Annots array or update a catalog-
    // level dict. Complex per-feature operations (rich appearance streams,
    // media playback) are not implemented; structural correctness — annotation
    // present with the right Subtype and Rect — is what's guaranteed here.
    // ──────────────────────────────────────────────────────────────────────────

    // ── Link annotations (stateful, void-return, System.Drawing.Rectangle) ──

    // ── Markup / shape annotations ──

    // ── Media annotations ──

    // ── File attachments ──

    // ── Bookmarks / outlines ──

    // ── Document-level actions / attachments ──

    // ── Image operations ──

    // ── Stamp move/hide (extend the existing GetStamps/DeleteStamp impl) ──

    /// <summary>
    /// Invert <see cref="RotatePoint"/> for a stamp of size (w,h): given the desired
    /// displayed lower-left corner, return the content-space translation (e,f) the cm needs.
    /// </summary>
    private static (double e, double f) DisplayedLowerLeftToContent(
        double x, double y, double w, double h, int rotate, double pageW, double pageH)
        => (((rotate % 360) + 360) % 360) switch
        {
            90 => (pageW - w - y, x),
            180 => (pageW - w - x, pageH - h - y),
            270 => (y, pageH - h - x),
            _ => (x, y),
        };

    // ── Annotation builders ──

}
