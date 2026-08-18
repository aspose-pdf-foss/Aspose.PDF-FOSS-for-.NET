using System.Collections.Generic;
using Aspose.Pdf.Operators;

namespace Aspose.Pdf.Vector;

/// <summary>
/// Extracts the painted vector sub-paths of a page as <see cref="SubPath"/>
/// elements, each carrying its page-space bounding <see cref="GraphicElement.Rectangle"/>,
/// and Form XObject invocations as <see cref="XFormPlacement"/> elements whose
/// children are extracted in form space. Walks the content stream tracking the
/// CTM (q/Q/cm) and the paint colours; every sub-path that is actually painted
/// (fill/stroke/fill-stroke) becomes one element. Clip-only paths (ending in
/// <c>n</c>) and text are ignored. The elements can be replayed onto another
/// page with <see cref="Page.AddGraphics(GraphicElementCollection)"/>.
/// </summary>
public sealed class GraphicsAbsorber
{
    /// <summary>The extracted vector elements (populated by <see cref="Visit"/>).</summary>
    public GraphicElementCollection Elements { get; } = new();

    public GraphicsAbsorber() { }

    /// <summary>Extract the painted sub-paths and form placements of <paramref name="page"/>.</summary>
    public void Visit(Page page)
    {
        if (page is null) return;
        var resources = Devices.SoftwarePageRenderer.ResolveInheritedPageResources(page.Dict, page.Reader);
        Walk(page.Contents, resources, page.Reader, Elements, depth: 0);
        // Bind each top-level element to its source page so a later Position change can
        // rewrite the page content in place.
        foreach (var element in Elements)
            element.BindSource(page, Elements);
    }

    /// <summary>Mutable per-path graphics state tracked during the walk.</summary>
    private sealed class WalkState
    {
        public Aspose.Pdf.Matrix Ctm = new();
        public (double R, double G, double B) Fill = (0, 0, 0);
        public (double R, double G, double B) Stroke = (0, 0, 0);
        public double LineWidth = 1.0;
        public int LineJoin;

        public WalkState Clone() => new()
        {
            Ctm = new Aspose.Pdf.Matrix(Ctm),
            Fill = Fill, Stroke = Stroke,
            LineWidth = LineWidth, LineJoin = LineJoin,
        };
    }

    private const int MaxFormDepth = 12;

    private static void Walk(IEnumerable<Aspose.Pdf.Operator> ops, Core.PdfDictionary? resources,
        IO.PdfReader reader, GraphicElementCollection elements, int depth)
    {
        var gs = new WalkState();
        var stack = new Stack<WalkState>();

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
            curCtm = new Aspose.Pdf.Matrix(gs.Ctm);
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
        void Paint(Aspose.Pdf.Operator paintOp, bool fill, bool stroke, bool evenOdd)
        {
            Flush();
            var style = new SubPathStyle(
                fill ? gs.Fill : null,
                stroke ? gs.Stroke : null,
                gs.LineWidth, gs.LineJoin, evenOdd);
            foreach (var sp in subpaths)
                elements.Add(new SubPath(sp.ctm, sp.ops, paintOp,
                    new Rectangle(sp.minX, sp.minY, sp.maxX, sp.maxY), style));
            subpaths.Clear();
        }
        void Discard()
        {
            Flush();
            subpaths.Clear();
        }

        foreach (Aspose.Pdf.Operator op in ops)
        {
            switch (op)
            {
                case GSave: stack.Push(gs.Clone()); break;
                case GRestore: if (stack.Count > 0) gs = stack.Pop(); break;
                case ConcatenateMatrix cm: gs.Ctm = cm.Matrix.Multiply(gs.Ctm); break;

                case SetRGBColor rgb:
                    gs.Fill = (rgb.R, rgb.G, rgb.B);
                    break;
                case SetGray or SetCMYKColor or SetColor or SetAdvancedColor:
                {
                    var c = ((SetColorOperator)op).getColor();
                    gs.Fill = (c.R / 255.0, c.G / 255.0, c.B / 255.0);
                    break;
                }
                case SetRGBColorStroke or SetGrayStroke or SetCMYKColorStroke
                    or SetColorStroke or SetAdvancedColorStroke:
                {
                    var c = ((SetColorOperator)op).getColor();
                    gs.Stroke = (c.R / 255.0, c.G / 255.0, c.B / 255.0);
                    break;
                }
                case SetLineWidth lw: gs.LineWidth = lw.Width; break;
                case SetLineJoin lj: gs.LineJoin = (int)lj.Join; break;

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

                case Stroke or ClosePathStroke:
                    Paint(op, fill: false, stroke: true, evenOdd: false); break;
                case Fill or ObsoleteFill:
                    Paint(op, fill: true, stroke: false, evenOdd: false); break;
                case EOFill:
                    Paint(op, fill: true, stroke: false, evenOdd: true); break;
                case FillStroke or ClosePathFillStroke:
                    Paint(op, fill: true, stroke: true, evenOdd: false); break;
                case EOFillStroke or ClosePathEOFillStroke:
                    Paint(op, fill: true, stroke: true, evenOdd: true); break;

                case EndPath: Discard(); break;   // n — clip / no-paint: drop the path

                case Do d when depth < MaxFormDepth:
                    VisitForm(d, resources, reader, gs, elements, depth);
                    break;

                default: break;                    // colour/clip/text/etc. don't affect path geometry
            }
        }
    }

