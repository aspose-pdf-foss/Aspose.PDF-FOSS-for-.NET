using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.IO;

internal sealed class PdfReader
{
    private byte[] _data;
    private readonly XRefTable _xref;
    private readonly PdfParser _parser;
    private readonly PdfReaderOptions _options;
    private readonly Dictionary<(int objNum, int gen), PdfObject> _cache = new();
    private readonly Dictionary<int, PdfObject[]> _objStmCache = new();
    // In-memory objects added after parsing (e.g. imported form fields).
    // These are checked before the xref table during resolution.
    private readonly Dictionary<int, PdfObject> _overlayObjects = new();
    private PdfDecryptor? _decryptor;
    private bool _decryptorInitialized;

    private PdfReader(byte[] data, XRefTable xref, PdfReaderOptions options)
    {
        _data = data;
        _xref = xref;
        _parser = new PdfParser(data);
        _options = options;
    }

    /// <summary>A no-op reader for newly created annotations that are not yet backed by a document.</summary>
    internal static PdfReader Empty { get; } = new(Array.Empty<byte>(), new XRefTable(), new PdfReaderOptions());

    /// <summary>
    /// Releases the raw file buffer and parsed-object caches so a disposed document
    /// stops pinning its source bytes. Lazy resolution is unusable afterwards.
    /// </summary>
    internal void ReleaseBuffers()
    {
        _data = Array.Empty<byte>();
        _parser.ReleaseBuffers();
        _cache.Clear();
        _objStmCache.Clear();
        _overlayObjects.Clear();
    }

    public static PdfReader FromBytes(byte[] data)
    {
        return FromBytes(data, new PdfReaderOptions());
    }

    public static PdfReader FromBytes(byte[] data, PdfReaderOptions options)
    {
        // Validate PDF signature — search for "%PDF-" in the file (some PDFs have junk before header)
        var hasPdfSignature = false;
        var searchLimit = Math.Min(data.Length - 5, data.Length > 4096 ? 4096 : data.Length);
        for (var i = 0; i <= searchLimit; i++)
        {
            if (data[i] == '%' && data[i + 1] == 'P' && data[i + 2] == 'D' &&
                data[i + 3] == 'F' && data[i + 4] == '-')
            {
                hasPdfSignature = true;
                break;
            }
        }
        if (!hasPdfSignature)
        {
            // No '%PDF-' magic in the first 4 KiB. Even in lenient mode this
            // is non-recoverable per PDF 32000-2 § 7.5.2 — the recovery
            // scanner needs SOMETHING (xref offset, '%PDF-' anywhere) to
            // anchor against, and a file without the magic is by definition
            // not a PDF. Surface the canonical typed exception so callers
            // can pattern-match on it.
            throw new Aspose.Pdf.InvalidPdfFileFormatException("Incorrect file header");
        }

        XRefTable xref;
        try
        {
            xref = XRefTable.Read(data);
        }
        catch (Exception ex) when (options.RepairXref)
        {
            xref = RecoverXref(data, ex);
        }

        // Handle hybrid xref: traditional trailer with /XRefStm pointing to a supplementary stream
        var reader = new PdfReader(data, xref, options);
        // Allow PdfParser to resolve stream /Length indirect refs using the xref table.
        reader._parser.LengthResolver = reader.ResolveLengthIndirectRef;
        reader.HandleHybridXref();
        reader.DetectLinearization();
        return reader;
    }

    /// <summary>
    /// Open an encrypted PDF with a password. Throws if the password is incorrect.
    /// </summary>
    public static PdfReader FromBytes(byte[] data, string password)
    {
        return FromBytes(data, password, new PdfReaderOptions());
    }

    public static PdfReader FromBytes(byte[] data, string password, PdfReaderOptions options)
    {
        var reader = FromBytes(data, options);
        reader.InitDecryptor(password);
        reader.Password = password;
        return reader;
    }

    /// <summary>The password this reader was opened with (user or owner), or
    /// null when opened without one. Lets facades that must re-open the raw
    /// bytes (e.g. the signer's incremental update) authenticate again.</summary>
    internal string? Password { get; private set; }

    /// <summary>Raw PDF file bytes.</summary>
    public byte[] RawData => _data;

    public PdfDictionary Trailer => _xref.Trailer;
    public XRefTable XRefTable => _xref;

    /// <summary>The <see cref="Aspose.Pdf.Document"/> that owns this reader, set
    /// during document construction. Lets in-assembly components that hold only a
    /// reader/page reach the document (e.g. to allocate new indirect objects).</summary>
    internal Aspose.Pdf.Document? OwnerDocument { get; set; }
    public bool IsEncrypted => Trailer.ContainsKey("Encrypt");
    public bool IsDecrypted => _decryptor is not null;
    /// <summary>The active standard-security decryptor (also used to encrypt
    /// newly appended objects), or null when the document is not encrypted.</summary>
    internal Security.PdfDecryptor? Decryptor => _decryptor;

    /// <summary>Attach an externally-built decryptor (e.g. the public-key handler,
    /// whose file key comes from CMS recipients + a private key rather than a
    /// password). Marks decryptor init done so lazy password init is skipped.</summary>
    internal void AttachDecryptor(Security.PdfDecryptor decryptor)
    {
        _decryptor = decryptor;
        _decryptorInitialized = true;
    }
    /// <summary>True when the supplied password matched the owner /O entry
    /// (full permissions). False when it matched the user /U entry or no
    /// password was needed.</summary>
    public bool IsOwnerAuthentication => _decryptor?.IsOwnerAuthentication ?? false;

