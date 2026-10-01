using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Tagging by hand: a caller who wrote the marked content or placed the annotation
/// attaches it to a structure element, and the saved file says so in every place a reader
/// looks (the element's /K, the page's parent tree, the annotation's /StructParent).</summary>
public class ManualTaggingTests
{
    private static byte[] Saved(Document document)
    {
        using var ms = new MemoryStream();
        document.Save(ms);
        return ms.ToArray();
    }

    /// <summary>A one-page document whose only text is wrapped, after a round trip, in a BDC that
    /// already names its MCID, language and expansion.</summary>
    private static Document WithMarkedText(out BDC bdc)
    {
        var first = new Document();
        var page = first.Pages.Add();
        page.Paragraphs.Add(new Aspose.Pdf.Text.TextFragment("Helloworld"));
        var reopened = new Document(new MemoryStream(Saved(first)));
        var operators = reopened.Pages[1].Contents;
        bdc = new BDC("P", new Aspose.Pdf.Facades.BDCProperties(1, "ru", "Hello world"));
        for (var i = 1; i <= operators.Count; i++)
        {
            var op = operators[i];
            if (op is BT) { operators.Insert(i - 1, bdc); i++; }
            if (op is ET) { operators.Insert(i + 1, new EMC()); i++; }
        }
        return reopened;
    }

    [Fact]
    public void Tag_MarkedContent_WritesAnMcrTheReaderGetsBack()
    {
        var document = WithMarkedText(out var bdc);
        var span = document.TaggedContent.CreateSpanElement();
        document.TaggedContent.RootElement.AppendChild(span);

        var mcr = span.Tag(bdc);

        Assert.Equal(1, mcr.MCID);
        Assert.Same(span, mcr.ParentElement);
        Assert.Contains(mcr, span.ChildElements);

        using var reread = new Document(new MemoryStream(Saved(document)));
        var readMcr = Assert.IsType<MCRElement>(reread.TaggedContent.RootElement.ChildElements[0].ChildElements[0]);
        Assert.Equal(1, readMcr.MCID);
        var readBdc = Assert.Single(reread.Pages[1].Contents.OfType<BDC>());
        Assert.Equal(("ru", "Hello world", 1), (readBdc.Properties!.Lang, readBdc.Properties.E, readBdc.Properties.MCID));
        // The page's parent tree points the id back at the span.
        var page = reread.Pages[1];
        var key = Assert.IsType<PdfInteger>(reread.Reader.Resolve(page.Dict.Get("StructParents")));
        var parentTree = reread.Reader.ResolveDict(reread.Reader.ResolveDict(reread.Catalog.Get("StructTreeRoot"))!.Get("ParentTree"))!;
        var nums = Assert.IsType<PdfArray>(reread.Reader.Resolve(parentTree.Get("Nums")));
        var index = Enumerable.Range(0, nums.Count / 2).First(i => ((PdfInteger)reread.Reader.Resolve(nums[2 * i])!).Value == key.Value);
        var pageArray = Assert.IsType<PdfArray>(reread.Reader.Resolve(nums[2 * index + 1]));
        var owner = Assert.IsType<PdfDictionary>(reread.Reader.Resolve(pageArray[1]));
        Assert.Equal("Span", owner.GetName("S"));
    }

    [Fact]
    public void Tag_MarkedContentWithoutAnId_GetsThePagesNextId()
    {
        var document = WithMarkedText(out var numbered);
        var bare = new BDC("Figure");
        var operators = document.Pages[1].Contents;
        operators.Insert(1, bare);
        operators.Insert(3, new EMC());
        var figure = document.TaggedContent.CreateFigureElement();
        document.TaggedContent.RootElement.AppendChild(figure);

        var mcr = figure.Tag(bare);

        Assert.Equal(2, mcr.MCID);  // the numbered one holds 1
        Assert.Equal(2, bare.Properties!.MCID);
        using var reread = new Document(new MemoryStream(Saved(document)));
        Assert.Contains(reread.Pages[1].Contents.OfType<BDC>(), b => b.Tag == "Figure" && b.Properties?.MCID == 2);
        Assert.Equal(2, ((MCRElement)reread.TaggedContent.RootElement.ChildElements[0].ChildElements[0]).MCID);
    }

    [Fact]
    public void ClearChilds_EmptiesALoadedElement_AndTheTreeSavesWithoutIt()
    {
        using var document = Document.Open(PdfBuilder.BuildTagged());
        var root = document.TaggedContent.RootElement;
        var paragraph = root.ChildElements[0];
        Assert.Single(root.ChildElements);

        root.ClearChilds();

        Assert.Empty(root.ChildElements);
        Assert.Null(paragraph.ParentElement);
        using var reread = new Document(new MemoryStream(Saved(document)));
        Assert.Empty(reread.TaggedContent.RootElement.ChildElements);
    }

    [Fact]
    public void InsertChild_PlacesTheElementAtTheIndex_AndRemoveChildTakesItOut()
    {
        var document = new Document();
        var content = document.TaggedContent;
        var root = content.RootElement;
        var first = content.CreateParagraphElement();
        var last = content.CreateParagraphElement();
        root.AppendChild(first);
        root.AppendChild(last);
        var middle = content.CreateFigureElement();

        root.InsertChild(middle, 1);

        Assert.Equal(new StructureElement[] { first, middle, last }, root.ChildElements.ToArray());
        Assert.Same(root, middle.ParentElement);
        var kids = (PdfArray)root._dict.Get("K")!;
        Assert.Same(middle._dict, kids[1]);

        root.RemoveChild(0);

        Assert.Equal(new StructureElement[] { middle, last }, root.ChildElements.ToArray());
        Assert.Null(first.ParentElement);
        Assert.Equal(2, kids.Count);
    }

