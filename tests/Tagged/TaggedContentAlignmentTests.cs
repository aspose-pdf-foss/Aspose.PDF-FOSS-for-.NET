using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>PositionSettings on a document authored through TaggedContent: the horizontal alignment
/// places a paragraph's lines, or a header, span, link or figure as one box, inside the column
/// x 90..505 less the element's margins; the leveled headers take their own sizes and margins.</summary>
public class TaggedContentAlignmentTests
{
    private const double ColumnLeft = 90;
    private const double ColumnRight = 505;
    private const double ColumnMiddle = (ColumnLeft + ColumnRight) / 2;
    private const string Lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Aenean nec lectus ac sem "
        + "faucibus imperdiet. Sed ut erat ac magna ullamcorper hendrerit. Cras pellentesque libero semper, "
        + "gravida magna sed, luctus leo.";

    private static PositionSettings Aligned(HorizontalAlignment align, MarginInfo? margin = null)
        => new() { HorizontalAlignment = align, Margin = margin };

    /// <summary>Author a document, save and reopen it, and return its first page's text fragments.</summary>
    private static List<TextFragment> Render(Action<ITaggedContent, StructureElement> author)
    {
        using var document = new Document();
        var content = document.TaggedContent;
        author(content, content.RootElement);
        using var saved = new MemoryStream();
        document.Save(saved);
        using var reopened = new Document(new MemoryStream(saved.ToArray()));
        var absorber = new TextFragmentAbsorber();
        reopened.Pages[1].Accept(absorber);
        return absorber.TextFragments.ToList();
    }

    private static ParagraphElement Paragraph(ITaggedContent content, StructureElement root, string text,
        PositionSettings? settings)
    {
        var paragraph = content.CreateParagraphElement();
        root.AppendChild(paragraph);
        paragraph.SetText(text);
        if (settings is not null) paragraph.AdjustPosition(settings);
        return paragraph;
    }

    private static TextFragment Find(List<TextFragment> fragments, string text)
        => fragments.Single(fragment => fragment.Text == text);

    private static Rectangle Box(TextFragment fragment) => fragment.Rectangle!;

    private static double Baseline(TextFragment fragment) => fragment.BaselinePosition!.YIndent;

    [Fact]
    public void Paragraph_CenteredAndRightAligned_PlacesTheLineInTheColumn()
    {
        var fragments = Render((content, root) =>
        {
            Paragraph(content, root, "Centred line", Aligned(HorizontalAlignment.Center));
            Paragraph(content, root, "Right line", Aligned(HorizontalAlignment.Right));
            Paragraph(content, root, "Right inside margins", Aligned(HorizontalAlignment.Right, new MarginInfo(30, 0, 50, 0)));
        });

        var centred = Box(Find(fragments, "Centred line"));
        Assert.InRange((centred.LLX + centred.URX) / 2, ColumnMiddle - 0.01, ColumnMiddle + 0.01);
        Assert.InRange(Box(Find(fragments, "Right line")).URX, ColumnRight - 0.01, ColumnRight + 0.01);
        Assert.InRange(Box(Find(fragments, "Right inside margins")).URX, ColumnRight - 50 - 0.01, ColumnRight - 50 + 0.01);
    }

    [Fact]
    public void Paragraph_RightAlignedWrapped_EndsEveryLineOnTheRightEdge()
    {
        var fragments = Render((content, root) => Paragraph(content, root, Lorem, Aligned(HorizontalAlignment.Right)));

        Assert.True(fragments.Count >= 3);
        foreach (var line in fragments)
        {
            // A line keeps the space it closes with, but its last glyph stands on the edge.
            var trailing = line.Text.Length - line.Text.TrimEnd(' ').Length;
            var lastGlyph = Box(line).URX - trailing * Box(line).Width / line.Text.Length;
            Assert.InRange(Box(line).URX, ColumnRight - 0.01, ColumnRight + 0.01 + trailing * line.TextState!.FontSize);
            Assert.True(Box(line).LLX > ColumnLeft, $"'{line.Text}' starts at {Box(line).LLX}");
            Assert.True(lastGlyph <= ColumnRight + 0.01);
        }
    }

    [Fact]
    public void Paragraph_Justified_SpreadsEveryLineButTheLast()
    {
        var fragments = Render((content, root) => Paragraph(content, root, Lorem, Aligned(HorizontalAlignment.Justify)));
        var lines = fragments.GroupBy(fragment => Math.Round(Baseline(fragment), 2))
            .OrderByDescending(line => line.Key).ToList();

        Assert.True(lines.Count >= 2);
        foreach (var line in lines.Take(lines.Count - 1))
        {
            Assert.InRange(line.Min(fragment => Box(fragment).LLX), ColumnLeft - 0.01, ColumnLeft + 0.01);
            Assert.InRange(line.Max(fragment => Box(fragment).URX), ColumnRight - 0.01, ColumnRight + 0.01);
        }
        var last = lines[^1].ToList();
        Assert.Single(last);
        Assert.InRange(Box(last[0]).LLX, ColumnLeft - 0.01, ColumnLeft + 0.01);
        Assert.True(Box(last[0]).URX < ColumnRight - 1);
    }

