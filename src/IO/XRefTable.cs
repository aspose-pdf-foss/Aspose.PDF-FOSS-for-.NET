using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

internal sealed class XRefTable
{
    private readonly Dictionary<int, XRefEntry> _entries = new();
    private readonly HashSet<int> _xrefStreamObjNums = new();
    private PdfDictionary? _trailer;

    public PdfDictionary Trailer => _trailer ?? throw new InvalidOperationException("No trailer found");
    public IReadOnlyDictionary<int, XRefEntry> Entries => _entries;

    /// <summary>
    /// Object numbers of the source file's cross-reference infrastructure: the cross-reference
    /// stream objects (/Type /XRef) and the object-stream containers (/Type /ObjStm) that hold
    /// compressed objects. The writer regenerates all of this from scratch, so on re-save these
    /// originals are skipped rather than carried over as dead, unreferenced streams. Exposing the
    /// set also lets the writer keep object numbering stable across a load/save round-trip.
    /// </summary>
    public HashSet<int> InfrastructureObjectNumbers()
    {
        var infra = new HashSet<int>(_xrefStreamObjNums);
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

        _trailer = trailer;
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
                if (obj is PdfDictionary dict && dict.ContainsKey("Root"))
                {
                    var rootRef = dict.Get("Root");
                    if (rootRef is PdfIndirectRef indRef)
                    {
                        // Verify the referenced object is a real Catalog by scanning
                        // the file for the object header and parsing it
                        var objHeader = $"{indRef.ObjectNumber} {indRef.Generation} obj";
                        var headerPos = text.IndexOf(objHeader, StringComparison.Ordinal);
                        if (headerPos >= 0)
                        {
                            parser.Lexer.Position = headerPos;
                            var indirect = parser.ParseIndirectObject();
                            if (indirect.Value is PdfDictionary catDict && catDict.GetName("Type") == "Catalog")
                            {
                                // Found a valid catalog — add entry if not present and set Root
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
                                return;
                            }
                        }
                    }
                }
            }
            catch { /* skip malformed trailer */ }
        }
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
        var table = new XRefTable();
        var startXrefOffset = FindStartXref(data);

        var visited = new HashSet<long>();
        table.ReadXrefAt(data, startXrefOffset, visited);

        // Validate: a valid xref must have a trailer with /Root.
        // If not, the startxref might be wrong (e.g., garbage header or empty offset).
        if (table._trailer is null || !table._trailer.ContainsKey("Root"))
        {
            throw new InvalidOperationException(
                "The root object missing or invalid");
        }

        return table;
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
        var pos = offset + 4; // skip "xref"
        pos = SkipWhitespace(data, pos);

        var firstSubsection = true;

        // Parse subsections
        while (pos < data.Length)
        {
            // Check if we've reached "trailer"
            if (pos + 7 <= data.Length && Encoding.ASCII.GetString(data, (int)pos, 7) == "trailer")
            {
                pos += 7;
                break;
            }

            // Read "startObj count"
            var (startObj, afterStart) = ReadLong(data, pos);
            if (afterStart == pos)
                throw new InvalidOperationException(
                    $"Corrupt xref table: expected subsection header at offset {pos}");
            var (count, afterCount) = ReadLong(data, afterStart);
            pos = SkipWhitespace(data, afterCount);

            // Off-by-one shifted xref signature: some PDFs ship "xref\n1 7\n0000000000 65535 f\n…"
            // where the head free-list entry "obj0 gen=65535 free" is in the very first slot but
            // the subsection declares it as object 1. Per PDF 32000 §7.5.4 obj 0 is always the
            // head of the linked free list, so a leading "0 65535 f" inside the very first
            // subsection of an xref table that nominally starts at startObj > 0 is the canonical
            // signature. Re-anchor here. (Restricted to firstSubsection because gen=65535 free
            // entries also legally appear deeper in the table as next-free-list pointers, and
            // re-anchoring those would break valid PDFs.)
            if (firstSubsection && startObj > 0 && count > 0 && pos + 20 <= data.Length)
            {
                var (peekOffset, peekP1) = ReadLong(data, pos);
                var (peekGen, peekP2) = ReadLong(data, peekP1);
                peekP2 = SkipWhitespace(data, peekP2);
                if (peekOffset == 0 && peekGen == 65535 &&
                    peekP2 < data.Length && data[peekP2] == 'f')
                {
                    startObj = 0;
                }
            }
            firstSubsection = false;

            for (var i = 0; i < count; i++)
            {
                var objNum = (int)startObj + i;
                // Each entry is exactly 20 bytes: "OOOOOOOOOO GGGGG F \n"
                if (pos + 20 > data.Length) break;

                var (entryOffset, p1) = ReadLong(data, pos);
                var (gen, p2) = ReadLong(data, p1);
                p2 = SkipWhitespace(data, p2);
                var flag = p2 < data.Length ? (char)data[p2] : 'f';

                // First occurrence wins in the /Prev chain (most-recent section read first),
                // EXCEPT a free entry with generation 65535 — the "free, never reuse"
                // placeholder a linearized PDF's first-page xref lists for objects that are
                // really defined (in use) further down. A later in-use entry must override
                // that placeholder, or e.g. the /Pages root resolves to nothing (PDF 32000
                // §7.5.4 / Annex F linearization). Mirrors the xref-stream override below.
                var inUse = flag == 'n';
                var overridePlaceholder = _entries.TryGetValue(objNum, out var existing)
                    && !existing.InUse && existing.Generation == 65535 && inUse;
                if (!_entries.ContainsKey(objNum) || overridePlaceholder)
                {
                    _entries[objNum] = new XRefEntry
                    {
                        ObjectNumber = objNum,
                        Generation = (int)gen,
                        Offset = entryOffset,
                        InUse = inUse
                    };
                }

                // Advance to next line
                pos = SkipToNextLine(data, p2);
            }

            pos = SkipWhitespace(data, pos);
        }

        // Parse trailer dictionary
        pos = SkipWhitespace(data, pos);
        var parser = new PdfParser(data);
        parser.Lexer.Position = pos;
        var trailerObj = parser.ParseObject();

        if (trailerObj is PdfDictionary trailerDict)
        {
            _trailer ??= trailerDict;

            // Hybrid-reference file (PDF 32000 §7.5.8.4): a traditional section may carry
            // a supplementary cross-reference STREAM via /XRefStm that holds the real
            // entries for compressed (and updated) objects, which the traditional table
            // lists only as free placeholders. Merge it for EVERY section in the /Prev
            // chain — not just the main trailer — so those in-use entries override the
            // free placeholders (the stream merge already prefers in-use over free).
            var xrefStm = trailerDict.GetInt("XRefStm", -1);
            if (xrefStm >= 0)
            {
                try { ReadXrefStream(data, xrefStm, visited); } catch { /* tolerate a bad supplementary stream */ }
            }

            // Follow /Prev
            var prev = trailerDict.GetInt("Prev", -1);
            if (prev >= 0)
            {
                ReadXrefAt(data, prev, visited);
            }
        }
    }

    private void ReadXrefStream(byte[] data, long offset, HashSet<long> visited)
    {
        var parser = new PdfParser(data);
        parser.Lexer.Position = offset;

        var indirectObj = parser.ParseIndirectObject();
        if (indirectObj.Value is not PdfStream stream)
            throw new InvalidOperationException($"Expected xref stream at offset {offset}");

        // Remember this xref stream's own object number so the writer can skip it on save
        // (the cross-reference stream is always regenerated, never carried over).
        _xrefStreamObjNums.Add(indirectObj.ObjectNumber);

        var dict = stream.Dict;
        _trailer ??= dict;

        // Decode the stream
        var decodedData = Filters.StreamFilter.Decode(stream.RawData, dict);

        // Parse W array
        var wArray = dict.Get("W") as PdfArray;
        if (wArray is null || wArray.Count < 3)
            throw new InvalidOperationException("XRef stream missing /W array");

        var w1 = (int)((PdfInteger)wArray[0]).Value;
        var w2 = (int)((PdfInteger)wArray[1]).Value;
        var w3 = (int)((PdfInteger)wArray[2]).Value;
        var entrySize = w1 + w2 + w3;

        // Parse Index array (default: [0 Size])
        var indexArray = dict.Get("Index") as PdfArray;
        var size = (int)dict.GetInt("Size");
        List<(int start, int count)> subsections;

        if (indexArray is not null)
        {
            subsections = new List<(int, int)>();
            for (var i = 0; i < indexArray.Count; i += 2)
            {
                subsections.Add((
                    (int)((PdfInteger)indexArray[i]).Value,
                    (int)((PdfInteger)indexArray[i + 1]).Value
                ));
            }
        }
        else
        {
            subsections = [(0, size)];
        }

        var dataPos = 0;
        foreach (var (start, count) in subsections)
        {
            for (var i = 0; i < count; i++)
            {
                if (dataPos + entrySize > decodedData.Length) break;

                var type = w1 > 0 ? ReadFieldValue(decodedData, dataPos, w1) : 1; // default type=1
                var field2 = ReadFieldValue(decodedData, dataPos + w1, w2);
                var field3 = ReadFieldValue(decodedData, dataPos + w1 + w2, w3);
                dataPos += entrySize;

                var objNum = start + i;
                // In hybrid-reference PDFs, the traditional xref marks compressed objects
                // as free while the xref stream has the real entries. Allow in-use entries
                // from the xref stream to override free entries from the traditional table.
                if (_entries.TryGetValue(objNum, out var existing))
                {
                    if (existing.InUse || type == 0) continue;
                    // Existing is free but xref stream says in-use — override below
                }

                switch (type)
                {
                    case 0: // free
                        _entries[objNum] = new XRefEntry
                        {
                            ObjectNumber = objNum,
                            Generation = (int)field3,
                            InUse = false
                        };
                        break;
                    case 1: // uncompressed
                        _entries[objNum] = new XRefEntry
                        {
                            ObjectNumber = objNum,
                            Offset = field2,
                            Generation = (int)field3,
                            InUse = true
                        };
                        break;
                    case 2: // compressed in object stream
                        _entries[objNum] = new XRefEntry
                        {
                            ObjectNumber = objNum,
                            InUse = true,
                            IsCompressed = true,
                            StreamObjectNumber = (int)field2,
                            IndexInStream = (int)field3
                        };
                        break;
                }
            }
        }

        // Follow /Prev
        var prev = dict.GetInt("Prev", -1);
        if (prev >= 0)
        {
            ReadXrefAt(data, prev, visited);
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
