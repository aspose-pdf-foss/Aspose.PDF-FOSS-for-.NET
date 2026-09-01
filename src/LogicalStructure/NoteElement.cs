using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class NoteElement : StructureElement
{
    internal NoteElement() : base("Note") { }
    internal NoteElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
