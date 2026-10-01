using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for adding text and images to existing PDF documents.
/// </summary>
public sealed partial class PdfFileMend : ISaveableFacade
{
    void IDisposable.Dispose() => Close();

    private Document? _document;
    private string? _inputFile;
    private string? _outputFile;
    private Stream? _inputStream;
    private Stream? _outputStream;
    private bool _ownsDocument;

    private bool _isWordWrap;

    /// <summary>
    /// Whether to enable word wrapping for AddText operations. Set-only on
    /// the public surface only; internal code reads
    /// <see cref="WrapMode"/> for the resolved behaviour.
    /// </summary>
    public bool IsWordWrap { set => _isWordWrap = value; }

    /// <summary>
    /// Word wrap mode (Default or ByWords).
    /// </summary>
    public WordWrapMode WrapMode { get; set; }

    /// <summary>
    /// Text positioning mode.
    /// </summary>
    public PositioningMode TextPositioningMode { get; set; }

    /// <summary>
    /// Input file path.
    /// </summary>
    public string? InputFile
    {
        get => _inputFile;
        set
        {
            _inputFile = value;
            if (value is not null && _document is null)
                LoadFromFile(value);
        }
    }

    /// <summary>
    /// Output file path.
    /// </summary>
    public string? OutputFile
    {
        get => _outputFile;
        set => _outputFile = value;
    }

    /// <summary>
    /// Input stream.
    /// </summary>
    public Stream? InputStream
    {
        get => _inputStream;
        set
        {
            _inputStream = value;
            if (value is not null && _document is null)
                LoadFromStream(value);
        }
    }

    /// <summary>
    /// Output stream.
    /// </summary>
    public Stream? OutputStream
    {
        get => _outputStream;
        set => _outputStream = value;
    }

    /// <summary>
    /// The document bound to this PdfFileMend, exposing the in-progress
    /// result so it can be chained into another facade.
    /// </summary>
    public Document Document => _document ?? throw new InvalidOperationException("No document bound.");

    /// <summary>
    /// Default constructor.
    /// </summary>
    public PdfFileMend()
    {
    }

    /// <summary>
    /// Create a PdfFileMend from input/output file paths.
    /// </summary>
    public PdfFileMend(string inputFileName, string outputFileName)
    {
        _inputFile = inputFileName;
        _outputFile = outputFileName;
        LoadFromFile(inputFileName);
    }

    /// <summary>
    /// Create a PdfFileMend from input/output streams.
    /// </summary>
    public PdfFileMend(Stream inputStream, Stream outputStream)
    {
        _inputStream = inputStream;
        _outputStream = outputStream;
        LoadFromStream(inputStream);
    }

    /// <summary>Bind a pre-loaded <see cref="Document"/>.</summary>
    public PdfFileMend(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _ownsDocument = false;
    }

