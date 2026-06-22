using System.Collections;

namespace Aspose.Pdf;

/// <summary>
/// Container for paragraph-level content (text fragments, tables, images,
/// header/footer fragments, etc.) attached to a Page, Cell, HeaderFooter or
/// FloatingBox.
/// </summary>
public sealed class Paragraphs : IList<BaseParagraph>, IReadOnlyList<BaseParagraph>
{
    private readonly List<BaseParagraph> _items = new();

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public BaseParagraph this[int index]
    {
        get => _items[index];
        set
        {
            if (value is null)
                throw new PdfException("Paragraph item cannot be null.");
            _items[index] = value;
        }
    }

    public void Add(BaseParagraph paragraph)
    {
        if (paragraph is null)
            throw new PdfException("Paragraph item cannot be null.");
        _items.Add(paragraph);
    }

    public void Insert(int index, BaseParagraph paragraph)
    {
        if (paragraph is null)
            throw new PdfException("Paragraph item cannot be null.");
        _items.Insert(index, paragraph);
    }

    public void AddRange(IEnumerable<BaseParagraph> items)
    {
        if (items is null) throw new PdfException("Paragraph item cannot be null.");
        foreach (var item in items) Add(item);
    }

    public void InsertRange(int index, IEnumerable<BaseParagraph> collection)
    {
        if (collection is null) throw new PdfException("Paragraph item cannot be null.");
        foreach (var item in collection)
            if (item is null) throw new PdfException("Paragraph item cannot be null.");
        _items.InsertRange(index, collection);
    }

    /// <summary>Drop <paramref name="count"/> entries starting at <paramref name="index"/>.</summary>
    public void RemoveRange(int index, int count) => _items.RemoveRange(index, count);

    /// <summary>Slice a sub-range into a new Paragraphs collection.</summary>
    public Paragraphs GetRange(int index, int count)
    {
        var slice = new Paragraphs();
        for (var i = 0; i < count; i++)
            slice._items.Add(_items[index + i]);
        return slice;
    }

    /// <summary>Shallow copy of the collection (items are shared by reference).</summary>
    public object Clone()
    {
        var c = new Paragraphs();
        c._items.AddRange(_items);
        return c;
    }

    public void Clear() => _items.Clear();

    public bool Contains(BaseParagraph item) => _items.Contains(item);

    public void CopyTo(BaseParagraph[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public int IndexOf(BaseParagraph item) => _items.IndexOf(item);

    /// <summary>Drop the first occurrence of <paramref name="paragraph"/>.</summary>
    public void Remove(BaseParagraph paragraph) => _items.Remove(paragraph);

    bool ICollection<BaseParagraph>.Remove(BaseParagraph item) => _items.Remove(item);

    public void RemoveAt(int index) => _items.RemoveAt(index);

    public IEnumerator<BaseParagraph> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
