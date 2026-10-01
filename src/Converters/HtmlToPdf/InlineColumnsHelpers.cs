using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The inline-columns render helper: a blank flow line.
    private static void AddBlank(InlineColumnsState ic) => ic.flow.Add((new List<(double, string, bool)>(), false));
}
