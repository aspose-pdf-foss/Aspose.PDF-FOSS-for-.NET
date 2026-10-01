using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A list structure element (tag <c>/L</c>) that holds list items.</summary>
public sealed class ListElement : StructureElement
{
    internal ListElement() : base("L") { }
    internal ListElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
