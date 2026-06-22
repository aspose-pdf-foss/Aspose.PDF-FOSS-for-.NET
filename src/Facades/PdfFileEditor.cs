using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for common PDF file editing operations: concatenate, split, extract, delete pages.
/// </summary>
public sealed partial class PdfFileEditor
{
    /// <summary>
    /// Obsolete property kept for API compatibility. Always throws NotSupportedException.
    /// </summary>
    public bool AllowConcatenateExceptions
    {
        get => throw new NotSupportedException("AllowConcatenateExceptions is not supported.");
        set => throw new NotSupportedException("AllowConcatenateExceptions is not supported.");
    }

    /// <summary>
    /// Mirrors <c>PdfFileEditor.CopyLogicalStructure</c>. Stored only;
    /// Concatenate does not currently copy structure trees.
    /// </summary>
    public bool CopyLogicalStructure { get; set; }

    /// <summary>
    /// Mirrors <c>PdfFileEditor.CopyOutlines</c>. Stored only;
    /// Concatenate does not currently copy outlines.
    /// </summary>
    public bool CopyOutlines { get; set; }

    /// <summary>How the facade reacts to corrupted input during Concatenate.</summary>
    public enum ConcatenateCorruptedFileAction
    {
        /// <summary>Stop on the first corrupted file (.NET default).</summary>
        StopWithError,
        /// <summary>Skip the corrupted file and continue concatenating.</summary>
        ConcatenateIgnoringCorruptedObjects,
        /// <summary>Alias for <see cref="ConcatenateIgnoringCorruptedObjects"/>.</summary>
        ConcatenateIgnoringCorrupted = ConcatenateIgnoringCorruptedObjects,
    }

    /// <summary>
    /// Mirrors <c>PdfFileEditor.CorruptedFileAction</c>. Stored only;
    /// Concatenate currently stops on any PDF parse error regardless of this value.
    /// </summary>
    public ConcatenateCorruptedFileAction CorruptedFileAction { get; set; }

    /// <summary>
    /// When true, the stream-based Concatenate / Append / Insert overloads
    /// dispose the input streams (and the output stream after writing) once
    /// the operation completes. Default is false — callers retain ownership
    /// of their streams. Mirrors the .NET API.
    /// </summary>
    public bool CloseConcatenatedStreams { get; set; }

    /// <summary>When true, Concatenate renames colliding form-field names
    /// in the inputs so the output has unique field names. Stored only;
    /// name-uniquification is not currently performed.</summary>
    public bool KeepFieldsUnique { get; set; }

    /// <summary>When true, identical outline entries from the inputs are
    /// merged in the output instead of duplicated. Stored only; Concatenate
    /// does not currently dedupe outlines.</summary>
    public bool MergeDuplicateOutlines { get; set; }

    /// <summary>When true, Concatenate appends pages to the destination
    /// using PDF incremental updates instead of rewriting the entire file.
    /// Stored only; the writer always full-rewrites.</summary>
    public bool IncrementalUpdates { get; set; }

    /// <summary>When true, Concatenate runs an optimization pass on the
    /// output before saving. Stored only; no automatic optimization is performed.</summary>
    public bool OptimizeSize { get; set; }

    /// <summary>Buffer-size hint (bytes) used by streaming Concatenate
    /// implementations. Stored only; the implementation always reads full inputs.</summary>
    public int ConcatenationPacketSize { get; set; }

    /// <summary>When true, Concatenate buffers intermediate pages on disk
    /// instead of in memory. Stored only; the implementation always works in-memory.</summary>
    public bool UseDiskBuffer { get; set; }


