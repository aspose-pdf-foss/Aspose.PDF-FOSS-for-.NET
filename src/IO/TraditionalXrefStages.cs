using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

internal sealed partial class XRefTable
{
    /// <summary>The stages of the traditional cross-reference table read: one xref section, then the trailer.</summary>
    private void ReadTraditionalTrailer(TraditionalXrefState xt, PdfDictionary trailerDict)
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
            try { ReadXrefStream(xt.data, xrefStm, xt.visited); } catch { /* tolerate a bad supplementary stream */ }
        }

        // Follow /Prev
        var prev = trailerDict.GetInt("Prev", -1);
        if (prev >= 0)
        {
            ReadXrefAt(xt.data, prev, xt.visited);
        }
    }

    /// <summary>The stages of the traditional cross-reference table read: one xref section, then the trailer.</summary>
    private bool ReadTraditionalXrefSection(TraditionalXrefState xt)
    {
        // Check if we've reached "trailer"
        if (xt.pos + 7 <= xt.data.Length && Encoding.ASCII.GetString(xt.data, (int)xt.pos, 7) == "trailer")
        {
            xt.pos += 7;
            return false;
        }

        // Read "startObj count"
        var (startObj, afterStart) = ReadLong(xt.data, xt.pos);
        if (afterStart == xt.pos)
            throw new InvalidOperationException(
                $"Corrupt xref table: expected subsection header at offset {xt.pos}");
        var (count, afterCount) = ReadLong(xt.data, afterStart);
        xt.pos = SkipWhitespace(xt.data, afterCount);

        // Off-by-one shifted xref signature: some PDFs ship "xref\n1 7\n0000000000 65535 f\n…"
        // where the head free-list entry "obj0 gen=65535 free" is in the very first slot but
        // the subsection declares it as object 1. Per PDF 32000 §7.5.4 obj 0 is always the
        // head of the linked free list, so a leading "0 65535 f" inside the very first
        // subsection of an xref table that nominally starts at startObj > 0 is the canonical
        // signature. Re-anchor here. (Restricted to firstSubsection because gen=65535 free
        // entries also legally appear deeper in the table as next-free-list pointers, and
        // re-anchoring those would break valid PDFs.)
        if (xt.firstSubsection && startObj > 0 && count > 0 && xt.pos + 20 <= xt.data.Length)
        {
            var (peekOffset, peekP1) = ReadLong(xt.data, xt.pos);
            var (peekGen, peekP2) = ReadLong(xt.data, peekP1);
            peekP2 = SkipWhitespace(xt.data, peekP2);
            if (peekOffset == 0 && peekGen == 65535 &&
                peekP2 < xt.data.Length && xt.data[peekP2] == 'f')
            {
                startObj = 0;
            }
        }
        xt.firstSubsection = false;

        for (var i = 0; i < count; i++)
        {
            var objNum = (int)startObj + i;
            // Each entry is exactly 20 bytes: "OOOOOOOOOO GGGGG F \n"
            if (xt.pos + 20 > xt.data.Length) break;

            var (entryOffset, p1) = ReadLong(xt.data, xt.pos);
            var (gen, p2) = ReadLong(xt.data, p1);
            p2 = SkipWhitespace(xt.data, p2);
            var flag = p2 < xt.data.Length ? (char)xt.data[p2] : 'f';

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
            xt.pos = SkipToNextLine(xt.data, p2);
        }

        xt.pos = SkipWhitespace(xt.data, xt.pos);
        return true;
    }
}
