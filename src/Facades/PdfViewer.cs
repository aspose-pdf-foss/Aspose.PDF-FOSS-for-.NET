#nullable disable

using System.Drawing;
using System.IO;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Printing;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Façade for viewing / printing a PDF document. Wraps a bound
/// <see cref="Aspose.Pdf.Document"/>; the configuration surface mirrors the
/// Aspose.PDF for .NET public API but printing methods reject calls because the FOSS
/// build does not depend on a print spooler. Page rasterisation
/// (<see cref="DecodePage"/>, <see cref="DecodeAllPages"/>) is left to
/// <see cref="ImageDevice"/> + SkiaSharp; the Bitmap-returning surface here
/// throws.
/// </summary>
public class PdfViewer : IFacade, System.IDisposable
{
    private Document _document;
    private bool _ownsDocument;

    public PdfViewer() { }

    public PdfViewer(Document document)
    {
        _document = document ?? throw new System.ArgumentNullException(nameof(document));
    }

    // ── Properties (auto-properties; configuration storage only) ────────────

    public bool AutoResize { get; set; }
    public bool AutoRotate { get; set; }
    public AutoRotateMode AutoRotateMode { get; set; }
    public PageCoordinateType CoordinateType { get; set; }
    public FormPresentationMode FormPresentationMode { get; set; }
    public HorizontalAlignment HorizontalAlignment { get; set; }
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Bottom;

    /// <summary>Number of pages in the bound document, or 0 when nothing is bound.</summary>
    public int PageCount => _document?.Pages.Count ?? 0;

    /// <summary>Owner password used when opening encrypted documents via
    /// <see cref="OpenPdfFile(string)"/>.</summary>
    public string Password { get; set; }

    public bool PrintAsGrayscale { get; set; }
    public bool PrintAsImage { get; set; }

    /// <summary>When true, <see cref="PrintDocumentWithSetup"/> would surface
    /// the OS print dialog. Stored only — printing is not implemented in this build.</summary>
    public bool PrintPageDialog { get; set; }

    /// <summary>Always returns null in this build; printing is not implemented.</summary>
    public object PrintStatus => null;

    public string PrinterJobName { get; set; } = "Aspose.Pdf Print";

    public Aspose.Pdf.RenderingOptions RenderingOptions { get; set; }

    /// <summary>Target rasterisation DPI consulted by viewer-level rendering
    /// (the FOSS Decode methods throw, so this is currently configuration-only).</summary>
    public int Resolution { get; set; } = 150;

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

    public void BindPdf(Document srcDoc)
    {
        ReleaseOwnedDocument();
        _document = srcDoc ?? throw new System.ArgumentNullException(nameof(srcDoc));
        _ownsDocument = false;
    }

    public void BindPdf(string srcFile)
    {
        ReleaseOwnedDocument();
        _document = string.IsNullOrEmpty(Password)
            ? Document.Open(srcFile)
            : Document.Open(srcFile, Password);
        _ownsDocument = true;
    }

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

    public void Close() => ReleaseOwnedDocument();
    public void ClosePdfFile() => Close();
    public void Dispose() => Close();

    // ── Save (passes through to the bound document) ────────────────────────

    public void Save(string destFile)
    {
        EnsureBound();
        _document.Save(destFile);
    }

    public void Save(Stream destStream)
    {
        EnsureBound();
        _document.Save(destStream);
    }

    // ── Page rasterisation (not implemented in FOSS) ───────────────────────

    /// <summary>Always throws: FOSS does not produce <see cref="Bitmap"/>
    /// objects from the viewer; render via <see cref="ImageDevice"/>
    /// (SkiaSharp-backed) instead.</summary>
    public Bitmap DecodePage(int pageNumber)
    {
        _ = pageNumber;
        throw new System.NotImplementedException(
            "PdfViewer.DecodePage is not implemented in FOSS. Use Aspose.Pdf.Devices.ImageDevice.");
    }

    /// <summary>Always throws: see <see cref="DecodePage(int)"/>.</summary>
    public Bitmap[] DecodeAllPages()
        => throw new System.NotImplementedException(
            "PdfViewer.DecodeAllPages is not implemented in FOSS. Use Aspose.Pdf.Devices.ImageDevice.");

