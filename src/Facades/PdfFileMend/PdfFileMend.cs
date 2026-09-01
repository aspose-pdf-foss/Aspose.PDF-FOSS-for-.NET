using System.Globalization;
using System.IO.Compression;
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
        var pos = 8; // skip signature
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        byte[]? palette = null;   // PLTE: RGB triples, one per index (colorType 3)
        byte[]? trns = null;      // tRNS: alpha per palette index (optional)
        var idatData = new MemoryStream();

        while (pos < png.Length - 4)
        {
            var chunkLen = ReadInt32BE(png, pos);
            var chunkType = Encoding.ASCII.GetString(png, pos + 4, 4);
            var dataStart = pos + 8;

            if (chunkType == "IHDR")
            {
                width = ReadInt32BE(png, dataStart);
                height = ReadInt32BE(png, dataStart + 4);
                bitDepth = png[dataStart + 8];
                colorType = png[dataStart + 9];
            }
            else if (chunkType == "PLTE")
            {
                palette = new byte[chunkLen];
                Array.Copy(png, dataStart, palette, 0, chunkLen);
            }
            else if (chunkType == "tRNS")
            {
                trns = new byte[chunkLen];
                Array.Copy(png, dataStart, trns, 0, chunkLen);
            }
            else if (chunkType == "IDAT")
            {
                idatData.Write(png, dataStart, chunkLen);
            }
            else if (chunkType == "IEND")
            {
                break;
            }

            pos = dataStart + chunkLen + 4; // +4 for CRC
        }

        if (width == 0 || height == 0)
            throw new ArgumentException("Invalid PNG: could not read IHDR");

        // Decompress IDAT data (deflate inside zlib wrapper)
        var compressedData = idatData.ToArray();
        byte[] decompressed;
        using (var compMs = new MemoryStream(compressedData))
        using (var zlib = new ZLibStream(compMs, CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            zlib.CopyTo(outMs);
            decompressed = outMs.ToArray();
        }

        var hasAlpha = colorType == 4 || colorType == 6;
        var channels = colorType switch
        {
            0 => 1, // Grayscale
            2 => 3, // RGB
            3 => 1, // Palette index
            4 => 2, // Grayscale + Alpha
            6 => 4, // RGBA
            _ => 3,
        };
        // Bytes per pixel used by the row filters is ceil(bitsPerPixel/8), min 1
        // (PNG spec §9.2). Stride is the packed scanline length; for 8/16-bit this
        // equals width*channels*(bitDepth/8) as before, and it also handles the
        // sub-byte (1/2/4-bit) palette/grayscale case.
        var bpp = Math.Max(1, channels * bitDepth / 8);
        var stride = (width * channels * bitDepth + 7) / 8;

        // Unfilter scanlines
        var raw = new byte[height * stride];
        var prevRow = new byte[stride];
        var srcPos = 0;

        for (var y = 0; y < height; y++)
        {
            var filterByte = decompressed[srcPos++];
            var rowStart = y * stride;

            Array.Copy(decompressed, srcPos, raw, rowStart, stride);
            srcPos += stride;

            switch (filterByte)
            {
                case 0: // None
                    break;
                case 1: // Sub
                    for (var x = bpp; x < stride; x++)
                        raw[rowStart + x] = (byte)(raw[rowStart + x] + raw[rowStart + x - bpp]);
                    break;
                case 2: // Up
                    for (var x = 0; x < stride; x++)
                        raw[rowStart + x] = (byte)(raw[rowStart + x] + prevRow[x]);
                    break;
                case 3: // Average
                    for (var x = 0; x < stride; x++)
                    {
                        var a = x >= bpp ? raw[rowStart + x - bpp] : 0;
                        raw[rowStart + x] = (byte)(raw[rowStart + x] + (a + prevRow[x]) / 2);
                    }
                    break;
                case 4: // Paeth
                    for (var x = 0; x < stride; x++)
                    {
                        var a = x >= bpp ? raw[rowStart + x - bpp] : 0;
                        var b = prevRow[x];
                        var c = x >= bpp ? prevRow[x - bpp] : 0;
                        raw[rowStart + x] = (byte)(raw[rowStart + x] + PaethPredictor(a, b, c));
                    }
                    break;
            }

            Array.Copy(raw, rowStart, prevRow, 0, stride);
        }

        // Convert to RGB or RGBA
        if (colorType == 3 && palette is not null) // Palette index -> RGB (RGBA when tRNS present)
        {
            var pixelCount = width * height;
            var indices = UnpackIndices(raw, width, height, stride, bitDepth);
            byte R(int idx) { var p = idx * 3; return p + 2 < palette.Length ? palette[p] : (byte)0; }
            byte G(int idx) { var p = idx * 3; return p + 2 < palette.Length ? palette[p + 1] : (byte)0; }
            byte B(int idx) { var p = idx * 3; return p + 2 < palette.Length ? palette[p + 2] : (byte)0; }
            if (trns is not null)
            {
                var rgba = new byte[pixelCount * 4];
                for (var i = 0; i < pixelCount; i++)
                {
                    var idx = indices[i];
                    rgba[i * 4] = R(idx); rgba[i * 4 + 1] = G(idx); rgba[i * 4 + 2] = B(idx);
                    rgba[i * 4 + 3] = idx < trns.Length ? trns[idx] : (byte)255;
                }
                return (rgba, width, height, true);
            }
            var rgb3 = new byte[pixelCount * 3];
            for (var i = 0; i < pixelCount; i++)
            {
                var idx = indices[i];
                rgb3[i * 3] = R(idx); rgb3[i * 3 + 1] = G(idx); rgb3[i * 3 + 2] = B(idx);
            }
            return (rgb3, width, height, false);
        }
        if (colorType == 0) // Grayscale -> RGB
        {
            var rgb = new byte[width * height * 3];
            for (var i = 0; i < width * height; i++)
            {
                rgb[i * 3] = raw[i];
                rgb[i * 3 + 1] = raw[i];
                rgb[i * 3 + 2] = raw[i];
            }
            return (rgb, width, height, false);
        }
        else if (colorType == 4) // Grayscale+Alpha -> RGBA
        {
            var rgba = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                rgba[i * 4] = raw[i * 2];
                rgba[i * 4 + 1] = raw[i * 2];
                rgba[i * 4 + 2] = raw[i * 2];
                rgba[i * 4 + 3] = raw[i * 2 + 1];
            }
            return (rgba, width, height, true);
        }

        // colorType 2 (RGB) or 6 (RGBA) — already in correct format
        return (raw, width, height, hasAlpha);
    }

}

// CompositingParameters / BlendMode / ImageFilterType moved to top-level
// Aspose.Pdf namespace (src/CompositingParameters.cs) so they match the
// reflection signature used by PdfFileMend.AddImage(...).
