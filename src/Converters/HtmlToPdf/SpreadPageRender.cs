using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Spread pages: one matched spread rendered as a page.</summary>
    private static bool RenderSpread(SpreadPagesState sp, Match spread)
    {
        var rs = new SpreadRenderState();
        rs.body = spread.Groups["body"].Value;
        rs.page = sp.doc.Pages.Add(sp.pageWidth, sp.pageHeight);
        EnsureFonts(rs.page);

        // 1. The spread's own absolutely positioned images: natural pixel
        // size × 0.75, anchored at the page TOP-left in source order.
        foreach (Match im in SafeRegex.Matches(rs.body,
                     "<img\\b[^>]*src\\s*=\\s*[\"']([^\"']+)[\"'][^>]*/?>",
                     RegexOptions.IgnoreCase))
        {
            // images inside the content div are the flow's own (the figure) —
            // only the ones before it are spread layers
            var contentOpen = SafeRegex.Match(rs.body, "<div\\b[^>]*class\\s*=", RegexOptions.IgnoreCase);
            if (contentOpen.Success && im.Index > contentOpen.Index) break;
            var data = LoadConverterImage(im.Groups[1].Value, sp.options);
            if (data is null || TryReadImagePixelSize(data) is not (var iw, var ih)
                || iw <= 0 || ih <= 0) continue;
            var w = iw * 0.75;
            var h = ih * 0.75;
            try
            {
                rs.page.AddImage(data, new Rectangle(0, sp.pageHeight - h, w, sp.pageHeight));
            }
            catch { /* undecodable layer: the canvas stays blank behind the text */ }
        }

        rs.contentM = SafeRegex.Match(rs.body,
            "<div\\b[^>]*class\\s*=\\s*[\"'][^\"']*[\"'][^>]*>(?<inner>[\\s\\S]*)$",
            RegexOptions.IgnoreCase);
        rs.inner = rs.contentM.Success ? rs.contentM.Groups["inner"].Value : rs.body;
        rs.y = sp.padT;
        rs.floatBottom = 0;
        rs.pendingRightTop = 0.0;

#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (Match el in SafeRegex.Matches(rs.inner,
#pragma warning restore S3267
                     "<(?<tag>h1|p|div)\\b(?<attrs>[^>]*)>(?<body>[\\s\\S]*?)</\\k<tag>>",
                     RegexOptions.IgnoreCase))
        {
            if (!RenderSpreadElement(sp, rs, el)) break;
        }
        return true;
    }
}
