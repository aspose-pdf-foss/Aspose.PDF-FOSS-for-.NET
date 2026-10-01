using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Spread page text helpers: flattened inner html and the wrapped emit.
    private static void EmitLine(SpreadRenderState rs, SpreadPagesState sp, string res, double fs, double x, double baselineTd, string text)
        => EmitPositionedRun(rs.page, res, fs, x, sp.pageHeight - baselineTd, text);

    private static string Flat(SpreadRenderState rs, string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, "<[^>]+>", " ")), "\\s+", " ").Trim();
}
