using System.Text;
using Aspose.Pdf.Annotations;
using Xunit;
using static Aspose.Pdf.Tests.Redaction.Redacting;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>The paths, images and forms <see cref="RedactionAnnotation.Redact"/> removes or cuts:
/// nothing they paint under the rectangle survives in the saved file, and what they paint outside
/// it does.</summary>
public class RedactRemovesGraphicsTests
{
    // A 10 x 10 image drawn as a 100 x 100 square with its lower left corner at (100, 500): each
    // pixel is 10 units square, column c spanning x 100 + 10c to 110 + 10c.
    private const int Side = 10;
    private const string DrawImage = "q 100 0 0 100 100 500 cm /Im1 Do Q";

    /// <summary>Pixel (column, row) as RGB: red = 20 x column + 5, green = 20 x row + 5, blue 77 -
    /// no pixel is black, and a row's run of pixels names the row.</summary>
    private static byte[] Pattern()
    {
        var samples = new byte[Side * Side * 3];
        for (var row = 0; row < Side; row++)
        for (var column = 0; column < Side; column++)
        {
            var at = (row * Side + column) * 3;
            samples[at] = (byte)(20 * column + 5);
            samples[at + 1] = (byte)(20 * row + 5);
            samples[at + 2] = 77;
        }
        return samples;
    }

    private static RawPage.Obj PatternImage() =>
        new($"<< /Type /XObject /Subtype /Image /Width {Side} /Height {Side} /ColorSpace /DeviceRGB " +
            "/BitsPerComponent 8 >>", Pattern());

    private static byte[] Run(byte[] samples, int row, int fromColumn, int toColumn) =>
        samples.Skip((row * Side + fromColumn) * 3).Take((toColumn - fromColumn) * 3).ToArray();

    [Fact]
    public void AnImagePartlyUnderTheRectangleLosesThePixelsThereAndOnlyThose()
    {
        var pdf = RawPage.Build(DrawImage, "<< /XObject << /Im1 5 0 R >> >>", PatternImage());

        // Columns 0-4 lie under the rectangle, 5-9 outside it.
        var saved = Save(pdf, new Rectangle(90, 490, 150, 610));

        using var doc = Document.Open(saved);
        var names = DrawnNames(doc);
        Assert.Single(names);
        var samples = SamplesOf(doc, names[0]);
        var original = Pattern();
        for (var row = 0; row < Side; row++)
        {
            Assert.All(Run(samples, row, 0, 5), b => Assert.Equal(0, b));
            Assert.Equal(Run(original, row, 5, Side), Run(samples, row, 5, Side));
            Assert.False(SavedFile.HoldsBytes(saved, Run(original, row, 0, 5)), $"row {row} left half");
        }
    }

    [Fact]
    public void AnImageEntirelyUnderTheRectangleIsPaintedOverAndItsPixelsAreNotInTheFile()
    {
        var pdf = RawPage.Build(DrawImage, "<< /XObject << /Im1 5 0 R >> >>", PatternImage());

        var saved = Save(pdf, new Rectangle(95, 495, 205, 605));

        using var doc = Document.Open(saved);
        Assert.Equal(["Im1"], DrawnNames(doc));
        Assert.All(SamplesOf(doc, "Im1"), b => Assert.Equal(0, b));
        Assert.False(SavedFile.HoldsBytes(saved, Run(Pattern(), 3, 0, Side)));
    }

    [Fact]
    public void PaintedPixelsTakeTheFillColour()
    {
        var pdf = RawPage.Build(DrawImage, "<< /XObject << /Im1 5 0 R >> >>", PatternImage());

        using var doc = Document.Open(Save(pdf, new Rectangle(90, 490, 150, 610), Color.FromRgb(1, 0.5, 0)));

        var samples = SamplesOf(doc, "Im1");
        for (var row = 0; row < Side; row++)
        for (var column = 0; column < 5; column++)
            Assert.Equal(new byte[] { 255, 128, 0 }, Run(samples, row, column, column + 1));
    }

