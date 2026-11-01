#nullable disable
using System;
using System.Collections;

namespace Aspose.Pdf;

public class DocumentCollection : IEnumerable
{
    public int Count { get; }
    public void Add(Document doc) { /* nothing in this library reads the collection */ }
    public IEnumerator GetEnumerator() => throw new NotImplementedException();
}
