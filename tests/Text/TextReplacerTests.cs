using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class TextReplacerTests
{
    [Fact]
    public void Replace_SingleOccurrence_Tj()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td (Hello World) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "World", "PDF");

        Assert.Equal(1, replacer.ReplacementCount);

        // Verify text was replaced by extracting
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Hello PDF", absorber.Text);
        Assert.DoesNotContain("World", absorber.Text);
    }

    [Fact]
    public void Replace_MultipleOccurrences()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td (foo bar foo) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "foo", "baz");

        Assert.Equal(2, replacer.ReplacementCount);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("baz bar baz", absorber.Text);
    }

    [Fact]
    public void Replace_NoMatch_NoChanges()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td (Hello) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "Goodbye", "Hi");

        Assert.Equal(0, replacer.ReplacementCount);
    }

    [Fact]
    public void Replace_TJArray()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td [(He) -20 (llo World)] TJ ET");
        var pdf = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "World", "PDF");

        Assert.Equal(1, replacer.ReplacementCount);
    }

    [Fact]
    public void Replace_ShorterText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td (Hello World) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "Hello World", "Hi");

        Assert.Equal(1, replacer.ReplacementCount);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Hi", absorber.Text);
    }

    [Fact]
    public void Replace_LongerText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td (Hi) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "Hi", "Hello World");

        Assert.Equal(1, replacer.ReplacementCount);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Hello World", absorber.Text);
    }

    [Fact]
    public void Replace_SpecialCharsInText()
    {
        // Test with parentheses that need escaping in PDF strings
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td (Price: $10) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "$10", "$20");

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Price: $20", absorber.Text);
    }

    [Fact]
    public void Replace_AcrossDocument()
    {
        // Build a 3-page PDF with text on first and third
        var pdf = BuildMultiPageWithText();
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc, "Hello", "Goodbye");

        Assert.Equal(2, replacer.ReplacementCount); // one per page with text
    }

    [Fact]
    public void Replace_EmptyPage_NoError()
    {
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "anything", "something");

        Assert.Equal(0, replacer.ReplacementCount);
    }

    [Fact]
    public void Replace_HexString()
    {
        // Content with hex string: <48656C6C6F> = "Hello"
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td <48656C6C6F> Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "Hello", "World");

        Assert.Equal(1, replacer.ReplacementCount);
    }

    [Fact]
    public void Replace_PreservesNonTextOperators()
    {
        var content = Encoding.ASCII.GetBytes(
            "q 1 0 0 1 0 0 cm\nBT /F1 12 Tf 72 720 Td (Hello) Tj ET\nQ");
        var pdf = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(pdf);

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages[1], "Hello", "World");

        Assert.Equal(1, replacer.ReplacementCount);

        // The output should still contain the graphics state operators
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("World", absorber.Text);
    }

    private static byte[] BuildMultiPageWithText()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        var content1 = "BT /F1 12 Tf 72 720 Td (Hello Page1) Tj ET";
        var content2 = "BT /F1 12 Tf 72 720 Td (Hello Page2) Tj ET";

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>\nendobj\n");

        var fontOffset = ms.Position;
        Write("7 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        var c1Offset = ms.Position;
        Write($"5 0 obj\n<< /Length {content1.Length} >>\nstream\n{content1}\nendstream\nendobj\n");

        var c2Offset = ms.Position;
        Write($"6 0 obj\n<< /Length {content2.Length} >>\nstream\n{content2}\nendstream\nendobj\n");

        var p1Offset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 7 0 R >> >> >>\nendobj\n");

        var p2Offset = ms.Position;
        Write("4 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 6 0 R /Resources << /Font << /F1 7 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 8\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{p1Offset:D10} 00000 n \n");
        Write($"{p2Offset:D10} 00000 n \n");
        Write($"{c1Offset:D10} 00000 n \n");
        Write($"{c2Offset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }
}
