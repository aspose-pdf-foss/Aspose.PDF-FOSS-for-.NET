#nullable disable

using System.Drawing;
using System.IO;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Printing;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Façade for viewing and printing a PDF document. Wraps a bound
/// <see cref="Aspose.Pdf.Document"/>. Page rasterisation
/// (<see cref="DecodePage"/>, <see cref="DecodeAllPages"/>) renders through
/// the same device pipeline as <see cref="ImageDevice"/>, and the print methods
/// hand those rendered pages to an installed printer - see the printing half of
/// this class for what a job does with them.
/// </summary>
public partial class PdfViewer : IFacade, System.IDisposable
{
    private Document _document;
    private bool _ownsDocument;

    /// <summary>Creates a viewer with no document bound; call <c>BindPdf</c> before printing or rendering.</summary>
    public PdfViewer() { }

    /// <summary>Creates a viewer bound to an already opened document, which the viewer does not dispose.</summary>
    public PdfViewer(Document document)
    {
        _document = document ?? throw new System.ArgumentNullException(nameof(document));
    }

    // ── Properties (auto-properties; configuration storage only) ────────────

    /// <summary>Gets or sets whether a printed page is scaled to fit the sheet (then multiplied by <c>ScaleFactor</c>). Default false.</summary>
    public bool AutoResize { get; set; }

    /// <summary>
    /// Turn a page whose orientation disagrees with the sheet's. One setting with
    /// <see cref="AutoRotateMode"/>, as in the reference: switching it on turns pages
    /// counter-clockwise unless a direction is already chosen, and switching it off clears the
    /// direction.
    /// </summary>
    public bool AutoRotate
    {
        get => AutoRotateMode != AutoRotateMode.None;
        set => AutoRotateMode = !value ? AutoRotateMode.None
            : AutoRotateMode == AutoRotateMode.None ? AutoRotateMode.AntiClockWise
            : AutoRotateMode;
    }

    /// <summary>Which way a page is turned onto a sheet of the other orientation;
    /// <see cref="AutoRotateMode.None"/> leaves it as it is.</summary>
    public AutoRotateMode AutoRotateMode { get; set; }
    /// <summary>Gets or sets the page box used for printing. Stored only; has no effect in this library.</summary>
    public PageCoordinateType CoordinateType { get; set; }
    /// <summary>Gets or sets how form fields are presented when printing. Stored only; has no effect in this library.</summary>
    public FormPresentationMode FormPresentationMode { get; set; }
    /// <summary>Gets or sets where a printed page that does not fill the sheet sits horizontally (left, center or right).</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; }
    /// <summary>Gets or sets where a printed page that does not fill the sheet sits vertically (top, center or bottom).</summary>
    public VerticalAlignment VerticalAlignment { get; set; }

    /// <summary>Number of pages in the bound document, or 0 when nothing is bound.</summary>
    public int PageCount => _document?.Pages.Count ?? 0;

    /// <summary>Owner password used when opening encrypted documents via
    /// <see cref="OpenPdfFile(string)"/>.</summary>
    public string Password { get; set; }

    /// <summary>Gets or sets whether pages print in gray ink only; such pages are sent to the printer as images. Default false.</summary>
    public bool PrintAsGrayscale { get; set; }
    /// <summary>Gets or sets whether each page is sent to the printer as an image rendered at <c>Resolution</c> DPI instead of as drawing commands. Default false.</summary>
    public bool PrintAsImage { get; set; }

    /// <summary>When true, <see cref="PrintDocumentWithSetup"/> surfaces the OS print
    /// dialog. Stored only — this build has no windowing dependency to raise one with,
    /// so a job runs with the settings it was given either way.</summary>
    public bool PrintPageDialog { get; set; }

    /// <summary>The failure that ended the last print job, or null when it finished.</summary>
    public object PrintStatus => _printStatus;

    /// <summary>Gets or sets the job name shown in the printer queue. Default <c>Aspose.PDF FOSS Print</c>.</summary>
    public string PrinterJobName { get; set; } = "Aspose.PDF FOSS Print";

    /// <summary>Rendering options applied to every page this viewer rasterises, for
    /// <see cref="DecodePage"/> and for print jobs alike. Never null by default, as on
    /// <see cref="PdfConverter"/> and <see cref="ImageDevice"/>: callers set a flag on it in
    /// place rather than assigning a fresh instance.</summary>
    public Aspose.Pdf.RenderingOptions RenderingOptions { get; set; } = new Aspose.Pdf.RenderingOptions();

