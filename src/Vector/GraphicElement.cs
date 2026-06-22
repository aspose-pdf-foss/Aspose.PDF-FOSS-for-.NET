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

    protected virtual void GetInitialPoint(out double x, out double y) { x = 0; y = 0; }
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
