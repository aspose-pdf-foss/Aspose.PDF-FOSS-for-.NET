using Aspose.Pdf.IO.Filters.Brotli;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class BrotliDecoderTests
{
    // Streams written by an independent encoder (the Brotli library the .NET runtime ships),
    // so these assert agreement with a shipping implementation. All three hold the same text:
    // a sentence repeated eight times, 1,104 bytes.
    private const string SmallestSize =
        "G08E4BwJNq5xHTuG5YBjIBLgg9Ugczsu400nBw7fa14tyAIJNE0Y0htel0QtPMRSqHqNERDb50BGvWyqOphRmhnraTTdMpzTdF4+KlptcZG58iSwguuTHrAZ";

    private const string Optimal =
        "G08EAMSwbaneOiMUkojkF1xhAw7cBp1s5gxmGGWPsfMkwyXGWVshvjo2gO7LM6NeXvTXtB5Y7mr/wNiP5P/Al0ZEZukow8TT0B0oZTuzAxP1AmV/2edubiSQW4BpuTsgK2GeYX+psizZ7WiXqeq/v181";

    private const string Fastest =
        "iycCAICqqqrq/3RluBvY4eInczAHdwNwdwN3UAc7q7uJmoq7qYqCqJqZu4NDboHb8SIYBiLBwZgzZNgwnnBb+PXBU2WLcPLFewkpQ1ZSFE+Y7f+HUaYa5nTA0N/PLThjaDrTXY9womn6F9HIcdqBoxMNtrDECl42WkkrcEaz7x8GiSTNBBtHFE+s8BIo1zA378s=";

    private static string Expected() => string.Concat(System.Linq.Enumerable.Repeat(
        "The Quick brown fox jumps over the lazy dog. THE WORLD is WAITING for the Morning; information, however, is ABOUT people and their homes. ", 8));

    [Theory]
    [InlineData(SmallestSize)]
    [InlineData(Optimal)]
    [InlineData(Fastest)]
    public void Decode_MatchesTheIndependentEncoder(string packed)
    {
        var decoded = BrotliDecoder.Decode(System.Convert.FromBase64String(packed));
        Assert.Equal(Expected(), System.Text.Encoding.UTF8.GetString(decoded));
    }

    [Fact]
    public void Decode_EmptyStream()
    {
        Assert.Empty(BrotliDecoder.Decode(new byte[] { 0x3B }));
    }

    [Fact]
    public void Decode_NonZeroPaddingAfterTheLastBlock_IsAFault()
    {
        var fault = Assert.Throws<BrotliDecodeException>(() => BrotliDecoder.Decode(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3 }));
        Assert.Equal(BrotliErrors.CorruptedPaddingBits, fault.Code);
    }

    [Fact]
    public void Decode_TruncatedStream_IsAFault()
    {
        var packed = System.Convert.FromBase64String(Optimal);
        Assert.Throws<BrotliDecodeException>(() => BrotliDecoder.Decode(packed[..(packed.Length / 2)]));
    }

    [Fact]
    public void Dictionary_IsTheStandardOne()
    {
        var data = BrotliDictionary.Data;
        Assert.Equal(122784, data.Length);
        Assert.Equal("timedownlifeleftback", System.Text.Encoding.ASCII.GetString(data, 0, 20));
    }

    [Fact]
    public void Dictionary_RefusesDataOfTheWrongShape()
    {
        Assert.False(BrotliDictionary.Set(new byte[10], new int[25]));
        Assert.Equal(122784, BrotliDictionary.Data.Length);
    }

    [Fact]
    public void StreamFilter_DecodesBrotliStreams()
    {
        var dict = new Aspose.Pdf.Core.PdfDictionary();
        dict.Set("Filter", new Aspose.Pdf.Core.PdfName("BrotliDecode"));
        var decoded = Aspose.Pdf.IO.Filters.StreamFilter.Decode(System.Convert.FromBase64String(SmallestSize), dict);
        Assert.Equal(Expected(), System.Text.Encoding.UTF8.GetString(decoded));
    }
}
