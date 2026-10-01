using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A text element: its text, position and dx/dy em offsets collected as a text element at the mapped pen, sized and measured in the chart face.</summary>
    private static void ReplayBarSvgText(BarSvgState bv, Match t)
    {
        var el = new SvgTextEl
        {
            X = AttrF(bv, bv.attrs, "x", 0),
            Y = AttrF(bv, bv.attrs, "y", 0),
            Text = DecodeEntities(t.Groups["body"].Value).Trim(),
        };
        if (el.Text.Length == 0) return;
        var dxm = Regex.Match(bv.attrs, @"dx\s*=\s*['""](-?[\d.]+)em", RegexOptions.IgnoreCase);
        if (dxm.Success) el.DxEm = double.Parse(dxm.Groups[1].Value, bv.invc);
        else el.DxEm = AttrF(bv, bv.attrs, "dx", 0) / 12.0;
        var dym = Regex.Match(bv.attrs, @"dy\s*=\s*['""](-?[\d.]+)em", RegexOptions.IgnoreCase);
        if (dym.Success) el.DyEm = double.Parse(dym.Groups[1].Value, bv.invc);
        else el.DyEm = AttrF(bv, bv.attrs, "dy", 0) / 12.0;
        el.AnchorEnd = Regex.IsMatch(bv.attrs, @"text-anchor\s*:\s*end|text-anchor\s*=\s*['""]end",
            RegexOptions.IgnoreCase);
        el.Rotate45 = Regex.IsMatch(bv.attrs, @"rotate\(\s*-45", RegexOptions.IgnoreCase);
        var fsPt = el.FontPx * 0.75;
        var w = MeasureFaceText("Times New Roman", el.Text, fsPt);
        var em = el.FontPx;
        // local svg-px offset off the (x,y) anchor
        var lx = el.X + el.DxEm * em;
        var ly = el.Y + el.DyEm * em;
        if (!el.Rotate45)
        {
            var bx = MapX(bv, lx) - (el.AnchorEnd ? w : 0);
            var byTd = MapYTd(bv, ly);
            bv.sb.Append(Compat.Format(bv.invc,
                $"q {bv.chartGray.R / 255.0:0.###} {bv.chartGray.G / 255.0:0.###} {bv.chartGray.B / 255.0:0.###} rg BT /F5 {fsPt:0.##} Tf " +
                $"1 0 0 1 {bx:0.##} {bv.pageHeight - byTd:0.##} Tm ({EscapePdfString(el.Text)}) Tj ET Q\n"));
        }
        else
        {
            // rotate(-45) about the anchor: the local offset turns with
            // the glyph run; anchor-end walks back along the rotated
            // baseline. (top-down R(-45) = [c c; -c c], c = √2/2)
            const double c = 0.70710678;
            var ox = (lx * c + ly * c) * 0.75;
            var oyTd = (-lx * c + ly * c) * 0.75;
            var px = MapX(bv, 0) + ox - (el.AnchorEnd ? w * c : 0);
            var pyTd = MapYTd(bv, 0) + oyTd + (el.AnchorEnd ? w * c : 0);
            bv.sb.Append(Compat.Format(bv.invc,
                $"q {bv.chartGray.R / 255.0:0.###} {bv.chartGray.G / 255.0:0.###} {bv.chartGray.B / 255.0:0.###} rg BT /F5 {fsPt:0.##} Tf " +
                $"{c:0.#####} {c:0.#####} {-c:0.#####} {c:0.#####} {px:0.##} {bv.pageHeight - pyTd:0.##} Tm ({EscapePdfString(el.Text)}) Tj ET Q\n"));
        }
    }

    /// <summary>A path element: its M/V/H outline's bounding box under the group translate fills as a bar in the current fill.</summary>
    private static void ReplayBarSvgPath(BarSvgState bv)
    {
        // The two axis-domain shapes: an area path fills its polygon
        // (the thick x-axis band); a degenerate zero-width one
        // strokes the hairline that is still expected to show.
        var dAttr = Regex.Match(bv.attrs, @"\bd\s*=\s*['""]([^'""]*)['""]",
            RegexOptions.IgnoreCase) is { Success: true } dm
            ? dm.Groups[1].Value : "";
        var pts = new List<(double x, double y)>();
        double cx = 0, cy = 0;
        foreach (Match c in Regex.Matches(dAttr, @"([MVHmvh])\s*([\d.,\s-]*)"))
        {
            var vals = Regex.Matches(c.Groups[2].Value, @"-?[\d.]+");
            switch (char.ToUpperInvariant(c.Groups[1].Value[0]))
            {
                case 'M':
                    if (vals.Count >= 2)
                    {
                        cx = double.Parse(vals[0].Value, bv.invc);
                        cy = double.Parse(vals[1].Value, bv.invc);
                    }
                    break;
                case 'V': if (vals.Count >= 1) cy = double.Parse(vals[0].Value, bv.invc); break;
                case 'H': if (vals.Count >= 1) cx = double.Parse(vals[0].Value, bv.invc); break;
            }
            pts.Add((cx, cy));
        }
        if (pts.Count < 2) return;
        double minX = double.MaxValue, maxX = double.MinValue,
            minY = double.MaxValue, maxY = double.MinValue;
        foreach (var (px, py) in pts)
        {
            minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
            minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
        }
        if (maxX - minX > 0 && maxY - minY > 0)
        {
            // area: fill the polygon's box (the domain band)
            var bx = MapX(bv, minX); var byTd = MapYTd(bv, minY);
            bv.sb.Append(Compat.Format(bv.invc,
                $"q {bv.chartGray.R / 255.0:0.###} {bv.chartGray.G / 255.0:0.###} {bv.chartGray.B / 255.0:0.###} rg " +
                $"{bx:0.##} {bv.pageHeight - byTd - (maxY - minY) * 0.75:0.##} {(maxX - minX) * 0.75:0.##} {(maxY - minY) * 0.75:0.##} re f Q\n"));
        }
        // A degenerate (zero-area) path fills nothing, like the lines.
    }
}
