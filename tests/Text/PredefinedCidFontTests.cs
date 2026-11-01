using System.IO;
using System.Linq;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>
/// A predefined CJK face named by FontRepository.FindFont: declared on the page as a Type0 font
/// over a CIDFontType0 with no program, its text written in the codes of a predefined CMap and
/// measured by the CID widths those codes select.
/// </summary>
public class PredefinedCidFontTests
{
    [Theory]
    [InlineData("HeiseiMin-W3", "UniJIS-UCS2-H", "Japan1")]
    [InlineData("HeiseiKakuGo-W5", "UniJIS-UCS2-H", "Japan1")]
    [InlineData("STSong-Light", "UniGB-UCS2-H", "GB1")]
    [InlineData("MSung-Light", "UniCNS-UCS2-H", "CNS1")]
    [InlineData("HYSMyeongJo-Medium", "UniKS-UCS2-H", "Korea1")]
    [InlineData("HYGothic-Medium", "UniKS-UCS2-H", "Korea1")]
    [InlineData("HeiseiMin-W3-90ms-RKSJ-H", "90ms-RKSJ-H", "Japan1")]
    [InlineData("HeiseiMin-W3-UniJIS-UCS2-H", "UniJIS-UCS2-H", "Japan1")]
    public void Face_AddedToResources_IsDeclaredAsType0OverAnUnembeddedCidFont(string name, string cmap, string ordering)
    {
        var raw = Draw(name, "Ab");

        Assert.Contains("/Subtype /Type0", raw);
        Assert.Contains("/Encoding /" + cmap, raw);
        Assert.Contains("/Subtype /CIDFontType0", raw);
        Assert.Contains("(" + ordering + ")", raw);
        Assert.DoesNotContain("/FontFile", raw);
    }

    [Fact]
    public void UnicodeFace_Text_IsWrittenAsUcs2AndReadsBack()
    {
        var bytes = DrawBytes("HeiseiMin-W3", "日本語 Ab");
        var raw = Latin1(bytes);

        Assert.Contains("<65E5672C8A9E002000410062>", raw);
        using var reloaded = new Document(new MemoryStream(bytes));
        var absorber = new TextAbsorber();
        reloaded.Pages[1].Accept(absorber);
        Assert.Contains("日本語 Ab", absorber.Text);
    }

    [Fact]
    public void ShiftJisFace_Text_IsWrittenInShiftJis()
    {
        var raw = Draw("HeiseiMin-W3-90ms-RKSJ-H", "A日ｱ");

        // 'A' and the half-width katakana take a byte, the kanji two.
        Assert.Contains("<4193FAB1>", raw);
    }

    [Theory]
    [InlineData("HeiseiMin-W3", "A", 722)]
    [InlineData("HeiseiMin-W3", " ", 250)]
    [InlineData("HeiseiMin-W3", "~", 333)]
    [InlineData("HeiseiMin-W3", "日", 1000)]
    [InlineData("HeiseiKakuGo-W5", "A", 727)]
    [InlineData("STSong-Light", "A", 684)]
    [InlineData("HYGothic-Medium", "A", 500)]
    [InlineData("HYGothic-Medium", "한", 1000)]
    [InlineData("HeiseiMin-W3-90ms-RKSJ-H", "日", 1000)]
    public void Face_Text_MeasuresByTheWidthsOfItsCids(string name, string text, int thousandths)
    {
        var font = FontRepository.FindFont(name);

        Assert.Equal(thousandths, font.MeasureString(text, 1000), 3);
    }

    [Fact]
    public void Face_AddedTwice_IsDeclaredOnce()
    {
        using var document = new Document();
        var page = document.Pages.Add();
        var font = FontRepository.FindFont("STSong-Light");

        page.Resources.Fonts.Add(font, out var first);
        page.Resources.Fonts.Add(font, out var second);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("MHei-Medium")]
    [InlineData("HeiseiMin-W3-EUC-H")]
    [InlineData("STSong-Light-90ms-RKSJ-H")]
    public void OtherName_IsNotFound(string name)
    {
        Assert.Throws<FontNotFoundException>(() => FontRepository.FindFont(name));
    }

    private static string Draw(string face, string text) => Latin1(DrawBytes(face, text));

    private static string Latin1(byte[] bytes) =>
        System.Text.Encoding.GetEncoding(28591).GetString(bytes).Replace("/Subtype/", "/Subtype /").Replace("/Encoding/", "/Encoding /");

    private static byte[] DrawBytes(string face, string text)
    {
        using var document = new Document();
        var page = document.Pages.Add();
        var font = FontRepository.FindFont(face);
        page.Resources.Fonts.Add(font, out var name);

        page.Contents.Add(new BT());
        page.Contents.Add(new SelectFont(name, 12));
        page.Contents.Add(new SetTextMatrix(1, 0, 0, 1, 72, 700));
        page.Contents.Add(new ShowText(text, font));
        page.Contents.Add(new ET());

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
}
