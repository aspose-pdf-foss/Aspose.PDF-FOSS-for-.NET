using System.Linq;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Real-only additions to <see cref="PdfFileEditor"/>: exception-handling
/// state, Try* wrappers around the existing working methods, and
/// MemoryStream/file overloads for SplitToBulks/SplitToPages that wrap the
/// real byte[] implementations already present in PdfFileEditor.cs.
/// </summary>
public sealed partial class PdfFileEditor
{
    /// <summary>When true, Try* methods propagate exceptions; when false
    /// (the default) they capture the exception in <see cref="LastException"/>
    /// and return false.</summary>
    public bool AllowExceptions { get; set; }

    /// <summary>Last exception captured by a Try* method, or null.</summary>
    public Exception? LastException { get; private set; }

    /// <summary>When true, signatures present in the inputs are stripped
    /// from the Concatenate output. Honoured by the byte[] Concatenate
    /// implementation: after concatenation, every signature field's /V
    /// entry is removed via the same StripSignatureValue path used by
    /// PdfFileSignature.RemoveSignatures.</summary>
    public bool RemoveSignatures { get; set; }

    /// <summary>When non-empty, Concatenate outputs are encrypted with this
    /// owner password (AES-128) after the merge completes.</summary>
    public string OwnerPassword { get; set; } = string.Empty;

    /// <summary>When true, Concatenate honours document-level /AA actions on
    /// the inputs and copies them onto the output catalog. Default false.</summary>
    public bool KeepActions { get; set; }

    /// <summary>When true, identical optional-content groups in the inputs
    /// are deduplicated in the Concatenate output. Default false.</summary>
    public bool MergeDuplicateLayers { get; set; }

    /// <summary>When true, the Concatenate output retains /UR usage-rights
    /// entries from the inputs. Default false.</summary>
    public bool PreserveUserRights { get; set; }

    private string _uniqueSuffix = "_unique%NUM%";
    /// <summary>Whether <see cref="UniqueSuffix"/> was explicitly assigned. The XFA
    /// merge renames duplicate top subforms with this suffix only when the caller
    /// set it; otherwise it appends a plain occurrence number.</summary>
    internal bool UniqueSuffixSet => _uniqueSuffixSet;
    private bool _uniqueSuffixSet;

    /// <summary>Suffix template used to disambiguate colliding form-field names when
    /// <see cref="KeepFieldsUnique"/> is true. Must contain the <c>%NUM%</c> placeholder
    /// (replaced by an incrementing number); assigning a value without it (or null) throws
    /// <see cref="System.ArgumentException"/> and leaves the previous value unchanged.</summary>
    public string UniqueSuffix
    {
        get => _uniqueSuffix;
        set
        {
            if (value is null || !value.Contains("%NUM%"))
                throw new System.ArgumentException("UniqueSuffix must contain the '%NUM%' placeholder.", nameof(value));
            _uniqueSuffix = value;
            _uniqueSuffixSet = true;
        }
    }

    /// <summary>Diagnostic log emitted by the most recent Concatenate /
    /// ConvertTo pass. Read-only — populated by <see cref="AppendConversionLog"/>
    /// from the byte[] Concatenate path.</summary>
    public string ConversionLog => _conversionLog.ToString();
    private readonly System.Text.StringBuilder _conversionLog = new();

    internal void AppendConversionLog(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        _conversionLog.AppendLine(line);
    }

    internal void ResetConversionLog() => _conversionLog.Clear();

    /// <summary>Target PDF/A format for the next Concatenate output. When
    /// set, the result is converted to that format via Document.Convert
    /// after concatenation.</summary>
    public PdfFormat ConvertTo
    {
        set => _convertToFormat = value;
    }
    private PdfFormat? _convertToFormat;

    /// <summary>Items the Concatenate path skipped over because their input
    /// failed to parse. Empty when no corruption was tolerated (the default
    /// <see cref="ConcatenateCorruptedFileAction.StopWithError"/> aborts on
    /// the first parse error).</summary>
    public CorruptedItem[] CorruptedItems => _corrupted.ToArray();
    private readonly System.Collections.Generic.List<CorruptedItem> _corrupted = new();

