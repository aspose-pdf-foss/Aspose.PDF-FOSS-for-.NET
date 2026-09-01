using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests;

public class RectangleTests
{
    [Fact]
    public void Width_Height()
    {
        var r = new Rectangle(10, 20, 110, 220);
        Assert.Equal(100, r.Width);
        Assert.Equal(200, r.Height);
    }

    [Fact]
    public void IsEmpty_ZeroWidth_True()
    {
        var r = new Rectangle(10, 20, 10, 220);
        Assert.True(r.IsEmpty);
    }

    [Fact]
    public void IsEmpty_ZeroHeight_True()
    {
        var r = new Rectangle(10, 20, 110, 20);
        Assert.True(r.IsEmpty);
    }

    [Fact]
    public void IsEmpty_NormalRect_False()
    {
        var r = new Rectangle(0, 0, 612, 792);
        Assert.False(r.IsEmpty);
    }

    [Fact]
    public void Contains_PointInside_True()
    {
        var r = new Rectangle(0, 0, 100, 100);
        Assert.True(r.Contains(50, 50));
    }

    [Fact]
    public void Contains_PointOutside_False()
    {
        var r = new Rectangle(0, 0, 100, 100);
        Assert.False(r.Contains(150, 50));
    }

    [Fact]
    public void Contains_PointOnEdge_True()
    {
        var r = new Rectangle(0, 0, 100, 100);
        Assert.True(r.Contains(0, 0));
        Assert.True(r.Contains(100, 100));
    }

    [Fact]
    public void Contains_RectInside_True()
    {
        var outer = new Rectangle(0, 0, 200, 200);
        var inner = new Rectangle(10, 10, 100, 100);
        Assert.True(outer.Contains(inner));
    }

    [Fact]
    public void Contains_RectPartiallyOutside_False()
    {
        var outer = new Rectangle(0, 0, 100, 100);
        var partial = new Rectangle(50, 50, 150, 150);
        Assert.False(outer.Contains(partial));
    }

    [Fact]
    public void Intersect_Overlapping_ReturnsIntersection()
    {
        var a = new Rectangle(0, 0, 100, 100);
        var b = new Rectangle(50, 50, 150, 150);
        var inter = a.Intersect(b);
        Assert.NotNull(inter);
        Assert.Equal(50, inter!.LLX);
        Assert.Equal(50, inter!.LLY);
        Assert.Equal(100, inter!.URX);
        Assert.Equal(100, inter!.URY);
    }

    [Fact]
    public void Intersect_NonOverlapping_ReturnsEmpty()
    {
        var a = new Rectangle(0, 0, 50, 50);
        var b = new Rectangle(100, 100, 200, 200);
        var inter = a.Intersect(b);
        Assert.NotNull(inter);
        Assert.True(inter.IsEmpty);
    }

    [Fact]
    public void Equals_SameValues_True()
    {
        var a = new Rectangle(10, 20, 30, 40);
        var b = new Rectangle(10, 20, 30, 40);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_DifferentValues_False()
    {
        var a = new Rectangle(10, 20, 30, 40);
        var b = new Rectangle(10, 20, 30, 41);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToString_ContainsCoordinates()
    {
        var r = new Rectangle(0, 0, 612, 792);
        Assert.Equal("0,0,612,792", r.ToString());
    }

    [Fact]
    public void GetHashCode_SameValues_Equal()
    {
        var a = new Rectangle(10, 20, 30, 40);
        var b = new Rectangle(10, 20, 30, 40);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
