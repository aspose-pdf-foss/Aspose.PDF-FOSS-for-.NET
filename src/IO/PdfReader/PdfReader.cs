using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.IO;

internal sealed partial class PdfReader
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

    private XRefTable? _declaredXref;
    private bool _declaredXrefProbed;

    /// <summary>Whether <paramref name="objNum"/> was reachable through the file's
    /// DECLARED cross-reference table — i.e. a strictly xref-driven loader (no scan
    /// recovery) would find the object's header at the declared offset. Always true
    /// when this reader's xref parsed normally, and when no declared classic table
    /// can be re-read at all; only a scan-recovered reader with a readable declared
    /// table reports false for its broken entries. The cross-document merge uses
    /// this to apply its strict-xref merge semantics.</summary>
    internal bool IsObjectDeclaredReachable(int objNum)
    {
        if (!_xref.RecoveredByScan || objNum <= 0) return true;
        if (!_declaredXrefProbed)
        {
            _declaredXrefProbed = true;
            try { _declaredXref = XRefTable.ReadLastDeclaredClassic(_data); } catch { }
        }
        if (_declaredXref is null) return true;
        if (_declaredXref.GetEntry(objNum) is not { InUse: true, IsCompressed: false } entry)
            return false;
        var target = Encoding.ASCII.GetBytes($"{objNum} {entry.Generation} obj");
        var off = entry.Offset;
        if (off < 0 || off + target.Length > _data.Length) return false;
        for (var i = 0; i < target.Length; i++)
            if (_data[off + i] != target[i]) return false;
        var after = off + target.Length;
        return after >= _data.Length || _data[after] <= 32 || _data[after] == '<'
            || _data[after] == '[' || _data[after] == '/';
    }

    /// <summary>Set when an in-place edit (e.g. replacing an image's data) may have left the
    /// original object orphaned. Signals the full-rewrite save to run a reachability pass so
    /// the superseded object is not written out (otherwise a load-replace-save keeps both the
    /// old and new image and the file never shrinks).</summary>
    internal bool MayHaveOrphansOnSave { get; set; }

    /// <summary>The linearization parameter dictionary and primary hint stream this file was
    /// loaded with. A save writes a fresh pair, so these are dropped rather than copied.</summary>
    internal HashSet<int> LinearizationInfraObjects { get; } = new();

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
                    _xref.RecoveredByScan = true;
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
                            _xref.RecoveredByScan = true;
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

    private static bool IsWhitespace(byte b) =>
        b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\0' || b == '\f';

    private static bool IsDigit(byte b) =>
        b >= '0' && b <= '9';

    private static bool IsDelimiter(byte b) =>
        b == '(' || b == ')' || b == '<' || b == '>' ||
        b == '[' || b == ']' || b == '{' || b == '}' ||
        b == '/' || b == '%';
}
