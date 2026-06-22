using System.IO.Compression;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.Filters;

public class FilterEdgeCaseTests
{
    #region FlateDecode

    [Fact]
    public void FlateDecode_EmptyData_Roundtrip()
    {
        var original = Array.Empty<byte>();
        var compressed = Compress(original);
        var result = FlateDecodeFilter.Decode(compressed, null);
        Assert.Empty(result);
    }

    [Fact]
    public void FlateDecode_LargeData_Roundtrip()
    {
        // 10KB of pseudo-random data
        var original = new byte[10240];
        var rng = new Random(42);
        rng.NextBytes(original);

        var compressed = Compress(original);
        var result = FlateDecodeFilter.Decode(compressed, null);
        Assert.Equal(original, result);
    }

    [Fact]
    public void FlateDecode_PngSubPredictor()
    {
        // Predictor=11 (PNG Sub), columns=4, colors=1, bpc=8
        // Row bytes = 4*1*8/8 = 4, bytesPerPixel = 1
        // Sub: each byte = raw + left neighbor
        // Original row: [10, 20, 30, 40]
        // Sub-encoded:  filterType=1, delta[0]=10, delta[1]=20-10=10, delta[2]=30-20=10, delta[3]=40-30=10
        var pngData = new byte[]
        {
            1, 10, 10, 10, 10, // row 0: Sub filter
            1, 5, 15, 5, 15,   // row 1: Sub filter → [5, 20, 25, 40]
        };

        var compressed = Compress(pngData);
        var parms = MakeParms(predictor: 11, columns: 4, colors: 1);
        var result = FlateDecodeFilter.Decode(compressed, parms);

        Assert.Equal(8, result.Length);
        // Row 0
        Assert.Equal(10, result[0]);
        Assert.Equal(20, result[1]);
        Assert.Equal(30, result[2]);
        Assert.Equal(40, result[3]);
        // Row 1
        Assert.Equal(5, result[4]);
        Assert.Equal(20, result[5]);
        Assert.Equal(25, result[6]);
        Assert.Equal(40, result[7]);
    }

    [Fact]
    public void FlateDecode_PngUpPredictor()
    {
        // Predictor=12 (PNG Up), columns=3, colors=1, bpc=8
        // Up: each byte = raw + byte above
        // Row 0: [10, 20, 30] — no previous row, so filterType=2, deltas = [10, 20, 30]
        // Row 1: original [15, 25, 35] → deltas = [15-10, 25-20, 35-30] = [5, 5, 5]
        var pngData = new byte[]
        {
            2, 10, 20, 30,  // row 0: Up
            2, 5, 5, 5,     // row 1: Up → [15, 25, 35]
        };

        var compressed = Compress(pngData);
        var parms = MakeParms(predictor: 12, columns: 3, colors: 1);
        var result = FlateDecodeFilter.Decode(compressed, parms);

        Assert.Equal(6, result.Length);
        Assert.Equal(new byte[] { 10, 20, 30, 15, 25, 35 }, result);
    }

    [Fact]
    public void FlateDecode_PngAveragePredictor()
    {
        // Predictor=13 (PNG Average), columns=4, colors=1, bpc=8
        // Average: raw + floor((left + above) / 2)
        // Row 0: no above, so average = floor(left / 2)
        //   col0: raw=20 + floor(0/2)=20
        //   col1: raw=10 + floor(20/2)=20
        //   col2: raw=10 + floor(20/2)=20 (actually raw + floor(left/2) since above=0)
        //   Wait — let me be precise. For row 0 (no prev row), b=0.
        //   col0: a=0, b=0 → avg=0, decoded=20
        //   col1: a=20, b=0 → avg=10, decoded=10+10=20
        //   col2: a=20, b=0 → avg=10, decoded=10+10=20
        //   col3: a=20, b=0 → avg=10, decoded=10+10=20
        // So original row 0 = [20, 20, 20, 20]
        var pngData = new byte[]
        {
            3, 20, 10, 10, 10,  // row 0: Average → [20, 20, 20, 20]
        };

        var compressed = Compress(pngData);
        var parms = MakeParms(predictor: 13, columns: 4, colors: 1);
        var result = FlateDecodeFilter.Decode(compressed, parms);

        Assert.Equal(4, result.Length);
        Assert.Equal(new byte[] { 20, 20, 20, 20 }, result);
    }

