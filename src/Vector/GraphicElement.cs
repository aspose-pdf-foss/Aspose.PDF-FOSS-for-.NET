using System.Collections;
using System.Collections.Generic;

namespace Aspose.Pdf.Vector;

/// <summary>Single vector-graphics element (path segment, image, text run)
/// extracted from a PDF content stream.</summary>
public class GraphicElement
{
    public virtual Rectangle Rectangle { get; } = new Rectangle(0, 0, 0, 0);

    /// <summary>The element's page-space anchor (the lower-left corner of its bounding
    /// box). Assigning a new value moves the element by the delta and rewrites the
    /// source page so the move survives a save/re-absorb round-trip. The base element
    /// exposes the anchor read-only.</summary>
    public virtual Point Position
    {
        get => new Point(Rectangle.LLX, Rectangle.LLY);
        set { }
    }

    /// <summary>The collection this element was absorbed into (the absorber's
    /// top-level elements, or a form placement's children). User collections
    /// refuse to mix elements from different parents.</summary>
    internal GraphicElementCollection? SourceCollection;

    /// <summary>The form placement this element is a child of (null for
    /// top-level elements). Replays add the ancestors' accumulated Position
    /// moves so a child lands where the moved parent would draw it.</summary>
    internal XFormPlacement? ParentPlacement;

    /// <summary>The clipping path in force when this element was painted on the
    /// source page (top-level elements only). A source-page move translates the
    /// clip together with the element.</summary>
    internal GraphicsClipInfo? SourceClip;

    /// <summary>The CTM in force when this element was constructed — used to map
    /// a page-space move into the element's own user space. Null when unknown.</summary>
    internal virtual Aspose.Pdf.Matrix? SourceCtm => null;

    /// <summary>The edit session of the page this element was absorbed from
    /// (null for form children and un-absorbed elements). Lets bulk operations
    /// batch their rewrites.</summary>
    internal GraphicsEditState? SourceEditState => EditState;

    /// <summary>Sum of the ancestors' page-space Position moves.</summary>
    internal (double Dx, double Dy) AncestorTranslation()
    {
        double dx = 0, dy = 0;
        for (var p = ParentPlacement; p is not null; p = p.ParentPlacement)
        {
            var t = p.SourceTranslation;
            dx += t.Dx;
            dy += t.Dy;
        }
        return (dx, dy);
    }

    // The element's operator range in the source page's content (top-level
    // elements only) and the shared edit session that rewrites the page.
    private int _srcStart = -1, _srcEnd = -1;
    private protected GraphicsEditState? EditState;

    internal void SetSourceRange(int start, int end) { _srcStart = start; _srcEnd = end; }

    /// <summary>Whether <see cref="Remove"/> took this element out of its page.</summary>
    internal bool SourceRemoved { get; private protected set; }

    /// <summary>The page-space translation accumulated by Position assignments.</summary>
    internal virtual (double Dx, double Dy) SourceTranslation => (0, 0);

    /// <summary>Bind this element to the edit session of the page it was absorbed
    /// from, so that changing <see cref="Position"/> or calling <see cref="Remove"/>
    /// rewrites the page.</summary>
    internal virtual void BindSource(GraphicsEditState editState)
    {
        EditState = editState;
        if (_srcStart >= 0) editState.Register(this, _srcStart, _srcEnd);
    }

