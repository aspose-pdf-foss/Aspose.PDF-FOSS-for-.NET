using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>A table cell's picture under the sheet's `img { max-width: 100% }`: what caps it, what it asks
/// of its column, and where it draws once capped.</summary>
public class HtmlToPdfCellPictureCapTests
{
    // (the sized sheet box is a layout rule: the page is laid out on its sheet's geometry, its grids as grids)
    private const string Head = "<!DOCTYPE html><html><head><style>img { max-width: {0}; } .sheet { width: 500px; }</style></head>"
        + "<body><div class='sheet'><p>Station log</p>";

    private const string WideText = "Readings are taken at every station along the line and written down here in full,"
        + " one after another, until the whole sheet is full of them";

    // a 2 x 2 px picture
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGNQSDgARAwQCgAgjgUB59mTewAAAABJRU5ErkJggg==";
    // a 300 x 60 px picture
    private const string WidePng = "iVBORw0KGgoAAAANSUhEUgAAASwAAAA8CAIAAABTvhANAAAAmElEQVR42u3TMQ0AAAgEsZeCNKQijREJLE2q4JJL9QCPIgGYEEwImBBMCJgQTAiYEEwImBBMCJgQTAiYEEwImBBMCJgQTAiYEEwImBBMCJgQTAiYEEwImBBMCJgQTAiYEEwImBBMCJgQTAiYEEwImBBMCJgQTAiYEEwImBBMCJgQTAiYEEwImBBMCJgQTAiYEEwImBBMCJwFVrTpGrZsQW8AAAAASUVORK5CYII=";
    // a 600 x 100 px picture (a high-density banner, its file larger than the box it is shown at)
    private const string RetinaPng = "iVBORw0KGgoAAAANSUhEUgAAAlgAAABkCAIAAADVI9l0AAABTElEQVR42u3VQQ0AAAjEsJOCNKQiDQM4oEkV7LNUDwC8FQkAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAMEIAjFAFAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAIwQAC4L36EJLGdzHkcAAAAASUVORK5CYII=";

    // the check box glyph the over-declared grid draws for a small picture amid cell text
    private const string TickMark = "☑";

    // a row above the picture's that holds the first column at 300 px: a declared spacer the sheet's cap leaves alone
    private const string HeldColumnRow = "<tr><td><img src='data:image/png;base64," + TinyPng + "' width='300' height='4'"
        + " style='max-width:none'></td><td>A</td></tr>";

    // a row above the picture's that holds the first column at the given width
    private static string HeldColumn(int px) => "<tr><td><img src='data:image/png;base64," + TinyPng + "' width='" + px + "' height='4'"
        + " style='max-width:none'></td><td>A</td></tr>";

    private static Document ConvertGrid(string firstCell, string sheetCap = "100%", string secondCell = WideText, string rowAbove = "")
        => ConvertHtml(Head.Replace("{0}", sheetCap) + "<table style='width:400px'>" + rowAbove + "<tr><td>" + firstCell + "</td><td>"
            + secondCell + "</td></tr></table></div></body></html>");

    // a grid whose row declares more than its whole width (110%) at a fixed layout: the over-declared grid dialect
    private static Document ConvertOverDeclaredGrid(string firstCell)
        => ConvertHtml("<!DOCTYPE html><html><body><table width='100%' style='table-layout:fixed'><tr><td width='60%'>" + firstCell
            + "</td><td width='50%'>B</td></tr></table></body></html>");