    [Fact]
    public void AnImageDrawnTwiceChangesOnlyWhereItIsDrawnUnderTheRectangle()
    {
        var pdf = RawPage.Build(DrawImage + " q 100 0 0 100 300 500 cm /Im1 Do Q",
            "<< /XObject << /Im1 5 0 R >> >>", PatternImage());

        using var doc = Document.Open(Save(pdf, new Rectangle(90, 490, 150, 610)));

        var names = DrawnNames(doc);
        Assert.Equal(2, names.Count);
        Assert.NotEqual(names[0], names[1]);
        Assert.Equal("Im1", names[1]);
        Assert.Equal(Pattern(), SamplesOf(doc, "Im1"));
    }

    [Fact]
    public void AJbig2ScanStaysJbig2WithThePixelsUnderTheRectanglePainted()
    {
        // A 16 x 8 scan, all black (JBIG2 1 = black), drawn as a 160 x 80 square at (100, 500).
        var bitmap = Enumerable.Repeat((byte)0xFF, 2 * 8).ToArray();
        var jbig2 = Aspose.Pdf.IO.Filters.Jbig2Encoder.Encode(bitmap, 16, 8, 2);
        var pdf = RawPage.Build("q 160 0 0 80 100 500 cm /Im1 Do Q", "<< /XObject << /Im1 5 0 R >> >>",
            new RawPage.Obj("<< /Type /XObject /Subtype /Image /Width 16 /Height 8 /ColorSpace /DeviceGray " +
                            "/BitsPerComponent 1 /Filter /JBIG2Decode >>", jbig2));

        // White fill; columns 0-7 (x 100-180) lie under the rectangle.
        using var doc = Document.Open(Save(pdf, new Rectangle(90, 490, 180, 590), Color.White));

        var page = doc.Pages[1];
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"))!;
        var image = (Aspose.Pdf.Core.PdfStream)page.Reader.Resolve(page.Reader.ResolveDict(resources.Get("XObject"))!.Get("Im1"))!;
        Assert.Equal("JBIG2Decode", image.Dict.GetName("Filter"));
        var samples = page.Reader.DecodeStream(image);
        for (var row = 0; row < 8; row++)
        {
            Assert.Equal(0xFF, samples[row * 2]);       // painted white
            Assert.Equal(0x00, samples[row * 2 + 1]);   // still black
        }
    }

    [Fact]
    public void AnInlineImagePartlyUnderTheRectangleLosesThePixelsThere()
    {
        var hex = string.Concat(Pattern().Select(b => b.ToString("X2")));
        var content = $"q 100 0 0 100 100 500 cm BI /W {Side} /H {Side} /CS /RGB /BPC 8 /F /AHx ID {hex}> EI Q";
        var pdf = RawPage.Build(content, "<< >>");

        var saved = Save(pdf, new Rectangle(90, 490, 150, 610));

        var original = Pattern();
        for (var row = 0; row < Side; row++)
            Assert.False(SavedFile.HoldsBytes(saved, Run(original, row, 0, 5)), $"row {row} left half");
        using var doc = Document.Open(saved);
        Assert.Contains("BI", ContentOf(doc));
    }

    [Fact]
    public void AFillCrossingTheRectangleIsCutAtItsEdges()
    {
        var pdf = RawPage.Build("0 0 1 rg 50 300 400 20 re f", "<< >>");

        using var doc = Document.Open(Save(pdf, new Rectangle(200, 290, 250, 330)));

        var xs = PathXs(doc);
        Assert.Contains(xs, x => Math.Abs(x - 50) < 0.01);
        Assert.Contains(xs, x => Math.Abs(x - 450) < 0.01);
        Assert.DoesNotContain(xs, x => x > 200.01 && x < 249.99);
    }

    [Fact]
    public void AStrokeCrossingTheRectangleIsCutAtItsEdges()
    {
        var pdf = RawPage.Build("2 w 50 400 m 450 400 l S", "<< >>");

        using var doc = Document.Open(Save(pdf, new Rectangle(200, 390, 250, 410)));

        var xs = PathXs(doc);
        Assert.Contains(xs, x => Math.Abs(x - 50) < 0.01);
        Assert.Contains(xs, x => Math.Abs(x - 200) < 0.01);
        Assert.Contains(xs, x => Math.Abs(x - 250) < 0.01);
        Assert.Contains(xs, x => Math.Abs(x - 450) < 0.01);
        Assert.DoesNotContain(xs, x => x > 200.01 && x < 249.99);
    }

