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
/// page with <see cref="Page.AddGraphics(GraphicElementCollection)"/>, and
/// top-level elements can be moved (<see cref="GraphicElement.Position"/>) or
/// removed on their source page — edits rewrite only the elements' own
/// operator ranges, so text and other non-vector content is untouched.
/// </summary>
public sealed class GraphicsAbsorber
{
    /// <summary>The extracted vector elements (populated by <see cref="Visit"/>).</summary>
    public GraphicElementCollection Elements { get; } = new();

    private GraphicsEditState? _editState;

    public GraphicsAbsorber() { }

    /// <summary>Extract the painted sub-paths and form placements of <paramref name="page"/>.</summary>
    public void Visit(Page page)
    {
        if (page is null) return;
        var resources = Devices.SoftwarePageRenderer.ResolveInheritedPageResources(page.Dict, page.Reader);

        // Snapshot the page's operators so element edits can be written back as
        // a faithful re-emission with only the edited ranges changed.
        var ops = new List<Aspose.Pdf.Operator>();
        foreach (var op in page.Contents) ops.Add(op);

        _editState = new GraphicsEditState(page, ops);
        Walk(ops, resources, page.Reader, Elements, depth: 0, _editState);
        // Bind each top-level element to the edit session so a later Position
        // change / removal rewrites the page content in place.
        foreach (var element in Elements)
            element.BindSource(_editState);
    }

    /// <summary>Defer source-page rewrites while a batch of element edits runs;
    /// <see cref="ResumeUpdate"/> applies them in one pass.</summary>
    public void SuppressUpdate() => _editState?.Suppress();

    /// <summary>Re-enable source-page rewrites and apply any deferred edits.</summary>
    public void ResumeUpdate() => _editState?.Resume();

    /// <summary>Mutable per-path graphics state tracked during the walk.</summary>
    private sealed class WalkState
    {
        public Aspose.Pdf.Matrix Ctm = new();
        public (double R, double G, double B) Fill = (0, 0, 0);
        public (double R, double G, double B) Stroke = (0, 0, 0);
        public double LineWidth = 1.0;
        public int LineJoin;
        // The most recent clip set in this graphics-state scope (restored by Q like
        // any other state). Elements painted under it record it so a source-page
        // move can translate the clip path along with the element.
        public GraphicsClipInfo? ActiveClip;

        public WalkState Clone() => new()
        {
            Ctm = new Aspose.Pdf.Matrix(Ctm),
            Fill = Fill, Stroke = Stroke,
            LineWidth = LineWidth, LineJoin = LineJoin,
            ActiveClip = ActiveClip,
        };
    }

    private const int MaxFormDepth = 12;

