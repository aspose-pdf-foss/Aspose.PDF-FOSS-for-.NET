using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A figure structure element (tag <c>/Figure</c>) that marks an illustration such as an image or drawing.</summary>
public sealed class FigureElement : IllustrationElement
{
    internal FigureElement() : base("Figure") { }
    internal FigureElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
