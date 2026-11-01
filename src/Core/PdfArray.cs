using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed class PdfArray : PdfObject, IReadOnlyList<PdfObject>
{
    private readonly List<PdfObject> _items;

    public PdfArray() => _items = [];
    public PdfArray(List<PdfObject> items) => _items = items;

    public PdfObject this[int index] => _items[index];
    public int Count => _items.Count;

    /// <summary>True once an item was added, removed or replaced after the array was read: an incremental
    /// save writes the object that holds it. An array filled by the reader (<see cref="AddRead"/>) is not edited.</summary>
    internal bool Edited { get; private set; }

    public void Add(PdfObject item) { _items.Add(item); Edited = true; }
    public void Insert(int index, PdfObject item) { _items.Insert(index, item); Edited = true; }
    public void RemoveAt(int index) { _items.RemoveAt(index); Edited = true; }
    public void ReplaceAt(int index, PdfObject item) { _items[index] = item; Edited = true; }

    /// <summary>An item as the file states it, added or replaced by the reader: no edit of the document.</summary>
    internal void AddRead(PdfObject item) => _items.Add(item);
    internal void ReplaceRead(int index, PdfObject item) => _items[index] = item;

    public IEnumerator<PdfObject> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