    // ── Print methods (not implemented in FOSS) ────────────────────────────

    /// <summary>Returns a fresh, empty <see cref="PageSettings"/>. The FOSS
    /// build does not query the OS print spooler.</summary>
    public PageSettings GetDefaultPageSettings() => new();

    /// <summary>Returns a fresh, empty <see cref="PrinterSettings"/>. The FOSS
    /// build does not query the OS print spooler.</summary>
    public PrinterSettings GetDefaultPrinterSettings() => new();

    public void PrintDocument() => ThrowPrintingNotSupported();
    public void PrintDocumentWithSettings(PrinterSettings printerSettings)
    { _ = printerSettings; ThrowPrintingNotSupported(); }
    public void PrintDocumentWithSettings(PageSettings pageSettings, PrinterSettings printerSettings)
    { _ = pageSettings; _ = printerSettings; ThrowPrintingNotSupported(); }
    public void PrintDocumentWithSetup() => ThrowPrintingNotSupported();

    public void PrintDocuments(Document[] documents)
    { _ = documents; ThrowPrintingNotSupported(); }
    public void PrintDocuments(string[] filePaths)
    { _ = filePaths; ThrowPrintingNotSupported(); }
    public void PrintDocuments(Stream[] documentStreams)
    { _ = documentStreams; ThrowPrintingNotSupported(); }
    public void PrintDocuments(PrinterSettings printerSettings, Document[] documents)
    { _ = printerSettings; _ = documents; ThrowPrintingNotSupported(); }
    public void PrintDocuments(PrinterSettings printerSettings, string[] filePaths)
    { _ = printerSettings; _ = filePaths; ThrowPrintingNotSupported(); }
    public void PrintDocuments(PrinterSettings printerSettings, Stream[] documentStreams)
    { _ = printerSettings; _ = documentStreams; ThrowPrintingNotSupported(); }
    public void PrintDocuments(PrinterSettings printerSettings, PageSettings pageSettings, Document[] documents)
    { _ = printerSettings; _ = pageSettings; _ = documents; ThrowPrintingNotSupported(); }
    public void PrintDocuments(PrinterSettings printerSettings, PageSettings pageSettings, string[] filePaths)
    { _ = printerSettings; _ = pageSettings; _ = filePaths; ThrowPrintingNotSupported(); }
    public void PrintDocuments(PrinterSettings printerSettings, PageSettings pageSettings, Stream[] documentStreams)
    { _ = printerSettings; _ = pageSettings; _ = documentStreams; ThrowPrintingNotSupported(); }

    public void PrintLargePdf(string filePath)
    { _ = filePath; ThrowPrintingNotSupported(); }
    public void PrintLargePdf(Stream inputStream)
    { _ = inputStream; ThrowPrintingNotSupported(); }
    public void PrintLargePdf(string filePath, PrinterSettings printerSettings)
    { _ = filePath; _ = printerSettings; ThrowPrintingNotSupported(); }
    public void PrintLargePdf(Stream inputStream, PrinterSettings printerSettings)
    { _ = inputStream; _ = printerSettings; ThrowPrintingNotSupported(); }
    public void PrintLargePdf(string filePath, PageSettings pageSettings, PrinterSettings printerSettings)
    { _ = filePath; _ = pageSettings; _ = printerSettings; ThrowPrintingNotSupported(); }
    public void PrintLargePdf(Stream inputStream, PageSettings pageSettings, PrinterSettings printerSettings)
    { _ = inputStream; _ = pageSettings; _ = printerSettings; ThrowPrintingNotSupported(); }

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

    private static void ThrowPrintingNotSupported()
        => throw new System.PlatformNotSupportedException(
            "PdfViewer printing is not implemented in FOSS. Use Aspose.PDF for .NET for spooler-backed printing.");

    // Keep the compiler from complaining about unused events (no FOSS code raises them).
    private void _suppressEventWarnings()
    {
        CustomPrint?.Invoke(this, null);
        EndPage?.Invoke(this, null);
        EndPrint?.Invoke(this, null);
        PdfQueryPageSettings?.Invoke(this, null!, null!);
        StartPage?.Invoke(this, null);
    }
}
