using Aspose.Pdf;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Devices;

/// <summary>A render frees only the objects it read for itself: what the document model already
/// held when it began - the document information, the outline - stays live, so an edit made
/// through it after the render reaches the saved file.</summary>
public class RenderKeepsEditsTests
{
    private static byte[] Source()
    {
        var document = new Document();
        document.Pages.Add().Paragraphs.Add(new TextFragment("hello"));
        document.Info.Add("Reviewer", "before");
        document.Outlines.Add(new OutlineItemCollection(document.Outlines) { Title = "before" });
        using var saved = new MemoryStream();
        document.Save(saved);
        return saved.ToArray();
    }

    [Fact]
    public void EditsThroughObjectsReadBeforeARenderAreSaved()
    {
        using var document = new Document(new MemoryStream(Source()));
        Assert.Equal("before", document.Info["Reviewer"]);
        var outline = document.Outlines[1];
        Assert.Equal("before", outline.Title);

        new BmpDevice(new Resolution(72)).Process(document.Pages[1], new MemoryStream());
        document.Info["Reviewer"] = "after";
        outline.Title = "after";

        using var saved = new MemoryStream();
        document.Save(saved);
        using var reopened = new Document(new MemoryStream(saved.ToArray()));
        Assert.Equal("after", reopened.Info["Reviewer"]);
        Assert.Equal("after", reopened.Outlines[1].Title);
    }
}
