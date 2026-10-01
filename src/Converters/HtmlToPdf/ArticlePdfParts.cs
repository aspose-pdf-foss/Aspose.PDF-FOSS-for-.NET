using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Article PDF helpers: pixel scale, inner html, flattening, block parsing, emit, new page and ink colours.
    private static double Px(ArticlePdfState ap, string sel, string prop, double fallback)
        => ap.css.TryGetValue(sel, out var r) && r.TryGetValue(prop, out var v)
           && TryParseLength(v) is { } pt ? pt : fallback;

    private static string Inner(ArticlePdfState ap, string cls)
        => Regex.Match(ap.html,
            @"<(?<tag>h1|div|footer)\b[^>]*class\s*=\s*[""'][^""']*" + Regex.Escape(cls) +
            @"[^""']*[""'][^>]*>(?<body>[\s\S]*?)</\k<tag>>",
            RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups["body"].Value : "";

    private static string Flat(ArticlePdfState ap, string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

    private static void ParseBlocksInto(ArticlePdfState ap, string frag, List<ApBlock> into)
    {
        foreach (Match bm in Regex.Matches(frag,
            @"<(?<tag>p|h2|h3|li)\b[^>]*>(?<body>[\s\S]*?)</\k<tag>>", RegexOptions.IgnoreCase))
        {
            var txt = Flat(ap, bm.Groups["body"].Value);
            if (txt.Length > 0)
                into.Add(new ApBlock { Kind = bm.Groups["tag"].Value.ToLowerInvariant(), Text = txt });
        }
    }

    private static void Emit(ArticlePdfState ap, string res, double fs, double x, double yTd, string text)
        => EmitPositionedRun(ap.page, res, fs, x, ap.pageHeight - yTd, text);

    private static void NewPage(ArticlePdfState ap)
    {
        ap.page = ap.doc.Pages.Add(ap.pageWidth, ap.pageHeight);
        EnsureFonts(ap.page);
    }

    private static void DescInk(ArticlePdfState ap) => ap.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(ap.invc,
        $"{ap.descColor.R / 255.0:0.###} {ap.descColor.G / 255.0:0.###} {ap.descColor.B / 255.0:0.###} rg\n")));

    private static void BlackInk(ArticlePdfState ap) => ap.page.AddContentStream(Encoding.ASCII.GetBytes("0 0 0 rg\n"));
}
