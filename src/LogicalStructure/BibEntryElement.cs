using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class BibEntryElement : StructureElement
{
    internal BibEntryElement() : base("BibEntry") { }
    internal BibEntryElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
