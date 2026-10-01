using System;

namespace Aspose.Pdf.Printing
{
    /// <summary>Event args supplied to <c>PdfViewer.PdfQueryPageSettings</c>: the settings
    /// the job is about to use for the page, which a handler may change.</summary>
    public class PdfQueryPageSettingsEventArgs : EventArgs
    {
        /// <summary>Creates args carrying no settings.</summary>
        public PdfQueryPageSettingsEventArgs() { }

        /// <summary>Creates args carrying the settings for the page about to print.</summary>
        public PdfQueryPageSettingsEventArgs(PageSettings pageSettings)
        {
            PageSettings = pageSettings;
        }

        /// <summary>The settings the page will be printed with.</summary>
        public PageSettings? PageSettings { get; set; }
    }

    /// <summary>Event args raised by <c>PdfViewer.StartPage</c> and <c>PdfViewer.EndPage</c>.</summary>
    /// <remarks>
    /// The job prints every copy itself, one after another, so a page event is raised once per
    /// page of every copy - three copies of a two-page range raise six. <see cref="CurrentPage"/>
    /// counts within the RANGE, from 1: printing pages 2 to 3 of a document reports 1 and 2,
    /// with <see cref="PageNumber"/> carrying the document's own numbering for a handler that
    /// needs it.
    /// </remarks>
    public class StartEndPageEventArgs : EventArgs
    {
        /// <summary>1-based position of this page within the printed range.</summary>
        public readonly int CurrentPage;

        /// <summary>Number of pages in the printed range - per copy, not in total.</summary>
        public readonly int TotalPages;

        /// <summary>1-based number of the copy being printed.</summary>
        public readonly int CurrentCopy;

        /// <summary>Number of copies the job prints.</summary>
        public readonly int TotalCopies;

        /// <summary>Creates args for page zero.</summary>
        public StartEndPageEventArgs() { }

        /// <summary>Creates args for the given document page, as the only page of a single copy.</summary>
        public StartEndPageEventArgs(int pageNumber)
            : this(pageNumber, currentPage: 1, totalPages: 1, currentCopy: 1, totalCopies: 1)
        {
        }

        /// <summary>Creates args for one page of one copy of a job.</summary>
        internal StartEndPageEventArgs(int pageNumber, int currentPage, int totalPages, int currentCopy, int totalCopies)
        {
            PageNumber = pageNumber;
            CurrentPage = currentPage;
            TotalPages = totalPages;
            CurrentCopy = currentCopy;
            TotalCopies = totalCopies;
        }

        /// <summary>The 1-based page of the document being printed.</summary>
        public int PageNumber { get; set; }

        /// <summary>Set by a handler to abandon the rest of the job.</summary>
        public bool Cancel { get; set; }
    }

    /// <summary>Event args raised by <c>PdfViewer.CustomPrint</c>: the printer surface the
    /// page has just been drawn on, so a handler can draw over it.</summary>
    public class CustomPrintEventArgs : EventArgs
    {
        /// <summary>Creates args carrying no surface.</summary>
        public CustomPrintEventArgs() { }

        /// <summary>Creates args for the given page and printer surface.</summary>
        public CustomPrintEventArgs(int pageNumber, System.Drawing.Graphics graphics)
        {
            PageNumber = pageNumber;
            Graphics = graphics;
        }

        /// <summary>The 1-based page being printed.</summary>
        public int PageNumber { get; set; }

        /// <summary>The printer surface the page was drawn on.</summary>
        public System.Drawing.Graphics? Graphics { get; set; }
    }
}
