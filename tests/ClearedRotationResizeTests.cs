using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>A 612 x 792 page turned a quarter by /Rotate 90 whose rotation is cleared before its size
/// is set: the resize turns the content the way the viewer showed it. What the page draws after it,
/// operators a caller holds in its list, decorations and links set on its text before or after a turn
/// (and on text read while the page was viewed turned), a rotation written since the clear and an
/// appearance its annotations share keep their places.</summary>
public class ClearedRotationResizeTests
{
    private const string Mark = "0 0 1 rg 100 200 50 30 re f";
    // The mark turned a quarter clockwise on the 612-wide page: (x, y) -> (y, 612 - x).
    private static readonly Rectangle TurnedMark = new(200, 462, 230, 512);
    private const double Tolerance = 0.5;
    private const string Fonts = "<< /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>";
    private const string Word = "Hello";
    // A word across the page, and one drawn up it - across the view a quarter turn shows.
    private const string WordAcross = "BT /F1 12 Tf 100 200 Td (Hello) Tj ET";
    private const string WordUp = "BT /F1 12 Tf 0 1 -1 0 300 100 Tm (Hello) Tj ET";
    // The same words drawn at half size under a scale the content leaves in force at its end.
    private const string ScaledWordAcross = "0.5 0 0 0.5 0 0 cm BT /F1 24 Tf 200 400 Td (Hello) Tj ET";
    private const string ScaledWordUp = "0.5 0 0 0.5 0 0 cm BT /F1 24 Tf 0 1 -1 0 600 200 Tm (Hello) Tj ET";
    // How far a rule or a highlight may stand off the box of the word it dresses.
    private const double DecorationReach = 2;

    private static Document Build(string content, string pageEntries = "", params string[] more)
        => BuildPage(content, 90, "<< >>", pageEntries, more);

