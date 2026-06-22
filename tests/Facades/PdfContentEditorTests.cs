using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public class PdfContentEditorTests
{
    [Fact]
    public void ReplaceText_SimpleLatin1()
    {
        var content = "BT /F1 12 Tf 72 720 Td (Hello World) Tj ET"u8.ToArray();
        var input = PdfBuilder.BuildWithTextContent(content);

        var editor = new PdfContentEditor();
        var result = editor.ReplaceText(input, "Hello", "Goodbye");

        using var doc = Document.Open(result);
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Goodbye", absorber.Text);
    }

    [Fact]
    public void ReplaceTextOnPage_SpecificPage()
    {
        var content = "BT /F1 12 Tf 72 720 Td (Test Text) Tj ET"u8.ToArray();
        var input = PdfBuilder.BuildWithTextContent(content);

        var editor = new PdfContentEditor();
        var result = editor.ReplaceTextOnPage(input, 1, "Test", "Demo");

        using var doc = Document.Open(result);
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Demo", absorber.Text);
    }
}