    /// <summary>Target rasterisation DPI used by <see cref="DecodePage"/> /
    /// <see cref="DecodeAllPages"/>.</summary>
    public int Resolution { get; set; } = 150;

    /// <summary>Gets or sets the scale applied to each printed page; values of 0 or less mean 1. Default 1.</summary>
    public float ScaleFactor { get; set; } = 1f;
    public bool ShowHiddenAreas { get; set; }
    public bool UseIntermidiateImage { get; set; }

    // ── Events ──────────────────────────────────────────────────────────────

    public event System.EventHandler<CustomPrintEventArgs> CustomPrint;
    public event System.EventHandler<StartEndPageEventArgs> EndPage;
    public event System.ComponentModel.CancelEventHandler EndPrint;
    public event PdfQueryPageSettingsEventHandler PdfQueryPageSettings;
    public event System.EventHandler<StartEndPageEventArgs> StartPage;

    // ── Lifecycle / file binding ────────────────────────────────────────────

    /// <summary>Binds an already opened document, which the viewer does not dispose. Throws when the document is null.</summary>
    public void BindPdf(Document srcDoc)
    {
        ReleaseOwnedDocument();
        _document = srcDoc ?? throw new System.ArgumentNullException(nameof(srcDoc));
        _ownsDocument = false;
    }

    /// <summary>Opens the PDF file at the given path (using <c>Password</c> when set) and binds it.</summary>
    public void BindPdf(string srcFile)
    {
        ReleaseOwnedDocument();
        _document = string.IsNullOrEmpty(Password)
            ? Document.Open(srcFile)
            : Document.Open(srcFile, Password);
        _ownsDocument = true;
    }

    /// <summary>Reads the whole stream (from the start when it is seekable), opens it using <c>Password</c> when set, and binds it.</summary>
    public void BindPdf(Stream srcStream)
    {
        ReleaseOwnedDocument();
        using var ms = new MemoryStream();
        if (srcStream.CanSeek) srcStream.Position = 0;
        srcStream.CopyTo(ms);
        _document = string.IsNullOrEmpty(Password)
            ? Document.Open(ms.ToArray())
            : Document.Open(ms.ToArray(), Password);
        _ownsDocument = true;
    }

    public void OpenPdfFile(string filePath) => BindPdf(filePath);
    public void OpenPdfFile(Stream inputStream) => BindPdf(inputStream);

    /// <summary>Releases the bound document; it is disposed only when the viewer opened it itself.</summary>
    public void Close() => ReleaseOwnedDocument();
    public void ClosePdfFile() => Close();
    public void Dispose() => Close();

    // ── Save (passes through to the bound document) ────────────────────────

    /// <summary>Saves the bound document to the given file path.</summary>
    public void Save(string destFile)
    {
        EnsureBound();
        _document.Save(destFile);
    }

    /// <summary>Saves the bound document to the given stream.</summary>
    public void Save(Stream destStream)
    {
        EnsureBound();
        _document.Save(destStream);
    }

    // ── Page rasterisation ─────────────────────────────────────────────────

    /// <summary>Renders one page (1-based) to a <see cref="Bitmap"/> at
    /// <see cref="Resolution"/> DPI.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public Bitmap DecodePage(int pageNumber)
    {
        EnsureBound();
        return RenderPage(_document, pageNumber, Resolution);
    }

    /// <summary>Renders one page (1-based) of <paramref name="document"/> at
    /// <paramref name="dpi"/>, with this viewer's rendering options.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal Bitmap RenderPage(Document document, int pageNumber, int dpi)
    {
        if (pageNumber < 1 || pageNumber > document.Pages.Count)
            throw new System.ArgumentOutOfRangeException(nameof(pageNumber));

        var device = new BmpDevice(new Devices.Resolution(dpi));
        if (RenderingOptions is not null) device.RenderingOptions = RenderingOptions;
        using var ms = new MemoryStream();
        device.Process(document.Pages[pageNumber], ms);
        ms.Position = 0;
        // Copy out of the stream-backed image: GDI+ requires the source stream
        // to outlive a Bitmap decoded from it, and callers own the result.
        using var decoded = new Bitmap(ms);
        var result = new Bitmap(decoded);
        // The Bitmap(Image) copy resets DPI metadata to the screen default;
        // restore the requested rasterisation resolution.
        result.SetResolution(dpi, dpi);
        return result;
    }

