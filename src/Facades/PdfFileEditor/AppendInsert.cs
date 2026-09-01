using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    /// <summary>
    /// Append pages from a source PDF to an input PDF.
    /// </summary>
    /// <param name="inputPdf">Destination PDF bytes.</param>
    /// <param name="portPdf">Source PDF bytes to append pages from.</param>
    /// <param name="startPage">Start page in source (1-based, inclusive).</param>
    /// <param name="endPage">End page in source (1-based, inclusive).</param>
    public byte[] Append(byte[] inputPdf, byte[] portPdf, int startPage, int endPage)
        => Append(inputPdf, new[] { portPdf }, startPage, endPage);

    /// <summary>
    /// Append pages from multiple source PDFs to an input PDF.
    /// </summary>
    public byte[] Append(byte[] inputPdf, byte[][] portPdfs, int startPage, int endPage)
    {
        // When the destination and at least one appended source both carry an XFA
        // template, go through Concatenate: the page-import path below keeps the
        // destination's AcroForm untouched, so the sources' /XFA packets would be
        // dropped instead of merged (top template subforms re-parented under a
        // synthetic "root", colliding names disambiguated per UniqueSuffix /
        // KeepFieldsUnique, datasets merged in parallel, AcroForm tree re-rooted).
        var xfaPieces = BuildXfaAppendInputs(inputPdf, portPdfs, startPage, endPage);
        if (xfaPieces is not null) return Concatenate(xfaPieces);

        // Open the destination document and import the requested source pages onto it via
        // the cross-doc Pages.Add path, then save. Unlike the byte-level Concatenate this
        // keeps the destination's own catalog (AcroForm/outlines) intact, remaps the added
        // pages' intra-document links onto the imported pages, and — because it writes
        // through the normal document serializer instead of expanding every object into a
        // plain indirect entry — produces a compact file (a 10-page
        // image-heavy append is ~3 MB, not the ~4.6 MB Concatenate emitted).
        using var destDoc = Document.Open(inputPdf);
        foreach (var portData in portPdfs)
        {
            using var portDoc = Document.Open(portData);
            var last = Math.Min(endPage, portDoc.PageCount);
            for (var i = Math.Max(1, startPage); i <= last; i++)
                destDoc.Pages.Add(portDoc.Pages[i]);
        }
        return destDoc.ToArray();
    }

    /// <summary>
    /// Insert pages from a source PDF into a destination PDF at a given position.
    /// </summary>
    /// <param name="inputPdf">Destination PDF bytes.</param>
    /// <param name="insertLocation">1-based page number in the destination after which to insert.
    /// Pages before this position remain before the inserted pages; pages from this position onward
    /// follow the inserted pages.</param>
    /// <param name="portPdf">Source PDF bytes.</param>
    /// <param name="startPage">Start page in source (1-based, inclusive).</param>
    /// <param name="endPage">End page in source (1-based, inclusive).</param>
    public byte[] Insert(byte[] inputPdf, int insertLocation, byte[] portPdf, int startPage, int endPage)
    {
        var extracted = Extract(portPdf, startPage, endPage);

        using var destDoc = Document.Open(inputPdf);
        var destCount = destDoc.PageCount;

        // Clamp insert location: 0 means prepend, >destCount means append
        var pos = Math.Max(0, Math.Min(insertLocation, destCount));

        if (pos == 0)
        {
            // Prepend: extracted + inputPdf
            return Concatenate(extracted, inputPdf);
        }

        if (pos >= destCount)
        {
            // Append: inputPdf + extracted
            return Concatenate(inputPdf, extracted);
        }

        // Split the destination into before and after the insert position
        var before = Extract(inputPdf, 1, pos);
        var after = Extract(inputPdf, pos + 1, destCount);
        return Concatenate(before, extracted, after);
    }

    /// <summary>
    /// Insert specific pages from a source PDF into a destination PDF at a given position.
    /// </summary>
    /// <param name="inputPdf">Destination PDF bytes.</param>
    /// <param name="insertLocation">1-based page number after which to insert.</param>
    /// <param name="portPdf">Source PDF bytes.</param>
    /// <param name="pageNumbers">1-based page numbers from the source to insert.</param>
    public byte[] Insert(byte[] inputPdf, int insertLocation, byte[] portPdf, int[] pageNumbers)
    {
        if (pageNumbers.Length == 0) return inputPdf;

        using var destDoc = Document.Open(inputPdf);
        using var portDoc = Document.Open(portPdf);
        var portTotal = portDoc.PageCount;

        var portPages = new List<Page>();
        foreach (var pn in pageNumbers)
            if (pn >= 1 && pn <= portTotal) portPages.Add(portDoc.Pages[pn]);
        if (portPages.Count == 0) return inputPdf;

        // Insert the port pages directly through the document's page collection so shared
        // resources are imported once (the clone cache dedupes them) instead of duplicated
        // by a byte-level concatenation. Map the facade's insert location — which clamps to
        // [0, destCount] and appends when it reaches the last page — to the 1-based
        // "insert before" index the page collection expects.
        var destCount = destDoc.PageCount;
        var pos = Math.Max(0, Math.Min(insertLocation, destCount));
        int insertBefore = pos <= 0 ? 1 : pos >= destCount ? destCount + 1 : pos + 1;

        destDoc.Pages.Insert(insertBefore, portPages.ToArray());
        return destDoc.ToArray();
    }

    /// <summary>Append pages from source stream(s) to input stream and write to output.</summary>
    public bool Append(Stream inputStream, Stream[] portStreams, int startPage, int endPage, Stream outputStream)
    {
        var input = ReadStream(inputStream);
        var ports = portStreams.Select(ReadStream).ToArray();
        var result = Append(input, ports, startPage, endPage);
        outputStream.Write(result, 0, result.Length);
        return true;
    }

    /// <summary>Append pages from a single source stream to input stream and write to output.</summary>
    public bool Append(Stream inputStream, Stream portStream, int startPage, int endPage, Stream outputStream)
    {
        var result = Append(ReadStream(inputStream), ReadStream(portStream), startPage, endPage);
        outputStream.Write(result, 0, result.Length);
        return true;
    }

    /// <summary>Insert pages from source stream into input stream at given position.</summary>
    public bool Insert(Stream inputStream, int insertLocation, Stream portStream, int startPage, int endPage, Stream outputStream)
    {
        var result = Insert(ReadStream(inputStream), insertLocation, ReadStream(portStream), startPage, endPage);
        outputStream.Write(result, 0, result.Length);
        return true;
    }

    /// <summary>Insert specific pages from source stream into input stream.</summary>
    public bool Insert(Stream inputStream, int insertLocation, Stream portStream, int[] pageNumber, Stream outputStream)
    {
        var result = Insert(ReadStream(inputStream), insertLocation, ReadStream(portStream), pageNumber);
        outputStream.Write(result, 0, result.Length);
        return true;
    }

    /// <summary>
    /// Insert pages from a source PDF into a destination PDF at a given position and write to output file.
    /// </summary>
    public bool Insert(string inputFile, int insertLocation, string portFile, int startPage, int endPage, string outputFile)
    {
        var input = File.ReadAllBytes(inputFile);
        var port = File.ReadAllBytes(portFile);
        var result = Insert(input, insertLocation, port, startPage, endPage);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// Insert specific pages from a source PDF into a destination PDF at a given position and write to output file.
    /// </summary>
    public bool Insert(string inputFile, int insertLocation, string portFile, int[] pageNumber, string outputFile)
    {
        var input = File.ReadAllBytes(inputFile);
        var port = File.ReadAllBytes(portFile);
        var result = Insert(input, insertLocation, port, pageNumber);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// Append pages from multiple source PDF files to an input PDF and write to output file.
    /// </summary>
    public bool Append(string inputFile, string[] portFiles, int startPage, int endPage, string outputFile)
    {
        _corrupted.Clear();
        var input = File.ReadAllBytes(inputFile);
        var namedPorts = portFiles.Select(f => (File.ReadAllBytes(f), (string?)f)).ToList();
        var ports = FilterCorruptedInputs(namedPorts).ToArray();
        var result = Append(input, ports, startPage, endPage);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// Append pages from a single source PDF file to an input PDF and write to output file.
    /// </summary>
    public bool Append(string inputFile, string portFile, int startPage, int endPage, string outputFile)
    {
        return Append(inputFile, new[] { portFile }, startPage, endPage, outputFile);
    }
}
