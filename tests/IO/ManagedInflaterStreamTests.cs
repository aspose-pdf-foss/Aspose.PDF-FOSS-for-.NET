using System.Linq;
using System.Text;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

/// <summary>
/// Whole streams read the way a decompressing stream reader reads them, the salvage of a
/// damaged stream, a bounded prefix, and the public inflating stream built on them.
/// </summary>
public class ManagedInflaterStreamTests
{
    private static readonly byte[] Text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 2000).Select(i => "line " + i + " of the stream test\n")));

    [Fact]
    public void InflateToEnd_WholeStream_FromAnOffset()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        var framed = new byte[] { 1, 2, 3 }.Concat(zlib).Concat(new byte[] { 4, 5 }).ToArray();
        Assert.Equal(Text, ManagedInflater.InflateToEnd(framed, 3, zlib.Length, zlibWrapper: true));
    }

    [Fact]
    public void InflateToEnd_CutShort_YieldsWhatItHolds()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        var part = ManagedInflater.InflateToEnd(zlib, 0, zlib.Length / 2, zlibWrapper: true);
        Assert.NotEmpty(part);
        Assert.Equal(Text.Take(part.Length), part);
    }

    [Fact]
    public void InflateToEnd_Malformed_Throws()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        var badHeader = (byte[])zlib.Clone();
        badHeader[1] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => ManagedInflater.InflateToEnd(badHeader, 0, badHeader.Length, zlibWrapper: true));

        var badCheck = (byte[])zlib.Clone();
        badCheck[badCheck.Length - 1] ^= 0x01;
        var e = Assert.Throws<InvalidDataException>(() => ManagedInflater.InflateToEnd(badCheck, 0, badCheck.Length, zlibWrapper: true));
        Assert.Contains("incorrect data check", e.Message);
    }

    [Fact]
    public void InflateToEnd_Raw_IgnoresWhatFollowsTheFinalBlock()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        // Past the two header bytes: raw deflate followed by the Adler-32, which raw reading leaves unread.
        Assert.Equal(Text, ManagedInflater.InflateToEnd(zlib, 2, zlib.Length - 2, zlibWrapper: false));
    }

    [Fact]
    public void Salvage_RawDeflateBehindABrokenHeader()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        zlib[0] = 0x00;
        Assert.Throws<InvalidDataException>(() => ManagedInflater.InflateZlib(zlib));
        Assert.Equal(Text, ManagedInflater.Salvage(zlib, 2, zlibWrapper: false));
    }

    [Fact]
    public void Salvage_DropsThePartlyFilledChunkBeforeAFault()
    {
        // Stored blocks of 65535 bytes: the second block's NLEN is broken, so the fault comes
        // after exactly one block, and the chunk it was filling (65535 mod 4096) is dropped.
        const int StoredBlock = 65535, ZlibHeader = 2, BlockHeader = 1, Lengths = 4;
        var data = Enumerable.Range(0, 2 * StoredBlock).Select(i => (byte)(i * 7)).ToArray();
        var damaged = ManagedDeflater.DeflateZlib(data, DeflateLevel.Store);
        damaged[ZlibHeader + BlockHeader + Lengths + StoredBlock + BlockHeader + 2] ^= 0xFF;

        var salvaged = ManagedInflater.Salvage(damaged, 0, zlibWrapper: true);
        Assert.Equal(data.Take(StoredBlock & ~4095), salvaged);
        Assert.Null(ManagedInflater.Salvage(new byte[] { 0xFF, 0xFF }, 0, zlibWrapper: false));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(4096)]
    [InlineData(1_000_000)]
    public void InflatePrefix_IsTheLeadingBytes(int maxBytes)
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        Assert.Equal(Text.Take(maxBytes), ManagedInflater.InflatePrefix(zlib, 2, maxBytes));
    }

    [Fact]
    public void InflaterInputStream_ReadsTheStream_AndLeavesTheSourceAfterIt()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        var source = new MemoryStream(zlib.Concat(new byte[] { 42, 43 }).ToArray());
        var output = new MemoryStream();
        using (var z = new ZInflaterInputStream(source))
            z.CopyTo(output);
        Assert.Equal(Text, output.ToArray());
        Assert.Equal(zlib.Length, source.Position);
        Assert.Equal(42, source.ReadByte());
    }

    [Fact]
    public void InflaterInputStream_Damaged_YieldsTheBytesBeforeTheFault_ThenThrows()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        zlib[zlib.Length - 1] ^= 0x01;
        using var z = new ZInflaterInputStream(new MemoryStream(zlib));
        var buffer = new byte[Text.Length];
        var read = 0;
        int n;
        while (read < buffer.Length && (n = z.Read(buffer, read, buffer.Length - read)) > 0) read += n;
        Assert.Equal(Text, buffer);
        Assert.Throws<InvalidDataException>(() => z.Read(buffer, 0, 1));
    }
}