    /// <summary>
    /// Concatenate multiple PDF documents into one.
    /// </summary>
    public byte[] Concatenate(params byte[][] inputFiles)
    {
        if (inputFiles.Length == 0)
            throw new ArgumentException("At least one input file required", nameof(inputFiles));

        if (inputFiles.Length == 1)
            return inputFiles[0];

        var allPageObjNums = new List<int>();

        // Use a temp file to avoid MemoryStream 2GB limit for large concatenations (100+ copies)
        var tempPath = Path.GetTempFileName();
        try
        {
            using (var output = File.Create(tempPath))
            {
                var writer = new PdfWriter(output);
                writer.WriteHeader("1.4");

                // Reserve obj 1 (catalog) and obj 2 (pages) — written last once all pages known.
                // All resource/page/content objects are allocated from 3 upwards.
                writer.SetMinObjectNumber(3);

                // Parse all inputs once, reuse readers for outlines/embedded files/AcroForm.
                // inputPageCounts preserves pages-per-input so MergeOutlines can shift
                // destinations by the sum of preceding page counts.
                var inputReaders = new List<PdfReader>();
                var inputPageCounts = new List<int>();
                // Retain the first input's reader + object map so its catalog-level
                // /OpenAction can be preserved (Aspose.PDF for .NET behaviour) and remapped through
                // the same map — the open-action destination then still points at its
                // (already-written) page rather than a duplicate.
                PdfReader? firstReader = null;
                Dictionary<int, int>? firstObjRemap = null;
                foreach (var inputData in inputFiles)
                {
                    var reader = PdfReader.FromBytes(inputData);
                    inputReaders.Add(reader);
                    var catalog = reader.Catalog;
                    var pagesDict = reader.ResolveDict(catalog.Get("Pages"));
                    if (pagesDict is null) { inputPageCounts.Add(0); continue; }

                    var pages = new List<PdfDictionary>();
                    CollectPages(pagesDict, reader, pages);
                    inputPageCounts.Add(pages.Count);

                    // Per-input object remapping: sourceObjNum → outputObjNum.
                    // Objects referenced by multiple pages in the same input are written once
                    // and shared via indirect refs — preventing resource duplication bloat.
                    var objRemap = new Dictionary<int, int>();
                    if (firstReader is null) { firstReader = reader; firstObjRemap = objRemap; }

                    foreach (var pageDict in pages)
                    {
                        // Remap the page dict: each source indirect ref is assigned a new
                        // output obj num and the referenced object is written once.
                        // RemapObject uses writer.AllocateObjectNumber() so it stays in sync
                        // with deferred stream promotions inside PdfWriter.WriteDictionary.
                        var cloned = (PdfDictionary)RemapObject(pageDict, reader, objRemap, writer);
                        cloned.Set("Parent", new PdfIndirectRef(2, 0));

                        var pageObjNum = writer.AllocateObjectNumber();
                        writer.WriteIndirectObject(pageObjNum, cloned);
                        allPageObjNums.Add(pageObjNum);
                    }
                }

                // Write Pages object
                var kids = new PdfArray();
                foreach (var pObjNum in allPageObjNums)
                    kids.Add(new PdfIndirectRef(pObjNum, 0));

                var pagesObj = new PdfDictionary();
                pagesObj.Set("Type", new PdfName("Pages"));
                pagesObj.Set("Kids", kids);
                pagesObj.Set("Count", new PdfInteger(allPageObjNums.Count));
                writer.WriteIndirectObject(2, pagesObj);

                // Merge embedded files from all input documents
                var embeddedEntries = new List<(string name, PdfObject fileSpec)>();
                foreach (var reader in inputReaders)
                {
                    var cat = reader.Catalog;
                    var names = reader.ResolveDict(cat.Get("Names"));
                    if (names is null) continue;
                    var efTree = reader.ResolveDict(names.Get("EmbeddedFiles"));
                    if (efTree is null) continue;

                    // Per-input remap for embedded file objects
                    var efRemap = new Dictionary<int, int>();
                    CollectNameTreeEntries(efTree, reader, embeddedEntries, efRemap, writer);
                }

                // Write catalog
                var catalogDict = new PdfDictionary();
                catalogDict.Set("Type", new PdfName("Catalog"));
                catalogDict.Set("Pages", new PdfIndirectRef(2, 0));

                // Add /Names/EmbeddedFiles if any were found
                if (embeddedEntries.Count > 0)
                {
                    var namesArr = new PdfArray();
                    foreach (var (name, fileSpec) in embeddedEntries)
                    {
                        namesArr.Add(new PdfString(System.Text.Encoding.Latin1.GetBytes(name)));
                        namesArr.Add(fileSpec);
                    }
                    var efTreeDict = new PdfDictionary();
                    efTreeDict.Set("Names", namesArr);
                    var efObjNum = writer.AllocateObjectNumber();
                    writer.WriteIndirectObject(efObjNum, efTreeDict);

                    var namesDict = new PdfDictionary();
                    namesDict.Set("EmbeddedFiles", new PdfIndirectRef(efObjNum, 0));
                    var namesObjNum = writer.AllocateObjectNumber();
                    writer.WriteIndirectObject(namesObjNum, namesDict);

                    catalogDict.Set("Names", new PdfIndirectRef(namesObjNum, 0));
                }

                // Merge AcroForm fields from all input documents
                var allFieldRefs = new PdfArray();
                foreach (var reader in inputReaders)
                {
                    var cat = reader.Catalog;
                    var acroForm = reader.ResolveDict(cat.Get("AcroForm"));
                    if (acroForm is null) continue;
                    var fieldsArr = reader.Resolve(acroForm.Get("Fields")) as PdfArray;
                    if (fieldsArr is null) continue;

                    var acroRemap = new Dictionary<int, int>();
                    foreach (var fieldRef in fieldsArr)
                    {
                        var cloned = RemapObject(fieldRef, reader, acroRemap, writer);
                        allFieldRefs.Add(cloned);
                    }
                }
                if (allFieldRefs.Count > 0)
                {
                    var acroFormDict = new PdfDictionary();
                    acroFormDict.Set("Fields", allFieldRefs);
                    var acroObjNum = writer.AllocateObjectNumber();
                    writer.WriteIndirectObject(acroObjNum, acroFormDict);
                    catalogDict.Set("AcroForm", new PdfIndirectRef(acroObjNum, 0));
                }

                // Merge outlines (bookmarks) from all input documents
                MergeOutlines(inputReaders, inputPageCounts, catalogDict, writer);
                if (CopyLogicalStructure)
                    MergeStructTrees(inputReaders, catalogDict, writer);

                // Preserve the first document's catalog /OpenAction (keep the
                // leading document's open action; the result opens at its start). Remap it
                // through the first input's object map so its destination still resolves to
                // the correct already-written page.
                if (firstReader is not null && firstObjRemap is not null)
                {
                    var openAction = firstReader.Catalog.Get("OpenAction");
                    if (openAction is not null)
                        catalogDict.Set("OpenAction", RemapObject(openAction, firstReader, firstObjRemap, writer));
                }

                writer.WriteIndirectObject(1, catalogDict);

                var trailer = new PdfDictionary();
                trailer.Set("Root", new PdfIndirectRef(1, 0));
                writer.WriteXRefAndTrailer(trailer);
            }

            // Read the result — use FileStream for chunk reading to avoid File.ReadAllBytes
            // 2 GB limit on older runtimes.
            var bytes = ReadAllBytesFromFile(tempPath);
            AppendConversionLog($"Concatenated {inputFiles.Length} inputs into {allPageObjNums.Count} pages.");
            return ApplyPostConcatenateOptions(bytes);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>Post-Concatenate pass that honours the RemoveSignatures /
    /// OwnerPassword / ConvertTo properties on the facade. Real implementations
    /// — each flag triggers a follow-on Document open/operate/save.</summary>
    private byte[] ApplyPostConcatenateOptions(byte[] bytes)
    {
        if (RemoveSignatures)
        {
            using var doc = Document.Open(bytes);
            var form = doc.Form;
            if (form is not null)
            {
                foreach (var field in form.Fields)
                {
                    if (field.Type != Forms.FieldType.Signature) continue;
                    field.Dict.Remove("V");
                }
                bytes = doc.ToArray();
                AppendConversionLog("RemoveSignatures: stripped /V from every signature field.");
            }
        }
        if (!string.IsNullOrEmpty(OwnerPassword))
        {
            using var doc = Document.Open(bytes);
            doc.Encrypt(string.Empty, OwnerPassword,
                permissions: null,
                algorithm: Aspose.Pdf.CryptoAlgorithm.AESx128);
            bytes = doc.ToArray();
            AppendConversionLog($"OwnerPassword set; output encrypted with AES-128.");
        }
        if (_convertToFormat is { } fmt)
        {
            using var doc = Document.Open(bytes);
            doc.Convert(Stream.Null, fmt, ConvertErrorAction.Delete);
            bytes = doc.ToArray();
            AppendConversionLog($"ConvertTo: output converted to {fmt}.");
        }
        return bytes;
    }

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

        return doc.ToArray();
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

        var parts = new List<byte[]>();
        foreach (var pn in pageNumbers)
            parts.Add(Extract(inputPdf, pn, pn));

        return parts.Count == 1 ? parts[0] : Concatenate(parts.ToArray());
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

    /// <summary>Error message for null/empty page ranges.</summary>
    public const string E_EMPTY_PAGE_RANGE = "Page ranges must not be null or empty";
    /// <summary>Error message for a page range with fewer than 2 elements.</summary>
    public const string E_SMALL_PAGE_RANGE = "Each page range must have at least 2 elements (start and end)";
    /// <summary>Error message for a page range where start > end.</summary>
    public const string E_WRONG_PAGE_RANGE = "Page range start must not be greater than end";

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
    /// Create a booklet from a PDF. Pages are reordered so that when printed double-sided
    /// and folded, they form a booklet. The page count is padded to a multiple of 4.
    /// </summary>
    public byte[] MakeBooklet(byte[] inputPdf)
    {
        using var doc = Document.Open(inputPdf);
        var n = doc.PageCount;

        // Pad to multiple of 4
        var padded = n;
        while (padded % 4 != 0) padded++;

        // Generate booklet page order: for each sheet (front and back)
        // Sheet i (0-based): front = [padded-i, i+1], back = [i+2, padded-i-1]
        // where i steps by 2
        var bookletOrder = new List<int>();
        for (int i = 0; i < padded; i += 4)
        {
            // Front side: left = padded - i, right = i + 1
            bookletOrder.Add(padded - i);
            bookletOrder.Add(i + 1);
            // Back side: left = i + 2, right = padded - i - 1
            bookletOrder.Add(i + 2);
            bookletOrder.Add(padded - i - 1);
        }

        // Extract pages in booklet order; blank pages for padding
        var blankPage = CreateBlankPagePdf();
        var parts = new List<byte[]>();
        foreach (var pageNum in bookletOrder)
        {
            if (pageNum >= 1 && pageNum <= n)
                parts.Add(Extract(inputPdf, pageNum, pageNum));
            else
                parts.Add(blankPage);
        }

        return Concatenate(parts.ToArray());
    }

    /// <summary>
    /// Create a booklet from a PDF using a specified page size.
    /// Pages are reordered so that when printed double-sided and folded, they form a booklet.
    /// </summary>
    public byte[] MakeBooklet(byte[] inputPdf, PageSize pageSize)
    {
        // First create the booklet ordering
        var booklet = MakeBooklet(inputPdf);
        // Then resize all pages to the specified size
        return ResizePages(booklet, pageSize.Width, pageSize.Height);
    }

    /// <summary>
    /// Create a booklet using only specified left and right pages.
    /// </summary>
    public byte[] MakeBooklet(byte[] inputPdf, int[] leftPages, int[] rightPages)
    {
        // Pair left[i] and right[i] onto each sheet
        var maxPairs = Math.Max(leftPages.Length, rightPages.Length);
        var parts = new List<byte[]>();
        var blankPage = CreateBlankPagePdf();

        using var doc = Document.Open(inputPdf);
        var n = doc.PageCount;

        for (int i = 0; i < maxPairs; i++)
        {
            var left = i < leftPages.Length ? leftPages[i] : -1;
            var right = i < rightPages.Length ? rightPages[i] : -1;

            if (left >= 1 && left <= n)
                parts.Add(Extract(inputPdf, left, left));
            else
                parts.Add(blankPage);

            if (right >= 1 && right <= n)
                parts.Add(Extract(inputPdf, right, right));
            else
                parts.Add(blankPage);
        }

        return Concatenate(parts.ToArray());
    }

    /// <summary>
    /// Create a booklet using specified left/right pages and a custom page size.
    /// </summary>
    public byte[] MakeBooklet(byte[] inputPdf, PageSize pageSize, int[] leftPages, int[] rightPages)
    {
        var booklet = MakeBooklet(inputPdf, leftPages, rightPages);
        return ResizePages(booklet, pageSize.Width, pageSize.Height);
    }

    private static byte[] ResizePages(byte[] pdf, double width, double height)
    {
        using var doc = Document.Open(pdf);
        for (int i = 1; i <= doc.PageCount; i++)
            doc.Pages[i].SetPageSize(width, height);
        return doc.ToArray();
    }

    private static byte[] CreateBlankPagePdf()
    {
        var doc = Document.Create();
        doc.Pages.Add();
        return doc.ToArray();
    }

    /// <summary>
    /// Append pages from a source PDF to an input PDF.
    /// </summary>
    /// <param name="inputPdf">Destination PDF bytes.</param>
    /// <param name="portPdf">Source PDF bytes to append pages from.</param>
    /// <param name="startPage">Start page in source (1-based, inclusive).</param>
    /// <param name="endPage">End page in source (1-based, inclusive).</param>
    public byte[] Append(byte[] inputPdf, byte[] portPdf, int startPage, int endPage)
    {
        var extracted = Extract(portPdf, startPage, endPage);
        return Concatenate(inputPdf, extracted);
    }

    /// <summary>
    /// Append pages from multiple source PDFs to an input PDF.
    /// </summary>
    public byte[] Append(byte[] inputPdf, byte[][] portPdfs, int startPage, int endPage)
    {
        var parts = new List<byte[]> { inputPdf };
        foreach (var src in portPdfs)
            parts.Add(Extract(src, startPage, endPage));
        return Concatenate(parts.ToArray());
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

        // Extract the requested pages by extracting ranges
        // Build a document from just those pages
        var parts = new List<byte[]>();
        foreach (var pn in pageNumbers)
            parts.Add(Extract(portPdf, pn, pn));

        var extracted = parts.Count == 1 ? parts[0] : Concatenate(parts.ToArray());

        using var destDoc = Document.Open(inputPdf);
        var destCount = destDoc.PageCount;
        var pos = Math.Max(0, Math.Min(insertLocation, destCount));

        if (pos == 0)
            return Concatenate(extracted, inputPdf);
        if (pos >= destCount)
            return Concatenate(inputPdf, extracted);

        var before = Extract(inputPdf, 1, pos);
        var after = Extract(inputPdf, pos + 1, destCount);
        return Concatenate(before, extracted, after);
    }

    // ── ResizeContents ────────────────────────────────────────────────────────

    /// <summary>
    /// Resize the contents of all pages in a document by applying a scale/translate
    /// transform derived from the margin parameters.
    /// </summary>
    public void ResizeContents(Document source, ContentsResizeParameters parameters)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));

