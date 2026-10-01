using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Devices;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

public class MarkedContentReadTests
{
    /// <summary>A tagged page: a paragraph of two shows a word gap apart, holding a bold Span on
    /// the next line; then a paragraph whose word is hyphenated over two lines.</summary>
    private static byte[] BuildTagged()
    {
        const string content =
            "/P <</MCID 0>> BDC BT /F1 12 Tf 72 700 Td (The amber) Tj 62 0 Td (meadow lies beyond) Tj ET EMC\n" +
            "/Span <</MCID 1>> BDC BT /F2 12 Tf 72 686 Td (northern fjord) Tj ET EMC\n" +
            "/P <</MCID 2>> BDC BT /F1 12 Tf 72 650 Td (An exam-) Tj 0 -14 Td (ple follows.) Tj ET EMC\n" +
            // A word hyphenated over the line shown glyph by glyph, a compound keeping its hyphen,
            // and a soft hyphen (\255 in WinAnsi).
            "/P <</MCID 3>> BDC BT /F1 12 Tf 72 600 Td (n) Tj 7 0 Td (o) Tj 7 0 Td (-) Tj -14 -14 Td (toriously) Tj " +
            "0 -14 Td (self-) Tj 0 -14 Td (attention state-of-) Tj 0 -14 Td (the-art fjord\\255) Tj 0 -14 Td (ling) Tj ET EMC\n" +
            "/P <</MCID 4>> BDC BT /F3 12 Tf 72 500 Td (Bold TeX) Tj /F4 12 Tf 72 486 Td (Italic URW) Tj ET EMC\n";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /StructParents 0 /Contents 10 0 R " +
                "/Resources << /Font << /F1 11 0 R /F2 12 0 R /F3 13 0 R /F4 14 0 R >> >> >>",
            "<< /Type /StructTreeRoot /K 5 0 R /ParentTree 9 0 R >>",
            "<< /Type /StructElem /S /Document /P 4 0 R /K [6 0 R 8 0 R 15 0 R 16 0 R] >>",
            "<< /Type /StructElem /S /P /P 5 0 R /Pg 3 0 R /K [0 7 0 R] >>",
            "<< /Type /StructElem /S /Span /P 6 0 R /Pg 3 0 R /K 1 >>",
            "<< /Type /StructElem /S /P /P 5 0 R /Pg 3 0 R /K 2 >>",
            "<< /Nums [0 [6 0 R 7 0 R 8 0 R 15 0 R 16 0 R]] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /CMBX10 /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /NimbusRomNo9L-ReguItal /Encoding /WinAnsiEncoding >>",
            "<< /Type /StructElem /S /P /P 5 0 R /Pg 3 0 R /K 3 >>",
            "<< /Type /StructElem /S /P /P 5 0 R /Pg 3 0 R /K 4 >>",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static StructureElement[] Paragraphs(Document doc)
        => doc.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true).Cast<StructureElement>().ToArray();

    [Fact]
    public void ParagraphReadsItsRunsWithSpacesAndItsSpanInOrder()
    {
        using var doc = new Document(new MemoryStream(BuildTagged()));
        var items = Paragraphs(doc)[0].GetMarkedContent();

        Assert.Equal(2, items.Count);
        Assert.Equal("The amber meadow lies beyond", items[0].Text);
        Assert.Equal("P", items[0].Element!.S.Name);
        Assert.False(items[0].IsBold);
        Assert.Equal("Helvetica", items[0].FontName);
        Assert.Equal(12f, items[0].FontSize, 3);
        Assert.Same(doc.Pages[1], items[0].Page);

        // The Span on the next line starts with the space the line change stands for.
        Assert.Equal(" northern fjord", items[1].Text);
        Assert.Equal("Span", items[1].Element!.S.Name);
        Assert.True(items[1].IsBold);
        Assert.Equal("Helvetica-Bold", items[1].FontName);
    }

    [Fact]
    public void OwnContentLeavesOutChildElements()
    {
        using var doc = new Document(new MemoryStream(BuildTagged()));
        var items = Paragraphs(doc)[0].GetMarkedContent(includeDescendants: false);
        Assert.Equal("The amber meadow lies beyond", Assert.Single(items).Text);
    }

    [Fact]
    public void HyphenatedWordJoinsAcrossTheLineWithoutItsHyphenTheBreakStated()
    {
        using var doc = new Document(new MemoryStream(BuildTagged()));
        var items = Paragraphs(doc)[1].GetMarkedContent();
        Assert.Equal("An example follows.", string.Concat(items.Select(i => i.Text)));
        // A run per line: the word broken over the line ends the first, where the page broke it with its hyphen.
        Assert.Equal(2, items.Count);
        Assert.Equal("An exam", items[0].Text);
        Assert.Equal(new[] { "An exam".Length }, items[0].HyphenBreaks);
        Assert.Empty(items[1].HyphenBreaks);
    }

