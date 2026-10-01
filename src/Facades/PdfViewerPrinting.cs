#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Aspose.Pdf.Printing;

namespace Aspose.Pdf.Facades;

// The printing half of <see cref="PdfViewer"/>: a job draws each page of the bound document
// onto an installed printer through the same renderer <see cref="PdfViewer.DecodePage"/> uses,
// honouring the paper, margins, orientation and scale the settings ask for.
/// <remarks>
/// A page reaches the printer as drawing commands, as the reference sends it. It is printed as a
/// rendered image instead when <see cref="PdfViewer.PrintAsImage"/> asks for one - at
/// <see cref="PdfViewer.Resolution"/> DPI - when <see cref="PdfViewer.PrintAsGrayscale"/> is set,
/// or when the page holds content composited from pixels, which a printer never receives; that
/// image is rendered at <see cref="PdfViewer.Resolution"/> DPI as well, with bare paper left
/// transparent. <see cref="PdfViewer.PrintPageDialog"/>
/// raises no dialog, because a library with no windowing dependency has no window to raise one
/// over.
/// </remarks>
public partial class PdfViewer
{
    private object _printStatus;

    /// <summary>How many copies the last job printed. The job prints every copy itself, so this is
    /// a count of what it did rather than a request passed on to a driver; zero until a job has
    /// finished.</summary>
    internal int CopiesPrinted { get; private set; }

    /// <summary>
    /// Page settings for a job on the default printer: bound to <see cref="GetDefaultPrinterSettings"/>
    /// and carrying its zero margins, with the paper left to the printer settings.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public PageSettings GetDefaultPageSettings() => new(GetDefaultPrinterSettings());

    /// <summary>
    /// Printer settings for a job on the default printer, covering every page of the bound document.
    /// </summary>
    /// <remarks>
    /// These are blank settings, not the installed printer's own, as the reference's are: no printer
    /// name (the job goes to whichever printer is the default), the paper left to that printer, and
    /// margins of zero that a job honours - the reference prints a page given these settings edge to
    /// edge at its own size, where the default printer's one-inch margins would push it off the sheet.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public PrinterSettings GetDefaultPrinterSettings()
    {
        var settings = new PrinterSettings { FromPage = 1, ToPage = PageCount };
        settings.DefaultPageSettings.Margins = new Aspose.Pdf.Devices.Margins(0, 0, 0, 0);
        return settings;
    }

    /// <summary>Prints the bound document to the default printer.</summary>
    [SupportedOSPlatform("windows")]
    public void PrintDocument() => PrintDocumentWithSettings(null, null);

    /// <summary>Prints the bound document with the given printer settings.</summary>
    [SupportedOSPlatform("windows")]
    public void PrintDocumentWithSettings(PrinterSettings printerSettings)
        => PrintDocumentWithSettings(null, printerSettings);

    /// <summary>Prints the bound document with the given page and printer settings.</summary>
    [SupportedOSPlatform("windows")]
    public void PrintDocumentWithSettings(PageSettings pageSettings, PrinterSettings printerSettings)
    {
        EnsureBound();
        if (TryPrintToPdfFile(_document, printerSettings)) return;
        Print(new[] { _document }, pageSettings, printerSettings);
    }

    /// <summary>Prints the bound document with its current settings. The print dialog
    /// <see cref="PrintPageDialog"/> asks for is not raised; see the class remarks.</summary>
    [SupportedOSPlatform("windows")]
    public void PrintDocumentWithSetup() => PrintDocument();

    /// <summary>Prints several documents as one job on the default printer.</summary>
    [SupportedOSPlatform("windows")]
    public static void PrintDocuments(params Document[] documents)
        => PrintDocuments(null, null, documents);

    /// <summary>Opens the PDF files at the given paths and prints them as one job on the default printer (Windows only).</summary>
    [SupportedOSPlatform("windows")]
    public static void PrintDocuments(params string[] filePaths)
        => PrintDocuments(null, null, filePaths);

    /// <summary>Opens the PDF documents in the given streams and prints them as one job on the default printer (Windows only).</summary>
    [SupportedOSPlatform("windows")]
    public static void PrintDocuments(params Stream[] documentStreams)
        => PrintDocuments(null, null, documentStreams);

