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
public sealed partial class GraphicsAbsorber : IDisposable
{
    /// <summary>The extracted vector elements (populated by <see cref="Visit"/>).</summary>
    public GraphicElementCollection Elements { get; } = new();

    private GraphicsEditState? _editState;

    /// <summary>Creates a graphics absorber with an empty <c>Elements</c> collection.</summary>
    public GraphicsAbsorber() { }

    /// <summary>Releases what the absorber holds: its elements and the page snapshot they edit.</summary>
    public void Dispose()
    {
        Elements.Clear();
        _editState = null;
    }

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
        // The region painting is clipped to, at any depth (a form's walk starts inside its placement's).
        public ClipRegion? Clip;

        public WalkState Clone() => new()
        {
            Ctm = new Aspose.Pdf.Matrix(Ctm),
            Fill = Fill, Stroke = Stroke,
            LineWidth = LineWidth, LineJoin = LineJoin,
            ActiveClip = ActiveClip,
            Clip = Clip,
        };
    }

    private const int MaxFormDepth = 12;

    private static void Walk(IReadOnlyList<Aspose.Pdf.Operator> ops, Core.PdfDictionary? resources, IO.PdfReader reader, GraphicElementCollection elements, int depth, GraphicsEditState? editState, Aspose.Pdf.Matrix? baseCtm = null, ClipRegion? baseClip = null)
    {
        var gw = new GraphicsWalkState();
        gw.ops = ops;
        gw.resources = resources;
        gw.reader = reader;
        gw.elements = elements;
        gw.depth = depth;
        gw.editState = editState;
        gw.baseCtm = baseCtm;
        gw.gs = new WalkState();
        if (gw.baseCtm is not null) gw.gs.Ctm = new Aspose.Pdf.Matrix(gw.baseCtm);
        gw.gs.Clip = baseClip;
        gw.stack = new Stack<WalkState>();

        gw.subpaths = new List<(Aspose.Pdf.Matrix ctm, List<Aspose.Pdf.Operator> ops,
            double minX, double minY, double maxX, double maxY, int startIdx, int endIdx)>();

        gw.curOps = null;
        gw.curCtm = null;
        gw.minX = 0;
        gw.minY = 0;
        gw.maxX = 0;
        gw.maxY = 0;
        gw.any = false;
        gw.curX = 0;   // current point in user space
        gw.curY = 0;
        gw.curStart = -1;
        gw.curEnd = -1;
        gw.pendingClip = null;
        gw.pendingRegion = null;

        for (var i = 0; i < gw.ops.Count; i++)
        {
            if (!WalkOperator(gw, i)) break;
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
        Walk(formOps, formResources, reader, children, depth + 1, editState: null, childBase, gs.Clip);

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
