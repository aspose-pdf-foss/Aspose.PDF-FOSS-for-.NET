using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>
/// A font program with PostScript (CFF) outlines is embedded as what it is: a CIDFontType0
/// descendant carrying a /FontFile3 of subtype /OpenType. Embedded as a TrueType program
/// would be - /FontFile2 under a CIDFontType2 - the descendant font says one thing and the
/// program in it another, and a reader that checks reports the mismatch.
/// </summary>
public class OpenTypeCffEmbedTests
{
    [Fact]
    public void AnOpenTypeCffProgram_EmbedsAsACidFontType0WithAnOpenTypeFontFile()
    {
        var descriptor = EmbedAndReadDescriptor(OpenTypeCffFrom(TrueTypeProgram()), out var descendant);

        Assert.Equal("CIDFontType0", descendant.GetName("Subtype"));
        // /CIDToGIDMap belongs to a CIDFontType2; a CFF program maps its own CIDs.
        Assert.False(descendant.ContainsKey("CIDToGIDMap"));
        Assert.False(descriptor.ContainsKey("FontFile2"));
        var program = descriptor.Get("FontFile3") as PdfStream;
        Assert.NotNull(program);
        Assert.Equal("OpenType", program!.Dict.GetName("Subtype"));
        // /Length1 is the length of a Type1 or TrueType program, which this is not.
        Assert.False(program.Dict.ContainsKey("Length1"));
        Assert.Equal("OTTO", Encoding.ASCII.GetString(program.RawData, 0, 4));
    }

    [Fact]
    public void ATrueTypeProgram_StillEmbedsAsACidFontType2()
    {
        var descriptor = EmbedAndReadDescriptor(TrueTypeProgram(), out var descendant);

        Assert.Equal("CIDFontType2", descendant.GetName("Subtype"));
        Assert.Equal("Identity", descendant.GetName("CIDToGIDMap"));
        Assert.False(descriptor.ContainsKey("FontFile3"));
        var program = descriptor.Get("FontFile2") as PdfStream;
        Assert.NotNull(program);
        Assert.True(program!.Dict.ContainsKey("Length1"));
    }

    /// <summary>Embed <paramref name="program"/> into a fresh font resource dictionary and
    /// hand back the descriptor of the descendant font it built.</summary>
    private static PdfDictionary EmbedAndReadDescriptor(byte[] program, out PdfDictionary descendant)
    {
        var fonts = new PdfDictionary();
        var (resName, _) = Type0FontEmbedder.Embed(fonts, program, "Demo Face", "A");
        var type0 = (PdfDictionary)fonts.Get(resName)!;
        Assert.Equal("Type0", type0.GetName("Subtype"));
        descendant = (PdfDictionary)((PdfArray)type0.Get("DescendantFonts")!)[0]!;
        return (PdfDictionary)descendant.Get("FontDescriptor")!;
    }

    /// <summary>A TrueType program the library carries itself, so the test needs no font on the box.</summary>
    private static byte[] TrueTypeProgram()
    {
        var program = FontRepository.DjVuDingbatsProgram;
        Assert.NotNull(program);
        return program!;
    }

    /// <summary>The same face as an sfnt with PostScript outlines: the tables that hold
    /// TrueType outlines ('glyf' and its 'loca' index) go, a 'CFF ' table takes their place,
    /// and the container opens with 'OTTO' — which is how the two are told apart. The
    /// metrics tables are untouched, so everything the embedder reads is still there.</summary>
    private static byte[] OpenTypeCffFrom(byte[] trueType)
    {
        var tables = new List<(string Tag, byte[] Data)>();
        var count = (trueType[4] << 8) | trueType[5];
        for (var i = 0; i < count; i++)
        {
            var entry = 12 + i * 16;
            var tag = Encoding.ASCII.GetString(trueType, entry, 4);
            if (tag is "glyf" or "loca") continue;
            var offset = (int)ReadUInt32(trueType, entry + 8);
            var length = (int)ReadUInt32(trueType, entry + 12);
            var data = new byte[length];
            Array.Copy(trueType, offset, data, 0, length);
            tables.Add((tag, data));
        }
        // A CFF header: major 1, minor 0, header size 4, one-byte offsets.
        tables.Add(("CFF ", new byte[] { 1, 0, 4, 1 }));
        tables.Sort((left, right) => string.CompareOrdinal(left.Tag, right.Tag));

        var directory = 12 + tables.Count * 16;
        var total = directory + tables.Sum(one => (one.Data.Length + 3) & ~3);
        var sfnt = new byte[total];
        Encoding.ASCII.GetBytes("OTTO").CopyTo(sfnt, 0);
        WriteUInt16(sfnt, 4, tables.Count);
        var at = directory;
        for (var i = 0; i < tables.Count; i++)
        {
            var entry = 12 + i * 16;
            Encoding.ASCII.GetBytes(tables[i].Tag).CopyTo(sfnt, entry);
            WriteUInt32(sfnt, entry + 8, (uint)at);
            WriteUInt32(sfnt, entry + 12, (uint)tables[i].Data.Length);
            tables[i].Data.CopyTo(sfnt, at);
            at += (tables[i].Data.Length + 3) & ~3;
        }
        return sfnt;
    }

    private static uint ReadUInt32(byte[] data, int at) =>
        (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);

    private static void WriteUInt16(byte[] data, int at, int value)
    {
        data[at] = (byte)(value >> 8);
        data[at + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] data, int at, uint value)
    {
        data[at] = (byte)(value >> 24);
        data[at + 1] = (byte)(value >> 16);
        data[at + 2] = (byte)(value >> 8);
        data[at + 3] = (byte)value;
    }
}