    /// <summary>Prints several documents as one job with the given printer settings (Windows only).</summary>
    [SupportedOSPlatform("windows")]
    public static void PrintDocuments(PrinterSettings printerSettings, params Document[] documents)
        => PrintDocuments(printerSettings, null, documents);

    /// <summary>Opens the PDF files at the given paths and prints them as one job with the given printer settings (Windows only).</summary>
    [SupportedOSPlatform("windows")]
    public static void PrintDocuments(PrinterSettings printerSettings, params string[] filePaths)
        => PrintDocuments(printerSettings, null, filePaths);

    /// <summary>Opens the PDF documents in the given streams and prints them as one job with the given printer settings (Windows only).</summary>
    [SupportedOSPlatform("windows")]
    public static void PrintDocuments(PrinterSettings printerSettings, params Stream[] documentStreams)
        => PrintDocuments(printerSettings, null, documentStreams);

    /// <summary>Prints several documents as one job with the given printer and page settings (Windows only). Throws when the array is null.</summary>
    [SupportedOSPlatform("windows")]
    public static void PrintDocuments(
        PrinterSettings printerSettings, PageSettings pageSettings, params Document[] documents)
    {
        if (documents is null) throw new ArgumentNullException(nameof(documents));
        using var viewer = new PdfViewer();
        viewer.Print(documents, pageSettings, printerSettings);
    }

    /// <summary>Opens the PDF files at the given paths and prints them as one job with the given printer and page settings (Windows only).</summary>
    [SupportedOSPlatform("windows")]
    public static void PrintDocuments(
        PrinterSettings printerSettings, PageSettings pageSettings, params string[] filePaths)
    {
        if (filePaths is null) throw new ArgumentNullException(nameof(filePaths));
        PrintOpened(filePaths, Document.Open, printerSettings, pageSettings);
    }

    /// <summary>Opens the PDF documents in the given streams and prints them as one job with the given printer and page settings (Windows only).</summary>
    [SupportedOSPlatform("windows")]
    public static void PrintDocuments(
        PrinterSettings printerSettings, PageSettings pageSettings, params Stream[] documentStreams)
    {
        if (documentStreams is null) throw new ArgumentNullException(nameof(documentStreams));
        PrintOpened(documentStreams, OpenStream, printerSettings, pageSettings);
    }

    /// <summary>Prints a document the viewer is not bound to. The "large" name is the
    /// published one: the pages are rendered and released one at a time here as they are
    /// for any other job, so a long document costs one page of memory either way.</summary>
    [SupportedOSPlatform("windows")]
    public void PrintLargePdf(string filePath) => PrintLargePdf(filePath, null, null);

    /// <inheritdoc cref="PrintLargePdf(string)"/>
    [SupportedOSPlatform("windows")]
    public void PrintLargePdf(Stream inputStream) => PrintLargePdf(inputStream, null, null);

    /// <inheritdoc cref="PrintLargePdf(string)"/>
    [SupportedOSPlatform("windows")]
    public void PrintLargePdf(string filePath, PrinterSettings printerSettings)
        => PrintLargePdf(filePath, null, printerSettings);

    /// <inheritdoc cref="PrintLargePdf(string)"/>
    [SupportedOSPlatform("windows")]
    public void PrintLargePdf(Stream inputStream, PrinterSettings printerSettings)
        => PrintLargePdf(inputStream, null, printerSettings);

    /// <inheritdoc cref="PrintLargePdf(string)"/>
    [SupportedOSPlatform("windows")]
    public void PrintLargePdf(string filePath, PageSettings pageSettings, PrinterSettings printerSettings)
    {
        using var document = Document.Open(filePath);
        PrintUnbound(document, pageSettings, printerSettings);
    }

    /// <inheritdoc cref="PrintLargePdf(string)"/>
    [SupportedOSPlatform("windows")]
    public void PrintLargePdf(Stream inputStream, PageSettings pageSettings, PrinterSettings printerSettings)
    {
        using var document = OpenStream(inputStream);
        PrintUnbound(document, pageSettings, printerSettings);
    }

    // ── the job ─────────────────────────────────────────────────────────────

