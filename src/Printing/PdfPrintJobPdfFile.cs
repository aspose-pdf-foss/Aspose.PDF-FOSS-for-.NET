using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace Aspose.Pdf.Printing
{
    internal sealed partial class PdfPrintJob
    {
        /// <summary>A sheet measures in points here: hundredths of an inch, 72 points to the inch.</summary>
        private const double PointsPerHundredth = 72d / 100d;

        /// <summary>
        /// Prints the job into a PDF file, as a PDF printer would print it, without a spooler: each page
        /// is laid out on a sheet of the job's paper - orientation, margins, AutoResize, AutoRotate, the
        /// scale factor and alignment, through the same <see cref="PdfPrintLayout.Fit"/> geometry as a
        /// printer sheet - with its form fields and annotations drawn in, in grey when the job asks for
        /// grey, and the page events raised per sheet. A job that names no paper prints each page on a
        /// sheet of its own shown size. The page stays drawing commands, so its text stays text.
        /// </summary>
        internal void RunToPdfFile(string fileName)
        {
            var jobSettings = LayeredPageSettings();
            var grey = _options.PrintAsGrayscale || (!jobSettings.IsDefaultColor && !jobSettings.Color);
            var (printed, printedPage) = PrintedPages(grey);
            using var printedPages = printed;
            using var output = new Document();
            var stamps = new Dictionary<Page, PdfPageStamp>();
            var cancelled = false;
            foreach (var sheet in _pages)
            {
                var start = PageEventArgs(sheet);
                _options.OnStartPage?.Invoke(start);
                if (start.Cancel)
                {
                    cancelled = true;
                    break;
                }
                var source = printed.Pages[printedPage[(sheet.Document, sheet.PageNumber)]];
                if (!stamps.TryGetValue(source, out var stamp))
                    stamps[source] = stamp = new PdfPageStamp(source) { CarryAnnotations = false };
                LaySheet(output, source, stamp, SheetSettings(jobSettings, sheet.PageNumber));
                _options.OnEndPage?.Invoke(PageEventArgs(sheet));
            }
            if (!cancelled) output.Save(fileName);
            _options.OnEndPrint?.Invoke(null, new CancelEventArgs(cancelled));
        }

        /// <summary>The pages the job prints, each once however many copies take it, in a document of
        /// their own: flattened - fields and annotations drawn into the page, as a printer receives
        /// them - and greyed when asked, so the caller's documents are left as they are.</summary>
        private (Document Printed, Dictionary<(Document Document, int PageNumber), int> PageOf) PrintedPages(bool grey)
        {
            var printed = new Document();
            var pageOf = new Dictionary<(Document Document, int PageNumber), int>();
            foreach (var sheet in _pages)
            {
                var key = (sheet.Document, sheet.PageNumber);
                if (pageOf.ContainsKey(key)) continue;
                var source = sheet.Document.Pages[sheet.PageNumber];
                // Paragraphs a save has not laid out yet are laid out first, once, as a printer
                // sheet's render lays them out: the copy is flattened and greyed as drawn.
                if (!source.LayoutApplied && (source.Paragraphs is { Count: > 0 } || source.ExplicitBackground is not null))
                    source.ProcessParagraphs();
                printed.Pages.Add(source);
                pageOf[key] = printed.Pages.Count;
            }
            printed.Flatten();
            if (grey)
                foreach (Page page in printed.Pages)
                    page.MakeGrayscale();
            return (printed, pageOf);
        }

        /// <summary>Settings a sheet is printed with: the job's, which a page-settings handler may
        /// replace for the sheet, as it does on a printer.</summary>
        private PageSettings SheetSettings(PageSettings jobSettings, int pageNumber)
        {
            if (_options.OnQueryPageSettings is null) return jobSettings;
            var args = new PdfQueryPageSettingsEventArgs(jobSettings);
            _options.OnQueryPageSettings(args, pageNumber);
            return args.PageSettings ?? jobSettings;
        }

        /// <summary>The settings the job's sheets start from: the printer's default page settings,
        /// then the caller's page settings over them, each only where it was assigned - the two layers
        /// a printer job lays its page out from.</summary>
        private PageSettings LayeredPageSettings()
        {
            var layered = new PageSettings();
            foreach (var layer in new[] { _printerSettings?.DefaultPageSettings, _pageSettings })
            {
                if (layer is null) continue;
                if (!layer.IsDefaultPaperSize) layered.PaperSize = layer.PaperSize;
                if (!layer.IsDefaultLandscape) layered.Landscape = layer.Landscape;
                if (layer.MarginsGiven) layered.Margins = layer.Margins;
                if (!layer.IsDefaultColor) layered.Color = layer.Color;
            }
            return layered;
        }

        /// <summary>Adds the sheet <paramref name="page"/> is printed on to <paramref name="output"/>
        /// and draws the page on it where <see cref="PdfPrintLayout.Fit"/> places it, turned a quarter
        /// when the fit turns it.</summary>
        private void LaySheet(Document output, Page page, PdfPageStamp stamp, PageSettings settings)
        {
            var (shownWidth, shownHeight) = PdfPageStamp.ShownPageSize(page);
            var (sheetWidth, sheetHeight) = SheetSize(settings, shownWidth, shownHeight);
            var margins = settings.Margins ?? new Devices.Margins();
            double left = margins.Left * PointsPerHundredth, right = margins.Right * PointsPerHundredth;
            double top = margins.Top * PointsPerHundredth, bottom = margins.Bottom * PointsPerHundredth;
            var placement = PdfPrintLayout.Fit(
                (float)(sheetWidth - left - right),
                (float)(sheetHeight - top - bottom),
                shownWidth,
                shownHeight,
                _options.AutoRotate,
                // As on a printer sheet: an intermediate-image job draws the page at its own size.
                _options.AutoResize && !_options.UseIntermediateImage,
                _options.ScaleFactor,
                _options.HorizontalAlignment,
                _options.VerticalAlignment);

            var sheet = output.Pages.Add();
            sheet.SetMediaBox(new Rectangle(0, 0, sheetWidth, sheetHeight));
            // The fit measures down from the printable area's top; the sheet's y runs up.
            var boxLeft = left + placement.X;
            var boxBottom = sheetHeight - top - placement.Y - placement.Height;
            stamp.Width = placement.Rotated ? placement.Height : placement.Width;
            stamp.Height = placement.Rotated ? placement.Width : placement.Height;
            stamp.XIndent = placement.Rotated ? 0 : boxLeft;
            stamp.YIndent = placement.Rotated ? 0 : boxBottom;
            var drawn = System.Text.Encoding.ASCII.GetString(stamp.BuildContentStream(sheet));
            if (placement.Rotated)
                drawn = $"q {QuarterTurnMatrix(boxLeft, boxBottom, placement.Width, placement.Height)} cm\n{drawn}Q\n";
            sheet.AddContentStream(System.Text.Encoding.ASCII.GetBytes(drawn));
        }

        /// <summary>The matrix that turns a page drawn from the origin a quarter turn into the box at
        /// (<paramref name="x"/>, <paramref name="y"/>) of the given size: counter-clockwise, as a printer
        /// sheet turns it, or clockwise when the job asks.</summary>
        private string QuarterTurnMatrix(double x, double y, double width, double height)
        {
            var m = _options.RotateClockwise
                ? new[] { 0d, -1, 1, 0, x, y + height }
                : new[] { 0d, 1, -1, 0, x + width, y };
            return string.Join(" ", Array.ConvertAll(m, v => v.ToString("0.####", CultureInfo.InvariantCulture)));
        }

        /// <summary>The sheet, in points: the paper the settings name, across its long edge when they
        /// ask for landscape; with no paper named, the page's own shown size, turned to the orientation
        /// the settings ask for, if any.</summary>
        private static (double Width, double Height) SheetSize(PageSettings settings, double pageWidth, double pageHeight)
        {
            if (!settings.IsDefaultPaperSize && settings.PaperSize is { Width: > 0, Height: > 0 })
            {
                var bounds = settings.Bounds;
                return (bounds.Width * PointsPerHundredth, bounds.Height * PointsPerHundredth);
            }
            if (settings.IsDefaultLandscape) return (pageWidth, pageHeight);
            var (shortSide, longSide) = (Math.Min(pageWidth, pageHeight), Math.Max(pageWidth, pageHeight));
            return settings.Landscape ? (longSide, shortSide) : (shortSide, longSide);
        }
    }
}
