namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for accessing PDF document metadata and properties.
/// </summary>
public sealed class PdfFileInfo : IDisposable
{
    private Document? _doc;
    private bool _hasOpenPassword;
    private bool _hasEditPassword;
    private bool _headerLooksLikePdf;

    /// <summary>True when the source bytes start with <c>%PDF-</c> at byte 0.
    /// PDF 32000-2 § 7.5.2 requires the magic at the very start of a strictly
    /// valid PDF; lenient parsers tolerate up to 1024 leading bytes (so PDF
    /// polyglots and email-attached PDFs still parse), but strict validation
    /// must reject them. Cached at bind time so <see cref="IsPdfFile"/> with
    /// <see cref="UseStrictValidation"/> can flag a polyglot or non-PDF
    /// payload even when the lenient parser accepted it.</summary>
    private static bool DetectPdfHeader(byte[] input)
    {
        if (input is null || input.Length < 5) return false;
        return input[0] == 0x25 && input[1] == 0x50 && input[2] == 0x44
            && input[3] == 0x46 && input[4] == 0x2D; // '%PDF-' at offset 0
    }

    private static (bool hasOpen, bool hasEdit) DetectPasswords(byte[] input)
    {
        bool hasOpen;
        try
        {
            using var probe = Document.Open(input);
            if (!probe.IsEncrypted)
                hasOpen = false;
            else if (!probe.IsDecrypted)
                hasOpen = true;
            else
                // Decrypted with empty pwd. If it succeeded only via the owner /O
                // path, the user /U entry is non-empty — file requires an open
                // (user) password to view normally.
                hasOpen = probe.IsOwnerAuthentication;
        }
        catch
        {
            hasOpen = true;
        }

        return (hasOpen, false); // hasEdit determined after doc is opened
    }

    private void DetectEditPassword(out bool hasEdit)
    {
        // HasEditPassword: the file has an effective owner password — i.e.
        // an owner password that is DIFFERENT from the user password. When
        // owner == user the encryption mechanically requires owner=user but
        // there is no separate owner-only authority, so HasEditPassword is
        // false even if /P happens to restrict things.
        if (_doc is null || !_doc.IsEncrypted)
        {
            hasEdit = false;
            return;
        }
        if (_doc.IsOwnerAuthentication)
        {
            // Authenticated via owner-path with the supplied password. User-path
            // failed, so user pwd ≠ supplied = owner → owner ≠ user.
            hasEdit = true;
        }
        else
        {
            // Authenticated via user-path. If owner-path with the same supplied
            // password ALSO works, the file's owner pwd equals the user pwd →
            // no separate owner password.
            hasEdit = !_doc.OwnerPasswordEqualsUserPassword;
        }
    }

    /// <summary>
    /// Create an empty PdfFileInfo instance. Use BindPdf to attach a document.
    /// </summary>
    public PdfFileInfo()
    {
        _doc = null;
        _hasOpenPassword = false;
        _hasEditPassword = false;
    }

    /// <summary>
    /// Create a PdfFileInfo instance wrapping an existing Document.
    /// </summary>
    public PdfFileInfo(Document document)
    {
        _doc = document;
        _hasOpenPassword = document.IsEncrypted && !document.IsDecrypted;
        DetectEditPassword(out _hasEditPassword);
    }

    /// <summary>The bound Document. Callers use this to perform
    /// document-level operations alongside the info-setting facade
    /// (e.g. <c>info.Document.Convert(...)</c>) before saving via
    /// <see cref="SaveNewInfo(string)"/>.</summary>
    public Document? Document => _doc;

    /// <summary>
    /// Create a PdfFileInfo instance from PDF bytes.
    /// </summary>
    public PdfFileInfo(byte[] input)
    {
        _headerLooksLikePdf = DetectPdfHeader(input);
        try
        {
            _doc = Document.Open(input);
            _hasOpenPassword = _doc.IsEncrypted && (!_doc.IsDecrypted || _doc.IsOwnerAuthentication);
            DetectEditPassword(out _hasEditPassword);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidPasswordException)
        {
            _doc = null;
            _hasOpenPassword = true;
            _hasEditPassword = false;
        }
    }