    /// <summary>True when the supplied password matched BOTH /U and /O —
    /// the file was encrypted with the same password for user and owner,
    /// so there is no effective owner password.</summary>
    public bool OwnerPasswordEqualsUserPassword => _decryptor?.OwnerPasswordEqualsUserPassword ?? false;
    internal PdfReaderOptions Options => _options;

    /// <summary>Suppresses <see cref="ClearCache"/> while set. In-memory edits to
    /// resolved objects (conversion steps, pending metadata) live in the cache until
    /// save; a mid-operation render (e.g. the PDF/A transparency simulation) must not
    /// flush them away.</summary>
    internal bool SuppressCacheClear;

    /// <summary>Clear the resolved-object cache to free memory after batch operations.</summary>
    internal void ClearCache()
    {
        if (!SuppressCacheClear) _cache.Clear();
    }

    /// <summary>Set when an in-place edit (e.g. replacing an image's data) may have left the
    /// original object orphaned. Signals the full-rewrite save to run a reachability pass so
    /// the superseded object is not written out (otherwise a load-replace-save keeps both the
    /// old and new image and the file never shrinks).</summary>
    internal bool MayHaveOrphansOnSave { get; set; }

    /// <summary>
    /// Returns true if the PDF is linearized (optimized for fast web viewing).
    /// A linearized PDF has a linearization dictionary as the first object in the file.
    /// </summary>
    public bool IsLinearized { get; private set; }

    public PdfDictionary Catalog
    {
        get
        {
            if (_catalog is not null) return _catalog;
            var rootRef = Trailer.Get("Root");
            _catalog = ResolveDict(rootRef);
            if (_catalog is null && _options.RepairXref)
            {
                // Root resolution failed — try xref recovery (handles concatenated PDFs
                // where the xref entries point to wrong offsets)
                try
                {
                    var recovered = RecoverXref(_data);
                    // Replace entries from recovery
                    foreach (var kvp in recovered.Entries)
                        _xref.SetEntry(kvp.Key, kvp.Value);
                    // Clear cache so objects are re-resolved from new offsets
                    _cache.Clear();
                    _objStmCache.Clear();
                    // Retry: use recovered trailer if original Root still fails
                    _catalog = ResolveDict(rootRef);
                    if (_catalog is null)
                    {
                        var recoveredRoot = recovered.Trailer.Get("Root");
                        _catalog = ResolveDict(recoveredRoot);
                    }
                }
                catch { /* recovery failed — fall through to throw */ }
            }
            else if (_catalog is not null)
            {
                // Catalog resolved but may be a garbage dict — validate it has /Type or /Pages
                if (!_catalog.ContainsKey("Pages") && _catalog.GetName("Type") != "Catalog")
                {
                    // Invalid catalog — clear and retry with recovery
                    _catalog = null;
                    if (_options.RepairXref)
                    {
                        try
                        {
                            var recovered = RecoverXref(_data);
                            foreach (var kvp in recovered.Entries)
                                _xref.SetEntry(kvp.Key, kvp.Value);
                            _cache.Clear();
                            _objStmCache.Clear();
                            _catalog = ResolveDict(rootRef);
                            if (_catalog is null)
                            {
                                var recoveredRoot = recovered.Trailer.Get("Root");
                                _catalog = ResolveDict(recoveredRoot);
                            }
                        }
                        catch { /* recovery failed */ }
                    }
                }
            }
            return _catalog ?? throw new Exception("The root object missing or invalid");
        }
    }
    private PdfDictionary? _catalog;

    /// <summary>
    /// Resolve an object — if it's an indirect reference, fetch and cache the actual object.
    /// </summary>
    public PdfObject? Resolve(PdfObject? obj)
    {
        if (obj is null or PdfNull) return null;
        if (obj is not PdfIndirectRef indirectRef) return obj;
        return ResolveRef(indirectRef.ObjectNumber, indirectRef.Generation);
    }

    public PdfDictionary? ResolveDict(PdfObject? obj)
    {
        var resolved = Resolve(obj);
        return resolved as PdfDictionary;
    }

    public PdfStream? ResolveStream(PdfObject? obj)
    {
        var resolved = Resolve(obj);
        return resolved as PdfStream;
    }

    /// <summary>A dictionary entry as a name, following an indirect reference.
    /// Some producers write even scalar entries like /Subtype indirect
    /// (<c>/Subtype 71 0 R</c>), where <see cref="PdfDictionary.GetName"/> —
    /// which has no reader — returns null and "is it a Form?" gates misfire.</summary>
    public string? ResolveName(PdfDictionary dict, string key)
        => (Resolve(dict.Get(key)) as PdfName)?.Value;

    /// <summary>A dictionary entry as an array, following an indirect reference.</summary>
    public PdfArray? ResolveArray(PdfObject? obj)
        => Resolve(obj) as PdfArray;

