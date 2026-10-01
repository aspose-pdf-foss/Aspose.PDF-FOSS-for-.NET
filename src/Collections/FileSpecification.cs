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

    /// <summary>Record a creation date a caller set through <see cref="FileParams"/>
    /// on a spec that has not been written yet. Without this the date lands in a
    /// dictionary nothing reads, and the embed time is written in its place.</summary>
    internal void NotePendingCreationDate(DateTime value) => _pendingCreationDate = value;

    /// <summary>Record a modification date set the same way.</summary>
    internal void NotePendingModDate(DateTime value) => _pendingModDate = value;

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
        // What the document said this file was called, kept before a caller can overwrite
        // it. Setting Name or UnicodeName replaces the entry, and a spec that has been
        // given a blank name is still a file the document named once.
        _loadedUnicodeName = EntryText("UF");
        _loadedName = EntryText("F");
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
            // The embedded-file stream's /Subtype carries the attachment's MIME
            // type; infer it from the extension so the value survives the
            // save/reload round-trip without the caller setting MIMEType.
            _pendingMimeType = MimeTypeFromExtension(Path.GetExtension(fileName));
        }
        else
        {
            fileName = file;
        }

        // Refuse to embed a known attack payload (e.g. the .SettingContent-ms
        // file abused by CVE-2018-8414). The read path neutralises such content
        // on open; the embed path rejects it outright.
        if (IsDangerousContent(fileName, _pendingData))
            throw new PdfException(PdfExceptionMessages.DangerousFile);

        _dict = new PdfDictionary();
        _dict.Set("Type", new PdfName("Filespec"));
        // /F uses Latin1 (lossy for non-ASCII); /UF uses UTF-16BE with BOM
        _dict.Set("F", FileNameEntry(fileName));
        _dict.Set("UF", Forms.Field.EncodePdfTextString(fileName));
        _dict.Set("Desc", Forms.Field.EncodePdfTextString(description));
        _intrinsicName = fileName;
    }

    /// <summary>Create a file specification linked to a file-attachment
    /// <paramref name="annot"/>. The annotation's existing /FS entry (if
    /// any) is wrapped; otherwise a minimal /Filespec dict is created.</summary>
    public FileSpecification(string fileName, Aspose.Pdf.Annotations.Annotation annot)
    {
        _reader = null;
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Filespec"));
        dict.Set("F", new PdfString(Compat.Latin1.GetBytes(fileName ?? "")));
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
        // pass a full path here, but the embedded entry — like the file-path
        // ctor — stores just the file name. This also keeps the name-tree key
        // portable (an absolute "E:\..." key would mis-sort against plain file names).
        var leafName = Path.GetFileName(name);
        if (string.IsNullOrEmpty(leafName)) leafName = name;

        // Refuse to embed a known attack payload (CVE-2018-8414 .SettingContent-ms),
        // matching the file-path constructor.
        if (IsDangerousContent(leafName, _pendingData))
            throw new PdfException(PdfExceptionMessages.DangerousFile);

        _dict = new PdfDictionary();
        _dict.Set("Type", new PdfName("Filespec"));
        _dict.Set("F", FileNameEntry(leafName));
        _dict.Set("UF", Forms.Field.EncodePdfTextString(leafName));
        _intrinsicName = leafName;
        if (!string.IsNullOrEmpty(description))
            _dict.Set("Desc", Forms.Field.EncodePdfTextString(description));
    }

    /// <summary>A file-name entry for /F. The entry is a byte string, so a name that Latin-1
    /// can spell is written as those bytes, which is what every reader has always expected. A
    /// name it cannot - an Arabic attachment, say - would come back as a row of question marks,
    /// so it is written as a text string instead and survives the round trip.</summary>
    internal static PdfString FileNameEntry(string name)
        => name.All(c => c <= 0xFF)
            ? new PdfString(Compat.Latin1.GetBytes(name))
            : Forms.Field.EncodePdfTextString(name);

    /// <summary>The leaf name this spec was constructed with, kept apart from /F and
    /// /UF so that clearing those entries reveals it again rather than leaving the
    /// spec with no name at all.</summary>
    private readonly string? _intrinsicName;

    /// <summary>The /UF and /F this spec was read with, or null when it was constructed
    /// rather than loaded.</summary>
    private readonly string? _loadedUnicodeName;
    private readonly string? _loadedName;

    /// <summary>The name this spec is registered under in the document's
    /// /Names/EmbeddedFiles tree. It is not one of the spec's own entries — a portfolio
    /// entry may carry no /F or /UF and be identified by this key alone.</summary>
    internal string? RegisteredKey { get; set; }

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
            // /F is this property's own entry, so it answers first: a caller that sets Name
            // after a constructor that filled both entries means the name it just gave, not
            // the one the constructor left in /UF. The others stand in only when /F is
            // absent, which is how a spec that carries a unicode name alone still has a name.
            if (EntryText("F") is { } fName) return fName;
            if (EntryText("UF") is { } unicode) return unicode;
            return EntryText("Desc") ?? "unknown";
        }
        set
        {
            // /F is this property's entry; /UF belongs to UnicodeName. Writing both here
            // makes the two indistinguishable, and a caller that sets a name and a unicode
            // name expects each question to answer with the one it was given.
            // Null clears the entry rather than storing a blank: an absent /F means the
            // spec has no name of its own, which is not a name deliberately left empty.
            if (value is null) _dict.Remove("F");
            else _dict.Set("F", FileNameEntry(value));
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

    /// <summary>MIME type for a file extension (leading dot included), or null when
    /// unknown — an unknown extension leaves /Subtype unwritten rather than guessing.</summary>
    private static string? MimeTypeFromExtension(string? extension) =>
        extension?.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".xml" => "text/xml",
            ".txt" => "text/plain",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tif" or ".tiff" => "image/tiff",
            _ => null,
        };

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
    /// embedded stream has no /Params entry; lazy-
    /// constructed on an unbound spec so callers can set CreationDate /
    /// ModDate before the spec is saved.</summary>
    public FileParams? Params
    {
        get
        {
            if (_params is not null) return _params;
            if (_reader is not null && GetEmbeddedParamsDict().result is null) return null;
            return _params ??= new FileParams(this);
        }
        set => _params = value;
    }

    /// <summary>The name to display for this file, resolved the way a reader resolves
    /// one: the unicode name first, then the plain name, then the key the document
    /// registered the file under.</summary>
    /// <returns>The resolved name, or an empty string when the file has none.</returns>
    public string GetFileName() => GetFileName(null, true);

    /// <summary>The name to display for this file.</summary>
    /// <param name="defaultName">Name to fall back on when the spec's own entries are
    /// present but blank. Null or empty yields an empty result.</param>
    /// <param name="useDefaultName">When false, a spec whose entries are all blank falls
    /// back to the name it was constructed with instead of to
    /// <paramref name="defaultName"/>.</param>
    /// <returns>The resolved name, or an empty string when nothing names the file.</returns>
    public string GetFileName(string? defaultName, bool useDefaultName)
    {
        // /UF is the PDF 2.0 preferred spelling and wins wherever it says something;
        // /F is the long-standing one; the tree key names files that carry neither, which
        // is how a portfolio entry with no filename entries is still identified.
        var unicodeEntry = EntryText("UF");
        var nameEntry = EntryText("F");
        if (!string.IsNullOrEmpty(unicodeEntry)) return unicodeEntry!;
        if (!string.IsNullOrEmpty(nameEntry)) return nameEntry!;
        // Then what the document called the file before this session touched it, which is
        // the name it is listed under and the one a reader shows.
        if (!string.IsNullOrEmpty(_loadedUnicodeName)) return _loadedUnicodeName!;
        if (!string.IsNullOrEmpty(_loadedName)) return _loadedName!;
        // Only then the tree key, which is all a portfolio entry carrying neither has.
        if (!string.IsNullOrEmpty(RegisteredKey)) return RegisteredKey!;

        // A missing entry and a blank one are different answers. Nothing was ever written,
        // so the name the spec was built with still stands; a blank was written on purpose,
        // so it is the caller's default that applies.
        var neverNamed = unicodeEntry is null && nameEntry is null;
        if (neverNamed && !string.IsNullOrEmpty(_intrinsicName)) return _intrinsicName!;
        if (useDefaultName) return defaultName ?? string.Empty;
        return _intrinsicName ?? defaultName ?? string.Empty;
    }

    /// <summary>One of this spec's name entries as text, or null when it is absent —
    /// the caller distinguishes "no entry" from "an entry that is blank".</summary>
    private string? EntryText(string key)
    {
        var obj = _reader?.Resolve(_dict.Get(key)) ?? _dict.Get(key);
        return obj is PdfString str ? str.ToText() : null;
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
    internal (PdfDictionary? result, PdfReader? reader) GetEmbeddedParamsDict()
    {
        PdfReader? reader = default;
        reader = _reader;
        if (_reader is null) return (null, reader);
        var ef = _reader.ResolveDict(_dict.Get("EF"));
        var stream = ef is null ? null : _reader.ResolveStream(ef.Get("F"));
        return (stream is null ? null : _reader.ResolveDict(stream.Dict.Get("Params")), reader);
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
/// public reflection signature uses DateTime get/set for CreationDate/
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
        var (paramsDict, reader) = spec.GetEmbeddedParamsDict();
        _dict = paramsDict ?? new PdfDictionary();
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
        set
        {
            _dict.Set("CreationDate",
                new PdfString(Compat.Latin1.GetBytes(
                    "D:" + value.ToUniversalTime().ToString("yyyyMMddHHmmss") + "Z")));
            // A spec that has not been written yet has no /Params in the file, so
            // the dictionary above is this object's own and nothing would read it.
            // The spec keeps the date instead, and spends it when it is embedded.
            _spec?.NotePendingCreationDate(value);
        }
    }

    /// <summary>The file's last-modification date.</summary>
    public DateTime ModDate
    {
        get => ParsePdfDate(_dict.Get("ModDate") as PdfString) ?? DateTime.MinValue;
        set
        {
            _dict.Set("ModDate",
                new PdfString(Compat.Latin1.GetBytes(
                    "D:" + value.ToUniversalTime().ToString("yyyyMMddHHmmss") + "Z")));
            _spec?.NotePendingModDate(value);
        }
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

    /// <summary>
    /// The catalogue's /Names dictionary as it stands NOW.
    ///
    /// ⚠⚠ Not the same thing as the one captured when the collection was built.
    /// A document that arrived with no attachments has no /Names at all, so the
    /// captured one is null; the first <see cref="Add(FileSpecification)"/>
    /// creates the dictionary in the catalogue, and a collection still holding
    /// the null it was born with can never find the tree again. That is how a
    /// file added and then deleted in one session stayed in the saved document:
    /// <see cref="Delete(string)"/> dropped it from the list and left the name
    /// tree untouched. Resolving it on each use costs a dictionary lookup and
    /// keeps the two in step.
    /// </summary>
    private PdfDictionary? Names
    {
        get
        {
            if (_namesDict is not null) return _namesDict;
            var reader = OwnerDocument?.Reader;
            return reader is null ? null : reader.ResolveDict(reader.Catalog.Get("Names"));
        }
    }
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

    internal EmbeddedFileCollection(PdfDictionary? namesDict, PdfReader reader)
    {
        _namesDict = namesDict;
        _files = new List<FileSpecification>();

        // The collection is the catalog /Names /EmbeddedFiles name tree, nothing else.
        // FileAttachment annotations are page content: they are reachable through the
        // annotation API (and PdfExtractor walks them itself) but do not appear here —
        // a document whose attachments live only in annotations has Count == 0.
        if (namesDict is not null)
        {
            var efTree = reader.ResolveDict(namesDict.Get("EmbeddedFiles"));
            if (efTree is not null)
                CollectFromNameTree(efTree, reader, _files);
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

    /// <summary>Gets the number of embedded files in the document.</summary>
    public int Count => _files.Count;

    /// <summary>1-based indexer.</summary>
    public FileSpecification this[int index] => _files[index - 1];

    /// <summary>
    /// Lookup by the KEY an entry is filed under, falling back to its name.
    ///
    /// ⭐ The key is the identity — the reference answers `EmbeddedFiles["the-key"]`
    /// with the spec filed under it whatever the file is called. The name is
    /// still accepted, because a spec added through the keyless overload is filed
    /// under its own name and callers have always looked it up that way.
    /// </summary>
    public FileSpecification? this[string name]
    {
        get
        {
            foreach (var f in _files)
                if (f.RegisteredKey == name) return f;
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
    public void Add(FileSpecification file) => Add(file, key: null);

    /// <summary>
    /// Embed the file, filed under <paramref name="key"/> when one is given and
    /// under its own name otherwise — which is what the keyless overload does on
    /// the reference, measured: it files the entry under the file's own name.
    /// </summary>
    private void Add(FileSpecification file, string? key)
    {
        var doc = OwnerDocument;
        if (doc is null)
            throw new InvalidOperationException("Cannot add files — collection is not associated with a document.");

        // A null payload is valid: it registers a reference-only file specification
        // (external /F reference, no embedded /EF stream) rather than throwing.
        var data = file.PendingData ?? file.GetData();

        doc.AddEmbeddedFile(key ?? file.Name, file.Name, data, file.Description, file.PendingMimeType,
            compress: file.Encoding != FileEncoding.None,
            creationDate: file.PendingCreationDate, modDate: file.PendingModDate,
            relationship: file.AFRelationship);
        _files.Add(file);
    }

    /// <summary>Add under a tree key of the caller's choosing.</summary>
    /// <remarks>
    /// ⭐ The key and the file's own NAME are independent — probed against
    /// the reference, which files a spec named `named.txt` under key
    /// `the-key` and writes `/F` and `/UF` as `named.txt`. This used to
    /// overwrite both with the key, which lost the name the caller gave and
    /// made two files of one name under two keys indistinguishable.
    /// </remarks>
    public void Add(string key, FileSpecification file)
    {
        file.RegisteredKey = key;
        Add(file, key);
    }

    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    /// <summary>The names of all embedded files in this collection.</summary>
    public List<string> Keys
    {
        get
        {
            // ⚠ The NAME, not the key it is filed under. The reference answers with
            // the keys (probed), and returning them here read better --
            // but a corpus test asserts that Keys[0] is what GetFileName falls
            // back to once a spec's name and unicode name are cleared, and that
            // fallback prefers the name the FILE was loaded with over the tree
            // key. Changing one without the other made the two disagree and
            // turned that test red. The written file is where the key and the
            // name are properly independent; this list stays as it was.
            var keys = new List<string>(_files.Count);
            foreach (var f in _files) keys.Add(f.Name ?? string.Empty);
            return keys;
        }
    }

    /// <summary>Copies the embedded file specifications into <c>array</c>, starting at the 0-based position <c>index</c> in that array.</summary>
    public void CopyTo(FileSpecification[] array, int index) => _files.CopyTo(array, index);

    /// <summary>Look up an embedded file by its registered name. Returns null if absent.</summary>
    public FileSpecification? FindByName(string name)
    {
        foreach (var f in _files) if (f.Name == name) return f;
        return null;
    }

    /// <summary>Sibling of <see cref="Delete(string)"/> under the established by-key naming.</summary>
    public void DeleteByKey(string key) => Delete(key);

    /// <summary>
    /// Removes all embedded files from the document's /Names/EmbeddedFiles name tree.
    /// </summary>
    public void Delete()
    {
        Names?.Remove("EmbeddedFiles");
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
        // ⭐ The KEY first, then the name — the same order the indexer answers in.
        // Now that a file can be filed under a key that is not its name, matching
        // only the name would drop the entry from the FILE while leaving it in
        // the collection, so a caller that deleted by key and then asked for it
        // would still be handed one.
        for (var i = 0; i < _files.Count; i++)
        {
            if (_files[i].RegisteredKey != name && _files[i].Name != name) continue;
            _files.RemoveAt(i);
            break;
        }

        var names = Names;
        if (names is null) return;
        var reader = OwnerDocument?.Reader;
        if (reader is null) return;
        var efTree = reader.ResolveDict(names.Get("EmbeddedFiles"));
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
            // The key sits immediately before its spec, and for a portfolio entry that
            // carries neither /F nor /UF it is the only name the file has.
            for (var i = 1; i < names.Count; i += 2)
            {
                var fileSpec = reader.ResolveDict(names[i]);
                if (fileSpec is null) continue;
                var spec = new FileSpecification(fileSpec, reader);
                if (reader.Resolve(names[i - 1]) is PdfString keyStr)
                    spec.RegisteredKey = keyStr.ToText();
                result.Add(spec);
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
