using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

/// <summary>
/// Represents a PDF document. This is the main entry point for the library.
/// </summary>
public sealed partial class Document : IDisposable
{
    private byte[] _data;

    private readonly PdfReader _reader;

    private PageCollection? _pages;

    private DocumentInfo? _info;

    private Form? _form;

    private OutlineCollection? _outlines;

    private XmpMetadata? _metadata;

    private bool _metadataChecked;

    private TaggedContent? _taggedContent;

    private StructureTreeBuilder? _structureTreeBuilder;

    private OutlineBuilder? _outlineBuilder;

    private PageLabelBuilder? _pageLabelBuilder;

    private string? _versionOverride;

    private FontUtilities? _fontUtilities;

#pragma warning disable CS0414 // Field is assigned but never read
    private bool _linearize;

    private bool _isNewDocument;

#pragma warning restore CS0414

    // File name of the in-progress Save(string) — read by save-time appearance generation
    // (PageInformationAnnotation prints it). Null during Save(Stream) (no file name known).
    private string? _pendingSaveFileName;

    private Stream? _sourceStream;

    /// <summary>Signed PDF bytes produced by a DOM-level
    /// <see cref="Forms.SignatureField.Sign(Forms.Signature)"/> since this document
    /// was opened. Each field-level Sign chains onto the previous value (interleaved
    /// Form.Add + Sign flows), and a subsequent no-arg <see cref="Save()"/> persists
    /// these bytes verbatim — re-serializing the in-memory model would shift offsets
    /// and break every signature's /ByteRange.</summary>
    internal byte[]? PendingSignedBytes;

    /// <summary>Permissions recovered from a public-key (certificate) security
    /// handler envelope, when the document was opened that way.</summary>
    private int? _pubSecPermissions;

    /// <summary>
    /// Create a new empty PDF document.
    /// </summary>
    public static Document Create()
    {
        var data = CreateEmptyPdf();
        var doc = new Document(data);
        doc._isNewDocument = true;
        return doc;
    }

    /// <summary>
    /// Document-level JavaScript (PDF spec §12.6.4.16 — /Names/JavaScript
    /// name tree in the catalog). Each entry maps a script name to a
    /// /S /JavaScript action containing a /JS source string.
    /// </summary>
    public JavaScriptCollection JavaScript => new(this);

    /// <summary>
    /// The collection of pages in this document.
    /// </summary>
    public PageCollection Pages
    {
        get
        {
            if (_pages is null)
            {
                _pages = new PageCollection(_reader);
                _pages.OwnerDocument = this;
            }
            return _pages;
        }
    }

    /// <summary>
    /// The document information dictionary (title, author, etc.).
    /// </summary>
    public DocumentInfo Info
    {
        get
        {
            if (_info is null)
            {
                // ResolveExistingInfoDict also reaches a pending /Info dict another
                // DocumentInfo instance created before save.
                _info = new DocumentInfo(ResolveExistingInfoDict(), _reader, this);
            }
            return _info;
        }
    }

    /// <summary>The file name/path this document was loaded from, or null if loaded from a stream.
    /// Set internally by the path-based constructor and by <c>Save(string)</c>.</summary>
    public string? FileName { get; internal set; }

    /// <summary>
    /// The PDF version from the file header (e.g., "1.4", "1.7", "2.0").
    /// </summary>
    public string? PdfVersion
    {
        get
        {
            // Check catalog /Version first (overrides header)
            var catalogVersion = _reader.Catalog.GetName("Version");
            if (catalogVersion is not null) return catalogVersion;

            // Fall back to file header: %PDF-X.Y
            if (_data.Length >= 8 && _data[0] == '%' && _data[1] == 'P' &&
                _data[2] == 'D' && _data[3] == 'F' && _data[4] == '-')
            {
                var end = 5;
                while (end < _data.Length && end < 12 && _data[end] != '\r' && _data[end] != '\n')
                    end++;
                return System.Text.Encoding.ASCII.GetString(_data, 5, end - 5);
            }

            return null;
        }
    }

    /// <summary>
    /// Shortcut for <c>Pages.Count</c>.
    /// Falls back to the Pages tree /Count when some page objects can't be resolved
    /// (e.g., incomplete xref tables in corrupt/truncated PDFs).
    /// </summary>
    public int PageCount => Pages.Count;

    /// <summary>Alias for <see cref="PdfVersion"/>.</summary>
    public string? Version => PdfVersion;

