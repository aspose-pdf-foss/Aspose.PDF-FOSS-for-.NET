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
    /// Extract pages from a PDF document.
    /// </summary>
    /// <param name="inputPdf">Source PDF bytes.</param>
    /// <param name="startPage">Start page (1-based, inclusive).</param>
    /// <param name="endPage">End page (1-based, inclusive).</param>
    public byte[] Extract(byte[] inputPdf, int startPage, int endPage)
    {
        using var doc = Document.Open(inputPdf);
        var totalPages = doc.PageCount;

        if (startPage < 1) startPage = 1;
        if (endPage > totalPages) endPage = totalPages;

        // Delete pages outside the range (from end first)
        for (var i = totalPages; i > endPage; i--)
            doc.Pages.Delete(i);
        for (var i = startPage - 1; i >= 1; i--)
            doc.Pages.Delete(i);

        // Drop the removed pages' now-orphaned objects (their images can be the bulk of the
        // file) instead of carrying the whole source into the extracted output.
        doc.CompactAfterPageRemoval();
        return ApplySizeOptimization(doc.ToArray());
    }

    /// <summary>
    /// Split a PDF into individual page files.
    /// </summary>
    public byte[][] Split(byte[] inputPdf)
    {
        using var doc = Document.Open(inputPdf);
        var results = new byte[doc.PageCount][];

        for (var i = 0; i < doc.PageCount; i++)
        {
            results[i] = Extract(inputPdf, i + 1, i + 1);
        }

        return results;
    }

    /// <summary>
    /// Delete pages from a PDF.
    /// </summary>
    /// <param name="inputPdf">Source PDF bytes.</param>
    /// <param name="pageNumbers">1-based page numbers to delete.</param>
    public byte[] Delete(byte[] inputPdf, params int[] pageNumbers)
    {
        using var doc = Document.Open(inputPdf);
        doc.Pages.Delete(pageNumbers);
        return doc.ToArray();
    }

    /// <summary>
    /// Extract specific pages (by page number array) from a PDF.
    /// </summary>
    /// <param name="inputPdf">Source PDF bytes.</param>
    /// <param name="pageNumbers">1-based page numbers to extract.</param>
    public byte[] Extract(byte[] inputPdf, int[] pageNumbers)
    {
        if (pageNumbers.Length == 0) return Concatenate(new byte[0][]);

        // Extract all requested pages from ONE document (delete the complement), not by
        // extracting each page separately and concatenating. A single pass keeps
        // cross-page links inside the kept set resolvable (a GoTo from one kept page to
        // another stays valid) and doesn't duplicate resources shared between kept pages,
        // which per-page concatenation would copy once per page.
        using var doc = Document.Open(inputPdf);
        var total = doc.PageCount;
        var keep = new HashSet<int>();
        foreach (var pn in pageNumbers)
            if (pn >= 1 && pn <= total) keep.Add(pn);
        if (keep.Count == 0) return Concatenate(new byte[0][]);

        for (var i = total; i >= 1; i--)
            if (!keep.Contains(i)) doc.Pages.Delete(i);

        doc.CompactAfterPageRemoval();
        return ApplySizeOptimization(doc.ToArray());
    }

    /// <summary>
    /// Extract the first N pages from a PDF.
    /// </summary>
    public byte[] SplitFromFirst(byte[] inputPdf, int pageCount)
    {
        return Extract(inputPdf, 1, pageCount);
    }

    /// <summary>
    /// Extract pages from startPage to the end of the document.
    /// </summary>
    public byte[] SplitToEnd(byte[] inputPdf, int startPage)
    {
        using var doc = Document.Open(inputPdf);
        return Extract(inputPdf, startPage, doc.PageCount);
    }

    /// <summary>
    /// Split a PDF into individual single-page documents (alias for Split).
    /// </summary>
    public byte[][] SplitToPages(byte[] inputPdf) => Split(inputPdf);

    /// <summary>
    /// Split a PDF file at the given path into MemoryStreams, one per page
    ///.
    /// </summary>
    public MemoryStream[] SplitToPages(string inputFile)
    {
        var parts = Split(File.ReadAllBytes(inputFile));
        var result = new MemoryStream[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            result[i] = new MemoryStream(parts[i]);
        return result;
    }

    /// <summary>
    /// Split a PDF into multiple parts based on page ranges.
    /// Each range is [start, end] (1-based, inclusive), clamped to valid bounds.
    /// </summary>
    public byte[][] SplitToBulks(byte[] inputPdf, int[][]? pageRanges)
    {
        if (pageRanges is null || pageRanges.Length == 0)
            throw new ArgumentException(E_EMPTY_PAGE_RANGE);

        using var doc = Document.Open(inputPdf);
        var total = doc.PageCount;

        var results = new byte[pageRanges.Length][];
        for (int i = 0; i < pageRanges.Length; i++)
        {
            var range = pageRanges[i];
            if (range is null)
                throw new ArgumentException(E_EMPTY_PAGE_RANGE);
            if (range.Length < 2)
                throw new ArgumentException(E_SMALL_PAGE_RANGE);
            if (range[0] > range[1])
                throw new ArgumentException(E_WRONG_PAGE_RANGE);

            var start = Math.Max(1, range[0]);
            var end = Math.Min(total, range[1]);
            results[i] = Extract(inputPdf, start, end);
        }

        return results;
    }

    /// <summary>
    /// Split from first N pages and write to output file.
    /// </summary>
    public bool SplitFromFirst(string inputFile, int location, string outputFile)
    {
        var input = File.ReadAllBytes(inputFile);
        var result = SplitFromFirst(input, location);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// Split from startPage to end and write to output file.
    /// </summary>
    public bool SplitToEnd(string inputFile, int location, string outputFile)
    {
        var input = File.ReadAllBytes(inputFile);
        var result = SplitToEnd(input, location);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// Extract specific pages from a PDF file and write to output file.
    /// </summary>
    public bool Extract(string inputFile, int[] pageNumber, string outputFile)
    {
        var input = File.ReadAllBytes(inputFile);
        var result = Extract(input, pageNumber);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// Extract a range of pages from a PDF file and write to output file.
    /// </summary>
    public bool Extract(string inputFile, int startPage, int endPage, string outputFile)
    {
        var input = File.ReadAllBytes(inputFile);
        var result = Extract(input, startPage, endPage);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// Extract a range of pages from a stream and write to an output stream
    ///.
    /// </summary>
    public bool Extract(Stream inputStream, int startPage, int endPage, Stream outputStream)
    {
        using var ms = new MemoryStream();
        if (inputStream.CanSeek) inputStream.Position = 0;
        inputStream.CopyTo(ms);
        var result = Extract(ms.ToArray(), startPage, endPage);
        outputStream.Write(result, 0, result.Length);
        return true;
    }

    /// <summary>Extract specific pages from a stream and write to output stream.</summary>
    public bool Extract(Stream inputStream, int[] pageNumber, Stream outputStream)
    {
        var result = Extract(ReadStream(inputStream), pageNumber);
        outputStream.Write(result, 0, result.Length);
        return true;
    }

    /// <summary>Delete specific pages from a stream and write to output stream.</summary>
    public bool Delete(Stream inputStream, int[] pageNumber, Stream outputStream)
    {
        var result = Delete(ReadStream(inputStream), pageNumber);
        outputStream.Write(result, 0, result.Length);
        return true;
    }

    /// <summary>Split a stream from the first page up to pageCount.</summary>
    public bool SplitFromFirst(Stream inputStream, int location, Stream outputStream)
    {
        var result = SplitFromFirst(ReadStream(inputStream), location);
        outputStream.Write(result, 0, result.Length);
        return true;
    }

    /// <summary>Split a stream from startPage to the end.</summary>
    public bool SplitToEnd(Stream inputStream, int location, Stream outputStream)
    {
        var result = SplitToEnd(ReadStream(inputStream), location);
        outputStream.Write(result, 0, result.Length);
        return true;
    }

    /// <summary>
    /// Delete specific pages from a PDF file and write to output file.
    /// </summary>
    public bool Delete(string inputFile, int[] pageNumber, string outputFile)
    {
        var input = File.ReadAllBytes(inputFile);
        var result = Delete(input, pageNumber);
        File.WriteAllBytes(outputFile, result);
        return true;
    }
}
