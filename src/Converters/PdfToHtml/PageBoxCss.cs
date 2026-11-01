using System.Globalization;
using System.Text;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>The page box's border, flow and margin declarations, as the save options place the pages: a border
    /// per side, the margins per side (a number of points or auto), else the pages one per row - or flowing side by
    /// side with the viewer's width - parted by the additional margin.</summary>
    private static string PageBoxCss(HtmlSaveOptions? options)
    {
        var css = new StringBuilder();
        if (options?.PageBorderIfAny is { } border)
        {
            BorderSide(css, "top", border.TopStyleIfAny);
            BorderSide(css, "right", border.RightStyleIfAny);
            BorderSide(css, "bottom", border.BottomStyleIfAny);
            BorderSide(css, "left", border.LeftStyleIfAny);
        }
        else css.Append("\tborder-style: none;\n");

        var additional = options?.AdditionalMarginWidthInPoints ?? 0;
        if (options?.PageMarginIfAny is { } margin && PlacesThePage(margin, additional))
        {
            MarginSide(css, "top", margin.TopMarginIfAny);
            MarginSide(css, "right", margin.RightMarginIfAny);
            MarginSide(css, "bottom", margin.BottomMarginIfAny);
            MarginSide(css, "left", margin.LeftMarginIfAny);
        }
        else
        {
            var display = options?.PagesFlowTypeDependsOnViewersScreenSize == true ? "inline-block" : "block";
            css.Append("\tdisplay: ").Append(display).Append(";\n\tmargin: ").Append(PointsEm(additional)).Append(";\n");
        }
        return css.ToString();
    }

    /// <summary>True when the margins move the page: a side of a fixed size other than none, or margins left to the
    /// viewer beside an additional margin. Margins that are all auto or nothing leave the page at the left.</summary>
    private static bool PlacesThePage(SaveOptions.MarginInfo margin, int additional)
    {
        var sides = new[] { margin.TopMarginIfAny, margin.RightMarginIfAny, margin.BottomMarginIfAny, margin.LeftMarginIfAny };
        return additional > 0 || sides.Any(s => s is { IsAuto: false, ValueInPoints: not 0 });
    }

    private static void MarginSide(StringBuilder css, string side, SaveOptions.MarginPartStyle? part)
    {
        var value = part is { IsAuto: true } ? "auto" : PointsEm(part?.ValueInPoints ?? 0);
        css.Append("\tmargin-").Append(side).Append(": ").Append(value).Append(";\n");
    }

    /// <summary>A side with no style of its own is a one-point gray side of no line.</summary>
    private static void BorderSide(StringBuilder css, string side, SaveOptions.BorderPartStyle? part)
    {
        var width = part?.WidthInPoints ?? 1;
        var line = (part?.LineType ?? SaveOptions.HtmlBorderLineType.None).ToString().ToLowerInvariant();
        var color = part?.Color ?? System.Drawing.Color.Gray;
        var name = System.Drawing.ColorTranslator.ToHtml(color);
        css.Append("\tborder-").Append(side).Append(": ").Append(PointsEm(width)).Append(' ').Append(line)
            .Append(' ').Append(name).Append(";\n");
    }

    /// <summary>A length in points as ems of the 12-point page font, at single precision.</summary>
    private static string PointsEm(double points) =>
        ((float)(points / 12.0)).ToString("R", CultureInfo.InvariantCulture) + "em";
}
