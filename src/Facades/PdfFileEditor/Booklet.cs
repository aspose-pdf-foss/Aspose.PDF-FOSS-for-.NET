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
    /// Create a booklet from a PDF: every output page is one SHEET SIDE carrying two
    /// source pages side by side, so that printed double-sided and folded the sheets
    /// form a booklet. The page count is padded to a multiple of 4 with blank slots.
    /// </summary>
    /// <remarks>
    /// Sheet model (measured): a sheet is the source page turned
    /// landscape (width = source height, height = source width); each source page is
    /// scaled uniformly to fit its half-sheet and centred in it (A4 portrait → a
    /// 842×595 sheet, scale 0.70665, left half at x≈0.27, right half at x≈421.27).
    /// Sheet sides run front/back per sheet: for N padded pages and sheet k,
    /// front = (N−2k, 2k+1), back = (2k+2, N−2k−1). A four-page document gives two
    /// output pages, a five-page document four.
    /// </remarks>
    public byte[] MakeBooklet(byte[] inputPdf)
    {
        using var doc = Document.Open(inputPdf);
        var n = doc.PageCount;
        var padded = n;
        while (padded % 4 != 0) padded++;

        var sides = new List<(int left, int right)>();
        for (int k = 0; 2 * k + 1 < padded - 2 * k; k++)
        {
            sides.Add((padded - 2 * k, 2 * k + 1));
            sides.Add((2 * k + 2, padded - 2 * k - 1));
        }
        return ComposeBookletSheets(doc, sides);
    }

    /// <summary>Compose one output page per (left, right) pair; a slot outside the
    /// source page range stays blank.</summary>
    private static byte[] ComposeBookletSheets(Document source, List<(int left, int right)> sides)
    {
        var first = source.Pages[1];
        var sheetW = first.Height;
        var sheetH = first.Width;
        var half = sheetW / 2;

        using var booklet = Document.Create();
        foreach (var (left, right) in sides)
        {
            var sheet = booklet.Pages.Add(sheetW, sheetH);
            PlaceOnHalfSheet(sheet, source, left, 0, half, sheetH);
            PlaceOnHalfSheet(sheet, source, right, half, half, sheetH);
        }
        return booklet.ToArray();
    }

    private static void PlaceOnHalfSheet(Page sheet, Document source, int pageNumber,
        double x0, double halfW, double sheetH)
    {
        if (pageNumber < 1 || pageNumber > source.PageCount) return;
        var page = source.Pages[pageNumber];
        var scale = Math.Min(halfW / page.Width, sheetH / page.Height);
        var w = page.Width * scale;
        var h = page.Height * scale;
        var stamp = new PdfPageStamp(page)
        {
            Width = w,
            Height = h,
            XIndent = x0 + (halfW - w) / 2,
            YIndent = (sheetH - h) / 2,
            CarryAnnotations = false,
        };
        sheet.AddStamp(stamp);
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
    /// Create a booklet from explicit left/right page lists: output page i carries
    /// <c>leftPages[i]</c> on its left half and <c>rightPages[i]</c> on its right;
    /// an index outside the source page range (0, negative, or past the end) leaves
    /// that half blank. One output page per pair — no front/back generation.
    /// </summary>
    public byte[] MakeBooklet(byte[] inputPdf, int[] leftPages, int[] rightPages)
    {
        var maxPairs = Math.Max(leftPages.Length, rightPages.Length);
        var sides = new List<(int left, int right)>();
        for (int i = 0; i < maxPairs; i++)
            sides.Add((i < leftPages.Length ? leftPages[i] : -1, i < rightPages.Length ? rightPages[i] : -1));

        using var doc = Document.Open(inputPdf);
        return ComposeBookletSheets(doc, sides);
    }

    /// <summary>
    /// Create a booklet using specified left/right pages and a custom page size.
    /// </summary>
    public byte[] MakeBooklet(byte[] inputPdf, PageSize pageSize, int[] leftPages, int[] rightPages)
    {
        var booklet = MakeBooklet(inputPdf, leftPages, rightPages);
        return ResizePages(booklet, pageSize.Width, pageSize.Height);
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
}
