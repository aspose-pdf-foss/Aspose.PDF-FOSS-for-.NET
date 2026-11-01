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
        if (_decoded.TryGetValue(stream, out var known) && known.Matches(stream, objectNumber, generation))
            return (byte[])known.Data.Clone();
        var data = StreamFilter.Decode(DecryptedRawData(stream, objectNumber, generation), ResolveStreamFilterDict(stream.Dict));
        Remember(stream, objectNumber, generation, data);
        return data;
    }

    // Decoded content by stream, kept while the stream's data and filter entries are the same
    // instances (a rewrite replaces the data array, a filter edit the entry): content and
    // resources are decoded over and over - a form once per page, a Type 3 glyph at every
    // showing, an XFA part once per field. Callers get a copy, so what they do to it stays
    // theirs. Streams up to 16 MB (a form of ten million operators, drawn on every page,
    // is one), and the whole cache goes once it holds 128 MB, so a document of large
    // images is never held twice.
    private const int MaxCachedDecodedLength = 16 << 20;
    private const int MaxDecodedCacheBytes = 128 << 20;
    private readonly Dictionary<PdfStream, DecodedStream> _decoded = new(ReferenceEqualityComparer.Instance);
    private int _decodedCacheBytes;

    private sealed record DecodedStream(byte[] Raw, PdfObject? Filter, PdfObject? Parms, int ObjectNumber, int Generation, byte[] Data)
    {
        public bool Matches(PdfStream stream, int objectNumber, int generation) =>
            ReferenceEquals(Raw, stream.RawData) && ReferenceEquals(Filter, stream.Dict.Get("Filter"))
            && ReferenceEquals(Parms, stream.Dict.Get("DecodeParms"))
            && ObjectNumber == objectNumber && Generation == generation;
    }

    /// <summary>The decoded content of <paramref name="stream"/> when the cache holds the decode
    /// <see cref="DecodeStream"/> makes of it without an object number; null otherwise.</summary>
    internal byte[]? DecodedIfCached(PdfStream stream) =>
        _decoded.TryGetValue(stream, out var known) && known.Matches(stream, 0, 0) ? (byte[])known.Data.Clone() : null;

    private void Remember(PdfStream stream, int objectNumber, int generation, byte[] data)
    {
        if (data.Length > MaxCachedDecodedLength) return;
        if (_decodedCacheBytes + data.Length > MaxDecodedCacheBytes) ForgetDecoded();
        if (_decoded.TryGetValue(stream, out var old)) _decodedCacheBytes -= old.Data.Length;
        _decoded[stream] = new DecodedStream(stream.RawData, stream.Dict.Get("Filter"), stream.Dict.Get("DecodeParms"),
            objectNumber, generation, (byte[])data.Clone());
        _decodedCacheBytes += data.Length;
    }

    private void ForgetDecoded()
    {
        _decoded.Clear();
        _decodedCacheBytes = 0;
    }

    /// <summary>
    /// Decode a stream as <see cref="DecodeStream"/> does, but with every predictor's
    /// /Colors replaced by <paramref name="predictorColors"/>. The replacement lives on a
    /// copy of the filter dictionary: the document's own objects are not touched, so a
    /// later save writes the stream's declared parameters unchanged.
    /// </summary>
    internal byte[] DecodeStreamWithPredictorColors(PdfStream stream, int predictorColors)
        => StreamFilter.Decode(DecryptedRawData(stream, 0, 0),
            WithPredictorColors(ResolveStreamFilterDict(stream.Dict), predictorColors));

    private static PdfDictionary WithPredictorColors(PdfDictionary filterDict, int predictorColors)
    {
        var copy = new PdfDictionary();
        foreach (var key in filterDict.Keys) copy.Set(key, filterDict.Get(key)!);
        switch (filterDict.Get("DecodeParms"))
        {
            case PdfDictionary parms:
                copy.Set("DecodeParms", PredictorParmsWithColors(parms, predictorColors));
                break;
            case PdfArray arr:
                var items = new PdfArray();
                foreach (var it in arr)
                    items.Add(it is PdfDictionary parms ? PredictorParmsWithColors(parms, predictorColors) : it);
                copy.Set("DecodeParms", items);
                break;
        }
        return copy;
    }

    private static PdfObject PredictorParmsWithColors(PdfDictionary parms, int predictorColors)
    {
        if (parms.GetInt("Predictor", 1) <= 1) return parms;
        var copy = new PdfDictionary();
        foreach (var key in parms.Keys) copy.Set(key, parms.Get(key)!);
        copy.Set("Colors", new PdfInteger(predictorColors));
        return copy;
    }

    /// <summary>The stream's raw bytes - still filtered - with the document's encryption removed: what a copy
    /// of the stream into another document holds. The stream itself is left as it is.</summary>
    internal byte[] PlaintextRawData(PdfStream stream) => DecryptedRawData(stream, 0, 0);

    /// <summary>The stream's raw bytes with the document's encryption removed.</summary>
    private byte[] DecryptedRawData(PdfStream stream, int objectNumber, int generation)
    {
        var data = stream.RawData;

        // Use stored object number if not explicitly provided
        if (objectNumber == 0 && stream.ObjectNumber > 0)
        {
            objectNumber = stream.ObjectNumber;
            generation = stream.Generation;
        }

        // Decrypt stream if needed (but not XRef streams or the Encrypt dict); replaced data
        // is what a writer put there, in plaintext, never the file's ciphertext.
        if (_decryptor is not null && objectNumber > 0 && !stream.DataReplaced)
        {
            // Check for /Crypt filter — if present, the crypt filter name overrides the default
            string? cryptFilterName = null;
            var filterObj = stream.Dict.Get("Filter");
            if (filterObj is PdfName filterName && filterName.Value == "Crypt")
            {
                var dp = stream.Dict.Get("DecodeParms") as PdfDictionary;
                cryptFilterName = dp?.GetName("Name") ?? "Identity";
            }
            // Check if first filter is Crypt
            else if (filterObj is PdfArray filterArr && filterArr.Count > 0 && filterArr[0] is PdfName fn && fn.Value == "Crypt")
            {
                var dpArr = stream.Dict.Get("DecodeParms") as PdfArray;
                if (dpArr is { Count: > 0 } && dpArr[0] is PdfDictionary dp)
                    cryptFilterName = dp.GetName("Name") ?? "Identity";
                else
                    cryptFilterName = "Identity";
            }

            data = _decryptor.DecryptStream(data, objectNumber, generation, cryptFilterName);
        }

        return data;
    }

    /// <summary>Decode at most <paramref name="maxBytes"/> of a stream's leading content
    /// without materialising the whole payload (see <see cref="StreamFilter.DecodePrefix"/>).
    /// Used to sniff a large embedded-file header cheaply. Decryption still runs in full —
    /// the cipher operates on the (typically small) raw stream bytes, not the decoded output.</summary>
    public byte[] DecodeStreamPrefix(PdfStream stream, int maxBytes, int objectNumber = 0, int generation = 0)
    {
        var data = DecryptedRawData(stream, objectNumber, generation);
        return StreamFilter.DecodePrefix(data, ResolveStreamFilterDict(stream.Dict), maxBytes);
    }

    /// <summary>
    /// Decrypt every in-use stream's RawData in place (data a writer replaced is plaintext
    /// already), then forget the decryptor. Call this before an encrypted-input document is
    /// written whole — otherwise the writer would copy still-encrypted bytes into a trailer
    /// with no /Encrypt, or encrypt them a second time.
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
            if (obj is not PdfStream s || s.DataReplaced) continue;

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
        {
            UndecryptableFilter = encryptDict.GetName("Filter");
            return;
        }

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
            // A dictionary of another handler (a rights-management /Filter) is not a malformed
            // Standard one: nothing in the file can be decrypted.
            if (encryptDict.GetName("Filter") is { } filter && filter != StandardFilter)
                UndecryptableFilter = filter;
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

    /// <summary>The /Filter of an encryption no password opens: a public-key handler, which only a
    /// certificate decrypts (<see cref="AttachDecryptor"/>), or a handler the library does not have.
    /// Null when the file is not encrypted or a password decryptor is in place.</summary>
    internal string? UndecryptableFilter { get; private set; }

    /// <summary>The password security handler's /Filter name.</summary>
    private const string StandardFilter = "Standard";

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
