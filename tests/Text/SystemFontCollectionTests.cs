using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>Which face of a host font collection answers a name and a style, and which families take a
/// host collection at all.</summary>
public class SystemFontCollectionTests
{
    // One family whose faces tell their style in head.macStyle, OS/2 saying otherwise: the bold faces'
    // fsSelection carries the regular bit only, as in the Times collection macOS ships.
    private static byte[] OneFamilyStyledByMacStyle() => TrueTypeFaceBuilder.Collection(
        TrueTypeFaceBuilder.Named("Times", "Regular", 0, 0x40),
        TrueTypeFaceBuilder.Named("Times", "Bold", 1, 0x40),
        TrueTypeFaceBuilder.Named("Times", "Italic", 2, 0x41),
        TrueTypeFaceBuilder.Named("Times", "Bold Italic", 3, 0x41));

    // One family whose faces carry no OS/2 table at all, as in the Courier collection macOS ships.
    private static byte[] OneFamilyWithoutOs2() => TrueTypeFaceBuilder.Collection(
        TrueTypeFaceBuilder.Named("Courier", "Regular", 0, null),
        TrueTypeFaceBuilder.Named("Courier", "Bold", 1, null),
        TrueTypeFaceBuilder.Named("Courier", "Oblique", 2, null),
        TrueTypeFaceBuilder.Named("Courier", "Bold Oblique", 3, null));

    private static TrueTypeParser Parsed(byte[]? face)
    {
        Assert.NotNull(face);
        var parser = new TrueTypeParser(face!);
        parser.Parse();
        return parser;
    }

    [Theory]
    [InlineData(false, false, "Regular")]
    [InlineData(true, false, "Bold")]
    [InlineData(false, true, "Italic")]
    [InlineData(true, true, "Bold Italic")]
    public void ACollectionOfOneFamilyAnswersEachStyleWithItsOwnFace(bool bold, bool italic, string subfamily)
    {
        Assert.Equal(subfamily, Parsed(SystemFontResolver.ExtractFromTtc(OneFamilyStyledByMacStyle(), "Times", bold, italic)).SubfamilyName);
        var oblique = subfamily.Replace("Italic", "Oblique");
        Assert.Equal(oblique, Parsed(SystemFontResolver.ExtractFromTtc(OneFamilyWithoutOs2(), "Courier", bold, italic)).SubfamilyName);
    }

    [Fact]
    public void ACollectionFaceOfTheRequestedFamilyOutranksAnotherFamilysFaceOfTheRequestedStyle()
    {
        // Faces of one collection drawn to different glyph orders: the family named must be the one embedded,
        // in the style asked for where it has it and in its own style where it has not.
        var collection = TrueTypeFaceBuilder.Collection(
            TrueTypeFaceBuilder.Named("Gothic", "Regular", 0, 0x40),
            TrueTypeFaceBuilder.Named("Gothic UI", "Bold", 1, 0x20),
            TrueTypeFaceBuilder.Named("PGothic", "Regular", 0, 0x40),
            TrueTypeFaceBuilder.Named("PGothic", "Bold", 1, 0x20));
        Assert.Equal("PGothic-Bold", Parsed(SystemFontResolver.ExtractFromTtc(collection, "PGothic", true, false)).PostScriptName);
        Assert.Equal("Gothic", Parsed(SystemFontResolver.ExtractFromTtc(collection, "Gothic", true, false)).PostScriptName);
    }

    [Theory]
    [InlineData("Arial", "Arial")]
    [InlineData("Arial,Bold", "Arial")]
    [InlineData("Arial-BoldItalicMT", "Arial")]
    [InlineData("Times New Roman", "Times New Roman")]
    [InlineData("TimesNewRomanPS-BoldMT", "Times New Roman")]
    [InlineData("Courier New", "Courier New")]
    [InlineData("CourierNewPS-ItalicMT", "Courier New")]
    public void AFamilyWithFilesOfItsOwnIsLookedUpAsItselfNotAsTheStandardFaceItStandsIn(string name, string family)
    {
        // A host collection of the standard faces (macOS Helvetica.ttc, Times.ttc, Courier.ttc) is the standard
        // names' own face; Arial, Times New Roman and Courier New are faces of their own, installed under their names.
        var (file, mapped, _, _) = SystemFontResolver.MapFontName(name);
        Assert.Equal(family, mapped);
        Assert.Null(file);
    }

    [Theory]
    [InlineData("Helvetica-Bold", "Helvetica.ttc", "Helvetica")]
    [InlineData("Times-Roman", "Times.ttc", "Times")]
    [InlineData("Courier-Oblique", "Courier.ttc", "Courier")]
    public void AStandardFaceTakesTheHostCollectionOfItsFamily(string name, string file, string family)
    {
        var (mappedFile, mapped, _, _) = SystemFontResolver.MapFontName(name);
        Assert.Equal(file, mappedFile);
        Assert.Equal(family, mapped);
    }

    [Theory]
    [InlineData("Times New Roman")]
    [InlineData("Arial")]
    [InlineData("Courier New")]
    public void TheResolverAndTheRepositoryAnswerAnInstalledFamilyWithOneFace(string family)
    {
        // The repository finds the family by its file or its name table; the resolver, which lends faces to the
        // renderer and to embedding, must not answer the same name with another face.
        var found = FontRepository.TryFindFont(family)?.SourceFontData?.TtfData;
        if (found is null) return; // the family is not installed on this host
        Assert.Equal(Parsed(CjkFallbackFont.NormalizeToSfnt(found)).PostScriptName,
            Parsed(SystemFontResolver.Resolve(family)).PostScriptName);
    }
}
