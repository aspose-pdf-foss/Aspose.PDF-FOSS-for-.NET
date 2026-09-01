#nullable disable
using System;
using System.Collections;

namespace Aspose.Pdf;

public class DocumentCollection : IEnumerable
{
    public int Count { get; }
    public void Add(Document doc) { }
    public IEnumerator GetEnumerator() => throw new NotImplementedException();
}
