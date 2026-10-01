using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A closing span gives back the typography, the background box and the emphasis run it opened, and a block-span breaks its line the way its open did.</summary>
    private static void CloseSpanTag(ParseBlocksState pb, string tag)
    {
        if (tag.Equals("span", StringComparison.OrdinalIgnoreCase))
        {
            CloseSpanTypography(pb);
        }
    }
}
