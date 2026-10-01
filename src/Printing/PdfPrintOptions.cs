using System;
using System.ComponentModel;
using System.Drawing;

namespace Aspose.Pdf.Printing
{
    /// <summary>
    /// The viewer-side half of a print job: how each page is rendered and placed, and the
    /// callbacks that carry the job's progress back to the viewer's own events.
    /// </summary>
    /// <remarks>
    /// The job takes the rendering as a callback rather than reaching for a device itself,
    /// so the placement and the spooler conversation can be exercised without a rasteriser.
    /// </remarks>
    internal sealed class PdfPrintOptions
    {
        internal PdfPrintOptions(Func<Document, int, int, Bitmap> renderPage)
        {
            RenderPage = renderPage ?? throw new ArgumentNullException(nameof(renderPage));
        }

        /// <summary>Renders one page of one document to a bitmap at the given DPI, for a printer.</summary>
        internal Func<Document, int, int, Bitmap> RenderPage { get; }

        /// <summary>Draws one page of one document as drawing commands into a rectangle of a
        /// printer surface, laid out as a render at the given DPI; false, with nothing drawn, when
        /// the page cannot be printed that way. Unset, every page prints as an image.</summary>
        internal Func<Document, int, Graphics, RectangleF, int, bool>? DrawPage { get; init; }

        /// <summary>Print each page as an image at <see cref="ImageResolution"/> DPI. Otherwise the
        /// job sends each page as drawing commands, as the reference does - see PdfPrintJob.</summary>
        internal bool PrintAsImage { get; init; }

        /// <summary>The viewer's own resolution: the DPI a page printed as an image is rendered
        /// at.</summary>
        internal int ImageResolution { get; init; }

        /// <summary>The job name the spooler queue shows.</summary>
        internal string JobName { get; init; } = "Aspose.PDF FOSS Print";

        /// <summary>Turn a page a quarter turn when its orientation and the sheet's disagree.</summary>
        internal bool AutoRotate { get; init; }

        /// <summary>Turn such a page clockwise rather than counter-clockwise.</summary>
        internal bool RotateClockwise { get; init; }

        /// <summary>Fit each page to the sheet rather than scaling it by a number.</summary>
        internal bool AutoResize { get; init; }

        /// <summary>Fixed scale for a page that is not auto-resized.</summary>
        internal float ScaleFactor { get; init; } = 1f;

        /// <summary>Print each page through an intermediate image of the whole sheet.</summary>
        internal bool UseIntermediateImage { get; init; }

        /// <summary>Print every page as grey ink, whatever colour it carries.</summary>
        internal bool PrintAsGrayscale { get; init; }

        /// <summary>Where a page that does not fill the sheet sits across it.</summary>
        internal HorizontalAlignment HorizontalAlignment { get; init; }

        /// <summary>Where a page that does not fill the sheet sits down it.</summary>
        internal VerticalAlignment VerticalAlignment { get; init; }

        /// <summary>Raised before each page is drawn; a handler may cancel the job.</summary>
        internal Action<StartEndPageEventArgs>? OnStartPage { get; init; }

        /// <summary>Raised after each page is drawn.</summary>
        internal Action<StartEndPageEventArgs>? OnEndPage { get; init; }

        /// <summary>Raised with the printer surface once the page is on it, so a handler
        /// can draw over the page.</summary>
        internal Action<CustomPrintEventArgs>? OnCustomPrint { get; init; }

        /// <summary>Raised once the last sheet has gone, with the print document that ran
        /// the job as the sender so callers can read the settings it used.</summary>
        internal Action<object?, CancelEventArgs>? OnEndPrint { get; init; }

        /// <summary>Raised with the settings the next sheet will use, which a handler may
        /// change before it is printed.</summary>
        internal Action<PdfQueryPageSettingsEventArgs, int>? OnQueryPageSettings { get; init; }
    }
}
