using System.Globalization;
using System.IO.Compression;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Text positioning mode for PdfFileMend.
/// </summary>
public enum PositioningMode
{
    /// <summary>Legacy line spacing mode.</summary>
    Legacy,
    /// <summary>Modern line spacing mode.</summary>
    ModernLineSpacing,
    /// <summary>Use the document's current positioning mode.</summary>
    Current,
}
