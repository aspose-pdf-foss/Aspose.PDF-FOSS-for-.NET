using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.IO;

internal sealed partial class PdfReader
{
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
}
