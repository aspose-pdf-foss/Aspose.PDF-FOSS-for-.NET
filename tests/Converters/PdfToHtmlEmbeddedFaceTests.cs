using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>The face a PDF to HTML export ships for an embedded font program the page text has no /ToUnicode
/// for: a simple font whose codes its /Differences names, and a CID font whose words are spaced by the gaps
/// of the shown text.</summary>
public class PdfToHtmlEmbeddedFaceTests
{
    /// <summary>A one-page PDF showing <paramref name="content"/> in font /F1, given by
    /// <paramref name="fontObjects"/> (objects 5 onwards) whose <c>{program}</c> object is the embedded face.</summary>
    private static byte[] BuildPdf(string content, byte[] program, params string[] fontObjects)
    {
        var objects = new List<object>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        };
        objects.AddRange(fontObjects);
        objects.Add(program);
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n");
            if (objects[i] is byte[] bytes)
            {
                Write($"<< /Length {bytes.Length} /Length1 {bytes.Length} >>\nstream\n");
                ms.Write(bytes);
                Write("\nendstream");
            }
            else Write((string)objects[i]);
            Write("\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static string NewFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "html-face-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Export(byte[] pdf, string dir, HtmlSaveOptions options)
    {
        var html = Path.Combine(dir, "out.html");
        using (var doc = new Document(new MemoryStream(pdf))) doc.Save(html, options);
        return html;
    }

    [Fact]
    public void ACodeOfASimpleFontIsMappedInTheShippedFaceToTheCharacterItsDifferencesNameStandsFor()
    {
        // A symbol face keys code c at U+F000 + c; the font's /Differences names codes 65 and 66 H and i, and
        // there is no /ToUnicode, so the page text reads "Hi".
        var pdf = BuildPdf("BT /F1 24 Tf 72 700 Td (AB) Tj ET", TrueTypeFaceBuilder.WithSymbolCmapOnly(),
            "<< /Type /Font /Subtype /TrueType /BaseFont /ABCDEF+TestSymbol /FirstChar 65 /LastChar 66 /Widths [500 500]"
            + " /Encoding << /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [65 /H /i] >> /FontDescriptor 6 0 R >>",
            "<< /Type /FontDescriptor /FontName /ABCDEF+TestSymbol /Flags 4 /FontBBox [0 -200 1000 800] /ItalicAngle 0"
            + " /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 7 0 R >>");
        var dir = NewFolder();
        try
        {
            Export(pdf, dir, new HtmlSaveOptions { FixedLayout = true, FontSavingMode = HtmlSaveOptions.FontSavingModes.SaveInAllFormats });

            var face = Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories).Single();
            var cmap = new GlyphOutlineParser(File.ReadAllBytes(face)).CMap;
            // Codes 65 and 66 draw glyphs 34 and 35 (code - 32 + 1).
            Assert.Equal(34, cmap.GetValueOrDefault('H'));
            Assert.Equal(35, cmap.GetValueOrDefault('i'));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ACodeTheToUnicodeLeavesOutIsWrittenAsTheCharacterItsDifferencesNameStandsFor()
    {
        // The /ToUnicode maps code 65 to alef; code 66 it leaves out, and the /Differences names it afii62760, the
        // final shape of alef: the page writes that presentation form, not the byte "B".
        const string toUnicode = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /T def /CMapType 2 def"
            + " 1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <41> <0627> endbfchar endcmap"
            + " CMapName currentdict /CMap defineresource pop end end";
        var pdf = BuildPdf("BT /F1 24 Tf 72 700 Td (AB) Tj ET", TrueTypeFaceBuilder.WithSymbolCmapOnly(),
            "<< /Type /Font /Subtype /TrueType /BaseFont /ABCDEF+TestSymbol /FirstChar 65 /LastChar 66 /Widths [500 500]"
            + " /Encoding << /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [65 /afii57415 /afii62760] >>"
            + " /ToUnicode 7 0 R /FontDescriptor 6 0 R >>",
            "<< /Type /FontDescriptor /FontName /ABCDEF+TestSymbol /Flags 4 /FontBBox [0 -200 1000 800] /ItalicAngle 0"
            + " /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 8 0 R >>",
            $"<< /Length {toUnicode.Length} >>\nstream\n{toUnicode}\nendstream");
        var dir = NewFolder();
        try
        {
            var html = File.ReadAllText(Export(pdf, dir, new HtmlSaveOptions { FixedLayout = true }));

            var text = string.Concat(Regex.Matches(html, "<span[^>]*>([^<]*)</span>").Cast<Match>().Select(m => m.Groups[1].Value));
            Assert.Contains("\u0627\uFE8E", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AStandInMarkAfterASpaceIsPartedFromItByAZeroWidthSpace()
    {
        // CID 0x22 maps into the unassigned gap U+FFF0, so it is written as the first minted stand-in, U+A880, a
        // spacing mark of Saurashtra; set a word apart from CID 0x21, it follows a space, which a browser would
        // join it to.
        const string toUnicode = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /T def /CMapType 2 def"
            + " 1 begincodespacerange <0000> <FFFF> endcodespacerange"
            + " 2 beginbfchar <0021> <0041> <0022> <FFF0> endbfchar endcmap"
            + " CMapName currentdict /CMap defineresource pop end end";
        var pdf = BuildPdf("BT /F1 20 Tf 72 700 Td [<0021> -600 <0022>] TJ ET",
            PdfBuilder.BuildMinimalTrueTypeFont(),
            "<< /Type /Font /Subtype /Type0 /BaseFont /ABCDEF+TestFont /Encoding /Identity-H /DescendantFonts [6 0 R]"
            + " /ToUnicode 8 0 R >>",
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /ABCDEF+TestFont /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >>"
            + " /DW 500 /CIDToGIDMap /Identity /FontDescriptor 7 0 R >>",
            "<< /Type /FontDescriptor /FontName /ABCDEF+TestFont /Flags 4 /FontBBox [0 -200 1000 800] /ItalicAngle 0"
            + " /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 9 0 R >>",
            $"<< /Length {toUnicode.Length} >>\nstream\n{toUnicode}\nendstream");
        var dir = NewFolder();
        try
        {
            var html = File.ReadAllText(Export(pdf, dir, new HtmlSaveOptions { FixedLayout = true })).Replace("&nbsp;", "\u00A0");

            Assert.Matches("A[ \u00A0]+\u200B\uA880", html);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AFaceGivenMarkGlyphsClassesThemAsMarksAndEveryOtherGlyphAsABaseGlyph()
    {
        var face = CffToTrueType.TryAddUnicodeCmap(TrueTypeFaceBuilder.WithSymbolCmapOnly(),
            new Dictionary<int, int> { [0xA8C4] = 5, [0x0301] = 6 }, new HashSet<int> { 6 });
        Assert.NotNull(face);

        var classes = GlyphClasses(face!);
        Assert.Equal(1, classes[5]);
        Assert.Equal(3, classes[6]);
        Assert.All(classes.Where((_, gid) => gid != 6), c => Assert.Equal(1, c));
    }

    /// <summary>The format 1 GDEF glyph classes of an sfnt, by glyph id.</summary>
    private static int[] GlyphClasses(byte[] font)
    {
        static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
        static int U32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        for (var i = 0; i < U16(font, 4); i++)
        {
            var e = 12 + i * 16;
            if (Encoding.ASCII.GetString(font, e, 4) != "GDEF") continue;
            var gdef = U32(font, e + 8);
            var cd = gdef + U16(font, gdef + 4);
            Assert.Equal(1, U16(font, cd));
            return Enumerable.Range(0, U16(font, cd + 4)).Select(g => U16(font, cd + 6 + 2 * g)).ToArray();
        }
        throw new InvalidOperationException("no GDEF table");
    }

    [Fact]
    public void AWordGapOfACidFontWithoutAToUnicodeIsNotSolvedAgainstTheAdvanceOfItsCid32()
    {
        // An Identity-H font with no /ToUnicode: its words are set apart by -277 gaps, and CID 32 is a wide
        // glyph of 900 units that the text never shows and the browser never draws as a space.
        var pdf = BuildPdf("BT /F1 20 Tf 72 700 Td [<0021002200230024> -277 <0025002600270028> -277 <0029002A>] TJ ET",
            PdfBuilder.BuildMinimalTrueTypeFont(),
            "<< /Type /Font /Subtype /Type0 /BaseFont /ABCDEF+TestFont /Encoding /Identity-H /DescendantFonts [6 0 R] >>",
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /ABCDEF+TestFont /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >>"
            + " /DW 500 /W [32 [900]] /CIDToGIDMap /Identity /FontDescriptor 7 0 R >>",
            "<< /Type /FontDescriptor /FontName /ABCDEF+TestFont /Flags 4 /FontBBox [0 -200 1000 800] /ItalicAngle 0"
            + " /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 8 0 R >>");
        var dir = NewFolder();
        try
        {
            var html = File.ReadAllText(Export(pdf, dir, new HtmlSaveOptions
            {
                PartsEmbeddingMode = HtmlSaveOptions.PartsEmbeddingModes.EmbedAllIntoHtml,
                RasterImagesSavingMode = HtmlSaveOptions.RasterImagesSavingModes.AsEmbeddedPartsOfPngPageBackground,
            }));

            var spacings = Regex.Matches(html, @"word-spacing:(-?[0-9.]+)em").Cast<Match>()
                .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
            // The gap is 277 units, a space of the browser's face about 250: a word space within a tenth of an em.
            Assert.All(spacings, s => Assert.True(Math.Abs(s) < 0.1, $"word-spacing {s}em"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
