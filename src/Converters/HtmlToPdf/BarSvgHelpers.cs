using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The bar-chart SVG replay helper: a numeric attribute of a tag with a default.
    private static double AttrF(BarSvgState bv, string tag, string name, double dflt)
        => Regex.Match(tag, name + @"\s*=\s*['""]([\d.-]+)", RegexOptions.IgnoreCase)
            is { Success: true } m
                ? double.Parse(m.Groups[1].Value, bv.invc) : dflt;

    private static double MapX(BarSvgState bv, double px) => bv.xPt + (bv.top.tx + px) * 0.75;

    private static double MapYTd(BarSvgState bv, double px) => bv.yTopTd + (bv.top.ty + px) * 0.75;
}
