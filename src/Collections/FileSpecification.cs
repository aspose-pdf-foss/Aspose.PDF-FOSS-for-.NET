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
    internal void NotePendingCreationDate(DateTime value)
    {
        _pendingCreationDate = value;
        SetParamsDate(_embeddedParams, CreationDateKey, value);
    }

    /// <summary>Record a modification date set the same way.</summary>
    internal void NotePendingModDate(DateTime value)
    {
        _pendingModDate = value;
        SetParamsDate(_embeddedParams, ModDateKey, value);
    }

    private const string CreationDateKey = "CreationDate";
    private const string ModDateKey = "ModDate";

    // The /Params of the stream this spec materialised into its own /EF: a date set after that still reaches it.
    private PdfDictionary? _embeddedParams;

    private static void SetParamsDate(PdfDictionary? parameters, string key, DateTime value)
        => parameters?.Set(key, new PdfString(Compat.Latin1.GetBytes(Document.FormatPdfDate(value))));

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
    /// <param name="name">The file name the entry holds.</param>
    /// <param name="owner">The dictionary whose /F is that string: a name given to the
    /// specification afterwards is written there, as the specification itself is in no file.</param>
    internal static FileSpecification CreateFromName(string name, PdfDictionary? owner = null)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Filespec"));
        // Keep non-Latin1 path characters intact (Latin1 would flatten them to '?').
        dict.Set("F", Forms.Field.EncodePdfTextString(name));
        return new FileSpecification(dict, null) { _nameOwner = owner };
    }

    // The dictionary whose /F string this specification was made from, when it was.
    private PdfDictionary? _nameOwner;

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

    /// <summary>Where a document that embeds this file keeps its specification: the reference
    /// its name tree files it under. An annotation made for the file points there, so the
    /// document holds the file once however many annotations open it.</summary>
    internal (PdfReader Reader, PdfIndirectRef Reference)? Filed { get; set; }

    /// <summary>The annotations that were given this specification before a document filed it, each pointing
    /// at this spec's own dictionary.</summary>
    private List<PdfDictionary>? _holders;

    /// <summary>Note an annotation whose /FS is this spec's own dictionary.</summary>
    internal void HeldBy(PdfDictionary annotation) => (_holders ??= []).Add(annotation);

    /// <summary>The document has filed this file at <paramref name="reference"/>: the annotations made for it
    /// earlier open that specification too, instead of keeping a second embedded copy of the same file
    /// behind the spec's own dictionary.</summary>
    internal void PointHoldersAt(PdfIndirectRef reference)
    {
        if (_holders is null) return;
        foreach (var annotation in _holders)
            if (ReferenceEquals(annotation.Get("FS"), _dict)) annotation.Set("FS", reference);
        _holders = null;
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
        // The dates as the document's embedded files record them: the caller's or the source file's, else the
        // embed time.
        var embedNow = DateTime.Now;
        SetParamsDate(paramsDict, CreationDateKey, _pendingCreationDate ?? embedNow);
        SetParamsDate(paramsDict, ModDateKey, _pendingModDate ?? embedNow);
        fileStreamDict.Set("Params", paramsDict);
        _embeddedParams = paramsDict;
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
            if (EntryText("F") is { } fName) return NamesExternalFile ? PlatformPath(fName) : fName;
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
            if (value is null)
            {
                _dict.Remove("F");
                return;
            }
            _dict.Set("F", FileNameEntry(value));
            _nameOwner?.Set("F", FileNameEntry(value));
        }
    }

    /// <summary>The specification names a file outside the document (the target of a remote go-to):
    /// its name is a path, stored in the PDF form and read in the platform's.</summary>
    internal bool NamesExternalFile { get; set; }

    /// <summary>An absolute path in the PDF form (ISO 32000-1 7.11.2), "/c/dir/file" or "/server/share/file",
    /// as Windows writes it: a drive letter with a colon, or a share after two backslashes. Any other name,
    /// and every name on a platform whose own paths start with a slash, is returned as it is.</summary>
    private static string PlatformPath(string name)
    {
        const char windowsSeparator = '\\';
        if (Path.DirectorySeparatorChar != windowsSeparator || name.Length < 2 || name[0] != '/' || name[1] == '/')
            return name;
        var rest = name.IndexOf('/', 1);
        if (rest < 0 || rest == name.Length - 1) return name;
        var tail = name.Substring(rest).Replace('/', windowsSeparator);
        var isDrive = rest == 2 && (name[1] is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        return isDrive
            ? name[1] + ":" + tail
            : new string(windowsSeparator, 2) + name.Substring(1, rest - 1) + tail;
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
                _dict.Set("Desc", Aspose.Pdf.Forms.Field.EncodePdfTextString(value));
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
        // /UF is the PDF 2.0 preferred spelling and wins wherever it says something; /F
        // is the long-standing one; the tree key names files that carry neither, which
        // is how a portfolio entry with no filename entries is still identified.
        var unicodeEntry = EntryText("UF");
        var nameEntry = EntryText("F");
        if (!string.IsNullOrEmpty(unicodeEntry)) return unicodeEntry;
        if (!string.IsNullOrEmpty(nameEntry)) return nameEntry;
        // Then what the document called the file before this session touched it, which is
        // the name it is listed under and the one a reader shows.
        if (!string.IsNullOrEmpty(_loadedUnicodeName)) return _loadedUnicodeName;
        if (!string.IsNullOrEmpty(_loadedName)) return _loadedName;
        // Only then the tree key, which is all a portfolio entry carrying neither has.
        if (!string.IsNullOrEmpty(RegisteredKey)) return RegisteredKey;

        // A missing entry and a blank one are different answers. Nothing was ever written,
        // so the name the spec was built with still stands; a blank was written on purpose,
        // so it is the caller's default that applies.
        var neverNamed = unicodeEntry is null && nameEntry is null;
        if (neverNamed && !string.IsNullOrEmpty(_intrinsicName)) return _intrinsicName;
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
        if (_reader is null) return (_embeddedParams, reader);
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

    internal FileParams(PdfDictionary dict, PdfReader reader)
    {
        _dict = dict;
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
        var (paramsDict, _) = spec.GetEmbeddedParamsDict();
        _dict = paramsDict ?? new PdfDictionary();
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
                new PdfString(Compat.Latin1.GetBytes(Document.FormatPdfDate(value))));
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
                new PdfString(Compat.Latin1.GetBytes(Document.FormatPdfDate(value))));
            _spec?.NotePendingModDate(value);
        }
    }

    // The digits of a PDF date up to the seconds: yyyyMMddHHmmss.
    private const int PdfDateDigits = 14;

    /// <summary>A PDF date as the instant it names, in this machine's local time: the face value is read in the
    /// zone its suffix states (Z, +HH'mm' or -HH'mm'), a date stating no zone is taken as universal, and the
    /// instant comes back the way a local date was set, so a date written and read here round-trips.</summary>
    private static DateTime? ParsePdfDate(PdfString? raw)
    {
        if (raw is null) return null;
        var s = raw.ToText();
        if (s.StartsWith("D:")) s = s.Substring(2);
        if (s.Length < PdfDateDigits
            || !DateTime.TryParseExact(s.Substring(0, PdfDateDigits), "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var face))
            return null;
        return (face - ZoneOffset(s.Substring(PdfDateDigits))).ToLocalTime();
    }

    /// <summary>The zone a date suffix states, as its offset from universal time: "+09'00'" is nine hours ahead;
    /// Z, no suffix or an unreadable one is universal time.</summary>
    private static TimeSpan ZoneOffset(string suffix)
    {
        if (suffix.Length == 0 || (suffix[0] != '+' && suffix[0] != '-')) return TimeSpan.Zero;
        var digits = suffix.Substring(1).Replace("'", string.Empty);
        if (digits.Length < 2 || !int.TryParse(digits.Substring(0, 2), out var hours)) return TimeSpan.Zero;
        var minutes = digits.Length >= 4 && int.TryParse(digits.Substring(2, 2), out var m) ? m : 0;
        var offset = new TimeSpan(hours, minutes, 0);
        return suffix[0] == '-' ? -offset : offset;
    }
}
