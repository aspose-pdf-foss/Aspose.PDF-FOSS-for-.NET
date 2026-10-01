using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.IO;

/// <summary>
/// Serializes PdfObject hierarchy to PDF byte output.
/// </summary>
internal sealed partial class PdfWriter
{
    // The file being written; swapped for a scratch buffer while an object is measured.
    private Stream _output;
    private readonly Dictionary<int, long> _offsets = new();
    private int _nextObjNum = 1;
    private readonly PdfEncryptor? _encryptor;
    private int _currentObjectNumber = -1;
    private readonly HashSet<int> _excludedFromEncryption = new();

    // Deferred stream objects: streams embedded in dicts are promoted to indirect objects.
    // Maps allocated objNum → stream, flushed after each WriteIndirectObject call.
    private readonly Queue<(int objNum, PdfStream stream)> _deferredStreams = new();

    // Cycle detection: track dicts being written to prevent infinite recursion
    // from circular references (e.g. Popup annotation Parent → back to annotation).
    private readonly HashSet<PdfDictionary> _dictStack = new(ReferenceEqualityComparer.Instance);

    // Dedup: track PdfStream instances that have already been promoted to indirect objects.
    // When the same stream instance appears in multiple page dicts (shared resources),
    // reuse the same object number instead of writing duplicate copies.
    private readonly Dictionary<PdfStream, int> _streamObjNums = new(ReferenceEqualityComparer.Instance);

    // Inline dicts promoted to indirect objects to break a write-time cycle (e.g. a
    // field widget's /Parent pointing back to a group that's still being written).
    // Writing the dict inline again would recurse forever and writing null would
    // silently drop the reference, so it's emitted as a deferred indirect object.
    private readonly Dictionary<PdfDictionary, int> _promotedDicts = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<(int objNum, PdfDictionary dict)> _deferredDicts = new();

    // Inline dicts referenced from more than one place in the object graph (e.g. a radio
    // group reached both from /AcroForm/Fields and from each option widget's /Parent).
    // Pre-scanned before writing so every reference becomes one shared indirect object
    // instead of duplicated inline copies; obj numbers are allocated lazily on first write.
    private readonly HashSet<PdfDictionary> _sharedDicts = new(ReferenceEqualityComparer.Instance);


    // Object stream support: maps objNum → (PdfObject, whether it's eligible for ObjStm)
    private readonly Dictionary<int, PdfObject> _allObjects = new();

    /// <summary>
    /// When true, writes a cross-reference stream (PDF 1.5+) instead of a traditional xref table.
    /// </summary>
    public bool UseXRefStream { get; set; }

    /// <summary>Re-deflate plain FlateDecode pass-through streams at the strongest
    /// level, keeping the smaller of old/new bytes. Enabled for PDF/A saves.</summary>
    public bool RecompressFlateStreams { get; set; }

    /// <summary>
    /// When true, packs eligible small non-stream objects into compressed object streams (PDF 1.6+).
    /// Implies UseXRefStream = true (object streams require type-2 xref entries).
    /// </summary>
    public bool UseObjectStreams { get; set; }

    /// <summary>
    /// Maximum number of objects packed into a single object stream.
    /// </summary>
    private const int MaxObjectsPerStream = 1000;

    /// <summary>
    /// Maximum serialized size (in bytes) for an object to be eligible for an object stream.
    /// </summary>
    private const int MaxObjectSizeForObjStm = 16384;

    public PdfWriter(Stream output, PdfEncryptor? encryptor = null)
    {
        _output = output;
        _encryptor = encryptor;
    }

    public long Position => _output.Position;

    /// <summary>
    /// Ensure the next allocated object number is at least <paramref name="minObjNum"/>.
    /// Call this before writing objects to prevent allocated obj numbers from colliding
    /// with pre-existing objects that will be written later in the same session.
    /// </summary>
    public void SetMinObjectNumber(int minObjNum)
    {
        if (_nextObjNum < minObjNum)
            _nextObjNum = minObjNum;
    }

