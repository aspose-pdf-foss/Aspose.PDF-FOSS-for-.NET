using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A character's box spans its own glyph advance: a TJ number before the
/// first glyph moves it, and a TJ number after a glyph opens a gap the glyph does
/// not reach across.</summary>
public class CharacterBoxTests
{
    // Helvetica advances at 10 pt: H 7.22, e 5.56, l 2.22.
    private const double Tolerance = 0.01;

    private static List<(char Ch, Rectangle Box, Position Pos)> CharactersOf(string content)
    {
        var data = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes(content));
        using var doc = Document.Open(data);
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(doc.Pages[1]);
        var chars = new List<(char, Rectangle, Position)>();
        foreach (var fragment in absorber.TextFragments)
            foreach (var segment in fragment.Segments)
            {
                var i = 0;
                foreach (var ci in segment.Characters)
                    chars.Add((segment.Text[i++], ci.Rectangle, ci.Position));
            }
        return chars;
    }

    [Fact]
    public void ALeadingNumberMovesTheFirstGlyph()
    {
        var chars = CharactersOf("BT /F1 10 Tf 100 700 Td [-1000 (He)] TJ ET");
        Assert.Equal('H', chars[0].Ch);
        Assert.Equal(110, chars[0].Box.LLX, Tolerance);
        Assert.Equal(117.22, chars[0].Box.URX, Tolerance);
        Assert.Equal(110, chars[0].Pos.XIndent, Tolerance);
        Assert.Equal(117.22, chars[1].Box.LLX, Tolerance);
        Assert.Equal(122.78, chars[1].Box.URX, Tolerance);
    }

    [Fact]
    public void AGlyphBeforeAGapEndsAtItsOwnAdvance()
    {
        var chars = CharactersOf("BT /F1 10 Tf 100 700 Td [(ll) -3000 (ll)] TJ ET");
        var l2 = chars[1];
        Assert.Equal('l', l2.Ch);
        Assert.Equal(102.22, l2.Box.LLX, Tolerance);
        Assert.Equal(104.44, l2.Box.URX, Tolerance);
        var l3 = chars.First(c => c.Ch == 'l' && c.Box.LLX > l2.Box.URX);
        Assert.Equal(134.44, l3.Box.LLX, Tolerance);
        Assert.Equal(136.66, l3.Box.URX, Tolerance);
    }

    [Fact]
    public void ASpaceStandingForAGapSpansTheGap()
    {
        var chars = CharactersOf("BT /F1 10 Tf 100 700 Td [(ll) -3000 (ll)] TJ ET");
        var space = chars.Single(c => c.Ch == ' ');
        Assert.Equal(104.44, space.Box.LLX, Tolerance);
        Assert.Equal(134.44, space.Box.URX, Tolerance);
    }

    [Fact]
    public void ABareStandardFaceReachesDownToItsDescent()
    {
        // Helvetica's descender is 0.207 em: its glyphs, and so their boxes, go that far below the baseline.
        var chars = CharactersOf("BT /F1 10 Tf 100 700 Td (gap) Tj ET");
        Assert.All(chars, c => Assert.Equal(697.93, c.Box.LLY, Tolerance));
        Assert.All(chars, c => Assert.Equal(c.Box.LLY, c.Pos.YIndent, Tolerance));
        Assert.All(chars, c => Assert.True(c.Box.URY > 707, $"top {c.Box.URY}"));
    }

    [Fact]
    public void GlyphsOfAPlainShowAdjoin()
    {
        var chars = CharactersOf("BT /F1 10 Tf 100 700 Td (Hel) Tj ET");
        Assert.Equal(100, chars[0].Box.LLX, Tolerance);
        Assert.Equal(107.22, chars[0].Box.URX, Tolerance);
        Assert.Equal(chars[0].Box.URX, chars[1].Box.LLX, Tolerance);
        Assert.Equal(chars[1].Box.URX, chars[2].Box.LLX, Tolerance);
        Assert.Equal(115.0, chars[2].Box.URX, Tolerance);
    }
}