    /// <summary>
    /// True if the document declares itself PDF/A compliant. In order of authority:
    /// 1) the last successful in-memory Convert() target, 2) the XMP /Metadata pdfaid tag.
    /// </summary>
    public bool IsPdfaCompliant
    {
        get
        {
            if (_lastConvertedFormat is { } f && IsPdfaFormat(f)) return true;
            return DetectPdfFormatFromXmp() is { } detected && IsPdfaFormat(detected);
        }
    }

    /// <summary>
    /// Returns the PDF format declared by this document. Prefers the last Convert() target
    /// (set in-memory), falls back to XMP metadata parsing, then to v_1_7 default.
    /// </summary>
    public Aspose.Pdf.PdfFormat PdfFormat
    {
        get
        {
            if (_lastConvertedFormat is { } f) return f;
            if (DetectPdfFormatFromXmp() is { } detected) return detected;
            // No PDF/A metadata: map the plain header/catalog version string.
            return PdfVersion switch
            {
                "1.0" => Aspose.Pdf.PdfFormat.v_1_0,
                "1.1" => Aspose.Pdf.PdfFormat.v_1_1,
                "1.2" => Aspose.Pdf.PdfFormat.v_1_2,
                "1.3" => Aspose.Pdf.PdfFormat.v_1_3,
                "1.4" => Aspose.Pdf.PdfFormat.v_1_4,
                "1.5" => Aspose.Pdf.PdfFormat.v_1_5,
                "1.6" => Aspose.Pdf.PdfFormat.v_1_6,
                "2.0" => Aspose.Pdf.PdfFormat.v_2_0,
                _ => Aspose.Pdf.PdfFormat.v_1_7,
            };
        }
    }

    /// <summary>
    /// Returns true if this PDF file is linearized (optimized for fast web viewing).
    /// Linearized PDFs have a linearization dictionary as the first object in the file body.
    /// </summary>
    /// <summary>
    /// Backing override for <see cref="IsLinearized"/>. Null means "inherit the source
    /// file's state"; a non-null value is an explicit request (<c>IsLinearized = false</c>
    /// to de-linearize on save, <c>true</c> to linearize). <see cref="LinearizeDocument"/>
    /// also requests linearization.
    /// </summary>
    private bool? _isLinearized;

    public bool IsLinearized
    {
        get => _isLinearized ?? _reader.IsLinearized;
        set => _isLinearized = value;
    }

    /// <summary>When true (default), the writer scrubs invalid signature
    /// references during save. Stored only; the writer behaves as if always false.</summary>
    public bool EnableSignatureSanitization { get; set; } = true;

    /// <summary>
    /// Options describing what repair is needed.
    /// </summary>
    public sealed class RepairOptions
    {
        /// <summary>Whether the document has validation issues.</summary>
        public bool HasValidationIssues { get; internal set; }

        /// <summary>Number of validation issues found.</summary>
        public int IssueCount { get; internal set; }

        /// <summary>Whether the xref table has integrity issues.</summary>
        public bool HasXRefIssues { get; internal set; }

        /// <summary>Whether the repair pass should restore object-generation numbers from the original xref. Stored only.</summary>
        public bool RestoreIndirectObjectGenerations { get; set; }
    }

    /// <summary>
    /// The interactive form (AcroForm). Returns an empty form with Count=0 if none exists.
    /// </summary>
    public Form Form
    {
        get
        {
            if (_form is not null) return _form;
            var acroForm = _reader.ResolveDict(_reader.Catalog.Get("AcroForm"));
            if (acroForm is not null)
            {
                _form = new Form(acroForm, _reader) { OwnerDocument = this };
                // Fallback: AcroForm exists but has no /Fields — scan page widgets instead
                // But keep the original Form if it's XFA (XFA forms often have no standard fields)
                if (_form.Count == 0 && !_form.IsXfa)
                {
                    // Only swap when the scan actually finds widgets: the AcroForm-bound
                    // wrapper carries /DR (DefaultResources), which a flattened document
                    // with an emptied /Fields must keep exposing.
                    var scanned = Form.FromPageWidgets(_reader.Catalog, _reader);
                    if (scanned.Count > 0) _form = scanned;
                }
            }
            else
            {
                // No AcroForm at all — scan page Widget annotations
                _form = Form.FromPageWidgets(_reader.Catalog, _reader);
            }
            // Never expose the shared Form.Empty singleton: Form.Add mutates the
            // instance's _fields list, which would bleed into any other document
            // that subsequently received Empty (classic singleton-mutation bug).
            // Wire a fresh empty Form to this document instead.
            if (ReferenceEquals(_form, Form.Empty))
                _form = Form.CreateEmptyForDocument(_reader);
            _form.OwnerDocument = this;
            return _form;
        }
    }

