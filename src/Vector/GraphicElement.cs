using System.Collections;
using System.Collections.Generic;

namespace Aspose.Pdf.Vector;

/// <summary>Single vector-graphics element (path segment, image, text run)
/// extracted from a PDF content stream. The FOSS renderer does not yet
/// produce these — the type exists for callers that walk a hypothetical
/// graphic-element pipeline.</summary>
public class GraphicElement
{
    public virtual Rectangle Rectangle { get; } = new Rectangle(0, 0, 0, 0);

    internal virtual GraphicElement Clone(XFormPlacement xFormPlacement) => this;

    /// <summary>Emit the PDF content-stream operators that reproduce this element
    /// in page space. The default element draws nothing.</summary>
    internal virtual string ToContent() => string.Empty;

    protected virtual void GetInitialPoint(out double x, out double y) { x = 0; y = 0; }
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

    internal SubPath(Aspose.Pdf.Matrix ctm,
        System.Collections.Generic.List<Aspose.Pdf.Operator> construction,
        Aspose.Pdf.Operator paint, Rectangle rectangle)
    {
        _ctm = ctm;
        _construction = construction;
        _paint = paint;
        _rectangle = rectangle;
    }

    public override Rectangle Rectangle => _rectangle;

    internal override GraphicElement Clone(XFormPlacement xFormPlacement) => this;

    internal override string ToContent()
    {
        var sb = new System.Text.StringBuilder();
        // Re-apply the original CTM, then replay the path operators verbatim so the
        // re-extracted bounding box is identical. Wrapped in q/Q to isolate the CTM.
        sb.Append("q ");
        sb.Append(new Operators.ConcatenateMatrix(_ctm).ToPdf());
        sb.Append('\n');
        foreach (var op in _construction)
        {
            sb.Append(op.ToPdf());
            sb.Append('\n');
        }
        sb.Append(_paint.ToPdf());
        sb.Append("\nQ\n");
        return sb.ToString();
    }
}

/// <summary>Mutable collection of <see cref="GraphicElement"/> entries.
/// Consumed by <see cref="Page.AddGraphics"/> and produced by
/// <c>GraphicsAbsorber</c>.</summary>
public sealed class GraphicElementCollection : IEnumerable<GraphicElement>
{
    private readonly List<GraphicElement> _items = new();

    public GraphicElementCollection() { }

    public int Count => _items.Count;
    public GraphicElement this[int index] => _items[index];

    public void Add(GraphicElement item) => _items.Add(item);
    public void Clear() => _items.Clear();
    public bool Contains(GraphicElement item) => _items.Contains(item);
    public void CopyTo(GraphicElement[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public bool Remove(GraphicElement item) => _items.Remove(item);

    public IEnumerator<GraphicElement> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"GraphicElementCollection ({_items.Count})";
}
