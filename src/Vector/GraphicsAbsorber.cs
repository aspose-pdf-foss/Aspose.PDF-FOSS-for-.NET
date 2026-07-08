using System.Collections.Generic;
using Aspose.Pdf.Operators;

namespace Aspose.Pdf.Vector;

/// <summary>
/// Extracts the painted vector sub-paths of a page as <see cref="SubPath"/>
/// elements, each carrying its page-space bounding <see cref="GraphicElement.Rectangle"/>.
/// Walks the page content stream tracking the CTM (q/Q/cm); every sub-path that
/// is actually painted (fill/stroke/fill-stroke) becomes one element. Clip-only
/// paths (ending in <c>n</c>) and text are ignored. The elements can be replayed
/// onto another page with <see cref="Page.AddGraphics(GraphicElementCollection)"/>.
/// </summary>
public sealed class GraphicsAbsorber
{
    /// <summary>The extracted vector elements (populated by <see cref="Visit"/>).</summary>
    public GraphicElementCollection Elements { get; } = new();

    public GraphicsAbsorber() { }

    /// <summary>Extract the painted sub-paths of <paramref name="page"/>.</summary>
    public void Visit(Page page)
    {
        if (page is null) return;

        var ctm = new Aspose.Pdf.Matrix();           // current transformation matrix
        var stack = new Stack<Aspose.Pdf.Matrix>();  // q/Q graphics-state CTM stack

        // Sub-paths constructed for the current (not-yet-painted) path.
        var subpaths = new List<(Aspose.Pdf.Matrix ctm, List<Aspose.Pdf.Operator> ops,
            double minX, double minY, double maxX, double maxY)>();

        List<Aspose.Pdf.Operator>? curOps = null;
        Aspose.Pdf.Matrix? curCtm = null;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        bool any = false;
        double curX = 0, curY = 0;   // current point in user space

        void Flush()
        {
            if (curOps is { Count: > 0 } && any)
                subpaths.Add((curCtm!, curOps, minX, minY, maxX, maxY));
            curOps = null; any = false;
        }
        void Start(double ux, double uy)
        {
            Flush();
            curOps = new List<Aspose.Pdf.Operator>();
            curCtm = new Aspose.Pdf.Matrix(ctm);
            any = false; curX = ux; curY = uy;
        }
        void AddPoint(double ux, double uy)
        {
            var (px, py) = curCtm!.TransformPoint(ux, uy);
            if (!any) { minX = maxX = px; minY = maxY = py; any = true; }
            else
            {
                if (px < minX) minX = px; else if (px > maxX) maxX = px;
                if (py < minY) minY = py; else if (py > maxY) maxY = py;
            }
        }
        void Paint(Aspose.Pdf.Operator paintOp)
        {
            Flush();
            foreach (var sp in subpaths)
                Elements.Add(new SubPath(sp.ctm, sp.ops, paintOp,
                    new Rectangle(sp.minX, sp.minY, sp.maxX, sp.maxY)));
            subpaths.Clear();
        }
        void Discard()
        {
            Flush();
            subpaths.Clear();
        }

        foreach (Aspose.Pdf.Operator op in page.Contents)
        {
            switch (op)
            {
                case GSave: stack.Push(new Aspose.Pdf.Matrix(ctm)); break;
                case GRestore: if (stack.Count > 0) ctm = stack.Pop(); break;
                case ConcatenateMatrix cm: ctm = cm.Matrix.Multiply(ctm); break;

                case MoveTo m:
                    Start(m.X, m.Y); curOps!.Add(m); AddPoint(m.X, m.Y); break;
                case Re re:
                    Start(re.X, re.Y); curOps!.Add(re);
                    AddPoint(re.X, re.Y); AddPoint(re.X + re.Width, re.Y);
                    AddPoint(re.X + re.Width, re.Y + re.Height); AddPoint(re.X, re.Y + re.Height);
                    break;
                case LineTo l:
                    if (curOps is null) Start(l.X, l.Y);
                    curOps!.Add(l); AddPoint(l.X, l.Y); curX = l.X; curY = l.Y; break;
                case CurveTo c:
                    if (curOps is null) Start(curX, curY);
                    curOps!.Add(c); AddPoint(c.X1, c.Y1); AddPoint(c.X2, c.Y2); AddPoint(c.X3, c.Y3);
                    curX = c.X3; curY = c.Y3; break;
                case CurveTo1 v:
                    if (curOps is null) Start(curX, curY);
                    curOps!.Add(v); AddPoint(v.X2, v.Y2); AddPoint(v.X3, v.Y3);
                    curX = v.X3; curY = v.Y3; break;
                case CurveTo2 y:
                    if (curOps is null) Start(curX, curY);
                    curOps!.Add(y); AddPoint(y.X1, y.Y1); AddPoint(y.X3, y.Y3);
                    curX = y.X3; curY = y.Y3; break;
                case ClosePath h:
                    curOps?.Add(h); break;

                case Stroke:
                case ClosePathStroke:
                case Fill:
                case ObsoleteFill:
                case EOFill:
                case FillStroke:
                case EOFillStroke:
                case ClosePathFillStroke:
                case ClosePathEOFillStroke:
                    Paint(op); break;

                case EndPath: Discard(); break;   // n — clip / no-paint: drop the path
                default: break;                    // colour/clip/text/etc. don't affect path geometry
            }
        }
    }
}
