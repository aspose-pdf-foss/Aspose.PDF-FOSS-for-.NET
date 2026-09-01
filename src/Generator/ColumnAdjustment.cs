using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// How a <see cref="Table"/> sizes its columns (API compatibility).
/// </summary>
public enum ColumnAdjustment
{
    /// <summary>Use <c>ColumnWidths</c> as specified.</summary>
    Customized,
    /// <summary>Distribute available width equally across columns.</summary>
    AutoFitToWindow,
    /// <summary>Size columns to fit the widest cell content.</summary>
    AutoFitToContent,
}