    /// <summary>Whether the face licence gate is lifted for this document: with it set,
    /// a face whose OS/2 fsType forbids embedding is embedded anyway and no
    /// <see cref="FontEmbeddingException"/> is raised. The caller has taken the licence
    /// question on itself.</summary>
    public bool DisableFontLicenseVerifications { get; set; }

    /// <summary>Font utilities for subsetting and font management.</summary>
    public IDocumentFontUtilities FontUtilities => _fontUtilities ??= new FontUtilities(this);

    /// <summary>Per-document font helper contract — implemented by
    /// <see cref="Aspose.Pdf.FontUtilities"/>. Real interface; the
    /// concrete impl performs actual font enumeration and embedded-font
    /// subsetting on save.</summary>
    public interface IDocumentFontUtilities
    {
        /// <summary>Return every font referenced by any page in this document.</summary>
        Text.Font[] GetAllFonts();

        /// <summary>Apply <paramref name="subsetStrategy"/> to the document's
        /// embedded fonts (removing unused glyphs).</summary>
        void SubsetFonts(FontSubsetStrategy subsetStrategy);
    }

    /// <summary>
    /// The document outline (bookmarks), or null if none exists.
    /// </summary>
    public OutlineCollection Outlines
    {
        get
        {
            if (_outlines is not null) return _outlines;
            var outlinesDict = _reader.ResolveDict(_reader.Catalog.Get("Outlines"));
            if (outlinesDict is null)
            {
                // Lazily create an empty /Outlines tree
                // behavior — Document.Outlines is never null and auto-initializes).
                outlinesDict = new PdfDictionary();
                outlinesDict.Set("Type", new PdfName("Outlines"));
                _reader.Catalog.Set("Outlines", outlinesDict);
            }
            _outlines = new OutlineCollection(outlinesDict, _reader);
            return _outlines;
        }
    }

    /// <summary>
    /// PDF Portfolio (collection) wrapper — returns the catalog's
    /// /Collection dictionary as a <see cref="Aspose.Pdf.Collection"/>, or
    /// null if the document is not a portfolio. Assigning a new
    /// <see cref="Aspose.Pdf.Collection"/> writes a /Collection entry into
    /// the catalog so that subsequent saves persist the portfolio.
    /// </summary>
    public Collection? Collection
    {
        get
        {
            if (_collection is not null) return _collection;
            var coll = _reader.ResolveDict(_reader.Catalog.Get("Collection"));
            if (coll is null) return null;
            var names = _reader.ResolveDict(_reader.Catalog.Get("Names"));
            _collection = new Collection(coll, names, _reader)
                { OwnerDocument = this };
            return _collection;
        }
        set
        {
            _collection = value;
            if (value is null)
            {
                _reader.Catalog.Remove("Collection");
                return;
            }
            value.OwnerDocument = this;
            _reader.Catalog.Set("Collection", value.Dict);
        }
    }

    private Collection? _collection;

    /// <summary>
    /// XMP metadata for the document. Returns a live <see cref="Aspose.Pdf.Metadata"/>
    /// adapter over the catalog /Metadata stream (created empty if absent).
    /// </summary>
    public Metadata Metadata => new(GetOrCreateXmpMetadata());

    /// <summary>True when the password supplied at Open() matched the
    /// owner /O entry (full permissions). False when it matched the user
    /// /U entry, when no password was needed, or when the document is
    /// not encrypted.</summary>
    internal bool IsOwnerAuthentication => _reader.IsOwnerAuthentication;

    /// <summary>True when the supplied password matched BOTH /U and /O —
    /// the file was encrypted with the same password for both, so there is
    /// no effective owner password distinct from the user password.</summary>
    internal bool OwnerPasswordEqualsUserPassword => _reader.OwnerPasswordEqualsUserPassword;