    [Fact]
    public void Paragraph_FullJustified_SpreadsItsOnlyLine()
    {
        var fragments = Render((content, root) => Paragraph(content, root, "two words", Aligned(HorizontalAlignment.FullJustify)));

        Assert.InRange(Box(Find(fragments, "two")).LLX, ColumnLeft - 0.01, ColumnLeft + 0.01);
        Assert.InRange(Box(Find(fragments, "words")).URX, ColumnRight - 0.01, ColumnRight + 0.01);
    }

    [Fact]
    public void HeaderSpanAndLink_Centered_ArePlacedAsOneBox()
    {
        var fragments = Render((content, root) =>
        {
            var header = content.CreateHeaderElement(1);
            header.SetText("Centred header");
            root.AppendChild(header);
            header.AdjustPosition(Aligned(HorizontalAlignment.Center));
            var span = content.CreateSpanElement();
            span.SetText("Right span");
            root.AppendChild(span);
            span.AdjustPosition(Aligned(HorizontalAlignment.Right));
            var link = content.CreateLinkElement();
            link.SetText("Centred link");
            root.AppendChild(link);
            link.AdjustPosition(Aligned(HorizontalAlignment.Center));
        });

        foreach (var text in new[] { "Centred header", "Centred link" })
        {
            var box = Box(Find(fragments, text));
            Assert.InRange((box.LLX + box.URX) / 2, ColumnMiddle - 0.01, ColumnMiddle + 0.01);
        }
        Assert.InRange(Box(Find(fragments, "Right span")).URX, ColumnRight - 0.01, ColumnRight + 0.01);
    }

    [Fact]
    public void Figure_RightAligned_StandsOnTheRightEdge()
    {
        var picture = Path.Combine(Path.GetTempPath(), $"tagged-align-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(picture, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="));
        try
        {
            using var document = new Document();
            var content = document.TaggedContent;
            var figure = content.CreateFigureElement();
            content.RootElement.AppendChild(figure);
            figure.AlternativeText = "dot";
            figure.SetImage(picture, 100, 50);
            figure.AdjustPosition(Aligned(HorizontalAlignment.Right));
            using var saved = new MemoryStream();
            document.Save(saved);
            using var reopened = new Document(new MemoryStream(saved.ToArray()));
            var absorber = new ImagePlacementAbsorber();
            reopened.Pages[1].Accept(absorber);

            var placed = Assert.Single(absorber.ImagePlacements).Rectangle;
            Assert.InRange(placed.LLX, ColumnRight - 100 - 0.01, ColumnRight - 100 + 0.01);
        }
        finally
        {
            File.Delete(picture);
        }
    }

    [Fact]
    public void Paragraph_AdjustedWithoutMargin_DropsItsDefaultMargins()
    {
        double Placed(PositionSettings? settings)
            => Baseline(Find(Render((content, root) => Paragraph(content, root, "Text.", settings)), "Text."));

        // The default paragraph opens 2 pt below the column top; an adjusted one at the top.
        Assert.InRange(Placed(new PositionSettings()) - Placed(null), 1.99, 2.01);
    }

    [Theory]
    [InlineData(1, 18, 9)]
    [InlineData(2, 16, 7)]
    [InlineData(3, 14, 6)]
    [InlineData(4, 12, 5)]
    [InlineData(5, 10, 4)]
    [InlineData(6, 9, 3)]
    public void Header_ByLevel_TakesItsSizeAndMargins(int level, double size, double margin)
    {
        var fragments = Render((content, root) =>
        {
            var before = content.CreateSpanElement();
            before.SetText("before");
            root.AppendChild(before);
            var header = content.CreateHeaderElement(level);
            header.SetText("Header");
            root.AppendChild(header);
            var after = content.CreateSpanElement();
            after.SetText("after");
            root.AppendChild(after);
        });

        Assert.Equal(size, Find(fragments, "Header").TextState!.FontSize, 3);
        // Two 12 pt spans around the header: their baselines lie one span line, the header and
        // its margin above and below apart.
        var pitch = Baseline(Find(fragments, "before")) - Baseline(Find(fragments, "after"));
        Assert.InRange(pitch, 12 + size + 2 * margin - 0.01, 12 + size + 2 * margin + 0.01);
    }

    [Fact]
    public void Header_Baseline_UsesTheDescentItsFontDescriptorStates()
    {
        using var document = new Document();
        var content = document.TaggedContent;
        var header = content.CreateHeaderElement(1);
        header.SetText("Header");
        content.RootElement.AppendChild(header);
        using var saved = new MemoryStream();
        document.Save(saved);
        using var reopened = new Document(new MemoryStream(saved.ToArray()));
        var page = reopened.Pages[1];
        var absorber = new TextFragmentAbsorber("Header");
        page.Accept(absorber);
        var reader = page.Reader;
        var fonts = reader.ResolveDict(reader.ResolveDict(page.Dict.Get("Resources"))!.Get("Font"))!;
        var font = reader.ResolveDict(Assert.Single(fonts.Keys.Select(fonts.Get)))!;
        var descendant = reader.ResolveDict(((Aspose.Pdf.Core.PdfArray)reader.Resolve(font.Get("DescendantFonts"))!)[0])!;
        var descent = Math.Abs(reader.ResolveDict(descendant.Get("FontDescriptor"))!.GetInt("Descent")) / 1000.0;

        // An H1 opens 9 pt below the column top (770) and its 18 pt line stands on the descent.
        var expected = 770 - 9 - (1 - descent) * 18;
        Assert.InRange(Baseline(absorber.TextFragments[1]), expected - 0.005, expected + 0.005);
    }
}
