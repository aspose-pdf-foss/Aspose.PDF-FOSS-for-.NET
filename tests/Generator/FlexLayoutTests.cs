using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>The flex algorithm against cases worked by hand from CSS Flexible Box Layout, section 9.</summary>
public class FlexLayoutTests
{
    private static FlexItem Item(float basis, float grow = 0, float shrink = 1, float chrome = 0, float min = 0, float max = float.PositiveInfinity) =>
        new() { BaseSize = basis, Grow = grow, Shrink = shrink, Chrome = chrome, MinSize = min, MaxSize = max };

    private static float[] Resolve(float room, float gap, params FlexItem[] items)
    {
        FlexLayout.ResolveLengths(items, room, gap);
        return items.Select(i => (float)System.Math.Round(i.MainSize, 2)).ToArray();
    }

    [Fact]
    public void Free_space_is_shared_by_grow_factor()
    {
        Assert.Equal(new[] { 113.33f, 186.67f }, Resolve(300, 0, Item(50, grow: 1), Item(60, grow: 2)));
    }

    [Fact]
    public void Factors_summing_under_one_share_only_that_fraction()
    {
        Assert.Equal(new[] { 100f, 100f }, Resolve(300, 0, Item(50, grow: 0.25f), Item(50, grow: 0.25f)));
    }

    [Fact]
    public void Shrinking_is_weighted_by_the_inner_base_size()
    {
        Assert.Equal(new[] { 160f, 80f }, Resolve(240, 0, Item(200), Item(100)));
        Assert.Equal(new[] { 148.57f, 91.43f }, Resolve(240, 0, Item(200, shrink: 3), Item(100)));
        // Chrome takes room but does not weigh: 200+40 and 200 in 300 give up 70 each.
        Assert.Equal(new[] { 130f, 130f }, Resolve(300, 0, Item(200, chrome: 40), Item(200)));
    }

    [Fact]
    public void A_clamped_item_is_frozen_and_the_rest_resolved_again()
    {
        Assert.Equal(new[] { 180f, 60f }, Resolve(240, 0, Item(200, min: 180), Item(200)));
        Assert.Equal(new[] { 80f, 220f }, Resolve(300, 0, Item(50, grow: 1, max: 80), Item(50, grow: 1)));
    }

    [Fact]
    public void An_item_that_cannot_shrink_leaves_the_overflow_to_the_others()
    {
        Assert.Equal(new[] { 200f, 100f }, Resolve(300, 0, Item(200, shrink: 0), Item(200)));
    }

    [Fact]
    public void Lines_break_where_the_next_item_and_its_gap_no_longer_fit()
    {
        var items = new[] { Item(150), Item(150), Item(145) };
        Assert.Equal(new[] { (0, 1), (1, 1), (2, 1) }, FlexLayout.CollectLines(items, 300, 10, true));
        Assert.Equal(new[] { (0, 2) }, FlexLayout.CollectLines(new[] { Item(145), Item(145) }, 300, 10, true));
        Assert.Equal(new[] { (0, 3) }, FlexLayout.CollectLines(items, 300, 10, false));
    }

    [Fact]
    public void Free_space_is_distributed_along_the_axis()
    {
        var sizes = new List<float> { 50, 60 };
        Assert.Equal(new[] { 95f, 145f }, FlexLayout.Distribute(sizes, 300, 0, FlexDistribution.Center));
        Assert.Equal(new[] { 190f, 240f }, FlexLayout.Distribute(sizes, 300, 0, FlexDistribution.End));
        Assert.Equal(new[] { 47.5f, 192.5f }, FlexLayout.Distribute(sizes, 300, 0, FlexDistribution.SpaceAround));
        Assert.Equal(new[] { 0f, 125f, 260f }, FlexLayout.Distribute(new List<float> { 50, 60, 40 }, 300, 10, FlexDistribution.SpaceBetween));
        var evenly = FlexLayout.Distribute(sizes, 300, 0, FlexDistribution.SpaceEvenly);
        Assert.Equal(63.33f, evenly[0], 2);
        Assert.Equal(new[] { 0f }, FlexLayout.Distribute(new List<float> { 50 }, 300, 0, FlexDistribution.SpaceBetween));
    }

    [Fact]
    public void Lines_stretch_into_a_definite_cross_size()
    {
        var placed = FlexLayout.PlaceLines(new List<float> { 20, 30 }, 200, 0, FlexDistribution.Stretch);
        Assert.Equal(new[] { (0f, 95f), (95f, 105f) }, placed);
        Assert.Equal(new[] { (0f, 20f), (20f, 30f) }, FlexLayout.PlaceLines(new List<float> { 20, 30 }, null, 0, FlexDistribution.Stretch));
        Assert.Equal(75f, FlexLayout.PlaceLines(new List<float> { 20, 30 }, 200, 0, FlexDistribution.Center)[0].Start);
    }

    [Fact]
    public void Items_are_placed_across_their_line()
    {
        Assert.Equal(0f, FlexLayout.PlaceAcross(20, 50, FlexCrossPlacement.Start));
        Assert.Equal(30f, FlexLayout.PlaceAcross(20, 50, FlexCrossPlacement.End));
        Assert.Equal(15f, FlexLayout.PlaceAcross(20, 50, FlexCrossPlacement.Center));
    }
}
