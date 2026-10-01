using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a formula an equation editor set in a line: its letters one string placed glyph by glyph,
/// the pen stepping back between them, its operators another string, its indices a third, all reaching into one another.
/// Each glyph's text stands where the glyph does: the line reads left to right, every index kept; a slash drawn as a path
/// between two of its glyphs is a glyph of the line, no mark of it.</summary>
public class AutoTagInterleavedTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Times-Roman, F2 Times-Italic, F3 Symbol and F4 Symbol as a CID font.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [6 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Italic /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Symbol /ToUnicode 8 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 7 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R /F4 9 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            $"<< /Length {SymbolMap.Length} >>\nstream\n{SymbolMap}\nendstream",
            "<< /Type /Font /Subtype /Type0 /BaseFont /SymbolMT /Encoding /Identity-H /DescendantFonts [10 0 R] /ToUnicode 12 0 R >>",
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /SymbolMT /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
            + "/FontDescriptor 11 0 R /DW 384 /W [3 [250]] /CIDToGIDMap /Identity >>",
            "<< /Type /FontDescriptor /FontName /SymbolMT /Flags 4 /FontBBox [0 -220 1113 1005] /ItalicAngle 0 /Ascent 1005 /Descent -220 /CapHeight 700 /StemV 80 >>",
            $"<< /Length {PieceMap.Length} >>\nstream\n{PieceMap}\nendstream",
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

    // The Symbol font's brackets and the ceiling's pieces, as the text they stand for.
    private const string SymbolMap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Sym def "
        + "1 begincodespacerange <00> <FF> endcodespacerange 6 beginbfchar <28> <0028> <29> <0029> <2D> <2212> <3D> <003D> <E9> <23A1> <F9> <23A4> "
        + "endbfchar endcmap CMapName currentdict /CMap defineresource pop end end";

    // F4: the Symbol face as a CID font of two-byte codes (an equation editor's): its blank and the ceiling's pieces.
    private const string PieceMap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Pieces def "
        + "1 begincodespacerange <0000> <FFFF> endcodespacerange 3 beginbfchar <0003> <0020> <00E9> <F0E9> <00F9> <F0F9> "
        + "endbfchar endcmap CMapName currentdict /CMap defineresource pop end end";

    private static string Show(string font, double size, double x, double y, string text)
        => System.FormattableString.Invariant($"BT /{font} {size} Tf {x} {y} Td ({text}) Tj ET\n");

    // "X = X1 || X2 || ... || Xn-1 || Xn ;" as an equation editor writes it: the equals sign, the indices n, the letters X
    // from the last back to the first, the bars and dots likewise, then the indices 1, 2 and 1 and the minus sign.
    private const string Concatenation =
        "BT /F1 1 Tf 11.861 0 0 11.861 194.88 195.54 Tm (=) Tj\n"
        + "/F2 1 Tf 6.9188 0 0 6.9188 308.04 192.54 Tm [(n) 4984 (n)] TJ\n"
        + "11.861 0 0 11.861 299.34 195.54 Tm [(X) 3226 (X) 4036 (X) 2493 (X) 2518 (X)] TJ\n"
        + "/F1 1 Tf -0.693 0 Td [(||) 3015 (||) 1280 (...) 1301 (||) 2393 (||)] TJ\n"
        + "6.9188 0 0 6.9188 281.2 192.54 Tm (-) Tj 6.9188 0 0 6.9188 284.94 192.54 Tm (1) Tj -7.016 0 Td [(2) 3839 (1)] TJ\n"
        + "12 0 0 12 314.28 195.6 Tm ( ;) Tj ET\n";

    // "Let n = ceil(len(X)/128)": the brackets round X one string, a blank between them, the slash a stroked
    // path, the ceiling's pieces one string spaced likewise, "128" and "len" one string, X and n another.
    private const string Ceiling =
        "BT /F1 12 Tf 126 228.12 Td (Let ) Tj ET\n"
        + "BT /F1 12 Tf 155.9 228.12 Td (=) Tj ET\n"
        + "BT /F3 1 Tf 0.8865 Tc 11.857 0 0 15.8438 184.8 228.18 Tm (\\( \\)) Tj 0 Tc ET\n"
        + "q 1 0 0 1 206.82 237.3 cm 0.499 w 1 j 0 0 m -3.84 -12 l S Q\n"
        + "BT /F4 1 Tf 4.5652 Tc 11.857 0 0 14.3749 165.9 226.02 Tm <00E900F9> Tj 0 Tc\n"
        + "/F1 1 Tf 12 0 0 12 206.52 228.12 Tm [(128) 4560 (len)] TJ\n"
        + "/F2 1 Tf -1.42 0 Td [(X) 4171 (n)] TJ ET\n"
        // A second stroke further along the line, the line's text between the two.
        + "q 1 0 0 1 249 237.3 cm 0.499 w 1 j 0 0 m -3.84 -12 l S Q\n";

    // "For i = 1 to n - 1, let Yi = X ... CIPH CB": the indices, CB, the letters from X back to i, CIPH, then the words and
    // figures back from "let" - a comma and a figure stepped back to by less than an em - and the signs back from the last.
    private const string Loop =
        "BT /F2 1 Tf 6.9998 0 0 6.9998 321.84 129.96 Tm [(i) 3835 (K)] TJ -9.977 0 Td [(i) 3861 (i)] TJ\n"
        + "12 0 0 12 306.96 132.96 Tm (CB) Tj -5.285 0 Td [(X) 2481 (Y) 3886 (n) 3330 (i)] TJ\n"
        + "/F1 1 Tf 2.11 0 Td (CIPH) Tj -5.135 0 Td [(let ) 1470 ( ) 455 (,) 655 (1) 2075 ( ) 1250 ( to) 1403 (1)] TJ ET\n"
        + "BT /F3 1 Tf 12 0 0 12 233.1 132.96 Tm [(=) 4174 (-) 3544 (=)] TJ ET\n"
        + "BT /F1 12 Tf 126 132.96 Td (For ) Tj ET\n";

    private static string Page()
        => Show("F1", 12, 72, 250, "The blocks of the string are the following, each a block of the size the cipher takes.")
           + Ceiling + Concatenation + Loop
           + Show("F1", 12, 72, 90, "The last of them may be a partial block, shorter than the others, and it is padded then.");

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static List<MarkedContentItem> Texts(Document doc)
        => doc.TaggedContent.StructTreeRootElement.GetMarkedContent(true).Where(i => i.Kind == MarkedContentKind.Text).ToList();

    [Fact]
    public void AFormulaSetGlyphByGlyphReadsLeftToRight()
    {
        using var doc = Tag(Build(Page()));
        var line = Texts(doc).Where(i => i.Rectangle.LLY > 185 && i.Rectangle.URY < 210).OrderBy(i => i.Rectangle.LLX).ToList();
        Assert.Equal("X=X||X||...||X||X;", string.Concat(line.Where(i => i.FontSize > 10).Select(i => i.Text)).Replace(" ", ""));
        // Every index kept, under the letter it marks: the ones the pen stepped back to, and the ones it stepped back from.
        Assert.Equal("12n-1n", string.Concat(line.Where(i => i.FontSize < 10).Select(i => i.Text)).Replace(" ", ""));
    }

    [Fact]
    public void GlyphsSteppedBackToByLessThanAnEmReadWhereTheyStand()
    {
        using var doc = Tag(Build(Page()));
        var line = Texts(doc).Where(i => i.Rectangle.LLY > 125 && i.Rectangle.URY < 150 && i.FontSize > 10).OrderBy(i => i.Rectangle.LLX).ToList();
        Assert.Equal("Fori=1ton−1,letY=XCIPHCB", string.Concat(line.Select(i => i.Text)).Replace(" ", ""));
    }

    [Fact]
    public void ASlashDrawnBetweenAFormulasGlyphsIsNoMarkOfItsLine()
    {
        using var doc = Tag(Build(Page()));
        Assert.DoesNotContain(doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true), e => e.S.Name == "Figure");
        // The text beside it stays with its line.
        var line = Texts(doc).Where(i => i.Rectangle.LLY > 220 && i.Rectangle.URY < 245).OrderBy(i => i.Rectangle.LLX).ToList();
        Assert.Contains(line, i => i.Text.Trim() == "128" && i.Element?.S.Name != "Figure");
        // ... and reads as it stands: len, then X, then 128.
        var words = line.Where(i => i.FontSize is > 10 and < 13).Select(i => i.Text.Trim()).ToList();
        Assert.True(words.IndexOf("len") >= 0 && words.IndexOf("len") < words.IndexOf("X") && words.IndexOf("X") < words.IndexOf("128"),
            string.Join("|", words));
        // The ceiling's pieces, one string spaced by the character spacing, each where it stands: round len(X)/128.
        var all = line.Select(i => i.Text.Trim()).ToList();
        Assert.True(all.IndexOf("\uF0E9") >= 0 && all.IndexOf("\uF0E9") < all.IndexOf("len") && all.IndexOf("128") < all.IndexOf("\uF0F9"),
            string.Join("|", all));
    }

    [Fact]
    public void GlyphsSpacedApartByTheCharacterSpacingAreCutApartOnALineCutApart()
    {
        // The ceiling's pieces alone, one string of the CID face spaced by the character spacing, on a line cut apart.
        using var doc = new Document(new MemoryStream(Build("BT /F4 1 Tf 4.5652 Tc 11.857 0 0 14.3749 165.9 226.02 Tm <00E900F9> Tj 0 Tc ET\n")));
        Assert.True(LinkTextSplit.Apply(doc.Pages[1], [], [], [new Rectangle(120, 222, 500, 240)]));
        var content = Compat.Latin1.GetString(doc.Pages[1].GetContentStreamBytes() ?? []);
        Assert.Contains("<00E9> Tj", content);
        Assert.Contains("<00F9> Tj", content);
    }

    [Fact]
    public void StrokesDrawnApartAreDrawingsOfTheirOwn()
    {
        using var doc = Tag(Build(Page()));
        // The two strokes of the ceiling's line, each an artifact of its own with the line's text between them.
        var strokes = doc.Pages[1].GetArtifactContent().Where(i => i.Kind == MarkedContentKind.Drawing && i.Rectangle.LLY > 220 && i.Rectangle.URY < 240).ToList();
        Assert.Equal(2, strokes.Count);
        Assert.All(strokes, s => Assert.True(s.Rectangle.Width < 5, s.Rectangle.ToString()));
    }

    [Fact]
    public void AnIndexLoweredOnALineARaisedNumberOpensReadsWhereItStands()
    {
        // A note: its number raised, then its text; X's index lowered a little, its star raised, drawn in turn.
        using var doc = Tag(Build(Show("F1", 12, 72, 400, "The blocks of the string are the following, each a block of the size the cipher takes.")
                                  + Show("F1", 6.5, 72, 78.8, "2") + Show("F1", 10, 75.2, 74.3, " Consequently, ") + Show("F2", 10, 137.7, 74.3, "X")
                                  + Show("F2", 6.5, 143.9, 72.8, "n") + Show("F1", 6.5, 147.1, 78.8, "*")
                                  + Show("F1", 10, 150.4, 74.3, " is either a complete block or a nonempty partial block.")));
        var note = Texts(doc).Where(i => i.Rectangle.LLY < 90).Select(i => i.Text.Trim()).ToList();
        Assert.True(note.IndexOf("n") >= 0 && note.IndexOf("n") < note.FindIndex(t => t.StartsWith("is either")), string.Join("|", note));
    }

    [Fact]
    public void ANotesMarksReachingOnlyWithTheirBlankLeaveTheLineWhole()
    {
        // A row's label with its notes' numbers set small after it, the first number's blank reaching over the comma.
        using var doc = Tag(Build(Show("F1", 12, 72, 400, "The rows of the table follow, each with its label and its figures in turn.")
                                  + Show("F1", 8, 96.8, 211, "All other consumer loans") + Show("F1", 5, 184.5, 214.3, "15 ")
                                  + Show("F1", 5, 189.5, 214.3, ", ") + Show("F1", 5, 193.1, 214.3, "16")
                                  + Show("F1", 12, 72, 150, "The figures are in billions, and the notes follow the table in their order.")));
        Assert.Contains(Texts(doc), i => i.Text.Trim() == "All other consumer loans");
        // Its shows stay as they are written: the line's pieces reach into none of the others.
        Assert.Contains("(All other consumer loans) Tj", Compat.Latin1.GetString(doc.Pages[1].GetContentStreamBytes() ?? []));
    }
}
