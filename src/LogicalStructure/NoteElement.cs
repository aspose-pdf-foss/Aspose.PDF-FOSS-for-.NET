using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A note structure element (tag <c>/Note</c>), such as a footnote or endnote.</summary>
public sealed class NoteElement : StructureElement
{
    internal NoteElement() : base("Note") { }
    internal NoteElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
