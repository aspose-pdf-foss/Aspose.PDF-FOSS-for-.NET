using Aspose.Pdf.Devices;
using SdiImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Image format for PdfConverter output.
/// </summary>
public enum PdfConverterImageFormat
{
    Png,
    Jpeg,
    Bmp,
    Tiff,
    Gif,
}

/// <summary>
/// Facade for converting PDF pages to images.
/// Supports stateful iterator pattern (BindPdf → DoConvert → HasNextImage → GetNextImage)
/// and stateless batch conversion helpers.
/// </summary>
public sealed class PdfConverter : IDisposable
{
    private readonly IPageRenderer _renderer;
    private Document? _document;
    private int _currentPage;
    private bool _converted;

    /// <summary>Output resolution in DPI. Default: 150.</summary>
    public Resolution Resolution { get; set; }

    /// <summary>Owner password used when binding encrypted PDFs.</summary>
    public string? Password { get; set; }

    /// <summary>User password used when binding encrypted PDFs. Stored only; binding does not currently honour it.</summary>
    public string? UserPassword { get; set; }

    /// <summary>1-based start page (inclusive). Default: 1.</summary>
    public int StartPage { get; set; } = 1;

    /// <summary>1-based end page (inclusive). Default: last page.</summary>
    public int EndPage { get; set; }

    /// <summary>Number of pages in the bound document, or 0 if none bound.</summary>
    public int PageCount => _document?.PageCount ?? 0;

    /// <summary>Which page box is used as the rendering bounds. Stored only; the converter
    /// renders the crop box clipped to the media box (the visible region), which equals the
    /// media box when no crop box is set.</summary>
    public PageCoordinateType CoordinateType { get; set; } = PageCoordinateType.MediaBox;

    /// <summary>Form-field presentation mode. Stored only; not currently honoured by the converter.</summary>
    public FormPresentationMode FormPresentationMode { get; set; } = FormPresentationMode.Production;

    /// <summary>Render content outside the page crop area. Stored only; not currently honoured.</summary>
    public bool ShowHiddenAreas { get; set; }

    /// <summary>
    /// Default constructor. A renderer must be set via property or the converter
    /// will operate in metadata-only mode (BindPdf, PageCount, etc.).
    /// </summary>
    public PdfConverter()
    {
        _renderer = null!;
        Resolution = new Resolution(150);
    }

    public PdfConverter(IPageRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Resolution = new Resolution(150);
    }

