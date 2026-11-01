using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

/// <summary>
/// Appends modifications to a PDF document without rewriting the original bytes.
/// Spec: PDF32000_2008 §7.5.6 (Incremental Updates)
/// </summary>
internal sealed class IncrementalWriter
{
    private readonly Stream _output;
    private readonly byte[] _originalData;
    private readonly Dictionary<int, (int generation, PdfObject obj)> _modifiedObjects = new();
    private int _nextObjectNumber;

    /// <summary>Streams encountered as direct values during write are
    /// promoted to indirect objects (PDF spec requires it). This map keeps
    /// each PdfStream's assigned object number so multiple references to
    /// the same instance share one indirect object.</summary>
    private readonly Dictionary<PdfStream, int> _promotedStreams = new();

    /// <summary>Dictionaries written as an object of their own: held as a value anywhere in the
    /// update, each is written as a reference to that object, never as a copy of it.</summary>
    private readonly Dictionary<PdfDictionary, int> _writtenAs = new(ReferenceEqualityComparer.Instance);

    /// <summary>Streams and dictionaries whose object body still needs to be written —
    /// drained at the end of <see cref="Flush"/>.</summary>
    private readonly Queue<(int objNum, PdfObject body)> _deferredBodies = new();

    /// <summary>The entries that hold annotations, fields and pages: PDF 32000-1 tables 30, 220
    /// and 164 make their items indirect objects, so an item held inline gets an object of its own.</summary>
    private static readonly HashSet<string> ArraysOfObjects = new(StringComparer.Ordinal) { "Annots", "Fields", "Kids" };

    /// <summary>A field's or a page's parent, an indirect reference by the same tables.</summary>
    private const string ParentKey = "Parent";

    /// <summary>When the source document is encrypted (the copied trailer keeps its
    /// /Encrypt), every string and every NEW stream appended here must be encrypted
    /// with the per-object key — readers decrypt the whole file uniformly, so a
    /// plaintext appendix comes back as garbage. Source-loaded streams
    /// (<see cref="PdfStream.ObjectNumber"/> &gt; 0) still hold their original
    /// ciphertext and are written verbatim.</summary>
    private readonly Security.PdfDecryptor? _encryptor;

    /// <summary>Object/generation of the indirect object currently being written —
    /// the encryption key context. −1 outside an object body (e.g. the trailer,
    /// which is never encrypted).</summary>
    private int _curObjNum = -1;
    private int _curGen;

    public IncrementalWriter(Stream output, byte[] originalData, int startObjectNumber,
        Security.PdfDecryptor? encryptor = null)
    {
        _output = output;
        _originalData = originalData;
        _nextObjectNumber = startObjectNumber;
        _encryptor = encryptor;
    }

    /// <summary>
    /// Register a modified or new object to be written, under the generation the file gives its number
    /// (a reference names both, so an edited object keeps its own).
    /// </summary>
    public void WriteObject(int objectNumber, PdfObject obj, int generation = 0)
    {
        _modifiedObjects[objectNumber] = (generation, obj);
        if (obj is PdfDictionary dict) _writtenAs.TryAdd(dict, objectNumber);
        else if (obj is PdfStream stream) _promotedStreams.TryAdd(stream, objectNumber);
    }

    /// <summary>Name the object a dictionary is (or stays) in the file: held as a value by an object
    /// of the update - a page as a widget's /P - it is written as a reference to it.</summary>
    public void WrittenAs(PdfDictionary dict, int objectNumber) => _writtenAs.TryAdd(dict, objectNumber);

    /// <summary>
    /// Allocate a new object number.
    /// </summary>
    public int AllocateObjectNumber() => _nextObjectNumber++;

