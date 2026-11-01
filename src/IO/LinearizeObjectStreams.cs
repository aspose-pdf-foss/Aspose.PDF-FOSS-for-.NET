using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
    // Object stream packing follows the plain save's limits (PdfWriter): objects per stream,
    // and the largest object worth packing.
    private const int ObjectsPerStream = 1000;
    private const int MaxPackedObjectSize = 16384;

    // Cross-reference stream fields (ISO 32000-1 Table 18): the type byte, a 4-byte offset or
    // object stream number, and a 2-byte generation or index - wide enough for any file held
    // in one array and for the free list head's generation 65535.
    private const int XrefOffsetWidth = 4;
    private const int XrefIndexWidth = 2;
    private const int XrefEntryWidth = 1 + XrefOffsetWidth + XrefIndexWidth;
    private const int FreeHeadGeneration = 65535;
    private const string XrefWidths = "[1 4 2]";

    /// <summary>
    /// Whether an object of a page's section may go into an object stream there: an object
    /// that is not a stream, not a page, and not too large. The object stream then belongs to
    /// the same section, as everything packed in it is used by the same pages - page objects
    /// themselves keep their own file offsets (Annex F).
    /// </summary>
    private static bool PackableWithPage(LinearizeState lz, RawObject o) =>
        !o.IsStream && !o.TypeIsPage && !lz.pageSet.Contains(o.Num) && o.End - o.BodyStart <= MaxPackedObjectSize;

    /// <summary>
    /// Whether a part 9 object may go into an object stream: one no page uses (structure
    /// elements, name trees, destinations), so its object stream stays out of every page's
    /// hint entry as well. The outlines keep their own offsets for the outline hint table, and
    /// the information dictionary does too, as in the plain save, so its changing dates do not
    /// move the compressed length.
    /// </summary>
    private static bool PackableAside(LinearizeState lz, RawObject o) =>
        PackableWithPage(lz, o) && o.Num != lz.infoNum
        && lz.otherPageUses[o.Num] == 0 && lz.thumbUses[o.Num] == 0
        && (lz.useFlags[o.Num] & UsedByFirstPage) == 0
        && Classify(lz, o.Num) is Placement.Other or Placement.Unused;

    /// <summary>A section with its packable objects moved into object streams, which take their
    /// place at the end of the section; the packed objects are added to <paramref name="packed"/>.</summary>
    private static List<RawObject> PackSection(LinearizeState lz, List<RawObject> section,
        System.Func<LinearizeState, RawObject, bool> packable, List<RawObject> packed)
    {
        var members = section.Where(o => packable(lz, o)).ToList();
        if (members.Count == 0) return section;
        var kept = section.Where(o => !packable(lz, o)).ToList();
        for (var start = 0; start < members.Count; start += ObjectsPerStream)
        {
            var container = new RawObject { Members = members.GetRange(start, System.Math.Min(ObjectsPerStream, members.Count - start)) };
            for (var i = 0; i < container.Members.Count; i++)
            {
                container.Members[i].Container = container;
                container.Members[i].PackedIndex = i;
            }
            packed.AddRange(container.Members);
            kept.Add(container);
        }
        return kept;
    }

    /// <summary>Writes the packed objects into their object stream, once every object has its new
    /// number, as their references are rewritten on the way.</summary>
    private static void FillObjectStream(LinearizeState lz, RawObject container)
    {
        var header = new StringBuilder();
        var body = new MemoryStream();
        foreach (var o in container.Members!)
        {
            header.Append(o.NewNum.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append(' ');
            var (from, to) = TrimmedBody(o);
            WriteRenumbered(lz, o, body, from, to);
            body.WriteByte((byte)' ');
        }
        var content = new MemoryStream();
        var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        content.Write(headerBytes, 0, headerBytes.Length);
        body.Position = 0;
        body.CopyTo(content);
        var data = ManagedDeflater.DeflateZlib(content.ToArray());
        var dict = $"<< /Type /ObjStm /N {container.Members!.Count} /First {headerBytes.Length} /Filter /FlateDecode /Length {data.Length} >>";
        var text = new MemoryStream();
        void Ascii(string t) { var b = Encoding.ASCII.GetBytes(t); text.Write(b, 0, b.Length); }
        Ascii("0 0 " + ObjKeyword + "\n" + dict + "\nstream\n");
        text.Write(data, 0, data.Length);
        Ascii("\nendstream\n" + EndObjKeyword);
        container.Buf = text.ToArray();
        container.BodyStart = BodyStartOf(container.Buf, 0, container.Buf.Length);
        container.End = container.Buf.Length;
    }

    /// <summary>The object's value without the white space around it.</summary>
    private static (int From, int To) TrimmedBody(RawObject o)
    {
        int from = o.BodyStart, to = o.End - EndObjKeyword.Length;
        while (from < to && IsWhite(o.Buf[from])) from++;
        while (to > from && IsWhite(o.Buf[to - 1])) to--;
        return (from, to);
    }

    private static void WriteXrefEntry(Stream s, int type, long field2, int field3)
    {
        s.WriteByte((byte)type);
        for (var i = XrefOffsetWidth - 1; i >= 0; i--) s.WriteByte((byte)(field2 >> (8 * i)));
        for (var i = XrefIndexWidth - 1; i >= 0; i--) s.WriteByte((byte)(field3 >> (8 * i)));
    }

    private static void PatchXrefOffset(byte[] buf, long entryAt, long offset)
    {
        for (var i = 0; i < XrefOffsetWidth; i++)
            buf[entryAt + 1 + i] = (byte)(offset >> (8 * (XrefOffsetWidth - 1 - i)));
    }

    /// <summary>The first-page cross-reference stream: uncompressed, so its entries can be
    /// patched in place once the offsets are known. Returns where each entry goes.</summary>
    private static List<(int Num, long At)> WriteFirstXrefStream(LinearizeState lz, Dictionary<string, long> patches)
    {
        var count = lz.size - lz.linObjNum;
        lz.W($"{lz.firstXrefNum} 0 obj\n<< /Type /XRef /Size {lz.size} /Index [{lz.linObjNum} {count}] /W {XrefWidths}");
        WriteTrailerIdentity(lz);
        lz.W(" /Prev "); patches["Prev"] = lz.ms.Position; lz.W(Z);
        lz.W($" /Length {count * XrefEntryWidth} >>\nstream\n");
        var entries = new List<(int, long)>();
        for (var n = lz.linObjNum; n < lz.size; n++)
        {
            entries.Add((n, lz.ms.Position));
            if (lz.byNewNum[n]?.Container is { } container) WriteXrefEntry(lz.ms, 2, container.NewNum, lz.byNewNum[n].PackedIndex);
            else WriteXrefEntry(lz.ms, 1, 0, 0);
        }
        lz.W("\nendstream\nendobj\n");
        return entries;
    }

    /// <summary>The main cross-reference stream: object 0, parts 7 to 9 and the packed objects.</summary>
    private static void WriteMainXrefStream(LinearizeState lz)
    {
        var data = new MemoryStream();
        WriteXrefEntry(data, 0, 0, FreeHeadGeneration);
        for (var n = 1; n <= lz.secondHalfCount; n++)
        {
            var o = lz.byNewNum[n];
            if (n == lz.mainXrefNum) WriteXrefEntry(data, 1, lz.mainXrefPos, 0);
            else if (o.Container is { } container) WriteXrefEntry(data, 2, container.NewNum, o.PackedIndex);
            else WriteXrefEntry(data, 1, o.Offset, 0);
        }
        var compressed = ManagedDeflater.DeflateZlib(data.ToArray());
        lz.W($"{lz.mainXrefNum} 0 obj\n<< /Type /XRef /Size {lz.secondHalfCount + 1} /W {XrefWidths}");
        if (lz.idStr is not null) lz.W(" /ID " + lz.idStr);
        lz.W($" /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
        lz.ms.Write(compressed, 0, compressed.Length);
        lz.W($"\nendstream\nendobj\nstartxref\n{lz.firstXrefPos}\n%%EOF\n");
    }
}
