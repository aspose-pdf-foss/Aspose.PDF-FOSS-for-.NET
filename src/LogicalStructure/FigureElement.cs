using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class FigureElement : IllustrationElement
{
    internal FigureElement() : base("Figure") { }
    internal FigureElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
