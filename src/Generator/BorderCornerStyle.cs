using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>Corner-rounding style for a <see cref="Table"/>'s border box.</summary>
public enum BorderCornerStyle
{
    /// <summary>Sharp corners (default).</summary>
    None,

    /// <summary>Rounded corners (using <c>BorderInfo.RoundedBorderRadius</c>).</summary>
    Round,
}