    /// <summary>Append this element's geometry to <paramref name="page"/>'s
    /// content (the single-element form of <see cref="Page.AddGraphics(GraphicElementCollection)"/>).</summary>
    public void AddOnPage(Page page)
    {
        if (page is null || SourceRemoved) return;
        var content = page.ReplayElement(this);
        if (string.IsNullOrEmpty(content)) return;
        page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(content));
    }

    /// <summary>Remove this element from its source page. The page content is
    /// rewritten without the element's operators; replays
    /// (<see cref="AddOnPage"/> / <see cref="Page.AddGraphics(GraphicElementCollection)"/>)
    /// skip it from then on.</summary>
    public void Remove()
    {
        SourceRemoved = true;
        EditState?.MarkDirty();
    }

    internal virtual GraphicElement Clone(XFormPlacement xFormPlacement) => this;

    /// <summary>Emit the PDF content-stream operators that reproduce this element
    /// in page space. The default element draws nothing.</summary>
    internal virtual string ToContent() => string.Empty;

    protected virtual void GetInitialPoint(out double x, out double y) { x = 0; y = 0; }

    /// <summary>Serial number for the emitted SVG document ids ("body_N").</summary>
    private static int _svgBodyCounter;

    /// <summary>Render this element alone as a standalone SVG document sized to
    /// its bounding box (CSS pixels, 1pt = 4/3 px).</summary>
    public string SaveToSvg()
    {
        var id = System.Threading.Interlocked.Increment(ref _svgBodyCounter);
        var r = Rectangle;
        var w = Math.Max(0.0, r.URX - r.LLX);
        var h = Math.Max(0.0, r.URY - r.LLY);
        var pxW = (int)Math.Ceiling(w * 4.0 / 3.0) + 1;
        var pxH = (int)Math.Ceiling(h * 4.0 / 3.0) + 1;
        var sb = new System.Text.StringBuilder();
        sb.Append("<?xml version=\"1.0\" standalone=\"no\"?>\n");
        sb.Append("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
            $"version=\"1.1\" id=\"body_{id}\" width=\"{pxW}\" height=\"{pxH}\">\n\n");
        sb.Append("<g transform=\"matrix(1.3333 0 0 1.3333 0 0)\">\n");
        AppendSvgContent(sb, r.LLX, r.LLY, h);
        sb.Append("</g>\n");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Render this element alone to an SVG file (see <see cref="SaveToSvg()"/>).</summary>
    public void SaveToSvg(string svgFilePath)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(svgFilePath));
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(svgFilePath, SaveToSvg());
    }

    /// <summary>Append the element's SVG markup in element-local coordinates:
    /// the element's bounding-box lower-left corner maps to (0,0), y still
    /// upward (callers flip via a matrix). The base element draws nothing.</summary>
    internal virtual void AppendSvgContent(System.Text.StringBuilder sb,
        double originX, double originY, double boxHeight)
    {
    }
}

/// <summary>The painting parameters a sub-path was drawn with: fill and/or
/// stroke colours (0-1 components), stroke geometry, and the fill rule.</summary>
internal sealed record SubPathStyle(
    (double R, double G, double B)? Fill,
    (double R, double G, double B)? Stroke,
    double LineWidth,
    int LineJoin,
    bool EvenOdd)
{
    public static readonly SubPathStyle Default =
        new((0, 0, 0), null, 1.0, 0, false);
}

/// <summary>A single painted sub-path extracted from a content stream: its
/// construction operators (in their original user-space coordinates), the CTM in
/// effect when it was drawn, and the painting operator that closed it. The public
/// <see cref="Rectangle"/> is the path's bounding box transformed into page space,
/// so it is stable across an extract → <see cref="Page.AddGraphics"/> → re-extract
/// round-trip (the same operators are re-emitted under the same CTM).</summary>
public sealed class SubPath : GraphicElement
{
    private readonly Aspose.Pdf.Matrix _ctm;
    private readonly System.Collections.Generic.List<Aspose.Pdf.Operator> _construction;
    private readonly Aspose.Pdf.Operator _paint;
    private readonly Rectangle _rectangle;
    private readonly SubPathStyle _style;

    internal SubPath(Aspose.Pdf.Matrix ctm,
        System.Collections.Generic.List<Aspose.Pdf.Operator> construction,
        Aspose.Pdf.Operator paint, Rectangle rectangle, SubPathStyle? style = null)
    {
        _ctm = ctm;
        _construction = construction;
        _paint = paint;
        _rectangle = rectangle;
        _style = style ?? SubPathStyle.Default;
    }

    // Page-space translation accumulated by assignments to Position.
    private double _dx, _dy;

    public override Rectangle Rectangle => _rectangle;

