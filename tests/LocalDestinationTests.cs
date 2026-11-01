using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>
/// A local destination names its page by a reference to the page object (ISO 32000-1
/// 12.3.2.2); the integer form belongs to a remote GoTo.
/// </summary>
public class LocalDestinationTests
{
    private static Document Reopen(Document doc)
    {
        var stream = new MemoryStream();
        doc.Save(stream);
        stream.Position = 0;
        return new Document(stream);
    }

    private static void AssertRefersTo(Document doc, PdfObject? destination, int pageNumber)
    {
        var array = Assert.IsType<PdfArray>(doc.Reader.Resolve(destination));
        var reference = Assert.IsType<PdfIndirectRef>(array[0]);
        Assert.Equal(doc.Pages[pageNumber].SourceObjectNumber, reference.ObjectNumber);
    }

    [Fact]
    public void LinkOutlineAndGoTo_OnNewPages_ReferToThePage()
    {
        var doc = new Document();
        var first = doc.Pages.Add();
        var second = doc.Pages.Add();
        first.Annotations.Add(new LinkAnnotation(first, new Rectangle(100, 100, 200, 120))
        {
            Destination = new XYZExplicitDestination(second, 10, 500, 0),
        });
        first.Annotations.Add(new LinkAnnotation(first, new Rectangle(100, 200, 200, 220))
        {
            Action = new GoToAction(new FitExplicitDestination(second)),
        });
        doc.Outlines.Add(new OutlineItemCollection(doc.Outlines) { Title = "Second", Destination = new FitExplicitDestination(second) });

        using var saved = Reopen(doc);
        var links = saved.Pages[1].Annotations;
        AssertRefersTo(saved, links[1].Dict.Get("Dest"), 2);
        var action = saved.Reader.ResolveDict(links[2].Dict.Get("A"))!;
        AssertRefersTo(saved, action.Get("D"), 2);
        var outlines = saved.Reader.ResolveDict(saved.Reader.Catalog.Get("Outlines"))!;
        var item = saved.Reader.ResolveDict(outlines.Get("First"))!;
        AssertRefersTo(saved, item.Get("Dest"), 2);
    }

    [Fact]
    public void Destination_ReadsTheSame_FromTheLiveDocumentAfterASave()
    {
        var doc = new Document();
        var first = doc.Pages.Add();
        var second = doc.Pages.Add();
        first.Annotations.Add(new LinkAnnotation(first, new Rectangle(100, 100, 200, 120))
        {
            Destination = new FitExplicitDestination(second),
        });
        doc.Save(new MemoryStream());

        var link = (LinkAnnotation)doc.Pages[1].Annotations[1];
        Assert.Equal(2, ((ExplicitDestination)link.Destination!).PageNumber);
    }

    [Fact]
    public void SecondSave_AfterAPageIsInserted_StillReachesTheSamePage()
    {
        var doc = new Document();
        var first = doc.Pages.Add();
        var second = doc.Pages.Add();
        first.Annotations.Add(new LinkAnnotation(first, new Rectangle(100, 100, 200, 120))
        {
            Destination = new FitExplicitDestination(second),
        });
        doc.Save(new MemoryStream());
        doc.Pages.Insert(1);

        using var saved = Reopen(doc);
        var link = saved.Pages[2].Annotations[1];
        AssertRefersTo(saved, link.Dict.Get("Dest"), 3);
    }

    [Fact]
    public void RemoteGoTo_KeepsThePageIndex()
    {
        var doc = new Document();
        var page = doc.Pages.Add();
        page.Annotations.Add(new LinkAnnotation(page, new Rectangle(100, 100, 200, 120))
        {
            Action = new GoToRemoteAction("other.pdf", new FitExplicitDestination(3)),
        });

        using var saved = Reopen(doc);
        var action = saved.Reader.ResolveDict(saved.Pages[1].Annotations[1].Dict.Get("A"))!;
        var array = Assert.IsType<PdfArray>(saved.Reader.Resolve(action.Get("D")));
        Assert.Equal(2, Assert.IsType<PdfInteger>(array[0]).Value);
    }
}
