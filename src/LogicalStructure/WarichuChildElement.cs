using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>Base for the warichu content elements (WT, WP) that appear only
/// inside a <see cref="WarichuElement"/>.</summary>
public abstract class WarichuChildElement : StructureElement
{
    private protected WarichuChildElement(string structureType) : base(structureType) { }
    private protected WarichuChildElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