    /// <summary>
    /// Get the decoded stream data (decryption + filters applied).
    /// </summary>
    public byte[] DecodeStream(PdfStream stream, int objectNumber = 0, int generation = 0)
    {
        var data = stream.RawData;

        // Use stored object number if not explicitly provided
        if (objectNumber == 0 && stream.ObjectNumber > 0)
        {
            objectNumber = stream.ObjectNumber;
            generation = stream.Generation;
        }

        // Decrypt stream if needed (but not XRef streams or the Encrypt dict)
        if (_decryptor is not null && objectNumber > 0)
        {
            // Check for /Crypt filter — if present, the crypt filter name overrides the default
            string? cryptFilterName = null;
            var filterObj = stream.Dict.Get("Filter");
            if (filterObj is PdfName filterName && filterName.Value == "Crypt")
            {
                var dp = stream.Dict.Get("DecodeParms") as PdfDictionary;
                cryptFilterName = dp?.GetName("Name") ?? "Identity";
            }
            else if (filterObj is PdfArray filterArr)
            {
                // Check if first filter is Crypt
                if (filterArr.Count > 0 && filterArr[0] is PdfName fn && fn.Value == "Crypt")
                {
                    var dpArr = stream.Dict.Get("DecodeParms") as PdfArray;
                    if (dpArr is { Count: > 0 } && dpArr[0] is PdfDictionary dp)
                        cryptFilterName = dp.GetName("Name") ?? "Identity";
                    else
                        cryptFilterName = "Identity";
                }
            }

            data = _decryptor.DecryptStream(data, objectNumber, generation, cryptFilterName);
        }

        // /Filter and /DecodeParms may be stored as indirect refs in the stream dict.
        // Resolve them before passing to StreamFilter so filters are always applied.
        return StreamFilter.Decode(data, ResolveStreamFilterDict(stream.Dict));
    }

    /// <summary>Decode at most <paramref name="maxBytes"/> of a stream's leading content
    /// without materialising the whole payload (see <see cref="StreamFilter.DecodePrefix"/>).
    /// Used to sniff a large embedded-file header cheaply. Decryption still runs in full —
    /// the cipher operates on the (typically small) raw stream bytes, not the decoded output.</summary>
    public byte[] DecodeStreamPrefix(PdfStream stream, int maxBytes, int objectNumber = 0, int generation = 0)
    {
        var data = stream.RawData;

        if (objectNumber == 0 && stream.ObjectNumber > 0)
        {
            objectNumber = stream.ObjectNumber;
            generation = stream.Generation;
        }

        if (_decryptor is not null && objectNumber > 0)
        {
            string? cryptFilterName = null;
            var filterObj = stream.Dict.Get("Filter");
            if (filterObj is PdfName filterName && filterName.Value == "Crypt")
            {
                var dp = stream.Dict.Get("DecodeParms") as PdfDictionary;
                cryptFilterName = dp?.GetName("Name") ?? "Identity";
            }
            else if (filterObj is PdfArray filterArr && filterArr.Count > 0
                     && filterArr[0] is PdfName fn && fn.Value == "Crypt")
            {
                var dpArr = stream.Dict.Get("DecodeParms") as PdfArray;
                cryptFilterName = dpArr is { Count: > 0 } && dpArr[0] is PdfDictionary dp
                    ? dp.GetName("Name") ?? "Identity"
                    : "Identity";
            }
            data = _decryptor.DecryptStream(data, objectNumber, generation, cryptFilterName);
        }

        return StreamFilter.DecodePrefix(data, ResolveStreamFilterDict(stream.Dict), maxBytes);
    }

    /// <summary>
    /// Decrypt every in-use stream's RawData in place, then forget the decryptor.
    /// Call this when an encrypted-input document is about to be saved unencrypted —
    /// otherwise the writer would copy still-encrypted bytes into a trailer with no
    /// /Encrypt, leaving the saved PDF undecodable.
    /// </summary>
    internal void EnsurePlaintextStreams()
    {
        if (_decryptor is null) return;

        // Materialize all uncompressed in-use objects so we can rewrite stream payloads.
        // Streams cannot be packed inside ObjStms (PDF 32000-2 § 7.5.7), so the compressed
        // entries don't need this pass.
        foreach (var objNum in _xref.Entries.Keys.ToArray())
        {
            var entry = _xref.Entries[objNum];
            if (!entry.InUse || entry.IsCompressed || objNum == 0) continue;

            PdfObject? obj;
            try { obj = ResolveRef(objNum, entry.Generation); }
            catch { continue; }
            if (obj is not PdfStream s) continue;

            // Mirror DecodeStream's /Crypt filter detection so we apply the same key.
            string? cryptFilterName = null;
            var filterObj = s.Dict.Get("Filter");
            if (filterObj is PdfName fn && fn.Value == "Crypt")
            {
                var dp = s.Dict.Get("DecodeParms") as PdfDictionary;
                cryptFilterName = dp?.GetName("Name") ?? "Identity";
            }
            else if (filterObj is PdfArray fa && fa.Count > 0 && fa[0] is PdfName fn2 && fn2.Value == "Crypt")
            {
                var dpArr = s.Dict.Get("DecodeParms") as PdfArray;
                cryptFilterName = (dpArr is { Count: > 0 } && dpArr[0] is PdfDictionary dp2)
                    ? dp2.GetName("Name") ?? "Identity"
                    : "Identity";
            }

            var plaintext = _decryptor.DecryptStream(s.RawData, objNum, entry.Generation, cryptFilterName);
            s.ReplaceData(plaintext);
        }

        _decryptor = null;
    }

