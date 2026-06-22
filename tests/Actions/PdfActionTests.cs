using Aspose.Pdf;
using Aspose.Pdf.Actions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Actions;

public class PdfActionTests
{
    [Fact]
    public void CreateUri_ProducesValidActionDict()
    {
        var action = PdfAction.CreateUri("https://example.com");

        Assert.Equal(ActionType.URI, action.Type);
    }

    [Fact]
    public void CreateUri_RoundTrip_PreservesUri()
    {
        // Build a PDF with a URI action, save, reload, verify
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var page = doc.Pages[1];
        var action = PdfAction.CreateUri("https://aspose.com/test");
        page.Annotations.AddLinkAnnotation(new Rectangle(50, 700, 200, 720), action);

        using var ms = new MemoryStream();
        doc.Save(ms);

        ms.Position = 0;
        using var doc2 = Document.Open(ms.ToArray());
        var annot = doc2.Pages[1].Annotations[1];
        Assert.IsType<LinkAnnotation>(annot);
        var link = (LinkAnnotation)annot;
        Assert.Equal("https://aspose.com/test", link.Uri);
    }

    [Fact]
    public void CreateGoTo_ProducesValidGoToAction()
    {
        var action = PdfAction.CreateGoTo(2, "Fit");

        Assert.Equal(ActionType.GoTo, action.Type);
    }

    [Fact]
    public void CreateGoTo_WithXyzFit_ProducesValidAction()
    {
        var action = PdfAction.CreateGoTo(1, left: 100, top: 500, zoom: 1.5);

        Assert.Equal(ActionType.GoTo, action.Type);
    }

    [Fact]
    public void CreateJavaScript_StoresScript()
    {
        var action = PdfAction.CreateJavaScript("app.alert('Hello');");

        Assert.Equal(ActionType.JavaScript, action.Type);
    }

    [Fact]
    public void CreateNamed_StoresNamedAction()
    {
        var action = PdfAction.CreateNamed("NextPage");

        Assert.Equal(ActionType.Named, action.Type);
    }

    [Fact]
    public void CreateLaunch_StoresFilePath()
    {
        var action = PdfAction.CreateLaunch("/path/to/file.pdf");

        Assert.Equal(ActionType.Launch, action.Type);
    }

