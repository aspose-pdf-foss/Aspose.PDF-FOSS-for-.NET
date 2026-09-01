using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class FormulaElement : IllustrationElement
{
    internal FormulaElement() : base("Formula") { }
    internal FormulaElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
