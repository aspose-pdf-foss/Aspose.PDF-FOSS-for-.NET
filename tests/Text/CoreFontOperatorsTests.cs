using System.IO;
using System.Linq;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>
/// A Standard-14 face drawn with operators: declared on the page by name, its text
/// written in WinAnsi, and its straight quotes measured as the straight glyphs.
/// </summary>
public class CoreFontOperatorsTests
{
    [Theory]
    [InlineData("Helvetica", "'", 191)]
    [InlineData("Helvetica", "`", 333)]
    [InlineData("Helvetica", "’", 222)]
    [InlineData("Helvetica", "‘", 222)]
    [InlineData("Helvetica-Bold", "'", 238)]
    [InlineData("Times-Roman", "'", 180)]
    [InlineData("Times-Roman", "’", 333)]
    [InlineData("Times-Italic", "'", 214)]
    [InlineData("Times-Bold", "'", 278)]
    public void StraightQuotes_AsText_MeasureAsTheStraightGlyphs(string face, string text, int thousandths)
    {
        var font = FontRepository.FindFont(face);

        Assert.Equal(thousandths, font.MeasureString(text, 1000), 3);
    }

    [Fact]
    public void CoreFace_AddedToResources_IsDeclaredAsType1WinAnsi()
    {
        var bytes = Draw("Times-Roman", "Nature’s God");

        using var reloaded = new Document(new MemoryStream(bytes));
        var font = reloaded.Pages[1].Resources.Fonts.Single();
        Assert.Equal("Times-Roman", font.FontName);
        Assert.False(font.IsEmbedded);

        var raw = System.Text.Encoding.GetEncoding(28591).GetString(bytes);
        Assert.Contains("/Subtype /Type1", raw.Replace("/Subtype/Type1", "/Subtype /Type1"));
        Assert.Contains("WinAnsiEncoding", raw);
    }

    [Fact]
    public void CoreFace_TextOutsideLatin1_IsWrittenInWinAnsiAndReadsBack()
    {
        var bytes = Draw("Helvetica", "Nature’s — God");

        using var reloaded = new Document(new MemoryStream(bytes));
        var absorber = new TextAbsorber();
        reloaded.Pages[1].Accept(absorber);
        Assert.Contains("Nature’s — God", absorber.Text);
    }

    [Fact]
    public void CoreFace_AddedTwice_IsDeclaredOnce()
    {
        using var document = new Document();
        var page = document.Pages.Add();
        var font = FontRepository.FindFont("Courier");

        page.Resources.Fonts.Add(font, out var first);
        page.Resources.Fonts.Add(font, out var second);

        Assert.Equal(first, second);
    }

    private static byte[] Draw(string face, string text)
    {
        using var document = new Document();
        var page = document.Pages.Add();
        var font = FontRepository.FindFont(face);
        page.Resources.Fonts.Add(font, out var name);

        page.Contents.Add(new BT());
        page.Contents.Add(new SelectFont(name, 12));
        page.Contents.Add(new SetTextMatrix(1, 0, 0, 1, 72, 700));
        page.Contents.Add(new ShowText(text, font));
        page.Contents.Add(new ET());

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
}