    /// <summary>The sub-path's page-space anchor (bounding-box lower-left). Assigning a
    /// new point translates the whole sub-path by the delta and rewrites the source
    /// page content so the move persists through a save and a subsequent re-extract.</summary>
    public override Point Position
    {
        get => new Point(_rectangle.LLX + _dx, _rectangle.LLY + _dy);
        set
        {
            _dx = value.X - _rectangle.LLX;
            _dy = value.Y - _rectangle.LLY;
            EditState?.MarkDirty();
        }
    }

    internal override (double Dx, double Dy) SourceTranslation => (_dx, _dy);

    internal override Aspose.Pdf.Matrix? SourceCtm => _ctm;

    internal override GraphicElement Clone(XFormPlacement xFormPlacement) => this;

    internal override string ToContent()
    {
        if (SourceRemoved) return string.Empty;
        var sb = new System.Text.StringBuilder();
        string F(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        // Re-apply the original CTM, then replay the path operators verbatim so the
        // re-extracted bounding box is identical. Wrapped in q/Q to isolate the CTM.
        // A page-space translation (from a Position change) is concatenated BEFORE the
        // original CTM so it composes on the outside: a user point maps to
        // p·CTM·translation, i.e. the page position shifts by exactly (dx, dy).
        sb.Append("q ");
        if (_dx != 0 || _dy != 0)
        {
            sb.Append(new Aspose.Pdf.Operators.ConcatenateMatrix(
                new Aspose.Pdf.Matrix(1, 0, 0, 1, _dx, _dy)).ToPdf());
            sb.Append('\n');
        }
        sb.Append(new Aspose.Pdf.Operators.ConcatenateMatrix(_ctm).ToPdf());
        sb.Append('\n');
        // Re-establish the paint state the sub-path was absorbed with — the
        // destination page's ambient colours/width are arbitrary at replay time.
        if (_style.Fill is { } fill)
            sb.Append($"{F(fill.R)} {F(fill.G)} {F(fill.B)} rg\n");
        if (_style.Stroke is { } stroke)
        {
            sb.Append($"{F(stroke.R)} {F(stroke.G)} {F(stroke.B)} RG\n");
            sb.Append($"{F(_style.LineWidth)} w\n");
            sb.Append($"{_style.LineJoin} j\n");
        }
        foreach (var op in _construction)
        {
            sb.Append(op.ToPdf());
            sb.Append('\n');
        }
        sb.Append(_paint.ToPdf());
        sb.Append("\nQ\n");
        return sb.ToString();
    }

    internal override void AppendSvgContent(System.Text.StringBuilder sb,
        double originX, double originY, double boxHeight)
    {
        var d = BuildLocalPathData(originX, originY);
        var paint = new System.Text.StringBuilder();
        if (_style.Stroke is { } sc)
        {
            var det = Math.Abs(_ctm.A * _ctm.D - _ctm.B * _ctm.C);
            var scale = det > 0 ? Math.Sqrt(det) : 1.0;
            paint.Append($"stroke=\"{Hex(sc.R, sc.G, sc.B)}\" ");
            paint.Append($"stroke-width=\"{F(_style.LineWidth * scale)}\" ");
            paint.Append($"stroke-linejoin=\"{(_style.LineJoin == 1 ? "round" : _style.LineJoin == 2 ? "bevel" : "miter")}\" ");
        }
        else
        {
            paint.Append("stroke=\"none\" ");
        }
        if (_style.Fill is { } fc)
        {
            paint.Append($"fill=\"{Hex(fc.R, fc.G, fc.B)}\" ");
            paint.Append($"fill-rule=\"{(_style.EvenOdd ? "evenodd" : "nonzero")}\" ");
        }
        else
        {
            paint.Append("fill=\"none\" ");
        }
        sb.Append($"\t<path id=\"\"  transform=\"matrix(1 0 0 -1 0 {F(boxHeight + 1)})\"  " +
            $"d=\"{d}\" {paint}/>\n");
    }

    /// <summary>The path data in element-local page-space coordinates
    /// (bounding-box lower-left at the origin, y upward).</summary>
    private string BuildLocalPathData(double originX, double originY)
    {
        var sb = new System.Text.StringBuilder();
        double curX = 0, curY = 0;   // current point in user space
        void Emit(char cmd, params double[] userXy)
        {
            sb.Append(cmd);
            for (var i = 0; i + 1 < userXy.Length; i += 2)
            {
                var (px, py) = _ctm.TransformPoint(userXy[i], userXy[i + 1]);
                if (i > 0 || cmd == 'C') sb.Append(' ');
                sb.Append(F(px - originX)).Append(' ').Append(F(py - originY));
            }
        }
        foreach (var op in _construction)
        {
            switch (op)
            {
                case Aspose.Pdf.Operators.MoveTo m:
                    Emit('M', m.X, m.Y); curX = m.X; curY = m.Y; break;
                case Aspose.Pdf.Operators.LineTo l:
                    Emit('L', l.X, l.Y); curX = l.X; curY = l.Y; break;
                case Aspose.Pdf.Operators.CurveTo c:
                    Emit('C', c.X1, c.Y1, c.X2, c.Y2, c.X3, c.Y3); curX = c.X3; curY = c.Y3; break;
                case Aspose.Pdf.Operators.CurveTo1 v:
                    Emit('C', curX, curY, v.X2, v.Y2, v.X3, v.Y3); curX = v.X3; curY = v.Y3; break;
                case Aspose.Pdf.Operators.CurveTo2 y:
                    Emit('C', y.X1, y.Y1, y.X3, y.Y3, y.X3, y.Y3); curX = y.X3; curY = y.Y3; break;
                case Aspose.Pdf.Operators.Re re:
                    Emit('M', re.X, re.Y);
                    Emit('L', re.X + re.Width, re.Y);
                    Emit('L', re.X + re.Width, re.Y + re.Height);
                    Emit('L', re.X, re.Y + re.Height);
                    sb.Append('z');
                    curX = re.X; curY = re.Y; break;
                case Aspose.Pdf.Operators.ClosePath:
                    sb.Append('z'); break;
            }
        }
        return sb.ToString();
    }

    private static string Hex(double r, double g, double b) =>
        $"#{Clamp(r):X2}{Clamp(g):X2}{Clamp(b):X2}";

    private static int Clamp(double v) => Math.Clamp((int)Math.Round(v * 255), 0, 255);

    private static string F(double v) => v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Mutable collection of <see cref="GraphicElement"/> entries.
/// Consumed by <see cref="Page.AddGraphics"/> and produced by
/// <c>GraphicsAbsorber</c>. Indexed 1-based, like the other document
/// collections (pages, annotations).</summary>
public sealed class GraphicElementCollection : IEnumerable<GraphicElement>
{
    private readonly List<GraphicElement> _items = new();

    public GraphicElementCollection() { }

    public int Count => _items.Count;

    /// <summary>1-based element access.</summary>
    public GraphicElement this[int index]
    {
        get
        {
            if (index < 1 || index > _items.Count)
                throw new ArgumentException(
                    "Invalid index: index should be in the range [1..n] where n equals to the elements count.");
            return _items[index - 1];
        }
    }

    /// <summary>Add an element. A user collection may only hold elements
    /// absorbed from the SAME parent (all top-level, or all children of one
    /// form placement) — mixing parents throws.</summary>
    public void Add(GraphicElement item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        if (_items.Count > 0 && !ReferenceEquals(_items[0].SourceCollection, item.SourceCollection))
            throw new InvalidOperationException(
                "Cannot add the graphic element: it belongs to a different parent than the collection's existing elements.");
        _items.Add(item);
    }

    /// <summary>Absorber-side add: stamps the element's parent collection.</summary>
    internal void AddInternal(GraphicElement item)
    {
        item.SourceCollection ??= this;
        _items.Add(item);
    }

    public void Clear() => _items.Clear();
    public bool Contains(GraphicElement item) => _items.Contains(item);
    public void CopyTo(GraphicElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public bool Remove(GraphicElement item) => _items.Remove(item);

    public IEnumerator<GraphicElement> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"GraphicElementCollection ({_items.Count})";
}
