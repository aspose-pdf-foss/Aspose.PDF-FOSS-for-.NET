using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class ArtElement : StructureElement
{
    internal ArtElement() : base("Art") { }
    internal ArtElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
