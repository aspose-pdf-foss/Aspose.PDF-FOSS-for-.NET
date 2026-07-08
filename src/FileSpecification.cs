using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Annotations;

namespace Aspose.Pdf;

/// <summary>
/// Represents an embedded file specification (PDF §7.11.3).
/// Can be created from an existing PDF dictionary or from a file path for adding new attachments.
/// </summary>
public sealed class FileSpecification : IDisposable
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader? _reader;
    // Raw file data for newly created specs (before being written to the PDF)
    private byte[]? _pendingData;
    // Source-file timestamps captured at construction (from a file path or a
    // FileStream's backing file) so the embedded stream's /Params records the
    // file's CreationDate / ModDate, not just its Size.
    private DateTime? _pendingCreationDate;
    private DateTime? _pendingModDate;

    /// <summary>Source-file creation time captured for a new attachment, or null.</summary>
    internal DateTime? PendingCreationDate => _pendingCreationDate;
    /// <summary>Source-file last-write time captured for a new attachment, or null.</summary>
    internal DateTime? PendingModDate => _pendingModDate;

    /// <summary>Capture CreationDate/ModDate from a file path if it exists.</summary>
    private void CaptureFileDates(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
            {
                _pendingCreationDate = File.GetCreationTime(path);
                _pendingModDate = File.GetLastWriteTime(path);
            }
        }
        catch { /* permission / IO — leave timestamps unset */ }
    }

    internal FileSpecification(PdfDictionary dict, PdfReader? reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>
    /// Construct a FileSpecification wrapping an existing /Filespec dictionary.
    /// Used when an action points at a file spec already stored in the PDF.
    /// </summary>
    internal static FileSpecification FromExistingDict(PdfDictionary dict, PdfReader? reader)
        => new FileSpecification(dict, reader);

    /// <summary>
    /// Construct a minimal FileSpecification from just a file name string.
    /// Used when an action's /F entry is a literal string rather than a dict.
    /// </summary>
    internal static FileSpecification CreateFromName(string name)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Filespec"));
        // Keep non-Latin1 path characters intact (Latin1 would flatten them to '?').
        dict.Set("F", Forms.Field.EncodePdfTextString(name));
        return new FileSpecification(dict, null);
    }

    /// <summary>Empty file specification with no backing file or stream.
    /// Useful as a builder target — callers populate properties (Name,
    /// MIMEType, etc.) before adding the spec to a document.</summary>
    public FileSpecification()
    {
        _reader = null;
        _dict = new PdfDictionary();
        _dict.Set("Type", new PdfName("Filespec"));
    }

    /// <summary>
    /// Create a new file specification for embedding a file (no description).
    /// </summary>
    public FileSpecification(string file) : this(file, "") { }

    /// <summary>
    /// Create a new file specification for embedding a file.
    /// The file data is read immediately and stored until the document is saved.
    /// </summary>
    /// <param name="file">Path to the file to embed.</param>
    /// <param name="description">Human-readable description of the attachment.</param>
    public FileSpecification(string file, string description)
    {
        _reader = null;

        // An existing local file is embedded under its display (base) name. A path
        // that does not resolve to a file is still a valid *reference* — e.g.
        // GoToRemoteAction.File pointing at an external document — so keep the path
        // verbatim and don't try to read it (which would throw):
        // FileSpecification(path) never reads eagerly.
        string fileName;
        if (File.Exists(file))
        {
            _pendingData = File.ReadAllBytes(file);
            CaptureFileDates(file);
            fileName = Path.GetFileName(file);
        }
        else
        {
            fileName = file;
        }

        _dict = new PdfDictionary();
        _dict.Set("Type", new PdfName("Filespec"));
        // /F uses Latin1 (lossy for non-ASCII); /UF uses UTF-16BE with BOM
        _dict.Set("F", new PdfString(System.Text.Encoding.Latin1.GetBytes(fileName)));
        _dict.Set("UF", Forms.Field.EncodePdfTextString(fileName));
        _dict.Set("Desc", Forms.Field.EncodePdfTextString(description));
    }

    /// <summary>Create a file specification linked to a file-attachment
    /// <paramref name="annot"/>. The annotation's existing /FS entry (if
    /// any) is wrapped; otherwise a minimal /Filespec dict is created.</summary>
    public FileSpecification(string fileName, Aspose.Pdf.Annotations.Annotation annot)
    {
        _reader = null;
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Filespec"));
        dict.Set("F", new PdfString(System.Text.Encoding.Latin1.GetBytes(fileName ?? "")));
        dict.Set("UF", Forms.Field.EncodePdfTextString(fileName ?? ""));
        _dict = dict;
        if (annot is not null)
            annot.Dict.Set("FS", _dict);
    }

    /// <summary>
    /// Create a new file specification for embedding a stream (no description).
    /// </summary>
    public FileSpecification(Stream stream, string name)
        : this(stream, name, string.Empty) { }

    /// <summary>
    /// Create a new file specification for embedding a stream.
    /// </summary>
    /// <param name="stream">Stream containing the file data.</param>
    /// <param name="name">File name for the attachment.</param>
    /// <param name="description">Human-readable description of the attachment.</param>
    public FileSpecification(Stream stream, string name, string description)
    {
        _reader = null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _pendingData = ms.ToArray();
        // A FileStream exposes its backing path, so the file's timestamps can be
        // captured even when embedding via the stream overload.
        if (stream is FileStream fileStream) CaptureFileDates(fileStream.Name);

        // The /F file name is a leaf name, not a path: callers (and the test inputs)
        // pass a full path here, but the embedded entry — like the file-path ctor and
        // Aspose.Pdf — stores just the file name. This also keeps the name-tree key
        // portable (an absolute "E:\..." key would mis-sort against plain file names).
        var leafName = Path.GetFileName(name);
        if (string.IsNullOrEmpty(leafName)) leafName = name;

        _dict = new PdfDictionary();
        _dict.Set("Type", new PdfName("Filespec"));
        _dict.Set("F", new PdfString(System.Text.Encoding.Latin1.GetBytes(leafName)));
        _dict.Set("UF", Forms.Field.EncodePdfTextString(leafName));
        if (!string.IsNullOrEmpty(description))
            _dict.Set("Desc", Forms.Field.EncodePdfTextString(description));
    }

    /// <summary>The backing PDF dictionary.</summary>
    internal PdfDictionary Dict => _dict;

    /// <summary>Build this spec's own /EF embedded-file stream from pending data
    /// so the bytes persist when the dict is referenced directly (e.g. from an
    /// annotation's /FS entry) rather than added to the document name tree. The
    /// writer promotes the inline stream to an indirect object on save.</summary>
    internal void MaterializeEmbeddedStream()
    {
        if (_pendingData is null) return;
        var fileStreamDict = new PdfDictionary();
        fileStreamDict.Set("Type", new PdfName("EmbeddedFile"));
        var paramsDict = new PdfDictionary();
        paramsDict.Set("Size", new PdfInteger(_pendingData.Length));
        fileStreamDict.Set("Params", paramsDict);
        var fileStream = new PdfStream(fileStreamDict, _pendingData);
        if (Encoding == FileEncoding.None) fileStream.DoNotCompress = true;
        var efDict = new PdfDictionary();
        efDict.Set("F", fileStream);
        _dict.Set("EF", efDict);
    }

    /// <summary>Pending file data for new attachments (not yet written to PDF).</summary>
    internal byte[]? PendingData => _pendingData;

    /// <summary>The file name.</summary>
    public string Name
    {
        get
        {
            // For reading, resolve through reader if available; otherwise read dict directly
            var ufObj = _reader?.Resolve(_dict.Get("UF")) ?? _dict.Get("UF");
            if (ufObj is PdfString s) return s.ToText();
            var fObj = _reader?.Resolve(_dict.Get("F")) ?? _dict.Get("F");
            if (fObj is PdfString s2) return s2.ToText();
            var descObj = _reader?.Resolve(_dict.Get("Desc")) ?? _dict.Get("Desc");
            return descObj is PdfString s3 ? s3.ToText() : "unknown";
        }
        set
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            _dict.Set("UF", new PdfString(bytes));
            _dict.Set("F", new PdfString(bytes));
        }
    }

    /// <summary>The file description.</summary>
    public string? Description
    {
        get
        {
            var obj = _reader?.Resolve(_dict.Get("Desc")) ?? _dict.Get("Desc");
            return obj is PdfString s ? s.ToText() : null;
        }
        set
        {
            if (value is null)
                _dict.Remove("Desc");
            else
                _dict.Set("Desc", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>
    /// Stream for reading or setting the embedded file data.
    /// Getter returns a MemoryStream with decoded content.
    /// Setter reads the entire stream and stores it as pending data.
    /// </summary>
    public Stream? Contents
    {
        get
        {
            if (_pendingData is not null)
                return new MemoryStream(_pendingData, writable: false);

            var data = GetData();
            return data is not null ? new MemoryStream(data, writable: false) : null;
        }
        set
        {
            if (value is null) { _pendingData = null; return; }
            // Reset position if possible so we read the full stream
            if (value.CanSeek) value.Position = 0;
            using var ms = new MemoryStream();
            value.CopyTo(ms);
            _pendingData = ms.ToArray();
        }
    }

    /// <summary>Alias for <see cref="Contents"/>.</summary>
    public Stream? StreamContents => Contents;

    /// <summary>The MIME type (from /Subtype of the embedded file stream).</summary>
    public string? MimeType
    {
        get
        {
            // Check pending MIME type first (set before save)
            if (_pendingMimeType is not null) return _pendingMimeType;
            if (_reader is null) return null;
            var ef = _reader.ResolveDict(_dict.Get("EF"));
            if (ef is null) return null;
            var stream = _reader.ResolveStream(ef.Get("F"));
            return stream?.Dict.GetName("Subtype");
        }
        set => _pendingMimeType = value;
    }
    private string? _pendingMimeType;

    /// <summary>Alias for <see cref="MimeType"/> naming.</summary>
    public string? MIMEType { get => MimeType; set => MimeType = value; }

    /// <summary>Pending MIME type for new specs (written during save).</summary>
    internal string? PendingMimeType => _pendingMimeType;

    /// <summary>Get the embedded file data as a byte array.</summary>
    public byte[]? GetData()
    {
        // For newly created specs, return the pending data
        if (_pendingData is not null) return _pendingData;

        if (_reader is null) return null;
        var ef = _reader.ResolveDict(_dict.Get("EF"));
        if (ef is null) return null;
        var stream = _reader.ResolveStream(ef.Get("F"));
        return stream is not null ? _reader.DecodeStream(stream) : null;
    }

    /// <summary>Return at most <paramref name="maxBytes"/> of the embedded file's decoded
    /// content without materialising the whole payload — used to sniff the header of a
    /// possibly very large attachment (e.g. the on-load dangerous-content check) cheaply.</summary>
    internal byte[]? GetDataPrefix(int maxBytes)
    {
        if (_pendingData is not null)
            return _pendingData.Length <= maxBytes ? _pendingData : _pendingData[..maxBytes];

        if (_reader is null) return null;
        var ef = _reader.ResolveDict(_dict.Get("EF"));
        if (ef is null) return null;
        var stream = _reader.ResolveStream(ef.Get("F"));
        return stream is not null ? _reader.DecodeStreamPrefix(stream, maxBytes) : null;
    }

    private FileParams? _params;

    /// <summary>The /Params dict on the embedded-file stream, wrapped as
    /// a <see cref="FileParams"/>. Null for a document-backed spec whose
    /// embedded stream has no /Params entry (reference behaviour); lazy-
    /// constructed on an unbound spec so callers can set CreationDate /
    /// ModDate before the spec is saved.</summary>
    public FileParams? Params
    {
        get
        {
            if (_params is not null) return _params;
            if (_reader is not null && GetEmbeddedParamsDict(out _) is null) return null;
            return _params ??= new FileParams(this);
        }
        set => _params = value;
    }

    /// <summary>UTF-16 file name (/UF entry, PDF 2.0 preferred over /F).</summary>
    public string? UnicodeName
    {
        get => (_dict.Get("UF") as PdfString)?.ToText();
        set
        {
            if (value is null) _dict.Remove("UF");
            else _dict.Set("UF", Forms.Field.EncodePdfTextString(value));
        }
    }

    /// <summary>File-system identifier carried in /FS. Stored only — the
    /// FOSS reader / writer only emits embedded-file specs.</summary>
    public string? FileSystem
    {
        get => _dict.GetName("FS");
        set
        {
            if (value is null) _dict.Remove("FS");
            else _dict.Set("FS", new PdfName(value));
        }
    }

    /// <summary>When true, the file spec carries an embedded-file stream
    /// (/EF entry). Setter toggles whether contents are included at save.</summary>
    public bool IncludeContents { get; set; } = true;

    /// <summary>Relationship between the embedded file and the document
    /// content that references it (/AFRelationship, PDF 2.0 §7.11.3).</summary>
    public AFRelationship AFRelationship
    {
        get => _dict.GetName("AFRelationship") switch
        {
            "Source" => AFRelationship.Source,
            "Data" => AFRelationship.Data,
            "Alternative" => AFRelationship.Alternative,
            "Supplement" => AFRelationship.Supplement,
            "EncryptedPayload" => AFRelationship.EncryptedPayload,
            "Unspecified" => AFRelationship.Unspecified,
            _ => AFRelationship.None,
        };
        set
        {
            if (value == AFRelationship.None) _dict.Remove("AFRelationship");
            else _dict.Set("AFRelationship", new PdfName(value.ToString()));
        }
    }

    /// <summary>Compression applied to the embedded stream's data when the
    /// spec is added to a document: <see cref="FileEncoding.Zip"/> stores it
    /// FlateDecode-compressed, <see cref="FileEncoding.None"/> stores it raw.</summary>
    public FileEncoding Encoding { get; set; } = FileEncoding.Zip;

    /// <summary>Encrypted-payload metadata (PDF 2.0 unencrypted-wrapper
    /// support). Always non-null; reflects the /EP entry's Type/Subtype/Version.</summary>
    public EncryptedPayload EncryptedPayload => _encryptedPayload ??= new EncryptedPayload(this);
    private EncryptedPayload? _encryptedPayload;

    /// <summary>Resolved /EP (encrypted-payload) dictionary on the file-spec
    /// dictionary, or null when the spec is not an encrypted-payload wrapper.</summary>
    internal PdfDictionary? GetEncryptedPayloadDict() => _reader?.ResolveDict(_dict.Get("EP"));

    /// <summary>Schema-driven collection metadata (portfolio /Collection
    /// items). Always non-null; entries are populated by the parser when
    /// the spec lives in a portfolio document.</summary>
    public CollectionItem CollectionItem { get; } = new CollectionItem();

    /// <summary>The uncompressed size of the embedded file (legacy alias —
    /// see <see cref="FileParams"/> for the proper accessor).</summary>
    public long? Size
    {
        get
        {
            if (_pendingData is not null) return _pendingData.Length;
            if (_reader is null) return null;
            var ef = _reader.ResolveDict(_dict.Get("EF"));
            if (ef is null) return null;
            var stream = _reader.ResolveStream(ef.Get("F"));
            if (stream is null) return null;
            var parms = _reader.ResolveDict(stream.Dict.Get("Params"));
            return parms is not null ? parms.GetInt("Size", -1) : -1;
        }
    }

    /// <summary>Resolve the embedded-file stream's /Params dictionary (and the
    /// backing reader), or null when this spec has no materialised /EF stream
    /// (e.g. a freshly built, not-yet-saved spec). Used by <see cref="FileParams"/>
    /// so the public accessor reflects the actual stored Size/CreationDate/ModDate.</summary>
    internal PdfDictionary? GetEmbeddedParamsDict(out PdfReader? reader)
    {
        reader = _reader;
        if (_reader is null) return null;
        var ef = _reader.ResolveDict(_dict.Get("EF"));
        var stream = ef is null ? null : _reader.ResolveStream(ef.Get("F"));
        return stream is null ? null : _reader.ResolveDict(stream.Dict.Get("Params"));
    }

    private readonly Dictionary<string, string> _customValues = new(StringComparer.Ordinal);

    /// <summary>Read a custom name/value pair previously set via
    /// <see cref="SetValue(string, string)"/>. Returns null when the key is
    /// missing.</summary>
    public string? GetValue(string key)
        => key is not null && _customValues.TryGetValue(key, out var v) ? v : null;

    /// <summary>Store a custom name/value pair on the spec.</summary>
    public void SetValue(string key, string value)
    {
        if (key is null) return;
        _customValues[key] = value ?? string.Empty;
    }

    /// <summary>
    /// Recognise a known dangerous embedded payload. Targets the Windows
    /// <c>.SettingContent-ms</c> file type abused by CVE-2018-8414 — either by
    /// its file extension or, when the attachment is renamed to hide that
    /// extension, by the SettingContent schema marker in its content.
    /// </summary>
    internal static bool IsDangerousContent(string? name, byte[]? data)
    {
        if (name is not null
            && name.EndsWith(".SettingContent-ms", StringComparison.OrdinalIgnoreCase))
            return true;

        if (data is { Length: > 0 })
        {
            // Sniff only a bounded prefix so large attachments aren't fully
            // materialised as text.
            var prefixLen = Math.Min(data.Length, 4096);
            var text = System.Text.Encoding.UTF8.GetString(data, 0, prefixLen);
            if (text.Contains("schemas.microsoft.com/Search/2013/SettingContent",
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Replace this spec's content with an empty stream and flag its description
    /// as dangerous. Called when an embedded file is recognised as a known
    /// attack vector so its payload is not exposed or re-saved.
    /// </summary>
    internal void NeutralizeAsDangerous()
    {
        _pendingData = System.Array.Empty<byte>();
        _dict.Remove("EF");
        Description = PdfExceptionMessages.DangerousFile;
    }

    /// <summary>Releases resources held by the file spec. Currently a no-op
    /// — pending data is freed when the parent document is disposed.</summary>
    public void Dispose() { _pendingData = null; _customValues.Clear(); }
}

/// <summary>
/// Wraps the /Params dict on an embedded-file stream (PDF §7.11.3 Table 46).
/// Aspose.Pdf reflection signature uses DateTime get/set for CreationDate/
/// ModDate; this version honours that.
/// </summary>
public sealed class FileParams
{
    private readonly FileSpecification? _spec;
    private readonly PdfDictionary _dict;
    private readonly PdfReader? _reader;

    internal FileParams(PdfDictionary dict, PdfReader reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>Construct a FileParams backed by a <see cref="FileSpecification"/>'s
    /// embedded-stream /Params entry (or a fresh empty dict when the spec
    /// has no stream yet).</summary>
    public FileParams(FileSpecification spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        // Reflect the spec's stored /Params (Size/CreationDate/ModDate) when it is
        // backed by a parsed document; otherwise start from an empty dict that the
        // caller can populate on a not-yet-saved spec.
        _dict = spec.GetEmbeddedParamsDict(out var reader) ?? new PdfDictionary();
        _reader = reader;
    }

    /// <summary>Uncompressed file size in bytes (PDF /Size entry). 0 when absent.</summary>
    public int Size
    {
        get
        {
            var s = (int)_dict.GetInt("Size", -1);
            return s < 0 ? 0 : s;
        }
    }

    /// <summary>Hex-encoded MD5 of the uncompressed file, or empty when absent.</summary>
    public string CheckSum => (_dict.Get("CheckSum") as PdfString)?.ToText() ?? string.Empty;

    /// <summary>The file's creation date.</summary>
    public DateTime CreationDate
    {
        get => ParsePdfDate(_dict.Get("CreationDate") as PdfString) ?? DateTime.MinValue;
        set => _dict.Set("CreationDate",
            new PdfString(System.Text.Encoding.Latin1.GetBytes(
                "D:" + value.ToUniversalTime().ToString("yyyyMMddHHmmss") + "Z")));
    }

    /// <summary>The file's last-modification date.</summary>
    public DateTime ModDate
    {
        get => ParsePdfDate(_dict.Get("ModDate") as PdfString) ?? DateTime.MinValue;
        set => _dict.Set("ModDate",
            new PdfString(System.Text.Encoding.Latin1.GetBytes(
                "D:" + value.ToUniversalTime().ToString("yyyyMMddHHmmss") + "Z")));
    }

    private static DateTime? ParsePdfDate(PdfString? raw)
    {
        if (raw is null) return null;
        var s = raw.ToText();
        if (s.StartsWith("D:")) s = s.Substring(2);
        if (s.Length >= 14
            && DateTime.TryParseExact(s.Substring(0, 14), "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return null;
    }
}

/// <summary>
/// Collection of embedded files in a document.
/// Supports reading existing attachments and adding new ones.
/// </summary>
public class EmbeddedFileCollection : IReadOnlyList<FileSpecification>
{
    private readonly List<FileSpecification> _files;
    private readonly PdfDictionary? _namesDict;
    // Newly added specs that need to be written during save
    private readonly List<FileSpecification> _pending = [];
    // Owning document for accessing catalog during save
    internal Document? OwnerDocument { get; set; }

    /// <summary>
    /// Construct an unbacked collection. Used by <see cref="Collection"/>'s
    /// public parameterless ctor; routes Add through <see cref="OwnerDocument"/>.
    /// </summary>
    protected EmbeddedFileCollection()
    {
        _namesDict = null;
        _files = new List<FileSpecification>();
    }

    internal EmbeddedFileCollection(PdfDictionary? namesDict, PdfReader reader,
        PageCollection? pages = null, PdfReader? pageReader = null)
    {
        _namesDict = namesDict;
        _files = new List<FileSpecification>();

        // 1. /Names/EmbeddedFiles name tree
        if (namesDict is not null)
        {
            var efTree = reader.ResolveDict(namesDict.Get("EmbeddedFiles"));
            if (efTree is not null)
                CollectFromNameTree(efTree, reader, _files);
        }

        // 2. FileAttachment annotations on pages
        if (pages is not null && pageReader is not null)
        {
            for (var i = 1; i <= pages.Count; i++)
            {
                var page = pages.At(i);
                var annotsObj = pageReader.Resolve(page.Dict.Get("Annots"));
                if (annotsObj is not PdfArray annotsArr) continue;

                foreach (var item in annotsArr)
                {
                    var annotDict = pageReader.ResolveDict(item);
                    if (annotDict is null) continue;
                    if (annotDict.GetName("Subtype") != "FileAttachment") continue;

                    var fs = pageReader.ResolveDict(annotDict.Get("FS"));
                    if (fs is not null)
                        _files.Add(new FileSpecification(fs, pageReader));
                }
            }
        }

        // Strip known dangerous embedded payloads (e.g. CVE-2018-8414
        // .SettingContent-ms attachments) on load so callers never see their
        // content and a re-save cannot carry them forward. A malformed
        // attachment stream must not break enumeration of the others.
        foreach (var spec in _files)
        {
            try
            {
                // IsDangerousContent only inspects the name and a bounded (4 KB) prefix, so
                // decode just that prefix — fully materialising every attachment here
                // buffered hundreds of MB for a large embedded file on load.
                if (FileSpecification.IsDangerousContent(spec.Name, spec.GetDataPrefix(4096)))
                    spec.NeutralizeAsDangerous();
            }
            catch
            {
                // Ignore decode failures — leave the spec untouched.
            }
        }
    }

    public int Count => _files.Count;

    /// <summary>1-based indexer.</summary>
    public FileSpecification this[int index] => _files[index - 1];

    /// <summary>By-name lookup. Returns null when no entry matches.</summary>
    public FileSpecification? this[string name]
    {
        get
        {
            foreach (var f in _files)
                if (f.Name == name) return f;
            return null;
        }
    }

    /// <summary>
    /// Add an embedded file attachment to the document.
    /// Delegates to Document.AddEmbeddedFile to write the file spec
    /// and embedded stream into the PDF structure immediately.
    /// </summary>
    public void Add(FileSpecification file)
    {
        var doc = OwnerDocument;
        if (doc is null)
            throw new InvalidOperationException("Cannot add files — collection is not associated with a document.");

        var data = file.PendingData ?? file.GetData();
        if (data is null)
            throw new InvalidOperationException("FileSpecification has no data to embed.");

        doc.AddEmbeddedFile(file.Name, data, file.Description, file.PendingMimeType,
            compress: file.Encoding != FileEncoding.None,
            creationDate: file.PendingCreationDate, modDate: file.PendingModDate);
        _files.Add(file);
    }

    /// <summary>Add with an explicit name, overriding the spec's own Name.</summary>
    public void Add(string key, FileSpecification file)
    {
        file.Name = key;
        Add(file);
    }

    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    /// <summary>The names of all embedded files in this collection.</summary>
    public List<string> Keys
    {
        get
        {
            var keys = new List<string>(_files.Count);
            foreach (var f in _files) keys.Add(f.Name ?? string.Empty);
            return keys;
        }
    }

    public void CopyTo(FileSpecification[] array, int index) => _files.CopyTo(array, index);

    /// <summary>Look up an embedded file by its registered name. Returns null if absent.</summary>
    public FileSpecification? FindByName(string name)
    {
        foreach (var f in _files) if (f.Name == name) return f;
        return null;
    }

    /// <summary>Sibling of <see cref="Delete(string)"/> matching Aspose.Pdf's by-key naming.</summary>
    public void DeleteByKey(string key) => Delete(key);

    /// <summary>
    /// Removes all embedded files from the document's /Names/EmbeddedFiles name tree.
    /// </summary>
    public void Delete()
    {
        _namesDict?.Remove("EmbeddedFiles");
        _files.Clear();
        _pending.Clear();
    }

    /// <summary>
    /// Remove a single embedded file by name. Drops it from the in-memory list and
    /// the /Names/EmbeddedFiles name-tree leaf array, so a subsequent Save persists
    /// the deletion.
    /// </summary>
    public void Delete(string name)
    {
        for (var i = 0; i < _files.Count; i++)
        {
            if (_files[i].Name != name) continue;
            _files.RemoveAt(i);
            break;
        }

        if (_namesDict is null) return;
        var reader = OwnerDocument?.Reader;
        if (reader is null) return;
        var efTree = reader.ResolveDict(_namesDict.Get("EmbeddedFiles"));
        if (efTree is null) return;
        if (reader.Resolve(efTree.Get("Names")) is not PdfArray namesArr) return;

        for (var i = 0; i + 1 < namesArr.Count; i += 2)
        {
            if (reader.Resolve(namesArr[i]) is not PdfString s) continue;
            if (s.ToText() != name) continue;
            namesArr.RemoveAt(i);
            namesArr.RemoveAt(i);
            return;
        }
    }

    /// <summary>
    /// Returns the list of pending file specs that need to be written during save.
    /// Called by the PdfWriter to embed file data into the output PDF.
    /// </summary>
    internal IReadOnlyList<FileSpecification> GetPendingFiles() => _pending;

    public IEnumerator<FileSpecification> GetEnumerator() => _files.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static void CollectFromNameTree(PdfDictionary node, PdfReader reader,
        List<FileSpecification> result)
    {
        // /Names array: [name1 ref1 name2 ref2 .]
        var names = reader.Resolve(node.Get("Names")) as PdfArray;
        if (names is not null)
        {
            for (var i = 1; i < names.Count; i += 2)
            {
                var fileSpec = reader.ResolveDict(names[i]);
                if (fileSpec is not null)
                    result.Add(new FileSpecification(fileSpec, reader));
            }
        }

        // /Kids array for intermediate nodes
        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is not null)
        {
            foreach (var kid in kids)
            {
                var kidDict = reader.ResolveDict(kid);
                if (kidDict is not null)
                    CollectFromNameTree(kidDict, reader, result);
            }
        }
    }
}
