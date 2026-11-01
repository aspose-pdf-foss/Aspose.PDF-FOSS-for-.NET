using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging keeps a line's text as content however it is drawn: a colon whose pen stops short of its glyph
/// under a negative character spacing, before the word it follows in a right-to-left line, and text drawn under a
/// mirroring scale with a negative font size on a flipped page, are their lines' text, no artifacts.</summary>
public class AutoTagMarkedTextTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Helvetica-Bold and F2 Arial as a CID font of
    /// two-byte codes (its blank and its colon).</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 3 0 R /F2 6 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type0 /BaseFont /Arial-BoldMT /Encoding /Identity-H /DescendantFonts [7 0 R] /ToUnicode 9 0 R >>",
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Arial-BoldMT /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
            + "/FontDescriptor 8 0 R /DW 1000 /W [3 [278] 29 [333]] /CIDToGIDMap /Identity >>",
            "<< /Type /FontDescriptor /FontName /Arial-BoldMT /Flags 32 /FontBBox [-628 -376 2000 1056] /ItalicAngle 0 /Ascent 905 /Descent -212 /CapHeight 716 /StemV 80 >>",
            $"<< /Length {BlankAndColon.Length} >>\nstream\n{BlankAndColon}\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    // F2's blank and colon as the text they stand for.
    private const string BlankAndColon = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Colon def "
        + "1 begincodespacerange <0000> <FFFF> endcodespacerange 2 beginbfchar <0003> <0020> <001D> <003A> "
        + "endbfchar endcmap CMapName currentdict /CMap defineresource pop end end";

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string Text(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text));

    // A right-to-left line as a word processor writes it, shown right to left: its blanks and its colon shown before the
    // word they follow, under a negative character spacing (the blanks advance the pen by nothing, the colon by less than
    // its glyph is wide), the second blank marked as a right-to-left mark; then the word, then the rest of the line.
    private const string RightToLeftLine =
        "BT /F2 1 Tf -0.278 Tc 0.278 Tw 16 0 0 16 178.807 551.97 Tm <0003> Tj\n"
        + "/Span << /ActualText <FEFF200F> >> BDC 0.278 0 Td <0003> Tj EMC <001D> Tj ET\n"
        + "BT /F1 1 Tf 0 Tc 0 Tw 16 0 0 16 188.58 551.97 Tm (Audi) Tj ET\n"
        + "BT /F1 1 Tf 16 0 0 16 224.127 551.97 Tm ( for the original kits of the car) Tj ET\n"
        + "BT /F1 1 Tf 16 0 0 16 221.189 532.77 Tm (4M0.051.790.H and 4M0.051.790.E) Tj ET\n";

    [Fact]
    public void AColonShownBeforeItsWordWithAPenStoppingShortOfItsGlyphIsTheLinesText()
    {
        using var doc = Tag(Build(RightToLeftLine));
        var text = Text(doc.TaggedContent.StructTreeRootElement);

        Assert.Contains(":", text);
        Assert.Contains("Audi", text);
    }

    // A table of contents as a report generator writes it: the page's space flipped, the text scaled by -100 % across and
    // shown with a negative font size (the two turning the glyphs back upright), each line's title and page number a text
    // object of its own placed by a matrix, a rule under the title.
    private static string ContentsLine(double y, string title, string page)
        => System.FormattableString.Invariant($"q 1 0 0 1 7.37 {y} cm BT 0 0.498 0.125 rg /F1 -10 Tf 0 9.053 Td ({title}) Tj ET Q\n")
           + System.FormattableString.Invariant($"0.5 g 7.37 {y + 14.75} m 368.22 {y + 14.75} l 368.22 {y + 13.75} l 7.37 {y + 13.75} l h f*\n")
           + System.FormattableString.Invariant($"q 1 0 0 1 429.81 {y} cm BT 0 0.498 0.125 rg /F1 -10 Tf 0 9.053 Td ({page}) Tj ET Q\n");

    [Fact]
    public void TextDrawnUnderAMirroringScaleWithANegativeSizeIsContent()
    {
        var content = "q 1 0 0 -1 0 792 cm -100 Tz q 1 0 0 1 72 72 cm\n"
                      + "q 1 0 0 1 16 30 cm BT 0 0.498 0.125 rg /F1 -22 Tf 0 20 Td (Table of contents) Tj ET Q\n"
                      + ContentsLine(82.12, "Field Report", "Page 1") + ContentsLine(99.13, "Soil Variability 10/9/2013", "Page 2")
                      + ContentsLine(116.14, "Yield Report 7/9/2013", "Page 3") + ContentsLine(133.15, "Soil Map", "Page 4")
                      + "Q Q\n";
        using var doc = Tag(Build(content));
        var text = Text(doc.TaggedContent.StructTreeRootElement);

        Assert.Contains("Table of contents", text);
        Assert.Contains("Field Report", text);
        Assert.Contains("Soil Variability 10/9/2013", text);
        Assert.Contains("Page 4", text);
    }
}
