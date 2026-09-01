using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>Base for the ruby-annotation content elements (RB, RT, RP) that
/// appear only inside a <see cref="RubyElement"/>.</summary>
public abstract class RubyChildElement : StructureElement
{
    internal RubyChildElement(string structureType) : base(structureType) { }
    internal RubyChildElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
