using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
    /// <summary>The stages of the linearization write: the head (header, parameter dictionary, first cross-reference and trailer), the first-page section with its hint stream, the main section, and the placeholder patch.</summary>
    private static void PatchLinearizationOffsets(LinearizeState lz)
    {
        // Patch every fixed-width placeholder now that offsets are final.
        Patch(lz.buf, lz.lin["L"], lz.buf.Length);
        Patch(lz.buf, lz.lin["Hoff"], lz.hintPos);
        Patch(lz.buf, lz.lin["Hlen"], lz.hintEnd - lz.hintStart);
        Patch(lz.buf, lz.lin["E"], lz.firstSectionEnd);
        Patch(lz.buf, lz.lin["T"], lz.mainXrefPos);
        Patch(lz.buf, lz.prevPos, lz.mainXrefPos);
        foreach (var kv in lz.xrefEntryPos)
            Patch(lz.buf, kv.Value, lz.offsets.TryGetValue(kv.Key.num, out var off) ? off : 0);

    }

    /// <summary></summary>
    private static void WriteLinearizedMainSection(LinearizeState lz)
    {
        // 6. Remaining objects.
        foreach (var o in lz.restSection) { lz.offsets[o.Num] = lz.ms.Position; lz.ms.Write(o.Bytes, 0, o.Bytes.Length); lz.W("\n"); }

        // 6b. Reserved space. A linearizer reserves room (between the body and the main
        // cross-reference table) for the overflow hint data of a single-pass write; real
        // linearizers (e.g. Acrobat) emit a comparable reservation, so
        // small linearized files settle on a multi-kilobyte floor rather than the compact size
        // a plain save produces. The region is PDF whitespace and carries no semantics.
        const int ReservedSpace = 2048;
        lz.ms.Write(Enumerable.Repeat((byte)' ', ReservedSpace).ToArray(), 0, ReservedSpace);
        lz.W("\n");

        lz.mainXrefPos = lz.ms.Position;
        EmitXref(lz.ms, lz.allNums, lz.xrefEntryPos, section: 1, lz.W, withFreeHead: true);
        lz.W($"trailer\n<< /Size {lz.size}");
        if (lz.idStr is not null) lz.W(" /ID " + lz.idStr);
        if (lz.reader.Trailer.Get("Encrypt") is PdfIndirectRef enc) lz.W($" /Encrypt {enc.ObjectNumber} 0 R");
        lz.W($" >>\nstartxref\n{lz.firstXrefPos}\n%%EOF\n");
    }

    /// <summary></summary>
    private static void WriteLinearizedFirstSection(LinearizeState lz)
    {
        // 4. First-page section bodies.
        foreach (var o in lz.firstSection) { lz.offsets[o.Num] = lz.ms.Position; lz.ms.Write(o.Bytes, 0, o.Bytes.Length); lz.W("\n"); }

        // 5. Primary hint stream (inside the first section; /H points here).
        lz.offsets[lz.hintObjNum] = lz.ms.Position;
        lz.hintPos = lz.ms.Position;
        lz.hintBytes = BuildHintStream();
        lz.W($"{lz.hintObjNum} 0 obj\n<< /Length {lz.hintBytes.Length} /S {lz.hintBytes.Length} {HintMarker} true >>\nstream\n");
        lz.hintStart = lz.ms.Position; lz.ms.Write(lz.hintBytes, 0, lz.hintBytes.Length); lz.hintEnd = lz.ms.Position;
        lz.W("\nendstream\nendobj\n");
        lz.firstSectionEnd = lz.ms.Position;
    }

    /// <summary></summary>
    private static void WriteLinearizationHead(LinearizeState lz, PdfIndirectRef rootRef)
    {
        // 1. Header — mirror PdfWriter.WriteHeader (version, binary comment, producer comment).
        // Linearizing re-serialises a document the writer has already emitted; it must carry
        // that document's version over. Stamping a fixed version here silently rewrote the
        // header of every linearized save, so a document converted to an older version came
        // back reporting a newer one.
        lz.W($"%PDF-{HeaderVersion(lz.src)}\n");
        lz.ms.WriteByte((byte)'%'); lz.ms.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3 }, 0, 4); lz.ms.WriteByte((byte)'\n');
        lz.W("%   \n");

        // 2. Linearization parameter dictionary (fixed-width placeholders).
        lz.offsets[lz.linObjNum] = lz.ms.Position;
        lz.W($"{lz.linObjNum} 0 obj\n<< /Linearized 1 /L "); lz.lin["L"] = lz.ms.Position; lz.W(Z());
        lz.W(" /H [ "); lz.lin["Hoff"] = lz.ms.Position; lz.W(Z()); lz.W(" "); lz.lin["Hlen"] = lz.ms.Position; lz.W(Z());
        lz.W($" ] /O {lz.firstPageNum} /E "); lz.lin["E"] = lz.ms.Position; lz.W(Z());
        lz.W($" /N {lz.pageCount} /T "); lz.lin["T"] = lz.ms.Position; lz.W(Z());
        lz.W(" >>\nendobj\n");

        lz.firstXrefPos = lz.ms.Position;
        EmitXref(lz.ms, lz.firstXrefNums, lz.xrefEntryPos, section: 0, lz.W, withFreeHead: false);
        lz.W($"trailer\n<< /Size {lz.size} /Root {rootRef.ObjectNumber} 0 R");
        if (lz.reader.Trailer.Get("Info") is PdfIndirectRef inf) lz.W($" /Info {inf.ObjectNumber} 0 R");
        lz.idStr = TrailerIdString(lz.reader.Trailer); if (lz.idStr is not null) lz.W(" /ID " + lz.idStr);
        lz.W(" /Prev "); lz.prevPos = lz.ms.Position; lz.W(Z());
        lz.W(" >>\nstartxref\n0\n%%EOF\n");
    }
}