    /// <summary>One file the Concatenate path could not parse — recorded
    /// only when <see cref="CorruptedFileAction"/> is set to skip-and-continue.</summary>
    public sealed class CorruptedItem
    {
        internal CorruptedItem(string? fileName, int index, Exception? exception)
        {
            FileName = fileName;
            Index = index;
            Exception = exception;
        }

        public string? FileName { get; set; }

        public int Index { get; private set; }

        public Exception? Exception { get; private set; }
    }

    // ── Try* wrappers — each wraps a real, working method ─────────────────

    public bool TryAppend(string inputFile, string[] portFiles, int startPage, int endPage, string outputFile) =>
        Try(() => Append(inputFile, portFiles, startPage, endPage, outputFile));

    public bool TryAppend(Stream inputStream, Stream[] portStreams, int startPage, int endPage, Stream outputStream) =>
        Try(() => Append(inputStream, portStreams, startPage, endPage, outputStream));

    public bool TryConcatenate(string[] inputFiles, string outputFile) =>
        Try(() => Concatenate(inputFiles, outputFile));

    public bool TryConcatenate(string firstInputFile, string secInputFile, string outputFile) =>
        Try(() => Concatenate(firstInputFile, secInputFile, outputFile));

    public bool TryConcatenate(string firstInputFile, string secInputFile, string blankPageFile, string outputFile) =>
        Try(() => Concatenate(firstInputFile, secInputFile, blankPageFile, outputFile));

    public bool TryConcatenate(Stream[] inputStream, Stream outputStream) =>
        Try(() => Concatenate(inputStream, outputStream));

    public bool TryConcatenate(Stream firstInputStream, Stream secInputStream, Stream blankPageStream, Stream outputStream) =>
        Try(() => Concatenate(firstInputStream, secInputStream, blankPageStream, outputStream));

    public bool TryConcatenate(Document[] src, Document dest) =>
        Try(() => Concatenate(src, dest));

    public bool TryDelete(string inputFile, int[] pageNumber, string outputFile) =>
        Try(() => Delete(inputFile, pageNumber, outputFile));

    public bool TryDelete(Stream inputStream, int[] pageNumber, Stream outputStream) =>
        Try(() => Delete(inputStream, pageNumber, outputStream));

    public bool TryExtract(string inputFile, int startPage, int endPage, string outputFile) =>
        Try(() => Extract(inputFile, startPage, endPage, outputFile));

    public bool TryExtract(string inputFile, int[] pageNumber, string outputFile) =>
        Try(() => Extract(inputFile, pageNumber, outputFile));

    public bool TryExtract(Stream inputStream, int[] pageNumber, Stream outputStream) =>
        Try(() => Extract(inputStream, pageNumber, outputStream));

    public bool TryInsert(string inputFile, int insertLocation, string portFile, int[] pageNumber, string outputFile) =>
        Try(() => Insert(inputFile, insertLocation, portFile, pageNumber, outputFile));

    public bool TryInsert(Stream inputStream, int insertLocation, Stream portStream, int[] pageNumber, Stream outputStream) =>
        Try(() => Insert(inputStream, insertLocation, portStream, pageNumber, outputStream));

    public bool TryMakeBooklet(string inputFile, string outputFile) =>
        Try(() => MakeBooklet(inputFile, outputFile));

    public bool TryMakeBooklet(string inputFile, string outputFile, PageSize pageSize) =>
        Try(() => MakeBooklet(inputFile, outputFile, pageSize));

    public bool TryMakeBooklet(string inputFile, string outputFile, int[] leftPages, int[] rightPages) =>
        Try(() => MakeBooklet(inputFile, outputFile, leftPages, rightPages));

    public bool TryMakeBooklet(string inputFile, string outputFile, PageSize pageSize, int[] leftPages, int[] rightPages) =>
        Try(() => MakeBooklet(inputFile, outputFile, pageSize, leftPages, rightPages));

    public bool TrySplitFromFirst(string inputFile, int location, string outputFile) =>
        Try(() => SplitFromFirst(inputFile, location, outputFile));

    public bool TrySplitFromFirst(Stream inputStream, int location, Stream outputStream) =>
        Try(() => SplitFromFirst(inputStream, location, outputStream));

    public bool TrySplitToEnd(string inputFile, int location, string outputFile) =>
        Try(() => SplitToEnd(inputFile, location, outputFile));

