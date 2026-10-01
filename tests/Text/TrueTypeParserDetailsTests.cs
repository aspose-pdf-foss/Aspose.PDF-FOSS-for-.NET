using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>What the TrueType reader answers beyond embedding: the counts, OS/2 fields, name
/// records, glyph boxes, kerning and cmap subtables of a face, and the sparse subset that keeps
/// the face's cmap for a simple font.</summary>
public sealed class TrueTypeParserDetailsTests
{
    private static TrueTypeParser Parsed(byte[] program)
    {
        var parser = new TrueTypeParser(program);
        parser.Parse();
        return parser;
    }

    [Fact]
    public void TheCountsAndOs2FieldsAreRead()
    {
        var parser = Parsed(PdfBuilder.BuildMinimalTrueTypeFont());
        Assert.Equal(96, parser.NumGlyphs);
        Assert.Equal(500, parser.AdvanceWidthMax);
        Assert.Equal(1, parser.IndexToLocFormat);
        Assert.True(parser.HasOs2);
        Assert.Equal(4, parser.Os2Version);
        Assert.Equal(500, parser.AvgCharWidth);
        Assert.Equal(5, parser.WidthClass);
        Assert.Equal(0, parser.FsType);
    }

    [Fact]
    public void TheNameRecordsAreDecoded()
    {
        var record = Assert.Single(Parsed(PdfBuilder.BuildMinimalTrueTypeFont()).NameRecords);
        Assert.Equal(new TrueTypeParser.NameRecord(3, 1, 0x0409, 6, "TestFont"), record);
    }

    [Fact]
    public void AGlyphBoxComesFromItsOutlineHeader()
    {
        var parser = Parsed(PdfBuilder.BuildMinimalTrueTypeFont());
        Assert.Equal(new[] { 0, 0, 500, 700 }, parser.GetGlyphBBox(1));
        // .notdef has no outline, and a glyph past the count is no glyph.
        Assert.Null(parser.GetGlyphBBox(0));
        Assert.Null(parser.GetGlyphBBox(96));
    }

    [Fact]
    public void AFaceWithoutAKernTableHasNoPairs() =>
        Assert.Empty(Parsed(PdfBuilder.BuildMinimalTrueTypeFont()).KernPairs);

    [Fact]
    public void ACmapSubtableIsReadOnItsOwn()
    {
        var parser = Parsed(PdfBuilder.BuildMinimalTrueTypeFont());
        Assert.Equal(new[] { (3, 1) }, parser.CMapSubtables);
        var map = parser.ReadCMapSubtable(3, 1);
        Assert.NotNull(map);
        Assert.Equal(95, map!.Count);
        Assert.Equal('A' - 31, map['A']);
        Assert.Null(parser.ReadCMapSubtable(1, 0));
    }

    [Fact]
    public void ASparseSubsetKeepsTheCmapOnlyWhenAsked()
    {
        var program = PdfBuilder.BuildMinimalTrueTypeFont();
        var subsetter = new TrueTypeSubsetter(program, Parsed(program));
        var glyphs = new[] { 'A' - 31, 'B' - 31 };
        Assert.Empty(Parsed(subsetter.SubsetSparse(glyphs)).CMapSubtables);
        var kept = Parsed(subsetter.SubsetSparse(glyphs, keepCmap: true));
        Assert.Equal(new[] { (3, 1) }, kept.CMapSubtables);
        // Glyph ids are kept, so the kept cmap still points at the right glyphs; the glyphs
        // not asked for lose their outlines, and the count ends at the last one kept.
        Assert.Equal('B' - 31 + 1, kept.NumGlyphs);
        Assert.Equal(new[] { 0, 0, 500, 700 }, kept.GetGlyphBBox('A' - 31));
        Assert.Null(kept.GetGlyphBBox(1));
    }
}