    /// <summary>
    /// Flush the incremental update: copy original bytes, append modified objects,
    /// write new xref + trailer.
    /// </summary>
    public void Flush(PdfDictionary originalTrailer, long originalStartXref)
    {
        // 1. Copy the entire original PDF. The output may be the very stream the
        // document was read from (Save() writes back into its source FileStream,
        // whose position is then at EOF) — rewind first, or the original bytes
        // are appended AFTER themselves and the file doubles every save.
        if (_output.CanSeek) _output.Seek(0, SeekOrigin.Begin);
        _output.Write(_originalData);

        // Nothing changed: an update would be a cross-reference section with no subsection,
        // which readers take for damage, so the file stays as it was.
        if (_modifiedObjects.Count == 0) return;

        // 1b. Producer comment
        WriteLn("%   ");

        // 2. Write modified/new objects, track offsets
        var offsets = new Dictionary<int, long>();
        foreach (var (objNum, (gen, obj)) in _modifiedObjects)
        {
            offsets[objNum] = _output.Position;
            WriteIndirectObject(objNum, gen, obj);
        }

        // 2b. Drain the streams and dictionaries promoted to objects of their own (assigned
        //     during step 2 and possibly during this loop itself if one is reachable from
        //     another's body).
        while (_deferredBodies.Count > 0)
        {
            var (objNum, body) = _deferredBodies.Dequeue();
            offsets[objNum] = _output.Position;
            WriteIndirectObject(objNum, 0, body);
        }

        // 3. Write new xref table (only modified entries)
        var xrefOffset = _output.Position;
        WriteLn("xref");

        // Group consecutive object numbers into subsections
        var sortedNums = offsets.Keys.OrderBy(k => k).ToList();
        var subsections = GroupConsecutive(sortedNums);

        foreach (var (start, count) in subsections)
        {
            WriteLn($"{start} {count}");
            for (var i = start; i < start + count; i++)
            {
                var gen = _modifiedObjects.TryGetValue(i, out var written) ? written.generation : 0;
                WriteLn($"{offsets[i]:D10} {gen:D5} n ");
            }
        }

        // 4. Write new trailer
        var newTrailer = new PdfDictionary();
        // Copy /Root, /Info, /Encrypt, /ID (and a MAC's /AuthCode) from original
        foreach (var key in new[] { "Root", "Info", "Encrypt", "ID", "AuthCode" })
        {
            var val = originalTrailer.Get(key);
            if (val is not null) newTrailer.Set(key, val);
        }

        // Set /Size to total objects, /Prev to original xref offset
        newTrailer.Set("Size", new PdfInteger(_nextObjectNumber));
        newTrailer.Set("Prev", new PdfInteger(originalStartXref));

        WriteLn("trailer");
        WriteObject(newTrailer);
        Write("\n");
        WriteLn("startxref");
        WriteLn(xrefOffset.ToString(CultureInfo.InvariantCulture));
        WriteLn("%%EOF");
    }

    private void WriteIndirectObject(int objNum, int gen, PdfObject obj)
    {
        _curObjNum = objNum;
        _curGen = gen;
        try
        {
            WriteLn($"{objNum} {gen} obj");
            // An array that is an object of its own is a page's /Annots, a form's /Fields, a node's
            // /Kids held by reference: an item added to it inline gets an object of its own too.
            if (obj is PdfArray items) WriteContainerObject(items, itemsAreObjects: true);
            else WriteObject(obj);
            Write("\n");
            WriteLn("endobj");
        }
        finally
        {
            _curObjNum = -1;
            _curGen = 0;
        }
    }

    /// <summary>Containers currently being written — detects direct-object cycles
    /// that would otherwise recurse to a StackOverflow and kill the process.</summary>
    private readonly HashSet<PdfObject> _inFlight = new(ReferenceEqualityComparer.Instance);

    private void WriteObject(PdfObject obj)
    {
        switch (obj)
        {
            case PdfNull: case PdfBoolean: case PdfInteger: case PdfReal: case PdfString: case PdfName: case PdfIndirectRef:
                WriteScalarObject(obj);
                break;
            case PdfArray: case PdfDictionary: case PdfStream:
                WriteContainerObject(obj);
                break;
        }
    }

    /// <summary>Assign (or look up) an indirect object number for a stream
    /// encountered as a direct value, and queue its body for later writing.
    /// Returns the indirect-ref token to splice into the calling context.</summary>
    private PdfIndirectRef PromoteStream(PdfStream stream)
    {
        if (!_promotedStreams.TryGetValue(stream, out var num))
        {
            num = _nextObjectNumber++;
            _promotedStreams[stream] = num;
            _deferredBodies.Enqueue((num, stream));
        }
        return new PdfIndirectRef(num, 0);
    }

    /// <summary>The reference a dictionary held as a value is written as: the object it is written as, or,
    /// when <paramref name="promote"/>, a new object for it, queued for writing. Null to write it inline.</summary>
    private PdfIndirectRef? ReferenceFor(PdfDictionary dict, bool promote)
    {
        if (!_writtenAs.TryGetValue(dict, out var num))
        {
            if (!promote) return null;
            num = _nextObjectNumber++;
            _writtenAs[dict] = num;
            _deferredBodies.Enqueue((num, dict));
        }
        return new PdfIndirectRef(num, 0);
    }

    /// <summary>A value inside a container: a stream, and a dictionary that is (or must be) an object of
    /// its own, are written as references.</summary>
    private void WriteValue(PdfObject value, bool promoteDict)
    {
        if (value is PdfStream s) WriteObject(PromoteStream(s));
        else if (value is PdfDictionary d && ReferenceFor(d, promoteDict) is { } dictRef) WriteObject(dictRef);
        else WriteObject(value);
    }

