using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

public sealed class TOCElement : StructureElement
{
    internal TOCElement() : base("TOC") { }
    internal TOCElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    // PDF/UA-1 tagged-TOC navigation: the TOC page whose TocInfo.Title the
    // linked header element mirrors (LinkTocPageTitleToHeaderElement).
    private Page? _linkedTocPage;
    private HeaderElement? _linkedTitleHeader;

    /// <summary>Links the TOC page's <see cref="TocInfo"/> title to the given
    /// header element so the tagged navigation header carries the page title
    /// (PDF/UA-1 tagged TOC). Throws <see cref="TOCpageHasNoTitleException"/>
    /// when the page's TocInfo has no title to link.</summary>
    public void LinkTocPageTitleToHeaderElement(Page tocPage, HeaderElement tocTitleHeader)
    {
        if (tocPage?.TocInfo?.Title is not { } title || string.IsNullOrEmpty(title.Text))
            throw new TOCpageHasNoTitleException();
        _linkedTocPage = tocPage;
        _linkedTitleHeader = tocTitleHeader;
    }

    /// <summary>Save-time consistency check for the linked title (called from
    /// the document's tagged-save path): a header that carries its OWN text
    /// different from the TOC page title is a conflict; an empty header
    /// inherits the page title.</summary>
    internal void ValidateLinkedTitleOnSave()
    {
        if (_linkedTocPage?.TocInfo?.Title is not { } title || _linkedTitleHeader is null)
            return;
        var headerText = _linkedTitleHeader.ActualText;
        var titleText = title.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(headerText) && headerText != titleText)
            throw new HeaderElementTextConflictException();
        if (string.IsNullOrEmpty(headerText))
            _linkedTitleHeader.SetText(titleText);
    }
}
