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

public sealed partial class Document
{
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

    /// <summary>Whether to unload large objects after use to reduce memory pressure. Stored only; not currently honoured.</summary>
    public bool EnableObjectUnload { get; set; }

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
}