    [Fact]
    public void FlateDecode_PngPaethPredictor()
    {
        // Predictor=14 (PNG Paeth), columns=3, colors=1, bpc=8
        // Row 0: a=0, b=0, c=0 → Paeth(0,0,0)=0 → decoded = raw + 0
        //   [100, 50, 25] from raw [100, 50, 25]... wait, Paeth with first row:
        //   col0: a=0,b=0,c=0 → Paeth=0, decoded=100
        //   col1: a=100,b=0,c=0 → p=100, pa=0, pb=100, pc=100 → pa<=pb → a=100, decoded=50+100=150
        //   col2: a=150,b=0,c=0 → p=150, pa=0, pb=150, pc=150 → pa<=pb → a=150, decoded=25+150=175
        // Simpler: use values that produce clean results
        // Row 0 with Paeth, all a=b=c=0 for col0:
        //   col0: decoded = raw
        //   col1: Paeth(col0_decoded, 0, 0) = col0_decoded
        // Let me use small values and just verify the round-trip property
        // Instead: encode two rows so Paeth actually uses all four neighbors
        var row0 = new byte[] { 4, 10, 10, 10 }; // Paeth, row0: [10, 20, 30]
        var row1 = new byte[] { 4, 5, 5, 5 };    // Paeth, row1

        // Row 0: col0: a=0,b=0,c=0 → P=0 → 10
        //         col1: a=10,b=0,c=0 → P=10, pa=0,pb=10,pc=10 → a=10 → 10+10=20
        //         col2: a=20,b=0,c=0 → P=20, pa=0,pb=20,pc=20 → a=20 → 10+20=30
        // Row 1: col0: a=0,b=10,c=0 → P=10, pa=10,pb=0,pc=10 → b=10 → 5+10=15
        //         col1: a=15,b=20,c=10 → P=25, pa=10,pb=5,pc=15 → pb<=pc → b=20 → 5+20=25
        //         col2: a=25,b=30,c=20 → P=35, pa=10,pb=5,pc=15 → pb<=pc → b=30 → 5+30=35

        var pngData = new byte[row0.Length + row1.Length];
        Array.Copy(row0, 0, pngData, 0, row0.Length);
        Array.Copy(row1, 0, pngData, row0.Length, row1.Length);

        var compressed = Compress(pngData);
        var parms = MakeParms(predictor: 14, columns: 3, colors: 1);
        var result = FlateDecodeFilter.Decode(compressed, parms);

        Assert.Equal(6, result.Length);
        Assert.Equal(new byte[] { 10, 20, 30, 15, 25, 35 }, result);
    }

    [Fact]
    public void FlateDecode_TiffPredictor()
    {
        // Predictor=2 (TIFF), columns=4, colors=1, bpc=8
        // TIFF predictor: each byte = data + previous byte (within row)
        // Encoded (difference) data:  [100, 10, 10, 10] → decoded [100, 110, 120, 130]
        var data = new byte[] { 100, 10, 10, 10 };
        var compressed = Compress(data);
        var parms = MakeParms(predictor: 2, columns: 4, colors: 1);
        var result = FlateDecodeFilter.Decode(compressed, parms);

        Assert.Equal(4, result.Length);
        Assert.Equal(new byte[] { 100, 110, 120, 130 }, result);
    }

    [Fact]
    public void FlateDecode_PngPredictor_MultipleColors()
    {
        // PNG Sub (predictor=11) with colors=3 (RGB), columns=2, bpc=8
        // rowBytes = 2 * 3 * 8 / 8 = 6, bytesPerPixel = 3
        // Original row: [R0, G0, B0, R1, G1, B1]
        // Sub: delta[i] = orig[i] - orig[i - bytesPerPixel]
        // For col < 3 (bytesPerPixel): delta = original
        // For col >= 3: delta = orig[col] - orig[col-3]
        // Original: [10, 20, 30, 50, 70, 90]
        // Encoded:  [10, 20, 30, 50-10=40, 70-20=50, 90-30=60]
        var pngData = new byte[]
        {
            1, 10, 20, 30, 40, 50, 60,  // Sub filter row
        };

        var compressed = Compress(pngData);
        var parms = MakeParms(predictor: 11, columns: 2, colors: 3);
        var result = FlateDecodeFilter.Decode(compressed, parms);

        Assert.Equal(6, result.Length);
        Assert.Equal(new byte[] { 10, 20, 30, 50, 70, 90 }, result);
    }

    #endregion

    #region ASCII85Decode

