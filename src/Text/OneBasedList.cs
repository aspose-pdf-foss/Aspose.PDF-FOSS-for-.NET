using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>Read-only list with 1-based indexer, matching the public API.</summary>
public sealed class OneBasedList<T>(IReadOnlyList<T> inner) : IReadOnlyList<T>
{
    public T this[int index] => inner[index - 1];
    public int Count => inner.Count;
    public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
