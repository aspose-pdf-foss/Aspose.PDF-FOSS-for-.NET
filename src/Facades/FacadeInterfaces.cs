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
        None,
        FlipLandscape,
        /// <summary>Rotate output 90° clockwise.</summary>
        ClockWise,
        /// <summary>Rotate output 90° counter-clockwise.</summary>
        AntiClockWise,
    }

    /// <summary>Custom event handler delegate for <c>PdfViewer.PdfQueryPageSettings</c>.
    /// Lives in <see cref="Aspose.Pdf.Facades"/> in Aspose.PDF for .NET.</summary>
    public delegate void PdfQueryPageSettingsEventHandler(
        object sender,
        Aspose.Pdf.Printing.PdfQueryPageSettingsEventArgs queryPageSettingsEventArgs,
        PdfPrintPageInfo currentPageInfo);

    public class PdfPrintPageInfo
    {
        public int PageNumber { get; }
    }
}