    private static Document BuildPage(string content, int rotate, string resources, string pageEntries = "", params string[] more)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate {rotate} /Contents 4 0 R /Resources {resources} {pageEntries}>>"),
            Stream(content),
        };
        objects.AddRange(more);
        return Open(objects);
    }

    private static string Stream(string content) => $"<< /Length {content.Length} >>\nstream\n{content}\nendstream";

    /// <summary>A document of <paramref name="objects"/>, numbered from 1 in order.</summary>
    private static Document Open(List<string> objects)
    {
        var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s), 0, s.Length);
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
        ms.Position = 0;
        return new Document(ms);
    }

    private static Document SaveAndReopen(Document doc)
    {
        var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    private static void AssertNear(Rectangle expected, Rectangle actual)
    {
        var near = System.Math.Abs(actual.LLX - expected.LLX) <= Tolerance && System.Math.Abs(actual.LLY - expected.LLY) <= Tolerance
            && System.Math.Abs(actual.URX - expected.URX) <= Tolerance && System.Math.Abs(actual.URY - expected.URY) <= Tolerance;
        Assert.True(near, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void AStampAddedAfterTheResizeIsDrawnUprightWhereItIsPlaced() => AssertStampUprightAtTheFoot(Mark);

    [Fact]
    public void AStampAddedAfterTheResizeOfContentLeavingASaveOpenIsDrawnUpright() => AssertStampUprightAtTheFoot("q " + Mark);

    [Fact]
    public void ContentRestoringTwiceBelowItsStartIsTurnedWithThePage()
    {
        // Two Q with no q of their own ahead of the mark: malformed, but found in real files.
        using var doc = Build("Q Q " + Mark);
        var page = doc.Pages[1];
        page.Rotate = Rotation.None;
        page.SetPageSize(792, 612);
        using var reopened = SaveAndReopen(doc);
        AssertNear(TurnedMark, reopened.Pages[1].CalculateContentBBox());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACropInsideTheMediaBoxIsWhatTheResizeKeepsWhole(bool cropFromThePageTree)
    {
        // A fill over the whole media box, of which the crop shows the top half: 612 x 396, turned 396 x 612.
        const string crop = "/CropBox [0 396 612 792] ";
        const string fill = "0 0 1 rg 0 0 612 792 re f";
        using var doc = Open(new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [3 0 R] /Count 1 {(cropFromThePageTree ? crop : "")}>>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate 90 /Contents 4 0 R /Resources << >> {(cropFromThePageTree ? "" : crop)}>>",
            Stream(fill),
        });
        var page = doc.Pages[1];
        page.Rotate = Rotation.None;
        page.SetPageSize(396, 612);
        using var reopened = SaveAndReopen(doc);
        // The cropped half turned onto the page at full size; the half the crop hid lies off it, to the left.
        AssertNear(new Rectangle(-396, 0, 396, 612), reopened.Pages[1].CalculateContentBBox());
        AssertNear(new Rectangle(0, 0, 396, 612), reopened.Pages[1].CropBox);
    }

    [Fact]
    public void ACropAsLargeAsTheMediaBoxKeepsEverythingDrawnOnThePage()
    {
        using var doc = Build("0 0 1 rg 0 0 612 792 re f", "/CropBox [0 0 612 792] ");
        var page = doc.Pages[1];
        page.Rotate = Rotation.None;
        page.SetPageSize(396, 612);
        using var reopened = SaveAndReopen(doc);
        // The whole fill, turned 792 x 612, shrunk by half and centred on the 396 x 612 page.
        AssertNear(new Rectangle(0, 153, 396, 459), reopened.Pages[1].CalculateContentBBox());
    }

    [Fact]
    public void ContentWithAParenthesisClosingNoStringIsTurnedByThePageEditor()
    {
        // A ) that opens no string: malformed, and the content scan used to spin on it for ever.
        using var doc = Build(Mark + " ) 0 g");
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        editor.PageRotations = new Dictionary<int, int> { [1] = 90 };
        using var reopened = SaveAndReopen(doc);
        AssertNear(TurnedMark, reopened.Pages[1].CalculateContentBBox());
    }

    private static void AssertStampUprightAtTheFoot(string content)
    {
        using var doc = Build(content);
        var page = doc.Pages[1];
        page.Rotate = Rotation.None;
        page.SetPageSize(792, 612);
        page.AddStamp(new TextStamp("Stamped")
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        using var reopened = SaveAndReopen(doc);
        var absorber = new TextFragmentAbsorber("Stamped");
        reopened.Pages[1].Accept(absorber);
        var stamp = Assert.Single(absorber.TextFragments.Cast<TextFragment>()).Rectangle!;
        Assert.True(System.Math.Abs((stamp.LLX + stamp.URX) / 2 - 396) <= 1, $"the stamp {stamp} is not centred across the page");
        Assert.True(stamp.Width > stamp.Height, $"the stamp {stamp} does not read across the page");
        Assert.True(stamp.URY < 612 / 2.0, $"the stamp {stamp} is not at the foot of the page");
    }

    [Fact]
    public void OperatorsHeldInTheContentsListAreTurnedWithThePage()
    {
        using var doc = Build(Mark);
        var page = doc.Pages[1];
        // A square drawn through the operator list, above the mark: [100 400 120 420].
        page.Contents.Add(new Aspose.Pdf.Operators.Re(100, 400, 20, 20));
        page.Contents.Add(new Aspose.Pdf.Operators.Fill());
        page.Rotate = Rotation.None;
        page.SetPageSize(792, 612);
        using var reopened = SaveAndReopen(doc);
        // The mark and the square turned together: [100 200 150 420] -> [200 462 420 512].
        AssertNear(new Rectangle(TurnedMark.LLX, TurnedMark.LLY, 420, TurnedMark.URY), reopened.Pages[1].CalculateContentBBox());
    }

    [Fact]
    public void ARotationWrittenAgainSinceTheClearIsNotTurnedIntoTheContent()
    {
        using var doc = Build(Mark);
        var page = doc.Pages[1];
        page.Rotate = Rotation.None;
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        editor.Rotation = 90;
        page.SetPageSize(612, 792);
        using var reopened = SaveAndReopen(doc);
        Assert.Equal(90, reopened.Pages[1].RotateDegrees);
        AssertNear(new Rectangle(100, 200, 150, 230), reopened.Pages[1].CalculateContentBBox());
    }

    [Fact]
    public void ARotationBakedSinceTheClearIsNotTurnedASecondTime()
    {
        using var doc = Build(Mark);
        var page = doc.Pages[1];
        page.Rotate = Rotation.None;
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        editor.PageRotations = new Dictionary<int, int> { [1] = 90 };
        page.SetPageSize(792, 612);
        using var reopened = SaveAndReopen(doc);
        AssertNear(TurnedMark, reopened.Pages[1].CalculateContentBBox());
    }

    [Fact]
    public void AnAppearanceSharedByTheAnnotationsOfThePageIsTurnedOnce()
    {
        const string form = "0 0 20 20 re f";
        var annots = string.Join(" ", Enumerable.Range(5, 3).Select(n => $"{n} 0 R"));
        using var doc = Build(Mark, $"/Annots [{annots}] ",
            "<< /Type /Annot /Subtype /Square /Rect [100 100 120 120] /AP << /N 8 0 R >> >>",
            "<< /Type /Annot /Subtype /Square /Rect [200 100 220 120] /AP << /N 8 0 R >> >>",
            "<< /Type /Annot /Subtype /Square /Rect [300 100 320 120] /AP << /N 8 0 R >> >>",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 20 20] /Length {form.Length} >>\nstream\n{form}\nendstream");
        var page = doc.Pages[1];
        page.Rotate = Rotation.None;
        page.SetPageSize(792, 612);
        using var reopened = SaveAndReopen(doc);
        // The quarter turn [0 -1 1 0], composed once onto the identity.
        AssertAppearanceMatrix(reopened.Pages[1], 0, -1, 1, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAppearanceSharedByAnnotationsOfTwoTurnedPagesIsTurnedOnceForEach(bool byThePageEditor)
    {
        const string form = "0 0 20 20 re f";
        const string page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate 90 /Contents {0} 0 R /Resources << >> /Annots [{1} 0 R] >>";
        using var doc = Open(new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            string.Format(page, 5, 7),
            string.Format(page, 6, 8),
            Stream(Mark),
            Stream(Mark),
            "<< /Type /Annot /Subtype /Square /Rect [100 100 120 120] /AP << /N 9 0 R >> >>",
            "<< /Type /Annot /Subtype /Square /Rect [200 100 220 120] /AP << /N 9 0 R >> >>",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 20 20] /Length {form.Length} >>\nstream\n{form}\nendstream",
        });
        if (byThePageEditor)
        {
            var editor = new PdfPageEditor();
            editor.BindPdf(doc);
            editor.PageRotations = new Dictionary<int, int> { [1] = 90, [2] = 90 };
        }
        else
        {
            foreach (Page each in doc.Pages)
            {
                each.Rotate = Rotation.None;
                each.SetPageSize(792, 612);
            }
        }
        using var reopened = SaveAndReopen(doc);
        foreach (Page each in reopened.Pages) AssertAppearanceMatrix(each, 0, -1, 1, 0);
    }

    [Fact]
    public void APageTurnedTwiceTurnsItsAppearanceTwice()
    {
        const string form = "0 0 20 20 re f";
        using var doc = Build(Mark, "/Annots [5 0 R] ",
            "<< /Type /Annot /Subtype /Square /Rect [100 100 120 120] /AP << /N 6 0 R >> >>",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 20 20] /Length {form.Length} >>\nstream\n{form}\nendstream");
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        editor.PageRotations = new Dictionary<int, int> { [1] = 90 };
        editor.PageRotations = new Dictionary<int, int> { [1] = 90 };
        using var reopened = SaveAndReopen(doc);
        // Two quarter turns: the half turn [-1 0 0 -1].
        AssertAppearanceMatrix(reopened.Pages[1], -1, 0, 0, -1);
    }

    /// <summary>The normal appearance of the page's first annotation carries the matrix [a b c d 0 0].</summary>
    private static void AssertAppearanceMatrix(Page page, double a, double b, double c, double d)
    {
        var reader = page.Reader;
        var annot = reader.ResolveDict(((Core.PdfArray)reader.Resolve(page.Dict.Get("Annots"))!)[0])!;
        var appearance = reader.ResolveStream(reader.ResolveDict(annot.Get("AP"))!.Get("N"))!;
        var matrix = (Core.PdfArray)reader.Resolve(appearance.Dict.Get("Matrix"))!;
        var expected = new[] { a, b, c, d, 0, 0 };
        var actual = Enumerable.Range(0, expected.Length).Select(i => Num(reader.Resolve(matrix[i]))).ToArray();
        Assert.True(expected.Zip(actual, (e, v) => System.Math.Abs(e - v) < 1e-6).All(ok => ok),
            $"page {page.Number}: the appearance matrix is [{string.Join(" ", actual)}]");
    }

    [Fact]
    public void AnUnderlineSetBeforeThePageEditorTurnsThePageSitsUnderTheTurnedWord()
    {
        using var doc = BuildPage(WordAcross, 0, Fonts);
        Dress(doc.Pages[1], state => state.Underline = true);
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        editor.PageRotations = new Dictionary<int, int> { [1] = 90 };
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void AnUnderlineSetBeforeTheResizeSitsUnderTheTurnedAndShrunkWord()
    {
        // A second word up the page makes the turned content overrun the new box, so it is shrunk.
        using var doc = BuildPage(WordUp + " BT /F1 12 Tf 0 1 -1 0 300 600 Tm (World) Tj ET", 90, Fonts);
        var page = doc.Pages[1];
        Dress(page, state => state.Underline = true);
        page.Rotate = Rotation.None;
        page.SetPageSize(396, 306);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void AHighlightSetBeforeThePageEditorTurnsThePageLiesUnderTheTurnedWord()
    {
        using var doc = BuildPage(WordAcross, 0, Fonts);
        Dress(doc.Pages[1], state => state.BackgroundColor = Color.Yellow);
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        editor.PageRotations = new Dictionary<int, int> { [1] = 90 };
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void AHighlightReplacingASourceBoxOnAPageTheEditorTurnsKeepsTheTurn()
    {
        // The box behind a word drawn up a quarter-turned page, captured so a new highlight replaces it.
        using var doc = BuildPage("1 1 0 rg 288 100 14 28 re f 0 g " + WordUp, 90, Fonts);
        var page = doc.Pages[1];
        var absorber = new TextFragmentAbsorber(Word)
        {
            TextEditOptions = new TextEditOptions(TextEditOptions.FontReplace.Default) { ToAttemptGetUnderlineFromSource = true },
        };
        page.Accept(absorber);
        Assert.Single(absorber.TextFragments.Cast<TextFragment>()).TextState.BackgroundColor = Color.Red;
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        editor.PageRotations = new Dictionary<int, int> { [1] = 90 };
        using var reopened = SaveAndReopen(doc);
        var found = new TextFragmentAbsorber(Word);
        reopened.Pages[1].Accept(found);
        // The word up the page turned with it reads across: [289.3 100 302.5 127.3] -> [100 309.5 127.3 322.7].
        AssertNear(new Rectangle(100, 309.516, 127.336, 322.716), Assert.Single(found.TextFragments.Cast<TextFragment>()).Rectangle!);
    }

    [Theory]
    [InlineData(Underlined)]
    [InlineData(StruckOut)]
    public void ADecorationSetAfterThePageEditorTurnsThePageDressesTheTurnedWord(string decoration)
    {
        using var doc = BuildPage(WordAcross, 0, Fonts);
        var word = AbsorbWord(doc.Pages[1]);
        TurnByTheEditor(doc);
        Decorate(word.TextState, decoration);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void AnUnderlineSetAfterTheResizeSitsUnderTheTurnedAndShrunkWord()
    {
        using var doc = BuildPage(WordUp + " BT /F1 12 Tf 0 1 -1 0 300 600 Tm (World) Tj ET", 90, Fonts);
        var page = doc.Pages[1];
        var word = AbsorbWord(page);
        page.Rotate = Rotation.None;
        page.SetPageSize(396, 306);
        word.TextState.Underline = true;
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>A rule is written in the space of the run it dresses and drawn through the scale the content
    /// leaves in force: the turn wraps that scale with the run, so the rule takes it with the turn.</summary>
    [Theory]
    [InlineData(Underlined, TurnedByTheEditor, false)]
    [InlineData(Underlined, TurnedByTheEditor, true)]
    [InlineData(StruckOut, TurnedByTheEditor, false)]
    [InlineData(StruckOut, TurnedByTheEditor, true)]
    [InlineData(Underlined, ClearedAndResized, false)]
    [InlineData(Underlined, ClearedAndResized, true)]
    [InlineData(StruckOut, ClearedAndResized, true)]
    public void ARuleOnContentLeavingAScaleInForceDressesTheTurnedWord(string decoration, string turn, bool setAfterTheTurn)
    {
        // Cleared and resized: a word up the content reads across the view the page was turned to.
        using var doc = turn == ClearedAndResized ? BuildPage(ScaledWordUp, 90, Fonts) : BuildPage(ScaledWordAcross, 0, Fonts);
        var page = doc.Pages[1];
        var word = AbsorbWord(page);
        if (!setAfterTheTurn) Decorate(word.TextState, decoration);
        if (turn == ClearedAndResized)
        {
            page.Rotate = Rotation.None;
            page.SetPageSize(792, 612);
        }
        else TurnByTheEditor(doc);
        if (setAfterTheTurn) Decorate(word.TextState, decoration);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Theory]
    [InlineData(Underlined)]
    [InlineData(Highlighted)]
    public void ADecorationSetBeforeTwoTurnsWithAResizeBetweenDressesTheWord(string decoration)
    {
        // The resize scales the content through a cm ahead of the first turn's wrap; the second turn wraps both.
        using var doc = BuildPage(WordAcross, 0, Fonts);
        var page = doc.Pages[1];
        Decorate(AbsorbWord(page).TextState, decoration);
        TurnByTheEditor(doc);
        page.Resize(PageSize.A4);
        TurnByTheEditor(doc);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>Text read after a half turn draws under the turn's matrix, which the turn's wrap pops at the end
    /// of the content: a rule written in the run's space is drawn through the run's own matrix.</summary>
    [Theory]
    [InlineData(Underlined, false, false, false)]
    [InlineData(StruckOut, false, false, false)]
    [InlineData(Underlined, true, false, false)]
    [InlineData(Underlined, false, true, false)]
    [InlineData(Underlined, false, false, true)]
    public void ARuleOnAWordReadAfterAHalfTurnDressesTheWord(string decoration, bool scaled, bool clearedAndResized, bool turnedAgain)
    {
        var content = scaled ? ScaledWordAcross : WordAcross;
        using var doc = BuildPage(content, clearedAndResized ? 180 : 0, Fonts);
        var page = doc.Pages[1];
        if (clearedAndResized)
        {
            page.Rotate = Rotation.None;
            page.SetPageSize(612, 792);
        }
        else TurnByTheEditor(doc, 180);
        Decorate(AbsorbWord(page).TextState, decoration);
        if (turnedAgain) TurnByTheEditor(doc);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Theory]
    [InlineData(Underlined)]
    [InlineData(StruckOut)]
    public void ARuleOnAWordReadAcrossThePageAfterAQuarterTurnDressesTheWord(string decoration)
    {
        // Drawn up the page, the word reads across it once the page editor has turned it a quarter.
        using var doc = BuildPage(WordUp, 0, Fonts);
        TurnByTheEditor(doc);
        Decorate(AbsorbWord(doc.Pages[1]).TextState, decoration);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void ARuleAlongAWordReadDownATurnedPageWhoseContentLeavesAScaleStaysUnderTheTurnedWord()
    {
        // Laid along the run at the place the reader reported, a rule takes the turn alone: the scale is in that place.
        using var doc = BuildPage(ScaledWordAcross, 90, Fonts);
        var page = doc.Pages[1];
        Dress(page, state => state.Underline = true);
        page.Rotate = Rotation.None;
        page.SetPageSize(792, 612);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void AnUnderlineSetBeforeAndAfterTheTurnIsDrawnOnceUnderTheTurnedWord()
    {
        using var doc = BuildPage(WordAcross, 0, Fonts);
        var word = AbsorbWord(doc.Pages[1]);
        word.TextState.Underline = true;
        TurnByTheEditor(doc);
        word.TextState.Underline = true;
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void AWordReadAfterTheTurnIsUnderlinedWithoutTurningAgain()
    {
        using var doc = BuildPage(WordAcross, 0, Fonts);
        TurnByTheEditor(doc);
        AbsorbWord(doc.Pages[1]).TextState.Underline = true;
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>A word drawn across the content of a page viewed a quarter turned reads down the view.</summary>
    [Theory]
    [InlineData(Underlined, Unturned)]
    [InlineData(Highlighted, Unturned)]
    [InlineData(StruckOut, Unturned)]
    [InlineData(Underlined, ClearedAndResized)]
    [InlineData(Highlighted, ClearedAndResized)]
    [InlineData(Underlined, TurnedByTheEditor)]
    [InlineData(Highlighted, TurnedByTheEditor)]
    public void ADecorationOnAWordReadOnATurnedPageDressesTheWord(string decoration, string turn)
    {
        using var doc = BuildPage(WordAcross, 90, Fonts);
        var page = doc.Pages[1];
        Decorate(AbsorbWord(page).TextState, decoration);
        if (turn == ClearedAndResized)
        {
            page.Rotate = Rotation.None;
            page.SetPageSize(792, 612);
        }
        else if (turn == TurnedByTheEditor) TurnByTheEditor(doc);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>A word drawn across the content of a page viewed half turned reads across the view, backwards.</summary>
    [Theory]
    [InlineData(Highlighted, Unturned)]
    [InlineData(Highlighted, TurnedByTheEditor)]
    [InlineData(Highlighted, ClearedAndResized)]
    [InlineData(Underlined, Unturned)]
    public void ADecorationOnAWordReadOnAHalfTurnedPageDressesTheWord(string decoration, string turn)
    {
        using var doc = BuildPage(WordAcross, 180, Fonts);
        Decorate(AbsorbWord(doc.Pages[1]).TextState, decoration);
        Turn(doc, turn);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void AHighlightReplacingASourceRuleBeforeTheTurnStandsWhereTheTurnedRunIsDrawn()
    {
        using var doc = BuildPage("0 g 100 197 27.3 0.7 re f " + WordAcross, 0, Fonts);
        var absorber = new TextFragmentAbsorber(Word)
        {
            TextEditOptions = new TextEditOptions(TextEditOptions.FontReplace.Default) { ToAttemptGetUnderlineFromSource = true },
        };
        doc.Pages[1].Accept(absorber);
        Assert.Single(absorber.TextFragments.Cast<TextFragment>()).TextState.BackgroundColor = Color.Red;
        TurnByTheEditor(doc);
        using var reopened = SaveAndReopen(doc);
        // Sought where the turn took the run, the highlight goes in just before it: after the rule the
        // source draws first, rather than ahead of all the page draws.
        var content = Compat.Latin1.GetString(reopened.Pages[1].GetDecodedContentBytes());
        var rule = content.IndexOf("0.7 re", System.StringComparison.Ordinal);
        var box = content.IndexOf("1 0 0 rg", System.StringComparison.Ordinal);
        var run = content.IndexOf("BT", System.StringComparison.Ordinal);
        Assert.True(rule >= 0 && rule < box && box < run, content);
    }

    [Fact]
    public void AHighlightReplacingASourceBoxUnderAWordReadAtItsDescentGoesInJustBeforeTheTurnedRun()
    {
        // A face whose descriptor reads a descent of 212: the reader seats the word 2.5 points below its baseline.
        using var doc = BuildPage("1 1 0 rg 100 197 27.3 12 re f 0 g " + WordAcross, 0, "<< /Font << /F1 5 0 R >> >>", "",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /FontDescriptor 6 0 R >>",
            "<< /Type /FontDescriptor /FontName /Helvetica /Flags 32 /FontBBox [-166 -225 1000 931] /ItalicAngle 0 /Ascent 718 /Descent -212 /CapHeight 718 /StemV 88 >>");
        var absorber = new TextFragmentAbsorber(Word)
        {
            TextEditOptions = new TextEditOptions(TextEditOptions.FontReplace.Default) { ToAttemptGetUnderlineFromSource = true },
        };
        doc.Pages[1].Accept(absorber);
        Assert.Single(absorber.TextFragments.Cast<TextFragment>()).TextState.BackgroundColor = Color.Red;
        TurnByTheEditor(doc);
        using var reopened = SaveAndReopen(doc);
        // Over the source's own box, just before the run: not ahead of all the page draws, under that box.
        var content = Compat.Latin1.GetString(reopened.Pages[1].GetDecodedContentBytes());
        var source = content.IndexOf("1 1 0 rg", System.StringComparison.Ordinal);
        var box = content.IndexOf("1 0 0 rg", System.StringComparison.Ordinal);
        var run = content.IndexOf("BT", System.StringComparison.Ordinal);
        Assert.True(source >= 0 && source < box && box < run, content);
    }

    // A source rule under a word drawn up a page viewed a quarter turned - under it across the view - and
    // the same rule under a run of two words, running on to 153 across the view.
    private const string RuledWordUp = "0 g 302.3 100 0.7 27.3 re f " + WordUp;
    private const string RuledRunUp = "0 g 302.3 100 0.7 53 re f BT /F1 12 Tf 0 1 -1 0 300 100 Tm (Big Hello) Tj ET";
    private const double RuledRunEnd = 153;

    private static TextFragment AbsorbWithSourceDecorations(Page page)
    {
        var absorber = new TextFragmentAbsorber(Word)
        {
            TextEditOptions = new TextEditOptions(TextEditOptions.FontReplace.Default) { ToAttemptGetUnderlineFromSource = true },
        };
        page.Accept(absorber);
        return Assert.Single(absorber.TextFragments.Cast<TextFragment>());
    }

    private static void Turn(Document doc, string turn)
    {
        if (turn == ClearedAndResized)
        {
            doc.Pages[1].Rotate = Rotation.None;
            doc.Pages[1].SetPageSize(792, 612);
        }
        else if (turn == TurnedByTheEditor) TurnByTheEditor(doc);
    }

    [Theory]
    [InlineData(Unturned)]
    [InlineData(TurnedByTheEditor)]
    [InlineData(ClearedAndResized)]
    public void AWordReplacedOnAQuarterTurnedPageKeepsTheSourceRuleUnderIt(string turn)
    {
        using var doc = BuildPage(RuledWordUp, 90, Fonts);
        AbsorbWithSourceDecorations(doc.Pages[1]).Text = "Hallo";
        Turn(doc, turn);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1], "Hallo");
    }

    [Theory]
    [InlineData(Unturned)]
    [InlineData(TurnedByTheEditor)]
    public void AWordReplacedInsideARunOnAQuarterTurnedPageKeepsTheRuleUnderTheRun(string turn)
    {
        // No text object starts at the word, so the pieces of the rule are drawn over the page.
        using var doc = BuildPage(RuledRunUp, 90, Fonts);
        AbsorbWithSourceDecorations(doc.Pages[1]).Text = "Hallo";
        Turn(doc, turn);
        using var reopened = SaveAndReopen(doc);
        var shapes = AssertRulesAlongTheLine(reopened.Pages[1], "Big Hallo", RuledRunEnd);
        Assert.True(shapes.Min(s => s.LLX) <= 100 + DecorationReach && shapes.Max(s => s.URX) >= RuledRunEnd - DecorationReach,
            $"the rules [{string.Join("; ", shapes)}] do not run under the run to the end of the source rule");
    }

    [Fact]
    public void AnUnderlineSwitchedOffOnAWordOfAQuarterTurnedPageLeavesTheSourceRulePastItsRun()
    {
        // The source rule runs past the run "Hello Big" to 160 across the view: what lies past the run stays.
        using var doc = BuildPage("0 g 302.3 100 0.7 60 re f BT /F1 12 Tf 0 1 -1 0 300 100 Tm (Hello Big) Tj ET", 90, Fonts);
        AbsorbWithSourceDecorations(doc.Pages[1]).TextState.Underline = false;
        using var reopened = SaveAndReopen(doc);
        var rule = Assert.Single(AssertRulesAlongTheLine(reopened.Pages[1], "Big", 160));
        Assert.True(rule.URX >= 160 - DecorationReach, $"the rule {rule} does not run on to the end of the source rule");
    }

    [Theory]
    [InlineData(Unturned)]
    [InlineData(TurnedByTheEditor)]
    public void AHighlightReplacingASourceBoxOnAQuarterTurnedPageGoesInJustBeforeTheRun(string turn)
    {
        using var doc = BuildPage("1 1 0 rg 290 100 14 27.3 re f 0 g " + WordUp, 90, Fonts);
        AbsorbWithSourceDecorations(doc.Pages[1]).TextState.BackgroundColor = Color.Red;
        Turn(doc, turn);
        using var reopened = SaveAndReopen(doc);
        var content = Compat.Latin1.GetString(reopened.Pages[1].GetDecodedContentBytes());
        var source = content.IndexOf("1 1 0 rg", System.StringComparison.Ordinal);
        var box = content.IndexOf("1 0 0 rg", System.StringComparison.Ordinal);
        var run = content.IndexOf("BT", System.StringComparison.Ordinal);
        Assert.True(source >= 0 && source < box && box < run, content);
        var word = AbsorbWord(reopened.Pages[1]).Rectangle!;
        using var graphics = new Aspose.Pdf.Vector.GraphicsAbsorber();
        graphics.Visit(reopened.Pages[1]);
        foreach (var element in graphics.Elements)
        {
            var shape = reopened.Pages[1].RotationMatrix.Transform(element.Rectangle);
            Assert.True(System.Math.Abs(shape.LLX - word.LLX) <= DecorationReach && System.Math.Abs(shape.LLY - word.LLY) <= DecorationReach
                && System.Math.Abs(shape.URX - word.URX) <= DecorationReach && System.Math.Abs(shape.URY - word.URY) <= DecorationReach,
                $"the box {shape} does not back the word {word}");
        }
    }

    /// <summary>Every shape the page paints is a rule across the line of <paramref name="text"/>, in the view the
    /// reader reports it in, between the start of the text and <paramref name="lineEnd"/>; the shapes, in that view.</summary>
    private static List<Rectangle> AssertRulesAlongTheLine(Page page, string text, double lineEnd)
    {
        var absorber = new TextFragmentAbsorber(text);
        page.Accept(absorber);
        var line = Assert.Single(absorber.TextFragments.Cast<TextFragment>()).Rectangle!;
        using var graphics = new Aspose.Pdf.Vector.GraphicsAbsorber();
        graphics.Visit(page);
        var shapes = graphics.Elements.Select(element => page.RotationMatrix.Transform(element.Rectangle)).ToList();
        Assert.NotEmpty(shapes);
        foreach (var shape in shapes)
            Assert.True(shape.Width > shape.Height && shape.LLY >= line.LLY - DecorationReach && shape.URY <= line.URY + DecorationReach
                && shape.LLX >= line.LLX - DecorationReach && shape.URX <= lineEnd + DecorationReach,
                $"the shape {shape} is no rule along the line {line} to {lineEnd}");
        return shapes;
    }

    [Fact]
    public void AnUnderlinedRunAppendedTurnedOnATurnedPageKeepsItsRuleUnderIt()
    {
        // A writer draws a run at its position in the page's own space, whatever the page is viewed under.
        using var doc = BuildPage("0 g", 90, Fonts);
        var run = new TextFragment(Word) { Position = new Position(300, 300) };
        run.TextState.FontSize = 12;
        run.TextState.Rotation = 90;
        run.TextState.Underline = true;
        new TextBuilder(doc.Pages[1]).AppendText(run);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void ALinkSetOnARunAppendedToATurnedPageCoversTheRunWhereTheWriterSeatsIt()
    {
        // A writer seats a run in the page's own space whatever the page is viewed under, and its link with it.
        using var doc = BuildPage("0 g", 90, Fonts);
        var run = new TextFragment(Word) { Position = new Position(300, 300) };
        run.TextState.FontSize = 12;
        new TextBuilder(doc.Pages[1]).AppendText(run);
        run.Hyperlink = new WebHyperlink("https://example.com/");
        using var reopened = SaveAndReopen(doc);
        var page = reopened.Pages[1];
        // The reader reports the run in the turned view, so the link is read in that view too.
        var word = AbsorbWord(page).Rectangle!;
        var link = page.RotationMatrix.Transform(Assert.Single(page.Annotations).Rect!);
        var covers = System.Math.Abs(link.LLX - word.LLX) <= DecorationReach && System.Math.Abs(link.LLY - word.LLY) <= DecorationReach
            && System.Math.Abs(link.URX - word.URX) <= DecorationReach && System.Math.Abs(link.URY - word.URY) <= DecorationReach;
        Assert.True(covers, $"the link {link} does not cover the run {word}");
    }

    [Fact]
    public void DecorationsSetBeforeTheTurnLeaveNoObjectNothingReferences()
    {
        using var doc = BuildPage(WordAcross, 0, Fonts);
        Dress(doc.Pages[1], state => { state.Underline = true; state.StrikeOut = true; state.BackgroundColor = Color.Yellow; });
        TurnByTheEditor(doc);
        var saved = new MemoryStream();
        doc.Save(saved);
        // The page's first content stream (object 4) is written as the page held it, as it was before the
        // turn popped its matrix: nothing else may stand in the file unreferenced.
        Assert.All(Unreferenced(saved.ToArray(), 0), number => Assert.Equal(4, number));
    }

    [Fact]
    public void AnIncrementalSaveAfterTheTurnWritesEachObjectOnceAndReferenced()
    {
        var source = new MemoryStream();
        using (var built = BuildPage(WordAcross, 0, Fonts)) built.Save(source);
        var original = (int)source.Length;
        source.Position = 0;
        using (var doc = new Document(source))
        {
            Dress(doc.Pages[1], state => state.Underline = true);
            TurnByTheEditor(doc);
            // A stamp takes objects of its own and puts the page, with its turned content, in the update.
            doc.Pages[1].AddStamp(new TextStamp("Stamped") { XIndent = 300, YIndent = 300 });
            doc.Save();
        }
        var bytes = source.ToArray();
        var update = Objects(bytes, original);
        Assert.True(update.Count == update.Distinct().Count(), $"the update writes [{string.Join(" ", update)}]");
        Assert.Empty(Unreferenced(bytes, original));
        using var reopened = new Document(new MemoryStream(bytes));
        var stamp = new TextFragmentAbsorber("Stamped");
        reopened.Pages[1].Accept(stamp);
        Assert.Single(stamp.TextFragments.Cast<TextFragment>());
        AssertNear(new Rectangle(197.516, 484.664, 210.716, 512), AbsorbWord(reopened.Pages[1]).Rectangle!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALinkSetBeforeTheTurnCoversTheTurnedWord(bool clearedAndShrunk)
    {
        // Two words up a quarter-turned page: turned onto a page half their length, they are shrunk.
        using var doc = clearedAndShrunk
            ? BuildPage(WordUp + " BT /F1 12 Tf 0 1 -1 0 300 600 Tm (World) Tj ET", 90, Fonts)
            : BuildPage(WordAcross, 0, Fonts);
        var page = doc.Pages[1];
        AbsorbWord(page).Hyperlink = new WebHyperlink("https://example.com/");
        if (clearedAndShrunk)
        {
            page.Rotate = Rotation.None;
            page.SetPageSize(396, 306);
        }
        else TurnByTheEditor(doc);
        using var reopened = SaveAndReopen(doc);
        var word = AbsorbWord(reopened.Pages[1]).Rectangle!;
        var link = Assert.Single(reopened.Pages[1].Annotations).Rect!;
        AssertNear(word, link);
    }

    private const string Underlined = "underline";
    private const string StruckOut = "strike-out";
    private const string Highlighted = "highlight";
    private const string Unturned = "unturned";
    private const string ClearedAndResized = "cleared and resized";
    private const string TurnedByTheEditor = "turned by the page editor";

    private static void Decorate(TextState state, string decoration)
    {
        if (decoration == Underlined) state.Underline = true;
        else if (decoration == StruckOut) state.StrikeOut = true;
        else state.BackgroundColor = Color.Yellow;
    }

    private static void TurnByTheEditor(Document doc, int degrees = 90)
    {
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        editor.PageRotations = new Dictionary<int, int> { [1] = degrees };
    }

    private static TextFragment AbsorbWord(Page page)
    {
        var absorber = new TextFragmentAbsorber(Word);
        page.Accept(absorber);
        return Assert.Single(absorber.TextFragments.Cast<TextFragment>());
    }

    /// <summary>Absorb the word on the page and set a decoration on it, drawn when the page is saved.</summary>
    private static void Dress(Page page, System.Action<TextState> dress) => dress(AbsorbWord(page).TextState);

    /// <summary>The numbers of the objects the file defines from <paramref name="from"/> on, in order.</summary>
    private static List<int> Objects(byte[] file, int from)
        => Regex.Matches(OutsideStreams(file, from), @"(?<!\d)(\d+) 0 obj\b").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();

    /// <summary>The objects defined from <paramref name="from"/> on that nothing in the file refers to.</summary>
    private static List<int> Unreferenced(byte[] file, int from)
    {
        var text = OutsideStreams(file, 0);
        return Objects(file, from).Where(n => !Regex.IsMatch(text, $@"(?<!\d){n} 0 R\b")).ToList();
    }

    private static string OutsideStreams(byte[] file, int from)
        => Regex.Replace(Compat.Latin1.GetString(file, from, file.Length - from), @"stream\r?\n.*?endstream", "", RegexOptions.Singleline);

    /// <summary>The one shape the page paints - the decoration - runs along the word, at its box. The text
    /// absorber reports a page viewed turned in the turned view, so the shape is read in that view too.</summary>
    private static void AssertDecorationAtTheWord(Page page, string text = Word)
    {
        var absorber = new TextFragmentAbsorber(text);
        page.Accept(absorber);
        var word = Assert.Single(absorber.TextFragments.Cast<TextFragment>()).Rectangle!;
        using var graphics = new Aspose.Pdf.Vector.GraphicsAbsorber();
        graphics.Visit(page);
        var shape = page.RotationMatrix.Transform(Assert.Single(graphics.Elements).Rectangle);
        var atTheWord = shape.LLX >= word.LLX - DecorationReach && shape.LLY >= word.LLY - DecorationReach
            && shape.URX <= word.URX + DecorationReach && shape.URY <= word.URY + DecorationReach;
        var alongTheWord = System.Math.Max(shape.Width, shape.Height) >= System.Math.Max(word.Width, word.Height) / 2;
        Assert.True(atTheWord && alongTheWord, $"the decoration {shape} does not dress the word {word}");
    }

    private static double Num(Core.PdfObject? value) => value switch
    {
        Core.PdfInteger i => i.Value,
        Core.PdfReal r => r.Value,
        _ => double.NaN,
    };
}
