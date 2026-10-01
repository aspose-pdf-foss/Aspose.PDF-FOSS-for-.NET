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
// A step of the SVG path conversion.
    private static void Cubic(SvgPathState sp, StringBuilder sb, BboxAcc bb, List<(double X, double Y)>? vertices, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        sb.Append($"{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} c ");
        bb.Add(x1, y1); bb.Add(x2, y2); bb.Add(x3, y3);
        sp.pcx = x2; sp.pcy = y2;
        sp.cx = x3; sp.cy = y3;
        vertices?.Add((sp.cx, sp.cy));
    }

    /// <summary>The move and straight-line commands: absolute and relative moveto, lineto, horizontal and vertical.</summary>
    private static void ApplySvgLineCommand(SvgPathState sp, StringBuilder sb, BboxAcc bb, List<(double X, double Y)>? vertices)
    {
        switch (sp.cmd)
        {
            case 'M' when sp.nums.Count >= 2:
                sp.cx = sp.nums[0]; sp.cy = sp.nums[1];
                sp.sx = sp.cx; sp.sy = sp.cy;
                sb.Append($"{F(sp.cx)} {F(sp.cy)} m ");
                bb.Add(sp.cx, sp.cy);
                vertices?.Add((sp.cx, sp.cy));
                sp.nums.Clear(); sp.cmd = 'L'; sp.prevCmd = 'M';
                break;
            case 'm' when sp.nums.Count >= 2:
                sp.cx += sp.nums[0]; sp.cy += sp.nums[1];
                sp.sx = sp.cx; sp.sy = sp.cy;
                sb.Append($"{F(sp.cx)} {F(sp.cy)} m ");
                bb.Add(sp.cx, sp.cy);
                vertices?.Add((sp.cx, sp.cy));
                sp.nums.Clear(); sp.cmd = 'l'; sp.prevCmd = 'M';
                break;
            case 'L' when sp.nums.Count >= 2:
                sp.cx = sp.nums[0]; sp.cy = sp.nums[1];
                sb.Append($"{F(sp.cx)} {F(sp.cy)} l ");
                bb.Add(sp.cx, sp.cy);
                vertices?.Add((sp.cx, sp.cy));
                sp.nums.Clear(); sp.prevCmd = 'L';
                break;
            case 'l' when sp.nums.Count >= 2:
                sp.cx += sp.nums[0]; sp.cy += sp.nums[1];
                sb.Append($"{F(sp.cx)} {F(sp.cy)} l ");
                bb.Add(sp.cx, sp.cy);
                vertices?.Add((sp.cx, sp.cy));
                sp.nums.Clear(); sp.prevCmd = 'L';
                break;
            case 'H' when sp.nums.Count >= 1:
                sp.cx = sp.nums[0];
                sb.Append($"{F(sp.cx)} {F(sp.cy)} l ");
                bb.Add(sp.cx, sp.cy);
                vertices?.Add((sp.cx, sp.cy));
                sp.nums.Clear(); sp.prevCmd = 'L';
                break;
            case 'h' when sp.nums.Count >= 1:
                sp.cx += sp.nums[0];
                sb.Append($"{F(sp.cx)} {F(sp.cy)} l ");
                bb.Add(sp.cx, sp.cy);
                vertices?.Add((sp.cx, sp.cy));
                sp.nums.Clear(); sp.prevCmd = 'L';
                break;
            case 'V' when sp.nums.Count >= 1:
                sp.cy = sp.nums[0];
                sb.Append($"{F(sp.cx)} {F(sp.cy)} l ");
                bb.Add(sp.cx, sp.cy);
                vertices?.Add((sp.cx, sp.cy));
                sp.nums.Clear(); sp.prevCmd = 'L';
                break;
            case 'v' when sp.nums.Count >= 1:
                sp.cy += sp.nums[0];
                sb.Append($"{F(sp.cx)} {F(sp.cy)} l ");
                bb.Add(sp.cx, sp.cy);
                vertices?.Add((sp.cx, sp.cy));
                sp.nums.Clear(); sp.prevCmd = 'L';
                break;
        }
    }

    /// <summary>The curve commands: cubic and quadratic Beziers with their smooth shorthands, and the elliptical arc.</summary>
    private static void ApplySvgCurveCommand(SvgPathState sp, StringBuilder sb, BboxAcc bb, List<(double X, double Y)>? vertices, char cmd)
    {
        switch (cmd)
        {
            case 'C' when sp.nums.Count >= 6:
                Cubic(sp, sb, bb, vertices, sp.nums[0], sp.nums[1], sp.nums[2], sp.nums[3], sp.nums[4], sp.nums[5]);
                sp.nums.Clear(); sp.prevCmd = 'C';
                break;
            case 'c' when sp.nums.Count >= 6:
                Cubic(sp, sb, bb, vertices, sp.cx + sp.nums[0], sp.cy + sp.nums[1], sp.cx + sp.nums[2], sp.cy + sp.nums[3], sp.cx + sp.nums[4], sp.cy + sp.nums[5]);
                sp.nums.Clear(); sp.prevCmd = 'C';
                break;
            case 'S' when sp.nums.Count >= 4:
            {
                var (rx, ry) = sp.prevCmd == 'C' ? (2 * sp.cx - sp.pcx, 2 * sp.cy - sp.pcy) : (sp.cx, sp.cy);
                Cubic(sp, sb, bb, vertices, rx, ry, sp.nums[0], sp.nums[1], sp.nums[2], sp.nums[3]);
                sp.nums.Clear(); sp.prevCmd = 'C';
                break;
            }
            case 's' when sp.nums.Count >= 4:
            {
                var (rx, ry) = sp.prevCmd == 'C' ? (2 * sp.cx - sp.pcx, 2 * sp.cy - sp.pcy) : (sp.cx, sp.cy);
                Cubic(sp, sb, bb, vertices, rx, ry, sp.cx + sp.nums[0], sp.cy + sp.nums[1], sp.cx + sp.nums[2], sp.cy + sp.nums[3]);
                sp.nums.Clear(); sp.prevCmd = 'C';
                break;
            }
            case 'Q' when sp.nums.Count >= 4:
            {
                var qx = sp.nums[0]; var qy = sp.nums[1];
                var ex = sp.nums[2]; var ey = sp.nums[3];
                Cubic(sp, sb, bb, vertices, sp.cx + 2.0 / 3.0 * (qx - sp.cx), sp.cy + 2.0 / 3.0 * (qy - sp.cy),
                    ex + 2.0 / 3.0 * (qx - ex), ey + 2.0 / 3.0 * (qy - ey), ex, ey);
                sp.pqx = qx; sp.pqy = qy;
                sp.nums.Clear(); sp.prevCmd = 'Q';
                break;
            }
            case 'q' when sp.nums.Count >= 4:
            {
                var qx = sp.cx + sp.nums[0]; var qy = sp.cy + sp.nums[1];
                var ex = sp.cx + sp.nums[2]; var ey = sp.cy + sp.nums[3];
                Cubic(sp, sb, bb, vertices, sp.cx + 2.0 / 3.0 * (qx - sp.cx), sp.cy + 2.0 / 3.0 * (qy - sp.cy),
                    ex + 2.0 / 3.0 * (qx - ex), ey + 2.0 / 3.0 * (qy - ey), ex, ey);
                sp.pqx = qx; sp.pqy = qy;
                sp.nums.Clear(); sp.prevCmd = 'Q';
                break;
            }
            case 'T' or 't' when sp.nums.Count >= 2:
            {
                var (qx, qy) = sp.prevCmd == 'Q' ? (2 * sp.cx - sp.pqx, 2 * sp.cy - sp.pqy) : (sp.cx, sp.cy);
                var ex = cmd == 'T' ? sp.nums[0] : sp.cx + sp.nums[0];
                var ey = cmd == 'T' ? sp.nums[1] : sp.cy + sp.nums[1];
                Cubic(sp, sb, bb, vertices, sp.cx + 2.0 / 3.0 * (qx - sp.cx), sp.cy + 2.0 / 3.0 * (qy - sp.cy),
                    ex + 2.0 / 3.0 * (qx - ex), ey + 2.0 / 3.0 * (qy - ey), ex, ey);
                sp.pqx = qx; sp.pqy = qy;
                sp.nums.Clear(); sp.prevCmd = 'Q';
                break;
            }
            case 'A' or 'a' when sp.nums.Count >= 7:
            {
                var ex = cmd == 'A' ? sp.nums[5] : sp.cx + sp.nums[5];
                var ey = cmd == 'A' ? sp.nums[6] : sp.cy + sp.nums[6];
                (sp.pcx, sp.pcy) = ArcToBeziers(sb, bb, sp.cx, sp.cy, sp.nums[0], sp.nums[1], sp.nums[2],
                    sp.nums[3] != 0, sp.nums[4] != 0, ex, ey, sp.pcx, sp.pcy);
                sp.cx = ex; sp.cy = ey;
                vertices?.Add((sp.cx, sp.cy));
                sp.nums.Clear(); sp.prevCmd = 'A';
                break;
            }
        }
    }
}
