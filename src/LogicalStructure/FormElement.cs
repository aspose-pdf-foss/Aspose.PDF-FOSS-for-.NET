using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class FormElement : StructureElement
{
    internal FormElement() : base("Form") { }
    internal FormElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
