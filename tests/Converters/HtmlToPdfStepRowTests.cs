using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>The step-row worksheet: a step's pictures, the text over its table and its heading.</summary>
public class HtmlToPdfStepRowTests
{
    // The worksheet's sheet - a flex row of the 74 px bullet, the 490 px content and the 130 px ack
    // columns, a warning block's heading box centred in its column - and a first step holding a
    // data-entry table, the widget the worksheet is known by.
    private const string Head = "<html><head><style>body { font-family: Arial; font-size: 12pt; }"
        + " .step-row { margin: 15px 0; display: flex; } .sr-bullet { width: 74px; }"
        + " .step-row .sr-content { width: 490px; } .sr-ack { width: 130px; }"
        + " .step-warning .sw-heading { width: 88px; margin: auto; }</style></head><body>"
        + "<div class='step-row'><div class='sr-bullet'>1.</div><div class='sr-content'>"
        + "<table class='swdt-table'><tr><th width='480'>Reading</th></tr><tr><td>-</td></tr></table></div></div>";

    // the content column: the 96 pt margin and the 55.5 pt bullet column to its left, 367.5 pt wide
    private const double ContentLeftPt = 151.5;
    private const double ContentWidthPt = 367.5;

    // a 2 x 2 px picture
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGNQSDgARAwQCgAgjgUB59mTewAAAABJRU5ErkJggg==";
    // a 40 x 30 px picture
    private const string MidPng = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAeCAIAAADRv8uKAAAAK0lEQVR42u3NMQ0AAAgDsElBGlKRhgw4mvRvqudExGKxWCwWi8VisVj8N15TRNxMk6VBRQAAAABJRU5ErkJggg==";

