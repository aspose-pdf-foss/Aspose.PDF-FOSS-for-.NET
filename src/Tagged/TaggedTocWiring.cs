using System.Text;
using Aspose.Pdf.Core;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

/// <summary>Marked content a table-of-contents page drew for an authored tagged document: the
/// title (no item, no annotation) or an entry, with the entry's text, the TOC item that carries
/// it and the link annotation the entry got.</summary>
internal sealed record TocTag(LS.StructureElement Header, LS.StructureElement? Item, Page Page, int Mcid,
    PdfDictionary? Annotation, string Text);

/// <summary>Ties what a table-of-contents page drew to the structure tree, after the page layout:
/// the title's marked content becomes the bound header's content, and each entry becomes a Link
/// element under its TOC item, holding the entry's marked content and its link annotation.</summary>
internal static class TaggedTocWiring
{
    public static void Wire(Document doc, LS.StructureElement root)
    {
        var tags = doc.PendingTocTags;
        if (tags.Count == 0) return;
        foreach (var tag in tags)
        {
            if (tag.Item is null)
            {
                tag.Header.TagMarkedContent(doc, tag.Page, tag.Mcid);
                continue;
            }
            var link = new LS.LinkElement();
            link._dict.Set("Alt", Aspose.Pdf.Forms.Field.EncodePdfTextString(tag.Text));
            // A list item holds only a label and a body (ISO 32000-1 14.8.4.3.3), so the link of
            // a nested entry goes into the item's body; a TOC item takes the link itself.
            var holder = tag.Item;
            if (tag.Item.Role == "LI")
            {
                holder = new LS.ListLBodyElement();
                tag.Item.AppendChild(holder, validate: false);
            }
            holder.AppendChild(link, validate: false);
            link.TagMarkedContent(doc, tag.Page, tag.Mcid);
            if (tag.Annotation is { } annotation)
                link.TagObject(doc, annotation, tag.Page);
        }
        tags.Clear();
        ManualTagWiring.Wire(doc, root);
    }
}
