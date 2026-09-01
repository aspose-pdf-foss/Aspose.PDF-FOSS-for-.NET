using System.Globalization;
using System.IO.Compression;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Word wrap mode for PdfFileMend text operations.
/// </summary>
public enum WordWrapMode
{
    /// <summary>Default word wrapping.</summary>
    Default,
    /// <summary>Wrap by words (no mid-word breaks).</summary>
    ByWords,
}
