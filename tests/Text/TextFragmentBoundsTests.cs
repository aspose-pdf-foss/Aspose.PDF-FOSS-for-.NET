using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class TextFragmentBoundsTests
{
    [Fact]
    public void TextFragment_HasBoundingRectangle()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td (Hello) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.NotEmpty(absorber.TextFragments);
        var frag = absorber.TextFragments[1];
        Assert.Equal("Hello", frag.Text);
        Assert.NotNull(frag.Rectangle);
        Assert.True(frag.Rectangle!.Width > 0, "Rectangle width should be > 0");
        Assert.True(frag.Rectangle.Height > 0, "Rectangle height should be > 0");
    }

    [Fact]
    public void TextFragment_HasPosition()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 100 500 Td (World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        Assert.NotNull(frag.Position);
        Assert.Equal(100, frag.Position!.XIndent);
        Assert.Equal(500, frag.Position.YIndent);
    }

    [Fact]
    public void TextFragment_FontName_Tracked()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf (Test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        // FontName should be set (may be the resource key or base font name)
        Assert.NotNull(frag.TextState.FontName);
    }

    [Fact]
    public void TextFragment_TJ_HasWidth()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td [(Hello) -250 (World)] TJ ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        Assert.Contains("Hello", frag.Text);
        Assert.NotNull(frag.Rectangle);
        Assert.True(frag.Rectangle!.Width > 0);
    }

    [Fact]
    public void TextFragment_FontSize()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 24 Tf (Big text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        Assert.Equal(24, frag.FontSize);
    }

    // ── CTM tracking tests ──────────────────────────────────────────

    [Fact]
    public void TextFragment_CtmTranslation_AppliedToPosition()
    {
        // Use cm to translate by (50, 100) before text
        var content = Encoding.ASCII.GetBytes(
            "q 1 0 0 1 50 100 cm BT /F1 12 Tf 10 20 Td (Shifted) Tj ET Q");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        Assert.NotNull(frag.Position);
        // CTM translates (10,20) by (50,100) -> (60,120)
        Assert.Equal(60, frag.Position!.XIndent, 1);
        Assert.Equal(120, frag.Position.YIndent, 1);
    }

    [Fact]
    public void TextFragment_CtmScale_AppliedToRectangle()
    {
        // Scale by 2x in both directions
        var content = Encoding.ASCII.GetBytes(
            "q 2 0 0 2 0 0 cm BT /F1 10 Tf 50 100 Td (AB) Tj ET Q");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        Assert.NotNull(frag.Rectangle);
        // Position should be scaled: (50*2, 100*2) = (100, 200)
        Assert.Equal(100, frag.Position!.XIndent, 1);
        Assert.Equal(200, frag.Position.YIndent, 1);
        // The fragment box is the canonical 1.1-em line box (bottom at baseline +
        // descent, 1.1 x FontSize tall - reported for every face);
        // under the 2x CTM the effective size is 20, so the box is 22 tall.
        Assert.Equal(22, frag.Rectangle!.Height, 1);
    }

    [Fact]
    public void TextFragment_CtmRestoredAfterQ()
    {
        // q/Q should save/restore the CTM
        var content = Encoding.ASCII.GetBytes(
            "q 1 0 0 1 100 200 cm Q BT /F1 12 Tf 10 20 Td (NoShift) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        // After Q, CTM should be identity so position = (10, 20)
        Assert.Equal(10, frag.Position!.XIndent, 1);
        Assert.Equal(20, frag.Position.YIndent, 1);
    }

    // ── T* operator tests ───────────────────────────────────────────

    [Fact]
    public void TextFragment_TStar_MovesToNextLine()
    {
        // TL sets leading=14, T* should subtract 14 from ty
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td 14 TL (Line1) Tj T* (Line2) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Equal(2, absorber.TextFragments.Count);
        var frag1 = absorber.TextFragments[1];
        var frag2 = absorber.TextFragments[2];

        Assert.Equal("Line1", frag1.Text);
        Assert.Equal("Line2", frag2.Text);
        Assert.Equal(700, frag1.Position!.YIndent, 1);
        Assert.Equal(686, frag2.Position!.YIndent, 1); // 700 - 14 = 686
    }

    [Fact]
    public void TextFragment_TD_SetsLeading()
    {
        // TD sets leading = -ty and moves by (tx, ty)
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td 0 -16 TD (Line1) Tj T* (Line2) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Equal(2, absorber.TextFragments.Count);
        // After TD(0,-16): ty=700-16=684, leading=16
        // After T*: ty=684-16=668
        Assert.Equal(684, absorber.TextFragments[1].Position!.YIndent, 1);
        Assert.Equal(668, absorber.TextFragments[2].Position!.YIndent, 1);
    }

    // ── " operator test ─────────────────────────────────────────────

    [Fact]
    public void TextFragment_DoubleQuote_MovesAndShows()
    {
        // " operator: set word/char spacing, move to next line, show text
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td 14 TL 0 0 (QuoteText) \" ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.TextFragments);
        var frag = absorber.TextFragments[1];
        Assert.Equal("QuoteText", frag.Text);
        // " moves to next line first: 700 - 14 = 686
        Assert.Equal(686, frag.Position!.YIndent, 1);
    }

    // ── Tj advance tests ────────────────────────────────────────────

    [Fact]
    public void TextFragment_ConsecutiveTj_AdvancesPosition()
    {
        // Two consecutive Tj calls; second should start after the first
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td (Hello) Tj ( World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Equal(2, absorber.TextFragments.Count);
        var frag1 = absorber.TextFragments[1];
        var frag2 = absorber.TextFragments[2];

        // Second fragment should be positioned after the first
        Assert.True(frag2.Position!.XIndent > frag1.Position!.XIndent,
            $"Second Tj X ({frag2.Position.XIndent}) should be > first Tj X ({frag1.Position.XIndent})");
        // They should be on the same line
        Assert.Equal(frag1.Position.YIndent, frag2.Position.YIndent, 1);
    }

    [Fact]
    public void TextFragment_TJ_AdvancesPosition()
    {
        // After TJ, next Tj should start at the advanced position
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td [(AB) -500 (CD)] TJ (EF) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Equal(2, absorber.TextFragments.Count);
        var tjFrag = absorber.TextFragments[1];
        var tjFrag2 = absorber.TextFragments[2];

        Assert.True(tjFrag2.Position!.XIndent > tjFrag.Position!.XIndent,
            "Tj after TJ should be at advanced position");
    }

    // ── TJ kerning tests ────────────────────────────────────────────

    [Fact]
    public void TextFragment_TJ_KerningAdjustsWidth()
    {
        // TJ with kerning: negative value moves right, so total width includes kerning
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td [(A) -100 (B)] TJ ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        Assert.NotNull(frag.Rectangle);
        // Width should include the kerning adjustment
        Assert.True(frag.Rectangle!.Width > 0);
    }

    // ── Search phrase bounds tests ──────────────────────────────────

    [Fact]
    public void SearchPhrase_HasBoundingRectangle()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td (Hello World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("Hello");
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.TextFragments);
        var frag = absorber.TextFragments[1];
        Assert.Equal("Hello", frag.Text);
        Assert.NotNull(frag.Rectangle);
        Assert.True(frag.Rectangle!.Width > 0, "Search match should have rectangle width > 0");
        Assert.True(frag.Rectangle.Height > 0, "Search match should have rectangle height > 0");
    }

    [Fact]
    public void SearchPhrase_HasPosition()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 100 500 Td (Find me here) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("Find me");
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        Assert.NotNull(frag.Position);
        Assert.Equal(100, frag.Position!.XIndent, 1);
        Assert.Equal(500, frag.Position.YIndent, 1);
    }

    [Fact]
    public void SearchPhrase_SpanningMultipleRuns_HasMergedRectangle()
    {
        // Two Tj calls; search phrase spans both runs
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td (Hel) Tj (lo World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("Hello");
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.TextFragments);
        var frag = absorber.TextFragments[1];
        Assert.Equal("Hello", frag.Text);
        Assert.NotNull(frag.Rectangle);
        Assert.True(frag.Rectangle!.Width > 0);
    }

    [Fact]
    public void SearchPhrase_Regex_HasBounds()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td (Test 123 done) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber(@"\d+", isRegex: true);
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.TextFragments);
        var frag = absorber.TextFragments[1];
        Assert.Equal("123", frag.Text);
        Assert.NotNull(frag.Rectangle);
        Assert.True(frag.Rectangle!.Width > 0);
    }

    [Fact]
    public void SearchPhrase_HasFontInfo()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td (Hello World) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("World");
        absorber.Visit(doc.Pages[1]);

        var frag = absorber.TextFragments[1];
        Assert.Equal(12, frag.FontSize);
        Assert.NotNull(frag.TextState.FontName);
    }

    // ── ' operator advance tests ────────────────────────────────────

    [Fact]
    public void TextFragment_SingleQuote_AdvancesAfterShow()
    {
        // ' operator should move to next line and advance tx after showing text
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf 72 700 Td 14 TL (Line1) ' (Line2) ' ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Equal(2, absorber.TextFragments.Count);
        Assert.Equal("Line1", absorber.TextFragments[1].Text);
        Assert.Equal("Line2", absorber.TextFragments[2].Text);
        // Each ' moves to next line
        Assert.Equal(686, absorber.TextFragments[1].Position!.YIndent, 1); // 700-14
        Assert.Equal(672, absorber.TextFragments[2].Position!.YIndent, 1); // 686-14
    }

    // ── CTM with search phrase ──────────────────────────────────────

    [Fact]
    public void SearchPhrase_WithCtm_HasTransformedBounds()
    {
        // CTM translates by (50, 100), text at (10, 20) -> effective (60, 120)
        var content = Encoding.ASCII.GetBytes(
            "q 1 0 0 1 50 100 cm BT /F1 12 Tf 10 20 Td (FindMe) Tj ET Q");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextFragmentAbsorber("FindMe");
        absorber.Visit(doc.Pages[1]);

        Assert.Single(absorber.TextFragments);
        var frag = absorber.TextFragments[1];
        Assert.NotNull(frag.Position);
        Assert.Equal(60, frag.Position!.XIndent, 1);
        Assert.Equal(120, frag.Position.YIndent, 1);
    }
}
