using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

/// <summary>
/// Re-lays-out a normally-saved PDF (traditional cross-reference table, no object streams)
/// into a linearized — "optimized for fast web view", PDF 32000 Annex F — document:
/// a leading linearization parameter dictionary, a first-page cross-reference section, the
/// catalog, the primary hint stream, the first page's objects, the other pages' objects page
/// by page, the objects they share, the rest, and the main cross-reference section. The
/// objects are renumbered in that order, as the hint tables name a page's objects by a count
/// from its page object; each object's bytes are copied with its references renumbered.
///
/// When the plain save would pack objects into object streams, the linearized file packs each
/// section's objects into object streams of that section - the pages, the catalog, the outlines
/// and every stream keep their own offsets - with cross-reference streams for both sections.
///
/// Every object's emitted length is known before the write, so the hint tables are computed
/// up front from the offsets the layout gives; the few values that precede what they
/// describe (/L /H /E /T, the first-page entries and /Prev) are fixed-width placeholders
/// patched at the end, so a single forward pass produces the file.
/// </summary>
internal static partial class PdfLinearizer
{
    // Private marker on the primary hint stream so a subsequent linearize cycle recognizes and
    // drops the previous one (keeping re-save idempotent). Readers ignore unknown stream keys.
    private const string HintMarker = "/AsposeHint";
    private static readonly byte[] LinearizedKeyName = Encoding.ASCII.GetBytes("Linearized");
    private static readonly byte[] HintMarkerName = Encoding.ASCII.GetBytes(HintMarker.Substring(1));

    /// <summary>
    /// Produce a linearized rendering of <paramref name="normalPdf"/>. Falls back to the input
    /// unchanged when it cannot be linearized safely (encrypted, no objects, unparseable).
    /// With <paramref name="useObjectStreams"/> the objects no page uses are packed into object
    /// streams, as the plain save of the document would pack them.
    /// </summary>
    public static byte[] Linearize(byte[] normalPdf, bool useObjectStreams = false)
    {
        try { return LinearizeCore(normalPdf, useObjectStreams); }
        catch { return normalPdf; }
    }

    /// <summary>The <c>%PDF-x.y</c> version of <paramref name="src"/>, or 1.7 when the
    /// header is missing or malformed.</summary>
    private static string HeaderVersion(byte[] src)
    {
        if (src.Length < 8 || src[0] != '%' || src[1] != 'P' || src[2] != 'D' || src[3] != 'F' || src[4] != '-')
            return "1.7";
        var end = 5;
        while (end < src.Length && end < 12 && src[end] != '\r' && src[end] != '\n') end++;
        var v = Encoding.ASCII.GetString(src, 5, end - 5).Trim();
        return v.Length == 0 ? "1.7" : v;
    }

    private static byte[] LinearizeCore(byte[] src, bool useObjectStreams)
    {
        var lz = new LinearizeState();
        lz.src = src;
        lz.packObjects = useObjectStreams;
        lz.reader = PdfReader.FromBytes(lz.src);

        // Encrypted documents: re-laying-out object bytes by offset is unsound (encryption
        // keys depend on object identity/position). Keep the compact output.
        if (lz.reader.IsDecrypted || lz.reader.Trailer.Get("Encrypt") is not null) return lz.src;
        if (!CollectObjects(lz)) return lz.src;
        if (lz.reader.Trailer.Get("Root") is not PdfIndirectRef rootRef || !lz.objs.ContainsKey(rootRef.ObjectNumber))
            return lz.src;
        lz.rootNum = rootRef.ObjectNumber;
        lz.infoNum = lz.reader.Trailer.Get("Info") is PdfIndirectRef infoRef ? infoRef.ObjectNumber : 0;
        if (!WalkPageTree(lz)) return lz.src;

        MarkUses(lz);
        OrderSections(lz);
        return WriteLinearized(lz);
    }

    /// <summary>
    /// Every in-use object of the saved file with its references. The plain save writes the
    /// objects back to back, so an object ends at the last "endobj" before the next one starts:
    /// its stream data is never searched.
    /// </summary>
    private static bool CollectObjects(LinearizeState lz)
    {
        var entries = new List<XRefEntry>();
        foreach (var e in lz.reader.XRefTable.Entries.Values)
        {
            if (!e.InUse || e.ObjectNumber == 0) continue;
            if (e.StreamObjectNumber > 0) return false; // compressed object — keep compact output
            if (e.Offset <= 0 || e.Offset >= lz.src.Length) return false;
            entries.Add(e);
        }
        if (entries.Count == 0) return false;
        entries.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        lz.objs = new Dictionary<int, RawObject>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var start = (int)entries[i].Offset;
            var limit = i + 1 < entries.Count ? (int)entries[i + 1].Offset : lz.src.Length;
            var end = LastEndObj(lz.src, start, limit);
            var bodyStart = end < 0 ? -1 : BodyStartOf(lz.src, start, end);
            if (bodyStart < 0) return false;
            var o = new RawObject { Num = entries[i].ObjectNumber, Buf = lz.src, Start = start, BodyStart = bodyStart, End = end };
            ScanObject(o, wantKeys: false);
            if (o.IsLinearizationInfra) continue;
            lz.objs[o.Num] = o;
            if (o.Num > lz.maxNum) lz.maxNum = o.Num;
        }
        return lz.objs.Count > 0;
    }

    /// <summary>Just past the last "endobj" in [start, limit), or -1.</summary>
    private static int LastEndObj(byte[] src, int start, int limit)
    {
        for (var i = limit - EndObjKeyword.Length; i >= start; i--)
        {
            if (src[i] != 'e' || src[i + 1] != 'n' || src[i + 2] != 'd' || src[i + 3] != 'o'
                || src[i + 4] != 'b' || src[i + 5] != 'j')
                continue;
            return i + EndObjKeyword.Length;
        }
        return -1;
    }

    private const string Z = "0000000000"; // 10-digit fixed-width placeholder

    private static void Patch(byte[] buf, long pos, long value)
    {
        var s = value.ToString("D10");
        if (s.Length > 10) s = s.Substring(s.Length - 10);
        for (int i = 0; i < 10; i++) buf[pos + i] = (byte)s[i];
    }

    private static string? TrailerIdString(PdfDictionary trailer)
    {
        if (trailer.Get("ID") is not PdfArray arr || arr.Count < 2) return null;
        string Hex(PdfObject o) => o is PdfString ps ? "<" + BytesToHex(ps.Value) + ">" : "<>";
        return "[" + Hex(arr[0]) + Hex(arr[1]) + "]";
    }

    private static string BytesToHex(byte[] b)
    {
        var sb = new StringBuilder(b.Length * 2);
        foreach (var x in b) sb.Append(x.ToString("X2"));
        return sb.ToString();
    }
}
