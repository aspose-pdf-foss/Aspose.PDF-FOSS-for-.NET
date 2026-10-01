using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Replays one SVG tag of the bar chart: a group pushes its translate and fill (a self-closing one pops at once), a rect fills its box, a path's M/V/H outline fills its bounding box, a text element is collected with its dx/dy offsets.</summary>
    private static bool ReplayBarSvgTag(BarSvgState bv, Match t)
    {
        bv.tag = t.Groups["tag"].Value.ToLowerInvariant();
        bv.attrs = t.Groups["attrs"].Value;
        bv.top = bv.stack.Peek();
        if (t.Groups["close"].Success)
        {
            if (bv.tag == "g" && bv.stack.Count > 1) bv.stack.Pop();
            return true;
        }
        switch (bv.tag)
        {
            case "g":
            {
                var (gx, gy) = (bv.top.tx, bv.top.ty);
                var tr = Regex.Match(bv.attrs,
                    @"translate\(\s*([\d.-]+)\s*[, ]\s*([\d.-]+)\s*\)", RegexOptions.IgnoreCase);
                if (tr.Success)
                {
                    gx += double.Parse(tr.Groups[1].Value, bv.invc);
                    gy += double.Parse(tr.Groups[2].Value, bv.invc);
                }
                var gFill = Regex.Match(bv.attrs, @"fill\s*:\s*(#[0-9a-fA-F]{3,6})")
                    is { Success: true } gf ? gf.Groups[1].Value : bv.top.fill;
                bv.stack.Push((gx, gy, gFill));
                if (t.Groups["self"].Success && bv.stack.Count > 1) bv.stack.Pop();
                break;
            }
            case "line":
                // Svg geometry is FILLED — a line has no area, so it leaves
                // no ink (the grid/tick lines are invisible on the expected
                // render).
                break;
            case "rect":
            {
                var rx = MapX(bv, AttrF(bv, bv.attrs, "x", 0));
                var ryTd = MapYTd(bv, AttrF(bv, bv.attrs, "y", 0));
                var rw = AttrF(bv, bv.attrs, "width", 0) * 0.75;
                var rh = AttrF(bv, bv.attrs, "height", 0) * 0.75;
                if (rw <= 0 || rh <= 0) break;
                var fill = ParseCssColor(bv.top.fill) ?? bv.chartGray;
                bv.sb.Append(Compat.Format(bv.invc,
                    $"q {fill.R / 255.0:0.###} {fill.G / 255.0:0.###} {fill.B / 255.0:0.###} rg " +
                    $"{rx:0.##} {bv.pageHeight - ryTd - rh:0.##} {rw:0.##} {rh:0.##} re f Q\n"));
                break;
            }
            case "path":
            {
                ReplayBarSvgPath(bv);
                break;
            }
            case "text":
            {
                ReplayBarSvgText(bv, t);
                break;
            }
        }
        return true;
    }
}
