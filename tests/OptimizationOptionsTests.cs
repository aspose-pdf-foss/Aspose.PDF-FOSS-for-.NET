using Aspose.Pdf.Optimization;
using Xunit;

namespace Aspose.Pdf.Tests;

public class OptimizationOptionsTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var opts = OptimizationOptions.Default;
        Assert.True(opts.RemoveUnusedObjects);
        Assert.True(opts.RemoveUnusedStreams);
        Assert.True(opts.LinkDuplicateStreams);
        Assert.False(opts.CompressImages);
        Assert.Equal(75, opts.ImageQuality);
        Assert.False(opts.UnembedFonts);
        Assert.False(opts.SubsetFonts);
        Assert.False(opts.RemoveMetadata);
    }

    [Fact]
    public void All_HasAgressiveValues()
    {
        var opts = OptimizationOptions.All();
        Assert.True(opts.RemoveUnusedObjects);
        Assert.True(opts.RemoveUnusedStreams);
        Assert.True(opts.LinkDuplicateStreams);
        Assert.True(opts.CompressImages);
        Assert.Equal(50, opts.ImageQuality);
        Assert.True(opts.UnembedFonts);
        Assert.True(opts.SubsetFonts);
        Assert.True(opts.RemoveMetadata);
    }

    [Fact]
    public void Properties_AreSettable()
    {
        var opts = new OptimizationOptions
        {
            CompressImages = true,
            ImageQuality = 90,
            SubsetFonts = true,
        };
        Assert.True(opts.CompressImages);
        Assert.Equal(90, opts.ImageQuality);
        Assert.True(opts.SubsetFonts);
    }
}