    /// <summary>Bind a document and pre-set the destination file path.</summary>
    public PdfFileMend(Document document, string outputFileName)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _outputFile = outputFileName;
        _ownsDocument = false;
    }

    /// <summary>Bind a document and pre-set the destination stream.</summary>
    public PdfFileMend(Document document, Stream destStream)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _outputStream = destStream;
        _ownsDocument = false;
    }

    /// <summary>
    /// Bind an existing Document to this PdfFileMend.
    /// </summary>
    public void BindPdf(Document document)
    {
        _document = document;
        _ownsDocument = false;
    }

    /// <summary>
    /// Bind a PDF document from a file path.
    /// </summary>
    public void BindPdf(string inputFile)
    {
        _inputFile = inputFile;
        LoadFromFile(inputFile);
    }

    /// <summary>
    /// Bind a PDF document from a stream.
    /// </summary>
    public void BindPdf(Stream inputStream)
    {
        LoadFromStream(inputStream);
    }

    /// <summary>
    /// Add formatted text to a specific page at the given lower-left position.
    /// </summary>
    public bool AddText(FormattedText text, int pageNum, float lowerLeftX, float lowerLeftY)
    {
        return AddText(text, pageNum, lowerLeftX, lowerLeftY, 0, 0);
    }

    /// <summary>
    /// Add formatted text to a specific page within the given rectangle.
    /// </summary>
    public bool AddText(FormattedText text, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");

        if (pageNum < 1 || pageNum > _document.PageCount)
            return false;

        var page = _document.Pages.At(pageNum);
        AddTextToPage(page, text, lowerLeftX, lowerLeftY, upperRightX, upperRightY);
        return true;
    }

    /// <summary>
    /// Add formatted text to multiple pages within the given rectangle.
    /// </summary>
    public bool AddText(FormattedText text, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");

        foreach (var pn in pageNums)
        {
            if (pn < 1 || pn > _document.PageCount) continue;
            var page = _document.Pages.At(pn);
            AddTextToPage(page, text, lowerLeftX, lowerLeftY, upperRightX, upperRightY);
        }
        return true;
    }

    /// <summary>
    /// Add an image from a stream to a specific page.
    /// </summary>
    public bool AddImage(Stream imageStream, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
        => AddImage(imageStream, pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY, BlendMode.Normal);

    private bool AddImage(Stream imageStream, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        BlendMode blend)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");

        if (pageNum < 1 || pageNum > _document.PageCount)
            return false;

        using var ms = new MemoryStream();
        imageStream.Position = 0;
        imageStream.CopyTo(ms);
        var imageData = ms.ToArray();

        var page = _document.Pages.At(pageNum);
        AddImageToPage(page, imageData, lowerLeftX, lowerLeftY, upperRightX, upperRightY, blend);
        return true;
    }

    /// <summary>
    /// Add an image from a file path to a specific page.
    /// </summary>
    public bool AddImage(string imageName, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
        => AddImage(imageName, pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY, BlendMode.Normal);

    private bool AddImage(string imageName, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        BlendMode blend)
    {
        var imageData = File.ReadAllBytes(imageName);
        return AddImage(new MemoryStream(imageData), pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY, blend);
    }

    /// <summary>
    /// Add an image from a stream to multiple pages.
    /// </summary>
    public bool AddImage(Stream imageStream, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
        => AddImage(imageStream, pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY, BlendMode.Normal);

    private bool AddImage(Stream imageStream, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        BlendMode blend)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");

        using var ms = new MemoryStream();
        imageStream.Position = 0;
        imageStream.CopyTo(ms);
        var imageData = ms.ToArray();

        foreach (var pn in pageNums)
        {
            if (pn < 1 || pn > _document.PageCount) continue;
            var page = _document.Pages.At(pn);
            AddImageToPage(page, imageData, lowerLeftX, lowerLeftY, upperRightX, upperRightY, blend);
        }
        return true;
    }

    /// <summary>
    /// Add an image from a file path to multiple pages.
    /// </summary>
    public bool AddImage(string imageName, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY)
        => AddImage(imageName, pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY, BlendMode.Normal);

    private bool AddImage(string imageName, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        BlendMode blend)
    {
        var imageData = File.ReadAllBytes(imageName);
        return AddImage(new MemoryStream(imageData), pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY, blend);
    }

    /// <summary>
    /// Add an image with compositing parameters (blend mode). Stream variant.
    /// </summary>
    public bool AddImage(Stream imageStream, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        CompositingParameters compositingParameters)
    {
        return AddImage(imageStream, pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY,
            compositingParameters?.BlendMode ?? BlendMode.Normal);
    }

    /// <summary>
    /// Add an image with compositing parameters (blend mode). File variant.
    /// </summary>
    public bool AddImage(string imageName, int pageNum, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        CompositingParameters compositingParameters)
    {
        return AddImage(imageName, pageNum, lowerLeftX, lowerLeftY, upperRightX, upperRightY,
            compositingParameters?.BlendMode ?? BlendMode.Normal);
    }

    /// <summary>
    /// Add an image with compositing parameters to multiple pages. Stream variant.
    /// </summary>
    public bool AddImage(Stream imageStream, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        CompositingParameters compositingParameters)
    {
        return AddImage(imageStream, pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY,
            compositingParameters?.BlendMode ?? BlendMode.Normal);
    }

    /// <summary>
    /// Add an image with compositing parameters to multiple pages. File variant.
    /// </summary>
    public bool AddImage(string imageName, int[] pageNums, float lowerLeftX, float lowerLeftY, float upperRightX, float upperRightY,
        CompositingParameters compositingParameters)
    {
        return AddImage(imageName, pageNums, lowerLeftX, lowerLeftY, upperRightX, upperRightY,
            compositingParameters?.BlendMode ?? BlendMode.Normal);
    }

    /// <summary>
    /// Save the modified document to a specific file path.
    /// </summary>
    public void Save(string destFile)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");
        _document.Save(destFile);
    }

    /// <summary>
    /// Save the modified document to a stream.
    /// </summary>
    public void Save(Stream destStream)
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound.");
        _document.Save(destStream);
    }

    /// <summary>
    /// Close the document and save to the output file/stream.
    /// </summary>
    public void Close()
    {
        if (_document is null) return;

        if (_outputFile is not null)
        {
            _document.Save(_outputFile);
        }
        else if (_outputStream is not null)
        {
            _document.Save(_outputStream);
        }

        if (_ownsDocument)
            _document.Dispose();
        _document = null;
    }

    // ── Private implementation ──────────────────────────────────────────────

    private void LoadFromFile(string path)
    {
        _document = Document.Open(path);
        _ownsDocument = true;
    }

    private void LoadFromStream(Stream stream)
    {
        _document = Document.Open(stream);
        _ownsDocument = true;
    }

    /// <summary>AddImage letterboxes: the image keeps its own aspect
    /// ratio, scaled uniformly by the smaller of the two fits and centered in the
    /// caller's rectangle (a full-page rect on a scanned page draws the logo as a
    /// centered band, the content above and below staying visible) — probed via a
    /// page-wide rect: cm "841.68 0 0 336.672 0 129.204" for a 2.5:1 image.</summary>
    private static (double x, double y, double w, double h) FitImageRect(
        double imgW, double imgH, double llx, double lly, double rectW, double rectH)
    {
        if (imgW <= 0 || imgH <= 0 || rectW <= 0 || rectH <= 0)
            return (llx, lly, rectW, rectH);
        var scale = Math.Min(rectW / imgW, rectH / imgH);
        var w = imgW * scale;
        var h = imgH * scale;
        return (llx + (rectW - w) / 2, lly + (rectH - h) / 2, w, h);
    }

    /// <summary>
    /// Minimal PNG decoder. Returns raw pixel data (RGB or RGBA), width, height, and hasAlpha flag.
    /// </summary>
    internal static (byte[] pixels, int width, int height, bool hasAlpha) DecodePng(byte[] png)
    {
        var pd = new MendPngDecodeState();
        pd.png = png;
        pd.pos = 8; // skip signature
        pd.width = 0;
        pd.height = 0;
        pd.bitDepth = 0;
        pd.colorType = 0;
        pd.palette = null;   // PLTE: RGB triples, one per index (colorType 3)
        pd.trns = null;      // tRNS: alpha per palette index (optional)
        pd.idatData = new MemoryStream();

        ReadPngChunks(pd);

        if (pd.width == 0 || pd.height == 0)
            throw new ArgumentException("Invalid PNG: could not read IHDR");

        pd.compressedData = pd.idatData.ToArray();
        pd.decompressed = IO.Filters.ManagedInflater.InflateToEnd(pd.compressedData, 0, pd.compressedData.Length, zlibWrapper: true);

        pd.hasAlpha = pd.colorType == 4 || pd.colorType == 6;
        pd.channels = pd.colorType switch
        {
            0 => 1, // Grayscale
            2 => 3, // RGB
            3 => 1, // Palette index
            4 => 2, // Grayscale + Alpha
            6 => 4, // RGBA
            _ => 3,
        };
        pd.bpp = Math.Max(1, pd.channels * pd.bitDepth / 8);
        pd.stride = (pd.width * pd.channels * pd.bitDepth + 7) / 8;

        pd.raw = new byte[pd.height * pd.stride];
        pd.prevRow = new byte[pd.stride];
        pd.srcPos = 0;

        UnfilterPngRows(pd);

        // Convert to RGB or RGBA
        if (pd.colorType == 3 && pd.palette is not null) // Palette index -> RGB (RGBA when tRNS present)
        {
            return DecodePalettePng(pd);
        }
        if (pd.colorType == 0) // Grayscale -> RGB
        {
            var rgb = new byte[pd.width * pd.height * 3];
            for (var i = 0; i < pd.width * pd.height; i++)
            {
                rgb[i * 3] = pd.raw[i];
                rgb[i * 3 + 1] = pd.raw[i];
                rgb[i * 3 + 2] = pd.raw[i];
            }
            return (rgb, pd.width, pd.height, false);
        }
        else if (pd.colorType == 4) // Grayscale+Alpha -> RGBA
        {
            var rgba = new byte[pd.width * pd.height * 4];
            for (var i = 0; i < pd.width * pd.height; i++)
            {
                rgba[i * 4] = pd.raw[i * 2];
                rgba[i * 4 + 1] = pd.raw[i * 2];
                rgba[i * 4 + 2] = pd.raw[i * 2];
                rgba[i * 4 + 3] = pd.raw[i * 2 + 1];
            }
            return (rgba, pd.width, pd.height, true);
        }

        // colorType 2 (RGB) or 6 (RGBA) — already in correct format
        return (pd.raw, pd.width, pd.height, pd.hasAlpha);
    }

}

// CompositingParameters / BlendMode / ImageFilterType moved to top-level
// Aspose.Pdf namespace (src/CompositingParameters.cs) so they match the
// reflection signature used by PdfFileMend.AddImage(...).
