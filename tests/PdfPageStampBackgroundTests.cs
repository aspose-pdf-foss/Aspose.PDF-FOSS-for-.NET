using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>A page stamped as a background is drawn first, beneath the page's own
/// content, however that content was made.</summary>
public class PdfPageStampBackgroundTests
{
    private static Document Source()
    {
        var src = new Document();
        var page = src.Pages.Add();
        page.Contents.Add(new SetRGBColor(1, 0, 0));
        page.Contents.Add(new Re(100, 500, 200, 200));
        page.Contents.Add(new Fill());
        var stream = new MemoryStream();
        src.Save(stream);
        stream.Position = 0;
        return new Document(stream);
    }

    private static string SavedContent(Document doc)
    {
        var stream = new MemoryStream();
        doc.Save(stream);
        stream.Position = 0;
        var saved = new Document(stream);
        var text = new StringBuilder();
        foreach (var part in saved.Pages[1].ContentStreamObjects())
            text.Append(Encoding.ASCII.GetString(saved.Reader.DecodeStream(part))).Append('\n');
        return text.ToString();
    }

    [Fact]
    public void Background_OnAPageDrawnThroughItsOperators_IsDrawnBeneathThem()
    {
        using var source = Source();
        var doc = new Document();
        var page = doc.Pages.Add();
        page.Contents.Add(new SetRGBColor(0, 0, 1));
        page.Contents.Add(new Re(200, 400, 200, 200));
        page.Contents.Add(new Fill());
        page.AddStamp(new PdfPageStamp(source.Pages[1]) { Background = true });

        var content = SavedContent(doc);
        var stamp = content.IndexOf(" Do", StringComparison.Ordinal);
        var own = content.IndexOf("0 0 1 rg", StringComparison.Ordinal);
        Assert.True(stamp >= 0, content);
        Assert.True(own > stamp, content);
    }
}
