using Aspose.Pdf.Devices;

namespace Aspose.Pdf.Printing
{
    /// <summary>
    /// How one page of a print job is laid on paper: the sheet, its orientation, the
    /// margins printed inside it, the bin it is drawn from and the resolution it is
    /// rendered at.
    /// </summary>
    /// <remarks>
    /// Every property remembers whether it was assigned. A property never assigned is
    /// "default", and a print job fills it from the printer rather than imposing the
    /// value this object happens to hold - that is how <c>PrinterSettings.DefaultPageSettings</c>
    /// can supply a paper source while the caller's own page settings leave it alone.
    /// </remarks>
    public class PageSettings
    {
        private PaperSize _paperSize = PaperSizes.Letter;
        private PaperSource _paperSource = PaperSources.AutomaticFeed;
        private PrinterResolution _printerResolution = new();
        private Margins _margins = new();
        private bool _landscape;
        private bool _color = true;

        /// <summary>Creates page settings whose every property is still at its default, bound to
        /// settings for the default printer - as the reference does, so a caller can fill in
        /// <see cref="PrinterSettings"/> in place and print with it.</summary>
        public PageSettings()
        {
            PrinterSettings = new PrinterSettings();
        }

        /// <summary>Creates page settings bound to a printer, starting from that printer's
        /// own defaults.</summary>
        public PageSettings(PrinterSettings printerSettings)
        {
            PrinterSettings = printerSettings;
            if (printerSettings is null) return;

            var defaults = printerSettings.DefaultPageSettings;
            // Only what the printer's defaults actually carry is adopted; anything still
            // default there stays default here, so the flags mean the same thing at both ends.
            if (!defaults.IsDefaultPaperSize) PaperSize = defaults.PaperSize;
            if (!defaults.IsDefaultPaperSource) PaperSource = defaults.PaperSource;
            if (!defaults.IsDefaultPrinterResolution) PrinterResolution = defaults.PrinterResolution;
            if (!defaults.IsDefaultMargins) Margins = defaults.Margins;
            if (!defaults.IsDefaultLandscape) Landscape = defaults.Landscape;
            if (!defaults.IsDefaultColor) Color = defaults.Color;
        }

        /// <summary>The sheet this page is printed on.</summary>
        public PaperSize PaperSize
        {
            get => _paperSize;
            set { _paperSize = value; IsDefaultPaperSize = false; }
        }

        /// <summary>The bin the sheet is drawn from.</summary>
        public PaperSource PaperSource
        {
            get => _paperSource;
            set { _paperSource = value; IsDefaultPaperSource = false; }
        }

        /// <summary>The resolution the page is rendered at.</summary>
        public PrinterResolution PrinterResolution
        {
            get => _printerResolution;
            set { _printerResolution = value; IsDefaultPrinterResolution = false; }
        }

        /// <summary>The margins left unprinted inside the sheet, in hundredths of an inch.</summary>
        public Margins Margins
        {
            get => _margins;
            set { _margins = value; IsDefaultMargins = false; }
        }

        /// <summary>True when the page is printed across the sheet's long edge.</summary>
        public bool Landscape
        {
            get => _landscape;
            set { _landscape = value; IsDefaultLandscape = false; }
        }

        /// <summary>True when the page is printed in colour.</summary>
        public bool Color
        {
            get => _color;
            set { _color = value; IsDefaultColor = false; }
        }

        /// <summary>The printer these settings belong to, if any.</summary>
        public PrinterSettings? PrinterSettings { get; set; }

        /// <summary>
        /// The printable sheet in hundredths of an inch, with the paper size's own
        /// dimensions swapped when <see cref="Landscape"/> is set.
        /// </summary>
        public Rectangle Bounds
        {
            get
            {
                var size = PaperSize ?? PaperSizes.Letter;
                return Landscape
                    ? new Rectangle(0, 0, size.Height, size.Width)
                    : new Rectangle(0, 0, size.Width, size.Height);
            }
        }

        /// <summary>True while <see cref="Margins"/> has never been assigned.</summary>
        public bool IsDefaultMargins { get; private set; } = true;

        internal bool IsDefaultPaperSize { get; private set; } = true;
        internal bool IsDefaultPaperSource { get; private set; } = true;
        internal bool IsDefaultPrinterResolution { get; private set; } = true;
        internal bool IsDefaultLandscape { get; private set; } = true;
        internal bool IsDefaultColor { get; private set; } = true;

        /// <inheritdoc/>
        public override string ToString()
            => "[PageSettings: Color=" + Color + ", Landscape=" + Landscape +
               ", Margins=" + Margins.Left + "," + Margins.Right + "," + Margins.Top + "," + Margins.Bottom +
               ", PaperSize=" + PaperSize + "]";
    }
}
