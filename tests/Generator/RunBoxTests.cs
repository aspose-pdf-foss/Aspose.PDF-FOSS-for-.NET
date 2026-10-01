using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>A run in a box of its own (<see cref="TextFormattingOptions.RunBorder"/>,
/// <see cref="TextFormattingOptions.BackgroundCornerRadii"/> and a segment's
/// <see cref="TextState.BackgroundColor"/> on a paragraph whose segments flow as runs):
/// the box is the run's advance wide and its line box tall, grown by its border's
/// bands; the run occupies the side bands; the line seats on the run whose box is
/// tallest; the space a wrapped line keeps hangs past the box. Read back from the page.</summary>
public sealed class RunBoxTests
{
    private const double Margin = 36;
    private const double PageTop = 842 - Margin;
    private const double Size = 12;
    private const double AscentEm = 0.8;
    private const double DescentEm = 0.2;
    private const double LineBox = 18;
    // Helvetica advances, in thousandths of an em: "mark" and a space.
    private const double MarkWidth = (833 + 556 + 333 + 500) * Size / 1000;
    private const double SpaceWidth = 278 * Size / 1000;

    [Fact]
    public void ARunsBackgroundIsItsAdvanceWideAndItsLineBoxTall()
    {
        var page = Render(Runs(Plain("a "), Boxed("mark", background: true), Plain(" z")));
        var mark = page.Texts.Single(t => t.Text == "mark");
        var fill = page.Fills.Single();
        Assert.Equal(mark.X, fill.X, 2);
        Assert.Equal(MarkWidth, fill.Width, 2);
        Assert.Equal(mark.Y - DescentEm * Size, fill.Y, 2);
        Assert.Equal((AscentEm + DescentEm) * Size, fill.Height, 2);
        // Painted between the run before it and its own text.
        Assert.Equal(1, page.TextsBeforeFill);
    }

    [Fact]
    public void ARoundedRunIsClippedCornerByCorner()
    {
        var run = Boxed("mark", background: true);
        run.TextState.FormattingOptions.BackgroundCornerRadii = new CornerRadii(new CornerRadius(3));
        Assert.Equal(4, Render(Runs(Plain("a "), run)).Clips);
    }

    [Fact]
    public void ABorderedRunOccupiesItsSideBands()
    {
        var run = Boxed("mark", border: new BorderInfo(BorderSide.All, 1) { Left = new GraphInfo { LineWidth = 4 } });
        var page = Render(Runs(Plain("a "), run, Plain("z")));
        var a = page.Texts.Single(t => t.Text.StartsWith('a'));
        var mark = page.Texts.Single(t => t.Text == "mark");
        var z = page.Texts.Single(t => t.Text == "z");
        var boxLeft = a.X + (556 + 278) * Size / 1000;
        Assert.Equal(boxLeft + 4, mark.X, 2);
        Assert.Equal(boxLeft + 4 + MarkWidth + 1, z.X, 2);
    }

    [Fact]
    public void TheLineSeatsOnTheRunWhoseBoxIsTallest()
    {
        // A 3 pt top band and a 1 pt bottom one on a run whose line box is 24: the
        // leading shares 24 - (12 + 4) round the grown box, and the text sits under the
        // top band - 4 + 3 + 9.6 under the line's top, where the plain runs would seat it
        // (24 - 12) / 2 + 9.6 under it.
        var border = new BorderInfo(BorderSide.Top | BorderSide.Bottom, 1) { Top = new GraphInfo { LineWidth = 3 } };
        var run = Boxed("mark", border: border);
        run.TextState.LineSpacing = (float)(24 - Size);
        var page = Render(Runs(Plain("a "), run));
        Assert.Equal(PageTop - (4 + 3 + AscentEm * Size), page.Texts.Single(t => t.Text == "mark").Y, 2);
    }

    [Fact]
    public void TheSpaceAWrappedLineKeepsHangsPastTheBox()
    {
        var words = string.Join(" ", Enumerable.Repeat("mark", 40));
        var paragraph = Runs(Boxed(words, background: true));
        paragraph.TextState.FormattingOptions.HangingBreakSpace = true;
        var page = Render(paragraph);
        var first = page.Fills.OrderByDescending(f => f.Y).First();
        var perLine = (int)((595 - 2 * Margin + SpaceWidth) / (MarkWidth + SpaceWidth));
        Assert.Equal(perLine * MarkWidth + (perLine - 1) * SpaceWidth, first.Width, 2);
    }

    private static TextFragment Runs(params TextSegment[] segments)
    {
        var fragment = new TextFragment();
        fragment.TextState.FontSize = (float)Size;
        fragment.TextState.LineSpacing = (float)(LineBox - Size);
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.LineBoxAscentEm = AscentEm;
        fragment.TextState.LineBoxDescentEm = DescentEm;
        fragment.TextState.FormattingOptions.SegmentsFlowAsRuns = true;
        foreach (var segment in segments) fragment.Segments.Add(segment);
        return fragment;
    }

    private static TextSegment Plain(string text)
    {
        var segment = new TextSegment(text);
        segment.TextState.FontSize = (float)Size;
        segment.TextState.LineSpacing = (float)(LineBox - Size);
        segment.TextState.LineBoxAscentEm = AscentEm;
        segment.TextState.LineBoxDescentEm = DescentEm;
        return segment;
    }

    private static TextSegment Boxed(string text, bool background = false, BorderInfo? border = null)
    {
        var segment = Plain(text);
        if (background) segment.TextState.BackgroundColor = Color.FromRgb(1, 1, 0);
        segment.TextState.FormattingOptions.RunBorder = border;
        return segment;
    }

    private static RenderedPage Render(TextFragment paragraph)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        page.Paragraphs.Add(paragraph);
        using var reopened = Document.Open(doc.ToArray());
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(reopened.Pages[1]);
        var placed = absorber.TextFragments.Select(f => new Placed(f.Text, f.Position!.XIndent, f.Position.YIndent)).ToList();
        var fills = new List<Re>();
        var clips = 0;
        var yellow = false;
        var texts = 0;
        var textsBeforeFill = -1;
        Re? pending = null;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case SetRGBColor rg: yellow = rg.R == 1 && rg.G == 1 && rg.B == 0; break;
                case Re re: pending = re; break;
                case Fill when pending is not null && yellow:
                    fills.Add(pending);
                    if (textsBeforeFill < 0) textsBeforeFill = texts;
                    pending = null;
                    break;
                case Clip: clips++; break;
                case BT: texts++; break;
            }
        }
        return new RenderedPage(placed, fills, clips, textsBeforeFill);
    }

    private sealed record Placed(string Text, double X, double Y);

    private sealed record RenderedPage(List<Placed> Texts, List<Re> Fills, int Clips, int TextsBeforeFill);
}
