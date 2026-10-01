using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>Text in an embedded face in a declared line box: a fragment's first line seated on
/// its baseline, a run among segments that flow as runs, the box a multiplied leading spans over
/// runs of different proportions, and faces whose advances are taken in whole units.</summary>
public sealed class EmbeddedFaceRunsTests
{
    private const double Margin = 36;
    private const double Top = 842 - Margin;

    /// <summary>The synthetic face: every glyph 500 units wide at 1000 units per em, hhea
    /// ascent 800 and descent 200, PostScript name TestFont.</summary>
    private static Font EmbeddedFace(byte[]? program = null) =>
        FontRepository.OpenFont(new MemoryStream(program ?? PdfBuilder.BuildMinimalTrueTypeFont()), FontTypes.TTF);

    /// <summary>A declared line box of 0.8 + 0.2 em under 6 pt of leading: at 12 pt the line is
    /// 18 tall and its baseline sits (18 - 12) / 2 + 9.6 = 12.6 under its top.</summary>
    private static void DeclareLineBox(TextState state, double size)
    {
        state.FontSize = (float)size;
        state.LineSpacing = (float)(size * 0.5);
        state.LineBoxAscentEm = 0.8;
        state.LineBoxDescentEm = 0.2;
    }

    [Fact]
    public void ADeclaredLineBoxSeatsAnEmbeddedFacesFirstBaseline()
    {
        var fragment = new TextFragment("Hello");
        DeclareLineBox(fragment.TextState, 12);
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.Font = EmbeddedFace();
        Assert.Equal("Hello", Assert.Single(Absorb(fragment)).Text);
        Assert.Equal(Top - 12.6, Assert.Single(Shows(fragment)).Y, 2);
    }

    [Fact]
    public void AnEmbeddedRunFlowsBesideAStandardRunInParagraphOrder()
    {
        var fragment = Segmented(("plain ", null), ("EMBED", EmbeddedFace()));
        var before = new TextFragment("first");
        DeclareLineBox(before.TextState, 12);
        before.TextState.Font = EmbeddedFace();
        before.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        var shown = Absorb(before, fragment);
        Assert.Equal(new[] { "first", "plain ", "EMBED" }, shown.Select(f => f.Text).ToArray());
        Assert.NotEqual(shown[1].TextState.Font!.FontName, shown[2].TextState.Font!.FontName);
        var shows = Shows(before, fragment);
        Assert.Equal(3, shows.Count);
        // The paragraph's line sits one 18 pt line under the first, both runs on its baseline.
        Assert.Equal(Top - 12.6, shows[0].Y, 2);
        Assert.Equal(Top - 18 - 12.6, shows[1].Y, 2);
        Assert.Equal(shows[1].Y, shows[2].Y, 2);
        // "plain " in 12 pt Helvetica is 28.68 wide; the embedded run starts right after it.
        Assert.Equal(Margin + 28.68, shows[2].X, 2);
    }

    [Fact]
    public void AMultipliedLeadingSpansTheHighestAscentAndTheDeepestDescent()
    {
        // One run reaches 0.9 em above the baseline, the other 0.3 em below it: at 12 pt the
        // line spans 10.8 + 3.6 = 14.4, times 1.5 is 21.6, and the baseline sits half the
        // surplus plus the highest ascent, 3.6 + 10.8 = 14.4, under the top.
        var fragment = Segmented(("tall ", null), ("deep", null));
        fragment.TextState.FormattingOptions.RunLineBoxMultiplier = 1.5;
        var (tall, deep) = (fragment.Segments[fragment.Segments.Count - 1], fragment.Segments[fragment.Segments.Count]);
        tall.TextState.LineBoxAscentEm = 0.9;
        tall.TextState.LineBoxDescentEm = 0.1;
        deep.TextState.LineBoxAscentEm = 0.7;
        deep.TextState.LineBoxDescentEm = 0.3;
        var shows = Shows(fragment);
        Assert.Equal(2, shows.Count);
        Assert.Equal(Top - 14.4, shows[0].Y, 2);
        Assert.Equal(Top - 14.4, shows[1].Y, 2);
    }

    [Fact]
    public void WholeUnitAdvancesTruncateWhatTheFaceMeasures()
    {
        // At 2048 units per em a 500-unit advance is 244.140625 thousandths of an em; taken
        // in whole units it is 244.
        var marked = WithUnitsPerEm(PdfBuilder.BuildMinimalTrueTypeFont(), 2048);
        var exact = WithUnitsPerEm(PdfBuilder.BuildMinimalTrueTypeFont(), 2048);
        WholeUnitAdvances.Mark(marked);
        Assert.True(WholeUnitAdvances.Holds(marked));
        Assert.False(WholeUnitAdvances.Holds(exact));
        Assert.Equal(4.88, Measure(marked, "AB"), 9);
        Assert.Equal(4.8828125, Measure(exact, "AB"), 9);
        Assert.Equal(4.88, Type0FontEmbedder.MeasureText(new Core.PdfDictionary(), marked, "TestFont", "AB", 10), 9);
    }

