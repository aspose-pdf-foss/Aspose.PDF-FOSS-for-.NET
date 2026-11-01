using System.IO;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Fonts;

/// <summary>A Type 1 font program's integers are read as the numbers they are whatever the culture of the machine: a
/// Subrs array whose count is written with a '+', as PostScript allows, keeps its subroutines, so a glyph drawn by
/// one keeps the width that subroutine sets.</summary>
public sealed class Type1SubroutineCultureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "type1-subrs-" + Guid.NewGuid().ToString("N"));

    public Type1SubroutineCultureTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The width of 'A' at 1000 units in the font, its program written with the Subrs count
    /// <paramref name="subrsCount"/> and opened under <paramref name="culture"/>.</summary>
    private double WidthOfA(string subrsCount, string culture)
    {
        using var scope = new CultureScope(culture);
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".pfb");
        File.WriteAllBytes(path, Type1FontBuilder.Pfb(subrsCount));
        return FontRepository.OpenFont(path).MeasureString("A", 1000);
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the sign, which a culture parse will not take for '+'
    [InlineData("he-IL")] // a left-to-right mark before the sign
    [InlineData(CultureScope.SyntheticName)] // both at once, on every runtime
    public void ASubrsCountWithAPlusSignKeepsTheSubroutinesAsUnderTheInvariantCulture(string culture)
    {
        Assert.Equal(Type1FontBuilder.AWidth, WidthOfA("1", string.Empty)); // the subroutine's hsbw sets the width
        Assert.Equal(Type1FontBuilder.AWidth, WidthOfA("+1", string.Empty));

        Assert.Equal(Type1FontBuilder.AWidth, WidthOfA("+1", culture));
    }
}
