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

    public void Add(PdfObject item) => _items.Add(item);
    public void Insert(int index, PdfObject item) => _items.Insert(index, item);
    public void RemoveAt(int index) => _items.RemoveAt(index);
    public void ReplaceAt(int index, PdfObject item) => _items[index] = item;

    public IEnumerator<PdfObject> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