    /// <summary>
    /// Create a PdfFileInfo instance from PDF bytes with a password.
    /// </summary>
    public PdfFileInfo(byte[] input, string password)
    {
        // Detect open password by trying without password first
        var (hasOpen, _) = DetectPasswords(input);
        _hasOpenPassword = hasOpen;

        try
        {
            _doc = Document.Open(input, password);
            DetectEditPassword(out _hasEditPassword);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidPasswordException)
        {
            _doc = null;
            _hasEditPassword = false;
        }
    }

    /// <summary>
    /// Create a PdfFileInfo instance from a file path.
    /// </summary>
    public PdfFileInfo(string inputFile) : this(File.ReadAllBytes(inputFile))
    {
        _inputFilePath = inputFile;
    }

    /// <summary>
    /// Create a PdfFileInfo instance from a file path with a password.
    /// </summary>
    public PdfFileInfo(string inputFile, string password) : this(File.ReadAllBytes(inputFile), password)
    {
        _inputFilePath = inputFile;
    }

    /// <summary>
    /// Create a PdfFileInfo instance from a stream.
    /// </summary>
    public PdfFileInfo(Stream inputStream)
    {
        using var ms = new MemoryStream();
        if (inputStream.CanSeek) inputStream.Position = 0;
        inputStream.CopyTo(ms);
        var bytes = ms.ToArray();
        _headerLooksLikePdf = DetectPdfHeader(bytes);
        try
        {
            _doc = Document.Open(bytes);
            _hasOpenPassword = _doc.IsEncrypted && (!_doc.IsDecrypted || _doc.IsOwnerAuthentication);
            DetectEditPassword(out _hasEditPassword);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidPasswordException)
        {
            _doc = null;
            _hasOpenPassword = true;
            _hasEditPassword = false;
        }
    }

    /// <summary>Open from a stream with a password.</summary>
    public PdfFileInfo(Stream inputStream, string password)
    {
        using var ms = new MemoryStream();
        if (inputStream.CanSeek) inputStream.Position = 0;
        inputStream.CopyTo(ms);
        var bytes = ms.ToArray();
        var (hasOpen, _) = DetectPasswords(bytes);
        _hasOpenPassword = hasOpen;
        try
        {
            _doc = Document.Open(bytes, password);
            DetectEditPassword(out _hasEditPassword);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidPasswordException)
        {
            _doc = null;
            _hasEditPassword = false;
        }
    }