    private static Document ConvertHtml(string html)
    {
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    private static Rectangle[] Pictures(Document doc)
    {
        var absorber = new ImagePlacementAbsorber();
        absorber.Visit(doc.Pages[1]);
        return absorber.ImagePlacements.Select(p => p.Rectangle).ToArray();
    }

    private static (string Text, Rectangle Box)[] Fragments(Document doc)
    {
        var absorber = new TextFragmentAbsorber();
        doc.Pages[1].Accept(absorber);
        return absorber.TextFragments.Cast<TextFragment>()
            .Where(f => f.Text.Trim().Length > 0 && f.Rectangle is not null).Select(f => (f.Text, f.Rectangle!)).ToArray();
    }

    // the left edge of the wide text's column: its leftmost line
    private static double TextColumnLeft(Document doc)
        => Fragments(doc).Where(f => WideText.Contains(f.Text.Trim())).Min(f => f.Box.LLX);

    // every picture's and text run's place on the page, to the tenth of a point
    private static string Layout(Document doc)
        => string.Join(" ", Pictures(doc).Select(r => FormattableString.Invariant($"[{r.LLX:0.#} {r.LLY:0.#} {r.URX:0.#} {r.URY:0.#}]"))
            .Concat(Fragments(doc).Select(f => FormattableString.Invariant($"{f.Text.Trim()}@{f.Box.LLX:0.#},{f.Box.LLY:0.#}"))));

    private static string Logo(string ownStyle) => "<img src='data:image/png;base64," + TinyPng + "' width='300' height='60'" + ownStyle + ">";

    private static string Wide(string box) => Picture(WidePng, box);

    private static string Picture(string png, string box) => "<img src='data:image/png;base64," + png + "'" + box + ">";

    [Theory]
    [InlineData("none")]
    [InlineData("600px")]
    public void APicturesOwnMaxWidthOverridesTheSheetCap(string ownMaxWidth)
    {
        // The 300 x 60 px logo keeps its 225 x 45 pt box and floors its column, as it does under no cap.
        using var doc = ConvertGrid(Logo(" style='max-width:" + ownMaxWidth + "'"));
        var logo = Assert.Single(Pictures(doc));
        Assert.Equal(225, logo.Width, 0.5);
        Assert.Equal(45, logo.Height, 0.5);
        Assert.True(logo.URX <= TextColumnLeft(doc), $"the logo stands in its own column: {logo.URX:0.#} vs {TextColumnLeft(doc):0.#}");
    }

    [Theory]
    [InlineData("100px")]
    [InlineData("75pt")]
    public void APicturesOwnMaxWidthLengthCapsItsBox(string ownMaxWidth)
    {
        // The used width is the smaller of the declared 300 px and the cap, the height following the picture's
        // aspect: the logo lays out as one declared 100 x 20 px, its column floored at 75 pt, not at 225.
        using var capped = ConvertGrid(Logo(" style='max-width:" + ownMaxWidth + "'"));
        using var declared = ConvertGrid("<img src='data:image/png;base64," + TinyPng + "' width='100' height='20' style='max-width:none'>");
        var logo = Assert.Single(Pictures(capped));
        Assert.Equal(75, logo.Width, 0.5);
        Assert.Equal(15, logo.Height, 0.5);
        Assert.Equal(Assert.Single(Pictures(declared)).LLX, logo.LLX, 0.5);
        Assert.Equal(TextColumnLeft(declared), TextColumnLeft(capped), 0.5);
    }

    [Theory]
    [InlineData(" height='60' style='max-width:100px'")]
    [InlineData(" style='height:60px; max-width:100px'")]
    [InlineData(" width='300' style='max-width:100px'")]
    public void AOneSidedPicturesOwnMaxWidthLengthCapsTheBoxItsFileCompletes(string box)
    {
        // The 300 x 60 px file gives the side the picture leaves open by its aspect - a 300 x 60 px box - which the cap
        // brings to 100 x 20 px, as it does a declared one: 75 x 15 pt, its column floored there.
        using var capped = ConvertGrid(Wide(box));
        using var declared = ConvertGrid(Wide(" width='100' height='20' style='max-width:none'"));
        var logo = Assert.Single(Pictures(capped));
        Assert.Equal(75, logo.Width, 0.5);
        Assert.Equal(15, logo.Height, 0.5);
        Assert.Equal(TextColumnLeft(declared), TextColumnLeft(capped), 0.5);
    }

    [Theory]
    [InlineData(WidePng, " height='30' style='max-width:160px'", " width='150' height='30'", 112.5, 22.5)]
    [InlineData(WidePng, " style='height:30px; max-width:160px'", " width='150' height='30'", 112.5, 22.5)]
    [InlineData(WidePng, " width='150' style='max-width:160px'", " width='150' height='30'", 112.5, 22.5)]
    [InlineData(RetinaPng, " height='40' style='max-width:240px'", " width='240' height='40'", 180, 30)]
    public void AOneSidedPictureItsCapDoesNotBindDrawsAtTheBoxItsFileCompletes(string png, string box, string cssBox, double width, double height)
    {
        // The file's aspect completes the side the picture leaves open - a 150 x 30 px box (240 x 40 px for the
        // 600 x 100 px banner), at or under its cap - and the picture lays out at that box as a declared one does:
        // drawn at it, its column floored there, not the file's own size drawn as wide as the column lets it.
        using var capped = ConvertGrid(Picture(png, box));
        using var declared = ConvertGrid(Picture(png, cssBox + " style='max-width:none'"));
        var logo = Assert.Single(Pictures(capped));
        Assert.Equal(width, logo.Width, 0.5);
        Assert.Equal(height, logo.Height, 0.5);
        Assert.Equal(TextColumnLeft(declared), TextColumnLeft(capped), 0.5);
        // In a column the row above holds at 300 px it keeps that box, no wider than its cap (the 3 pt spacer aside).
        using var held = ConvertGrid(Picture(png, box), rowAbove: HeldColumnRow);
        var heldLogo = Assert.Single(Pictures(held), p => p.Height > height / 2);
        Assert.Equal(width, heldLogo.Width, 0.5);
        Assert.Equal(height, heldLogo.Height, 0.5);
    }

    [Theory]
    [InlineData(560, 200, " width='100%' style='max-width:560px'", " width='100%' style='max-width:none'", 600, 420, 150)]
    [InlineData(300, 60, " height='100%' style='max-width:600px'", " height='100%' style='max-width:none'", 300, 225, 45)]
    public void APictureSizedByAPercentAttributeUnderACapThatDoesNotBindLaysOutAsUncapped(int fileWidth, int fileHeight, string box,
        string uncappedBox, int column, double width, double height)
    {
        // A percent attribute gives no side in pixels for the file's aspect to complete: under a cap that does not bind the
        // picture lays out as it does under none - the 560 x 200 px banner at 100% of its 600 px column at its file's
        // 420 x 150 pt, the 300 x 60 px logo of height 100% at its file's 225 x 45 pt, flooring its column at nothing wider.
        var png = System.Convert.ToBase64String(Aspose.Pdf.Tests.FlowImageTests.Png(fileWidth, fileHeight));
        using var capped = ConvertGrid(Picture(png, box), secondCell: "A", rowAbove: HeldColumn(column));
        using var uncapped = ConvertGrid(Picture(png, uncappedBox), secondCell: "A", rowAbove: HeldColumn(column));
        // (the 3 pt spacer aside)
        var picture = Assert.Single(Pictures(capped), p => p.Height > 4);
        Assert.Equal(width, picture.Width, 0.5);
        Assert.Equal(height, picture.Height, 0.5);
        Assert.Equal(Layout(uncapped), Layout(capped));
    }

    [Fact]
    public void ASmallPictureGivenOneSideStaysAPictureInAnOverDeclaredGridUnderACapThatDoesNotBind()
    {
        // The over-declared grid draws a picture amid cell text as a tick glyph when the box it loads at, as LoadCellImage
        // gives it, is 20 px or under each way. The 16 x 16 px flag given
        // only its height stays a picture under a cap that does not bind, as it does under none: the side the file's aspect
        // completes for the cap is none of that box.
        var flag = System.Convert.ToBase64String(Aspose.Pdf.Tests.FlowImageTests.Png(16, 16));
        using var capped = ConvertOverDeclaredGrid("Status: " + Picture(flag, " height='12' style='max-width:100px'") + " OK");
        Assert.Single(Pictures(capped));
        Assert.DoesNotContain(Fragments(capped), f => f.Text.Contains(TickMark));
        // (the dialect holds on this grid: the flag given both sides is the glyph)
        using var tick = ConvertOverDeclaredGrid("Status: " + Picture(flag, " width='12' height='12' style='max-width:100px'") + " OK");
        Assert.Empty(Pictures(tick));
        Assert.Contains(Fragments(tick), f => f.Text.Contains(TickMark));
    }

    [Theory]
    [InlineData(16, " width='40' height='40' style='max-width:16px'")]
    [InlineData(40, " style='max-width:16px'")]
    public void APictureItsCapScalesUnder20PxStaysAPictureInAnOverDeclaredGrid(int fileSide, string box)
    {
        // The box the picture loads at - its markup's 40 x 40 px, or its 40 x 40 px file where the markup gives no side - is
        // no tick: the 16 px cap draws it at 12 x 12 pt as a picture, not as the glyph the capped box would be taken for.
        var png = System.Convert.ToBase64String(Aspose.Pdf.Tests.FlowImageTests.Png(fileSide, fileSide));
        using var doc = ConvertOverDeclaredGrid("Status: " + Picture(png, box) + " OK");
        var picture = Assert.Single(Pictures(doc));
        Assert.Equal(12, picture.Width, 0.5);
        Assert.Equal(12, picture.Height, 0.5);
        Assert.DoesNotContain(Fragments(doc), f => f.Text.Contains(TickMark));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" style='max-width:100px'")]
    [InlineData(" style='max-width:8px'")]
    public void AFlagGivenNoSideIsTheTickGlyphInAnOverDeclaredGridWhateverItsCap(string box)
    {
        // The 16 x 16 px flag given no side loads at its file's own size, a tick's: the glyph, under a cap that binds or
        // does not as under none.
        var flag = System.Convert.ToBase64String(Aspose.Pdf.Tests.FlowImageTests.Png(16, 16));
        using var doc = ConvertOverDeclaredGrid("Status: " + Picture(flag, box) + " OK");
        Assert.Empty(Pictures(doc));
        Assert.Contains(Fragments(doc), f => f.Text.Contains(TickMark));
    }

