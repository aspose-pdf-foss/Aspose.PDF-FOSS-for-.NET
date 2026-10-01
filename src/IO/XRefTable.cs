using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

internal sealed partial class XRefTable
{
    private readonly Dictionary<int, XRefEntry> _entries = new();
    private readonly HashSet<(int objNum, long offset)> _xrefStreamObjNums = new();
    private PdfDictionary? _trailer;

    public PdfDictionary Trailer => _trailer ?? throw new InvalidOperationException("No trailer found");
    public IReadOnlyDictionary<int, XRefEntry> Entries => _entries;

    /// <summary>True when any node of the cross-reference chain (primary,
    /// /Prev, or hybrid /XRefStm) was a cross-reference STREAM (/Type /XRef).
    /// PDF/A-1 prohibits xref streams, so validation reports this.</summary>
    public bool UsedXrefStream { get; private set; }

    /// <summary>True when this table was rebuilt by <c>RecoverXref</c>'s full-file
    /// scan because the declared cross-reference structure could not be read.
    /// The cross-document merge path uses this to apply its
    /// strict-xref semantics (see PageCollection.Add(PageCollection)).</summary>
    public bool RecoveredByScan { get; internal set; }

    /// <summary>True when the final startxref was broken and this table is the last
    /// cross-reference STREAM recovered by scanning back from the end of file. An
    /// object such a table has no entry for resolves by a file-wide header search;
    /// one the file holds nowhere is a NULL slot (probed).</summary>
    public bool RecoveredFromBrokenTail { get; internal set; }

    /// <summary>
    /// Object numbers of the source file's cross-reference infrastructure: the cross-reference
    /// stream objects (/Type /XRef) and the object-stream containers (/Type /ObjStm) that hold
    /// compressed objects. The writer regenerates all of this from scratch, so on re-save these
    /// originals are skipped rather than carried over as dead, unreferenced streams. Exposing the
    /// set also lets the writer keep object numbering stable across a load/save round-trip.
    /// </summary>
    public HashSet<int> InfrastructureObjectNumbers()
    {
        var infra = new HashSet<int>();
        foreach (var (objNum, offset) in _xrefStreamObjNums)
        {
            // An incremental update can REUSE a superseded xref stream's object
            // number for a live object (a page content stream can sit at the object
            // number an older increment used for its xref stream). The number is
            // infrastructure only while the merged table still points AT that xref
            // stream; if the newest chain redirected it to other data, the object is
            // live and must be carried over on save.
            if (!_entries.TryGetValue(objNum, out var e) || !e.InUse
                || (!e.IsCompressed && e.Offset == offset))
                infra.Add(objNum);
        }
        foreach (var entry in _entries.Values)
            if (entry.IsCompressed)
                infra.Add(entry.StreamObjectNumber);
        return infra;
    }

    public XRefEntry? GetEntry(int objectNumber) =>
        _entries.TryGetValue(objectNumber, out var entry) ? entry : null;

    /// <summary>
    /// Add or update an xref entry (used during xref recovery).
    /// First occurrence wins — later entries are ignored.
    /// </summary>
    internal void AddEntry(int objectNumber, XRefEntry entry)
    {
        _entries.TryAdd(objectNumber, entry);
    }

    /// <summary>
    /// Set (overwrite) an xref entry. Used during recovery where last occurrence wins.
    /// </summary>
    internal void SetEntry(int objectNumber, XRefEntry entry)
    {
        _entries[objectNumber] = entry;
    }