    public bool TrySplitToEnd(Stream inputStream, int location, Stream outputStream) =>
        Try(() => SplitToEnd(inputStream, location, outputStream));

    public bool TryResizeContents(string source, string destination, int[] pages, ContentsResizeParameters parameters) =>
        Try(() => ResizeContents(source, destination, pages, parameters));

    public bool TryResizeContents(Stream source, Stream destination, int[] pages, ContentsResizeParameters parameters) =>
        Try(() => ResizeContents(source, destination, pages, parameters));

    public bool TryResizeContents(Stream source, Stream destination, int[] pages, double newWidth, double newHeight) =>
        Try(() => ResizeContents(source, destination, pages, newWidth, newHeight));

    public bool TryMakeBooklet(Stream inputStream, Stream outputStream) =>
        Try(() => MakeBooklet(inputStream, outputStream));

    public bool TryMakeBooklet(Stream inputStream, Stream outputStream, PageSize pageSize) =>
        Try(() => MakeBooklet(inputStream, outputStream, pageSize));

    public bool TryMakeBooklet(Stream inputStream, Stream outputStream, int[] leftPages, int[] rightPages) =>
        Try(() => MakeBooklet(inputStream, outputStream, leftPages, rightPages));

    public bool TryMakeBooklet(Stream inputStream, Stream outputStream, PageSize pageSize, int[] leftPages, int[] rightPages) =>
        Try(() => MakeBooklet(inputStream, outputStream, pageSize, leftPages, rightPages));

    public bool TryMakeNUp(string firstInputFile, string secondInputFile, string outputFile) =>
        Try(() => MakeNUp(firstInputFile, secondInputFile, outputFile));

    public bool TryMakeNUp(string inputFile, string outputFile, int x, int y) =>
        Try(() => MakeNUp(inputFile, outputFile, x, y));

    public bool TryMakeNUp(string inputFile, string outputFile, int x, int y, PageSize pageSize) =>
        Try(() => MakeNUp(inputFile, outputFile, x, y, pageSize));

    public bool TryMakeNUp(string[] inputFiles, string outputFile, bool isSidewise) =>
        Try(() => MakeNUp(inputFiles, outputFile, isSidewise));

    public bool TryMakeNUp(Stream firstInputStream, Stream secondInputStream, Stream outputStream) =>
        Try(() => MakeNUp(firstInputStream, secondInputStream, outputStream));

    public bool TryMakeNUp(Stream inputStream, Stream outputStream, int x, int y) =>
        Try(() => MakeNUp(inputStream, outputStream, x, y));

    public bool TryMakeNUp(Stream inputStream, Stream outputStream, int x, int y, PageSize pageSize) =>
        Try(() => MakeNUp(inputStream, outputStream, x, y, pageSize));

    public bool TryMakeNUp(Stream[] inputStreams, Stream outputStream, bool isSidewise) =>
        Try(() => MakeNUp(inputStreams, outputStream, isSidewise));

    // ── ResizeContents / ResizeContentsPct (real — wraps ResizeContents(Document,…))

    public bool ResizeContents(string source, string destination, int[] pages,
        ContentsResizeParameters parameters)
    {
        using var doc = Document.Open(source);
        if (pages is null) ResizeContents(doc, parameters);
        else ResizeContents(doc, pages, parameters);
        doc.Save(destination);
        return true;
    }

    public bool ResizeContents(Stream source, Stream destination, int[] pages,
        ContentsResizeParameters parameters)
    {
        using var doc = new Document(source);
        if (pages is null) ResizeContents(doc, parameters);
        else ResizeContents(doc, pages, parameters);
        doc.Save(destination);
        return true;
    }

    public bool ResizeContents(string source, string destination, int[] pages,
        double newWidth, double newHeight)
        => ResizeContents(source, destination, pages, BuildSizeParams(newWidth, newHeight, percent: false));

    public bool ResizeContents(Stream source, Stream destination, int[] pages,
        double newWidth, double newHeight)
        => ResizeContents(source, destination, pages, BuildSizeParams(newWidth, newHeight, percent: false));

    public bool ResizeContentsPct(string source, string destination, int[] pages,
        double newWidth, double newHeight)
        => ResizeContents(source, destination, pages, BuildSizeParams(newWidth, newHeight, percent: true));

