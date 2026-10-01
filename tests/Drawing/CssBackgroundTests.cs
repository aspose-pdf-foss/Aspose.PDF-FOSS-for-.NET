using Aspose.Pdf.Drawing;
using Xunit;

namespace Aspose.Pdf.Tests.Drawing;

/// <summary>CSS background geometry: sizes (natural, contain, cover, given sides),
/// positions from an edge, and rounded and spaced repeats.</summary>
public sealed class CssBackgroundTests
{
    private const double Tolerance = 1e-4;

    private static CssBackgroundTile Tile(float x, float y, float w, float h) => new() { X = x, Y = y, Width = w, Height = h };

    private static void Same(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], Tolerance);
    }

    [Fact]
    public void SizesKeepThePicturesProportionsUnlessBothSidesAreGiven()
    {
        Same([30, 20], CssBackground.Size(30, 20, CssBackgroundSizing.Explicit, null, false, null, false, 100, 50));
        Same([75, 50], CssBackground.Size(30, 20, CssBackgroundSizing.Contain, null, false, null, false, 100, 50));
        Same([300, 200], CssBackground.Size(30, 20, CssBackgroundSizing.Cover, null, false, null, false, 100, 200));
        Same([50, 10], CssBackground.Size(30, 20, CssBackgroundSizing.Explicit, 50, true, 20, true, 100, 50));
        Same([37.5, 25], CssBackground.Size(30, 20, CssBackgroundSizing.Explicit, null, false, 50, true, 100, 50));
        // A picture without a natural size fills the area on any side not given.
        Same([60, 50], CssBackground.Size(null, null, CssBackgroundSizing.Explicit, 60, false, null, false, 100, 50));
    }

    [Fact]
    public void PositionsAreMeasuredFromTheirEdgeAndACentredOffsetIsNothing()
    {
        Assert.Equal(93, CssBackground.Position(CssBackgroundEdge.End, 7, false, 100), Tolerance);
        Assert.Equal(20, CssBackground.Position(CssBackgroundEdge.Start, 20, true, 100), Tolerance);
        Assert.Equal(50, CssBackground.Position(CssBackgroundEdge.Center, 0, false, 100), Tolerance);
        Assert.Equal(0, CssBackground.Position(CssBackgroundEdge.Center, 7, false, 100), Tolerance);
    }

    [Fact]
    public void RoundingFitsWholeCopiesKeepingTheTopEdge()
    {
        var tile = Tile(7, 9, 40, 20);
        var gap = CssBackground.Arrange(ref tile, CssBackgroundRepeat.Round, CssBackgroundRepeat.NoRepeat, false, false, Tile(0, 0, 100, 50));
        Same([0, 0], [gap[0], gap[1]]);
        Same([7, 12.3333, 33.3333, 16.6667], [tile.X, tile.Y, tile.Width, tile.Height]);

        tile = Tile(0, 0, 80, 60);
        CssBackground.Arrange(ref tile, CssBackgroundRepeat.Round, CssBackgroundRepeat.Round, false, false, Tile(0, 0, 100, 30));
        Same([0, 30, 100, 30], [tile.X, tile.Y, tile.Width, tile.Height]);
    }

    [Fact]
    public void SpacingSpreadsWholeCopiesOrLeavesTheLargerSpaceBeside()
    {
        var tile = Tile(10, 20, 30, 20);
        var gap = CssBackground.Arrange(ref tile, CssBackgroundRepeat.Space, CssBackgroundRepeat.Space, false, false, Tile(0, 0, 100, 50));
        Same([5, 10], [gap[0], gap[1]]);
        Same([0, 30], [tile.X, tile.Y]);

        tile = Tile(7, 9, 60, 30);
        gap = CssBackground.Arrange(ref tile, CssBackgroundRepeat.Space, CssBackgroundRepeat.Space, false, false, Tile(2, 3, 100, 50));
        Same([35, 14], [gap[0], gap[1]]);
        Same([7, 9], [tile.X, tile.Y]);
    }
}
