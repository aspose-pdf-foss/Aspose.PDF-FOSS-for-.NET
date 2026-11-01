using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The size an em offset of a table tag is measured in when nothing sets one.</summary>
    private const double RelativeOffsetEmPt = 12.0;

    /// <summary>How far a relatively positioned table moves right: its tag's `position: relative` with a
    /// `left` offset (or a `right` one, which moves it left), 0 otherwise. The offset moves the box where it
    /// draws; its size and the flow around it stay as laid out.</summary>
    private static double TableRelativeShiftPt(string? tableHtml)
    {
        if (string.IsNullOrEmpty(tableHtml)) return 0;
        var tag = SafeRegex.Match(tableHtml!, @"<table\b[^>]*>", RegexOptions.IgnoreCase).Value;
        if (tag.Length == 0 || SafeRegex.Match(tag, @"\bstyle\s*=\s*(['""])(.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Singleline) is not { Success: true } st)
            return 0;
        var style = st.Groups[2].Value;
        if (!SafeRegex.IsMatch(style, @"(?:^|;)\s*position\s*:\s*relative\b", RegexOptions.IgnoreCase)) return 0;
        if (SafeRegex.Match(style, @"(?:^|;)\s*left\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } left)
            return ChainLenPt(left.Groups[1].Value, RelativeOffsetEmPt);
        if (SafeRegex.Match(style, @"(?:^|;)\s*right\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } right)
            return -ChainLenPt(right.Groups[1].Value, RelativeOffsetEmPt);
        return 0;
    }
}