    [Fact]
    public void APercentWidePicturesOwnMaxWidthLengthCapsItOnceItsColumnIsSolved()
    {
        // `width: 100%` of a wide column would draw the 300 x 60 px picture across it; the 100 px cap holds it to
        // 75 x 15 pt. Beside the long text it asks its column for those 75 pt at most, as a 100 x 20 px one does.
        using var alone = ConvertGrid(Wide(" style='width:100%; max-width:100px'"), secondCell: "A");
        var logo = Assert.Single(Pictures(alone));
        Assert.Equal(75, logo.Width, 0.5);
        Assert.Equal(15, logo.Height, 0.5);
        using var capped = ConvertGrid(Wide(" style='width:100%; max-width:100px'"));
        using var declared = ConvertGrid(Wide(" width='100' height='20'"));
        Assert.Equal(TextColumnLeft(declared), TextColumnLeft(capped), 0.5);
    }

    [Fact]
    public void TheSheetsMaxWidthLengthCapsACellPicture()
    {
        // `img { max-width: 100px }` caps the 300 x 60 px logo as its own declaration would: 75 x 15 pt.
        using var capped = ConvertGrid(Logo(""), "100px");
        using var declared = ConvertGrid(Logo(" style='max-width:100px'"), "none");
        var logo = Assert.Single(Pictures(capped));
        Assert.Equal(75, logo.Width, 0.5);
        Assert.Equal(15, logo.Height, 0.5);
        Assert.Equal(TextColumnLeft(declared), TextColumnLeft(capped), 0.5);
    }