    /// <summary>Runs one job over the given documents, remembering any failure in
    /// <see cref="PrintStatus"/> before letting it out.</summary>
    [SupportedOSPlatform("windows")]
    private void Print(
        IEnumerable<Document> documents, PageSettings pageSettings, PrinterSettings printerSettings)
    {
        _printStatus = null;
        CopiesPrinted = 0;
        try
        {
            new PdfPrintJob(BuildPrintOptions(), documents, pageSettings, printerSettings).Run();
            CopiesPrinted = Math.Max(1, printerSettings?.Copies ?? 1);
        }
        catch (Exception e)
        {
            _printStatus = e;
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private void PrintUnbound(Document document, PageSettings pageSettings, PrinterSettings printerSettings)
    {
        if (TryPrintToPdfFile(document, printerSettings)) return;
        Print(new[] { document }, pageSettings, printerSettings);
    }

    /// <summary>Opens each source, prints them as one job, and closes them again.</summary>
    [SupportedOSPlatform("windows")]
    private static void PrintOpened<T>(
        IReadOnlyList<T> sources,
        Func<T, Document> open,
        PrinterSettings printerSettings,
        PageSettings pageSettings)
    {
        var documents = new List<Document>(sources.Count);
        try
        {
            foreach (var source in sources) documents.Add(open(source));
            PrintDocuments(printerSettings, pageSettings, documents.ToArray());
        }
        finally
        {
            foreach (var document in documents) document.Dispose();
        }
    }

    private static Document OpenStream(Stream stream)
    {
        using var buffer = new MemoryStream();
        if (stream.CanSeek) stream.Position = 0;
        stream.CopyTo(buffer);
        return Document.Open(buffer.ToArray());
    }

    /// <summary>
    /// A print-TO-FILE job with a .pdf target needs no spooler: the document a spooler
    /// would hand to "Microsoft Print to PDF" is reproduced directly — each requested
    /// copy of the page range lands in the target file as its own pages (2 copies of a
    /// 1-page document print as a 2-page PDF).
    /// </summary>
    /// <returns>True when the job was satisfied this way and no printer is needed.</returns>
    private static bool TryPrintToPdfFile(Document document, PrinterSettings printerSettings)
    {
        if (printerSettings is not { PrintToFile: true } settings
            || string.IsNullOrEmpty(settings.PrintFileName)
            || !settings.PrintFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return false;

        using var outDoc = new Document();
        var from = settings.FromPage > 0 ? settings.FromPage : 1;
        var to = settings.ToPage >= from && settings.ToPage > 0 && settings.ToPage <= document.Pages.Count
            ? settings.ToPage : document.Pages.Count;
        var copies = Math.Max(1, settings.Copies);
        for (var c = 0; c < copies; c++)
            for (var p = from; p <= to; p++)
                outDoc.Pages.Add(document.Pages[p]);
        outDoc.Save(settings.PrintFileName);
        return true;
    }

    /// <summary>The viewer's own configuration, and the bridges from the job's progress
    /// back to this viewer's events.</summary>
    [SupportedOSPlatform("windows")]
    private PdfPrintOptions BuildPrintOptions() => new(RenderPageForPrint)
    {
        DrawPage = DrawPage,
        JobName = PrinterJobName,
        PrintAsImage = PrintAsImage,
        UseIntermediateImage = UseIntermidiateImage,
        ImageResolution = Resolution,
        AutoRotate = AutoRotate,
        RotateClockwise = AutoRotateMode == AutoRotateMode.ClockWise,
        AutoResize = AutoResize,
        ScaleFactor = ScaleFactor,
        PrintAsGrayscale = PrintAsGrayscale,
        HorizontalAlignment = HorizontalAlignment,
        VerticalAlignment = VerticalAlignment,
        OnStartPage = args => StartPage?.Invoke(this, args),
        OnEndPage = args => EndPage?.Invoke(this, args),
        OnCustomPrint = args => CustomPrint?.Invoke(this, args),
        OnEndPrint = (sender, args) => EndPrint?.Invoke(sender, args),
        OnQueryPageSettings = (args, pageNumber) =>
            PdfQueryPageSettings?.Invoke(this, args, new PdfPrintPageInfo(pageNumber)),
    };
}
