using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>Base for the warichu content elements (WT, WP) that appear only
/// inside a <see cref="WarichuElement"/>.</summary>
public abstract class WarichuChildElement : StructureElement
{
    internal WarichuChildElement(string structureType) : base(structureType) { }
    internal WarichuChildElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
