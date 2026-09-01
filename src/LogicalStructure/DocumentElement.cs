using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>The document root structure element (/S Document).</summary>
public sealed class DocumentElement : StructureElement
{
    internal DocumentElement() : base("Document") { }
    internal DocumentElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
