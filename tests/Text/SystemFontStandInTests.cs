using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class SystemFontStandInTests
{
    // A face that is not one of the Standard-14 names, on each platform the tests run on.
    private static readonly string[] NonStandardFaces =
    {
        @"C:\Windows\Fonts\verdana.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/System/Library/Fonts/Geneva.ttf",
    };

    [Fact]
    public void OpenedFont_NoInstalledFaceAnswersTo_MeasuresEachGlyphByItsOwnAdvance()
    {
        var path = NonStandardFaces.FirstOrDefault(File.Exists);
        if (path is null) return; // Skip where none of the faces is installed

        // A face opened from a file has no /Widths. An installed face lent it its advances by
        // name; one nothing installed answers to - Liberation Sans on Linux, whose file is not
        // named after it - measured every glyph as 1 em. Renamed here (one letter, same length)
        // so no installed face answers to it on any platform.
        var bytes = File.ReadAllBytes(path);
        var name = FontRepository.OpenFont(path).FontName;
        var token = name[..Math.Min(5, name.Length)];
        Renamed(bytes, System.Text.Encoding.ASCII.GetBytes(token));
        Renamed(bytes, System.Text.Encoding.BigEndianUnicode.GetBytes(token));
        var font = FontRepository.OpenFont(new MemoryStream(bytes), FontTypes.TTF);
        Assert.StartsWith("Q", font.FontName);

        var narrow = font.MeasureString("i", 10f);
        var wide = font.MeasureString("W", 10f);
        Assert.True(narrow < wide, $"{path}: 'i' {narrow} is not narrower than 'W' {wide}");
        Assert.True(wide < 10.0, $"{path}: 'W' {wide} is a whole em or more");
    }

    /// <summary>Replaces the first letter of every occurrence of <paramref name="token"/> with Q:
    /// an ASCII name's first byte, a UTF-16BE name's second (its first is the zero high byte).</summary>
    private static void Renamed(byte[] bytes, byte[] token)
    {
        var letter = token[0] == 0 ? 1 : 0;
        for (var at = 0; at + token.Length <= bytes.Length; at++)
        {
            if (bytes.AsSpan(at, token.Length).SequenceEqual(token))
                bytes[at + letter] = (byte)'Q';
        }
    }

    [Theory]
    [InlineData("Calibri", false, false, "Carlito-Regular.ttf")]
    [InlineData("Calibri", true, true, "Carlito-BoldItalic.ttf")]
    [InlineData("Cambria", true, false, "Caladea-Bold.ttf")]
    [InlineData("Verdana", false, true, "DejaVuSans-Oblique.ttf")]
    [InlineData("Comic Sans MS", false, false, "ComicNeue-Regular.otf")]
    [InlineData("Arial", false, false, "LiberationSans-Regular.ttf")]
    [InlineData("Times New Roman", true, false, "LiberationSerif-Bold.ttf")]
    [InlineData("Courier New", false, true, "LiberationMono-Italic.ttf")]
    public void WindowsFamily_HasItsLinuxStandIn(string family, bool bold, bool italic, string expected)
    {
        Assert.Contains(expected, SystemFontResolver.LinuxEquivalents(family, bold, italic));
    }

    [Fact]
    public void Helvetica_FallsBackToDejaVuAfterLiberation()
    {
        Assert.Equal(new[] { "LiberationSans-Bold.ttf", "DejaVuSans-Bold.ttf" },
            SystemFontResolver.LinuxEquivalents("Helvetica", true, false));
    }
}