    /// <summary>
    /// Resolve a stream /Length indirect ref to a long integer value at parse time.
    /// Called by PdfParser.LengthResolver before the full reader resolution is available.
    /// Reads the referenced object directly from the PDF bytes via the xref offset.
    /// </summary>
    private long ResolveLengthIndirectRef(int objectNumber)
    {
        var entry = _xref.GetEntry(objectNumber);
        if (entry is null || !entry.Value.InUse) return -1;

        // If the Length object is in a compressed ObjStm, use the full resolve path.
        // This avoids ScanForEndstream which can find false "endstream" inside binary data.
        if (entry.Value.IsCompressed)
        {
            try
            {
                var obj = ResolveCompressedObject(entry.Value, objectNumber);
                return obj is PdfInteger pi ? pi.Value : -1;
            }
            catch
            {
                return -1;
            }
        }

        // Read directly from bytes at the xref offset (fast path for uncompressed objects)
        var offset = entry.Value.Offset;
        if (offset < 0 || offset >= _data.Length) return -1;

        // Quick scan: skip "N G obj" header, parse the integer
        var lexer = new PdfLexer(_data);
        lexer.Position = offset;
        // Skip obj num, gen, "obj"
        lexer.NextToken(); lexer.NextToken(); lexer.NextToken();
        var valToken = lexer.NextToken();
        if (valToken.Kind == TokenKind.Integer)
            return valToken.IntValue;
        return -1;
    }

    /// <summary>
    /// Return a stream dictionary with /Filter and /DecodeParms resolved from indirect refs.
    /// Returns the original dict if neither key is an indirect ref.
    /// </summary>
    private PdfDictionary ResolveStreamFilterDict(PdfDictionary dict)
    {
        var filterRaw = dict.Get("Filter");
        var filterResolved = Resolve(filterRaw);
        var decodeParmsRaw = dict.Get("DecodeParms");
        var decodeParmsResolved = ResolveDecodeParms(Resolve(decodeParmsRaw));

        // If neither changed (both non-indirect or both null), use the dict as-is.
        if (ReferenceEquals(filterResolved, filterRaw) && ReferenceEquals(decodeParmsResolved, decodeParmsRaw))
            return dict;

        var resolved = new PdfDictionary();
        foreach (var key in dict.Keys)
            resolved.Set(key, dict.Get(key)!);
        if (filterResolved is not null) resolved.Set("Filter", filterResolved);
        if (decodeParmsResolved is not null) resolved.Set("DecodeParms", decodeParmsResolved);
        return resolved;
    }

    /// <summary>
    /// Resolve indirect stream references nested inside DecodeParms so filters that read
    /// auxiliary streams (e.g. JBIG2Decode's /JBIG2Globals symbol dictionary) receive the
    /// stream object rather than an unresolved reference. Handles both the single-dict and
    /// per-filter array forms of /DecodeParms.
    /// </summary>
    private PdfObject? ResolveDecodeParms(PdfObject? parms)
    {
        if (parms is PdfDictionary d) return ResolveGlobalsInParm(d);
        if (parms is PdfArray arr)
        {
            var items = new List<PdfObject>(arr.Count);
            bool changed = false;
            foreach (var it in arr)
            {
                var r = Resolve(it);
                var rr = r is PdfDictionary pd ? ResolveGlobalsInParm(pd) : r;
                if (!ReferenceEquals(rr, it)) changed = true;
                items.Add(rr ?? it);
            }
            return changed ? new PdfArray(items) : parms;
        }
        return parms;
    }

    private PdfObject ResolveGlobalsInParm(PdfDictionary parm)
    {
        var g = parm.Get("JBIG2Globals");
        if (g is null || g is PdfStream) return parm; // already a stream or absent
        var gStream = ResolveStream(g);
        if (gStream is null) return parm;
        var copy = new PdfDictionary();
        foreach (var k in parm.Keys) copy.Set(k, parm.Get(k)!);
        copy.Set("JBIG2Globals", gStream);
        return copy;
    }