    public bool ResizeContentsPct(Stream source, Stream destination, int[] pages,
        double newWidth, double newHeight)
        => ResizeContents(source, destination, pages, BuildSizeParams(newWidth, newHeight, percent: true));

    private static ContentsResizeParameters BuildSizeParams(double w, double h, bool percent)
    {
        var value = percent ? ContentsResizeValue.Percents : (System.Func<double, ContentsResizeValue>)ContentsResizeValue.Units;
        return new ContentsResizeParameters
        {
            ContentsWidth = value(w),
            ContentsHeight = value(h),
            LeftMargin = ContentsResizeValue.Auto(),
            RightMargin = ContentsResizeValue.Auto(),
            TopMargin = ContentsResizeValue.Auto(),
            BottomMargin = ContentsResizeValue.Auto(),
        };
    }

    // ── AddMargins / AddMarginsPct (real — wraps ResizeContents(Document,…))

    public bool AddMargins(string source, string destination, int[] pages,
        double leftMargin, double rightMargin, double topMargin, double bottomMargin)
        => ResizeContents(source, destination, pages,
            BuildMarginParams(leftMargin, rightMargin, topMargin, bottomMargin, percent: false));

    public bool AddMargins(Stream source, Stream destination, int[] pages,
        double leftMargin, double rightMargin, double topMargin, double bottomMargin)
        => ResizeContents(source, destination, pages,
            BuildMarginParams(leftMargin, rightMargin, topMargin, bottomMargin, percent: false));

    public bool AddMarginsPct(string source, string destination, int[] pages,
        double leftMargin, double rightMargin, double topMargin, double bottomMargin)
        => ResizeContents(source, destination, pages,
            BuildMarginParams(leftMargin, rightMargin, topMargin, bottomMargin, percent: true));

    public bool AddMarginsPct(Stream source, Stream destination, int[] pages,
        double leftMargin, double rightMargin, double topMargin, double bottomMargin)
        => ResizeContents(source, destination, pages,
            BuildMarginParams(leftMargin, rightMargin, topMargin, bottomMargin, percent: true));

    private static ContentsResizeParameters BuildMarginParams(double l, double r, double t, double b, bool percent)
    {
        var value = percent ? ContentsResizeValue.Percents : (System.Func<double, ContentsResizeValue>)ContentsResizeValue.Units;
        return new ContentsResizeParameters
        {
            LeftMargin = value(l),
            RightMargin = value(r),
            TopMargin = value(t),
            BottomMargin = value(b),
            ContentsWidth = ContentsResizeValue.Auto(),
            ContentsHeight = ContentsResizeValue.Auto(),
        };
    }

    // ── MakeBooklet stream wrappers (real — wraps byte[] MakeBooklet) ────────

    public bool MakeBooklet(Stream inputStream, Stream outputStream)
        => WriteBytes(outputStream, MakeBooklet(ReadStream(inputStream)));

    public bool MakeBooklet(Stream inputStream, Stream outputStream, PageSize pageSize)
        => WriteBytes(outputStream, MakeBooklet(ReadStream(inputStream), pageSize));

    public bool MakeBooklet(Stream inputStream, Stream outputStream, int[] leftPages, int[] rightPages)
        => WriteBytes(outputStream, MakeBooklet(ReadStream(inputStream), leftPages, rightPages));

    public bool MakeBooklet(Stream inputStream, Stream outputStream, PageSize pageSize, int[] leftPages, int[] rightPages)
        => WriteBytes(outputStream, MakeBooklet(ReadStream(inputStream), pageSize, leftPages, rightPages));

    private static bool WriteBytes(Stream output, byte[] bytes)
    {
        output.Write(bytes, 0, bytes.Length);
        return true;
    }

    // ── MakeNUp (real — basic grid imposition + side-by-side) ────────────────

    public bool MakeNUp(string inputFile, string outputFile, int x, int y)
        => WriteFile(outputFile, MakeNUpCore(File.ReadAllBytes(inputFile), x, y, pageSize: null));

    public bool MakeNUp(string inputFile, string outputFile, int x, int y, PageSize pageSize)
        => WriteFile(outputFile, MakeNUpCore(File.ReadAllBytes(inputFile), x, y, pageSize));

