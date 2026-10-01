using System.IO.Compression;
using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>An image paragraph in the flow: its box with a background and a
/// border, its own left and right margins, a fit to the room left on the page,
/// the page it goes to when it does not fit, and a caller-placed rectangle
/// seated on the page the flow has reached.</summary>
public sealed class FlowImageTests
{
    private const double Margin = 36;
    private const double Band = 595 - 2 * Margin;

    [Fact]
    public void APictureThatDoesNotFitMovesWholeToTheNextPageWithItsWash()
    {
        using var doc = Build(fillers: 28, Picture(100, 200, background: Color.Yellow));
        Assert.Equal(2, doc.Pages.Count);
        Assert.Empty(doc.Pages[1].Contents.OfType<Do>());
        var second = doc.Pages[2].Contents.ToList();
        var wash = second.OfType<Re>().First(r => r.Height > 100);
        Assert.Equal(200, wash.Height, 6);
        Assert.Equal(842 - Margin, wash.Y + wash.Height, 6);
        var draw = second.OfType<ConcatenateMatrix>().Single(m => m.Matrix.D > 100);
        Assert.Equal(200, draw.Matrix.D, 6);
        Assert.Equal(wash.Y, draw.Matrix.F, 6);
        Assert.True(second.IndexOf(wash) < second.IndexOf(draw), "the wash goes under the picture");
        Assert.Single(second.OfType<Do>());
    }

    [Fact]
    public void ABorderAndMarginsShapeTheBoxAndTheFollowingParagraphSeat()
    {
        using var doc = Build(fillers: 0, Picture(120, 80, background: Color.Yellow, border: 2, marginLeft: 40, marginRight: 60), then: true);
        var ops = doc.Pages[1].Contents.ToList();
        var wash = ops.OfType<Re>().First(r => r.Height > 50);
        Assert.Equal(124, wash.Width, 6);
        Assert.Equal(84, wash.Height, 6);
        Assert.Equal(Margin + 40, wash.X, 6);
        var draw = ops.OfType<ConcatenateMatrix>().Single(m => m.Matrix.D > 50);
        Assert.Equal(Margin + 40 + 2, draw.Matrix.E, 6);
        Assert.Equal(wash.Y + 2, draw.Matrix.F, 6);
        var after = ops.OfType<MoveTextPosition>().Last();
        Assert.True(after.Y < wash.Y, "the next paragraph sits under the box");
    }

    [Fact]
    public void ACentredPictureRespectsItsMargins()
    {
        using var doc = Build(fillers: 0, Picture(120, 80, marginLeft: 40, marginRight: 60, align: HorizontalAlignment.Center));
        var draw = doc.Pages[1].Contents.OfType<ConcatenateMatrix>().Single(m => m.Matrix.D > 50);
        Assert.Equal(Margin + 40 + (Band - 100 - 120) / 2, draw.Matrix.E, 6);
    }

    [Fact]
    public void AFitToTheRemainingAreaFillsWhatIsFree()
    {
        using var doc = Build(fillers: 20, Picture(120, 80, fit: true));
        var draw = doc.Pages[1].Contents.OfType<ConcatenateMatrix>().Single(m => m.Matrix.D > 50);
        Assert.Equal(Margin, draw.Matrix.F, 6);
        Assert.Equal(120.0 / 80, draw.Matrix.A / draw.Matrix.D, 6);
        Assert.True(draw.Matrix.A > 120, "it grows into the room");
    }

    [Fact]
    public void AFixedRectangleIsDrawnOnThePageTheFlowHasReachedAndMovesNothing()
    {
        var fixedAt = Picture(30, 20);
        fixedAt.FixedRectangle = new Rectangle(400, 700, 430, 720);
        using var doc = Build(fillers: 32, fixedAt, then: true);
        Assert.Equal(2, doc.Pages.Count);
        Assert.Empty(doc.Pages[1].Contents.OfType<Do>());
        var draw = doc.Pages[2].Contents.OfType<ConcatenateMatrix>().Single();
        Assert.Equal(400, draw.Matrix.E, 6);
        Assert.Equal(700, draw.Matrix.F, 6);
        // The paragraph after it is where it would be without it: one pitch under
        // the last filler on that page, the picture having consumed no room.
        var texts = doc.Pages[2].Contents.OfType<MoveTextPosition>().Select(t => t.Y).ToList();
        Assert.Equal(texts[^2] - 25.98, texts[^1], 1);
    }

