using Aspose.Pdf;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>Background pictures on a block box: each copy's transform and place read back
/// from the page, the clip the copies are cut to, and the order the layers are painted in.</summary>
public sealed class BackgroundPictureTests
{
    private const double Margin = 36;
    private const double PageTop = 842 - Margin;
    private const double Band = 2;
    private const double Pad = 6;

    // A 30 x 20 picture in a box whose padding box is 519 wide: copies step 30 across from
    // the padding box's left (38), the first one right, then one left of it, then on right.
    [Fact]
    public void CopiesRepeatFromThePlacedOneOutToTheClip()
    {
        var box = Boxed(new BackgroundPicture(Image(30, 20)));
        var copies = RenderCopies(box).Single().Copies;
        var top = PageTop - Band - 20;
        var row = copies.Where(c => Math.Abs(c.Y - top) < 0.01).Select(c => c.X).ToList();
        Assert.Equal(new[] { 38.0, 68, 8, 98, 128 }, row.Take(5).Select(x => Math.Round(x, 2)));
        Assert.Equal(548, row.Max(), 2);
        Assert.All(copies, c => Assert.Equal((30.0, 20.0), (c.Width, c.Height)));
        // Rows: the placed one, the one under it, the one over it (reaching the band).
        var rows = copies.Select(c => Math.Round(c.Y, 2)).Distinct().ToList();
        Assert.Equal(new[] { Math.Round(top, 2), Math.Round(top - 20, 2), Math.Round(top + 20, 2) }, rows);
    }

    [Fact]
    public void AnUnrepeatedPictureSitsWhereItsEdgesPutIt()
    {
        var picture = new BackgroundPicture(Image(30, 20))
        {
            RepeatAcross = CssBackgroundRepeat.NoRepeat,
            RepeatUp = CssBackgroundRepeat.NoRepeat,
            HorizontalEdge = CssBackgroundEdge.End,
            HorizontalOffset = 5,
            VerticalEdge = CssBackgroundEdge.Center,
        };
        var copy = RenderCopies(Boxed(picture)).Single().Copies.Single();
        Assert.Equal(595 - Margin - Band - 5 - 30, copy.X, 2);
        var paddingBottom = PageTop - BoxHeight + Band;
        Assert.Equal(paddingBottom + (BoxHeight - 2 * Band - 20) / 2, copy.Y, 2);
    }

    [Fact]
    public void ARoundedRepeatFitsWholeCopies()
    {
        var picture = new BackgroundPicture(Image(30, 20)) { RepeatAcross = CssBackgroundRepeat.Round, RepeatUp = CssBackgroundRepeat.Round };
        var copies = RenderCopies(Boxed(picture)).Single().Copies;
        // 519 / 30 = 17.3 -> 17 copies; 30 / 20 leaves half a copy over, which makes two.
        Assert.All(copies, c => Assert.Equal(519.0 / 17, c.Width, 3));
        Assert.All(copies, c => Assert.Equal((BoxHeight - 2 * Band) / 2, c.Height, 3));
    }

    [Fact]
    public void ASpacedRepeatSpreadsTheGaps()
    {
        var picture = new BackgroundPicture(Image(30, 20)) { RepeatAcross = CssBackgroundRepeat.Space, RepeatUp = CssBackgroundRepeat.NoRepeat };
        var xs = RenderCopies(Boxed(picture)).Single().Copies.Select(c => c.X).OrderBy(x => x).ToList();
        // 17 copies in 519 leave 9 over 16 gaps.
        Assert.Equal(30 + 9.0 / 16, xs[2] - xs[1], 3);
    }

    [Fact]
    public void TheClipAndTheOriginAreTheBoxesAsked()
    {
        var picture = new BackgroundPicture(Image(30, 20)) { Origin = BackgroundBoxArea.Border, Clip = BackgroundBoxArea.Content };
        var layer = RenderCopies(Boxed(picture)).Single();
        Assert.Equal(Margin + Band + Pad, layer.Clip.X, 2);
        Assert.Equal(595 - 2 * (Margin + Band + Pad), layer.Clip.Width, 2);
        Assert.Contains(layer.Copies, c => Math.Abs(c.X - Margin) < 0.01 && Math.Abs(c.Y - (PageTop - 20)) < 0.01);
        Assert.DoesNotContain(layer.Copies, c => c.X + c.Width <= layer.Clip.X);
    }

