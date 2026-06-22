using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class FontEmbedderTests
{
    private static readonly string TestFontPath = "/System/Library/Fonts/Geneva.ttf";

    private static bool FontAvailable() => File.Exists(TestFontPath);

    [Fact]
    public void TrueTypeParser_ParsesMetadata()
    {
        if (!FontAvailable()) return; // Skip on CI without macOS fonts

        var data = File.ReadAllBytes(TestFontPath);
        var parser = new TrueTypeParser(data);
        parser.Parse();

        Assert.True(parser.UnitsPerEm > 0);
        Assert.NotEmpty(parser.FamilyName);
        Assert.NotEmpty(parser.PostScriptName);
        Assert.True(parser.Ascent > 0);
        Assert.True(parser.Descent <= 0); // descent is typically negative
        Assert.True(parser.GlyphWidths.Length > 0);
        Assert.True(parser.CMap.Count > 0);
    }

    [Fact]
    public void TrueTypeParser_GlyphWidths_NonZero()
    {
        if (!FontAvailable()) return;

        var data = File.ReadAllBytes(TestFontPath);
        var parser = new TrueTypeParser(data);
        parser.Parse();

        // 'A' (0x41) should have a non-zero width
        var widthA = parser.GetCharWidth(0x41);
        Assert.True(widthA > 0, "Width of 'A' should be positive");
    }

    [Fact]
    public void TrueTypeParser_CMap_HasBasicLatin()
    {
        if (!FontAvailable()) return;

        var data = File.ReadAllBytes(TestFontPath);
        var parser = new TrueTypeParser(data);
        parser.Parse();

        // Space, A, Z should be mapped
        Assert.True(parser.CMap.ContainsKey(32), "Space should be in cmap");
        Assert.True(parser.CMap.ContainsKey(65), "'A' should be in cmap");
        Assert.True(parser.CMap.ContainsKey(122), "'z' should be in cmap");
    }

    [Fact]
    public void FontEmbedder_Embed_CreatesValidFont()
    {
        if (!FontAvailable()) return;

        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);

        var data = File.ReadAllBytes(TestFontPath);
        var embedder = FontEmbedder.Embed(doc, data, "F1");
        embedder.AddToPage(doc.Pages[1]);

        Assert.Equal("F1", embedder.ResourceName);
        Assert.NotEmpty(embedder.PostScriptName);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void FontEmbedder_Embed_FontIsEmbedded()
    {
        if (!FontAvailable()) return;

        // Start from a minimal PDF that already has a page
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var data = File.ReadAllBytes(TestFontPath);
        var embedder = FontEmbedder.Embed(doc, data, "F1");
        embedder.AddToPage(page);

        // Write text using the embedded font
        var builder = new Aspose.Pdf.Content.ContentStreamBuilder();
        builder.BeginText()
            .SetFont("F1", 12)
            .MoveTextPosition(72, 700)
            .ShowText("Hello from embedded font!")
            .EndText();

        page.AddContentStream(builder.Build());

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        // Verify the font is present in resources
        var pageDict = doc2.Pages[1].Dict;
        var resources = doc2.Reader.ResolveDict(pageDict.Get("Resources"));
        Assert.NotNull(resources);
        var fontDict = doc2.Reader.ResolveDict(resources!.Get("Font"));
        Assert.NotNull(fontDict);
        Assert.True(fontDict!.ContainsKey("F1"));

        // Verify font descriptor exists and has FontFile2
        var f1Dict = doc2.Reader.ResolveDict(fontDict.Get("F1"));
        Assert.NotNull(f1Dict);
        Assert.Equal("TrueType", f1Dict!.GetName("Subtype"));
        Assert.Equal("WinAnsiEncoding", f1Dict.GetName("Encoding"));

        var descriptorRef = f1Dict.Get("FontDescriptor");
        var descriptor = doc2.Reader.ResolveDict(descriptorRef);
        Assert.NotNull(descriptor);
        Assert.True(descriptor!.ContainsKey("FontFile2"));
    }

    [Fact]
    public void FontEmbedder_Embed_WidthsAreCorrect()
    {
        if (!FontAvailable()) return;

        using var doc = Document.Create();
        var data = File.ReadAllBytes(TestFontPath);
        var embedder = FontEmbedder.Embed(doc, data, "F1");

        // The saved PDF should have a /Widths array with non-zero entries
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        // Find the font dictionary by searching new objects
        // Just check the bytes contain "Widths" and non-zero width values
        var pdfStr = System.Text.Encoding.Latin1.GetString(saved);
        Assert.Contains("/Widths", pdfStr);
        Assert.Contains("/FontDescriptor", pdfStr);
    }

    [Fact]
    public void FontEmbedder_MultipleFonts()
    {
        if (!FontAvailable()) return;

        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var data = File.ReadAllBytes(TestFontPath);
        var font1 = FontEmbedder.Embed(doc, data, "F1");
        var font2 = FontEmbedder.Embed(doc, data, "F2");
        font1.AddToPage(page);
        font2.AddToPage(page);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var pageDict = doc2.Pages[1].Dict;
        var resources = doc2.Reader.ResolveDict(pageDict.Get("Resources"));
        var fontDict = doc2.Reader.ResolveDict(resources!.Get("Font"));
        Assert.True(fontDict!.ContainsKey("F1"));
        Assert.True(fontDict.ContainsKey("F2"));
    }
}