    public bool MakeNUp(Stream inputStream, Stream outputStream, int x, int y)
        => WriteBytes(outputStream, MakeNUpCore(ReadStream(inputStream), x, y, pageSize: null));

    public bool MakeNUp(Stream inputStream, Stream outputStream, int x, int y, PageSize pageSize)
        => WriteBytes(outputStream, MakeNUpCore(ReadStream(inputStream), x, y, pageSize));

    public bool MakeNUp(string firstInputFile, string secondInputFile, string outputFile)
        => WriteFile(outputFile, MakeNUpTwoFiles(
            File.ReadAllBytes(firstInputFile), File.ReadAllBytes(secondInputFile), isSidewise: true));

    public bool MakeNUp(Stream firstInputStream, Stream secondInputStream, Stream outputStream)
        => WriteBytes(outputStream, MakeNUpTwoFiles(
            ReadStream(firstInputStream), ReadStream(secondInputStream), isSidewise: true));

    public bool MakeNUp(string[] inputFiles, string outputFile, bool isSidewise)
    {
        if (inputFiles is null || inputFiles.Length == 0)
            throw new System.ArgumentException("At least one input file required", nameof(inputFiles));
        var bytes = inputFiles.Select(File.ReadAllBytes).ToArray();
        return WriteFile(outputFile, MakeNUpMany(bytes, isSidewise));
    }

    public bool MakeNUp(Stream[] inputStreams, Stream outputStream, bool isSidewise)
    {
        if (inputStreams is null || inputStreams.Length == 0)
            throw new System.ArgumentException("At least one input stream required", nameof(inputStreams));
        var bytes = inputStreams.Select(ReadStream).ToArray();
        return WriteBytes(outputStream, MakeNUpMany(bytes, isSidewise));
    }

    private static bool WriteFile(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        return true;
    }

    /// <summary>Pack N=x*y source pages per output sheet (real grid imposition).
    /// Each source page becomes a Form XObject; output sheets place the
    /// XObjects at row+column positions, scaled to fit the cell. Each sheet keeps
    /// the source page size (unless an explicit <paramref name="pageSize"/> is given),
    /// so the x*y pages are scaled DOWN to 1/x × 1/y and tiled — matching Aspose.PDF
    /// for .NET, whose N-up sheets are the same size as the input pages. Page-level
    /// annotations (markup, shapes, etc.) are carried onto the sheet with the same
    /// scale+translate as their page, since annotations live in page space and are
    /// not affected by the XObject's content-stream matrix.</summary>
    private static byte[] MakeNUpCore(byte[] input, int x, int y, PageSize? pageSize)
    {
        if (x < 1 || y < 1) throw new System.ArgumentOutOfRangeException(nameof(x), "x and y must be >= 1");
        var perSheet = x * y;
        using var src = Document.Open(input);
        var target = Document.Create();
        var n = src.PageCount;

        var sheetW = pageSize?.Width ?? src.Pages[1].MediaBox.Width;
        var sheetH = pageSize?.Height ?? src.Pages[1].MediaBox.Height;

        // One clone map shared across the whole build so annotation resources shared
        // between source pages (fonts, colour spaces, appearance forms) import once.
        var cloneMap = new System.Collections.Generic.Dictionary<int, int>();

        for (var srcPage = 1; srcPage <= n; srcPage += perSheet)
        {
            var sheet = target.Pages.Add();
            sheet.SetMediaBox(new Rectangle(0, 0, sheetW, sheetH));
            for (var slot = 0; slot < perSheet && srcPage + slot <= n; slot++)
            {
                var row = slot / x;        // top-down
                var col = slot % x;
                var page = src.Pages[srcPage + slot];
                var pw = page.MediaBox.Width;
                var ph = page.MediaBox.Height;
                var cellW = sheetW / x;
                var cellH = sheetH / y;
                var scale = System.Math.Min(cellW / pw, cellH / ph);
                var dstX = col * cellW + (cellW - pw * scale) / 2;
                var dstY = (y - 1 - row) * cellH + (cellH - ph * scale) / 2;
                StampPageAsXObject(target, sheet, src, page, dstX, dstY, scale);
                CarrySourceAnnotations(target, sheet, page, scale, dstX, dstY, cloneMap);
            }
        }
        return target.ToArray();
    }

