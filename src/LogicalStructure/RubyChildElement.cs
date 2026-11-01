using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>Base for the ruby-annotation content elements (RB, RT, RP) that
/// appear only inside a <see cref="RubyElement"/>.</summary>
public abstract class RubyChildElement : StructureElement
{
    private protected RubyChildElement(string structureType) : base(structureType) { }
    private protected RubyChildElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
