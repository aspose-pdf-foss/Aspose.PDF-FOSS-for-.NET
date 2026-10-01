using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>The /StructTreeRoot wrapper at the top of the logical-
/// structure tree. Hosts the document's top-level structure elements
/// under its /K entry.</summary>
public sealed class StructTreeRootElement : StructureElement
{
    internal StructTreeRootElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }
    internal StructTreeRootElement() : base(new PdfDictionary(), null)
    {
        _dict.Set("Type", new PdfName("StructTreeRoot"));
    }

    /// <summary>Flat list of every structure element below the root,
    /// produced by a depth-first walk of <see cref="StructureElement.ChildElements"/>.</summary>
    public IReadOnlyList<StructureElement> AllElements
    {
        get
        {
            // Every element of the tree, marked-content and object references included
            // (a FindElements query for StructureElement leaves those out).
            var all = new List<StructureElement>();
            void Walk(StructureElement el)
            {
                foreach (var child in el.ChildElements)
                {
                    all.Add(child);
                    if (child is not (MCRElement or OBJRElement)) Walk(child);
                }
            }
            Walk(this);
            return all;
        }
    }
}
