using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>The name a host face found by its file name reports: a styled face keeps its style, as the
/// files macOS ships are named after the face ("Times New Roman Italic.ttf"), so the styled member of
/// a family is told apart from the regular one.</summary>
public class SystemFontSourceFaceNameTests
{
    [Theory]
    [InlineData("Times", "Times")]
    [InlineData("Times Italic", "Times Italic")]
    [InlineData("Times Bold Italic", "Times Bold Italic")]
    public void AFaceFoundByItsFileNameReportsItsStyle(string query, string expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "foss-facename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Times.ttf"), TrueTypeFaceBuilder.Named("Times", "Regular", 0, 0x40));
            File.WriteAllBytes(Path.Combine(dir, "Times Italic.ttf"), TrueTypeFaceBuilder.Named("Times", "Italic", 2, 0x01));
            File.WriteAllBytes(Path.Combine(dir, "Times Bold Italic.ttf"), TrueTypeFaceBuilder.Named("Times", "Bold Italic", 3, 0x21));

            var found = SystemFontSource.SearchDir(dir, query, query.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

            Assert.NotNull(found);
            Assert.Equal(expected, found!.FontName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Arial", "Regular", "Arial")]
    [InlineData("Arial", "Normal", "Arial")]
    [InlineData("Arial", null, "Arial")]
    [InlineData("Arial", "Bold", "Arial Bold")]
    [InlineData("Segoe UI Semibold", "Semibold", "Segoe UI Semibold")]
    public void TheFaceNameAddsAStyleTheFamilyDoesNotCarry(string family, string? subfamily, string expected)
        => Assert.Equal(expected, SystemFontSource.FaceName(family, subfamily));
}