    /// <summary>Open from a stream with a password and a custom security handler. The handler is stored only.</summary>
    public PdfFileInfo(Stream inputStream, string password, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(inputStream, password)
    {
        _ = customSecurityHandler;
    }

    /// <summary>Open from a file path with a password and a custom security handler. The handler is stored only.</summary>
    public PdfFileInfo(string inputFile, string password, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(inputFile, password)
    {
        _ = customSecurityHandler;
    }

    private string? _inputFilePath;

    /// <summary>
    /// Path of the source PDF. Setting opens and binds the file by reading its bytes
    /// — matches the facade-style "set-then-read" pattern where the file handle is
    /// released immediately (so the file can be deleted after the setter returns).
    /// </summary>
    public string? InputFile
    {
        get => _inputFilePath;
        set
        {
            _inputFilePath = value;
            if (value is null)
            {
                _doc?.Dispose();
                _doc = null;
                _hasOpenPassword = false;
                _hasEditPassword = false;
                return;
            }
            var bytes = File.ReadAllBytes(value);
            try
            {
                _doc = Document.Open(bytes);
                _hasOpenPassword = _doc.IsEncrypted && (!_doc.IsDecrypted || _doc.IsOwnerAuthentication);
                DetectEditPassword(out _hasEditPassword);
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidPasswordException)
            {
                _doc = null;
                _hasOpenPassword = true;
                _hasEditPassword = false;
            }
        }
    }

    /// <summary>Whether the PDF requires a user/open password to open.</summary>
    public bool HasOpenPassword => _hasOpenPassword;

    /// <summary>Whether the PDF has an owner/edit password (i.e. /P is
    /// restricted by an owner password). Throws
    /// <see cref="InvalidPasswordException"/> when
    /// <see cref="PasswordType"/> is <see cref="PasswordType.Inaccessible"/>
    /// because the file's permission bits cannot be read until a valid
    /// open password is supplied.</summary>
    public bool HasEditPassword
    {
        get
        {
            if (PasswordType == PasswordType.Inaccessible)
                throw new InvalidPasswordException(
                    "Cannot determine HasEditPassword without a valid open password.");
            return _hasEditPassword;
        }
    }

    /// <summary>Which kind of password (if any) authenticated the bound PDF.
    /// Returns <see cref="PasswordType.None"/> for unencrypted PDFs,
    /// <see cref="PasswordType.Inaccessible"/> when the file requires an
    /// open password that was not supplied,
    /// <see cref="PasswordType.Owner"/> when the supplied password matched
    /// the owner /O entry (full permissions), and
    /// <see cref="PasswordType.User"/> otherwise (the file opened with no
    /// password or with the user /U entry).</summary>
    public PasswordType PasswordType
    {
        get
        {
            if (!IsEncrypted) return PasswordType.None;
            if (_doc is null || !_doc.IsDecrypted) return PasswordType.Inaccessible;
            return _doc.IsOwnerAuthentication ? PasswordType.Owner : PasswordType.User;
        }
    }

    /// <summary>The document title.</summary>
    public string? Title
    {
        get => _doc?.Info.Title;
        set { if (_doc is not null) _doc.Info.Title = value; }
    }

    /// <summary>The document author.</summary>
    public string? Author
    {
        get => _doc?.Info.Author;
        set { if (_doc is not null) _doc.Info.Author = value; }
    }

    /// <summary>The document subject.</summary>
    public string? Subject
    {
        get => _doc?.Info.Subject;
        set { if (_doc is not null) _doc.Info.Subject = value; }
    }

    /// <summary>The document keywords.</summary>
    public string? Keywords
    {
        get => _doc?.Info.Keywords;
        set { if (_doc is not null) _doc.Info.Keywords = value; }
    }

    /// <summary>The creator application.</summary>
    public string? Creator
    {
        get => _doc?.Info.Creator;
        set { if (_doc is not null) _doc.Info.Creator = value; }
    }

    /// <summary>The producer application that created the PDF.</summary>
    public string? Producer => _doc?.Info.Producer;

    /// <summary>
    /// Custom metadata entries from the underlying /Info dictionary, keyed by
    /// name. Excludes the predefined PDF 32000-2 § 14.3.3 keys (Title, Author,
    /// Subject, Keywords, Creator, Producer, CreationDate, ModDate, Trapped) —
    /// those have dedicated typed properties on this class.
    /// </summary>
    public Dictionary<string, string> Header
    {
        get
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_doc is null) return result;
            foreach (var key in _doc.Info.Keys)
            {
                if (DocumentInfo.IsPredefinedKey(key)) continue;
                if (_doc.Info[key] is { } v) result[key] = v;
            }
            return result;
        }
        set
        {
            if (_doc is null || value is null) return;
            // Clear existing custom entries, then apply the supplied map.
            var existing = new List<string>();
            foreach (var key in _doc.Info.Keys)
                if (!DocumentInfo.IsPredefinedKey(key)) existing.Add(key);
            foreach (var key in existing) _doc.Info.SetCustom(key, string.Empty);
            foreach (var kv in value) _doc.Info.SetCustom(kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// Save the modified info to a new PDF file.
    /// </summary>
    public byte[] SaveNewInfo()
    {
        return _doc?.ToArray() ?? [];
    }

    /// <summary>
    /// Save the modified info to a new PDF file, synchronizing Info dict entries to XMP metadata.
    /// </summary>
    public byte[] SaveNewInfoWithXmp()
    {
        if (_doc is null) return [];
        SyncInfoToXmp();
        return _doc.ToArray();
    }

    /// <summary>
    /// Save the modified info to a file, synchronizing Info dict entries to XMP metadata.
    /// Returns true on success.
    /// </summary>
    public bool SaveNewInfoWithXmp(string outputFileName)
    {
        if (_doc is null) return false;
        SyncInfoToXmp();
        File.WriteAllBytes(outputFileName, _doc.ToArray());
        return true;
    }

    /// <summary>
    /// Set a custom metadata property in the document info dictionary.
    /// </summary>
    public void SetMetaInfo(string name, string value)
    {
        _doc?.Info.SetCustom(name, value);
    }

    /// <summary>
    /// Get a custom metadata property from the document info dictionary.
    /// </summary>
    public string GetMetaInfo(string name)
    {
        return _doc?.Info.GetCustom(name) ?? string.Empty;
    }

    private void SyncInfoToXmp()
    {
        if (_doc is null) return;
        var meta = _doc.GetOrCreateMetadata();
        var info = _doc.Info;

        if (info.Title is not null) meta.Set("dc:title", info.Title);
        if (info.Author is not null) meta.Set("dc:creator", info.Author);
        if (info.Creator is not null) meta.Set("xmp:CreatorTool", info.Creator);
        if (info.Subject is not null) meta.Set("dc:description", info.Subject);
        if (info.Keywords is not null) meta.Set("pdf:Keywords", info.Keywords);
        if (info.Producer is not null) meta.Set("pdf:Producer", info.Producer);
        if (info.CreationDate != DateTime.MinValue) meta.Set("xmp:CreateDate", info.CreationDate.ToString("yyyy-MM-ddTHH:mm:sszzz"));
        if (info.ModDate != DateTime.MinValue) meta.Set("xmp:ModifyDate", info.ModDate.ToString("yyyy-MM-ddTHH:mm:sszzz"));
    }

    /// <summary>The creation date in PDF native format
    /// (<c>D:YYYYMMDDHHmmSSOHH'mm'</c>), or empty when absent. Returns the raw
    /// /Info/CreationDate string verbatim rather than re-formatting from the
    /// parsed DateTime, preserving the source document's timezone offset.</summary>
    public string CreationDate
    {
        get => _doc?.Info.GetRawString("CreationDate") ?? string.Empty;
        set
        {
            if (_doc is null) return;
            if (string.IsNullOrEmpty(value))
            {
                _doc.Info.CreationDate = DateTime.MinValue;
                return;
            }
            _doc.Info.SetRawString("CreationDate", value);
        }
    }

    /// <summary>Raw <see cref="DateTime"/> view of <see cref="CreationDate"/> (FOSS-only convenience).</summary>
    public DateTime CreationDateValue
    {
        get => _doc?.Info.CreationDate ?? DateTime.MinValue;
        set { if (_doc is not null) _doc.Info.CreationDate = value; }
    }

    /// <summary>The modification date in PDF native format
    /// (<c>D:YYYYMMDDHHmmSSOHH'mm'</c>), or empty when absent.</summary>
    public string ModDate
    {
        get => _doc?.Info.GetRawString("ModDate") ?? string.Empty;
        set
        {
            if (_doc is null) return;
            if (string.IsNullOrEmpty(value))
            {
                _doc.Info.ModDate = DateTime.MinValue;
                return;
            }
            _doc.Info.SetRawString("ModDate", value);
        }
    }

    /// <summary>Raw <see cref="DateTime"/> view of <see cref="ModDate"/> (FOSS-only convenience).</summary>
    public DateTime ModDateValue
    {
        get => _doc?.Info.ModDate ?? DateTime.MinValue;
        set { if (_doc is not null) _doc.Info.ModDate = value; }
    }

    /// <summary>Number of pages.</summary>
    public int NumberOfPages => _doc?.PageCount ?? 0;

    /// <summary>The PDF version string (e.g., "1.4", "1.7").</summary>
    public string? PdfVersion => _doc?.PdfVersion;

    /// <summary>Method-form alias of <see cref="PdfVersion"/> matching the
    /// Aspose.PDF for .NET PdfFileInfo.GetPdfVersion() public surface.</summary>
    public string? GetPdfVersion() => PdfVersion;

    /// <summary>Whether the document is encrypted.</summary>
    public bool IsEncrypted => _hasOpenPassword || (_doc?.IsEncrypted ?? false);

    /// <summary>Whether the document has a form.</summary>
    public bool HasForm => _doc?.HasForm ?? false;

    /// <summary>Whether the document has bookmarks.</summary>
    public bool HasOutlines => _doc?.HasOutlines ?? false;

    /// <summary>Whether the document has XMP metadata.</summary>
    public bool HasMetadata => _doc?.HasMetadata ?? false;

    /// <summary>Whether the document has embedded files.</summary>
    public bool HasEmbeddedFiles => _doc?.HasEmbeddedFiles ?? false;

    /// <summary>Whether the document has page labels.</summary>
    public bool HasPageLabels => _doc?.HasPageLabels ?? false;

    /// <summary>Whether the document has layers (optional content).</summary>
    public bool HasLayers => _doc?.HasLayers ?? false;

    /// <summary>Whether the document is a PDF Portfolio (has a /Collection entry in the catalog).</summary>
    public bool HasCollection => _doc?.HasCollection ?? false;

    /// <summary>Whether the document is tagged.</summary>
    public bool IsTagged => _doc?.IsTagged ?? false;

    /// <summary>The page layout mode (e.g., "SinglePage", "TwoColumnLeft").</summary>
    public string? PageLayout => _doc?.PageLayoutName;

    /// <summary>The page mode (e.g., "UseOutlines", "FullScreen").</summary>
    public string? PageMode => _doc?.PageModeName;

    /// <summary>
    /// Get the width of a specific page in points (1-based page number).
    /// </summary>
    public float GetPageWidth(int pageNum) => (float)(_doc?.Pages.At(pageNum).Width ?? 0);

    /// <summary>
    /// Get the height of a specific page in points (1-based page number).
    /// </summary>
    public float GetPageHeight(int pageNum) => (float)(_doc?.Pages.At(pageNum).Height ?? 0);

    /// <summary>
    /// Get the rotation of a specific page (0, 90, 180, 270).
    /// </summary>
    public int GetPageRotation(int pageNum) => _doc?.Pages.At(pageNum).RotateDegrees ?? 0;

    /// <summary>X coordinate of the page's crop origin within the media boundary
    /// (CropBox left inset). Returns 0 when the crop box matches the media box.</summary>
    public float GetPageXOffset(int pageNum)
    {
        var page = _doc?.Pages.At(pageNum);
        if (page is null) return 0f;
        var media = page.MediaBox;
        if (media is null) return 0f;
        var crop = page.CropBox ?? media;
        return (float)(crop.LLX - media.LLX);
    }

    /// <summary>Y coordinate of the page's crop origin within the media boundary
    /// (CropBox bottom inset). Returns 0 when the crop box matches the media box.</summary>
    public float GetPageYOffset(int pageNum)
    {
        var page = _doc?.Pages.At(pageNum);
        if (page is null) return 0f;
        var media = page.MediaBox;
        if (media is null) return 0f;
        var crop = page.CropBox ?? media;
        return (float)(crop.LLY - media.LLY);
    }

    /// <summary>
    /// Get all document metadata as a dictionary.
    /// </summary>
    public Dictionary<string, string?> GetDocumentInfo()
    {
        return new Dictionary<string, string?>
        {
            ["Title"] = Title,
            ["Author"] = Author,
            ["Subject"] = Subject,
            ["Keywords"] = Keywords,
            ["Creator"] = Creator,
            ["Producer"] = Producer,
            ["CreationDate"] = string.IsNullOrEmpty(CreationDate) ? null : CreationDate,
            ["ModDate"] = string.IsNullOrEmpty(ModDate) ? null : ModDate,
        };
    }

    /// <summary>
    /// Check if the given bytes represent a valid PDF file. Renamed from
    /// <c>IsPdfFile</c> so it doesn't collide with the instance
    /// <see cref="IsPdfFile"/> property.
    /// </summary>
    public static bool IsValidPdfBytes(byte[] data)
    {
        if (data is null || data.Length < 5) return false;
        // Check for %PDF- header
        return data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F' && data[4] == '-';
    }

    /// <summary>
    /// Check if the given stream represents a valid PDF file. Renamed from
    /// <c>IsPdfFile</c> so it doesn't collide with the instance
    /// <see cref="IsPdfFile"/> property.
    /// </summary>
    public static bool IsValidPdfStream(Stream stream)
    {
        var buf = new byte[5];
        var pos = stream.Position;
        var read = stream.Read(buf, 0, 5);
        stream.Position = pos;
        if (read < 5) return false;
        return buf[0] == '%' && buf[1] == 'P' && buf[2] == 'D' && buf[3] == 'F' && buf[4] == '-';
    }

    /// <summary>
    /// Get the document privileges (permissions).
    /// </summary>
    public DocumentPrivilege? GetDocumentPrivilege()
    {
        return _doc is null ? null : new DocumentPrivilege(_doc.Permissions);
    }

    /// <summary>
    /// Bind this facade to an existing Document instance.
    /// </summary>
    public void BindPdf(Document srcDoc)
    {
        _doc = srcDoc;
        _hasOpenPassword = srcDoc.IsEncrypted && !srcDoc.IsDecrypted;
        DetectEditPassword(out _hasEditPassword);
    }

    /// <summary>
    /// Clears the standard /Info dictionary entries (Title, Author, Subject,
    /// Keywords, Creator, Producer, Trapped, CreationDate, ModDate) on the
    /// bound document. No-op when no document is bound.
    /// </summary>
    public void ClearInfo()
    {
        if (_doc is null) return;
        var info = _doc.Info;
        info.Title = null;
        info.Author = null;
        info.Subject = null;
        info.Keywords = null;
        info.Creator = null;
        info.Producer = null;
        info.Trapped = null;
        info.CreationDate = DateTime.MinValue;
        info.ModDate = DateTime.MinValue;
        info.ClearCustomData();
    }

    /// <summary>
    /// Bind this facade to a PDF file at the given path.
    /// </summary>
    public void BindPdf(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            _doc = Document.Open(bytes);
            _hasOpenPassword = _doc.IsEncrypted && (!_doc.IsDecrypted || _doc.IsOwnerAuthentication);
            DetectEditPassword(out _hasEditPassword);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidPasswordException)
        {
            _doc = null;
            _hasOpenPassword = true;
            _hasEditPassword = false;
        }
    }

    /// <summary>
    /// Whether the bound document is a valid PDF file (instance property).
    /// Property form mirrors the Aspose.PDF for .NET PdfFileInfo.IsPdfFile public surface.
    /// </summary>
    public bool IsPdfFile => _doc != null && (!UseStrictValidation || _headerLooksLikePdf);

    /// <summary>
    /// Save the bound document to a stream.
    /// </summary>
    public void Save(Stream destStream)
    {
        if (_doc == null) throw new InvalidOperationException("No document bound");
        var bytes = _doc.ToArray();
        destStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Save the bound document to a file path.</summary>
    public void Save(string destFile)
    {
        if (_doc == null) throw new InvalidOperationException("No document bound");
        _doc.Save(destFile);
    }

    /// <summary>Close and release the bound document.</summary>
    public void Close()
    {
        _doc?.Dispose();
        _doc = null;
    }

    /// <summary>Save the modified Info dictionary to a stream. Returns true on success.</summary>
    public bool SaveNewInfo(Stream outputStream)
    {
        if (_doc is null || outputStream is null) return false;
        var bytes = _doc.ToArray();
        outputStream.Write(bytes, 0, bytes.Length);
        return true;
    }

    /// <summary>Save the modified Info dictionary to a file. Returns true on success.</summary>
    public bool SaveNewInfo(string outputFile)
    {
        if (_doc is null) return false;
        // Sync /Info values into the XMP packet so a re-read against
        // Document.Metadata sees the same Title / Author / … the caller wrote
        // through this facade. Without this, PDF/A-style readers that look at
        // XMP first surface stale "Untitled"-style placeholders even though
        // /Info has the updated value.
        SyncInfoToXmp();
        File.WriteAllBytes(outputFile, _doc.ToArray());
        return true;
    }

    /// <summary>Source-PDF stream. Setting opens and binds the document; getter returns null.</summary>
    public Stream? InputStream
    {
        get => null;
        set
        {
            if (value is null)
            {
                _doc?.Dispose();
                _doc = null;
                return;
            }
            using var ms = new MemoryStream();
            if (value.CanSeek) value.Position = 0;
            value.CopyTo(ms);
            try
            {
                _doc = Document.Open(ms.ToArray());
                _hasOpenPassword = _doc.IsEncrypted && (!_doc.IsDecrypted || _doc.IsOwnerAuthentication);
                DetectEditPassword(out _hasEditPassword);
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidPasswordException)
            {
                _doc = null;
                _hasOpenPassword = true;
                _hasEditPassword = false;
            }
        }
    }

    /// <summary>When true, the parser applies strict PDF validation rules. Stored only; the parser remains lenient.</summary>
    public bool UseStrictValidation { get; set; }

    public void Dispose() => _doc?.Dispose();
}
