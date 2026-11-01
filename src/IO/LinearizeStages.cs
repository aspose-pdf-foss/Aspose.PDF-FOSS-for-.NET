using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
    // A reservation between the body and the main cross-reference table, as single-pass
    // linearizers leave for overflow hint data. It is a comment, so the object before it
    // still ends at its own line feed - a reader measures an object up to the next token.
    private const int ReservedSpace = 2048;

    /// <summary>The stages of the linearization write: the head (header, parameter dictionary,
    /// first-page cross-reference section), part 4, the primary hint stream, parts 6 to 9, the
    /// main cross-reference section, and the placeholder patch. With packed objects both
    /// cross-reference sections are streams.</summary>
    private static byte[] WriteLinearized(LinearizeState lz)
    {
        lz.byNewNum = new RawObject[lz.size];
        foreach (var o in lz.objs.Values)
            lz.byNewNum[o.NewNum] = o;
        foreach (var part in new[] { lz.part4, lz.part6, lz.part7, lz.part8, lz.part9 })
            foreach (var o in part)
            {
                if (o.Members is not null) FillObjectStream(lz, o);
                lz.byNewNum[o.NewNum] = o;
                o.Length = EmittedLength(lz, o);
            }

        lz.ms = new MemoryStream();
        var patches = new Dictionary<string, long>();
        WriteLinearizationDictionary(lz, patches);
        lz.firstXrefPos = lz.ms.Position;
        var xrefStreams = lz.mainXrefNum > 0;
        var firstXrefEntries = xrefStreams ? WriteFirstXrefStream(lz, patches) : WriteFirstXrefTable(lz, patches);
        WriteObjects(lz, lz.part4);
        WritePrimaryHintStream(lz);
        WriteObjects(lz, lz.part6);
        lz.firstSectionEnd = lz.ms.Position;
        WriteObjects(lz, lz.part7);
        WriteObjects(lz, lz.part8);
        WriteObjects(lz, lz.part9);
        lz.W("%" + new string(' ', ReservedSpace - 1) + "\n");
        lz.mainXrefPos = lz.ms.Position;
        if (xrefStreams) WriteMainXrefStream(lz);
        else WriteMainXrefTable(lz);

        var buf = lz.ms.ToArray();
        Patch(buf, patches["L"], buf.Length);
        Patch(buf, patches["Hoff"], lz.hintPos);
        Patch(buf, patches["Hlen"], lz.hintObject.Length);
        Patch(buf, patches["E"], lz.firstSectionEnd);
        // /T: the white space before the main section's first entry - for a stream, before the object.
        Patch(buf, patches["T"], xrefStreams ? lz.mainXrefPos - 1 : lz.mainXrefPos + MainXrefSubsection(lz).Length - 1);
        Patch(buf, patches["Prev"], lz.mainXrefPos);
        foreach (var (num, at) in firstXrefEntries)
        {
            if (lz.byNewNum[num]?.Container is not null) continue; // a packed entry names its stream
            var offset = num == lz.hintObjNum ? lz.hintPos : num == lz.linObjNum ? lz.linDictPos
                : num == lz.firstXrefNum ? lz.firstXrefPos : lz.byNewNum[num].Offset;
            if (xrefStreams) PatchXrefOffset(buf, at, offset);
            else Patch(buf, at, offset);
        }
        return buf;
    }

    private static void WriteObjects(LinearizeState lz, List<RawObject> part)
    {
        foreach (var o in part)
        {
            o.Offset = lz.ms.Position;
            EmitObject(lz, o);
        }
    }

    /// <summary>The primary hint stream, built from where parts 6 to 9 will lie without it.</summary>
    private static void WritePrimaryHintStream(LinearizeState lz)
    {
        lz.hintPos = lz.ms.Position;
        var hintFree = new Dictionary<RawObject, long>();
        var at = lz.hintPos;
        foreach (var part in new[] { lz.part6, lz.part7, lz.part8, lz.part9 })
            foreach (var o in part) { hintFree[o] = at; at += o.Length; }
        var tables = BuildHintTables(lz, hintFree, out var shared, out var outline);
        var data = ManagedDeflater.DeflateZlib(tables);
        var dict = $"{lz.hintObjNum} 0 obj\n<< /Filter /FlateDecode /Length {data.Length} /S {shared}"
            + (outline > 0 ? $" /O {outline}" : "") + $" {HintMarker} true >>\nstream\n";
        var hint = new MemoryStream();
        var head = System.Text.Encoding.ASCII.GetBytes(dict);
        hint.Write(head, 0, head.Length);
        hint.Write(data, 0, data.Length);
        var tail = System.Text.Encoding.ASCII.GetBytes("\nendstream\nendobj\n");
        hint.Write(tail, 0, tail.Length);
        lz.hintObject = hint.ToArray();
        lz.ms.Write(lz.hintObject, 0, lz.hintObject.Length);
    }

    private static string MainXrefSubsection(LinearizeState lz) =>
        $"xref\n0 {lz.secondHalfCount + 1}\n";

    /// <summary>The main cross-reference section: object 0 and parts 7 to 9, whose offsets are
    /// final by now, and a trailer that points back at the first-page section.</summary>
    private static void WriteMainXrefTable(LinearizeState lz)
    {
        lz.W(MainXrefSubsection(lz));
        lz.W("0000000000 65535 f \n");
        for (var n = 1; n <= lz.secondHalfCount; n++)
            lz.W(lz.byNewNum[n].Offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        lz.W($"trailer\n<< /Size {lz.size}");
        if (lz.idStr is not null) lz.W(" /ID " + lz.idStr);
        lz.W($" >>\nstartxref\n{lz.firstXrefPos}\n%%EOF\n");
    }

    /// <summary>The header and the linearization parameter dictionary, its values fixed-width
    /// placeholders.</summary>
    private static void WriteLinearizationDictionary(LinearizeState lz, Dictionary<string, long> patches)
    {
        // Linearizing re-serialises a document the writer has already emitted; it carries that
        // document's version over (PdfWriter.WriteHeader writes the same three lines).
        lz.W($"%PDF-{HeaderVersion(lz.src)}\n");
        lz.ms.WriteByte((byte)'%'); lz.ms.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3 }, 0, 4); lz.ms.WriteByte((byte)'\n');
        lz.W("%   \n");

        lz.linDictPos = lz.ms.Position;
        lz.W($"{lz.linObjNum} 0 obj\n<< /Linearized 1 /L "); patches["L"] = lz.ms.Position; lz.W(Z);
        lz.W(" /H [ "); patches["Hoff"] = lz.ms.Position; lz.W(Z); lz.W(" "); patches["Hlen"] = lz.ms.Position; lz.W(Z);
        lz.W($" ] /O {lz.objs[lz.pages[0]].NewNum} /E "); patches["E"] = lz.ms.Position; lz.W(Z);
        lz.W($" /N {lz.pages.Count} /T "); patches["T"] = lz.ms.Position; lz.W(Z);
        lz.W(" >>\nendobj\n");
    }

    /// <summary>The first-page cross-reference table and its trailer, every offset a fixed-width
    /// placeholder. Returns where each entry's offset goes.</summary>
    private static List<(int Num, long At)> WriteFirstXrefTable(LinearizeState lz, Dictionary<string, long> patches)
    {
        var entries = new List<(int, long)>();
        lz.W($"xref\n{lz.linObjNum} {lz.size - lz.linObjNum}\n");
        for (var n = lz.linObjNum; n < lz.size; n++)
        {
            entries.Add((n, lz.ms.Position));
            lz.W(Z + " 00000 n \n");
        }
        lz.W($"trailer\n<< /Size {lz.size}");
        WriteTrailerIdentity(lz);
        lz.W(" /Prev "); patches["Prev"] = lz.ms.Position; lz.W(Z);
        lz.W(" >>\nstartxref\n0\n%%EOF\n");
        return entries;
    }

    /// <summary>The first-page trailer's /Root, /Info and /ID.</summary>
    private static void WriteTrailerIdentity(LinearizeState lz)
    {
        lz.W($" /Root {lz.objs[lz.rootNum].NewNum} 0 R");
        if (lz.infoNum > 0 && lz.objs.TryGetValue(lz.infoNum, out var info)) lz.W($" /Info {info.NewNum} 0 R");
        lz.idStr = TrailerIdString(lz.reader.Trailer);
        if (lz.idStr is not null) lz.W(" /ID " + lz.idStr);
    }
}
