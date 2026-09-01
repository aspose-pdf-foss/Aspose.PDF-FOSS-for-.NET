using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class Field
{
    /// <summary>
    /// Internal: enumerate every kid in the /Kids array that resolves to a
    /// dictionary, in array order. Unlike <see cref="FieldKids"/>, this does
    /// not filter out pure widget annotations (kids without /T or /FT), so it
    /// surfaces the per-option widgets of a grouped checkbox/radio field.
    /// </summary>
    internal IEnumerable<PdfDictionary> AllKids()
    {
        var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
        if (kids is null) yield break;
        foreach (var kid in kids)
        {
            if (_reader.Resolve(kid) is PdfDictionary kidDict)
                yield return kidDict;
        }
    }

    /// <summary>
    /// Iterates widget annotations for this field's kids in the /Kids array.
    /// </summary>
    public IEnumerator<Aspose.Pdf.Annotations.WidgetAnnotation> GetEnumerator()
    {
        // A merged single-widget leaf that has also grown extra visual widgets (multi-
        // widget field via Form.AddFieldAppearance) keeps its own /Rect+/AP on the field
        // dict — yield that as the first widget so all visual widgets are enumerated.
        if (HasMergedSelfWidget)
            yield return new Aspose.Pdf.Annotations.WidgetAnnotation(_dict, _reader);

        // Walk every /Kids entry. A kid that is itself a field node (/T or /FT) is yielded
        // as the typed child field with its Parent wired back. An UNNAMED CONTAINER kid
        // (no /T, no /FT, but with /Kids of its own — an XFA #subform level) contributes
        // no name component and is flattened through: its named descendants surface as
        // this field's children, so caller recursion reaches every leaf. A pure widget
        // kid (no /T, no /Kids) is yielded as a plain WidgetAnnotation so callers can
        // read its per-widget appearance.
        foreach (var w in EnumerateKidsThrough(_dict, depth: 0))
            yield return w;
    }

    private IEnumerable<Aspose.Pdf.Annotations.WidgetAnnotation> EnumerateKidsThrough(
        PdfDictionary node, int depth)
    {
        if (depth > 10) yield break;
        var kids = _reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) yield break;
        foreach (var kid in kids)
        {
            if (_reader.Resolve(kid) is not PdfDictionary kidDict) continue;
            if (kidDict.ContainsKey("T") || kidDict.ContainsKey("FT"))
            {
                var child = Field.Create(kidDict, _reader);
                child.OwnerDocument = OwnerDocument;
                child.Parent = this;
                yield return child;
            }
            else if (_reader.Resolve(kidDict.Get("Kids")) is PdfArray)
            {
                foreach (var w in EnumerateKidsThrough(kidDict, depth + 1))
                    yield return w;
            }
            else
            {
                yield return new Aspose.Pdf.Annotations.WidgetAnnotation(kidDict, _reader);
            }
        }
    }

    /// <summary>True when this field is a merged single-widget leaf (its own /Rect)
    /// that has nonetheless grown extra widget kids — a multi-widget text field. The
    /// field dict itself is then the first visual widget alongside the /Kids widgets.</summary>
    internal bool HasMergedSelfWidget =>
        _dict.ContainsKey("Rect") &&
        _reader.Resolve(_dict.Get("Kids")) is PdfArray k && k.Count > 0;

    /// <summary>
    /// Internal: enumerate the Field-typed child kids of this field. Skips pure
    /// widget annotations (kids without /T or /FT) and recreates each child via
    /// <see cref="Field.Create"/>.
    /// </summary>
    internal IEnumerable<Field> FieldKids()
    {
        var kids = _reader.Resolve(_dict.Get("Kids")) as PdfArray;
        if (kids is null) yield break;

        foreach (var kid in kids)
        {
            var kidDict = _reader.Resolve(kid) as PdfDictionary;
            if (kidDict is null) continue;
            if (!kidDict.ContainsKey("T") && !kidDict.ContainsKey("FT")) continue;

            var childField = Field.Create(kidDict, _reader);
            childField.OwnerDocument = OwnerDocument;
            yield return childField;
        }
    }

    private PdfDictionary? FindPageDict()
    {
        // The field's /P entry points to the page
        var pageRef = _reader.ResolveDict(_dict.Get("P"));
        if (pageRef is not null) return pageRef;

        // Walk parent chain looking for /P
        var parent = _reader.ResolveDict(_dict.Get("Parent"));
        while (parent is not null)
        {
            pageRef = _reader.ResolveDict(parent.Get("P"));
            if (pageRef is not null) return pageRef;
            parent = _reader.ResolveDict(parent.Get("Parent"));
        }

        return null;
    }

    /// <summary>Bounding box (union) of all descendant widget /Rects (depth-limited), or null.</summary>
    private Rectangle? UnionKidRect(PdfDictionary dict, int depth)
    {
        if (depth > 2) return null;
        var kids = _reader.Resolve(dict.Get("Kids")) as PdfArray;
        if (kids is null) return null;
        Rectangle? acc = null;
        foreach (var k in kids)
        {
            var kid = _reader.ResolveDict(k);
            if (kid is null) continue;
            Rectangle? r = _reader.Resolve(kid.Get("Rect")) is PdfArray { Count: >= 4 } ra
                ? Rectangle.FromPdfArray(ra, _reader)
                : UnionKidRect(kid, depth + 1);
            if (r is null) continue;
            acc = acc is null
                ? r
                : new Rectangle(
                    System.Math.Min(acc.LLX, r.LLX), System.Math.Min(acc.LLY, r.LLY),
                    System.Math.Max(acc.URX, r.URX), System.Math.Max(acc.URY, r.URY));
        }
        return acc;
    }

    /// <summary>Whether this field is a container/group of child fields.</summary>
    public bool IsGroup => Count > 0;

    private List<Field> GetKids()
    {
        var list = new List<Field>();
        foreach (var k in FieldKids()) list.Add(k);
        return list;
    }

    /// <summary>Whether this field belongs to a shared-fields group. Stored only.</summary>
    public bool IsSharedField { get; set; }

    /// <summary>Copy widget annotations into an array starting at <paramref name="index"/>.</summary>
    public void CopyTo(Aspose.Pdf.Annotations.WidgetAnnotation[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        if (OwnerDocument is null) return;
        var kids = GetKids();
        for (var i = 0; i < kids.Count; i++)
            array[index + i] = new Aspose.Pdf.Annotations.WidgetAnnotation(OwnerDocument);
    }

    /// <summary>Copy this field's kids (as Field instances) into an array starting at <paramref name="index"/>.</summary>
    public void CopyTo(Field[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        var kids = GetKids();
        for (var i = 0; i < kids.Count; i++)
            array[index + i] = kids[i];
    }

    /// <summary>Enumerator typed to <see cref="Aspose.Pdf.Annotations.WidgetAnnotation"/>.</summary>
    public IEnumerator<Aspose.Pdf.Annotations.WidgetAnnotation> GetWidgetEnumerator()
    {
        if (OwnerDocument is null) yield break;
        var kids = GetKids();
        for (var i = 0; i < kids.Count; i++)
            yield return new Aspose.Pdf.Annotations.WidgetAnnotation(OwnerDocument);
    }
}
