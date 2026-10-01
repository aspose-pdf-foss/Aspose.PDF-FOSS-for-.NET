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
    /// <summary>Each SVG shape element contributes its own geometry to the path: rect, circle, ellipse, line, polyline, polygon and path.</summary>
    private static void AppendShapeGeometry(Ctx ctx, XmlElement elem, StringBuilder sb, BboxAcc bb, List<(double X, double Y)> vertices)
    {
        switch (elem.LocalName)
        {
            case "rect":
            {
                var x = GetLen(elem, "x", ctx.VpW);
                var y = GetLen(elem, "y", ctx.VpH);
                var w = GetLen(elem, "width", ctx.VpW);
                var h = GetLen(elem, "height", ctx.VpH);
                if (w <= 0 || h <= 0) break;
                var rx = GetLen(elem, "rx", ctx.VpW);
                var ry = GetLen(elem, "ry", ctx.VpH);
                if (rx <= 0 && ry > 0) rx = ry;
                if (ry <= 0 && rx > 0) ry = rx;
                rx = Math.Min(rx, w / 2); ry = Math.Min(ry, h / 2);
                bb.Add(x, y); bb.Add(x + w, y + h);
                if (rx > 0.01 && ry > 0.01)
                {
                    const double k = 0.5522847498;
                    var kx = rx * k; var ky = ry * k;
                    sb.Append($"{F(x + rx)} {F(y)} m ");
                    sb.Append($"{F(x + w - rx)} {F(y)} l ");
                    sb.Append($"{F(x + w - rx + kx)} {F(y)} {F(x + w)} {F(y + ry - ky)} {F(x + w)} {F(y + ry)} c ");
                    sb.Append($"{F(x + w)} {F(y + h - ry)} l ");
                    sb.Append($"{F(x + w)} {F(y + h - ry + ky)} {F(x + w - rx + kx)} {F(y + h)} {F(x + w - rx)} {F(y + h)} c ");
                    sb.Append($"{F(x + rx)} {F(y + h)} l ");
                    sb.Append($"{F(x + rx - kx)} {F(y + h)} {F(x)} {F(y + h - ry + ky)} {F(x)} {F(y + h - ry)} c ");
                    sb.Append($"{F(x)} {F(y + ry)} l ");
                    sb.Append($"{F(x)} {F(y + ry - ky)} {F(x + rx - kx)} {F(y)} {F(x + rx)} {F(y)} c h ");
                }
                else
                {
                    sb.Append($"{F(x)} {F(y)} {F(w)} {F(h)} re ");
                }
                break;
            }
            case "circle":
            {
                var cx = GetLen(elem, "cx", ctx.VpW);
                var cy = GetLen(elem, "cy", ctx.VpH);
                var r = GetLen(elem, "r", Diag(ctx));
                if (r <= 0) break;
                bb.Add(cx - r, cy - r); bb.Add(cx + r, cy + r);
                AppendEllipsePath(sb, cx, cy, r, r);
                break;
            }
            case "ellipse":
            {
                var cx = GetLen(elem, "cx", ctx.VpW);
                var cy = GetLen(elem, "cy", ctx.VpH);
                var rx = GetLen(elem, "rx", ctx.VpW);
                var ry = GetLen(elem, "ry", ctx.VpH);
                if (rx <= 0 || ry <= 0) break;
                bb.Add(cx - rx, cy - ry); bb.Add(cx + rx, cy + ry);
                AppendEllipsePath(sb, cx, cy, rx, ry);
                break;
            }
            case "line":
            {
                var x1 = GetLen(elem, "x1", ctx.VpW);
                var y1 = GetLen(elem, "y1", ctx.VpH);
                var x2 = GetLen(elem, "x2", ctx.VpW);
                var y2 = GetLen(elem, "y2", ctx.VpH);
                bb.Add(x1, y1); bb.Add(x2, y2);
                vertices.Add((x1, y1)); vertices.Add((x2, y2));
                sb.Append($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l ");
                break;
            }
            case "polyline":
            case "polygon":
            {
                var points = elem.GetAttribute("points");
                if (string.IsNullOrEmpty(points)) break;
                var nums = Regex.Matches(points, @"-?[\d.]+(?:[eE][+-]?\d+)?").Cast<Match>()
                    .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
                if (nums.Length < 4) break;
                sb.Append($"{F(nums[0])} {F(nums[1])} m ");
                bb.Add(nums[0], nums[1]);
                vertices.Add((nums[0], nums[1]));
                for (int i = 2; i + 1 < nums.Length; i += 2)
                {
                    sb.Append($"{F(nums[i])} {F(nums[i + 1])} l ");
                    bb.Add(nums[i], nums[i + 1]);
                    vertices.Add((nums[i], nums[i + 1]));
                }
                if (elem.LocalName == "polygon") sb.Append("h ");
                break;
            }
            case "path":
            {
                var d = elem.GetAttribute("d");
                if (string.IsNullOrEmpty(d)) break;
                ConvertSvgPathToPdf(d, sb, bb, vertices);
                break;
            }
        }
    }
}
