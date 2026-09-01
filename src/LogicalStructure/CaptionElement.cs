using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class CaptionElement : StructureElement
{
    internal CaptionElement() : base("Caption") { }
    internal CaptionElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
