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
            var m = ViewBoxMatrix(sv.width, sv.height, sv.vbMinX, sv.vbMinY, sv.vbW, sv.vbH,
                sv.svgRoot!.GetAttribute("preserveAspectRatio"));
            sv.sb.Append($"{F(m[0])} 0 0 {F(m[3])} {F(m[4])} {F(m[5])} cm\n");
            sv.ctm = Mul(m, sv.ctm);
        }
    }

    /// <summary>The matrix that maps a viewBox onto a viewport of the given size.</summary>
    private static double[] ViewBoxMatrix(double width, double height, double vbMinX, double vbMinY,
        double vbW, double vbH, string preserveAspectRatio)
    {
        double sx = width / vbW, sy = height / vbH;
        double tx0 = 0, ty0 = 0;
        // preserveAspectRatio (default "xMidYMid meet"): a uniform scale with
        // the leftover space distributed by the alignment; "none" keeps the
        // independent per-axis stretch.
        var par = preserveAspectRatio.Trim();
        if (par != "none")
        {
            var slice = par.EndsWith("slice", StringComparison.Ordinal);
            var s = slice ? Math.Max(sx, sy) : Math.Min(sx, sy);
            var align = par.Length > 0 ? par.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] : "xMidYMid";
            double fx = 0;
            if (align.StartsWith("xMid", StringComparison.OrdinalIgnoreCase)) fx = 0.5;
            else if (align.StartsWith("xMax", StringComparison.OrdinalIgnoreCase)) fx = 1;
            double fy = 0;
            if (align.Contains("YMid", StringComparison.OrdinalIgnoreCase)) fy = 0.5;
            else if (align.Contains("YMax", StringComparison.OrdinalIgnoreCase)) fy = 1;
            tx0 = (width - vbW * s) * fx;
            ty0 = (height - vbH * s) * fy;
            sx = sy = s;
        }
        return new[] { sx, 0, 0, sy, tx0 - sx * vbMinX, ty0 - sy * vbMinY };
    }

    /// <summary>A nested viewport (a nested svg, or a symbol drawn by a use): its viewBox is mapped
    /// onto the width and height of <paramref name="sized"/>, each defaulting to the enclosing
    /// viewport's, as the root's is onto the page. Returns the CTM inside the viewport.</summary>
    private static double[] ApplyNestedViewport(XmlElement viewport, XmlElement sized, Ctx ctx, double[] ctm)
    {
        var parts = viewport.GetAttribute("viewBox").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return ctm;
        double vbW = ParseLength(parts[2]), vbH = ParseLength(parts[3]);
        if (vbW <= 0 || vbH <= 0) return ctm;
        var width = GetLen(sized, "width", ctx.VpW);
        var height = GetLen(sized, "height", ctx.VpH);
        var m = ViewBoxMatrix(width > 0 ? width : ctx.VpW, height > 0 ? height : ctx.VpH,
            ParseLength(parts[0]), ParseLength(parts[1]), vbW, vbH, viewport.GetAttribute("preserveAspectRatio"));
        ctx.Surface.Sb.Append($"{F(m[0])} 0 0 {F(m[3])} {F(m[4])} {F(m[5])} cm\n");
        return Mul(m, ctm);
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
            if (left > 0) sv.offX = left * px;
            else if (right > 0) sv.offX = sv.pageW - sv.width - right * px;
            else sv.offX = 0;
            if (top > 0) sv.offY = top * px;
            else if (bottom > 0) sv.offY = sv.pageH - sv.height - bottom * px;
            else sv.offY = 0;
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

        // Image mode falls back to the viewBox extent (natural artwork aspect); document
        // mode falls back to the 500pt default per dimension.
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
