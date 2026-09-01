using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.IO;

internal sealed partial class PdfReader
{
    /// <summary>Register an in-memory object that can be resolved by object number.</summary>
    internal void RegisterOverlayObject(int objectNumber, PdfObject obj)
    {
        _overlayObjects[objectNumber] = obj;
    }

    private PdfObject? ResolveRef(int objectNumber, int generation)
    {
        var key = (objectNumber, generation);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // Check in-memory overlay objects first
        if (_overlayObjects.TryGetValue(objectNumber, out var overlay))
            return overlay;

        // Auto-init decryptor on first access
        EnsureDecryptorInitialized();

        var entry = _xref.GetEntry(objectNumber);
        if (entry is null || !entry.Value.InUse)
        {
            // A broken-tail recovered xref (final startxref garbage; the last xref
            // STREAM taken instead) resolves entry-less objects by a file-wide
            // header search — the tail revision's trailer names a Root only the
            // front document holds (probed: the catalog resolves; a page
            // kid the file holds NOWHERE stays a null slot).
            if (entry is null && _xref.RecoveredFromBrokenTail)
            {
                var found = FindObjectAnywhere(objectNumber, generation);
                if (found is not null) { _cache[key] = found; return found; }
            }
            if (!_options.LenientMode)
            {
                if (entry is null)
                    throw new InvalidOperationException(
                        $"Object {objectNumber} {generation} not found in xref table");
            }
            return null;
        }

        PdfObject? result;

        if (entry.Value.IsCompressed)
        {
            result = ResolveCompressedObject(entry.Value, objectNumber);
        }
        else
        {
            result = ResolveUncompressedObject(entry.Value, objectNumber, generation);
            if (result is null) return null;

            // Decrypt strings within the resolved object
            if (_decryptor is not null)
            {
                result = DecryptObject(result, objectNumber, generation);
            }
        }

        if (result is not null)
            _cache[key] = result;
        return result;
    }

    /// <summary>Find "N G obj" anywhere in the file and parse it — the broken-tail
    /// resolution for objects the recovered xref stream has no entry for. Returns
    /// null when the file holds no such header.</summary>
    private PdfObject? FindObjectAnywhere(int objectNumber, int generation)
    {
        var target = Encoding.ASCII.GetBytes($"{objectNumber} {generation} obj");
        for (var i = 0; i + target.Length <= _data.Length; i++)
        {
            if (_data[i] != target[0]) continue;
            if (!_data.AsSpan(i, target.Length).SequenceEqual(target)) continue;
            if (i > 0 && _data[i - 1] >= (byte)'0' && _data[i - 1] <= (byte)'9') continue;
            try
            {
                _parser.Lexer.Position = i;
                var ind = _parser.ParseIndirectObject();
                if (ind.ObjectNumber == objectNumber) return ind.Value;
            }
            catch { /* keep scanning */ }
        }
        return null;
    }

