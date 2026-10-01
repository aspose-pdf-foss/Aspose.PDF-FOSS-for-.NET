using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using NativePrintDocument = System.Drawing.Printing.PrintDocument;
using NativePrintPageEventArgs = System.Drawing.Printing.PrintPageEventArgs;
using NativeQueryPageSettingsEventArgs = System.Drawing.Printing.QueryPageSettingsEventArgs;
using NativeRectangleF = System.Drawing.RectangleF;

namespace Aspose.Pdf.Printing
{
    /// <summary>
    /// One print job: a flat list of pages drawn onto a printer's sheets through
    /// <see cref="NativePrintDocument"/>.
    /// </summary>
    /// <remarks>
    /// Both the page range and the copies are resolved into that list up front, and the job
    /// prints every copy itself - copy one's pages, then copy two's, whether or not collation
    /// was asked for. A range left on the settings only ever reaches a print dialog, so it has
    /// to be resolved here; the copies are resolved here because the reference raises its page
    /// events once per page of every copy, and a job that handed them to the driver instead
    /// would raise them once. The driver is then told ONE copy, or it would multiply the ones
    /// already printed. The range is left on the settings, since a caller reading them back off
    /// the job is asking what was requested.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    internal sealed class PdfPrintJob
    {
        /// <summary>Printer surfaces measure in hundredths of an inch; a PDF measures in
        /// points, 72 to the inch.</summary>
        private const double HundredthsOfInchPerPoint = 100d / 72d;

        /// <summary>What the driver is told to print of a job that has already expanded its
        /// own copies.</summary>
        private const short DriverCopies = 1;

        /// <summary>The highest DPI a page sent as drawing commands is laid out at for a printer
        /// that reports more. Beyond 600 a page costs memory, not precision any comparison can
        /// see.</summary>
        private const int MaxDeviceDpi = 600;

        /// <summary>The largest page bitmap a job renders, in pixels. A4 at 600 DPI is about 35
        /// million; a sheet large enough to exceed this is rendered at whatever DPI fits it.</summary>
        private const long MaxPagePixels = 60_000_000;

        /// <summary>The DPI assumed for a printer surface that reports none, and for an image
        /// print given no resolution.</summary>
        private const int FallbackDeviceDpi = 300;

        private readonly PdfPrintOptions _options;
        private readonly List<PrintedPage> _pages;
        private readonly PageSettings? _pageSettings;
        private readonly PrinterSettings? _printerSettings;
        private int _next;

        /// <summary>One sheet of the job: which document page it draws, and where it falls in
        /// the range and among the copies, both counted from 1.</summary>
        private readonly record struct PrintedPage(
            Document Document, int PageNumber, int CurrentPage, int TotalPages, int CurrentCopy, int TotalCopies);

        internal PdfPrintJob(
            PdfPrintOptions options,
            IEnumerable<Document> documents,
            PageSettings? pageSettings,
            PrinterSettings? printerSettings)
        {
            _options = options;
            _pageSettings = pageSettings;
            _printerSettings = printerSettings;
            _pages = SelectPages(documents, printerSettings);
        }

        /// <summary>Submits the job and waits for the spooler to take it.</summary>
        internal void Run()
        {
            PrintingOptionalDependencyGuard.EnsureDependenciesAvailable();
            if (_pages.Count == 0) return;

            using var print = new NativePrintDocument();
            print.DocumentName = _options.JobName;
            // The page is laid out from two sources, in order: the printer settings' own
            // DefaultPageSettings, then the caller's page settings over them. Configuring the
            // paper through PrinterSettings.DefaultPageSettings and passing no page settings is
            // the common case, and dropping that layer printed an A4 job on the driver's default
            // paper, one inch in from every edge - where the reference fills the sheet.
            if (_printerSettings is not null)
            {
                print.PrinterSettings.Assign(_printerSettings);
                print.DefaultPageSettings.Assign(_printerSettings.DefaultPageSettings);
            }
            print.PrinterSettings.Copies = DriverCopies;
            if (_pageSettings is not null) print.DefaultPageSettings.Assign(_pageSettings);

            print.QueryPageSettings += OnQueryPageSettings;
            print.PrintPage += OnPrintPage;
            // The handler is handed the print document itself, not the viewer: callers read
            // the settings the job actually ran with off the sender.
            print.EndPrint += (sender, e) => _options.OnEndPrint?.Invoke(sender, e);
            print.Print();
        }

        /// <summary>The sheets the job prints, in the order it prints them: every page of the
        /// range for the first copy, then again for each further copy.</summary>
        private static List<PrintedPage> SelectPages(
            IEnumerable<Document> documents, PrinterSettings? printerSettings)
        {
            var range = new List<(Document Document, int PageNumber)>();
            foreach (var document in documents)
            {
                var (from, to) = ResolveRange(printerSettings, document.Pages.Count);
                for (var page = from; page <= to; page++)
                    range.Add((document, page));
            }

            var copies = Math.Max(1, printerSettings?.Copies ?? 1);
            var sheets = new List<PrintedPage>(range.Count * copies);
            for (var copy = 1; copy <= copies; copy++)
            {
                for (var i = 0; i < range.Count; i++)
                    sheets.Add(new PrintedPage(range[i].Document, range[i].PageNumber, i + 1, range.Count, copy, copies));
            }
            return sheets;
        }

        /// <summary>The first and last page a document contributes, clamped to what it has.</summary>
        private static (int From, int To) ResolveRange(PrinterSettings? settings, int pageCount)
        {
            if (settings is null || settings.PrintRange != PrintRange.SomePages)
                return (1, pageCount);
            var from = Math.Max(1, settings.FromPage);
            var to = settings.ToPage > 0 ? Math.Min(settings.ToPage, pageCount) : pageCount;
            return (from, to);
        }

        private static StartEndPageEventArgs PageEventArgs(PrintedPage sheet)
            => new(sheet.PageNumber, sheet.CurrentPage, sheet.TotalPages, sheet.CurrentCopy, sheet.TotalCopies);

        private void OnQueryPageSettings(object? sender, NativeQueryPageSettingsEventArgs e)
        {
            if (_options.OnQueryPageSettings is null || _next >= _pages.Count) return;
            var args = new PdfQueryPageSettingsEventArgs(e.PageSettings.ToAsposePageSettings());
            _options.OnQueryPageSettings(args, _pages[_next].PageNumber);
            if (args.PageSettings is not null) e.PageSettings.Assign(args.PageSettings);
        }

        private void OnPrintPage(object? sender, NativePrintPageEventArgs e)
        {
            var sheet = _pages[_next];
            var start = PageEventArgs(sheet);
            _options.OnStartPage?.Invoke(start);
            if (start.Cancel)
            {
                e.Cancel = true;
                return;
            }

            DrawPage(e, sheet.Document, sheet.PageNumber);
            _options.OnCustomPrint?.Invoke(new CustomPrintEventArgs(sheet.PageNumber, e.Graphics!));
            _options.OnEndPage?.Invoke(PageEventArgs(sheet));

            _next++;
            e.HasMorePages = _next < _pages.Count;
        }

        private void DrawPage(NativePrintPageEventArgs e, Document document, int pageNumber)
        {
            var graphics = e.Graphics;
            if (graphics is null) return;

            // MarginBounds is the sheet less the margins the page settings asked for, and is
            // already in the surface's own hundredths of an inch.
            var sheet = PrintsWholeSheet ? e.PageBounds : e.MarginBounds;
            var pageRect = document.Pages[pageNumber].GetPageRect(considerRotation: true);
            var pageWidth = (float)(pageRect.Width * HundredthsOfInchPerPoint);
            var pageHeight = (float)(pageRect.Height * HundredthsOfInchPerPoint);
            var (fitWidth, fitHeight) = _options.PrintAsImage
                ? ImageExtent(pageRect.Width, pageRect.Height, ImageDpi(pageWidth, pageHeight))
                : (pageRect.Width * HundredthsOfInchPerPoint, pageRect.Height * HundredthsOfInchPerPoint);
            var placement = PdfPrintLayout.Fit(
                sheet.Width,
                sheet.Height,
                fitWidth,
                fitHeight,
                _options.AutoRotate,
                // An intermediate-image job draws the page at its own size: the reference placed a
                // Letter page on A4 unscaled, cut at the right edge, with AutoResize on.
                _options.AutoResize && !_options.UseIntermediateImage,
                _options.ScaleFactor,
                _options.HorizontalAlignment,
                _options.VerticalAlignment);

            var state = graphics.Save();
            try
            {
                graphics.TranslateTransform(sheet.Left + placement.X, sheet.Top + placement.Y);
                if (placement.Rotated && _options.RotateClockwise)
                {
                    // Turning clockwise about the origin sweeps the page off the sheet to the
                    // left; the width it now occupies puts it back.
                    graphics.TranslateTransform(placement.Width, 0);
                    graphics.RotateTransform(QuarterTurn);
                }
                else if (placement.Rotated)
                {
                    // Counter-clockwise sweeps it off the top; its height puts it back.
                    graphics.TranslateTransform(0, placement.Height);
                    graphics.RotateTransform(-QuarterTurn);
                }
                var target = placement.Rotated
                    ? new NativeRectangleF(0, 0, placement.Height, placement.Width)
                    : new NativeRectangleF(0, 0, placement.Width, placement.Height);
                var seated = placement.Rotated
                    ? new NativeRectangleF(0, 0, placement.SeatedHeight, placement.SeatedWidth)
                    : new NativeRectangleF(0, 0, placement.SeatedWidth, placement.SeatedHeight);
                if (!DrawPageAsCommands(graphics, document, pageNumber, target, placement.Height, pageWidth, pageHeight))
                    DrawPageAsImage(graphics, document, pageNumber, target, seated, ImageDpi(pageWidth, pageHeight));
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        /// <summary>
        /// Sends the page to the printer as drawing commands, as the reference does. False, with
        /// nothing drawn, when the job prints images, prints in grey - a colour matrix applies to
        /// an image only - or the page holds content that is composited from pixels.
        /// </summary>
        /// <remarks>
        /// The page is seated on a whole hundredth of an inch of the height it is drawn on the sheet:
        /// it moves up the sheet by the fraction of a hundredth that height carries, however it is
        /// turned. The reference's own print of a page fitted to 1070.24 hundredths sits that 0.24
        /// higher than one seated at the exact top - placed there, the XPS writer's rounding of every
        /// glyph and image to its 1/600 inch grid lands where the reference's does, and the page left
        /// 2,242 pixels outside the corpus template's match window instead of 111,848. A page turned
        /// counter-clockwise onto a sheet, drawn 1395.96 hundredths tall, sits 0.96 higher in the
        /// reference's print than turned about its exact height: 6,585 pixels outside the window
        /// unseated, 946 seated. Pages whose drawn height is already whole (550.00 at half scale,
        /// 1169.00 fitted to A4) are unmoved, as are theirs.
        /// </remarks>
        /// <param name="graphics">The printer graphics the page is drawn on.</param>
        /// <param name="document">The document being printed.</param>
        /// <param name="pageNumber">The 1-based number of the page to draw.</param>
        /// <param name="target">The rectangle on the sheet the page is drawn into, in hundredths of an inch.</param>
        /// <param name="drawnHeight">The height the page takes on the sheet, in hundredths of an inch.</param>
        /// <param name="pageWidth">The page's own width, in hundredths of an inch.</param>
        /// <param name="pageHeight">The page's own height, in hundredths of an inch.</param>
        private bool DrawPageAsCommands(Graphics graphics, Document document, int pageNumber, NativeRectangleF target,
            float drawnHeight, float pageWidth, float pageHeight)
        {
            if (_options.PrintAsImage || _options.PrintAsGrayscale || _options.UseIntermediateImage
                || _options.DrawPage is null)
                return false;
            var state = graphics.Save();
            try
            {
                // Appended, the move is along the sheet whatever turn the page transform carries.
                graphics.TranslateTransform(0, (float)Math.Floor(drawnHeight) - drawnHeight, System.Drawing.Drawing2D.MatrixOrder.Append);
                return _options.DrawPage(document, pageNumber, graphics, target, CommandDpi(graphics, pageWidth, pageHeight));
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        /// <summary>
        /// Whether the page is laid over the whole sheet rather than inside its margins: a grey job,
        /// or an intermediate-image job asked to resize, whose margins nobody assigned. The reference
        /// prints those edge to edge - a grey print of a page given no margins reached the XPS writer
        /// as one image at the sheet's origin, and an intermediate-image print with AutoResize on as
        /// one image from the top-left corner - where its colour print of the same page, its
        /// print-as-image, and its intermediate-image print with AutoResize off sit one inch in.
        /// </summary>
        private bool PrintsWholeSheet
            => (_options.PrintAsGrayscale || (_options.UseIntermediateImage && _options.AutoResize))
               && (_pageSettings?.IsDefaultMargins ?? true)
               && (_printerSettings?.DefaultPageSettings.IsDefaultMargins ?? true);

        /// <summary>Renders the page to a bitmap and prints that as a PNG: fitted to the sheet, into the
        /// page's seated size (see <see cref="PdfPrintPlacement.SeatedWidth"/>); otherwise at the
        /// image's own size.</summary>
        /// <remarks>
        /// <para>Truncated, not rounded: the reference placed a page fitted to 759.9 hundredths wide as
        /// an image 759 wide (728.64 XPS units), and so placed, the fallback image of that page left
        /// 54,520 pixels outside its template's match window instead of 256,080.</para>
        /// <para>Handed over as a PNG: a bitmap decoded from PNG bytes reaches the XPS writer as that
        /// PNG at its own pixel size, as the reference's page image does, where a bitmap drawn
        /// directly is resampled to the sheet or re-encoded as a JPEG (the fallback page above:
        /// 54,520 -> 10,590 pixels outside the window).</para>
        /// </remarks>
        private void DrawPageAsImage(Graphics graphics, Document document, int pageNumber, NativeRectangleF target,
            NativeRectangleF seated, int dpi)
        {
            using var rendered = _options.RenderPage(document, pageNumber, dpi);
            using var attributes = BuildImageAttributes();
            using var png = EncodePng(rendered, dpi);
            using var image = new Bitmap(png);
            var destination = FitsPageToSheet ? seated : NaturalSize(image, target);
            graphics.DrawImage(
                image,
                new[] { destination.Location, new PointF(destination.Right, destination.Top), new PointF(destination.Left, destination.Bottom) },
                new NativeRectangleF(0, 0, image.Width, image.Height),
                GraphicsUnit.Pixel,
                attributes);
        }

        /// <summary>The size, in hundredths of an inch, of a page's image at <paramref name="dpi"/>: whole
        /// pixels at that resolution.</summary>
        /// <remarks>A job printed as images fits the image to the sheet, not the page: a page 450 by 792
        /// points, 937 by 1650 pixels at 150 DPI, printed 663 hundredths wide on A4 - 937 pixels fitted
        /// to 1169 in 1650 - where the page itself fits to 664.2.</remarks>
        private static (double Width, double Height) ImageExtent(double widthPoints, double heightPoints, int dpi)
            => (Devices.SoftwarePageRenderer.PagePixels(widthPoints, dpi) * (double)HundredthsPerInch / dpi,
                Devices.SoftwarePageRenderer.PagePixels(heightPoints, dpi) * (double)HundredthsPerInch / dpi);

        /// <summary>Whether the page is fitted to the sheet, not drawn at its own size.</summary>
        private bool FitsPageToSheet => _options.AutoResize && !_options.UseIntermediateImage;

        /// <summary>The page image at the size its own resolution stamp gives it, times the scale factor.</summary>
        /// <remarks>
        /// A page drawn at its own size is sized by its image, not by the page: the reference placed a
        /// Letter page rendered at 150 DPI 816.16 XPS units wide, one printer pixel over the page's
        /// 816 - 1275 pixels at the stamped 149.987 DPI are 850.07 hundredths - and the same page at
        /// 72 DPI, half scale, 408.16 wide, and 816.16 wide again inside one-inch margins, running
        /// past them. Sized this way, all three of ours land on exactly those widths.
        /// </remarks>
        private NativeRectangleF NaturalSize(Bitmap image, NativeRectangleF target)
        {
            var factor = _options.ScaleFactor > 0 ? _options.ScaleFactor : 1f;
            return new NativeRectangleF(
                target.X,
                target.Y,
                image.Width / image.HorizontalResolution * HundredthsPerInch * factor,
                image.Height / image.VerticalResolution * HundredthsPerInch * factor);
        }

        /// <summary>The page image as PNG bytes, stamped with the resolution it was rendered at.</summary>
        /// <remarks>
        /// The stamp is the rendered DPI in whole pixels per metre, truncated: 5905 for 150 DPI and 2834
        /// for 72, as the reference's page images carry. The XPS rasteriser sizes the image by it, and
        /// GDI+'s own rounded stamp (5906) scaled the reference's own page image, pixel for pixel, from
        /// 0 pixels outside its template's match window to 23,622.
        /// </remarks>
        private static System.IO.MemoryStream EncodePng(Bitmap rendered, int dpi)
        {
            var stamped = (float)(Math.Floor(dpi / InchesPerMetre) * InchesPerMetre);
            rendered.SetResolution(stamped, stamped);
            var png = new System.IO.MemoryStream();
            rendered.Save(png, ImageFormat.Png);
            png.Position = 0;
            return png;
        }

        /// <summary>A PNG's resolution is kept in pixels per metre; this many inches make a metre.</summary>
        private const double InchesPerMetre = 0.0254;

        /// <summary>
        /// The DPI a page printed as an image is rendered at: the viewer's resolution, whether the
        /// job asked for images or the page could not be sent as drawing commands. The reference
        /// prints both at that resolution - a page it could not send as commands reached the XPS
        /// writer as one 1275x1650 image, a letter page at the viewer's default 150 DPI - and the
        /// corpus' own print matrix varies resolution only for image prints.
        /// </summary>
        /// <param name="pageWidth">The page's own width, in hundredths of an inch.</param>
        /// <param name="pageHeight">The page's own height, in hundredths of an inch.</param>
        private int ImageDpi(float pageWidth, float pageHeight)
        {
            double dpi = _options.ImageResolution > 0 ? _options.ImageResolution : FallbackDeviceDpi;
            // The bitmap covers the page at its own size; keep it inside the pixel budget.
            var pixels = pageWidth / HundredthsPerInch * dpi * (pageHeight / HundredthsPerInch * dpi);
            if (pixels > MaxPagePixels)
                dpi *= Math.Sqrt(MaxPagePixels / pixels);
            return Math.Max(1, (int)Math.Round(dpi));
        }

        /// <summary>
        /// The resolution a page sent as drawing commands is laid out at: the printer's own, at the
        /// page's own size, within the same pixel budget as a rendered page. Not scaled by how much
        /// the page is shrunk onto the sheet, as a rendered page is - the layout only sets the
        /// precision the outlines are computed to, and a page fitted to A4 at 584 DPI (600 times
        /// its 0.973 fit) put every image one 1/600 inch cell off the reference's, where 300, 600,
        /// 601 and 1200 all put them exactly on it. Below the printer's resolution the outlines
        /// lose the precision the printer keeps: at 72 DPI a line of text sat half a device pixel
        /// high.
        /// </summary>
        /// <param name="graphics">The printer graphics whose resolution is read.</param>
        /// <param name="pageWidth">The page's own width, in hundredths of an inch.</param>
        /// <param name="pageHeight">The page's own height, in hundredths of an inch.</param>
        private static int CommandDpi(Graphics graphics, float pageWidth, float pageHeight)
        {
            var device = (int)Math.Round(Math.Max(graphics.DpiX, graphics.DpiY));
            double dpi = Math.Min(device > 0 ? device : FallbackDeviceDpi, MaxDeviceDpi);
            var pixels = pageWidth / HundredthsPerInch * dpi * (pageHeight / HundredthsPerInch * dpi);
            if (pixels > MaxPagePixels)
                dpi *= Math.Sqrt(MaxPagePixels / pixels);
            return Math.Max(1, (int)Math.Round(dpi));
        }

        /// <summary>A quarter turn, in the degrees a surface rotates by.</summary>
        private const float QuarterTurn = 90f;

        /// <summary>A printer surface measures in hundredths of an inch.</summary>
        private const float HundredthsPerInch = 100f;

        /// <summary>Drawing attributes for the page image: grey ink when the job asks for
        /// it, and nothing otherwise.</summary>
        private ImageAttributes? BuildImageAttributes()
        {
            if (!_options.PrintAsGrayscale) return null;
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(GrayscaleMatrix);
            return attributes;
        }

        /// <summary>Luminance weights: the same Rec. 601 mix a printer driver greys with.</summary>
        private static readonly ColorMatrix GrayscaleMatrix = new(new[]
        {
            new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
            new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
            new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
    }
}
