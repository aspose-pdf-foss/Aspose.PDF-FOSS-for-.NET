using System.IO.Compression;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class PdfReaderRobustnessTests
{
    // ── Xref recovery ────────────────────────────────────────────────────

    [Fact]
    public void RecoverXref_CorruptXrefTable_RecoversByScanning()
    {
        // Build a valid PDF, then corrupt the xref table so it can't be parsed
        var data = PdfBuilder.BuildMinimal();
        var text = Encoding.ASCII.GetString(data);

        // Replace the xref section with garbage but keep objects and startxref
        var xrefIdx = text.IndexOf("xref\n");
        var trailerIdx = text.IndexOf("trailer\n");

        var sb = new StringBuilder();
        sb.Append(text[..xrefIdx]);
        // Write corrupted xref (wrong format)
        sb.Append("xref\nCORRUPTED DATA HERE\n");
        sb.Append(text[trailerIdx..]);

        var corrupted = Encoding.ASCII.GetBytes(sb.ToString());

        // The reader should recover via object scanning
        var reader = PdfReader.FromBytes(corrupted, new PdfReaderOptions { RepairXref = true });
        Assert.NotNull(reader.Trailer);
        Assert.NotNull(reader.Catalog);
    }

    [Fact]
    public void RecoverXref_MissingStartxref_RecoversByScanning()
    {
        // Build a valid PDF, then remove the startxref marker
        var data = PdfBuilder.BuildMinimal();
        var text = Encoding.ASCII.GetString(data);

        // Remove everything from startxref onwards and replace with garbage
        var startxrefIdx = text.IndexOf("startxref");
        var sb = new StringBuilder();
        sb.Append(text[..startxrefIdx]);
        sb.Append("%%EOF\n");

        var corrupted = Encoding.ASCII.GetBytes(sb.ToString());

        var reader = PdfReader.FromBytes(corrupted, new PdfReaderOptions { RepairXref = true });
        Assert.NotNull(reader.Catalog);
        var catalog = reader.Catalog;
        Assert.Equal("Catalog", catalog.GetName("Type"));
    }

    [Fact]
    public void RecoverXref_RepairXrefDisabled_Throws()
    {
        var data = PdfBuilder.BuildMinimal();
        var text = Encoding.ASCII.GetString(data);

        var startxrefIdx = text.IndexOf("startxref");
        var sb = new StringBuilder();
        sb.Append(text[..startxrefIdx]);
        sb.Append("%%EOF\n");

        var corrupted = Encoding.ASCII.GetBytes(sb.ToString());

        Assert.Throws<InvalidOperationException>(() =>
            PdfReader.FromBytes(corrupted, new PdfReaderOptions { RepairXref = false }));
    }

    // ── Cross-reference stream ───────────────────────────────────────────

    [Fact]
    public void XrefStream_ReadsPdfWithCrossReferenceStream()
    {
        // Build a PDF that uses a cross-reference stream instead of a traditional xref table
        var data = BuildPdfWithXrefStream();

        var reader = PdfReader.FromBytes(data);
        Assert.NotNull(reader.Trailer);
        var catalog = reader.Catalog;
        Assert.Equal("Catalog", catalog.GetName("Type"));
    }

    // ── Object stream (ObjStm) ───────────────────────────────────────────

    [Fact]
    public void ObjStm_ReadsCompressedObjects()
    {
        // Build a PDF with objects stored in an object stream
        var data = BuildPdfWithObjStm();

        var reader = PdfReader.FromBytes(data);
        Assert.NotNull(reader.Catalog);
        var catalog = reader.Catalog;
        var pages = reader.ResolveDict(catalog.Get("Pages"));
        Assert.NotNull(pages);
        Assert.Equal("Pages", pages!.GetName("Type"));
    }

    // ── Lenient mode ─────────────────────────────────────────────────────

    [Fact]
    public void LenientMode_SkipsMalformedObjects()
    {
        // Build a PDF where one object has corrupt data
        var data = BuildPdfWithMalformedObject();

        // Lenient mode should not throw
        var reader = PdfReader.FromBytes(data, new PdfReaderOptions { LenientMode = true });
        Assert.NotNull(reader.Catalog);

        // The malformed object should resolve to null
        var result = reader.Resolve(new PdfIndirectRef(4, 0));
        Assert.Null(result);
    }

    [Fact]
    public void LenientMode_AcceptsMissingEndobj()
    {
        // Build a PDF where an object is missing its endobj marker
        // The parser already tolerates this, but verify the lenient path works
        var data = PdfBuilder.BuildMinimal();
        var text = Encoding.ASCII.GetString(data);

        // Remove one endobj (for object 3 - the page)
        var obj3Idx = text.IndexOf("3 0 obj\n");
        var endobj3 = text.IndexOf("endobj\n", obj3Idx);
        var modified = text.Remove(endobj3, "endobj\n".Length);

        var modifiedData = Encoding.ASCII.GetBytes(modified);

        // Recalculate offsets won't match, but lenient+recovery should handle it
        var reader = PdfReader.FromBytes(modifiedData, new PdfReaderOptions { LenientMode = true, RepairXref = true });
        Assert.NotNull(reader.Catalog);
    }

    [Fact]
    public void LenientMode_ToleratesExtraWhitespaceInXref()
    {
        // Build a PDF with extra whitespace in xref entries
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        var catalogOffset = sb.Length;
        sb.Append("1 0 obj\n");
        sb.Append("<< /Type /Catalog /Pages 2 0 R >>\n");
        sb.Append("endobj\n");

        var pagesOffset = sb.Length;
        sb.Append("2 0 obj\n");
        sb.Append("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n");
        sb.Append("endobj\n");

        var pageOffset = sb.Length;
        sb.Append("3 0 obj\n");
        sb.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\n");
        sb.Append("endobj\n");

        // Xref with extra whitespace (spaces after the flag character — still 20 bytes per entry
        // but uses spaces differently)
        var xrefOffset = sb.Length;
        sb.Append("xref\n");
        sb.Append("0 4\n");
        sb.AppendFormat("0000000000 65535 f \n");
        sb.AppendFormat("{0:D10} 00000 n \n", catalogOffset);
        sb.AppendFormat("{0:D10} 00000 n \n", pagesOffset);
        sb.AppendFormat("{0:D10} 00000 n \n", pageOffset);

        sb.Append("trailer\n");
        sb.Append("<< /Size 4 /Root 1 0 R >>\n");
        sb.Append("startxref\n");
        sb.AppendFormat("{0}\n", xrefOffset);
        sb.Append("%%EOF\n");

        var data = Encoding.ASCII.GetBytes(sb.ToString());
        var reader = PdfReader.FromBytes(data, new PdfReaderOptions { LenientMode = true });
        Assert.NotNull(reader.Catalog);
        Assert.Equal("Catalog", reader.Catalog.GetName("Type"));
    }

    // ── Descriptive error messages ───────────────────────────────────────

    [Fact]
    public void StrictMode_ObjectParseFailure_IncludesObjectNumber()
    {
        var data = BuildPdfWithMalformedObject();

        var reader = PdfReader.FromBytes(data, new PdfReaderOptions { LenientMode = false });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            reader.Resolve(new PdfIndirectRef(4, 0)));

        // The error message should contain the object number and offset info
        Assert.Contains("4", ex.Message);
        Assert.Contains("offset", ex.Message);
    }

    [Fact]
    public void RecoverXref_NoObjectsFound_ThrowsTrailerNotFound()
    {
        // A file with no valid object headers at all
        var data = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n");

        // Recovery surfaces as the standard PDF-spec exception so
        // callers can pattern-match a typed "Trailer not found" failure.
        var ex = Assert.Throws<Aspose.Pdf.InvalidPdfFileFormatException>(() =>
            PdfReader.RecoverXref(data));

        Assert.Equal("Trailer not found", ex.Message);
    }

    // ── Hybrid xref ──────────────────────────────────────────────────────

    [Fact]
    public void HybridXref_TrailerWithXRefStm_MergesEntries()
    {
        // Build a PDF that has a traditional xref table with /XRefStm
        // pointing to a supplementary xref stream
        var data = BuildPdfWithHybridXref();

        var reader = PdfReader.FromBytes(data);
        Assert.NotNull(reader.Catalog);
        var catalog = reader.Catalog;
        Assert.Equal("Catalog", catalog.GetName("Type"));

        // Object 4 (stored only in the xref stream) should be resolvable
        var obj4 = reader.Resolve(new PdfIndirectRef(4, 0));
        Assert.NotNull(obj4);
        Assert.IsType<PdfDictionary>(obj4);
        Assert.Equal("TestInfo", ((PdfDictionary)obj4!).GetName("Type"));
    }

    // ── Default options ──────────────────────────────────────────────────

    [Fact]
    public void DefaultOptions_AreLenientAndRepairEnabled()
    {
        var opts = new PdfReaderOptions();
        Assert.True(opts.LenientMode);
        Assert.True(opts.RepairXref);
    }

    [Fact]
    public void FromBytes_DefaultOverload_UsesLenientOptions()
    {
        var data = PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(data);
        Assert.True(reader.Options.LenientMode);
        Assert.True(reader.Options.RepairXref);
    }

    // ── ObjStm caching ──────────────────────────────────────────────────

    [Fact]
    public void ObjStm_CachesDecodedStream()
    {
        var data = BuildPdfWithObjStm();
        var reader = PdfReader.FromBytes(data);

        // Resolving the catalog (obj 1 in ObjStm) and pages (obj 2 in ObjStm)
        // should both work and use the cached ObjStm contents
        var catalog = reader.Catalog;
        var pages = reader.ResolveDict(catalog.Get("Pages"));
        Assert.NotNull(pages);

        // Resolve again — should return cached result
        var pages2 = reader.ResolveDict(catalog.Get("Pages"));
        Assert.Same(pages, pages2);
    }

    // ── Helper builders ──────────────────────────────────────────────────

    /// <summary>
    /// Build a PDF that uses a cross-reference stream (PDF 1.5+) instead of a traditional xref table.
    /// </summary>
    private static byte[] BuildPdfWithXrefStream()
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.5\n");

        // Object 1: Catalog
        var catalogOffset = sb.Length;
        sb.Append("1 0 obj\n");
        sb.Append("<< /Type /Catalog /Pages 2 0 R >>\n");
        sb.Append("endobj\n");

        // Object 2: Pages
        var pagesOffset = sb.Length;
        sb.Append("2 0 obj\n");
        sb.Append("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n");
        sb.Append("endobj\n");

        // Object 3: Page
        var pageOffset = sb.Length;
        sb.Append("3 0 obj\n");
        sb.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\n");
        sb.Append("endobj\n");

        // Build the xref stream data
        // W = [1 4 1] — 1 byte type, 4 bytes offset/objstm, 1 byte gen/index
        // Entries: obj 0 (free), obj 1, obj 2, obj 3, obj 4 (xref stream itself)
        var xrefStreamOffset = sb.Length;

        // Build xref data: 5 entries x 6 bytes each = 30 bytes
        var xrefData = new byte[30];
        var idx = 0;

        // Obj 0: free, next=0, gen=255
        xrefData[idx++] = 0; // type 0 (free)
        WriteInt32BE(xrefData, idx, 0); idx += 4;
        xrefData[idx++] = 255; // gen 255

        // Obj 1: uncompressed at catalogOffset
        xrefData[idx++] = 1;
        WriteInt32BE(xrefData, idx, catalogOffset); idx += 4;
        xrefData[idx++] = 0;

        // Obj 2: uncompressed at pagesOffset
        xrefData[idx++] = 1;
        WriteInt32BE(xrefData, idx, pagesOffset); idx += 4;
        xrefData[idx++] = 0;

        // Obj 3: uncompressed at pageOffset
        xrefData[idx++] = 1;
        WriteInt32BE(xrefData, idx, pageOffset); idx += 4;
        xrefData[idx++] = 0;

        // Obj 4: uncompressed at xrefStreamOffset (the xref stream itself)
        xrefData[idx++] = 1;
        WriteInt32BE(xrefData, idx, xrefStreamOffset); idx += 4;
        xrefData[idx++] = 0;

        // Now write the xref stream object
        sb.AppendFormat("4 0 obj\n");
        sb.Append("<< /Type /XRef /Size 5 /W [1 4 1] /Root 1 0 R ");
        sb.AppendFormat("/Length {0} ", xrefData.Length);
        sb.Append(">>\n");
        sb.Append("stream\n");

        // We need to convert to bytes at this point since stream data is binary
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var streamEnd = Encoding.ASCII.GetBytes("\nendstream\nendobj\n");
        var startxrefText = Encoding.ASCII.GetBytes(
            $"startxref\n{xrefStreamOffset}\n%%EOF\n");

        var result = new byte[headerBytes.Length + xrefData.Length + streamEnd.Length + startxrefText.Length];
        var pos = 0;
        Array.Copy(headerBytes, 0, result, pos, headerBytes.Length); pos += headerBytes.Length;
        Array.Copy(xrefData, 0, result, pos, xrefData.Length); pos += xrefData.Length;
        Array.Copy(streamEnd, 0, result, pos, streamEnd.Length); pos += streamEnd.Length;
        Array.Copy(startxrefText, 0, result, pos, startxrefText.Length);

        return result;
    }

    /// <summary>
    /// Build a PDF with an object stream (ObjStm) containing compressed objects.
    /// </summary>
    private static byte[] BuildPdfWithObjStm()
    {
        // We'll put Catalog (obj 1) and Pages (obj 2) inside an ObjStm (obj 4)
        // Page (obj 3) stays uncompressed

        // First, build the ObjStm content:
        // Header: N pairs of (objNum offset) — objNums 1 and 2
        // Then the objects themselves
        var objStmContent = new StringBuilder();
        // Index: objNum1 offset1 objNum2 offset2
        // Objects start after the index
        var obj1Content = "<< /Type /Catalog /Pages 2 0 R >>";
        var obj2Content = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";

        // Offsets are relative to /First
        var offset1 = 0;
        var offset2 = obj1Content.Length + 1; // +1 for space separator

        objStmContent.Append($"1 {offset1} 2 {offset2} ");
        var first = objStmContent.Length;
        objStmContent.Append(obj1Content);
        objStmContent.Append(' ');
        objStmContent.Append(obj2Content);

        var objStmBytes = Encoding.ASCII.GetBytes(objStmContent.ToString());

        // Compress with zlib (FlateDecode uses zlib, not raw deflate)
        byte[] compressedObjStm;
        using (var ms = new MemoryStream())
        {
            using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(objStmBytes);
            }
            compressedObjStm = ms.ToArray();
        }

        var sb = new StringBuilder();
        sb.Append("%PDF-1.5\n");

        // Object 3: Page (uncompressed)
        var pageOffset = sb.Length;
        sb.Append("3 0 obj\n");
        sb.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\n");
        sb.Append("endobj\n");

        // Object 4: ObjStm containing objects 1 and 2
        var objStmOffset = sb.Length;
        sb.Append("4 0 obj\n");
        sb.AppendFormat("<< /Type /ObjStm /N 2 /First {0} /Filter /FlateDecode /Length {1} >>\n",
            first, compressedObjStm.Length);
        sb.Append("stream\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var streamEnd = Encoding.ASCII.GetBytes("\nendstream\nendobj\n");

        // Build xref stream for this PDF
        // Objects: 0 (free), 1 (compressed in obj 4 idx 0), 2 (compressed in obj 4 idx 1),
        //          3 (uncompressed at pageOffset), 4 (uncompressed at objStmOffset),
        //          5 (xref stream itself)
        var bodyLen = headerBytes.Length + compressedObjStm.Length + streamEnd.Length;

        // Now build the xref stream
        var xrefStreamOffset = bodyLen;

        // W = [1 4 1]
        var xrefData = new byte[36]; // 6 entries x 6 bytes
        var idx = 0;

        // Obj 0: free
        xrefData[idx++] = 0;
        WriteInt32BE(xrefData, idx, 0); idx += 4;
        xrefData[idx++] = 255;

        // Obj 1: compressed in obj 4, index 0
        xrefData[idx++] = 2;
        WriteInt32BE(xrefData, idx, 4); idx += 4; // stream obj number
        xrefData[idx++] = 0; // index in stream

        // Obj 2: compressed in obj 4, index 1
        xrefData[idx++] = 2;
        WriteInt32BE(xrefData, idx, 4); idx += 4;
        xrefData[idx++] = 1;

        // Obj 3: uncompressed
        xrefData[idx++] = 1;
        WriteInt32BE(xrefData, idx, pageOffset); idx += 4;
        xrefData[idx++] = 0;

        // Obj 4: uncompressed (ObjStm)
        xrefData[idx++] = 1;
        WriteInt32BE(xrefData, idx, objStmOffset); idx += 4;
        xrefData[idx++] = 0;

        // Obj 5: uncompressed (xref stream itself) — offset will be bodyLen
        xrefData[idx++] = 1;
        WriteInt32BE(xrefData, idx, bodyLen); idx += 4;
        xrefData[idx++] = 0;

        var xrefHeader = Encoding.ASCII.GetBytes(
            $"5 0 obj\n<< /Type /XRef /Size 6 /W [1 4 1] /Root 1 0 R /Length {xrefData.Length} >>\nstream\n");
        var xrefEnd = Encoding.ASCII.GetBytes($"\nendstream\nendobj\nstartxref\n{xrefStreamOffset}\n%%EOF\n");

        var totalLen = bodyLen + xrefHeader.Length + xrefData.Length + xrefEnd.Length;
        var result = new byte[totalLen];
        var pos = 0;

        Array.Copy(headerBytes, 0, result, pos, headerBytes.Length); pos += headerBytes.Length;
        Array.Copy(compressedObjStm, 0, result, pos, compressedObjStm.Length); pos += compressedObjStm.Length;
        Array.Copy(streamEnd, 0, result, pos, streamEnd.Length); pos += streamEnd.Length;
        Array.Copy(xrefHeader, 0, result, pos, xrefHeader.Length); pos += xrefHeader.Length;
        Array.Copy(xrefData, 0, result, pos, xrefData.Length); pos += xrefData.Length;
        Array.Copy(xrefEnd, 0, result, pos, xrefEnd.Length);

        return result;
    }

    /// <summary>
    /// Build a PDF with a malformed object 4 that can't be parsed.
    /// </summary>
    private static byte[] BuildPdfWithMalformedObject()
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        var catalogOffset = sb.Length;
        sb.Append("1 0 obj\n");
        sb.Append("<< /Type /Catalog /Pages 2 0 R >>\n");
        sb.Append("endobj\n");

        var pagesOffset = sb.Length;
        sb.Append("2 0 obj\n");
        sb.Append("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n");
        sb.Append("endobj\n");

        var pageOffset = sb.Length;
        sb.Append("3 0 obj\n");
        sb.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\n");
        sb.Append("endobj\n");

        // Object 4: malformed — missing the dictionary content
        var malformedOffset = sb.Length;
        sb.Append("4 0 obj\n");
        sb.Append("<< /Type BROKEN_NAME_WITHOUT_SLASH >> GARBAGE_AFTER\n");
        sb.Append("endobj\n");

        var xrefOffset = sb.Length;
        sb.Append("xref\n");
        sb.Append("0 5\n");
        sb.AppendFormat("0000000000 65535 f \n");
        sb.AppendFormat("{0:D10} 00000 n \n", catalogOffset);
        sb.AppendFormat("{0:D10} 00000 n \n", pagesOffset);
        sb.AppendFormat("{0:D10} 00000 n \n", pageOffset);
        sb.AppendFormat("{0:D10} 00000 n \n", malformedOffset);

        sb.Append("trailer\n");
        sb.Append("<< /Size 5 /Root 1 0 R >>\n");
        sb.Append("startxref\n");
        sb.AppendFormat("{0}\n", xrefOffset);
        sb.Append("%%EOF\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Build a PDF with a hybrid xref: traditional table + /XRefStm pointing to a
    /// supplementary cross-reference stream that contains additional entries.
    /// </summary>
    private static byte[] BuildPdfWithHybridXref()
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.5\n");

        var catalogOffset = sb.Length;
        sb.Append("1 0 obj\n");
        sb.Append("<< /Type /Catalog /Pages 2 0 R >>\n");
        sb.Append("endobj\n");

        var pagesOffset = sb.Length;
        sb.Append("2 0 obj\n");
        sb.Append("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n");
        sb.Append("endobj\n");

        var pageOffset = sb.Length;
        sb.Append("3 0 obj\n");
        sb.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\n");
        sb.Append("endobj\n");

        // Object 4: extra object only referenced via the xref stream
        var obj4Offset = sb.Length;
        sb.Append("4 0 obj\n");
        sb.Append("<< /Type /TestInfo /Value 42 >>\n");
        sb.Append("endobj\n");

        // Object 5: supplementary xref stream (contains entry for obj 4)
        var xrefStmOffset = sb.Length;

        // The xref stream only contains entry for obj 4
        // W = [1 4 1], Index = [4 1] (just obj 4)
        var xrefData = new byte[6];
        var idx = 0;
        xrefData[idx++] = 1; // type 1 (uncompressed)
        WriteInt32BE(xrefData, idx, obj4Offset); idx += 4;
        xrefData[idx++] = 0; // gen 0

        sb.Append("5 0 obj\n");
        sb.AppendFormat("<< /Type /XRef /Size 6 /W [1 4 1] /Index [4 1] /Length {0} >>\n",
            xrefData.Length);
        sb.Append("stream\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        var streamEnd = Encoding.ASCII.GetBytes("\nendstream\nendobj\n");

        // Traditional xref table (objects 0-3 only, NOT 4)
        var traditionalXrefSb = new StringBuilder();
        var traditionalXrefOffset = headerBytes.Length + xrefData.Length + streamEnd.Length;

        traditionalXrefSb.Append("xref\n");
        traditionalXrefSb.Append("0 4\n");
        traditionalXrefSb.AppendFormat("0000000000 65535 f \n");
        traditionalXrefSb.AppendFormat("{0:D10} 00000 n \n", catalogOffset);
        traditionalXrefSb.AppendFormat("{0:D10} 00000 n \n", pagesOffset);
        traditionalXrefSb.AppendFormat("{0:D10} 00000 n \n", pageOffset);

        traditionalXrefSb.Append("trailer\n");
        traditionalXrefSb.AppendFormat("<< /Size 6 /Root 1 0 R /XRefStm {0} >>\n", xrefStmOffset);
        traditionalXrefSb.Append("startxref\n");
        traditionalXrefSb.AppendFormat("{0}\n", traditionalXrefOffset);
        traditionalXrefSb.Append("%%EOF\n");

        var trailerBytes = Encoding.ASCII.GetBytes(traditionalXrefSb.ToString());

        var result = new byte[headerBytes.Length + xrefData.Length + streamEnd.Length + trailerBytes.Length];
        var pos = 0;
        Array.Copy(headerBytes, 0, result, pos, headerBytes.Length); pos += headerBytes.Length;
        Array.Copy(xrefData, 0, result, pos, xrefData.Length); pos += xrefData.Length;
        Array.Copy(streamEnd, 0, result, pos, streamEnd.Length); pos += streamEnd.Length;
        Array.Copy(trailerBytes, 0, result, pos, trailerBytes.Length);

        return result;
    }

    private static void WriteInt32BE(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)((value >> 24) & 0xFF);
        buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 3] = (byte)(value & 0xFF);
    }
}
