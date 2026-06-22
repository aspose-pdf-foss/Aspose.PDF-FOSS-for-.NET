using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class PageAddStampTests
{
    [Fact]
    public void AddStamp_AppendsContent()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var page = doc.Pages[1];

        var stamp = new TextStamp("STAMP");
        page.AddStamp(stamp);

        // After stamping, we should be able to save and reload
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void AddStamp_TextVisibleAfterSave()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Original text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var stamp = new TextStamp("CONFIDENTIAL")
        {
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        doc.Pages[1].AddStamp(stamp);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        // Original text should still be there
        var absorber = new TextAbsorber();
        absorber.Visit(doc2.Pages[1]);
        Assert.Contains("Original text", absorber.Text);
    }

    [Fact]
    public void AddStamp_ToEmptyPage()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var stamp = new TextStamp("Hello")
        {
            XIndent = 72,
            YIndent = 72,
        };
        doc.Pages[1].AddStamp(stamp);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void AddStamp_RotatedStamp()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var stamp = new TextStamp("DRAFT")
        {
            RotateAngle = 45,
            FontSize = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        doc.Pages[1].AddStamp(stamp);

        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);
    }

    [Fact]
    public void AddStamp_MultipleStamps()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        doc.Pages[1].AddStamp(new TextStamp("Header")
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        doc.Pages[1].AddStamp(new TextStamp("Footer")
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }
}
