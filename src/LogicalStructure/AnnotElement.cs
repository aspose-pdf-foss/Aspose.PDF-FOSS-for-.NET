using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class AnnotElement : StructureElement
{
    internal AnnotElement() : base("Annot") { }
    internal AnnotElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
