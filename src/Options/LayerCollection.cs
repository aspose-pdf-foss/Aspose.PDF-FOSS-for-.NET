using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Represents the collection of layers on a specific page.
/// </summary>
public sealed class LayerCollection : IReadOnlyList<OptionalContentGroup>
{
    private readonly List<OptionalContentGroup> _layers;

    internal LayerCollection(List<OptionalContentGroup> layers)
    {
        _layers = layers;
        // Set back-reference so Delete() can update the collection
        foreach (var l in _layers)
            l._layerCollection = this;
    }

    internal void Remove(OptionalContentGroup layer) => _layers.Remove(layer);

    /// <summary>Number of layers on this page.</summary>
    public int Count => _layers.Count;

    /// <summary>Get a layer by 0-based index.</summary>
    public OptionalContentGroup this[int index] => _layers[index];

    /// <summary>
    /// Add a new layer to this page. The layer's pending operators are injected
    /// into the page content stream wrapped in BDC/EMC markers.
    /// </summary>
    public void Add(OptionalContentGroup layer)
    {
        if (_page is null)
            throw new InvalidOperationException("LayerCollection is not associated with a page.");
        LayerHelper.AddLayerToPage(_page, layer);
        _layers.Add(layer);
    }

    internal Page? _page;

    internal void SetPage(Page page) => _page = page;

    public IEnumerator<OptionalContentGroup> GetEnumerator() => _layers.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
