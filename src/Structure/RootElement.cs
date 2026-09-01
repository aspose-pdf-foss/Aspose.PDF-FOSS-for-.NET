using System.Collections;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Structure;

/// <summary>Top-level wrapper for the PDF /StructTreeRoot dictionary.
/// Returned by <see cref="Aspose.Pdf.Document.LogicalStructure"/>.</summary>
public sealed class RootElement : Element
{
    internal RootElement(PdfDictionary dict, PdfReader reader)
        : base(dict, reader, parent: null) { }
}
