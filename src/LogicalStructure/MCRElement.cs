using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A marked-content reference leaf (role "MCR") emitted by the auto-tagger to mark
/// where a structure element's page content lives. Counted by
/// <see cref="StructTreeRootElement.AllElements"/>. (A loaded document's bare MCID integers /
/// /MCR dicts in /K are intentionally NOT surfaced as elements — only these explicit role-MCR
/// structure elements are, so the auto-tagger's tree round-trips without inflating the element
/// count of externally-authored tagged PDFs.)</summary>
public sealed class MCRElement : StructureElement
{
    internal MCRElement() : base("MCR") { }
    internal MCRElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
}