    private static Document ConvertSteps(params string[] stepContents)
    {
        var html = new StringBuilder(Head);
        for (var i = 0; i < stepContents.Length; i++)
            html.Append("<div class='step-row'><div class='sr-bullet'>").Append(i + 2).Append(".</div><div class='sr-content'>")
                .Append(stepContents[i]).Append("</div></div>");
        html.Append("</body></html>");
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html.ToString())), new HtmlLoadOptions());
        var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    private static (string Text, Rectangle Box)[] Fragments(Document doc)
    {
        var absorber = new TextFragmentAbsorber();
        doc.Pages[1].Accept(absorber);
        return absorber.TextFragments.Cast<TextFragment>()
            .Where(f => f.Text.Trim().Length > 0 && f.Rectangle is not null).Select(f => (f.Text, f.Rectangle!)).ToArray();
    }

    private static string PictureSource(string kind) => kind switch
    {
        "svg" => "data:image/svg+xml;base64," + System.Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\"/></svg>")),
        "webp" => "data:image/webp;base64,UklGRhYAAABXRUJQVlA4IAoAAAAAAAAAAAAAAAAA",
        // a 40 x 30 px header over data that does not inflate
        _ => "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAAeCAIAAADRv8uKAAAAGklEQVR4nN6tvu/erb7v3q2+796tvu/erb7v3q2+7x0RN/gAAAAASUVORK5CYII=",
    };

    [Theory]
    [InlineData("webp", " style='width:120px;height:80px'", 60)]
    [InlineData("corrupt-png", "", 22.5)]
    public void AStepPictureThePageCannotDecodeIsLeftOutAndTheStepGoesOn(string kind, string box, double boxHeightPt)
    {
        // The picture's box is kept, as a browser keeps a broken picture's box: the text after it stands under it.
        using var doc = ConvertSteps("Read the dial <img src='" + PictureSource(kind) + "'" + box + "> and note it");
        var frags = Fragments(doc);
        var before = Assert.Single(frags, f => f.Text.Contains("Read the dial"));
        var after = Assert.Single(frags, f => f.Text.Contains("and note it"));
        Assert.Equal(ContentLeftPt, after.Box.LLX, 0.5);
        Assert.True(before.Box.LLY - after.Box.LLY >= boxHeightPt,
            $"the text after the picture stands under its {boxHeightPt} pt box: {before.Box.LLY:0.#} -> {after.Box.LLY:0.#}");
    }

    [Fact]
    public void AVectorStepPictureDrawsLetterboxedInItsBox()
    {
        // A square drawing in its 120 x 80 px (90 x 60 pt) box draws as a raster of the box with the drawing centred
        // in it, not stretched to it: the raster has the box's 3:2 shape. The text after it stands under the box.
        using var doc = ConvertSteps("Read the dial <img src='" + PictureSource("svg") + "' style='width:120px;height:80px'> and note it");
        var absorber = new ImagePlacementAbsorber();
        absorber.Visit(doc.Pages[1]);
        var drawing = Assert.Single(absorber.ImagePlacements);
        Assert.Equal(90, drawing.Rectangle.Width, 0.5);
        Assert.Equal(60, drawing.Rectangle.Height, 0.5);
        Assert.Equal(ContentLeftPt, drawing.Rectangle.LLX, 0.5);
        Assert.NotNull(drawing.Image);
        Assert.Equal(1.5, (double)drawing.Image!.Width / drawing.Image.Height, 2);
        var frags = Fragments(doc);
        var before = Assert.Single(frags, f => f.Text.Contains("Read the dial"));
        var after = Assert.Single(frags, f => f.Text.Contains("and note it"));
        Assert.True(before.Box.LLY - after.Box.LLY >= 60,
            $"the text after the drawing stands under its 60 pt box: {before.Box.LLY:0.#} -> {after.Box.LLY:0.#}");
    }

    [Theory]
    [InlineData(" width='160' height='120'", 120, 90)]
    [InlineData(" width=160 height=120", 120, 90)]
    [InlineData(" width='160' height='120' style='width:80px'", 60, 90)]
    public void AStepPictureSizedByItsAttributesDrawsAtThatBox(string box, double widthPt, double heightPt)
    {
        // 160 x 120 px is 120 x 90 pt, and a style side overrides its attribute; the file's own 2 x 2 px would
        // draw the picture 1.5 pt square.
        var picture = StepPictureBox(box);
        Assert.Equal(widthPt, picture.Width, 0.5);
        Assert.Equal(heightPt, picture.Height, 0.5);
        Assert.Equal(ContentLeftPt, picture.LLX, 0.5);
    }

    [Theory]
    [InlineData(" alt='Gauge, height: 40 mm' width='160' height='120'")]
    [InlineData(" width='160' height='120' title='width:20px'")]
    [InlineData(" alt='set width=40 here' width='160' height='120'")]
    public void TextInAStepPicturesOtherAttributesSizesNothing(string box)
    {
        // Only the style and the width and height attributes size the picture: a side named in its alt text or
        // title is prose, and the 160 x 120 px box still draws 120 x 90 pt.
        var picture = StepPictureBox(box);
        Assert.Equal(120, picture.Width, 0.5);
        Assert.Equal(90, picture.Height, 0.5);
    }

    [Theory]
    [InlineData("data-src='data:image/png;base64,{mid}' src='data:image/png;base64,{tiny}'", 1.5, 1.5)]
    [InlineData("alt=\"src='data:image/png;base64,{mid}'\" src='data:image/png;base64,{tiny}'", 1.5, 1.5)]
    [InlineData("src=data:image/png;base64,{tiny} width=160 height=120", 120, 90)]
    public void AStepPictureDrawsTheFileItsSrcAttributeNames(string attributes, double widthPt, double heightPt)
    {
        // The 2 x 2 px file the src attribute names draws - at its own 1.5 pt square, or at its declared box - not the
        // 40 x 30 px one a data-src or the alt text names; an unquoted src names its file as a quoted one does.
        var picture = StepPictureRectangle("<img " + attributes.Replace("{mid}", MidPng).Replace("{tiny}", TinyPng) + ">");
        Assert.Equal(widthPt, picture.Width, 0.5);
        Assert.Equal(heightPt, picture.Height, 0.5);
        Assert.Equal(ContentLeftPt, picture.LLX, 0.5);
    }

    private static Rectangle StepPictureBox(string box) => StepPictureRectangle("<img src='data:image/png;base64," + TinyPng + "'" + box + ">");

    private static Rectangle StepPictureRectangle(string imgTag)
    {
        using var doc = ConvertSteps(imgTag);
        var absorber = new ImagePlacementAbsorber();
        absorber.Visit(doc.Pages[1]);
        return Assert.Single(absorber.ImagePlacements).Rectangle;
    }

    [Theory]
    [InlineData("<h4>Readings <table border='1'>{0}</table></h4>")]
    [InlineData("<h4>Readings<table border='1'>{0}</table><h4>Next</h4>")]
    public void AHeadingThatHoldsTheStepTableStillDrawsTheTable(string step)
    {
        // The heading's close comes after the table it holds, or the heading is left open up to a later one;
        // the table's cells still draw, each its own 12 pt run (the heading's 16 pt run is not a cell).
        using var doc = ConvertSteps(step.Replace("{0}", "<tr><td style='width:50%'>Pump</td><td style='width:50%'>Flow</td></tr>"));
        var absorber = new TextFragmentAbsorber();
        doc.Pages[1].Accept(absorber);
        var cells = absorber.TextFragments.Cast<TextFragment>().Where(f => System.Math.Abs(f.TextState.FontSize - 12) < 0.01)
            .Select(f => f.Text.Trim()).ToArray();
        Assert.Contains("Pump", cells);
        Assert.Contains("Flow", cells);
    }

    [Fact]
    public void TextOverAStepTableIsCentredOnlyWhenItsMarkupCentresIt()
    {
        // An instruction paragraph and a data-entry widget's own caption start at the content column's edge;
        // the template's `text-align: center` caption div and the warning heading the sheet centres stand
        // centred over their tables.
        const string table = "<table border='1'><tr><td style='width:50%'>Pump</td><td style='width:50%'>Flow</td></tr></table>";
        using var doc = ConvertSteps("<p>Record each pump.</p>" + table, "<div style='text-align:center'>Readings</div>" + table,
            "<div class='swdt-caption'>Pump readings</div><table class='swdt-table'><tr><th width='480'>Flow</th></tr><tr><td>-</td></tr></table>",
            "<div class='step-warning'><div class='sw-heading'><p>Warning</p></div><div class='sw-content'>" + table + "</div></div>");
        var frags = Fragments(doc);
        Assert.Equal(ContentLeftPt, Assert.Single(frags, f => f.Text.Contains("Record each pump.")).Box.LLX, 0.5);
        Assert.Equal(ContentLeftPt, Assert.Single(frags, f => f.Text.Contains("Pump readings")).Box.LLX, 0.5);
        foreach (var centred in new[] { "Readings", "Warning" })
        {
            var caption = Assert.Single(frags, f => f.Text.Trim() == centred).Box;
            Assert.Equal(ContentLeftPt + ContentWidthPt / 2, (caption.LLX + caption.URX) / 2, 1.0);
        }
    }
}
