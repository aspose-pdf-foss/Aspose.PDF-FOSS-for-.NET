#nullable disable

using System;
using System.IO;

namespace Aspose.Pdf.Facades
{
    public interface IFacade : IDisposable
    {
        void BindPdf(Document srcDoc);
        void BindPdf(string srcFile);
        void BindPdf(Stream srcStream);
        void Close();
    }

    public interface ISaveableFacade : IFacade
    {
        void Save(string destFile);
        void Save(Stream destStream);
    }

    /// <summary>Auto-rotation behaviour for the PDF viewer.</summary>
    public enum AutoRotateMode
    {
        /// <summary>Print pages as they are.</summary>
        None,
        /// <summary>Rotate output 90° clockwise.</summary>
        ClockWise,
        /// <summary>Rotate output 90° counter-clockwise.</summary>
        AntiClockWise,
    }

    /// <summary>Event handler for <c>PdfViewer.PdfQueryPageSettings</c>, raised before each page
    /// is printed so the caller can change its page settings.</summary>
    public delegate void PdfQueryPageSettingsEventHandler(
        object sender,
        Aspose.Pdf.Printing.PdfQueryPageSettingsEventArgs queryPageSettingsEventArgs,
        PdfPrintPageInfo currentPageInfo);

    public class PdfPrintPageInfo
    {
        public PdfPrintPageInfo(int pageNumber) => PageNumber = pageNumber;

        public int PageNumber { get; }
    }
}
