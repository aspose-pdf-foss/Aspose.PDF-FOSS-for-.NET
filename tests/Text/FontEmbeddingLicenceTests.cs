using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>What a face's OS/2 type flags allow: installable, editable and preview-and-print
/// faces all go into a document that is viewed and printed; only a restricted licence
/// keeps a program out. A table too old to carry the flags says nothing.</summary>
public sealed class FontEmbeddingLicenceTests
{
    private const int FsTypeOffset = 8;
    private const int Installable = 0;
    private const int Restricted = 2;
    private const int PreviewAndPrint = 4;
    private const int Editable = 8;

    [Theory]
    [InlineData(Installable, false)]
    [InlineData(Restricted, true)]
    [InlineData(PreviewAndPrint, false)]
    [InlineData(Editable, false)]
    [InlineData(PreviewAndPrint | Editable, false)]
    public void OnlyARestrictedLicenceForbidsEmbedding(int fsType, bool forbidden)
    {
        var program = PdfBuilder.BuildMinimalTrueTypeFont();
        WriteUInt16(program, Os2Offset(program) + FsTypeOffset, fsType);
        Assert.Equal(forbidden, FontData.EmbeddingForbiddenByLicence(program));
    }

    [Fact]
    public void AnOldTypeFlagsTableIsNotALicence()
    {
        var program = PdfBuilder.BuildMinimalTrueTypeFont();
        var os2 = Os2Offset(program);
        WriteUInt16(program, os2, 1);
        WriteUInt16(program, os2 + FsTypeOffset, Restricted);
        Assert.False(FontData.EmbeddingForbiddenByLicence(program));
    }

    private static int Os2Offset(byte[] program)
    {
        var numTables = program[4] << 8 | program[5];
        for (var i = 0; i < numTables; i++)
        {
            var entry = 12 + i * 16;
            if (System.Text.Encoding.ASCII.GetString(program, entry, 4) != "OS/2") continue;
            return program[entry + 8] << 24 | program[entry + 9] << 16 | program[entry + 10] << 8 | program[entry + 11];
        }
        throw new Xunit.Sdk.XunitException("the test face has no OS/2 table");
    }

    private static void WriteUInt16(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }
}
