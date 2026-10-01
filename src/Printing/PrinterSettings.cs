using System;

namespace Aspose.Pdf.Printing
{
    /// <summary>
    /// Which printer a job goes to and how it is run there: the copies, the page range,
    /// the duplex mode, and whether the job is spooled to a file instead of to paper.
    /// </summary>
    public class PrinterSettings
    {
        /// <summary>The highest page number a range may name when the caller sets none.</summary>
        private const int DefaultMaximumPage = 9999;

        private PageSettings? _defaultPageSettings;
        private string _printerName = string.Empty;

        /// <summary>Creates settings for the default printer.</summary>
        public PrinterSettings() { }

        /// <summary>
        /// The printer this job goes to. Assigning a name makes the settings name a
        /// printer explicitly; until then they mean "whatever the default printer is".
        /// </summary>
        public string PrinterName
        {
            get => _printerName;
            set { _printerName = value; IsDefaultPrinterName = false; }
        }

        /// <summary>Number of copies of the range to print.</summary>
        public int Copies { get; set; } = 1;

        /// <summary>True when the copies of a multi-page range are printed
        /// document-by-document rather than page-by-page.</summary>
        public bool Collate { get; set; }

        /// <summary>First page of the range, used when <see cref="PrintRange"/> is
        /// <see cref="Printing.PrintRange.SomePages"/>.</summary>
        public int FromPage { get; set; }

        /// <summary>Last page of the range, used when <see cref="PrintRange"/> is
        /// <see cref="Printing.PrintRange.SomePages"/>.</summary>
        public int ToPage { get; set; }

        /// <summary>Lowest page number <see cref="FromPage"/> may take.</summary>
        public int MinimumPage { get; set; }

        /// <summary>Highest page number <see cref="ToPage"/> may take.</summary>
        public int MaximumPage { get; set; } = DefaultMaximumPage;

        /// <summary>Which pages of the document the job covers.</summary>
        public PrintRange PrintRange { get; set; } = PrintRange.AllPages;

        /// <summary>How the printer prints on both sides of a sheet.</summary>
        public Duplex Duplex { get; set; } = Duplex.Default;

        /// <summary>Route the job to <see cref="PrintFileName"/> rather than to paper.</summary>
        public bool PrintToFile { get; set; }

        /// <summary>Target path for a <see cref="PrintToFile"/> job.</summary>
        public string? PrintFileName { get; set; }

        /// <summary>The printer's address when it is reached over a network protocol
        /// rather than by name.</summary>
        public Uri? PrinterUri { get; set; }

        /// <summary>
        /// The page settings this printer applies to a page the job does not describe
        /// itself. Created on first use, so a caller can configure it in place.
        /// </summary>
        public PageSettings DefaultPageSettings
            => _defaultPageSettings ??= new PageSettings { PrinterSettings = this };

        /// <summary>True while <see cref="PrinterName"/> has never been assigned, meaning
        /// the job goes to whichever printer is the default when it runs.</summary>
        internal bool IsDefaultPrinterName { get; private set; } = true;

        /// <inheritdoc/>
        public override string ToString()
            => "[PrinterSettings " + PrinterName + " Copies=" + Copies +
               " Collate=" + Collate + " Duplex=" + Duplex +
               " FromPage=" + FromPage + " ToPage=" + ToPage + "]";
    }
}