    /// <summary>Extract a Form XObject invocation as an <see cref="XFormPlacement"/>:
    /// children are walked in the form's own space; the placement rectangle is the
    /// form /BBox (under its /Matrix) mapped through the current CTM.</summary>
    private static void VisitForm(Do d, Core.PdfDictionary? resources, IO.PdfReader reader,
        WalkState gs, GraphicElementCollection elements, int depth)
    {
        if (resources is null) return;
        var xobjDict = reader.ResolveDict(resources.Get("XObject"));
        var xobj = xobjDict is null ? null : reader.ResolveStream(xobjDict.Get(d.Name));
        if (xobj is null || xobj.Dict.GetName("Subtype") != "Form") return;

        byte[] bytes;
        try { bytes = reader.DecodeStream(xobj); }
        catch { return; }

        var formResources = reader.ResolveDict(xobj.Dict.Get("Resources")) ?? resources;
        var formMatrix = ReadMatrix(xobj.Dict.Get("Matrix"), reader);

        // Children in form space (the form's own /Matrix applied, placement CTM not).
        var children = new GraphicElementCollection();
        var formOps = new List<Aspose.Pdf.Operator>();
        foreach (var raw in ContentStreamOperatorParser.ParseOperators(bytes))
            formOps.Add(TypedOperatorParser.Parse(raw));
        Walk(formOps, formResources, reader, children, depth + 1);

        // Placement rectangle: /BBox under /Matrix × CTM.
        var rect = new Rectangle(0, 0, 0, 0);
        if (reader.Resolve(xobj.Dict.Get("BBox")) is Core.PdfArray bbox && bbox.Count >= 4)
        {
            var full = formMatrix is null ? gs.Ctm : formMatrix.Multiply(gs.Ctm);
            var (x0, y0) = full.TransformPoint(NumOf(bbox[0]), NumOf(bbox[1]));
            var (x1, y1) = full.TransformPoint(NumOf(bbox[2]), NumOf(bbox[3]));
            rect = new Rectangle(Math.Min(x0, x1), Math.Min(y0, y1),
                Math.Max(x0, x1), Math.Max(y0, y1));
        }

        elements.Add(new XFormPlacement(d.Name, rect, children));
    }

    private static Aspose.Pdf.Matrix? ReadMatrix(Core.PdfObject? obj, IO.PdfReader reader)
    {
        if (reader.Resolve(obj) is not Core.PdfArray arr || arr.Count < 6) return null;
        return new Aspose.Pdf.Matrix(NumOf(arr[0]), NumOf(arr[1]), NumOf(arr[2]),
            NumOf(arr[3]), NumOf(arr[4]), NumOf(arr[5]));
    }

    private static double NumOf(Core.PdfObject obj) => obj switch
    {
        Core.PdfInteger i => i.Value,
        Core.PdfReal r => r.Value,
        _ => 0,
    };
}