    private static double Measure(byte[] program, string text)
    {
        var face = new FontData("TestFont", FontType.TrueType);
        face.SetTtfData(program);
        return TextPaginator.CreateMeasurer("TestFont", 10, face)(text);
    }

    /// <summary>The program with its head table's units per em rewritten.</summary>
    private static byte[] WithUnitsPerEm(byte[] program, int unitsPerEm)
    {
        var tables = (program[4] << 8) | program[5];
        for (var i = 0; i < tables; i++)
        {
            var entry = 12 + i * 16;
            if (System.Text.Encoding.ASCII.GetString(program, entry, 4) != "head") continue;
            var head = (program[entry + 8] << 24) | (program[entry + 9] << 16) | (program[entry + 10] << 8) | program[entry + 11];
            program[head + 18] = (byte)(unitsPerEm >> 8);
            program[head + 19] = (byte)unitsPerEm;
        }
        return program;
    }

    /// <summary>A fragment of runs that flow as runs in declared 12 pt line boxes, each in the
    /// face given or in Helvetica.</summary>
    [Fact]
    public void AStandardParagraphAfterAnEmbeddedOneKeepsParagraphOrder()
    {
        // An embedded-face paragraph is written when the flow drains; a standard-face paragraph
        // after it must wait too, or it lands before it in the content stream.
        var embedded = new TextFragment("first");
        DeclareLineBox(embedded.TextState, 12);
        embedded.TextState.Font = EmbeddedFace();
        embedded.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        var standard = new TextFragment("second");
        DeclareLineBox(standard.TextState, 12);
        standard.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        var shown = Absorb(embedded, standard);
        Assert.Equal(new[] { "first", "second" }, shown.Select(f => f.Text).ToArray());
    }

    [Fact]
    public void ATableAfterAnEmbeddedParagraphKeepsItsPlaceInTheStream()
    {
        // A table's content is laid at once; after an embedded-face paragraph it waits with the
        // deferred text instead, or it lands before that paragraph in the content stream.
        var embedded = new TextFragment("first");
        DeclareLineBox(embedded.TextState, 12);
        embedded.TextState.Font = EmbeddedFace();
        embedded.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        var table = new Table { ColumnWidths = "100" };
        table.Rows.Add().Cells.Add().Paragraphs.Add(new TextFragment("cell"));
        var after = new TextFragment("third");
        DeclareLineBox(after.TextState, 12);
        after.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        using var reopened = Document.Open(Laid(embedded, table, after));
        var absorber = new TextFragmentAbsorber();
        reopened.Pages[1].Accept(absorber);
        Assert.Equal(new[] { "first", "cell", "third" }, absorber.TextFragments.Select(f => f.Text).ToArray());
    }

