using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class FontMetricsTests
{
    [Fact]
    public void Standard14_Helvetica_IsRecognized()
    {
        Assert.True(Standard14Fonts.IsStandard14("Helvetica"));
        Assert.True(Standard14Fonts.IsStandard14("Helvetica-Bold"));
        Assert.True(Standard14Fonts.IsStandard14("Helvetica-Oblique"));
        Assert.True(Standard14Fonts.IsStandard14("Helvetica-BoldOblique"));
    }

    [Fact]
    public void Standard14_TimesRoman_IsRecognized()
    {
        Assert.True(Standard14Fonts.IsStandard14("Times-Roman"));
        Assert.True(Standard14Fonts.IsStandard14("Times-Bold"));
        Assert.True(Standard14Fonts.IsStandard14("Times-Italic"));
        Assert.True(Standard14Fonts.IsStandard14("Times-BoldItalic"));
    }

    [Fact]
    public void Standard14_Courier_IsRecognized()
    {
        Assert.True(Standard14Fonts.IsStandard14("Courier"));
        Assert.True(Standard14Fonts.IsStandard14("Courier-Bold"));
        Assert.True(Standard14Fonts.IsStandard14("Courier-Oblique"));
        Assert.True(Standard14Fonts.IsStandard14("Courier-BoldOblique"));
    }

    [Fact]
    public void Standard14_Symbol_IsRecognized()
    {
        Assert.True(Standard14Fonts.IsStandard14("Symbol"));
        Assert.True(Standard14Fonts.IsStandard14("ZapfDingbats"));
    }

    [Fact]
    public void Standard14_NonStandard_IsNotRecognized()
    {
        Assert.False(Standard14Fonts.IsStandard14("ArialBlack"));
        Assert.False(Standard14Fonts.IsStandard14("Verdana"));
        Assert.False(Standard14Fonts.IsStandard14(""));
    }

    [Fact]
    public void Standard14_Aliases_AreRecognized()
    {
        Assert.True(Standard14Fonts.IsStandard14("ArialMT"));
        Assert.True(Standard14Fonts.IsStandard14("Arial"));
        Assert.True(Standard14Fonts.IsStandard14("TimesNewRomanPSMT"));
        Assert.True(Standard14Fonts.IsStandard14("CourierNewPSMT"));
    }

    [Fact]
    public void Helvetica_SpaceWidth_Is278()
    {
        Assert.Equal(278, Standard14Fonts.GetWidth("Helvetica", 32));
    }

    [Fact]
    public void Helvetica_UppercaseA_Is667()
    {
        Assert.Equal(667, Standard14Fonts.GetWidth("Helvetica", 65));
    }

    [Fact]
    public void Helvetica_LowercaseA_Is556()
    {
        Assert.Equal(556, Standard14Fonts.GetWidth("Helvetica", 97));
    }

    [Fact]
    public void Helvetica_Zero_Is556()
    {
        Assert.Equal(556, Standard14Fonts.GetWidth("Helvetica", 48));
    }

    [Fact]
    public void Helvetica_M_Is833()
    {
        Assert.Equal(833, Standard14Fonts.GetWidth("Helvetica", 77));
    }

    [Fact]
    public void TimesRoman_SpaceWidth_Is250()
    {
        Assert.Equal(250, Standard14Fonts.GetWidth("Times-Roman", 32));
    }

    [Fact]
    public void TimesRoman_UppercaseA_Is722()
    {
        Assert.Equal(722, Standard14Fonts.GetWidth("Times-Roman", 65));
    }

    [Fact]
    public void TimesRoman_LowercaseA_Is444()
    {
        Assert.Equal(444, Standard14Fonts.GetWidth("Times-Roman", 97));
    }

    [Fact]
    public void TimesRoman_Zero_Is500()
    {
        Assert.Equal(500, Standard14Fonts.GetWidth("Times-Roman", 48));
    }

    [Fact]
    public void TimesRoman_M_Is889()
    {
        Assert.Equal(889, Standard14Fonts.GetWidth("Times-Roman", 77));
    }

    [Fact]
    public void Courier_AllWidths_Are600()
    {
        for (int code = 32; code < 127; code++)
        {
            Assert.Equal(600, Standard14Fonts.GetWidth("Courier", code));
        }
    }

    [Fact]
    public void Courier_Bold_AllWidths_Are600()
    {
        for (int code = 32; code < 127; code++)
        {
            Assert.Equal(600, Standard14Fonts.GetWidth("Courier-Bold", code));
        }
    }

    [Fact]
    public void GetWidth_NonStandard_ReturnsNegative()
    {
        Assert.Equal(-1, Standard14Fonts.GetWidth("Verdana", 65));
    }

    [Fact]
    public void Alias_Arial_GetsHelveticaWidths()
    {
        Assert.Equal(278, Standard14Fonts.GetWidth("ArialMT", 32));
        Assert.Equal(667, Standard14Fonts.GetWidth("ArialMT", 65));
    }

    [Fact]
    public void NormalizeFontName_StripsSubsetPrefix()
    {
        Assert.Equal("Helvetica", FontMetrics.NormalizeFontName("ABCDEF+Helvetica"));
        Assert.Equal("Times-Bold", FontMetrics.NormalizeFontName("GHIJKL+Times-Bold"));
    }

    [Fact]
    public void NormalizeFontName_PreservesNormalName()
    {
        Assert.Equal("Helvetica", FontMetrics.NormalizeFontName("Helvetica"));
        Assert.Equal("Short", FontMetrics.NormalizeFontName("Short"));
    }

    [Fact]
    public void Helvetica_ExclamationMark_Is278()
    {
        Assert.Equal(278, Standard14Fonts.GetWidth("Helvetica", 33));
    }

    [Fact]
    public void HelveticaBold_UppercaseA_Is722()
    {
        Assert.Equal(722, Standard14Fonts.GetWidth("Helvetica-Bold", 65));
    }

    [Fact]
    public void HelveticaBold_Space_Is278()
    {
        Assert.Equal(278, Standard14Fonts.GetWidth("Helvetica-Bold", 32));
    }

    [Fact]
    public void TimesBold_UppercaseA_Is722()
    {
        Assert.Equal(722, Standard14Fonts.GetWidth("Times-Bold", 65));
    }

    [Fact]
    public void TimesBold_Space_Is250()
    {
        Assert.Equal(250, Standard14Fonts.GetWidth("Times-Bold", 32));
    }

    [Fact]
    public void TimesItalic_UppercaseA_Is611()
    {
        Assert.Equal(611, Standard14Fonts.GetWidth("Times-Italic", 65));
    }

    [Fact]
    public void MeasureString_Helvetica_HelloWorld()
    {
        // Build a minimal PDF with Helvetica font and measure "Hello" text
        var content = System.Text.Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello) Tj ET");
        var data = Helpers.PdfBuilder.BuildWithTextContent(content);
        using var doc = Aspose.Pdf.Document.Open(data);

        var fonts = TextAbsorber.ResolveFonts(doc.Pages[1].Dict, doc.Pages[1].Reader);
        Assert.True(fonts.ContainsKey("F1"));
        var metrics = FontMetrics.FromFontDict(fonts["F1"], doc.Pages[1].Reader);

        // Helvetica widths for H=722 e=556 l=222 l=222 o=556 → total = 2278
        var width = metrics.MeasureString("Hello", 12);
        Assert.True(width > 0, "Width should be positive");
        // At 12pt: 2278 * 12 / 1000 = 27.336
        Assert.InRange(width, 20, 35);
    }

}
