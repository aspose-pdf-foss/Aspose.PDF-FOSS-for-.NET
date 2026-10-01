using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The css @page margin rule, lifted out of ConvertFromHtml; it reads and
// writes the conversion state only. Body is verbatim.
    private static void ApplyCssPageMargins(ConvertState cv)
    {
        if (cv.cssPageRule is not { } cp) return;
        if (cp.MarginLeftPt is { } cpL) cv.marginLeft = cpL;
        if (cp.MarginRightPt is { } cpR) cv.marginRight = cpR;
        if (cp.MarginTopPt is { } cpT) cv.marginTop = cpT;
        if (cp.MarginBottomPt is { } cpB) cv.marginBottom = cpB;
        cv.cssPageFirstTopLift = cp.FirstMarginTopPt is { } cpF ? cv.marginTop - cpF : 0.0;
    }

    /// <summary>The body tag's own style attribute, or null when the tag carries none.</summary>
    private static string? BodyTagStyleAttr(string html)
    {
        var m = Regex.Match(html, @"<body\b[^>]*style\s*=\s*(['""])([^'""]*)\1", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[2].Value : null;
    }

    /// <summary>One side of the margin a body tag's style states: the side's longhand first, else its
    /// slot in the `margin` shorthand; null when the style states neither.</summary>
    private static double? BodyAttrMarginPt(string style, string side, double emPt)
    {
        var longhand = Regex.Match(style, @"(?<![-\w])margin-" + side + @"\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (longhand.Success) return StatedMarginPt(longhand.Groups[1].Value, emPt);
        var shorthand = Regex.Match(style, @"(?<![-\w])margin\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (!shorthand.Success) return null;
        var (t, r, b, l) = ChainPadPt(shorthand.Groups[1].Value, emPt);
        return side switch { "top" => t, "right" => r, "bottom" => b, _ => l };
    }
}
