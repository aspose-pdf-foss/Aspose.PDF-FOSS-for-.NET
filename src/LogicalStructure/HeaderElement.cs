using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>A heading structure element: tag <c>/H</c>, or <c>/H1</c>, <c>/H2</c> ... when created with a level.</summary>
public sealed class HeaderElement : StructureElement
{
    internal HeaderElement() : base("H") { }
    internal HeaderElement(int level) : base(level <= 0 ? "H" : $"H{level}") { }
    internal HeaderElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>The table-of-contents page this header asked for an entry on, and the TOC item
    /// (a TOCI or a list item) that stands for it there; null until AddEntryToTocPage.</summary>
    internal (Page TocPage, StructureElement Item)? TocEntry { get; private set; }

    /// <summary>Asks for an entry on <paramref name="tocPage"/> - a table-of-contents page, one
    /// with a <see cref="Page.TocInfo"/> - reading this header's text and naming the page the
    /// header lands on, with <paramref name="tocEntry"/> as the item that stands for it in the
    /// structure tree. The entry is written when the document is rendered on save.</summary>
    public void AddEntryToTocPage(Page tocPage, TOCIElement tocEntry) => AddTocEntry(tocPage, tocEntry);

    /// <summary>The same entry, standing for a list item of the table of contents.</summary>
    public void AddEntryToTocPage(Page tocPage, ListLIElement tocEntry) => AddTocEntry(tocPage, tocEntry);

    private void AddTocEntry(Page tocPage, StructureElement item)
    {
        if (tocPage is null) throw new System.ArgumentNullException(nameof(tocPage));
        if (item is null) throw new System.ArgumentNullException(nameof(item));
        var info = tocPage.TocInfo
            ?? throw new System.ArgumentException("tocPage.TocInfo cannot be null.", nameof(tocPage));
        TocEntry = (tocPage, item);
        info.TaggedEntries.Add((this, item));
    }
}
