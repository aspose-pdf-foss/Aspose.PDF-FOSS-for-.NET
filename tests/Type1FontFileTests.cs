using Aspose.Pdf;
using Aspose.Pdf.Core;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>
/// A Type 1 face opened from its PFB (and AFM) is declared as a simple /Type1 font with
/// its program embedded, and text shown in it is written in the codes it is declared with.
/// </summary>
public class Type1FontFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "type1-" + Guid.NewGuid().ToString("N"));

    public Type1FontFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Pfb()
    {
        var path = Path.Combine(_dir, "testone.pfb");
        File.WriteAllBytes(path, Type1FontBuilder.Pfb());
        return path;
    }

    private string Afm()
    {
        var path = Path.Combine(_dir, "testone-metrics.afm");
        File.WriteAllText(path, Type1FontBuilder.Afm);
        return path;
    }

    private static (Document Saved, PdfDictionary Font) ShowAndSave(Font font, string text)
    {
        var doc = new Document();
        var page = doc.Pages.Add();
        page.Resources.Fonts.Add(font, out var name);
        page.Contents.Add(new BT());
        page.Contents.Add(new SelectFont(name, 24));
        page.Contents.Add(new MoveTextPosition(72, 700));
        page.Contents.Add(new ShowText(text, font));
        page.Contents.Add(new ET());
        var stream = new MemoryStream();
        doc.Save(stream);
        stream.Position = 0;

        var saved = new Document(stream);
        var reader = saved.Reader;
        var resources = reader.ResolveDict(saved.Pages[1].Dict.Get("Resources"))!;
        var fonts = reader.ResolveDict(resources.Get("Font"))!;
        return (saved, reader.ResolveDict(fonts.Get(name))!);
    }

    [Fact]
    public void OpenedWithItsAfm_IsDeclaredAsASimpleType1FontWithItsProgram()
    {
        var (saved, font) = ShowAndSave(FontRepository.OpenFont(Pfb(), Afm()), "A A");
        var reader = saved.Reader;

        Assert.Equal("Type1", font.GetName("Subtype"));
        Assert.Equal(Type1FontBuilder.FontName, font.GetName("BaseFont"));
        Assert.Equal("WinAnsiEncoding", font.GetName("Encoding"));
        var widths = Assert.IsType<PdfArray>(reader.Resolve(font.Get("Widths")));
        Assert.Equal(Type1FontBuilder.AWidth, ((PdfInteger)widths['A']).Value);
        Assert.Equal(Type1FontBuilder.SpaceWidth, ((PdfInteger)widths[' ']).Value);

        var descriptor = reader.ResolveDict(font.Get("FontDescriptor"))!;
        var program = reader.ResolveStream(descriptor.Get("FontFile"))!;
        var bytes = reader.DecodeStream(program);
        Assert.Equal(bytes.Length,
            program.Dict.GetInt("Length1") + program.Dict.GetInt("Length2") + program.Dict.GetInt("Length3"));

        var absorber = new TextAbsorber();
        saved.Pages[1].Accept(absorber);
        Assert.Equal("A A", absorber.Text.Trim());
    }

    [Fact]
    public void WithoutAnAfm_TheWidthsComeFromTheCharstrings()
    {
        var (_, font) = ShowAndSave(FontRepository.OpenFont(Pfb()), "A");
        var widths = Assert.IsType<PdfArray>(font.Get("Widths"));
        Assert.Equal(Type1FontBuilder.AWidth, ((PdfInteger)widths['A']).Value);
    }

    [Fact]
    public void Measures_ByItsOwnMetrics()
    {
        Font font = FontRepository.OpenFont(Pfb(), Afm());
        Assert.Equal(2 * Type1FontBuilder.AWidth * 10 / 1000.0, font.MeasureString("AA", 10.0), 6);
    }
}