    /// <summary>
    /// Build a synthetic trailer dictionary from recovered objects.
    /// Scans for the Catalog object to set /Root.
    /// </summary>
    internal void BuildSyntheticTrailer(byte[] data, int size)
    {
        if (_trailer is not null) return;

        var trailer = new PdfDictionary();
        trailer.Set("Size", new PdfInteger(size));

        // Find a Catalog object from recovered entries
        var parser = new PdfParser(data);
        foreach (var (objNum, entry) in _entries)
        {
            if (!entry.InUse || entry.IsCompressed) continue;
            try
            {
                parser.Lexer.Position = entry.Offset;
                var indirect = parser.ParseIndirectObject();
                if (indirect.Value is PdfDictionary dict)
                {
                    var type = dict.GetName("Type");
                    if (type == "Catalog")
                    {
                        trailer.Set("Root", new PdfIndirectRef(objNum, entry.Generation));
                        break;
                    }
                }
            }
            catch
            {
                // Skip unparseable objects
            }
        }

        // Fallback for concatenated PDFs: when duplicate object numbers cause the
        // recovery scanner (last-wins) to pick a non-catalog version, scan the raw
        // file for "trailer" dictionaries and use the first valid /Root found.
        if (!trailer.ContainsKey("Root"))
        {
            ScanTrailersForRoot(data, trailer);
        }

        AdoptScannedTrailerKeys(data, trailer);
        _trailer = trailer;
    }

    // A file whose table is wrong usually still carries its trailer: the last parseable one names the
    // document information, the file identifier and the encryption, which the recovered table cannot.
    private static readonly string[] ScannedTrailerKeys = { "Info", "ID", "Encrypt" };

    private void AdoptScannedTrailerKeys(byte[] data, PdfDictionary trailer)
    {
        var text = System.Text.Encoding.ASCII.GetString(data);
        for (var pos = text.LastIndexOf("trailer", StringComparison.Ordinal); pos >= 0;
             pos = pos == 0 ? -1 : text.LastIndexOf("trailer", pos - 1, StringComparison.Ordinal))
        {
            var at = pos + "trailer".Length;
            while (at < data.Length && PdfLexer.IsWhitespace(data[at])) at++;
            if (at >= data.Length || data[at] != '<') continue;
            PdfDictionary? scanned;
            try
            {
                var parser = new PdfParser(data);
                parser.Lexer.Position = at;
                scanned = parser.ParseObject() as PdfDictionary;
            }
            catch { continue; /* a malformed trailer: try an earlier one */ }
            if (scanned is null) continue;
            foreach (var key in ScannedTrailerKeys)
            {
                if (trailer.ContainsKey(key) || scanned.Get(key) is not { } value) continue;
                if (value is PdfIndirectRef reference && !_entries.ContainsKey(reference.ObjectNumber)) continue;
                trailer.Set(key, value);
            }
            return;
        }
    }

    /// <summary>
    /// Scan raw PDF data for trailer dictionaries to find a /Root reference.
    /// Used as a last-resort fallback when normal object-based catalog scanning fails
    /// (e.g. concatenated PDFs where duplicate objects mask the real catalog).
    /// </summary>
    private void ScanTrailersForRoot(byte[] data, PdfDictionary trailer)
    {
        var text = System.Text.Encoding.ASCII.GetString(data);
        int pos = 0;
        while ((pos = text.IndexOf("trailer", pos, StringComparison.Ordinal)) >= 0)
        {
            pos += 7;
            try
            {
                var parser = new PdfParser(data);
                // Skip whitespace after "trailer"
                var parsePos = pos;
                while (parsePos < data.Length && (data[parsePos] == ' ' || data[parsePos] == '\r' || data[parsePos] == '\n' || data[parsePos] == '\t'))
                    parsePos++;
                if (parsePos >= data.Length || data[parsePos] != '<') continue;

                parser.Lexer.Position = parsePos;
                var obj = parser.ParseObject();
                if (obj is PdfDictionary dict && dict.ContainsKey("Root")
                    && TryAdoptScannedRoot(parser, text, dict.Get("Root"), trailer))
                    return;
            }
            catch { /* skip malformed trailer */ }
        }
    }