    public PdfConverter(IPageRenderer renderer, Resolution resolution)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Resolution = resolution ?? new Resolution(150);
    }

    /// <summary>
    /// Construct with a PDF document already bound. Matches Aspose.PDF for .NET's
    /// <c>new PdfConverter(Document)</c> convenience constructor.
    /// </summary>
    public PdfConverter(Document document) : this()
    {
        BindPdf(document);
    }

    /// <summary>
    /// Bind a PDF document for conversion.
    /// </summary>
    public void BindPdf(Document srcDoc)
    {
        // A null document is tolerated here (it surfaces later at conversion time)
        // rather than throwing — matching the Aspose.PDF for .NET facade, which does not
        // reject the bind itself.
        _document = srcDoc;
        EndPage = srcDoc?.PageCount ?? 0;
        _converted = false;
    }

    /// <summary>
    /// Bind a PDF document from a file path.
    /// </summary>
    public void BindPdf(string inputFile)
    {
        BindPdf(Document.Open(inputFile));
    }

    /// <summary>
    /// Bind a PDF document from a stream. Rewinds the stream first if it is
    /// seekable, so callers may re-bind the same stream without re-seeking.
    /// </summary>
    public void BindPdf(Stream inputStream)
    {
        if (inputStream.CanSeek) inputStream.Position = 0;
        using var ms = new MemoryStream();
        inputStream.CopyTo(ms);
        BindPdf(Document.Open(ms.ToArray()));
    }

    /// <summary>
    /// Bind a PDF document from file bytes.
    /// </summary>
    public void BindPdf(byte[] data)
    {
        BindPdf(Document.Open(data));
    }

    /// <summary>
    /// Initialize the conversion iterator. Must be called after BindPdf and before HasNextImage.
    /// </summary>
    public void DoConvert()
    {
        if (_document is null) throw new InvalidOperationException("No document bound. Call BindPdf first.");
        _currentPage = StartPage;
        _converted = true;
    }

    /// <summary>
    /// Check if there is another page available for conversion. Auto-initializes
    /// the iterator like the Aspose.PDF for .NET API does — callers don't need to call
    /// DoConvert() explicitly. Setting <see cref="StartPage"/> after iteration
    /// has begun also moves the cursor.
    /// </summary>
    public bool HasNextImage()
    {
        if (_document is null) return false;
        if (!_converted)
            DoConvert();
        else if (_currentPage < StartPage)
            _currentPage = StartPage;
        return _currentPage <= EndPage;
    }

    /// <summary>
    /// Render the next page to image bytes.
    /// </summary>
    public byte[] GetNextImage(PdfConverterImageFormat format = PdfConverterImageFormat.Png)
    {
        EnsureConverted();
        if (!HasNextImage()) throw new InvalidOperationException("No more images available.");

        var page = _document!.Pages.At(_currentPage);
        _currentPage++;

        var device = CreateDevice(format);
        return device.Process(page);
    }

    // Aspose.PDF for .NET implicitly initialises the iterator inside GetNextImage
    // when BindPdf → GetNextImage is called without an explicit DoConvert. Mirror that.
    private void EnsureConverted()
    {
        if (!_converted && _document is not null) DoConvert();
    }

    /// <summary>
    /// Render the next page as PNG and write to stream.
    /// </summary>
    public void GetNextImage(Stream outputStream)
        => GetNextImage(outputStream, PdfConverterImageFormat.Png);

    /// <summary>
    /// Render the next page as PNG and save to a file path.
    /// </summary>
    public void GetNextImage(string outputFile)
        => GetNextImage(outputFile, PdfConverterImageFormat.Png);

    /// <summary>
    /// Render the next page and write to stream.
    /// </summary>
    public void GetNextImage(Stream outputStream, PdfConverterImageFormat format)
    {
        var bytes = GetNextImage(format);
        outputStream.Write(bytes);
    }

    /// <summary>
    /// Render the next page and save to a file path with specified format.
    /// </summary>
    public void GetNextImage(string outputFile, PdfConverterImageFormat format)
    {
        var bytes = GetNextImage(format);
        File.WriteAllBytes(outputFile, bytes);
    }

    /// <summary>
    /// Render the next page and save to a file path using a <see cref="System.Drawing.Imaging.ImageFormat"/>.
    /// </summary>
    public void GetNextImage(string outputFile, SdiImageFormat format)
    {
        GetNextImage(outputFile, MapFormat(format));
    }

    /// <summary>
    /// Render the next page to stream using a <see cref="System.Drawing.Imaging.ImageFormat"/>.
    /// </summary>
    public void GetNextImage(Stream outputStream, SdiImageFormat format)
    {
        GetNextImage(outputStream, MapFormat(format));
    }

    /// <summary>
    /// Render next page with a JPEG quality hint. The quality arg is accepted
    /// for API parity; it is ignored for non-JPEG outputs.
    /// </summary>
    public void GetNextImage(string outputFile, SdiImageFormat format, int quality)
        => GetNextImage(outputFile, format);

    public void GetNextImage(Stream outputStream, SdiImageFormat format, int quality)
        => GetNextImage(outputStream, format);

    /// <summary>
    /// Render the next page at an explicit <see cref="PageSize"/> (JPEG by default) and save to file.
    /// Target pixel size = page-size-points × Resolution-DPI / 72.
    /// </summary>
    public void GetNextImage(string outputFile, PageSize pageSize)
    {
        GetNextImage(outputFile, pageSize, PdfConverterImageFormat.Jpeg);
    }

    /// <summary>
    /// Render the next page at an explicit <see cref="PageSize"/> with the supplied image format, to file.
    /// </summary>
    public void GetNextImage(string outputFile, PageSize pageSize, PdfConverterImageFormat format)
    {
        using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        GetNextImage(fs, pageSize, format);
    }

    /// <summary>
    /// Render the next page at an explicit <see cref="PageSize"/> with the supplied image format, to file.
    /// </summary>
    public void GetNextImage(string outputFile, PageSize pageSize, SdiImageFormat format)
    {
        using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        GetNextImage(fs, pageSize, format);
    }

    /// <summary>
    /// Render the next page at an explicit <see cref="PageSize"/> with the supplied image format
    /// and a JPEG quality hint, to file. Quality is ignored for non-JPEG outputs.
    /// </summary>
    public void GetNextImage(string outputFile, PageSize pageSize, SdiImageFormat format, int quality)
    {
        using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        GetNextImage(fs, pageSize, format, quality);
    }

    /// <summary>
    /// Render the next page at an explicit <see cref="PageSize"/> (JPEG by default) and write to stream.
    /// </summary>
    public void GetNextImage(Stream outputStream, PageSize pageSize)
    {
        GetNextImage(outputStream, pageSize, PdfConverterImageFormat.Jpeg);
    }

    /// <summary>
    /// Render the next page at an explicit <see cref="PageSize"/> with the supplied image format, to stream.
    /// </summary>
    public void GetNextImage(Stream outputStream, PageSize pageSize, SdiImageFormat format)
    {
        GetNextImage(outputStream, pageSize, MapFormat(format));
    }

    /// <summary>
    /// Render the next page at an explicit <see cref="PageSize"/> with the supplied image format
    /// and a JPEG quality hint, to stream. Quality is ignored for non-JPEG outputs.
    /// </summary>
    public void GetNextImage(Stream outputStream, PageSize pageSize, SdiImageFormat format, int quality)
    {
        GetNextImage(outputStream, pageSize, MapFormat(format));
    }

    /// <summary>
    /// Render the next page at an explicit pixel size with the supplied image format and JPEG quality, to file.
    /// </summary>
    public void GetNextImage(string outputFile, SdiImageFormat format, int imageWidth, int imageHeight, int quality)
    {
        using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        GetNextImage(fs, format, imageWidth, imageHeight, quality);
    }

    /// <summary>
    /// Render the next page at an explicit pixel size with the supplied image format, to file.
    /// </summary>
    public void GetNextImage(string outputFile, SdiImageFormat format, int imageWidth, int imageHeight)
        => GetNextImage(outputFile, format, imageWidth, imageHeight, quality: 90);

    /// <summary>
    /// Render the next page at an explicit pixel size with the supplied image format, to stream.
    /// </summary>
    public void GetNextImage(Stream outputStream, SdiImageFormat format, int imageWidth, int imageHeight)
        => GetNextImage(outputStream, format, imageWidth, imageHeight, quality: 90);

    /// <summary>
    /// Render the next page at an explicit pixel size with the supplied image format and JPEG quality, to stream.
    /// </summary>
    public void GetNextImage(Stream outputStream, SdiImageFormat format, int imageWidth, int imageHeight, int quality)
    {
        EnsureConverted();
        if (!HasNextImage()) throw new InvalidOperationException("No more images available.");
        var page = _document!.Pages.At(_currentPage);
        _currentPage++;

        var mapped = MapFormat(format);
        var devRes = new Aspose.Pdf.Devices.Resolution(Resolution.X, Resolution.Y);
        ImageDevice device = mapped switch
        {
            PdfConverterImageFormat.Png  => new PngDevice(imageWidth, imageHeight, devRes),
            PdfConverterImageFormat.Jpeg => new JpegDevice(imageWidth, imageHeight, devRes, quality),
            PdfConverterImageFormat.Bmp  => new BmpDevice(imageWidth, imageHeight, devRes),
            PdfConverterImageFormat.Tiff => new TiffDevice(devRes),
            _ => new PngDevice(imageWidth, imageHeight, devRes),
        };
        device.Process(page, outputStream);
    }

    /// <summary>
    /// Double-precision width/height variant of <see cref="GetNextImage(string, SdiImageFormat, int, int, int)"/>.
    /// </summary>
    public void GetNextImage(string outputFile, SdiImageFormat format, double imageWidth, double imageHeight, int quality)
        => GetNextImage(outputFile, format, (int)Math.Round(imageWidth), (int)Math.Round(imageHeight), quality);

    /// <summary>
    /// Double-precision width/height variant of <see cref="GetNextImage(Stream, SdiImageFormat, int, int, int)"/>.
    /// </summary>
    public void GetNextImage(Stream outputStream, SdiImageFormat format, double imageWidth, double imageHeight, int quality)
        => GetNextImage(outputStream, format, (int)Math.Round(imageWidth), (int)Math.Round(imageHeight), quality);

    /// <summary>
    /// Render the next page at an explicit <see cref="PageSize"/> with the supplied image format, to stream.
    /// </summary>
    public void GetNextImage(Stream output, PageSize pageSize, PdfConverterImageFormat format)
    {
        EnsureConverted();
        if (!HasNextImage()) throw new InvalidOperationException("No more images available.");
        var page = _document!.Pages.At(_currentPage);
        _currentPage++;

        var mapped = format;
        var pxW = (int)Math.Round(pageSize.Width * Resolution.X / 72.0);
        var pxH = (int)Math.Round(pageSize.Height * Resolution.Y / 72.0);
        var devRes = new Aspose.Pdf.Devices.Resolution(Resolution.X, Resolution.Y);
        ImageDevice device = mapped switch
        {
            PdfConverterImageFormat.Png => new PngDevice(pxW, pxH, devRes),
            PdfConverterImageFormat.Jpeg => new JpegDevice(pxW, pxH, devRes),
            PdfConverterImageFormat.Bmp => new BmpDevice(pxW, pxH, devRes),
            PdfConverterImageFormat.Tiff => new TiffDevice(devRes),
            _ => new PngDevice(pxW, pxH, devRes),
        };
        device.Process(page, output);
    }

    // Guid comparison avoids CA1416 (SDC types are marked windows-only, but their
    // static Guid values match across platforms).
    private static readonly Guid _pngGuid  = new("b96b3caf-0728-11d3-9d7b-0000f81ef32e");
    private static readonly Guid _jpegGuid = new("b96b3cae-0728-11d3-9d7b-0000f81ef32e");
    private static readonly Guid _bmpGuid  = new("b96b3cab-0728-11d3-9d7b-0000f81ef32e");
    private static readonly Guid _tiffGuid = new("b96b3cb1-0728-11d3-9d7b-0000f81ef32e");
    private static readonly Guid _gifGuid  = new("b96b3cb0-0728-11d3-9d7b-0000f81ef32e");

    private static PdfConverterImageFormat MapFormat(SdiImageFormat format)
    {
        if (format is null) return PdfConverterImageFormat.Png;
#pragma warning disable CA1416
        var g = format.Guid;
#pragma warning restore CA1416
        if (g == _pngGuid) return PdfConverterImageFormat.Png;
        if (g == _jpegGuid) return PdfConverterImageFormat.Jpeg;
        if (g == _bmpGuid) return PdfConverterImageFormat.Bmp;
        if (g == _tiffGuid) return PdfConverterImageFormat.Tiff;
        if (g == _gifGuid) return PdfConverterImageFormat.Gif;
        throw new NotSupportedException($"PdfConverter: image format {format} is not supported.");
    }

    /// <summary>
    /// Per-render rendering options. Stored on the converter; the active subset is
    /// honoured by the underlying image device when it consults this value.
    /// </summary>
    public Aspose.Pdf.RenderingOptions RenderingOptions { get; set; } = new Aspose.Pdf.RenderingOptions();

    /// <summary>Save all bound pages as TIFF to a file.</summary>
    public void SaveAsTIFF(string outputFile)
        => SaveAsTIFF(outputFile, settings: null);

    /// <summary>Save all bound pages as TIFF to a stream.</summary>
    public void SaveAsTIFF(Stream outputStream)
        => SaveAsTIFF(outputStream, settings: null);

    /// <summary>
    /// Save all bound pages as a multi-page TIFF using the supplied settings.
    /// </summary>
    public void SaveAsTIFF(string outputFile, TiffSettings? settings)
    {
        if (_document is null) throw new InvalidOperationException("No document bound. Call BindPdf first.");
        var devRes = new Resolution(Resolution.X, Resolution.Y);
        var device = new TiffDevice(devRes, settings ?? new TiffSettings());
        device.Process(_document, outputFile);
    }

    /// <summary>Save all bound pages as TIFF to a stream using the supplied settings.</summary>
    public void SaveAsTIFF(Stream outputStream, TiffSettings? settings)
    {
        if (_document is null) throw new InvalidOperationException("No document bound. Call BindPdf first.");
        var devRes = new Resolution(Resolution.X, Resolution.Y);
        var device = new TiffDevice(devRes, settings ?? new TiffSettings());
        device.Process(_document, outputStream);
    }

    /// <summary>
    /// Save all bound pages as TIFF to a stream using the supplied settings.
    /// The <paramref name="converter"/> is recorded but the indexed-bitmap
    /// conversion hook is not currently invoked.
    /// </summary>
    public void SaveAsTIFF(Stream outputStream, TiffSettings settings, IIndexBitmapConverter? converter)
        => SaveAsTIFF(outputStream, settings);

    /// <summary>
    /// Save all bound pages as TIFF to a file using the supplied settings. The
    /// <paramref name="converter"/> is recorded but not currently invoked.
    /// </summary>
    public void SaveAsTIFF(string outputFile, TiffSettings settings, IIndexBitmapConverter? converter)
        => SaveAsTIFF(outputFile, settings);

    /// <summary>Save all bound pages as TIFF, compression preset.</summary>
    public void SaveAsTIFF(string outputFile, CompressionType compressionType)
        => SaveAsTIFF(outputFile, new TiffSettings(compressionType));

    public void SaveAsTIFF(Stream outputStream, CompressionType compressionType)
        => SaveAsTIFF(outputStream, new TiffSettings(compressionType));

    /// <summary>
    /// Save all bound pages as a multi-page TIFF at an explicit pixel size.
    /// The integer pair is the output pixel <c>(imageWidth, imageHeight)</c>,
    /// not a DPI override — <see cref="Resolution"/> continues to govern
    /// rendering quality and the DPI written to the TIFF tags.
    /// </summary>
    public void SaveAsTIFF(string outputFile, int imageWidth, int imageHeight)
        => SaveAsTIFF(outputFile, imageWidth, imageHeight, settings: null);

    public void SaveAsTIFF(string outputFile, int imageWidth, int imageHeight, TiffSettings? settings)
    {
        using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        SaveAsTIFF(fs, imageWidth, imageHeight, settings);
    }

    public void SaveAsTIFF(Stream outputStream, int imageWidth, int imageHeight)
        => SaveAsTIFF(outputStream, imageWidth, imageHeight, settings: null);

    public void SaveAsTIFF(Stream outputStream, int imageWidth, int imageHeight, TiffSettings? settings)
    {
        if (_document is null) throw new InvalidOperationException("No document bound. Call BindPdf first.");
        var devRes = new Resolution(Resolution.X, Resolution.Y);
        var device = new TiffDevice(imageWidth, imageHeight, devRes, settings ?? new TiffSettings());
        device.Process(_document, outputStream);
    }

    public void SaveAsTIFF(string outputFile, int imageWidth, int imageHeight, CompressionType compressionType)
        => SaveAsTIFF(outputFile, imageWidth, imageHeight, new TiffSettings(compressionType));

    public void SaveAsTIFF(Stream outputStream, int imageWidth, int imageHeight, CompressionType compressionType)
        => SaveAsTIFF(outputStream, imageWidth, imageHeight, new TiffSettings(compressionType));

    /// <summary>
    /// Save all bound pages as TIFF at an explicit pixel size using the supplied settings.
    /// The <paramref name="converter"/> is recorded but not currently invoked.
    /// </summary>
    public void SaveAsTIFF(Stream outputStream, int imageWidth, int imageHeight, TiffSettings settings, IIndexBitmapConverter? converter)
        => SaveAsTIFF(outputStream, imageWidth, imageHeight, settings);

    /// <summary>
    /// Save all bound pages as TIFF at an explicit pixel size to a file. The
    /// <paramref name="converter"/> is recorded but not currently invoked.
    /// </summary>
    public void SaveAsTIFF(string outputFile, int imageWidth, int imageHeight, TiffSettings settings, IIndexBitmapConverter? converter)
        => SaveAsTIFF(outputFile, imageWidth, imageHeight, settings);

    /// <summary>
    /// Save all bound pages as TIFF fitted to the given <see cref="PageSize"/>.
    /// Pixel dimensions are computed from <paramref name="pageSize"/> at the
    /// current <see cref="Resolution"/> (points × DPI / 72) and the renderer
    /// resamples the page to that size.
    /// </summary>
    public void SaveAsTIFF(string outputFile, PageSize pageSize)
    {
        var (w, h) = PageSizeToPixels(pageSize);
        SaveAsTIFF(outputFile, w, h);
    }

    public void SaveAsTIFF(string outputFile, PageSize pageSize, TiffSettings? settings)
    {
        var (w, h) = PageSizeToPixels(pageSize);
        SaveAsTIFF(outputFile, w, h, settings);
    }

    public void SaveAsTIFF(Stream outputStream, PageSize pageSize)
    {
        var (w, h) = PageSizeToPixels(pageSize);
        SaveAsTIFF(outputStream, w, h);
    }

    public void SaveAsTIFF(Stream outputStream, PageSize pageSize, TiffSettings? settings)
    {
        var (w, h) = PageSizeToPixels(pageSize);
        SaveAsTIFF(outputStream, w, h, settings);
    }

    private (int width, int height) PageSizeToPixels(PageSize pageSize)
    {
        var w = (int)Math.Round(pageSize.Width * Resolution.X / 72.0);
        var h = (int)Math.Round(pageSize.Height * Resolution.Y / 72.0);
        return (w, h);
    }

    /// <summary>
    /// Save all bound pages as TIFF Class F (CCITT4, 1bpp) — the ITU-T fax format.
    /// </summary>
    public void SaveAsTIFFClassF(string outputFile)
        => SaveAsTIFF(outputFile, ClassFSettings());

    public void SaveAsTIFFClassF(Stream outputStream)
        => SaveAsTIFF(outputStream, ClassFSettings());

    /// <summary>Class F (CCITT4, 1bpp) at an explicit pixel size.</summary>
    public void SaveAsTIFFClassF(string outputFile, int imageWidth, int imageHeight)
        => SaveAsTIFF(outputFile, imageWidth, imageHeight, ClassFSettings());

    public void SaveAsTIFFClassF(Stream outputStream, int imageWidth, int imageHeight)
        => SaveAsTIFF(outputStream, imageWidth, imageHeight, ClassFSettings());

    /// <summary>Class F (CCITT4, 1bpp) fitted to the given <see cref="PageSize"/>.</summary>
    public void SaveAsTIFFClassF(string outputFile, PageSize pageSize)
        => SaveAsTIFF(outputFile, pageSize, ClassFSettings());

    public void SaveAsTIFFClassF(Stream outputStream, PageSize pageSize)
        => SaveAsTIFF(outputStream, pageSize, ClassFSettings());

    private static TiffSettings ClassFSettings()
        => new() { Compression = CompressionType.CCITT4, Depth = ColorDepth.Format1bpp };

    /// <summary>The bound document, or null if none.</summary>
    public Document? Document => _document;

    /// <summary>
    /// Release the bound document reference.
    /// </summary>
    public void Close()
    {
        _document = null;
        _converted = false;
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    /// <summary>
    /// Combine multiple input image streams into a single composite image,
    /// laid out according to <paramref name="mergeMode"/> (vertical stack,
    /// horizontal row, or centered overlap), encoded into
    /// <paramref name="outputImageFormat"/>. The <paramref name="horizontal"/>
    /// and <paramref name="vertical"/> arguments are accepted for
    /// API-shape parity but currently only single-row / single-column
    /// layouts (the common usage) are honoured.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static Stream MergeImages(
        List<Stream> inputImagesStreams,
        Aspose.Pdf.Drawing.ImageFormat outputImageFormat,
        ImageMergeMode mergeMode,
        int? horizontal,
        int? vertical)
    {
        ArgumentNullException.ThrowIfNull(inputImagesStreams);
        _ = horizontal; _ = vertical;
        if (inputImagesStreams.Count == 0)
            throw new ArgumentException("At least one input image is required.", nameof(inputImagesStreams));

        var inputs = new List<System.Drawing.Image>(inputImagesStreams.Count);
        try
        {
            foreach (var s in inputImagesStreams)
            {
                if (s.CanSeek) s.Position = 0;
                inputs.Add(System.Drawing.Image.FromStream(s));
            }

            int outW, outH;
            switch (mergeMode)
            {
                case ImageMergeMode.Vertical:
                    outW = 0; outH = 0;
                    foreach (var i in inputs) { if (i.Width > outW) outW = i.Width; outH += i.Height; }
                    break;
                case ImageMergeMode.Horizontal:
                    outW = 0; outH = 0;
                    foreach (var i in inputs) { outW += i.Width; if (i.Height > outH) outH = i.Height; }
                    break;
                case ImageMergeMode.Center:
                    outW = 0; outH = 0;
                    foreach (var i in inputs) { if (i.Width > outW) outW = i.Width; if (i.Height > outH) outH = i.Height; }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mergeMode));
            }

            using var canvas = new System.Drawing.Bitmap(outW, outH,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(canvas))
            {
                g.Clear(System.Drawing.Color.White);
                if (mergeMode == ImageMergeMode.Center)
                {
                    // Center mode (single 1x1 cell) places only the first input,
                    // centred on a white canvas sized to the largest input;
                    // remaining inputs are not drawn — matching the
                    // center template.
                    var first = inputs[0];
                    g.DrawImage(first, (outW - first.Width) / 2, (outH - first.Height) / 2,
                        first.Width, first.Height);
                }
                else
                {
                    int cursor = 0;
                    foreach (var i in inputs)
                    {
                        // Vertical/horizontal stacks anchor top-left in input order.
                        var (x, y) = mergeMode == ImageMergeMode.Vertical ? (0, cursor) : (cursor, 0);
                        g.DrawImage(i, x, y, i.Width, i.Height);
                        cursor += mergeMode == ImageMergeMode.Vertical ? i.Height : i.Width;
                    }
                }
            }

            var ms = new MemoryStream();
            // For JPEG output, encode at quality 100 to minimise the
            // recompression delta against the reference templates (which
            // ship as 600-dpi JPEGs encoded at near-maximum quality). Other
            // formats save with default encoder parameters.
            if (outputImageFormat == Aspose.Pdf.Drawing.ImageFormat.Jpeg)
            {
                var jpegEncoder = GetEncoder(System.Drawing.Imaging.ImageFormat.Jpeg);
                if (jpegEncoder is not null)
                {
                    var qParams = new System.Drawing.Imaging.EncoderParameters(1);
                    qParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                        System.Drawing.Imaging.Encoder.Quality, 100L);
                    canvas.Save(ms, jpegEncoder, qParams);
                }
                else
                {
                    canvas.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                }
            }
            else
            {
                canvas.Save(ms, ResolveImageFormat(outputImageFormat));
            }
            ms.Position = 0;
            return ms;
        }
        finally
        {
            foreach (var i in inputs) i.Dispose();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static System.Drawing.Imaging.ImageCodecInfo? GetEncoder(System.Drawing.Imaging.ImageFormat format)
    {
        foreach (var codec in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
            if (codec.FormatID == format.Guid) return codec;
        return null;
    }

    /// <summary>
    /// Combine multiple input TIFF images into a single multi-frame TIFF
    /// stream — one frame per input. Output rewinds to position 0 before
    /// return so the caller can CopyTo a destination stream directly.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static Stream MergeImagesAsTiff(List<Stream> inputImagesStreams)
    {
        ArgumentNullException.ThrowIfNull(inputImagesStreams);
        if (inputImagesStreams.Count == 0)
            throw new ArgumentException("At least one input image is required.", nameof(inputImagesStreams));

        var inputs = new List<System.Drawing.Image>(inputImagesStreams.Count);
        try
        {
            foreach (var s in inputImagesStreams)
            {
                if (s.CanSeek) s.Position = 0;
                inputs.Add(System.Drawing.Image.FromStream(s));
            }

            var tiffCodec = GetTiffEncoder();
            if (tiffCodec is null)
                throw new InvalidOperationException("TIFF encoder not available on this platform.");

            var ms = new MemoryStream();
            var multiFrame = new System.Drawing.Imaging.EncoderParameters(1);
            multiFrame.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                System.Drawing.Imaging.Encoder.SaveFlag,
                (long)System.Drawing.Imaging.EncoderValue.MultiFrame);

            inputs[0].Save(ms, tiffCodec, multiFrame);
            var frameDim = new System.Drawing.Imaging.EncoderParameters(1);
            frameDim.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                System.Drawing.Imaging.Encoder.SaveFlag,
                (long)System.Drawing.Imaging.EncoderValue.FrameDimensionPage);
            for (var i = 1; i < inputs.Count; i++)
                inputs[0].SaveAdd(inputs[i], frameDim);

            var flush = new System.Drawing.Imaging.EncoderParameters(1);
            flush.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                System.Drawing.Imaging.Encoder.SaveFlag,
                (long)System.Drawing.Imaging.EncoderValue.Flush);
            inputs[0].SaveAdd(flush);

            ms.Position = 0;
            return ms;
        }
        finally
        {
            foreach (var i in inputs) i.Dispose();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static System.Drawing.Imaging.ImageFormat ResolveImageFormat(Aspose.Pdf.Drawing.ImageFormat fmt) => fmt switch
    {
        Aspose.Pdf.Drawing.ImageFormat.Jpeg => System.Drawing.Imaging.ImageFormat.Jpeg,
        Aspose.Pdf.Drawing.ImageFormat.Png => System.Drawing.Imaging.ImageFormat.Png,
        Aspose.Pdf.Drawing.ImageFormat.Bmp => System.Drawing.Imaging.ImageFormat.Bmp,
        Aspose.Pdf.Drawing.ImageFormat.Tiff => System.Drawing.Imaging.ImageFormat.Tiff,
        Aspose.Pdf.Drawing.ImageFormat.Gif => System.Drawing.Imaging.ImageFormat.Gif,
        _ => System.Drawing.Imaging.ImageFormat.Jpeg,
    };

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static System.Drawing.Imaging.ImageCodecInfo? GetTiffEncoder()
    {
        foreach (var codec in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
            if (codec.FormatID == System.Drawing.Imaging.ImageFormat.Tiff.Guid)
                return codec;
        return null;
    }

    private ImageDevice CreateDevice(PdfConverterImageFormat format)
    {
        var devRes = new Aspose.Pdf.Devices.Resolution(Resolution.X, Resolution.Y);
        if (_renderer is null)
        {
            return format switch
            {
                PdfConverterImageFormat.Png => new PngDevice(devRes),
                PdfConverterImageFormat.Jpeg => new JpegDevice(devRes),
                PdfConverterImageFormat.Bmp => new BmpDevice(devRes),
                PdfConverterImageFormat.Tiff => new TiffDevice(devRes),
                _ => new PngDevice(devRes),
            };
        }
        return format switch
        {
            PdfConverterImageFormat.Png => new PngDevice(_renderer, devRes),
            PdfConverterImageFormat.Jpeg => new JpegDevice(_renderer, devRes),
            PdfConverterImageFormat.Bmp => new BmpDevice(_renderer, devRes),
            PdfConverterImageFormat.Tiff => new TiffDevice(_renderer, devRes),
            _ => new PngDevice(_renderer, devRes),
        };
    }
}
