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

    /// <summary>True when this document was opened from a writable+seekable stream, i.e. a
    /// no-arg <see cref="Save()"/> will do an incremental (append-only) update. Only then do
    /// page edits need to register their new content/form streams as indirect objects (an
    /// append writes only registered new/dirty objects); a full save promotes inline streams,
    /// so a document being written to a fresh output keeps the compact inline layout.</summary>
    internal bool HasWritableSourceStream => _sourceStream is { CanWrite: true };

    private Document(byte[] data, string? password = null)
    {
        // Empty input is not a document: opening zero bytes surfaces a format error like
        // any other unreadable input (the public API contract — see
        // DocumentFacadeTests.Open_EmptyBytes_Throws). A blank document is created only
        // through the explicit no-argument / version constructors, never inferred from
        // empty bytes (which otherwise masks an unmaterialised or truncated source).
        _data = data;
        _reader = password is not null
            ? PdfReader.FromBytes(data, password)
            : PdfReader.FromBytes(data);
        // Eagerly verify decryption so encrypted PDFs fail fast rather than
        // silently producing garbled content. When a password was supplied,
        // FromBytes already called InitDecryptor(password).
        if (password is null)
        {
            _reader.InitDecryptor();
            // A document carrying a real (non-empty) user password must not open
            // without one — strings/streams would decode to garbage. Owner-only
            // encryption (empty user password) authenticates above and opens fine.
            if (_reader.RequiresPassword)
                throw new InvalidPasswordException(
                    "The document is password protected. A password is required to open this document.");
        }
        // Eagerly validate the catalog so corrupt PDFs fail at construction time
        // (matches .NET public API behavior)
        _ = _reader.Catalog;
        // Back-reference so components holding only this document's reader/page
        // can reach the document (e.g. to allocate new indirect objects).
        _reader.OwnerDocument = this;
    }

    /// <summary>
    /// Create a new empty PDF document (parameterless constructor, matches public API).
    /// </summary>
    public Document() : this(CreateEmptyPdf())
    {
        _isNewDocument = true;
    }

    /// <summary>
    /// Open a PDF document from a file path.
    /// </summary>
    public Document(string filename) : this(File.ReadAllBytes(filename)) { FileName = filename; }

    /// <summary>
    /// Create an empty document targeting the given PDF specification version.
    /// The version is stored on the catalog /Version entry; tests/callers can
    /// read it back via <see cref="PdfVersion"/> (string).
    /// </summary>
    public Document(PdfVersion version) : this(CreateEmptyPdf())
    {
        _isNewDocument = true;
        var name = version switch
        {
            Aspose.Pdf.PdfVersion.v_1_0 => "1.0",
            Aspose.Pdf.PdfVersion.v_1_1 => "1.1",
            Aspose.Pdf.PdfVersion.v_1_2 => "1.2",
            Aspose.Pdf.PdfVersion.v_1_3 => "1.3",
            Aspose.Pdf.PdfVersion.v_1_4 => "1.4",
            Aspose.Pdf.PdfVersion.v_1_5 => "1.5",
            Aspose.Pdf.PdfVersion.v_1_6 => "1.6",
            Aspose.Pdf.PdfVersion.v_1_7 => "1.7",
            Aspose.Pdf.PdfVersion.v_2_0 => "2.0",
            _ => "1.4",
        };
        _reader.Catalog.Set("Version", new PdfName(name));
    }

    /// <summary>
    /// Open an encrypted PDF document from a file path with a password.
    /// </summary>
    public Document(string filename, string password) : this(File.ReadAllBytes(filename), password) { FileName = filename; }

    /// <summary>Open a Public-Key security-handler encrypted document from
    /// a file path using the supplied certificate-encryption options. The
    /// private key (from PFX or Windows store) is unwrapped to derive the
    /// per-document AES/RC4 key. Throws <see cref="NotSupportedException"/>
    /// when the document doesn't carry a Public-Key /Filter — fall back to
    /// the password ctor for Standard-handler files.</summary>
    public Document(string filename, Security.CertificateEncryptionOptions certOptions)
        : this(File.ReadAllBytes(filename), certOptions)
    {
        FileName = filename;
    }

    /// <summary>File ctor with cert options and the <c>isManagedStream</c>
    /// flag (stored only — the in-memory byte buffer is always reusable).</summary>
    public Document(string filename, Security.CertificateEncryptionOptions certOptions, bool isManagedStream)
        : this(filename, certOptions)
    {
        _ = isManagedStream;
    }

    /// <summary>Open a Public-Key security-handler encrypted document from
    /// a stream. See <see cref="Document(string, Security.CertificateEncryptionOptions)"/>.</summary>
    public Document(Stream input, Security.CertificateEncryptionOptions certOptions)
        : this(ReadStreamToBytes(input), certOptions)
    {
    }

    /// <summary>Stream ctor with cert options and the <c>isManagedStream</c>
    /// flag.</summary>
    public Document(Stream input, Security.CertificateEncryptionOptions certOptions, bool isManagedStream)
        : this(input, certOptions)
    {
        _ = isManagedStream;
    }

    private Document(byte[] data, Security.CertificateEncryptionOptions certOptions)
        : this(data)
    {
        if (certOptions is null) throw new ArgumentNullException(nameof(certOptions));
        // Public-Key security handler decryption is NOT implemented in this
        // FOSS branch. The Standard handler (RC4/AES password) is the only
        // wired path. Throw clearly when the document carries a non-Standard
        // /Filter so the caller knows to switch to the password ctor.
        if (!IsEncrypted) return;
        var encrypt = _reader.ResolveDict(_reader.Trailer.Get("Encrypt"));
        var filter = encrypt?.GetName("Filter");
        if (filter == "Standard") return;
        if (filter is "Adobe.PPKLite" or "Adobe.PPKMS" or "Adobe.PubSec")
        {
            // Public-key security handler: recover the file key from a recipient
            // envelope with the private key and attach a decryptor to the reader.
            _pubSecPermissions = Security.PubSecHandler.Open(_reader, encrypt!, certOptions);
            return;
        }
        throw new NotSupportedException(
            $"Document uses an unsupported security handler (/Filter = {filter ?? "?"}). " +
            "Only Standard (password) and Adobe.PPKLite/Adobe.PPKMS (certificate) handlers are supported.");
    }

    /// <summary>Permissions recovered from a public-key (certificate) security
    /// handler envelope, when the document was opened that way.</summary>
    private int? _pubSecPermissions;

    /// <summary>
    /// Open an encrypted PDF document from a stream with a password.
    /// </summary>
    public Document(Stream input, string password) : this(ReadStreamToBytes(input), password) { }

    /// <summary>
    /// Open an HTML file and convert to PDF using the given options.
    /// Matches the public API constructor: new Document(path, HtmlLoadOptions).
    /// </summary>
    public Document(string path, HtmlLoadOptions options)
        : this(Converters.HtmlToPdfConverter.Convert(path, options).ToArray()) { }

    /// <summary>
    /// Open an HTML stream and convert to PDF using the given options.
    /// </summary>
    public Document(Stream stream, HtmlLoadOptions options)
        : this(Converters.HtmlToPdfConverter.Convert(ReadStreamToBytes(stream), options).ToArray()) { }

    /// <summary>
    /// Open a plain-text (.txt) file and convert it to PDF using the given options.
    /// Matches the public API constructor: new Document(path, TxtLoadOptions).
    /// </summary>
    public Document(string path, TxtLoadOptions options)
        : this(Converters.TxtToPdfConverter.Convert(path, options)) { }

    /// <summary>
    /// Open a plain-text stream and convert it to PDF using the given options.
    /// </summary>
    public Document(Stream stream, TxtLoadOptions options)
        : this(Converters.TxtToPdfConverter.Convert(ReadStreamToBytes(stream), options)) { }

    /// <summary>
    /// Open a Markdown file and convert it to PDF using the given options.
    /// Matches the public API constructor: new Document(path, MdLoadOptions).
    /// </summary>
    public Document(string path, MdLoadOptions options)
        : this(Converters.MarkdownToPdfConverter.Convert(path, options).ToArray()) { }

    /// <summary>
    /// Open a Markdown stream and convert it to PDF using the given options.
    /// </summary>
    public Document(Stream stream, MdLoadOptions options)
        : this(Converters.MarkdownToPdfConverter.Convert(ReadStreamToBytes(stream), options).ToArray()) { }

    /// <summary>
    /// Open an SVG file and convert it to PDF using the given options.
    /// Matches the public API constructor: new Document(path, SvgLoadOptions).
    /// More specific than the <see cref="LoadOptions"/> catch-all, so it wins
    /// overload resolution and the SVG is parsed as SVG (not as PDF).
    /// </summary>
    public Document(string path, SvgLoadOptions options)
        : this(SvgConvertToPdfBytes(Converters.SvgToPdfConverter.Convert(path, options))) { }

    /// <summary>
    /// Open an SVG stream and convert it to PDF using the given options.
    /// </summary>
    public Document(Stream stream, SvgLoadOptions options)
        : this(SvgConvertToPdfBytes(Converters.SvgToPdfConverter.Convert(ReadStreamToBytes(stream), options))) { }

    /// <summary>Serialise a freshly converted SVG-&gt;PDF <see cref="Document"/> to
    /// bytes so it can be re-opened through the normal PDF constructor chain
    /// (keeps the converted document's reader/xref consistent).</summary>
    private static byte[] SvgConvertToPdfBytes(Document converted)
    {
        using var ms = new MemoryStream();
        converted.Save(ms);
        return ms.ToArray();
    }

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
    /// Whether a license is currently applied. Always returns <c>true</c> —
    /// no evaluation-watermark restrictions are imposed by this library.
    /// </summary>
    public static bool IsLicensed => true;

    /// <summary>
    /// Document-level JavaScript (PDF spec §12.6.4.16 — /Names/JavaScript
    /// name tree in the catalog). Each entry maps a script name to a
    /// /S /JavaScript action containing a /JS source string.
    /// </summary>
    public JavaScriptCollection JavaScript => new(this);

    /// <summary>
    /// Open a PDF document from a byte array.
    /// </summary>
    public static Document Open(byte[] data)
    {
        return new Document(data);
    }

    /// <summary>
    /// Open an encrypted PDF document from a byte array with a password.
    /// </summary>
    public static Document Open(byte[] data, string password)
    {
        return new Document(data, password);
    }

    /// <summary>
    /// Open a PDF document from a file path.
    /// </summary>
    public static Document Open(string path)
    {
        var data = File.ReadAllBytes(path);
        return new Document(data) { FileName = path };
    }

    /// <summary>
    /// Open an encrypted PDF document from a file path with a password.
    /// </summary>
    public static Document Open(string path, string password)
    {
        var data = File.ReadAllBytes(path);
        return new Document(data, password) { FileName = path };
    }

    /// <summary>
    /// Open a PDF document from a stream (constructor, matches public API).
    /// </summary>
    public Document(Stream input) : this(ReadStreamToBytes(input))
    {
        var stream = input;
        // Retain any writable+seekable stream as the incremental-save source so the
        // no-arg Save()/ToArray() write back into the caller's stream — the
        // contract for `new Document(stream)`. Internal facades that
        // must not mutate their buffer open via Document.Open()/the byte[] ctor (which
        // do not set _sourceStream), so they are unaffected.
        if (stream.CanWrite && stream.CanSeek)
            _sourceStream = stream;
    }

    /// <summary>
    /// Open a PDF document from a stream.
    /// </summary>
    public static Document Open(Stream stream)
    {
        using var ms = new MemoryStream();
        if (stream.CanSeek && stream.Position != 0) stream.Position = 0;
        stream.CopyTo(ms);
        return new Document(ms.ToArray());
    }

    /// <summary>
    /// Open an encrypted PDF document from a stream with a password.
    /// </summary>
    public static Document Open(Stream stream, string password)
    {
        using var ms = new MemoryStream();
        if (stream.CanSeek && stream.Position != 0) stream.Position = 0;
        stream.CopyTo(ms);
        return new Document(ms.ToArray(), password);
    }

    /// <summary>
    /// Open a Markdown file and convert it to a PDF document.
    /// </summary>
    public static Document Open(string path, MdLoadOptions options)
    {
        return Converters.MarkdownToPdfConverter.Convert(path, options);
    }

    /// <summary>
    /// Open a Markdown file from bytes and convert it to a PDF document.
    /// </summary>
    public static Document Open(byte[] data, MdLoadOptions options)
    {
        return Converters.MarkdownToPdfConverter.Convert(data, options);
    }

    /// <summary>
    /// Open an SVG file and convert it to a PDF document.
    /// </summary>
    public static Document Open(string path, SvgLoadOptions options)
    {
        return Converters.SvgToPdfConverter.Convert(path, options);
    }

    /// <summary>
    /// Open an SVG file from bytes and convert it to a PDF document.
    /// </summary>
    /// <summary>
    /// Open an HTML file and convert it to a PDF document.
    /// </summary>
    public static Document Open(string path, HtmlLoadOptions options)
    {
        return Converters.HtmlToPdfConverter.Convert(path, options);
    }

    /// <summary>
    /// Open an HTML file from bytes and convert it to a PDF document.
    /// </summary>
    public static Document Open(byte[] data, HtmlLoadOptions options)
    {
        return Converters.HtmlToPdfConverter.Convert(data, options);
    }

    public static Document Open(byte[] data, SvgLoadOptions options)
    {
        return Converters.SvgToPdfConverter.Convert(data, options);
    }

    /// <summary>
    /// Bind an Aspose.Pdf XML template to this document. Parses the XML and builds
    /// pages with text, tables, and formatting.
    /// </summary>
    public void BindXml(string file)
    {
        var xml = File.ReadAllText(file);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(file));
        XmlBinding.Bind(this, xml, baseDir);
    }

    /// <summary>
    /// Bind an Aspose.Pdf XML template from a stream to this document.
    /// </summary>
    public void BindXml(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd();
        XmlBinding.Bind(this, xml);
    }

    /// <summary>
    /// Bind an Aspose.Pdf XML template from a stream with an optional XSLT
    /// stylesheet stream. A null xslStream skips the XSLT transform.
    /// </summary>
    public void BindXml(Stream xmlStream, Stream? xslStream)
        => BindXml(xmlStream, xslStream, settings: null);

    /// <summary>
    /// Bind an Aspose.Pdf XML template from a stream with an optional XSLT
    /// stylesheet stream and explicit XmlReader settings for the input parse.
    /// </summary>
    public void BindXml(Stream xmlStream, Stream? xslStream, System.Xml.XmlReaderSettings? settings)
    {
        using var reader = new StreamReader(xmlStream);
        var xml = reader.ReadToEnd();
        if (xslStream is not null)
        {
            var xslt = new System.Xml.Xsl.XslCompiledTransform();
            using var xsltReader = System.Xml.XmlReader.Create(xslStream);
            xslt.Load(xsltReader);
            using var input = new System.IO.StringReader(xml);
            using var xr = settings is not null
                ? System.Xml.XmlReader.Create(input, settings)
                : System.Xml.XmlReader.Create(input);
            using var sw = new System.IO.StringWriter();
            xslt.Transform(xr, null, sw);
            xml = sw.ToString();
        }
        XmlBinding.Bind(this, xml);
    }

    /// <summary>
    /// Bind an Aspose.Pdf XML template with an optional XSLT stylesheet.
    /// </summary>
    public void BindXml(string xmlFile, string? xslFile)
    {
        string xml = File.ReadAllText(xmlFile);
        if (xslFile is not null)
        {
            var xslt = new System.Xml.Xsl.XslCompiledTransform();
            xslt.Load(xslFile);
            using var input = new System.IO.StringReader(xml);
            using var xr = System.Xml.XmlReader.Create(input);
            using var sw = new System.IO.StringWriter();
            xslt.Transform(xr, null, sw);
            xml = sw.ToString();
        }
        XmlBinding.Bind(this, xml);
    }

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

    /// <summary>Whether to unload large objects after use to reduce memory pressure. Stored only; not currently honoured.</summary>
    public bool EnableObjectUnload { get; set; }

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

    private static bool IsPdfaFormat(Aspose.Pdf.PdfFormat f) => f switch
    {
        Aspose.Pdf.PdfFormat.PDF_A_1A or Aspose.Pdf.PdfFormat.PDF_A_1B
        or Aspose.Pdf.PdfFormat.PDF_A_2A or Aspose.Pdf.PdfFormat.PDF_A_2B or Aspose.Pdf.PdfFormat.PDF_A_2U
        or Aspose.Pdf.PdfFormat.PDF_A_3A or Aspose.Pdf.PdfFormat.PDF_A_3B or Aspose.Pdf.PdfFormat.PDF_A_3U
        or Aspose.Pdf.PdfFormat.PDF_A_4 or Aspose.Pdf.PdfFormat.PDF_A_4E or Aspose.Pdf.PdfFormat.PDF_A_4F => true,
        _ => false,
    };

    private Aspose.Pdf.PdfFormat? DetectPdfFormatFromXmp()
    {
        try
        {
            // Prefer the in-memory Metadata dict (reflects Remove/Set after load) over raw stream.
            if (!HasMetadata) return null;
            var m = Metadata;
            var partRaw = m.Get("pdfaid:part");
            if (string.IsNullOrEmpty(partRaw)) return null;
            var part = partRaw.ToUpperInvariant();
            var conf = (m.Get("pdfaid:conformance") ?? "").ToUpperInvariant();
            return (part, conf) switch
            {
                ("1", "A") => Aspose.Pdf.PdfFormat.PDF_A_1A,
                ("1", "B") => Aspose.Pdf.PdfFormat.PDF_A_1B,
                ("2", "A") => Aspose.Pdf.PdfFormat.PDF_A_2A,
                ("2", "B") => Aspose.Pdf.PdfFormat.PDF_A_2B,
                ("2", "U") => Aspose.Pdf.PdfFormat.PDF_A_2U,
                ("3", "A") => Aspose.Pdf.PdfFormat.PDF_A_3A,
                ("3", "B") => Aspose.Pdf.PdfFormat.PDF_A_3B,
                ("3", "U") => Aspose.Pdf.PdfFormat.PDF_A_3U,
                ("4", _) => Aspose.Pdf.PdfFormat.PDF_A_4,
                _ => (Aspose.Pdf.PdfFormat?)null,
            };
        }
        catch { return null; }
    }

    private string? GetMetadata()
    {
        var metaObj = _reader.Resolve(_reader.Catalog.Get("Metadata"));
        if (metaObj is Aspose.Pdf.Core.PdfStream st)
        {
            var bytes = _reader.DecodeStream(st);
            return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
        }
        return null;
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
    /// Engine handle exposed for the legacy <c>doc._engineDoc.X</c> access pattern. Returns a
    /// thin wrapper that forwards engine-level reads/writes (currently only
    /// <see cref="EngineDocFacade.IsLinearized"/>) to the public Document surface.
    /// </summary>
    public EngineDocFacade _engineDoc => new EngineDocFacade(this);

    /// <summary>
    /// Wrapper providing the small slice of engine-level state callers may set
    /// via <c>doc._engineDoc.X</c>.
    /// </summary>
    public sealed class EngineDocFacade
    {
        private readonly Document _doc;
        internal EngineDocFacade(Document doc) { _doc = doc; }
        /// <summary>Forwards to <see cref="Document.IsLinearized"/>.</summary>
        public bool IsLinearized { get => _doc.IsLinearized; set => _doc.IsLinearized = value; }
    }

    /// <summary>
    /// Validate the document structure and return any issues found.
    /// </summary>
    public ValidationIssue[] Validate() => DocumentValidator.Validate(this);

    /// <summary>
    /// Validate the document against a specific PDF format (PDF/A, PDF/X).
    /// </summary>
    /// <param name="outputLogStream">Stream for logging validation results (can be Stream.Null).</param>
    /// <param name="format">Target format to validate against.</param>
    /// <returns>True if the document conforms to the specified format.</returns>
    public bool Validate(Stream outputLogStream, PdfFormat format)
    {
        var logStream = outputLogStream;
        var result = Optimization.PdfAValidator.Validate(this, format);
        // Write the log into the supplied stream and take ownership of it.
        // For example, a caller does
        //   var f = new FileStream(path, Create);
        //   doc.Validate(f, fmt);
        //   f = new FileStream(path, Create);   // <-- IOException without dispose here
        // because the previous FileStream is still open under the old reference.
        // The contract is that the stream is disposed on return, so callers
        // can re-open the same path immediately.
        if (logStream is not null)
        {
            try
            {
                using var writer = new StreamWriter(logStream, System.Text.Encoding.UTF8);
                WriteValidationLogXml(writer, format, result);
            }
            catch { }
        }
        return result.IsValid;
    }

    /// <summary>Serialise a validation result in the established log schema:
    /// <c>&lt;Compliance&gt;&lt;File&gt;…&lt;Fonts&gt;&lt;Problem Severity Clause&gt;</c> —
    /// font problems nest under &lt;Fonts&gt;, everything else sits directly under
    /// &lt;File&gt; alongside the empty section markers.</summary>
    private void WriteValidationLogXml(TextWriter writer, PdfFormat format,
        Optimization.PdfAValidationResult result, string operation = "Validation")
    {
        static string ClauseFor(string rule) => rule switch
        {
            "FontCmap" => "7.21.4.2",
            "FontEmbedding" or "FontNotEmbedded" => "6.2.11.4",
            "MetadataPdfAId" or "MetadataPdfAConformance" or "Metadata" => "6.6.4",
            "TaggedPdf" or "StructureTree" => "6.7.3.3",
            "DocumentTitle" => "7.1",
            _ => "",
        };
        static string Problem(Optimization.PdfAViolation v)
        {
            var clause = v.Clause ?? ClauseFor(v.Rule);
            var page = v.PageNumber is int p ? $" Page=\"{p}\"" : "";
            // Convertable defaults to true — every regular violation class this
            // validator reports is either repaired structurally (fonts, metadata,
            // OutputIntent, version, file ID, xref form) or stripped under
            // ConvertErrorAction.Delete. Implementation-limit violations baked into
            // the content mark themselves unconvertable instead.
            var convertable = v.Convertable ? "True" : "False";
            return $"<Problem Severity=\"Error\" Clause=\"{clause}\" Code=\"{clause}\" Convertable=\"{convertable}\"{page}>{EscapeXml(v.Description)}</Problem>";
        }

        var fontProblems = new System.Text.StringBuilder();
        var otherProblems = new System.Text.StringBuilder();
        foreach (var v in result.Violations)
        {
            if (v.Rule.StartsWith("Font", StringComparison.Ordinal)) fontProblems.Append(Problem(v));
            else otherProblems.Append(Problem(v));
        }

        int pages;
        try { pages = Pages.Count; } catch { pages = 0; }
        writer.Write(
            $"<Compliance Name=\"Log\" Operation=\"{operation}\" Target=\"{EscapeXml(GetVersionString(format))}\">" +
            "<Version>1.0</Version>" +
            $"<Date>{DateTime.Now}</Date>" +
            $"<File Version=\"{EscapeXml(PdfVersion ?? string.Empty)}\" Name=\"{EscapeXml(Path.GetFileName(FileName ?? string.Empty))}\" Pages=\"{pages}\">" +
            "<Security /><Catalog /><Header /><Annotations />" +
            (fontProblems.Length > 0 ? $"<Fonts>{fontProblems}</Fonts>" : "<Fonts />") +
            "<trailer />" + otherProblems +
            "<Metadata /><objects /><xObjects /><actions /><xmpmeta /><EmbeddedFiles />" +
            "</File></Compliance>");
    }

    /// <summary>
    /// Validate the document against a specific PDF format using conversion options.
    /// </summary>
    public bool Validate(PdfFormatConversionOptions options)
    {
        var result = Optimization.PdfAValidator.Validate(this, options.TargetFormat);
        return result.IsValid;
    }

    /// <summary>
    /// Validate the document against a specific PDF format, writing log to a file.
    /// </summary>
    /// <param name="outputLogFileName">Path to write validation log.</param>
    /// <param name="format">Target format to validate against.</param>
    /// <returns>True if the document conforms to the specified format.</returns>
    public bool Validate(string outputLogFileName, PdfFormat format)
    {
        var result = Optimization.PdfAValidator.Validate(this, format);
        if (!string.IsNullOrEmpty(outputLogFileName))
        {
            try
            {
                using var writer = new StreamWriter(outputLogFileName, append: false, System.Text.Encoding.UTF8);
                WriteValidationLogXml(writer, format, result);
            }
            catch
            {
                // Log write failure should not prevent validation result
            }
        }
        return result.IsValid;
    }

    /// <summary>
    /// Returns the PDF/A compliance level detected from XMP metadata, or null if not a PDF/A document.
    /// </summary>
    public PdfFormat? GetPdfACompliance()
    {
        var part = Metadata.Get("pdfaid:part");
        var conformance = Metadata.Get("pdfaid:conformance")?.ToUpperInvariant();

        if (string.IsNullOrEmpty(part))
            return null;

        return (part, conformance) switch
        {
            ("1", "A") => PdfFormat.PDF_A_1A,
            ("1", "B") => PdfFormat.PDF_A_1B,
            ("2", "A") => PdfFormat.PDF_A_2A,
            ("2", "B") => PdfFormat.PDF_A_2B,
            ("2", "U") => PdfFormat.PDF_A_2U,
            ("3", "A") => PdfFormat.PDF_A_3A,
            ("3", "B") => PdfFormat.PDF_A_3B,
            ("3", "U") => PdfFormat.PDF_A_3U,
            ("4", _) => PdfFormat.PDF_A_4,
            _ => null,
        };
    }

    /// <summary>
    /// Check whether the document needs repair.
    /// </summary>
    /// <param name="options">When repair is needed, receives the repair options describing the issues.</param>
    /// <returns>True if the document has structural issues that can be repaired.</returns>
    public bool IsRepairNeeded(out RepairOptions options)
    {
        options = new RepairOptions();

        // Check for common structural issues
        try
        {
            var issues = Validate();
            if (issues.Length > 0)
            {
                options.HasValidationIssues = true;
                options.IssueCount = issues.Length;
                return true;
            }
        }
        catch
        {
            options.HasValidationIssues = true;
            return true;
        }

        // Check xref table integrity
        try
        {
            foreach (var entry in _reader.XRefTable.Entries.Values)
            {
                if (!entry.InUse) continue;
                var obj = _reader.Resolve(new Core.PdfIndirectRef(entry.ObjectNumber, entry.Generation));
                // If resolution fails for an in-use object, repair is needed
            }
        }
        catch
        {
            options.HasXRefIssues = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Repair the document by re-serializing it.
    /// Rebuilds xref table, fixes object numbering, and normalizes structure.
    /// After repair, Save() will produce a clean PDF.
    /// </summary>
    public void Repair()
    {
        // Re-serialization through Save() inherently repairs the document:
        // - Rebuilds xref table from scratch
        // - Re-numbers all objects sequentially
        // - Normalizes the page tree structure
        // - Drops orphaned/corrupt objects
        _linearize = false; // ensure full rewrite

        // Fix oversized annotation rects (clamp to short.MaxValue per PDF spec recommendation)
        const double maxCoord = short.MaxValue; // 32767
        foreach (var page in Pages)
        {
            foreach (var annot in page.Annotations)
            {
                var rect = annot.Rect;
                if (rect is null) continue;
                bool needsFix = Math.Abs(rect.LLX) > maxCoord || Math.Abs(rect.LLY) > maxCoord ||
                                Math.Abs(rect.URX) > maxCoord || Math.Abs(rect.URY) > maxCoord;
                if (needsFix)
                {
                    var mb = page.MediaBox;
                    annot.Rect = new Rectangle(
                        Math.Max(rect.LLX, mb.LLX),
                        Math.Max(rect.LLY, mb.LLY),
                        Math.Min(rect.URX, mb.URX),
                        Math.Min(rect.URY, mb.URY));
                }
            }
        }
    }

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
    /// Linearize the document for fast web viewing.
    /// Reorders objects so the first page can be displayed before the entire file is downloaded.
    /// </summary>
    public void LinearizeDocument()
    {
        // For now, this is a stub that marks the document for linearization on save.
        // Full linearization is a future enhancement.
        _linearize = true;
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

    /// <summary>
    /// Whether the document has an interactive form.
    /// </summary>
    public bool HasForm => _reader.Catalog.ContainsKey("AcroForm");

    /// <summary>
    /// Reads a value from the document's /Catalog by name. Returns the
    /// resolved object's text representation (or its raw stream data
    /// decoded as UTF-8 for streams), or null if no entry by that name
    /// exists. Useful for inspecting custom catalog entries that vendors
    /// stash next to the standard PDF spec keys.
    /// </summary>
    public object? GetCatalogValue(string key)
    {
        var raw = _reader.Catalog.Get(key);
        if (raw is null) return null;
        var resolved = _reader.Resolve(raw);
        return resolved switch
        {
            Aspose.Pdf.Core.PdfString s => s.ToText(),
            Aspose.Pdf.Core.PdfName n => n.Value,
            Aspose.Pdf.Core.PdfStream stream => System.Text.Encoding.UTF8.GetString(_reader.DecodeStream(stream)),
            _ => resolved?.ToString(),
        };
    }

    /// <summary>Whether to disable font license verification checks. Stored only; not currently honoured.</summary>
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
    /// Flatten all form fields — renders their visual appearance into page content
    /// and removes the interactive form. Convenience wrapper for Form.Flatten().
    /// </summary>
    public void Flatten()
    {
        // Flatten form fields first (removes AcroForm + widget annotations)
        Form?.Flatten(this);
        _form = null; // Reset cache so Form re-reads from (now removed) AcroForm

        // Flatten all remaining annotations on every page
        foreach (var page in Pages)
        {
            page.Flatten();
        }
    }

    /// <summary>Flatten with explicit settings. When
    /// <see cref="Forms.Form.FlattenSettings.HideButtons"/> is true the
    /// XFA template's button fields are marked presence="hidden" and the
    /// AcroForm/XFA structure is preserved (only the visuals are
    /// suppressed for downstream rendering); otherwise behaves like
    /// <see cref="Flatten()"/>.</summary>
    public void Flatten(Forms.Form.FlattenSettings flattenSettings)
    {
        // An XFA form keeps its template; HideButtons only marks the XFA button
        // fields presence="hidden" (the dynamic form is preserved, not folded
        // into page content) — see HideXfaButtons.
        if (flattenSettings is { HideButtons: true } && Form is { IsXfa: true })
        {
            HideXfaButtons();
            return;
        }
        // Forward the settings to Form.Flatten so UpdateAppearances /
        // ApplyRedactions / HideButtons are honoured. Without this the flag was
        // stored only and a flatten of a programmatically re-valued form rendered
        // stale appearances. For a
        // plain AcroForm, HideButtons drops push-button widgets while flattening
        // the rest into page content.
        Form?.FlattenWithSettings(this, flattenSettings);
        _form = null;
        foreach (var page in Pages) page.Flatten();
    }

    private void HideXfaButtons()
    {
        // Walk the XFA template and mark any <field> whose <ui> contains a
        // <button> as presence="hidden" so the flatten step (and downstream
        // template inspection) treats them as absent.
        if (Form is null || !Form.IsXfa) return;
        var xml = Form.GetXfaTemplateXml();
        if (xml is null) return;
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);
            // Use the template packet's actual xfa-template namespace version (2.6 / 2.8 /
            // 3.0 / …) rather than a hard-coded 2.6, otherwise the button XPath matches
            // nothing on a non-2.6 form and no field is marked hidden.
            var tplNs = doc.DocumentElement?.NamespaceURI;
            if (string.IsNullOrEmpty(tplNs)) return;
            var nsm = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nsm.AddNamespace("tpl", tplNs);
            var buttons = doc.SelectNodes("//tpl:field/tpl:ui/tpl:button", nsm);
            if (buttons is null) return;
            foreach (System.Xml.XmlNode btn in buttons)
            {
                var field = btn.ParentNode?.ParentNode as System.Xml.XmlElement;
                field?.SetAttribute("presence", "hidden");
            }
            Form.SetXfaTemplateXml(doc.OuterXml);
        }
        catch { /* malformed XFA — leave untouched */ }
    }

    /// <summary>Invalidate the cached Form so it re-reads from the AcroForm dict.</summary>
    internal void InvalidateForm() => _form = null;

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
    /// Whether the document has bookmarks.
    /// </summary>
    public bool HasOutlines => Outlines is not null && Outlines.Count > 0;

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

    private XmpMetadata GetOrCreateXmpMetadata()
    {
        if (_metadataChecked && _metadata is not null) return _metadata;
        _metadataChecked = true;
        var stream = _reader.ResolveStream(_reader.Catalog.Get("Metadata"));
        if (stream is not null)
            _metadata = new XmpMetadata(stream, _reader);
        _metadata ??= new XmpMetadata();
        // Standard XMP properties fall back to the document Info dictionary when
        // the packet is absent or omits them (per the XMP↔DocInfo mapping).
        _metadata.SetInfoFallback(ResolveInfoDerivedXmp);
        return _metadata;
    }

    /// <summary>Map a standard XMP key to its /Info-dictionary equivalent, or
    /// null when Info carries no such value. Used as the XMP value fallback so
    /// e.g. <c>Metadata["xmp:ModifyDate"]</c> resolves from /ModDate on documents
    /// that have no XMP packet.</summary>
    private string? ResolveInfoDerivedXmp(string key)
    {
        static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
        static string? XmpDate(DateTime dt)
            => dt == DateTime.MinValue ? null : dt.ToString("yyyy-MM-ddTHH:mm:ss");

        var info = Info;
        return key switch
        {
            "xmp:CreatorTool" => NonEmpty(info.Creator),
            "pdf:Producer" => NonEmpty(info.Producer),
            "dc:title" => NonEmpty(info.Title),
            "dc:creator" => NonEmpty(info.Author),
            "dc:description" => NonEmpty(info.Subject),
            "pdf:Keywords" => NonEmpty(info.Keywords),
            "xmp:CreateDate" => XmpDate(info.CreationDate),
            "xmp:ModifyDate" => XmpDate(info.ModDate),
            _ => null,
        };
    }

    /// <summary>
    /// Whether the document has XMP metadata.
    /// </summary>
    public bool HasMetadata => _reader.Catalog.ContainsKey("Metadata");

    /// <summary>
    /// Get or create XMP metadata for this document.
    /// If no metadata exists, a new empty XmpMetadata instance is created.
    /// </summary>
    public XmpMetadata GetOrCreateMetadata() => GetOrCreateXmpMetadata();

    /// <summary>
    /// Whether the document is encrypted.
    /// </summary>
    public bool IsEncrypted => _reader.Trailer.ContainsKey("Encrypt") || _encryptor is not null;

    /// <summary>
    /// Whether the document has been successfully decrypted (i.e., the decryptor was initialised).
    /// False for encrypted documents that were opened without (or with incorrect) password.
    /// </summary>
    public bool IsDecrypted => _reader.IsDecrypted;

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

    /// <summary>
    /// Add an embedded file to the document.
    /// </summary>
    public void AddEmbeddedFile(string fileName, byte[]? fileData, string? description = null,
        string? mimeType = null, bool compress = true,
        DateTime? creationDate = null, DateTime? modDate = null)
    {
        var fsDict = new PdfDictionary();
        fsDict.Set("Type", new PdfName("Filespec"));
        // /F uses Latin1; /UF uses UTF-16BE with BOM for non-ASCII file names
        fsDict.Set("F", new PdfString(Encoding.Latin1.GetBytes(fileName)));
        fsDict.Set("UF", Forms.Field.EncodePdfTextString(fileName));
        if (description is not null)
            fsDict.Set("Desc", Forms.Field.EncodePdfTextString(description));

        // A null payload registers a reference-only file specification — an external
        // file reference (/F) with no embedded /EF stream, e.g. a path that does not
        // resolve to a local file. A non-null payload embeds the bytes as an /EF stream.
        if (fileData is not null)
        {
            var fileStreamDict = new PdfDictionary();
            fileStreamDict.Set("Type", new PdfName("EmbeddedFile"));
            if (mimeType is not null)
                fileStreamDict.Set("Subtype", new PdfName(mimeType));
            var paramsDict = new PdfDictionary();
            paramsDict.Set("Size", new PdfInteger(fileData.Length));
            // /Params CreationDate and ModDate (PDF §7.11.3) record the source file's
            // timestamps when known, so an embedded attachment round-trips its dates.
            if (creationDate is { } cd)
                paramsDict.Set("CreationDate", new PdfString(Encoding.Latin1.GetBytes(FormatPdfDate(cd))));
            if (modDate is { } md)
                paramsDict.Set("ModDate", new PdfString(Encoding.Latin1.GetBytes(FormatPdfDate(md))));
            fileStreamDict.Set("Params", paramsDict);
            var fileStream = new PdfStream(fileStreamDict, fileData);
            // FileEncoding.None embeds the bytes uncompressed (no /Filter).
            if (!compress) fileStream.DoNotCompress = true;

            var efDict = new PdfDictionary();
            efDict.Set("F", fileStream);
            fsDict.Set("EF", efDict);
        }

        // Register as new object
        var fsObjNum = AllocateObjectNumber();
        AddNewObject(fsObjNum, fsDict);

        // Get or create /Names dict in catalog
        var namesDict = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        if (namesDict is null)
        {
            namesDict = new PdfDictionary();
            _reader.Catalog.Set("Names", namesDict);
        }

        // Get or create /EmbeddedFiles name tree
        var efTree = _reader.ResolveDict(namesDict.Get("EmbeddedFiles"));
        PdfArray numsArray;
        if (efTree is not null)
        {
            numsArray = _reader.Resolve(efTree.Get("Names")) as PdfArray ?? new PdfArray();
        }
        else
        {
            efTree = new PdfDictionary();
            namesDict.Set("EmbeddedFiles", efTree);
            numsArray = new PdfArray();
        }

        // PDF name trees require lexical ordering on the Names array (PDF 32000-1 §7.9.6).
        // Find the first existing key that compares > fileName and insert before it; if none,
        // append. Reading and 1-based indexing then match the alphabetical order callers
        // expect from /Names/EmbeddedFiles.
        var insertAt = numsArray.Count;
        for (var i = 0; i + 1 < numsArray.Count; i += 2)
        {
            if (_reader.Resolve(numsArray[i]) is not PdfString s) continue;
            if (string.CompareOrdinal(s.ToText(), fileName) <= 0) continue;
            insertAt = i;
            break;
        }
        numsArray.Insert(insertAt, new PdfString(Encoding.Latin1.GetBytes(fileName)));
        numsArray.Insert(insertAt + 1, new PdfIndirectRef(fsObjNum, 0));
        efTree.Set("Names", numsArray);
    }

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

    /// <summary>Whether the document has page labels.</summary>
    public bool HasPageLabels => _reader.Catalog.ContainsKey("PageLabels");

    /// <summary>Whether the document is a PDF Portfolio (has a /Collection dictionary in the catalog).</summary>
    public bool HasCollection => _reader.Catalog.ContainsKey("Collection");

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

    /// <summary>Whether the document has named destinations.</summary>
    public bool HasDestinations =>
        _reader.Catalog.ContainsKey("Dests") || _reader.Catalog.ContainsKey("Names");

    /// <summary>Named destinations in the document. Always non-null — when the
    /// catalog has no /Dests dict and no /Names tree, an empty mutable
    /// collection is returned so a freshly-constructed Document can call
    /// <c>doc.NamedDestinations.Add(...)</c> without an NRE and have the entry
    /// persist on Save (the underlying /Dests dict is materialised on first
    /// write through the collection's indexer/Add).</summary>
    public NamedDestinationCollection NamedDestinations =>
        new NamedDestinationCollection(_reader.Catalog, _reader);

    /// <summary>
    /// Add a named destination to the document using the /Names → /Dests name tree.
    /// </summary>
    /// <param name="name">The destination name.</param>
    /// <param name="destination">A destination array created by <see cref="NamedDestination"/> factory methods.</param>
    public void AddNamedDestination(string name, DestinationArray destination)
    {
        // Get or create /Names dict in catalog
        var namesDict = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        if (namesDict is null)
        {
            namesDict = new PdfDictionary();
            _reader.Catalog.Set("Names", namesDict);
        }

        // Get or create /Dests name tree
        var destsTree = _reader.ResolveDict(namesDict.Get("Dests"));
        PdfArray namesArray;
        if (destsTree is not null)
        {
            namesArray = _reader.Resolve(destsTree.Get("Names")) as PdfArray ?? new PdfArray();
        }
        else
        {
            destsTree = new PdfDictionary();
            namesDict.Set("Dests", destsTree);
            namesArray = new PdfArray();
        }

        namesArray.Add(new PdfString(Encoding.Latin1.GetBytes(name)));
        namesArray.Add(destination.Array);
        destsTree.Set("Names", namesArray);
    }

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

    private bool RemoveFromNameTree(PdfDictionary node, string name)
    {
        var namesArr = _reader.Resolve(node.Get("Names")) as PdfArray;
        if (namesArr is not null)
        {
            for (var i = 0; i + 1 < namesArr.Count; i += 2)
            {
                var nameObj = namesArr[i];
                var entryName = nameObj is PdfString s ? s.ToText() : nameObj.ToString() ?? "";
                if (entryName == name)
                {
                    // Rebuild array without the pair at i, i+1
                    var newArr = new PdfArray();
                    for (var j = 0; j < namesArr.Count; j++)
                    {
                        if (j == i || j == i + 1) continue;
                        newArr.Add(namesArr[j]);
                    }
                    node.Set("Names", newArr);
                    return true;
                }
            }
        }

        // Recurse into /Kids
        var kids = _reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is not null)
        {
            foreach (var kid in kids)
            {
                var kidDict = _reader.ResolveDict(kid);
                if (kidDict is not null && RemoveFromNameTree(kidDict, name))
                    return true;
            }
        }

        return false;
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

    /// <summary>Whether the document has optional content (layers).</summary>
    public bool HasLayers => _reader.Catalog.ContainsKey("OCProperties");

    /// <summary>
    /// Returns true if this PDF uses incremental updates (has multiple %%EOF markers).
    /// </summary>
    public bool HasIncrementalUpdate()
    {
        // Count occurrences of %%EOF in the raw data
        int eofCount = 0;
        var marker = "%%EOF"u8;
        for (int i = 0; i <= _data.Length - marker.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < marker.Length; j++)
            {
                if (_data[i + j] != marker[j]) { match = false; break; }
            }
            if (match)
            {
                eofCount++;
                i += marker.Length - 1;
            }
        }

        if (eofCount < 2) return false;

        // Hybrid-reference PDFs have 2 %%EOF markers (one for the traditional xref table,
        // one for the xref stream) but are NOT incrementally updated.
        bool isHybrid = _reader.Trailer.GetInt("XRefStm", -1) >= 0;

        // A linearized ("fast web view") PDF likewise has 2 %%EOF markers — the first-page
        // cross-reference section and the main one, linked by a /Prev — yet it is a single
        // generation file produced by Optimize(), not an incremental update. Its
        // linearization parameter dictionary (/Linearized) is the first body object, so
        // detect it near the start of the raw data (which is what the %%EOF count reflects).
        bool isLinearized = ContainsMarker(_data, "/Linearized"u8,
            System.Math.Min(_data.Length, 2048));

        int threshold = (isHybrid || isLinearized) ? 2 : 1;
        return eofCount > threshold;
    }

    /// <summary>Whether <paramref name="marker"/> occurs in the first <paramref name="limit"/>
    /// bytes of <paramref name="data"/> (a small forward scan; used for header-region markers).</summary>
    private static bool ContainsMarker(byte[] data, System.ReadOnlySpan<byte> marker, int limit)
    {
        for (int i = 0; i <= limit - marker.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < marker.Length; j++)
                if (data[i + j] != marker[j]) { match = false; break; }
            if (match) return true;
        }
        return false;
    }

    /// <summary>
    /// Set the PDF version for the output header (e.g., "1.7", "2.0").
    /// This overrides the version read from the original document.
    /// </summary>
    public void SetVersion(string version)
    {
        _versionOverride = version;
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
    /// Import specified pages from another document into this document.
    /// Deep-clones page dictionaries and all referenced resources.
    /// </summary>
    public void ImportPages(Document source, int[] pageNumbers, int insertAt = -1)
    {
        if (pageNumbers.Length == 0) return;

        foreach (var pn in pageNumbers)
        {
            if (pn < 1 || pn > source.PageCount)
                throw new ArgumentOutOfRangeException(nameof(pageNumbers),
                    $"Page number {pn} is out of range (1-{source.PageCount})");
        }

        var cloneMap = new Dictionary<int, int>();

        foreach (var pageNumber in pageNumbers)
        {
            var sourcePage = source.Pages.At(pageNumber);
            var clonedPageDict = DeepCloneDictExcluding(sourcePage.Dict, source._reader, cloneMap, "Parent");
            clonedPageDict.Set("Type", new PdfName("Page"));

            if (insertAt == -1)
                Pages.AddFromDict(clonedPageDict);
            else
            {
                Pages.InsertFromDict(insertAt, clonedPageDict);
                insertAt++;
            }
        }
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

    /// <summary>
    /// A clone map shared across all imports from <paramref name="sourceReader"/> into this
    /// document, so objects shared between several imported source pages (fonts, images,
    /// colour spaces) are imported only once. See <see cref="ImportDict"/>.
    /// </summary>
    internal Dictionary<int, int> GetSharedImportCloneMap(PdfReader sourceReader)
    {
        _importCloneMaps ??= new Dictionary<PdfReader, Dictionary<int, int>>();
        if (!_importCloneMaps.TryGetValue(sourceReader, out var map))
        {
            map = new Dictionary<int, int>();
            _importCloneMaps[sourceReader] = map;
        }
        return map;
    }

    /// <summary>
    /// Deep-clone a PdfDictionary, recursively cloning all referenced objects.
    /// Indirect references from the source document are resolved and inlined.
    /// </summary>
    private PdfDictionary DeepCloneDict(PdfDictionary source, PdfReader sourceReader,
        Dictionary<int, int> cloneMap)
    {
        var result = new PdfDictionary();
        foreach (var key in source.Keys)
        {
            var value = source.Get(key);
            result.Set(key, DeepCloneObject(value, sourceReader, cloneMap));
        }
        return result;
    }

    /// <summary>
    /// Deep-clone a PdfDictionary, excluding specified keys.
    /// </summary>
    private PdfDictionary DeepCloneDictExcluding(PdfDictionary source, PdfReader sourceReader,
        Dictionary<int, int> cloneMap, params string[] excludeKeys)
    {
        var excludeSet = new HashSet<string>(excludeKeys, StringComparer.Ordinal);
        var result = new PdfDictionary();
        foreach (var key in source.Keys)
        {
            if (excludeSet.Contains(key)) continue;
            var value = source.Get(key);
            result.Set(key, DeepCloneObject(value, sourceReader, cloneMap));
        }
        return result;
    }

    private PdfObject DeepCloneObject(PdfObject? obj, PdfReader sourceReader,
        Dictionary<int, int> cloneMap)
    {
        if (obj is null or PdfNull) return PdfNull.Instance;

        if (obj is PdfIndirectRef iref)
        {
            // Check if we already cloned this object
            if (cloneMap.TryGetValue(iref.ObjectNumber, out var mappedObjNum))
                return new PdfIndirectRef(mappedObjNum, 0);

            // Resolve in source and deep-clone
            var resolved = sourceReader.Resolve(iref);
            if (resolved is null) return PdfNull.Instance;

            // Allocate a new object number in this document and reserve it in both
            // the clone map (so shared/cyclic references reuse it) and the new-object
            // list (so AllocateObjectNumber accounts for it during the recursive clone
            // of this object's children — otherwise a nested reference would be handed
            // the very same number, producing a self-referential object).
            var newObjNum = AllocateObjectNumber();
            cloneMap[iref.ObjectNumber] = newObjNum;
            var slot = _newObjects.Count;
            _newObjects.Add((newObjNum, PdfNull.Instance));

            var cloned = DeepCloneObject(resolved, sourceReader, cloneMap);
            _newObjects[slot] = (newObjNum, cloned);
            // Make the cloned object resolvable immediately, not just at save time.
            // Imported resources (e.g. a PdfPageStamp's fonts) are referenced by these
            // new object numbers; without an overlay registration the reader can't resolve
            // them while rendering the in-memory target document, so stamped text vanishes
            // The overlay carries the same number written at save.
            _reader.RegisterOverlayObject(newObjNum, cloned);
            return new PdfIndirectRef(newObjNum, 0);
        }

        if (obj is PdfStream stream)
        {
            var clonedDict = DeepCloneDict(stream.Dict, sourceReader, cloneMap);
            // Copy raw stream data
            return new PdfStream(clonedDict, stream.RawData);
        }

        if (obj is PdfDictionary dict)
        {
            return DeepCloneDict(dict, sourceReader, cloneMap);
        }

        if (obj is PdfArray arr)
        {
            var result = new PdfArray();
            foreach (var item in arr)
                result.Add(DeepCloneObject(item, sourceReader, cloneMap));
            return result;
        }

        // Primitive types (PdfInteger, PdfReal, PdfString, PdfName, PdfBoolean) — no cloning needed
        return obj;
    }

    /// <summary>
    /// Copy the logical-structure (/StructTreeRoot) elements of <paramref name="source"/>
    /// into this document's structure tree, under a single merged Document element so a
    /// concatenated tagged PDF keeps one structure root. Marked-content / page references
    /// are dropped (the element tree itself is preserved for tagging/reading tools); used
    /// by <c>PdfFileEditor.Concatenate</c> when <c>CopyLogicalStructure</c> is set.
    /// </summary>
    internal void MergeLogicalStructure(Document source)
    {
        if (source is null) return;
        var srcRoot = source._reader.ResolveDict(source._reader.Catalog.Get("StructTreeRoot"));
        if (srcRoot is null) return;
        var srcKids = ResolveStructKids(srcRoot, source._reader);
        if (srcKids.Count == 0) return;

        // Ensure this document has a /StructTreeRoot whose /K holds one Document
        // element that everything merged is appended under.
        var destRoot = _reader.ResolveDict(_reader.Catalog.Get("StructTreeRoot"));
        if (destRoot is null)
        {
            destRoot = new PdfDictionary();
            destRoot.Set("Type", new PdfName("StructTreeRoot"));
            _reader.Catalog.Set("StructTreeRoot", destRoot);
        }
        var rootK = _reader.Resolve(destRoot.Get("K")) as PdfArray;
        if (rootK is null) { rootK = new PdfArray(); destRoot.Set("K", rootK); }

        PdfDictionary mergedDoc;
        if (rootK.Count > 0 && _reader.ResolveDict(rootK[0]) is { } existing
            && existing.GetName("S") == "Document")
        {
            mergedDoc = existing;
        }
        else
        {
            mergedDoc = new PdfDictionary();
            mergedDoc.Set("Type", new PdfName("StructElem"));
            mergedDoc.Set("S", new PdfName("Document"));
            rootK.Insert(0, mergedDoc);
            // Remember that THIS merge created the container: later sources of the
            // same concatenate must not treat it as the destination's own tree.
            _syntheticMergedStructDoc = mergedDoc;
        }
        var mergedK = mergedDoc.Get("K") as PdfArray;
        if (mergedK is null) { mergedK = new PdfArray(); mergedDoc.Set("K", mergedK); }

        // Where the source subtrees land depends on what the merged Document IS.
        // An untagged destination gets a synthetic container and every source keeps
        // its own root beneath it — N tagged sources contribute their full subtrees.
        // A destination that carried its OWN Document element instead absorbs a
        // source whose root is also /S Document by grafting that root's CHILDREN —
        // the reader sees one document tree, not a document nested in a document.
        // Non-Document source roots (a bare Caption, a Part) always append whole.
        var collapseDocRoots = !ReferenceEquals(mergedDoc, _syntheticMergedStructDoc);
        foreach (var kid in srcKids)
        {
            if (collapseDocRoots && kid.GetName("S") == "Document")
            {
                foreach (var grand in ResolveStructKids(kid, source._reader))
                    mergedK.Add(CloneStructElem(grand, source._reader));
            }
            else
                mergedK.Add(CloneStructElem(kid, source._reader));
        }
    }

    // The synthetic /S Document container MergeLogicalStructure creates for an
    // untagged destination — distinguishes it from a destination's own pre-existing
    // Document element across the per-source merge calls of one concatenate.
    private PdfDictionary? _syntheticMergedStructDoc;

    /// <summary>Resolve the structure-element kids (/K) of a structure dictionary.</summary>
    private static System.Collections.Generic.List<PdfDictionary> ResolveStructKids(
        PdfDictionary structDict, PdfReader reader)
    {
        var result = new System.Collections.Generic.List<PdfDictionary>();
        var k = reader.Resolve(structDict.Get("K"));
        if (k is PdfArray arr)
        {
            foreach (var item in arr)
                if (reader.ResolveDict(item) is { } d && d.GetName("Type") is null or "StructElem")
                    result.Add(d);
        }
        else if (k is PdfDictionary single && single.GetName("Type") is null or "StructElem")
        {
            result.Add(single);
        }
        return result;
    }

    /// <summary>Deep-clone a structure element (and its element children) as a direct
    /// (inline) dictionary, copying primitive attributes (/S, /ActualText, ...) and
    /// dropping page / marked-content references. Emitting inline dicts avoids any
    /// object-number interaction with the page-copy step.</summary>
    private PdfDictionary CloneStructElem(PdfDictionary src, PdfReader srcReader)
    {
        var clone = new PdfDictionary();
        foreach (var key in src.Keys)
        {
            if (key is "Pg" or "P" or "K") continue;
            var v = src.Get(key);
            if (v is PdfName or PdfString or PdfInteger or PdfReal or PdfBoolean)
                clone.Set(key, v!);
        }
        var children = new PdfArray();
        foreach (var childDict in ResolveStructKids(src, srcReader))
            children.Add(CloneStructElem(childDict, srcReader));
        if (children.Count > 0) clone.Set("K", children);
        return clone;
    }

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

    /// <summary>
    /// Get or create viewer preferences for this document.
    /// </summary>
    public ViewerPreferences GetOrCreateViewerPreferences()
    {
        var existing = ViewerPreferences;
        if (existing is not null) return existing;

        var dict = new PdfDictionary();
        _reader.Catalog.Set("ViewerPreferences", dict);
        return new ViewerPreferences(dict, _reader.Catalog);
    }

    private bool GetViewerPrefBool(string key) =>
        _reader.ResolveDict(_reader.Catalog.Get("ViewerPreferences"))?.Get(key) is PdfBoolean b && b.Value;

    private void SetViewerPrefBool(string key, bool value)
    {
        var prefs = _reader.ResolveDict(_reader.Catalog.Get("ViewerPreferences"));
        if (prefs is null)
        {
            prefs = new PdfDictionary();
            _reader.Catalog.Set("ViewerPreferences", prefs);
        }
        prefs.Set(key, value ? PdfBoolean.True : PdfBoolean.False);
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

    private void WriteViewerPrefName(string key, string? value)
    {
        var dict = _reader.ResolveDict(_reader.Catalog.Get("ViewerPreferences"));
        if (dict is null)
        {
            if (value is null) return;
            dict = new PdfDictionary();
            _reader.Catalog.Set("ViewerPreferences", dict);
        }
        if (value is null) dict.Remove(key);
        else dict.Set(key, new PdfName(value));
    }

    private static string PdfStringToHex(PdfString s)
    {
        var bytes = s.Value;
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.AppendFormat("{0:X2}", b);
        return sb.ToString();
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

    /// <summary>Whether the document has a structure tree.</summary>
    public bool HasStructTree => _reader.Catalog.ContainsKey("StructTreeRoot");

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
    /// Remove a form field by name from all pages and the AcroForm.
    /// Returns true if the field was found and removed.
    /// </summary>
    public bool RemoveFormField(string fieldName)
    {
        if (!HasForm) return false;

        // Collect ALL fields with this fully-qualified name (the PDF spec
        // permits duplicate-named siblings — every match is removed).
        var targets = new List<Forms.Field>();
        foreach (var f in Form.Fields)
        {
            if (string.Equals(f.FullName, fieldName, StringComparison.Ordinal))
                targets.Add(f);
        }
        if (targets.Count == 0) return false;

        var targetDicts = new HashSet<PdfDictionary>(targets.Select(t => t.Dict));

        // Helper: a dict is a "target" if it IS one of the matched field dicts,
        // or its /T equals fieldName, or any of its /Parent ancestors are targets.
        bool IsTargetField(PdfDictionary? dict)
        {
            if (dict is null) return false;
            if (targetDicts.Contains(dict)) return true;
            if (MatchesFieldName(dict, fieldName)) return true;
            // Walk Parent chain
            var cur = dict;
            for (int hop = 0; hop < 16; hop++)
            {
                var parent = _reader.ResolveDict(cur.Get("Parent"));
                if (parent is null) return false;
                if (targetDicts.Contains(parent)) return true;
                if (MatchesFieldName(parent, fieldName)) return true;
                cur = parent;
            }
            return false;
        }

        // Remove widget annotation from all pages
        foreach (var page in Pages)
        {
            var annots = _reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
            if (annots is null) continue;

            var remaining = new PdfArray();
            foreach (var annotRef in annots)
            {
                var annotDict = _reader.ResolveDict(annotRef);
                if (IsTargetField(annotDict))
                    continue;
                remaining.Add(annotRef);
            }

            if (remaining.Count < annots.Count)
            {
                if (remaining.Count > 0)
                    page.Dict.Set("Annots", remaining);
                else
                    page.Dict.Remove("Annots");
            }
        }

        // Remove from AcroForm/Fields array
        var acroForm = _reader.ResolveDict(Catalog.Get("AcroForm"));
        if (acroForm is not null)
        {
            var fields = _reader.Resolve(acroForm.Get("Fields")) as PdfArray;
            if (fields is not null)
            {
                var newFields = new PdfArray();
                foreach (var fRef in fields)
                {
                    var fDict = _reader.ResolveDict(fRef);
                    if (IsTargetField(fDict))
                        continue;
                    newFields.Add(fRef);
                }
                acroForm.Set("Fields", newFields);
            }
        }

        // Reset cached form
        _form = null;
        return true;
    }

    private bool MatchesFieldName(PdfDictionary dict, string fieldName)
    {
        var t = dict.Get("T");
        if (t is PdfString s) return s.ToText() == fieldName;
        if (t is PdfName n) return n.Value == fieldName;
        return false;
    }

    /// <summary>
    /// Merge multiple PDF documents into one.
    /// </summary>
    private bool _generatedFormFieldsRegistered;

    /// <summary>Walk each page's generator paragraph tree (including table cells and
    /// floating boxes), find any form fields placed there, and register them in the
    /// document's AcroForm so they persist as interactive fields. Radio groups are
    /// reached through their option fields' back-reference.</summary>
    private void RegisterGeneratedFormFields()
    {
        if (_generatedFormFieldsRegistered) return;
        _generatedFormFieldsRegistered = true;

        var already = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (_reader.ResolveDict(_reader.Catalog.Get("AcroForm")) is { } af
            && _reader.Resolve(af.Get("Fields")) is PdfArray existing)
            foreach (var item in existing)
                if (_reader.ResolveDict(item) is { } d) already.Add(d);

        var seenRadios = new HashSet<Forms.RadioButtonField>();
        for (var pi = 0; pi < Pages.Count; pi++)
        {
            var fields = new List<Forms.Field>();
            var radios = new List<Forms.RadioButtonField>();
            CollectGeneratorFormFields(Pages[pi + 1].Paragraphs, fields, radios);
            foreach (var f in fields)
                if (!already.Contains(f.Dict)) { Form.Add(f, pi + 1); already.Add(f.Dict); }
            foreach (var rb in radios)
                if (seenRadios.Add(rb) && !already.Contains(rb.Dict)) { Form.Add(rb, pi + 1); already.Add(rb.Dict); }
        }
    }

    private static void CollectGeneratorFormFields(IEnumerable<BaseParagraph> paragraphs,
        List<Forms.Field> fields, List<Forms.RadioButtonField> radios)
    {
        foreach (var p in paragraphs)
        {
            switch (p)
            {
                // RadioButtonOptionField is now a RadioButtonField, so its cases MUST precede
                // both the RadioButtonField and the general Field case: an option registers
                // its OWNER radio group (not itself) in the AcroForm.
                case Forms.RadioButtonOptionField { OwnerRadio: { } owner }: radios.Add(owner); break;
                case Forms.RadioButtonOptionField: break;
                case Forms.RadioButtonField rb: radios.Add(rb); break;
                case Forms.Field f: fields.Add(f); break;
                case Table t:
                    foreach (var row in t.Rows)
                        foreach (var cell in row.Cells)
                            CollectGeneratorFormFields(cell.Paragraphs, fields, radios);
                    break;
                case FloatingBox fb:
                    CollectGeneratorFormFields(fb.Paragraphs, fields, radios);
                    break;
            }
        }
    }

    /// <summary>Collect every <see cref="Heading"/> across the document whose
    /// <see cref="Heading.TocPage"/> is <paramref name="tocPage"/>, paired with the
    /// 1-based destination page number shown in its TOC entry. A heading qualifies
    /// when it is flagged <see cref="Heading.IsInList"/> (added to a content page
    /// and listed in the TOC) or when it sits directly on the TOC page itself
    /// (the entries are authored on the TOC page). The destination number is the
    /// heading's <see cref="Heading.DestinationPage"/> index when set, otherwise
    /// the index of the page the heading sits on.</summary>
    /// <summary>Advance the per-level auto-sequence counters for a content
    /// heading and return its printed number prefix ("2  ", "b.a  ", …).
    /// The number is hierarchical: every ancestor level's counter joined with
    /// '.', each part formatted in THIS heading's style — this prints
    /// "b.a" for a LettersLowercase level-2 under the second level-1 heading.
    /// The DEFAULT style (None) still numbers in arabic, and the number is
    /// followed by TWO spaces (the standard prefix fragment). A level-N
    /// bump restarts the deeper sequences. StartNumber seeds a level's FIRST
    /// use only — a later heading continues the running sequence (printing
    /// "ii" for the second roman heading even when it asks
    /// for StartNumber=13).</summary>
    private static string NextHeadingPrefix(Dictionary<int, int> counters, Heading heading)
    {
        if (!heading.IsAutoSequence) return "";
        var lvl = heading.Level > 0 ? heading.Level : 1;
        var next = (counters.TryGetValue(lvl, out var c) ? c : heading.StartNumber - 1) + 1;
        counters[lvl] = next;
        var stale = new List<int>();
        foreach (var k in counters.Keys) if (k > lvl) stale.Add(k);
        foreach (var k in stale) counters.Remove(k);
        var style = heading.Style == NumberingStyle.None ? NumberingStyle.Decimal : heading.Style;
        var parts = new List<string>();
        for (var k = 1; k <= lvl; k++)
            if (counters.TryGetValue(k, out var ck) && ck > 0)
                parts.Add(Heading.FormatNumber(style, ck));
        return parts.Count > 0 ? string.Join(".", parts) + "  " : "";
    }

    /// <summary>Measure TOC entry text with real Helvetica advances (a crude
    /// half-em estimate over-measures typical entry text and wraps lines that
    /// should stay whole, pushing the page number down a line). CJK
    /// ideographs / kana / fullwidth forms are full-width (1 em) in the CJK
    /// face substituted for them — measuring them as '?'
    /// under-counts the line and mis-sizes the dot leader.</summary>
    private static double MeasureEntry(string s, double fs, string face = "Helvetica")
    {
        double w = 0;
        foreach (var c in s)
        {
            double cw;
            if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFF00 && c <= 0xFF60))
                cw = 1000;
            else
                cw = Text.Standard14Fonts.GetWidth(face, c < 256 ? c : '?');
            if (cw < 0) cw = 500;
            w += cw * fs / 1000.0;
        }
        return w;
    }

    /// <summary>True when the text carries CJK ideographs / kana / fullwidth
    /// forms (the same ranges <see cref="MeasureEntry"/> treats as full-width).</summary>
    private static bool ContainsCjkText(string s)
    {
        foreach (var c in s)
            if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0x3000 && c <= 0x303F))
                return true;
        return false;
    }

    /// <summary>Standard-14 Helvetica variant for a TOC level format's font style.</summary>
    private static string EntryFace(Text.FontStyles style) => style switch
    {
        Text.FontStyles.Bold => "Helvetica-Bold",
        Text.FontStyles.Italic => "Helvetica-Oblique",
        Text.FontStyles.Bold | Text.FontStyles.Italic => "Helvetica-BoldOblique",
        _ => "Helvetica",
    };

    private System.Collections.Generic.List<(Heading h, int pageIdx)> CollectTocHeadings(Page tocPage)
    {
        var tocIdx = Pages.IndexOf(tocPage);
        var result = new System.Collections.Generic.List<(Heading, int)>();
        for (var pi = 1; pi <= PageCount; pi++)
            CollectTocHeadingsFrom(Pages.At(pi).Paragraphs, tocPage, pi, pi == tocIdx, result);
        return result;
    }

    private void CollectTocHeadingsFrom(System.Collections.Generic.IEnumerable<BaseParagraph> paragraphs,
        Page tocPage, int pageIdx, bool isTocPage, System.Collections.Generic.List<(Heading, int)> result)
    {
        foreach (var p in paragraphs)
        {
            if (p is Heading h && ReferenceEquals(h.TocPage, tocPage) && (h.IsInList || isTocPage))
            {
                var destIdx = h.DestinationPage is not null ? Pages.IndexOf(h.DestinationPage) : pageIdx;
                if (destIdx <= 0) destIdx = pageIdx;
                result.Add((h, destIdx));
            }
            else if (p is FloatingBox fb)
                CollectTocHeadingsFrom(fb.Paragraphs, tocPage, pageIdx, isTocPage, result);
        }
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

    /// <summary>Whether <paramref name="p"/> can join an inline-chained line, and
    /// the single-line text it contributes. Only simple runs join: an unpositioned,
    /// footnote-free, Standard-14 TextFragment, or an HtmlFragment whose content
    /// strips to plain single-line text (no tables, images or vector markup) —
    /// that one renders in the serif HTML body face.</summary>
    private static bool InlineJoinable(BaseParagraph p, out string text, out bool serif)
    {
        text = string.Empty;
        serif = false;
        if (p is Text.TextFragment f)
        {
            if (f.HasExplicitPosition || f.FootNote is not null) return false;
            if (f.TextState.FontData is not null || f.TextState.Font?.SourceFontData is not null)
                return false;
            if (f.HyperlinkValue is not null) return false;
            text = f.Text ?? string.Empty;
            return text.Length > 0 && text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0;
        }
        if (p is HtmlFragment h)
        {
            var content = h.HtmlContent ?? string.Empty;
            if (content.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0
                || content.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0
                || content.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0
                || content.IndexOf("<br", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            text = HtmlFragment.StripHtmlTags(content).Trim();
            serif = true;
            return text.Length > 0 && text.IndexOf('\n') < 0;
        }
        return false;
    }

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
        try { return Text.FontRepository.FindFont(family, ignoreCase: true); }
        catch { return null; }
    }

    /// <summary>Resolve a family in a specific style (bold/italic) through the repository,
    /// swallowing lookup failures. Returns null when the styled variant is unavailable.</summary>
    private static Text.Font? SafeFindFontStyled(string family, Text.FontStyles style)
    {
        try { return Text.FontRepository.FindFont(family, style, ignoreCase: true); }
        catch { return null; }
    }

    /// <summary>The CSS "normal" line height (pt) for an embedded face at a
    /// point size: the OS/2 win line-box height quantized to whole CSS pixels, recovering
    /// half the leading when the pixel round truncated down (exact across
    /// 6-22pt for Verdana/Calibri). Zero when the metrics are unreadable.</summary>
    private static double HtmlNormalLineHeightPt(byte[]? ttf, double sizePt)
    {
        if (ttf is null || ttf.Length < 12) return 0;
        try
        {
            var tp = new Text.TrueTypeParser(ttf);
            tp.Parse();
            if (tp.UsWinAscent <= 0 || tp.UnitsPerEm <= 0) return 0;
            double upm = tp.UnitsPerEm, winSum = tp.UsWinAscent + tp.UsWinDescent;
            var px = sizePt * 96.0 / 72.0;
            var rawPx = winSum * px / upm;
            var rpx = Math.Round(rawPx, MidpointRounding.AwayFromZero);
            var pitchPx = rpx + Math.Max(0, rawPx - rpx) / 2;
            return pitchPx * 0.75;
        }
        catch { return 0; }
    }

    /// <summary>TJ adjustment array (thousandths of text space; positive pulls the following
    /// glyphs left) for the pair-kerning of <paramref name="s"/> under <paramref name="gp"/>,
    /// or null when no pair kerns.</summary>
    private static double[]? StepKernAdjustments(string s, Text.GlyphOutlineParser gp)
    {
        if (s.Length < 2) return null;
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0;
        double[]? adj = null;
        var prev = -1;
        for (var i = 0; i < s.Length; i++)
        {
            var gid = gp.CMap.TryGetValue(s[i], out var g) ? g : 0;
            if (prev >= 0)
            {
                var kern = gp.GetKernAdjustment(prev, gid);
                if (kern != 0)
                {
                    adj ??= new double[s.Length - 1];
                    adj[i - 1] = -kern * 1000.0 / upm;
                }
            }
            prev = gid;
        }
        return adj;
    }

    /// <summary>Render a recognised step-list (a single <c>ul</c> whose items nest heading /
    /// paragraph blocks) with browser-style HTML layout: embedded serif faces,
    /// pixel-quantized CSS line boxes, UA block margins, pair-kerned runs split at inline-bold
    /// edges, and a real bullet marker. All metrics exact to 4 decimals.
    /// Returns false without emitting anything when the serif faces are unavailable, the flow
    /// has already page-broken, or the list does not fit the remaining page space — the caller
    /// then falls back to the legacy flat flow.</summary>
    private static bool RenderHtmlStepList(List<Converters.HtmlToPdfConverter.StepListItem> items,
        FlowLayout flow, double marginLeft, double marginRight, Color? htmlColor)
    {
        if (flow.HasOverflowed) return false;
        byte[]? regTtf, boldTtf;
        try
        {
            regTtf = Text.FontRepository.GetTtfData("Times New Roman");
            boldTtf = Text.FontRepository.GetTtfData("Times New Roman Bold");
        }
        catch { return false; }
        if (regTtf is null || boldTtf is null) return false;

        Text.TrueTypeParser tp;
        Text.GlyphOutlineParser gpReg, gpBold;
        try
        {
            tp = new Text.TrueTypeParser(regTtf);
            tp.Parse();
            gpReg = new Text.GlyphOutlineParser(regTtf);
            gpBold = new Text.GlyphOutlineParser(boldTtf);
        }
        catch { return false; }
        if (tp.UnitsPerEm <= 0 || tp.UsWinAscent <= 0) return false;

        double upm = tp.UnitsPerEm, winAsc = tp.UsWinAscent, winDesc = tp.UsWinDescent;
        var hheaSum = tp.Ascent + System.Math.Abs(tp.Descent) + tp.LineGap;
        const double em = 12.0;                    // HTML default body size (16px)

        // CSS "normal" line box: the hhea line height rounds to whole CSS pixels; the
        // baseline sits winAscent + half the surplus leading below the box top.
        double Pitch(double s) => 0.75 * System.Math.Floor(hheaSum * (s * 96.0 / 72.0) / upm + 0.5);
        double Asc(double s) => winAsc * s / upm + (Pitch(s) - (winAsc + winDesc) * s / upm) / 2;

        // UA defaults: heading sizes and margins in em of the base size. The margin
        // resolves from the exact CSS size while Tf/metrics carry the 3-decimal-truncated
        // value (a float-formatting quirk — visually inert, kept for fidelity).
        (double size, double margin, bool bold) TagStyle(string tag)
        {
            var (factor, marginEm) = tag switch
            {
                "h1" => (2.0, 0.67),
                "h2" => (1.5, 0.75),
                "h3" => (1.17, 0.83),
                _ => (1.0, 0.0),
            };
            var css = factor * em;
            return (System.Math.Floor(css * 1000.0) / 1000.0, marginEm * css, tag is "h1" or "h2" or "h3");
        }

        int Gid(char c, bool bold)
        {
            var gp = bold ? gpBold : gpReg;
            return gp.CMap.TryGetValue(c, out var g) ? g : 0;
        }
        double GlyphW(int gid, bool bold, double s)
        {
            var gp = bold ? gpBold : gpReg;
            var u = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0;
            return gp.GetAdvanceWidth(gid) * s / u;
        }
        double KernW(int prev, int cur, bool bold, double s)
        {
            var gp = bold ? gpBold : gpReg;
            var u = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0;
            return gp.GetKernAdjustment(prev, cur) * s / u;
        }

        // Greedy space-break wrap with pair kerning (IsBreakWords=false semantics: a word
        // never splits; an overlong word overflows). The space a line breaks at is dropped;
        // all other spaces stay in-string, so a run boundary keeps its inter-word space.
        List<List<(char c, bool bold)>> Wrap(List<(char c, bool bold)> stream, double size, double maxW)
        {
            var lines = new List<List<(char c, bool bold)>>();
            var line = new List<(char c, bool bold)>();
            var w = 0.0;
            var prevGid = -1;
            var prevBold = false;

            (double w, int endGid, bool endBold) Measure(int from, int to, int pg, bool pb)
            {
                var mw = 0.0;
                for (var k = from; k < to; k++)
                {
                    var (c, bl) = stream[k];
                    var gid = Gid(c, bl);
                    if (pg >= 0 && pb == bl) mw += KernW(pg, gid, bl, size);
                    mw += GlyphW(gid, bl, size);
                    pg = gid;
                    pb = bl;
                }
                return (mw, pg, pb);
            }

            var i = 0;
            while (i < stream.Count)
            {
                // One wrap token: the pending space (if any) plus the following word.
                var j = i + (stream[i].c == ' ' ? 1 : 0);
                while (j < stream.Count && stream[j].c != ' ') j++;
                var (extW, endGid, endBold) = Measure(i, j, prevGid, prevBold);
                if (line.Count > 0 && w + extW > maxW + 1e-9)
                {
                    lines.Add(line);
                    line = new List<(char c, bool bold)>();
                    (w, prevGid, prevBold) = (0, -1, false);
                    var from = stream[i].c == ' ' ? i + 1 : i;
                    if (from < j)
                    {
                        (w, prevGid, prevBold) = Measure(from, j, -1, false);
                        for (var k = from; k < j; k++) line.Add(stream[k]);
                    }
                }
                else
                {
                    for (var k = i; k < j; k++) line.Add(stream[k]);
                    w += extW;
                    prevGid = endGid;
                    prevBold = endBold;
                }
                i = j;
            }
            if (line.Count > 0) lines.Add(line);
            return lines;
        }

        // ---- Lay the whole list out (top-down distances from the flow cursor) ----
        var pageW = flow.CurrentPage.Width;
        var ulMargin = 1.12 * em;                 // ul top/bottom margin
        var liLeft = marginLeft + 30.0;           // ul default padding-left: 40px per level

        var runsOut = new List<(double yDown, double x, string text, bool bold, double size)>();
        var bulletsOut = new List<(double yDown, double x)>();
        var yDown = 0.0;
        var pendingMargin = ulMargin;             // collapses (max) with the next block's own

        foreach (var item in items)
        {
            var textLeft = liLeft + item.PadLeftPt;
            var maxW = pageW - marginRight - textLeft;
            var liFirstLine = true;
            foreach (var block in item.Blocks)
            {
                var (size, margin, boldTag) = TagStyle(block.Tag);
                var stream = new List<(char c, bool bold)>();
                foreach (var r in block.Runs)
                    foreach (var ch in r.Text)
                        stream.Add((ch, boldTag || r.Bold));
                if (stream.Count == 0) continue;

                var lines = Wrap(stream, size, maxW);
                var pitch = Pitch(size);
                yDown += System.Math.Max(pendingMargin, margin);
                var baseline = yDown + Asc(size);
                foreach (var ln in lines)
                {
                    if (liFirstLine)
                    {
                        var bAdv = GlyphW(Gid('•', false), false, em);
                        bulletsOut.Add((baseline, liLeft - 0.375 * em - bAdv));
                        liFirstLine = false;
                    }
                    var x = textLeft;
                    var gi = 0;
                    while (gi < ln.Count)
                    {
                        var runBold = ln[gi].bold;
                        var sb = new System.Text.StringBuilder();
                        var runW = 0.0;
                        var pg = -1;
                        while (gi < ln.Count && ln[gi].bold == runBold)
                        {
                            var gid = Gid(ln[gi].c, runBold);
                            if (pg >= 0) runW += KernW(pg, gid, runBold, size);
                            runW += GlyphW(gid, runBold, size);
                            sb.Append(ln[gi].c);
                            pg = gid;
                            gi++;
                        }
                        runsOut.Add((baseline, x, sb.ToString(), runBold, size));
                        x += runW;
                    }
                    baseline += pitch;
                }
                yDown += lines.Count * pitch;
                pendingMargin = margin;
            }
        }
        var totalH = yDown + ulMargin;
        if (runsOut.Count == 0 || flow.CurrentY - totalH < flow.BottomMargin) return false;

        // ---- Emit: bullet markers + text runs as embedded Type0 serif ----
        var csb = new Content.ContentStreamBuilder();
        if (htmlColor is not null) csb.SetFillColor(htmlColor);
        var fontDict = Table.ResolvePageFontDict(flow.CurrentPage);

        void EmitRun(string text, bool bold, double size, double x, double yAbs)
        {
            var (res, hex) = Text.Type0FontEmbedder.Embed(fontDict,
                bold ? boldTtf : regTtf,
                bold ? "Times New Roman Bold" : "Times New Roman",
                text, stripSpacesInBaseFont: true);
            csb.BeginText();
            csb.SetFont(res, size);
            csb.MoveTextPosition(x, yAbs);
            if (StepKernAdjustments(text, bold ? gpBold : gpReg) is { } adj)
                csb.ShowTextHexKerned(hex, adj);
            else
                csb.ShowTextHex(hex);
            csb.EndText();
        }

        foreach (var (by, bx) in bulletsOut)
            EmitRun("•", false, em, bx, flow.CurrentY - by);
        foreach (var (ry, rx, text, bold, size) in runsOut)
            EmitRun(text, bold, size, rx, flow.CurrentY - ry);

        flow.InjectContentAtCursor(csb.Build());
        flow.AdvanceY(totalH);
        return true;
    }

    private static void ShapeArabicForGenerator(Text.TextFragment tf)
    {
        if (!Text.ArabicTextShaper.ContainsArabic(tf.Text)) return;
        var font = Text.FontRepository.FindFont("Arial");
        if (font?.SourceFontData?.TtfData is null) return;

        // Collect each segment's display text (shape Arabic segments to visual order; keep
        // non-Arabic segments as-is) and the effective size. Segment-level bidi: a fragment
        // whose first content segment is Arabic lays out right-to-left, so the segments are
        // emitted in reverse order (each segment is treated as a directional unit,
        // which keeps e.g. a leading "." attached to its Latin segment rather than migrating
        // to the adjacent Arabic run as a per-character bidi pass would).
        var size = tf.TextState.FontSize;
        var displays = new List<string>();
        var firstArabic = (bool?)null;
        if (tf.Segments is { Count: > 0 })
        {
            foreach (var s in tf.Segments)
            {
                if (string.IsNullOrEmpty(s.Text)) continue;
                var arabic = Text.ArabicTextShaper.ContainsArabic(s.Text);
                firstArabic ??= arabic;
                if (s.TextState.FontSize > 0 && size <= 0) size = s.TextState.FontSize;
                displays.Add(arabic ? Text.ArabicTextShaper.Shape(s.Text) : s.Text);
            }
        }
        if (displays.Count == 0) displays.Add(Text.ArabicTextShaper.Shape(tf.Text));
        if (firstArabic == true) displays.Reverse();

        tf.TextState.Font = font;
        if (size > 0) tf.TextState.FontSize = size;
        tf.Text = string.Concat(displays);
    }
}

/// <summary>
/// Document-level JavaScript scripts (PDF spec §12.6.4.16). Walks the
    /// /Names/JavaScript name tree in the document catalog and exposes
    /// scripts by name.
    /// </summary>
    public sealed class JavaScriptCollection
{
        private readonly Document _doc;
        private List<string>? _keys;
        private Dictionary<string, string>? _scripts;

        internal JavaScriptCollection(Document doc) => _doc = doc;

        /// <summary>Script names in lexical order.</summary>
        public IList<string> Keys
        {
            get
            {
                EnsureLoaded();
                return _keys!;
            }
        }

        /// <summary>
        /// Get or set the JavaScript source for the named script. Setting
        /// null is equivalent to <see cref="Remove"/>.
        /// </summary>
        public string? this[string name]
        {
            get
            {
                EnsureLoaded();
                return _scripts!.TryGetValue(name, out var src) ? src : null;
            }
            set
            {
                if (value is null) { Remove(name); return; }
                EnsureLoaded();
                _scripts![name] = value;
                if (!_keys!.Contains(name))
                {
                    var insertAt = _keys!.Count;
                    for (var i = 0; i < _keys.Count; i++)
                    {
                        if (string.CompareOrdinal(_keys[i], name) > 0) { insertAt = i; break; }
                    }
                    _keys.Insert(insertAt, name);
                }
                WriteBack();
            }
        }

        /// <summary>Remove the named JavaScript entry. Returns true if it existed.</summary>
        public bool Remove(string key)
        {
            EnsureLoaded();
            if (!_scripts!.Remove(key)) return false;
            _keys!.Remove(key);
            WriteBack();
            return true;
        }

        private void EnsureLoaded()
        {
            if (_keys is not null) return;
            _keys = new List<string>();
            _scripts = new Dictionary<string, string>(StringComparer.Ordinal);
            var reader = _doc.Reader;
            var catalog = reader.Catalog;
            var names = reader.ResolveDict(catalog.Get("Names"));
            if (names is null) return;
            var jsTree = reader.ResolveDict(names.Get("JavaScript"));
            if (jsTree is null) return;
            CollectFromNameTree(jsTree, reader);
        }

        private void CollectFromNameTree(Aspose.Pdf.Core.PdfDictionary node, Aspose.Pdf.IO.PdfReader reader)
        {
            // Either /Names array (leaf) or /Kids array (intermediate).
            var namesArr = reader.Resolve(node.Get("Names")) as Aspose.Pdf.Core.PdfArray;
            if (namesArr is not null)
            {
                for (int i = 0; i + 1 < namesArr.Count; i += 2)
                {
                    var key = (reader.Resolve(namesArr[i]) as Aspose.Pdf.Core.PdfString)?.ToText();
                    if (key is null) continue;
                    var actionDict = reader.ResolveDict(namesArr[i + 1]);
                    var jsObj = actionDict is null ? null : reader.Resolve(actionDict.Get("JS"));
                    string? src = jsObj switch
                    {
                        Aspose.Pdf.Core.PdfString s => s.ToText(),
                        Aspose.Pdf.Core.PdfStream st => System.Text.Encoding.UTF8.GetString(reader.DecodeStream(st)),
                        _ => null,
                    };
                    _keys!.Add(key);
                    if (src is not null) _scripts![key] = src;
                }
            }
            var kidsArr = reader.Resolve(node.Get("Kids")) as Aspose.Pdf.Core.PdfArray;
            if (kidsArr is not null)
            {
                foreach (var kid in kidsArr)
                {
                    var kidDict = reader.ResolveDict(kid);
                    if (kidDict is not null) CollectFromNameTree(kidDict, reader);
                }
            }
        }

        private void WriteBack()
        {
            var reader = _doc.Reader;
            var catalog = reader.Catalog;
            var names = reader.ResolveDict(catalog.Get("Names"));
            if (names is null)
            {
                names = new Aspose.Pdf.Core.PdfDictionary();
                catalog.Set("Names", names);
            }
            if (_scripts!.Count == 0)
            {
                names.Remove("JavaScript");
                return;
            }
            // Flat /Names array, lexically ordered (PDF 32000-1 § 7.9.6). Use
            // inline action dicts (rather than indirect refs to newly-allocated
            // objects) so a subsequent in-process EnsureLoaded — which reads
            // through the reader's xref — can still resolve them; new objects
            // aren't visible to PdfReader.Resolve until Save runs.
            var arr = new Aspose.Pdf.Core.PdfArray();
            foreach (var key in _keys!)
            {
                var actionDict = new Aspose.Pdf.Core.PdfDictionary();
                actionDict.Set("Type", new Aspose.Pdf.Core.PdfName("Action"));
                actionDict.Set("S", new Aspose.Pdf.Core.PdfName("JavaScript"));
                actionDict.Set("JS", new Aspose.Pdf.Core.PdfString(
                    System.Text.Encoding.Latin1.GetBytes(_scripts![key])));
                arr.Add(new Aspose.Pdf.Core.PdfString(System.Text.Encoding.Latin1.GetBytes(key)));
                arr.Add(actionDict);
            }
            var jsTree = new Aspose.Pdf.Core.PdfDictionary();
            jsTree.Set("Names", arr);
            names.Set("JavaScript", jsTree);
        }
    }