    private PdfObject? ResolveUncompressedObject(XRefEntry entry, int objectNumber, int generation)
    {
        try
        {
            _parser.Lexer.Position = entry.Offset;
            var indirect = _parser.ParseIndirectObject();

            // If the parsed object number doesn't match, try scanning nearby
            // for the correct header (handles shifted xref offsets)
            if (indirect.ObjectNumber != objectNumber)
            {
                var correctedOffset = FindObjectOffset(entry.Offset, objectNumber, generation);
                if (correctedOffset != entry.Offset)
                {
                    _parser.Lexer.Position = correctedOffset;
                    indirect = _parser.ParseIndirectObject();
                }
            }

            return indirect.Value;
        }
        catch (Exception ex)
        {
            if (_options.LenientMode)
            {
                // Try scanning nearby for the object header
                var result = ScanForObject(objectNumber, entry.Offset);
                if (result is not null) return result;

                // In lenient mode, skip malformed objects
                return null;
            }

            throw new InvalidOperationException(
                $"Failed to parse object {objectNumber} {generation} at offset {entry.Offset}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Find the actual file offset where "objNum genNum obj" starts.
    /// If the reported offset already points to the correct header, returns it.
    /// Otherwise scans forward/backward up to 512 bytes to find the header.
    /// Modelled on the equivalent TypeScript findObjectOffset() routine.
    /// </summary>
    private long FindObjectOffset(long reported, int objNum, int genNum)
    {
        var target = Encoding.ASCII.GetBytes($"{objNum} {genNum} obj");

        // Fast path: check if reported offset already starts with the expected header
        if (reported >= 0 && reported + target.Length <= _data.Length)
        {
            var match = true;
            for (var i = 0; i < target.Length && match; i++)
                if (_data[reported + i] != target[i]) match = false;
            // Also verify "obj" is followed by whitespace or '<<' (not e.g. "object")
            if (match)
            {
                var afterObj = reported + target.Length;
                if (afterObj >= _data.Length || _data[afterObj] <= 32 || _data[afterObj] == '<')
                    return reported;
            }
        }

        // Slow path: scan forward up to 512 bytes
        var end = Math.Min(_data.Length - target.Length, reported + 512);
        for (var i = reported; i < end; i++)
        {
            if (_data[i] != target[0]) continue;
            var found = true;
            for (var j = 1; j < target.Length && found; j++)
                if (_data[i + j] != target[j]) found = false;
            if (!found) continue;
            // Verify "obj" is followed by whitespace/delimiter and preceded by newline or start
            var afterObj = i + target.Length;
            if (afterObj < _data.Length && _data[afterObj] > 32 && _data[afterObj] != '<') continue;
            if (i > 0 && _data[i - 1] != '\n' && _data[i - 1] != '\r' && _data[i - 1] != ' ' && i != 0) continue;
            return i;
        }

        // Scan backward up to 512 bytes
        var start = Math.Max(0, reported - 512);
        for (var i = reported - 1; i >= start; i--)
        {
            if (_data[i] != target[0]) continue;
            var found = true;
            for (var j = 1; j < target.Length && found; j++)
                if (_data[i + j] != target[j]) found = false;
            if (!found) continue;
            var afterObj = i + target.Length;
            if (afterObj < _data.Length && _data[afterObj] > 32 && _data[afterObj] != '<') continue;
            if (i > 0 && _data[i - 1] != '\n' && _data[i - 1] != '\r' && _data[i - 1] != ' ') continue;
            return i;
        }

        // No match — use reported offset as-is
        return reported;
    }

    /// <summary>
    /// Recursively decrypt PdfString values within an object tree.
    /// Does NOT decrypt the /Encrypt dictionary itself.
    /// </summary>
    private PdfObject DecryptObject(PdfObject obj, int objectNumber, int generation)
    {
        // Don't decrypt the Encrypt dictionary
        var encryptRef = Trailer.Get("Encrypt");
        if (encryptRef is PdfIndirectRef eRef && eRef.ObjectNumber == objectNumber)
            return obj;

        return DecryptObjectInner(obj, objectNumber, generation);
    }

    private PdfObject DecryptObjectInner(PdfObject obj, int objectNumber, int generation)
    {
        switch (obj)
        {
            case PdfString str:
                var decrypted = _decryptor!.DecryptString(str.Value, objectNumber, generation);
                return new PdfString(decrypted, str.IsHex);

            case PdfDictionary dict:
                foreach (var key in dict.Keys.ToArray())
                {
                    var val = dict.Get(key);
                    if (val is not null)
                    {
                        var newVal = DecryptObjectInner(val, objectNumber, generation);
                        if (!ReferenceEquals(val, newVal))
                            dict.Set(key, newVal);
                    }
                }
                return dict;

            case PdfArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var val = arr[i];
                    var newVal = DecryptObjectInner(val, objectNumber, generation);
                    if (!ReferenceEquals(val, newVal))
                        arr.ReplaceAt(i, newVal);
                }
                return arr;

            case PdfStream stream:
                // Decrypt the stream dict (strings inside it), but not the stream data itself
                // Stream data is decrypted in DecodeStream when accessed
                DecryptObjectInner(stream.Dict, objectNumber, generation);
                // Store the object/gen for later stream decryption
                stream.ObjectNumber = objectNumber;
                stream.Generation = generation;
                return stream;

            default:
                return obj;
        }
    }

    /// <summary>
    /// Resolve an object stored in a compressed object stream (ObjStm, type 2 xref entry).
    /// Caches the entire parsed ObjStm so repeated lookups are efficient.
    /// </summary>
    private PdfObject ResolveCompressedObject(XRefEntry entry, int objectNumber)
    {
        // Check if we've already parsed this ObjStm
        if (_objStmCache.TryGetValue(entry.StreamObjectNumber, out var cachedObjects))
        {
            if (entry.IndexInStream < cachedObjects.Length)
                return cachedObjects[entry.IndexInStream];

            throw new InvalidOperationException(
                $"Object {objectNumber} references index {entry.IndexInStream} in object stream " +
                $"{entry.StreamObjectNumber}, but stream only contains {cachedObjects.Length} objects");
        }

        // The object is in an object stream — resolve and parse the whole stream
        var streamObj = ResolveRef(entry.StreamObjectNumber, 0);
        if (streamObj is not PdfStream objStream)
        {
            // The xref says this object is compressed, but the object stream it names is not in
            // the file - a sanitised or truncated document. The object itself is very often
            // still there as a PLAIN one (in the file that brought this up, the missing entry
            // was the /Root catalog, present verbatim while the xref pointed at an object
            // stream that had been stripped), so look for its header before giving up. The
            // uncompressed path has scanned for a misplaced object for a while; this is the
            // compressed half of the same recovery.
            var scanned = ScanForObject(objectNumber, 0, wholeFile: true);
            if (scanned is not null) return scanned;

            // Nothing to recover. In lenient mode a missing object reads as null, which the
            // resolvers already treat as absent; otherwise the file is genuinely unreadable.
            if (_options.LenientMode) return PdfNull.Instance;
            throw new InvalidOperationException(
                $"Object stream {entry.StreamObjectNumber} not found or not a stream " +
                $"(needed to resolve compressed object {objectNumber})");
        }

        var decodedData = DecodeStream(objStream, entry.StreamObjectNumber, 0);
        var n = (int)objStream.Dict.GetInt("N");      // number of objects in the stream
        var first = (int)objStream.Dict.GetInt("First"); // byte offset of first object in stream

        // Parse the index: N pairs of (objNum, offset)
        var indexParser = new PdfParser(decodedData);
        var offsets = new (int objNum, int offset)[n];
        for (var i = 0; i < n; i++)
        {
            var numToken = indexParser.Lexer.NextToken();
            var offsetToken = indexParser.Lexer.NextToken();
            offsets[i] = ((int)numToken.IntValue, (int)offsetToken.IntValue);
        }

        // Parse ALL objects in the stream and cache them
        var parsedObjects = new PdfObject[n];
        for (var i = 0; i < n; i++)
        {
            var targetOffset = first + offsets[i].offset;
            try
            {
                parsedObjects[i] = indexParser.ParseObjectAt(targetOffset);
            }
            catch (Exception ex)
            {
                if (_options.LenientMode)
                {
                    parsedObjects[i] = PdfNull.Instance;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Failed to parse object at index {i} (objNum={offsets[i].objNum}) " +
                        $"in object stream {entry.StreamObjectNumber}: {ex.Message}", ex);
                }
            }
        }

        _objStmCache[entry.StreamObjectNumber] = parsedObjects;

        if (entry.IndexInStream >= parsedObjects.Length)
        {
            throw new InvalidOperationException(
                $"Object {objectNumber} references index {entry.IndexInStream} in object stream " +
                $"{entry.StreamObjectNumber}, but stream only contains {parsedObjects.Length} objects");
        }

        return parsedObjects[entry.IndexInStream];
    }

    /// <summary>
    /// Scan nearby bytes for an object header pattern "N G obj" when the xref offset is wrong.
    /// Searches +/-1024 bytes around the expected offset.
    /// </summary>
    private PdfObject? ScanForObject(int objectNumber, long expectedOffset, bool wholeFile = false)
    {
        // A COMPRESSED entry carries no file offset to search around - its object lives in an
        // object stream, and when that stream is missing there is nothing to centre a window
        // on. Scan the whole file for the header instead.
        var searchRadius = 1024;
        var startPos = wholeFile ? 0 : Math.Max(0, expectedOffset - searchRadius);
        var endPos = wholeFile ? _data.Length : Math.Min(_data.Length, expectedOffset + searchRadius);

        // Build the pattern to search for: "objectNumber 0 obj"
        var pattern = Encoding.ASCII.GetBytes($"{objectNumber} 0 obj");

        for (var pos = startPos; pos + pattern.Length < endPos; pos++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (_data[pos + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match) continue;

            // Verify it's preceded by whitespace or start of data
            if (pos > 0 && !IsWhitespace(_data[pos - 1])) continue;

            try
            {
                _parser.Lexer.Position = pos;
                var indirect = _parser.ParseIndirectObject();
                if (indirect.ObjectNumber == objectNumber)
                    return indirect.Value;
            }
            catch
            {
                // Continue scanning
            }
        }

        return null;
    }

    /// <summary>
    /// Recover xref table by scanning the entire file for "N G obj" object headers.
    /// Used as a fallback when normal xref parsing fails.
    /// </summary>
    internal static XRefTable RecoverXref(byte[] data, Exception? originalException = null)
    {
        var table = new XRefTable();
        var text = data;
        var maxObjNum = 0;

        // Scan for object headers: digits whitespace digits whitespace "obj"
        for (long pos = 0; pos < text.Length - 5; pos++)
        {
            if (!IsDigit(text[pos])) continue;

            // Check if preceded by whitespace or start of file
            if (pos > 0 && !IsWhitespace(text[pos - 1])) continue;

            // Parse object number
            var numStart = pos;
            while (pos < text.Length && IsDigit(text[pos])) pos++;
            if (pos >= text.Length || !IsWhitespace(text[pos])) continue;

            var objNumStr = Encoding.ASCII.GetString(text, (int)numStart, (int)(pos - numStart));
            if (!int.TryParse(objNumStr, out var objNum)) continue;

            // Skip whitespace
            while (pos < text.Length && IsWhitespace(text[pos])) pos++;

            // Parse generation number
            var genStart = pos;
            while (pos < text.Length && IsDigit(text[pos])) pos++;
            if (pos >= text.Length || !IsWhitespace(text[pos])) continue;

            var genStr = Encoding.ASCII.GetString(text, (int)genStart, (int)(pos - genStart));
            if (!int.TryParse(genStr, out var gen)) continue;

            // Skip whitespace
            while (pos < text.Length && IsWhitespace(text[pos])) pos++;

            // Check for "obj" keyword
            if (pos + 3 > text.Length) continue;
            if (text[pos] != 'o' || text[pos + 1] != 'b' || text[pos + 2] != 'j') continue;

            // Verify followed by whitespace or delimiter
            var afterObj = pos + 3;
            if (afterObj < text.Length && !IsWhitespace(text[afterObj]) && !IsDelimiter(text[afterObj]))
                continue;

            // Use SetEntry (last occurrence wins) to handle incremental updates:
            // later object definitions supersede earlier ones.
            table.SetEntry(objNum, new XRefEntry
            {
                ObjectNumber = objNum,
                Generation = gen,
                Offset = numStart,
                InUse = true
            });

            if (objNum > maxObjNum) maxObjNum = objNum;

            // Reset pos to just after "obj" so we continue scanning
            pos = afterObj;
        }

        if (table.Entries.Count == 0)
        {
            // No object headers found in the body — the file looked like a PDF
            // (header + startxref) but has no recoverable structure. Surface as
            // the standard "Trailer not found" InvalidPdfFileFormatException so
            // callers can pattern-match on the typed exception.
            throw new Aspose.Pdf.InvalidPdfFileFormatException("Trailer not found");
        }

        // Extract compressed objects from object streams (ObjStm).
        // Needed for linearized PDFs where Pages/Catalog dicts are in ObjStm.
        ExtractObjectStreams(data, table);

        // Build a synthetic trailer by finding the Catalog object
        table.BuildSyntheticTrailer(data, maxObjNum + 1);

        table.RecoveredByScan = true;
        return table;
    }

    /// <summary>
    /// Extract compressed objects from ObjStm streams found during recovery.
    /// This is needed for linearized PDFs where /Pages, /Catalog, etc. are inside
    /// object streams.
    /// </summary>
    private static void ExtractObjectStreams(byte[] data, XRefTable table)
    {
        // Find ObjStm entries in the current xref
        var objStmEntries = new List<(int objNum, XRefEntry entry)>();
        foreach (var kvp in table.Entries)
        {
            if (!kvp.Value.InUse || kvp.Value.IsCompressed) continue;
            try
            {
                var parser = new PdfParser(data);
                parser.Lexer.Position = kvp.Value.Offset;
                var indirect = parser.ParseIndirectObject();
                if (indirect.Value is PdfStream stream && stream.Dict.GetName("Type") == "ObjStm")
                {
                    objStmEntries.Add((kvp.Key, kvp.Value));
                }
            }
            catch { /* skip unparseable */ }
        }

        foreach (var (stmObjNum, stmEntry) in objStmEntries)
        {
            try
            {
                var parser = new PdfParser(data);
                parser.Lexer.Position = stmEntry.Offset;
                var indirect = parser.ParseIndirectObject();
                if (indirect.Value is not PdfStream stream) continue;

                var n = (int)(stream.Dict.Get("N") is PdfInteger ni ? ni.Value : 0);
                var first = (int)(stream.Dict.Get("First") is PdfInteger fi ? fi.Value : 0);
                if (n <= 0 || first <= 0) continue;

                // Decode the stream
                var decoded = Filters.StreamFilter.Decode(stream.RawData, stream.Dict);

                // Parse the header: N pairs of (objNum offset)
                var headerParser = new PdfParser(decoded);
                var pairs = new List<(int objNum, int offset)>();
                for (var i = 0; i < n; i++)
                {
                    var objNumTok = headerParser.Lexer.NextToken();
                    var offsetTok = headerParser.Lexer.NextToken();
                    if (objNumTok.Kind != TokenKind.Integer || offsetTok.Kind != TokenKind.Integer) break;
                    pairs.Add(((int)objNumTok.IntValue, (int)offsetTok.IntValue));
                }

                // Add entries for compressed objects
                foreach (var (objNum, _) in pairs)
                {
                    table.AddEntry(objNum, new XRefEntry
                    {
                        ObjectNumber = objNum,
                        InUse = true,
                        IsCompressed = true,
                        StreamObjectNumber = stmObjNum,
                        IndexInStream = pairs.FindIndex(p => p.objNum == objNum)
                    });
                }
            }
            catch { /* skip malformed ObjStm */ }
        }
    }

    /// <summary>
    /// Detect whether this PDF is linearized by checking for a linearization dictionary
    /// in the first indirect object in the file (the one at the lowest byte offset).
    /// Per PDF spec, a linearized PDF has a dictionary with /Linearized key as the very
    /// first object in the body.
    /// </summary>
    private void DetectLinearization()
    {
        try
        {
            // Find the object with the lowest file offset — this is the first object in the body.
            long minOffset = long.MaxValue;
            int firstObjNum = -1;
            int firstGen = 0;
            foreach (var kvp in _xref.Entries)
            {
                var entry = kvp.Value;
                if (!entry.InUse || entry.IsCompressed) continue;
                if (entry.Offset < minOffset)
                {
                    minOffset = entry.Offset;
                    firstObjNum = kvp.Key;
                    firstGen = entry.Generation;
                }
            }

            if (firstObjNum < 0) return;

            // Parse the first object without caching — we just need to peek at it.
            _parser.Lexer.Position = minOffset;
            var indirect = _parser.ParseIndirectObject();
            if (indirect.Value is PdfDictionary dict && dict.ContainsKey("Linearized"))
            {
                // Validate: /L must match actual file size (PDF spec §F.2).
                // If it doesn't, the file was modified after linearization and is no longer valid.
                var declaredLength = dict.GetInt("L", -1);
                IsLinearized = declaredLength < 0 || declaredLength == _data.Length;
                // A save regenerates this pair, so the ORIGINALS are infrastructure: nothing
                // but the dictionary references the hint stream, and nothing references the
                // dictionary at all, so carried forward they are dead objects that keep the
                // output one object wider than the document. The hint stream is only
                // identifiable HERE - its byte offset is the first slot of /H, which means
                // nothing once the objects have been re-laid out.
                LinearizationInfraObjects.Add(firstObjNum);
                if (dict.Get("H") is PdfArray hintArr && hintArr.Count > 0
                    && hintArr[0] is PdfInteger hintOff)
                    foreach (var kv in _xref.Entries)
                        if (kv.Value.InUse && !kv.Value.IsCompressed && kv.Value.Offset == hintOff.Value)
                            LinearizationInfraObjects.Add(kv.Key);
            }
        }
        catch
        {
            // If we can't parse the first object, it's not linearized (or corrupt).
            IsLinearized = false;
        }
    }

    /// <summary>
    /// Handle hybrid xref: when a traditional trailer contains /XRefStm,
    /// merge entries from the supplementary cross-reference stream.
    /// </summary>
    private void HandleHybridXref()
    {
        var xrefStmOffset = _xref.Trailer.GetInt("XRefStm", -1);
        if (xrefStmOffset < 0) return;

        try
        {
            _xref.MergeXrefStreamAt(_data, xrefStmOffset);
        }
        catch
        {
            if (!_options.LenientMode)
                throw;
            // In lenient mode, ignore failure to read supplementary xref stream
        }
    }
}
