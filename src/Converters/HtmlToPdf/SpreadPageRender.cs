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
        foreach (Match im in Regex.Matches(rs.body,
                     "<img\\b[^>]*src\\s*=\\s*[\"']([^\"']+)[\"'][^>]*/?>",
                     RegexOptions.IgnoreCase))
        {
            // images inside the content div are the flow's own (the figure) —
            // only the ones before it are spread layers
            var contentOpen = Regex.Match(rs.body, "<div\\b[^>]*class\\s*=", RegexOptions.IgnoreCase);
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

        rs.contentM = Regex.Match(rs.body,
            "<div\\b[^>]*class\\s*=\\s*[\"'][^\"']*[\"'][^>]*>(?<inner>[\\s\\S]*)$",
            RegexOptions.IgnoreCase);
        rs.inner = rs.contentM.Success ? rs.contentM.Groups["inner"].Value : rs.body;
        rs.y = sp.padT;
        rs.floatBottom = 0;
        rs.pendingRightTop = 0.0;

        foreach (Match el in Regex.Matches(rs.inner,
                     "<(?<tag>h1|p|div)\\b(?<attrs>[^>]*)>(?<body>[\\s\\S]*?)</\\k<tag>>",
                     RegexOptions.IgnoreCase))
        {
            if (!RenderSpreadElement(sp, rs, el)) break;
        }
        return true;
    }
}