    [Fact]
    public void AnEmbeddedFaceCarriesItsWordSpacingGlyphByGlyph()
    {
        // Tw reaches only the single-byte code 32: a Type0 run moves the pen after each space
        // glyph instead, 6 pt at 12 pt being 500 thousandths of the em, its trailing space included.
        var fragment = new TextFragment("a b ");
        DeclareLineBox(fragment.TextState, 12);
        fragment.TextState.Font = EmbeddedFace();
        fragment.TextState.WordSpacing = 6;
        using var reopened = Document.Open(Laid(fragment));
        var ops = reopened.Pages[1].Contents.ToList();
        Assert.DoesNotContain(ops, op => op is Operators.SetWordSpacing);
        var shown = Assert.Single(ops.OfType<Operators.SetGlyphsPositionShowText>());
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(shown.ToPdf(), "-500").Count);
    }

    [Fact]
    public void ASyntheticItalicLeansAnEmbeddedFacesMatrix()
    {
        var fragment = new TextFragment("lean");
        DeclareLineBox(fragment.TextState, 12);
        fragment.TextState.Font = EmbeddedFace();
        fragment.TextState.FormattingOptions.SyntheticItalicLean = 0.2;
        using var reopened = Document.Open(Laid(fragment));
        var matrix = Assert.Single(reopened.Pages[1].Contents.OfType<Operators.SetTextMatrix>());
        Assert.Equal(0.2, matrix.C, 6);
        Assert.Equal(Margin, matrix.E, 2);
    }

    [Fact]
    public void AStandardFaceKeepsItsNameOnAnOverflowPage()
    {
        // An overflow page used to register Helvetica as F1 and take every plain chunk's F1 as
        // that; it now takes the start page's names first, so a Courier chunk stays Courier.
        var lines = new List<TextFragment>();
        for (var i = 0; i < 80; i++)
        {
            var line = new TextFragment("Courier line " + i);
            line.TextState.FontSize = 12;
            line.TextState.FontName = "Courier";
            lines.Add(line);
        }
        using var reopened = Document.Open(Laid(lines.ToArray()));
        Assert.True(reopened.Pages.Count > 1);
        var absorber = new TextFragmentAbsorber();
        reopened.Pages[2].Accept(absorber);
        Assert.NotEmpty(absorber.TextFragments);
        Assert.All(absorber.TextFragments, f => Assert.Equal("Courier", f.TextState.Font!.FontName));
    }

    [Fact]
    public void ACellFragmentNamingAStandardFaceDrawsInIt()
    {
        var table = new Table { ColumnWidths = "200" };
        // A fragment that declares its line box asks for the face it names; one that does not
        // (the generator dialects) keeps the table's face.
        var times = new TextFragment("Times cell");
        DeclareLineBox(times.TextState, 12);
        times.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        times.TextState.FontName = "Times-Roman";
        table.Rows.Add().Cells.Add().Paragraphs.Add(times);
        using var reopened = Document.Open(Laid(table));
        var absorber = new TextFragmentAbsorber();
        reopened.Pages[1].Accept(absorber);
        Assert.Equal("Times-Roman", Assert.Single(absorber.TextFragments).TextState.Font!.FontName);
    }

    [Fact]
    public void ContentSizedColumnsMeasureAnEmbeddedFace()
    {
        // The synthetic face's glyphs are all half an em wide; thirteen i's in it are 78 pt at
        // 12 pt where Helvetica's are 34.6, so the first column is wider when the face is measured.
        static double SecondColumnX(Font? face)
        {
            var table = new Table { SizesColumnsToContent = true, StretchesToBand = true };
            var row = table.Rows.Add();
            var wide = new TextFragment("iiiiiiiiiiiii");
            wide.TextState.FontSize = 12;
            if (face is not null) wide.TextState.Font = face;
            row.Cells.Add().Paragraphs.Add(wide);
            var narrow = new TextFragment("Hi");
            narrow.TextState.FontSize = 12;
            row.Cells.Add().Paragraphs.Add(narrow);
            using var reopened = Document.Open(Laid(table));
            var absorber = new TextFragmentAbsorber("Hi");
            reopened.Pages[1].Accept(absorber);
            return Assert.Single(absorber.TextFragments).Rectangle!.LLX;
        }
        Assert.True(SecondColumnX(EmbeddedFace()) > SecondColumnX(null) + 20);
    }

    private static TextFragment Segmented(params (string Text, Font? Face)[] runs)
    {
        var fragment = new TextFragment();
        DeclareLineBox(fragment.TextState, 12);
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.FormattingOptions.SegmentsFlowAsRuns = true;
        foreach (var (text, face) in runs)
        {
            var segment = new TextSegment(text);
            DeclareLineBox(segment.TextState, 12);
            if (face is not null) segment.TextState.Font = face;
            fragment.Segments.Add(segment);
        }
        return fragment;
    }

    /// <summary>The fragments laid on one page and saved.</summary>
    private static byte[] Laid(params BaseParagraph[] paragraphs)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        foreach (var paragraph in paragraphs) page.Paragraphs.Add(paragraph);
        return doc.ToArray();
    }

    /// <summary>The text read back from the saved file, in content order.</summary>
    private static List<TextFragment> Absorb(params TextFragment[] fragments)
    {
        using var reopened = Document.Open(Laid(fragments));
        var absorber = new TextFragmentAbsorber();
        reopened.Pages[1].Accept(absorber);
        return absorber.TextFragments.ToList();
    }

    /// <summary>Where every show of the saved page starts: its baseline as the content places
    /// it (the absorber reports an embedded face at its descriptor's descent instead).</summary>
    private static List<(double X, double Y)> Shows(params TextFragment[] fragments)
    {
        using var reopened = Document.Open(Laid(fragments));
        var shows = new List<(double X, double Y)>();
        double x = 0, y = 0;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case Operators.BT: x = y = 0; break;
                case Operators.SetTextMatrix tm: x = tm.Matrix.E; y = tm.Matrix.F; break;
                case Operators.MoveTextPosition td: x += td.X; y += td.Y; break;
                case Operators.ShowText or Operators.SetGlyphsPositionShowText: shows.Add((x, y)); break;
            }
        }
        return shows;
    }
}