    /// <summary>
    /// Encryption details, or null if not encrypted.
    /// </summary>
    public EncryptionInfo? EncryptionInfo
    {
        get
        {
            if (!IsEncrypted) return null;
            var encrypt = _reader.ResolveDict(_reader.Trailer.Get("Encrypt"));
            if (encrypt is null) return null;

            var v = (int)encrypt.GetInt("V");
            var r = (int)encrypt.GetInt("R");
            var length = (int)encrypt.GetInt("Length", 40);

            var algo = (v, r) switch
            {
                (1, _) => Aspose.Pdf.CryptoAlgorithm.RC4x40,
                (2, _) => Aspose.Pdf.CryptoAlgorithm.RC4x128,
                (4, 4) => Aspose.Pdf.CryptoAlgorithm.AESx128,
                (5, 5) or (5, 6) => Aspose.Pdf.CryptoAlgorithm.AESx256,
                _ => Aspose.Pdf.CryptoAlgorithm.RC4x128,
            };

            return new EncryptionInfo
            {
                Algorithm = algo,
                KeyLength = length,
                Version = v,
                Revision = r,
            };
        }
    }

    /// <summary>
    /// Raw /P permissions bitmask from the encryption dictionary
    /// (PDF 32000-1:2008 Table 22). Returns -1 (all bits set) when the
    /// document is not encrypted. Wrap with <see cref="DocumentPrivilege"/>
    /// for typed Allow* flag access.
    /// </summary>
    public int Permissions
    {
        get
        {
            if (!IsEncrypted) return -1;
            // Public-key handler stores permissions in the recipient envelope, not /P.
            if (_pubSecPermissions is int pubSecPerms) return pubSecPerms;
            var encrypt = _reader.ResolveDict(_reader.Trailer.Get("Encrypt"));
            return (int)(encrypt?.GetInt("P") ?? -1);
        }
    }

    /// <summary>True when PDF/A·X·UA validation/conversion is blocked by the
    /// document's encryption permissions: it was opened with USER access (not
    /// the owner password, which has full rights) and /P withholds the
    /// modify-contents permission (PDF 32000-1 Table 22 bit 4 = value 1&lt;&lt;3).
    /// Conformance work rewrites the document, so it is refused up front.</summary>
    internal bool PdfAConversionBlockedByPermissions
    {
        get
        {
            // A pending re-encryption (Encrypt/SetPrivilege queued this session)
            // supersedes the loaded file's /P: the caller is rewriting the
            // security handler itself, so the old restrictions cannot block.
            if (_encryptor is not null) return false;
            if (!IsEncrypted || _reader.Decryptor is null) return false;
            if (_reader.Decryptor.IsOwnerAuthentication) return false;
            const int modifyContentsFlag = 1 << 3;
            return (Permissions & modifyContentsFlag) == 0;
        }
    }

    /// <summary>
    /// The document open action (action or destination executed when opening the PDF), or null.
    /// Setting a <see cref="PdfAction"/> writes the action dictionary to the catalog's
    /// /OpenAction entry; setting an <see cref="Annotations.ExplicitDestination"/> writes the
    /// destination array. Setting null removes the entry.
    /// </summary>
    public IAppointment? OpenAction
    {
        get
        {
            var actionDict = _reader.ResolveDict(_reader.Catalog.Get("OpenAction"));
            return actionDict is not null ? PdfAction.Create(actionDict, _reader) : null;
        }
        set
        {
            switch (value)
            {
                case null:
                    _reader.Catalog.Remove("OpenAction");
                    break;
                case PdfAction action:
                    _reader.Catalog.Set("OpenAction", action.Dict);
                    break;
                case Annotations.ExplicitDestination dest:
                    _reader.Catalog.Set("OpenAction", dest.ToPdfArrayPublic());
                    break;
            }
        }
    }

    /// <summary>
    /// Default page dimensions/margins applied to pages added after this is set.
    /// </summary>
    public PageInfo PageInfo { get; set; } = new PageInfo();

    /// <summary>
    /// Document-scoped alias for <see cref="Aspose.Pdf.Optimization.OptimizationOptions"/>.
    /// Mirrors the the public API nested-type form
    /// <c>Document.OptimizationOptions</c> so test code that constructs
    /// <c>new Document.OptimizationOptions()</c> resolves the same type.
    /// </summary>
    public class OptimizationOptions : Aspose.Pdf.Optimization.OptimizationOptions
    {
        /// <summary>Factory: every optimization toggle enabled. Stored only.</summary>
        public static new OptimizationOptions All() => new();
    }

