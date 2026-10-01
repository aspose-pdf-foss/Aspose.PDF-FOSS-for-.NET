using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class TextAbsorberEncodingTests
{
    [Fact]
    public void DifferencesArray_MapsGlyphNamesToUnicode()
    {
        // Font encoding dict with Differences: code 65→eacute, 66→agrave
        // So byte 0x41 should produce e-acute, 0x42 should produce a-grave
        var fontDict = "/Encoding << /BaseEncoding /WinAnsiEncoding /Differences [65 /eacute /agrave] >>";
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (AB) Tj ET");
        var data = PdfBuilder.BuildWithFontDict(content, fontDict);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        // Code 65 (A) → eacute (U+00E9), code 66 (B) → agrave (U+00E0)
        Assert.Contains("\u00E9", absorber.Text); // e-acute
        Assert.Contains("\u00E0", absorber.Text); // a-grave
    }

    [Fact]
    public void DifferencesArray_MultipleStartCodes()
    {
        // Differences: [32 /space /exclam 65 /A /B /C]
        // code 32→space, 33→exclam, 65→A, 66→B, 67→C
        var fontDict = "/Encoding << /Differences [32 /space /exclam 65 /A /B /C] >>";
        // Send bytes: 0x41=65(A), 0x20=32(space), 0x42=66(B), 0x43=67(C)
        var contentBytes = new byte[] { 0x41, 0x42, 0x43 };
        // Build content stream with hex string
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf <414243> Tj ET");
        var data = PdfBuilder.BuildWithFontDict(content, fontDict);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("ABC", absorber.Text);
    }

    [Fact]
    public void DifferencesArray_LigaturesMap()
    {
        // Map code 1→fi ligature, code 2→fl ligature
        var fontDict = "/Encoding << /Differences [1 /fi /fl] >>";
        // Content uses hex string with bytes 01, 02
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf <0102> Tj ET");
        var data = PdfBuilder.BuildWithFontDict(content, fontDict);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("\uFB01", absorber.Text); // fi ligature
        Assert.Contains("\uFB02", absorber.Text); // fl ligature
    }

    [Fact]
    public void WinAnsiEncoding_AccentedCharacters()
    {
        // WinAnsi: byte 0xE9 = e-acute, 0xE0 = a-grave, 0xF1 = n-tilde
        var fontDict = "/Encoding /WinAnsiEncoding";
        // Content: raw bytes for accented chars via hex string
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf <E9E0F1> Tj ET");
        var data = PdfBuilder.BuildWithFontDict(content, fontDict);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("\u00E9", absorber.Text); // e-acute
        Assert.Contains("\u00E0", absorber.Text); // a-grave
        Assert.Contains("\u00F1", absorber.Text); // n-tilde
    }

    [Fact]
    public void WinAnsiEncoding_SpecialSymbols()
    {
        // WinAnsi: byte 0x93=left double quote, 0x94=right double quote,
        // 0x96=en dash, 0x97=em dash, 0x95=bullet
        var fontDict = "/Encoding /WinAnsiEncoding";
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf <9394969795> Tj ET");
        var data = PdfBuilder.BuildWithFontDict(content, fontDict);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("\u201C", absorber.Text); // left double quote
        Assert.Contains("\u201D", absorber.Text); // right double quote
        Assert.Contains("\u2013", absorber.Text); // en dash
        Assert.Contains("\u2014", absorber.Text); // em dash
        Assert.Contains("\u2022", absorber.Text); // bullet
    }

    [Fact]
    public void MacRomanEncoding_AccentedCharacters()
    {
        // MacRoman: byte 0x87=a-acute (135), 0x88=a-grave (136), 0x8E=e-acute (142)
        var fontDict = "/Encoding /MacRomanEncoding";
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf <87888E> Tj ET");
        var data = PdfBuilder.BuildWithFontDict(content, fontDict);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("\u00E1", absorber.Text); // a-acute (MacRoman 0x87)
        Assert.Contains("\u00E0", absorber.Text); // a-grave (MacRoman 0x88)
        Assert.Contains("\u00E9", absorber.Text); // e-acute (MacRoman 0x8E)
    }

    [Fact]
    public void DoubleQuoteOperator_ExtractsTextWithSpacing()
    {
        // The " operator: aw ac string " → set word spacing, set char spacing, T*, Tj
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 0 0 (Hello) \" ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("Hello", absorber.Text);
    }

    [Fact]
    public void DoubleQuoteOperator_WithRealSpacingValues()
    {
        // aw=1.5 ac=0.5 string "
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 1.5 0.5 (World) \" ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("World", absorber.Text);
    }

    [Fact]
    public void MixedEncodings_OnSamePage()
    {
        // F1: WinAnsiEncoding, F2: encoding dict with Differences
        var entries = new[]
        {
            ("F1", "/Encoding /WinAnsiEncoding",
                Encoding.ASCII.GetBytes("BT /F1 12 Tf <E9> Tj ET")),
            ("F2", "/Encoding << /Differences [65 /ntilde] >>",
                Encoding.ASCII.GetBytes("BT /F2 12 Tf (A) Tj ET")),
        };
        var data = PdfBuilder.BuildWithMultipleFontDicts(entries);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        // F1 with WinAnsi: 0xE9 = e-acute
        Assert.Contains("\u00E9", absorber.Text);
        // F2 with Differences: code 65 (A) → ntilde
        Assert.Contains("\u00F1", absorber.Text);
    }

    [Fact]
    public void DifferencesArray_FallsBackToBaseEncoding()
    {
        // Differences only maps code 65. Code 66 should fall back to BaseEncoding (WinAnsi).
        var fontDict = "/Encoding << /BaseEncoding /WinAnsiEncoding /Differences [65 /eacute] >>";
        // Send bytes: 0x41=65 (mapped by Differences), 0x42=66 (fallback to WinAnsi = 'B')
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf <4142> Tj ET");
        var data = PdfBuilder.BuildWithFontDict(content, fontDict);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("\u00E9", absorber.Text); // code 65 → eacute
        Assert.Contains("B", absorber.Text);       // code 66 → fallback to WinAnsi 'B'
    }

    [Fact]
    public void GlyphNameToUnicode_CoreSetPresent()
    {
        // Verify the glyph list has essential mappings
        var glyph = TextAbsorber.GlyphNameToUnicode;

        Assert.Equal("\u0020", glyph["space"]);
        Assert.Equal("\u0041", glyph["A"]);
        Assert.Equal("\u007A", glyph["z"]);
        Assert.Equal("\u0030", glyph["zero"]);
        Assert.Equal("\u002E", glyph["period"]);
        Assert.Equal("\u002C", glyph["comma"]);
        Assert.Equal("\u002D", glyph["hyphen"]);

        // Accented
        Assert.Equal("\u00E0", glyph["agrave"]);
        Assert.Equal("\u00E9", glyph["eacute"]);
        Assert.Equal("\u00F1", glyph["ntilde"]);

        // Ligatures
        Assert.Equal("\uFB01", glyph["fi"]);
        Assert.Equal("\uFB02", glyph["fl"]);
        Assert.Equal("\uFB00", glyph["ff"]);

        // Symbols
        Assert.Equal("\u2022", glyph["bullet"]);
        Assert.Equal("\u2013", glyph["endash"]);
        Assert.Equal("\u2014", glyph["emdash"]);
        Assert.Equal("\u2018", glyph["quoteleft"]);
        Assert.Equal("\u2019", glyph["quoteright"]);
        Assert.Equal("\u201C", glyph["quotedblleft"]);
        Assert.Equal("\u201D", glyph["quotedblright"]);
    }

    [Fact]
    public void GlyphNameToUnicode_AdobeGlyphList2_0_FullyLoaded()
    {
        var glyph = TextAbsorber.GlyphNameToUnicode;

        // The combined resource (Adobe's 2.0 list plus its own 1.2 supplement)
        // resolves to 4,200 single-codepoint names, plus the 16 PDF/ZapfDingbats
        // names the list itself does not carry.
        Assert.Equal(4200 + 16, glyph.Count);

        // Names the list does not carry.
        Assert.Equal("\uFFFD", glyph[".notdef"]);
        Assert.Equal("\uFFFD", glyph["notdef"]);
        Assert.Equal("\u0000", glyph["null"]);
        Assert.Equal("\u000D", glyph["CR"]);
        Assert.Equal("\u25A1", glyph["square"]);
        Assert.Equal("\u2701", glyph["a1"]);
        Assert.Equal("\u261E", glyph["a11"]);

        // The 1.2 supplement, appended after the 2.0 list, gives "Delta" and
        // "Omega" the Greek letters rather than the 2.0 list's own increment
        // and ohm-sign answer for those names -- see LoadGlyphNames.
        Assert.Equal("\u0394", glyph["Delta"]);
        Assert.Equal("\u03A9", glyph["Omega"]);
        // "mu" is pinned to the 2.0 list's own answer, the micro sign, rather
        // than letting the 1.2 supplement's Greek mu win.
        Assert.Equal("\u00B5", glyph["mu"]);

        // The 2.0 list's own separate, disambiguated entries are untouched.
        Assert.Equal("\u2206", glyph["increment"]);
        Assert.Equal("\u2126", glyph["Ohm"]);
        Assert.Equal("\u03A9", glyph["Omegagreek"]);

        // Names the earlier hand-written table never carried.
        Assert.Equal("\u0431", glyph["becyrillic"]);
        Assert.Equal("\u05D3", glyph["dalethebrew"]);
        Assert.Equal("\u0641", glyph["feharabic"]);
    }

    [Fact]
    public void UnicodeToGlyphName_ReverseLookupBuilt()
    {
        var reverse = TextAbsorber.UnicodeToGlyphName;

        Assert.Equal("space", reverse[0x0020]);
        Assert.Equal("Delta", reverse[0x0394]);
        Assert.Equal("Omega", reverse[0x03A9]);
        Assert.Equal("mu", reverse[0x00B5]);
        Assert.Equal("mu", reverse[0x03BC]);
    }

    [Fact]
    public void AdobeGlyphOnlyTables_ExcludeTheNonAdobeExtras()
    {
        // Adobe's own list resolves to 4,200 single-codepoint names and 3,680
        // distinct codepoints (some names are synonyms for the same one) --
        // a caller reporting or reading from Adobe's own list uses
        // AdobeGlyphNameToUnicode/AdobeUnicodeToGlyphName rather than
        // GlyphNameToUnicode/UnicodeToGlyphName, which also carry the 16
        // PDF-synthetic names this engine keeps beside it.
        Assert.Equal(4200, TextAbsorber.AdobeGlyphNameToUnicode.Count);
        Assert.Equal(3680, TextAbsorber.AdobeUnicodeToGlyphName.Count);
        Assert.Equal(4200 + 16, TextAbsorber.GlyphNameToUnicode.Count);

        Assert.False(TextAbsorber.AdobeGlyphNameToUnicode.ContainsKey(".notdef"));
        Assert.False(TextAbsorber.AdobeUnicodeToGlyphName.ContainsKey(0xFFFD));
        Assert.Equal("µ", TextAbsorber.AdobeGlyphNameToUnicode["mu"]);
    }

    [Fact]
    public void WinAnsiEncoding_AsciiRange_Unchanged()
    {
        // ASCII bytes should pass through unchanged with WinAnsiEncoding
        var fontDict = "/Encoding /WinAnsiEncoding";
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello 123) Tj ET");
        var data = PdfBuilder.BuildWithFontDict(content, fontDict);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("Hello 123", absorber.Text);
    }

    [Fact]
    public void WinAnsiEncoding_EuroSign()
    {
        // WinAnsi: byte 0x80 = Euro sign (U+20AC)
        var fontDict = "/Encoding /WinAnsiEncoding";
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf <80> Tj ET");
        var data = PdfBuilder.BuildWithFontDict(content, fontDict);
        using var doc = Document.Open(data);

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);

        Assert.Contains("\u20AC", absorber.Text);
    }
}
