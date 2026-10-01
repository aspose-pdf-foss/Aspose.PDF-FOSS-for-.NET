using System.Linq;
using System.Text;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class ManagedInflaterProgressTests
{
    private static readonly byte[] Text = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 40).Select(i => "row " + i + " of the progress test; ")));

    [Fact]
    public void Inflate_WholeZlibStream_StopsAfterTheCheckValue()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        var withTail = zlib.Concat(new byte[] { 9, 9, 9 }).ToArray();
        var progress = ManagedInflater.Inflate(withTail, withTail.Length, true, null);
        Assert.Equal(InflateState.Done, progress.State);
        Assert.Equal(zlib.Length, progress.Consumed);
        Assert.Equal(Text, progress.Output);
    }

    [Fact]
    public void Inflate_CutShort_NeedsInputAndKeepsWhatItDecoded()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        var progress = ManagedInflater.Inflate(zlib, zlib.Length / 2, true, null);
        Assert.Equal(InflateState.NeedsInput, progress.State);
        Assert.Equal(zlib.Length / 2, progress.Consumed);
        Assert.Equal(Text.Take(progress.Output.Length), progress.Output);

        var noTrailer = ManagedInflater.Inflate(zlib, zlib.Length - 2, true, null);
        Assert.Equal(InflateState.NeedsInput, noTrailer.State);
        Assert.Equal(Text, noTrailer.Output);
    }

    [Fact]
    public void Inflate_RawStream_EndsAtTheFinalBlock()
    {
        var raw = ManagedDeflater.DeflateRaw(Text, 0, Text.Length);
        var progress = ManagedInflater.Inflate(raw, raw.Length, false, null);
        Assert.Equal(InflateState.Done, progress.State);
        Assert.Equal(raw.Length, progress.Consumed);
        Assert.Equal(Text, progress.Output);
    }

    [Theory]
    [InlineData(new byte[] { 0x12, 0x34 }, (int)InflateFault.UnknownCompressionMethod, 1, "unknown compression method")]
    [InlineData(new byte[] { 0x88, 0x00 }, (int)InflateFault.InvalidWindowSize, 1, "invalid window size")]
    [InlineData(new byte[] { 0x78, 0x00 }, (int)InflateFault.IncorrectHeaderCheck, 2, "incorrect header check")]
    [InlineData(new byte[] { 0x78, 0x9C, 0xFF, 0xFF }, (int)InflateFault.InvalidBlockType, 3, "invalid block type")]
    public void Inflate_Malformed_ReportsZlibFaults(byte[] data, int fault, int consumed, string message)
    {
        var progress = ManagedInflater.Inflate(data, data.Length, true, null);
        Assert.Equal(InflateState.Failed, progress.State);
        Assert.Equal((InflateFault)fault, progress.Fault);
        Assert.Equal(consumed, progress.Consumed);
        Assert.Equal(message, progress.Message);
    }

    [Fact]
    public void Inflate_WrongCheckValue_IsADataCheckFault()
    {
        var zlib = ManagedDeflater.DeflateZlib(Text);
        zlib[^1] ^= 0xFF;
        var progress = ManagedInflater.Inflate(zlib, zlib.Length, true, null);
        Assert.Equal(InflateFault.IncorrectDataCheck, progress.Fault);
        Assert.Equal("incorrect data check", progress.Message);
    }

    [Fact]
    public void Inflate_PresetDictionary_ReferencesReachIntoIt()
    {
        // One fixed block: length 3 at distance 1, then end of block -- a copy of the last byte
        // before the data, which only a primed window has.
        var dictionary = Encoding.ASCII.GetBytes("abc");
        var id = ManagedDeflater.Adler32(dictionary, 0, dictionary.Length);
        var header = new byte[] { 0x78, 0x20, (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id };
        header[1] = (byte)(header[1] + 31 - (0x78 * 256 + header[1]) % 31);
        var expected = Encoding.ASCII.GetBytes("ccc");
        var check = ManagedDeflater.Adler32(expected, 0, expected.Length);
        var zlib = header.Concat(new byte[] { 0x03, 0x02, 0x00 }).Concat(new[] { (byte)(check >> 24), (byte)(check >> 16), (byte)(check >> 8), (byte)check }).ToArray();

        var given = ManagedInflater.Inflate(zlib, zlib.Length, true, dictionary);
        Assert.Equal(InflateState.Done, given.State);
        Assert.Equal(expected, given.Output);
        Assert.Equal(zlib.Length, given.Consumed);
    }

    [Fact]
    public void Inflate_PresetDictionary_AskedForThenPrimesTheWindow()
    {
        var dictionary = Encoding.ASCII.GetBytes("of the progress test; ");
        // Deflating the dictionary and the text together, the text refers back into the dictionary.
        var raw = ManagedDeflater.DeflateRaw(dictionary.Concat(Text).ToArray(), 0, dictionary.Length + Text.Length);
        var inflatedWithPrefix = ManagedInflater.Inflate(raw, raw.Length, false, null).Output;
        Assert.Equal(dictionary.Concat(Text), inflatedWithPrefix);

        var id = ManagedDeflater.Adler32(dictionary, 0, dictionary.Length);
        var header = new byte[] { 0x78, 0x20, (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id };
        header[1] = (byte)(header[1] + 31 - (0x78 * 256 + header[1]) % 31);
        var body = ManagedDeflater.DeflateRaw(Text, 0, Text.Length);
        var check = ManagedDeflater.Adler32(Text, 0, Text.Length);
        var zlib = header.Concat(body).Concat(new[] { (byte)(check >> 24), (byte)(check >> 16), (byte)(check >> 8), (byte)check }).ToArray();

        var asked = ManagedInflater.Inflate(zlib, zlib.Length, true, null);
        Assert.Equal(InflateState.NeedsDictionary, asked.State);
        Assert.Equal(id, asked.DictionaryId);
        Assert.Equal(6, asked.Consumed);

        var given = ManagedInflater.Inflate(zlib, zlib.Length, true, dictionary);
        Assert.Equal(InflateState.Done, given.State);
        Assert.Equal(Text, given.Output);
    }
}