    [Fact]
    public void AnImportantSheetCapStillBeatsThePicturesOwnMaxWidth()
    {
        // `img { max-width: 100% !important }` (a print sheet's reset) wins over a plain inline declaration.
        using var doc = ConvertGrid(Logo(" style='max-width:none'"), "100% !important");
        var logo = Assert.Single(Pictures(doc));
        Assert.True(logo.Width < 225, $"the logo is capped to its column: {logo.Width:0.#}");
    }

    [Fact]
    public void ACappedVectorPictureDrawsInsideItsColumn()
    {
        // A square drawing declared 400 x 200 px - a 300 x 150 pt box it rides centred in - capped to a column
        // narrower than the box: it draws from the column's left edge, no wider than the column.
        var svg = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\" fill=\"#0000ff\"/></svg>"));
        using var doc = ConvertGrid("<img src='data:image/svg+xml;base64," + svg + "' width='400' height='200'>");
        var drawing = Assert.Single(Pictures(doc));
        var columnLeft = Assert.Single(Fragments(doc), f => f.Text.Trim() == "Station log").Box.LLX;
        var textLeft = TextColumnLeft(doc);
        Assert.True(drawing.Width < 225, $"the drawing is capped to its column: {drawing.Width:0.#}");
        Assert.Equal(columnLeft, drawing.LLX, 0.5);
        Assert.True(drawing.URX <= textLeft + 0.5, $"the drawing stands in its own column: {drawing.LLX:0.#}..{drawing.URX:0.#} vs {textLeft:0.#}");
    }

    [Fact]
    public void APositionedCellPictureLendsItsColumnNoWidth()
    {
        // Out of flow, a picture in a `position: absolute` span adds nothing to its column: the columns are those
        // of the same grid without it.
        using var withPicture = ConvertGrid("A<span style='position:absolute'><img src='data:image/png;base64," + TinyPng
            + "' width='400' height='200'></span>");
        using var without = ConvertGrid("A");
        Assert.Equal(TextColumnLeft(without), TextColumnLeft(withPicture), 0.5);
    }
}
