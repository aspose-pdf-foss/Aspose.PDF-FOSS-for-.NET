using System;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.LogicalStructure;

public partial class StructureElement
{
    /// <summary>Removes every child of this element, marked-content and object references
    /// included: the /K entry goes with them, and each removed element is detached so it can
    /// be placed again elsewhere.</summary>
    public void ClearChilds()
    {
        EnsureChildrenLoaded();
        foreach (var child in _children!)
            child._parent = null;
        ClearChildren();
    }

    /// <summary>Places <paramref name="child"/> among this element's children at the
    /// zero-based <paramref name="index"/>, before the child that held that slot; an index at
    /// or past the end appends. The /K entry lands in the same place, among whatever
    /// marked-content references the array also carries.</summary>
    public StructureElement InsertChild(StructureElement child, int index)
    {
        if (child is null) throw new ArgumentNullException(nameof(child));
        if (ReferenceEquals(child._parent, this))
            throw new Aspose.Pdf.Tagged.TaggedException("Structure element is already a child of this element.");
        ValidateAppend(child);
        EnsureChildrenLoaded();
        if (index < 0 || index > _children!.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == _children.Count)
            return AppendChildCore(child, validate: false);

        var before = _children[index];
        Adopt(child);
        _children.Insert(index, child);
        var k = KidsArray();
        var at = IndexInKids(k, before);
        if (at < 0) k.Add(child._dict);
        else k.Insert(at, child._dict);
        return child;
    }

    /// <summary>Removes the child at the zero-based <paramref name="index"/> and detaches it,
    /// its own children going with it.</summary>
    public void RemoveChild(int index)
    {
        EnsureChildrenLoaded();
        if (index < 0 || index >= _children!.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _children[index].Detach();
    }

    /// <summary>This element's /K as an array, made one from a single entry or from nothing.</summary>
    private PdfArray KidsArray()
    {
        var k = Resolve(_dict.Get("K"));
        switch (k)
        {
            case PdfArray arr:
                return arr;
            case null:
                {
                    var made = new PdfArray();
                    _dict.Set("K", made);
                    return made;
                }
            default:
                {
                    // A single marked-content id or dictionary: the array it becomes keeps it first.
                    var made = new PdfArray();
                    made.Add(_dict.Get("K")!);
                    _dict.Set("K", made);
                    return made;
                }
        }
    }

    /// <summary>Where <paramref name="child"/>'s dictionary sits in the /K array, direct or through
    /// a reference; -1 when it is not there.</summary>
    private int IndexInKids(PdfArray k, StructureElement child)
    {
        for (var i = 0; i < k.Count; i++)
            if (ReferenceEquals(Resolve(k[i]), child._dict))
                return i;
        return -1;
    }
}