    /// <summary>
    /// Initialize the decryptor with a password (empty string for no-password / owner-only PDFs).
    /// </summary>
    internal void InitDecryptor(string password = "")
    {
        if (_decryptorInitialized) return;
        _decryptorInitialized = true;

        if (!IsEncrypted) return;

        // Resolve the Encrypt dict without triggering decryption init (avoid recursion).
        // We must set _decryptorInitialized = true BEFORE calling Resolve to prevent
        // infinite recursion (Resolve → InitDecryptor → Resolve → .).
        var encryptRef = Trailer.Get("Encrypt");
        PdfDictionary? encryptDict = null;
        if (encryptRef is PdfDictionary dict)
        {
            encryptDict = dict;
        }
        else if (encryptRef is PdfIndirectRef iref)
        {
            var entry = _xref.GetEntry(iref.ObjectNumber);
            if (entry is not null && entry.Value.InUse && !entry.Value.IsCompressed)
            {
                // Fast path: direct uncompressed object read without full resolve machinery
                _parser.Lexer.Position = entry.Value.Offset;
                var indirect = _parser.ParseIndirectObject();
                encryptDict = indirect.Value as PdfDictionary;
            }
            else
            {
                // Fallback: resolve normally (handles compressed objects in object streams).
                // Safe because _decryptorInitialized = true prevents re-entry.
                encryptDict = ResolveDict(encryptRef);
            }
        }

        if (encryptDict is null) return;

        // Public-key (certificate) security handler has no password: the Document
        // certificate constructor recovers the file key from a recipient envelope and
        // attaches the decryptor via AttachDecryptor. Leave decryption uninitialised
        // here (no password gating) so the open does not fail before that runs.
        if (encryptDict.GetName("Filter") is "Adobe.PPKLite" or "Adobe.PPKMS" or "Adobe.PubSec")
            return;

        // A caller-supplied handler takes over whenever it claims this document's
        // /Filter — the Standard handler cannot read an alternative one's /O and /U.
        if (_options.CustomSecurityHandler is { } custom
            && encryptDict.GetName("Filter") == custom.Filter)
        {
            _decryptor = PdfDecryptor.CreateWithCustomHandler(custom, encryptDict, password);
            if (_decryptor is null)
                throw new InvalidPasswordException("Incorrect password for encrypted PDF document.");
            return;
        }

        var fileId = Trailer.Get("ID") as PdfArray;

        // V≥5 (AES-256) requires the correct password to open; V1–4 is lenient (empty-
        // password failure is silently ignored so IsEncrypted can be read without a key).
        var encryptVersion = (int)encryptDict.GetInt("V");
        var isModernEncryption = encryptVersion >= 5;

        var malformedEncrypt = false;
        try
        {
            _decryptor = PdfDecryptor.TryCreate(encryptDict, fileId, password);
        }
        catch
        {
            // If creation fails (e.g., malformed encryption dict), continue without decryption
            _decryptor = null;
            malformedEncrypt = true;
        }

        // If TryCreate returned null (password verification failed, not malformed dict):
        if (_decryptor is null && !malformedEncrypt)
        {
            if (password.Length > 0)
                // Explicit password was wrong
                throw new InvalidPasswordException("Incorrect password for encrypted PDF document.");
            else if (isModernEncryption)
                // AES-256 (V5/R5/R6): strict — always require password
                throw new InvalidPasswordException("The document is password protected. A password is required to open this document.");
            // V1–V4 with empty password: lenient here — the reader stays usable so
            // metadata surfaces (IsEncrypted etc.) work without a key — but flag the
            // failed authentication so Document construction can refuse the open
            // (the document has a real user password; strings/streams would decode
            // to garbage).
            RequiresPassword = true;
        }
    }

    /// <summary>True when the document is encrypted with a non-empty user password and no
    /// (or an empty) password was supplied: object structure is readable but strings and
    /// streams cannot be decrypted. Set by <see cref="InitDecryptor"/>'s lenient V1–V4
    /// branch; <see cref="Document"/> construction turns it into
    /// <see cref="InvalidPasswordException"/>.</summary>
    internal bool RequiresPassword { get; private set; }

    /// <summary>
    /// Try to initialize decryption with empty password (for PDFs with owner-only protection).
    /// Called automatically on first object resolution if the document is encrypted.
    /// </summary>
    private void EnsureDecryptorInitialized()
    {
        if (_decryptorInitialized) return;
        InitDecryptor("");
    }

    /// <summary>Register an in-memory object that can be resolved by object number.</summary>
    internal void RegisterOverlayObject(int objectNumber, PdfObject obj)
    {
        _overlayObjects[objectNumber] = obj;
    }

    private PdfObject? ResolveRef(int objectNumber, int generation)
    {
        var key = (objectNumber, generation);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // Check in-memory overlay objects first
        if (_overlayObjects.TryGetValue(objectNumber, out var overlay))
            return overlay;

        // Auto-init decryptor on first access
        EnsureDecryptorInitialized();

        var entry = _xref.GetEntry(objectNumber);
        if (entry is null || !entry.Value.InUse)
        {
            if (!_options.LenientMode)
            {
                if (entry is null)
                    throw new InvalidOperationException(
                        $"Object {objectNumber} {generation} not found in xref table");
            }
            return null;
        }

        PdfObject? result;

        if (entry.Value.IsCompressed)
        {
            result = ResolveCompressedObject(entry.Value, objectNumber);
        }
        else
        {
            result = ResolveUncompressedObject(entry.Value, objectNumber, generation);
            if (result is null) return null;

            // Decrypt strings within the resolved object
            if (_decryptor is not null)
            {
                result = DecryptObject(result, objectNumber, generation);
            }
        }

        if (result is not null)
            _cache[key] = result;
        return result;
    }

