using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The body's content up to its opening grid, when nothing but whitespace, breaks, a form's open tag and
    /// comments stands between the body tag and the first table.</summary>
    private static readonly Regex OpeningGridLeadRx = new(@"<body\b[^>]*>((?:\s|<br\s*/?>|<form\b[^>]*>|<!--.*?-->)*)<table\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>The line boxes the standalone breaks before a document's opening grid stand above it. A break with no
    /// text before it opens no block in the calibrated flow, but the browser keeps its line box - the body font's normal
    /// line - between the content top and the grid (probed: one break under a 10 pt body seats the grid 11.25 lower,
    /// two 22.5; a form's open tag among them changes nothing).</summary>
    private static double OpeningGridLeadingBreaksPt(ConvertState cv)
    {
        var lead = SafeRegex.Match(OpeningGridLeadRx, cv.html);
        if (!lead.Success) return 0.0;
        var breaks = SafeRegex.Matches(lead.Groups[1].Value, @"<br\b", RegexOptions.IgnoreCase).Count;
        if (breaks == 0) return 0.0;
        return breaks * NormalLineHeightPt(BodyFontPt(cv));
    }

    /// <summary>The body's font size in points: the body tag's own style first, else the sheet's body rule, else the UA 12 pt.</summary>
    private static double BodyFontPt(ConvertState cv)
    {
        var inline = SafeRegex.Match(BodyTagStyleAttr(cv.html) ?? "", @"(?<![-\w])font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        var declared = inline.Success ? inline.Groups[1].Value
            : cv.css.TryGetValue("body", out var bodyRule) && bodyRule.TryGetValue("font-size", out var sheetSize) ? sheetSize : null;
        if (declared is not null && (TryParseLength(declared.Trim()) ?? BodyPercentFontPt(declared.Trim())) is { } pt && pt > 0) return pt;
        return DefaultBodyFontPt;
    }
}
