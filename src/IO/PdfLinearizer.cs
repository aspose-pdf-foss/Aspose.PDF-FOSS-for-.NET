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
/// first-page object section, a primary hint stream, the remaining objects, and the main
/// cross-reference section. Object numbers are preserved (only the physical order and the
/// cross-reference infrastructure change), so every reference in the source bytes stays valid.
///
/// All offset-dependent numeric fields (the /L /H /E /T linearization values, every xref
/// entry offset, and the /Prev link) are emitted as fixed-width placeholders and patched once
/// every byte position is final, so a single forward pass produces the file.
/// </summary>
internal static partial class PdfLinearizer
{
    // Private marker on the primary hint stream so a subsequent linearize cycle recognizes and
    // drops the previous one (keeping re-save idempotent). Readers ignore unknown stream keys.
    private const string HintMarker = "/AsposeHint";

    private readonly struct RawObject
    {
        public RawObject(int num, byte[] bytes) { Num = num; Bytes = bytes; }
        public int Num { get; }
        public byte[] Bytes { get; }
    }

    /// <summary>
    /// Produce a linearized rendering of <paramref name="normalPdf"/>. Falls back to the input
    /// unchanged when it cannot be linearized safely (encrypted, no objects, unparseable).
    /// </summary>
    public static byte[] Linearize(byte[] normalPdf)
    {
        try { return LinearizeCore(normalPdf); }
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

    private static byte[] LinearizeCore(byte[] src)
    {
        var lz = new LinearizeState();
        lz.src = src;
        lz.reader = PdfReader.FromBytes(lz.src);

        // Encrypted documents: re-laying-out object bytes by offset is unsound (encryption
        // keys depend on object identity/position). Keep the compact output.
        if (lz.reader.IsDecrypted || lz.reader.Trailer.Get("Encrypt") is not null) return lz.src;

        lz.skip = new HashSet<int>();
        foreach (var e in lz.reader.XRefTable.Entries.Values)
        {
            if (!e.InUse || e.ObjectNumber == 0 || e.StreamObjectNumber > 0) continue;
            var b = ExtractObjectBytes(lz.src, e.Offset, e.ObjectNumber);
            if (b is null) continue;
            if (IndexOf(b, "/Linearized", 0) >= 0 || IndexOf(b, HintMarker, 0) >= 0)
                lz.skip.Add(e.ObjectNumber);
        }

        lz.objs = new Dictionary<int, RawObject>();
        foreach (var entry in lz.reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;
            if (entry.StreamObjectNumber > 0) return lz.src; // compressed object — keep compact output
            if (lz.skip.Contains(entry.ObjectNumber)) continue;
            var bytes = ExtractObjectBytes(lz.src, entry.Offset, entry.ObjectNumber);
            if (bytes is null) return lz.src; // compressed/odd object — give up, keep compact output
            lz.objs[entry.ObjectNumber] = new RawObject(entry.ObjectNumber, bytes);
        }
        if (lz.objs.Count == 0) return lz.src;
        if (lz.reader.Trailer.Get("Root") is not PdfIndirectRef rootRef) return lz.src;

        lz.catalog = lz.reader.Catalog;
        lz.pagesRoot = lz.reader.ResolveDict(lz.catalog.Get("Pages"));
        lz.pageCount = lz.pagesRoot?.Get("Count") is PdfInteger pc ? (int)pc.Value : 1;
        lz.firstPageNum = FindFirstPageObjectNumber(lz.reader, lz.pagesRoot) ?? rootRef.ObjectNumber;

        lz.firstPageSet = CollectFirstPageSection(lz.reader, lz.catalog, rootRef.ObjectNumber, lz.firstPageNum);

        lz.maxNum = lz.objs.Keys.Max();
        lz.linObjNum = lz.maxNum + 1;
        lz.hintObjNum = lz.maxNum + 2;
        lz.size = lz.maxNum + 3;

        lz.firstSection = lz.objs.Values.Where(o => lz.firstPageSet.Contains(o.Num)).OrderBy(o => o.Num).ToList();
        lz.restSection = lz.objs.Values.Where(o => !lz.firstPageSet.Contains(o.Num)).OrderBy(o => o.Num).ToList();

        lz.firstXrefNums = new List<int> { lz.linObjNum, lz.hintObjNum };
        lz.firstXrefNums.AddRange(lz.firstSection.Select(o => o.Num));
        lz.firstXrefNums = lz.firstXrefNums.Distinct().OrderBy(n => n).ToList();
        lz.allNums = lz.restSection.Select(o => o.Num).Distinct().OrderBy(n => n).ToList();

        lz.ms = new MemoryStream();
        lz.offsets = new Dictionary<int, long>();
        lz.xrefEntryPos = new Dictionary<(int section, int num), long>(); // placeholder offset positions
        lz.lin = new Dictionary<string, long>();
        lz.prevPos = 0;

        WriteLinearizationHead(lz, rootRef);

        WriteLinearizedFirstSection(lz);

        WriteLinearizedMainSection(lz);

        lz.buf = lz.ms.ToArray();

        PatchLinearizationOffsets(lz);
        return lz.buf;
    }

    // Emit a traditional xref section, recording each entry's 10-digit offset placeholder
    // position so it can be patched once object offsets are final.
    /// <summary><paramref name="withFreeHead"/>: whether this table opens on object 0's free
    /// entry. Only the MAIN table does - Annex F gives the free-list head to the table that
    /// describes the rest of the file, and a first-page table that also claims object 0
    /// describes a section it holds nothing of.</summary>
    private static void EmitXref(Stream ms, List<int> nums, Dictionary<(int, int), long> entryPos,
        int section, Action<string> W, bool withFreeHead)
    {
        W("xref\n");
        var withZero = !withFreeHead || nums.Contains(0) ? nums : new List<int>(nums) { 0 };
        withZero = withZero.Distinct().OrderBy(n => n).ToList();
        int i = 0;
        while (i < withZero.Count)
        {
            int j = i;
            while (j + 1 < withZero.Count && withZero[j + 1] == withZero[j] + 1) j++;
            W($"{withZero[i]} {j - i + 1}\n");
            for (int k = i; k <= j; k++)
            {
                int num = withZero[k];
                if (num == 0) { W("0000000000 65535 f \n"); continue; }
                entryPos[(section, num)] = ms.Position;
                W("0000000000 00000 n \n");
            }
            i = j + 1;
        }
    }

    // A structurally-present primary hint stream. Readers consume it only for progressive
    // (first-page-first) download and tolerate a conservative table, so emit a small zero
    // table rather than an optimized one; the cross-reference tables remain authoritative.
    private static byte[] BuildHintStream() => new byte[16];

    private static byte[]? ExtractObjectBytes(byte[] src, long offset, int expectedNum)
    {
        if (offset <= 0 || offset >= src.Length) return null;
        int start = (int)offset;
        int p = start;
        while (p < src.Length && (src[p] == ' ' || src[p] == '\r' || src[p] == '\n')) p++;
        int numStart = p;
        while (p < src.Length && src[p] >= '0' && src[p] <= '9') p++;
        if (p == numStart) return null;
        int e = IndexOf(src, "endobj", start);
        if (e < 0) return null;
        int end = e + "endobj".Length;
        var slice = new byte[end - start];
        Array.Copy(src, start, slice, 0, slice.Length);
        return slice;
    }

    private static int? FindFirstPageObjectNumber(PdfReader reader, PdfDictionary? pagesRoot)
    {
        if (pagesRoot?.Get("Kids") is PdfArray kids && kids.Count > 0)
        {
            var cur = kids[0];
            for (int guard = 0; guard < 32; guard++)
            {
                if (cur is not PdfIndirectRef r) return null;
                var d = reader.ResolveDict(r);
                if (d is null) return r.ObjectNumber;
                if (d.GetName("Type") == "Page") return r.ObjectNumber;
                if (d.Get("Kids") is PdfArray k && k.Count > 0) { cur = k[0]; continue; }
                return r.ObjectNumber;
            }
        }
        return null;
    }

    private static HashSet<int> CollectFirstPageSection(PdfReader reader, PdfDictionary catalog,
        int rootNum, int firstPageNum)
    {
        var section = new HashSet<int> { rootNum };
        var otherPages = new HashSet<int>();
        var pagesRoot = reader.ResolveDict(catalog.Get("Pages"));
        if (pagesRoot?.Get("Kids") is PdfArray kids)
            for (int i = 1; i < kids.Count; i++)
                if (kids[i] is PdfIndirectRef pr) otherPages.Add(pr.ObjectNumber);

        // The page TREE is not first-page material (PDF 32000-1 Annex F): a tree node names
        // every page beneath it through /Kids, so putting one in the first-page section
        // drags the whole document's page identity into the part that is supposed to be the
        // first page alone. The first page reaches its parent through /Parent, so the walk
        // has to EXCLUDE the nodes, not merely refrain from seeding them.
        var treeNodes = new HashSet<int>();
        {
            var pending = new Stack<PdfObject?>();
            pending.Push(catalog.Get("Pages"));
            var treeGuard = 0;
            while (pending.Count > 0 && treeGuard++ < 4096)
            {
                if (pending.Pop() is not PdfIndirectRef nodeRef) continue;
                if (!treeNodes.Add(nodeRef.ObjectNumber)) continue;
                var node = reader.ResolveDict(nodeRef);
                if (node?.GetName("Type") != "Pages") { treeNodes.Remove(nodeRef.ObjectNumber); continue; }
                if (node.Get("Kids") is PdfArray nodeKids)
                    foreach (var kid in nodeKids) pending.Push(kid);
            }
        }

        var stack = new Stack<int>();
        stack.Push(firstPageNum);
        int guard = 0;
        while (stack.Count > 0 && guard++ < 8192)
        {
            int n = stack.Pop();
            if (otherPages.Contains(n) || treeNodes.Contains(n)) continue;
            if (!section.Add(n) && n != firstPageNum) continue;
            PdfObject? obj;
            try { obj = reader.Resolve(new PdfIndirectRef(n, 0)); } catch { continue; }
            foreach (var refNum in ReferencedObjectNumbers(obj))
                if (!otherPages.Contains(refNum) && !treeNodes.Contains(refNum)
                    && !section.Contains(refNum)) stack.Push(refNum);
        }
        return section;
    }

    private static IEnumerable<int> ReferencedObjectNumbers(PdfObject? obj)
    {
        var result = new List<int>();
        void Walk(PdfObject? o, int depth)
        {
            if (o is null || depth > 64) return;
            switch (o)
            {
                case PdfIndirectRef r: result.Add(r.ObjectNumber); break;
                case PdfArray a: foreach (var e in a) Walk(e, depth + 1); break;
                case PdfStream s: Walk(s.Dict, depth + 1); break;
                case PdfDictionary d: foreach (var k in d.Keys) Walk(d.Get(k), depth + 1); break;
            }
        }
        Walk(obj, 0);
        return result;
    }

    private static string Z() => "0000000000"; // 10-digit fixed-width placeholder

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

    private static int IndexOf(byte[] hay, string needle, int from)
    {
        var n = Encoding.ASCII.GetBytes(needle);
        for (int i = Math.Max(0, from); i <= hay.Length - n.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < n.Length; j++) if (hay[i + j] != n[j]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }
}
