using System.IO;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Fonts;

/// <summary>A Type 1 font program whose Private dictionary states <c>/lenIV -1</c> keeps its charstrings and
/// subroutines unencrypted (Adobe Type 1 Font Format): they are read as they stand, so a glyph keeps the width its
/// own hsbw sets, as in the same font with encrypted charstrings.</summary>
public sealed class Type1UnencryptedCharStringTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "type1-leniv-" + Guid.NewGuid().ToString("N"));

    public Type1UnencryptedCharStringTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The width of 'A' at 1000 units in the font written with <paramref name="lenIV"/>, its glyph drawn by
    /// a subroutine when <paramref name="subrsCount"/> is given.</summary>
    private double WidthOfA(int lenIV, string? subrsCount)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".pfb");
        File.WriteAllBytes(path, Type1FontBuilder.Pfb(subrsCount, lenIV));
        return FontRepository.OpenFont(path).MeasureString("A", 1000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1")] // the glyph's hsbw sits in a subroutine, read with the same lenIV
    public void UnencryptedCharStringsKeepTheirWidthAsEncryptedOnesDo(string? subrsCount)
    {
        Assert.Equal(Type1FontBuilder.AWidth, WidthOfA(Type1FontBuilder.DefaultLenIV, subrsCount));

        Assert.Equal(Type1FontBuilder.AWidth, WidthOfA(-1, subrsCount));
    }
}