    private void Write(string text)
    {
        _output.Write(Encoding.ASCII.GetBytes(text));
    }

    private void WriteLn(string text)
    {
        Write(text);
        _output.WriteByte((byte)'\n');
    }

    private static List<(int start, int count)> GroupConsecutive(List<int> numbers)
    {
        var result = new List<(int, int)>();
        if (numbers.Count == 0) return result;

        var start = numbers[0];
        var count = 1;

        for (var i = 1; i < numbers.Count; i++)
        {
            if (numbers[i] == start + count)
            {
                count++;
            }
            else
            {
                result.Add((start, count));
                start = numbers[i];
                count = 1;
            }
        }
        result.Add((start, count));
        return result;
    }

    /// <summary>The scalar objects: null, booleans, numbers, strings, names and indirect references.</summary>
    private void WriteScalarObject(PdfObject obj)
    {
        switch (obj)
        {
            case PdfNull:
                Write("null");
                break;
            case PdfBoolean b:
                Write(b.Value ? "true" : "false");
                break;
            case PdfInteger i:
                Write(i.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case PdfReal r:
                // Plain decimal only — PDF forbids exponential reals like
                // "6.10352E-05" (PdfReal.ToString handles the expansion).
                Write(r.ToString());
                break;
            case PdfString s:
                // In-memory strings are plaintext (the reader decrypts at parse); under
                // an encrypted trailer they must be re-encrypted with the
                // owning object's key. Ciphertext is binary — hex form keeps it
                // byte-exact (a literal string would normalise CR bytes).
                var strBytes = s.Value;
                var writeHex = s.IsHex;
                if (_encryptor is not null && _curObjNum > 0)
                {
                    strBytes = _encryptor.EncryptString(strBytes, _curObjNum, _curGen);
                    writeHex = true;
                }
                if (writeHex)
                {
                    Write($"<{Compat.ToHexString(strBytes)}>");
                }
                else
                {
                    PdfWriter.WriteLiteralString(_output, strBytes);
                }
                break;
            case PdfName n:
                PdfWriter.WriteNameToken(_output, n.Value);
                break;
            case PdfIndirectRef iref:
                Write($"{iref.ObjectNumber} {iref.Generation} R");
                break;
        }
    }

    /// <summary>The container objects: arrays, dictionaries and streams, each written with its members.</summary>
    private void WriteContainerObject(PdfObject obj, bool itemsAreObjects = false)
    {
        switch (obj)
        {
            case PdfArray arr:
                // A DIRECT container cycle (self-referential graph without indirect refs)
                // must not recurse forever: emit null for the back-edge.
                if (!_inFlight.Add(arr)) { Write("null"); break; }
                try
                {
                    Write("[");
                    for (var i = 0; i < arr.Count; i++)
                    {
                        if (i > 0) Write(" ");
                        WriteValue(arr[i], itemsAreObjects);
                    }
                    Write("]");
                }
                finally { _inFlight.Remove(arr); }
                break;
            case PdfDictionary dict:
                if (!_inFlight.Add(dict)) { Write("null"); break; }
                try
                {
                    Write("<< ");
                    foreach (var key in dict.Keys)
                    {
                        PdfWriter.WriteNameToken(_output, key);
                        Write(" ");
                        var v = dict.Get(key)!;
                        if (v is PdfArray items && ArraysOfObjects.Contains(key)) WriteContainerObject(items, itemsAreObjects: true);
                        else WriteValue(v, key == ParentKey);
                        Write(" ");
                    }
                    Write(">>");
                }
                finally { _inFlight.Remove(dict); }
                break;
            case PdfStream stream:
                // A NEW in-memory stream (ObjectNumber == 0, e.g. a regenerated
                // appearance) holds plaintext and must be encrypted under the
                // encrypted trailer, as does a source-loaded stream whose data was
                // replaced (a filled XFA datasets packet); a source-loaded stream
                // left as read still holds its original ciphertext keyed to its own
                // number and is copied verbatim.
                var body = stream.RawData;
                if (_encryptor is not null && _curObjNum > 0 && (stream.ObjectNumber == 0 || stream.DataReplaced))
                    body = _encryptor.EncryptStream(body, _curObjNum, _curGen);
                stream.Dict.Set("Length", new PdfInteger(body.Length));
                WriteObject(stream.Dict);
                Write("\nstream\n");
                _output.Write(body);
                Write("\nendstream");
                break;
        }
    }
}