    [Fact]
    public void TheFirstPictureIsPaintedLastOnTop()
    {
        var box = Boxed(new BackgroundPicture(Form(40, 10)) { RepeatAcross = CssBackgroundRepeat.NoRepeat, RepeatUp = CssBackgroundRepeat.NoRepeat },
            new BackgroundPicture(Image(30, 20)));
        var layers = RenderCopies(box);
        Assert.Equal(2, layers.Count);
        Assert.True(layers[0].Copies.Count > 1, "the repeated image goes down first");
        Assert.Equal((40.0, 10.0), (layers[1].Copies.Single().Width, layers[1].Copies.Single().Height));
    }

    [Fact]
    public void APictureDrawnForItsAreaIsScaledToItsSize()
    {
        (double W, double H)? asked = null;
        var picture = BackgroundPicture.ForArea((w, h) =>
        {
            asked = (w, h);
            var dict = FormDict(w, h);
            return new Core.PdfStream(dict, System.Text.Encoding.ASCII.GetBytes("0 g 0 0 1 1 re f"));
        });
        picture.Width = 100;
        picture.Height = 10;
        picture.RepeatAcross = CssBackgroundRepeat.NoRepeat;
        picture.RepeatUp = CssBackgroundRepeat.NoRepeat;
        var copy = RenderCopies(Boxed(picture)).Single().Copies.Single();
        Assert.Equal(519, asked!.Value.W, 2);
        Assert.Equal((100.0, 10.0), (Math.Round(copy.Width, 3), Math.Round(copy.Height, 3)));
    }

    [Fact]
    public void ABlendModeIsAStateOfItsOwn()
    {
        var box = Boxed(new BackgroundPicture(Image(30, 20)) { BlendMode = "Multiply" });
        using var reopened = Document.Open(Render(box));
        var page = reopened.Pages[1];
        Assert.Contains(page.Contents.OfType<GS>(), gs => true);
        var states = page.Reader.ResolveDict(page.Reader.ResolveDict(page.Dict.Get("Resources"))!.Get("ExtGState"))!;
        Assert.Contains(states.Keys, key => page.Reader.ResolveDict(states.Get(key))!.Get("BM") is Core.PdfName { Value: "Multiply" });
    }

    [Fact]
    public void ARoundedBoxPaintsItsPicturesInsideTheOutline()
    {
        var box = Boxed(new BackgroundPicture(Image(30, 20)) { RepeatAcross = CssBackgroundRepeat.NoRepeat, RepeatUp = CssBackgroundRepeat.NoRepeat });
        box.CornerRadii = new CornerRadii(new CornerRadius(8));
        using var reopened = Document.Open(Render(box));
        var ops = reopened.Pages[1].Contents.ToList();
        var firstDo = ops.FindIndex(o => o is Do);
        // Four corner clips, then the picture's own rectangular clip, before its copy.
        Assert.Equal(5, ops.Take(firstDo).Count(o => o is Clip or EOClip));
    }

    [Fact]
    public void EachPartOfABrokenBoxTilesFromItsOwnTop()
    {
        var box = new BoxBlock { Border = new BorderInfo(BorderSide.All, 1), Padding = new MarginInfo(4, 4, 4, 4) };
        box.BackgroundPictures.Add(new BackgroundPicture(Image(30, 20)));
        for (var i = 0; i < 60; i++) box.Paragraphs.Add(Plain("line " + i));
        var pages = RenderCopies(Render(box));
        Assert.True(pages.Count >= 2);
        Assert.Contains(pages[1].Copies, c => Math.Abs(c.Y - (PageTop - 1 - 20)) < 0.01);
    }

    private const double BoxHeight = 2 * Band + 2 * Pad + 18;

    private static BoxBlock Boxed(params BackgroundPicture[] pictures)
    {
        var box = new BoxBlock { Border = new BorderInfo(BorderSide.All, Band), Padding = new MarginInfo(Pad, Pad, Pad, Pad), MinHeight = 18 };
        foreach (var picture in pictures) box.BackgroundPictures.Add(picture);
        return box;
    }

