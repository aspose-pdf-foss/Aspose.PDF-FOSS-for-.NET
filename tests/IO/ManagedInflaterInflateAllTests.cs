using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class ManagedInflaterInflateAllTests
{
    private static byte[] Text() => System.Text.Encoding.ASCII.GetBytes(string.Concat(System.Linq.Enumerable.Range(0, 2000).Select(i => "row " + i + " ")));

    [Fact]
    public void InflateAll_SoundStream_IsComplete()
    {
        var packed = ManagedDeflater.DeflateZlib(Text());
        var inflated = ManagedInflater.InflateAll(packed, true, out var complete);
        Assert.True(complete);
        Assert.Equal(Text(), inflated);
    }

    [Fact]
    public void InflateAll_RawStream()
    {
        var data = Text();
        var packed = ManagedDeflater.DeflateRaw(data, 0, data.Length);
        Assert.Equal(data, ManagedInflater.InflateAll(packed, false, out var complete));
        Assert.True(complete);
    }

    [Fact]
    public void InflateAll_CutShort_KeepsEveryByteItGot()
    {
        var packed = ManagedDeflater.DeflateZlib(Text(), DeflateLevel.Store);
        var inflated = ManagedInflater.InflateAll(packed[..(packed.Length / 2)], true, out var complete);
        Assert.False(complete);
        Assert.NotEmpty(inflated);
        Assert.Equal(Text()[..inflated.Length], inflated);
        // The lenient inflater keeps only whole 4 KB chunks; this keeps them all.
        Assert.NotEqual(0, inflated.Length % 4096);
    }
}
