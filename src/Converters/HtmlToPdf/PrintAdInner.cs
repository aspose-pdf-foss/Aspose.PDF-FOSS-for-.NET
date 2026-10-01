using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Print ad tags: the inner-html reader.
    private static string InnerOf(AdTagState at, AdWalkState wa, string frag, string tg)
    {
        var innerX = BalancedInner(frag, wa.p2, tg);
        if (innerX is null) { innerX = ""; }
        else wa.p2 += innerX.Length + tg.Length + 3;
        return innerX;
    }
}
