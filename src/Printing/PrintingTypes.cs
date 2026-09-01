#nullable disable

using System;

namespace Aspose.Pdf.Printing
{
    /// <summary>Event args supplied to <c>PdfViewer.PdfQueryPageSettings</c>.</summary>
    public class PdfQueryPageSettingsEventArgs : EventArgs
    {
        public PdfQueryPageSettingsEventArgs() { }
        public PageSettings PageSettings { get; set; }
    }

    /// <summary>Per-page print settings (paper size / orientation / margins).
    /// Stored only; the FOSS PdfViewer rejects PrintDocument-style calls.</summary>
    public class PageSettings
    {
        public PageSettings() { }
        public PaperSize PaperSize { get; set; }
        public bool Landscape { get; set; }
        public string Margins { get; set; }
        public string Color { get; set; }
    }

    /// <summary>Printer-side settings (printer name, copies, range).
    /// Stored only; the FOSS PdfViewer rejects PrintDocument-style calls.</summary>
    public class PrinterSettings
    {
        public PrinterSettings() { }
        public string PrinterName { get; set; }
        public int Copies { get; set; } = 1;
        public int FromPage { get; set; }
        public int ToPage { get; set; }
        public bool Collate { get; set; }
        /// <summary>Route the job to <see cref="PrintFileName"/> instead of a
        /// physical printer. The FOSS viewer honours a PDF target directly.</summary>
        public bool PrintToFile { get; set; }
        /// <summary>Target path for <see cref="PrintToFile"/> jobs.</summary>
        public string PrintFileName { get; set; }
    }

    public class PaperSize
    {
        public PaperSize() { }
        public PaperSize(string name, int width, int height) { PaperName = name; Width = width; Height = height; }
        public string PaperName { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Kind { get; set; }
    }

    /// <summary>Event args raised by <c>PdfViewer.StartPage</c> / <c>EndPage</c>.</summary>
    public class StartEndPageEventArgs : System.EventArgs
    {
        public StartEndPageEventArgs() { }
        public int PageNumber { get; set; }
        public bool Cancel { get; set; }
    }

    /// <summary>Event args raised by <c>PdfViewer.CustomPrint</c>.</summary>
    public class CustomPrintEventArgs : System.EventArgs
    {
        public CustomPrintEventArgs() { }
        public int PageNumber { get; set; }
        public System.Drawing.Graphics Graphics { get; set; }
    }
}
