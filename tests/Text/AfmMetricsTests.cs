using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>An AFM file read into its global keys, character metrics and kerning pairs
/// (<see cref="AfmMetrics"/>).</summary>
public sealed class AfmMetricsTests
{
    private const string Sample =
        "StartFontMetrics 4.1\r\nComment made for the test\nFontName ProbeSans-Bold\nFullName Probe Sans Bold\n" +
        "Weight Bold\nItalicAngle -10.5\nFontBBox -100 -200 900 800\nAscender 750\nDescender -220\n" +
        "StartCharMetrics 3\nC 32 ; WX 250 ; N space ; B 0 0 0 0 ;\nC 65 ; WX 700 ; N A ; B 10 0 690 700 ;\n" +
        "C -1 ; N Aacute ; B 10 0 690 900 ;\nEndCharMetrics\nStartKernData\nStartKernPairs 1\nKPX A space -40\n" +
        "EndKernPairs\nEndKernData\nEndFontMetrics\n";

    [Fact]
    public void TheGlobalKeysReadAsTextAndNumbers()
    {
        var afm = AfmMetrics.Parse(System.Text.Encoding.ASCII.GetBytes(Sample))!;
        Assert.Equal("ProbeSans-Bold", afm.Text("FontName"));
        Assert.Equal("Probe Sans Bold", afm.Text("FullName"));
        Assert.Equal(-10.5, afm.Number("ItalicAngle"));
        Assert.Equal(-10, afm.Integer("ItalicAngle"));
        Assert.Equal(-220, afm.Integer("Descender"));
        Assert.Equal("-100 -200 900 800", afm.Text("FontBBox"));
        Assert.Null(afm.Text("Comment"));
        Assert.Null(afm.Integer("CapHeight"));
    }

    [Fact]
    public void CharactersAndKerningPairsAreReadInOrder()
    {
        var afm = AfmMetrics.Parse(System.Text.Encoding.ASCII.GetBytes(Sample))!;
        Assert.Equal(3, afm.Characters.Count);
        Assert.Equal(new AfmMetrics.CharMetric(65, 700, "A", afm.Characters[1].Box), afm.Characters[1]);
        Assert.Equal(new[] { 10, 0, 690, 700 }, afm.Characters[1].Box);
        // A character naming no code is -1; one naming no width is 0 wide.
        Assert.Equal((-1, 0, "Aacute"), (afm.Characters[2].Code, afm.Characters[2].Width, afm.Characters[2].Name));
        Assert.Equal(("A", "space", -40), afm.KernPairs.Single());
    }

    [Fact]
    public void TextThatIsNotAnAfmFileIsRefused()
    {
        Assert.Null(AfmMetrics.Parse(System.Text.Encoding.ASCII.GetBytes("not metrics")));
        Assert.Null(AfmMetrics.Parse(System.Array.Empty<byte>()));
    }
}
