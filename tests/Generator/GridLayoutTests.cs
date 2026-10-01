using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>The grid algorithm against cases worked by hand from CSS Grid Layout, sections 8.5 and 11.</summary>
public class GridLayoutTests
{
    private static (GridLinePlacement, GridLinePlacement) Auto(int columnSpan = 1, int rowSpan = 1) =>
        (new GridLinePlacement(null, null, columnSpan), new GridLinePlacement(null, null, rowSpan));

    private static float[] Size(float? room, float gap, GridTrack[] tracks, params GridContribution[] items)
    {
        GridLayout.SizeTracks(tracks, items, room, gap);
        return tracks.Select(t => (float)System.Math.Round(t.Base, 2)).ToArray();
    }

    [Fact]
    public void Sparse_placement_never_goes_back_and_dense_fills_the_holes()
    {
        var items = new[] { Auto(), Auto(columnSpan: 3), Auto(), Auto(columnSpan: 2), Auto() };
        var sparse = GridLayout.Place(items, 3, 0, false, false, out _, out var sparseRows);
        Assert.Equal(new[] { (0, 1, 0, 1), (0, 3, 1, 1), (0, 1, 2, 1), (1, 2, 2, 1), (0, 1, 3, 1) }, sparse);
        Assert.Equal(4, sparseRows);
        var dense = GridLayout.Place(items, 3, 0, false, true, out _, out var denseRows);
        Assert.Equal(new[] { (0, 1, 0, 1), (0, 3, 1, 1), (1, 1, 0, 1), (0, 2, 2, 1), (2, 1, 0, 1) }, dense);
        Assert.Equal(3, denseRows);
    }

    [Fact]
    public void A_column_locked_item_moves_the_cursor_to_its_column()
    {
        var items = new[] { (new GridLinePlacement(2, null, 2), new GridLinePlacement(null, null, 1)), Auto() };
        var placed = GridLayout.Place(items, 3, 0, false, false, out _, out _);
        Assert.Equal((1, 2, 0, 1), placed[0]);
        Assert.Equal((0, 1, 1, 1), placed[1]);
    }

    [Fact]
    public void Negative_lines_count_back_from_the_explicit_end_and_later_lines_add_tracks()
    {
        var items = new[] { (new GridLinePlacement(-2, null, null), new GridLinePlacement(null, null, null)) };
        Assert.Equal((2, 1, 0, 1), GridLayout.Place(items, 3, 0, false, false, out _, out _)[0]);
        var beyond = new[] { (new GridLinePlacement(3, null, null), new GridLinePlacement(null, null, null)) };
        GridLayout.Place(beyond, 1, 0, false, false, out var columns, out _);
        Assert.Equal(3, columns);
    }

    [Fact]
    public void Column_flow_fills_columns_first()
    {
        var placed = GridLayout.Place(new[] { Auto(), Auto(), Auto() }, 0, 2, true, false, out var columns, out var rows);
        Assert.Equal(new[] { (0, 1, 0, 1), (0, 1, 1, 1), (1, 1, 0, 1) }, placed);
        Assert.Equal((2, 2), (columns, rows));
    }

    [Fact]
    public void Auto_tracks_take_their_content_and_share_the_rest()
    {
        var tracks = new[] { GridTrack.Of(GridSizing.Auto), GridTrack.Of(GridSizing.Auto) };
        Assert.Equal(new[] { 133f, 167f }, Size(300, 0, tracks, new GridContribution(0, 1, 20.02f, 20.02f), new GridContribution(1, 1, 32f, 54.02f)));
    }

    [Fact]
    public void Fr_tracks_share_what_is_left_but_never_shrink_below_their_content()
    {
        Assert.Equal(new[] { 100f, 100f, 100f }, Size(300, 0, new[] { GridTrack.Of(GridSizing.Fixed, 100), GridTrack.Of(GridSizing.Flex, 1), GridTrack.Of(GridSizing.Flex, 1) }));
        var tracks = new[] { GridTrack.Of(GridSizing.Flex, 1), GridTrack.Of(GridSizing.Flex, 1) };
        Assert.Equal(new[] { 172f, 6f }, Size(150, 0, tracks, new GridContribution(0, 1, 172f, 250f), new GridContribution(1, 1, 6f, 6f)));
    }

    [Fact]
    public void Minmax_and_fit_content_grow_to_their_limits()
    {
        var minmax = new GridTrack { MinSizing = GridSizing.Fixed, MinValue = 50, MaxSizing = GridSizing.Fixed, MaxValue = 80 };
        Assert.Equal(new[] { 80f, 100f }, Size(300, 0, new[] { minmax, GridTrack.Of(GridSizing.Fixed, 100) }));
        var fit = new[] { GridTrack.Of(GridSizing.FitContent, 40), GridTrack.Of(GridSizing.FitContent, 200) };
        Assert.Equal(new[] { 40f, 54f }, Size(300, 0, fit, new GridContribution(0, 1, 32, 54), new GridContribution(1, 1, 32, 54)));
    }

    [Fact]
    public void A_spanning_item_grows_the_tracks_that_can_still_grow()
    {
        var tracks = new[] { GridTrack.Of(GridSizing.Auto), GridTrack.Of(GridSizing.Auto), GridTrack.Of(GridSizing.Flex, 1) };
        var sizes = Size(300, 0, tracks, new GridContribution(0, 2, 45, 142), new GridContribution(0, 1, 6, 6));
        Assert.Equal(new[] { 6f, 136f, 158f }, sizes);
    }

    [Fact]
    public void Beyond_their_limits_spanning_rows_share_equally()
    {
        var tracks = new[] { GridTrack.Of(GridSizing.Auto), GridTrack.Of(GridSizing.Auto) };
        Assert.Equal(new[] { 60f, 40f }, Size(null, 0, tracks, new GridContribution(0, 1, 30, 30), new GridContribution(1, 1, 10, 10), new GridContribution(0, 2, 100, 100)));
    }
}
