using System.IO.Compression;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.Filters;

public class FilterTests
{
    [Fact]
    public void FlateDecode_BasicDecompression()
    {
        var original = Encoding.ASCII.GetBytes("Hello, World! This is a test of FlateDecode compression.");
        var compressed = Compress(original);
        var result = FlateDecodeFilter.Decode(compressed, null);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Ascii85Decode_BasicDecode()
    {
        // "Man " = 9jqo^
        var input = Encoding.ASCII.GetBytes("9jqo^~>");
        var result = Ascii85DecodeFilter.Decode(input);
        Assert.Equal("Man ", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Ascii85Decode_ZShortcut()
    {
        // 'z' = four zero bytes
        var input = Encoding.ASCII.GetBytes("z~>");
        var result = Ascii85DecodeFilter.Decode(input);
        Assert.Equal(4, result.Length);
        Assert.All(result, b => Assert.Equal(0, b));
    }

    [Fact]
    public void AsciiHexDecode_BasicDecode()
    {
        var input = Encoding.ASCII.GetBytes("48656C6C6F>");
        var result = AsciiHexDecodeFilter.Decode(input);
        Assert.Equal("Hello", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void AsciiHexDecode_WithWhitespace()
    {
        var input = Encoding.ASCII.GetBytes("48 65 6C 6C 6F>");
        var result = AsciiHexDecodeFilter.Decode(input);
        Assert.Equal("Hello", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void AsciiHexDecode_OddNibble()
    {
        var input = Encoding.ASCII.GetBytes("ABC>");
        var result = AsciiHexDecodeFilter.Decode(input);
        Assert.Equal(2, result.Length);
        Assert.Equal(0xAB, result[0]);
        Assert.Equal(0xC0, result[1]);
    }

    [Fact]
    public void RunLengthDecode_LiteralRun()
    {
        // Length=2 means copy next 3 bytes literally
        var input = new byte[] { 2, 0x41, 0x42, 0x43, 128 }; // "ABC" + EOD
        var result = RunLengthDecodeFilter.Decode(input);
        Assert.Equal("ABC", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void RunLengthDecode_RepeatRun()
    {
        // Length=253 means repeat next byte (257-253)=4 times
        var input = new byte[] { 253, 0x41, 128 }; // "AAAA" + EOD
        var result = RunLengthDecodeFilter.Decode(input);
        Assert.Equal("AAAA", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void RunLengthDecode_MixedRuns()
    {
        // Literal "AB" (1, 0x41, 0x42) + Repeat 'C' x3 (254, 0x43) + EOD
        var input = new byte[] { 1, 0x41, 0x42, 254, 0x43, 128 };
        var result = RunLengthDecodeFilter.Decode(input);
        Assert.Equal("ABCCC", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void LzwTiffEncoder_RoundTrip_SingleByte()
    {
        var original = new byte[] { 0x80 };
        var encoded = LzwTiffEncoder.Encode(original);
        var decoded = LzwDecodeFilter.Decode(encoded, null);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void LzwTiffEncoder_RoundTrip_AsciiRepeating()
    {
        var original = Encoding.ASCII.GetBytes(
            "The quick brown fox jumps over the lazy dog. " +
            "The quick brown fox jumps over the lazy dog. " +
            "The quick brown fox jumps over the lazy dog.");
        var encoded = LzwTiffEncoder.Encode(original);
        var decoded = LzwDecodeFilter.Decode(encoded, null);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void LzwTiffEncoder_RoundTrip_AllBytes_Short()
    {
        // Stay well below the 9→10-bit bump threshold to isolate basic-path
        // correctness from code-size-bump correctness.
        var original = new byte[200];
        for (var i = 0; i < original.Length; i++) original[i] = (byte)i;
        var encoded = LzwTiffEncoder.Encode(original);
        var decoded = LzwDecodeFilter.Decode(encoded, null);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void LzwTiffEncoder_RoundTrip_AllBytes()
    {
        // Full 256-byte alphabet + repeats — forces growth into 9-bit codes and
        // exercises the code-size bump early.
        var original = new byte[1024];
        for (var i = 0; i < original.Length; i++) original[i] = (byte)i;
        var encoded = LzwTiffEncoder.Encode(original);
        var decoded = LzwDecodeFilter.Decode(encoded, null);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void LzwTiffEncoder_RoundTrip_LargeCompressible()
    {
        // Compressible pattern large enough to force a 9→10→11→12-bit bump and
        // eventually trigger the mid-stream table reset (ClearCode).
        var original = new byte[200_000];
        for (var i = 0; i < original.Length; i++)
            original[i] = (byte)((i * 7) % 251);
        var encoded = LzwTiffEncoder.Encode(original);
        Assert.True(encoded.Length < original.Length,
            "encoded output should be at least somewhat smaller than the input");
        var decoded = LzwDecodeFilter.Decode(encoded, null);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void CmykToRgbLut_HitsTemplateTargetWithinTolerance()
    {
        // Canonical regression target: /IC [0.2 0.3 0.7 0.6].
        // GDI+ template renders that as sRGB (102, 102, 51); the naïve
        // (1−C)(1−K) formula gives (82, 71, 31) with G off by 31. The LUT
        // should land within the visual-comparison tolerance of 30 per
        // channel.
        var (r, g, b) = CmykToRgbLut.Convert(0.2, 0.3, 0.7, 0.6);
        Assert.InRange(r, 102 - 30, 102 + 30);
        Assert.InRange(g, 102 - 30, 102 + 30);
        Assert.InRange(b, 51 - 30, 51 + 30);
    }

    [Fact]
    public void CmykToRgbLut_PureBlackAndWhite()
    {
        // Pure white = (0,0,0,0) → near-white sRGB.
        var (wr, wg, wb) = CmykToRgbLut.Convert(0, 0, 0, 0);
        Assert.True(wr >= 240 && wg >= 240 && wb >= 240, $"white was ({wr},{wg},{wb})");
        // Pure black = (0,0,0,1) → near-black sRGB.
        var (br, bg, bb) = CmykToRgbLut.Convert(0, 0, 0, 1);
        Assert.True(br < 64 && bg < 64 && bb < 64, $"black was ({br},{bg},{bb})");
    }

    [Fact]
    public void TiffPaletteQuantizer_ColorMap_HasBlackAndWhite()
    {
        var map = TiffPaletteQuantizer.BuildColorMap332();
        Assert.Equal(768, map.Length);
        // Index 0 → 0,0,0 (black). Index 255 (0xFF = RRRGGGBB = 111 111 11)
        // → top 3 R bits = 7 → full red 255, same for G, B = 3 → full blue.
        Assert.Equal((ushort)0, map[0]);
        Assert.Equal((ushort)0, map[256]);
        Assert.Equal((ushort)0, map[512]);
        Assert.Equal((ushort)65535, map[255]);
        Assert.Equal((ushort)65535, map[511]);
        Assert.Equal((ushort)65535, map[767]);
    }

    [Fact]
    public void TiffPaletteQuantizer_Quantize_PreservesBlackAndWhite()
    {
        var rgb = new byte[] { 0, 0, 0, 255, 255, 255, 128, 128, 128 };
        var indexed = TiffPaletteQuantizer.QuantizeRgbTo8bpp(rgb, 3, 1);
        Assert.Equal(0, indexed[0]);       // pure black → index 0
        Assert.Equal(255, indexed[1]);     // pure white → index 255 (0xFF)
        // 128 in top-3 bits = 4, same for B (top-2 bits of 128 = 2).
        // Expected: (4 << 5) | (4 << 2) | 2 = 128 | 16 | 2 = 146
        Assert.Equal(146, indexed[2]);
    }

    [Fact]
    public void LzwDecode_BasicData()
    {
        // Simple LZW-encoded data test
        // Use a known LZW sequence — this is a minimal test
        // LZW encoding of [0x80] with clear code
        // Clear=256, EOD=257
        // At 9 bits: 256 (clear), 128 (literal 0x80), 257 (EOD)
        // Binary: 100000000 010000000 100000001
        var bits = new byte[]
        {
            0b10000000, 0b00100000, 0b00100000, 0b001_00000
        };
        var result = LzwDecodeFilter.Decode(bits, null);
        Assert.Contains((byte)0x80, result);
    }

    [Theory]
    [InlineData("DCTDecode")]
    [InlineData("DCT")]
    [InlineData("JPXDecode")]
    [InlineData("JPX")]
    [InlineData("Crypt")]
    public void PassThroughFilters_ReturnDataUnchanged(string filterName)
    {
        var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var dict = new PdfDictionary();
        dict.Set("Filter", new PdfName(filterName));
        var result = StreamFilter.Decode(data, dict);
        Assert.Equal(data, result);
    }

    [Fact]
    public void Jbig2Decode_InvalidData_DoesNotThrow()
    {
        var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var dict = new PdfDictionary();
        dict.Set("Filter", new PdfName("JBIG2Decode"));
        var ex = Record.Exception(() => StreamFilter.Decode(data, dict));
        Assert.Null(ex);
    }

    [Fact]
    public void Jbig2_RealCorpusPdf_RendersWithContent()
    {
        // Renders page 2 of a JBIG2-using PDF (a German waybill scan). Page 2 is the JBIG2 page
        // — page 1 contains only FlateDecode'd content. Gated on the ASPOSE_PDF_TESTDATA env var.
        // The 50 KB lower-bound guards against the prior all-mask/all-black regression in which
        // the QM coder emitted bit-stream garbage and the page rendered as a near-empty bitmap.
        var root = Environment.GetEnvironmentVariable("ASPOSE_PDF_TESTDATA");
        if (string.IsNullOrEmpty(root)) return;
        var pdfPath = Path.Combine(root!, "Aspose.Pdf", "59727", "1002P010459348.pdf");
        if (!File.Exists(pdfPath)) return;

        using var doc = Document.Open(File.ReadAllBytes(pdfPath));
        using var ms = new MemoryStream();
        var dev = new PngDevice(new Resolution(150));
        dev.Process(doc.Pages[2], ms);
        Assert.True(ms.Length > 50_000,
            $"rendered JBIG2 page suspiciously small ({ms.Length} bytes) — likely QM-decoder regression");
    }

    [Fact]
    public void UnknownFilter_PassesThrough()
    {
        var data = new byte[] { 1, 2, 3, 4 };
        var dict = new PdfDictionary();
        dict.Set("Filter", new PdfName("SomeUnknownFilter"));
        var result = StreamFilter.Decode(data, dict);
        Assert.Equal(data, result);
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(data);
        }
        return ms.ToArray();
    }
}