    [Fact]
    public void AStrokedRectangleCrossingTheRectangleKeepsItsCornersAndGainsNoDiagonals()
    {
        var pdf = RawPage.Build("1 w 60 400 475 30 re S", "<< >>");

        using var doc = Document.Open(Save(pdf, new Rectangle(200, 380, 300, 450)));

        // Every piece left runs along the rectangle's sides: consecutive points share an x or a y.
        (double X, double Y)? previous = null;
        var points = 0;
        foreach (var line in ContentOf(doc).Split('\n'))
        {
            var parts = line.Trim().Split(' ');
            if (parts.Length != 3 || parts[2] is not ("m" or "l")) continue;
            var point = (X: double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                         Y: double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
            if (parts[2] == "l" && previous is { } p)
                Assert.True(Math.Abs(p.X - point.X) < 0.01 || Math.Abs(p.Y - point.Y) < 0.01, $"{p} -> {point}");
            previous = point;
            points++;
        }
        Assert.True(points >= 8);
        var xs = PathXs(doc);
        Assert.Contains(xs, x => Math.Abs(x - 60) < 0.01);
        Assert.Contains(xs, x => Math.Abs(x - 535) < 0.01);
        Assert.DoesNotContain(xs, x => x > 200.01 && x < 299.99);
    }

    [Fact]
    public void APathEntirelyUnderTheRectangleIsNoLongerInTheContent()
    {
        var pdf = RawPage.Build("0 0 1 rg 217 313 41 29 re f 0 1 0 rg 400 400 20 20 re f", "<< >>");

        var saved = Save(pdf, new Rectangle(200, 300, 300, 360));

        using var doc = Document.Open(saved);
        var content = ContentOf(doc);
        Assert.DoesNotContain("217 313 41 29 re", content);
        Assert.Contains("400 400 20 20 re", content);
        Assert.False(SavedFile.Holds(saved, "217 313 41 29 re"));
    }

    [Fact]
    public void AFormDrawnTwiceLosesItsTextOnlyWhereItIsDrawnUnderTheRectangle()
    {
        var form = "BT /F1 12 Tf 0 10 Td (SECRET-FORM) Tj ET"u8.ToArray();
        var pdf = RawPage.Build(
            "BT /F1 12 Tf 72 700 Td (Visible) Tj ET q 1 0 0 1 100 400 cm /Fm1 Do Q q 1 0 0 1 100 200 cm /Fm1 Do Q",
            "<< /Font << /F1 5 0 R >> /XObject << /Fm1 6 0 R >> >>",
            new RawPage.Obj(RawPage.Helvetica),
            new RawPage.Obj("<< /Type /XObject /Subtype /Form /BBox [0 0 200 50] " +
                            "/Resources << /Font << /F1 5 0 R >> >> >>", form));

        using var doc = Document.Open(Save(pdf, new Rectangle(95, 400, 250, 430)));

        var text = TextOf(doc);
        Assert.Contains("Visible", text);
        Assert.Equal(1, text.Split(["SECRET-FORM"], StringSplitOptions.None).Length - 1);
        Assert.Contains("Fm1", DrawnNames(doc));
    }

    [Fact]
    public void AFormEntirelyUnderTheRectangleIsNoLongerDrawn()
    {
        var form = Encoding.ASCII.GetBytes("BT /F1 12 Tf 0 10 Td (SECRET-FORM) Tj ET");
        var pdf = RawPage.Build(
            "q 1 0 0 1 100 400 cm /Fm1 Do Q",
            "<< /Font << /F1 5 0 R >> /XObject << /Fm1 6 0 R >> >>",
            new RawPage.Obj(RawPage.Helvetica),
            new RawPage.Obj("<< /Type /XObject /Subtype /Form /BBox [0 0 200 50] " +
                            "/Resources << /Font << /F1 5 0 R >> >> >>", form));

        var saved = Save(pdf, new Rectangle(90, 390, 310, 460));

        using var doc = Document.Open(saved);
        Assert.Empty(DrawnNames(doc));
        Assert.False(SavedFile.Holds(saved, "SECRET-FORM"));
    }
}
