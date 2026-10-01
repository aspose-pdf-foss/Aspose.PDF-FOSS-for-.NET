using System;
using Aspose.Pdf.Core;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Tagged;

namespace Aspose.Pdf.LogicalStructure;

public partial class StructureElement
{
    /// <summary>Makes the marked-content sequence <paramref name="bdc"/> opens the content of this
    /// element: a marked-content reference (/Type /MCR) naming the page and the sequence's /MCID
    /// joins this element's children, and the page's parent tree points that id back here on
    /// save. A BDC written without an /MCID is given the page's next free one, and the content
    /// stream is rewritten with it.</summary>
    public MCRElement Tag(BDC bdc)
    {
        if (bdc is null) throw new ArgumentNullException(nameof(bdc));
        var doc = FindSourceDocument()
            ?? throw new TaggedException("The structure element belongs to no document.");
        var page = PageHolding(doc, bdc)
            ?? throw new TaggedException("The marked content is on no page of this document.");
        var mcid = bdc.Properties?.MCID ?? NextMcid(page);
        if (bdc.Properties?.MCID != mcid)
        {
            bdc.Retag(mcid);
            page.Contents.MarkEdited();
        }
        return TagMarkedContent(doc, page, mcid);
    }

    /// <summary>Makes <paramref name="annotation"/> the content of this element: an object
    /// reference (/Type /OBJR) to the annotation joins this element's children, and on save the
    /// annotation's /StructParent and the parent tree point back here.</summary>
    public OBJRElement Tag(Aspose.Pdf.Annotations.Annotation annotation)
    {
        if (annotation is null) throw new ArgumentNullException(nameof(annotation));
        var doc = FindSourceDocument()
            ?? throw new TaggedException("The structure element belongs to no document.");
        return TagObject(doc, annotation.Dict, annotation.Page);
    }

    /// <summary>The marked-content sequence <paramref name="mcid"/> on <paramref name="page"/> as
    /// this element's content: the reference every reader looks for, its file side written on save.</summary>
    internal MCRElement TagMarkedContent(Document doc, Page page, int mcid)
    {
        var kid = new PdfDictionary();
        kid.Set("Type", new PdfName("MCR"));
        kid.Set("MCID", new PdfInteger(mcid));
        var mcr = new MCRElement(kid, _reader);
        AttachReference(mcr);
        doc.PendingManualTags.Add(new ManualTag(this, kid, page, mcid, null));
        return mcr;
    }

    /// <summary>The annotation <paramref name="annotation"/> as this element's content, through an
    /// object reference; its file side is written on save.</summary>
    internal OBJRElement TagObject(Document doc, PdfDictionary annotation, Page? page)
    {
        var kid = new PdfDictionary();
        kid.Set("Type", new PdfName("OBJR"));
        var objr = new OBJRElement(kid, _reader);
        AttachReference(objr);
        doc.PendingManualTags.Add(new ManualTag(this, kid, page, -1, annotation));
        return objr;
    }

    /// <summary>A marked-content or object reference becomes the last child, in memory and in /K.</summary>
    private void AttachReference(StructureElement reference)
    {
        EnsureChildrenLoaded();
        Adopt(reference);
        _children!.Add(reference);
        KidsArray().Add(reference._dict);
    }

    /// <summary>The page whose content holds the operator <paramref name="bdc"/>, by identity: the
    /// collections hand out stable operator instances once read.</summary>
    private static Page? PageHolding(Document doc, BDC bdc)
    {
        foreach (var page in doc.Pages)
        {
            var contents = page.Contents;
            contents.EnsureMaterialized();
            foreach (var op in contents)
                if (ReferenceEquals(op, bdc))
                    return page;
        }
        return null;
    }

    /// <summary>One past the largest marked-content id the page's content already carries.</summary>
    private static int NextMcid(Page page)
    {
        var max = -1;
        foreach (var op in page.Contents)
            if (op is BDC other && other.Properties?.MCID is { } id && id > max)
                max = id;
        return max + 1;
    }
}