    [Fact]
    public void ALaterPagesPushedPictureBindsToItsOwnContinuation()
    {
        // Page 1 spills first, so page 2's flow is not the first to take a slot.
        var doc = new Aspose.Pdf.Document();
        var first = doc.Pages.Add(595, 842);
        first.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        for (var i = 0; i < 32; i++) first.Paragraphs.Add(Fragment("one " + i));
        var second = doc.Pages.Add(595, 842);
        second.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        for (var i = 0; i < 28; i++) second.Paragraphs.Add(Fragment("two " + i));
        second.Paragraphs.Add(Picture(100, 200, background: Color.Yellow));
        second.Paragraphs.Add(Fragment("after"));
        using var reopened = Aspose.Pdf.Document.Open(doc.ToArray());
        Assert.Equal(4, reopened.Pages.Count);
        var page4 = reopened.Pages[4].Contents.ToList();
        Assert.Single(page4.OfType<Do>());
        Assert.Equal(200, page4.OfType<ConcatenateMatrix>().Single(m => m.Matrix.D > 100).Matrix.D, 6);
        Assert.Empty(reopened.Pages[2].Contents.OfType<Do>());
    }

    [Fact]
    public void AQueuedBlackWhitePictureIsEmbeddedOneBitLikeItsStartPageSibling()
    {
        // Two 1-bit pictures: the first fits page 1, the second is pushed to page 2
        // through the slot queue. Both embed as 1-bit gray; the queue used to drop
        // the flag and bind the second as 8-bit RGB (a 5-frame bilevel TIFF grew
        // from 15 KB a frame to 200 KB).
        var doc = new Aspose.Pdf.Document();
        var page = doc.Pages.Add(595, 842);
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        foreach (var _ in new[] { 1, 2 })
            page.Paragraphs.Add(new Image { ImageStream = new MemoryStream(Png(100, 400)), FixWidth = 100, FixHeight = 400, IsBlackWhite = true });
        using var reopened = Aspose.Pdf.Document.Open(doc.ToArray());
        Assert.Equal(2, reopened.Pages.Count);
        Assert.Equal(1, BitsPerComponent(reopened.Pages[1]));
        Assert.Equal(1, BitsPerComponent(reopened.Pages[2]));
    }

    private static int BitsPerComponent(Page page)
    {
        Assert.Equal(1, page.Resources.Images.Count);
        return page.Resources.Images[1].BitsPerComponent;
    }

    private static Image Picture(int width, int height, Color? background = null, double border = 0,
        double marginLeft = 0, double marginRight = 0, HorizontalAlignment align = HorizontalAlignment.Left, bool fit = false)
    {
        var image = new Image { ImageStream = new MemoryStream(Png(width, height)) };
        if (!fit) { image.FixWidth = width; image.FixHeight = height; }
        image.FitToRemainingArea = fit;
        image.Margin = new MarginInfo(marginLeft, 0, marginRight, 0);
        image.HorizontalAlignment = align;
        image.BackgroundColor = background;
        if (border > 0) image.Border = new BorderInfo(BorderSide.All, (float)border, Color.Blue);
        return image;
    }

    private static Aspose.Pdf.Document Build(int fillers, Image picture, bool then = false)
    {
        var doc = new Aspose.Pdf.Document();
        var page = doc.Pages.Add(595, 842);
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        for (var i = 0; i < fillers; i++) page.Paragraphs.Add(Fragment("filler " + i));
        page.Paragraphs.Add(picture);
        if (then) page.Paragraphs.Add(Fragment("after"));
        return Aspose.Pdf.Document.Open(doc.ToArray());
    }

    private static TextFragment Fragment(string text)
    {
        var tf = new TextFragment(text);
        tf.TextState.FontSize = 12;
        tf.TextState.LineSpacing = 6;
        tf.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        tf.TextState.LineBoxAscentEm = 0.8;
        tf.TextState.LineBoxDescentEm = 0.2484;
        tf.Margin = new MarginInfo(0, 4, 0, 4);
        return tf;
    }

    /// <summary>A gray 8-bit PNG of the size, every row the "none" filter over mid-gray.</summary>
    internal static byte[] Png(int width, int height)
    {
        var rows = new byte[(width + 1) * height];
        for (var y = 0; y < height; y++) for (var x = 1; x <= width; x++) rows[y * (width + 1) + x] = 0x80;
        byte[] idat;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(rows, 0, rows.Length);
            idat = ms.ToArray();
        }
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        Chunk(png, "IHDR", new byte[] { (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height, 8, 0, 0, 0, 0 });
        Chunk(png, "IDAT", idat);
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        png.Write(new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length });
        png.Write(typeBytes);
        png.Write(data);
        uint c = 0xFFFFFFFF;
        foreach (var b in typeBytes.Concat(data))
        {
            c ^= b;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        c ^= 0xFFFFFFFF;
        png.Write(new[] { (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c });
    }
}