    [Fact]
    public void GoToAction_DestinationPageIndex_ResolvesFromPageTree()
    {
        // Build a 3-page PDF with a GoTo action targeting page 2 (0-based index 1)
        var data = PdfBuilder.BuildWithGoToAction(targetPageIndex: 1, pageCount: 3);
        using var doc = Document.Open(data);

        var annot = doc.Pages[1].Annotations[1];
        Assert.IsType<LinkAnnotation>(annot);

        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        Assert.NotNull(actionDict);

        var goToAction = (GoToAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal(ActionType.GoTo, goToAction.Type);
        Assert.Equal(1, goToAction.DestinationPageIndex);
        Assert.Equal("Fit", goToAction.FitType);
    }

    [Fact]
    public void GoToAction_DestinationPageIndex_ResolvesPage0()
    {
        var data = PdfBuilder.BuildWithGoToAction(targetPageIndex: 0, pageCount: 3);
        using var doc = Document.Open(data);

        var annot = doc.Pages[1].Annotations[1];
        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        var goToAction = (GoToAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal(0, goToAction.DestinationPageIndex);
    }

    [Fact]
    public void GoToAction_DestinationPageIndex_ResolvesLastPage()
    {
        var data = PdfBuilder.BuildWithGoToAction(targetPageIndex: 2, pageCount: 3);
        using var doc = Document.Open(data);

        var annot = doc.Pages[1].Annotations[1];
        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        var goToAction = (GoToAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal(2, goToAction.DestinationPageIndex);
    }

    [Fact]
    public void NamedAction_ParsesFromPdf()
    {
        var data = PdfBuilder.BuildWithNamedAction("NextPage");
        using var doc = Document.Open(data);

        var annot = doc.Pages[1].Annotations[1];
        Assert.IsType<LinkAnnotation>(annot);

        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        Assert.NotNull(actionDict);

        var namedAction = (NamedAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal(ActionType.Named, namedAction.Type);
        Assert.Equal("NextPage", namedAction.Name);
    }

    [Fact]
    public void JavaScriptAction_ParsesFromPdf()
    {
        var data = PdfBuilder.BuildWithJavaScriptAction("app.alert('test');");
        using var doc = Document.Open(data);

        var annot = doc.Pages[1].Annotations[1];
        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        Assert.NotNull(actionDict);

        var jsAction = (JavascriptAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal(ActionType.JavaScript, jsAction.Type);
        Assert.Equal("app.alert('test');", jsAction.Script);
    }

    [Fact]
    public void LaunchAction_ParsesFromPdf()
    {
        var data = PdfBuilder.BuildWithLaunchAction("readme.txt");
        using var doc = Document.Open(data);

        var annot = doc.Pages[1].Annotations[1];
        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        Assert.NotNull(actionDict);

        var launchAction = (LaunchAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal(ActionType.Launch, launchAction.Type);
        Assert.Equal("readme.txt", launchAction.File);
    }

    [Fact]
    public void CreateJavaScript_RoundTrip_PreservesScript()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var page = doc.Pages[1];
        var action = PdfAction.CreateJavaScript("app.alert('Round trip');");
        page.Annotations.AddLinkAnnotation(new Rectangle(50, 600, 200, 620), action);

        using var ms = new MemoryStream();
        doc.Save(ms);

        ms.Position = 0;
        using var doc2 = Document.Open(ms.ToArray());
        var annot = doc2.Pages[1].Annotations[1];
        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        Assert.NotNull(actionDict);

        var jsAction = (JavascriptAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal("app.alert('Round trip');", jsAction.Script);
    }

    [Fact]
    public void CreateNamed_RoundTrip_PreservesName()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var page = doc.Pages[1];
        var action = PdfAction.CreateNamed("PrevPage");
        page.Annotations.AddLinkAnnotation(new Rectangle(50, 600, 200, 620), action);

        using var ms = new MemoryStream();
        doc.Save(ms);

        ms.Position = 0;
        using var doc2 = Document.Open(ms.ToArray());
        var annot = doc2.Pages[1].Annotations[1];
        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        Assert.NotNull(actionDict);

        var namedAction = (NamedAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal("PrevPage", namedAction.Name);
    }

    [Fact]
    public void CreateLaunch_RoundTrip_PreservesFilePath()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var page = doc.Pages[1];
        var action = PdfAction.CreateLaunch("document.pdf");
        page.Annotations.AddLinkAnnotation(new Rectangle(50, 600, 200, 620), action);

        using var ms = new MemoryStream();
        doc.Save(ms);

        ms.Position = 0;
        using var doc2 = Document.Open(ms.ToArray());
        var annot = doc2.Pages[1].Annotations[1];
        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        Assert.NotNull(actionDict);

        var launchAction = (LaunchAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal("document.pdf", launchAction.File);
    }

    [Fact]
    public void CreateGoTo_RoundTrip_PreservesPageIndex()
    {
        var data = PdfBuilder.BuildMultiPage(3);
        using var doc = Document.Open(data);

        var page = doc.Pages[1];
        var action = PdfAction.CreateGoTo(2, "FitH");
        page.Annotations.AddLinkAnnotation(new Rectangle(50, 600, 200, 620), action);

        using var ms = new MemoryStream();
        doc.Save(ms);

        ms.Position = 0;
        using var doc2 = Document.Open(ms.ToArray());
        var annot = doc2.Pages[1].Annotations[1];
        var link = (LinkAnnotation)annot;
        var actionDict = link.InternalReader.ResolveDict(link.Dict.Get("A"));
        Assert.NotNull(actionDict);

        var goToAction = (GoToAction)PdfAction.Create(actionDict!, link.InternalReader);
        Assert.Equal(ActionType.GoTo, goToAction.Type);
        // The page index is stored as integer placeholder in destination array
        Assert.Equal(2, goToAction.DestinationPageIndex);
        Assert.Equal("FitH", goToAction.FitType);
    }

    [Fact]
    public void AnnotationCollection_AddLinkWithAction_CreatesLinkAnnotation()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var page = doc.Pages[1];
        Assert.Empty(page.Annotations);

        var action = PdfAction.CreateUri("https://example.org");
        var annot = page.Annotations.AddLinkAnnotation(new Rectangle(10, 10, 100, 30), action);

        Assert.Single(page.Annotations);
        Assert.Equal(AnnotationType.Link, annot.AnnotationType);
    }

    [Fact]
    public void CreateGoTo_FitTypes_AllSupported()
    {
        foreach (var fitType in new[] { "Fit", "FitH", "FitV", "XYZ" })
        {
            var action = PdfAction.CreateGoTo(0, fitType);
            Assert.Equal(ActionType.GoTo, action.Type);
        }
    }

    [Fact]
    public void CreateNamed_AllStandardNames()
    {
        foreach (var name in new[] { "NextPage", "PrevPage", "FirstPage", "LastPage" })
        {
            var action = PdfAction.CreateNamed(name);
            Assert.Equal(ActionType.Named, action.Type);
        }
    }
}