    /// <summary>Verify a scanned trailer's /Root points at a real /Catalog object; when it does,
    /// register the catalog's xref entry at the offset the scan found and set the trailer's
    /// /Root. Returns false when the reference does not resolve to a catalog.</summary>
    private bool TryAdoptScannedRoot(PdfParser parser, string text, PdfObject? rootRef, PdfDictionary trailer)
    {
        if (rootRef is not PdfIndirectRef indRef) return false;

        // Verify the referenced object is a real Catalog by scanning
        // the file for the object header and parsing it
        var objHeader = $"{indRef.ObjectNumber} {indRef.Generation} obj";
        var headerPos = text.IndexOf(objHeader, StringComparison.Ordinal);
        if (headerPos < 0) return false;
        parser.Lexer.Position = headerPos;
        var indirect = parser.ParseIndirectObject();
        if (indirect.Value is not PdfDictionary catDict || catDict.GetName("Type") != "Catalog") return false;

        // Found a valid catalog - add entry if not present and set Root
        if (!_entries.ContainsKey(indRef.ObjectNumber) ||
            _entries[indRef.ObjectNumber].Offset != headerPos)
        {
            _entries[indRef.ObjectNumber] = new XRefEntry
            {
                ObjectNumber = indRef.ObjectNumber,
                Generation = indRef.Generation,
                Offset = headerPos,
                InUse = true
            };
        }
        trailer.Set("Root", rootRef);
        return true;
    }

    /// <summary>
    /// Merge entries from a supplementary cross-reference stream (hybrid xref /XRefStm).
    /// Only adds entries not already present in the table.
    /// </summary>
    internal void MergeXrefStreamAt(byte[] data, long offset)
    {
        var visited = new HashSet<long>();
        ReadXrefStream(data, offset, visited);
    }

    /// <summary>
    /// Read the entire xref structure from raw PDF bytes.
    /// </summary>
    public static XRefTable Read(byte[] data)
    {
        // The FINAL startxref decides — but when its target is not an xref AT ALL
        // (a tail revision whose startxref points into stream data — neither an
        // "xref" keyword nor an object header), the reader recovers the
        // LAST cross-reference STREAM the file actually holds, scanning backward
        // from the end — the revision the broken pointer was written for — rather
        // than scan-repairing the file into a merge of every revision (probed on
        // a concatenated 6-page tail: Pages.Count 6, the kid its xref
        // cannot name anywhere is a NULL page slot). Objects the recovered table
        // has no entry for resolve by a file-wide header search. A target that IS
        // a readable header whose CHAIN then fails keeps the classic full-scan
        // recovery — such files pin the merged read.
        var startXrefOffset = FindStartXref(data);
        var headerPos = SkipWhitespace(data, startXrefOffset);
        var targetIsXref = headerPos + 4 <= data.Length
            && (Encoding.ASCII.GetString(data, (int)headerPos, 4) == "xref"
                || (data[headerPos] >= (byte)'0' && data[headerPos] <= (byte)'9'));
        if (!targetIsXref)
        {
            var tail = ReadLastDeclaredXrefStream(data);
            if (tail is not null) return tail;
        }
        var table = new XRefTable();
        table.ReadXrefAt(data, startXrefOffset, new HashSet<long>());

        // Validate: a valid xref must have a trailer with /Root.
        // If not, the startxref might be wrong (e.g., garbage header or empty offset).
        if (table._trailer is null || !table._trailer.ContainsKey("Root"))
        {
            throw new InvalidOperationException(
                "The root object missing or invalid");
        }

        return table;
    }

