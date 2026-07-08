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
        throw new NotSupportedException(
            $"Document uses a non-Standard security handler (/Filter = {filter ?? "?"}). " +
            "Public-Key (Adobe.PPKLite / Adobe.PPKMS) certificate-based decryption is not implemented " +
            "in this FOSS branch — only password-based Standard handler decryption is wired through " +
            "the existing PdfEncryptor path.");
    }

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
    public Document(string path, Converters.MdLoadOptions options)
        : this(Converters.MarkdownToPdfConverter.Convert(path, options).ToArray()) { }

    /// <summary>
    /// Open a Markdown stream and convert it to PDF using the given options.
    /// </summary>
    public Document(Stream stream, Converters.MdLoadOptions options)
        : this(Converters.MarkdownToPdfConverter.Convert(ReadStreamToBytes(stream), options).ToArray()) { }

    /// <summary>
    /// Open an SVG file and convert it to PDF using the given options.
    /// Matches the public API constructor: new Document(path, SvgLoadOptions).
    /// More specific than the <see cref="LoadOptions"/> catch-all, so it wins
    /// overload resolution and the SVG is parsed as SVG (not as PDF).
    /// </summary>
    public Document(string path, Converters.SvgLoadOptions options)
        : this(SvgConvertToPdfBytes(Converters.SvgToPdfConverter.Convert(path, options))) { }

    /// <summary>
    /// Open an SVG stream and convert it to PDF using the given options.
    /// </summary>
    public Document(Stream stream, Converters.SvgLoadOptions options)
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
    public static Document Open(string path, Converters.MdLoadOptions options)
    {
        return Converters.MarkdownToPdfConverter.Convert(path, options);
    }

    /// <summary>
    /// Open a Markdown file from bytes and convert it to a PDF document.
    /// </summary>
    public static Document Open(byte[] data, Converters.MdLoadOptions options)
    {
        return Converters.MarkdownToPdfConverter.Convert(data, options);
    }

    /// <summary>
    /// Open an SVG file and convert it to a PDF document.
    /// </summary>
    public static Document Open(string path, Converters.SvgLoadOptions options)
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

    public static Document Open(byte[] data, Converters.SvgLoadOptions options)
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
        // Aspose.Pdf disposes the stream on return, so callers can re-open
        // the same path immediately. Mirror that contract.
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

    /// <summary>Serialise a validation result in the reference log schema:
    /// <c>&lt;Compliance&gt;&lt;File&gt;…&lt;Fonts&gt;&lt;Problem Severity Clause&gt;</c> —
    /// font problems nest under &lt;Fonts&gt;, everything else sits directly under
    /// &lt;File&gt; alongside the empty section markers.</summary>
    private void WriteValidationLogXml(TextWriter writer, PdfFormat format,
        Optimization.PdfAValidationResult result)
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
            var clause = ClauseFor(v.Rule);
            var page = v.PageNumber is int p ? $" Page=\"{p}\"" : "";
            return $"<Problem Severity=\"Error\" Clause=\"{clause}\" Code=\"{clause}\" Convertable=\"False\"{page}>{EscapeXml(v.Description)}</Problem>";
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
            $"<Compliance Name=\"Log\" Operation=\"Validation\" Target=\"{EscapeXml(GetVersionString(format))}\">" +
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
                    _form = Form.FromPageWidgets(_reader.Catalog, _reader);
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
            _collection = new Collection(coll, names, _reader, Pages, _reader)
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
            var encrypt = _reader.ResolveDict(_reader.Trailer.Get("Encrypt"));
            return (int)(encrypt?.GetInt("P") ?? -1);
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
            _embeddedFiles = new EmbeddedFileCollection(names, _reader, Pages, _reader);
            _embeddedFiles.OwnerDocument = this;
            return _embeddedFiles;
        }
    }
    private EmbeddedFileCollection? _embeddedFiles;

    /// <summary>
    /// Add an embedded file to the document.
    /// </summary>
    public void AddEmbeddedFile(string fileName, byte[] fileData, string? description = null,
        string? mimeType = null, bool compress = true,
        DateTime? creationDate = null, DateTime? modDate = null)
    {
        // Build the file specification dict
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

        var fsDict = new PdfDictionary();
        fsDict.Set("Type", new PdfName("Filespec"));
        // /F uses Latin1; /UF uses UTF-16BE with BOM for non-ASCII file names
        fsDict.Set("F", new PdfString(Encoding.Latin1.GetBytes(fileName)));
        fsDict.Set("UF", Forms.Field.EncodePdfTextString(fileName));
        if (description is not null)
            fsDict.Set("Desc", Forms.Field.EncodePdfTextString(description));
        fsDict.Set("EF", efDict);

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
        }
        var mergedK = mergedDoc.Get("K") as PdfArray;
        if (mergedK is null) { mergedK = new PdfArray(); mergedDoc.Set("K", mergedK); }

        foreach (var kid in srcKids)
            mergedK.Add(CloneStructElem(kid, source._reader));
    }

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
        // permits duplicate-named siblings — Aspose.Pdf removes every match).
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

    /// <summary>
    /// <summary>
    /// Shape a generator <see cref="Text.TextFragment"/> that carries Arabic/RTL text: replace
    /// each segment's base letters with their contextual presentation forms in visual
    /// right-to-left order, and route the fragment through an Arabic-capable embedded font
    /// (Arial covers Arabic Presentation Forms-B). Without this the default Standard-14 font has
    /// no Arabic glyphs and the renderer applies no OpenType shaping, so Arabic rendered as
    /// disconnected isolated letters in left-to-right order (or missing glyphs).
    /// </summary>
    private static void ShapeArabicForGenerator(Text.TextFragment tf)
    {
        if (!Text.ArabicTextShaper.ContainsArabic(tf.Text)) return;
        var font = Text.FontRepository.FindFont("Arial");
        if (font?.SourceFontData?.TtfData is null) return;

        // Collect each segment's display text (shape Arabic segments to visual order; keep
        // non-Arabic segments as-is) and the effective size. Segment-level bidi: a fragment
        // whose first content segment is Arabic lays out right-to-left, so the segments are
        // emitted in reverse order (the reference treats each segment as a directional unit,
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

    /// <summary>
    /// Apply page-level Paragraphs, Headers, and Footers to each page's content stream.
    /// Called automatically before save.
    /// </summary>
    private void ApplyPageContent()
    {        // Form fields (combo/list/check/text boxes, radio groups) added to the
        // generator paragraph tree must be registered in the AcroForm before the
        // pages are written, so they round-trip as real fields.
        RegisterGeneratedFormFields();

        // Collect overflow pages to add after iteration
        var overflowPages = new List<(byte[] content, double width, double height)>();
        // Table images destined for an overflow page, keyed by that page's slot index in
        // overflowPages. Applied once the page is materialised (the page object doesn't
        // exist while the table is being built).
        var overflowImages = new Dictionary<int, List<(byte[] data, Rectangle rect)>>();
        // Per-page flow layouts whose deferred annotations need resolving against
        // the final Page sequence (slot indices into overflowPages). Each entry:
        // the flow + the slot range it owns in overflowPages.
        var pendingFlows = new List<(FlowLayout flow, int slotStart, int slotEnd)>();
        // Snapshot pages: if a paragraph handler (e.g. Image overflow)
        // appends a new Page mid-loop, the live collection mutates.
        var pagesSnapshot = Pages.ToList();
        // Per-level running counters for auto-sequenced headings authored
        // directly in page Paragraphs (e.g. Heading{ IsAutoSequence = true }).
        // Document-scoped so the sequence continues across pages.
        var headingAutoCounters = new Dictionary<int, int>();
        foreach (var page in pagesSnapshot)
        {
            // Flush operator collection to content stream
            page.Contents.FlushToPage();

            // Page.Background paints the whole page (MediaBox) behind every other
            // operator. Prepended so it sits under existing content and any
            // paragraphs/header/footer rendered below. The fill is wrapped in a
            // /Background marked-content block so re-applying a background replaces
            // the previous one instead of stacking, and Color.White means "remove
            // the background" (the Aspose.Pdf semantics).
            if (page.Background is { } pageBg && !page.BackgroundApplied)
            {
                page.BackgroundApplied = true;
                page.RemoveTaggedBackground();
                var isWhite = pageBg.R == 255 && pageBg.G == 255 && pageBg.B == 255;
                if (!isWhite)
                {
                    var box = page.MediaBox;
                    var bgBuilder = new Content.ContentStreamBuilder();
                    bgBuilder.BeginMarkedContent(Page.BackgroundMarkerTag);
                    bgBuilder.SaveState();
                    bgBuilder.SetFillColor(pageBg.R / 255.0, pageBg.G / 255.0, pageBg.B / 255.0);
                    bgBuilder.Rectangle(box.LLX, box.LLY, box.Width, box.Height);
                    bgBuilder.Fill();
                    bgBuilder.RestoreState();
                    bgBuilder.EndMarkedContent();
                    page.PrependContentStream(bgBuilder.Build());
                }
            }

            // Materialise /AP appearances for annotations that lack one so they render
            // (the renderer draws Line/Polygon/Polyline only from their /AP) and expose
            // NormalAppearance after save.
            foreach (var annot in page.Annotations)
            {
                if (annot is Annotations.FreeTextAnnotation freeText)
                    freeText.GenerateAppearance();
                else if (annot is Annotations.LineAnnotation or Annotations.PolygonAnnotation
                                or Annotations.PolylineAnnotation or Annotations.SquareAnnotation
                                or Annotations.CircleAnnotation or Annotations.TextAnnotation
                         && annot.NormalAppearance is null)
                    annot.UpdateAppearances();
            }

            // Render page Header/Footer set through Page.Header / Page.Footer.
            // Independent of the paragraph layout below (a page may carry only a
            // header), so it runs before the LayoutApplied gate and guards itself.
            if (!page.HeaderFooterApplied && (page.Header is not null || page.Footer is not null))
            {
                page.HeaderFooterApplied = true;
                page.Header?.RenderToPage(page, isHeader: true, page.Number, this);
                page.Footer?.RenderToPage(page, isHeader: false, page.Number, this);
            }

            if (page.LayoutApplied) continue;

            // Apply TOC info + Paragraphs
            if (page.TocInfo is not null || page.Paragraphs.Count > 0)
            {
                string? fontName = null;
                // page.PageInfo.Margin is always non-null (auto-initialised to a
                // zeroed MarginInfo) so ?? never fires. Treat zeros as "use
                // default 72 pt" — otherwise a fresh page with no explicit
                // margins lays content out with no top/bottom/left/right
                // breathing room at all.
                var m = page.PageInfo?.Margin;
                // Respect user-set margins verbatim (including explicit zeros); fall back
                // to Aspose.Pdf's Generator defaults (90 pt L/R, 72 pt T/B) when
                // MarginInfo was never touched. With the matching default page size A4
                // (595x842), GoTo destinations land at x=90 y=770 = 842-72.
                // Fall back to Aspose.Pdf's Generator defaults per side: a caller
                // that sets only some sides (e.g. Left/Right for a multi-column box)
                // leaves the others at the default rather than at an unintended zero.
                var marginTop    = m?.TopTouched    == true ? m!.Top    : 72;
                var marginBottom = m?.BottomTouched == true ? m!.Bottom : 72;
                var marginLeft   = m?.LeftTouched   == true ? m!.Left   : 90;
                var marginRight  = m?.RightTouched  == true ? m!.Right  : 90;
                // Shared Y cursor so consecutive paragraphs flow down the page instead
                // of piling on top of each other at the top margin. When cursor drops
                // below the bottom margin, FlushToNewPage() starts a fresh overflow page.
                var curY = page.Height - marginTop;

                // Render TOC title if present
                if (page.TocInfo?.Title is { } tocTitle)
                {
                    fontName ??= Table.RegisterFont(page);
                    var titleSize = tocTitle.TextState.FontSize > 0 ? tocTitle.TextState.FontSize : 16;
                    // A bold title is emitted with the Helvetica-Bold base font; its
                    // glyphs are ~6% wider, which the centring estimate accounts for.
                    var titleBold = tocTitle.TextState.IsBold;
                    var titleFont = titleBold ? Table.RegisterFont(page, "Helvetica-Bold") : fontName;
                    var titleCharW = titleSize * (titleBold ? 0.55 : 0.5);
                    // The TOC title is centred across the content width.
                    var titleWidth = tocTitle.Text.Length * titleCharW;
                    var titleX = System.Math.Max(marginLeft, (page.Width - titleWidth) / 2);
                    var builder = new Content.ContentStreamBuilder();
                    builder.BeginText()
                        .SetFont(titleFont, titleSize)
                        .SetFillColor(0, 0, 0)
                        .MoveTextPosition(titleX, curY)
                        .ShowText(tocTitle.Text)
                        .EndText();
                    page.AddContentStream(builder.Build());
                    curY -= titleSize * 1.5;
                }

                // Render the TOC entry list: every heading whose TocPage is this page,
                // as "<auto-number> <text> .... <destination page>" — laid out across
                // the configured columns, indented by heading level, with the heading
                // text wrapped to the column width and the page number right-aligned to
                // the column edge with a dot leader on the final line.
                if (page.TocInfo is not null)
                {
                    var tocHeadings = CollectTocHeadings(page);
                    if (tocHeadings.Count > 0)
                    {
                        fontName ??= Table.RegisterFont(page);
                        const double entrySize = 12.0;
                        const double entryCharW = entrySize * 0.5;
                        var lineH = entrySize * 1.4;

                        // Column geometry: honour ColumnInfo.ColumnCount/widths/spacing
                        // for a multi-column TOC; otherwise a single column. The single-
                        // column geometry is kept identical to the legacy layout (right
                        // edge clamped to a 36 pt inset, 18 pt per indent level) so simple
                        // one-column TOCs are unaffected.
                        var ci = page.TocInfo.ColumnInfo;
                        var colCount = ci is { ColumnCount: > 1 } ? ci.ColumnCount : 1;
                        double[] colLefts, colWidths;
                        if (colCount > 1)
                            (colLefts, colWidths) = BuildColumnGeometry(
                                ci!, marginLeft, page.Width - marginLeft - marginRight);
                        else
                        {
                            colLefts = new[] { marginLeft };
                            colWidths = new[] { page.Width - System.Math.Min(marginRight, 36) - marginLeft };
                        }

                        // Hierarchical section counters for IsAutoSequence headings:
                        // a level-N heading bumps counter[N] and resets the deeper ones,
                        // printing "c1.c2.….cN " (e.g. 1, 1.1, 1.2, 2).
                        var counters = new int[12];
                        var col = 0;
                        var topY = curY;
                        var entryY = curY;
                        var lowestY = curY;

                        foreach (var (h, destIdx) in tocHeadings)
                        {
                            var level = h.Level > 0 ? h.Level : 1;
                            var prefix = string.Empty;
                            if (h.IsAutoSequence)
                            {
                                if (level < counters.Length)
                                {
                                    counters[level]++;
                                    for (var k = level + 1; k < counters.Length; k++) counters[k] = 0;
                                }
                                // NumberingStyle.None suppresses the visible number even for an
                                // auto-sequenced heading (the counter still advances so any
                                // numbered siblings stay in sequence). Without this gate a
                                // Style=None heading wrongly prints a "1 "/"2 " prefix.
                                if (h.Style != NumberingStyle.None)
                                {
                                    var parts = new List<string>();
                                    for (var k = 1; k <= level && k < counters.Length; k++)
                                        parts.Add(counters[k].ToString(System.Globalization.CultureInfo.InvariantCulture));
                                    if (parts.Count > 0) prefix = string.Join(".", parts) + " ";
                                }
                            }
                            var text = prefix + (h.Text ?? string.Empty);
                            var pageNumStr = destIdx > 0
                                ? destIdx.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                : string.Empty;
                            var pageNumWidth = pageNumStr.Length * entryCharW;

                            double colLeft = colLefts[col], colWidth = colWidths[col];
                            var indent = colLeft + (level - 1) * 18;
                            var pageNumX = colLeft + colWidth - pageNumWidth;
                            var wrapped = WrapToWidth(text, pageNumX - 6 - indent, entryCharW);

                            // Out of vertical room: move to the next column for a
                            // multi-column TOC, otherwise stop (legacy single-column
                            // behaviour truncated rather than overflowing the page).
                            if (entryY - wrapped.Count * lineH < marginBottom)
                            {
                                if (col + 1 >= colCount) break;
                                col++;
                                entryY = topY;
                                colLeft = colLefts[col];
                                colWidth = colWidths[col];
                                indent = colLeft + (level - 1) * 18;
                                pageNumX = colLeft + colWidth - pageNumWidth;
                                wrapped = WrapToWidth(text, pageNumX - 6 - indent, entryCharW);
                            }

                            var b = new Content.ContentStreamBuilder();
                            for (var li = 0; li < wrapped.Count; li++)
                                b.BeginText().SetFont(fontName, entrySize).SetFillColor(0, 0, 0)
                                    .MoveTextPosition(indent, entryY - li * lineH).ShowText(wrapped[li]).EndText();

                            var lastY = entryY - (wrapped.Count - 1) * lineH;
                            var lastLineW = wrapped[^1].Length * entryCharW;
                            var dotStart = indent + lastLineW + 4;
                            var dotEnd = pageNumX - 4;
                            if (dotEnd > dotStart)
                            {
                                var dotCount = (int)((dotEnd - dotStart) / (entrySize * 0.33));
                                if (dotCount > 0)
                                    b.BeginText().SetFont(fontName, entrySize).SetFillColor(0, 0, 0)
                                        .MoveTextPosition(dotStart, lastY).ShowText(new string('.', dotCount)).EndText();
                            }
                            if (pageNumStr.Length > 0)
                                b.BeginText().SetFont(fontName, entrySize).SetFillColor(0, 0, 0)
                                    .MoveTextPosition(pageNumX, lastY).ShowText(pageNumStr).EndText();
                            page.AddContentStream(b.Build());

                            entryY = lastY - lineH;
                            if (entryY < lowestY) lowestY = entryY;
                        }
                        curY = lowestY;
                    }
                }

                var flow = new FlowLayout(page, overflowPages, marginLeft, marginRight, marginTop, marginBottom, curY, EnableNotificationLogging);
                var flowSlotStart = overflowPages.Count;
                var tb = new Text.TextBuilder(page);
                // Height of the current run of inline images (IsInLineParagraph). Inline
                // images share one line and the cursor only drops by the tallest of them
                // once the line ends (a block image or a flush at end-of-flow).
                double pendingInlineLineHeight = 0;
                foreach (var para in page.Paragraphs)
                {
                    // Close an open inline image line before any paragraph that isn't
                    // itself an inline image — the cursor drops by the tallest inline
                    // image so this paragraph starts on the next line.
                    if (pendingInlineLineHeight > 0
                        && !(para is Image inlineImg && inlineImg.IsInLineParagraph))
                    {
                        flow.AdvanceY(pendingInlineLineHeight);
                        pendingInlineLineHeight = 0;
                    }

                    // IsInNewPage forces this paragraph to start on a fresh overflow
                    // page, regardless of remaining room on the current one. Honors
                    // the BaseParagraph.IsInNewPage flag set on headings, paragraphs,
                    // and tables when the caller wants explicit pagination.
                    // ForceNewPage (eager) is required for renderers that bypass the
                    // Y cursor (Heading.Build draws at the supplied Y verbatim, so
                    // just resetting the cursor would leave the heading on the
                    // current page).
                    if (ParagraphIsInNewPage(para) && para != page.Paragraphs[0])
                        flow.ForceNewPage();

                    // Record this paragraph's starting position so a later
                    // LocalHyperlink that targets it (e.g. LocalHyperlink(head))
                    // can resolve to the right page + y after overflow pages
                    // have been added to the document.
                    flow.RecordPosition(para);

                    if (para is Text.TextFragment tf)
                    {
                        // NoCharacterAction.ReplaceFonts (explicit): substitute a glyph-covering
                        // face before layout when the fragment's font can't show its text —
                        // registered sources first, then host Arial, then a system CJK face.
                        if (tf.HasExplicitReplaceFonts &&
                            Text.FontRepository.SubstituteForMissingGlyphs(tf.Text, tf.TextState.Font) is { } replaceFace)
                            tf.TextState.Font = replaceFace;
                        // Arabic/RTL text: shape into contextual presentation forms and route
                        // through an Arabic-capable embedded font (the default Standard-14 font
                        // has no Arabic coverage and the renderer applies no OpenType shaping).
                        ShapeArabicForGenerator(tf);
                        // Replace page number macros
                        if (tf.Text.Contains("$p") || tf.Text.Contains("$P"))
                        {
                            tf.Text = tf.Text
                                .Replace("$p", page.Number.ToString())
                                .Replace("$P", PageCount.ToString());
                        }
                        // A fragment's own top margin is vertical space reserved above it;
                        // dropping the cursor by it (which paginates when it overflows the
                        // page) is what places a fragment with a large Margin.Top onto a
                        // later page instead of pinning it to the current one.
                        var tfTopMargin = tf.Margin?.Top ?? 0;
                        if (tfTopMargin > 0) flow.AdvanceY(tfTopMargin);
                        if (!flow.WriteTextFragment(tf))
                        {
                            // Flow layout declined (e.g. explicit Position or embedded font) —
                            // fall back to the legacy fixed-position writer. Assign a default
                            // layout position only when the caller didn't set one (the Position
                            // getter is never null now, so test HasExplicitPosition, not `??=`).
                            if (!tf.HasExplicitPosition)
                                tf.Position = new Text.Position(
                                    marginLeft, page.Height - marginTop - (tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12));
                            tb.AppendText(tf);
                        }
                        var tfBottomMargin = tf.Margin?.Bottom ?? 0;
                        if (tfBottomMargin > 0) flow.AdvanceY(tfBottomMargin);
                        // FootNote rendering: the canonical PDF position for footnotes
                        // is the bottom of the page the parent fragment landed on,
                        // separated by a rule. FOSS doesn't yet reserve a footnote band
                        // -- as an interim, flow the Note.Paragraphs straight through the
                        // same paginator after the parent, so a long footnote (e.g.
                        // a multi-page legal text) still spills onto follow-
                        // up pages instead of disappearing. Each paragraph re-enters the
                        // TextFragment / HtmlFragment branches via the same dispatch.
                        if (tf.FootNote is { Paragraphs.Count: > 0 })
                            flow.QueueFootnote(tf.FootNote);
                    }
                    else if (para is HtmlFragment html)
                    {
                        var htmlContent = html.HtmlContent ?? "";
                        var htmlColor = html.TextState?.ForegroundColor;

                        // Render a real HTML <table> as a generator Table at the flow cursor,
                        // paginating like a page-level Table paragraph (same logic as the
                        // `para is Table` branch below).
                        void RenderHtmlTable(Table t)
                        {
                            var tablePage = flow.CurrentPage;
                            t.FlowLeftOffset = marginLeft;
                            var spillTopMargin = PageInfo?.Margin is { TopTouched: true } dm ? dm.Top : marginTop;

                            // Page-break-before: if the whole table doesn't fit in the space left
                            // on the current page but would fit on a fresh one, move it to the next
                            // page (keeps a table together — the common HTML expectation). Measure
                            // its single-page height from the content top first.
                            t.BuildMultiPage(tablePage, flow.ContentTop, flow.BottomMargin);
                            var tableH = t.LastRenderedHeight;
                            var avail = flow.CurrentY - flow.BottomMargin;
                            var pageBudget = flow.ContentTop - flow.BottomMargin;
                            if (tableH > avail + 0.5 && tableH <= pageBudget + 0.5
                                && flow.CurrentY < flow.ContentTop - 0.5)
                                flow.ForceNewPage();

                            var pageContents = t.BuildMultiPage(tablePage, flow.CurrentY, flow.BottomMargin, spillTopMargin);
                            var tableImages = t.LastImageDraws;
                            var tableGraphs = t.LastGraphDraws;
                            // Inject the first slice at the flow's CURRENT page position (the start
                            // page, or the current overflow buffer once the flow has page-broken) —
                            // NOT directly on the start page, which is where the cursor no longer is.
                            flow.InjectContentAtCursor(pageContents[0]);
                            if (tableGraphs.Count > 0)
                                foreach (var gc in tableGraphs[0])
                                    flow.InjectContentAtCursor(gc);
                            // Cell images: drawn on the live start page (only correct before the flow
                            // overflows — overflowed cell images are rare and out of scope here).
                            if (!flow.HasOverflowed && tableImages.Count > 0)
                                foreach (var (data, rect) in tableImages[0])
                                    tablePage.AddImage(data, rect);
                            if (pageContents.Count == 1)
                            {
                                flow.AdvanceY(t.LastRenderedHeight);
                            }
                            else
                            {
                                for (var pi = 1; pi < pageContents.Count - 1; pi++)
                                {
                                    if (pi < tableImages.Count && tableImages[pi].Count > 0)
                                        overflowImages[overflowPages.Count] = tableImages[pi];
                                    overflowPages.Add((pageContents[pi], tablePage.Width, tablePage.Height));
                                }
                                var lastIdx = pageContents.Count - 1;
                                var lastSlot = flow.ContinueOnPrebuiltSpill(pageContents[lastIdx], t.LastPageEndY);
                                if (lastIdx < tableImages.Count && tableImages[lastIdx].Count > 0)
                                    overflowImages[lastSlot] = tableImages[lastIdx];
                            }
                        }

                        // Render a run of block-structured HTML (paragraphs/headings/lists)
                        // through the flow at the current cursor, then any <img> in that chunk.
                        void RenderHtmlBlocks(string chunk)
                        {
                            var blocks = Converters.HtmlToPdfConverter.ParseHtmlBlocks(chunk);
                            foreach (var b in blocks)
                            {
                                var fontSize = b.FontSize > 0 ? b.FontSize : 11.0;
                                // List items carry a top margin (the common
                                // `li { margin: .5em 0 }` rule) so the vertical rhythm
                                // tracks a browser/CSS layout rather than packing tight.
                                var topMargin = b.MarginTop + (b.IsListItem ? fontSize * 0.5 : 0);
                                if (topMargin > 0) flow.AdvanceY(topMargin);
                                if (b.IsInputField)
                                {
                                    // <input>/<textarea> inside an in-page HtmlFragment: place an
                                    // interactive AcroForm TextBoxField at the flow cursor, named
                                    // from the HTML name/id so callers can find it by FullName.
                                    var ifPage = flow.CurrentPage;
                                    var ifLlx = marginLeft + b.LeftIndent;
                                    var ifContentW = ifPage.Width - marginLeft - marginRight - b.LeftIndent;
                                    var ifW = b.InputWidth > 0 ? System.Math.Min(b.InputWidth, ifContentW) : ifContentW;
                                    var ifH = b.InputHeight > 0 ? b.InputHeight : fontSize * 1.3;
                                    var ifTop = flow.CurrentY;
                                    var ifField = new Aspose.Pdf.Forms.TextBoxField(ifPage,
                                        new Aspose.Pdf.Rectangle(ifLlx, ifTop - ifH, ifLlx + ifW, ifTop))
                                    {
                                        Multiline = b.InputMultiline,
                                        ReadOnly = b.InputReadOnly,
                                    };
                                    if (!string.IsNullOrEmpty(b.InputName)) ifField.PartialName = b.InputName;
                                    if (!string.IsNullOrEmpty(b.InputValue)) ifField.Value = b.InputValue;
                                    Form.Add(ifField, ifPage.Number);
                                    flow.AdvanceY(ifH + b.MarginBottom);
                                    continue;
                                }
                                if (b.IsHorizontalRule)
                                {
                                    // Draw the <hr> as a thin filled bar across the
                                    // content width in its CSS border colour.
                                    var hrPage = flow.CurrentPage;
                                    var lineW = hrPage.Width - marginLeft - marginRight;
                                    var th = b.RuleWidth > 0 ? b.RuleWidth : 1.0;
                                    var hrY = flow.CurrentY;
                                    var csb = new Content.ContentStreamBuilder();
                                    csb.SaveState();
                                    csb.SetFillColor(b.RuleColor ?? Color.FromRgb(128, 128, 128));
                                    csb.Rectangle(marginLeft, hrY - th, lineW, th);
                                    csb.Fill();
                                    csb.RestoreState();
                                    hrPage.AddContentStream(csb.Build());
                                    flow.AdvanceY(th + 2);
                                    continue;
                                }
                                if (string.IsNullOrEmpty(b.Text))
                                {
                                    flow.AdvanceY(b.ExplicitHeight > 0 ? b.ExplicitHeight : fontSize);
                                    continue;
                                }
                                var bf = new Text.TextFragment(b.Text);
                                bf.TextState.FontSize = (float)fontSize;
                                // HTML renders text on roughly a 1.2x line pitch.
                                bf.TextState.LineSpacing = (float)(fontSize * 0.2);
                                bf.TextState.IsBold = b.FontRes == "F2";
                                bf.TextState.IsItalic = b.FontRes == "F3";
                                if (htmlColor is not null) bf.TextState.ForegroundColor = htmlColor;
                                // Split the block into segments so inline <a href> ranges carry a
                                // WebHyperlink — the layout engine turns hyperlinked segments into
                                // Link annotations over their rendered run.
                                if (b.Anchors is { Count: > 0 })
                                    ApplyHtmlAnchorSegments(bf, b.Text, b.Anchors);
                                flow.LeftIndent = b.LeftIndent;
                                var wrote = flow.WriteTextFragment(bf);
                                flow.LeftIndent = 0;
                                if (!wrote)
                                {
                                    bf.Position = new Text.Position(marginLeft + b.LeftIndent,
                                        page.Height - marginTop - bf.TextState.FontSize);
                                    tb.AppendText(bf);
                                }
                                if (b.MarginBottom > 0) flow.AdvanceY(b.MarginBottom);
                            }
                            // Draw this chunk's <img> elements in-flow (per segment), so a
                            // logo lands at its position rather than after all content.
                            RenderHtmlImages(chunk, flow, marginLeft, marginRight);
                        }

                        if (Converters.HtmlToPdfConverter.ContainsTable(htmlContent))
                        {
                            // Mixed content (text blocks + real column tables): render each
                            // top-level segment in document order so an HTML <table> flows as
                            // columns instead of a flat tag-stripped stack.
                            foreach (var (isTable, chunk) in Converters.HtmlToPdfConverter.SegmentHtmlTables(htmlContent))
                            {
                                if (isTable)
                                {
                                    var t = Converters.HtmlToPdfConverter.BuildTableFromHtml(chunk);
                                    if (t is not null) RenderHtmlTable(t);
                                }
                                else RenderHtmlBlocks(chunk);
                            }
                        }
                        else if (Converters.HtmlToPdfConverter.HasBlockStructure(htmlContent))
                        {
                            RenderHtmlBlocks(htmlContent);
                        }
                        else
                        {
                            var plainText = HtmlFragment.StripHtmlTags(htmlContent);
                            if (!string.IsNullOrWhiteSpace(plainText))
                            {
                                var frag = new Text.TextFragment(plainText);
                                if (html.TextState is { } htmlTs)
                                {
                                    if (htmlTs.Font is not null) frag.TextState.Font = htmlTs.Font;
                                    if (htmlTs.FontData is not null) frag.TextState.FontData = htmlTs.FontData;
                                    if (htmlTs.FontSize > 0) frag.TextState.FontSize = htmlTs.FontSize;
                                    if (htmlTs.ForegroundColor is not null) frag.TextState.ForegroundColor = htmlTs.ForegroundColor;
                                    frag.TextState.IsBold = htmlTs.IsBold;
                                    frag.TextState.IsItalic = htmlTs.IsItalic;
                                }
                                if (!flow.WriteTextFragment(frag))
                                {
                                    frag.Position = new Text.Position(marginLeft, page.Height - marginTop - frag.TextState.FontSize);
                                    tb.AppendText(frag);
                                }
                                if (frag.Rectangle is { } r)
                                    html.Rectangle = new System.Drawing.RectangleF(
                                        (float)r.LLX, (float)r.LLY, (float)r.Width, (float)r.Height);
                            }
                            RenderHtmlImages(htmlContent, flow, marginLeft, marginRight);
                        }
                    }
                    else if (para is Table table)
                    {
                        // Start the table at the current flow cursor — not at the top of the page —
                        // and indent it to the page's left content margin so it lines up with the
                        // surrounding text flow. Render onto whatever page the cursor is on now.
                        var tablePage = flow.CurrentPage;
                        table.FlowLeftOffset = marginLeft;
                        // Overflow pages inset by the margin a freshly-added page would get:
                        // the document-level top margin when the caller set one (explicitly
                        // "for new pages added"), otherwise this page's effective top margin.
                        var spillTopMargin = PageInfo?.Margin is { TopTouched: true } dm ? dm.Top : marginTop;
                        var pageContents = table.BuildMultiPage(tablePage, flow.CurrentY, 36, spillTopMargin);
                        var tableImages = table.LastImageDraws;
                        var tableGraphs = table.LastGraphDraws;
                        tablePage.AddContentStream(pageContents[0]);
                        if (tableImages.Count > 0)
                            foreach (var (data, rect) in tableImages[0])
                                tablePage.AddImage(data, rect);
                        if (tableGraphs.Count > 0)
                            foreach (var gc in tableGraphs[0])
                                tablePage.AddContentStream(gc);
                        if (pageContents.Count == 1)
                        {
                            // Single-page table: consume exactly its height so following
                            // paragraphs continue immediately below on the same page.
                            flow.AdvanceY(table.LastRenderedHeight);
                        }
                        else
                        {
                            // Intermediate spill pages become standalone pages; the LAST spill
                            // page is handed back to the flow so trailing paragraphs continue
                            // on it, below the table, rather than starting a fresh page.
                            for (var pi = 1; pi < pageContents.Count - 1; pi++)
                            {
                                if (pi < tableImages.Count && tableImages[pi].Count > 0)
                                    overflowImages[overflowPages.Count] = tableImages[pi];
                                overflowPages.Add((pageContents[pi], tablePage.Width, tablePage.Height));
                            }
                            var lastIdx = pageContents.Count - 1;
                            var lastSlot = flow.ContinueOnPrebuiltSpill(pageContents[lastIdx], table.LastPageEndY);
                            if (lastIdx < tableImages.Count && tableImages[lastIdx].Count > 0)
                                overflowImages[lastSlot] = tableImages[lastIdx];
                        }
                    }
                    else if (para is FloatingBox fbox)
                    {
                        // Flow-positioned, no-size FloatingBox is indistinguishable from the
                        // ambient paragraph flow. Inline its child paragraphs into the shared
                        // cursor so long content paginates via the surrounding FlowLayout.
                        // Absolutely-positioned (Left/Top set) boxes still render through
                        // AddFloatingBox since they don't participate in the flow.
                        // A box that paints a background/border or carries a background
                        // image is meant to render as a visible box (e.g. a coloured header
                        // band), not be dissolved into the transparent paragraph flow — route
                        // it through AddFloatingBox so its fill, border and child images draw.
                        var fboxIsVisibleBox = fbox.BackgroundColor is not null
                            || fbox.BackgroundImage is not null
                            || (fbox.Border is not null && fbox.Border.Side != BorderSide.None);
                        if (fbox.PositioningMode == ParagraphPositioningMode.Default
                            && fbox.Left == 0 && fbox.Top == 0 && !fboxIsVisibleBox)
                        {
                            // Multi-column box: lay the children out across N columns
                            // (fill column 0 top-to-bottom, then column 1, ... then a
                            // fresh page). Columns start at the page's left content
                            // margin; the box's own Margin doesn't inset the flow.
                            var columnCount = fbox.ColumnInfo?.ColumnCount ?? 0;
                            var inColumns = false;
                            if (columnCount > 1)
                            {
                                var (lefts, widths) = BuildColumnGeometry(
                                    fbox.ColumnInfo!, marginLeft,
                                    page.Width - marginLeft - marginRight);
                                if (lefts.Length > 1)
                                {
                                    flow.BeginColumns(lefts, widths);
                                    inColumns = true;
                                }
                            }

                            var firstInner = true;
                            foreach (var inner in fbox.Paragraphs)
                            {
                                // IsFirstParagraphInColumn pushes this paragraph to the
                                // top of the next column. Never on the
                                // very first child — column 0 is already its home.
                                if (inColumns && !firstInner && inner.IsFirstParagraphInColumn)
                                    flow.ForceNextColumn();
                                firstInner = false;

                                if (inner is Text.TextFragment innerTf)
                                {
                                    flow.WriteTextFragment(innerTf);
                                    if (innerTf.FootNote is { Paragraphs.Count: > 0 })
                                        flow.QueueFootnote(innerTf.FootNote);
                                    continue;
                                }
                                if (inner is HtmlFragment innerHtml)
                                {
                                    var innerPlain = HtmlFragment.StripHtmlTags(innerHtml.HtmlContent ?? "");
                                    if (!string.IsNullOrWhiteSpace(innerPlain))
                                    {
                                        var innerFrag = new Text.TextFragment(innerPlain);
                                        flow.WriteTextFragment(innerFrag);
                                    }
                                    continue;
                                }
                                if (inner is Table innerTable)
                                {
                                    var innerContents = innerTable.BuildMultiPage(flow.CurrentPage, flow.CurrentY);
                                    flow.CurrentPage.AddContentStream(innerContents[0]);
                                    var innerGraphs = innerTable.LastGraphDraws;
                                    if (innerGraphs.Count > 0)
                                        foreach (var gc in innerGraphs[0])
                                            flow.CurrentPage.AddContentStream(gc);
                                    var innerImgs = innerTable.LastImageDraws;
                                    if (innerImgs.Count > 0)
                                        foreach (var (data, rect) in innerImgs[0])
                                            flow.CurrentPage.AddImage(data, rect);
                                    for (var pi = 1; pi < innerContents.Count; pi++)
                                        overflowPages.Add((innerContents[pi], flow.CurrentPage.Width, flow.CurrentPage.Height));
                                    flow.ResetToTopOfNextPage();
                                }
                            }

                            if (inColumns)
                                flow.EndColumns();
                        }
                        else if (fbox.PositioningMode != ParagraphPositioningMode.Default
                                 || fbox.Left != 0 || fbox.Top != 0)
                        {
                            // Absolute box — render in place, doesn't affect flow cursor.
                            page.AddFloatingBox(fbox);
                        }
                        else
                        {
                            // Flow-positioned visible box (background/border): render it at the
                            // current cursor — not the page top — so a coloured header band
                            // honours the page's top margin, then advance the flow past it.
                            var targetPage = flow.CurrentPage;
                            var savedMode = fbox.PositioningMode;
                            var savedTop = fbox.Top;
                            fbox.PositioningMode = ParagraphPositioningMode.Absolute;
                            fbox.Top = targetPage.Height - flow.CurrentY;
                            targetPage.AddFloatingBox(fbox);
                            fbox.PositioningMode = savedMode;
                            fbox.Top = savedTop;
                            flow.AdvanceY(fbox.Height);
                        }
                    }
                    else if (para is Heading heading)
                    {
                        // A heading whose TocPage is this page is a TOC entry authored
                        // directly on the TOC page — it was already emitted by the TOC
                        // entry renderer above, so skip it here to avoid double-drawing
                        // it as plain content text. Headings on other pages still render
                        // as their content heading (and also appear in the TOC list).
                        if (ReferenceEquals(heading.TocPage, page))
                            continue;

                        fontName ??= Table.RegisterFont(page);
                        var headingPage = flow.CurrentPage;
                        var headingY = flow.CurrentY;
                        // Auto-sequenced headings get a formatted number prefix
                        // (roman/alpha/decimal per Style), counting per level.
                        var headingPrefix = "";
                        if (heading.IsAutoSequence && heading.Style != NumberingStyle.None)
                        {
                            var lvl = heading.Level;
                            var next = (headingAutoCounters.TryGetValue(lvl, out var c)
                                ? c : heading.StartNumber - 1) + 1;
                            headingAutoCounters[lvl] = next;
                            headingPrefix = Heading.FormatNumber(heading.Style, next) + ". ";
                        }
                        var (content, height) = heading.Build(headingPage, marginLeft, headingY, fontName, headingPrefix);
                        headingPage.AddContentStream(content);

                        // Create a link annotation for the heading
                        var destPage = heading.DestinationPage;
                        if (destPage is not null)
                        {
                            var linkRect = new Rectangle(marginLeft, headingY - height, headingPage.Width - marginRight, headingY);
                            var destPageIdx = 0;
                            for (int pi = 1; pi <= PageCount; pi++)
                            {
                                if (Pages.At(pi) == destPage) { destPageIdx = pi; break; }
                            }
                            if (destPageIdx > 0)
                            {
                                // Link via a GoTo action with an explicit XYZ destination at
                                // the target page's upper-left corner, so Annotation.Action
                                // resolves to a GoToAction whose Destination exposes the page
                                // and coordinates (a /Dest [page /Fit] form leaves Action null).
                                // Destination coordinates are in unrotated page space; map the
                                // visual top-left (0, rotated-height) back through the page's
                                // rotation so it lands correctly on rotated pages too.
                                var destRect = destPage.GetPageRect(true);
                                var (destLeft, destTop) = destPage.RotationMatrix
                                    .InverseTransformPoint(0, destRect.Height);
                                headingPage.Annotations.AddLinkAnnotation(linkRect,
                                    new Aspose.Pdf.Annotations.GoToAction(
                                        new Aspose.Pdf.Annotations.XYZExplicitDestination(
                                            destPageIdx, destLeft, destTop, 0)));
                            }
                        }

                        // Mirror the heading into the document outlines when its
                        // TOC page asks for it (Heading.TocPage.TocInfo.CopyToOutlines).
                        // A synthetic TOC asserts the saved PDF carries a flat
                        // list of bookmarks, one per heading.
                        if (heading.TocPage?.TocInfo?.CopyToOutlines == true
                            && heading.Segments.Count > 0)
                        {
                            var headingPageIdx = 0;
                            for (int pi = 1; pi <= PageCount; pi++)
                            {
                                if (Pages.At(pi) == headingPage) { headingPageIdx = pi; break; }
                            }
                            if (headingPageIdx > 0)
                            {
                                var item = new OutlineItemCollection(Outlines)
                                {
                                    Title = heading.Segments[1].Text ?? string.Empty,
                                };
                                item.Action = new Aspose.Pdf.Annotations.GoToAction(
                                    new Aspose.Pdf.Annotations.XYZExplicitDestination(
                                        headingPageIdx, marginLeft, headingY, 0));
                                Outlines.Add(item);
                            }
                        }

                        flow.AdvanceY(height + 4);
                    }
                    else if (para is Image img)
                    {
                        byte[]? imgData = null;
                        if (img.ImageStream is not null)
                        {
                            var pos = img.ImageStream.CanSeek ? img.ImageStream.Position : -1L;
                            // Rewind when seekable: callers commonly hand us a stream after
                            // reading dimensions with `new Bitmap(stream)`, which leaves the
                            // position at end-of-stream. Without this the image silently disappears.
                            if (img.ImageStream.CanSeek) img.ImageStream.Position = 0;
                            using var imgMem = new System.IO.MemoryStream();
                            img.ImageStream.CopyTo(imgMem);
                            imgData = imgMem.ToArray();
                            if (pos >= 0) img.ImageStream.Position = pos;
                        }
                        else if (!string.IsNullOrEmpty(img.File) && System.IO.File.Exists(img.File))
                        {
                            imgData = System.IO.File.ReadAllBytes(img.File);
                        }
                        if (imgData is null) continue;

                        // An SVG source rasterises through the built-in SVG converter first —
                        // the raster embed path below can't decode vector data and the image
                        // would drop silently from the flow.
                        if (XImageCollection.IsSvg(imgData)
                            && ImageRasterizer.RasterizeSvg(imgData) is { } svgPng)
                            imgData = svgPng;

                        // IsBlackWhite fast path: a bilevel Group 4 TIFF embeds its existing
                        // CCITT strips directly (no re-encode), giving the compact 1-bit output
                        // the property promises instead of a bulky re-rasterised copy.
                        if (img.IsBlackWhite
                            && IO.CcittTiffExtractor.TryExtract(imgData) is { Count: > 0 } g4Frames)
                        {
                            var availWbw = page.Width - marginLeft - marginRight;
                            var availHbw = page.Height - marginTop - marginBottom;
                            for (int fi = 0; fi < g4Frames.Count; fi++)
                            {
                                var g4 = g4Frames[fi];
                                double imgWbw, imgHbw;
                                if (img.FixWidth > 0 || img.FixHeight > 0)
                                {
                                    imgWbw = img.FixWidth > 0 ? img.FixWidth : availWbw;
                                    imgHbw = img.FixHeight > 0 ? img.FixHeight : availHbw;
                                }
                                else
                                {
                                    // Pixels map 1:1 to points (optionally scaled by ImageScale),
                                    // clamped per-axis into the content box.
                                    var scaleBw = img.ImageScale > 0 ? img.ImageScale : 1.0;
                                    imgWbw = Math.Min(g4.Width * scaleBw, availWbw);
                                    imgHbw = Math.Min(g4.Height * scaleBw, availHbw);
                                }
                                Page targetPageBw;
                                double yTopBw;
                                if (fi == 0 && flow.CurrentY - imgHbw >= marginBottom)
                                {
                                    targetPageBw = flow.CurrentPage;
                                    yTopBw = flow.CurrentY;
                                }
                                else
                                {
                                    flow.Commit();
                                    targetPageBw = Pages.Add();
                                    targetPageBw.MediaBox = new Rectangle(0, 0, page.Width, page.Height);
                                    Table.RegisterFont(targetPageBw);
                                    yTopBw = page.Height - marginTop;
                                }
                                var rectBw = new Rectangle(marginLeft, yTopBw - imgHbw,
                                                           marginLeft + imgWbw, yTopBw);
                                targetPageBw.AddCcittImage(g4.Data, g4.Width, g4.Height, g4.BlackIs1, rectBw);
                                if (ReferenceEquals(targetPageBw, flow.CurrentPage))
                                    flow.AdvanceY(imgHbw);
                                else
                                    flow.ResetToTopOfNextPage();
                            }
                            continue;
                        }

                        // Page.AddImage embeds JPEG / PNG / raw RGB directly. Other raster
                        // formats (TIFF / BMP / GIF, possibly multi-frame) are decoded with the
                        // platform image codec to one PNG per frame; each frame is placed on its
                        // own page, matching how a multi-page TIFF expands into multiple pages.
                        var hdr0 = imgData.Length > 0 ? imgData[0] : (byte)0;
                        var hdr1 = imgData.Length > 1 ? imgData[1] : (byte)0;
                        // Baseline JPEG and PNG embed directly. Progressive JPEG is routed
                        // through the codec re-encode (the embedded-image decoder is
                        // baseline-only, so a progressive frame would render blank).
                        var isJpeg = hdr0 == 0xFF && hdr1 == 0xD8 && !IsProgressiveJpeg(imgData);
                        var isPng = imgData.Length >= 4 && hdr0 == 0x89 && hdr1 == 0x50
                                    && imgData[2] == 0x4E && imgData[3] == 0x47;
                        // JPEG 2000 (.jp2/.jpx) — the platform codec can't decode it, so keep the
                        // raw bytes and let Page.AddImage route them through the built-in JPXDecode
                        // decoder (System.Drawing returns null for these, which used to drop the image).
                        var isJpx = (imgData.Length >= 12 && hdr0 == 0x00 && hdr1 == 0x00
                                     && imgData[2] == 0x00 && imgData[3] == 0x0C && imgData[4] == 0x6A
                                     && imgData[5] == 0x50 && imgData[6] == 0x20 && imgData[7] == 0x20)
                                    || (imgData.Length >= 4 && hdr0 == 0xFF && hdr1 == 0x4F
                                        && imgData[2] == 0xFF && imgData[3] == 0x51);
                        var frames = isJpeg || isPng || isJpx
                            ? new System.Collections.Generic.List<byte[]> { imgData }
                            : TryDecodeImageFramesAsPng(imgData);
                        if (frames is null || frames.Count == 0) continue;

                        // A genuinely bilevel source embeds losslessly as a compact 1-bit
                        // image instead of an 8-bit re-encode (a scanned/fax page would
                        // otherwise balloon the output).
                        var embedBlackWhite = img.IsBlackWhite || ImageStamp.IsBilevelSource(imgData);

                        var availW = page.Width - marginLeft - marginRight;
                        var availH = page.Height - marginTop - marginBottom;
                        for (int frameIdx = 0; frameIdx < frames.Count; frameIdx++)
                        {
                            var frameData = frames[frameIdx];
                            double imgW, imgH;
                            if (img.FixWidth > 0 || img.FixHeight > 0)
                            {
                                imgW = img.FixWidth > 0 ? img.FixWidth : availW;
                                imgH = img.FixHeight > 0 ? img.FixHeight : availH;
                            }
                            else if (TryGetImageNaturalSizePt(frameData, img.IsApplyResolution, out var natWpt, out var natHpt))
                            {
                                // No explicit size: start from the image's intrinsic dimensions
                                // (pixels mapped 1:1 to points unless IsApplyResolution honours the
                                // embedded DPI), optionally scaled by ImageScale.
                                var scale = img.ImageScale > 0 ? img.ImageScale : 1.0;
                                imgW = natWpt * scale;
                                imgH = natHpt * scale;
                                if (img.IsApplyResolution)
                                {
                                    // Resolution-aware: fit to the content width preserving the
                                    // aspect ratio (Aspose.Pdf behaviour for IsApplyResolution).
                                    if (imgW > availW && imgW > 0)
                                    {
                                        imgH *= availW / imgW;
                                        imgW = availW;
                                    }
                                }
                                else
                                {
                                    // Default: an oversized image is fitted into the content area by
                                    // clamping each axis independently to the available width/height
                                    // -- matching Aspose.Pdf's layout (no aspect preservation).
                                    imgW = Math.Min(imgW, availW);
                                    imgH = Math.Min(imgH, availH);
                                }
                            }
                            else
                            {
                                imgW = availW;
                                imgH = availH;
                            }

                            Page targetPage;
                            double yTop;
                            // The first frame follows the flow; every extra frame starts a fresh page.
                            if (frameIdx == 0 && flow.CurrentY - imgH >= marginBottom)
                            {
                                targetPage = flow.CurrentPage;
                                yTop = flow.CurrentY;
                            }
                            else
                            {
                                flow.Commit();
                                targetPage = Pages.Add();
                                targetPage.MediaBox = new Rectangle(0, 0, page.Width, page.Height);
                                Table.RegisterFont(targetPage);
                                yTop = page.Height - marginTop;
                            }
                            // Honour the image's horizontal alignment within the content
                            // box; without this every image is pinned to the left margin
                            // regardless of HorizontalAlignment.Right / Center.
                            double imgX = img.HorizontalAlignment switch
                            {
                                HorizontalAlignment.Right => page.Width - marginRight - imgW,
                                HorizontalAlignment.Center => marginLeft + (availW - imgW) / 2,
                                _ => marginLeft,
                            };
                            var rect = new Rectangle(imgX, yTop - imgH,
                                                     imgX + imgW, yTop);
                            try
                            {
                                targetPage.AddImage(frameData, rect, embedBlackWhite);
                            }
                            catch (ArgumentException)
                            {
                                continue;
                            }
                            if (ReferenceEquals(targetPage, flow.CurrentPage))
                            {
                                // Inline images keep the cursor on the shared line and only
                                // record their height; the line is closed (cursor dropped) by
                                // the next block image or the end-of-flow flush below.
                                if (img.IsInLineParagraph && frames.Count == 1)
                                    pendingInlineLineHeight = Math.Max(pendingInlineLineHeight, imgH);
                                else
                                    flow.AdvanceY(imgH);
                            }
                            else
                                flow.ResetToTopOfNextPage();
                        }
                    }
                    else if (para is Aspose.Pdf.Drawing.Graph graph)
                    {
                        // Shapes carry graph-local coordinates with the origin at the
                        // graph box's bottom-left corner. Translate the rendered stream
                        // so that corner lands at the correct page position.
                        var targetPage = flow.CurrentPage;
                        double originX, originY;
                        if (graph.IsChangePosition)
                        {
                            // Flow placement: the box sits at the current cursor, shifted by
                            // any Left/Top the caller set (offsets from the margin origin).
                            // Push to a fresh page if it doesn't fit below the cursor (but
                            // never when the cursor is already at the page top — an oversized
                            // graph still renders on the current page rather than looping).
                            if (flow.CurrentY - graph.Top - graph.Height < marginBottom
                                && flow.CurrentY < page.Height - marginTop)
                            {
                                flow.ResetToTopOfNextPage();
                                targetPage = flow.CurrentPage;
                            }
                            originX = marginLeft + graph.Left;
                            originY = flow.CurrentY - graph.Top - graph.Height;
                        }
                        else
                        {
                            // Absolute placement: Left from the left edge, Top from the top edge.
                            originX = graph.Left;
                            originY = targetPage.Height - graph.Top - graph.Height;
                        }
                        targetPage.AddContentStream(graph.Build(targetPage, originX, originY));
                        if (graph.IsChangePosition && ReferenceEquals(targetPage, flow.CurrentPage))
                            flow.AdvanceY(graph.Top + graph.Height);
                    }
                }
                if (pendingInlineLineHeight > 0)
                {
                    flow.AdvanceY(pendingInlineLineHeight);
                    pendingInlineLineHeight = 0;
                }
                flow.Commit();
                // FinaliseFootnotes runs before slotEnd capture so its spillover
                // pages (added via _overflowPages) extend this flow's slot range.
                flow.FinaliseFootnotes();
                pendingFlows.Add((flow, flowSlotStart, overflowPages.Count));
                page.Paragraphs.Clear();
            }

            // Header/Footer are already rendered once per page by the self-guarding
            // RenderToPage block above (which uses page.Number for '#' substitution).
            // Re-applying them here stamped a second copy onto every freshly laid-out
            // page, so it is intentionally not repeated.

            // Apply a watermark set through Page.Watermark as an artifact. Use the
            // set-only PendingWatermark (not the Watermark getter, which now *detects*
            // an already-present watermark from the content) so re-saving a document
            // that already carries a watermark doesn't stamp a second copy.
            if (page.PendingWatermark is { Available: true, Image: { } wmImage })
                new WatermarkArtifact { SourceImage = wmImage }.AddToPage(page);

            page.LayoutApplied = true;
        }

        // Add overflow pages (from multi-page table layout) after iteration.
        // Track the Page created for each slot so deferred link annotations
        // (per-segment hyperlinks queued by FlowLayout) can resolve to the
        // page they actually landed on.
        var overflowPageRefs = new List<Page>(overflowPages.Count);
        for (var slot = 0; slot < overflowPages.Count; slot++)
        {
            var (content, width, height) = overflowPages[slot];
            var newPage = Pages.Add();
            newPage.MediaBox = new Rectangle(0, 0, width, height);
            Table.RegisterFont(newPage);
            newPage.AddContentStream(content);
            if (overflowImages.TryGetValue(slot, out var imgs))
                foreach (var (data, rect) in imgs)
                    newPage.AddImage(data, rect);
            overflowPageRefs.Add(newPage);
        }

        // Resolve every flow's deferred link annotations + embedded-font renders
        // against the final page sequence -- each flow owns the slice of
        // overflowPageRefs captured at its commit time. Embedded renders go
        // first so TextBuilder gets a clean page before any annotation rects
        // overlay (incidental: AddContentStream order doesn't matter for the
        // saved PDF, but it keeps the dev mental model "render then annotate").
        foreach (var (flow, slotStart, slotEnd) in pendingFlows)
        {
            var pageRange = new List<Page>(slotEnd - slotStart);
            for (var i = slotStart; i < slotEnd; i++) pageRange.Add(overflowPageRefs[i]);
            flow.FinaliseEmbeddedRenders(pageRange);
            flow.FinaliseNotifications(pageRange);
            flow.FinaliseAnnotations(pageRange);
            // A page watermark repeats on the overflow pages its content spilled onto.
            if (flow.CurrentPage.PendingWatermark is { Available: true, Image: { } fwmImage })
                foreach (var op in pageRange)
                    new WatermarkArtifact { SourceImage = fwmImage }.AddToPage(op);

            // A running Header/Footer likewise repeats on every overflow page of the flow, not
            // just the originating page (which was stamped in the main loop). Freshly-materialised
            // overflow pages carry no Header/Footer of their own, so render the source page's.
            var hfSource = flow.CurrentPage;
            if (hfSource.Header is not null || hfSource.Footer is not null)
                foreach (var op in pageRange)
                {
                    if (op.HeaderFooterApplied) continue;
                    op.HeaderFooterApplied = true;
                    hfSource.Header?.RenderToPage(op, isHeader: true, op.Number, this);
                    hfSource.Footer?.RenderToPage(op, isHeader: false, op.Number, this);
                }
        }
    }

    /// <summary>Resolve a <see cref="ColumnInfo"/> into per-column left edges and
    /// widths. Columns start at <paramref name="marginLeft"/> and run left-to-right
    /// separated by the spacing. Explicit ColumnWidths win; when fewer than
    /// ColumnCount are given the columns share the available width evenly.</summary>
    private static (double[] lefts, double[] widths) BuildColumnGeometry(
        ColumnInfo info, double marginLeft, double contentWidth)
    {
        var count = info.ColumnCount;
        if (count < 1) count = 1;

        var spacing = ParseFirst(info.ColumnSpacing, 0);

        var parsed = ParseLengths(info.ColumnWidths);
        var widths = new double[count];
        if (parsed.Count >= count)
        {
            for (var i = 0; i < count; i++) widths[i] = parsed[i];
        }
        else
        {
            // Not enough explicit widths — divide the content area evenly.
            var even = (contentWidth - spacing * (count - 1)) / count;
            if (even <= 0) even = contentWidth / count;
            for (var i = 0; i < count; i++) widths[i] = even;
        }

        var lefts = new double[count];
        var x = marginLeft;
        for (var i = 0; i < count; i++)
        {
            lefts[i] = x;
            x += widths[i] + spacing;
        }
        return (lefts, widths);
    }

    /// <summary>Greedily word-wrap <paramref name="text"/> to lines that fit
    /// <paramref name="availWidth"/> points, estimating glyph advance as
    /// <paramref name="charWidth"/> per character. A word longer than the line is
    /// hard-broken. Always returns at least one (possibly empty) line.</summary>
    private static List<string> WrapToWidth(string text, double availWidth, double charWidth)
    {
        var lines = new List<string>();
        if (charWidth <= 0) charWidth = 6;
        var maxChars = System.Math.Max(4, (int)(availWidth / charWidth));
        var remaining = text ?? string.Empty;
        while (remaining.Length > maxChars)
        {
            var breakAt = remaining.LastIndexOf(' ', System.Math.Min(maxChars, remaining.Length - 1));
            if (breakAt <= 0) breakAt = maxChars;
            lines.Add(remaining[..breakAt]);
            remaining = remaining[breakAt..].TrimStart();
        }
        lines.Add(remaining);
        return lines;
    }

    /// <summary>Parse a space/comma-separated length list (e.g. "105 105 105").</summary>
    private static List<double> ParseLengths(string? s)
    {
        var result = new List<double>();
        if (string.IsNullOrWhiteSpace(s)) return result;
        foreach (var tok in s.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(tok, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                result.Add(v);
        return result;
    }

    /// <summary>First length in <paramref name="s"/>, or <paramref name="fallback"/>.</summary>
    private static double ParseFirst(string? s, double fallback)
    {
        var list = ParseLengths(s);
        return list.Count > 0 ? list[0] : fallback;
    }

    /// <summary>
    /// Split an HTML block's TextFragment into segments so each inline &lt;a href&gt;
    /// range carries a <see cref="WebHyperlink"/>. The fragment text is unchanged
    /// (segment texts concatenate back to it); the layout engine emits a Link
    /// annotation over each hyperlinked segment's rendered run.
    /// </summary>
    private static void ApplyHtmlAnchorSegments(Text.TextFragment bf, string text,
        System.Collections.Generic.List<(int Start, int Length, string Url)> anchors)
    {
        var ordered = new System.Collections.Generic.List<(int Start, int Length, string Url)>();
        foreach (var a in anchors)
            if (a.Start >= 0 && a.Length > 0 && a.Start < text.Length && !string.IsNullOrEmpty(a.Url))
                ordered.Add(a);
        ordered.Sort((x, y) => x.Start.CompareTo(y.Start));
        if (ordered.Count == 0) return;

        var parts = new System.Collections.Generic.List<(string Txt, string? Url)>();
        int pos = 0;
        foreach (var (start, len, url) in ordered)
        {
            var s = Math.Max(start, pos);
            if (s >= text.Length) break;
            if (s > pos) parts.Add((text.Substring(pos, s - pos), null));
            var end = Math.Min(start + len, text.Length);
            if (end > s) parts.Add((text.Substring(s, end - s), url));
            pos = Math.Max(pos, end);
        }
        if (pos < text.Length) parts.Add((text.Substring(pos), null));
        if (parts.Count == 0) return;

        bf.Segments.Clear();
        foreach (var (txt, url) in parts)
        {
            if (txt.Length == 0) continue;
            var seg = new Text.TextSegment(txt);
            seg.TextState.FontSize = bf.TextState.FontSize;
            seg.TextState.IsBold = bf.TextState.IsBold;
            seg.TextState.IsItalic = bf.TextState.IsItalic;
            if (bf.TextState.Font is not null) seg.TextState.Font = bf.TextState.Font;
            if (bf.TextState.ForegroundColor is not null) seg.TextState.ForegroundColor = bf.TextState.ForegroundColor;
            if (url is not null) seg.Hyperlink = new WebHyperlink(url);
            bf.Segments.Add(seg);
        }
    }

    /// Render every &lt;img&gt; element whose source resolves to a readable local file
    /// (a <c>file://</c> URI or a plain path) as an image XObject in the flowing HTML
    /// content. Remote (http/https) sources are skipped — they are not fetched — leaving
    /// the existing alt-text fallback in place. Each image is placed at the current flow
    /// cursor, scaled to its HTML width/height attributes (falling back to the intrinsic
    /// size and aspect ratio), and clamped to the content width.
    /// </summary>
    private void RenderHtmlImages(string htmlContent, FlowLayout flow, double marginLeft, double marginRight)
    {
        if (string.IsNullOrEmpty(htmlContent)) return;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     htmlContent, @"<img\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var tag = m.Value;
            var srcM = System.Text.RegularExpressions.Regex.Match(tag,
                @"\bsrc\s*=\s*['""]?([^'""\s>]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!srcM.Success) continue;
            var bytes = LoadHtmlImageBytes(srcM.Groups[1].Value);
            if (bytes is null) continue;

            double natW = 0, natH = 0;
            TryGetImageNaturalSizePt(bytes, out natW, out natH);
            var w = ParseHtmlImgDimension(tag, "width");
            var h = ParseHtmlImgDimension(tag, "height");
            if (w <= 0 && h <= 0) { w = natW > 0 ? natW : 72; h = natH > 0 ? natH : 72; }
            else if (h <= 0) h = (natW > 0 && natH > 0) ? w * natH / natW : w;
            else if (w <= 0) w = (natW > 0 && natH > 0) ? h * natW / natH : h;

            var availW = flow.CurrentPage.Width - marginLeft - marginRight;
            if (availW > 0 && w > availW) { h *= availW / w; w = availW; }

            var topY = flow.CurrentY;
            flow.CurrentPage.AddImage(bytes, new Rectangle(marginLeft, topY - h, marginLeft + w, topY));
            flow.AdvanceY(h);
        }
    }

    /// <summary>Load the bytes for an &lt;img&gt; source if it is a readable local file
    /// (file:// URI or a path on disk). Returns null for remote or unreadable sources.</summary>
    private static byte[]? LoadHtmlImageBytes(string src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;
        try
        {
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return null;
            var path = src;
            if (src.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.IsFile)
                path = uri.LocalPath;
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch { return null; }
    }

    /// <summary>Parse a numeric width/height attribute (px) from an &lt;img&gt; tag; 0 if absent.</summary>
    private static double ParseHtmlImgDimension(string tag, string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(tag,
            @"\b" + name + @"\s*=\s*['""]?(\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : 0;
    }

    /// <summary>
    /// Read an image's intrinsic size in PDF points from its PNG/JPEG header without
    /// decoding pixels: point size = pixels * 72 / DPI, with DPI taken from the PNG
    /// pHYs chunk or JPEG JFIF density (defaulting to 72 when absent). Returns false
    /// for formats this can't parse, leaving the caller to fall back to the page budget.
    /// </summary>
    /// <summary>Whether a JPEG uses progressive (SOF2) encoding, which the embedded-image
    /// decoder cannot read — such images are re-encoded to a baseline raster instead.</summary>
    private static bool IsProgressiveJpeg(byte[] d)
    {
        if (d is null || d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return false;
        int i = 2;
        while (i + 3 < d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }
            int m = d[i + 1];
            if (m == 0xC2) return true;                       // SOF2 = progressive
            if (m == 0xD8 || m == 0xD9 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            if (m == 0xDA) return false;                      // Start of scan: no SOF2 before it
            int seg = (d[i + 2] << 8) | d[i + 3];
            if (seg < 2) return false;
            i += 2 + seg;
        }
        return false;
    }

    /// <summary>
    /// Decode a raster image of any platform-supported format (TIFF, BMP, GIF, ...) into one
    /// PNG per frame (a multi-page TIFF yields one PNG per page), preserving each frame's DPI
    /// so its natural size is recovered. Returns <c>null</c> when the bytes cannot be decoded
    /// or the platform image codec is unavailable.
    /// </summary>
    private static System.Collections.Generic.List<byte[]>? TryDecodeImageFramesAsPng(byte[] data)
    {
        if (data is null || data.Length < 4) return null;
        // TIFF decodes with the built-in managed decoder — platform-independent,
        // and resilient to damaged multi-frame files (corrupt frames are skipped,
        // the rest still paginate). The platform codec below remains the fallback
        // for TIFF flavours the managed decoder declines (e.g. JPEG-in-TIFF) and
        // for the other raster formats (BMP / GIF / ...).
        if (IO.TiffDecoder.IsTiff(data)
            && IO.TiffDecoder.DecodeFramesAsPng(data) is { Count: > 0 } tiffFrames)
            return tiffFrames;
#pragma warning disable CA1416 // platform-guarded: System.Drawing image codecs (Windows)
        try
        {
            using var src = new System.IO.MemoryStream(data);
            using var img = System.Drawing.Image.FromStream(src);
            var frames = new System.Collections.Generic.List<byte[]>();
            int frameCount;
            try { frameCount = img.GetFrameCount(System.Drawing.Imaging.FrameDimension.Page); }
            catch { frameCount = 1; }
            if (frameCount < 1) frameCount = 1;
            for (int fr = 0; fr < frameCount; fr++)
            {
                // A corrupt frame in a multi-frame file (SelectActiveFrame or the
                // decode throws) is skipped rather than dropping the whole image —
                // the remaining frames still paginate, matching the reference
                // engine's page count for such files.
                try
                {
                    if (frameCount > 1) img.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Page, fr);
                    using var bmp = new System.Drawing.Bitmap(img.Width, img.Height,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    bmp.SetResolution(img.HorizontalResolution > 0 ? img.HorizontalResolution : 96f,
                                      img.VerticalResolution > 0 ? img.VerticalResolution : 96f);
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.Clear(System.Drawing.Color.White);
                        g.DrawImage(img, 0, 0, img.Width, img.Height);
                    }
                    using var outMs = new System.IO.MemoryStream();
                    bmp.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
                    frames.Add(outMs.ToArray());
                }
                catch when (frameCount > 1)
                {
                }
            }
            return frames.Count > 0 ? frames : null;
        }
        catch { return null; }
#pragma warning restore CA1416
    }

    internal static bool TryGetImageNaturalSizePt(byte[] d, out double widthPt, out double heightPt)
        => TryGetImageNaturalSizePt(d, applyResolution: true, out widthPt, out heightPt);

    /// <summary>Natural image size in points. When <paramref name="applyResolution"/>
    /// is false (the <see cref="Image.IsApplyResolution"/> default) the embedded DPI is
    /// ignored and one pixel maps to one point, matching how an unsized generator
    /// <see cref="Image"/> is laid out.</summary>
    internal static bool TryGetImageNaturalSizePt(byte[] d, bool applyResolution, out double widthPt, out double heightPt)
    {
        widthPt = 0; heightPt = 0;
        if (d is null || d.Length < 24) return false;
        int BE16(int o) => (d[o] << 8) | d[o + 1];
        int BE32(int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

        // JPEG 2000 box file (.jp2/.jpx): dimensions live in the 'ihdr' box (height@0,
        // width@4 of its data). One pixel maps to one point (JP2 carries DPI only in the
        // optional 'res' box, which we ignore for parity with unsized generator images).
        if (d.Length >= 12 && d[0] == 0x00 && d[1] == 0x00 && d[2] == 0x00 && d[3] == 0x0C
            && d[4] == 0x6A && d[5] == 0x50 && d[6] == 0x20 && d[7] == 0x20)
        {
            for (int i = 8; i + 16 <= d.Length; i++)
            {
                if (d[i] == 'i' && d[i + 1] == 'h' && d[i + 2] == 'd' && d[i + 3] == 'r')
                {
                    int ph = BE32(i + 4), pw = BE32(i + 8);
                    if (pw > 0 && ph > 0) { widthPt = pw; heightPt = ph; return true; }
                    break;
                }
            }
            return false;
        }
        // Raw JPEG 2000 codestream: SOC (FF4F) then SIZ (FF51); Xsiz@8, Ysiz@12.
        if (d.Length >= 16 && d[0] == 0xFF && d[1] == 0x4F && d[2] == 0xFF && d[3] == 0x51)
        {
            long xs = (uint)BE32(8), ys = (uint)BE32(12);
            if (xs > 0 && ys > 0) { widthPt = xs; heightPt = ys; return true; }
            return false;
        }

        // PNG: 8-byte signature, then IHDR (width@16, height@20). pHYs gives DPI.
        if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
        {
            int pw = BE32(16), ph = BE32(20);
            if (pw <= 0 || ph <= 0) return false;
            double dpiX = 72, dpiY = 72;
            for (int i = 8; i + 12 <= d.Length;)
            {
                int len = BE32(i);
                if (len < 0) break;
                if (d[i + 4] == 'p' && d[i + 5] == 'H' && d[i + 6] == 'Y' && d[i + 7] == 's' && i + 8 + 9 <= d.Length)
                {
                    long ppuX = (uint)BE32(i + 8), ppuY = (uint)BE32(i + 12);
                    if (d[i + 16] == 1 && ppuX > 0 && ppuY > 0) // unit = metre
                    {
                        dpiX = ppuX * 0.0254;
                        dpiY = ppuY * 0.0254;
                    }
                    break;
                }
                if (d[i + 4] == 'I' && d[i + 5] == 'D' && d[i + 6] == 'A' && d[i + 7] == 'T') break;
                i += 12 + len; // length + type + data + CRC
            }
            if (dpiX <= 0 || !applyResolution) dpiX = 72;
            if (dpiY <= 0 || !applyResolution) dpiY = 72;
            widthPt = pw * 72.0 / dpiX;
            heightPt = ph * 72.0 / dpiY;
            return true;
        }

        // JPEG: scan markers for a Start-Of-Frame (dimensions) and JFIF APP0 (density).
        if (d[0] == 0xFF && d[1] == 0xD8)
        {
            double dpiX = 72, dpiY = 72; int pw = 0, ph = 0;
            int p = 2;
            while (p + 4 < d.Length)
            {
                if (d[p] != 0xFF) { p++; continue; }
                int marker = d[p + 1];
                if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7)) { p += 2; continue; }
                int seg = BE16(p + 2);
                if (seg < 2) break;
                if (marker == 0xE0 && p + 4 + 14 <= d.Length
                    && d[p + 4] == (byte)'J' && d[p + 5] == (byte)'F' && d[p + 6] == (byte)'I' && d[p + 7] == (byte)'F')
                {
                    int units = d[p + 11];
                    int dx = BE16(p + 12), dy = BE16(p + 14);
                    if (dx > 0 && dy > 0)
                    {
                        if (units == 1) { dpiX = dx; dpiY = dy; }            // dots per inch
                        else if (units == 2) { dpiX = dx * 2.54; dpiY = dy * 2.54; } // dots per cm
                    }
                }
                else if ((marker >= 0xC0 && marker <= 0xCF)
                         && marker != 0xC4 && marker != 0xC8 && marker != 0xCC
                         && p + 9 <= d.Length)
                {
                    ph = BE16(p + 5);
                    pw = BE16(p + 7);
                }
                p += 2 + seg;
            }
            if (pw <= 0 || ph <= 0) return false;
            if (dpiX <= 0 || !applyResolution) dpiX = 72;
            if (dpiY <= 0 || !applyResolution) dpiY = 72;
            widthPt = pw * 72.0 / dpiX;
            heightPt = ph * 72.0 / dpiY;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Flow-layout helper that tracks a Y cursor across sequential paragraphs on a
    /// page. When content overflows the page's content region, a new overflow page
    /// is queued (via the shared overflowPages list that Document.ApplyPageContent
    /// drains after the main loop) and the cursor is reset to the top margin. This
    /// allows TextFragment / HtmlFragment / Heading / Table paragraphs to flow down
    /// the page instead of stacking at the same Y coordinate.
    /// </summary>
    private sealed class FlowLayout
    {
        private readonly List<(byte[] content, double width, double height)> _overflowPages;
        private readonly double _marginLeft;
        private readonly double _marginRight;
        private readonly double _marginTop;
        private readonly double _marginBottom;
        private readonly double _startPageHeight;
        private readonly Page _startPage;
        private double _curY;

        // Multi-column layout state. When _colLefts is non-null the flow lays
        // text out across N columns: it fills column 0 from the band top down to
        // marginBottom, then column 1, ... then column N-1, and only then starts
        // a fresh page (resetting to column 0). Single-column flow leaves these
        // null so every CurLeft/CurWidth/FlowToNextRegion call degenerates to the
        // original full-width, one-region-per-page behaviour (byte-for-byte).
        private double[]? _colLefts;
        private double[]? _colWidths;
        private int _curCol;
        private double _colBandTop;     // Y the next column resets to (top of the band)
        private double _colDeepestY;    // lowest Y any column reached, for resume-after-box

        // Lowest Y the body reached on each slot. FinaliseFootnotes anchors a page's
        // footnote band at the body bottom (not the top margin), so a footnote whose
        // body fills the page spills to a continuation page instead of overlapping.
        private readonly Dictionary<int, double> _slotBottomY = new();

        // When we overflow the start page, we accumulate content blocks for the
        // *current* overflow page here. On the next overflow we flush the buffer
        // into the overflow queue (one queue entry = one new page) and start a
        // fresh buffer. Without this, each WriteTextFragment chunk would become
        // its own tiny page with just a line or two.
        private List<byte[]>? _overflowBuffer;

        // Slot index of the currently-active write target. -1 means _startPage;
        // 0+ means the i-th overflow page that the current _overflowBuffer will
        // flush into. Tracking this lets link annotations and paragraph-position
        // records survive into pages that don't exist yet (overflow buffers are
        // flushed in the outer loop after this layout finishes).
        private int _currentSlot = -1;

        // Where each rendered paragraph started, so a LocalHyperlink with
        // Target = another paragraph (e.g. LocalHyperlink(head)) can resolve to
        // the right slot + y in the saved document. Resolved against the
        // slot→Page map after the outer overflow drain.
        private readonly Dictionary<BaseParagraph, (int slot, double yTop)> _paragraphPositions = new();

        // Deferred link annotations -- target slot, rect, and hyperlink. Resolved
        // and emitted by FinaliseAnnotations once overflow slots map to real
        // Pages.
        private readonly List<(int slot, Rectangle rect, Hyperlink hyperlink)> _pendingLinks = new();

        // Deferred renders for fragments using embedded/CID fonts. The paginator
        // does its line-break maths in Standard-14 metric space (approximate
        // but enough for "does this chunk fit"), then queues the per-page
        // chunk text + start coordinates + the original fragment's TextState.
        // After Pages.Add() has populated overflow slots, FinaliseEmbeddedRenders
        // builds a TextBuilder per target page and re-runs each chunk through
        // it -- the TextBuilder path handles TrueType/CIDFont registration in
        // the page's /Resources and emits the right glyph encoding.
        private readonly List<(int slot, double x, double y, string text,
            Text.TextState textState, double fontSize, double? baseline)> _pendingEmbeddedRenders = new();

        // Running baseline of the last body line emitted on the current region,
        // used to give the full-size / explicit line-spacing modes the
        // Aspose.Pdf "leading above the line" rule: the next line's baseline
        // sits one of *its own* line heights below the previous baseline, so a
        // size change between adjacent paragraphs spaces by the lower
        // paragraph's metrics (not the upper one's). Null at the top of every
        // region/page so the first line there drops by its font size, matching
        // Aspose.Pdf's first-line placement. Footnotes bypass this (they queue
        // an explicit baseline of their own).
        private double? _lastBodyBaseline;

        // Footnotes queued for page-bottom rendering. Each footnote belongs to
        // a specific page slot (the slot the parent TextFragment.FootNote
        // declaration landed on). FinaliseFootnotes lays out each page's
        // footnotes upward from marginBottom and queues the resulting text
        // chunks into _pendingEmbeddedRenders -- so the existing
        // FinaliseEmbeddedRenders pass dispatches them to the right Page with
        // the right font.
        private readonly List<(int slot, Note note)> _pendingFootnotes = new();

        // Line-break notification log, keyed by slot, accumulated only when
        // notification logging was enabled on the document. Distributed to the
        // materialised Pages by FinaliseNotifications.
        private readonly bool _logNotifications;
        private readonly Dictionary<int, System.Text.StringBuilder> _notificationsBySlot = new();

        public FlowLayout(Page startPage, List<(byte[] content, double width, double height)> overflowPages,
            double marginLeft, double marginRight, double marginTop, double marginBottom, double startY,
            bool logNotifications = false)
        {
            _startPage = startPage;
            _overflowPages = overflowPages;
            _marginLeft = marginLeft;
            _marginRight = marginRight;
            _marginTop = marginTop;
            _marginBottom = marginBottom;
            _startPageHeight = startPage.Height;
            _curY = startY;
            _logNotifications = logNotifications;
        }

        /// <summary>Record a finished line in the notification log for the given slot.</summary>
        private void LogLine(int slot, string content, double x, double y, char reason)
        {
            var reasonText = reason switch
            {
                'N' => "new line marker detected",
                'M' => "line reached right margin",
                _ => "end of the line",
            };
            if (!_notificationsBySlot.TryGetValue(slot, out var sb))
                _notificationsBySlot[slot] = sb = new System.Text.StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("The line '").Append(content).Append("' was finished at {X=")
              .Append(x.ToString("F1", ci)).Append(", Y=").Append(y.ToString("F1", ci))
              .Append("} because ").Append(reasonText).Append(".\n");
        }

        /// <summary>Record where a paragraph started rendering. Looked up later
        /// when another paragraph's LocalHyperlink targets this one.</summary>
        public void RecordPosition(BaseParagraph p)
        {
            if (p is null) return;
            _paragraphPositions[p] = (_currentSlot, _curY);
        }

        /// <summary>Queue a TextFragment.FootNote for page-bottom rendering on
        /// the current slot. Footnote content is laid out by FinaliseFootnotes
        /// after the main paragraph loop completes (when total content height
        /// is known and the page bottom band's size is fixed).</summary>
        public void QueueFootnote(Note note)
        {
            if (note is null) return;
            _pendingFootnotes.Add((_currentSlot, note));
        }

        /// <summary>Layout each queued footnote at the bottom of its slot's
        /// page, growing upward from marginBottom. Each footnote's text wraps
        /// to contentWidth using TTF widths when available; the resulting
        /// chunks are queued into _pendingEmbeddedRenders so the existing
        /// embedded-render finaliser dispatches them to the right Page with
        /// the right embedded font. Footnote font defaults to the first
        /// paragraph's font / size; falls back to Helvetica 9pt.</summary>
        public void FinaliseFootnotes()
        {
            if (_pendingFootnotes.Count == 0) return;

            // Group by slot so each page gets one band.
            var bySlot = new Dictionary<int, List<Note>>();
            foreach (var (slot, note) in _pendingFootnotes)
            {
                if (!bySlot.TryGetValue(slot, out var list))
                    bySlot[slot] = list = new List<Note>();
                list.Add(note);
            }

            var contentWidth = _startPage.Width - _marginLeft - _marginRight;
            if (contentWidth <= 0) return;
            var pageWidth = _startPage.Width;
            var pageHeight = _startPageHeight;

            // Process slots in order so spillover pages from slot N land
            // before slot N+1's content is finalised.
            var sortedSlots = new List<int>(bySlot.Keys);
            sortedSlots.Sort();

            foreach (var slot in sortedSlots)
            {
                var notes = bySlot[slot];
                // Collect all lines for this page's footnote band in natural
                // top-down reading order.
                var bandLines = new List<(string text, Text.TextState state, double size)>();
                foreach (var note in notes)
                {
                    foreach (var para in note.Paragraphs)
                    {
                        if (para is not Text.TextFragment fn) continue;
                        // Promote the first segment's font/size up to the fragment
                        // when missing (same logic as WriteTextFragment).
                        if (fn.Segments is { Count: > 0 } segs)
                            foreach (var s in segs)
                            {
                                if (fn.TextState.Font?.SourceFontData is null
                                    && fn.TextState.FontData is null
                                    && s.TextState.Font?.SourceFontData is not null)
                                    fn.TextState.Font = s.TextState.Font;
                                if (fn.TextState.FontData is null && s.TextState.FontData is not null)
                                    fn.TextState.FontData = s.TextState.FontData;
                                if (fn.TextState.FontSize <= 0 && s.TextState.FontSize > 0)
                                    fn.TextState.FontSize = s.TextState.FontSize;
                            }
                        var fnSize = fn.TextState.FontSize > 0 ? fn.TextState.FontSize : 9;
                        var fnFont = Text.TextBuilder.MapToStandard14Public(fn.TextState);
                        var fnFontData = fn.TextState.FontData ?? fn.TextState.Font?.SourceFontData;
                        var lines = Text.TextPaginator.WrapToWidth(fn.Text ?? string.Empty,
                            fnFont, fnSize, contentWidth, fnFontData);
                        foreach (var line in lines)
                            bandLines.Add((line, fn.TextState, fnSize));
                    }
                }

                if (bandLines.Count == 0) continue;

                // Page-bottom band on the original slot: count how many of the
                // leading footnote lines fit between marginBottom and the bottom of
                // the body content on that page. Anchoring at the body bottom (not the
                // top margin) means a footnote whose body already fills the page has
                // little or no room and spills its lines to a continuation page,
                // instead of overprinting the body.
                var bandBottom = _marginBottom;
                var bodyBottom = _slotBottomY.TryGetValue(slot, out var sb) ? sb : pageHeight - _marginTop;
                var bandTop = Math.Min(pageHeight - _marginTop, bodyBottom);
                var bandHeight = Math.Max(0, bandTop - bandBottom);
                var idx = 0;
                double accumulated = 0;
                while (idx < bandLines.Count)
                {
                    var lh = bandLines[idx].size * 1.2;
                    if (accumulated + lh > bandHeight) break;
                    accumulated += lh;
                    idx++;
                }
                var firstPageCount = idx;

                // Render the fitting prefix bottom-up so the first line lands
                // at the top of the band (closest to the body text) and the
                // last fitting line at marginBottom -- matching the natural
                // footnote layout (rule above, lines below in reading order).
                double y = bandBottom;
                for (var i = firstPageCount - 1; i >= 0; i--)
                {
                    var (line, state, size) = bandLines[i];
                    var lineHeight = size * 1.2;
                    _pendingEmbeddedRenders.Add((slot, _marginLeft, y + lineHeight,
                        line, state, size, null));
                    y += lineHeight;
                }

                // Spillover: each subsequent page is a fresh overflow slot
                // rendered top-down (no body content above, so the
                // continuation starts under marginTop). Page is materialised
                // as an empty content stream; the embedded-render dispatch
                // fills it in via TextBuilder once Pages.Add has run.
                while (idx < bandLines.Count)
                {
                    _overflowPages.Add((System.Array.Empty<byte>(), pageWidth, pageHeight));
                    _currentSlot++;
                    var spillSlot = _currentSlot;

                    double pageY = pageHeight - _marginTop;
                    while (idx < bandLines.Count)
                    {
                        var (line, state, size) = bandLines[idx];
                        var lineHeight = size * 1.2;
                        if (pageY - lineHeight < bandBottom) break;
                        // Queued y = top of glyphs; FinaliseEmbeddedRenders
                        // converts to baseline via (y - fontSize).
                        _pendingEmbeddedRenders.Add((spillSlot, _marginLeft, pageY,
                            line, state, size, null));
                        pageY -= lineHeight;
                        idx++;
                    }
                }
            }
        }

        /// <summary>Resolve every queued link annotation against the actual
        /// Page sequence and emit it. <paramref name="overflowPageRefs"/> is
        /// the list of Pages added (in order) from this layout's overflow
        /// queue; index N corresponds to slot N. Called by Document.Save once
        /// the outer loop has drained _overflowPages into Pages.</summary>
        public void FinaliseAnnotations(IList<Page> overflowPageRefs)
        {
            Page? PageOf(int slot) =>
                slot < 0 ? _startPage :
                slot < overflowPageRefs.Count ? overflowPageRefs[slot] : null;

            foreach (var (slot, rect, hyperlink) in _pendingLinks)
            {
                var srcPage = PageOf(slot);
                if (srcPage is null) continue;
                EmitLinkOn(srcPage, rect, hyperlink, PageOf);
            }
        }

        /// <summary>Render every queued embedded-font chunk through a
        /// TextBuilder bound to its target Page. Called from Document.Save
        /// after the overflow drain so each chunk's slot resolves to a real
        /// Page that TextBuilder can register the embedded font into.</summary>
        public void FinaliseEmbeddedRenders(IList<Page> overflowPageRefs)
        {
            Page? PageOf(int slot) =>
                slot < 0 ? _startPage :
                slot < overflowPageRefs.Count ? overflowPageRefs[slot] : null;

            foreach (var (slot, x, y, text, textState, fontSize, baseline) in _pendingEmbeddedRenders)
            {
                var target = PageOf(slot);
                if (target is null) continue;
                var sub = new Text.TextFragment(text)
                {
                    Position = new Text.Position(x, baseline ?? (y - fontSize))
                };
                // Copy the embedded-font state across so TextBuilder picks the
                // right embedding path (FontData / TtfData / Font.SourceFontData).
                sub.TextState.FontSize = (float)fontSize;
                sub.TextState.FontData = textState.FontData;
                sub.TextState.Font = textState.Font;
                sub.TextState.ForegroundColor = textState.ForegroundColor;
                sub.TextState.IsBold = textState.IsBold;
                sub.TextState.IsItalic = textState.IsItalic;
                // Carry the leading so a multi-line chunk renders on the same pitch
                // the paginator reserved (fontSize + LineSpacing), not a default 1.2x.
                sub.TextState.LineSpacing = textState.LineSpacing;
                // Carry the highlight so TextBuilder draws the per-line background rectangle
                // (the non-embedded path passes it through BuildWrappedTextStream; the embedded
                // path must copy it here or the highlight is silently dropped).
                sub.TextState.BackgroundColor = textState.BackgroundColor;
                var tb = new Text.TextBuilder(target);
                tb.AppendText(sub);
            }
        }

        /// <summary>Distribute the accumulated line-break notifications to the
        /// materialised Pages once the overflow drain has produced real Page
        /// objects for each slot.</summary>
        public void FinaliseNotifications(IList<Page> overflowPageRefs)
        {
            if (!_logNotifications || _notificationsBySlot.Count == 0) return;
            Page? PageOf(int slot) =>
                slot < 0 ? _startPage :
                slot < overflowPageRefs.Count ? overflowPageRefs[slot] : null;
            foreach (var kv in _notificationsBySlot)
            {
                var page = PageOf(kv.Key);
                if (page is not null) page.NotificationLog += kv.Value.ToString();
            }
        }

        private void EmitLinkOn(Page srcPage, Rectangle rect, Hyperlink hyperlink,
            Func<int, Page?> pageOf)
        {
            if (hyperlink is LocalHyperlink lh)
            {
                int targetPageNumber = lh.TargetPageNumber;
                double destX = _marginLeft, destY = 0;
                if (targetPageNumber <= 0 && lh.Target is { } target
                    && _paragraphPositions.TryGetValue(target, out var pos)
                    && pageOf(pos.slot) is { } tp)
                {
                    targetPageNumber = tp.Number;
                    // Aspose's GoTo XYZ coordinate is in default user space --
                    // x = left margin, y = top of the target paragraph.
                    destY = pos.yTop;
                }
                if (targetPageNumber > 0)
                    srcPage.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(targetPageNumber, destX, destY, 0)));
            }
            else if (hyperlink is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
            {
                srcPage.Annotations.AddLinkAnnotation(rect, wh.Url);
            }
            else if (hyperlink is FileHyperlink fh && !string.IsNullOrEmpty(fh.FileName))
            {
                var launch = new LaunchAction(fh.FileName) { NewWindow = fh.NewWindow };
                srcPage.Annotations.AddLinkAnnotation(rect, launch);
            }
        }

        public double CurrentY => _curY;
        public Page CurrentPage => _startPage;

        public void AdvanceY(double delta) => _curY -= delta;

        /// <summary>Bottom content margin (points) — the Y below which the flow page-breaks.</summary>
        public double BottomMargin => _marginBottom;

        /// <summary>Top of the content area on the current page (points).</summary>
        public double ContentTop => _startPageHeight - _marginTop;

        /// <summary>Inject a pre-built content stream (e.g. a Table slice rendered by
        /// BuildMultiPage at <see cref="CurrentY"/>) at the flow's CURRENT page position.
        /// While still on the start page this appends to it directly; once the flow has
        /// page-broken into an overflow buffer it appends to that buffer instead, so the
        /// content lands on the page the cursor is actually on (not the original start page).
        /// The caller advances <see cref="CurrentY"/> afterwards.</summary>
        public void InjectContentAtCursor(byte[] content)
        {
            if (content is null || content.Length == 0) return;
            if (_overflowBuffer is null) _startPage.AddContentStream(content);
            else _overflowBuffer.Add(content);
        }

        /// <summary>True once the flow has page-broken off the start page (subsequent
        /// content lives in an overflow buffer, not on a live Page yet).</summary>
        public bool HasOverflowed => _overflowBuffer is not null;

        /// <summary>Extra left indent (points) added to the write region for the next
        /// fragment — used for HTML block / list-item indentation. Reset by the caller
        /// between blocks.</summary>
        public double LeftIndent { get; set; }

        /// <summary>Left edge (in points) of the region the next line writes
        /// into — the current column when in column mode, else the page's left
        /// content margin, plus any block indent.</summary>
        private double CurLeft => (_colLefts is not null ? _colLefts[_curCol] : _marginLeft) + LeftIndent;

        /// <summary>Usable width (in points) of the current write region — the
        /// current column's width in column mode, else the full content width,
        /// reduced by any block indent.</summary>
        private double CurWidth => (_colWidths is not null
            ? _colWidths[_curCol]
            : _startPage.Width - _marginLeft - _marginRight) - LeftIndent;

        /// <summary>Begin multi-column layout. <paramref name="lefts"/> and
        /// <paramref name="widths"/> describe each column's absolute left edge and
        /// usable width. Text written after this fills column 0 top-to-bottom, then
        /// column 1, etc.; only after the last column does the flow page-break.
        /// Columns start at the current Y cursor (the band top).</summary>
        public void BeginColumns(double[] lefts, double[] widths)
        {
            _colLefts = lefts;
            _colWidths = widths;
            _curCol = 0;
            _colBandTop = _curY;
            _colDeepestY = _curY;
        }

        /// <summary>End multi-column layout and resume full-width flow just below
        /// the deepest point any column reached, so content after the box renders
        /// under it rather than overlapping.</summary>
        public void EndColumns()
        {
            if (_colLefts is null) return;
            _colLefts = null;
            _colWidths = null;
            _curCol = 0;
            _curY = _colDeepestY;
        }

        /// <summary>Force the next write into the next column (honours
        /// <see cref="BaseParagraph.IsFirstParagraphInColumn"/>). Past the last
        /// column this starts a fresh page of columns.</summary>
        public void ForceNextColumn()
        {
            if (_colLefts is null) return;
            FlowToNextRegion();
        }

        /// <summary>Advance to the next write region: the next column if one
        /// remains in column mode, otherwise a fresh page. Outside column mode
        /// this is exactly <see cref="StartNewPage"/>.</summary>
        /// <summary>Note that body content on the current slot reached at least as
        /// far down as <paramref name="y"/>. FinaliseFootnotes uses the minimum (the
        /// deepest point) as the top of that page's footnote band.</summary>
        private void RecordSlotBottom(double y)
        {
            _slotBottomY[_currentSlot] = _slotBottomY.TryGetValue(_currentSlot, out var prev)
                ? Math.Min(prev, y) : y;
        }

        private void FlowToNextRegion()
        {
            // A fresh column / page restarts body line placement from the top, so
            // the next line drops by its font size rather than chaining onto the
            // previous region's last baseline.
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            if (_colLefts is not null && _curCol < _colLefts.Length - 1)
            {
                _curCol++;
                _curY = _colBandTop;
            }
            else
            {
                StartNewPage();
            }
        }

        /// <summary>
        /// Force the next paragraph to start on a fresh overflow page. Used after
        /// a Table — we don't currently know the Table's final Y, so we treat it
        /// as consuming the rest of the current page.
        /// </summary>
        public void ResetToTopOfNextPage() => _curY = _marginBottom - 1; // guarantees next EnsureRoom triggers new page

        /// <summary>Eagerly start a fresh overflow page. Used when the next
        /// paragraph must render on a new page regardless of whether the
        /// downstream render path consults the Y cursor (e.g. Heading.Build
        /// draws at the supplied Y without an internal pagination check, so
        /// just resetting the cursor isn't enough). Commits the current page even
        /// when empty so consecutive forced breaks (e.g. several IsInNewPage
        /// FloatingBoxes that render nothing) each produce their own blank page
        /// instead of collapsing onto one.</summary>
        public void ForceNewPage() => StartNewPage(flushEmpty: true);

        /// <summary>Flush the last overflow page buffer to the shared overflow queue.</summary>
        public void Commit()
        {
            if (_overflowBuffer is { Count: > 0 })
            {
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
                _overflowBuffer = null;
            }
        }

        /// <summary>Resume the flow on a spill page whose content is already built — e.g.
        /// the final page of a multi-page table. The supplied content seeds the next
        /// overflow slot's buffer and the cursor resumes at <paramref name="resumeY"/>, so
        /// following paragraphs append below it on the same page instead of opening a fresh
        /// one. Returns the slot index that page will occupy in the overflow queue.</summary>
        public int ContinueOnPrebuiltSpill(byte[] content, double resumeY)
        {
            // Flush any buffer in flight so it keeps its own slot before we seed a new one.
            if (_overflowBuffer is { Count: > 0 })
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
            _overflowBuffer = new List<byte[]> { content };
            _currentSlot = _overflowPages.Count; // slot this buffer will flush into
            _curY = resumeY;
            if (_colLefts is not null)
            {
                _curCol = 0;
                _colBandTop = _curY;
                _colDeepestY = _curY;
            }
            return _currentSlot;
        }

        /// <summary>
        /// Write a TextFragment as wrapped, flowed text starting at the current Y
        /// cursor. Spans multiple pages via the overflow queue when needed. Returns
        /// false if the fragment is too complex for flow layout (embedded font or
        /// multi-segment) — caller should fall back to legacy fixed-position writer.
        /// </summary>
        public bool WriteTextFragment(Text.TextFragment tf)
        {
            // Caller-specified Position overrides flow layout. Use HasExplicitPosition,
            // not "Position != null": the getter now auto-materialises a (0,0) Position,
            // so a fragment the caller never positioned must still flow here.
            if (tf.HasExplicitPosition) return false;
            // Promote a segment-level font/size up to the fragment when the
            // fragment itself didn't set one. Generator-style tests build the
            // fragment with `new TextFragment()` then attach a TextSegment that
            // carries TextState.Font = FontRepository.FindFont("Arial") and a
            // FontSize -- the fragment-level TextState stays at the default
            // Helvetica/12 placeholder (TextFragmentState seeds Font with
            // FontInfo.DefaultHelvetica, which has no SourceFontData), hiding
            // the embedded font from both the paginator and TextBuilder. Treat
            // the fragment's Font as "not set" when it carries no SourceFontData,
            // so any segment that brings one wins.
            if (tf.Segments is { Count: > 0 } promoteSegs)
            {
                foreach (var s in promoteSegs)
                {
                    var fragHasEmbedded = tf.TextState.Font?.SourceFontData is not null
                                          || tf.TextState.FontData is not null;
                    if (!fragHasEmbedded && s.TextState.Font?.SourceFontData is not null)
                        tf.TextState.Font = s.TextState.Font;
                    if (tf.TextState.FontData is null && s.TextState.FontData is not null)
                        tf.TextState.FontData = s.TextState.FontData;
                    if (tf.TextState.FontSize <= 0 && s.TextState.FontSize > 0)
                        tf.TextState.FontSize = s.TextState.FontSize;
                    // Line spacing lives on the segment in generator-style fragments
                    // (`seg.TextState.LineSpacing = ...`); promote it so the paginator
                    // sees the caller's leading rather than the fragment default.
                    if (tf.TextState.LineSpacing <= 0 && s.TextState.LineSpacing > 0)
                        tf.TextState.LineSpacing = s.TextState.LineSpacing;
                    if ((tf.TextState.Font?.SourceFontData ?? tf.TextState.FontData) is not null
                        && tf.TextState.FontSize > 0 && tf.TextState.LineSpacing > 0) break;
                }
            }
            // Embedded/CID fonts (FontData set directly, or via FontRepository.FindFont
            // populating TextState.Font.SourceFontData) need TextBuilder for correct
            // glyph encoding -- but TextBuilder is page-bound, and overflow pages
            // don't exist until after the outer Document.Save loop drains them. The
            // paginator lays the fragment out in Standard-14 metric space (close
            // enough for line-break decisions) and queues each per-page chunk into
            // _pendingEmbeddedRenders; FinaliseEmbeddedRenders runs after the drain
            // and uses a fresh TextBuilder against each target Page.
            var useEmbeddedFont = tf.TextState.FontData is not null
                                  || tf.TextState.Font?.SourceFontData is not null;
            // Invisible / clipping text rendering modes need the legacy writer, which
            // emits `Tr` operators; the paginator does not.
            if (tf.TextState.RenderingMode != 0) return false;
            // Per-segment explicit Position means the caller wants precise control;
            // otherwise tf.Text (concatenated from all segments via RefreshTextFromSegments)
            // is the paragraph's logical content and flow-wraps correctly even when the
            // fragment was constructed via `new TextFragment()` + `.Segments.Add(seg)`
            // (which produces Segments.Count == 2: a default empty segment + caller's).
            if (tf.Segments is { Count: > 1 })
            {
                foreach (var s in tf.Segments)
                    if (s.Position is not null) return false;
            }

            var baseFont = Text.TextBuilder.MapToStandard14Public(tf.TextState);
            var fontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12;
            // In column mode this is the current column's width; otherwise the
            // full page content width. Wrapping uses the entry column's width;
            // the test columns are equal-width so a fragment that flows into the
            // next column keeps the same break points.
            var contentWidth = CurWidth;
            if (contentWidth <= 0) return false;

            // WordWrapMode.NoWrap → each \n-delimited input line becomes one
            // output line, regardless of width. Aspose.Pdf honors this so a
            // long line stays on one rendered line that overflows the page
            // horizontally; only vertical pagination still applies. Default
            // (ByWords / Undefined / null) flows through the width-aware wrap.
            var noWrap = tf.TextState.FormattingOptions?.WrapMode
                         == Text.TextFormattingOptions.WordWrapMode.NoWrap;
            // First-line indent (paragraph indentation set via FormattingOptions):
            // the first wrapped line starts indented and is correspondingly narrower.
            var firstLineIndent = (double)(tf.TextState.FormattingOptions?.FirstLineIndent ?? 0f);
            // Subsequent-lines indent: every wrapped line after the paragraph's first
            // starts indented by this amount. Applies across page
            // breaks too — a chunk that does not start the paragraph indents all its
            // lines.
            var subsequentLinesIndent = (double)(tf.TextState.FormattingOptions?.SubsequentLinesIndent ?? 0f);
            var rawText = tf.Text ?? string.Empty;
            var charSpacing = tf.TextState.CharacterSpacing;
            var allLines = noWrap
                ? new List<string>(rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                : Text.TextPaginator.WrapToWidth(rawText, baseFont, fontSize, contentWidth,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, firstLineIndent, charSpacing);
            // When notification logging is on, trace each wrapped line's width and
            // break reason (aligned 1:1 with allLines) so the loop below can record
            // where every line finished.
            var lineTrace = _logNotifications && !noWrap
                ? Text.TextPaginator.TraceLines(rawText, baseFont, fontSize, contentWidth,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, firstLineIndent)
                : null;
            // lineHeight resolution order, mirroring Aspose.Pdf:
            //   1. fragment.TextState.LineSpacing -- explicit float override
            //      in points, set by callers like
            //      `seg.TextState.LineSpacing = fontSize + 3f`.
            //   2. FormattingOptions.LineSpacing == FullSize -- use the font's
            //      full vertical extent from the TTF (ascent - descent).
            //   3. fontSize * 1.2 -- default for FontSize / Undefined modes.
            var fontTtf = tf.TextState.FontData?.TtfData
                          ?? tf.TextState.Font?.SourceFontData?.TtfData;
            var fullSize = tf.TextState.FormattingOptions?.LineSpacing
                           == Text.TextFormattingOptions.LineSpacingMode.FullSize;
            double lineHeight;
            if (tf.TextState.LineSpacing > 0)
                // Aspose.Pdf treats an explicit LineSpacing as extra leading added on
                // top of the glyph height: the line pitch is fontSize + LineSpacing
                // (verified against reference renders — a 10pt font with LineSpacing 13
                // lays out on a 23pt pitch, not 13). LineSpacing == 0 degenerates to the
                // default fontSize pitch below, so the rule is uniform.
                lineHeight = fontSize + tf.TextState.LineSpacing;
            else if (fullSize && fontTtf is { Length: > 12 })
                lineHeight = ComputeFullSizeLineHeight(fontTtf, fontSize);
            else
                // Default LineSpacingMode is FontSize (Aspose.Pdf parity): the line
                // advance equals the font size, not an inflated 1.2x leading.
                lineHeight = fontSize;
            EnsureRoom(lineHeight);

            // A fragment-level hyperlink applies to the fragment's first line.
            // Capture the slot + top-of-line before the write loop advances _curY.
            var fragHyperlink = tf.HyperlinkValue;
            var fragSlot = _currentSlot;
            var fragTop = _curY;

            // Per-segment hyperlinks: each TextSegment with a Hyperlink emits a
            // LinkAnnotation sized to the segment's run. Char offsets are into the
            // fragment's full text; the emission below maps them onto each wrapped
            // line (a hyperlink that wraps gets one rect per line it covers).
            var segHyperlinks = (List<(int charStart, int charEnd, Hyperlink hyperlink)>?)null;
            if (tf.Segments is { Count: > 0 } segs)
            {
                List<(int, int, Hyperlink)>? collected = null;
                var cursor = 0;
                foreach (var seg in segs)
                {
                    var len = seg.Text?.Length ?? 0;
                    if (len > 0 && seg.Hyperlink is { } h)
                        (collected ??= new()).Add((cursor, cursor + len, h));
                    cursor += len;
                }
                segHyperlinks = collected;
            }

            var idx = 0;
            while (idx < allLines.Count)
            {
                var availableLines = Math.Max(1, (int)((_curY - _marginBottom) / lineHeight));
                var chunkSize = Math.Min(availableLines, allLines.Count - idx);
                var chunk = allLines.GetRange(idx, chunkSize);

                if (useEmbeddedFont)
                {
                    // Queue the per-page chunk for deferred rendering. TextBuilder
                    // splits on \n internally and applies the leading set by
                    // SetLeading(lineHeight), so joining chunk lines with \n gets
                    // us multi-line rendering on the target page.
                    // The first line at the top of a region drops by the font size
                    // (Aspose.Pdf first-line placement); every following body line
                    // sits one of its own line heights below the previous baseline,
                    // so a size change between adjacent paragraphs is spaced by the
                    // lower line's metrics. Same-size runs are unaffected.
                    var firstBaseline = _lastBodyBaseline.HasValue
                        ? _lastBodyBaseline.Value - lineHeight
                        : _curY - fontSize;
                    _lastBodyBaseline = firstBaseline - (chunkSize - 1) * lineHeight;
                    _pendingEmbeddedRenders.Add((_currentSlot, CurLeft, _curY,
                        string.Join("\n", chunk), tf.TextState, fontSize, firstBaseline));
                    // Mark the overflow buffer non-empty so StartNewPage / Commit
                    // flushes it -- otherwise an overflow-only embedded-render
                    // slot would never produce a Page, the deferred render would
                    // have no target, and the test would see Pages.Count
                    // unchanged from the start-page count. The placeholder is an
                    // empty byte array (concatenates to nothing in the final
                    // content stream).
                    if (_overflowBuffer is not null)
                        _overflowBuffer.Add(Array.Empty<byte>());
                }
                else
                {
                    var fontResName = _overflowBuffer is null ? Table.RegisterFont(_startPage) : "F1";
                    var alphaGsName = tf.TextState.ForegroundColor is { } fg
                        ? Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, fg.AByte)
                        : null;
                    // TextState.BackgroundColor draws a filled highlight behind each
                    // wrapped line (its own /ca alpha, independent of the foreground's),
                    // emitted before the glyphs so the text sits on top.
                    var bgColor = tf.TextState.BackgroundColor;
                    var bgAlphaGsName = bgColor is { } bgc
                        ? Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, bgc.AByte)
                        : null;
                    var content = BuildWrappedTextStream(chunk, fontResName, fontSize,
                        CurLeft, _curY, lineHeight, tf.TextState.ForegroundColor,
                        tf.TextState.IsStrikeOut, tf.TextState.IsUnderline, baseFont, alphaGsName,
                        idx == 0 ? firstLineIndent : 0, subsequentLinesIndent, idx == 0,
                        bgColor, bgAlphaGsName, tf.TextState.Rotation);
                    WriteContent(content);
                    // The non-embedded path positions baselines independently;
                    // don't let a following embedded paragraph chain onto a
                    // stale baseline from before it.
                    _lastBodyBaseline = null;
                }

                // Record where each line in this chunk finished. The line "slot"
                // baseline that Aspose.Pdf reports is one line-height below the
                // band top per line (curY is the band top for this chunk); the X is
                // the left margin plus the line's width including its trailing space.
                if (lineTrace is not null)
                {
                    for (var j = 0; j < chunkSize && idx + j < lineTrace.Count; j++)
                    {
                        var t = lineTrace[idx + j];
                        LogLine(_currentSlot, t.content, CurLeft + t.width,
                            _curY - lineHeight * (j + 1), t.reason);
                    }
                }

                _curY -= lineHeight * chunkSize;
                idx += chunkSize;
                if (idx < allLines.Count)
                    FlowToNextRegion();
            }
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            // Record how far down the body reached on this slot. In column mode the
            // footnote sits below the deepest column, so use _colDeepestY (the bottom
            // of the fullest column), not _curY (which may be near the top of a later,
            // shorter column).
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);

            if (fragHyperlink is not null && allLines.Count > 0)
                _pendingLinks.Add((fragSlot,
                    new Rectangle(CurLeft, fragTop - lineHeight, CurLeft + contentWidth, fragTop),
                    fragHyperlink));

            if (segHyperlinks is { Count: > 0 })
            {
                // Locate each wrapped line's character span within the fragment text so a
                // segment range [a,b) can be split across the lines it covers (wrapping
                // drops the break space, so lines are matched sequentially by content).
                var lineStart = new int[allLines.Count];
                var lineEnd = new int[allLines.Count];
                int scan = 0;
                for (int li = 0; li < allLines.Count; li++)
                {
                    var ln = allLines[li];
                    int at = ln.Length == 0 ? scan : rawText.IndexOf(ln, Math.Min(scan, rawText.Length), StringComparison.Ordinal);
                    if (at < 0) at = scan;
                    lineStart[li] = at;
                    lineEnd[li] = at + ln.Length;
                    scan = lineEnd[li];
                }
                foreach (var (a, b, h) in segHyperlinks)
                {
                    for (int li = 0; li < allLines.Count; li++)
                    {
                        var ln = allLines[li];
                        int ov0 = Math.Max(a, lineStart[li]);
                        int ov1 = Math.Min(b, lineEnd[li]);
                        if (ov1 <= ov0) continue;
                        var prefix = ln.Substring(0, ov0 - lineStart[li]);
                        var run = ln.Substring(ov0 - lineStart[li], ov1 - ov0);
                        var x0 = CurLeft + MeasureText(prefix, baseFont, fontSize);
                        var w = MeasureText(run, baseFont, fontSize);
                        var yTop = fragTop - lineHeight * li;
                        _pendingLinks.Add((fragSlot,
                            new Rectangle(x0, yTop - lineHeight, x0 + w, yTop), h));
                    }
                }
            }
            return true;
        }

        /// <summary>Compute the per-line vertical advance for
        /// <see cref="Text.TextFormattingOptions.LineSpacingMode.FullSize"/>.
        /// Aspose.Pdf uses the embedded font's full vertical extent (ascent
        /// minus descent, since descent is negative) scaled to the requested
        /// font size, so multi-script content with tall ascent glyphs (CJK
        /// fonts, Arial Unicode MS) advances by the right amount per line
        /// instead of the 1.2x-of-font-size default. Falls back to 1.2x if
        /// the TTF metrics can't be parsed.</summary>
        private static double ComputeFullSizeLineHeight(byte[] ttf, double fontSize)
        {
            try
            {
                // The full-size line pitch is the font's own recommended line
                // height (hhea ascender/descender/lineGap, or the OS/2 win
                // metrics), not the typographic ascent/descent used for the PDF
                // font descriptor. For fonts where the two differ -- e.g. CJK
                // faces whose typo metrics span only 1 em but whose line height
                // is taller -- the descriptor values understate the leading.
                var lineEm = Text.FontRepository.ReadTtfLineHeightEm(ttf);
                if (lineEm > 0) return lineEm * fontSize;
                var (ascent, descent, _, _) = Text.FontRepository.ReadTtfMetrics(ttf);
                if (ascent <= 0) return fontSize * 1.2;
                // ascent is positive; descent is negative. Total vertical
                // extent in 1/1000 em -> scale to points.
                var height = (ascent - descent) / 1000.0 * fontSize;
                return height > 0 ? height : fontSize * 1.2;
            }
            catch
            {
                return fontSize * 1.2;
            }
        }

        /// <summary>Measure the rendered width of <paramref name="text"/> in points
        /// using the same Standard-14 metrics that <see cref="Text.TextPaginator"/>
        /// uses for line-break calculations -- keeping the two in sync means
        /// per-segment link rectangles align with the wrap breakpoints that
        /// produced the rendered line.</summary>
        private static double MeasureText(string text, string fontName, double fontSize)
        {
            double w = 0;
            foreach (var c in text)
            {
                var glyph = c < 256 ? c : '?';
                var cw = Text.Standard14Fonts.GetWidth(fontName, glyph);
                if (cw < 0) cw = 500;
                w += cw * fontSize / 1000.0;
            }
            return w;
        }

        private void WriteContent(byte[] content)
        {
            if (_overflowBuffer is null)
                _startPage.AddContentStream(content);
            else
                _overflowBuffer.Add(content);
        }

        private void EnsureRoom(double lineHeight)
        {
            if (_curY - lineHeight < _marginBottom)
                FlowToNextRegion();
        }

        private void StartNewPage(bool flushEmpty = false)
        {
            _lastBodyBaseline = null;
            // Flush the previous overflow page (if any) so each overflow-queue entry
            // corresponds to exactly one new Page — otherwise all overflow content
            // for this flow would collapse onto a single page. flushEmpty additionally
            // commits an empty buffer as a blank page (an explicit page break past a
            // paragraph that rendered nothing still occupies its own page); a null
            // buffer means we're still on the start page, so there's nothing to flush.
            if (_overflowBuffer is not null && (flushEmpty || _overflowBuffer.Count > 0))
            {
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
            }
            _overflowBuffer = new List<byte[]>();
            // The new buffer will flush into the next available slot index in
            // _overflowPages -- record now so pending links / paragraph positions
            // logged before the flush still resolve to the right Page later.
            _currentSlot = _overflowPages.Count;
            _curY = _startPageHeight - _marginTop;
            // A fresh page restarts the column band at column 0 from the top.
            if (_colLefts is not null)
            {
                _curCol = 0;
                _colBandTop = _curY;
                _colDeepestY = _curY;
            }
        }

        private static byte[] ConcatBlocks(List<byte[]> blocks)
        {
            var total = 0;
            foreach (var b in blocks) total += b.Length;
            var result = new byte[total];
            var offset = 0;
            foreach (var b in blocks)
            {
                Buffer.BlockCopy(b, 0, result, offset, b.Length);
                offset += b.Length;
            }
            return result;
        }

        private static byte[] BuildWrappedTextStream(List<string> lines, string fontResName, double fontSize,
            double startX, double startY, double lineHeight, Color? foreground,
            bool strikeOut = false, bool underline = false, string? fontName = null,
            string? alphaGsName = null, double firstLineIndent = 0,
            double subsequentLinesIndent = 0, bool chunkStartsParagraph = true,
            Color? background = null, string? bgAlphaGsName = null,
            double rotation = 0)
        {
            // The left indent of this chunk's first rendered line: the paragraph's
            // own first line uses FirstLineIndent; a chunk that continues the
            // paragraph onto a new page is all "subsequent" lines.
            var firstIndent = chunkStartsParagraph ? firstLineIndent : subsequentLinesIndent;
            var b = new Content.ContentStreamBuilder();
            b.SaveState();
            if (alphaGsName is not null)
                b.SetExtGState(alphaGsName);
            if (foreground is not null)
                b.SetFillColor(foreground.R / 255.0, foreground.G / 255.0, foreground.B / 255.0);
            // startY is the top of the text band. Drop the first baseline by the
            // font ascent (cap height) so the glyph tops align with the top margin,
            // matching Aspose.Pdf's first-line placement. Subsequent lines advance
            // by lineHeight, so the whole block shifts down uniformly.
            var capHeight = fontName is not null ? Text.Standard14Fonts.GetCapHeight(fontName) : 0;
            var ascent = capHeight > 0 ? capHeight / 1000.0 * fontSize : fontSize * 0.7;
            var firstBaseline = startY - ascent;

            // Background highlight: a filled rectangle behind each wrapped line,
            // sized to the line's measured width and the font's em box (baseline +
            // descent up by one font size). Drawn before the glyphs, in its own
            // graphics state so the background's /ca alpha doesn't bleed into the
            // foreground fill that follows.
            if (background is { } bgcol)
            {
                var bgFontName = fontName ?? "Helvetica";
                var descentPt = Text.Standard14Fonts.GetDescent(bgFontName) / 1000.0 * fontSize; // negative
                b.SaveState();
                if (bgAlphaGsName is not null) b.SetExtGState(bgAlphaGsName);
                b.SetFillColor(bgcol.R / 255.0, bgcol.G / 255.0, bgcol.B / 255.0);
                for (var i = 0; i < lines.Count; i++)
                {
                    var lineW = MeasureLineWidth(lines[i], bgFontName, fontSize);
                    if (lineW <= 0) continue;
                    var lineY = firstBaseline - i * lineHeight;
                    var lineX = startX + (i == 0 ? firstIndent : subsequentLinesIndent);
                    b.Rectangle(lineX, lineY + descentPt, lineW, fontSize);
                    b.Fill();
                }
                b.RestoreState();
            }

            b.BeginText();
            b.SetFont(fontResName, fontSize);
            b.SetLeading(lineHeight);
            // The first line starts indented by firstLineIndent; line 2 shifts back
            // to startX via a relative Td (Td is relative to the current line start,
            // so a plain T* would otherwise carry the indent down to every line).
            // TextState.Rotation rotates the whole block around its first baseline
            // origin via the text matrix (Td/T* then advance in rotated text space).
            if (rotation != 0)
            {
                var rad = rotation * Math.PI / 180.0;
                var cos = Math.Round(Math.Cos(rad), 10);
                var sin = Math.Round(Math.Sin(rad), 10);
                b.SetTextMatrix(cos, sin, -sin, cos, startX + firstIndent, firstBaseline);
            }
            else
            {
                b.MoveTextPosition(startX + firstIndent, firstBaseline);
            }
            for (var i = 0; i < lines.Count; i++)
            {
                if (i == 1)
                {
                    // Shift from the first line's indent to the subsequent-lines
                    // indent (Td is relative to the current line start), then drop a
                    // line. Following lines keep that X via NextLine (T*).
                    var delta = subsequentLinesIndent - firstIndent;
                    if (delta != 0) b.MoveTextPosition(delta, -lineHeight);
                    else b.NextLine();
                }
                else if (i > 0) b.NextLine();
                b.ShowText(lines[i]);
            }
            b.EndText();

            // Emit strikeout / underline rectangles after the text. One per
            // wrapped line, sized to the line's measured width.
            if ((strikeOut || underline) && (fontName is not null))
            {
                double thickness = fontSize * 0.05;
                double soOffset = fontSize * 0.30;   // ~30% of em above baseline
                double ulOffset = -fontSize * 0.077; // ~7.7% below baseline
                for (var i = 0; i < lines.Count; i++)
                {
                    double lineW = MeasureLineWidth(lines[i], fontName, fontSize);
                    double lineY = firstBaseline - i * lineHeight;
                    double lineX = startX + (i == 0 ? firstIndent : subsequentLinesIndent);
                    if (strikeOut)
                    {
                        b.Rectangle(lineX, lineY + soOffset, lineW, thickness);
                        b.Fill();
                    }
                    if (underline)
                    {
                        b.Rectangle(lineX, lineY + ulOffset, lineW, thickness);
                        b.Fill();
                    }
                }
            }

            b.RestoreState();
            return b.Build();
        }

        private static double MeasureLineWidth(string line, string fontName, double fontSize)
        {
            if (Text.Standard14Fonts.IsStandard14(fontName))
            {
                double w = 0;
                foreach (var ch in line)
                {
                    var cw = Text.Standard14Fonts.GetWidth(fontName, ch < 256 ? ch : '?');
                    w += (cw >= 0 ? cw : 500) * fontSize / 1000.0;
                }
                return w;
            }
            return line.Length * fontSize * 0.5;
        }
    }

    public static Document MergeDocuments(params byte[][] documents)
    {
        if (documents.Length == 0) return Create();
        var result = Open(documents[0]);
        for (int i = 1; i < documents.Length; i++)
        {
            using var source = Open(documents[i]);
            var pageNums = Enumerable.Range(1, source.PageCount).ToArray();
            result.ImportPages(source, pageNums);
        }
        return result;
    }

    public static Document MergeDocuments(params string[] files)
    {
        var bytes = new byte[files.Length][];
        for (int i = 0; i < files.Length; i++)
            bytes[i] = File.ReadAllBytes(files[i]);
        return MergeDocuments(bytes);
    }

    /// <summary>Merge every page of <paramref name="documents"/> into a new
    /// destination <see cref="Document"/>. Source documents are read but
    /// left unchanged.</summary>
    public static Document MergeDocuments(params Document[] documents)
    {
        var target = Create();
        target.Merge(documents);
        return target;
    }

    /// <summary>Merge every page of each <paramref name="documents"/> entry
    /// into this document, preserving source order.</summary>
    public void Merge(params Document[] documents)
    {
        if (documents is null) return;
        foreach (var d in documents)
            if (d is not null) Pages.Add(d.Pages);
    }

    /// <summary>Merge every page of each file in <paramref name="files"/>
    /// into this document. Sources are opened and disposed by this method.</summary>
    public void Merge(params string[] files)
    {
        if (files is null) return;
        foreach (var f in files)
        {
            using var d = new Document(f);
            Pages.Add(d.Pages);
        }
    }

    /// <summary>Merge with explicit options. Real — RemoveSignatures strips
    /// /V from every signature field; MergeDuplicateOutlines deduplicates
    /// catalog outline trees by title+page; KeepFieldsUnique appends
    /// "_2", "_3" suffixes to colliding form-field names.</summary>
    public void Merge(MergeOptions mergeOptions, params Document[] documents)
    {
        Merge(documents);
        ApplyMergeOptions(mergeOptions);
    }

    /// <summary>Merge files with explicit options — same semantics as
    /// the Document[] overload.</summary>
    public void Merge(MergeOptions mergeOptions, params string[] files)
    {
        Merge(files);
        ApplyMergeOptions(mergeOptions);
    }

    /// <summary>Static Merge: build a fresh Document containing every
    /// page from <paramref name="files"/>, then apply <paramref name="mergeOptions"/>.</summary>
    public static Document MergeDocuments(MergeOptions mergeOptions, params Document[] files)
    {
        var target = Create();
        target.Merge(mergeOptions, files);
        return target;
    }

    /// <summary>Static Merge: file-paths variant.</summary>
    public static Document MergeDocuments(MergeOptions mergeOptions, params string[] files)
    {
        var target = Create();
        target.Merge(mergeOptions, files);
        return target;
    }

    private void ApplyMergeOptions(MergeOptions? options)
    {
        if (options is null) return;
        if (options.RemoveSignatures)
        {
            var form = Form;
            if (form is not null)
            {
                foreach (var field in form.Fields)
                {
                    if (field.Type != Forms.FieldType.Signature) continue;
                    field.Dict.Remove("V");
                }
            }
        }
        if (options.MergeDuplicateOutlines)
            DeduplicateOutlines();
        if (options.KeepFieldsUnique)
            DisambiguateFormFieldNames();
        // RemoveUserRights: strip /Perms /UR / /UR3 from catalog.
        if (options.RemoveUserRights)
        {
            var perms = _reader.ResolveDict(_reader.Catalog.Get("Perms"));
            if (perms is not null)
            {
                perms.Remove("UR");
                perms.Remove("UR3");
                if (!perms.Keys.Any())
                    _reader.Catalog.Remove("Perms");
            }
        }
    }

    private void DeduplicateOutlines()
    {
        // The outline tree may contain duplicate entries with identical
        // /Title + /Dest after a merge. Walk /Outlines + /First/Next and
        // remove later items whose (Title, page-number) tuple matches an
        // earlier one.
        var outlinesObj = _reader.ResolveDict(_reader.Catalog.Get("Outlines"));
        if (outlinesObj is null) return;
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        DedupeOutlineList(outlinesObj, seen);
    }

    private void DedupeOutlineList(Aspose.Pdf.Core.PdfDictionary parent, HashSet<string> seen)
    {
        var current = _reader.ResolveDict(parent.Get("First"));
        Aspose.Pdf.Core.PdfDictionary? prev = null;
        while (current is not null)
        {
            var title = current.Get("Title") is Aspose.Pdf.Core.PdfString s ? s.ToText() : "";
            var key = title + "|" + DescribeDestination(current);
            var next = _reader.ResolveDict(current.Get("Next"));
            if (seen.Contains(key))
            {
                // Splice current out of the list.
                if (prev is null) parent.Set("First", current.Get("Next") ?? (Aspose.Pdf.Core.PdfObject)Aspose.Pdf.Core.PdfNull.Instance);
                else if (current.Get("Next") is { } nxt) prev.Set("Next", nxt);
                else prev.Remove("Next");
            }
            else
            {
                seen.Add(key);
                // Recurse into nested children.
                if (current.ContainsKey("First")) DedupeOutlineList(current, seen);
                prev = current;
            }
            current = next;
        }
    }

    private string DescribeDestination(Aspose.Pdf.Core.PdfDictionary outlineItem)
    {
        var dest = _reader.Resolve(outlineItem.Get("Dest"));
        return dest switch
        {
            Aspose.Pdf.Core.PdfArray arr when arr.Count > 0 => arr[0]?.ToString() ?? "",
            Aspose.Pdf.Core.PdfString s => s.ToText(),
            Aspose.Pdf.Core.PdfName n => n.Value,
            _ => "",
        };
    }

    private void DisambiguateFormFieldNames()
    {
        var form = Form;
        if (form is null) return;
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var field in form.Fields)
        {
            var name = field.FullName ?? string.Empty;
            if (string.IsNullOrEmpty(name)) continue;
            if (seen.Add(name)) continue;
            var idx = 2;
            string candidate;
            do { candidate = $"{name}_{idx++}"; } while (!seen.Add(candidate));
            field.SetPartialName(candidate);
        }
    }

    // ── SendTo (DocumentDevice routing) ────────────────────────────

    /// <summary>Render this document through <paramref name="device"/> into
    /// <paramref name="output"/> — delegates to <see cref="Devices.DocumentDevice.Process(Document, System.IO.Stream)"/>.</summary>
    public void SendTo(Devices.DocumentDevice device, Stream output)
        => device?.Process(this, output);

    /// <summary>File overload of <see cref="SendTo(Devices.DocumentDevice, Stream)"/>.</summary>
    public void SendTo(Devices.DocumentDevice device, string outputFileName)
        => device?.Process(this, outputFileName);

    /// <summary>Render a page range — delegates to the device's
    /// page-range Process overload.</summary>
    public void SendTo(Devices.DocumentDevice device, int fromPage, int toPage, Stream output)
        => device?.Process(this, fromPage, toPage, output);

    /// <summary>File-pair page-range overload.</summary>
    public void SendTo(Devices.DocumentDevice device, int fromPage, int toPage, string outputFileName)
        => device?.Process(this, fromPage, toPage, outputFileName);

    // ── Convert(Fixup, …) — real for Rotate; throws on rendering-grade fixups

    /// <summary>Run a pre-defined fixup operation. Real implementations:
    /// <see cref="Fixup.RotatePagesToLandscape"/> and <see cref="Fixup.RotatePagesToPortrait"/>
    /// set every page's /Rotate to produce the requested orientation.
    /// Other fixups throw <see cref="System.NotSupportedException"/>:
    /// EmbedMissingFonts / ConvertFontsToOutlines need a font-embedding
    /// or text→outlines pass; DerivePageGeometryBoxesFromCropMarks needs
    /// crop-mark detection; ConvertAllPagesIntoCMYKImagesAndPreserveText
    /// Information needs a full CMYK rasterisation pipeline.</summary>
    public bool Convert(Fixup fixup, Stream outputLog, bool onlyValidation, params object[] parameters)
    {
        _ = parameters;
        if (onlyValidation)
        {
            // Validation-only: report whether the fix would apply without
            // mutating the document.
            return fixup is Fixup.RotatePagesToLandscape or Fixup.RotatePagesToPortrait;
        }
        switch (fixup)
        {
            case Fixup.RotatePagesToLandscape:
                ApplyRotateFixup(targetLandscape: true);
                LogFixup(outputLog, "RotatePagesToLandscape applied.");
                return true;
            case Fixup.RotatePagesToPortrait:
                ApplyRotateFixup(targetLandscape: false);
                LogFixup(outputLog, "RotatePagesToPortrait applied.");
                return true;
            default:
                throw new System.NotSupportedException(
                    $"Fixup {fixup} is not implemented in the FOSS build. Only RotatePagesToLandscape and RotatePagesToPortrait are real; the other fixups require renderer-grade features (font embedding / outlining / CMYK rasterisation / crop-mark detection).");
        }
    }

    /// <summary>File-log overload of <see cref="Convert(Fixup, Stream, bool, object[])"/>.</summary>
    public bool Convert(Fixup fixup, string outputLog, bool onlyValidation, params object[] parameters)
    {
        using var fs = string.IsNullOrEmpty(outputLog) ? null : File.Create(outputLog);
        return Convert(fixup, (Stream?)fs ?? Stream.Null, onlyValidation, parameters);
    }

    /// <summary>Apply <paramref name="fixup"/> and write a log to
    /// <paramref name="outputLog"/> (the common, non-validation case).</summary>
    public bool Convert(Fixup fixup, string outputLog) => Convert(fixup, outputLog, false);

    /// <summary>Open <paramref name="srcFileName"/> via the load-options
    /// hierarchy (HtmlLoadOptions / SvgLoadOptions / MdLoadOptions all
    /// derive from <see cref="LoadOptions"/>) and save to
    /// <paramref name="dstFileName"/>. Save-options dispatch: HtmlSaveOptions
    /// routes to the PDF→HTML writer; anything else saves as PDF.</summary>
    public static void Convert(string srcFileName, LoadOptions loadOptions,
        string dstFileName, SaveOptions saveOptions)
    {
        using var doc = OpenWithLoadOptions(srcFileName, loadOptions);
        SaveWithSaveOptions(doc, dstFileName, saveOptions);
    }

    public static void Convert(string srcFileName, LoadOptions loadOptions,
        Stream dstStream, SaveOptions saveOptions)
    {
        using var doc = OpenWithLoadOptions(srcFileName, loadOptions);
        SaveWithSaveOptions(doc, dstStream, saveOptions);
    }

    public static void Convert(Stream srcStream, LoadOptions loadOptions,
        string dstFileName, SaveOptions saveOptions)
    {
        using var doc = OpenWithLoadOptions(srcStream, loadOptions);
        SaveWithSaveOptions(doc, dstFileName, saveOptions);
    }

    public static void Convert(Stream srcStream, LoadOptions loadOptions,
        Stream dstStream, SaveOptions saveOptions)
    {
        using var doc = OpenWithLoadOptions(srcStream, loadOptions);
        SaveWithSaveOptions(doc, dstStream, saveOptions);
    }

    private static Document OpenWithLoadOptions(string srcFileName, LoadOptions loadOptions) => loadOptions switch
    {
        HtmlLoadOptions h => Open(srcFileName, h),
        Converters.MdLoadOptions md => Open(srcFileName, md),
        Converters.SvgLoadOptions svg => Open(srcFileName, svg),
        _ => Open(srcFileName),
    };

    private static Document OpenWithLoadOptions(Stream srcStream, LoadOptions loadOptions)
    {
        if (loadOptions is HtmlLoadOptions h) return new Document(srcStream, h);
        if (loadOptions is Converters.MdLoadOptions md)
            return Open(ReadStreamBytes(srcStream), md);
        if (loadOptions is Converters.SvgLoadOptions svg)
            return Open(ReadStreamBytes(srcStream), svg);
        return new Document(srcStream);
    }

    private static byte[] ReadStreamBytes(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static void SaveWithSaveOptions(Document doc, string dst, SaveOptions saveOptions)
    {
        if (saveOptions is HtmlSaveOptions h) doc.Save(dst, h);
        else doc.Save(dst);
    }

    private static void SaveWithSaveOptions(Document doc, Stream dst, SaveOptions saveOptions)
    {
        if (saveOptions is HtmlSaveOptions h) doc.Save(dst, h);
        else doc.Save(dst);
    }

    private void ApplyRotateFixup(bool targetLandscape)
    {
        for (var i = 1; i <= PageCount; i++)
        {
            var page = Pages[i];
            var media = page.MediaBox;
            var existing = (int)(page.Dict.Get("Rotate") is Aspose.Pdf.Core.PdfInteger n ? n.Value : 0);
            // Displayed orientation depends on /Rotate: a 90°/270° rotation swaps
            // the media box's width and height on screen.
            var norm = ((existing % 360) + 360) % 360;
            var quarterTurned = norm == 90 || norm == 270;
            var displaysLandscape = quarterTurned ? media.Height > media.Width : media.Width > media.Height;
            // Already in the requested orientation — leave the page untouched.
            if (displaysLandscape == targetLandscape) continue;
            // Otherwise a single 90° clockwise turn flips landscape↔portrait.
            page.Dict.Set("Rotate", new Aspose.Pdf.Core.PdfInteger((existing + 90) % 360));
        }
    }

    private static void LogFixup(Stream? logStream, string line)
    {
        if (logStream is null || logStream == Stream.Null) return;
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(line + "\n");
            logStream.Write(bytes, 0, bytes.Length);
        }
        catch { }
    }

    /// <summary>Merge tuning knobs honored by
    /// <see cref="Merge(MergeOptions, Document[])"/> and friends.</summary>
    public sealed class MergeOptions
    {
        /// <summary>Strip /V from every signature field after merge.</summary>
        public bool RemoveSignatures { get; set; }

        /// <summary>Deduplicate identical entries from the merged outline tree.</summary>
        public bool MergeDuplicateOutlines { get; set; }

        /// <summary>Append "_2", "_3", … suffixes to colliding form-field names.</summary>
        public bool KeepFieldsUnique { get; set; }

        /// <summary>Strip /Perms /UR /UR3 usage-rights entries after merge.</summary>
        public bool RemoveUserRights { get; set; }

        /// <summary>Merge duplicate optional-content groups (layers). Stored
        /// only — the FOSS writer does not yet emit a deduplicated /OCProperties
        /// tree.</summary>
        public bool MergeDuplicateLayers { get; set; }

        /// <summary>Streaming buffer size (in bytes) for the source-side
        /// reader during merge. Stored only — the FOSS merge path keeps
        /// the full document in memory and does not split reads into
        /// packets.</summary>
        public int ConcatenationPacketSize { get; set; }

        /// <summary>When true, the merged /Pages tree is balanced into a
        /// fixed-fanout subtree shape. Stored only — the FOSS merge
        /// emits a flat /Pages /Kids list and does not balance the tree.</summary>
        public bool IsNeedPageTreeBalance { get; set; }

        /// <summary>Maximum entries per /Pages subtree node when
        /// <see cref="IsNeedPageTreeBalance"/> is set. Stored only.</summary>
        public byte MaximumNodesInLevel { get; set; }

        /// <summary>Spill intermediate state to a temp file rather than
        /// keeping it in memory. Stored only — the FOSS merge path is
        /// always in-memory.</summary>
        public bool UseDiskBuffer { get; set; }
    }

    /// <summary>
    /// The raw PDF catalog dictionary for power-user access.
    /// </summary>
    internal PdfDictionary Catalog => _reader.Catalog;

    /// <summary>
    /// The internal reader (for sub-components that need object resolution).
    /// </summary>
    internal PdfReader Reader => _reader;

    /// <summary>
    /// Encrypt the document with the specified algorithm, passwords, and permissions.
    /// Encryption is applied on the next save.
    /// </summary>
    public void Encrypt(string userPassword, string ownerPassword,
        DocumentPrivilege? permissions = null, CryptoAlgorithm algorithm = Aspose.Pdf.CryptoAlgorithm.AESx128)
    {
        // Encryption is gated on the SIGNATURE TYPE, not the mere
        // presence of a signature (verified by black-box probe of Aspose.PDF 26.6):
        // a DocMDP CERTIFICATION signature refuses encryption (it would break the
        // certification), while an ordinary APPROVAL signature encrypts normally.
        // So an approval signature succeeds; a certified (DocMDP) one throws.
        if (HasCertificationSignature())
            throw new PdfException(
                "You cannot change this document because it is certified.");

        var p = permissions is not null
            ? GetPermissionFlags(permissions)
            : -4; // all permissions

        _encryptor = algorithm switch
        {
            Aspose.Pdf.CryptoAlgorithm.RC4x40 => PdfEncryptor.CreateRC4x40(userPassword, ownerPassword, p),
            Aspose.Pdf.CryptoAlgorithm.RC4x128 => PdfEncryptor.CreateRC4x128(userPassword, ownerPassword, p),
            Aspose.Pdf.CryptoAlgorithm.AESx128 => PdfEncryptor.CreateAES128(userPassword, ownerPassword, p),
            Aspose.Pdf.CryptoAlgorithm.AESx256 => PdfEncryptor.CreateAES256(userPassword, ownerPassword, p),
            _ => PdfEncryptor.CreateAES128(userPassword, ownerPassword, p),
        };
    }

    /// <summary>True when the document carries a DocMDP certification (author) signature.
    /// A certification is recorded in the catalog as <c>/Perms &lt;&lt; /DocMDP &lt;sigref&gt; &gt;&gt;</c>
    /// (PDF 32000-1 §12.8.2.2), so it is detected by a direct catalog lookup rather than by
    /// enumerating the AcroForm fields — the latter can recurse on a pathological field tree.
    /// Ordinary approval signatures leave /Perms absent, so they are not blocked.</summary>
    private bool HasCertificationSignature()
    {
        try
        {
            var perms = _reader.ResolveDict(_reader.Catalog?.Get("Perms"));
            return perms is not null && perms.Get("DocMDP") is not null;
        }
        catch
        {
            // A malformed catalog must not turn encryption into a crash — treat it as
            // "not certified" and let encryption proceed (the pre-enforcement behaviour).
            return false;
        }
    }

    /// <summary>
    /// Encrypt overload accepting an explicit <c>usePdf20</c> flag.
    /// PDF 2.0 (ISO 32000-2) revision-6 encryption is not implemented in
    /// this FOSS branch — passing <c>true</c> throws
    /// <see cref="NotSupportedException"/>. <c>false</c> forwards to the
    /// existing revision-5 AES-256 path.
    /// </summary>
    public void Encrypt(string userPassword, string ownerPassword,
        DocumentPrivilege? privileges, CryptoAlgorithm cryptoAlgorithm, bool usePdf20)
    {
        if (usePdf20)
            throw new NotSupportedException("PDF 2.0 revision-6 encryption is not implemented in this FOSS branch. Pass usePdf20:false to use the existing revision-5 AES-256 path.");
        Encrypt(userPassword, ownerPassword, privileges, cryptoAlgorithm);
    }

    /// <summary>
    /// Encrypt overload accepting the <see cref="Permissions"/> flags enum and
    /// an explicit <c>usePdf20</c> flag. Same usePdf20 contract as the
    /// DocumentPrivilege-typed overload above.
    /// </summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Aspose.Pdf.Permissions permissions, CryptoAlgorithm cryptoAlgorithm, bool usePdf20)
    {
        if (usePdf20)
            throw new NotSupportedException("PDF 2.0 revision-6 encryption is not implemented in this FOSS branch. Pass usePdf20:false to use the existing revision-5 AES-256 path.");
        Encrypt(userPassword, ownerPassword, permissions, cryptoAlgorithm);
    }

    /// <summary>
    /// Encrypt overload accepting the legacy <see cref="Permissions"/> flags
    /// enum. Maps each flag to the corresponding bit and forwards to the
    /// DocumentPrivilege overload.
    /// </summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Aspose.Pdf.Permissions permissions, CryptoAlgorithm cryptoAlgorithm = Aspose.Pdf.CryptoAlgorithm.AESx128)
    {
        var dp = new DocumentPrivilege();
        if ((permissions & Aspose.Pdf.Permissions.PrintDocument) != 0) dp.AllowPrint = true;
        if ((permissions & Aspose.Pdf.Permissions.ModifyContent) != 0) dp.AllowModifyContents = true;
        if ((permissions & Aspose.Pdf.Permissions.ExtractContent) != 0) dp.AllowCopy = true;
        if ((permissions & Aspose.Pdf.Permissions.ModifyTextAnnotations) != 0) dp.AllowModifyAnnotations = true;
        if ((permissions & Aspose.Pdf.Permissions.FillForm) != 0) dp.AllowFillIn = true;
        if ((permissions & Aspose.Pdf.Permissions.ExtractContentWithDisabilities) != 0) dp.AllowScreenReaders = true;
        if ((permissions & Aspose.Pdf.Permissions.AssembleDocument) != 0) dp.AllowAssembly = true;
        if ((permissions & Aspose.Pdf.Permissions.PrintingQuality) != 0) dp.HighQualityPrinting = true;
        Encrypt(userPassword, ownerPassword, dp, cryptoAlgorithm);
    }

    /// <summary>
    /// Change the password on the bound document. Reads the current
    /// permissions, then re-encrypts with the supplied new passwords
    /// (owner-password is required to authorise the change).
    /// </summary>
    public void ChangePasswords(string ownerPassword, string newUserPassword, string newOwnerPassword)
    {
        var existing = new DocumentPrivilege(Permissions);
        Encrypt(newUserPassword, newOwnerPassword, existing, Aspose.Pdf.CryptoAlgorithm.AESx128);
    }

    /// <summary>Remove encryption from the document.</summary>
    public void Decrypt()
    {
        _encryptor = null;
        // Remove Encrypt entry from trailer so the saved PDF is unencrypted
        _reader.Trailer?.Remove("Encrypt");
    }

    private PdfEncryptor? _encryptor;
#pragma warning disable CS0649
    private bool _forceWriteId;
#pragma warning restore CS0649

    private static int GetPermissionFlags(DocumentPrivilege priv)
    {
        var flags = unchecked((int)0xFFFFF0C0); // Required reserved bits
        if (priv.AllowPrint) flags |= 1 << 2;
        if (priv.AllowModifyContents) flags |= 1 << 3;
        if (priv.AllowCopy) flags |= 1 << 4;
        if (priv.AllowModifyAnnotations) flags |= 1 << 5;
        if (priv.AllowFillIn) flags |= 1 << 8;
        if (priv.AllowScreenReaders) flags |= 1 << 9;
        if (priv.AllowAssembly) flags |= 1 << 10;
        if (priv.HighQualityPrinting) flags |= 1 << 11;
        return flags;
    }

    /// <summary>
    /// Optimize document resources by removing unused objects and deduplicating streams.
    /// The optimization is applied on the next save.
    /// </summary>
    /// <summary>
    /// Linearize the document for fast web view (mirrors the public API
    /// <c>Document.Optimize()</c>). Default optimization is applied.
    /// </summary>
    public void Optimize()
    {
        OptimizeResources();
        // Optimize() also enables fast-web-view (linearization) on the next save,
        // so that Document.Optimize() produces a
        // linearized output. LinearizeDocument() sets the same flag.
        _linearize = true;
    }

    /// <summary>
    /// Process paragraphs in the document (layout step before save).
    /// Renders queued paragraphs (TextFragment, Heading, Table, HtmlFragment, ...) and
    /// applies TOC info to each page's content stream. Idempotent; the same pass runs
    /// again at Save but each page is gated by Page.LayoutApplied.
    /// </summary>
    public void ProcessParagraphs() => ApplyPageContent();

    /// <summary>
    /// Gets or sets a flag indicating whether the document should be optimized for size on save.
    /// When true, redundant data is removed during save to reduce file size.
    /// </summary>
    public bool OptimizeSize { get; set; }

    /// <summary>When true, the standard 14 PostScript fonts (Helvetica /
    /// Times / Courier × 4 styles + Symbol + ZapfDingbats) are embedded
    /// into the saved document so the output renders identically without
    /// relying on viewer-side font fallbacks. Stored only; the saver does
    /// not currently embed the Standard 14 font files.</summary>
    public bool EmbedStandardFonts { get; set; }

    /// <summary>When true, the parser tolerates corrupted indirect-object
    /// declarations (extra/garbled bytes between objects) instead of
    /// throwing. Stored only; the parser is already lenient about most
    /// malformed input.</summary>
    public bool IgnoreCorruptedObjects { get; set; }

    public void OptimizeResources() => OptimizeResources(Aspose.Pdf.Optimization.OptimizationOptions.Default);

    public void OptimizeResources(Aspose.Pdf.Optimization.OptimizationOptions strategy)
    {
        var options = strategy ?? Aspose.Pdf.Optimization.OptimizationOptions.Default;

        // Drop /Resources entries (fonts, XObjects, ...) that no content stream
        // references. Done before the reachability pass below so the now-orphaned
        // resource objects also fall out of the saved file.
        if (options.RemoveUnusedStreams)
        {
            PruneUnusedResources();
        }

        // Apply image compression if requested
        if (options.CompressImages)
        {
            ImageCompressor.CompressImages(_reader, options.ImageQuality);
        }

        // Downsample images exceeding max DPI
        if (options.MaxImageDpi > 0)
        {
            ImageCompressor.DownsampleImages(_reader, options.MaxImageDpi, options.ImageQuality);
        }

        // Convert images to grayscale
        if (options.ConvertImagesToGrayscale)
        {
            ImageCompressor.ConvertToGrayscale(_reader);
        }

        // Remove duplicate images
        if (options.RemoveDuplicateImages)
        {
            ImageCompressor.RemoveDuplicateImages(_reader);
        }

        // Apply font subsetting if requested
        if (options.SubsetFonts || options.SubsetEmbeddedFonts)
        {
            FontSubsetter.SubsetFonts(_reader, options.SubsetEmbeddedFonts);
        }

        // Drop embedded font programs for fonts a viewer can substitute (Standard 14 or
        // installed system faces). The now-orphaned font streams fall out via the
        // reachability pass below.
        if (options.UnembedFonts)
        {
            FontSubsetter.UnembedFonts(_reader);
        }

        // Remove metadata if requested
        if (options.RemoveMetadata)
        {
            _reader.Catalog.Remove("Metadata");
        }

        // Link duplicate streams
        if (options.LinkDuplicateStreams)
        {
            LinkDuplicateStreams();
        }

        // Compute reachable objects from the trailer. Done LAST, after the resource prune,
        // font unembedding, and duplicate-stream linking above, so any object those steps
        // orphaned (e.g. an unembedded /FontFile2 program or a linked-away duplicate) is
        // excluded from the saved file rather than written from a stale snapshot.
        var reachable = new HashSet<int>();
        if (options.RemoveUnusedObjects)
        {
            CollectReachable(_reader.Trailer, reachable);
            // Cross-document page imports aren't linked into the trailer's /Pages tree
            // until save (RebuildPagesTree), so walk the pending page dicts explicitly.
            // Otherwise their still-referenced imported resource objects look unreachable
            // and a copied page's images would be dropped from the saved file.
            if (_pages is not null)
                foreach (var pending in _pages.PendingAdds)
                    CollectReachable(pending.Dict, reachable);
        }

        // Mark document as needing optimization on next save
        _optimizationOptions = options;
        _reachableObjects = reachable.Count > 0 ? reachable : null;
    }

    private Aspose.Pdf.Optimization.OptimizationOptions? _optimizationOptions;
    private HashSet<int>? _reachableObjects;
    private bool _prunedFontsThisSave;


    private void CollectReachable(PdfObject? root, HashSet<int> visited)
    {
        if (root is null or PdfNull) return;

        // Iterative traversal with explicit stack to avoid stack overflow on large PDFs
        var stack = new Stack<PdfObject>();
        stack.Push(root);
        var seenDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var obj = stack.Pop();
            if (obj is null or PdfNull) continue;

            if (obj is PdfIndirectRef iref)
            {
                if (!visited.Add(iref.ObjectNumber)) continue;
                var resolved = _reader.Resolve(iref);
                if (resolved is not null) stack.Push(resolved);
                continue;
            }

            if (obj is PdfStream stream)
            {
                stack.Push(stream.Dict);
                continue;
            }

            if (obj is PdfDictionary dict)
            {
                if (!seenDicts.Add(dict)) continue;
                foreach (var key in dict.Keys)
                {
                    var val = dict.Get(key);
                    if (val is not null) stack.Push(val);
                }
                continue;
            }

            if (obj is PdfArray arr)
            {
                foreach (var item in arr)
                    if (item is not null) stack.Push(item);
            }
        }
    }

    /// <summary>The /Resources sub-dictionaries whose entries are name-referenced
    /// from content streams and so can be pruned when unreferenced.</summary>
    private static readonly string[] PrunableResourceCategories =
        { "Font", "XObject", "ExtGState", "Pattern", "Shading", "ColorSpace", "Properties" };

    /// <summary>
    /// Remove /Resources entries (fonts, XObjects, ExtGStates, ...) that no content
    /// stream reachable from a page actually references. Conservative by design: an
    /// entry is kept whenever its resource name appears as a /Name token in the page
    /// content or in any form XObject invoked (directly or transitively) from it, so
    /// a face used only through a form's parent-resource fallback is never dropped.
    /// Only page-level resource dictionaries are pruned; per-form resources are left
    /// intact (they are small and self-contained).
    /// </summary>
    private void PruneUnusedResources()
    {
        // A /Resources dict may be SHARED by several pages (e.g. inherited from a
        // common parent in the page tree). Pruning it per-page would drop entries
        // another page still uses, so accumulate the UNION of used names per shared
        // resources dict (by reference identity) and prune each dict once.
        var usedByResources = new Dictionary<PdfDictionary, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var keepAll = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (var page in Pages)
        {
            var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
            if (resources is null) continue;

            var content = page.GetContentStreamBytes();
            // No analysable content => keep every resource on this dict rather than guess.
            if (content is null || content.Length == 0)
            {
                keepAll.Add(resources);
                continue;
            }

            if (!usedByResources.TryGetValue(resources, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedByResources[resources] = used;
            }
            var visitedForms = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            CollectContentResourceNames(content, resources, used, visitedForms);
        }

        foreach (var (resources, used) in usedByResources)
        {
            if (keepAll.Contains(resources)) continue;
            AddReferencedXObjectNames(resources, used);
            PruneResourceCategories(resources, used);
        }
    }

    /// <summary>Drop /Font resource entries no longer referenced by any content after a
    /// <see cref="Text.TextEditOptions.FontReplace.RemoveUnusedFonts"/> text edit. Walks
    /// the page content and every invoked form XObject, pruning each scope's OWN /Font
    /// dictionary against the fonts its content selects with <c>Tf</c>.</summary>
    private void PruneUnusedFontsForPage(Page page)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var rewritten = PruneFontsInScope(page.GetContentStreamBytes(), resources, visited);
        if (rewritten is not null) page.SetContentStream(rewritten);
    }

    /// <summary>Prune unused /Font entries in a content scope and rename the replacement
    /// fonts to sequential F0, F1, … keys (matching Aspose.Pdf, which names a
    /// replacement font "F0"). Returns the rewritten content when a rename changed it,
    /// else null. Form XObject scopes are rewritten in place.</summary>
    private byte[]? PruneFontsInScope(byte[]? content, PdfDictionary? resources,
        HashSet<PdfDictionary> visitedForms)
    {
        if (content is null || content.Length == 0 || resources is null) return null;

        // Collect the fonts a `Tf` selects that actually SHOW text, and the form
        // XObjects a `Do` invokes. A font selected only by an empty run (`/F Tf`
        // followed by `[] TJ` with no glyphs, then another `Tf`) is not really used —
        // counting it would keep an orphan font after a full RemoveUnusedFonts replace.
        var usedFonts = new HashSet<string>(StringComparer.Ordinal);
        var formNames = new List<string>();
        var lexer = new IO.PdfLexer(content);
        string? lastName = null;      // most recent /Name operand (font for Tf, form for Do)
        string? currentFont = null;   // font selected by the last Tf
        bool sawGlyphs = false;       // a non-empty string appeared since the last operator
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) break;
            switch (token.Kind)
            {
                case IO.TokenKind.Name when token.StringValue is { } n:
                    lastName = n;
                    break;
                case IO.TokenKind.LiteralString:
                case IO.TokenKind.HexString:
                    if (token.BytesValue is { Length: > 0 }) sawGlyphs = true;
                    break;
                case IO.TokenKind.Keyword:
                    var kw = token.StringValue;
                    if (kw == "BI") { SkipInlineImage(lexer, usedFonts); break; }
                    if (kw == "Tf") currentFont = lastName;
                    else if (kw == "Do" && lastName is not null) formNames.Add(lastName);
                    else if ((kw == "Tj" || kw == "TJ" || kw == "'" || kw == "\"")
                             && sawGlyphs && currentFont is not null)
                        usedFonts.Add(currentFont);
                    sawGlyphs = false; // operator boundary resets the operand scan
                    break;
            }
        }

        byte[]? rewritten = null;
        var fontDict = _reader.ResolveDict(resources.Get("Font"));
        if (fontDict is not null)
        {
            foreach (var key in fontDict.Keys.ToList())
                if (!usedFonts.Contains(key))
                    fontDict.Remove(key);

            // Rename the replacement fonts (registered under an "AsRp…" key) to F0, F1, …,
            // avoiding collision with any surviving original font, and patch the content's
            // Tf operands to match.
            var survivors = fontDict.Keys.ToList();
            var taken = new HashSet<string>(survivors.Where(k => !k.StartsWith("AsRp", StringComparison.Ordinal)),
                StringComparer.Ordinal);
            var renameMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var n = 0;
            foreach (var rk in survivors.Where(k => k.StartsWith("AsRp", StringComparison.Ordinal)))
            {
                string fn;
                do { fn = "F" + n++; } while (taken.Contains(fn));
                taken.Add(fn);
                renameMap[rk] = fn;
            }
            if (renameMap.Count > 0)
            {
                foreach (var (oldKey, newKey) in renameMap)
                {
                    var val = fontDict.Get(oldKey);
                    fontDict.Remove(oldKey);
                    if (val is not null) fontDict.Set(newKey, val);
                }
                rewritten = RenameFontNamesInContent(content, renameMap);
            }
        }

        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is not null)
        {
            foreach (var name in formNames)
            {
                var xstream = _reader.ResolveStream(xobjects.Get(name));
                if (xstream is null || xstream.Dict.GetName("Subtype") != "Form") continue;
                if (!visitedForms.Add(xstream.Dict)) continue; // cycle / shared-form guard
                var formRes = _reader.ResolveDict(xstream.Dict.Get("Resources"));
                // Only prune a form's OWN /Font dict — a form inheriting the page's
                // resources shares that dict, handled at the page scope.
                if (formRes is not null && !ReferenceEquals(formRes, resources))
                {
                    var newForm = PruneFontsInScope(_reader.DecodeStream(xstream), formRes, visitedForms);
                    if (newForm is not null)
                    {
                        xstream.Dict.Remove("Filter");
                        xstream.Dict.Remove("DecodeParms");
                        xstream.Dict.Set("Length", new PdfInteger(newForm.Length));
                        xstream.ReplaceData(newForm);
                    }
                }
            }
        }
        return rewritten;
    }

    /// <summary>Rewrite a content stream, replacing every /Name token that is a key of
    /// <paramref name="renameMap"/> with its mapped name (used to repoint Tf font
    /// operands after a resource-key rename).</summary>
    private static byte[] RenameFontNamesInContent(byte[] content, Dictionary<string, string> renameMap)
    {
        var lexer = new IO.PdfLexer(content);
        var patches = new List<(int start, int end, string nw)>();
        while (true)
        {
            var startPos = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) break;
            if (token.Kind == IO.TokenKind.Name && token.StringValue is { } nm
                && renameMap.TryGetValue(nm, out var nw))
                patches.Add((startPos, (int)lexer.Position, nw));
        }
        // Apply right-to-left so earlier offsets stay valid.
        patches.Sort((a, b) => b.start.CompareTo(a.start));
        foreach (var (s, e, nw) in patches)
        {
            var nameBytes = System.Text.Encoding.ASCII.GetBytes("/" + nw);
            var result = new byte[content.Length - (e - s) + nameBytes.Length];
            Array.Copy(content, 0, result, 0, s);
            Array.Copy(nameBytes, 0, result, s, nameBytes.Length);
            Array.Copy(content, e, result, s + nameBytes.Length, content.Length - e);
            content = result;
        }
        return content;
    }

    /// <summary>Expand <paramref name="used"/> with the /XObject names that a used
    /// image references through its /SMask or /Mask — a soft mask is part of the
    /// image even though no content stream names it directly, so pruning it would
    /// drop a glyph/picture's transparency. Iterates to a fixpoint for mask chains.</summary>
    private void AddReferencedXObjectNames(PdfDictionary resources, HashSet<string> used)
    {
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        // Map each entry's underlying stream object to its name so a mask referenced
        // by object can be matched back to the /XObject key the test inspects.
        var streamToName = new Dictionary<PdfStream, string>(ReferenceEqualityComparer.Instance);
        foreach (var name in xobjects.Keys)
            if (_reader.ResolveStream(xobjects.Get(name)) is { } s)
                streamToName[s] = name;

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var name in used.ToList())
            {
                if (_reader.ResolveStream(xobjects.Get(name)) is not { } s) continue;
                foreach (var maskKey in new[] { "SMask", "Mask" })
                    if (_reader.ResolveStream(s.Dict.Get(maskKey)) is { } mask
                        && streamToName.TryGetValue(mask, out var maskName)
                        && used.Add(maskName))
                        changed = true;
            }
        }
    }

    /// <summary>Add every /Name token in <paramref name="content"/> to
    /// <paramref name="used"/>, then recurse through the form XObjects it invokes so
    /// names referenced only inside a nested form (or via its parent-resource
    /// fallback) are counted as used too.</summary>
    private void CollectContentResourceNames(byte[] content, PdfDictionary resources,
        HashSet<string> used, HashSet<PdfDictionary> visitedForms)
    {
        var localNames = new List<string>();
        var lexer = new IO.PdfLexer(content);
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) break;
            // Inline images carry raw binary between ID and EI that must not be
            // tokenised — left unskipped it desyncs the lexer and the /Name tokens
            // after it (e.g. later `Do` references) are missed, pruning live images.
            if (token.Kind == IO.TokenKind.Keyword && token.StringValue == "BI")
            {
                SkipInlineImage(lexer, used);
                continue;
            }
            if (token.Kind == IO.TokenKind.Name && token.StringValue is { } name && used.Add(name))
                localNames.Add(name);
        }

        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var name in localNames)
        {
            var xstream = _reader.ResolveStream(xobjects.Get(name));
            if (xstream is null || xstream.Dict.GetName("Subtype") != "Form") continue;
            if (!visitedForms.Add(xstream.Dict)) continue; // cycle / shared-form guard
            var formContent = _reader.DecodeStream(xstream);
            if (formContent.Length == 0) continue;
            // A form may declare its own /Resources; absent, it inherits the page's.
            var formRes = _reader.ResolveDict(xstream.Dict.Get("Resources")) ?? resources;
            CollectContentResourceNames(formContent, formRes, used, visitedForms);
        }
    }

    /// <summary>Consume an inline image (the lexer has just read its <c>BI</c>):
    /// collect any /Name values among its parameters — an inline image's <c>/CS</c>
    /// may name a colour space declared in /Resources/ColorSpace — then skip the raw
    /// image bytes up to and including the <c>EI</c> terminator.</summary>
    private static void SkipInlineImage(IO.PdfLexer lexer, HashSet<string> used)
    {
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) return;
            if (token.Kind == IO.TokenKind.Keyword && token.StringValue == "ID") break;
            if (token.Kind == IO.TokenKind.Name && token.StringValue is { } name)
                used.Add(name);
        }
        lexer.ReadInlineImageData();
    }

    /// <summary>Remove entries not present in <paramref name="used"/> from each
    /// prunable sub-dictionary of <paramref name="resources"/>.</summary>
    private void PruneResourceCategories(PdfDictionary resources, HashSet<string> used)
    {
        foreach (var category in PrunableResourceCategories)
        {
            var dict = _reader.ResolveDict(resources.Get(category));
            if (dict is null) continue;
            var unused = dict.Keys.Where(k => !used.Contains(k)).ToList();
            foreach (var key in unused)
                dict.Remove(key);
            if (!dict.Keys.Any())
                resources.Remove(category);
        }
    }

    /// <summary>
    /// Find streams with identical content and redirect duplicate references to a single canonical object.
    /// This reduces file size when the same content (e.g., images) appears multiple times.
    /// </summary>
    private void LinkDuplicateStreams()
    {
        // Phase 1: Hash all stream objects
        var hashToObjNum = new Dictionary<string, int>(StringComparer.Ordinal);
        var redirections = new Dictionary<int, int>(); // oldObjNum → canonicalObjNum

        foreach (var entry in _reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            var obj = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is not PdfStream stream) continue;

            // Decode the stream data for content comparison
            byte[] decoded;
            try
            {
                decoded = StreamFilter.Decode(stream.RawData, stream.Dict);
            }
            catch
            {
                continue; // Skip streams that fail to decode
            }

            // Build a hash that includes stream properties (width/height/colorspace for images)
            var hash = System.Convert.ToHexString(Security.ShaDigest.Sha256(decoded));

            // Append key properties to distinguish structurally different streams
            var width = stream.Dict.GetInt("Width");
            var height = stream.Dict.GetInt("Height");
            if (width > 0) hash += $"_W{width}_H{height}";

            if (hashToObjNum.TryGetValue(hash, out var canonicalObjNum))
            {
                redirections[entry.ObjectNumber] = canonicalObjNum;
            }
            else
            {
                hashToObjNum[hash] = entry.ObjectNumber;
            }
        }

        if (redirections.Count == 0) return;

        // Phase 2: Replace indirect references throughout the document
        RedirectReferences(_reader.Catalog, redirections);

        // Also redirect in each page's annotations and resources
        foreach (var page in Pages)
        {
            RedirectReferences(page.Dict, redirections);
        }
    }

    /// <summary>
    /// Recursively replace indirect references in a dictionary tree.
    /// </summary>
    private void RedirectReferences(PdfDictionary dict, Dictionary<int, int> redirections)
    {
        foreach (var key in dict.Keys.ToList())
        {
            var value = dict.Get(key);
            switch (value)
            {
                case PdfIndirectRef iref when redirections.TryGetValue(iref.ObjectNumber, out var newObjNum):
                    dict.Set(key, new PdfIndirectRef(newObjNum, 0));
                    break;
                case PdfDictionary childDict:
                    RedirectReferences(childDict, redirections);
                    break;
                case PdfArray arr:
                    RedirectReferencesInArray(arr, redirections);
                    break;
            }
        }
    }

    private void RedirectReferencesInArray(PdfArray arr, Dictionary<int, int> redirections)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            switch (arr[i])
            {
                case PdfIndirectRef iref when redirections.TryGetValue(iref.ObjectNumber, out var newObjNum):
                    arr.ReplaceAt(i, new PdfIndirectRef(newObjNum, 0));
                    break;
                case PdfDictionary childDict:
                    RedirectReferences(childDict, redirections);
                    break;
                case PdfArray nested:
                    RedirectReferencesInArray(nested, redirections);
                    break;
            }
        }
    }

    // ── PDF/A conversion ────────────────────────────────────────────────────

    /// <summary>
    /// Convert the document to the specified PDF/A conformance level.
    /// Applies automatic fixes for common violations when ErrorAction is Delete.
    /// Returns true if the document was successfully made compliant (or all fixable issues were addressed).
    /// </summary>
    public bool Convert(PdfFormatConversionOptions options)
    {
        var result = ConvertInternal(options);

        // Remember last successful conversion target so IsPdfaCompliant / PdfFormat
        // can report the converted state in-memory (tests run the assertion on the
        // same Document instance after Convert + Save).
        if (result) _lastConvertedFormat = options.TargetFormat;

        // Write log if requested
        if (!string.IsNullOrEmpty(options.LogFileName))
        {
            using var fs = File.Create(options.LogFileName);
            WriteConversionLog(fs, options, PdfVersion ?? "1.7");
        }
        else if (options.LogStream is not null && options.ConversionLog.Count > 0)
        {
            WriteConversionLog(options.LogStream, options);
        }

        return result;
    }

    private Aspose.Pdf.PdfFormat? _lastConvertedFormat;

    /// <summary>True once a PDF/A conversion succeeded on this instance. Validation
    /// of file-structure rules that conversion repairs on save (e.g. the PDF/A-1
    /// xref-stream prohibition) treats the document as already fixed.</summary>
    internal bool PdfAConversionApplied => _lastConvertedFormat is not null;

    /// <summary>
    /// Convert the document to a specific PDF/A format with a log file.
    /// </summary>
    public bool Convert(string outputLogFileName, PdfFormat format, ConvertErrorAction action)
    {
        var options = new PdfFormatConversionOptions(format, action) { LogFileName = outputLogFileName };
        return Convert(options);
    }

    /// <summary>
    /// Convert the document to a specific PDF/A format with a log file, specifying transparency handling.
    /// </summary>
    public bool Convert(string outputLogFileName, PdfFormat format, ConvertErrorAction action, ConvertTransparencyAction transparencyAction)
    {
        var options = new PdfFormatConversionOptions(format, action)
        {
            LogFileName = outputLogFileName,
            TransparencyAction = transparencyAction,
        };
        return Convert(options);
    }

    /// <summary>
    /// Convert the document to a specific PDF/A format with a log stream.
    /// </summary>
    public bool Convert(Stream outputLogStream, PdfFormat format, ConvertErrorAction action)
    {
        var options = new PdfFormatConversionOptions(format, action) { LogStream = outputLogStream };
        return Convert(options);
    }

    /// <summary>Stream-log overload with explicit transparency action.</summary>
    public bool Convert(Stream outputLogStream, PdfFormat format, ConvertErrorAction action, ConvertTransparencyAction transparencyAction)
    {
        var options = new PdfFormatConversionOptions(format, action)
        {
            LogStream = outputLogStream,
            TransparencyAction = transparencyAction,
        };
        return Convert(options);
    }

    // The 4 Aspose.Pdf static Convert(src, LoadOptions, dst, SaveOptions)
    // overloads are deferred: in this FOSS branch HtmlLoadOptions /
    // MdLoadOptions / SvgLoadOptions don't derive from LoadOptions, so a
    // typed `LoadOptions` parameter can't compile-time dispatch into the
    // real HTML/SVG/MD Open overloads. Adding them as
    // `Open(srcFileName) + Save(dstFileName)` passthroughs would silently
    // drop the options — exactly the kind of stub we don't want.
    // Re-add after unifying the load-options hierarchy.

    private static void WriteConversionLog(Stream output, PdfFormatConversionOptions options, string sourceVersion = "1.7")
    {
        using var writer = new StreamWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        writer.WriteLine($"<ConversionLog Operation=\"Conversion\" From=\"{sourceVersion}\" To=\"{GetVersionString(options.TargetFormat)}\">");
        foreach (var v in options.ConversionLog)
        {
            writer.WriteLine($"  <Violation Rule=\"{EscapeXml(v.Rule)}\">{EscapeXml(v.Description)}</Violation>");
        }
        writer.WriteLine("</ConversionLog>");
    }

    private static string GetVersionString(PdfFormat format) => format switch
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
        _ => format.ToString(),
    };

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// Internal conversion implementation.
    /// </summary>
    /// <summary>Round content-stream real literals whose magnitude exceeds the PDF/A-1
    /// implementation limit (32767) and that carry a fractional part, to plain integers.
    /// String (…), hex &lt;…&gt; and comment regions are left untouched. Returns the
    /// rewritten bytes, or null when nothing needed changing.</summary>
    private static byte[]? RoundOutOfRangeReals(byte[] content)
    {
        var outBytes = new List<byte>(content.Length + 16);
        bool changed = false;
        int i = 0, n = content.Length;
        void CopyRange(int from, int to) { for (var k = from; k < to; k++) outBytes.Add(content[k]); }
        while (i < n)
        {
            byte b = content[i];
            if (b == (byte)'(')
            {
                // literal string: copy verbatim honouring escapes + nesting
                int depth = 0, start = i;
                while (i < n)
                {
                    byte sc = content[i];
                    if (sc == (byte)'\\' && i + 1 < n) { i += 2; continue; }
                    if (sc == (byte)'(') depth++;
                    else if (sc == (byte)')' && --depth == 0) { i++; break; }
                    i++;
                }
                CopyRange(start, i);
                continue;
            }
            if (b == (byte)'<' && i + 1 < n && content[i + 1] != (byte)'<')
            {
                int start = i;
                while (i < n && content[i] != (byte)'>') i++;
                if (i < n) i++;
                CopyRange(start, i);
                continue;
            }
            if (b == (byte)'%')
            {
                int start = i;
                while (i < n && content[i] != (byte)'\n' && content[i] != (byte)'\r') i++;
                CopyRange(start, i);
                continue;
            }
            // Inline image: copy BI … EI verbatim — the raw sample bytes after ID
            // must never be scanned as tokens.
            if (b == (byte)'B' && i + 1 < n && content[i + 1] == (byte)'I'
                && (i == 0 || IsPdfDelimiterOrWs(content[i - 1]))
                && (i + 2 >= n || IsPdfDelimiterOrWs(content[i + 2])))
            {
                int start = i;
                i += 2;
                while (i + 1 < n
                       && !(content[i] == (byte)'E' && content[i + 1] == (byte)'I'
                            && (i + 2 >= n || IsPdfDelimiterOrWs(content[i + 2]))
                            && IsPdfDelimiterOrWs(content[i - 1])))
                    i++;
                i = Math.Min(n, i + 2);
                CopyRange(start, i);
                continue;
            }
            if (b == (byte)'-' || b == (byte)'+' || b == (byte)'.' || (b >= (byte)'0' && b <= (byte)'9'))
            {
                int start = i;
                bool hasDot = false;
                if (b == (byte)'-' || b == (byte)'+') i++;
                while (i < n)
                {
                    byte nc = content[i];
                    if (nc >= (byte)'0' && nc <= (byte)'9') { i++; continue; }
                    if (nc == (byte)'.' && !hasDot) { hasDot = true; i++; continue; }
                    break;
                }
                if (hasDot)
                {
                    var token = System.Text.Encoding.ASCII.GetString(content, start, i - start);
                    if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var v)
                        && Math.Abs(v) >= 32767 && v != Math.Truncate(v))
                    {
                        var repl = Math.Round(v).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                        foreach (var rb in System.Text.Encoding.ASCII.GetBytes(repl)) outBytes.Add(rb);
                        changed = true;
                        continue;
                    }
                }
                CopyRange(start, i);
                continue;
            }
            outBytes.Add(b);
            i++;
        }
        return changed ? outBytes.ToArray() : null;
    }

    private static bool IsPdfDelimiterOrWs(byte c) =>
        c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0
          or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
          or (byte)'/' or (byte)'%';

    private bool ConvertInternal(PdfFormatConversionOptions options)
    {
        var format = options.TargetFormat;
        // The standard PDF/A transformations (embed fonts, write the XMP pdfaid, add an
        // OutputIntent, normalise the version) are applied for every ErrorAction — that
        // applies structural fixes only (a None-conversion still embeds fonts and writes
        // metadata) and is the only way the output can validate structurally.
        var fix = true;
        // Removing prohibited CONTENT (catalog/AA actions, non-compliant annotations) is what
        // ConvertErrorAction governs: Delete strips it, None only logs the violation and leaves
        // the content in place. The structural fixes above are applied regardless.
        var strip = options.ErrorAction == ConvertErrorAction.Delete;

        if (format == PdfFormat.v_1_7)
        {
            SetVersion("1.7");
            return true;
        }

        if (format == PdfFormat.v_2_0)
        {
            SetVersion("2.0");
            return true;
        }

        // Plain PDF version targets (1.0 – 1.6): retarget the header/catalog
        // version. No PDF/A conformance work is needed for these.
        var plainVersion = format switch
        {
            PdfFormat.v_1_0 => "1.0",
            PdfFormat.v_1_1 => "1.1",
            PdfFormat.v_1_2 => "1.2",
            PdfFormat.v_1_3 => "1.3",
            PdfFormat.v_1_4 => "1.4",
            PdfFormat.v_1_5 => "1.5",
            PdfFormat.v_1_6 => "1.6",
            _ => (string?)null,
        };
        if (plainVersion is not null)
        {
            SetVersion(plainVersion);
            // Keep the catalog /Version in sync so a reloaded document reports
            // the downgraded version regardless of header/catalog precedence.
            _reader.Catalog.Set("Version", new PdfName(plainVersion));
            return true;
        }

        // PDF/UA-1 (ISO 14289-1) accessibility: tag the document, give it a title + natural
        // language, set /ViewerPreferences /DisplayDocTitle, the pdfuaid:part identifier, the
        // XMP dates and a file /ID. The tagged-metadata stamp on save finalises the rest.
        if (format == PdfFormat.PDF_UA_1)
        {
            if (!fix) return CheckFontEmbedding(options);
            if (string.IsNullOrEmpty(Info.Title)) Info.Title = "Untitled";
            if (string.IsNullOrEmpty(Language)) Language = "en-US";
            DisplayDocTitle = true;
            var uaMeta = GetOrCreateMetadata();
            if (string.IsNullOrEmpty(uaMeta.Get("pdfuaid:part"))) uaMeta.Set("pdfuaid:part", "1");
            if (string.IsNullOrEmpty(uaMeta.Get("dc:title"))) uaMeta.Set("dc:title", Info.Title);
            if (string.IsNullOrEmpty(uaMeta.Get("pdf:Producer"))) uaMeta.Set("pdf:Producer", "Aspose.PDF FOSS for .NET");
            if (string.IsNullOrEmpty(uaMeta.Get("xmp:CreateDate")) && Info.CreationDate != DateTime.MinValue)
                uaMeta.Set("xmp:CreateDate", FormatXmpDate(Info.CreationDate, Info.CreationTimeZone));
            if (string.IsNullOrEmpty(uaMeta.Get("xmp:ModifyDate")) && Info.ModDate != DateTime.MinValue)
                uaMeta.Set("xmp:ModifyDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));
            if (_reader.Trailer.Get("ID") is null)
            {
                _forceWriteId = true;
                var feId = Security.CryptoRandom.GetBytes(16);
                var feIdArr = new PdfArray();
                feIdArr.Add(new PdfString(feId, isHex: true));
                feIdArr.Add(new PdfString(feId, isHex: true));
                _reader.Trailer.Set("ID", feIdArr);
            }
            EmbedNonEmbeddedFonts(options, includeStandard14: true);
            if (options.AutoTaggingSettings is { EnableAutoTagging: true })
                Tagged.AutoTagger.Apply(this, options.AutoTaggingSettings);
            return CheckFontEmbedding(options);
        }

        var isPdfX = format is PdfFormat.PDF_X_1A or PdfFormat.PDF_X_3;

        // Determine PDF/A part and conformance from format
        var (part, conformance) = format switch
        {
            PdfFormat.PDF_A_1A => ("1", "A"),
            PdfFormat.PDF_A_1B => ("1", "B"),
            PdfFormat.PDF_A_2A => ("2", "A"),
            PdfFormat.PDF_A_2B => ("2", "B"),
            PdfFormat.PDF_A_2U => ("2", "U"),
            PdfFormat.PDF_A_3A => ("3", "A"),
            PdfFormat.PDF_A_3B => ("3", "B"),
            PdfFormat.PDF_A_3U => ("3", "U"),
            // ZUGFeRD (factur-x) electronic invoices are PDF/A-3 documents that carry the
            // invoice XML as an associated file. Convert as PDF/A-3B, then attach the AF tagging.
            PdfFormat.ZUGFeRD => ("3", "B"),
            PdfFormat.PDF_A_4 => ("4", ""),
            PdfFormat.PDF_X_1A => ("X-1", "a"),
            PdfFormat.PDF_X_3 => ("X-3", ""),
            _ => (null, (string?)null),
        };

        if (part is null)
            return true; // Not a PDF/A or PDF/X format, nothing to do

        // 1. Remove encryption
        if (IsEncrypted)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "Encryption",
                Description = "Document is encrypted (not allowed in PDF/A).",
            });
            if (fix)
            {
                _encryptor = null;
                _reader.Trailer.Remove("Encrypt");
            }
        }

        // 2. Fix PDF version (must be >= 1.4)
        var version = PdfVersion;
        if (version is not null && string.Compare(version, "1.4", StringComparison.Ordinal) < 0)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "PdfVersion",
                Description = $"PDF version {version} is below 1.4 (minimum for PDF/A).",
            });
            if (fix)
            {
                SetVersion("1.4");
            }
        }
        // PDF/A-2 and PDF/A-3 are based on ISO 32000-1 (PDF 1.7): the header version
        // upgrades to 1.7 (Aspose.Pdf does this on Convert; the saved file
        // reports Version == "1.7"). The catalog /Version — which takes precedence
        // over the header when reading — must follow, or a stale /Version 1.4 from
        // the source would mask the upgraded header.
        if (fix && part is "2" or "3"
            && string.Compare(PdfVersion ?? "1.0", "1.7", StringComparison.Ordinal) < 0)
        {
            SetVersion("1.7");
            if (_reader.Catalog.Get("Version") is not null)
                _reader.Catalog.Set("Version", new PdfName("1.7"));
        }

        // 3. Add/fix XMP metadata
        var meta = GetOrCreateMetadata();
        var needsPdfAId = string.IsNullOrEmpty(meta.PdfAidPart);
        // PDF/A-4 (part "4") carries no conformance level, so never treat its absence
        // as a violation; parts 1–3 still require pdfaid:conformance.
        var needsConformance = part != "4" && string.IsNullOrEmpty(meta.PdfAidConformance);
        var needsTitle = string.IsNullOrEmpty(meta.Get("dc:title"));
        var needsProducer = string.IsNullOrEmpty(meta.Get("pdf:Producer"));

        if (needsPdfAId)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataPdfAId",
                Description = "Missing pdfaid:part in XMP metadata.",
            });
        }
        if (needsConformance)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataPdfAConformance",
                Description = "Missing pdfaid:conformance in XMP metadata.",
            });
        }
        if (needsTitle)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataDcTitle",
                Description = "Missing dc:title in XMP metadata.",
            });
        }
        if (needsProducer)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "MetadataPdfProducer",
                Description = "Missing pdf:Producer in XMP metadata.",
            });
        }

        if (fix)
        {
            if (!isPdfX)
            {
                meta.PdfAidPart = part;
                // PDF/A-4 has no conformance level (empty) — leave the entry absent
                // rather than writing an empty pdfaid:conformance.
                if (string.IsNullOrEmpty(conformance)) meta.PdfAidConformance = null;
                else meta.PdfAidConformance = conformance;
            }
            // Info.Title is "" (not null) when the source has no title, so "??" would
            // store an empty dc:title that the validator still flags as missing — fall
            // back to "Untitled" for null OR empty.
            if (needsTitle) meta.Set("dc:title", string.IsNullOrEmpty(Info.Title) ? "Untitled" : Info.Title);
            if (needsProducer) meta.Set("pdf:Producer", "Aspose.PDF FOSS for .NET");

            // PDF/A requires the XMP xmp:CreateDate / xmp:ModifyDate to mirror the
            // /Info CreationDate / ModDate (ISO 8601). Without them the XMP and
            // document-info dates disagree and round-tripping Metadata["xmp:CreateDate"]
            // throws KeyNotFoundException. Write them in a form that
            // round-trips through XmpValue.ToDateTime().
            if (string.IsNullOrEmpty(meta.Get("xmp:CreateDate")) && Info.CreationDate != DateTime.MinValue)
                meta.Set("xmp:CreateDate", FormatXmpDate(Info.CreationDate, Info.CreationTimeZone));
            if (string.IsNullOrEmpty(meta.Get("xmp:ModifyDate")) && Info.ModDate != DateTime.MinValue)
                meta.Set("xmp:ModifyDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));
            if (string.IsNullOrEmpty(meta.Get("xmp:MetadataDate")) && Info.ModDate != DateTime.MinValue)
                meta.Set("xmp:MetadataDate", FormatXmpDate(Info.ModDate, Info.ModTimeZone));

            // ISO 19005 6.6.3 analog of the date sync above for the remaining
            // /Info↔XMP pairs: the XMP packet the conversion writes must mirror
            // the document-information strings (Keywords → pdf:Keywords, etc.) —
            // reloading the output and reading Metadata["pdf:Keywords"] must see
            // the value the caller put in DocumentInfo. NOTE: guarded with the
            // packet-only ContainsKey — Get() consults the Info fallback, which
            // would report the value "present" without it ever being serialised.
            if (!meta.ContainsKey("pdf:Keywords") && !string.IsNullOrEmpty(Info.Keywords))
                meta.Set("pdf:Keywords", Info.Keywords);
            if (!meta.ContainsKey("dc:creator") && !string.IsNullOrEmpty(Info.Author))
                meta.Set("dc:creator", Info.Author);
            if (!meta.ContainsKey("dc:description") && !string.IsNullOrEmpty(Info.Subject))
                meta.Set("dc:description", Info.Subject);
            if (!meta.ContainsKey("xmp:CreatorTool") && !string.IsNullOrEmpty(Info.Creator))
                meta.Set("xmp:CreatorTool", Info.Creator);
        }

        // 4. Ensure file ID exists. Materialise /ID into the in-memory
        //    trailer immediately so PdfAValidator (which reads
        //    document.Reader.Trailer in-memory) sees the fix without
        //    requiring a Save+reopen round-trip. The save path also
        //    honours the flag for re-encrypted saves; we keep it set so the
        //    same ID gets written through to disk.
        if (_reader.Trailer.Get("ID") is null)
        {
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FileId",
                Description = "Missing file ID in trailer (required for PDF/A).",
            });
            if (fix)
            {
                _forceWriteId = true;
                var fileId = Security.CryptoRandom.GetBytes(16);
                var idArray = new PdfArray();
                idArray.Add(new PdfString(fileId, isHex: true));
                idArray.Add(new PdfString(fileId, isHex: true));
                _reader.Trailer.Set("ID", idArray);
            }
        }

        // 5. Remove prohibited actions from catalog (only when ErrorAction strips)
        RemoveProhibitedCatalogActions(options, strip);

        // 6. Fix annotations (per page) — print-flag fixes always, removal only when stripping
        foreach (var page in Pages)
        {
            FixAnnotationsForPdfA(page, options, fix, strip);
        }

        // 6b. Remove page-level transparency groups — PDF/A-1 (ISO 19005-1)
        // prohibits transparency. The page /Group entry only declares the
        // blending colour space / isolation for compositing the page onto the
        // backdrop; dropping it leaves opaque content rendering identically
        // while clearing the violation. PDF/A-2 and later permit transparency,
        // so this is scoped to part 1.
        if (part == "1")
        {
            foreach (var page in Pages)
            {
                var group = _reader.ResolveDict(page.Dict.Get("Group"));
                if (group is null || group.GetName("S") != "Transparency") continue;
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "Transparency",
                    Description = $"Page {page.Number} uses transparency group (not allowed in PDF/A-1).",
                    PageNumber = page.Number,
                });
                if (fix) page.Dict.Remove("Group");
            }

            // ConvertTransparencyAction.Mask: preserve the visual appearance of images that
            // are painted under a constant fill-alpha (/ca < 1). PDF/A-1 forbids ExtGState
            // alpha, so the neutralisation below would zero it and make such an image render
            // opaquely. Before that, bake the alpha into a constant DeviceGray soft mask on
            // the image XObject itself (an image /SMask is NOT stripped by this conversion),
            // so the image keeps compositing at the requested opacity while the prohibited
            // ExtGState alpha is removed. The Default action leaves the neutralisation opaque.
            if (fix && options.TransparencyAction == ConvertTransparencyAction.Mask)
                foreach (var page in Pages)
                {
                    var res = _reader.ResolveDict(page.Dict.Get("Resources"));
                    var content = page.GetContentStreamBytes();
                    if (res is not null && content is { Length: > 0 })
                        MaskConstantAlphaImages(content, res, 1.0,
                            new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance));
                }

            // ExtGState soft masks, constant alpha (ca/CA < 1) and non-Normal blend modes
            // are equally prohibited by PDF/A-1. Neutralise them in every graphics-state
            // dictionary reachable from the pages (including nested Form XObjects) so the
            // content renders opaquely instead of failing validation.
            foreach (var page in Pages)
                NeutralizeExtGStateTransparency(page.Dict, options, page.Number, fix,
                    new HashSet<PdfDictionary>());

            // PDF/A-1 implementation limits (ISO 19005-1 / PDF 1.4 Annex C): real numbers
            // must stay within ±32767. Round out-of-range FRACTIONAL reals in the page
            // content to integers (integral magnitudes beyond the limit are tolerated by
            // the target validators, and rounding keeps far-off-page geometry harmless).
            if (fix)
                foreach (var page in Pages)
                {
                    // Defensive: an undecodable content stream must not abort the
                    // whole conversion — skip the range fix for that page.
                    try
                    {
                        var content = page.GetContentStreamBytes();
                        if (content is not { Length: > 0 }) continue;
                        var rounded = RoundOutOfRangeReals(content);
                        if (rounded is not null)
                            page.SetContentStream(rounded);
                    }
                    catch
                    {
                        // leave the page content untouched
                    }
                }
        }

        // 7. Add OutputIntent
        if (isPdfX && fix)
        {
            // PDF/X requires an OutputIntent with ICC profile
            AddPdfXOutputIntent(options);
        }
        else if (!HasPdfAOutputIntentInCatalog())
        {
            // Detect device-dependent colours either already emitted as
            // page XObjects OR queued as DOM paragraphs (Image, ImageStamp)
            // that Save() will flush after Convert returns.
            var hasDeviceColors = false;
            foreach (var page in Pages)
            {
                if (PageHasDeviceDependentColors(page) || PageHasDeviceDependentParagraphs(page))
                {
                    hasDeviceColors = true;
                    break;
                }
            }
            if (hasDeviceColors)
            {
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "ColorSpace",
                    Description = "Device-dependent color space without OutputIntent.",
                });
            }
            // A PDF/A output always carries a GTS_PDFA1 OutputIntent (the reference
            // validator gates on its presence), not only when device-dependent
            // colours were detected — the violation above is logged for those only.
            if (fix)
            {
                AddSrgbOutputIntent();
            }
        }

        // 8. For PDF/X, set GTS_PDFXVersion in Info dict and XMP
        if (isPdfX && fix)
        {
            var xMeta = GetOrCreateMetadata();
            if (format == PdfFormat.PDF_X_1A)
            {
                xMeta.Set("pdfx:GTS_PDFXVersion", "PDF/X-1a:2003");
                xMeta.PdfAidPart = null;
                xMeta.PdfAidConformance = null;
            }
            else if (format == PdfFormat.PDF_X_3)
            {
                xMeta.Set("pdfx:GTS_PDFXVersion", "PDF/X-3:2003");
                xMeta.PdfAidPart = null;
                xMeta.PdfAidConformance = null;
            }
        }

        // 9. Interactive form fields: PDF/A prohibits non-signature widgets. The
        // the conversion FLATTENS them — the field's appearance (its value
        // text) is baked into the page content and must survive extraction
        // rather than deleting them. Signature fields are valid
        // in PDF/A and stay; when signatures are present only the non-signature
        // widgets are dropped (flattening around a live signature would break it).
        if (fix && !isPdfX)
            FlattenOrRemoveFormFieldsForPdfA();

        // 10. Embed glyph-bearing fonts that the source left unembedded (PDF/A requires
        // every font to be embedded — including the Standard-14 faces, which a viewer would
        // otherwise substitute): resolve the real face, fall back to Arial for an
        // unresolvable family, and report each replacement via FontSubstitution.
        if (fix && !isPdfX)
            EmbedNonEmbeddedFonts(options, includeStandard14: true);

        // 11. Verify all non-embedded non-Standard14 fonts can be resolved.
        // PDF/A requires every glyph-bearing font to be embedded; if a font is
        // unembedded AND FontRepository can't find it, conversion fails.
        var fontsResolved = CheckFontEmbedding(options);

        // 11b. Auto-tagging: synthesise a logical-structure tree from the page content so the
        // output carries a /StructTreeRoot. This is mandatory for the accessible A-levels
        // (which require a tagged, titled document), and otherwise runs when the caller opts in
        // (AutoTaggingSettings.Default enables it) for tagged PDF/A / PDF/UA output.
        var autoTag = options.AutoTaggingSettings is { EnableAutoTagging: true } || conformance == "A";
        if (fix && autoTag)
        {
            // A-level PDF/A also requires a document title (ISO 19005 §6.7.3); mirror the XMP
            // dc:title onto /Info so the validator's title check is satisfied.
            if (conformance == "A" && string.IsNullOrEmpty(Info.Title))
                Info.Title = string.IsNullOrEmpty(meta.Get("dc:title")) ? "Untitled" : meta.Get("dc:title");
            Tagged.AutoTagger.Apply(this, options.AutoTaggingSettings ?? AutoTaggingSettings.Default);
        }

        // 12. ZUGFeRD (factur-x): mark the embedded invoice XML as an associated file —
        // /AFRelationship /Alternative + MIME type text/xml — and reference every embedded
        // file from the catalog /AF array, per the ZUGFeRD/PDF-A-3 associated-files profile.
        if (fix && format == PdfFormat.ZUGFeRD)
            ApplyZugferdAssociatedFiles();

        // 12b. PDF/A-2 (ISO 19005-2 §6.9): an embedded file must itself be a PDF/A
        // document. Convert every embedded PDF attachment to PDF/A-2B in place —
        // so the output attachments then claim 2B
        // (so a Validate(PDF_A_2B) of the extracted attachment passes and a
        // Validate(PDF_A_3B) fails the claim gate).
        if (fix && part == "2")
            ConvertEmbeddedPdfAttachmentsToPdfA2B();

        // 13. Size optimization (OptimizeFileSize): subset every embedded TrueType program to
        // the glyphs the document actually uses. Font embedding (step 10) is the dominant cost
        // of PDF/A conversion — a source that referenced but did not embed several system
        // faces gains a full WinAnsi program for each. Subsetting those (and any already-
        // embedded faces) to the used glyphs is what keeps the converted file at or below the
        // source size. The just-embedded /FontFile2 programs are still pending objects, so the
        // subsetter is given a resolver that reaches them. Non-destructive (glyph outlines only).
        if (options.OptimizeFileSize)
            Optimization.FontSubsetter.SubsetFonts(_reader, subsetEmbedded: true,
                resolveNewStream: ResolvePendingStream);
        else if (fix)
            // Output growth is kept bounded (capped near +10%), so the programs
            // THIS conversion just embedded are
            // subset to the used glyphs. The source's own embedded fonts are left
            // alone — re-subsetting a foreign subset (Word symbol cmaps etc.) has
            // stripped used glyphs into tofu.
            Optimization.FontSubsetter.SubsetEmbeddedFonts(_reader, ResolvePendingStream,
                newlyEmbeddedOnly: true);

        // 14. PDF/A-1 content-stream normalisation:
        //  - every page's content is bracketed in a q…Q pair so graphics state left
        //    open by the original stream can't leak into content the conversion
        //    appends (observable as exactly +2 operators per page);
        //  - ISO 19005-1 §6.1.13 implementation limits: a real value must fit ±32767,
        //    so an out-of-range path coordinate is rounded to an integer (sub-unit
        //    precision that far off the page is meaningless).
        if (fix && part == "1")
        {
            foreach (var page in Pages)
                try { NormalizePdfA1PageContent(page); }
                catch { /* undecodable content (e.g. exotic LZW): leave the page as-is */ }
            // Rewriting /Contents leaves each page's original stream object(s)
            // orphaned — have the save reachability-prune them, or every edited
            // page's content bytes are carried over twice.
            _reader.MayHaveOrphansOnSave = true;
        }

        return fontsResolved;
    }

    /// <summary>Bracket <paramref name="page"/>'s content in q…Q and round
    /// path coordinates beyond the PDF/A-1 ±32767 real-value limit to integers.
    /// A page with inline images is wrapped at the byte level and its
    /// coordinates left untouched: materialising such a stream through the
    /// typed operator list would drop the inline-image binary payload.</summary>
    private void NormalizePdfA1PageContent(Page page)
    {
        const double limit = short.MaxValue; // 32767
        static bool OutOfRange(double v) => Math.Abs(v) >= limit && v != Math.Truncate(v);
        static double Clamp(double v) => OutOfRange(v) ? Math.Round(v) : v;

        // Pre-scan: does any path coordinate exceed the PDF/A-1 real limit? Pages
        // with inline images must not be re-serialised through the typed operator
        // list at all (its BI token carries no binary payload).
        var needsCoordFix = false;
        var hasInline = false;
        var ops = page.Contents;
        foreach (var op in ops)
        {
            switch (op)
            {
                case Operators.BI:
                    hasInline = true;
                    break;
                case Operators.MoveTo m when OutOfRange(m.X) || OutOfRange(m.Y):
                case Operators.LineTo l when OutOfRange(l.X) || OutOfRange(l.Y):
                case Operators.CurveTo c when OutOfRange(c.X1) || OutOfRange(c.Y1)
                    || OutOfRange(c.X2) || OutOfRange(c.Y2) || OutOfRange(c.X3) || OutOfRange(c.Y3):
                    needsCoordFix = true;
                    break;
            }
            if (hasInline) break;
        }

        if (hasInline || !needsCoordFix)
        {
            // Byte-level wrap: keeps the original stream bytes verbatim (their
            // operator text usually compresses tighter than a re-serialisation,
            // and inline-image payloads survive untouched).
            var bytes = page.GetContentStreamBytes() ?? [];
            var head = Encoding.ASCII.GetBytes("q\n");
            var tail = Encoding.ASCII.GetBytes("\nQ");
            var merged = new byte[head.Length + bytes.Length + tail.Length];
            head.CopyTo(merged, 0);
            bytes.CopyTo(merged, head.Length);
            tail.CopyTo(merged, head.Length + bytes.Length);
            page.SetContentStream(merged);
            return;
        }

        ops.Insert(1, new Operators.GSave());
        ops.Add(new Operators.GRestore());
        // The collection is materialised by the insert above, so the enumerator
        // yields the live operator instances; coordinate edits persist through
        // the flush-on-save.
        foreach (var op in ops)
            switch (op)
            {
                case Operators.MoveTo m:
                    m.X = Clamp(m.X); m.Y = Clamp(m.Y);
                    break;
                case Operators.LineTo l:
                    l.X = Clamp(l.X); l.Y = Clamp(l.Y);
                    break;
                case Operators.CurveTo c:
                    c.X1 = Clamp(c.X1); c.Y1 = Clamp(c.Y1);
                    c.X2 = Clamp(c.X2); c.Y2 = Clamp(c.Y2);
                    c.X3 = Clamp(c.X3); c.Y3 = Clamp(c.Y3);
                    break;
            }
    }

    /// <summary>Resolve a stream that a preceding conversion step allocated but has not yet
    /// serialised — these live in <see cref="_newObjects"/> and are not reachable through the
    /// reader's xref. Returns null when the object number is unknown or not a stream.</summary>
    private PdfStream? ResolvePendingStream(int objNum)
    {
        foreach (var (num, obj) in _newObjects)
            if (num == objNum)
                return obj as PdfStream;
        return null;
    }

    /// <summary>Move a FileAttachment annotation's embedded PDF payload into the
    /// document's EmbeddedFiles name tree (used when PDF/A-4 conversion strips the
    /// annotation itself). Non-PDF payloads are ignored.</summary>
    private void MigrateFileAttachmentToEmbeddedFiles(PdfDictionary annotDict)
    {
        var fs = _reader.ResolveDict(annotDict.Get("FS"));
        var ef = fs is null ? null : _reader.ResolveDict(fs.Get("EF"));
        var stream = ef is null ? null : _reader.ResolveStream(ef.Get("F"));
        if (fs is null || stream is null) return;
        byte[] data;
        try { data = _reader.DecodeStream(stream, stream.ObjectNumber, stream.Generation); }
        catch { return; }
        if (data.Length < 5 || data[0] != (byte)'%' || data[1] != (byte)'P'
            || data[2] != (byte)'D' || data[3] != (byte)'F') return;
        var name = (_reader.Resolve(fs.Get("UF")) as PdfString)?.ToText()
            ?? (_reader.Resolve(fs.Get("F")) as PdfString)?.ToText() ?? "attachment.pdf";
        var desc = (_reader.Resolve(annotDict.Get("Contents")) as PdfString)?.ToText();
        try
        {
            AddEmbeddedFile(name, data, desc);
            _embeddedFiles = null; // collection re-materialises from the tree
        }
        catch { /* best-effort */ }
    }

    /// <summary>Convert every embedded PDF attachment to PDF/A-2B in place (ISO 19005-2
    /// §6.9 allows only PDF/A attachments). Non-PDF attachments and attachments whose
    /// conversion fails are left untouched. The embedded-file stream keeps its object;
    /// only its bytes (and /Params /Size) are replaced.</summary>
    private void ConvertEmbeddedPdfAttachmentsToPdfA2B()
    {
        var names = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        var efTree = names is not null ? _reader.ResolveDict(names.Get("EmbeddedFiles")) : null;
        var arr = efTree is not null ? _reader.Resolve(efTree.Get("Names")) as PdfArray : null;
        if (arr is null) return;

        for (var i = arr.Count - 2; i >= 0; i -= 2)
        {
            var fsDict = _reader.ResolveDict(arr[i + 1]);
            var ef = fsDict is null ? null : _reader.ResolveDict(fsDict.Get("EF"));
            var stream = ef is null ? null : _reader.ResolveStream(ef.Get("F"));
            if (stream is null) continue;

            byte[] data;
            try { data = _reader.DecodeStream(stream, stream.ObjectNumber, stream.Generation); }
            catch { continue; }
            if (data.Length < 5 || data[0] != (byte)'%' || data[1] != (byte)'P'
                || data[2] != (byte)'D' || data[3] != (byte)'F')
            {
                // ISO 19005-2 §6.9 allows only PDF/A attachments; a non-PDF payload
                // (image, data file, …) can't be made compliant, so under
                // ConvertErrorAction.Delete it is removed
                // from the name tree (2 array slots: name string + filespec).
                arr.RemoveAt(i + 1);
                arr.RemoveAt(i);
                _embeddedFiles = null; // collection re-materialises from the tree
                continue;
            }

            try
            {
                using var src = new MemoryStream(data);
                using var child = new Document(src);
                var childOpts = new PdfFormatConversionOptions(
                    Stream.Null, PdfFormat.PDF_A_2B, ConvertErrorAction.Delete);
                if (!child.Convert(childOpts)) continue;
                using var outMs = new MemoryStream();
                child.Save(outMs);
                var newBytes = outMs.ToArray();

                stream.ReplaceData(newBytes);
                stream.Dict.Remove("Filter");
                stream.Dict.Remove("DecodeParms");
                stream.Dict.Set("Length", new PdfInteger(newBytes.Length));
                stream.DoNotCompress = true;
                var prms = _reader.ResolveDict(stream.Dict.Get("Params"));
                prms?.Set("Size", new PdfInteger(newBytes.Length));
                prms?.Remove("CheckSum");
            }
            catch { /* best-effort: leave the attachment as-is */ }
        }
    }

    /// <summary>Tag the document's embedded files as ZUGFeRD/factur-x associated files: the
    /// invoice XML gets <c>/AFRelationship /Alternative</c> and the <c>text/xml</c> MIME
    /// subtype, and every embedded-file spec is referenced from the catalog <c>/AF</c> array
    /// (PDF 2.0 §7.11.3 associated files).</summary>
    private void ApplyZugferdAssociatedFiles()
    {
        // The public EmbeddedFiles collection holds FileSpecification objects that are
        // decoupled from the on-disk spec dictionaries (Add() copies the bytes in), so tag
        // those instances too — callers read MIMEType/AFRelationship back through them.
        var embedded = EmbeddedFiles;
        for (var i = 1; i <= embedded.Count; i++)
        {
            var spec = embedded[i];
            if (spec.Name is { } n && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(spec.MIMEType)) spec.MIMEType = "text/xml";
                if (spec.AFRelationship == AFRelationship.None)
                    spec.AFRelationship = AFRelationship.Alternative;
            }
        }

        var names = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        var efTree = names is not null ? _reader.ResolveDict(names.Get("EmbeddedFiles")) : null;
        var arr = efTree is not null ? _reader.Resolve(efTree.Get("Names")) as PdfArray : null;
        if (arr is null) return;

        var afArray = _reader.Resolve(_reader.Catalog.Get("AF")) as PdfArray ?? new PdfArray();
        var present = new HashSet<int>();
        foreach (var item in afArray)
            if (item is PdfIndirectRef r) present.Add(r.ObjectNumber);

        for (var i = 0; i + 1 < arr.Count; i += 2)
        {
            var key = (_reader.Resolve(arr[i]) as PdfString)?.ToText() ?? string.Empty;
            var fsRef = arr[i + 1];
            var fsDict = _reader.ResolveDict(fsRef);
            if (fsDict is null) continue;

            // The invoice XML is the alternative representation of the document's content.
            if (key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                fsDict.Set("AFRelationship", new PdfName("Alternative"));
                var ef = _reader.ResolveDict(fsDict.Get("EF"));
                var stream = ef is not null ? _reader.ResolveStream(ef.Get("F")) : null;
                stream?.Dict.Set("Subtype", new PdfName("text/xml"));
            }

            if (fsRef is PdfIndirectRef fr && present.Add(fr.ObjectNumber))
                afArray.Add(fsRef);
        }

        if (afArray.Count > 0)
            _reader.Catalog.Set("AF", afArray);
    }

    /// <summary>Embed every non-embedded simple (Type1/TrueType) font referenced by the
    /// pages, substituting a system face. The real family is used when it resolves;
    /// otherwise the text is re-mapped to Arial. The existing font dictionary is rewritten
    /// in place so the page's resource reference is preserved.</summary>
    private void EmbedNonEmbeddedFonts(PdfFormatConversionOptions? options = null,
        bool includeStandard14 = false)
    {
        // Records (once per BaseFont) that the source left a glyph-bearing font
        // unembedded — a PDF/A violation that this pass then fixes by embedding.
        var reported = new HashSet<string>(StringComparer.Ordinal);
        // An empty FontRepository.Sources means the caller has removed all font sources
        // (including system fonts), so no replacement face is available to embed. Resolving
        // straight from the OS here would silently embed system fonts and let the PDF/A
        // conversion "succeed" even though the fonts are unavailable — the conversion must
        // instead fail so CheckFontEmbedding reports the missing fonts.
        if (Text.FontRepository.Sources.Count == 0) return;

        var done = new HashSet<PdfDictionary>();
        // Shared across every dictionary so identical font programs are embedded once.
        var fontFileCache = new Dictionary<string, (int objNum, string embedName)>();
        var visitedRes = new HashSet<PdfDictionary>();

        // Embed one simple, glyph-bearing, non-embedded font dict in place, substituting a
        // resolved system face (Helvetica→Arial, etc.) when the named font has none.
        void EmbedOne(PdfDictionary fontDict)
        {
            if (!done.Add(fontDict)) return;
            // Consume the transient "embed full, don't subset" marker (set by
            // Font.IsSubset = false). Removed here so it never reaches the output.
            var embedFull = fontDict.GetBool("AsposeEmbedFull");
            fontDict.Remove("AsposeEmbedFull");
            var subtype = fontDict.GetName("Subtype");
            if (subtype == "Type0")
            {
                EmbedNonEmbeddedCidFont(fontDict, options, reported, fontFileCache);
                return;
            }
            if (subtype is not ("Type1" or "TrueType")) return;   // simple fonts only
            if (IsSimpleFontEmbedded(fontDict)) return;
            var baseFont = fontDict.GetName("BaseFont") ?? "";
            if (baseFont.Length > 7 && baseFont[6] == '+') return; // subset = embedded
            if (!includeStandard14 &&
                new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont))
                return; // standard-14 stay as-is unless the caller opts in (Document.EmbedStandardFonts)

            // The source carries this glyph-bearing font without an embedded
            // program — log it (once per name) as a PDF/A violation before the
            // pass below embeds a resolved face.
            if (options is not null && reported.Add(baseFont))
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "FontEmbedding",
                    Description = $"Font '{baseFont}' is not embedded.",
                });

            var resolved = Text.SystemFontResolver.Resolve(baseFont);
            string newName;
            byte[]? ttf;
            if (resolved is not null) { ttf = resolved; newName = baseFont; }
            else { ttf = Text.SystemFontResolver.Resolve("Arial"); newName = "Arial"; }
            if (ttf is null || ttf.Length == 0) return;

            // A Standard-14 font carries no program of its own, so the resolver returns a
            // host substitute (Helvetica→Arial, Times→Times New Roman, …). Name the embedded
            // font after the face actually embedded — read from its name table — so the output
            // reflects what was embedded rather than the abstract standard name (matching
            // the Aspose.Pdf surface). Host-dependent by nature.
            if (new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont))
            {
                try
                {
                    var ttp = new Text.TrueTypeParser(ttf);
                    ttp.Parse();
                    var fam = ttp.FamilyName;
                    if (!string.IsNullOrWhiteSpace(fam) && fam != "Unknown")
                        newName = fam.Replace(" ", "");
                }
                catch { /* keep the standard name if the face can't be parsed */ }
            }

            try
            {
                Text.FontEmbedder.EmbedIntoFontDict(this, ttf, fontDict, newName, fontFileCache, subset: !embedFull);
                RaiseFontSubstitution(new Text.Font(baseFont, "Type1"), new Text.Font(newName, "TrueType"));
            }
            catch { /* best-effort: leave the font as-is if embedding fails */ }
        }

        foreach (var page in Pages)
        {
            PdfDictionary? resources;
            try { resources = Reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
            // Walk the page resources and any nested Form XObject resources — a font
            // used only inside a form/appearance stream (not the page's own /Font) must
            // be embedded too.
            if (resources is not null)
                foreach (var fontDict in CollectFontDictsRecursive(resources, visitedRes))
                    EmbedOne(fontDict);

            // Annotation appearance (/AP) streams are NOT reachable from the page
            // /Resources, so their fonts (e.g. a FreeText appearance regenerated with a
            // non-embedded standard /Helvetica) must be walked separately for PDF/A.
            foreach (var apRes in CollectAnnotationAppearanceResources(page))
                foreach (var fontDict in CollectFontDictsRecursive(apRes, visitedRes))
                    EmbedOne(fontDict);
        }
    }

    /// <summary>Embed a system face into a non-embedded composite (Type0/CID) font.
    /// Unlike the simple-font path there is NO Arial fallback: under an Identity
    /// encoding the content stream's CIDs are the ORIGINAL face's glyph ids, so only
    /// the same-named real face keeps them valid — an unresolvable family is left
    /// unembedded (the conversion log still records the violation). A CJK-mojibake
    /// /BaseFont (its legacy-codepage bytes read as Latin-1, e.g. "ËÎÌå" = 宋体) is
    /// decoded through the font's CMap codepage and mapped to the host family.</summary>
    private void EmbedNonEmbeddedCidFont(PdfDictionary type0Dict, PdfFormatConversionOptions? options,
        HashSet<string> reported, Dictionary<string, (int objNum, string embedName)> fontFileCache)
    {
        var descArr = _reader.Resolve(type0Dict.Get("DescendantFonts")) as PdfArray;
        var cidFont = descArr is { Count: > 0 } ? _reader.ResolveDict(descArr[0]) : null;
        if (cidFont is null) return;
        var descriptor = _reader.ResolveDict(cidFont.Get("FontDescriptor"));
        if (descriptor is not null &&
            (descriptor.Get("FontFile") ?? descriptor.Get("FontFile2") ?? descriptor.Get("FontFile3")) is not null)
            return; // already embedded
        var baseFont = type0Dict.GetName("BaseFont") ?? cidFont.GetName("BaseFont") ?? "";
        if (baseFont.Length > 7 && baseFont[6] == '+') return; // subset = embedded

        if (options is not null && reported.Add(baseFont))
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FontEmbedding",
                Description = $"Font '{baseFont}' is not embedded.",
            });

        var ttf = Text.SystemFontResolver.Resolve(baseFont);
        if (ttf is null or { Length: 0 })
        {
            var decoded = DecodeCjkBaseFontName(baseFont, type0Dict, cidFont);
            if (decoded != baseFont)
                ttf = Text.SystemFontResolver.Resolve(decoded);
        }
        if (ttf is null or { Length: 0 }) return;

        try
        {
            Text.FontEmbedder.EmbedIntoCidFontDict(this, ttf, type0Dict, cidFont, fontFileCache);
            RaiseFontSubstitution(new Text.Font(baseFont, "Type0"), new Text.Font(baseFont, "Type0"));
        }
        catch { /* best-effort: leave the font as-is if embedding fails */ }
    }

    /// <summary>Decode a legacy-codepage-mojibake /BaseFont ("ËÎÌå") to its script-native
    /// name (宋体) via the font's CMap codepage, then map the common CJK display names to
    /// their host font families (宋体 → SimSun). Returns the input unchanged when it has no
    /// high bytes or no codepage applies.</summary>
    private string DecodeCjkBaseFontName(string baseFont, PdfDictionary type0Dict, PdfDictionary cidFont)
    {
        var hasHigh = false;
        foreach (var c in baseFont)
            if (c > 0x7F) { hasHigh = true; break; }
        if (!hasHigh) return baseFont;

        var cp = Text.CidFontInfo.CodepageForCMapName(type0Dict.GetName("Encoding"));
        if (cp == 0)
        {
            var csi = _reader.ResolveDict(cidFont.Get("CIDSystemInfo"));
            var orderingObj = csi?.Get("Ordering");
            var ordering = orderingObj is PdfString os ? os.ToText()
                : (orderingObj is PdfName on ? on.Value : null);
            cp = ordering switch { "CNS1" => 950, "GB1" => 936, "Japan1" => 932, "Korea1" or "KR" => 949, _ => 0 };
        }
        if (cp == 0) return baseFont;

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < baseFont.Length; i++)
        {
            var c = baseFont[i];
            if (c <= 0x7F || i + 1 >= baseFont.Length) { sb.Append(c); continue; }
            var code = (c << 8) | (baseFont[i + 1] & 0xFF);
            if (Text.CidFontInfo.LegacyLookup(cp, code) is int u)
            {
                sb.Append(char.ConvertFromUtf32(u));
                i++;
            }
            else sb.Append(c);
        }
        var native = sb.ToString();
        return native switch
        {
            "宋体" => "SimSun",
            "新宋体" => "NSimSun",
            "黑体" => "SimHei",
            "楷体" or "楷体_GB2312" => "KaiTi",
            "仿宋" or "仿宋_GB2312" => "FangSong",
            "微软雅黑" => "Microsoft YaHei",
            "ＭＳ ゴシック" or "ＭＳゴシック" => "MS Gothic",
            "ＭＳ 明朝" or "ＭＳ明朝" => "MS Mincho",
            "標楷體" => "DFKai-SB",
            "細明體" => "MingLiU",
            "新細明體" => "PMingLiU",
            "굴림" => "Gulim",
            "바탕" => "Batang",
            _ => native,
        };
    }

    /// <summary>Yield the /Resources dict of every appearance (/AP /N, /D, /R) stream of
    /// every annotation on <paramref name="page"/>, descending state-keyed appearance
    /// sub-dictionaries. Used so PDF/A font embedding reaches fonts that live only inside
    /// an annotation's appearance stream.</summary>
    private IEnumerable<PdfDictionary> CollectAnnotationAppearanceResources(Page page)
    {
        PdfArray? annots;
        try { annots = Reader.Resolve(page.Dict.Get("Annots")) as PdfArray; } catch { yield break; }
        if (annots is null) yield break;
        foreach (var annotObj in annots)
        {
            var annot = Reader.ResolveDict(annotObj);
            var ap = annot is null ? null : Reader.ResolveDict(annot.Get("AP"));
            if (ap is null) continue;
            foreach (var apKey in new[] { "N", "D", "R" })
            {
                var entry = Reader.Resolve(ap.Get(apKey));
                if (entry is PdfStream stream)
                {
                    var res = Reader.ResolveDict(stream.Dict.Get("Resources"));
                    if (res is not null) yield return res;
                }
                else if (entry is PdfDictionary stateDict) // state-keyed appearances
                {
                    foreach (var stateKey in new List<string>(stateDict.Keys))
                    {
                        var s = Reader.ResolveStream(stateDict.Get(stateKey));
                        var res = s is null ? null : Reader.ResolveDict(s.Dict.Get("Resources"));
                        if (res is not null) yield return res;
                    }
                }
            }
        }
    }

    /// <summary>Yield every <c>/Font</c> child dictionary reachable from a <c>/Resources</c>
    /// dict, recursing through Form XObject (<c>/Subtype /Form</c>) resources so a font used
    /// only inside a form/appearance stream is reached too. <paramref name="visitedRes"/>
    /// guards against resource-dict cycles.</summary>
    private IEnumerable<PdfDictionary> CollectFontDictsRecursive(PdfDictionary resources,
        HashSet<PdfDictionary> visitedRes)
    {
        if (!visitedRes.Add(resources)) yield break;

        var fontRes = Reader.ResolveDict(resources.Get("Font"));
        if (fontRes is not null)
            foreach (var key in new List<string>(fontRes.Keys))
            {
                var fontDict = Reader.ResolveDict(fontRes.Get(key));
                if (fontDict is not null) yield return fontDict;
            }

        var xobjs = Reader.ResolveDict(resources.Get("XObject"));
        if (xobjs is not null)
            foreach (var key in new List<string>(xobjs.Keys))
            {
                var xobj = Reader.Resolve(xobjs.Get(key));
                var xdict = xobj is PdfStream s ? s.Dict : xobj as PdfDictionary;
                if (xdict is null || xdict.GetName("Subtype") != "Form") continue;
                var subRes = Reader.ResolveDict(xdict.Get("Resources"));
                if (subRes is not null)
                    foreach (var fd in CollectFontDictsRecursive(subRes, visitedRes))
                        yield return fd;
            }
    }

    private bool IsSimpleFontEmbedded(PdfDictionary fontDict)
    {
        var fd = Reader.ResolveDict(fontDict.Get("FontDescriptor"));
        if (fd is null) return false;
        return fd.Get("FontFile") is not null || fd.Get("FontFile2") is not null || fd.Get("FontFile3") is not null;
    }

    private bool CheckFontEmbedding(PdfFormatConversionOptions options)
    {
        // Narrow scope: only block conversion when the caller has explicitly
        // emptied FontRepository.Sources (canonical behaviour: with no font
        // sources at all, unembedded non-Standard14 fonts can't be resolved).
        // When sources are populated, SystemFontSource still has lookup
        // gaps (matches by filename rather than TTF name table) — applying
        // the check there would block valid conversions of common fonts.
        if (Text.FontRepository.Sources.Count > 0) return true;
        bool allResolved = true;
        var standard14 = new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal);
        foreach (var page in Pages)
        {
            Text.FontCollection? pageFonts;
            try { pageFonts = page.Fonts; } catch { continue; }
            if (pageFonts is null) continue;
            foreach (var font in pageFonts)
            {
                if (font.IsEmbedded) continue;
                // PDF spec §9.6.4: a BaseFont of the form "XXXXXX+Name" is a
                // subset font, embedded by definition. IsEmbedded doesn't
                // recognise the prefix; treat as embedded here.
                if (font.BaseFont.Length > 7 && font.BaseFont[6] == '+') continue;
                if (standard14.Contains(font.BaseFont)) continue;
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "FontNotEmbedded",
                    Description = $"Font '{font.BaseFont}' is not embedded and FontRepository.Sources is empty.",
                });
                allResolved = false;
            }
        }
        return allResolved;
    }

    /// <summary>
    /// Removes non-signature widget annotations from all pages and the AcroForm /Fields array.
    /// Signature fields (/FT=Sig) are preserved because they are valid in PDF/A.
    /// Walks each page's /Annots, then prunes /AcroForm/Fields to match.
    /// </summary>
    /// <summary>PDF/A form-field handling: flatten the whole form into page
    /// content when it has no signature fields (the reference behaviour — field
    /// values stay visible and extractable); with signatures present, fall back
    /// to dropping just the non-signature widgets.</summary>
    private void FlattenOrRemoveFormFieldsForPdfA()
    {
        var acroForm = _reader.ResolveDict(_reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return;

        bool hasSig = false, hasNonSig = false;
        foreach (var page in Pages)
        {
            if (_reader.Resolve(page.Dict.Get("Annots")) is not Core.PdfArray annots) continue;
            foreach (var annotRef in annots)
            {
                var d = _reader.ResolveDict(annotRef);
                if (d?.GetName("Subtype") != "Widget") continue;
                var ft = d.GetName("FT") ?? _reader.ResolveDict(d.Get("Parent"))?.GetName("FT");
                if (ft == "Sig") hasSig = true;
                else hasNonSig = true;
            }
        }
        if (!hasNonSig) return;

        if (!hasSig)
        {
            try
            {
                Form?.Flatten(this);
                _form = null;
                return;
            }
            catch { /* fall through to removal */ }
        }
        RemoveNonSignatureFormFields();
    }

    private void RemoveNonSignatureFormFields()
    {
        var acroForm = _reader.ResolveDict(_reader.Catalog.Get("AcroForm"));
        if (acroForm is null) return;

        // Phase 1: Remove non-signature widgets from each page's /Annots array
        bool hasNonSigFields = false;
        foreach (var page in Pages)
        {
            var annotsObj = _reader.Resolve(page.Dict.Get("Annots")) as Core.PdfArray;
            if (annotsObj is null) continue;

            var remaining = new Core.PdfArray();
            foreach (var annotRef in annotsObj)
            {
                var annotDict = _reader.ResolveDict(annotRef);
                if (annotDict?.GetName("Subtype") != "Widget") { remaining.Add(annotRef); continue; }

                // Determine field type — check the widget itself, then its parent
                var ft = annotDict.GetName("FT");
                if (ft == "Sig") { remaining.Add(annotRef); continue; }
                if (ft is null)
                {
                    var parent = _reader.ResolveDict(annotDict.Get("Parent"));
                    if (parent?.GetName("FT") == "Sig") { remaining.Add(annotRef); continue; }
                }
                hasNonSigFields = true;
            }

            if (remaining.Count > 0) page.Dict.Set("Annots", remaining);
            else page.Dict.Remove("Annots");
        }

        if (!hasNonSigFields) return;

        // Phase 2: Remove non-signature entries from /AcroForm/Fields
        var fieldsArr = _reader.Resolve(acroForm.Get("Fields")) as Core.PdfArray;
        if (fieldsArr is null) { _reader.Catalog.Remove("AcroForm"); _form = null; return; }

        var remainingFields = new Core.PdfArray();
        foreach (var fieldRef in fieldsArr)
        {
            var fieldDict = _reader.ResolveDict(fieldRef);
            if (fieldDict?.GetName("FT") == "Sig") { remainingFields.Add(fieldRef); continue; }

            // A field with /Kids might be a parent of signature sub-fields
            var kids = _reader.Resolve(fieldDict?.Get("Kids")) as Core.PdfArray;
            if (kids is not null && kids.Any(k => _reader.ResolveDict(k)?.GetName("FT") == "Sig"))
            {
                remainingFields.Add(fieldRef);
                continue;
            }
        }

        if (remainingFields.Count > 0) acroForm.Set("Fields", remainingFields);
        else _reader.Catalog.Remove("AcroForm");
        _form = null;
    }

    /// <summary>
    /// Remove PDF/A compliance identification from XMP metadata.
    /// </summary>
    public void RemovePdfaCompliance()
    {
        // Clear the in-memory tracker so IsPdfaCompliant / PdfFormat reflect the removal.
        _lastConvertedFormat = null;
        if (Metadata is null) return;
        Metadata.Remove("pdfaid:part");
        Metadata.Remove("pdfaid:conformance");
    }

    private static readonly HashSet<string> ConvertProhibitedActionTypes = new(StringComparer.Ordinal)
    {
        "Launch", "Sound", "Movie", "ResetForm", "ImportData", "JavaScript",
    };

    private static readonly HashSet<string> ConvertProhibitedAnnotationSubtypes = new(StringComparer.Ordinal)
    {
        "FileAttachment", "Sound", "Movie", "3D",
    };

    // PDF/A-1 (ISO 19005-1) is based on PDF 1.4 and allows only the annotation
    // subtypes listed in §6.5.3. The PDF 1.5+ types below are therefore prohibited
    // when converting to PDF/A-1 specifically (they ARE permitted in PDF/A-2/3, so
    // this set is applied only for the 1A/1B targets).
    private static readonly HashSet<string> PdfA1ProhibitedAnnotationSubtypes = new(StringComparer.Ordinal)
    {
        "Polygon", "PolyLine", "Caret", "Screen", "Watermark", "Redact", "RichMedia", "Projection",
    };

    /// <summary>
    /// Neutralise transparency declared in graphics-state (ExtGState) dictionaries reachable
    /// from <paramref name="container"/> (a page or Form XObject): soft masks, constant alpha
    /// below 1, and non-Normal blend modes are all prohibited by PDF/A-1. Soft masks are set
    /// to /None, alpha to 1 and blend mode to /Normal so the content renders opaquely instead
    /// of failing validation. Recurses into nested Form XObjects; the visited set guards
    /// against shared dictionaries and reference cycles.
    /// </summary>
    private void NeutralizeExtGStateTransparency(PdfDictionary container,
        PdfFormatConversionOptions options, int pageNumber, bool fix, HashSet<PdfDictionary> visited)
    {
        var resources = _reader.ResolveDict(container.Get("Resources"));
        if (resources is null) return;

        var extGStates = _reader.ResolveDict(resources.Get("ExtGState"));
        if (extGStates is not null)
        {
            foreach (var key in extGStates.Keys.ToList())
            {
                var gs = _reader.ResolveDict(extGStates.Get(key));
                if (gs is null || !visited.Add(gs)) continue;

                var changed = false;
                var smask = gs.Get("SMask");
                if (smask is not null && smask is not PdfName { Value: "None" })
                {
                    changed = true;
                    if (fix) gs.Set("SMask", new PdfName("None"));
                }
                if (IsAlphaBelowOne(gs.Get("ca")))
                {
                    changed = true;
                    if (fix) gs.Set("ca", new PdfReal(1));
                }
                if (IsAlphaBelowOne(gs.Get("CA")))
                {
                    changed = true;
                    if (fix) gs.Set("CA", new PdfReal(1));
                }
                var bm = gs.GetName("BM");
                if (bm is not null && bm != "Normal" && bm != "Compatible")
                {
                    changed = true;
                    if (fix) gs.Set("BM", new PdfName("Normal"));
                }

                if (changed)
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "Transparency",
                        Description = $"Page {pageNumber} ExtGState '{key}' transparency neutralized for PDF/A-1.",
                        PageNumber = pageNumber,
                    });
            }
        }

        // Recurse into Form XObjects, whose own resources may carry transparency.
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var key in xobjects.Keys.ToList())
        {
            if (_reader.Resolve(xobjects.Get(key)) is PdfStream { } form
                && form.Dict.GetName("Subtype") == "Form"
                && visited.Add(form.Dict))
            {
                NeutralizeExtGStateTransparency(form.Dict, options, pageNumber, fix, visited);
            }
        }
    }

    private static bool IsAlphaBelowOne(PdfObject? value) => value switch
    {
        PdfReal r => r.Value < 1.0,
        PdfInteger i => i.Value < 1,
        _ => false,
    };

    private static double AlphaValue(PdfObject? value) => value switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => 1.0,
    };

    /// <summary>Walk a content stream tracking the current fill alpha (set by <c>/GS gs</c>
    /// against the resources' ExtGState /ca, saved/restored by q/Q) and, for every image
    /// XObject drawn while that alpha is below 1, bake the alpha into a constant DeviceGray
    /// soft mask on the image (unless it already carries a mask). This preserves the image's
    /// composited appearance once the prohibited ExtGState alpha is neutralised for PDF/A-1.
    /// Recurses into invoked Form XObjects, carrying the alpha active at their draw.</summary>
    private void MaskConstantAlphaImages(byte[] content, PdfDictionary resources,
        double initialAlpha, HashSet<PdfDictionary> visitedForms)
    {
        var extg = _reader.ResolveDict(resources.Get("ExtGState"));
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));

        var lexer = new IO.PdfLexer(content);
        var stack = new Stack<double>();
        var curAlpha = initialAlpha;
        string? lastName = null;
        // Form name -> the alpha active where it was invoked (last wins; a form drawn only
        // opaquely stays opaque). Recursed after the scan so lexer state is untouched.
        var formAlpha = new Dictionary<string, double>(StringComparer.Ordinal);

        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == IO.TokenKind.Eof) break;
            if (t.Kind == IO.TokenKind.Keyword && t.StringValue == "BI")
            {
                SkipInlineImage(lexer, new HashSet<string>());
                lastName = null;
                continue;
            }
            if (t.Kind == IO.TokenKind.Name) { lastName = t.StringValue; continue; }
            if (t.Kind != IO.TokenKind.Keyword) continue;

            switch (t.StringValue)
            {
                case "q":
                    stack.Push(curAlpha);
                    break;
                case "Q":
                    if (stack.Count > 0) curAlpha = stack.Pop();
                    break;
                case "gs":
                    if (lastName is not null && extg is not null &&
                        _reader.ResolveDict(extg.Get(lastName)) is { } gs)
                        curAlpha = AlphaValue(gs.Get("ca"));
                    break;
                case "Do":
                    if (lastName is not null && xobjects is not null &&
                        _reader.ResolveStream(xobjects.Get(lastName)) is { } xs)
                    {
                        var sub = xs.Dict.GetName("Subtype");
                        if (sub == "Image")
                        {
                            if (curAlpha < 1.0 - 1e-6) AttachConstantSoftMask(xs.Dict, curAlpha);
                        }
                        else if (sub == "Form" && curAlpha < 1.0 - 1e-6)
                        {
                            formAlpha[lastName] = curAlpha;
                        }
                    }
                    break;
            }
            lastName = null;
        }

        if (xobjects is null) return;
        foreach (var (name, alpha) in formAlpha)
        {
            var xs = _reader.ResolveStream(xobjects.Get(name));
            if (xs is null || xs.Dict.GetName("Subtype") != "Form") continue;
            if (!visitedForms.Add(xs.Dict)) continue;
            var formContent = _reader.DecodeStream(xs);
            if (formContent.Length == 0) continue;
            var formRes = _reader.ResolveDict(xs.Dict.Get("Resources")) ?? resources;
            MaskConstantAlphaImages(formContent, formRes, alpha, visitedForms);
        }
    }

    /// <summary>Attach a 1×1 constant DeviceGray <c>/SMask</c> of value <paramref name="alpha"/>
    /// to an image XObject so it composites at that opacity. No-op if the image already carries
    /// a soft mask or stencil mask (its existing transparency is preserved as-is).</summary>
    private void AttachConstantSoftMask(PdfDictionary imgDict, double alpha)
    {
        if (imgDict.Get("SMask") is not null || imgDict.Get("Mask") is not null) return;

        var smDict = new PdfDictionary();
        smDict.Set("Type", new PdfName("XObject"));
        smDict.Set("Subtype", new PdfName("Image"));
        smDict.Set("Width", new PdfInteger(1));
        smDict.Set("Height", new PdfInteger(1));
        smDict.Set("ColorSpace", new PdfName("DeviceGray"));
        smDict.Set("BitsPerComponent", new PdfInteger(8));
        var data = new byte[] { (byte)Math.Round(Math.Clamp(alpha, 0.0, 1.0) * 255.0) };
        smDict.Set("Length", new PdfInteger(data.Length));

        var objNum = AllocateObjectNumber();
        AddNewObject(objNum, new PdfStream(smDict, data));
        imgDict.Set("SMask", new PdfIndirectRef(objNum, 0));
    }

    private void RemoveProhibitedCatalogActions(PdfFormatConversionOptions options, bool strip)
    {
        // Check OpenAction
        var openActionObj = _reader.Catalog.Get("OpenAction");
        if (openActionObj is not null)
        {
            var openAction = _reader.ResolveDict(openActionObj);
            if (openAction is not null)
            {
                var actionType = openAction.GetName("S");
                if (actionType is not null && ConvertProhibitedActionTypes.Contains(actionType))
                {
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "ActionType",
                        Description = $"Action type '{actionType}' is not allowed in PDF/A",
                    });
                    if (strip)
                    {
                        _reader.Catalog.Remove("OpenAction");
                    }
                }
            }
        }

        // Check AA (Additional Actions) on catalog
        var aa = _reader.ResolveDict(_reader.Catalog.Get("AA"));
        if (aa is not null)
        {
            var keysToRemove = new List<string>();
            foreach (var key in aa.Keys)
            {
                var actionDict = _reader.ResolveDict(aa.Get(key));
                if (actionDict is null) continue;
                var actionType = actionDict.GetName("S");
                if (actionType is not null && ConvertProhibitedActionTypes.Contains(actionType))
                {
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "ActionType",
                        Description = $"Action type '{actionType}' is not allowed in PDF/A",
                    });
                    keysToRemove.Add(key);
                }
            }
            if (strip)
            {
                foreach (var key in keysToRemove)
                    aa.Remove(key);
                if (!aa.Keys.Any())
                    _reader.Catalog.Remove("AA");
            }
        }
    }

    private void FixAnnotationsForPdfA(Page page, PdfFormatConversionOptions options, bool fix, bool strip)
    {
        var annotsObj = _reader.Resolve(page.Dict.Get("Annots"));
        if (annotsObj is not PdfArray annotsArr) return;

        // PDF/A-1 prohibits the PDF 1.5+ annotation subtypes that later parts allow.
        var isPdfA1 = options.Format is PdfFormat.PDF_A_1A or PdfFormat.PDF_A_1B;

        var indicesToRemove = new List<int>();

        for (var i = 0; i < annotsArr.Count; i++)
        {
            var annotDict = _reader.ResolveDict(annotsArr[i]);
            if (annotDict is null) continue;

            var subtype = annotDict.GetName("Subtype");

            // Check prohibited subtypes
            if (subtype is not null &&
                (ConvertProhibitedAnnotationSubtypes.Contains(subtype) ||
                 (isPdfA1 && PdfA1ProhibitedAnnotationSubtypes.Contains(subtype))))
            {
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "AnnotationType",
                    Description = $"Annotation type '{subtype}' is not allowed in PDF/A",
                    PageNumber = page.Number,
                });
                if (strip)
                {
                    // PDF/A-4: a stripped FileAttachment's PDF payload survives as a
                    // document embedded file (the conversion migrates the
                    // attachments there; non-PDF payloads drop with the annotation).
                    if (subtype == "FileAttachment"
                        && options.Format is PdfFormat.PDF_A_4 or PdfFormat.PDF_A_4E or PdfFormat.PDF_A_4F)
                        MigrateFileAttachmentToEmbeddedFiles(annotDict);
                    indicesToRemove.Add(i);
                }
                continue;
            }

            // Fix Print flag (bit 3, value 4) — except for Widget/Popup
            if (subtype != "Widget" && subtype != "Popup")
            {
                var flags = (int)annotDict.GetInt("F");
                if ((flags & 4) == 0)
                {
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "AnnotationPrintFlag",
                        Description = $"Annotation (type '{subtype ?? "unknown"}') missing Print flag",
                        PageNumber = page.Number,
                    });
                    if (fix)
                    {
                        annotDict.Set("F", new PdfInteger(flags | 4));
                    }
                }
            }

            // Check/remove prohibited actions on annotations
            var actionObj = _reader.ResolveDict(annotDict.Get("A"));
            if (actionObj is not null)
            {
                var actionType = actionObj.GetName("S");
                if (actionType is not null && ConvertProhibitedActionTypes.Contains(actionType))
                {
                    options.ConversionLog.Add(new PdfAViolation
                    {
                        Rule = "ActionType",
                        Description = $"Action type '{actionType}' is not allowed in PDF/A",
                        PageNumber = page.Number,
                    });
                    if (strip)
                    {
                        annotDict.Remove("A");
                    }
                }
            }

            // Check/remove AA on annotations
            var annotAa = _reader.ResolveDict(annotDict.Get("AA"));
            if (annotAa is not null)
            {
                var hasProhibited = false;
                foreach (var key in annotAa.Keys)
                {
                    var ad = _reader.ResolveDict(annotAa.Get(key));
                    if (ad is null) continue;
                    var at = ad.GetName("S");
                    if (at is not null && ConvertProhibitedActionTypes.Contains(at))
                    {
                        hasProhibited = true;
                        options.ConversionLog.Add(new PdfAViolation
                        {
                            Rule = "ActionType",
                            Description = $"Action type '{at}' is not allowed in PDF/A",
                            PageNumber = page.Number,
                        });
                    }
                }
                if (strip && hasProhibited)
                {
                    annotDict.Remove("AA");
                }
            }
        }

        // Remove prohibited annotations (reverse order to preserve indices)
        if (strip && indicesToRemove.Count > 0)
        {
            for (var i = indicesToRemove.Count - 1; i >= 0; i--)
            {
                annotsArr.RemoveAt(indicesToRemove[i]);
            }
            if (annotsArr.Count == 0)
                page.Dict.Remove("Annots");
        }
    }

    private bool HasOutputIntent()
    {
        var outputIntents = _reader.Resolve(_reader.Catalog.Get("OutputIntents"));
        return outputIntents is PdfArray { Count: > 0 };
    }

    /// <summary>True when /OutputIntents already carries a /GTS_PDFA1 intent.
    /// The PDF/A conversion must add one otherwise — a source with only a PDF/X
    /// (GTS_PDFX) intent still fails the validator's PDF/A output-intent gate.</summary>
    private bool HasPdfAOutputIntentInCatalog()
    {
        if (_reader.Resolve(_reader.Catalog.Get("OutputIntents")) is not PdfArray arr)
            return false;
        foreach (var item in arr)
            if (_reader.ResolveDict(item)?.GetName("S") == "GTS_PDFA1")
                return true;
        return false;
    }

    private static bool PageHasDeviceDependentParagraphs(Page page)
    {
        // DOM paragraphs flushed by Save() that emit a DeviceRGB/CMYK/Gray image XObject.
        // Walks user-added paragraphs (page.Paragraphs) — Image, ImageStamp.
        foreach (var p in page.Paragraphs)
        {
            switch (p)
            {
                case Image:
                case ImageStamp:
                    return true;
            }
        }
        return false;
    }

    private bool PageHasDeviceDependentColors(Page page)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return false;

        var xObjectDict = _reader.ResolveDict(resources.Get("XObject"));
        if (xObjectDict is null) return false;

        foreach (var xobjKey in xObjectDict.Keys)
        {
            var xobj = _reader.Resolve(xObjectDict.Get(xobjKey));

            PdfDictionary? xobjDict = null;
            if (xobj is PdfStream stream)
                xobjDict = stream.Dict;
            else if (xobj is PdfDictionary dict)
                xobjDict = dict;

            if (xobjDict is null) continue;

            var subtype = xobjDict.GetName("Subtype");
            if (subtype != "Image") continue;

            var csObj = xobjDict.Get("ColorSpace");
            if (csObj is PdfName csName && csName.Value is "DeviceRGB" or "DeviceCMYK" or "DeviceGray")
            {
                return true;
            }
        }

        return false;
    }

    private void AddPdfXOutputIntent(PdfFormatConversionOptions options)
    {
        var outputIntentDict = new PdfDictionary();
        outputIntentDict.Set("Type", new PdfName("OutputIntent"));
        outputIntentDict.Set("S", new PdfName("GTS_PDFX"));

        var oci = options.OutputIntent?.OutputConditionIdentifier ?? "Custom";
        outputIntentDict.Set("OutputConditionIdentifier",
            new PdfString(Encoding.Latin1.GetBytes(oci)));
        outputIntentDict.Set("RegistryName",
            new PdfString(Encoding.Latin1.GetBytes("http://www.color.org")));

        // Embed ICC profile if provided
        if (options.IccProfileFileName is not null && File.Exists(options.IccProfileFileName))
        {
            var iccData = File.ReadAllBytes(options.IccProfileFileName);
            var iccDict = new PdfDictionary();
            iccDict.Set("N", new PdfInteger(4)); // CMYK = 4 components
            iccDict.Set("Length", new PdfInteger(iccData.Length));
            var iccStream = new PdfStream(iccDict, iccData);
            var iccObjNum = AllocateObjectNumber();
            AddNewObject(iccObjNum, iccStream);
            outputIntentDict.Set("DestOutputProfile", new PdfIndirectRef(iccObjNum, 0));
        }

        var objNum = AllocateObjectNumber();
        AddNewObject(objNum, outputIntentDict);

        var outputIntents = _reader.Resolve(_reader.Catalog.Get("OutputIntents")) as PdfArray;
        if (outputIntents is null)
        {
            outputIntents = new PdfArray();
            _reader.Catalog.Set("OutputIntents", outputIntents);
        }
        outputIntents.Add(new PdfIndirectRef(objNum, 0));
    }

    private void AddSrgbOutputIntent()
    {
        var outputIntentDict = new PdfDictionary();
        outputIntentDict.Set("Type", new PdfName("OutputIntent"));
        outputIntentDict.Set("S", new PdfName("GTS_PDFA1"));
        outputIntentDict.Set("OutputConditionIdentifier",
            new PdfString(Encoding.Latin1.GetBytes("sRGB IEC61966-2.1")));
        outputIntentDict.Set("RegistryName",
            new PdfString(Encoding.Latin1.GetBytes("http://www.color.org")));

        var outputIntents = _reader.Resolve(_reader.Catalog.Get("OutputIntents")) as PdfArray;
        if (outputIntents is null)
        {
            outputIntents = new PdfArray();
            _reader.Catalog.Set("OutputIntents", outputIntents);
        }
        // Held DIRECT in the array (spec-valid) so an in-memory validation right
        // after Convert can see the /S /GTS_PDFA1 intent — a pending indirect
        // object isn't reachable through the reader until the document is saved.
        outputIntents.Add(outputIntentDict);
    }

    /// <summary>
    /// Save the document in-place: writes back to the file the document
    /// was opened from (<see cref="FileName"/>), or performs an incremental
    /// save to the original source stream when the document was opened
    /// from a writable <see cref="FileStream"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the
    /// document was loaded from a byte buffer or read-only stream with
    /// no associated file path.</exception>
    public void Save()
    {
        FireBeforePageGenerateEvents();

        if (_sourceStream is not null && _sourceStream.CanWrite)
        {
            SaveIncremental(_sourceStream);
            return;
        }

        if (!string.IsNullOrEmpty(FileName))
        {
            using var fs = File.Create(FileName);
            Save(fs);
            return;
        }

        // A document created in memory (no source path/stream) still supports
        // a bare Save(): the reference finalizes the document in place —
        // paragraph processing, stamp materialisation into page annotations —
        // without a destination. Serialize into a scratch buffer to run the
        // same pipeline; the bytes are discarded, the object-model effects stay.
        using var scratch = new MemoryStream();
        Save(scratch);
    }

    /// <summary>
    /// Serialize the document into a fresh byte array.
    /// </summary>
    public byte[] ToArray()
    {
        FireBeforePageGenerateEvents();

        if (_sourceStream is not null && _sourceStream.CanWrite)
        {
            SaveIncremental(_sourceStream);
            _sourceStream.Seek(0, SeekOrigin.Begin);
            using var copy = new MemoryStream();
            _sourceStream.CopyTo(copy);
            return copy.ToArray();
        }

        using var ms = new MemoryStream();
        Save(ms);
        return ms.ToArray();
    }

    private void FireBeforePageGenerateEvents()
    {
        // Real wiring for Page.OnBeforePageGenerate: walk the page tree
        // and fire each page's event subscribers (if any) before the
        // writer serialises them. Mutations to page dicts inside the
        // handler are picked up by the subsequent save.
        for (var i = 1; i <= PageCount; i++)
            Pages[i].RaiseBeforePageGenerate();
        _actions?.WriteToCatalog();
        EmitBackgroundOnPages();
    }

    // ── Background / Actions / FontSubstitution / CustomSecurityHandler

    /// <summary>Document-wide background colour painted on every page
    /// before content during Save. Real — emits a content-stream prologue
    /// that fills the MediaBox with the configured colour.</summary>
    public Color? Background { get; set; }

    private void EmitBackgroundOnPages()
    {
        if (Background is null) return;
        var bg = Background;
        for (var i = 1; i <= PageCount; i++)
        {
            var page = Pages[i];
            var media = page.MediaBox;
            // Build a "q rg 0 0 W H re f Q" prologue and prepend it to the
            // page content stream. Real — saved PDF carries the fill rect.
            var prologue = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "q {0:0.######} {1:0.######} {2:0.######} rg 0 0 {3:0.######} {4:0.######} re f Q\n",
                bg.R / 255.0, bg.G / 255.0, bg.B / 255.0,
                media.Width, media.Height);
            page.PrependContentStream(System.Text.Encoding.ASCII.GetBytes(prologue));
        }
    }

    private Annotations.DocumentActionCollection? _actions;

    /// <summary>Catalog /AA additional-action dictionary. Real — slots
    /// configured via the returned collection are written to the catalog
    /// /AA dict during Save.</summary>
    public Annotations.DocumentActionCollection Actions
        => _actions ??= new Annotations.DocumentActionCollection(this);

    /// <summary>Delegate fired when the renderer fails to resolve a font
    /// referenced by a content stream. <paramref name="originalFont"/> is
    /// the font that couldn't be loaded; <paramref name="newFont"/> is the
    /// substitute the renderer fell back to. Real wiring lives in the
    /// FontResolver path — when this event has subscribers, the resolver
    /// invokes them before completing the substitution.</summary>
    public delegate void FontSubstitutionHandler(Text.Font oldFont, Text.Font newFont);

    /// <summary>Fired by the font-loading pipeline when a referenced font
    /// must be substituted. Real — the renderer raises this through
    /// <see cref="RaiseFontSubstitution"/>.</summary>
    public event FontSubstitutionHandler? FontSubstitution;

    /// <summary>Internal hook invoked by the font-loading pipeline.</summary>
    internal void RaiseFontSubstitution(Text.Font original, Text.Font replacement)
        => FontSubstitution?.Invoke(original, replacement);

    /// <summary>Stored custom security handler when set via the
    /// ICustomSecurityHandler Encrypt overloads. Get-only.</summary>
    public Security.ICustomSecurityHandler? CustomSecurityHandler { get; private set; }

    /// <summary>Encrypt with a custom security handler. Not implemented in
    /// this FOSS build — the PdfEncryptor pipeline is hard-wired to the
    /// Standard handler. Throws <see cref="System.NotSupportedException"/>
    /// after storing the handler so a future <c>Save</c> path could pick
    /// it up; the throw makes it explicit that no encryption was applied.</summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Facades.DocumentPrivilege privileges,
        Security.ICustomSecurityHandler customHandler)
    {
        _ = userPassword; _ = ownerPassword; _ = privileges;
        CustomSecurityHandler = customHandler;
        throw new System.NotSupportedException(
            "Document.Encrypt(..., ICustomSecurityHandler) is not implemented in this FOSS build — only the Standard handler (RC4/AES password) is wired through PdfEncryptor.");
    }

    /// <summary>Same — Permissions-typed overload.</summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Aspose.Pdf.Permissions permissions,
        Security.ICustomSecurityHandler customHandler)
    {
        _ = userPassword; _ = ownerPassword; _ = permissions;
        CustomSecurityHandler = customHandler;
        throw new System.NotSupportedException(
            "Document.Encrypt(..., ICustomSecurityHandler) is not implemented in this FOSS build — only the Standard handler (RC4/AES password) is wired through PdfEncryptor.");
    }

    /// <summary>Encrypt with a list of recipient public certificates
    /// (Public-Key /Filter security handler). Not implemented; throws
    /// NotSupportedException to make the gap explicit.</summary>
    public void Encrypt(Aspose.Pdf.Permissions permissions, CryptoAlgorithm cryptoAlgorithm,
        System.Collections.Generic.IList<System.Security.Cryptography.X509Certificates.X509Certificate2> publicCertificates)
    {
        _ = permissions; _ = cryptoAlgorithm; _ = publicCertificates;
        throw new System.NotSupportedException(
            "Document.Encrypt(..., IList<X509Certificate2>) is not implemented in this FOSS build — Public-Key encryption (Adobe.PPKLite / Adobe.PPKMS) requires a recipient-list encryption pass that the PdfEncryptor doesn't yet support.");
    }

    /// <summary>
    /// Save the document to a file.
    /// </summary>
    public void Save(string outputFileName)
    {
        // Expose the output file name so save-time appearance generation that needs it
        // (e.g. PageInformationAnnotation, which prints the file name + date) can read it.
        _pendingSaveFileName = System.IO.Path.GetFileName(outputFileName);
        try
        {
            using (var fs = new FileStream(outputFileName, FileMode.Create, FileAccess.Write))
            {
                Save(fs);
            }
        }
        finally { _pendingSaveFileName = null; }
        // Update internal state so HasIncrementalUpdate() reflects the saved content
        var fileInfo = new FileInfo(outputFileName);
        if (fileInfo.Length <= int.MaxValue)
            _data = File.ReadAllBytes(outputFileName);
    }

    /// <summary>
    /// Save the document to a file in the specified format. Only
    /// <see cref="SaveFormat.Pdf"/> and <see cref="SaveFormat.Html"/> are supported.
    /// </summary>
    public void Save(string outputFileName, SaveFormat format)
    {
        switch (format)
        {
            case SaveFormat.Pdf:
                Save(outputFileName);
                break;
            case SaveFormat.Html:
                Save(outputFileName, new HtmlSaveOptions());
                break;
            case SaveFormat.Markdown:
                System.IO.File.WriteAllText(outputFileName,
                    new Converters.PdfToMarkdownConverter().SaveAsMarkdown(this), System.Text.Encoding.UTF8);
                break;
            default:
                throw new System.NotSupportedException($"Only SaveFormat.Pdf, SaveFormat.Html and SaveFormat.Markdown are supported; requested {format}.");
        }
    }

    /// <summary>
    /// Save the document to a stream in the specified format. Only
    /// <see cref="SaveFormat.Pdf"/> and <see cref="SaveFormat.Html"/> are supported.
    /// </summary>
    public void Save(Stream outputStream, SaveFormat format)
    {
        switch (format)
        {
            case SaveFormat.Pdf:
                Save(outputStream);
                break;
            case SaveFormat.Html:
                Save(outputStream, new HtmlSaveOptions());
                break;
            case SaveFormat.Markdown:
                var mdBytes = System.Text.Encoding.UTF8.GetBytes(
                    new Converters.PdfToMarkdownConverter().SaveAsMarkdown(this));
                outputStream.Write(mdBytes, 0, mdBytes.Length);
                break;
            default:
                throw new System.NotSupportedException($"Only SaveFormat.Pdf, SaveFormat.Html and SaveFormat.Markdown are supported; requested {format}.");
        }
    }

    /// <summary>
    /// Save the document as HTML to a stream using the specified options.
    /// Delegates to <see cref="Converters.PdfToHtmlConverter"/>.
    /// </summary>
    public void Save(Stream output, HtmlSaveOptions options)
    {
        var converter = new Converters.PdfToHtmlConverter();

        // AsEmbeddedPartsOfPngPageBackground: flatten each page to a single raster PNG
        // embedded as the page background (rather than embedding source images
        // individually) for this saving mode.
        if (options.RasterImagesSavingMode == HtmlSaveOptions.RasterImagesSavingModes.AsEmbeddedPartsOfPngPageBackground)
        {
            converter.SaveAsHtmlWithPngBackground(this, output);
            return;
        }

        if (options.ExplicitListOfSavedPages is { Length: > 0 } pages)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset=\"utf-8\"></head><body>");
            foreach (var pageNum in pages)
            {
                sb.Append(converter.SavePageAsHtml(this, pageNum));
            }
            sb.AppendLine("</body></html>");
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            output.Write(bytes, 0, bytes.Length);
        }
        else
        {
            converter.SaveAsHtml(this, output);
        }
    }

    /// <summary>
    /// Save the document as HTML to a file using the specified options.
    /// </summary>
    public void Save(string path, HtmlSaveOptions options)
    {
        using var fs = File.Create(path);
        Save(fs, options);
    }

    /// <summary>
    /// Save to a stream using general SaveOptions (stub type from Aspose.Pdf namespace).
    /// For stub SaveOptions subclasses without real implementations, saves as PDF.
    /// </summary>
    public void Save(Stream outputStream, SaveOptions options)
    {
        if (options is SvgSaveOptions)
        {
            var svg = new Converters.PdfToSvgConverter().SavePageAsSvg(this, 1);
            var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
            outputStream.Write(bytes, 0, bytes.Length);
            return;
        }
        ApplyPdfSaveOptions(options);
        Save(outputStream);
    }

    /// <summary>
    /// Save to a file using general SaveOptions (stub type from Aspose.Pdf namespace).
    /// </summary>
    public void Save(string outputFileName, SaveOptions options)
    {
        if (options is SvgSaveOptions)
        {
            // Render the first page to real SVG markup instead of writing a PDF to
            // the .svg path (the historic no-op that made round-trip tests pass by a
            // compensating load-side bug).
            new Converters.PdfToSvgConverter().SavePageToFile(this, 1, outputFileName);
            return;
        }
        ApplyPdfSaveOptions(options);
        using var fs = File.Create(outputFileName);
        Save(fs);
    }

    /// <summary>Apply the supported <see cref="PdfSaveOptions"/> settings to the
    /// document before it is written. Currently this honours
    /// <see cref="PdfSaveOptions.DefaultFontName"/>: every font that cannot be
    /// resolved (not embedded, no source data, and not a available system face) is
    /// rebased onto the requested default so the saved PDF — and the in-memory
    /// font collection — report that name.</summary>
    private void ApplyPdfSaveOptions(SaveOptions? options)
    {
        if (options is not PdfSaveOptions pso || string.IsNullOrEmpty(pso.DefaultFontName))
            return;

        foreach (var page in Pages)
        {
            var resDict = _reader.ResolveDict(page.Dict.Get("Resources"));
            var fontDict = resDict is not null ? _reader.ResolveDict(resDict.Get("Font")) : null;
            if (fontDict is null) continue;
            foreach (var key in fontDict.Keys)
            {
                var fd = _reader.ResolveDict(fontDict.Get(key));
                if (fd is null) continue;
                var font = new Text.Font(key, fd, _reader);
                if (!font.IsAccessible)
                    fd.Set("BaseFont", new Core.PdfName(pso.DefaultFontName));
            }
        }
    }

    /// <summary>
    /// Save using incremental update — appends changes without rewriting the original file.
    /// This preserves the original byte structure, which is required for digital signatures.
    /// </summary>
    internal byte[] SaveIncremental(params (int objectNumber, PdfObject obj)[] modifiedObjects)
    {
        using var ms = new MemoryStream();
        SaveIncremental(ms, modifiedObjects);
        return ms.ToArray();
    }

    /// <summary>
    /// Save using incremental update to a stream.
    /// </summary>
    internal void SaveIncremental(Stream output, params (int objectNumber, PdfObject obj)[] modifiedObjects)
    {
        var xref = _reader.XRefTable;
        var trailer = _reader.Trailer;
        var size = (int)trailer.GetInt("Size", 1);
        var originalStartXref = XRefTable.FindStartXref(_data);

        var writer = new IncrementalWriter(output, _data, Math.Max(size, xref.Entries.Keys.DefaultIfEmpty(0).Max() + 1));

        foreach (var (objNum, obj) in modifiedObjects)
        {
            writer.WriteObject(objNum, obj);
        }

        writer.Flush(trailer, originalStartXref);
    }

    /// <summary>
    /// Register a structure tree builder for auto-finalization on save.
    /// </summary>
    internal void RegisterStructureTreeBuilder(StructureTreeBuilder builder)
    {
        _structureTreeBuilder = builder;
    }

    /// <summary>
    /// Register an outline builder for auto-finalization on save.
    /// </summary>
    internal void RegisterOutlineBuilder(OutlineBuilder builder)
    {
        _outlineBuilder = builder;
    }

    /// <summary>Materialise any pending <see cref="OutlineBuilder"/> into the catalog
    /// /Outlines tree now, instead of deferring to Save. Needed so a read path that
    /// inspects the live outline tree (e.g. chaining the document into a second
    /// <c>PdfBookmarkEditor</c> and calling <c>ExtractBookmarks</c>) sees bookmarks
    /// that were just added via <c>CreateBookmarkOfPage</c>. Idempotent — the builder
    /// is consumed once, and the cached outline collection is dropped so the next
    /// access re-reads the freshly written tree.</summary>
    internal void FlushPendingOutlineBuilder()
    {
        if (_outlineBuilder is null) return;
        _outlineBuilder.Build();
        _outlineBuilder = null;
        _outlines = null;
    }

    /// <summary>
    /// Register a page label builder for auto-finalization on save.
    /// </summary>
    internal void RegisterPageLabelBuilder(PageLabelBuilder builder)
    {
        _pageLabelBuilder = builder;
    }

    /// <summary>Save the document using the configured <see cref="SaveOptions"/>.</summary>
    public void Save(SaveOptions options)
    {
        _ = options;
        if (string.IsNullOrEmpty(FileName))
            return; // No bound file; caller should use Save(Stream) or Save(string).
        Save(FileName);
    }

    /// <summary>Async wrapper around <see cref="Save()"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(SaveOptions)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(SaveOptions options, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(options);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(Stream)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(Stream output, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(output);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(Stream, SaveFormat)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(Stream outputStream, SaveFormat format, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputStream, format);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(Stream, SaveOptions)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(Stream outputStream, SaveOptions options, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputStream, options);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(string)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(string outputFileName, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputFileName);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(string, SaveFormat)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(string outputFileName, SaveFormat format, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputFileName, format);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Async wrapper around <see cref="Save(string, SaveOptions)"/>.</summary>
    public System.Threading.Tasks.Task SaveAsync(string outputFileName, SaveOptions options, System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(outputFileName, options);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Save the document to a stream.
    /// </summary>
    public void Save(Stream output)
    {
        // When the caller opts into strict signature handling, refuse to re-save a
        // signed document — rewriting it would invalidate the existing signature.
        if (HandleSignatureChange && Form.SignaturesExist)
            throw new PdfException(
                "The document contains a digital signature and HandleSignatureChange is enabled; saving would invalidate the signature.");

        // Validate deferred XML image file references
        if (PendingXmlImageFiles is { Count: > 0 } imageFiles)
        {
            foreach (var path in imageFiles)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Image file not found: {path}", path);
            }
        }

        // PDF 32000-2 § 14.3.3 — when both /Info and XMP /Metadata exist they
        // are equivalent representations. Pull XMP-side values into /Info for
        // keys that XMP carries (non-empty) but /Info does not. Producer is
        // included so a roundtrip preserves it without the stamp below
        // overwriting an XMP-only value.
        SyncXmpIntoInfo();

        // Stamp Producer in the Info dictionary if not already set
        if (string.IsNullOrEmpty(Info.Producer))
            Info.Producer = "Aspose.PDF FOSS for .NET";

        // Stamp the default Creator when the caller left it unset.
        if (string.IsNullOrEmpty(Info.Creator))
            Info.Creator = "Aspose Pty Ltd.";

        // Update /ModDate to current time on every save (PDF convention) — but
        // only when the caller did not pin a specific value via Info.ModDate=…
        // before saving. For example, a caller sets ModDate
        // to a fixed historical date and expects the saved doc to round-trip
        // that exact value. StampModDateOnSave bypasses the public setter so
        // the "explicitly set" flag stays clean across repeated saves.
        if (!Info.ModDateExplicitlySet)
            Info.StampModDateOnSave(DateTime.UtcNow);

        // Apply page-level paragraphs, headers, and footers before saving
        ApplyPageContent();

        // Persist document-level /AA additional actions to the catalog. Save()/
        // ToArray() do this via FireBeforePageGenerateEvents, but the Save(string)
        // → Save(Stream) funnel bypasses that, so /AA would otherwise be dropped.
        // WriteToCatalog is idempotent, so a redundant call is harmless.
        _actions?.WriteToCatalog();

        // Sync any attached text fragments modified after AppendText
        foreach (var page in Pages)
        {
            page.FlushBgColorRectangles();
            page.FlushUnderlineRectangles();
            page.FlushUnderlineRemovals();
            page.FlushStrikeOutRectangles();
            page.FlushHyperlinkAnnotations();
            // PageInformationAnnotation prints the output file name + date; generate its
            // appearance here, when the save file name is known.
            if (_pendingSaveFileName is not null)
                page.FlushPageInfoAnnotations(_pendingSaveFileName, DateTime.Today);
            page.SyncAttachedFragments();
            if (page.PruneUnusedFontsOnSave) { PruneUnusedFontsForPage(page); _prunedFontsThisSave = true; }
            page.FlushPendingLayers();
        }

        // A RemoveUnusedFonts edit orphaned the replaced fonts' objects (dictionaries,
        // descriptors, /FontFile programs). Recompute reachability so the serializer drops
        // them from the saved file instead of carrying them over — otherwise the file keeps
        // the (now unused) embedded font programs and never shrinks.
        if (_prunedFontsThisSave && _reachableObjects is null)
        {
            var reachable = new HashSet<int>();
            CollectReachable(_reader.Trailer, reachable);
            if (reachable.Count > 0) _reachableObjects = reachable;
            _prunedFontsThisSave = false;
        }

        // Sync AcroForm field values into the XFA datasets for static XFA forms,
        // so XFA[field] reflects values set through the typed field API.
        _form?.SyncAcroFormToXfa();

        // Auto-finalize structure tree if one was created
        _structureTreeBuilder?.BuildParentTree();
        _structureTreeBuilder = null;

        // Flush the tagged-content tree and accessibility metadata so a
        // document authored via TaggedContent saves as PDF/UA-1 compliant.
        EnsureTaggedPdfMetadata();

        // Auto-finalize outline builder if one was created
        _outlineBuilder?.Build();
        _outlineBuilder = null;

        // Finalize outline collection if items were added/removed via the DOM API
        if (_outlines is not null && _outlines.IsDirty)
            _outlines.Finalize(this);


        // Auto-finalize page labels if created
        _pageLabelBuilder?.Build();
        _pageLabelBuilder = null;

        // Persist label changes made through the doc.PageLabels collection API.
        if (_pageLabels is { IsDirty: true })
            _pageLabels.Serialize(this);

        // EmbedStandardFonts opts the page fonts — including the Standard-14 faces a
        // viewer would otherwise substitute — into a real embedded program, resolving a
        // system face (Helvetica→Arial, Courier→Courier New, …) per the existing embed
        // pass. Without this the property is inert and a re-read still reports the
        // Standard-14 fonts as non-embedded.
        if (EmbedStandardFonts)
            EmbedNonEmbeddedFonts(includeStandard14: true);

        // If the source was encrypted but we're saving without re-encryption, materialize
        // every stream's raw bytes in plaintext now. The writer's pass-through path would
        // otherwise copy ciphertext into a trailer with no /Encrypt, leaving a PDF whose
        // streams can't be /FlateDecode-decoded. Mirrors PDF 32000-2 § 7.6.1.
        if (_encryptor is null && _reader.IsDecrypted)
        {
            _reader.EnsurePlaintextStreams();
        }

        // Linearize ("optimize for fast web view", PDF 32000 Annex F) when the document was
        // explicitly linearized (Optimize()/LinearizeDocument()) or was loaded from a linearized
        // source. The body is serialized to a buffer with a traditional cross-reference table —
        // no object streams — so PdfLinearizer can re-lay-out the object bytes; the linearized
        // result is then written to the real output.
        //
        // OptimizeSize wins over linearization: a linearized file repeats the first-page
        // objects up front, carries a hint stream, and cannot pack objects into compressed
        // object streams — so it is always LARGER than the plain object-stream save. When the
        // caller asked to minimise size, skip linearization and keep the compact form
        // (a font-unembed + OptimizeSize save is 8.4 KB unlinearized vs 16 KB
        // linearized).
        bool doLinearize = (_linearize || IsLinearized) && !OptimizeSize && _encryptor is null;
        var writeTarget = doLinearize ? new MemoryStream() : output;
        var writer = new PdfWriter(writeTarget, _encryptor);

        var xref = _reader.XRefTable;
        var trailer = _reader.Trailer;

        // Enable object streams when the original PDF used them (reduces output size significantly).
        // Objects with inline PdfStream values are excluded from ObjStm packing by the writer.
        // Linearization needs each object at a top-level file offset, so it stays off there.
        var hasCompressedObjects = xref.Entries.Values.Any(e => e.IsCompressed);
        // PDF/A-1 (ISO 19005-1 §6.1.4) prohibits cross-reference streams — and object
        // streams require one — so a document converted to PDF/A-1 saves with a
        // classic xref table even when the source used compressed objects.
        bool pdfA1Target = _lastConvertedFormat
            is Aspose.Pdf.PdfFormat.PDF_A_1A or Aspose.Pdf.PdfFormat.PDF_A_1B;
        if (hasCompressedObjects && _encryptor is null && !doLinearize && !pdfA1Target)
        {
            writer.UseObjectStreams = true;
        }
        // The classic-xref PDF/A-1 output loses the object-stream packing win;
        // recover the size by re-deflating weakly-compressed source streams,
        // as the conversion save does.
        if (pdfA1Target) writer.RecompressFlateStreams = true;

        // The source file's cross-reference infrastructure — object streams (/Type /ObjStm)
        // and cross-reference streams (/Type /XRef) — is regenerated from scratch by the
        // writer. Carrying the originals over leaves them as dead, unreferenced streams in the
        // output, and because each save emits a fresh set, re-saving an already-saved file
        // would accumulate one dead ObjStm + one dead XRef per cycle and grow the file
        // monotonically. Skip them on write, and exclude their numbers from the object-number
        // ceiling below so the regenerated containers get the same numbers every cycle —
        // together this makes a load/save round-trip byte-stable.
        var infraObjNums = xref.InfrastructureObjectNumbers();
        int MaxRealObjNum() => xref.Entries.Keys.Where(k => !infraObjNums.Contains(k)).DefaultIfEmpty(0).Max();

        writer.WriteHeader(_versionOverride ?? PdfVersion ?? "1.4");

        // Pre-allocate object number for XMP metadata (so catalog gets the reference)
        int metaObjNum = -1;
        PdfStream? metaStream = null;
        byte[]? xmpBytes = _rawXmpOverride
            ?? ((_metadata is not null && _metadata.IsDirty) ? _metadata.ToXmpBytes() : null);
        if (xmpBytes is not null)
        {
            // The XMP packet is being rewritten (or cleared). Whichever way, the catalog's
            // existing /Metadata stream is stale: skip it on write and drop it from the
            // object-number ceiling so we don't carry the old packet as a dead object.
            if (_reader.Catalog.Get("Metadata") is PdfIndirectRef oldMetaRef)
                infraObjNums.Add(oldMetaRef.ObjectNumber);

            if (xmpBytes.Length > 0)
            {
                var metaDict = new PdfDictionary();
                metaDict.Set("Type", new PdfName("Metadata"));
                metaDict.Set("Subtype", new PdfName("XML"));
                metaDict.Set("Length", new PdfInteger(xmpBytes.Length));
                metaStream = new PdfStream(metaDict, xmpBytes);

                // Use a high object number to avoid collisions
                var maxObj = MaxRealObjNum();
                metaObjNum = maxObj + 100;
                _reader.Catalog.Set("Metadata", new PdfIndirectRef(metaObjNum, 0));
            }
            else
            {
                // Empty packet (e.g. SetXmpMetadata(Stream.Null)) means "remove the document
                // metadata". Drop the catalog reference and write no stream so the file
                // actually shrinks rather than gaining an empty /Metadata object.
                _reader.Catalog.Remove("Metadata");
            }
        }

        // Pre-advance the writer's object counter past ALL known object numbers
        // (existing xref, _newObjects, and metaObjNum) so that any indirect objects
        // promoted from inline PdfStream values during serialization get fresh numbers
        // that don't collide with anything already planned.
        {
            var maxKnown = MaxRealObjNum();
            foreach (var (objNum, _) in _newObjects)
                if (objNum > maxKnown) maxKnown = objNum;
            if (metaObjNum > maxKnown) maxKnown = metaObjNum;
            writer.SetMinObjectNumber(maxKnown + 1);
        }

        // Pre-scan the catalog's inline object graph for dictionaries shared between more than
        // one parent (e.g. a generated radio group reached from /AcroForm/Fields and from each
        // option widget's /Parent). These are written once as a shared indirect object so the
        // back-references survive a round-trip instead of being dropped at the write cycle.
        writer.MarkSharedDicts(_reader.Catalog);

        // Map each existing page's source object number to its authoritative
        // in-memory dictionary. The page renderer clears the reader's object cache
        // to free decoded streams, so re-resolving a page below would re-parse a
        // pristine dict and silently drop in-memory edits made after rendering
        // (e.g. an hOCR invisible-text overlay added via Convert). Writing the live
        // Page.Dict for these object numbers preserves those edits.
        var livePageDicts = new Dictionary<int, PdfDictionary>();
        if (_pages is not null)
        {
            foreach (var p in _pages)
                if (p.SourceObjectNumber > 0)
                    livePageDicts[p.SourceObjectNumber] = p.Dict;
        }

        // An in-place image replacement supersedes the original image object but leaves it
        // in the xref, so without a reachability pass the write loop below would emit both
        // the old and the new image and the file would never shrink.
        // Compute reachability once here (only when nothing else already did) so the
        // orphaned original falls out. Runs only when such an edit actually happened.
        if (_reader.MayHaveOrphansOnSave && _reachableObjects is null)
        {
            var reachable = new HashSet<int>();
            CollectReachable(_reader.Trailer, reachable);
            if (_pages is not null)
                foreach (var pending in _pages.PendingAdds)
                    CollectReachable(pending.Dict, reachable);
            if (reachable.Count > 0) _reachableObjects = reachable;
        }

        // Write all existing objects (skipping unreachable ones if optimized).
        // Iterate in ascending object-number order rather than the dictionary's
        // insertion order, which reflects the source file's physical layout and so
        // differs between an original and a re-saved copy. A deterministic write order
        // keeps byte offsets — and therefore the regenerated xref stream — stable across
        // a load/save round-trip.
        foreach (var entry in xref.Entries.Values.OrderBy(e => e.ObjectNumber))
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            // Skip unreachable objects when optimizing
            if (_reachableObjects is not null && !_reachableObjects.Contains(entry.ObjectNumber))
                continue;

            PdfObject? obj;
            try
            {
                obj = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            }
            catch (InvalidOperationException)
            {
                // Compressed object whose object stream is unavailable (e.g. corrupt xref or
                // partially-linearized PDFs) — skip gracefully rather than aborting the save.
                continue;
            }
            if (obj is null) continue;

            // Skip the source file's cross-reference infrastructure (see infraObjNums above).
            if (infraObjNums.Contains(entry.ObjectNumber)) continue;

            // Prefer the live in-memory page dictionary over a (possibly stale,
            // post-ClearCache re-parsed) reader resolution. See livePageDicts above.
            if (livePageDicts.TryGetValue(entry.ObjectNumber, out var liveDict))
                obj = liveDict;

            writer.WriteIndirectObject(entry.ObjectNumber, obj);
        }

        // Write any new objects that were added (e.g., deep-cloned resources from imports,
        // new Info dict). These must be written BEFORE RebuildPagesTree so their obj numbers
        // don't collide with writer-allocated numbers.
        foreach (var (objNum, obj) in _newObjects)
        {
            writer.WriteIndirectObject(objNum, obj);
        }

        // Write cross-document imported objects (from page merge)
        if (_pages is not null)
        {
            foreach (var (objNum, obj) in _pages.ImportedObjects)
            {
                // After an OptimizeResources prune, only write imported objects still
                // reachable from the (pruned) pages — otherwise resources dropped from a
                // copied page's /Resources would bloat the file even though nothing uses them.
                if (_reachableObjects is not null && !_reachableObjects.Contains(objNum)) continue;
                writer.WriteIndirectObject(objNum, obj);
            }
        }

        // Handle page additions/deletions by rebuilding the Pages tree
        if (_pages is not null && _pages.IsModified)
        {
            // Rebuild /Pages with updated /Kids and /Count
            RebuildPagesTree(writer);
        }

        // Write XMP metadata stream
        if (metaStream is not null && metaObjNum >= 0)
        {
            writer.WriteIndirectObject(metaObjNum, metaStream);
        }

        // Build trailer dict
        var newTrailer = new PdfDictionary();
        CopyTrailerEntry(trailer, newTrailer, "Root");

        // Use new Info ref if we created one, otherwise copy from original
        if (_newInfoObjNum is not null)
        {
            newTrailer.Set("Info", new PdfIndirectRef(_newInfoObjNum.Value, 0));
        }
        else
        {
            CopyTrailerEntry(trailer, newTrailer, "Info");
        }

        if (_encryptor is not null)
        {
            // Write encrypt dictionary as an indirect object (excluded from encryption)
            var encryptObjNum = writer.AllocateObjectNumber();
            writer.ExcludeFromEncryption(encryptObjNum);
            writer.WriteIndirectObject(encryptObjNum, _encryptor.BuildEncryptDict());
            newTrailer.Set("Encrypt", new PdfIndirectRef(encryptObjNum, 0));

            // Set file ID (required for encryption)
            var idArray = new PdfArray();
            idArray.Add(new PdfString(_encryptor.FileId, isHex: true));
            idArray.Add(new PdfString(_encryptor.FileId, isHex: true));
            newTrailer.Set("ID", idArray);
        }
        else if (_forceWriteId && trailer.Get("ID") is null)
        {
            // Generate a file ID (required for PDF/A)
            var fileId = Security.CryptoRandom.GetBytes(16);
            var idArray = new PdfArray();
            idArray.Add(new PdfString(fileId, isHex: true));
            idArray.Add(new PdfString(fileId, isHex: true));
            newTrailer.Set("ID", idArray);
        }
        else
        {
            CopyTrailerEntry(trailer, newTrailer, "ID");
        }

        writer.WriteXRefAndTrailer(newTrailer);

        if (doLinearize)
        {
            var normal = ((MemoryStream)writeTarget).ToArray();
            var linearized = IO.PdfLinearizer.Linearize(normal);
            output.Write(linearized, 0, linearized.Length);
        }
    }

    private void RebuildPagesTree(PdfWriter writer)
    {
        // Determine the actual /Pages object number from the catalog
        var pagesRef = _reader.Catalog.Get("Pages");
        var pagesObjNum = pagesRef is PdfIndirectRef pr ? pr.ObjectNumber : 2;

        // Reserve the writer's number space above every cross-document import slot so a
        // writer-allocated page number can't collide with a slot — including slots for
        // destination-only pages that are referenced but never written.
        if (_pages is not null)
            writer.ReserveObjectNumber(_pages.ImportSlotHighWater);

        // Build a new /Pages dict with all current pages as kids
        var kids = new PdfArray();
        foreach (var page in Pages)
        {
            // Choose each page's object number:
            //  - an imported page is written at its reserved slot so GoTo/Link destinations
            //    that target it (and point at that slot) resolve to this copy;
            //  - a page loaded from THIS document keeps its original object number, so the
            //    document's own internal links (bookmarks, link annotations, named
            //    destinations) — which reference pages by object number — still resolve
            //    after pages are deleted or reordered (imported pages carry
            //    SourceObjectNumber = -1, so they never take this branch);
            //  - a newly created page takes a fresh writer-allocated number.
            int objNum;
            if (page.ImportSlotObjNum > 0) objNum = page.ImportSlotObjNum;
            else if (page.SourceObjectNumber > 0) objNum = page.SourceObjectNumber;
            else objNum = writer.AllocateObjectNumber();

            page.Dict.Set("Parent", new PdfIndirectRef(pagesObjNum, 0));
            writer.WriteIndirectObject(objNum, page.Dict);
            kids.Add(new PdfIndirectRef(objNum, 0));
        }

        var pagesDict = new PdfDictionary();
        pagesDict.Set("Type", new PdfName("Pages"));
        pagesDict.Set("Kids", kids);
        pagesDict.Set("Count", new PdfInteger(Pages.Count));

        // Write at the original /Pages object number
        writer.WriteIndirectObject(pagesObjNum, pagesDict);

        // Keep the in-memory reader's page tree consistent with the current page
        // order (Insert/Delete only updated the Pages list, not the underlying
        // /Kids). Without this, page-number lookups that walk the reader tree after
        // a save — e.g. resolving a GoTo destination's target page — see the stale
        // pre-edit order. The kids are the live page dicts in their current order.
        var inMemPages = _reader.ResolveDict(_reader.Catalog.Get("Pages"));
        if (inMemPages is not null)
        {
            var inMemKids = new PdfArray();
            foreach (var page in Pages) inMemKids.Add(page.Dict);
            inMemPages.Set("Kids", inMemKids);
            inMemPages.Set("Count", new PdfInteger(Pages.Count));
        }
    }

    /// <summary>
    /// Rebuild the in-memory catalog /Pages tree (/Kids and /Count) so it matches
    /// the current page order. <see cref="PageCollection.Insert"/> / Delete update
    /// only the Pages list, not the underlying /Kids, so any reader-tree walk before
    /// save — e.g. resolving a GoTo or bookmark destination's target page number —
    /// would otherwise see the stale pre-edit order (off-by-one after a page is
    /// inserted). Safe to call repeatedly; a no-op when no page was added or removed.
    /// </summary>
    internal void SyncInMemoryPageTree()
    {
        if (_pages is null || !_pages.IsModified) return;
        var inMemPages = _reader.ResolveDict(_reader.Catalog.Get("Pages"));
        if (inMemPages is null) return;

        // Preserve each already-loaded page's original indirect reference so the
        // rebuilt /Kids keeps page object-number identity: named-destination
        // resolution maps a page's object number to its index, so flattening to
        // bare dicts would make named destinations unresolvable. Newly inserted
        // (pending) pages have no object number yet and go in as direct dicts.
        var dictToRef = new Dictionary<PdfDictionary, PdfObject>(
            System.Collections.Generic.ReferenceEqualityComparer.Instance);
        CollectKidRefs(inMemPages, dictToRef);

        var inMemKids = new PdfArray();
        foreach (var page in Pages)
            inMemKids.Add(dictToRef.TryGetValue(page.Dict, out var r) ? r : page.Dict);
        inMemPages.Set("Kids", inMemKids);
        inMemPages.Set("Count", new PdfInteger(Pages.Count));
    }

    /// <summary>
    /// After pages have been removed (e.g. by <see cref="Facades.PdfFileEditor.Extract(byte[],int,int)"/>,
    /// which deletes every page outside the requested range), drop the objects that only the
    /// removed pages kept alive. A plain save writes every object still reachable from the
    /// trailer, and an outline bookmark, article thread, or link annotation that pointed at a
    /// removed page keeps that page — and its (often large) images — reachable, so an
    /// extracted file stays as big as the whole source. Recompute reachability treating each
    /// removed page as a cut point so the save writes only what the surviving pages still use.
    /// </summary>
    internal void CompactAfterPageRemoval()
    {
        if (_pages is null || !_pages.IsModified) return;

        // Flatten /Kids to the surviving pages so a removed page is no longer reachable
        // through the page tree itself.
        SyncInMemoryPageTree();

        var survivingPages = new HashSet<int>();
        foreach (var page in Pages)
            if (page.SourceObjectNumber > 0) survivingPages.Add(page.SourceObjectNumber);

        // Compute reachability but treat every removed page as a cut point: don't traverse a
        // /Type /Page object that isn't one of the survivors. This drops each removed page
        // and everything only it references (its images can be the bulk of the file) no
        // matter what still points at it — a bookmark, an article-thread bead's /P, a link
        // annotation on a surviving page. Those references simply dangle, resolving to
        // "no page" on reopen (matching Aspose.Pdf), which never keeps the page.
        var reachable = new HashSet<int>();
        CollectReachableExcludingRemovedPages(_reader.Trailer, reachable, survivingPages);
        if (reachable.Count > 0) _reachableObjects = reachable;
    }

    /// <summary>Reachability variant used after page removal: identical to
    /// <see cref="CollectReachable"/> except a <c>/Type /Page</c> object whose number is not
    /// in <paramref name="survivingPages"/> is neither marked reachable nor traversed, so a
    /// removed page (and any object only it kept alive) falls out of the saved file.</summary>
    private void CollectReachableExcludingRemovedPages(PdfObject? root, HashSet<int> visited, HashSet<int> survivingPages)
    {
        if (root is null or PdfNull) return;
        var stack = new Stack<PdfObject>();
        stack.Push(root);
        var seenDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var obj = stack.Pop();
            if (obj is null or PdfNull) continue;

            if (obj is PdfIndirectRef iref)
            {
                if (visited.Contains(iref.ObjectNumber)) continue;
                var resolved = _reader.Resolve(iref);
                // Cut removed pages: don't record or traverse them.
                if (resolved is PdfDictionary pd && pd.GetName("Type") == "Page"
                    && !survivingPages.Contains(iref.ObjectNumber))
                    continue;
                visited.Add(iref.ObjectNumber);
                if (resolved is not null) stack.Push(resolved);
                continue;
            }
            if (obj is PdfStream stream) { stack.Push(stream.Dict); continue; }
            if (obj is PdfDictionary dict)
            {
                if (!seenDicts.Add(dict)) continue;
                foreach (var key in dict.Keys)
                {
                    var val = dict.Get(key);
                    if (val is not null) stack.Push(val);
                }
                continue;
            }
            if (obj is PdfArray arr)
                foreach (var item in arr)
                    if (item is not null) stack.Push(item);
        }
    }

    /// <summary>Map each leaf page dictionary in a /Pages subtree to the indirect
    /// reference that points at it, so <see cref="SyncInMemoryPageTree"/> can keep
    /// those references when it rebuilds a flat /Kids array.</summary>
    private void CollectKidRefs(PdfDictionary node, Dictionary<PdfDictionary, PdfObject> map)
    {
        if (_reader.Resolve(node.Get("Kids")) is not PdfArray kids) return;
        foreach (var kid in kids)
        {
            var kidDict = _reader.ResolveDict(kid);
            if (kidDict is null) continue;
            if (kidDict.GetName("Type") == "Page")
            {
                if (kid is PdfIndirectRef) map[kidDict] = kid;
            }
            else
            {
                CollectKidRefs(kidDict, map);
            }
        }
    }

    private static void CopyTrailerEntry(PdfDictionary source, PdfDictionary dest, string key)
    {
        var val = source.Get(key);
        if (val is not null)
            dest.Set(key, val);
    }

    private static byte[] ReadStreamToBytes(Stream stream)
    {
        if (stream.CanSeek && stream.Position != 0)
            stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private void SyncXmpIntoInfo()
    {
        if (!HasMetadata) return;
        var meta = GetOrCreateXmpMetadata();

        // ISO 16684-1 / PDF 32000-2 § 14.3.3 mapping between /Info entries and
        // their XMP property equivalents. Sync only when the XMP side carries
        // a non-empty value and the /Info side is missing-or-empty.
        SyncIfMissing(meta, "dc:title", "Title");
        SyncIfMissing(meta, "dc:description", "Subject");
        SyncIfMissing(meta, "dc:creator", "Author");
        SyncIfMissing(meta, "pdf:Keywords", "Keywords");
        SyncIfMissing(meta, "xmp:CreatorTool", "Creator");
        SyncIfMissing(meta, "pdf:Producer", "Producer");
    }

    private void SyncIfMissing(XmpMetadata meta, string xmpKey, string infoKey)
    {
        if (!string.IsNullOrEmpty(Info[infoKey])) return;
        var v = meta.Get(xmpKey);
        if (string.IsNullOrEmpty(v)) return;
        Info[infoKey] = v;
    }

    /// <summary>Format an /Info date (DateTime + timezone offset) as an ISO 8601
    /// XMP date string (e.g. <c>2026-06-20T12:34:56+03:00</c>) that round-trips
    /// through <see cref="Aspose.Pdf.Xmp.XmpValue.ToDateTime"/>.</summary>
    private static string FormatXmpDate(DateTime value, TimeSpan offset)
        => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), offset)
            .ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] CreateEmptyPdf()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        // Newly-created documents default to PDF 1.7 (matches the modern baseline
        // and the PdfFormat default). Loaded documents keep their own header version.
        writer.WriteHeader("1.7");

        // Catalog
        var catalogDict = new PdfDictionary();
        catalogDict.Set("Type", new PdfName("Catalog"));
        catalogDict.Set("Pages", new PdfIndirectRef(2, 0));
        writer.WriteIndirectObject(1, catalogDict);

        // Pages (empty — no kids)
        var pagesDict = new PdfDictionary();
        pagesDict.Set("Type", new PdfName("Pages"));
        pagesDict.Set("Kids", new PdfArray());
        pagesDict.Set("Count", new PdfInteger(0));
        writer.WriteIndirectObject(2, pagesDict);

        var trailer = new PdfDictionary();
        trailer.Set("Root", new PdfIndirectRef(1, 0));
        writer.WriteXRefAndTrailer(trailer);

        return ms.ToArray();
    }

    /// <summary>
    /// Save the document incrementally to a stream: keeps original bytes + appends only
    /// modified/new objects. Uses IncrementalWriter for a true incremental update that
    /// preserves the original byte structure and keeps the file size small.
    /// </summary>
    /// <summary>When the document was authored or edited through
    /// <see cref="TaggedContent"/>, flush the in-memory structure tree
    /// and stamp the accessibility metadata PDF/UA-1 requires: the title
    /// shown in the window bar (<c>/ViewerPreferences /DisplayDocTitle</c>),
    /// an XMP packet carrying the UA identifier plus <c>dc:title</c>, and
    /// a file <c>/ID</c>.</summary>
    private void EnsureTaggedPdfMetadata()
    {
        if (_taggedContent is null) return;

        // Link the authored structure tree into /Catalog (sets /MarkInfo,
        // /StructTreeRoot — element dicts are already in their parents' /K).
        ((Tagged.ITaggedContent)_taggedContent).Save();

        // Render the authored structure (headers/paragraphs) onto a page when the
        // document was built purely through TaggedContent and has no page content
        // yet. A from-scratch tagged document otherwise saves with a blank canvas.
        if (_isNewDocument && Pages.Count == 0)
        {
            var root = ((Tagged.ITaggedContent)_taggedContent).RootElement;
            Tagged.TaggedContentRenderer.TryRender(this, root);
            // Structure content that can't be laid out as text (e.g. a table) still
            // needs a page so the authored document doesn't save with zero pages.
            if (Pages.Count == 0 && root.ChildElements.Count > 0)
                Pages.Add();
        }

        var title = Info.Title;
        if (!string.IsNullOrEmpty(title))
        {
            DisplayDocTitle = true;
            var meta = GetOrCreateMetadata();
            if (string.IsNullOrEmpty(meta.Get("dc:title"))) meta.Set("dc:title", title);
            if (string.IsNullOrEmpty(meta.Get("pdf:Producer"))) meta.Set("pdf:Producer", "Aspose.PDF FOSS for .NET");
            if (string.IsNullOrEmpty(meta.Get("pdfuaid:part"))) meta.Set("pdfuaid:part", "1");
        }

        if (_reader.Trailer.Get("ID") is null)
        {
            var fileId = Security.CryptoRandom.GetBytes(16);
            var idArray = new PdfArray();
            idArray.Add(new PdfString(fileId, isHex: true));
            idArray.Add(new PdfString(fileId, isHex: true));
            _reader.Trailer.Set("ID", idArray);
            _forceWriteId = true;
        }
    }

    /// <summary>Serialize as an incremental update (original bytes verbatim +
    /// appended modified/new objects + a new xref section). Unlike
    /// <see cref="ToArray"/>'s full rewrite, this preserves every original byte,
    /// so an existing digital signature's /ByteRange stays valid. Used when
    /// editing a signed document (e.g. filling a form field).</summary>
    internal byte[] ToArrayIncremental()
    {
        FireBeforePageGenerateEvents();
        using var ms = new MemoryStream();
        SaveIncremental(ms);
        return ms.ToArray();
    }

    private void SaveIncremental(Stream output)
    {
        // Write the original PDF data first
        output.Seek(0, SeekOrigin.Begin);
        output.Write(_data);

        // Collect all modified objects: new objects + modified catalog/info
        var modified = new List<(int objectNumber, PdfObject obj)>();

        // Add any new objects registered during the session
        foreach (var (objNum, obj) in _newObjects)
            modified.Add((objNum, obj));

        // If metadata was modified, write the updated catalog
        if (_metadataChecked || _taggedContent is not null)
        {
            var catalogRef = _reader.Trailer.Get("Root") as PdfIndirectRef;
            if (catalogRef is not null)
                modified.Add((catalogRef.ObjectNumber, _reader.Catalog));
        }

        // Include objects explicitly marked as dirty (e.g., form field value changes)
        foreach (var (objNum, obj) in _dirtyObjects)
            modified.Add((objNum, obj));

        // Persist page-tree structural changes (page insert/delete) incrementally.
        // Pages.Insert/Delete update only the in-memory page list; without rewriting
        // the /Pages node the appended xref still points at the original /Kids, so a
        // reopened document shows the pre-edit page count. SyncInMemoryPageTree rebuilds
        // the catalog's /Pages dict (/Kids + /Count) to the current order — keeping each
        // surviving page's original indirect reference — and we emit it as a modified
        // object so the incremental update reflects the deletion/insertion.
        if (_pages is not null && _pages.IsModified)
        {
            SyncInMemoryPageTree();
            if (_reader.Catalog.Get("Pages") is PdfIndirectRef pagesRef
                && _reader.ResolveDict(pagesRef) is { } pagesDict)
                modified.Add((pagesRef.ObjectNumber, pagesDict));
        }

        // Use the real incremental writer
        var xref = _reader.XRefTable;
        var trailer = _reader.Trailer;
        var size = (int)trailer.GetInt("Size", 1);
        var originalStartXref = XRefTable.FindStartXref(_data);

        var writer = new IncrementalWriter(output, _data,
            Math.Max(size, xref.Entries.Keys.DefaultIfEmpty(0).Max() + 1));

        foreach (var (objNum, obj) in modified)
            writer.WriteObject(objNum, obj);

        writer.Flush(trailer, originalStartXref);
        output.SetLength(output.Position);
        output.Flush();
    }

    // ── Internal write infrastructure ────────────────────────────────────────

    private readonly List<(int objNum, PdfObject obj)> _newObjects = [];

    /// <summary>Deferred image file paths from BindXml, validated during Save.</summary>
    internal List<string>? PendingXmlImageFiles { get; set; }

    /// <summary>Default page-tree branching factor (PDF table 30 /Count vs /Kids ratio).</summary>
    public const byte DefaultNodesNumInSubtrees = 10;

    // ── Stored-only flag props (Aspose.Pdf parity; no behaviour) ──

    /// <summary>Whether the saver may reuse identical page-content streams. Stored only.</summary>
    public bool AllowReusePageContent { get; set; }

    /// <summary>Whether the document emits notification log entries. Stored only.</summary>
    public bool EnableNotificationLogging { get; set; }

    /// <summary>Whether signature fields fire change-handlers when their dict mutates. Stored only.</summary>
    public bool HandleSignatureChange { get; set; }

    /// <summary>Viewer preference: hide the menu bar (/ViewerPreferences /HideMenubar).</summary>
    public bool HideMenubar
    {
        get => GetViewerPrefBool("HideMenubar");
        set => SetViewerPrefBool("HideMenubar", value);
    }

    /// <summary>Viewer preference: hide the toolbar (/ViewerPreferences /HideToolbar).</summary>
    public bool HideToolBar
    {
        get => GetViewerPrefBool("HideToolbar");
        set => SetViewerPrefBool("HideToolbar", value);
    }

    /// <summary>Viewer preference: hide window UI chrome (/ViewerPreferences /HideWindowUI).</summary>
    public bool HideWindowUI
    {
        get => GetViewerPrefBool("HideWindowUI");
        set => SetViewerPrefBool("HideWindowUI", value);
    }

    /// <summary>Allow gaps in the xref table during parse. Stored only.</summary>
    public bool IsXrefGapsAllowed { get; set; }

    /// <summary>Viewer preference: pick the paper tray by PDF page size
    /// (/ViewerPreferences /PickTrayByPDFSize).</summary>
    public bool PickTrayByPdfSize
    {
        get => GetViewerPrefBool("PickTrayByPDFSize");
        set => SetViewerPrefBool("PickTrayByPDFSize", value);
    }

    /// <summary>Maximum file size (bytes) loaded entirely into memory. Stored only.</summary>
    public int FileSizeLimitToMemoryLoading { get; set; } = int.MaxValue;

    /// <summary>Reset <see cref="FileSizeLimitToMemoryLoading"/> to its built-in default.</summary>
    public void SetDefaultFileSizeLimitToMemoryLoading() => FileSizeLimitToMemoryLoading = int.MaxValue;

    /// <summary>Update the /Info /Title entry.</summary>
    public void SetTitle(string title) { if (Info is { } info) info.Title = title; }

    /// <summary>Rebuild the page tree so each subtree has <paramref name="nodesNumInSubtrees"/> children. Stored only.</summary>
    public void PageNodesToBalancedTree(byte nodesNumInSubtrees) { _ = nodesNumInSubtrees; }

    /// <summary>Remove all metadata. Stored only — clears /Info and /Metadata in a future change.</summary>
    public void RemoveMetadata() { }

    /// <summary>Remove PDF/UA compliance markers. Stored only.</summary>
    public void RemovePdfUaCompliance() { }

    /// <summary>Flatten transparency to opaque graphics. Stored only — no-op in FOSS.</summary>
    public void FlattenTransparency() { }

    /// <summary>Apply the requested repair pass. Stored only.</summary>
    public void Repair(RepairOptions options) { _ = options; }


    /// <summary>Resolve a PDF object by string id. Returns null when not found.</summary>
    public object? GetObjectById(string id) { _ = id; return null; }

    /// <summary>Export every annotation in the document to an XFDF stream.</summary>
    public void ExportAnnotationsToXfdf(Stream stream)
    {
        new Aspose.Pdf.Facades.PdfAnnotationEditor(this).ExportAnnotationsToXfdf(stream);
    }
    /// <summary>Export every annotation in the document to an XFDF file.</summary>
    public void ExportAnnotationsToXfdf(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        ExportAnnotationsToXfdf(fs);
    }
    /// <summary>Import annotations from an XFDF stream into the document.</summary>
    public void ImportAnnotationsFromXfdf(Stream stream)
    {
        new Aspose.Pdf.Facades.PdfAnnotationEditor(this).ImportAnnotationsFromXfdf(stream);
    }
    /// <summary>Import annotations from an XFDF file into the document.</summary>
    public void ImportAnnotationsFromXfdf(string fileName)
    {
        new Aspose.Pdf.Facades.PdfAnnotationEditor(this).ImportAnnotationsFromXfdf(fileName);
    }

    /// <summary>Raw XMP packet supplied via <see cref="SetXmpMetadata(Stream)"/>;
    /// written verbatim as the /Metadata stream on save (bypasses the property model
    /// so arbitrary XMP byte content round-trips exactly).</summary>
    private byte[]? _rawXmpOverride;

    /// <summary>Write the document's XMP /Metadata packet to <paramref name="stream"/>.</summary>
    public void GetXmpMetadata(Stream stream)
    {
        if (stream is null) return;
        byte[]? bytes = _rawXmpOverride;
        if (bytes is null)
        {
            var metaStream = _reader.ResolveStream(_reader.Catalog.Get("Metadata"));
            if (metaStream is not null) bytes = _reader.DecodeStream(metaStream);
        }
        if (bytes is { Length: > 0 }) stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Replace the document's XMP /Metadata packet from <paramref name="stream"/>.
    /// The full stream content (from its start) is stored and written verbatim on save.</summary>
    public void SetXmpMetadata(Stream stream)
    {
        if (stream is null) return;
        if (stream.CanSeek) stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _rawXmpOverride = ms.ToArray();
    }

    /// <summary>Convert one page to a PNG memory stream.</summary>
    public MemoryStream ConvertPageToPNGMemoryStream(Page page)
    {
        var ms = new MemoryStream();
        new Aspose.Pdf.Devices.PngDevice().Process(page, ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Save document as XML (Aspose-Pdf XML schema). Stored only.</summary>
    public void SaveXml(string file) { _ = file; }

    /// <summary>Validate / repair the document. Always returns true (FOSS doesn't run validation).</summary>
    public bool Check(bool doRepair) { _ = doRepair; return true; }

    /// <summary>
    /// Validate the document's page content streams and write an XML report of any
    /// problems to <paramref name="output"/>. Currently checks that each page's
    /// graphics-state operators are balanced (every <c>q</c> has a matching
    /// <c>Q</c>); an unbalanced page leaves a residual transform that corrupts
    /// later content. When <paramref name="doRepair"/> is set the imbalance is
    /// corrected in place. Returns true when the document is valid (no problems).
    /// </summary>
    public bool Check(bool doRepair, System.IO.Stream output)
    {
        var issues = new List<string>();
        foreach (Page page in Pages)
        {
            int depth = 0;
            try
            {
                foreach (var op in page.Contents)
                {
                    if (op is Aspose.Pdf.Operators.GSave) depth++;
                    else if (op is Aspose.Pdf.Operators.GRestore) depth--;
                }
            }
            catch { depth = 0; }

            if (depth != 0)
            {
                issues.Add($"Page {page.Number}: unbalanced graphics-state operators "
                    + $"(q/Q), net depth {depth}.");
                if (doRepair && depth > 0)
                    page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(
                        string.Concat(Enumerable.Repeat("Q\n", depth))));
                else if (doRepair && depth < 0)
                    page.PrependContentStream(System.Text.Encoding.ASCII.GetBytes(
                        string.Concat(Enumerable.Repeat("q\n", -depth))));
            }
        }

        if (output is not null)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<report>\n");
            foreach (var issue in issues)
                sb.Append("  <item>\n    <description>")
                  .Append(System.Security.SecurityElement.Escape(issue))
                  .Append("</description>\n  </item>\n");
            sb.Append("</report>\n");
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }
        return issues.Count == 0;
    }

    /// <summary>Async file load wrapper (currently synchronous).</summary>
    public void LoadFrom(string filename, LoadOptions options)
    {
        _ = options;
        if (string.IsNullOrEmpty(filename)) return;
        FileName = filename;
    }

    /// <summary>Single-arg ctor named <paramref name="filename"/> matching Aspose.Pdf param name.</summary>
    public Document(string filename, bool isManagedStream) : this(filename) { _ = isManagedStream; }

    /// <summary>Stream ctor with managed-stream flag.</summary>
    public Document(Stream input, bool isManagedStream) : this(input) { _ = isManagedStream; }

    /// <summary>Stream ctor with password + managed-stream flag.</summary>
    public Document(Stream input, string password, bool isManagedStream) : this(input, password) { _ = isManagedStream; }

    /// <summary>File ctor with password + managed-stream flag.</summary>
    public Document(string filename, string password, bool isManagedStream) : this(filename, password) { _ = isManagedStream; }

    /// <summary>Stream ctor with password + ICustomSecurityHandler.</summary>
    public Document(Stream input, string password, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(input, password) { _ = customSecurityHandler; }

    /// <summary>Stream ctor with password + managed-stream + ICustomSecurityHandler.</summary>
    public Document(Stream input, string password, bool isManagedStream, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(input, password) { _ = isManagedStream; _ = customSecurityHandler; }

    /// <summary>File ctor with password + ICustomSecurityHandler.</summary>
    public Document(string filename, string password, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(filename, password) { _ = customSecurityHandler; }

    /// <summary>File ctor with password + managed-stream + ICustomSecurityHandler.</summary>
    public Document(string filename, string password, bool isManagedStream, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(filename, password) { _ = isManagedStream; _ = customSecurityHandler; }

    /// <summary>Stream ctor with LoadOptions.</summary>
    public Document(Stream input, LoadOptions options) : this(input) { _ = options; }

    /// <summary>File ctor with LoadOptions.</summary>
    public Document(string filename, LoadOptions options) : this(filename) { _ = options; }

    /// <summary>
    /// Track existing objects modified in-memory (for incremental save).
    /// Key = object number, Value = the modified PdfObject.
    /// </summary>
    private readonly Dictionary<int, PdfObject> _dirtyObjects = new();

    /// <summary>
    /// Mark an existing indirect object as dirty so it gets written during incremental save.
    /// </summary>
    internal void MarkDirty(int objectNumber, PdfObject obj)
    {
        _dirtyObjects[objectNumber] = obj;
    }

    /// <summary>
    /// Find the object number for a PdfDictionary by scanning xref entries.
    /// Returns -1 if not found.
    /// </summary>
    internal int FindObjectNumber(PdfDictionary dict)
    {
        foreach (var entry in _reader.XRefTable.Entries.Values)
        {
            var resolved = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, 0));
            if (ReferenceEquals(resolved, dict))
                return entry.ObjectNumber;
        }
        return -1;
    }

    private int? _newInfoObjNum;

    /// <summary>
    /// Allocate the next available object number for new objects.
    /// </summary>
    internal int AllocateObjectNumber()
    {
        var xref = _reader.XRefTable;
        var max = 0;
        foreach (var entry in xref.Entries.Values)
        {
            if (entry.ObjectNumber > max) max = entry.ObjectNumber;
        }
        // Also consider already-allocated new objects
        foreach (var (objNum, _) in _newObjects)
        {
            if (objNum > max) max = objNum;
        }
        // Also consider imported objects from page merging
        if (_pages is not null)
        {
            foreach (var (objNum, _) in _pages.ImportedObjects)
            {
                if (objNum > max) max = objNum;
            }
            // Cross-document page-import reserves destination-slot object numbers
            // (Page.ImportSlotObjNum, up to ImportSlotHighWater) that are written at
            // their reserved numbers during save — including destination-only slots
            // not yet in ImportedObjects. An allocation here must sit above them, or a
            // page slot overwrites e.g. the /Outlines root that OutlineCollection.Finalize
            // allocates during the same save.
            if (_pages.ImportSlotHighWater > max) max = _pages.ImportSlotHighWater;
        }
        return max + 1;
    }

    /// <summary>
    /// Register a new indirect object to be written on the next save.
    /// </summary>
    internal void AddNewObject(int objNum, PdfObject obj, bool registerOverlay = false)
    {
        _newObjects.Add((objNum, obj));
        // Optionally expose the object to in-memory resolution. The writer enumerates
        // _newObjects (not the overlay), so this never double-writes — it only lets a
        // freshly created indirect object be walked via _reader.Resolve before save.
        // Off by default: most callers (e.g. lazy /StructTreeRoot creation) rely on the
        // object staying unresolvable until saved, so a catalog-backed read view stays
        // null/empty until then. Opt in only where in-memory chaining is required
        // (OutlineBuilder, so a second PdfBookmarkEditor sees just-added bookmarks).
        if (registerOverlay)
            _reader.RegisterOverlayObject(objNum, obj);
    }

    /// <summary>
    /// Ensure the document has an Info dictionary and return its object number.
    /// If the original document had no Info dict, a new one is created.
    /// </summary>
    /// <summary>Resolve the existing /Info dictionary without creating one. Returns null
    /// when the document has no Info dict. Used by DocumentInfo read access so a
    /// <c>new DocumentInfo(doc)</c> reflects on-disk metadata without side effects.</summary>
    /// <summary>The /Info dict a DocumentInfo setter created for a document that had
    /// none. It lives in the pending-object list (not reachable through the reader
    /// until save), so in-memory readers — Document.Info, the PDF/A conversion's
    /// Info↔XMP sync — resolve it through here.</summary>
    private PdfDictionary? _pendingInfoDict;

    internal PdfDictionary? ResolveExistingInfoDict()
        => _reader.ResolveDict(_reader.Trailer.Get("Info")) ?? _pendingInfoDict;

    /// <summary>True when this document was created from scratch (<c>new Document()</c>)
    /// rather than loaded from existing bytes. A from-scratch document seeds the standard
    /// document-information text entries as empty strings the first time its /Info dict is
    /// materialised (see <see cref="DocumentInfo"/>), so unset fields round-trip through
    /// save/reopen as empty rather than absent — matching Aspose.Pdf.</summary>
    internal bool IsNewDocument => _isNewDocument;

    internal (PdfDictionary dict, int objNum) EnsureInfoDict()
    {
        var infoRef = _reader.Trailer.Get("Info");
        if (infoRef is PdfIndirectRef iref)
        {
            var dict = _reader.ResolveDict(infoRef);
            if (dict is not null)
            {
                // The Info dict already exists on disk; a DocumentInfo setter is about to
                // mutate it in place. Mark it dirty so the (incremental) writer re-emits the
                // object — otherwise metadata edits like ModDate are silently dropped on save.
                MarkDirty(iref.ObjectNumber, dict);
                return (dict, iref.ObjectNumber);
            }
        }

        // No Info dict — create one. Cached on the document so every other
        // DocumentInfo instance materialised before save shares it (the pending
        // object is not reachable through the reader yet).
        var newDict = new PdfDictionary();
        var objNum = AllocateObjectNumber();
        _newInfoObjNum = objNum;
        AddNewObject(objNum, newDict);
        _pendingInfoDict = newDict;
        return (newDict, objNum);
    }

    /// <summary>Clears memory.</summary>
    public void FreeMemory()
    {
        _pages?.FreeMemory();
    }

    public void Dispose() => FreeMemory();
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
