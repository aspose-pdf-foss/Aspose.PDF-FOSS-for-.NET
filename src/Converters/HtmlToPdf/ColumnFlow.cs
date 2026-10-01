using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The INLINE CSS multi-column dialect (a container div whose own style declares
// `columns: N …`): the converter pours the container's block flow down N
// equal columns, filling each to the page's content bottom before opening the
// next and starting a fresh page after the last — no balancing, unlike the
// class-rule dialect in CssColumns.cs, which lays a single balanced page.
//
// The document this models is a filing export: every paragraph declares
// `margin: 0` and the vertical rhythm comes from EMPTY paragraphs, so the whole
// flow sits on one uniform line grid. Its numbered section headings are tiny
// percent-width tables, which pour through the same grid a line at a time.
internal static partial class HtmlToPdfConverter
{
    /// <summary>Render an inline-`columns` document, or null when no container
    /// declares them in its own style attribute.</summary>
    private static Document? TryRenderInlineCssColumns(string html, HtmlLoadOptions? options,
        double pageWidth, double pageHeight)
    {
        var ic = new InlineColumnsState();
        ic.html = html;
        ic.options = options;
        ic.pageWidth = pageWidth;
        ic.pageHeight = pageHeight;
        ic.contM = Regex.Match(ic.html,
            @"<div\b[^>]*style\s*=\s*([""'])(?<st>(?:(?!\1).)*?(?<![-\w])columns\s*:\s*(?<n>\d+)[^""']*)\1[^>]*>",
            RegexOptions.IgnoreCase);
        if (!ic.contM.Success) return null;
        ic.nCols = int.Parse(ic.contM.Groups["n"].Value);
        if (ic.nCols is < 2 or > 12) return null;
        ic.body = ic.html[(ic.contM.Index + ic.contM.Length)..];
        ic.endM = Regex.Match(ic.body, @"</body\s*>", RegexOptions.IgnoreCase);
        if (ic.endM.Success) ic.body = ic.body[..ic.endM.Index];

        ic.gapM = Regex.Match(ic.contM.Groups["st"].Value,
            @"column-gap\s*:\s*([\d.]+\s*\w*)", RegexOptions.IgnoreCase);

        ic.bodyM = Regex.Match(ic.html, @"<body\b[^>]*style\s*=\s*([""'])(?<st>(?:(?!\1).)*)\1",
            RegexOptions.IgnoreCase);
        ic.bodySt = ic.bodyM.Success ? DecodeEntities(ic.bodyM.Groups["st"].Value) : "";
        ic.face = "Times New Roman";
        if (Regex.Match(ic.bodySt, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase)
                is { Success: true } bfM
            && FirstFontFamily(bfM.Groups[1].Value) is { Length: > 0 } bfName
            && WinMetricsFor(bfName) is not null)
            ic.face = bfName;
        if (WinMetricsFor(ic.face) is not { } fm) return null;
        ic.fs = 10.0;
        if (Regex.Match(ic.bodySt, @"font-size\s*:\s*([\d.]+\s*\w*)", RegexOptions.IgnoreCase)
                is { Success: true } bsM
            && TryParseLength(bsM.Groups[1].Value.Replace(" ", "")) is { } bsPt && bsPt > 0)
            ic.fs = bsPt;
        ic.padX = 0.0;
        if (Regex.Match(ic.bodySt, @"padding\s*:\s*([\d.]+\s*\w*)\s+([\d.]+\s*\w*)",
                RegexOptions.IgnoreCase) is { Success: true } bpM
            && TryParseLength(bpM.Groups[2].Value.Replace(" ", "")) is { } padXv)
            ic.padX = padXv;

        ic.lineH = MetricLineHeight(ic.fs, HheaLineSumFor(ic.face) ?? fm.sum);
        ic.drop = MetricBaselineDrop(ic.fs, ic.lineH, fm);
        ic.gap = ic.gapM.Success && TryParseLength(ic.gapM.Groups[1].Value.Replace(" ", "")) is { } gv
            ? gv : ic.fs;

        ic.pageInfo = ic.options?.PageInfo;
        ic.marginTop = ic.pageInfo?.Margin?.IsTouched == true ? ic.pageInfo.Margin.Top : ColMarginTop;
        ic.marginBottom = ic.pageInfo?.Margin?.IsTouched == true ? ic.pageInfo.Margin.Bottom : ColMarginTop;
        ic.marginX = (ic.pageInfo?.Margin?.IsTouched == true ? ic.pageInfo.Margin.Left : ColMarginX) + ic.padX;
        ic.contentW = ic.pageWidth - 2 * ic.marginX;
        ic.colW = (ic.contentW - (ic.nCols - 1) * ic.gap) / ic.nCols;
        if (ic.colW <= ic.fs) return null;

        ic.flow = new List<(List<(double Dx, string Text, bool Bold)> Runs, bool Justify)>();
        CollectInlineColumnRuns(ic);
        if (ic.flow.Count == 0) return null;

        ic.doc = new Document();
        ic.page = ic.doc.Pages.Add(ic.pageWidth, ic.pageHeight);
        EnsureFonts(ic.page);
        ic.col = 0;
        ic.y = ic.marginTop;
        ic.bottom = ic.pageHeight - ic.marginBottom;
        ic.inv = System.Globalization.CultureInfo.InvariantCulture;
        EmitInlineColumnFlow(ic);
        return ic.doc;
    }
}