    private static TextFragment Plain(string text)
    {
        var fragment = new TextFragment(text);
        fragment.TextState.FontSize = 12;
        fragment.TextState.LineSpacing = 6;
        return fragment;
    }

    private static Core.PdfStream Image(int width, int height)
    {
        var dict = new Core.PdfDictionary();
        dict.Set("Type", new Core.PdfName("XObject"));
        dict.Set("Subtype", new Core.PdfName("Image"));
        dict.Set("Width", new Core.PdfInteger(width));
        dict.Set("Height", new Core.PdfInteger(height));
        dict.Set("ColorSpace", new Core.PdfName("DeviceGray"));
        dict.Set("BitsPerComponent", new Core.PdfInteger(8));
        return new Core.PdfStream(dict, Enumerable.Repeat((byte)128, width * height).ToArray());
    }

    private static Core.PdfStream Form(double width, double height) =>
        new(FormDict(width, height), System.Text.Encoding.ASCII.GetBytes("0 g 0 0 5 5 re f"));

    private static Core.PdfDictionary FormDict(double width, double height)
    {
        var dict = new Core.PdfDictionary();
        dict.Set("Type", new Core.PdfName("XObject"));
        dict.Set("Subtype", new Core.PdfName("Form"));
        var box = new Core.PdfArray();
        foreach (var v in new[] { 0, 0, width, height }) box.Add(new Core.PdfReal(v));
        dict.Set("BBox", box);
        return dict;
    }

    private static byte[] Render(params BaseParagraph[] paragraphs)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        foreach (var paragraph in paragraphs) page.Paragraphs.Add(paragraph);
        return doc.ToArray();
    }

    private static List<Layer> RenderCopies(BoxBlock box) => RenderCopies(Render(box)).Where(l => l.Copies.Count > 0).ToList();

    /// <summary>Every clipped group of copies, page by page for a broken box: the clip
    /// rectangle and each copy's place and size.</summary>
    private static List<Layer> RenderCopies(byte[] pdf)
    {
        using var reopened = Document.Open(pdf);
        var layers = new List<Layer>();
        for (var n = 1; n <= reopened.Pages.Count; n++)
        {
            Layer? current = null;
            Re? pending = null;
            ConcatenateMatrix? cm = null;
            foreach (var op in reopened.Pages[n].Contents)
            {
                switch (op)
                {
                    case Re re: pending = re; break;
                    case Clip when pending is not null:
                        current = new Layer(new Box(pending.X, pending.Y, pending.Width, pending.Height), new List<Copy>());
                        layers.Add(current);
                        break;
                    case ConcatenateMatrix m: cm = m; break;
                    case Do d when cm is not null && current is not null:
                        var unit = Unit(reopened.Pages[n], d.Name);
                        current.Copies.Add(new Copy(cm.Matrix.E, cm.Matrix.F, cm.Matrix.A * unit.W, cm.Matrix.D * unit.H));
                        cm = null;
                        break;
                }
            }
        }
        return layers.Where(l => l.Copies.Count > 0).ToList();
    }

    /// <summary>What one unit of the named xobject covers: 1 x 1 for an image, the
    /// bounding box for a form (every form here starts at the origin).</summary>
    private static (double W, double H) Unit(Page page, string name)
    {
        var xobjects = page.Reader.ResolveDict(page.Reader.ResolveDict(page.Dict.Get("Resources"))!.Get("XObject"))!;
        var stream = (Core.PdfStream)page.Reader.Resolve(xobjects.Get(name))!;
        if (stream.Dict.Get("Subtype") is not Core.PdfName { Value: "Form" }) return (1, 1);
        var box = (Core.PdfArray)page.Reader.Resolve(stream.Dict.Get("BBox"))!;
        return (Num(page.Reader.Resolve(box[2])), Num(page.Reader.Resolve(box[3])));
    }

    private static double Num(Core.PdfObject? o) => o switch { Core.PdfInteger i => i.Value, Core.PdfReal r => r.Value, _ => 0 };

    private sealed record Box(double X, double Y, double Width, double Height);

    private sealed record Copy(double X, double Y, double Width, double Height);

    private sealed record Layer(Box Clip, List<Copy> Copies);
}
