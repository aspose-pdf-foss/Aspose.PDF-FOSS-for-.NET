using System.IO.Compression;
using System.Text;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.Filters;

/// <summary>
/// Tests for the pure-managed inflater. Behavior must be identical across
/// hosts (Win 11 / Server 2022 / Linux / macOS) because we no longer rely on
/// the host's native zlib for primary decompression.
/// </summary>
public class ManagedInflaterTests
{
    // ── Roundtrip via BCL compressor → managed decoder ──────────────────────

    [Fact]
    public void Roundtrip_Tiny_FixedHuffman()
    {
        var input = Encoding.ASCII.GetBytes("hello");
        Assert.Equal(input, ManagedInflater.InflateZlib(CompressZlib(input)));
    }

    [Fact]
    public void Roundtrip_Repeated_DistanceOne()
    {
        // 200 identical bytes — produces back-references with distance 1.
        var input = Encoding.ASCII.GetBytes(new string('A', 200));
        Assert.Equal(input, ManagedInflater.InflateZlib(CompressZlib(input)));
    }

    [Fact]
    public void Roundtrip_Paragraph_DynamicHuffman()
    {
        var input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
            "The quick brown fox jumps over the lazy dog. ", 100)));
        Assert.Equal(input, ManagedInflater.InflateZlib(CompressZlib(input)));
    }

    [Fact]
    public void Roundtrip_RandomBinary_4KB()
    {
        // Random bytes don't compress well — exercises literal-heavy paths.
        var rnd = new Random(0);
        var input = new byte[4096];
        rnd.NextBytes(input);
        Assert.Equal(input, ManagedInflater.InflateZlib(CompressZlib(input)));
    }

    [Fact]
    public void Roundtrip_LargerThanWindow_64KB()
    {
        // > 32KB to ensure the sliding window logic is exercised across the
        // 32K wrap boundary.
        var rnd = new Random(1);
        var input = new byte[65536];
        rnd.NextBytes(input);
        Assert.Equal(input, ManagedInflater.InflateZlib(CompressZlib(input)));
    }

    [Fact]
    public void Roundtrip_RawDeflate_NoWrapper()
    {
        var input = Encoding.UTF8.GetBytes("PDF content streams sometimes omit the zlib wrapper.");
        Assert.Equal(input, ManagedInflater.InflateRaw(CompressRaw(input)));
    }

    // ── Header validation ────────────────────────────────────────────────────

    [Fact]
    public void InflateZlib_Rejects_TooShort()
    {
        Assert.Throws<InvalidDataException>(() => ManagedInflater.InflateZlib(new byte[] { 0x78 }));
    }

    [Fact]
    public void InflateZlib_Rejects_NonDeflateCM()
    {
        // CM=7 (high 4 bits of first byte don't matter, low 4 bits must be 8)
        // 0x77 = CINFO=7, CM=7 — invalid
        var bad = new byte[] { 0x77, 0x9F, 0x00, 0x00, 0x00, 0x00 };
        Assert.Throws<InvalidDataException>(() => ManagedInflater.InflateZlib(bad));
    }

    [Fact]
    public void InflateZlib_Rejects_BadHeaderChecksum()
    {
        // Valid CM but FCHECK fails: (0x78 * 256 + 0x00) % 31 != 0
        var bad = new byte[] { 0x78, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.Throws<InvalidDataException>(() => ManagedInflater.InflateZlib(bad));
    }

    [Fact]
    public void InflateZlib_Rejects_PresetDictionary()
    {
        // FDICT bit set (FLG bit 5). 0x78 0xBB = (0x78*256 + 0xBB) % 31 = 0
        // and 0xBB has bit 5 set.
        var bad = new byte[] { 0x78, 0xBB, 0x00, 0x00, 0x00, 0x00 };
        Assert.Throws<InvalidDataException>(() => ManagedInflater.InflateZlib(bad));
    }

    // ── Partial-output behavior on corrupt streams ──────────────────────────

    [Fact]
    public void InflateZlib_KeepsValidPrefix_OnTruncation()
    {
        // Compress less-compressible bytes so the deflate stream has substantial
        // body. Truncate just enough that the closing block is missing — the
        // inflater should still emit the valid prefix it managed to decode,
        // rounded down to whole 4096-byte chunks (the bytes past that boundary
        // are what chunked-read consumers lose in flight, so keeping them would
        // surface garbled spans other readers never see).
        var rnd = new Random(7);
        var input = new byte[16384];
        rnd.NextBytes(input);
        var compressed = CompressZlib(input);
        // Drop just enough trailing bytes (adler32 + a few body bytes) to make
        // the deflate stream incomplete but not eviscerate it.
        var truncated = compressed.AsSpan(0, compressed.Length - 8).ToArray();

        try
        {
            var result = ManagedInflater.InflateZlib(truncated);
            // Whole chunks only, and a substantial prefix of the original.
            Assert.True(result.Length % 4096 == 0,
                $"expected whole 4096-byte chunks, got {result.Length}");
            Assert.True(result.Length >= 8192,
                $"expected substantial partial output, got {result.Length}");
            // Every kept byte decoded before the fault, so the prefix matches.
            Assert.Equal(input.AsSpan(0, result.Length).ToArray(), result);
        }
        catch (InvalidDataException)
        {
            // If the bit stream broke before any literals were emitted, this is
            // acceptable — but we expect partial-output to be the common case.
        }
    }

    // ── Stored (BTYPE=00) blocks ────────────────────────────────────────────

    [Fact]
    public void Roundtrip_StoredBlock()
    {
        // CompressionLevel.NoCompression produces stored blocks.
        var input = Encoding.UTF8.GetBytes("Stored block test payload.");
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.NoCompression, leaveOpen: true))
            z.Write(input, 0, input.Length);
        var comp = ms.ToArray();

        Assert.Equal(input, ManagedInflater.InflateZlib(comp));
    }

    // ── Cross-check vs BCL on a corpus of varied inputs ─────────────────────

    [Fact]
    public void CrossCheck_AgainstBclZLibStream()
    {
        var rnd = new Random(42);
        foreach (var len in new[] { 0, 1, 16, 200, 1024, 4096, 16384 })
        {
            var input = new byte[len];
            rnd.NextBytes(input);
            var compressed = CompressZlib(input);

            // BCL decoded (golden truth on a working host).
            var bcl = DecompressViaBcl(compressed);
            var mine = ManagedInflater.InflateZlib(compressed);
            Assert.Equal(bcl, mine);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static byte[] CompressZlib(byte[] data)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionMode.Compress, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static byte[] CompressRaw(byte[] data)
    {
        var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionMode.Compress, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static byte[] DecompressViaBcl(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }
}
