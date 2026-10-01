using System.IO;
using System.Linq;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Content;

/// <summary>
/// A plain <see cref="TextShowOperator"/> is the template that selects every text-showing operator of a page, which
/// is how a program removes all the text of a document.
/// </summary>
public class TextShowSelectorTests
{
    private static Document TwoLines()
    {
        var document = new Document();
        var page = document.Pages.Add();
        page.Paragraphs.Add(new TextFragment("First line"));
        page.Paragraphs.Add(new TextFragment("Second line"));
        using var saved = new MemoryStream();
        document.Save(saved);
        return new Document(new MemoryStream(saved.ToArray()));
    }

    [Fact]
    public void TextShowTemplate_SelectsEveryTextShowOperator()
    {
        var page = TwoLines().Pages[1];
        var selector = new OperatorSelector(new TextShowOperator());
        page.Contents.Accept(selector);

        Assert.NotEmpty(selector.Selected);
        Assert.All(selector.Selected, op => Assert.IsAssignableFrom<TextShowOperator>(op));
        Assert.Equal(page.Contents.Cast<Operator>().Count(op => op is TextShowOperator), selector.Selected.Count);
    }

    [Fact]
    public void DeletingTheSelection_RemovesTheText()
    {
        var document = TwoLines();
        var page = document.Pages[1];
        var selector = new OperatorSelector(new TextShowOperator());
        page.Contents.Accept(selector);
        page.Contents.Delete(selector.Selected);

        using var saved = new MemoryStream();
        document.Save(saved);
        var absorber = new TextAbsorber();
        new Document(new MemoryStream(saved.ToArray())).Pages[1].Accept(absorber);
        Assert.Equal(string.Empty, absorber.Text.Trim());
    }

    [Fact]
    public void PlainTextShowOperator_WritesTj()
    {
        Assert.Equal("(a\\(b\\)) Tj", new TextShowOperator { Text = "a(b)" }.ToPdf());
    }
}
