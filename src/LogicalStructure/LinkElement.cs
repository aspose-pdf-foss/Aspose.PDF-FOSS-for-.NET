using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class LinkElement : StructureElement
{
    internal LinkElement() : base("Link") { }
    internal LinkElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>The hyperlink target for this link element. Stored only —
    /// the FOSS structure builder records it but doesn't emit a /Link
    /// annotation for it.</summary>
    public Aspose.Pdf.WebHyperlink? Hyperlink { get; set; }

    /// <summary>Alternate description(s) for the link (/Alt). Stored only.</summary>
    public string? AlternateDescriptions { get; set; }
}
