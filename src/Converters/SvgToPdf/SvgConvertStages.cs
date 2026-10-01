using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
    /// <summary>The stages of the SVG document conversion: the viewBox read, the page-info fit and the viewBox transform.</summary>
    private static void ApplySvgViewBoxTransform(SvgConvertState sv)
    {
        if (sv.hasViewBox)
        {
            double sx = sv.width / sv.vbW, sy = sv.height / sv.vbH;
            double tx0 = 0, ty0 = 0;
            // preserveAspectRatio (default "xMidYMid meet"): a uniform scale with
            // the leftover space distributed by the alignment; "none" keeps the
            // independent per-axis stretch.
            var par = sv.svgRoot!.GetAttribute("preserveAspectRatio").Trim();
            if (par != "none")
            {
                var slice = par.EndsWith("slice", StringComparison.Ordinal);
                var s = slice ? Math.Max(sx, sy) : Math.Min(sx, sy);
                var align = par.Length > 0 ? par.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] : "xMidYMid";
                double fx = align.StartsWith("xMid", StringComparison.OrdinalIgnoreCase) ? 0.5
                    : align.StartsWith("xMax", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                double fy = align.Contains("YMid", StringComparison.OrdinalIgnoreCase) ? 0.5
                    : align.Contains("YMax", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                tx0 = (sv.width - sv.vbW * s) * fx;
                ty0 = (sv.height - sv.vbH * s) * fy;
                sx = sy = s;
            }
            sv.sb.Append($"{F(sx)} 0 0 {F(sy)} {F(tx0 - sx * sv.vbMinX)} {F(ty0 - sy * sv.vbMinY)} cm\n");
            sv.ctm = Mul(new[] { sx, 0, 0, sy, tx0 - sx * sv.vbMinX, ty0 - sy * sv.vbMinY }, sv.ctm);
        }
    }

    /// <summary></summary>
    private static void ApplySvgPageInfo(SvgConvertState sv)
    {
        if (sv.pi is not null)
        {
            const double px = 0.75;
            // Only a margin side the caller SET grows the page or anchors the artwork;
            // a free-standing PageInfo carries the 90/72 pt defaults untouched, and
            // those must not wrap an SVG that was asked to fill its own page.
            var m = sv.pi.Margin;
            double left = m.LeftTouched ? m.Left : 0, right = m.RightTouched ? m.Right : 0;
            double top = m.TopTouched ? m.Top : 0, bottom = m.BottomTouched ? m.Bottom : 0;
            sv.pageW = sv.pi.Width > 0 ? sv.pi.Width * px : sv.width + (left + right) * px;
            sv.pageH = sv.pi.Height > 0 ? sv.pi.Height * px : sv.height + (top + bottom) * px;
            sv.offX = left > 0 ? left * px : right > 0 ? sv.pageW - sv.width - right * px : 0;
            sv.offY = top > 0 ? top * px : bottom > 0 ? sv.pageH - sv.height - bottom * px : 0;
        }
    }

    /// <summary></summary>
    private static void ReadSvgViewBox(SvgConvertState sv)
    {
        if (!string.IsNullOrEmpty(sv.viewBox))
        {
            var parts = sv.viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                sv.vbMinX = ParseLength(parts[0]);
                sv.vbMinY = ParseLength(parts[1]);
                sv.vbW = ParseLength(parts[2]);
                sv.vbH = ParseLength(parts[3]);
                sv.hasViewBox = sv.vbW > 0 && sv.vbH > 0;
            }
        }

        // Image mode falls back to the viewBox extent (natural artwork aspect);
        // document mode falls back to the 500pt default per dimension.
        if (sv.width <= 0) sv.width = sv.imageMode && sv.hasViewBox ? sv.vbW : 500;
        if (sv.height <= 0) sv.height = sv.imageMode && sv.hasViewBox ? sv.vbH : 500;

        if (!sv.hasViewBox)
        {
            // No viewBox: content is authored in CSS px user units. Document mode
            // maps 1px → 0.75pt; image mode reads them 1:1.
            sv.vbMinX = 0; sv.vbMinY = 0;
            var pxFactor = sv.imageMode ? 1.0 : 0.75;
            sv.vbW = sv.width / pxFactor;
            sv.vbH = sv.height / pxFactor;
            sv.hasViewBox = true;
        }
    }
}
