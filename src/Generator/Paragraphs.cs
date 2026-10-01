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

    /// <summary>Creates an empty paragraph collection.</summary>
    public Paragraphs() { }

    /// <summary>Gets the number of paragraphs in the collection.</summary>
    public int Count => _items.Count;

    public bool IsReadOnly => false;

    /// <summary>Gets or sets the paragraph at the given zero-based index. Setting <c>null</c> throws a <c>PdfException</c>.</summary>
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

    /// <summary>Appends a paragraph to the end of the collection. Throws a <c>PdfException</c> for <c>null</c>; an <c>HtmlFragment</c> that is already in the collection is not added again.</summary>
    public void Add(BaseParagraph paragraph)
    {
        if (paragraph is null)
            throw new PdfException("Paragraph item cannot be null.");
        // An HtmlFragment already in the collection stays where it is: adding the same
        // instance again lays it out once, in its first place (probed: a fragment added
        // before and after another renders only in its first position). Every other
        // paragraph kind repeats with each add (a blank TextFragment added three times
        // is three lines, an image added twice is drawn twice).
        if (paragraph is HtmlFragment && _items.Contains(paragraph)) return;
        _items.Add(paragraph);
    }

    /// <summary>Inserts a paragraph at the given zero-based index. Throws a <c>PdfException</c> for <c>null</c>.</summary>
    public void Insert(int index, BaseParagraph paragraph)
    {
        if (paragraph is null)
            throw new PdfException("Paragraph item cannot be null.");
        _items.Insert(index, paragraph);
    }

    /// <summary>Appends each paragraph of the sequence in order, as <c>Add</c> does. Throws a <c>PdfException</c> when the sequence is <c>null</c>.</summary>
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

    /// <summary>Removes all paragraphs from the collection.</summary>
    public void Clear() => _items.Clear();

    /// <summary>Returns <c>true</c> when the given paragraph instance is in the collection.</summary>
    public bool Contains(BaseParagraph item) => _items.Contains(item);

    /// <summary>Copies the paragraphs into an array, starting at the given array index.</summary>
    public void CopyTo(BaseParagraph[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public int IndexOf(BaseParagraph item) => _items.IndexOf(item);

    /// <summary>Drop the first occurrence of <paramref name="paragraph"/>.</summary>
    public void Remove(BaseParagraph paragraph) => _items.Remove(paragraph);

    bool ICollection<BaseParagraph>.Remove(BaseParagraph item) => _items.Remove(item);

    public void RemoveAt(int index) => _items.RemoveAt(index);

    public IEnumerator<BaseParagraph> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