    /// <summary>
    /// Exclude an object number from encryption (e.g., the /Encrypt dict itself).
    /// </summary>
    public void ExcludeFromEncryption(int objectNumber)
    {
        _excludedFromEncryption.Add(objectNumber);
    }

    private readonly HashSet<int> _keptDirect = new();

    /// <summary>Keep an object out of object streams: it is written as a plain
    /// indirect object even when <see cref="UseObjectStreams"/> would pack it.
    /// The document information dictionary asks for this -- its dates change on
    /// every save, and packed into a compressed stream a changed digit changes the
    /// deflated length, so two saves of one document a second apart would differ in
    /// size by the compressor's whim; written direct, a date is as long as the last.</summary>
    public void KeepOutOfObjectStreams(int objectNumber)
    {
        _keptDirect.Add(objectNumber);
    }

    public void WriteHeader(string version = "1.4")
    {
        WriteRaw($"%PDF-{version}\n");
        // Binary comment to signal binary content
        _output.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]); // %âãÏÓ\n
        // Producer comment matching the public API convention
        WriteRaw("%   \n");
    }

    /// <summary>
    /// Write an indirect object. Returns the object number assigned.
    /// When UseObjectStreams is true, non-stream objects that are small enough
    /// are deferred for packing into compressed object streams.
    /// </summary>
    public int WriteIndirectObject(int objNum, PdfObject obj)
    {
        // Track all objects for potential object stream packing
        _allObjects[objNum] = obj;
        if (objNum >= _nextObjNum) _nextObjNum = objNum + 1;

        // When object streams are enabled, defer eligible non-stream objects
        var hasInline = ContainsInlineStream(obj);
        if (UseObjectStreams && obj is not PdfStream && !_excludedFromEncryption.Contains(objNum)
            && !_keptDirect.Contains(objNum) && !hasInline)
        {
            var serialized = SerializeObject(obj);
            if (serialized.Length <= MaxObjectSizeForObjStm)
            {
                // Object will be packed into an ObjStm later — don't write it now
                return objNum;
            }
        }

        _offsets[objNum] = _output.Position;

        _currentObjectNumber = objNum;
        WriteRaw($"{objNum} 0 obj\n");
        WriteObject(obj);
        WriteRaw("\nendobj\n");
        _currentObjectNumber = -1;

        FlushDeferredObjects();
        return objNum;
    }

    /// <summary>Write the streams and dictionaries promoted to indirect objects while
    /// writing (or measuring) other objects. Iterative rather than recursive to avoid a
    /// stack overflow on large PDFs; writing a deferred object may itself promote more,
    /// and the loop picks those up until both queues drain. Also called before the
    /// cross-reference section, because a promotion made while measuring an object for
    /// an object stream has no later write to flush it.</summary>
    private void FlushDeferredObjects()
    {
        while (_deferredStreams.Count > 0 || _deferredDicts.Count > 0)
        {
            while (_deferredStreams.Count > 0)
            {
                var (deferredNum, deferredStream) = _deferredStreams.Dequeue();
                _allObjects[deferredNum] = deferredStream;
                _offsets[deferredNum] = _output.Position;
                if (deferredNum >= _nextObjNum) _nextObjNum = deferredNum + 1;
                _currentObjectNumber = deferredNum;
                WriteRaw($"{deferredNum} 0 obj\n");
                WriteObject(deferredStream);
                WriteRaw("\nendobj\n");
                _currentObjectNumber = -1;
            }

            while (_deferredDicts.Count > 0)
            {
                var (deferredNum, deferredDict) = _deferredDicts.Dequeue();
                _allObjects[deferredNum] = deferredDict;
                _offsets[deferredNum] = _output.Position;
                if (deferredNum >= _nextObjNum) _nextObjNum = deferredNum + 1;
                _currentObjectNumber = deferredNum;
                WriteRaw($"{deferredNum} 0 obj\n");
                WriteObject(deferredDict);
                WriteRaw("\nendobj\n");
                _currentObjectNumber = -1;
            }
        }
    }

    /// <summary>
    /// Allocate next available object number.
    /// </summary>
    public int AllocateObjectNumber() => _nextObjNum++;

    /// <summary>Ensure the next allocated object number is above <paramref name="objNum"/>,
    /// reserving a number that will be written later (e.g. an imported page's destination
    /// slot) so <see cref="AllocateObjectNumber"/> never hands it out to something else.</summary>
    public void ReserveObjectNumber(int objNum)
    {
        if (objNum >= _nextObjNum) _nextObjNum = objNum + 1;
    }

    private int _writeDepth;
    private const int MaxWriteDepth = 100;

    public void WriteObject(PdfObject obj)
    {
        if (++_writeDepth > MaxWriteDepth)
        {
            _writeDepth--;
            WriteRaw("null"); // Prevent StackOverflow on deeply nested inline structures
            return;
        }
        try
        {
        switch (obj)
        {
            // ⚠ Through UNTOUCHED, and first. Raw bytes are the one kind that is
            // not a value: whatever they say is what the file gets, which is
            // what a content stream's operators and a reserved run of spaces
            // both need. Formatting them as anything writes something else.
            case PdfRaw raw:
                WriteBytes(raw.Bytes);
                break;
            case PdfNull:
                WriteRaw("null");
                break;
            case PdfBoolean b:
                WriteRaw(b.Value ? "true" : "false");
                break;
            case PdfInteger i:
                WriteRaw(i.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case PdfReal r:
                // PdfReal.ToString writes a plain decimal (never exponential),
                // which PDF requires; a bare "G" emits e.g. "6.10352E-05" that
                // PDF parsers reject.
                WriteRaw(r.ToString());
                break;
            case PdfString s:
                WriteString(s);
                break;
            case PdfName n:
                WriteName(n);
                break;
            case PdfArray a:
                WriteArray(a);
                break;
            case PdfDictionary d:
                WriteDictionary(d);
                break;
            case PdfStream s:
                WriteStream(s);
                break;
            case PdfIndirectRef r:
                WriteRaw($"{r.ObjectNumber} {r.Generation} R");
                break;
        }
        }
        finally
        {
            _writeDepth--;
        }
    }

    private bool ShouldEncrypt =>
        _encryptor is not null && _currentObjectNumber >= 0 &&
        !_excludedFromEncryption.Contains(_currentObjectNumber);

    private void WriteString(PdfString str)
    {
        if (ShouldEncrypt && !_encryptor!.EmbeddedFilesOnly)
        {
            // Encrypt the string value and always write as hex (encrypted bytes may contain parens)
            var encrypted = _encryptor!.EncryptString(str.Value, _currentObjectNumber, 0);
            WriteRaw("<");
            WriteRaw(Compat.ToHexString(encrypted));
            WriteRaw(">");
            return;
        }

        if (str.IsHex)
        {
            WriteRaw("<");
            WriteRaw(Compat.ToHexString(str.Value));
            WriteRaw(">");
        }
        else
        {
            WriteRaw("(");
            foreach (var b in str.Value)
            {
                switch (b)
                {
                    case (byte)'(' or (byte)')' or (byte)'\\':
                        _output.WriteByte((byte)'\\');
                        _output.WriteByte(b);
                        break;
                    case (byte)'\r':
                        _output.Write("\\r"u8);
                        break;
                    case (byte)'\n':
                        _output.Write("\\n"u8);
                        break;
                    default:
                        _output.WriteByte(b);
                        break;
                }
            }
            WriteRaw(")");
        }
    }

    private void WriteName(PdfName name)
    {
        WriteRaw("/");
        foreach (var c in name.Value)
        {
            if (c is '#' || c <= ' ' || c > '~' || IsDelimiter((byte)c))
            {
                WriteRaw($"#{(int)c:X2}");
            }
            else
            {
                _output.WriteByte((byte)c);
            }
        }
    }

    /// <summary>Write an array. With <paramref name="promoteDicts"/> every inline dictionary
    /// item becomes its own indirect object: a page's /Annots holds annotation objects, never
    /// inline dictionaries, so an annotation keeps an object number that a later incremental
    /// update (and a signature-coverage walk over it) can address.</summary>
    private void WriteArray(PdfArray array, bool promoteDicts = false)
    {
        WriteRaw("[");
        var first = true;
        PdfObject? prev = null;
        foreach (var item in array)
        {
            // A name is self-delimiting, so a name that FOLLOWS a name needs no
            // separator - the serialiser writes [/Indexed/DeviceRGB 255 ...]
            // compact, and tests regex that exact shape.
            if (!first && !(prev is PdfName && item is PdfName)) WriteRaw(" ");
            // PDF spec requires streams to be indirect objects; promote inline streams in arrays
            if (item is PdfStream embeddedStream)
            {
                if (!_streamObjNums.TryGetValue(embeddedStream, out var streamObjNum))
                {
                    streamObjNum = _nextObjNum++;
                    _streamObjNums[embeddedStream] = streamObjNum;
                    _deferredStreams.Enqueue((streamObjNum, embeddedStream));
                }
                WriteObject(new PdfIndirectRef(streamObjNum, 0));
            }
            else if (item is PdfDictionary itemDict && (promoteDicts || TryPromoteSharedDict(itemDict) is not null))
            {
                WriteObject(new PdfIndirectRef(PromoteDict(itemDict), 0));
            }
            else
            {
                WriteObject(item);
            }
            first = false;
            prev = item;
        }
        WriteRaw("]");
    }

    private void WriteDictionary(PdfDictionary dict)
    {
        // Cycle detection: if this dict is already being written up the call stack,
        // promote it to an indirect object and emit a reference. Writing the dict
        // inline again would recurse forever; writing null (the previous behaviour)
        // silently dropped the reference — e.g. a radio option widget's /Parent back
        // to its group, which then failed to round-trip.
        if (!_dictStack.Add(dict))
        {
            if (!_promotedDicts.TryGetValue(dict, out var cycleObjNum))
            {
                cycleObjNum = _nextObjNum++;
                _promotedDicts[dict] = cycleObjNum;
                _deferredDicts.Enqueue((cycleObjNum, dict));
            }
            WriteRaw($"{cycleObjNum} 0 R");
            return;
        }
        try
        {
        WriteRaw("<<");
        foreach (var key in dict.Keys)
        {
            WriteName(new PdfName(key));
            var val = dict.Get(key)!;
            // PDF spec requires streams to be indirect objects; promote inline streams.
            // Deduplicate: if the same PdfStream instance was already promoted, reuse its obj number.
            if (val is PdfStream embeddedStream)
            {
                if (!_streamObjNums.TryGetValue(embeddedStream, out var streamObjNum))
                {
                    streamObjNum = _nextObjNum++;
                    _streamObjNums[embeddedStream] = streamObjNum;
                    _deferredStreams.Enqueue((streamObjNum, embeddedStream));
                }
                WriteRaw(" "); // indirect ref begins with a digit, so a separator is required
                WriteObject(new PdfIndirectRef(streamObjNum, 0));
            }
            else if (val is PdfDictionary sharedDict && TryPromoteSharedDict(sharedDict) is { } valDictNum)
            {
                WriteRaw(" "); // indirect ref begins with a digit, so a separator is required
                WriteObject(new PdfIndirectRef(valDictNum, 0));
            }
            else if (key == AnnotsKey && val is PdfArray annots)
            {
                WriteArray(annots, promoteDicts: true);
            }
            else
            {
                // Only insert a separating space when the value's token does not begin with a
                // self-delimiting character. Name (/), array ([) and string ((, <) values need no
                // space after the key, matching the compact form (e.g. "/Type/Metadata"). Numbers,
                // booleans, null, indirect refs and (cycle-promotable) dictionaries still need one.
                if (!StartsWithDelimiter(val)) WriteRaw(" ");
                WriteObject(val);
            }
        }
        WriteRaw(">>");
        }
        finally
        {
            _dictStack.Remove(dict);
        }
    }

    /// <summary>
    /// Whether the serialized form of <paramref name="val"/> begins with a self-delimiting
    /// character, in which case no separating space is needed after a preceding dictionary key.
    /// Dictionaries are excluded because cycle detection may promote them to an indirect ref
    /// (which begins with a digit) at write time.
    /// </summary>
    private static bool StartsWithDelimiter(PdfObject val) =>
        val is PdfName or PdfArray or PdfString;

    /// <summary>
    /// Pre-scan the inline object graph rooted at <paramref name="root"/> and record every
    /// dictionary that is reached inline from more than one place. Such dictionaries are written
    /// once as a shared indirect object so that all references resolve to it, instead of being
    /// duplicated inline (or, when the references form a cycle, silently dropped).
    /// </summary>
    internal void MarkSharedDicts(PdfObject? root)
    {
        if (root is null) return;
        var refCount = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        CountInlineRefs(root, refCount, visited);
        foreach (var kv in refCount)
            if (kv.Value >= 2)
                _sharedDicts.Add(kv.Key);
    }

    private static void CountInlineRefs(PdfObject obj, Dictionary<PdfDictionary, int> refCount, HashSet<PdfObject> visited)
    {
        switch (obj)
        {
            case PdfDictionary dict:
                refCount.TryGetValue(dict, out var c);
                refCount[dict] = c + 1;
                if (!visited.Add(dict)) return; // already descended; the extra reference is now counted
                foreach (var key in dict.Keys)
                    CountInlineRefs(dict.Get(key)!, refCount, visited);
                break;
            case PdfArray arr:
                if (!visited.Add(arr)) return;
                foreach (var item in arr)
                    CountInlineRefs(item, refCount, visited);
                break;
            // PdfIndirectRef and PdfStream are (or become) separate objects; scalars are leaves.
        }
    }

    /// <summary>
    /// If <paramref name="dict"/> is a shared inline dictionary, ensure it has an allocated
    /// indirect object number (allocating and queueing it for deferred writing on first use)
    /// and return that number so the caller can emit a reference instead of an inline copy.
    /// </summary>
    private int? TryPromoteSharedDict(PdfDictionary dict)
    {
        int objNum = default;
        if (_promotedDicts.TryGetValue(dict, out objNum)) return objNum;
        if (_sharedDicts.Contains(dict))
        {
            objNum = PromoteDict(dict);
            return objNum;
        }
        objNum = 0;
        return null;
    }

    /// <summary>The dictionary's promoted object number, allocated and queued for the
    /// deferred write on first use.</summary>
    private int PromoteDict(PdfDictionary dict)
    {
        if (_promotedDicts.TryGetValue(dict, out var objNum)) return objNum;
        objNum = _nextObjNum++;
        _promotedDicts[dict] = objNum;
        _deferredDicts.Enqueue((objNum, dict));
        return objNum;
    }

    /// <summary>The page key whose array items are always written as indirect objects.</summary>
    private const string AnnotsKey = "Annots";

    private void WriteStream(PdfStream stream)
    {
        // Check if the stream already has a filter that shouldn't be re-compressed
        // (JPEG, JPEG2000, JBIG2, or already FlateDecode-compressed)
        var existingFilter = GetExistingFilter(stream.Dict);
        var isPassThrough = existingFilter is not null;

        byte[] data;
        var dict = CloneDictionary(stream.Dict);

        if (stream.DoNotCompress)
        {
            // Caller requires the bytes verbatim (e.g. an embedded file added
            // with FileEncoding.None) — write raw with no /Filter.
            data = stream.RawData;
            dict.Remove("Filter");
        }
        else if (isPassThrough)
        {
            // Pass through: keep the raw data and original filter as-is
            data = stream.RawData;
            // dict already has the correct Filter from CloneDictionary

            // PDF/A saves re-compress plain Flate streams at the strongest level
            // (the conversion save does this; without
            // it, outputs carried over from weakly-deflated producers come out
            // needlessly oversized). Streams with DecodeParms (predictors) are
            // left untouched — recompressing would need the parms re-applied.
            if (RecompressFlateStreams && existingFilter == "FlateDecode"
                && dict.Get("DecodeParms") is null && dict.Get("DP") is null)
            {
                try
                {
                    var raw = Filters.FlateDecodeFilter.Decode(stream.RawData, null);
                    var recompressed = Compress(raw);
                    if (recompressed.Length < data.Length) data = recompressed;
                }
                catch { /* keep the original bytes on any decode failure */ }
            }
        }
        else
        {
            // Try to compress with FlateDecode
            var compressed = Compress(stream.RawData);
            var useCompression = compressed.Length < stream.RawData.Length;
            data = useCompression ? compressed : stream.RawData;
            if (useCompression)
                dict.Set("Filter", new PdfName("FlateDecode"));
            else
                dict.Remove("Filter");
        }

        // Encrypt after compression but before writing (embedded files alone when only they are)
        if (ShouldEncrypt && (!_encryptor!.EmbeddedFilesOnly || dict.GetName("Type") == "EmbeddedFile"))
        {
            data = _encryptor!.EncryptStream(data, _currentObjectNumber, 0);
        }

        dict.Set("Length", new PdfInteger(data.Length));

        WriteDictionary(dict);
        WriteRaw("\nstream\n");
        _output.Write(data);
        WriteRaw("\nendstream");
    }

    private static string? GetExistingFilter(PdfDictionary dict)
    {
        var f = dict.Get("Filter");
        return f switch
        {
            PdfName n => n.Value,
            PdfArray a when a.Count > 0 && a[0] is PdfName n2 => n2.Value,
            _ => null,
        };
    }

    /// <summary>
    /// Write the cross-reference table and trailer, then %%EOF.
    /// </summary>
    public void WriteXRefAndTrailer(PdfDictionary trailerEntries)
    {
        FlushDeferredObjects();
        if (UseObjectStreams)
        {
            // Object streams require xref streams (type-2 entries)
            WriteObjectStreamsAndXRefStream(trailerEntries);
            return;
        }

        if (UseXRefStream)
        {
            WriteXRefStream(trailerEntries);
            return;
        }

        WriteTraditionalXRef(trailerEntries);
    }

    /// <summary>
    /// Write a traditional cross-reference table and trailer.
    /// </summary>
    private void WriteTraditionalXRef(PdfDictionary trailerEntries)
    {
        var xrefOffset = _output.Position;

        // Find the range of object numbers
        var maxObjNum = 0;
        foreach (var num in _offsets.Keys)
        {
            if (num > maxObjNum) maxObjNum = num;
        }

        WriteRaw("xref\n");
        WriteRaw($"0 {maxObjNum + 1}\n");

        // Object 0: free entry
        WriteRaw("0000000000 65535 f \n");

        for (var i = 1; i <= maxObjNum; i++)
        {
            if (_offsets.TryGetValue(i, out var offset))
            {
                WriteRaw($"{offset:D10} 00000 n \n");
            }
            else
            {
                WriteRaw("0000000000 65535 f \n");
            }
        }

        // Trailer
        trailerEntries.Set("Size", new PdfInteger(maxObjNum + 1));

        WriteRaw("trailer\n");
        WriteDictionary(trailerEntries);
        WriteRaw($"\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    /// <summary>
    /// Write a cross-reference stream (PDF 1.5+, §7.5.8) instead of a traditional xref table.
    /// The xref stream is itself an indirect object with /Type /XRef.
    /// </summary>
    private void WriteXRefStream(PdfDictionary trailerEntries)
    {
        WriteXRefStreamCore(trailerEntries, compressedEntries: null);
    }

    /// <summary>
    /// Core method that writes the xref stream. Accepts optional compressed (type-2) entries
    /// from object streams.
    /// </summary>
    /// <param name="trailerEntries">Trailer entries (/Root, /Info, /ID, etc.)</param>
    /// <param name="compressedEntries">Map of objNum → (streamObjNum, indexInStream) for type-2 entries</param>
    private void WriteXRefStreamCore(PdfDictionary trailerEntries, Dictionary<int, (int streamObjNum, int indexInStream)>? compressedEntries)
    {
        var xw = new XRefStreamWriteState();
        xw.trailerEntries = trailerEntries;
        xw.compressedEntries = compressedEntries;
        PlanXRefStreamLayout(xw);
        xw.w3 = ByteWidth(xw.maxField3);

        xw.entrySize = 1 + xw.w2 + xw.w3;
        xw.streamData = new byte[xw.size * xw.entrySize];

        // Object 0: free entry (type=0, next free=0, gen=65535)
        xw.streamData[0] = 0; // type 0
        WriteField(xw.streamData, 1, xw.w2, 0); // next free obj
        WriteField(xw.streamData, 1 + xw.w2, xw.w3, 65535); // gen

        WriteXRefStreamEntries(xw);

        xw.compressedData = Compress(xw.streamData);

        xw.xrefDict = new PdfDictionary();
        xw.xrefDict.Set("Type", new PdfName("XRef"));
        xw.xrefDict.Set("Size", new PdfInteger(xw.size));
        xw.wArray = new PdfArray();
        xw.wArray.Add(new PdfInteger(1));
        xw.wArray.Add(new PdfInteger(xw.w2));
        xw.wArray.Add(new PdfInteger(xw.w3));
        xw.xrefDict.Set("W", xw.wArray);
        xw.xrefDict.Set("Filter", new PdfName("FlateDecode"));
        xw.xrefDict.Set("Length", new PdfInteger(xw.compressedData.Length));

        // Copy trailer entries into the xref stream dict
        foreach (var key in xw.trailerEntries.Keys)
        {
            if (key is "Size" or "Type" or "W" or "Filter" or "Length") continue;
            var val = xw.trailerEntries.Get(key);
            if (val is not null) xw.xrefDict.Set(key, val);
        }

        // Write the xref stream as an indirect object
        _currentObjectNumber = xw.xrefObjNum;
        WriteRaw($"{xw.xrefObjNum} 0 obj\n");
        WriteDictionary(xw.xrefDict);
        WriteRaw("\nstream\n");
        _output.Write(xw.compressedData);
        WriteRaw("\nendstream\nendobj\n");
        _currentObjectNumber = -1;

        WriteRaw($"startxref\n{xw.xrefOffset}\n%%EOF\n");
    }

    /// <summary>
    /// Pack eligible objects into object streams (§7.5.7) then write xref stream.
    /// </summary>
    private void WriteObjectStreamsAndXRefStream(PdfDictionary trailerEntries)
    {
        var ow = new ObjectStreamWriteState();
        ow.trailerEntries = trailerEntries;
        ow.eligible = new List<int>();
        foreach (var (objNum, obj) in _allObjects)
        {
            if (obj is PdfStream) continue; // Streams cannot go in ObjStm
            if (_excludedFromEncryption.Contains(objNum) || _keptDirect.Contains(objNum)) continue;
            if (ContainsInlineStream(obj)) continue; // Dicts with inline streams need promotion

            // Check serialized size
            var serialized = SerializeObject(obj);
            if (serialized.Length > MaxObjectSizeForObjStm) continue;

            ow.eligible.Add(objNum);
        }

        // Pack in a deterministic order (ascending object number) rather than the order
        // objects happened to be enumerated from _allObjects, which varies with the source
        // file's physical layout. Canonical ordering makes a load/save round-trip
        // byte-stable — re-saving an already-saved file reproduces the same ObjStm instead
        // of a re-ordered (and differently-compressed) one. Sequential object
        // numbers also tend to group related objects, which compresses slightly better.
        ow.eligible.Sort();

        if (ow.eligible.Count == 0)
        {
            // No eligible objects — just write xref stream without object streams
            WriteXRefStream(ow.trailerEntries);
            return;
        }

        // Clear the output and rewrite — we need to rewrite because objects that go into
        // ObjStm should not also appear as standalone indirect objects
        // Strategy: we already wrote all objects. Now we'll write ObjStm objects and
        // build the compressed entries map. The standalone objects are already written,
        // but we'll remove their offsets for ones going into ObjStm.

        ow.compressedEntries = new Dictionary<int, (int streamObjNum, int indexInStream)>();
        ow.groups = new List<List<int>>();
        for (var i = 0; i < ow.eligible.Count; i += MaxObjectsPerStream)
        {
            var count = Math.Min(MaxObjectsPerStream, ow.eligible.Count - i);
            ow.groups.Add(ow.eligible.GetRange(i, count));
        }

        // Write each group as an ObjStm
        foreach (var group in ow.groups)
        {
            WriteObjectStreamGroup(ow, group);
        }

        // Write xref stream with the compressed entries
        WriteXRefStreamCore(ow.trailerEntries, ow.compressedEntries);
    }

    /// <summary>
    /// Check if an object (dict or array) contains inline PdfStream values.
    /// Such objects cannot be packed into ObjStm because the streams need their own indirect objects.
    /// </summary>
    private static bool ContainsInlineStream(PdfObject obj)
        => ContainsInlineStream(obj, new HashSet<PdfObject>(ReferenceEqualityComparer.Instance));

    private static bool ContainsInlineStream(PdfObject obj, HashSet<PdfObject> seen)
    {
        if (!seen.Add(obj)) return false; // cycle guard
        if (obj is PdfStream) return true;
        if (obj is PdfDictionary dict)
        {
            foreach (var key in dict.Keys)
            {
                var val = dict.Get(key);
                if (val is null) continue;
                // Indirect refs are followed by the writer separately; skip them
                // here so we only catch genuinely inline structures.
                if (val is PdfIndirectRef) continue;
                if (ContainsInlineStream(val, seen)) return true;
            }
        }
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                if (item is null) continue;
                if (item is PdfIndirectRef) continue;
                if (ContainsInlineStream(item, seen)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Serialize a PdfObject to bytes (for measuring size and packing into ObjStm).
    /// </summary>
    /// <summary>The object's serialised bytes, written by THIS writer into a scratch
    /// buffer so that a stream or dictionary promoted to an indirect object during the
    /// measurement keeps its number and its place in the deferred queue - a throwaway
    /// writer would allocate the number in its own space and never write the object,
    /// leaving a dangling reference. Strings are not encrypted here: an object inside an
    /// object stream is protected by the stream's own encryption.</summary>
    private byte[] SerializeObject(PdfObject obj)
    {
        var file = _output;
        var objectNumber = _currentObjectNumber;
        using var scratch = new MemoryStream();
        _output = scratch;
        _currentObjectNumber = -1;
        try
        {
            WriteObject(obj);
        }
        finally
        {
            _output = file;
            _currentObjectNumber = objectNumber;
        }
        return scratch.ToArray();
    }

    /// <summary>
    /// Write a big-endian integer value into a byte array field of the given width.
    /// </summary>
    private static void WriteField(byte[] data, int offset, int width, long value)
    {
        for (var i = width - 1; i >= 0; i--)
        {
            data[offset + i] = (byte)(value & 0xFF);
            value >>= 8;
        }
    }

    /// <summary>
    /// Determine the minimum number of bytes needed to represent a value.
    /// </summary>
    private static int ByteWidth(long value)
    {
        if (value <= 0xFF) return 1;
        if (value <= 0xFFFF) return 2;
        if (value <= 0xFFFFFF) return 3;
        if (value <= 0xFFFFFFFFL) return 4;
        return 5;
    }

    private void WriteRaw(string text) =>
        _output.Write(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Bytes straight to the file, encoded by nothing.
    ///
    /// ⚠⚠ NOT WriteRaw. That takes a string and encodes it as ASCII, which
    /// replaces every byte above 127 with a question mark — silently, and only
    /// in the documents that happen to contain one.
    /// </summary>
    private void WriteBytes(byte[] bytes) => _output.Write(bytes);

    private static byte[] Compress(byte[] data) => Filters.ManagedDeflater.DeflateZlib(data, Filters.DeflateLevel.Best);

    private static PdfDictionary CloneDictionary(PdfDictionary source)
    {
        var clone = new PdfDictionary();
        foreach (var key in source.Keys)
        {
            var value = source.Get(key);
            if (value is not null)
                clone.Set(key, value);
        }
        return clone;
    }

    private static bool IsDelimiter(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or
             (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or
             (byte)'/' or (byte)'%';
}
