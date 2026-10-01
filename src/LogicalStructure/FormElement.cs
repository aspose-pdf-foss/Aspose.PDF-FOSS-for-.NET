using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A form structure element (tag <c>/Form</c>) that marks a form field or other interactive widget.</summary>
public sealed class FormElement : StructureElement
{
    internal FormElement() : base("Form") { }
    internal FormElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
