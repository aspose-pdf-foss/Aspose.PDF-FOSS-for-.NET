using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.Filters;

public class ManagedDeflaterTests
{
    private static readonly DeflateLevel[] Levels = { DeflateLevel.Store, DeflateLevel.Fast, DeflateLevel.Default, DeflateLevel.Best };

    private static IEnumerable<(string Name, byte[] Data)> Corpus()
    {
        var rnd = new Random(42);
        byte[] Random(int n) { var b = new byte[n]; rnd.NextBytes(b); return b; }
        yield return ("empty", Array.Empty<byte>());
        yield return ("one", new byte[] { 0x41 });
        yield return ("two", new byte[] { 0x41, 0x41 });
        yield return ("three", new byte[] { 0x41, 0x41, 0x41 });
        // literals 144..255 take the 9-bit fixed codes; a short input keeps the block fixed
        yield return ("high-literals-short", new byte[] { 0xF6, 0xF0, 0xA0, 0x90, 0xFF, 0x91 });
        yield return ("every-byte", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
        yield return ("every-byte-twice", Enumerable.Range(0, 512).Select(i => (byte)i).ToArray());
        yield return ("text", Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("BT /F1 12 Tf 72 700 Td (Hello, compressed PDF world) Tj ET\n", 300))));
        yield return ("zeros", new byte[100_000]);
        yield return ("random-small", Random(200));
        yield return ("random-4k", Random(4096));
        yield return ("random-over-stored-limit", Random(70_000));
        yield return ("random-300k", Random(300_000));
        var pattern = new byte[50_000];
        for (var i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i % 251);
        yield return ("pattern", pattern);
        var farRepeat = new byte[40_000];
        Array.Copy(Random(20_000), farRepeat, 20_000);
        Array.Copy(farRepeat, 0, farRepeat, 20_000, 20_000);
        yield return ("far-repeat", farRepeat);
    }

    [Fact]
    public void RoundTrips_ThroughTheManagedInflater_AtEveryLevel()
    {
        foreach (var (name, data) in Corpus())
            foreach (var level in Levels)
            {
                var compressed = ManagedDeflater.DeflateZlib(data, level);
                Assert.Equal(data, ManagedInflater.InflateZlib(compressed));
            }
    }

    [Fact]
    public void RoundTrips_ThroughTheFrameworkDecompressor_AtEveryLevel()
    {
        foreach (var (name, data) in Corpus())
            foreach (var level in Levels)
            {
                var compressed = ManagedDeflater.DeflateZlib(data, level);
                using var input = new MemoryStream(compressed);
                using var z = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                z.CopyTo(output);
                Assert.Equal(data, output.ToArray());
            }
    }

    [Fact]
    public void RawDeflate_RoundTrips()
    {
        foreach (var (_, data) in Corpus())
        {
            var raw = ManagedDeflater.DeflateRaw(data, 0, data.Length);
            Assert.Equal(data, ManagedInflater.InflateRaw(raw));
        }
    }

    [Fact]
    public void FixedBlock_HighLiterals_DecodeToTheSameBytes()
    {
        // The fixed code is defined over 288 symbols; canonical assignment over 286 put every
        // 9-bit literal four codes too low, and only a checksum-checking decoder noticed.
        var data = new byte[] { 0xF6, 0x00, 0x00, 0x63, 0x00, 0xF6 };
        var compressed = ManagedDeflater.DeflateZlib(data);
        using var input = new MemoryStream(compressed);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        Assert.Equal(data, output.ToArray());
    }

    [Fact]
    public void EmptyInput_IsTheCanonicalEmptyStream()
    {
        Assert.Equal(new byte[] { 0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01 }, ManagedDeflater.DeflateZlib(Array.Empty<byte>()));
    }

    [Fact]
    public void Output_IsTheSameOnEveryHostAndTarget()
    {
        // A pinned digest: the same input must deflate to the same bytes wherever the library
        // runs. Changing the encoder changes this value deliberately, never by runtime.
        var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("BT /F1 12 Tf 72 700 Td (Hello, compressed PDF world) Tj ET\n", 300)));
        Assert.Equal("E3B691BEA2A7559F489660E5FA355621269A4089", Sha1Hex(ManagedDeflater.DeflateZlib(text)));
    }

    [Fact]
    public void Output_IsDeterministic()
    {
        foreach (var (_, data) in Corpus())
            foreach (var level in Levels)
                Assert.Equal(ManagedDeflater.DeflateZlib(data, level), ManagedDeflater.DeflateZlib(data, level));
    }

    [Fact]
    public void Compresses_RedundantInput()
    {
        var text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("BT /F1 12 Tf 72 700 Td (Hello, compressed PDF world) Tj ET\n", 300)));
        Assert.True(ManagedDeflater.DeflateZlib(text).Length < text.Length / 20);
        Assert.True(ManagedDeflater.DeflateZlib(new byte[100_000]).Length < 200);
    }

    [Fact]
    public void IncompressibleInput_CostsAtMostTheStoredOverhead()
    {
        var rnd = new Random(7);
        var data = new byte[70_000];
        rnd.NextBytes(data);
        // every 16384-token block is stored (5 bytes of framing each) inside the 6-byte zlib envelope
        const int TokensPerBlock = 16384, StoredBlockFraming = 5, ZlibFraming = 6;
        var blocks = (data.Length + TokensPerBlock - 1) / TokensPerBlock;
        Assert.Equal(data.Length + blocks * StoredBlockFraming + ZlibFraming, ManagedDeflater.DeflateZlib(data).Length);
    }

    [Fact]
    public void Adler32_MatchesTheReferenceValue()
    {
        var data = Encoding.ASCII.GetBytes("Wikipedia");
        Assert.Equal(0x11E60398u, ManagedDeflater.Adler32(data, 0, data.Length));
    }

    [Fact]
    public void DeflaterOutputStream_RoundTrips()
    {
        var data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("stream payload ", 500)));
        using var ms = new MemoryStream();
        using (var z = new ZDeflaterOutputStream(ms))
        {
            z.Write(data, 0, 1000);
            z.Write(data, 1000, data.Length - 1000);
        }
        Assert.Equal(data, ManagedInflater.InflateZlib(ms.ToArray()));
    }

    private static string Sha1Hex(byte[] bytes)
    {
        using var sha1 = SHA1.Create();
        return BitConverter.ToString(sha1.ComputeHash(bytes)).Replace("-", "");
    }
}
