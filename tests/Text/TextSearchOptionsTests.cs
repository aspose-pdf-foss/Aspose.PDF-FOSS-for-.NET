using System.Text;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class TextSearchOptionsTests
{
    private static byte[] BuildPdfWithText(string text)
    {
        var content = Encoding.ASCII.GetBytes($"BT /F1 12 Tf 100 700 Td ({text}) Tj ET");
        return PdfBuilder.BuildWithTextContent(content);
    }

    [Fact]
    public void CaseInsensitive_FindsMatches()
    {
        var pdf = BuildPdfWithText("Hello World");
        using var doc = Document.Open(pdf);

        var absorber = new TextFragmentAbsorber("hello world", new TextSearchOptions
        {
            CaseSensitive = false,
        });
        absorber.Visit(doc);

        Assert.Single(absorber.TextFragments);
        Assert.Equal("Hello World", absorber.TextFragments[1].Text);
    }

    [Fact]
    public void CaseSensitive_DoesNotMatchDifferentCase()
    {
        var pdf = BuildPdfWithText("Hello World");
        using var doc = Document.Open(pdf);

        var absorber = new TextFragmentAbsorber("hello world", new TextSearchOptions
        {
            CaseSensitive = true,
        });
        absorber.Visit(doc);

        Assert.Empty(absorber.TextFragments);
    }

    [Fact]
    public void WholeWord_DoesNotMatchPartial()
    {
        var pdf = BuildPdfWithText("HelloWorld");
        using var doc = Document.Open(pdf);

        var absorber = new TextFragmentAbsorber("Hello", new TextSearchOptions
        {
            WholeWord = true,
        });
        absorber.Visit(doc);

        Assert.Empty(absorber.TextFragments);
    }

    [Fact]
    public void WholeWord_MatchesFullWord()
    {
        var pdf = BuildPdfWithText("Hello World");
        using var doc = Document.Open(pdf);

        var absorber = new TextFragmentAbsorber("Hello", new TextSearchOptions
        {
            WholeWord = true,
        });
        absorber.Visit(doc);

        Assert.Single(absorber.TextFragments);
        Assert.Equal("Hello", absorber.TextFragments[1].Text);
    }

    [Fact]
    public void CaseInsensitive_WholeWord_Combined()
    {
        var pdf = BuildPdfWithText("Hello World");
        using var doc = Document.Open(pdf);

        var absorber = new TextFragmentAbsorber("hello", new TextSearchOptions
        {
            CaseSensitive = false,
            WholeWord = true,
        });
        absorber.Visit(doc);

        Assert.Single(absorber.TextFragments);
        Assert.Equal("Hello", absorber.TextFragments[1].Text);
    }

    [Fact]
    public void CaseInsensitive_WholeWord_DoesNotMatchPartial()
    {
        var pdf = BuildPdfWithText("HelloWorld");
        using var doc = Document.Open(pdf);

        var absorber = new TextFragmentAbsorber("hello", new TextSearchOptions
        {
            CaseSensitive = false,
            WholeWord = true,
        });
        absorber.Visit(doc);

        Assert.Empty(absorber.TextFragments);
    }

    [Fact]
    public void Regex_CaseInsensitive()
    {
        var pdf = BuildPdfWithText("Hello World");
        using var doc = Document.Open(pdf);

        var absorber = new TextFragmentAbsorber("hel+o", new TextSearchOptions
        {
            IsRegularExpression = true,
            CaseSensitive = false,
        });
        absorber.Visit(doc);

        Assert.Single(absorber.TextFragments);
        Assert.Equal("Hello", absorber.TextFragments[1].Text);
    }
}
