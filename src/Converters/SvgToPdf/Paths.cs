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
    /// <summary>Parse a <c>matrix(a,b,c,d,e,f)</c> transform, or null if the transform
    /// is missing or contains any other function.</summary>
    private static double[]? ParseMatrixOnly(string transform)
    {
        if (string.IsNullOrEmpty(transform)) return null;
        var m = Regex.Match(transform, @"^\s*matrix\s*\(([^)]*)\)\s*$");
        if (!m.Success) return null;
        var nums = Regex.Matches(m.Groups[1].Value, @"-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")
            .Cast<Match>()
            .Select(x => double.Parse(x.Value, CultureInfo.InvariantCulture))
            .ToArray();
        return nums.Length >= 6 ? nums : null;
    }

    private static void ConvertSvgPathToPdf(string d, StringBuilder sb, BboxAcc bb,
        List<(double X, double Y)>? vertices = null)
    {
        var tokens = Regex.Matches(d, @"[MmLlHhVvCcSsQqTtAaZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][+-]?\d+)?");
        double cx = 0, cy = 0; // current point
        double sx = 0, sy = 0; // subpath start
        double pcx = 0, pcy = 0; // previous cubic control (for S/s)
        double pqx = 0, pqy = 0; // previous quadratic control (for T/t)
        char prevCmd = ' ';

        var nums = new List<double>();
        char cmd = 'M';

        void Cubic(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            sb.Append($"{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} c ");
            bb.Add(x1, y1); bb.Add(x2, y2); bb.Add(x3, y3);
            pcx = x2; pcy = y2;
            cx = x3; cy = y3;
            vertices?.Add((cx, cy));
        }

        foreach (Match token in tokens)
        {
            var val = token.Value;
            if (val.Length == 1 && char.IsLetter(val[0]) && !char.IsDigit(val[0]))
            {
                cmd = val[0];
                nums.Clear();
                if (cmd is 'Z' or 'z')
                {
                    sb.Append("h ");
                    cx = sx; cy = sy;
                    prevCmd = 'Z';
                }
                continue;
            }

            if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                continue;
            nums.Add(num);

            switch (cmd)
            {
                case 'M' when nums.Count >= 2:
                    cx = nums[0]; cy = nums[1];
                    sx = cx; sy = cy;
                    sb.Append($"{F(cx)} {F(cy)} m ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); cmd = 'L'; prevCmd = 'M';
                    break;
                case 'm' when nums.Count >= 2:
                    cx += nums[0]; cy += nums[1];
                    sx = cx; sy = cy;
                    sb.Append($"{F(cx)} {F(cy)} m ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); cmd = 'l'; prevCmd = 'M';
                    break;
                case 'L' when nums.Count >= 2:
                    cx = nums[0]; cy = nums[1];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'l' when nums.Count >= 2:
                    cx += nums[0]; cy += nums[1];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'H' when nums.Count >= 1:
                    cx = nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'h' when nums.Count >= 1:
                    cx += nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'V' when nums.Count >= 1:
                    cy = nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'v' when nums.Count >= 1:
                    cy += nums[0];
                    sb.Append($"{F(cx)} {F(cy)} l ");
                    bb.Add(cx, cy);
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'L';
                    break;
                case 'C' when nums.Count >= 6:
                    Cubic(nums[0], nums[1], nums[2], nums[3], nums[4], nums[5]);
                    nums.Clear(); prevCmd = 'C';
                    break;
                case 'c' when nums.Count >= 6:
                    Cubic(cx + nums[0], cy + nums[1], cx + nums[2], cy + nums[3], cx + nums[4], cy + nums[5]);
                    nums.Clear(); prevCmd = 'C';
                    break;
                case 'S' when nums.Count >= 4:
                {
                    var (rx, ry) = prevCmd == 'C' ? (2 * cx - pcx, 2 * cy - pcy) : (cx, cy);
                    Cubic(rx, ry, nums[0], nums[1], nums[2], nums[3]);
                    nums.Clear(); prevCmd = 'C';
                    break;
                }
                case 's' when nums.Count >= 4:
                {
                    var (rx, ry) = prevCmd == 'C' ? (2 * cx - pcx, 2 * cy - pcy) : (cx, cy);
                    Cubic(rx, ry, cx + nums[0], cy + nums[1], cx + nums[2], cy + nums[3]);
                    nums.Clear(); prevCmd = 'C';
                    break;
                }
                case 'Q' when nums.Count >= 4:
                {
                    var qx = nums[0]; var qy = nums[1];
                    var ex = nums[2]; var ey = nums[3];
                    Cubic(cx + 2.0 / 3.0 * (qx - cx), cy + 2.0 / 3.0 * (qy - cy),
                        ex + 2.0 / 3.0 * (qx - ex), ey + 2.0 / 3.0 * (qy - ey), ex, ey);
                    pqx = qx; pqy = qy;
                    nums.Clear(); prevCmd = 'Q';
                    break;
                }
                case 'q' when nums.Count >= 4:
                {
                    var qx = cx + nums[0]; var qy = cy + nums[1];
                    var ex = cx + nums[2]; var ey = cy + nums[3];
                    Cubic(cx + 2.0 / 3.0 * (qx - cx), cy + 2.0 / 3.0 * (qy - cy),
                        ex + 2.0 / 3.0 * (qx - ex), ey + 2.0 / 3.0 * (qy - ey), ex, ey);
                    pqx = qx; pqy = qy;
                    nums.Clear(); prevCmd = 'Q';
                    break;
                }
                case 'T' or 't' when nums.Count >= 2:
                {
                    var (qx, qy) = prevCmd == 'Q' ? (2 * cx - pqx, 2 * cy - pqy) : (cx, cy);
                    var ex = cmd == 'T' ? nums[0] : cx + nums[0];
                    var ey = cmd == 'T' ? nums[1] : cy + nums[1];
                    Cubic(cx + 2.0 / 3.0 * (qx - cx), cy + 2.0 / 3.0 * (qy - cy),
                        ex + 2.0 / 3.0 * (qx - ex), ey + 2.0 / 3.0 * (qy - ey), ex, ey);
                    pqx = qx; pqy = qy;
                    nums.Clear(); prevCmd = 'Q';
                    break;
                }
                case 'A' or 'a' when nums.Count >= 7:
                {
                    var ex = cmd == 'A' ? nums[5] : cx + nums[5];
                    var ey = cmd == 'A' ? nums[6] : cy + nums[6];
                    ArcToBeziers(sb, bb, cx, cy, nums[0], nums[1], nums[2],
                        nums[3] != 0, nums[4] != 0, ex, ey, ref pcx, ref pcy);
                    cx = ex; cy = ey;
                    vertices?.Add((cx, cy));
                    nums.Clear(); prevCmd = 'A';
                    break;
                }
            }
        }
    }

    /// <summary>Convert an SVG elliptical arc to cubic Bezier segments
    /// (endpoint → center parameterization, PDF-ready).</summary>
    private static void ArcToBeziers(StringBuilder sb, BboxAcc bb, double x1, double y1,
        double rx, double ry, double rotDeg, bool largeArc, bool sweep,
        double x2, double y2, ref double pcx, ref double pcy)
    {
        if (rx == 0 || ry == 0 || (x1 == x2 && y1 == y2))
        {
            sb.Append($"{F(x2)} {F(y2)} l ");
            bb.Add(x2, y2);
            return;
        }
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        var phi = rotDeg * Math.PI / 180.0;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);

        // Step 1: compute (x1', y1')
        var dx2 = (x1 - x2) / 2.0;
        var dy2 = (y1 - y2) / 2.0;
        var x1p = cosPhi * dx2 + sinPhi * dy2;
        var y1p = -sinPhi * dx2 + cosPhi * dy2;

        // Correct radii
        var lam = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lam > 1)
        {
            var s = Math.Sqrt(lam);
            rx *= s; ry *= s;
        }

        // Step 2: compute (cx', cy')
        var rxSq = rx * rx; var rySq = ry * ry;
        var x1pSq = x1p * x1p; var y1pSq = y1p * y1p;
        var num = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
        if (num < 0) num = 0;
        var den = rxSq * y1pSq + rySq * x1pSq;
        var coef = den == 0 ? 0 : Math.Sqrt(num / den);
        if (largeArc == sweep) coef = -coef;
        var cxp = coef * (rx * y1p / ry);
        var cyp = coef * (-ry * x1p / rx);

        // Step 3: center
        var cxc = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2;
        var cyc = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2;

        // Step 4: angles
        double Angle(double ux, double uy, double vx, double vy)
        {
            var dot = ux * vx + uy * vy;
            var len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            var ang = Math.Acos(Math.Clamp(dot / len, -1, 1));
            if (ux * vy - uy * vx < 0) ang = -ang;
            return ang;
        }
        var theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var dTheta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && dTheta > 0) dTheta -= 2 * Math.PI;
        else if (sweep && dTheta < 0) dTheta += 2 * Math.PI;

        // Split into segments of at most 90°
        var segments = (int)Math.Ceiling(Math.Abs(dTheta) / (Math.PI / 2));
        if (segments == 0) segments = 1;
        var delta = dTheta / segments;
        var t = 4.0 / 3.0 * Math.Tan(delta / 4);

        var cosT1 = Math.Cos(theta1);
        var sinT1 = Math.Sin(theta1);
        var curX = x1; var curY = y1;
        for (var i = 0; i < segments; i++)
        {
            var theta2 = theta1 + delta;
            var cosT2 = Math.Cos(theta2);
            var sinT2 = Math.Sin(theta2);

            // Endpoint of this segment
            var ex = cxc + rx * (cosPhi * cosT2) - ry * (sinPhi * sinT2);
            var ey = cyc + rx * (sinPhi * cosT2) + ry * (cosPhi * sinT2);

            // Control points
            var c1x = curX + t * (-rx * cosPhi * sinT1 - ry * sinPhi * cosT1);
            var c1y = curY + t * (-rx * sinPhi * sinT1 + ry * cosPhi * cosT1);
            var c2x = ex - t * (-rx * cosPhi * sinT2 - ry * sinPhi * cosT2);
            var c2y = ey - t * (-rx * sinPhi * sinT2 + ry * cosPhi * cosT2);

            sb.Append($"{F(c1x)} {F(c1y)} {F(c2x)} {F(c2y)} {F(ex)} {F(ey)} c ");
            bb.Add(c1x, c1y); bb.Add(c2x, c2y); bb.Add(ex, ey);
            pcx = c2x; pcy = c2y;

            theta1 = theta2;
            cosT1 = cosT2; sinT1 = sinT2;
            curX = ex; curY = ey;
        }
    }

    /// <summary>Emit the element's transform functions as <c>cm</c> operators and
    /// return the CTM composed with them.</summary>
    private static double[] ApplyTransform(XmlElement elem, StringBuilder sb, double[] ctm)
    {
        var transform = elem.GetAttribute("transform");
        if (string.IsNullOrEmpty(transform)) return ctm;

        // A transform attribute may chain several functions, e.g.
        // "translate(0,540) scale(1,-1)". SVG applies them left-to-right, so emit
        // a `cm` per function in the same order.
        var result = ctm;
        foreach (var m in EnumerateTransforms(transform))
        {
            sb.Append($"{F(m[0])} {F(m[1])} {F(m[2])} {F(m[3])} {F(m[4])} {F(m[5])} cm\n");
            result = Mul(m, result);
        }
        return result;
    }

    /// <summary>Parse a full transform list into a single composed matrix (or null).</summary>
    private static double[]? ParseTransformMatrix(string transform)
    {
        if (string.IsNullOrEmpty(transform)) return null;
        double[]? total = null;
        foreach (var m in EnumerateTransforms(transform))
            total = total is null ? m : Mul(m, total);
        return total;
    }

    private static IEnumerable<double[]> EnumerateTransforms(string transform)
    {
        foreach (Match fn in Regex.Matches(transform, @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)"))
        {
            var op = fn.Groups[1].Value;
            var args = Regex.Matches(fn.Groups[2].Value, @"-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?")
                .Cast<Match>()
                .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
                .ToArray();

            switch (op)
            {
                case "matrix" when args.Length >= 6:
                    yield return new[] { args[0], args[1], args[2], args[3], args[4], args[5] };
                    break;
                case "translate" when args.Length >= 1:
                    yield return new[] { 1.0, 0, 0, 1, args[0], args.Length > 1 ? args[1] : 0 };
                    break;
                case "scale" when args.Length >= 1:
                    yield return new[] { args[0], 0, 0, args.Length > 1 ? args[1] : args[0], 0, 0 };
                    break;
                case "rotate" when args.Length >= 1:
                {
                    var rad = args[0] * Math.PI / 180.0;
                    var cos = Math.Cos(rad);
                    var sin = Math.Sin(rad);
                    if (args.Length >= 3)
                    {
                        // rotate(angle, cx, cy) == translate(cx,cy) rotate translate(-cx,-cy)
                        yield return new[] { 1.0, 0, 0, 1, args[1], args[2] };
                        yield return new[] { cos, sin, -sin, cos, 0, 0 };
                        yield return new[] { 1.0, 0, 0, 1, -args[1], -args[2] };
                    }
                    else
                    {
                        yield return new[] { cos, sin, -sin, cos, 0, 0 };
                    }
                    break;
                }
                case "skewX" when args.Length >= 1:
                    yield return new[] { 1.0, 0, Math.Tan(args[0] * Math.PI / 180.0), 1, 0, 0 };
                    break;
                case "skewY" when args.Length >= 1:
                    yield return new[] { 1.0, Math.Tan(args[0] * Math.PI / 180.0), 0, 1, 0, 0 };
                    break;
            }
        }
    }

    private static void AppendEllipsePath(StringBuilder sb, double cx, double cy, double rx, double ry)
    {
        // Approximate ellipse with 4 cubic Bezier curves
        const double k = 0.5522847498; // 4/3 * (sqrt(2) - 1)
        var kx = rx * k;
        var ky = ry * k;

        sb.Append($"{F(cx - rx)} {F(cy)} m ");
        sb.Append($"{F(cx - rx)} {F(cy - ky)} {F(cx - kx)} {F(cy - ry)} {F(cx)} {F(cy - ry)} c ");
        sb.Append($"{F(cx + kx)} {F(cy - ry)} {F(cx + rx)} {F(cy - ky)} {F(cx + rx)} {F(cy)} c ");
        sb.Append($"{F(cx + rx)} {F(cy + ky)} {F(cx + kx)} {F(cy + ry)} {F(cx)} {F(cy + ry)} c ");
        sb.Append($"{F(cx - kx)} {F(cy + ry)} {F(cx - rx)} {F(cy + ky)} {F(cx - rx)} {F(cy)} c h ");
    }

    /// <summary>Length of an attribute; <c>%</c> resolves against <paramref name="refLen"/>.</summary>
    private static double GetLen(XmlElement elem, string attr, double refLen)
    {
        var val = elem.GetAttribute(attr);
        if (string.IsNullOrEmpty(val)) return 0;
        val = val.Trim();
        if (val.EndsWith("%"))
            return ParseLength(val[..^1]) / 100.0 * refLen;
        return ParseLength(val);
    }

    /// <summary>First value of a (possibly space/comma separated) coordinate list.</summary>
    private static double GetFirstLen(XmlElement elem, string attr, double refLen)
    {
        var val = elem.GetAttribute(attr);
        if (string.IsNullOrEmpty(val)) return 0;
        var first = val.Split(new[] { ' ', ',', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(first)) return 0;
        if (first.EndsWith("%"))
            return ParseLength(first[..^1]) / 100.0 * refLen;
        return ParseLength(first);
    }

    /// <summary>Root width/height attribute → points:
    /// unitless/px ×0.75, pt ×1, in ×72, pc ×12, cm ×28.346, mm ×2.8346, em/ex ×1.
    /// Missing, percentage, zero, or unparsable → 0 (caller defaults to 500pt).</summary>
    private static double ParseRootLength(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return 0;
        val = val.Trim();
        if (val.EndsWith("%")) return 0;
        var m = Regex.Match(val, @"^([-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][+-]?\d+)?)\s*(px|pt|em|ex|cm|mm|in|pc)?$");
        if (!m.Success) return 0;
        var num = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var factor = m.Groups[2].Value switch
        {
            "pt" => 1.0,
            "in" => 72.0,
            "pc" => 12.0,
            "cm" => 28.346,
            "mm" => 2.8346,
            "em" or "ex" => 1.0,
            // px or unitless: CSS 96-per-inch pixels
            _ => 0.75,
        };
        return num * factor;
    }

    /// <summary>Parse a CSS/SVG length into USER units (CSS px): px/unitless ×1,
    /// pt ×4/3, in ×96, cm ×37.795, mm ×3.7795, pc ×16, em ×16, ex ×8.</summary>
    private static double ParseLength(string val)
    {
        val = val.Trim();
        var m = Regex.Match(val, @"^([-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][+-]?\d+)?)\s*(px|pt|em|ex|cm|mm|in|pc|%)?$");
        if (!m.Success)
        {
            // Fall back to the first number in the string.
            var n = Regex.Match(val, @"[-+]?(?:\d*\.\d+|\d+\.?)");
            return n.Success ? double.Parse(n.Value, CultureInfo.InvariantCulture) : 0;
        }
        var num = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value switch
        {
            "pt" => num * 4.0 / 3.0,
            "in" => num * 96.0,
            "cm" => num * 96.0 / 2.54,
            "mm" => num * 96.0 / 25.4,
            "pc" => num * 16.0,
            "em" => num * 16.0,
            "ex" => num * 8.0,
            // px, %, unitless: 1 user unit
            _ => num,
        };
    }

    private static double[] Identity6() => new[] { 1.0, 0, 0, 1, 0, 0 };

    /// <summary>Compose two affine matrices (row-vector convention): m1 × m2.</summary>
    private static double[] Mul(double[] m1, double[] m2) => new[]
    {
        m1[0] * m2[0] + m1[1] * m2[2],
        m1[0] * m2[1] + m1[1] * m2[3],
        m1[2] * m2[0] + m1[3] * m2[2],
        m1[2] * m2[1] + m1[3] * m2[3],
        m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
        m1[4] * m2[1] + m1[5] * m2[3] + m2[5],
    };

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
