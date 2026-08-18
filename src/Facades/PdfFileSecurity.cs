using Aspose.Pdf.Security;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for encrypting, decrypting, and changing passwords on PDF files.
/// Supports both static byte[]-based use and a stateful Document-bound use.
/// </summary>
public sealed class PdfFileSecurity : IDisposable
{
    private Document? _doc;
    private bool _ownsDoc;
    private Stream? _outputStream;
    private string? _outputFile;
    private Stream? _inputStream;
    private string? _inputFile;
    // Raw bytes of the bound source, captured even when the PDF is encrypted and
    // can't be opened without a password — DecryptFile/ChangePassword re-open these.
    private byte[]? _sourceBytes;

    /// <summary>The document bound to this instance, exposed so it can be
    /// chained into another facade.</summary>
    public Document Document => _doc ?? throw new InvalidOperationException("No document bound.");

    /// <summary>Default ctor — for the static byte[]-based overloads.</summary>
    public PdfFileSecurity() { }

    /// <summary>Wrap an existing <see cref="Document"/>; caller owns its
    /// lifetime. Stateful EncryptFile/DecryptFile mutate this document and
    /// <see cref="Save(string)"/> writes it out.</summary>
    public PdfFileSecurity(Document document)
    {
        _doc = document ?? throw new ArgumentNullException(nameof(document));
        _ownsDoc = false;
    }

    /// <summary>Open the file at <paramref name="path"/> and bind it.
    /// The instance owns the Document and disposes it.</summary>
    public PdfFileSecurity(string path)
    {
        _sourceBytes = File.ReadAllBytes(path);
        _doc = TryBindOpen(_sourceBytes);
        _ownsDoc = _doc is not null;
    }

    /// <summary>Wrap a document and route subsequent saves to
    /// <paramref name="outputStream"/>.</summary>
    public PdfFileSecurity(Document document, Stream outputStream)
        : this(document)
    {
        _outputStream = outputStream;
    }

    /// <summary>Wrap a document and route subsequent saves to
    /// <paramref name="outputFile"/>.</summary>
    public PdfFileSecurity(Document document, string outputFile)
        : this(document)
    {
        _outputFile = outputFile;
    }

    /// <summary>Bind from a stream and save to a stream.</summary>
    public PdfFileSecurity(Stream inputStream, Stream outputStream)
    {
        _inputStream = inputStream;
        _outputStream = outputStream;
        // Capture the raw source so DecryptFile/ChangePassword can re-open the
        // original (still-encrypted) bytes rather than a re-serialised copy.
        using (var ms = new MemoryStream())
        {
            if (inputStream.CanSeek) inputStream.Position = 0;
            inputStream.CopyTo(ms);
            _sourceBytes = ms.ToArray();
        }
        _doc = TryBindOpen(_sourceBytes);
        _ownsDoc = _doc is not null;
    }

    /// <summary>Bind from a path and save to a path.</summary>
    public PdfFileSecurity(string inputFile, string outputFile)
    {
        _inputFile = inputFile;
        _outputFile = outputFile;
        // Capture the raw source so DecryptFile/ChangePassword can re-open the
        // original (still-encrypted) bytes rather than a re-serialised copy.
        _sourceBytes = File.ReadAllBytes(inputFile);
        _doc = TryBindOpen(_sourceBytes);
        _ownsDoc = _doc is not null;
    }

    public void Dispose()
    {
        if (_ownsDoc && _doc is not null) _doc.Dispose();
        _doc = null;
    }

    /// <summary>Alias for <see cref="Dispose"/>.</summary>
    public void Close() => Dispose();

    /// <summary>Always true and cannot be changed — the direct (non-Try) methods
    /// always propagate exceptions; the <c>Try*</c> variants always capture the
    /// last exception in <see cref="LastException"/> and return false. The
    /// setter throws <see cref="NotSupportedException"/>.</summary>
    public bool AllowExceptions
    {
        get => true;
        set => throw new NotSupportedException(
            "PdfFileSecurity.AllowExceptions cannot be changed; use the Try* methods to suppress exceptions.");
    }

    /// <summary>Last exception captured by a Try* method, or null.</summary>
    public Exception? LastException { get; private set; }

    /// <summary>Set the input file path used by stateful operations.</summary>
    public string InputFile
    {
        set
        {
            _inputFile = value;
            if (_ownsDoc && _doc is not null) _doc.Dispose();
            _sourceBytes = File.ReadAllBytes(value);
            _doc = TryBindOpen(_sourceBytes);
            _ownsDoc = _doc is not null;
        }
    }

