using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>The underline-removal predicate: true for a content operator that belongs to a
    /// removed underline (its paint, or a rule matched to a doomed fragment).</summary>
    private bool IsRemovedUnderlineOp(Operator op, System.Collections.Generic.HashSet<Operator> doomedPaint, List<(double X, double Y, double W, double H)> targets, System.Collections.Generic.Dictionary<Operator, Operator> paintOfRe)
    {
        if (doomedPaint.Remove(op)) return true;
        // Content operators materialize lazily as generic operators, so match by
        // command name + operands rather than only the typed classes.
        double x, y, w, h;
        if (op is Aspose.Pdf.Operators.Re re)
        {
            (x, y, w, h) = (re.X, re.Y, re.Width, re.Height);
        }
        else if (op.CommandName == "re")
        {
            var nums = ParseLeadingNumbers(op.ToString());
            if (nums is not { Length: >= 4 }) return false;
            (x, y, w, h) = (nums[0], nums[1], nums[2], nums[3]);
        }
        else if (op is Aspose.Pdf.Operators.MoveTo || op is Aspose.Pdf.Operators.LineTo
            || op.CommandName is "m" or "l")
        {
            // A stroked-line underline ("x1 y m x2 y l S") was captured with
            // raw X = left end, W = span, Y = the stroke's y. Splice both path
            // points whose coordinates hit either end of a captured target;
            // the leftover S strokes an empty path and paints nothing.
            double px, py;
            if (op is Aspose.Pdf.Operators.MoveTo mv) { px = mv.X; py = mv.Y; }
            else if (op is Aspose.Pdf.Operators.LineTo lt) { px = lt.X; py = lt.Y; }
            else
            {
                var nums = ParseLeadingNumbers(op.ToString());
                if (nums is not { Length: >= 2 }) return false;
                (px, py) = (nums[0], nums[1]);
            }
            foreach (var t in targets)
            {
                if (Math.Abs(py - t.Y) < 0.75 &&
                    (Math.Abs(px - t.X) < 0.75 || Math.Abs(px - (t.X + t.W)) < 0.75))
                    return true;
            }
            return false;
        }
        else return false;

        foreach (var t in targets)
        {
            if (Math.Abs(x - t.X) < 0.5 && Math.Abs(y - t.Y) < 0.5 &&
                Math.Abs(w - t.W) < 0.5 && Math.Abs(h - t.H) < 0.5)
            {
                if (paintOfRe.TryGetValue(op, out var paint)) doomedPaint.Add(paint);
                return true;
            }
        }
        return false;
    }
}
