using System.Collections;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Structure;

/// <summary>A live, mutable collection of structure elements held
/// under a parent's /K array. Mirrors the
/// <c>Aspose.Pdf.Structure.ElementCollection</c> surface.</summary>
public class ElementCollection : IEnumerable<Element>
{
    private readonly Element _parent;
    private readonly List<Element> _items;

    internal ElementCollection(Element parent, List<Element> items)
    {
        _parent = parent;
        _items = items;
    }

    /// <summary>Number of direct children.</summary>
    public int Count => _items.Count;

    /// <summary>0-based access to a child by index.</summary>
    public Element this[int index] => _items[index];

    /// <summary>Iterate the children in tree order.</summary>
    public IEnumerator<Element> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Remove <paramref name="item"/> from this collection and
    /// from the parent's /K array. Returns true when the child was
    /// found and removed.</summary>
    public bool Remove(Element item)
    {
        if (item is null) return false;
        var idx = _items.IndexOf(item);
        if (idx < 0) return false;
        _items.RemoveAt(idx);
        var removedObjNum = RemoveFromParentK(item);
        item.SetParent(null);
        // Physically drop the detached subtree's structure-element objects so a
        // subsequent Save shrinks the file instead of carrying them as orphans.
        item.PurgeStructureSubtree(removedObjNum);
        return true;
    }

    /// <summary>Drop <paramref name="item"/> from the parent's /K array (or single /K
    /// entry). Returns the object number of the removed entry when it was an indirect
    /// reference, or -1 when it was inline (or not found).</summary>
    private int RemoveFromParentK(Element item)
    {
        if (_parent._reader is null) return -1;
        var k = _parent._reader.Resolve(_parent.Dict.Get("K"));
        if (k is PdfArray arr)
        {
            // Walk the array and drop the entry that resolves to item.Dict.
            for (var i = 0; i < arr.Count; i++)
            {
                var raw = arr[i];
                var resolved = _parent._reader.Resolve(raw);
                if (ReferenceEquals(resolved, item.Dict))
                {
                    arr.RemoveAt(i);
                    return raw is PdfIndirectRef r ? r.ObjectNumber : -1;
                }
            }
        }
        else if (k is PdfDictionary single && ReferenceEquals(single, item.Dict))
        {
            var raw = _parent.Dict.Get("K");
            _parent.Dict.Remove("K");
            return raw is PdfIndirectRef r ? r.ObjectNumber : -1;
        }
        return -1;
    }
}