    private static void Walk(IReadOnlyList<Aspose.Pdf.Operator> ops, Core.PdfDictionary? resources,
        IO.PdfReader reader, GraphicElementCollection elements, int depth,
        GraphicsEditState? editState, Aspose.Pdf.Matrix? baseCtm = null)
    {
        var gs = new WalkState();
        if (baseCtm is not null) gs.Ctm = new Aspose.Pdf.Matrix(baseCtm);
        var stack = new Stack<WalkState>();

        // Sub-paths constructed for the current (not-yet-painted) path, each with
        // the operator index span of its construction ops.
        var subpaths = new List<(Aspose.Pdf.Matrix ctm, List<Aspose.Pdf.Operator> ops,
            double minX, double minY, double maxX, double maxY, int startIdx, int endIdx)>();

        List<Aspose.Pdf.Operator>? curOps = null;
        Aspose.Pdf.Matrix? curCtm = null;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        bool any = false;
        double curX = 0, curY = 0;   // current point in user space
        int curStart = -1, curEnd = -1;
        // A W/W* seen for the current path: the clip activates at the path-ending
        // op (n or a paint) and stays in force until the enclosing Q.
        GraphicsClipInfo? pendingClip = null;

        void Flush()
        {
            if (curOps is { Count: > 0 } && any)
                subpaths.Add((curCtm!, curOps, minX, minY, maxX, maxY, curStart, curEnd));
            curOps = null; any = false;
        }
        int PathStart() =>
            subpaths.Count > 0 ? subpaths[0].startIdx : curStart;
        void Start(double ux, double uy, int idx)
        {
            Flush();
            curOps = new List<Aspose.Pdf.Operator>();
            curCtm = new Aspose.Pdf.Matrix(gs.Ctm);
            any = false; curX = ux; curY = uy;
            curStart = idx; curEnd = idx;
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
            {
                var el = new SubPath(sp.ctm, sp.ops, paintOp,
                    new Rectangle(sp.minX, sp.minY, sp.maxX, sp.maxY), style);
                if (depth == 0)
                {
                    el.SetSourceRange(sp.startIdx, sp.endIdx);
                    el.SourceClip = gs.ActiveClip;
                }
                elements.AddInternal(el);
            }
            subpaths.Clear();
            ActivatePendingClip();
        }
        void Discard()
        {
            Flush();
            subpaths.Clear();
            ActivatePendingClip();
        }
        void ActivatePendingClip()
        {
            if (pendingClip is not null) { gs.ActiveClip = pendingClip; pendingClip = null; }
        }

        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
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
                    Start(m.X, m.Y, i); curOps!.Add(m); AddPoint(m.X, m.Y); break;
                case Re re:
                    Start(re.X, re.Y, i); curOps!.Add(re);
                    AddPoint(re.X, re.Y); AddPoint(re.X + re.Width, re.Y);
                    AddPoint(re.X + re.Width, re.Y + re.Height); AddPoint(re.X, re.Y + re.Height);
                    break;
                case LineTo l:
                    if (curOps is null) Start(l.X, l.Y, i);
                    curOps!.Add(l); AddPoint(l.X, l.Y); curX = l.X; curY = l.Y; curEnd = i; break;
                case CurveTo c:
                    if (curOps is null) Start(curX, curY, i);
                    curOps!.Add(c); AddPoint(c.X1, c.Y1); AddPoint(c.X2, c.Y2); AddPoint(c.X3, c.Y3);
                    curX = c.X3; curY = c.Y3; curEnd = i; break;
                case CurveTo1 v:
                    if (curOps is null) Start(curX, curY, i);
                    curOps!.Add(v); AddPoint(v.X2, v.Y2); AddPoint(v.X3, v.Y3);
                    curX = v.X3; curY = v.Y3; curEnd = i; break;
                case CurveTo2 y:
                    if (curOps is null) Start(curX, curY, i);
                    curOps!.Add(y); AddPoint(y.X1, y.Y1); AddPoint(y.X3, y.Y3);
                    curX = y.X3; curY = y.Y3; curEnd = i; break;
                case ClosePath h:
                    if (curOps is not null) { curOps.Add(h); curEnd = i; }
                    break;

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

                case Clip or EOClip:
                    // Record the clip path's construction range so a later element
                    // move can translate the clip with the element (the clip takes
                    // effect at the path-ending op that follows).
                    if (depth == 0 && PathStart() >= 0)
                        pendingClip = new GraphicsClipInfo(PathStart(), i, new Aspose.Pdf.Matrix(gs.Ctm));
                    break;

                case EndPath: Discard(); break;   // n — clip / no-paint: drop the path

                case Do d when depth < MaxFormDepth:
                    VisitForm(d, i, resources, reader, gs, elements, depth, editState);
                    break;

                default: break;                    // colour/clip/text/etc. don't affect path geometry
            }
        }
    }

    /// <summary>Extract a Form XObject invocation as an <see cref="XFormPlacement"/>:
    /// children are walked under the composed CTM (form /Matrix × placement CTM);
    /// the placement rectangle is the form /BBox mapped the same way.</summary>
    private static void VisitForm(Do d, int opIndex, Core.PdfDictionary? resources, IO.PdfReader reader,
        WalkState gs, GraphicElementCollection elements, int depth, GraphicsEditState? editState)
    {
        if (resources is null) return;
        var xobjDict = reader.ResolveDict(resources.Get("XObject"));
        var xobjEntry = xobjDict?.Get(d.Name);
        var xobj = xobjDict is null ? null : reader.ResolveStream(xobjEntry);
        if (xobj is null || xobj.Dict.GetName("Subtype") != "Form") return;

        byte[] bytes;
        try { bytes = reader.DecodeStream(xobj); }
        catch { return; }

        var formResources = reader.ResolveDict(xobj.Dict.Get("Resources")) ?? resources;
        var formMatrix = ReadMatrix(xobj.Dict.Get("Matrix"), reader);

        // Children carry the FULL composed CTM (form /Matrix × placement CTM), so
        // replaying a child onto another page reproduces it at the position it
        // had on the source page.
        var children = new GraphicElementCollection();
        var formOps = new List<Aspose.Pdf.Operator>();
        foreach (var raw in ContentStreamOperatorParser.ParseOperators(bytes))
            formOps.Add(TypedOperatorParser.Parse(raw));
        var childBase = formMatrix is null ? gs.Ctm : formMatrix.Multiply(gs.Ctm);
        Walk(formOps, formResources, reader, children, depth + 1, editState: null, childBase);

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

        var placement = new XFormPlacement(d.Name, rect, children,
            new Aspose.Pdf.Matrix(gs.Ctm), xobj, reader,
            (xobjEntry as Core.PdfIndirectRef)!);
        if (depth == 0) placement.SetSourceRange(opIndex, opIndex);
        // Children know their containing placement so a replay can honour the
        // ancestors' accumulated Position moves.
        foreach (var child in children)
            child.ParentPlacement = placement;
        elements.AddInternal(placement);
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

/// <summary>The construction range of a clipping path set in the source content,
/// with the CTM in force when it was constructed. Elements painted under the clip
/// reference it so a move can translate the clip along with the element.</summary>
internal sealed class GraphicsClipInfo
{
    internal readonly int Start;
    internal readonly int End;
    internal readonly Aspose.Pdf.Matrix Ctm;
    internal GraphicsClipInfo(int start, int end, Aspose.Pdf.Matrix ctm)
    { Start = start; End = end; Ctm = ctm; }
}

/// <summary>
/// Edit session shared by every element absorbed from one page. Records each
/// top-level element's operator range plus the pending moves/removals, and
/// rewrites the page content by re-emitting the ORIGINAL operator snapshot
/// with only the edited ranges changed: a moved sub-path's construction
/// coordinates are translated in their own user space (the page-space delta
/// mapped through the inverse of the construction CTM), together with the
/// clipping path that scopes it — so a panel clipped to a region moves as a
/// whole, as consumers of the rewrite expect. A moved form invocation is
/// wrapped in <c>q &lt;translate&gt; cm … Do … Q</c>; a removed element's ops
/// are omitted. Everything else — text, images, state — is preserved verbatim.
/// </summary>
internal sealed class GraphicsEditState
{
    private readonly Page _page;
    private readonly List<Aspose.Pdf.Operator> _ops;
    private readonly List<(int Start, int End, GraphicElement Element)> _ranges = new();
    private bool _suppressed;
    private bool _dirty;

    internal GraphicsEditState(Page page, List<Aspose.Pdf.Operator> ops)
    {
        _page = page;
        _ops = ops;
    }

    internal Page Page => _page;

    internal void Register(GraphicElement element, int start, int end)
        => _ranges.Add((start, end, element));

    internal bool IsSuppressed => _suppressed;

    internal void Suppress() => _suppressed = true;

    internal void Resume()
    {
        _suppressed = false;
        if (_dirty) Apply();
    }

    internal void MarkDirty()
    {
        _dirty = true;
        if (!_suppressed) Apply();
    }

    /// <summary>Map a page-space delta into the user space of <paramref name="ctm"/>
    /// (solve linear(ctm) · local = page). Identity when the CTM is unknown or singular.</summary>
    private static (double Dx, double Dy) LocalDelta(Aspose.Pdf.Matrix? ctm, double dx, double dy)
    {
        if (ctm is null) return (dx, dy);
        var det = ctm.A * ctm.D - ctm.C * ctm.B;
        if (System.Math.Abs(det) < 1e-12) return (dx, dy);
        return ((ctm.D * dx - ctm.C * dy) / det, (ctm.A * dy - ctm.B * dx) / det);
    }

    private void Apply()
    {
        _dirty = false;
        var translateAt = new Dictionary<int, (double Dx, double Dy)>();
        var openAt = new Dictionary<int, (double Dx, double Dy)>();
        var closeAt = new HashSet<int>();
        var dropped = new HashSet<int>();
        var clips = new Dictionary<GraphicsClipInfo, (double Dx, double Dy)>();
        var anyChange = false;

        foreach (var (start, end, element) in _ranges)
        {
            if (element.SourceRemoved)
            {
                for (var i = start; i <= end; i++) dropped.Add(i);
                anyChange = true;
                continue;
            }
            var (dx, dy) = element.SourceTranslation;
            if (dx == 0 && dy == 0) continue;
            anyChange = true;
            var local = LocalDelta(element.SourceCtm, dx, dy);
            if (element is XFormPlacement)
            {
                openAt[start] = local;
                closeAt.Add(end);
            }
            else
            {
                for (var i = start; i <= end; i++) translateAt[i] = local;
            }
            if (element.SourceClip is { } clip && !clips.ContainsKey(clip))
                clips[clip] = (dx, dy);
        }
        // A net-zero edit keeps the original stream untouched (byte-identical render).
        if (!anyChange) return;

        foreach (var kv in clips)
        {
            var local = LocalDelta(kv.Key.Ctm, kv.Value.Dx, kv.Value.Dy);
            for (var i = kv.Key.Start; i <= kv.Key.End; i++)
                if (!translateAt.ContainsKey(i)) translateAt[i] = local;
        }

        var sb = new System.Text.StringBuilder();
        string F(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < _ops.Count; i++)
        {
            if (openAt.TryGetValue(i, out var t))
                sb.Append($"q 1 0 0 1 {F(t.Dx)} {F(t.Dy)} cm\n");
            if (!dropped.Contains(i))
            {
                sb.Append(translateAt.TryGetValue(i, out var d)
                    ? TranslatedToPdf(_ops[i], d.Dx, d.Dy, F)
                    : _ops[i].ToPdf());
                sb.Append('\n');
            }
            if (closeAt.Contains(i))
                sb.Append("Q\n");
        }
        _page.SetContentStream(System.Text.Encoding.ASCII.GetBytes(sb.ToString()));
        _page.ResetContentsCache();
    }

    /// <summary>Re-emit a path-construction operator with its coordinates shifted
    /// by a user-space delta. Non-construction operators pass through verbatim.</summary>
    private static string TranslatedToPdf(Aspose.Pdf.Operator op, double dx, double dy,
        System.Func<double, string> f) => op switch
    {
        Re re => $"{f(re.X + dx)} {f(re.Y + dy)} {f(re.Width)} {f(re.Height)} re",
        MoveTo m => $"{f(m.X + dx)} {f(m.Y + dy)} m",
        LineTo l => $"{f(l.X + dx)} {f(l.Y + dy)} l",
        CurveTo c => $"{f(c.X1 + dx)} {f(c.Y1 + dy)} {f(c.X2 + dx)} {f(c.Y2 + dy)} {f(c.X3 + dx)} {f(c.Y3 + dy)} c",
        CurveTo1 v => $"{f(v.X2 + dx)} {f(v.Y2 + dy)} {f(v.X3 + dx)} {f(v.Y3 + dy)} v",
        CurveTo2 y => $"{f(y.X1 + dx)} {f(y.Y1 + dy)} {f(y.X3 + dx)} {f(y.Y3 + dy)} y",
        _ => op.ToPdf(),
    };
}