    /// <summary>Set the input stream used by stateful operations.</summary>
    public Stream InputStream
    {
        set
        {
            _inputStream = value;
            if (_ownsDoc && _doc is not null) _doc.Dispose();
            using (var ms = new MemoryStream())
            {
                if (value.CanSeek) value.Position = 0;
                value.CopyTo(ms);
                _sourceBytes = ms.ToArray();
            }
            _doc = TryBindOpen(_sourceBytes);
            _ownsDoc = _doc is not null;
        }
    }

    /// <summary>Route the next Save to this file.</summary>
    public string OutputFile
    {
        set => _outputFile = value;
    }

    /// <summary>Route the next Save to this stream.</summary>
    public Stream OutputStream
    {
        set => _outputStream = value;
    }

    /// <summary>Bind to a PDF file at <paramref name="srcFile"/>.
    /// Disposes any previously-bound document the instance owned.</summary>
    public void BindPdf(string srcFile)
    {
        if (_ownsDoc && _doc is not null) _doc.Dispose();
        _sourceBytes = File.ReadAllBytes(srcFile);
        _doc = TryBindOpen(_sourceBytes);
        _ownsDoc = _doc is not null;
    }

    /// <summary>Bind to a PDF read from <paramref name="srcStream"/>.
    /// Disposes any previously-bound document the instance owned.</summary>
    public void BindPdf(Stream srcStream)
    {
        if (_ownsDoc && _doc is not null) _doc.Dispose();
        using var ms = new MemoryStream();
        if (srcStream.CanSeek) srcStream.Position = 0;
        srcStream.CopyTo(ms);
        _sourceBytes = ms.ToArray();
        _doc = TryBindOpen(_sourceBytes);
        _ownsDoc = _doc is not null;
    }

    /// <summary>Bind to an existing <see cref="Document"/>; caller owns its
    /// lifetime.</summary>
    public void BindPdf(Document document)
    {
        if (_ownsDoc && _doc is not null) _doc.Dispose();
        _doc = document ?? throw new ArgumentNullException(nameof(document));
        _ownsDoc = false;
    }

