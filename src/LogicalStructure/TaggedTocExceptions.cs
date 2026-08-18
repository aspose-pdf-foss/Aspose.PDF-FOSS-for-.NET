namespace Aspose.Pdf.LogicalStructure;

/// <summary>Thrown by <see cref="TOCElement.LinkTocPageTitleToHeaderElement"/>
/// when the TOC page's <see cref="Aspose.Pdf.TocInfo"/> carries no title —
/// the tagged-TOC navigation header must mirror an existing page title
/// (PDF/UA-1 tagged TOC support).</summary>
public class TOCpageHasNoTitleException : System.Exception
{
    public TOCpageHasNoTitleException()
        : base("The TOC page has no TocInfo title to link the header element to.") { }

    public TOCpageHasNoTitleException(string message) : base(message) { }
}

/// <summary>Thrown at save when a header element linked via
/// <see cref="TOCElement.LinkTocPageTitleToHeaderElement"/> carries its own
/// text that differs from the TOC page's <see cref="Aspose.Pdf.TocInfo"/>
/// title — the two would render conflicting navigation titles.</summary>
public class HeaderElementTextConflictException : System.Exception
{
    public HeaderElementTextConflictException()
        : base("The linked header element's text conflicts with the TOC page title.") { }

    public HeaderElementTextConflictException(string message) : base(message) { }
}