    /// <summary>
    /// Embedded files collection. Always returns a collection (creates an empty one if needed)
    /// so callers can use <c>EmbeddedFiles.Add()</c> without null checks.
    /// </summary>
    public EmbeddedFileCollection EmbeddedFiles
    {
        get
        {
            if (_embeddedFiles is not null) return _embeddedFiles;
            var names = _reader.ResolveDict(_reader.Catalog.Get("Names"));
            _embeddedFiles = new EmbeddedFileCollection(names, _reader);
            _embeddedFiles.OwnerDocument = this;
            return _embeddedFiles;
        }
    }

    private EmbeddedFileCollection? _embeddedFiles;

    /// <summary>Format a <see cref="DateTime"/> as a PDF date string
    /// (<c>D:yyyyMMddHHmmssZ</c>, UTC) per PDF 32000-1 §7.9.4.</summary>
    private static string FormatPdfDate(DateTime value)
        => "D:" + value.ToUniversalTime().ToString("yyyyMMddHHmmss",
               System.Globalization.CultureInfo.InvariantCulture) + "Z";

    /// <summary>Whether the document has embedded files.</summary>
    public bool HasEmbeddedFiles
    {
        get
        {
            var names = _reader.ResolveDict(_reader.Catalog.Get("Names"));
            return names is not null && names.ContainsKey("EmbeddedFiles");
        }
    }

    /// <summary>
    /// Page labels, or null.
    /// </summary>
    public PageLabelCollection PageLabels
    {
        get
        {
            if (_pageLabels is null)
            {
                var tree = _reader.ResolveDict(_reader.Catalog.Get("PageLabels"));
                _pageLabels = tree is not null
                    ? new PageLabelCollection(tree, _reader)
                    : new PageLabelCollection();
            }
            return _pageLabels;
        }
    }

    private PageLabelCollection? _pageLabels;

    private OutputIntents? _outputIntents;

    /// <summary>Output intents declared in the document catalog. Mutations
    /// to the collection (<see cref="OutputIntents.Add"/>, etc.) write
    /// back to the /OutputIntents catalog array so they survive Save.</summary>
    public OutputIntents OutputIntents => _outputIntents ??= new OutputIntents(this);

    /// <summary>
    /// Provides destination lookup methods (e.g., GetPageNumber by destination name).
    /// </summary>
    public DestinationCollection Destinations =>
        new DestinationCollection(_reader.Catalog, _reader);

    /// <summary>Named destinations in the document. Always non-null — when the
    /// catalog has no /Dests dict and no /Names tree, an empty mutable
    /// collection is returned so a freshly-constructed Document can call
    /// <c>doc.NamedDestinations.Add(...)</c> without an NRE and have the entry
    /// persist on Save (the underlying /Dests dict is materialised on first
    /// write through the collection's indexer/Add).</summary>
    public NamedDestinationCollection NamedDestinations =>
        new NamedDestinationCollection(_reader.Catalog, _reader);

    /// <summary>
    /// Remove a named destination from the document's /Names → /Dests name tree.
    /// </summary>
    /// <param name="name">The destination name to remove.</param>
    /// <returns><c>true</c> if the destination was found and removed; otherwise <c>false</c>.</returns>
    public bool RemoveNamedDestination(string name)
    {
        var namesDict = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        if (namesDict is null) return false;

        var destsTree = _reader.ResolveDict(namesDict.Get("Dests"));
        if (destsTree is null) return false;

        return RemoveFromNameTree(destsTree, name);
    }

    /// <summary>Optional content properties (layers), or null if none.</summary>
    public OptionalContentProperties? OptionalContent
    {
        get
        {
            var ocProps = _reader.ResolveDict(_reader.Catalog.Get("OCProperties"));
            return ocProps is not null ? new OptionalContentProperties(ocProps, _reader) : null;
        }
    }

    /// <summary>
    /// Import a single page from another document into this document.
    /// </summary>
    /// <param name="source">The source document to import from.</param>
    /// <param name="pageNumber">1-based page number to import.</param>
    /// <param name="insertAt">1-based position to insert at, or -1 to append at end.</param>
    public void ImportPage(Document source, int pageNumber, int insertAt = -1)
    {
        ImportPages(source, [pageNumber], insertAt);
    }

    /// <summary>
    /// Import a dictionary and its full object graph from another document into this
    /// one, allocating fresh object numbers for every referenced indirect object so
    /// the result is self-contained and valid in this document. Shared objects are
    /// imported once per <paramref name="cloneMap"/> (key = source object number).
    /// </summary>
    internal PdfDictionary ImportDict(PdfDictionary source, PdfReader sourceReader,
        Dictionary<int, int> cloneMap)
        => DeepCloneDict(source, sourceReader, cloneMap);

