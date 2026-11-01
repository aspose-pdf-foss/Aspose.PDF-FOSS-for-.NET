using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>A form made from a page draws that page: its content, with its resources.</summary>
public class XFormFromPageTests
{
    [Fact]
    public void CreateNewForm_FromAnotherDocumentsPage_DrawsItsContentAndText()
    {
        var srcDoc = new Document();
        var srcPage = srcDoc.Pages.Add();
        var helvetica = FontRepository.FindFont("Helvetica");
        srcPage.Resources.Fonts.Add(helvetica, out var fontName);
        srcPage.Contents.Add(new BT());
        srcPage.Contents.Add(new SelectFont(fontName, 20));
        srcPage.Contents.Add(new MoveTextPosition(100, 450));
        srcPage.Contents.Add(new ShowText("merged text", helvetica));
        srcPage.Contents.Add(new ET());
        var srcStream = new MemoryStream();
        srcDoc.Save(srcStream);
        srcStream.Position = 0;
        using var source = new Document(srcStream);

        var doc = new Document();
        var page = doc.Pages.Add();
        var form = XForm.CreateNewForm(source.Pages[1], doc);
        page.Resources.Forms.Add(form);
        page.Contents.Add(new GSave());
        page.Contents.Add(new Do(form.Name!));
        page.Contents.Add(new GRestore());
        var stream = new MemoryStream();
        doc.Save(stream);
        stream.Position = 0;

        using var saved = new Document(stream);
        var absorber = new TextAbsorber();
        saved.Pages[1].Accept(absorber);
        Assert.Equal("merged text", absorber.Text.Trim());
    }
}