    // Re-create each of the source page's annotations on the imposed sheet, mapping
    // its coordinates by the SAME uniform scale+translate applied to the page content
    // (a source point (px,py) lands at (dstX+px*scale, dstY+py*scale)). Annotations
    // carry absolute page-space geometry, so they must be transformed explicitly — the
    // Form XObject's `cm` matrix only affects the drawn content, not annotations.
    private static void CarrySourceAnnotations(Document target, Page sheet, Page srcPage,
        double scale, double dstX, double dstY, System.Collections.Generic.Dictionary<int, int> cloneMap)
    {
        var srcReader = srcPage.Reader;
        if (srcReader.Resolve(srcPage.Dict.Get("Annots")) is not PdfArray annots) return;

        var sheetAnnots = sheet.Annotations;
        foreach (var entry in annots)
        {
            if (srcReader.ResolveDict(entry) is not PdfDictionary srcAnnot) continue;
            var subtype = srcAnnot.GetName("Subtype");
            // Skip annotations whose geometry/targets reference other objects a plain
            // coordinate transform can't fix: Links/Widgets carry /Dest,/A,/Parent page
            // and field refs; Popups depend on their parent annotation.
            if (subtype is "Link" or "Popup" or "Widget") continue;

            // Deep-import a copy that omits the page-targeting keys, so the clone does
            // not drag the whole source page (via /P) or a popup graph into the target.
            var shallow = new PdfDictionary();
            foreach (var key in srcAnnot.Keys)
            {
                if (key is "P" or "Popup") continue;
                if (srcAnnot.Get(key) is { } v) shallow.Set(key, v);
            }
            var imported = target.ImportDict(shallow, srcReader, cloneMap);
            TransformAnnotationGeometry(imported, target.Reader, scale, dstX, dstY);
            sheetAnnots.Add(Aspose.Pdf.Annotations.Annotation.Create(imported, sheet.Reader));
        }
    }

    // Apply x' = x*s + tx, y' = y*s + ty to every coordinate-bearing entry of an
    // annotation dict. Uniform scaling means the /AP appearance auto-maps to the new
    // /Rect (PDF 32000-1 §12.5.5), so only the coordinate arrays need rewriting.
    private static void TransformAnnotationGeometry(PdfDictionary d, IO.PdfReader reader,
        double s, double tx, double ty)
    {
        foreach (var key in new[] { "Rect", "L", "QuadPoints", "Vertices", "CL" })
        {
            if (reader.Resolve(d.Get(key)) is PdfArray arr)
                d.Set(key, TransformFlatCoords(arr, s, tx, ty));
        }
        // /InkList is an array of coordinate paths.
        if (reader.Resolve(d.Get("InkList")) is PdfArray ink)
        {
            var newInk = new PdfArray();
            foreach (var sub in ink)
                newInk.Add(reader.Resolve(sub) is PdfArray path ? TransformFlatCoords(path, s, tx, ty) : sub);
            d.Set("InkList", newInk);
        }
    }

    // Map a flat [x0 y0 x1 y1 …] coordinate array by (scale, tx, ty).
    private static PdfArray TransformFlatCoords(PdfArray arr, double s, double tx, double ty)
    {
        var result = new PdfArray();
        for (var i = 0; i < arr.Count; i++)
        {
            var v = arr[i] switch { PdfReal r => r.Value, PdfInteger n => n.Value, _ => 0.0 };
            result.Add(new PdfReal(i % 2 == 0 ? v * s + tx : v * s + ty));
        }
        return result;
    }