    [Fact]
    public void Ascii85_FullGroups_DecodeCorrectly()
    {
        // "Hello" (5 bytes) in ASCII85: 87cURD
        // Actually, "Hello" = 0x48656C6C6F
        // Group1: 0x48656C6C → base85 encode
        // Let's use a known reference: "Man " = 9jqo^
        // Two groups: "Man Man " = 9jqo^9jqo^
        var input = Encoding.ASCII.GetBytes("9jqo^9jqo^~>");
        var result = Ascii85DecodeFilter.Decode(input);
        Assert.Equal("Man Man ", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Ascii85_PartialGroup_2Chars_Produces1Byte()
    {
        // 2-char partial group produces 1 output byte
        // "!!" → values [0,0], padded to [0,0,84,84,84]
        // value = 0 + 0 + 84*7225 + 84*85 + 84 = 614124
        // byte0 = (614124 >> 24) & 0xFF = 0
        var input = Encoding.ASCII.GetBytes("!!~>");
        var result = Ascii85DecodeFilter.Decode(input);
        Assert.Single(result);
        Assert.Equal(0, result[0]);
    }

    [Fact]
    public void Ascii85_PartialGroup_3Chars_Produces2Bytes()
    {
        // 3-char partial group produces 2 output bytes
        var input = Encoding.ASCII.GetBytes("!!!~>");
        var result = Ascii85DecodeFilter.Decode(input);
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void Ascii85_PartialGroup_4Chars_Produces3Bytes()
    {
        // 4-char partial group produces 3 output bytes
        var input = Encoding.ASCII.GetBytes("!!!!~>");
        var result = Ascii85DecodeFilter.Decode(input);
        Assert.Equal(3, result.Length);
    }

    [Fact]
    public void Ascii85_EmptyInput_ReturnsEmpty()
    {
        var input = Encoding.ASCII.GetBytes("~>");
        var result = Ascii85DecodeFilter.Decode(input);
        Assert.Empty(result);
    }

    [Fact]
    public void Ascii85_WhitespaceAndNewlines_AreIgnored()
    {
        // "Man " encoded with whitespace interspersed
        var input = Encoding.ASCII.GetBytes("9 j\nq\to\r^~>");
        var result = Ascii85DecodeFilter.Decode(input);
        Assert.Equal("Man ", Encoding.ASCII.GetString(result));
    }

    #endregion

    #region AsciiHexDecode

    [Fact]
    public void AsciiHex_LowercaseHexDigits()
    {
        var input = Encoding.ASCII.GetBytes("48656c6c6f>");
        var result = AsciiHexDecodeFilter.Decode(input);
        Assert.Equal("Hello", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void AsciiHex_EmptyInput_JustEod()
    {
        var input = Encoding.ASCII.GetBytes(">");
        var result = AsciiHexDecodeFilter.Decode(input);
        Assert.Empty(result);
    }

    [Fact]
    public void AsciiHex_MixedCase()
    {
        var input = Encoding.ASCII.GetBytes("4f6B>");  // 'O' (4F) and 'k' (6B)
        var result = AsciiHexDecodeFilter.Decode(input);
        Assert.Equal(2, result.Length);
        Assert.Equal(0x4F, result[0]);
        Assert.Equal(0x6B, result[1]);
    }

    #endregion

    #region RunLengthDecode

    [Fact]
    public void RunLength_SingleLiteralByte()
    {
        // Length=0 means copy next 1 byte literally
        var input = new byte[] { 0, 0x42, 128 }; // 'B' + EOD
        var result = RunLengthDecodeFilter.Decode(input);
        Assert.Single(result);
        Assert.Equal(0x42, result[0]);
    }

    [Fact]
    public void RunLength_MaxRepeat()
    {
        // Length=129 → repeat next byte (257-129)=128 times (maximum repeat)
        var input = new byte[] { 129, 0xFF, 128 };
        var result = RunLengthDecodeFilter.Decode(input);
        Assert.Equal(128, result.Length);
        Assert.All(result, b => Assert.Equal(0xFF, b));
    }

    [Fact]
    public void RunLength_EmptyInput_JustEod()
    {
        var input = new byte[] { 128 };
        var result = RunLengthDecodeFilter.Decode(input);
        Assert.Empty(result);
    }

    #endregion

    #region StreamFilter Chaining

    [Fact]
    public void StreamFilter_MultipleFilters_Ascii85ThenFlate()
    {
        // Prepare data: original → FlateDecode → ASCII85Encode
        // StreamFilter decodes in order: ASCII85Decode first, then FlateDecode
        var original = Encoding.ASCII.GetBytes("StreamFilter chaining test data.");
        var deflated = Compress(original);
        var ascii85Encoded = Ascii85Encode(deflated);

        var dict = new PdfDictionary();
        var filters = new PdfArray();
        filters.Add(new PdfName("ASCII85Decode"));
        filters.Add(new PdfName("FlateDecode"));
        dict.Set("Filter", filters);

        var result = StreamFilter.Decode(ascii85Encoded, dict);
        Assert.Equal(original, result);
    }

    [Fact]
    public void StreamFilter_FilterWithDecodeParmsArray()
    {
        // FlateDecode with predictor params in a PdfArray of DecodeParms
        // Single filter as array, single parms as array
        var columns = 3;
        var colors = 1;
        // TIFF predictor data: [100, 10, 10] → decoded [100, 110, 120]
        var rawData = new byte[] { 100, 10, 10 };
        var compressed = Compress(rawData);

        var dict = new PdfDictionary();
        var filters = new PdfArray();
        filters.Add(new PdfName("FlateDecode"));
        dict.Set("Filter", filters);

        var parmsDict = new PdfDictionary();
        parmsDict.Set("Predictor", new PdfInteger(2));
        parmsDict.Set("Columns", new PdfInteger(columns));
        parmsDict.Set("Colors", new PdfInteger(colors));

        var parmsArray = new PdfArray();
        parmsArray.Add(parmsDict);
        dict.Set("DecodeParms", parmsArray);

        var result = StreamFilter.Decode(compressed, dict);
        Assert.Equal(new byte[] { 100, 110, 120 }, result);
    }

    [Theory]
    [InlineData("Fl", "FlateDecode")]
    [InlineData("A85", "ASCII85Decode")]
    [InlineData("AHx", "ASCIIHexDecode")]
    [InlineData("RL", "RunLengthDecode")]
    public void StreamFilter_ShortFilterNames(string shortName, string longName)
    {
        // Verify short names produce the same result as long names
        byte[] testData;
        switch (longName)
        {
            case "FlateDecode":
                testData = Compress(Encoding.ASCII.GetBytes("test"));
                break;
            case "ASCII85Decode":
                testData = Encoding.ASCII.GetBytes("9jqo^~>");
                break;
            case "ASCIIHexDecode":
                testData = Encoding.ASCII.GetBytes("48656C6C6F>");
                break;
            case "RunLengthDecode":
                testData = new byte[] { 2, 0x41, 0x42, 0x43, 128 };
                break;
            default:
                testData = new byte[] { 1, 2, 3 };
                break;
        }

        var dictShort = new PdfDictionary();
        dictShort.Set("Filter", new PdfName(shortName));
        var resultShort = StreamFilter.Decode(testData, dictShort);

        var dictLong = new PdfDictionary();
        dictLong.Set("Filter", new PdfName(longName));
        var resultLong = StreamFilter.Decode(testData, dictLong);

        Assert.Equal(resultLong, resultShort);
    }

    #endregion

    #region Helpers

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(data);
        }
        return ms.ToArray();
    }

    private static PdfDictionary MakeParms(int predictor, int columns, int colors, int bpc = 8)
    {
        var parms = new PdfDictionary();
        parms.Set("Predictor", new PdfInteger(predictor));
        parms.Set("Columns", new PdfInteger(columns));
        parms.Set("Colors", new PdfInteger(colors));
        parms.Set("BitsPerComponent", new PdfInteger(bpc));
        return parms;
    }

    /// <summary>
    /// Minimal ASCII85 encoder for test purposes.
    /// </summary>
    private static byte[] Ascii85Encode(byte[] data)
    {
        var sb = new StringBuilder();
        var i = 0;

        while (i + 4 <= data.Length)
        {
            uint value = (uint)data[i] << 24 | (uint)data[i + 1] << 16 |
                         (uint)data[i + 2] << 8 | data[i + 3];
            i += 4;

            if (value == 0)
            {
                sb.Append('z');
                continue;
            }

            var chars = new char[5];
            for (var j = 4; j >= 0; j--)
            {
                chars[j] = (char)('!' + (int)(value % 85));
                value /= 85;
            }
            sb.Append(chars);
        }

        // Handle remaining bytes (1-3)
        var remaining = data.Length - i;
        if (remaining > 0)
        {
            uint value = 0;
            for (var j = 0; j < remaining; j++)
                value |= (uint)data[i + j] << (24 - j * 8);

            var chars = new char[remaining + 1];
            var tmp = value;
            var all = new char[5];
            for (var j = 4; j >= 0; j--)
            {
                all[j] = (char)('!' + (int)(tmp % 85));
                tmp /= 85;
            }
            Array.Copy(all, chars, remaining + 1);
            sb.Append(chars);
        }

        sb.Append("~>");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    #endregion
}
