using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>A decoration set on text read off a page - an underline, a strike-out, a highlight, a link - is
/// drawn at save over the word where the page draws it then: from the place it was measured in, through every
/// matrix the page's content was redrawn through since the text was read (a turn, a resize), with the matrix
/// the content leaves in force where the decoration is drawn divided out; on a page whose content was never
/// redrawn, a decoration appended to it inherits what the content leaves in force at its end.</summary>
public class DecorationMatrixTests
{
    private const string Fonts = "<< /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>";
    private const string Word = "Hello";
    private const string WordAcross = "BT /F1 12 Tf 100 200 Td (Hello) Tj ET";
    private const string WordUp = "BT /F1 12 Tf 0 1 -1 0 300 100 Tm (Hello) Tj ET";
    // A scale the content sets after its text, outside any save: what a stream appended to it draws through.
    private const string TrailingScale = " 0.5 0 0 0.5 0 0 cm";
    // A word drawn at half size, then another matrix the content leaves in force.
    private const string ScaledWordThenMatrix = "0.5 0 0 0.5 0 0 cm BT /F1 24 Tf 200 400 Td (Hello) Tj ET 3 0 0 3 0 0 cm";
    // A word drawn at half size inside a save it restores: the content leaves no matrix in force.
    private const string ScaledWordInsideASave = "q 0.5 0 0 0.5 0 0 cm BT /F1 24 Tf 200 400 Td (Hello) Tj ET Q";
    // A word drawn at twice its size by a matrix the content leaves in force, drawing ink or none (an OCR layer).
    private const string DoubledWord = "2 0 0 2 0 0 cm BT /F1 6 Tf 50 100 Td (Hello) Tj ET";
    private const string DoubledInvisibleWord = "2 0 0 2 0 0 cm BT 3 Tr /F1 6 Tf 50 100 Td (Hello) Tj ET";
    // How far a rule or a box may stand off the box of the word it dresses.
    private const double DecorationReach = 2;
    private const string Underlined = "underline";
    private const string StruckOut = "strike-out";
    private const string Highlighted = "highlight";

