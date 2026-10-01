using System;

namespace Aspose.Pdf.Printing
{
    /// <summary>Where one page's image lands on one sheet, and whether it is turned.</summary>
    internal readonly struct PdfPrintPlacement
    {
        internal PdfPrintPlacement(float x, float y, float width, float height, bool rotated)
            : this(x, y, width, height, width, height, rotated)
        {
        }

        internal PdfPrintPlacement(float x, float y, float width, float height, float seatedWidth, float seatedHeight,
            bool rotated)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            SeatedWidth = seatedWidth;
            SeatedHeight = seatedHeight;
            Rotated = rotated;
        }

        /// <summary>Left edge of the drawn page, in the sheet's own units.</summary>
        internal float X { get; }

        /// <summary>Top edge of the drawn page, in the sheet's own units.</summary>
        internal float Y { get; }

        /// <summary>Width the page is drawn at.</summary>
        internal float Width { get; }

        /// <summary>Height the page is drawn at.</summary>
        internal float Height { get; }

        /// <summary>Width the page takes when it is seated on the sheet: its fitted width truncated to
        /// whole units, then scaled.</summary>
        internal float SeatedWidth { get; }

        /// <summary>Height the page takes when it is seated on the sheet: its fitted height truncated
        /// to whole units, then scaled.</summary>
        internal float SeatedHeight { get; }

        /// <summary>True when the page is turned a quarter turn to meet the sheet.</summary>
        internal bool Rotated { get; }
    }

    /// <summary>
    /// Fits a PDF page onto a sheet of paper. The unit is whatever the caller measures the
    /// sheet in - the printer surface works in hundredths of an inch and the page is
    /// converted to match before it gets here - so this is pure geometry.
    /// </summary>
    /// <remarks>
    /// Every rule here is read off the reference's printed templates rather than chosen:
    /// <list type="bullet">
    /// <item><b>Scale.</b> A scale factor MULTIPLIES the fit rather than replacing it. Two
    /// templates that differ only in AutoResize, both at a factor of 0.5, show the page 0.902x
    /// as large with AutoResize on - the fit into that sheet's printable area - and at 0.5 of
    /// that; a third, at 0.2 with AutoResize, draws the page at exactly 0.2 of its fitted size.
    /// </item>
    /// <item><b>Placement.</b> The viewer's alignments default to None, which pins the page to
    /// the TOP-LEFT of the sheet, as Left and Top do. Center and Bottom move a page down, Center
    /// and Right move it across: a Letter page fitted to A4 and aligned Bottom reached the XPS
    /// writer 95.04 units down the sheet, drawn as commands or as one image alike.</item>
    /// </list>
    /// </remarks>
    internal static class PdfPrintLayout
    {
        /// <summary>A scale factor at or below zero states no factor at all: the page is drawn
        /// at its fitted (or natural) size, not collapsed to nothing.</summary>
        private const float NoScaleFactor = 0f;

        /// <summary>
        /// Places <paramref name="pageWidth"/> x <paramref name="pageHeight"/> inside the
        /// printable area at (0,0)-(<paramref name="sheetWidth"/>,<paramref name="sheetHeight"/>).
        /// </summary>
        /// <param name="sheetWidth">Width of the printable area.</param>
        /// <param name="sheetHeight">Height of the printable area.</param>
        /// <param name="pageWidth">The page's own width.</param>
        /// <param name="pageHeight">The page's own height.</param>
        /// <param name="autoRotate">Turn the page a quarter turn when its orientation and
        /// the sheet's disagree, so a landscape page prints across a portrait sheet.</param>
        /// <param name="autoResize">Fit the page to the sheet, keeping its proportions, before
        /// <paramref name="scaleFactor"/> is applied.</param>
        /// <param name="scaleFactor">Multiplies the fitted size when <paramref name="autoResize"/>
        /// is set and the natural size when it is not.</param>
        /// <param name="horizontal">Horizontal placement on the sheet; None pins the page to the left.</param>
        /// <param name="vertical">Vertical placement on the sheet; None pins the page to the top.</param>
        internal static PdfPrintPlacement Fit(
            float sheetWidth,
            float sheetHeight,
            double pageWidth,
            double pageHeight,
            bool autoRotate,
            bool autoResize,
            float scaleFactor,
            HorizontalAlignment horizontal,
            VerticalAlignment vertical)
        {
            if (pageWidth <= 0 || pageHeight <= 0)
                return new PdfPrintPlacement(0, 0, sheetWidth, sheetHeight, rotated: false);

            var rotated = autoRotate
                && pageWidth > pageHeight != sheetWidth > sheetHeight;
            var turnedWidth = rotated ? pageHeight : pageWidth;
            var turnedHeight = rotated ? pageWidth : pageHeight;

            // In double precision, as the reference sizes a page: a Letter page is 850 hundredths
            // wide there, 849.99994 in single precision, and at a factor of 0.6 that truncated a
            // whole hundredth short and put a right-aligned page one hundredth right of the reference's.
            var fit = autoResize
                ? Math.Min((double)sheetWidth / turnedWidth, (double)sheetHeight / turnedHeight)
                : 1d;
            var factor = scaleFactor > NoScaleFactor ? scaleFactor : 1f;
            var scale = fit * factor;

            var width = turnedWidth * scale;
            var height = turnedHeight * scale;
            var seatedWidth = Seat(turnedWidth * fit) * factor;
            var seatedHeight = Seat(turnedHeight * fit) * factor;
            return new PdfPrintPlacement(
                (sheetWidth - (float)seatedWidth) * ToOffsetShare(horizontal),
                (sheetHeight - (float)seatedHeight) * ToOffsetShare(vertical),
                (float)width,
                (float)height,
                (float)seatedWidth,
                (float)seatedHeight,
                rotated);
        }

        /// <summary>The share of the leftover room that goes before the page: 0 pins it to
        /// the near edge, 1 to the far edge, 0.5 centres it.</summary>
        private static float ToOffsetShare(HorizontalAlignment horizontal) => horizontal switch
        {
            HorizontalAlignment.Center => 0.5f,
            HorizontalAlignment.Right => 1f,
            // None - the default - pins the page to the left edge, as does Left; the justifying
            // modes have no meaning for a whole page and fall in with them.
            _ => 0f,
        };

        /// <inheritdoc cref="ToOffsetShare(HorizontalAlignment)"/>
        private static float ToOffsetShare(VerticalAlignment vertical) => vertical switch
        {
            VerticalAlignment.Center => 0.5f,
            VerticalAlignment.Bottom => 1f,
            // None - the default - pins the page to the top edge, as does Top.
            _ => 0f,
        };

        /// <summary>A fitted size truncated to whole units: the size the page is seated at, before
        /// the scale factor.</summary>
        /// <remarks>
        /// <para>The room left beside a page is measured against it: the reference centred a page
        /// drawn 570.906 hundredths wide in 1149 as if it were 570 wide, 0.45 hundredths right of the
        /// exact centre, and every edge of its print sat there.</para>
        /// <para>Truncated before the scale factor, not after: a Letter page fitted to A4 is 827 by
        /// 1070.24 hundredths, and at a factor of 0.6 the reference printed its image 496.2 by 642 and
        /// aligned it right 330.8 from the edge - 827 and 1070 scaled - where the scaled size
        /// truncated would have been 496.</para>
        /// </remarks>
        private static double Seat(double fitted) => Math.Floor(fitted + SeatTolerance);

        /// <summary>How far under a whole unit a fitted size may fall from rounding and still count
        /// as that unit.</summary>
        private const double SeatTolerance = 0.001;
    }
}
