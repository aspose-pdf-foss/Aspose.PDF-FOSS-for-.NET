using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static Document? TryRenderIsharesFactSheet(string html)
    {
        var ix = new IsharesFactSheetState();
        ix.html = html;
        if (!ix.html.Contains("iShares_Custom", StringComparison.Ordinal)
            || !ix.html.Contains("class=\"sideBySide\"", StringComparison.Ordinal)
            || !SafeRegex.IsMatch(ix.html, @"subtype\s*=\s*[""']TSR_", RegexOptions.IgnoreCase))
            return null;

        ix.cols = new List<IfsColumn>();
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (Match colM in SafeRegex.Matches(ix.html,
#pragma warning restore S3267
            @"<div class=""sideBySideColumn\w+"">([\s\S]*?)(?=<div class=""sideBySideColumn|<!-- 2-COLUMN|</body)",
            RegexOptions.IgnoreCase))
        {
            if (!ParseIfsColumn(ix, colM)) break;
        }
        if (ix.cols.Count == 0) return null;

        OpenIfsPage(ix);

        for (var c = 0; c < ix.cols.Count && c < 2; c++)
        {
            RenderIfsColumn(ix, c);
        }

        ix.page.AddContentStream(Encoding.ASCII.GetBytes(ix.sb.ToString()));
        return ix.doc;
    }
}