    [Fact]
    public void AHyphenShownAsAGlyphOfItsOwnJoinsAndCompoundsAndSoftHyphensReadRight()
    {
        using var doc = new Document(new MemoryStream(BuildTagged()));
        // A run per line: the lines' runs read as the paragraph, each joined word within one run.
        var items = Paragraphs(doc)[2].GetMarkedContent();
        Assert.Equal("notoriously self-attention state-of-the-art fjordling", string.Concat(items.Select(i => i.Text)));
        Assert.All(items, i => Assert.DoesNotContain("  ", i.Text));
        Assert.True(items.Count >= 2, "a run per line: the paragraph's lines are runs of their own");
    }

    [Fact]
    public void TexAndUrwFaceNamesStateTheirWeightAndSlope()
    {
        using var doc = new Document(new MemoryStream(BuildTagged()));
        var items = Paragraphs(doc)[3].GetMarkedContent();
        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsBold);
        Assert.False(items[0].IsItalic);
        Assert.Equal("CMBX10", items[0].FontName);
        Assert.True(items[1].IsItalic);
        Assert.False(items[1].IsBold);
    }

    [Fact]
    public void ElementWithoutADocumentReadsNothing()
    {
        using var doc = new Document();
        var p = doc.TaggedContent.CreateParagraphElement();
        Assert.Empty(p.GetMarkedContent());
    }

    // ---- link text split ----

    private const string Uri = "https://example.com/fjord";

    /// <summary>One line of text, shown the way <paramref name="show"/> spells it, with a link
    /// annotation over exactly "northern fjord".</summary>
    private static byte[] BuildLinked(string show)
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 14 TL 72 700 Td " + show + " ET");
        using var doc = new Document(new MemoryStream(PdfBuilder.BuildWithTextContent(content)));
        var absorber = new TextFragmentAbsorber("northern fjord");
        doc.Pages[1].Accept(absorber);
        var rect = Assert.Single(absorber.TextFragments.Cast<TextFragment>()).Rectangle!;
        doc.Pages[1].Annotations.AddLinkAnnotation(rect, Uri);
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    public static TheoryData<string> Shows => new()
    {
        "(The meadow beyond the northern fjord, where willows grow.) Tj",
        "[(The meadow beyond) -250 (the northern fj) 10 (ord, where willows grow.)] TJ",
        "(The meadow beyond the northern fjord, where willows grow.) '",
        "0 0 (The meadow beyond the northern fjord, where willows grow.) \"",
    };

    private static byte[] Render(byte[] pdf) => new SoftwarePageRenderer().RenderPage(pdf, 1, 72).Data;

    [Theory]
    [MemberData(nameof(Shows))]
    public void SplitShowsDrawTheSamePixels(string show)
    {
        var source = BuildLinked(show);
        using var doc = new Document(new MemoryStream(source));
        var page = doc.Pages[1];
        var rects = page.Annotations.Cast<Annotations.Annotation>().Select(a => a.Rect!).ToList();
        Assert.True(LinkTextSplit.Apply(page, rects));
        using var ms = new MemoryStream();
        doc.Save(ms);
        Assert.Equal(Render(source), Render(ms.ToArray()));
    }

    [Theory]
    [MemberData(nameof(Shows))]
    public void LinkOwnsExactlyTheWordsItCovers(string show)
    {
        using var doc = new Document(new MemoryStream(BuildLinked(show)));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = AutoTaggingSettings.Default,
        });
        var root = doc.TaggedContent.StructTreeRootElement;
        var link = Assert.Single(root.FindElements<LinkElement>(true));
        Assert.Equal("northern fjord", string.Concat(link.GetMarkedContent().Select(i => i.Text)));
        var paragraph = link.ParentElement!;
        Assert.Equal("The meadow beyond the northern fjord, where willows grow.",
            string.Concat(paragraph.GetMarkedContent().Select(i => i.Text)));
    }

    [Fact]
    public void RectangleEndingInsideAWordLeavesTheShowWhole()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 700 Td (The meadow beyond the northern fjord.) Tj ET");
        using var doc = new Document(new MemoryStream(PdfBuilder.BuildWithTextContent(content)));
        var absorber = new TextFragmentAbsorber("northern fj");
        doc.Pages[1].Accept(absorber);
        var rect = Assert.Single(absorber.TextFragments.Cast<TextFragment>()).Rectangle!;
        Assert.False(LinkTextSplit.Apply(doc.Pages[1], [rect]));
    }
}