    // Per-source-reader clone maps so repeated imports from the SAME source document into
    // this one share already-imported objects (a font/image referenced by several source
    // pages is cloned once, not once per import). Keyed by source reader identity.
    private Dictionary<PdfReader, Dictionary<int, int>>? _importCloneMaps;

    // The synthetic /S Document container MergeLogicalStructure creates for an
    // untagged destination — distinguishes it from a destination's own pre-existing
    // Document element across the per-source merge calls of one concatenate.
    private PdfDictionary? _syntheticMergedStructDoc;

    /// <summary>
    /// Viewer preferences that control how the document is displayed.
    /// </summary>
    public ViewerPreferences? ViewerPreferences
    {
        get
        {
            var dict = _reader.ResolveDict(_reader.Catalog.Get("ViewerPreferences"));
            return dict is not null ? new ViewerPreferences(dict, _reader.Catalog) : null;
        }
    }

    /// <summary>/CenterWindow viewer preference: position document window
    /// in the centre of the screen.</summary>
    public bool CenterWindow
    {
        get => GetViewerPrefBool("CenterWindow");
        set => SetViewerPrefBool("CenterWindow", value);
    }

    /// <summary>/FitWindow viewer preference: resize the window to fit the
    /// first displayed page.</summary>
    public bool FitWindow
    {
        get => GetViewerPrefBool("FitWindow");
        set => SetViewerPrefBool("FitWindow", value);
    }

    /// <summary>/DisplayDocTitle viewer preference: show the document
    /// title in the window's title bar instead of the file name.</summary>
    public bool DisplayDocTitle
    {
        get => GetViewerPrefBool("DisplayDocTitle");
        set => SetViewerPrefBool("DisplayDocTitle", value);
    }

    /// <summary>Raw /PageLayout name read from the catalog (e.g., "SinglePage", "TwoColumnLeft").
    /// The typed <see cref="PageLayout"/> property is preferred — this string view exists for legacy callers.</summary>
    public string? PageLayoutName
    {
        get => _reader.Catalog.GetName("PageLayout");
        set
        {
            if (value is null)
                _reader.Catalog.Remove("PageLayout");
            else
                _reader.Catalog.Set("PageLayout", new PdfName(value));
        }
    }

    /// <summary>Raw /PageMode name read from the catalog (e.g., "UseNone", "UseOutlines").
    /// The typed <see cref="PageMode"/> property is preferred — this string view exists for legacy callers.</summary>
    public string? PageModeName
    {
        get => _reader.Catalog.GetName("PageMode");
        set
        {
            if (value is null)
                _reader.Catalog.Remove("PageMode");
            else
                _reader.Catalog.Set("PageMode", new PdfName(value));
        }
    }

    /// <summary>The /PageLayout entry as a typed enum.</summary>
    public PageLayout PageLayout
    {
        get => PageLayoutName switch
        {
            "SinglePage" => Aspose.Pdf.PageLayout.SinglePage,
            "OneColumn" => Aspose.Pdf.PageLayout.OneColumn,
            "TwoColumnLeft" => Aspose.Pdf.PageLayout.TwoColumnLeft,
            "TwoColumnRight" => Aspose.Pdf.PageLayout.TwoColumnRight,
            "TwoPageLeft" => Aspose.Pdf.PageLayout.TwoPageLeft,
            "TwoPageRight" => Aspose.Pdf.PageLayout.TwoPageRight,
            _ => Aspose.Pdf.PageLayout.Default,
        };
        set => PageLayoutName = value == Aspose.Pdf.PageLayout.Default ? null : value.ToString();
    }

    /// <summary>The /PageMode entry as a typed enum.</summary>
    public PageMode PageMode
    {
        get => PageModeName switch
        {
            "UseOutlines" => Aspose.Pdf.PageMode.UseOutlines,
            "UseThumbs" => Aspose.Pdf.PageMode.UseThumbs,
            "FullScreen" => Aspose.Pdf.PageMode.FullScreen,
            "UseOC" => Aspose.Pdf.PageMode.UseOC,
            "UseAttachments" => Aspose.Pdf.PageMode.UseAttachments,
            _ => Aspose.Pdf.PageMode.UseNone,
        };
        set => PageModeName = value == Aspose.Pdf.PageMode.UseNone ? null : value.ToString();
    }