    private static byte[] MakeNUpTwoFiles(byte[] left, byte[] right, bool isSidewise)
    {
        // 2-up: each output sheet pairs page-i of left with page-i of right.
        using var l = Document.Open(left);
        using var r = Document.Open(right);
        var n = System.Math.Max(l.PageCount, r.PageCount);
        var target = Document.Create();
        for (var i = 1; i <= n; i++)
        {
            var lp = i <= l.PageCount ? l.Pages[i] : null;
            var rp = i <= r.PageCount ? r.Pages[i] : null;
            var sample = lp ?? rp!;
            var w = sample.MediaBox.Width;
            var h = sample.MediaBox.Height;
            var sheet = target.Pages.Add();
            if (isSidewise)
            {
                sheet.SetMediaBox(new Rectangle(0, 0, w * 2, h));
                if (lp is not null) StampPageAsXObject(target, sheet, l, lp, 0, 0, scale: 1.0);
                if (rp is not null) StampPageAsXObject(target, sheet, r, rp, w, 0, scale: 1.0);
            }
            else
            {
                sheet.SetMediaBox(new Rectangle(0, 0, w, h * 2));
                if (lp is not null) StampPageAsXObject(target, sheet, l, lp, 0, h, scale: 1.0);
                if (rp is not null) StampPageAsXObject(target, sheet, r, rp, 0, 0, scale: 1.0);
            }
        }
        return target.ToArray();
    }

    private static byte[] MakeNUpMany(byte[][] inputs, bool isSidewise)
    {
        // Pair-wise reduce: combine inputs[0]+[1], then result+[2], etc.
        var current = inputs[0];
        for (var i = 1; i < inputs.Length; i++)
            current = MakeNUpTwoFiles(current, inputs[i], isSidewise);
        return current;
    }

    private static void StampPageAsXObject(Document target, Page sheet,
        Document srcDoc, Page srcPage, double x, double y, double scale)
    {
        // Convert the source page into a Form XObject inserted into the
        // target sheet's resource dictionary, then emit a `cm + Do` content
        // stream snippet placing it at (x, y) scaled by `scale`.
        var sourceStamp = new PdfPageStamp(srcPage)
        {
            XIndent = x,
            YIndent = y,
            Width = srcPage.MediaBox.Width * scale,
            Height = srcPage.MediaBox.Height * scale,
            // N-up carries annotations itself (transformed to the tile
            // geometry); the stamp must not import them a second time.
            CarryAnnotations = false,
        };
        sourceStamp.ApplyTo(sheet);
        _ = target; _ = srcDoc; // contextual refs (target.Pages already holds sheet; srcDoc owns srcPage).
    }

    // ── SplitToBulks / SplitToPages: file/stream wrappers over real byte[] impls ─

    public MemoryStream[] SplitToBulks(string inputFile, int[][] numberOfPage)
    {
        var bulks = SplitToBulks(File.ReadAllBytes(inputFile), numberOfPage);
        return bulks.Select(b => new MemoryStream(b, writable: false)).ToArray();
    }

    public MemoryStream[] SplitToBulks(Stream inputStream, int[][] numberOfPage)
    {
        var bulks = SplitToBulks(ReadStream(inputStream), numberOfPage);
        return bulks.Select(b => new MemoryStream(b, writable: false)).ToArray();
    }

    public MemoryStream[] SplitToPages(Stream inputStream)
    {
        var pages = SplitToPages(ReadStream(inputStream));
        return pages.Select(p => new MemoryStream(p, writable: false)).ToArray();
    }

    public void SplitToPages(string inputFile, string fileNameTemplate)
        => WriteSplitPages(SplitToPages(File.ReadAllBytes(inputFile)), fileNameTemplate);

    public void SplitToPages(Stream inputStream, string fileNameTemplate)
        => WriteSplitPages(SplitToPages(ReadStream(inputStream)), fileNameTemplate);

    private static void WriteSplitPages(byte[][] pages, string fileNameTemplate)
    {
        for (var i = 0; i < pages.Length; i++)
        {
            var path = fileNameTemplate.Contains("{0}", System.StringComparison.Ordinal)
                ? string.Format(fileNameTemplate, i + 1)
                : System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(fileNameTemplate) ?? "",
                    $"{System.IO.Path.GetFileNameWithoutExtension(fileNameTemplate)}_{i + 1}{System.IO.Path.GetExtension(fileNameTemplate)}");
            File.WriteAllBytes(path, pages[i]);
        }
    }

    private bool Try(Func<bool> action)
    {
        try { return action(); }
        catch (Exception ex)
        {
            LastException = ex;
            if (AllowExceptions) throw;
            return false;
        }
    }

    private bool Try(Action action)
    {
        try { action(); return true; }
        catch (Exception ex)
        {
            LastException = ex;
            if (AllowExceptions) throw;
            return false;
        }
    }
}