    [Theory]
    [InlineData(Underlined, false)]
    [InlineData(Underlined, true)]
    [InlineData(StruckOut, false)]
    [InlineData(StruckOut, true)]
    [InlineData(Highlighted, true)]
    public void ADecorationOnAWordTheContentFollowsWithAMatrixDressesTheTurnedWord(string decoration, bool setAfterTheTurn)
    {
        using var doc = BuildPage(WordAcross + TrailingScale);
        var word = AbsorbWord(doc.Pages[1]);
        if (!setAfterTheTurn) Decorate(word.TextState, decoration);
        TurnByTheEditor(doc);
        if (setAfterTheTurn) Decorate(word.TextState, decoration);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>The resize's matrix stands ahead of the turn's wrap, so the content leaves it in force at its end
    /// (a resize to half the turned page's size, so the word keeps its shape); the page is saved before the read.</summary>
    [Theory]
    [InlineData(PageResize)]
    [InlineData(PageEditorSize)]
    public void AWordReadAfterATurnAndAResizeIsUnderlinedWhereItIsDrawn(string resize)
    {
        using var doc = BuildPage(WordUp);
        TurnByTheEditor(doc);
        if (resize == PageResize) doc.Pages[1].Resize(new PageSize(396, 306));
        else ResizeContent(doc, PageEditorSize, new PageSize(396, 306));
        SaveAndReopen(doc).Dispose();
        AbsorbWord(doc.Pages[1]).TextState.Underline = true;
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>A highlight goes in ahead of the content, so it is drawn from the run's matrix whatever the
    /// content leaves in force at its end.</summary>
    [Theory]
    [InlineData(ScaledWordThenMatrix)]
    [InlineData(ScaledWordInsideASave)]
    public void AHighlightOnAScaledWordOfAnUnturnedPageBacksTheWord(string content)
    {
        using var doc = BuildPage(content);
        AbsorbWord(doc.Pages[1]).TextState.BackgroundColor = Color.Yellow;
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Theory]
    [InlineData(Underlined, false)]
    [InlineData(Underlined, true)]
    [InlineData(StruckOut, true)]
    [InlineData(Highlighted, false)]
    [InlineData(Highlighted, true)]
    public void ADecorationOnAWordReadBeforeAResizeAndATurnDressesTheWord(string decoration, bool setAfterTheTurn)
    {
        using var doc = BuildPage(WordAcross);
        var page = doc.Pages[1];
        var word = AbsorbWord(page);
        if (!setAfterTheTurn) Decorate(word.TextState, decoration);
        page.Resize(PageSize.A4);
        TurnByTheEditor(doc);
        if (setAfterTheTurn) Decorate(word.TextState, decoration);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>The resize's matrix reaches the content when the operators it is held with are written: text read
    /// before then is read without it.</summary>
    [Theory]
    [InlineData(Underlined)]
    [InlineData(StruckOut)]
    [InlineData(Highlighted)]
    public void ADecorationOnAWordReadAfterAResizeDressesTheWord(string decoration)
    {
        using var doc = BuildPage(WordAcross);
        var page = doc.Pages[1];
        page.Resize(PageSize.A4);
        Decorate(AbsorbWord(page).TextState, decoration);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Theory]
    [InlineData(Underlined, PageEditorSize)]
    [InlineData(Highlighted, PageEditorSize)]
    [InlineData(Underlined, PageEditorZoom)]
    [InlineData(Highlighted, PageEditorZoom)]
    [InlineData(Underlined, FileEditorResize)]
    [InlineData(StruckOut, FileEditorResize)]
    public void ADecorationOnAWordReadBeforeAnEditorResizesTheContentDressesTheWord(string decoration, string resize)
    {
        using var doc = BuildPage(WordAcross);
        var word = AbsorbWord(doc.Pages[1]);
        ResizeContent(doc, resize);
        Decorate(word.TextState, decoration);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    [Fact]
    public void ALinkSetOnAWordReadBeforeAResizeCoversTheResizedWord()
    {
        using var doc = BuildPage(WordAcross);
        var page = doc.Pages[1];
        var word = AbsorbWord(page);
        page.Resize(PageSize.A4);
        word.Hyperlink = new WebHyperlink("https://example.com/");
        using var reopened = SaveAndReopen(doc);
        var drawn = AbsorbWord(reopened.Pages[1]).Rectangle!;
        var link = Assert.Single(reopened.Pages[1].Annotations).Rect!;
        Assert.True(Near(drawn, link), $"the link {link} does not cover the word {drawn}");
    }

    /// <summary>A highlight set on a segment of a run is placed where the reader reports the segment, in the page's
    /// space as the text was read - not taken back through the run's matrix, as the band of a whole run drawn at
    /// that size is.</summary>
    [Theory]
    [InlineData(DoubledWord, false)]
    [InlineData(DoubledWord, true)]
    [InlineData(DoubledInvisibleWord, true)]
    public void AHighlightOnASegmentOfAWordDrawnAtTwiceItsSizeBacksTheWord(string content, bool turnedAfter)
    {
        using var doc = BuildPage(content);
        AbsorbWord(doc.Pages[1]).Segments[1].TextState.BackgroundColor = Color.Yellow;
        if (turnedAfter) TurnByTheEditor(doc);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>Operators read off a page are held until save, when they are written back as its content: an
    /// editor's resize is written before them, so it is not overwritten by them.</summary>
    [Theory]
    [InlineData(PageEditorSize)]
    [InlineData(PageEditorZoom)]
    [InlineData(FileEditorResize)]
    public void AnEditorsResizeReachesAPageWhoseOperatorsWereRead(string resize)
    {
        using var doc = BuildPage(WordAcross);
        var page = doc.Pages[1];
        var before = AbsorbWord(page).Rectangle!;
        _ = page.Contents[1];
        ResizeContent(doc, resize);
        using var reopened = SaveAndReopen(doc);
        var after = AbsorbWord(reopened.Pages[1]).Rectangle!;
        Assert.True(System.Math.Abs(after.Width - before.Width / 2) <= DecorationReach, $"the word {after} is not half the word {before}");
    }

    [Theory]
    [InlineData(Underlined, PageEditorSize)]
    [InlineData(Highlighted, PageEditorZoom)]
    [InlineData(StruckOut, FileEditorResize)]
    public void ADecorationOnAWordReadBeforeAnEditorResizesAPageWhoseOperatorsWereReadDressesTheWord(string decoration, string resize)
    {
        using var doc = BuildPage(WordAcross);
        var page = doc.Pages[1];
        Decorate(AbsorbWord(page).TextState, decoration);
        _ = page.Contents[1];
        ResizeContent(doc, resize);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>A phrase search over the whole document reads each content stream of a page alone, from the
    /// identity: a matrix an earlier stream leaves in force reaches the rule as it reaches the word, by what the
    /// content leaves in force at its end.</summary>
    [Theory]
    [InlineData(Underlined)]
    [InlineData(StruckOut)]
    public void ARuleOnAWordADocumentSearchFoundUnderAMatrixAnEarlierStreamLeavesLiesOnTheWord(string decoration)
    {
        using var doc = BuildPage("2 0 0 2 0 0 cm", "BT /F1 6 Tf 50 100 Td (Hello) Tj ET");
        var absorber = new TextFragmentAbsorber(Word);
        absorber.Visit(doc);
        Decorate(Assert.Single(absorber.TextFragments.Cast<TextFragment>()).TextState, decoration);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    /// <summary>A matrix a caller puts at the head of the page's operators after the text was read draws the word
    /// through it, and the rule too, by what the content leaves in force at its end.</summary>
    [Theory]
    [InlineData(Underlined, false)]
    [InlineData(Underlined, true)]
    [InlineData(StruckOut, false)]
    [InlineData(StruckOut, true)]
    public void ARuleOnAWordReadBeforeACallerPutsAMatrixAtTheHeadOfThePageLiesOnTheWord(string decoration, bool throughTheAppender)
    {
        using var doc = BuildPage(WordAcross);
        var page = doc.Pages[1];
        Decorate(AbsorbWord(page).TextState, decoration);
        var half = new Aspose.Pdf.Operators.ConcatenateMatrix(0.5, 0, 0, 0.5, 0, 0);
        if (throughTheAppender)
        {
            page.ContentsAppender.AppendToBegin(half);
            page.ContentsAppender.UpdateData();
        }
        else page.Contents.Insert(1, half);
        using var reopened = SaveAndReopen(doc);
        AssertDecorationAtTheWord(reopened.Pages[1]);
    }

    private const string PageResize = "page resize";
    private const string PageEditorSize = "page editor size";
    private const string PageEditorZoom = "page editor zoom";
    private const string FileEditorResize = "file editor resize";

    /// <summary>Scale the page's content to half: the page editor's resize (to <paramref name="size"/>, A4 when
    /// none is given) or zoom, or the file editor's content resize.</summary>
    private static void ResizeContent(Document doc, string resize, PageSize? size = null)
    {
        if (resize == FileEditorResize)
        {
            new PdfFileEditor().ResizeContents(doc, PdfFileEditor.ContentsResizeParameters.ContentSizePercent(50, 50));
            return;
        }
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        if (resize == PageEditorSize) editor.PageSize = size ?? PageSize.A4;
        editor.Zoom = 0.5f;
        editor.ApplyChanges();
    }

    /// <summary>A page drawing <paramref name="contents"/>, one content stream each.</summary>
    private static Document BuildPage(params string[] contents)
    {
        var streams = contents.Length == 1 ? "4 0 R" : "[" + string.Join(" ", contents.Select((_, i) => $"{i + 4} 0 R")) + "]";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {streams} /Resources {Fonts} >>",
        };
        objects.AddRange(contents.Select(content => $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"));
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

    private static void TurnByTheEditor(Document doc)
    {
        var editor = new PdfPageEditor();
        editor.BindPdf(doc);
        editor.PageRotations = new Dictionary<int, int> { [1] = 90 };
    }

    private static TextFragment AbsorbWord(Page page)
    {
        var absorber = new TextFragmentAbsorber(Word);
        page.Accept(absorber);
        return Assert.Single(absorber.TextFragments.Cast<TextFragment>());
    }

    private static void Decorate(TextState state, string decoration)
    {
        if (decoration == Underlined) state.Underline = true;
        else if (decoration == StruckOut) state.StrikeOut = true;
        else state.BackgroundColor = Color.Yellow;
    }

    private static bool Near(Rectangle expected, Rectangle actual)
        => System.Math.Abs(actual.LLX - expected.LLX) <= DecorationReach && System.Math.Abs(actual.LLY - expected.LLY) <= DecorationReach
            && System.Math.Abs(actual.URX - expected.URX) <= DecorationReach && System.Math.Abs(actual.URY - expected.URY) <= DecorationReach;

    /// <summary>The one shape the page paints - the decoration - runs along the word, at its box (a form the
    /// content was moved into is no shape of its own).</summary>
    private static void AssertDecorationAtTheWord(Page page)
    {
        var word = AbsorbWord(page).Rectangle!;
        using var graphics = new Aspose.Pdf.Vector.GraphicsAbsorber();
        graphics.Visit(page);
        var shape = Assert.Single(graphics.Elements, e => e is not Aspose.Pdf.Vector.XFormPlacement).Rectangle;
        var atTheWord = shape.LLX >= word.LLX - DecorationReach && shape.LLY >= word.LLY - DecorationReach
            && shape.URX <= word.URX + DecorationReach && shape.URY <= word.URY + DecorationReach;
        var alongTheWord = System.Math.Max(shape.Width, shape.Height) >= System.Math.Max(word.Width, word.Height) / 2;
        Assert.True(atTheWord && alongTheWord, $"the decoration {shape} does not dress the word {word}");
    }
}