        for (int i = 1; i <= source.PageCount; i++)
            ResizePage(source.Pages.At(i), parameters);
    }

    /// <summary>
    /// Resize the contents of specific pages in a document by applying a scale/translate
    /// transform derived from the margin parameters.
    /// </summary>
    public void ResizeContents(Document source, int[] pages, ContentsResizeParameters parameters)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (pages is null) throw new ArgumentNullException(nameof(pages));
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));

        foreach (var pageNum in pages)
        {
            if (pageNum < 1 || pageNum > source.PageCount) continue;
            ResizePage(source.Pages.At(pageNum), parameters);
        }
    }

    private static void ResizePage(Page page, ContentsResizeParameters parameters)
    {
        var box = page.MediaBox;
        double w = box.Width;
        double h = box.Height;

        // Left margin, content width and right margin partition the page width
        // (their sum is the page width); top margin, content height and bottom
        // margin partition the page height. Any value left unspecified ("auto")
        // shares the space remaining after the fixed values equally.
        Partition(parameters.LeftMargin, parameters.ContentsWidth, parameters.RightMargin,
            w, out double left, out double contentW, out double right);
        Partition(parameters.TopMargin, parameters.ContentsHeight, parameters.BottomMargin,
            h, out double top, out double contentH, out double bottom);

        double sx = contentW / w;
        double sy = contentH / h;
        double tx = left;
        double ty = bottom;

        page.ApplyContentResizeAsForm(sx, sy, tx, ty);

        // Annotations live in /Annots (not the content stream), so the content-form
        // transform above doesn't reach them. Apply the same affine to their
        // geometry so ink strokes / rects / quadpoints track the resized content.
        TransformAnnotationGeometry(page, sx, sy, tx, ty);

        // The margins plus content span the new page box. When that differs from
        // the current media box (e.g. fixed-zero margins shrinking the page to the
        // content size), resize the page boxes to match.
        double newW = left + contentW + right;
        double newH = top + contentH + bottom;
        if (Math.Abs(newW - w) > 1e-6 || Math.Abs(newH - h) > 1e-6)
            page.Rect = new Rectangle(box.LLX, box.LLY, box.LLX + newW, box.LLY + newH);
    }

    /// <summary>Split <paramref name="total"/> across the three slots (leading
    /// margin, content, trailing margin). Fixed (non-auto) slots resolve against
    /// <paramref name="total"/>; the remaining space is divided equally among the
    /// auto slots.</summary>
    private static void Partition(
        ContentsResizeValue? lead, ContentsResizeValue? content, ContentsResizeValue? trail,
        double total, out double leadOut, out double contentOut, out double trailOut)
    {
        bool leadAuto    = lead    is null || lead.IsAutoInternal;
        bool contentAuto = content is null || content.IsAutoInternal;
        bool trailAuto   = trail   is null || trail.IsAutoInternal;

        double fixedSum = 0;
        if (!leadAuto)    fixedSum += lead!.ResolveAgainst(total);
        if (!contentAuto) fixedSum += content!.ResolveAgainst(total);
        if (!trailAuto)   fixedSum += trail!.ResolveAgainst(total);

        int autoCount = (leadAuto ? 1 : 0) + (contentAuto ? 1 : 0) + (trailAuto ? 1 : 0);
        double autoShare = autoCount > 0 ? (total - fixedSum) / autoCount : 0;

        leadOut    = leadAuto    ? autoShare : lead!.ResolveAgainst(total);
        contentOut = contentAuto ? autoShare : content!.ResolveAgainst(total);
        trailOut   = trailAuto   ? autoShare : trail!.ResolveAgainst(total);
    }

    /// <summary>
    /// Represents a content-resize value. A value can be auto (let the engine derive it),
    /// a percentage of the corresponding page dimension, or an absolute number of points.
    /// </summary>
    public sealed class ContentsResizeValue
    {
        private double _value;
        private bool _isPercent;
        private bool _isAuto;

        private ContentsResizeValue(double value, bool isPercent, bool isAuto)
        {
            _value = value;
            _isPercent = isPercent;
            _isAuto = isAuto;
        }

        /// <summary>The numeric value (percentage or points, depending on <see cref="IsPercent"/>).</summary>
        public double Value => _value;

        /// <summary>True when <see cref="Value"/> is a percentage of the page dimension.</summary>
        public bool IsPercent => _isPercent;

        /// <summary>Set this value as a percentage of the page dimension.</summary>
        public double PercentValue
        {
            set { _value = value; _isPercent = true; _isAuto = false; }
        }

        /// <summary>Set this value as an absolute number of points.</summary>
        public double UnitValue
        {
            set { _value = value; _isPercent = false; _isAuto = false; }
        }

        /// <summary>Create an auto-sized value (engine derives it from the surrounding constraints).</summary>
        public static ContentsResizeValue Auto() => new(0, false, true);

        /// <summary>Create a value expressed as a percentage of the page dimension.</summary>
        public static ContentsResizeValue Percents(double value) => new(value, true, false);

        /// <summary>Create a value expressed as absolute points.</summary>
        public static ContentsResizeValue Units(double value) => new(value, false, false);

        internal bool IsAutoInternal => _isAuto;

        internal double ResolveAgainst(double pageDim)
            => _isPercent ? pageDim * _value / 100.0 : _value;
    }

    /// <summary>
    /// Parameters for <see cref="ResizeContents"/> describing margins, content size,
    /// and whether the page media box should change to match.
    /// </summary>
    public sealed class ContentsResizeParameters
    {
        /// <summary>Left margin, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? LeftMargin { get; set; }
        /// <summary>Content width, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? ContentsWidth { get; set; }
        /// <summary>Right margin, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? RightMargin { get; set; }
        /// <summary>Top margin, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? TopMargin { get; set; }
        /// <summary>Content height, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? ContentsHeight { get; set; }
        /// <summary>Bottom margin, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? BottomMargin { get; set; }

        /// <summary>Whether the page media box should be resized along with the content.</summary>
        public bool ChangeMediaBox { get; set; }

        /// <summary>Initialise an empty parameter set (all values auto).</summary>
        public ContentsResizeParameters() { }

        /// <summary>Initialise with explicit values for every margin / content dimension.</summary>
        public ContentsResizeParameters(
            ContentsResizeValue leftMargin,
            ContentsResizeValue contentsWidth,
            ContentsResizeValue rightMargin,
            ContentsResizeValue topMargin,
            ContentsResizeValue contentsHeight,
            ContentsResizeValue bottomMargin)
        {
            LeftMargin     = leftMargin;
            ContentsWidth  = contentsWidth;
            RightMargin    = rightMargin;
            TopMargin      = topMargin;
            ContentsHeight = contentsHeight;
            BottomMargin   = bottomMargin;
        }

        /// <summary>Resize parameters that fit page content into <paramref name="width"/>×<paramref name="height"/> points with zero margins.</summary>
        public static ContentsResizeParameters PageResize(double width, double height) =>
            new(
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Units(width),
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Units(height),
                ContentsResizeValue.Units(0));

        /// <summary>Resize parameters that scale page content by <paramref name="widthPct"/>%×<paramref name="heightPct"/>% with zero margins.</summary>
        public static ContentsResizeParameters PageResizePct(double widthPct, double heightPct) =>
            new(
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Percents(widthPct),
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Percents(heightPct),
                ContentsResizeValue.Units(0));

        /// <summary>Resize parameters with explicit content size in points; margins auto.</summary>
        public static ContentsResizeParameters ContentSize(double width, double height) =>
            new()
            {
                ContentsWidth  = ContentsResizeValue.Units(width),
                ContentsHeight = ContentsResizeValue.Units(height),
                LeftMargin     = ContentsResizeValue.Auto(),
                RightMargin    = ContentsResizeValue.Auto(),
                TopMargin      = ContentsResizeValue.Auto(),
                BottomMargin   = ContentsResizeValue.Auto(),
            };

        /// <summary>Resize parameters with explicit content size in percent of page; margins auto.</summary>
        public static ContentsResizeParameters ContentSizePercent(double width, double height) =>
            new()
            {
                ContentsWidth  = ContentsResizeValue.Percents(width),
                ContentsHeight = ContentsResizeValue.Percents(height),
                LeftMargin     = ContentsResizeValue.Auto(),
                RightMargin    = ContentsResizeValue.Auto(),
                TopMargin      = ContentsResizeValue.Auto(),
                BottomMargin   = ContentsResizeValue.Auto(),
            };

        /// <summary>Resize parameters with explicit margins in points; content size auto.</summary>
        public static ContentsResizeParameters Margins(double left, double right, double top, double bottom) =>
            new()
            {
                LeftMargin     = ContentsResizeValue.Units(left),
                RightMargin    = ContentsResizeValue.Units(right),
                TopMargin      = ContentsResizeValue.Units(top),
                BottomMargin   = ContentsResizeValue.Units(bottom),
                ContentsWidth  = ContentsResizeValue.Auto(),
                ContentsHeight = ContentsResizeValue.Auto(),
            };

        /// <summary>Resize parameters with explicit margins as percent of page; content size auto.</summary>
        public static ContentsResizeParameters MarginsPercent(double left, double right, double top, double bottom) =>
            new()
            {
                LeftMargin     = ContentsResizeValue.Percents(left),
                RightMargin    = ContentsResizeValue.Percents(right),
                TopMargin      = ContentsResizeValue.Percents(top),
                BottomMargin   = ContentsResizeValue.Percents(bottom),
                ContentsWidth  = ContentsResizeValue.Auto(),
                ContentsHeight = ContentsResizeValue.Auto(),
            };
    }

    // ── File-path overloads ─────────────────────────────────────────────────

    /// <summary>
    /// Concatenate multiple PDF files into one output file.
    /// </summary>
    public bool Concatenate(string[] inputFiles, string outputFile)
    {
        _corrupted.Clear();
        var named = inputFiles.Select(f => (File.ReadAllBytes(f), (string?)f)).ToList();
        var inputs = FilterCorruptedInputs(named).ToArray();
        var result = Concatenate(inputs);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>Parse-probe each input and honour <see cref="CorruptedFileAction"/>.
    /// Returns the inputs that parsed cleanly; the rest are recorded in
    /// <see cref="CorruptedItems"/>. When the action is
    /// <see cref="ConcatenateCorruptedFileAction.StopWithError"/>, the first
    /// unparseable input raises an <see cref="ArgumentException"/>. The recorded
    /// <see cref="CorruptedItem.Index"/> is the position within
    /// <paramref name="inputs"/>.</summary>
    private List<byte[]> FilterCorruptedInputs(IReadOnlyList<(byte[] data, string? name)> inputs)
    {
        var valid = new List<byte[]>();
        for (int i = 0; i < inputs.Count; i++)
        {
            var (data, name) = inputs[i];
            try
            {
                var reader = PdfReader.FromBytes(data);
                // Force trailer/catalog/page-tree resolution so a structurally
                // broken file is detected here rather than mid-merge.
                var pages = reader.ResolveDict(reader.Catalog.Get("Pages"));
                if (pages is null) throw new PdfException("No page tree.");
                valid.Add(data);
            }
            catch (Exception ex)
            {
                if (CorruptedFileAction == ConcatenateCorruptedFileAction.StopWithError)
                    throw new ArgumentException($"Input at index {i} could not be parsed.", ex);
                _corrupted.Add(new CorruptedItem(name, i, ex));
            }
        }
        return valid;
    }

    /// <summary>
    /// Concatenate two PDF files into one output file.
    /// </summary>
    public bool Concatenate(string firstInputFile, string secInputFile, string outputFile)
    {
        return Concatenate(new[] { firstInputFile, secInputFile }, outputFile);
    }

    /// <summary>
    /// Concatenate two PDF files with a blank-page separator inserted between them.
    /// </summary>
    public bool Concatenate(string firstInputFile, string secInputFile, string blankPageFile, string outputFile)
    {
        return Concatenate(new[] { firstInputFile, blankPageFile, secInputFile }, outputFile);
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

    // ── Stream-based overloads ────────────────────────────────────────

    /// <summary>Concatenate multiple PDF streams into one output stream.</summary>
    public bool Concatenate(Stream[] inputStream, Stream outputStream)
    {
        var inputs = inputStream.Select(ReadStream).ToArray();
        var result = Concatenate(inputs);
        outputStream.Write(result, 0, result.Length);
        if (CloseConcatenatedStreams)
        {
            foreach (var s in inputStream) s.Dispose();
            outputStream.Dispose();
        }
        return true;
    }

    /// <summary>Concatenate two PDF streams into one output stream.</summary>
    public bool Concatenate(Stream firstInputStream, Stream secInputStream, Stream outputStream)
    {
        var result = Concatenate(ReadStream(firstInputStream), ReadStream(secInputStream));
        outputStream.Write(result, 0, result.Length);
        if (CloseConcatenatedStreams)
        {
            firstInputStream.Dispose();
            secInputStream.Dispose();
            outputStream.Dispose();
        }
        return true;
    }

    /// <summary>Concatenate two PDF streams with a blank-page separator inserted between them.</summary>
    public bool Concatenate(Stream firstInputStream, Stream secInputStream, Stream blankPageStream, Stream outputStream)
    {
        var result = Concatenate(new[]
        {
            ReadStream(firstInputStream),
            ReadStream(blankPageStream),
            ReadStream(secInputStream),
        });
        outputStream.Write(result, 0, result.Length);
        if (CloseConcatenatedStreams)
        {
            firstInputStream.Dispose();
            secInputStream.Dispose();
            blankPageStream.Dispose();
            outputStream.Dispose();
        }
        return true;
    }

    /// <summary>Concatenate the pages of <paramref name="src"/> into the
    /// existing <paramref name="dest"/> document. Each source is left
    /// untouched; the target receives all pages in source-order. Honours
    /// the same flag set as the file/stream Concatenate overloads.</summary>
    public bool Concatenate(Document[] src, Document dest)
    {
        if (dest is null) throw new ArgumentNullException(nameof(dest));
        if (src is null) return false;
        foreach (var s in src)
        {
            if (s is null) continue;
            dest.Pages.Add(s.Pages);
            if (CopyLogicalStructure) dest.MergeLogicalStructure(s);
        }
        return true;
    }

    /// <summary>
    /// Describes a single horizontal cut on a source page: <see cref="PageNumber"/>
    /// (1-based) identifies the page in the source document, <see cref="Position"/>
    /// the PDF y-coordinate where the page is split. Used as input to
    /// <see cref="AddPageBreak(Document, Document, PageBreak[])"/>. Multiple
    /// <c>PageBreak</c>s targeting the same source page produce multiple
    /// horizontal bands; the source page becomes that many destination pages
    /// in reading order (top of the original first).
    /// </summary>
    public class PageBreak
    {
        /// <summary>1-based source page number to split.</summary>
        public int PageNumber { get; set; }

        /// <summary>Y coordinate (in PDF user space) at which to split the page.</summary>
        public double Position { get; set; }

        public PageBreak() { }

        public PageBreak(int pageNumber, double position)
        {
            PageNumber = pageNumber;
            Position = position;
        }
    }

    /// <summary>
    /// Copy every source page into <paramref name="destination"/>, splitting any
    /// source page that is referenced by one or more <see cref="PageBreak"/>
    /// entries into separate destination pages whose MediaBoxes describe the
    /// horizontal band each page occupies in the original. Pages without a
    /// break are deep-cloned unchanged.
    /// </summary>
    /// <remarks>
    /// PDF readers (Adobe / Aspose / our renderer) treat each page's MediaBox
    /// as the physical paper size and clip drawing operators outside it. So a
    /// source page with MediaBox [0,0,612,792] and a PageBreak at y=450 becomes:
    ///   - destination page with MediaBox [0,450,612,792] (top half)
    ///   - destination page with MediaBox [0,0,612,450] (bottom half)
    /// Both share the original content stream; the band that's visible at
    /// render time is the one whose MediaBox contains the drawing's y.
    ///
    /// Reading order is top-down: the band with the highest y range comes first.
    /// Multiple breaks on the same page produce that many extra destination pages
    /// (n breaks ⇒ n+1 bands).
    /// </remarks>
    public void AddPageBreak(Document src, Document dest, PageBreak[] pageBreaks)
    {
        if (src is null || dest is null) return;

        // Group break y-positions by 1-based source page number.
        var breaksByPage = new Dictionary<int, List<double>>();
        if (pageBreaks is not null)
        {
            foreach (var b in pageBreaks)
            {
                if (b is null) continue;
                if (!breaksByPage.TryGetValue(b.PageNumber, out var ys))
                    breaksByPage[b.PageNumber] = ys = new List<double>();
                ys.Add(b.Position);
            }
        }

        for (var i = 1; i <= src.PageCount; i++)
        {
            var srcPage = src.Pages[i];
            if (!breaksByPage.TryGetValue(i, out var ys))
            {
                dest.Pages.Add(srcPage);
                continue;
            }

            // Build top-to-bottom bands from the source page's MediaBox + break ys.
            // PDF y increases upward, so reading order = descending y. n breaks
            // ⇒ n+1 bands. Bands include the original MediaBox's full x extent
            // and only restrict y to the band.
            var media = srcPage.MediaBox;
            ys.Sort();
            var edges = new List<double> { media.LLY };
            foreach (var y in ys) edges.Add(y);
            edges.Add(media.URY);

            for (var k = edges.Count - 1; k > 0; k--)
            {
                var bandTop = edges[k];
                var bandBottom = edges[k - 1];
                if (bandTop <= bandBottom) continue; // skip zero-height bands
                var added = dest.Pages.Add(srcPage);
                // Each band keeps the FULL original page size; only this band's content
                // is shown by clipping the page content to the band's y-range. The content
                // is split across full-size pages (the band sits at its original
                // position with the rest blank) rather than shrinking the page to the band.
                added.SetMediaBox(new Rectangle(media.LLX, media.LLY, media.URX, media.URY));
                added.Dict.Remove("CropBox");
                added.Dict.Remove("BleedBox");
                added.Dict.Remove("TrimBox");
                added.Dict.Remove("ArtBox");
                // Translate the band up so its top edge aligns with the top of the page
                // (each band is drawn at the top of a full page, not at its
                // original y), then clip to the band's y-range.
                var dy = media.URY - bandTop;
                var clip = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "q 1 0 0 1 0 {0:0.####} cm {1:0.####} {2:0.####} {3:0.####} {4:0.####} re W n\n",
                    dy, media.LLX, bandBottom, media.URX - media.LLX, bandTop - bandBottom);
                added.PrependContentStream(System.Text.Encoding.ASCII.GetBytes(clip));
                added.AddContentStream(System.Text.Encoding.ASCII.GetBytes("\nQ"));

                // Annotations (form fields, links) draw from /Annots, not the content
                // stream, so the clip doesn't touch them. Keep only those whose rectangle
                // lies in this band and shift them up with the content; drop the rest.
                ClipAnnotationsToBand(added, bandBottom, bandTop, dy);
            }
        }
    }

    private static void ClipAnnotationsToBand(Page page, double bandBottom, double bandTop, double dy)
    {
        var reader = page.Reader;
        if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return;
        var kept = new PdfArray();
        foreach (var item in annots)
        {
            var annot = reader.ResolveDict(item);
            if (annot is null) { kept.Add(item); continue; }
            if (reader.Resolve(annot.Get("Rect")) is not PdfArray rect || rect.Count < 4)
            {
                kept.Add(item);
                continue;
            }
            double y1 = NumberFrom(rect[1]), y3 = NumberFrom(rect[3]);
            // Assign by the annotation's bottom edge: one that straddles the break goes
            // entirely to the lower band (a field cut by the break
            // moves wholesale to the next page rather than being split).
            var bottomY = Math.Min(y1, y3);
            if (bottomY < bandBottom || bottomY >= bandTop) continue; // outside band — drop
            var newRect = new PdfArray();
            newRect.Add(new PdfReal(NumberFrom(rect[0])));
            newRect.Add(new PdfReal(y1 + dy));
            newRect.Add(new PdfReal(NumberFrom(rect[2])));
            newRect.Add(new PdfReal(y3 + dy));
            annot.Set("Rect", newRect);
            kept.Add(item);
        }
        page.Dict.Set("Annots", kept);
    }

    /// <summary>Apply the content-resize affine (x' = x*sx+tx, y' = y*sy+ty) to each
    /// annotation's /InkList stroke points. Only /InkList is handled here — the
    /// resize path already transforms /Rect and /QuadPoints, so they must NOT be
    /// touched again (double-transform). Mutates the annotation dictionaries in
    /// place.</summary>
    private static void TransformAnnotationGeometry(Page page, double sx, double sy, double tx, double ty)
    {
        var reader = page.Reader;
        if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return;
        foreach (var item in annots)
        {
            var annot = reader.ResolveDict(item);
            if (annot is null) continue;

            // /InkList: an array of strokes, each a flat [x1 y1 x2 y2 …] coordinate list.
            if (reader.Resolve(annot.Get("InkList")) is PdfArray inkList)
            {
                var newInk = new PdfArray();
                foreach (var strokeObj in inkList)
                {
                    if (reader.Resolve(strokeObj) is PdfArray stroke)
                    {
                        var ns = new PdfArray();
                        for (var i = 0; i + 1 < stroke.Count; i += 2)
                        {
                            ns.Add(new PdfReal(NumberFrom(stroke[i]) * sx + tx));
                            ns.Add(new PdfReal(NumberFrom(stroke[i + 1]) * sy + ty));
                        }
                        newInk.Add(ns);
                    }
                    else newInk.Add(strokeObj);
                }
                annot.Set("InkList", newInk);
            }
        }
    }

    private static double NumberFrom(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    /// <summary>Stream overload of <see cref="AddPageBreak(Document, Document, PageBreak[])"/>.
    /// Reads <paramref name="src"/> into a <see cref="Document"/>, runs the page-break
    /// logic, and writes the result to <paramref name="dest"/>.</summary>
    public void AddPageBreak(Stream src, Stream dest, PageBreak[] pageBreaks)
    {
        if (src is null || dest is null) return;
        using var srcDoc = new Document(src);
        var dstDoc = new Document();
        AddPageBreak(srcDoc, dstDoc, pageBreaks);
        dstDoc.Save(dest);
    }

    /// <summary>File-path overload of <see cref="AddPageBreak(Document, Document, PageBreak[])"/>.</summary>
    public void AddPageBreak(string src, string dest, PageBreak[] pageBreaks)
    {
        if (src is null || dest is null) return;
        using var srcDoc = new Document(src);
        var dstDoc = new Document();
        AddPageBreak(srcDoc, dstDoc, pageBreaks);
        dstDoc.Save(dest);
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

    /// <summary>Read a stream fully into a byte array.</summary>
    private static byte[] ReadStream(Stream s)
    {
        if (s is MemoryStream ms && ms.TryGetBuffer(out var buf))
            return buf.Count == ms.Length ? buf.Array! : ms.ToArray();
        if (s.CanSeek) s.Position = 0;
        using var copy = new MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
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

    /// <summary>
    /// MakeBooklet from file path to file path.
    /// </summary>
    public bool MakeBooklet(string inputFile, string outputFile)
    {
        var result = MakeBooklet(File.ReadAllBytes(inputFile));
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// MakeBooklet from file path to file path with a specific page size.
    /// </summary>
    public bool MakeBooklet(string inputFile, string outputFile, PageSize pageSize)
    {
        var result = MakeBooklet(File.ReadAllBytes(inputFile), pageSize);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// MakeBooklet from file path to file path with left/right page arrays.
    /// </summary>
    public bool MakeBooklet(string inputFile, string outputFile, int[] leftPages, int[] rightPages)
    {
        var result = MakeBooklet(File.ReadAllBytes(inputFile), leftPages, rightPages);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>
    /// MakeBooklet from file path to file path with page size and left/right page arrays.
    /// </summary>
    public bool MakeBooklet(string inputFile, string outputFile, PageSize pageSize, int[] leftPages, int[] rightPages)
    {
        var result = MakeBooklet(File.ReadAllBytes(inputFile), pageSize, leftPages, rightPages);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    // Keys that should not be remapped (circular refs reset during merge).
    private static readonly HashSet<string> SkipRemapKeys =
        ["Parent", "StructParent", "StructParents"];

    /// <summary>
    /// Remap a source PdfObject into the output stream.
    /// Indirect references are assigned new output object numbers via
    /// <see cref="PdfWriter.AllocateObjectNumber"/> so allocations stay in sync with
    /// deferred stream promotions inside the writer; each source object is written exactly
    /// once per input PDF (deduplicated via <paramref name="objRemap"/>).
    /// Inline dicts/arrays/streams have their contents recursively remapped.
    /// </summary>
    private static PdfObject RemapObject(PdfObject obj, PdfReader reader,
        Dictionary<int, int> objRemap, PdfWriter writer, int depth = 0)
    {
        if (depth > 200) return obj; // guard against StackOverflow on deeply nested/circular structures
        if (obj is PdfIndirectRef indRef)
        {
            // Already remapped — return existing output ref.
            if (objRemap.TryGetValue(indRef.ObjectNumber, out var existing))
                return new PdfIndirectRef(existing, 0);

            // Allocate output obj num BEFORE recursing to handle circular refs.
            // Use writer.AllocateObjectNumber() so we stay in sync with deferred stream
            // allocations that PdfWriter.WriteDictionary makes internally.
            var outputNum = writer.AllocateObjectNumber();
            objRemap[indRef.ObjectNumber] = outputNum;

            var resolved = reader.Resolve(obj);
            if (resolved is null)
                return new PdfIndirectRef(outputNum, 0); // unresolvable — placeholder

            // Recursively remap the resolved object and write it.
            var remapped = RemapObject(resolved, reader, objRemap, writer, depth + 1);
            writer.WriteIndirectObject(outputNum, remapped);

            return new PdfIndirectRef(outputNum, 0);
        }

        switch (obj)
        {
            case PdfDictionary dict:
            {
                var clone = new PdfDictionary();
                foreach (var key in dict.Keys)
                {
                    if (SkipRemapKeys.Contains(key)) continue;
                    var val = dict.Get(key);
                    if (val is not null)
                        clone.Set(key, RemapObject(val, reader, objRemap, writer, depth + 1));
                }
                return clone;
            }

            case PdfArray arr:
            {
                var clonedArr = new PdfArray();
                foreach (var item in arr)
                    clonedArr.Add(RemapObject(item, reader, objRemap, writer, depth + 1));
                return clonedArr;
            }

            case PdfStream stream:
            {
                var clonedDict = new PdfDictionary();
                foreach (var key in stream.Dict.Keys)
                {
                    if (SkipRemapKeys.Contains(key)) continue;
                    var val = stream.Dict.Get(key);
                    if (val is not null)
                        clonedDict.Set(key, RemapObject(val, reader, objRemap, writer, depth + 1));
                }
                var dataCopy = new byte[stream.RawData.Length];
                Array.Copy(stream.RawData, dataCopy, stream.RawData.Length);
                return new PdfStream(clonedDict, dataCopy);
            }

            default:
                return obj; // Immutable primitive types: PdfName, PdfInteger, PdfReal, PdfBoolean, PdfString, PdfNull
        }
    }

    /// <summary>
    /// Merge outlines (bookmarks) from all input documents into the output catalog.
    /// Collects top-level outline items from each source and writes them as a flat
    /// linked list under a single /Outlines dictionary in the output.
    /// </summary>
    /// <summary>Merge the logical-structure trees (/StructTreeRoot) of all inputs into
    /// a single tree under one Document element, written into the concatenated output's
    /// catalog. Element subtrees are cloned inline (primitive attributes only; page and
    /// marked-content references are dropped), mirroring Document.MergeLogicalStructure.
    /// Invoked from <see cref="Concatenate(byte[][])"/> when CopyLogicalStructure is set.</summary>
    private static void MergeStructTrees(List<PdfReader> readers, PdfDictionary catalogDict, PdfWriter writer)
    {
        var mergedKids = new PdfArray();
        foreach (var reader in readers)
        {
            var root = reader.ResolveDict(reader.Catalog.Get("StructTreeRoot"));
            if (root is null) continue;
            foreach (var kid in StructKids(root, reader))
                mergedKids.Add(CloneStructElemInline(kid, reader));
        }
        if (mergedKids.Count == 0) return;

        var mergedDoc = new PdfDictionary();
        mergedDoc.Set("Type", new PdfName("StructElem"));
        mergedDoc.Set("S", new PdfName("Document"));
        mergedDoc.Set("K", mergedKids);

        var rootK = new PdfArray();
        rootK.Add(mergedDoc);
        var structRoot = new PdfDictionary();
        structRoot.Set("Type", new PdfName("StructTreeRoot"));
        structRoot.Set("K", rootK);

        var objNum = writer.AllocateObjectNumber();
        writer.WriteIndirectObject(objNum, structRoot);
        catalogDict.Set("StructTreeRoot", new PdfIndirectRef(objNum, 0));
    }

    /// <summary>The structure-element kids (/K) of a structure dictionary.</summary>
    private static List<PdfDictionary> StructKids(PdfDictionary structDict, PdfReader reader)
    {
        var result = new List<PdfDictionary>();
        var k = reader.Resolve(structDict.Get("K"));
        if (k is PdfArray arr)
        {
            foreach (var item in arr)
                if (reader.ResolveDict(item) is { } d && d.GetName("Type") is null or "StructElem")
                    result.Add(d);
        }
        else if (k is PdfDictionary single && single.GetName("Type") is null or "StructElem")
        {
            result.Add(single);
        }
        return result;
    }

    /// <summary>Clone a structure element (and element children) as an inline dict,
    /// copying primitive attributes and dropping page / marked-content references.</summary>
    private static PdfDictionary CloneStructElemInline(PdfDictionary src, PdfReader reader)
    {
        var clone = new PdfDictionary();
        foreach (var key in src.Keys)
        {
            if (key is "Pg" or "P" or "K") continue;
            var v = src.Get(key);
            if (v is PdfName or PdfString or PdfInteger or PdfReal or PdfBoolean)
                clone.Set(key, v!);
        }
        var children = new PdfArray();
        foreach (var c in StructKids(src, reader))
            children.Add(CloneStructElemInline(c, reader));
        if (children.Count > 0) clone.Set("K", children);
        return clone;
    }

    private static void MergeOutlines(List<PdfReader> readers, List<int> inputPageCounts,
        PdfDictionary catalogDict, PdfWriter writer)
    {
        // Phase 1: collect all top-level outline items with pre-allocated object numbers
        var items = new List<(int objNum, PdfDictionary dict)>();

        int pagesBefore = 0;
        for (int ri = 0; ri < readers.Count; ri++)
        {
            var reader = readers[ri];
            // Page-number offset for destinations in this input: sum of page counts
            // of all preceding inputs. Concatenate output appends inputs in order.
            int offset = pagesBefore;
            pagesBefore += ri < inputPageCounts.Count ? inputPageCounts[ri] : 0;

            var cat = reader.Catalog;
            var outlinesDict = reader.ResolveDict(cat.Get("Outlines"));
            if (outlinesDict is null) continue;

            var firstRef = outlinesDict.Get("First");
            var current = reader.ResolveDict(firstRef);
            var outlineRemap = new Dictionary<int, int>();
            while (current is not null)
            {
                var cloned = (PdfDictionary)RemapObject(current, reader, outlineRemap, writer);
                cloned.Remove("Parent");
                cloned.Remove("Prev");
                cloned.Remove("Next");

                // Destinations in /Dest or /A→/D may carry page indices that were
                // valid in the original document. After concatenation, shift them
                // by the total page count of preceding inputs so the bookmark
                // still points to the intended page.
                ShiftDestinationPages(cloned, offset, reader);

                var itemObjNum = writer.AllocateObjectNumber();
                items.Add((itemObjNum, cloned));

                var nextRef = current.Get("Next");
                if (nextRef is null) break;
                current = reader.ResolveDict(nextRef);
            }
        }

        if (items.Count == 0) return;

        // Allocate the /Outlines dict object number first so items can reference it as Parent
        var outlinesObjNum = writer.AllocateObjectNumber();

        // Phase 2: link items and write them
        for (int i = 0; i < items.Count; i++)
        {
            var (objNum, dict) = items[i];
            dict.Set("Parent", new PdfIndirectRef(outlinesObjNum, 0));
            if (i > 0)
                dict.Set("Prev", new PdfIndirectRef(items[i - 1].objNum, 0));
            if (i < items.Count - 1)
                dict.Set("Next", new PdfIndirectRef(items[i + 1].objNum, 0));
            writer.WriteIndirectObject(objNum, dict);
        }

        // Write the /Outlines dictionary
        var outlines = new PdfDictionary();
        outlines.Set("Type", new PdfName("Outlines"));
        outlines.Set("Count", new PdfInteger(items.Count));
        outlines.Set("First", new PdfIndirectRef(items[0].objNum, 0));
        outlines.Set("Last", new PdfIndirectRef(items[^1].objNum, 0));
        writer.WriteIndirectObject(outlinesObjNum, outlines);
        catalogDict.Set("Outlines", new PdfIndirectRef(outlinesObjNum, 0));
    }

    /// <summary>
    /// Walk an outline-item dict and shift every page-index destination by the
    /// given offset. Handles /Dest arrays and /A→/D arrays where the first
    /// element is a PdfInteger (0-based page index). Indirect refs to pages
    /// are left alone — they were already remapped by RemapObject.
    /// </summary>
    private static void ShiftDestinationPages(PdfDictionary item, int offset, PdfReader reader)
    {
        if (offset == 0) return;

        ShiftDest(item.Get("Dest"));
        var action = item.Get("A");
        if (action is PdfDictionary actionDict)
            ShiftDest(actionDict.Get("D"));

        void ShiftDest(PdfObject? destObj)
        {
            if (destObj is not PdfArray arr || arr.Count == 0) return;
            if (arr[0] is PdfInteger pi)
            {
                var newArr = new PdfArray();
                newArr.Add(new PdfInteger(pi.Value + offset));
                for (int k = 1; k < arr.Count; k++)
                    newArr.Add(arr[k]);
                // Mutate in place isn't possible for PdfArray; replace the key.
                if (destObj == item.Get("Dest"))
                    item.Set("Dest", newArr);
                else if (item.Get("A") is PdfDictionary ad)
                    ad.Set("D", newArr);
            }
        }
    }

    private static void CollectPages(PdfDictionary node, PdfReader reader, List<PdfDictionary> result)
    {
        var type = node.GetName("Type");
        if (type == "Page")
        {
            result.Add(node);
            return;
        }

        // Some PDFs omit explicit /Type on page nodes; treat as Page if /MediaBox is present
        // (and no /Kids — otherwise it's a Pages node)
        if (type is null && node.ContainsKey("MediaBox") && !node.ContainsKey("Kids"))
        {
            result.Add(node);
            return;
        }

        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) return;
        foreach (var kid in kids)
        {
            var kidDict = reader.ResolveDict(kid);
            if (kidDict is not null)
                CollectPages(kidDict, reader, result);
        }
    }

    /// <summary>
    /// Collect entries from a PDF name tree (/Names array + /Kids subtrees).
    /// Each entry is (name, cloned fileSpec ref).
    /// </summary>
    private static void CollectNameTreeEntries(PdfDictionary treeNode, PdfReader reader,
        List<(string name, PdfObject fileSpec)> result,
        Dictionary<int, int> remap, PdfWriter writer)
    {
        // /Names array: pairs of [string, value, string, value, .]
        var namesArr = reader.Resolve(treeNode.Get("Names")) as PdfArray;
        if (namesArr is not null)
        {
            for (int i = 0; i + 1 < namesArr.Count; i += 2)
            {
                var nameObj = reader.Resolve(namesArr[i]);
                var valueObj = namesArr[i + 1];

                var name = nameObj switch
                {
                    PdfString s => s.ToText(),
                    _ => $"file_{result.Count}"
                };

                // Clone the value (file spec dict) via remap
                var cloned = RemapObject(valueObj, reader, remap, writer);
                result.Add((name, cloned));
            }
        }

        // /Kids array: subtrees
        var kids = reader.Resolve(treeNode.Get("Kids")) as PdfArray;
        if (kids is not null)
        {
            foreach (var kid in kids)
            {
                var kidDict = reader.ResolveDict(kid);
                if (kidDict is not null)
                    CollectNameTreeEntries(kidDict, reader, result, remap, writer);
            }
        }
    }

    /// <summary>
    /// Read all bytes from a file, supporting files larger than 2 GB.
    /// </summary>
    private static byte[] ReadAllBytesFromFile(string path)
    {
        var fileInfo = new FileInfo(path);
        var length = fileInfo.Length;
        if (length > Array.MaxLength)
            throw new InvalidOperationException(
                $"Concatenated PDF is {length / (1024 * 1024)} MB, exceeding the 2 GB byte[] limit.");

        var bytes = new byte[length];
        using var fs = File.OpenRead(path);
        var bytesRead = 0;
        while (bytesRead < bytes.Length)
        {
            var read = fs.Read(bytes, bytesRead, bytes.Length - bytesRead);
            if (read == 0) break;
            bytesRead += read;
        }
        return bytes;
    }
}