    private PdfObject? ResolveUncompressedObject(XRefEntry entry, int objectNumber, int generation)
    {
        try
        {
            _parser.Lexer.Position = entry.Offset;
            var indirect = _parser.ParseIndirectObject();

            // If the parsed object number doesn't match, try scanning nearby
            // for the correct header (handles shifted xref offsets)
            if (indirect.ObjectNumber != objectNumber)
            {
                var correctedOffset = FindObjectOffset(entry.Offset, objectNumber, generation);
                if (correctedOffset != entry.Offset)
                {
                    _parser.Lexer.Position = correctedOffset;
                    indirect = _parser.ParseIndirectObject();
                }
            }

            return indirect.Value;
        }
        catch (Exception ex)
        {
            if (_options.LenientMode)
            {
                // Try scanning nearby for the object header
                var result = ScanForObject(objectNumber, entry.Offset);
                if (result is not null) return result;

                // In lenient mode, skip malformed objects
                return null;
            }

            throw new InvalidOperationException(
                $"Failed to parse object {objectNumber} {generation} at offset {entry.Offset}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Find the actual file offset where "objNum genNum obj" starts.
    /// If the reported offset already points to the correct header, returns it.
    /// Otherwise scans forward/backward up to 512 bytes to find the header.
    /// Modelled on the equivalent TypeScript findObjectOffset() routine.
    /// </summary>
    private long FindObjectOffset(long reported, int objNum, int genNum)
    {
        var target = Encoding.ASCII.GetBytes($"{objNum} {genNum} obj");

        // Fast path: check if reported offset already starts with the expected header
        if (reported >= 0 && reported + target.Length <= _data.Length)
        {
            var match = true;
            for (var i = 0; i < target.Length && match; i++)
                if (_data[reported + i] != target[i]) match = false;
            // Also verify "obj" is followed by whitespace or '<<' (not e.g. "object")
            if (match)
            {
                var afterObj = reported + target.Length;
                if (afterObj >= _data.Length || _data[afterObj] <= 32 || _data[afterObj] == '<')
                    return reported;
            }
        }

        // Slow path: scan forward up to 512 bytes
        var end = Math.Min(_data.Length - target.Length, reported + 512);
        for (var i = reported; i < end; i++)
        {
            if (_data[i] != target[0]) continue;
            var found = true;
            for (var j = 1; j < target.Length && found; j++)
                if (_data[i + j] != target[j]) found = false;
            if (!found) continue;
            // Verify "obj" is followed by whitespace/delimiter and preceded by newline or start
            var afterObj = i + target.Length;
            if (afterObj < _data.Length && _data[afterObj] > 32 && _data[afterObj] != '<') continue;
            if (i > 0 && _data[i - 1] != '\n' && _data[i - 1] != '\r' && _data[i - 1] != ' ' && i != 0) continue;
            return i;
        }

        // Scan backward up to 512 bytes
        var start = Math.Max(0, reported - 512);
        for (var i = reported - 1; i >= start; i--)
        {
            if (_data[i] != target[0]) continue;
            var found = true;
            for (var j = 1; j < target.Length && found; j++)
                if (_data[i + j] != target[j]) found = false;
            if (!found) continue;
            var afterObj = i + target.Length;
            if (afterObj < _data.Length && _data[afterObj] > 32 && _data[afterObj] != '<') continue;
            if (i > 0 && _data[i - 1] != '\n' && _data[i - 1] != '\r' && _data[i - 1] != ' ') continue;
            return i;
        }

        // No match — use reported offset as-is
        return reported;
    }

    /// <summary>
    /// Recursively decrypt PdfString values within an object tree.
    /// Does NOT decrypt the /Encrypt dictionary itself.
    /// </summary>
    private PdfObject DecryptObject(PdfObject obj, int objectNumber, int generation)
    {
        // Don't decrypt the Encrypt dictionary
        var encryptRef = Trailer.Get("Encrypt");
        if (encryptRef is PdfIndirectRef eRef && eRef.ObjectNumber == objectNumber)
            return obj;

        return DecryptObjectInner(obj, objectNumber, generation);
    }

    private PdfObject DecryptObjectInner(PdfObject obj, int objectNumber, int generation)
    {
        switch (obj)
        {
            case PdfString str:
                var decrypted = _decryptor!.DecryptString(str.Value, objectNumber, generation);
                return new PdfString(decrypted, str.IsHex);

            case PdfDictionary dict:
                foreach (var key in dict.Keys.ToArray())
                {
                    var val = dict.Get(key);
                    if (val is not null)
                    {
                        var newVal = DecryptObjectInner(val, objectNumber, generation);
                        if (!ReferenceEquals(val, newVal))
                            dict.Set(key, newVal);
                    }
                }
                return dict;

            case PdfArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var val = arr[i];
                    var newVal = DecryptObjectInner(val, objectNumber, generation);
                    if (!ReferenceEquals(val, newVal))
                        arr.ReplaceAt(i, newVal);
                }
                return arr;

            case PdfStream stream:
                // Decrypt the stream dict (strings inside it), but not the stream data itself
                // Stream data is decrypted in DecodeStream when accessed
                DecryptObjectInner(stream.Dict, objectNumber, generation);
                // Store the object/gen for later stream decryption
                stream.ObjectNumber = objectNumber;
                stream.Generation = generation;
                return stream;

            default:
                return obj;
        }
    }

    /// <summary>
    /// Resolve an object stored in a compressed object stream (ObjStm, type 2 xref entry).
    /// Caches the entire parsed ObjStm so repeated lookups are efficient.
    /// </summary>
    private PdfObject ResolveCompressedObject(XRefEntry entry, int objectNumber)
    {
        // Check if we've already parsed this ObjStm
        if (_objStmCache.TryGetValue(entry.StreamObjectNumber, out var cachedObjects))
        {
            if (entry.IndexInStream < cachedObjects.Length)
                return cachedObjects[entry.IndexInStream];

            throw new InvalidOperationException(
                $"Object {objectNumber} references index {entry.IndexInStream} in object stream " +
                $"{entry.StreamObjectNumber}, but stream only contains {cachedObjects.Length} objects");
        }

        // The object is in an object stream — resolve and parse the whole stream
        var streamObj = ResolveRef(entry.StreamObjectNumber, 0);
        if (streamObj is not PdfStream objStream)
            throw new InvalidOperationException(
                $"Object stream {entry.StreamObjectNumber} not found or not a stream " +
                $"(needed to resolve compressed object {objectNumber})");

        var decodedData = DecodeStream(objStream, entry.StreamObjectNumber, 0);
        var n = (int)objStream.Dict.GetInt("N");      // number of objects in the stream
        var first = (int)objStream.Dict.GetInt("First"); // byte offset of first object in stream

        // Parse the index: N pairs of (objNum, offset)
        var indexParser = new PdfParser(decodedData);
        var offsets = new (int objNum, int offset)[n];
        for (var i = 0; i < n; i++)
        {
            var numToken = indexParser.Lexer.NextToken();
            var offsetToken = indexParser.Lexer.NextToken();
            offsets[i] = ((int)numToken.IntValue, (int)offsetToken.IntValue);
        }

        // Parse ALL objects in the stream and cache them
        var parsedObjects = new PdfObject[n];
        for (var i = 0; i < n; i++)
        {
            var targetOffset = first + offsets[i].offset;
            try
            {
                parsedObjects[i] = indexParser.ParseObjectAt(targetOffset);
            }
            catch (Exception ex)
            {
                if (_options.LenientMode)
                {
                    parsedObjects[i] = PdfNull.Instance;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Failed to parse object at index {i} (objNum={offsets[i].objNum}) " +
                        $"in object stream {entry.StreamObjectNumber}: {ex.Message}", ex);
                }
            }
        }

        _objStmCache[entry.StreamObjectNumber] = parsedObjects;

        if (entry.IndexInStream >= parsedObjects.Length)
        {
            throw new InvalidOperationException(
                $"Object {objectNumber} references index {entry.IndexInStream} in object stream " +
                $"{entry.StreamObjectNumber}, but stream only contains {parsedObjects.Length} objects");
        }

        return parsedObjects[entry.IndexInStream];
    }

    /// <summary>
    /// Scan nearby bytes for an object header pattern "N G obj" when the xref offset is wrong.
    /// Searches +/-1024 bytes around the expected offset.
    /// </summary>
    private PdfObject? ScanForObject(int objectNumber, long expectedOffset)
    {
        var searchRadius = 1024;
        var startPos = Math.Max(0, expectedOffset - searchRadius);
        var endPos = Math.Min(_data.Length, expectedOffset + searchRadius);

        // Build the pattern to search for: "objectNumber 0 obj"
        var pattern = Encoding.ASCII.GetBytes($"{objectNumber} 0 obj");

        for (var pos = startPos; pos + pattern.Length < endPos; pos++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (_data[pos + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match) continue;

            // Verify it's preceded by whitespace or start of data
            if (pos > 0 && !IsWhitespace(_data[pos - 1])) continue;

            try
            {
                _parser.Lexer.Position = pos;
                var indirect = _parser.ParseIndirectObject();
                if (indirect.ObjectNumber == objectNumber)
                    return indirect.Value;
            }
            catch
            {
                // Continue scanning
            }
        }

        return null;
    }

    /// <summary>
    /// Recover xref table by scanning the entire file for "N G obj" object headers.
    /// Used as a fallback when normal xref parsing fails.
    /// </summary>
    internal static XRefTable RecoverXref(byte[] data, Exception? originalException = null)
    {
        var table = new XRefTable();
        var text = data;
        var maxObjNum = 0;

        // Scan for object headers: digits whitespace digits whitespace "obj"
        for (long pos = 0; pos < text.Length - 5; pos++)
        {
            if (!IsDigit(text[pos])) continue;

            // Check if preceded by whitespace or start of file
            if (pos > 0 && !IsWhitespace(text[pos - 1])) continue;

            // Parse object number
            var numStart = pos;
            while (pos < text.Length && IsDigit(text[pos])) pos++;
            if (pos >= text.Length || !IsWhitespace(text[pos])) continue;

            var objNumStr = Encoding.ASCII.GetString(text, (int)numStart, (int)(pos - numStart));
            if (!int.TryParse(objNumStr, out var objNum)) continue;

            // Skip whitespace
            while (pos < text.Length && IsWhitespace(text[pos])) pos++;

            // Parse generation number
            var genStart = pos;
            while (pos < text.Length && IsDigit(text[pos])) pos++;
            if (pos >= text.Length || !IsWhitespace(text[pos])) continue;

            var genStr = Encoding.ASCII.GetString(text, (int)genStart, (int)(pos - genStart));
            if (!int.TryParse(genStr, out var gen)) continue;

            // Skip whitespace
            while (pos < text.Length && IsWhitespace(text[pos])) pos++;

            // Check for "obj" keyword
            if (pos + 3 > text.Length) continue;
            if (text[pos] != 'o' || text[pos + 1] != 'b' || text[pos + 2] != 'j') continue;

            // Verify followed by whitespace or delimiter
            var afterObj = pos + 3;
            if (afterObj < text.Length && !IsWhitespace(text[afterObj]) && !IsDelimiter(text[afterObj]))
                continue;

            // Use SetEntry (last occurrence wins) to handle incremental updates:
            // later object definitions supersede earlier ones.
            table.SetEntry(objNum, new XRefEntry
            {
                ObjectNumber = objNum,
                Generation = gen,
                Offset = numStart,
                InUse = true
            });

            if (objNum > maxObjNum) maxObjNum = objNum;

            // Reset pos to just after "obj" so we continue scanning
            pos = afterObj;
        }

        if (table.Entries.Count == 0)
        {
            // No object headers found in the body — the file looked like a PDF
            // (header + startxref) but has no recoverable structure. Surface as
            // the standard "Trailer not found" InvalidPdfFileFormatException so
            // callers can pattern-match on the typed exception.
            throw new Aspose.Pdf.InvalidPdfFileFormatException("Trailer not found");
        }

        // Extract compressed objects from object streams (ObjStm).
        // Needed for linearized PDFs where Pages/Catalog dicts are in ObjStm.
        ExtractObjectStreams(data, table);

        // Build a synthetic trailer by finding the Catalog object
        table.BuildSyntheticTrailer(data, maxObjNum + 1);

        return table;
    }

    /// <summary>
    /// Extract compressed objects from ObjStm streams found during recovery.
    /// This is needed for linearized PDFs where /Pages, /Catalog, etc. are inside
    /// object streams.
    /// </summary>
    private static void ExtractObjectStreams(byte[] data, XRefTable table)
    {
        // Find ObjStm entries in the current xref
        var objStmEntries = new List<(int objNum, XRefEntry entry)>();
        foreach (var kvp in table.Entries)
        {
            if (!kvp.Value.InUse || kvp.Value.IsCompressed) continue;
            try
            {
                var parser = new PdfParser(data);
                parser.Lexer.Position = kvp.Value.Offset;
                var indirect = parser.ParseIndirectObject();
                if (indirect.Value is PdfStream stream && stream.Dict.GetName("Type") == "ObjStm")
                {
                    objStmEntries.Add((kvp.Key, kvp.Value));
                }
            }
            catch { /* skip unparseable */ }
        }

        foreach (var (stmObjNum, stmEntry) in objStmEntries)
        {
            try
            {
                var parser = new PdfParser(data);
                parser.Lexer.Position = stmEntry.Offset;
                var indirect = parser.ParseIndirectObject();
                if (indirect.Value is not PdfStream stream) continue;

                var n = (int)(stream.Dict.Get("N") is PdfInteger ni ? ni.Value : 0);
                var first = (int)(stream.Dict.Get("First") is PdfInteger fi ? fi.Value : 0);
                if (n <= 0 || first <= 0) continue;

                // Decode the stream
                var decoded = Filters.StreamFilter.Decode(stream.RawData, stream.Dict);

                // Parse the header: N pairs of (objNum offset)
                var headerParser = new PdfParser(decoded);
                var pairs = new List<(int objNum, int offset)>();
                for (var i = 0; i < n; i++)
                {
                    var objNumTok = headerParser.Lexer.NextToken();
                    var offsetTok = headerParser.Lexer.NextToken();
                    if (objNumTok.Kind != TokenKind.Integer || offsetTok.Kind != TokenKind.Integer) break;
                    pairs.Add(((int)objNumTok.IntValue, (int)offsetTok.IntValue));
                }

                // Add entries for compressed objects
                foreach (var (objNum, _) in pairs)
                {
                    table.AddEntry(objNum, new XRefEntry
                    {
                        ObjectNumber = objNum,
                        InUse = true,
                        IsCompressed = true,
                        StreamObjectNumber = stmObjNum,
                        IndexInStream = pairs.FindIndex(p => p.objNum == objNum)
                    });
                }
            }
            catch { /* skip malformed ObjStm */ }
        }
    }

    /// <summary>
    /// Detect whether this PDF is linearized by checking for a linearization dictionary
    /// in the first indirect object in the file (the one at the lowest byte offset).
    /// Per PDF spec, a linearized PDF has a dictionary with /Linearized key as the very
    /// first object in the body.
    /// </summary>
    private void DetectLinearization()
    {
        try
        {
            // Find the object with the lowest file offset — this is the first object in the body.
            long minOffset = long.MaxValue;
            int firstObjNum = -1;
            int firstGen = 0;
            foreach (var kvp in _xref.Entries)
            {
                var entry = kvp.Value;
                if (!entry.InUse || entry.IsCompressed) continue;
                if (entry.Offset < minOffset)
                {
                    minOffset = entry.Offset;
                    firstObjNum = kvp.Key;
                    firstGen = entry.Generation;
                }
            }

            if (firstObjNum < 0) return;

            // Parse the first object without caching — we just need to peek at it.
            _parser.Lexer.Position = minOffset;
            var indirect = _parser.ParseIndirectObject();
            if (indirect.Value is PdfDictionary dict && dict.ContainsKey("Linearized"))
            {
                // Validate: /L must match actual file size (PDF spec §F.2).
                // If it doesn't, the file was modified after linearization and is no longer valid.
                var declaredLength = dict.GetInt("L", -1);
                IsLinearized = declaredLength < 0 || declaredLength == _data.Length;
            }
        }
        catch
        {
            // If we can't parse the first object, it's not linearized (or corrupt).
            IsLinearized = false;
        }
    }

    /// <summary>
    /// Handle hybrid xref: when a traditional trailer contains /XRefStm,
    /// merge entries from the supplementary cross-reference stream.
    /// </summary>
    private void HandleHybridXref()
    {
        var xrefStmOffset = _xref.Trailer.GetInt("XRefStm", -1);
        if (xrefStmOffset < 0) return;

        try
        {
            _xref.MergeXrefStreamAt(_data, xrefStmOffset);
        }
        catch
        {
            if (!_options.LenientMode)
                throw;
            // In lenient mode, ignore failure to read supplementary xref stream
        }
    }

    private static bool IsWhitespace(byte b) =>
        b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\0' || b == '\f';

    private static bool IsDigit(byte b) =>
        b >= '0' && b <= '9';

    private static bool IsDelimiter(byte b) =>
        b == '(' || b == ')' || b == '<' || b == '>' ||
        b == '[' || b == ']' || b == '{' || b == '}' ||
        b == '/' || b == '%';
}
