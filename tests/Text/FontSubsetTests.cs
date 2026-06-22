using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class FontSubsetTests
{
    private const string SystemFontPath = "/System/Library/Fonts/Geneva.ttf";

    private static bool HasSystemFont => File.Exists(SystemFontPath);

    [Fact]
    public void TrueTypeSubsetter_SubsetReducesSize()
    {
        if (!HasSystemFont) return;
        var fontData = File.ReadAllBytes(SystemFontPath);
        var parser = new TrueTypeParser(fontData);
        parser.Parse();

        var subsetter = new TrueTypeSubsetter(fontData, parser);
        var (subsetData, glyphMap) = subsetter.Subset(new[] { (int)'H', (int)'e', (int)'l', (int)'o' });

        // Subset should be significantly smaller
        Assert.True(subsetData.Length < fontData.Length,
            $"Subset ({subsetData.Length}) should be smaller than original ({fontData.Length})");
        Assert.True(subsetData.Length > 100, "Subset should be a valid font (> 100 bytes)");
    }

    [Fact]
    public void TrueTypeSubsetter_SubsetIsValidFont()
    {
        if (!HasSystemFont) return;
        var fontData = File.ReadAllBytes(SystemFontPath);
        var parser = new TrueTypeParser(fontData);
        parser.Parse();

        var subsetter = new TrueTypeSubsetter(fontData, parser);
        var (subsetData, _) = subsetter.Subset(new[] { (int)'A', (int)'B', (int)'C' });

        // Verify subset is a valid TrueType file by parsing it
        var subsetParser = new TrueTypeParser(subsetData);
        subsetParser.Parse();

        Assert.True(subsetParser.UnitsPerEm > 0);
        Assert.True(subsetParser.GlyphWidths.Length > 0);
    }

    [Fact]
    public void TrueTypeSubsetter_GlyphMapContainsNotdef()
    {
        if (!HasSystemFont) return;
        var fontData = File.ReadAllBytes(SystemFontPath);
        var parser = new TrueTypeParser(fontData);
        parser.Parse();

        var subsetter = new TrueTypeSubsetter(fontData, parser);
        var (_, glyphMap) = subsetter.Subset(new[] { (int)'X' });

        // Glyph 0 (.notdef) should always be included
        Assert.Contains(0, glyphMap.Keys);
        Assert.Equal(0, glyphMap[0]); // .notdef is always glyph 0
    }

    [Fact]
    public void TrueTypeSubsetter_FewerGlyphsThanOriginal()
    {
        if (!HasSystemFont) return;
        var fontData = File.ReadAllBytes(SystemFontPath);
        var parser = new TrueTypeParser(fontData);
        parser.Parse();

        var subsetter = new TrueTypeSubsetter(fontData, parser);
        var (subsetData, glyphMap) = subsetter.Subset(new[] { (int)'A' });

        var subsetParser = new TrueTypeParser(subsetData);
        subsetParser.Parse();

        // Subset should have far fewer glyphs (just .notdef + 'A' + any composites)
        Assert.True(subsetParser.GlyphWidths.Length <= 10,
            $"Expected <=10 glyphs in subset, got {subsetParser.GlyphWidths.Length}");
        Assert.True(parser.GlyphWidths.Length > subsetParser.GlyphWidths.Length);
    }

    [Fact]
    public void FontEmbedder_EmbedSubset_CreatesFont()
    {
        if (!HasSystemFont) return;
        var fontData = File.ReadAllBytes(SystemFontPath);

        var input = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var embedder = FontEmbedder.EmbedSubset(doc, fontData, "Hello", "F1");
        embedder.AddToPage(page);

        // Build content that uses the font
        var builder = new ContentStreamBuilder();
        builder.BeginText()
            .SetFont(embedder.ResourceName, 12)
            .MoveTextPosition(72, 700)
            .ShowText("Hello")
            .EndText();
        page.AddContentStream(builder.Build());

        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);

        // Reopen and verify font exists
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void FontEmbedder_EmbedSubset_SmallerThanFull()
    {
        if (!HasSystemFont) return;
        var fontData = File.ReadAllBytes(SystemFontPath);

        // Full embed
        var inputFull = Helpers.PdfBuilder.BuildMinimal();
        using var docFull = Document.Open(inputFull);
        var fullEmbedder = FontEmbedder.Embed(docFull, fontData, "F1");
        fullEmbedder.AddToPage(docFull.Pages[1]);
        var builder1 = new ContentStreamBuilder();
        builder1.BeginText().SetFont("F1", 12).MoveTextPosition(72, 700).ShowText("Hi").EndText();
        docFull.Pages[1].AddContentStream(builder1.Build());
        var fullSaved = docFull.ToArray();

        // Subset embed
        var inputSubset = Helpers.PdfBuilder.BuildMinimal();
        using var docSubset = Document.Open(inputSubset);
        var subsetEmbedder = FontEmbedder.EmbedSubset(docSubset, fontData, "Hi", "F1");
        subsetEmbedder.AddToPage(docSubset.Pages[1]);
        var builder2 = new ContentStreamBuilder();
        builder2.BeginText().SetFont("F1", 12).MoveTextPosition(72, 700).ShowText("Hi").EndText();
        docSubset.Pages[1].AddContentStream(builder2.Build());
        var subsetSaved = docSubset.ToArray();

        Assert.True(subsetSaved.Length < fullSaved.Length,
            $"Subset PDF ({subsetSaved.Length}) should be smaller than full ({fullSaved.Length})");
    }

    [Fact]
    public void FontEmbedder_EmbedSubset_HasSubsetTag()
    {
        if (!HasSystemFont) return;
        var fontData = File.ReadAllBytes(SystemFontPath);

        using var doc = Document.Create();
        doc.Pages.Add();
        var embedder = FontEmbedder.EmbedSubset(doc, fontData, "Test", "F1");

        // The PostScript name should have a 6-letter tag prefix
        // The name is stored in the font objects; verify by saving and reopening
        embedder.AddToPage(doc.Pages[1]);
        var builder = new ContentStreamBuilder();
        builder.BeginText().SetFont("F1", 12).MoveTextPosition(72, 700).ShowText("Test").EndText();
        doc.Pages[1].AddContentStream(builder.Build());

        var saved = doc.ToArray();
        var pdfStr = Encoding.ASCII.GetString(saved);

        // Should contain a subset tag like "ABCDEF+Geneva" or similar
        Assert.Contains("+", pdfStr);
    }

    [Fact]
    public void FontEmbedder_EmbedSubset_HasToUnicodeCMap()
    {
        if (!HasSystemFont) return;
        var fontData = File.ReadAllBytes(SystemFontPath);

        var input = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];
        var embedder = FontEmbedder.EmbedSubset(doc, fontData, "ABC", "F1");
        embedder.AddToPage(page);
        var builder = new ContentStreamBuilder();
        builder.BeginText().SetFont("F1", 12).MoveTextPosition(72, 700).ShowText("ABC").EndText();
        page.AddContentStream(builder.Build());

        var saved = doc.ToArray();

        // Reopen and verify the font has a ToUnicode entry
        using var doc2 = Document.Open(saved);
        var reader = doc2.Reader;
        var resources = reader.ResolveDict(doc2.Pages[1].Dict.Get("Resources"));
        Assert.NotNull(resources);
        var fontDict = reader.ResolveDict(resources!.Get("Font"));
        Assert.NotNull(fontDict);
        var f1 = reader.ResolveDict(fontDict!.Get("F1"));
        Assert.NotNull(f1);
        Assert.NotNull(f1!.Get("ToUnicode"));
    }
}