    /// <summary>Parse the LAST cross-reference STREAM in the file, found by scanning
    /// backward for a /Type/XRef stream object — the recovery target when the final
    /// startxref is broken. Missing-entry objects on such a table resolve by a
    /// file-wide header search (<see cref="RecoveredFromBrokenTail"/>). Returns null
    /// when no candidate parses to a rooted table.</summary>
    private static XRefTable? ReadLastDeclaredXrefStream(byte[] data)
    {
        var needle = "/XRef"u8.ToArray();
        for (var i = data.Length - needle.Length; i >= 0; i--)
        {
            if (!data.AsSpan(i, needle.Length).SequenceEqual(needle)) continue;
            // Backtrack to the "N G obj" header that owns this dictionary.
            var h = LastIndexOf(data, "obj"u8.ToArray(), i);
            if (h < 0) continue;
            var lineStart = h;
            while (lineStart > 0 && data[lineStart - 1] is not ((byte)'\r' or (byte)'\n')) lineStart--;
            if (lineStart >= h || data[lineStart] < (byte)'0' || data[lineStart] > (byte)'9') continue;
            var t = new XRefTable();
            try { t.ReadXrefStream(data, lineStart, new HashSet<long>()); }
            catch { continue; }
            if (t._trailer is null || !t._trailer.ContainsKey("Root")) continue;
            t.RecoveredFromBrokenTail = true;
            return t;
        }
        return null;
    }