    /// <summary>The /NonFullScreenPageMode viewer preference (which page mode to revert to when leaving full-screen).</summary>
    public PageMode NonFullScreenPageMode
    {
        get => ReadViewerPrefName("NonFullScreenPageMode") switch
        {
            "UseOutlines" => Aspose.Pdf.PageMode.UseOutlines,
            "UseThumbs" => Aspose.Pdf.PageMode.UseThumbs,
            "FullScreen" => Aspose.Pdf.PageMode.FullScreen,
            "UseOC" => Aspose.Pdf.PageMode.UseOC,
            "UseAttachments" => Aspose.Pdf.PageMode.UseAttachments,
            _ => Aspose.Pdf.PageMode.UseNone,
        };
        set => WriteViewerPrefName("NonFullScreenPageMode", value == Aspose.Pdf.PageMode.UseNone ? null : value.ToString());
    }

    /// <summary>The /PrintScaling viewer preference.</summary>
    public PrintScaling PrintScaling
    {
        get => ReadViewerPrefName("PrintScaling") switch
        {
            "None" => Aspose.Pdf.PrintScaling.None,
            _ => Aspose.Pdf.PrintScaling.AppDefault,
        };
        set => WriteViewerPrefName("PrintScaling", value == Aspose.Pdf.PrintScaling.AppDefault ? null : value.ToString());
    }

    /// <summary>The /Duplex viewer preference (default Simplex when unset).</summary>
    public PrintDuplex Duplex
    {
        get => ReadViewerPrefName("Duplex") switch
        {
            "DuplexFlipShortEdge" => PrintDuplex.DuplexFlipShortEdge,
            "DuplexFlipLongEdge" => PrintDuplex.DuplexFlipLongEdge,
            _ => PrintDuplex.Simplex,
        };
        set => WriteViewerPrefName("Duplex", value == PrintDuplex.Simplex ? null : value.ToString());
    }

    /// <summary>The /Direction viewer preference (text-flow direction).</summary>
    public Direction Direction
    {
        get => ReadViewerPrefName("Direction") == "R2L" ? Direction.R2L : Direction.L2R;
        set => WriteViewerPrefName("Direction", value == Direction.L2R ? null : "R2L");
    }

    /// <summary>The encryption algorithm in use, or null when the document is not encrypted.</summary>
    public CryptoAlgorithm? CryptoAlgorithm => EncryptionInfo?.Algorithm;

    /// <summary>The /ID array from the trailer, or null when no /ID entry is present.</summary>
    public Id? Id
    {
        get
        {
            if (_reader.Trailer.Get("ID") is not PdfArray ids || ids.Count == 0)
                return null;
            string a = ids[0] is PdfString s0 ? PdfStringToHex(s0) : string.Empty;
            string b = ids.Count > 1 && ids[1] is PdfString s1 ? PdfStringToHex(s1) : a;
            return new Id(a, b);
        }
    }

    /// <summary>True when the document's XMP metadata carries a
    /// <c>pdfuaid:part</c> entry (PDF/UA-1 identifier). Read-only —
    /// determined by reading the XMP packet, not a stored flag.</summary>
    public bool IsPdfUaCompliant
    {
        get
        {
            if (!HasMetadata) return false;
            var part = Metadata.Get("pdfuaid:part");
            return !string.IsNullOrEmpty(part);
        }
    }

    private string? ReadViewerPrefName(string key)
    {
        var dict = _reader.ResolveDict(_reader.Catalog.Get("ViewerPreferences"));
        return dict?.GetName(key);
    }

    /// <summary>The natural language of the document (BCP 47).</summary>
    public string? Language
    {
        get
        {
            var obj = _reader.Resolve(_reader.Catalog.Get("Lang"));
            return obj is PdfString s ? s.ToText() : null;
        }
        set
        {
            if (value is null)
                _reader.Catalog.Remove("Lang");
            else
                _reader.Catalog.Set("Lang", new PdfString(Encoding.Latin1.GetBytes(value)));
        }
    }

    /// <summary>Whether the document is tagged PDF.</summary>
    public bool IsTagged
    {
        get
        {
            var markInfo = _reader.ResolveDict(_reader.Catalog.Get("MarkInfo"));
            if (markInfo is null) return false;
            var marked = markInfo.Get("Marked");
            return marked switch
            {
                PdfBoolean b => b.Value,
                PdfInteger i => i.Value != 0,
                _ => false,
            };
        }
    }

