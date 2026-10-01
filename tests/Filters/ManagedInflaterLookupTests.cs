using System.IO.Compression;
using System.Linq;
using System.Text;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.Filters;

/// <summary>
/// The inflater decodes most codes through a lookup of the next few bits and leaves longer
/// codes, and whatever a damaged code set does, to the bit-at-a-time walk. These streams
/// reach both paths, the stored-block copy and every shape of back-reference copy.
/// </summary>
public class ManagedInflaterLookupTests
{
    [Fact]
    public void SkewedFrequencies_GiveCodesLongerThanTheLookup_AndStillDecode()
    {
        // Fibonacci counts give the deepest possible Huffman tree: the rarest symbols get
        // codes of up to 15 bits, past any lookup width.
        var data = new List<byte>();
        long a = 1, b = 1;
        for (var symbol = 0; symbol < 22; symbol++)
        {
            for (var i = 0; i < a; i++) data.Add((byte)(symbol * 11));
            (a, b) = (b, a + b);
        }
        var rnd = new Random(5);
        var shuffled = data.OrderBy(_ => rnd.Next()).ToArray();

        Assert.Equal(shuffled, ManagedInflater.InflateZlib(ManagedDeflater.DeflateZlib(shuffled)));
        Assert.Equal(shuffled, ManagedInflater.InflateZlib(FrameworkZlib(shuffled)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(100)]
    [InlineData(258)]
    [InlineData(300)]
    public void Runs_OfEveryPeriod_CopyTheirPattern(int period)
    {
        // A pattern repeated far past its own length is coded as back-references whose
        // distance (the period) is shorter than their length: the copy reads what it writes.
        var pattern = Enumerable.Range(0, period).Select(i => (byte)(i * 37 + 11)).ToArray();
        var data = Enumerable.Range(0, 5000).Select(i => pattern[i % period]).ToArray();

        foreach (var level in new[] { DeflateLevel.Fast, DeflateLevel.Default, DeflateLevel.Best })
            Assert.Equal(data, ManagedInflater.InflateZlib(ManagedDeflater.DeflateZlib(data, level)));
        Assert.Equal(data, ManagedInflater.InflateZlib(FrameworkZlib(data)));
    }

    [Fact]
    public void StoredBlocks_AfterCodedOnes_StartOnTheRightByte()
    {
        // Text then noise: coded blocks, then stored ones that begin on a byte boundary the
        // reader reaches with bits of the following bytes already fetched.
        var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("0 0 m 10 10 l S\n", 3000)));
        var noise = new byte[100_000];
        new Random(9).NextBytes(noise);
        var data = text.Concat(noise).Concat(text).ToArray();

        var zlib = ManagedDeflater.DeflateZlib(data);
        Assert.Equal(data, ManagedInflater.InflateZlib(zlib));
        var progress = ManagedInflater.Inflate(zlib, zlib.Length, true, null);
        Assert.Equal(InflateState.Done, progress.State);
        Assert.Equal(zlib.Length, progress.Consumed);
    }

    [Fact]
    public void StoredBlock_CutShort_KeepsTheBytesItHeld()
    {
        var data = new byte[1000];
        new Random(2).NextBytes(data);
        var stored = ManagedDeflater.DeflateZlib(data, DeflateLevel.Store);
        // zlib header (2) + block header (1) + LEN/NLEN (4) + 600 of the 1000 bytes
        const int Kept = 600;
        var cut = stored.Take(2 + 1 + 4 + Kept).ToArray();

        var all = ManagedInflater.InflateAll(cut, true, out var complete);
        Assert.False(complete);
        Assert.Equal(data.Take(Kept), all);
    }

    [Fact]
    public void OverSubscribedCodeSet_FailsTheSameWayWhateverTheInput()
    {
        // A dynamic block whose code-length code gives every one of the 19 symbols one bit:
        // no valid decoder table exists. Decoding must fault, not crash or loop.
        var stream = new byte[] { 0x78, 0x9C, 0x05, 0xE0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        var all = ManagedInflater.InflateAll(stream, true, out var complete);
        Assert.False(complete);
        var progress = ManagedInflater.Inflate(stream, stream.Length, true, null);
        Assert.NotEqual(InflateState.Done, progress.State);
        Assert.Equal(all, progress.Output);
    }

    private static byte[] FrameworkZlib(byte[] data)
    {
        var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data, 0, data.Length);
        return output.ToArray();
    }
}