    private static int LastIndexOf(byte[] data, byte[] needle, int before)
    {
        for (var i = Math.Min(before, data.Length - needle.Length); i >= 0; i--)
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    /// <summary>Parse the LAST classic cross-reference table in the file, found by
    /// scanning backwards for an "xref" line. Used after a full-scan recovery to
    /// answer "was this object reachable through the file's DECLARED xref?" —
    /// the recovered table has correct offsets, so the declared one must be
    /// re-read separately. Returns null when no classic table parses.</summary>
    internal static XRefTable? ReadLastDeclaredClassic(byte[] data)
    {
        for (var pos = data.Length - 4; pos >= 0; pos--)
        {
            if (data[pos] != 'x' || data[pos + 1] != 'r' || data[pos + 2] != 'e' || data[pos + 3] != 'f')
                continue;
            // A real table's keyword starts its own line ("startxref" is rejected
            // here too — its 'xref' is preceded by 't').
            if (pos > 0 && data[pos - 1] != '\n' && data[pos - 1] != '\r')
                continue;
            var t = new XRefTable();
            try { t.ReadTraditionalXref(data, pos, new HashSet<long>()); }
            catch { /* partially parsed entries still count */ }
            if (t._entries.Count > 0) return t;
        }
        return null;
    }

    private void ReadXrefAt(byte[] data, long offset, HashSet<long> visited)
    {
        if (!visited.Add(offset))
            return; // circular chain protection

        // Determine if this is a traditional xref table or an xref stream
        var pos = SkipWhitespace(data, offset);
        if (pos + 4 <= data.Length && Encoding.ASCII.GetString(data, (int)pos, 4) == "xref")
        {
            ReadTraditionalXref(data, pos, visited);
        }
        else
        {
            ReadXrefStream(data, offset, visited);
        }
    }

    private void ReadTraditionalXref(byte[] data, long offset, HashSet<long> visited)
    {
        var xt = new TraditionalXrefState();
        xt.data = data;
        xt.offset = offset;
        xt.visited = visited;
        xt.pos = xt.offset + 4; // skip "xref"
        xt.pos = SkipWhitespace(xt.data, xt.pos);

        xt.firstSubsection = true;

        // Parse subsections
        while (xt.pos < xt.data.Length)
        {
            if (!ReadTraditionalXrefSection(xt)) break;
        }

        // Parse trailer dictionary
        xt.pos = SkipWhitespace(xt.data, xt.pos);
        xt.parser = new PdfParser(xt.data);
        xt.parser.Lexer.Position = xt.pos;
        xt.trailerObj = xt.parser.ParseObject();

        if (xt.trailerObj is PdfDictionary trailerDict)
        {
            ReadTraditionalTrailer(xt, trailerDict);
        }
    }

    private void ReadXrefStream(byte[] data, long offset, HashSet<long> visited)
    {
        var xs = new XrefStreamState();
        xs.data = data;
        xs.offset = offset;
        xs.visited = visited;
        xs.parser = new PdfParser(xs.data);
        xs.parser.Lexer.Position = xs.offset;

        xs.indirectObj = xs.parser.ParseIndirectObject();
        if (xs.indirectObj.Value is not PdfStream stream)
            throw new InvalidOperationException($"Expected xref stream at offset {xs.offset}");

        UsedXrefStream = true;

        // Remember this xref stream's own object number so the writer can skip it on save
        // (the cross-reference stream is always regenerated, never carried over). The
        // offset is kept so InfrastructureObjectNumbers can tell a still-current xref
        // stream from a number a later increment redefined as a live object.
        _xrefStreamObjNums.Add((xs.indirectObj.ObjectNumber, xs.offset));

        xs.dict = stream.Dict;
        _trailer ??= xs.dict;

        xs.decodedData = Filters.StreamFilter.Decode(stream.RawData, xs.dict);

        xs.wArray = xs.dict.Get("W") as PdfArray;
        if (xs.wArray is null || xs.wArray.Count < 3)
            throw new InvalidOperationException("XRef stream missing /W array");

        xs.w1 = (int)((PdfInteger)xs.wArray[0]).Value;
        xs.w2 = (int)((PdfInteger)xs.wArray[1]).Value;
        xs.w3 = (int)((PdfInteger)xs.wArray[2]).Value;
        xs.entrySize = xs.w1 + xs.w2 + xs.w3;

        xs.indexArray = xs.dict.Get("Index") as PdfArray;
        xs.size = (int)xs.dict.GetInt("Size");

        if (xs.indexArray is not null)
        {
            ReadXrefIndex(xs);
        }
        else
        {
            xs.subsections = [(0, xs.size)];
        }

        xs.dataPos = 0;
        foreach (var (start, count) in xs.subsections)
        {
            ReadXrefSubsection(xs, start, count);
        }

        xs.prev = xs.dict.GetInt("Prev", -1);
        if (xs.prev >= 0)
        {
            ReadXrefAt(xs.data, xs.prev, xs.visited);
        }
    }

    private static long ReadFieldValue(byte[] data, int offset, int width)
    {
        if (width == 0) return 0;
        long value = 0;
        for (var i = 0; i < width; i++)
        {
            value = (value << 8) | data[offset + i];
        }
        return value;
    }

    /// <summary>
    /// Find the "startxref" offset from the end of file.
    /// </summary>
    internal static long FindStartXref(byte[] data)
    {
        // Search backwards from end for "startxref"
        var needle = "startxref"u8;
        var searchStart = Math.Max(0, data.Length - 1024);

        for (var i = data.Length - needle.Length; i >= searchStart; i--)
        {
            if (data.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                // Parse the offset that follows
                var pos = i + needle.Length;
                pos = (int)SkipWhitespace(data, pos);
                var (offset, _) = ReadLong(data, pos);
                return offset;
            }
        }

        throw new InvalidOperationException("Could not find startxref marker");
    }

    private static long SkipWhitespace(byte[] data, long pos)
    {
        while (pos < data.Length)
        {
            var b = data[pos];
            if (b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\0' || b == '\f')
                pos++;
            else
                break;
        }
        return pos;
    }

    private static long SkipToNextLine(byte[] data, long pos)
    {
        while (pos < data.Length && data[pos] != '\r' && data[pos] != '\n')
            pos++;
        while (pos < data.Length && (data[pos] == '\r' || data[pos] == '\n'))
            pos++;
        return pos;
    }

    private static (long value, long nextPos) ReadLong(byte[] data, long pos)
    {
        pos = SkipWhitespace(data, pos);
        long value = 0;
        var negative = false;

        if (pos < data.Length && data[pos] == '-')
        {
            negative = true;
            pos++;
        }
        else if (pos < data.Length && data[pos] == '+')
        {
            pos++;
        }

        while (pos < data.Length && data[pos] >= '0' && data[pos] <= '9')
        {
            value = value * 10 + (data[pos] - '0');
            pos++;
        }

        return (negative ? -value : value, pos);
    }
}