    /// <summary>The structure-tree root (/StructTreeRoot) for reading a tagged
    /// document's logical structure, or null when the document is not tagged.</summary>
    public Aspose.Pdf.Tagged.StructTreeRoot? StructTreeRoot
    {
        get
        {
            var dict = _reader.ResolveDict(_reader.Catalog.Get("StructTreeRoot"));
            return dict is not null ? new Aspose.Pdf.Tagged.StructTreeRoot(dict, _reader) : null;
        }
    }

    /// <summary>
    /// The root of the document's logical-structure tree. Creates a
    /// /StructTreeRoot dictionary on demand when the document hasn't
    /// been tagged yet, so callers can always walk
    /// <see cref="Structure.Element.Children"/> on the result.
    /// </summary>
    public Aspose.Pdf.Structure.RootElement LogicalStructure
    {
        get
        {
            var dict = _reader.ResolveDict(_reader.Catalog.Get("StructTreeRoot"));
            if (dict is null)
            {
                dict = new Aspose.Pdf.Core.PdfDictionary();
                dict.Set("Type", new Aspose.Pdf.Core.PdfName("StructTreeRoot"));
                _reader.Catalog.Set("StructTreeRoot", dict);
            }
            return new Aspose.Pdf.Structure.RootElement(dict, _reader);
        }
    }

    /// <summary>
    /// Tagged-content surface for accessibility metadata and the
    /// logical-structure tree. Auto-initialises /MarkInfo and
    /// /StructTreeRoot on first access so the property never returns
    /// null.
    /// </summary>
    public Tagged.ITaggedContent TaggedContent
    {
        get
        {
            if (_taggedContent is not null) return _taggedContent;
            _taggedContent = Tagged.TaggedContent.Create(this)
                             ?? Tagged.TaggedContent.CreateForNewDocument(this);
            return _taggedContent;
        }
    }

    /// <summary>
    /// Merge multiple PDF documents into one.
    /// </summary>
    private bool _generatedFormFieldsRegistered;

    private System.Collections.Generic.List<(Heading h, int pageIdx)> CollectTocHeadings(Page tocPage)
    {
        var tocIdx = Pages.IndexOf(tocPage);
        var result = new System.Collections.Generic.List<(Heading, int)>();
        for (var pi = 1; pi <= PageCount; pi++)
            CollectTocHeadingsFrom(Pages.At(pi).Paragraphs, tocPage, pi, pi == tocIdx, result);
        return result;
    }

    /// <summary>
    /// Read the type-shadowed IsInNewPage flag. TextFragment and HtmlFragment redeclare it with
    /// <c>new</c>, so a BaseParagraph-typed read would miss the value the caller set on the
    /// concrete type (mirrors the IsInLineParagraph shadowing).
    private static bool ParagraphIsInNewPage(BaseParagraph p) => p switch
    {
        Text.TextFragment tf => tf.IsInNewPage,
        HtmlFragment hf => hf.IsInNewPage,
        _ => p.IsInNewPage,
    };

    /// <summary>Read the type-shadowed IsInLineParagraph flag (TextFragment
    /// redeclares it with <c>new</c> — same trap as IsInNewPage above).</summary>
    private static bool ParagraphInlineFlag(BaseParagraph p) => p switch
    {
        Text.TextFragment tf => tf.IsInLineParagraph,
        _ => p.IsInLineParagraph,
    };

    /// <summary>
    /// <summary>
    /// Shape a generator <see cref="Text.TextFragment"/> that carries Arabic/RTL text: replace
    /// each segment's base letters with their contextual presentation forms in visual
    /// right-to-left order, and route the fragment through an Arabic-capable embedded font
    /// (Arial covers Arabic Presentation Forms-B). Without this the default Standard-14 font has
    /// no Arabic glyphs and the renderer applies no OpenType shaping, so Arabic rendered as
    /// disconnected isolated letters in left-to-right order (or missing glyphs).
    /// </summary>
    /// <summary>Resolve a font family through the repository, swallowing lookup failures
    /// (an unknown family just yields null so the caller falls back to Standard-14).</summary>
    private static Text.Font? SafeFindFont(string family)
    {
        try { return Text.FontRepository.TryFindFont(family, ignoreCase: true); }
        catch { return null; }
    }

    /// <summary>Resolve a family in a specific style (bold/italic) through the repository,
    /// swallowing lookup failures. Returns null when the styled variant is unavailable.</summary>
    private static Text.Font? SafeFindFontStyled(string family, Text.FontStyles style)
    {
        try { return Text.FontRepository.TryFindFont(family, style, ignoreCase: true); }
        catch { return null; }
    }

}
