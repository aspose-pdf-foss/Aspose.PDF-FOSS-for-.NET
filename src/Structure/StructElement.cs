using System.Collections;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Structure;

/// <summary>A generic structure element (anything other than the
/// recognised typed subclasses).</summary>
public class StructElement : Element
{
    internal StructElement(PdfDictionary dict, PdfReader reader, Element? parent)
        : base(dict, reader, parent) { }
}
