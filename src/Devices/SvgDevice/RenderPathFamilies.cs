using System.Globalization;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

public sealed partial class SvgDevice
{
    /// <summary>The path construction operators: moves, lines, curves, closes and rectangles into the pending path data.</summary>
    private static bool RenderSvgPathBuildOperator(SvgRenderState sv, string op)
    {
        switch (op)
        {
            // --- Path construction (coordinates transformed by the CTM) ---
            case "m": // moveto
                if (sv.operands.Count >= 2)
                {
                    sv.curX = Num(sv.operands[0]); sv.curY = Num(sv.operands[1]);
                    var (px, py) = Apply(sv.gs.Ctm, sv.curX, sv.curY);
                    sv.pathData.Append($"M{F(px)} {F(py)} ");
                }
                break;
            case "l": // lineto
                if (sv.operands.Count >= 2)
                {
                    sv.curX = Num(sv.operands[0]); sv.curY = Num(sv.operands[1]);
                    var (px, py) = Apply(sv.gs.Ctm, sv.curX, sv.curY);
                    // Polylines are emitted as edge
                    // pairs, so every line vertex appears twice —
                    // geometrically a no-op, kept so the path matches the expected output.
                    sv.pathData.Append($"L{F(px)} {F(py)} L{F(px)} {F(py)} ");
                }
                break;
            case "c": // curveto
                if (sv.operands.Count >= 6)
                {
                    var (x1, y1) = Apply(sv.gs.Ctm, Num(sv.operands[0]), Num(sv.operands[1]));
                    var (x2, y2) = Apply(sv.gs.Ctm, Num(sv.operands[2]), Num(sv.operands[3]));
                    sv.curX = Num(sv.operands[4]); sv.curY = Num(sv.operands[5]);
                    var (x3, y3) = Apply(sv.gs.Ctm, sv.curX, sv.curY);
                    sv.pathData.Append($"C{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} ");
                }
                break;
            case "v": // curveto (initial point replicated)
                if (sv.operands.Count >= 4)
                {
                    var (x1, y1) = Apply(sv.gs.Ctm, sv.curX, sv.curY);
                    var (x2, y2) = Apply(sv.gs.Ctm, Num(sv.operands[0]), Num(sv.operands[1]));
                    sv.curX = Num(sv.operands[2]); sv.curY = Num(sv.operands[3]);
                    var (x3, y3) = Apply(sv.gs.Ctm, sv.curX, sv.curY);
                    sv.pathData.Append($"C{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} ");
                }
                break;
            case "y": // curveto (final point replicated)
                if (sv.operands.Count >= 4)
                {
                    var (x1, y1) = Apply(sv.gs.Ctm, Num(sv.operands[0]), Num(sv.operands[1]));
                    sv.curX = Num(sv.operands[2]); sv.curY = Num(sv.operands[3]);
                    var (x3, y3) = Apply(sv.gs.Ctm, sv.curX, sv.curY);
                    sv.pathData.Append($"C{F(x1)} {F(y1)} {F(x3)} {F(y3)} {F(x3)} {F(y3)} ");
                }
                break;
            case "h": // closepath
                sv.pathData.Append("Z ");
                break;
            case "re":
                if (sv.operands.Count >= 4)
                {
                    var rx = Num(sv.operands[0]); var ry = Num(sv.operands[1]);
                    var rw = Num(sv.operands[2]); var rh = Num(sv.operands[3]);
                    var (p1x, p1y) = Apply(sv.gs.Ctm, rx, ry);
                    var (p2x, p2y) = Apply(sv.gs.Ctm, rx + rw, ry);
                    var (p3x, p3y) = Apply(sv.gs.Ctm, rx + rw, ry + rh);
                    var (p4x, p4y) = Apply(sv.gs.Ctm, rx, ry + rh);
                    sv.pathData.Append($"M{F(p1x)} {F(p1y)} L{F(p2x)} {F(p2y)} L{F(p2x)} {F(p2y)} " +
                        $"L{F(p3x)} {F(p3y)} L{F(p3x)} {F(p3y)} L{F(p4x)} {F(p4y)} L{F(p4x)} {F(p4y)} " +
                        $"L{F(p1x)} {F(p1y)} Z ");
                    sv.curX = rx; sv.curY = ry;
                }
                break;

        }
        return false;
    }

    /// <summary>The painting and clipping operators: the pending path stroked, filled, both or dropped, with its clip.</summary>
    private bool RenderSvgPaintOperator(SvgRenderState sv, StringBuilder sb, string op)
    {
        switch (op)
        {
            // --- Clipping: takes effect once the path is painted or ended (PDF 32000-1 §8.5.4) ---
            case "W":
                sv.clipPending = true; sv.clipEvenOdd = false;
                return false;
            case "W*":
                sv.clipPending = true; sv.clipEvenOdd = true;
                return false;
        }

        if (op is "s" or "b" or "b*") sv.pathData.Append("Z "); // the closing variants
        var d = sv.pathData.ToString().Trim();
        if (d.Length > 0)
        {
            switch (op)
            {
                // --- Path painting ---
                case "S" or "s": // stroke; close and stroke
                    EmitPath(sb, sv.gs, d, stroke: true, fill: false, evenOdd: false);
                    break;
                case "f" or "F": // fill (nonzero)
                    EmitPath(sb, sv.gs, d, stroke: false, fill: true, evenOdd: false);
                    break;
                case "f*": // fill (even-odd)
                    EmitPath(sb, sv.gs, d, stroke: false, fill: true, evenOdd: true);
                    break;
                case "B" or "b": // fill and stroke (nonzero); close, fill and stroke
                    EmitPath(sb, sv.gs, d, stroke: true, fill: true, evenOdd: false);
                    break;
                case "B*" or "b*": // fill and stroke (even-odd); close, fill and stroke
                    EmitPath(sb, sv.gs, d, stroke: true, fill: true, evenOdd: true);
                    break;
                // "n": end path (no fill, no stroke)
            }
            // The clip is the painted path, so content outside it - a text layer parked in a
            // strip, say - stays unseen as on the page.
            if (sv.clipPending)
                sv.gs.ClipId = AddClipPath(d, sv.clipEvenOdd, sv.gs.ClipId);
        }
        sv.clipPending = false;
        sv.pathData.Clear();
        return false;
    }
}
