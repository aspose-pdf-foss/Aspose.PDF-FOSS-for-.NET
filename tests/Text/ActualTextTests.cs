using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class ActualTextTests
{
    [Fact]
    public void TextAbsorber_ActualText_OverridesGlyphDecoding()
    {
        // Build a PDF with BDC /ActualText (hi) overriding the glyph operator.
        // (A whole-span ActualText of exactly "fi"/"fl"/"ff" is deliberately
        // collapsed to its ligature codepoint — see CollapseTwoCharLigature —
        // so a non-ligature payload is used here.)
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf " +
            "/Span << /ActualText (hi) >> BDC " +
            "(X) Tj " +
            "EMC " +
            "( rest) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("hi", absorber.Text);
        Assert.Contains("rest", absorber.Text);
        // The "X" glyph should NOT appear — ActualText overrides it
        Assert.DoesNotContain("X", absorber.Text);
    }

    [Fact]
    public void TextAbsorber_NoActualText_UsesGlyphs()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf (Hello World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Hello World", absorber.Text);
    }

    [Fact]
    public void TextAbsorber_ActualText_MultipleMarkedContent()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf " +
            "/Span << /ActualText (AB) >> BDC (X) Tj EMC " +
            "/Span << /ActualText (CD) >> BDC (Y) Tj EMC " +
            "ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("AB", absorber.Text);
        Assert.Contains("CD", absorber.Text);
        Assert.DoesNotContain("X", absorber.Text);
        Assert.DoesNotContain("Y", absorber.Text);
    }

    [Fact]
    public void TextAbsorber_TJ_ImprovedSpaceThreshold()
    {
        // -250 should produce a space; -100 should not
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf [(Hello) -250 (World)] TJ ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Hello World", absorber.Text);
    }

    [Fact]
    public void TextAbsorber_TJ_SmallAdjustment_NoSpace()
    {
        // -50 is kerning, not a word space
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf [(Hel) -50 (lo)] TJ ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        Assert.Contains("Hello", absorber.Text);
        Assert.DoesNotContain("Hel lo", absorber.Text);
    }
}