    [Fact]
    public void AddEntryToTocPage_DrawsTheEntries_AndTiesThemToTheirItems()
    {
        var document = new Document();
        var content = document.TaggedContent;
        var root = content.RootElement;
        var tocPage = document.Pages.Add();
        tocPage.TocInfo = new TocInfo { Title = new Aspose.Pdf.Text.TextFragment("Table of Contents") };
        var title = content.CreateHeaderElement(1);
        root.AppendChild(title);
        var toc = content.CreateTOCElement();
        root.AppendChild(toc);
        toc.LinkTocPageTitleToHeaderElement(tocPage, title);
        document.Pages.Add();
        var toci = content.CreateTOCIElement();
        toc.AppendChild(toci);
        var header = content.CreateHeaderElement(1);
        header.SetText("1. Main Header");
        header.AddEntryToTocPage(tocPage, toci);
        toci.AddRef(header);
        root.AppendChild(header);
        var body = content.CreateParagraphElement();
        body.SetText("some text");
        root.AppendChild(body);

        using var reread = new Document(new MemoryStream(Saved(document)));

        Assert.Equal(2, reread.Pages.Count);
        var tocText = new Aspose.Pdf.Text.TextAbsorber();
        reread.Pages[1].Accept(tocText);
        Assert.Contains("Table of Contents", tocText.Text);
        Assert.Contains("1. Main Header", tocText.Text);
        Assert.EndsWith("2", tocText.Text.Trim());  // the entry names the page the header landed on
        var bodyText = new Aspose.Pdf.Text.TextAbsorber();
        reread.Pages[2].Accept(bodyText);
        Assert.DoesNotContain("Table of Contents", bodyText.Text);  // the bound title header renders only as the title
        Assert.Single(reread.Pages[1].Annotations);
        var readRoot = reread.TaggedContent.RootElement;
        var readTitle = readRoot.ChildElements[0];
        Assert.Equal("H1", readTitle.Role);
        Assert.IsType<MCRElement>(Assert.Single(readTitle.ChildElements));
        var readToci = Assert.Single(readRoot.ChildElements[1].ChildElements);
        var link = Assert.IsType<LinkElement>(Assert.Single(readToci.ChildElements));
        Assert.Equal(new[] { "MCRElement", "OBJRElement" }, link.ChildElements.Select(k => k.GetType().Name).ToArray());
        var target = Assert.IsType<PdfDictionary>(((OBJRElement)link.ChildElements[1]).Obj);
        Assert.Equal("Link", target.GetName("Subtype"));
    }

    [Fact]
    public void Tag_Annotation_WritesAnObjrAndTheAnnotationsStructParent()
    {
        var document = new Document();
        var page = document.Pages.Add();
        page.Paragraphs.Add(new Aspose.Pdf.Text.TextFragment("a link"));
        var annotation = new LinkAnnotation(page, new Rectangle(50, 700, 150, 720));
        page.Annotations.Add(annotation);
        var link = document.TaggedContent.CreateLinkElement();
        document.TaggedContent.RootElement.AppendChild(link);

        var objr = link.Tag(annotation);

        Assert.Same(link, objr.ParentElement);
        using var reread = new Document(new MemoryStream(Saved(document)));
        var readLink = reread.TaggedContent.RootElement.ChildElements[0];
        var readObjr = Assert.IsType<OBJRElement>(readLink.ChildElements[0]);
        var target = Assert.IsType<PdfDictionary>(readObjr.Obj);
        Assert.Equal("Link", target.GetName("Subtype"));
        var key = Assert.IsType<PdfInteger>(reread.Reader.Resolve(target.Get("StructParent")));
        var parentTree = reread.Reader.ResolveDict(reread.Reader.ResolveDict(reread.Catalog.Get("StructTreeRoot"))!.Get("ParentTree"))!;
        var nums = Assert.IsType<PdfArray>(reread.Reader.Resolve(parentTree.Get("Nums")));
        var index = Enumerable.Range(0, nums.Count / 2).First(i => ((PdfInteger)reread.Reader.Resolve(nums[2 * i])!).Value == key.Value);
        var owner = Assert.IsType<PdfDictionary>(reread.Reader.Resolve(nums[2 * index + 1]));
        Assert.Equal("Link", owner.GetName("S"));
    }

    [Fact]
    public void Tag_BeforeTheElementJoinsTheTree_WritesTheSameObjr()
    {
        var document = new Document();
        var page = document.Pages.Add();
        var field = new Aspose.Pdf.Forms.SignatureField(page, new Rectangle(50, 50, 100, 100)) { PartialName = "Signature1" };
        document.Form.Add(field);
        var form = document.TaggedContent.CreateFormElement();

        var objr = form.Tag(field);
        document.TaggedContent.RootElement.AppendChild(form);

        Assert.Same(form, objr.ParentElement);
        using var reread = new Document(new MemoryStream(Saved(document)));
        var readForm = reread.TaggedContent.RootElement.ChildElements[0];
        var readObjr = Assert.IsType<OBJRElement>(readForm.ChildElements[0]);
        var target = Assert.IsType<PdfDictionary>(readObjr.Obj);
        Assert.Equal("Widget", target.GetName("Subtype"));
        Assert.IsType<PdfInteger>(reread.Reader.Resolve(target.Get("StructParent")));
    }
}