    /// <summary>Save the bound document to a file path.</summary>
    public void Save(string path)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        _doc.Save(path);
    }

    /// <summary>Save the bound document to a stream.</summary>
    public void Save(Stream stream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        _doc.Save(stream);
    }

    private void SaveBound()
    {
        if (_doc is null) return;
        if (_outputStream is not null) _doc.Save(_outputStream);
        else if (_outputFile is not null) _doc.Save(_outputFile);
    }

    // Open the source, tolerating an encrypted PDF that needs a password — returns
    // null so DecryptFile/ChangePassword can re-open it with the supplied password.
    private static Document? TryBindOpen(byte[] bytes)
    {
        try { return Document.Open(bytes); }
        catch (InvalidPasswordException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    // The bytes to (re-)open for password operations: the captured source if any,
    // otherwise the currently-bound document serialised.
    private byte[] RequireSource() =>
        _sourceBytes ?? _doc?.ToArray()
            ?? throw new InvalidOperationException("No document bound. Call BindPdf first.");

    /// <summary>Encrypt the bound document. Returns true on success.
    /// <paramref name="keySize"/> + <paramref name="cipher"/> map to a
    /// concrete <see cref="CryptoAlgorithm"/>.</summary>
    public bool EncryptFile(string userPassword, string ownerPassword,
        DocumentPrivilege privilege, KeySize keySize, Algorithm cipher)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        var crypto = MapCrypto(cipher, keySize);
        _doc.Encrypt(userPassword, ownerPassword, privilege, crypto);
        SaveBound();
        // Document.Encrypt only configures a pending encryptor; the in-memory document's
        // trailer gains /Encrypt (and IsEncrypted flips to true) only once it is written
        // out and reopened. Re-materialise the bound document from its now-encrypted bytes
        // so chaining it into another facade (e.g. new PdfFileInfo(fileSecurity.Document))
        // sees the encryption immediately.
        var encryptedBytes = _doc.ToArray();
        if (_ownsDoc) _doc.Dispose();
        _doc = Document.Open(encryptedBytes, ownerPassword);
        _ownsDoc = true;
        _sourceBytes = encryptedBytes;
        // The re-opened document decrypts its content in memory and carries no pending
        // encryptor, so a subsequent Save() would write it back out unencrypted (dropping
        // the /Encrypt trailer entry). Re-arm the encryptor so saving re-applies the same
        // encryption and the permission flags survive the round-trip.
        _doc.Encrypt(userPassword, ownerPassword, privilege, crypto);
        return true;
    }

    /// <summary>Encrypt the bound document defaulting cipher to AES.</summary>
    public bool EncryptFile(string userPassword, string ownerPassword,
        DocumentPrivilege privilege, KeySize keySize)
        => EncryptFile(userPassword, ownerPassword, privilege, keySize, Algorithm.AES);

    /// <summary>Try-variant of <see cref="EncryptFile(string,string,DocumentPrivilege,KeySize,Algorithm)"/>.
    /// On exception, captures it in <see cref="LastException"/> and returns false.</summary>
    public bool TryEncryptFile(string userPassword, string ownerPassword,
        DocumentPrivilege privilege, KeySize keySize)
        => Try(() => EncryptFile(userPassword, ownerPassword, privilege, keySize, Algorithm.AES));

    /// <summary>Apply privilege flags to the bound document by re-encrypting
    /// with empty passwords and the given permissions. Returns true on success.</summary>
    public bool SetPrivilege(DocumentPrivilege privilege)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (privilege is null) throw new ArgumentNullException(nameof(privilege));
        _doc.Encrypt(string.Empty, string.Empty, privilege, Aspose.Pdf.CryptoAlgorithm.AESx128);
        SaveBound();
        return true;
    }

    /// <summary>Apply privilege flags with explicit passwords.</summary>
    public bool SetPrivilege(string userPassword, string ownerPassword, DocumentPrivilege privilege)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (privilege is null) throw new ArgumentNullException(nameof(privilege));
        _doc.Encrypt(userPassword, ownerPassword, privilege, Aspose.Pdf.CryptoAlgorithm.AESx128);
        SaveBound();
        return true;
    }

    /// <summary>Try-variant of <see cref="SetPrivilege(string,string,DocumentPrivilege)"/>.</summary>
    public bool TrySetPrivilege(string userPassword, string ownerPassword, DocumentPrivilege privilege)
        => Try(() => SetPrivilege(userPassword, ownerPassword, privilege));

    /// <summary>Decrypt the bound document using the given password.
    /// Returns true on success.</summary>
    public bool DecryptFile(string ownerPassword)
    {
        var src = RequireSource();
        // Open with the password (throws "incorrect password" for an encrypted
        // document with the wrong password — the Try* wrappers turn that into false).
        var opened = Document.Open(src, ownerPassword);
        if (!opened.IsEncrypted)
        {
            // Source wasn't encrypted — there's nothing to decrypt.
            opened.Dispose();
            return false;
        }
        if (_ownsDoc) _doc?.Dispose();
        _doc = opened;
        _ownsDoc = true;
        // Strip the encryption so the saved output is plaintext — DecryptFile's
        // whole purpose. Without this the /Encrypt entry would survive the save.
        _doc.Decrypt();
        SaveBound();
        return true;
    }

    /// <summary>Try-variant of <see cref="DecryptFile(string)"/>.</summary>
    public bool TryDecryptFile(string ownerPassword)
        => Try(() => DecryptFile(ownerPassword));

    /// <summary>Change passwords on the bound document while preserving
    /// existing privileges and AES-128 encryption.</summary>
    public bool ChangePassword(string ownerPassword, string newUserPassword, string newOwnerPassword)
    {
        var src = RequireSource();
        var opened = Document.Open(src, ownerPassword);
        if (!opened.IsEncrypted)
        {
            // No existing encryption to change the password of. If the caller
            // supplied an access password (even ""), the facade
            // rejects the call — you should not provide a password for an
            // unencrypted document. A null password means "none supplied", so
            // fall through to the historical no-op false return (the Try*
            // variants also convert the throw to a false via LastException).
            opened.Dispose();
            if (ownerPassword != null)
                throw new PdfException("Pdf document is not encrypted, so don't provide password to get access.");
            return false;
        }
        if (_ownsDoc) _doc?.Dispose();
        _doc = opened;
        _ownsDoc = true;
        var existing = new DocumentPrivilege(_doc.Permissions);
        _doc.Encrypt(newUserPassword, newOwnerPassword, existing, Aspose.Pdf.CryptoAlgorithm.AESx128);
        SaveBound();
        return true;
    }

    /// <summary>Change passwords and override privileges + key size.</summary>
    public bool ChangePassword(string ownerPassword, string newUserPassword,
        string newOwnerPassword, DocumentPrivilege privilege, KeySize keySize)
        => ChangePassword(ownerPassword, newUserPassword, newOwnerPassword, privilege, keySize, Algorithm.AES);

    /// <summary>Change passwords and override privileges + key size + cipher.</summary>
    public bool ChangePassword(string ownerPassword, string newUserPassword,
        string newOwnerPassword, DocumentPrivilege privilege, KeySize keySize, Algorithm cipher)
    {
        var src = RequireSource();
        var opened = Document.Open(src, ownerPassword);
        if (!opened.IsEncrypted)
        {
            // No existing encryption to change the password of. If the caller
            // supplied an access password (even ""), the facade
            // rejects the call — you should not provide a password for an
            // unencrypted document. A null password means "none supplied", so
            // fall through to the historical no-op false return (the Try*
            // variants also convert the throw to a false via LastException).
            opened.Dispose();
            if (ownerPassword != null)
                throw new PdfException("Pdf document is not encrypted, so don't provide password to get access.");
            return false;
        }
        if (_ownsDoc) _doc?.Dispose();
        _doc = opened;
        _ownsDoc = true;
        var crypto = MapCrypto(cipher, keySize);
        _doc.Encrypt(newUserPassword, newOwnerPassword, privilege, crypto);
        SaveBound();
        return true;
    }

    /// <summary>Try-variant of <see cref="ChangePassword(string,string,string)"/>.</summary>
    public bool TryChangePassword(string ownerPassword, string newUserPassword, string newOwnerPassword)
        => Try(() => ChangePassword(ownerPassword, newUserPassword, newOwnerPassword));

    /// <summary>Try-variant of the 5-arg <see cref="ChangePassword(string,string,string,DocumentPrivilege,KeySize)"/>.</summary>
    public bool TryChangePassword(string ownerPassword, string newUserPassword,
        string newOwnerPassword, DocumentPrivilege privilege, KeySize keySize)
        => Try(() => ChangePassword(ownerPassword, newUserPassword, newOwnerPassword, privilege, keySize));

    /// <summary>Try-variant of the 6-arg <see cref="ChangePassword(string,string,string,DocumentPrivilege,KeySize,Algorithm)"/>.</summary>
    public bool TryChangePassword(string ownerPassword, string newUserPassword,
        string newOwnerPassword, DocumentPrivilege privilege, KeySize keySize, Algorithm cipher)
        => Try(() => ChangePassword(ownerPassword, newUserPassword, newOwnerPassword, privilege, keySize, cipher));

    private bool Try(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            // Try* variants intentionally suppress and report via LastException.
            LastException = ex;
            return false;
        }
    }

    private static CryptoAlgorithm MapCrypto(Algorithm algorithm, KeySize keySize)
    {
        // Algorithm enum is the high-level family; KeySize picks the strength.
        return algorithm switch
        {
            Algorithm.RC4 when keySize == KeySize.x40 => Aspose.Pdf.CryptoAlgorithm.RC4x40,
            Algorithm.RC4 => Aspose.Pdf.CryptoAlgorithm.RC4x128,
            Algorithm.AES when keySize == KeySize.x256 => Aspose.Pdf.CryptoAlgorithm.AESx256,
            Algorithm.AES => Aspose.Pdf.CryptoAlgorithm.AESx128,
            _ => Aspose.Pdf.CryptoAlgorithm.AESx128,
        };
    }

    /// <summary>
    /// Encrypt a PDF with the specified passwords and algorithm.
    /// </summary>
    public byte[] EncryptFile(byte[] input, string userPassword, string ownerPassword,
        DocumentPrivilege? permissions = null, CryptoAlgorithm algorithm = Aspose.Pdf.CryptoAlgorithm.AESx128)
    {
        using var doc = Document.Open(input);
        doc.Encrypt(userPassword, ownerPassword, permissions, algorithm);
        return doc.ToArray();
    }

    /// <summary>
    /// Remove encryption from a PDF using the correct password.
    /// </summary>
    public byte[] DecryptFile(byte[] input, string password)
    {
        using var doc = Document.Open(input, password);
        // Save without encryption
        return doc.ToArray();
    }

    /// <summary>
    /// Change the passwords on an encrypted PDF.
    /// </summary>
    public byte[] ChangePasswords(byte[] input, string oldOwnerPassword,
        string newUserPassword, string newOwnerPassword,
        CryptoAlgorithm algorithm = Aspose.Pdf.CryptoAlgorithm.AESx128)
    {
        using var doc = Document.Open(input, oldOwnerPassword);
        doc.Encrypt(newUserPassword, newOwnerPassword, algorithm: algorithm);
        return doc.ToArray();
    }
}
