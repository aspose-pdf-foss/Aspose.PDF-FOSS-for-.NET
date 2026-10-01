using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A marked-content reference (/Type /MCR) in a structure element's /K: names the
/// marked-content sequence (<see cref="MCID"/>) on a page (/Pg) that holds the element's
/// content. The auto-tagger creates one per run of content; a loaded document surfaces each
/// MCR dictionary as one of these. (Bare MCID integers in /K are not surfaced as elements.)</summary>
public sealed class MCRElement : StructureElement
{
    internal MCRElement() : base("MCR") { }
    internal MCRElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>The structure element whose content this reference names.</summary>
    public StructureElement? ParentStructureElement => _parent;

    /// <summary>The marked-content identifier on the referenced page; -1 when absent.</summary>
    public int MCID => (_reader is not null ? _reader.Resolve(_dict.Get("MCID")) : _dict.Get("MCID")) is PdfInteger n
        ? (int)n.Value
        : -1;
}
