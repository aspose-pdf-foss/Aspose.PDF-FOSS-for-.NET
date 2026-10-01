using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render a fully-positioned DTP export (see the class comment).
    /// Null when the document does not carry the dialect's fingerprint —
    /// a flat set of pt-positioned id rules covering divs, text inputs and
    /// images under a <c>&lt;body id="page"&gt;</c>.</summary>
    private static Document? TryRenderPositionedDtp(string html, double pageW, double pageH)
    {
        var dp = new DtpPositionedState();
        dp.html = html;
        dp.pageW = pageW;
        dp.pageH = pageH;
        if (!Regex.IsMatch(dp.html, @"<body\b[^>]*\bid\s*=\s*[""']page[""']", RegexOptions.IgnoreCase))
            return null;
        dp.band = dp.pageH - 2 * DtpVertMarginPt;
        if (dp.band <= 0) return null;
        if (!ParseDtpStyles(dp)) return null;

        dp.bodyM = Regex.Match(dp.html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
        if (!dp.bodyM.Success) return null;
        dp.bodyHtml = dp.html[(dp.bodyM.Index + dp.bodyM.Length)..];
        dp.inv = System.Globalization.CultureInfo.InvariantCulture;

        dp.doc = new Document();
        dp.fontDict = new Core.PdfDictionary();
        dp.pageOps = new List<List<object>>();
        dp.pos = 0;
        dp.tagRx = new Regex(@"<(div|input|img)\b", RegexOptions.IgnoreCase);
        while (true)
        {
            if (!WalkDtpTag(dp)) break;
        }

        if (dp.pageOps.Count == 0) return null;
        WriteDtpPages(dp);
        return dp.doc;
    }
}