    /// <summary>
    /// Renders one page (1-based) of <paramref name="document"/> at <paramref name="dpi"/> for a
    /// printer, with this viewer's rendering options, as the reference renders a page it prints as
    /// an image: opaque, on white paper.
    /// </summary>
    /// <remarks>
    /// The reference's printed page image is its own PNG render of the page, byte for byte, alpha
    /// 255 throughout. A transparent render, cropped by the XPS writer to what the page paints,
    /// measures the same once the page reaches the writer as a PNG (a fallback page 112 pixels
    /// outside its template's match window transparent, 111 opaque).
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal Bitmap RenderPageForPrint(Document document, int pageNumber, int dpi)
    {
        if (pageNumber < 1 || pageNumber > document.Pages.Count)
            throw new System.ArgumentOutOfRangeException(nameof(pageNumber));

        var renderer = new GdiPlusPageRenderer
        {
            DefaultFontName = RenderingOptions?.DefaultFontName,
            AliasedVectorFills = RenderingOptions?.BarcodeOptimization ?? false,
            ConvertFontsToUnicodeTtf = RenderingOptions?.ConvertFontsToUnicodeTTF ?? false,
            PrintedPageImage = true,
        };
        var rgba = renderer.RenderPage(document.Pages[pageNumber], dpi);
        var bitmap = new Bitmap(rgba.Width, rgba.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bits = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, rgba.Width, rgba.Height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[rgba.Width * 4];
            for (var y = 0; y < rgba.Height; y++)
            {
                // RGBA to GDI+'s BGRA byte order.
                System.Array.Copy(rgba.Data, y * row.Length, row, 0, row.Length);
                for (var x = 0; x < row.Length; x += 4)
                    (row[x], row[x + 2]) = (row[x + 2], row[x]);
                System.Runtime.InteropServices.Marshal.Copy(row, 0, System.IntPtr.Add(bits.Scan0, y * bits.Stride), row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }
        bitmap.SetResolution(dpi, dpi);
        return bitmap;
    }

    /// <summary>Draws one page (1-based) of <paramref name="document"/> as drawing commands into
    /// <paramref name="destination"/> on a printer surface, with this viewer's rendering options,
    /// laid out as a render at <paramref name="dpi"/>. False, with nothing drawn, when the page
    /// has to be printed as an image: it holds content composited from pixels, or the options ask
    /// for a render that only exists in pixels.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal bool DrawPage(Document document, int pageNumber, Graphics graphics, RectangleF destination, int dpi)
    {
        if (pageNumber < 1 || pageNumber > document.Pages.Count)
            throw new System.ArgumentOutOfRangeException(nameof(pageNumber));
        if (RenderingOptions?.BarcodeOptimization == true) return false;

        var renderer = new GdiPlusPageRenderer
        {
            DefaultFontName = RenderingOptions?.DefaultFontName,
            ConvertFontsToUnicodeTtf = RenderingOptions?.ConvertFontsToUnicodeTTF ?? false,
        };
        return renderer.TryRenderPageToGraphics(document.Pages[pageNumber], graphics, destination, dpi);
    }

    /// <summary>Renders one page (1-based) at <see cref="Resolution"/> DPI and returns it as an
    /// image encoded in <paramref name="imageFormat"/> - a PNG by default. The encoded form is
    /// the point: the image is decoded back from that format, so a caller saving it again, or
    /// handing it to a print job, sees exactly what that format keeps, resolution included.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public System.Drawing.Image DecodePageToImage(int pageNumber, System.Drawing.Imaging.ImageFormat imageFormat)
    {
        using var page = DecodePage(pageNumber);
        // Image.FromStream reads lazily from the stream it is given, so the stream has to live as
        // long as the image does; it is left to the image rather than disposed here.
        var encoded = new MemoryStream();
        page.Save(encoded, imageFormat ?? System.Drawing.Imaging.ImageFormat.Png);
        encoded.Position = 0;
        return System.Drawing.Image.FromStream(encoded);
    }

    /// <summary>Renders every page to a <see cref="Bitmap"/>; see
    /// <see cref="DecodePage(int)"/>.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public Bitmap[] DecodeAllPages()
    {
        EnsureBound();
        var pages = new Bitmap[_document.Pages.Count];
        for (var i = 0; i < pages.Length; i++)
            pages[i] = DecodePage(i + 1);
        return pages;
    }


    // ── helpers ─────────────────────────────────────────────────────────────

    private void EnsureBound()
    {
        if (_document is null)
            throw new System.InvalidOperationException(
                "PdfViewer is not bound to a document. Call BindPdf or OpenPdfFile first.");
    }

    private void ReleaseOwnedDocument()
    {
        if (_ownsDocument && _document is not null)
            _document.Dispose();
        _document = null;
        _ownsDocument = false;
    }

}
