using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A fragment whose segments flow as runs
/// (<see cref="TextFormattingOptions.SegmentsFlowAsRuns"/>): the text wraps across
/// the segments, every run is drawn at its own size and face, and a line is boxed
/// by its largest run.</summary>
public sealed class SegmentsFlowAsRunsTests
{
    private const double Margin = 36;

    [Fact]
    public void RunsShareOneBaselineAndAdvanceByTheirOwnWidths()
    {
        var shows = Render(("small ", 9, null), ("big", 24, null));
        Assert.Equal(2, shows.Count);
        Assert.Equal(shows[0].Y, shows[1].Y, 6);
        Assert.Equal(9, shows[0].Size, 6);
        Assert.Equal(24, shows[1].Size, 6);
        // "small " at 9 pt is 23.5 wide in Helvetica; the big run starts right after it.
        Assert.Equal(Margin + 23.5, shows[1].X, 2);
    }

    [Fact]
    public void ALineIsBoxedByItsLargestRun()
    {
        // Twelve short runs alternating 18 and 10 pt wrap onto two lines; both hold
        // an 18 pt run, so the pitch between them is the 18 pt line box.
        var runs = new List<(string, double, string?)>();
        for (var i = 0; i < 12; i++) runs.Add(("alpha beta ", i % 3 == 0 ? 18 : 10, null));
        var shows = Render(runs.ToArray());
        var baselines = shows.Select(s => Math.Round(s.Y, 3)).Distinct().OrderByDescending(y => y).ToArray();
        Assert.Equal(2, baselines.Length);
        Assert.Equal(18 * 1.5, baselines[0] - baselines[1], 3);
    }

    [Fact]
    public void AWordSplitBetweenSegmentsIsNotBroken()
    {
        // "alphabet" + "ical " repeated: with a 200 pt band each pair is one word of
        // 81.6 pt, so the wrap breaks between pairs, never inside one.
        var runs = new List<(string, double, string?)>();
        for (var i = 0; i < 6; i++) { runs.Add(("alphabet", 12, null)); runs.Add(("ical ", 20, null)); }
        var shows = Render(runs.ToArray(), width: 200);
        for (var i = 0; i < shows.Count; i += 2)
            Assert.Equal(shows[i].Y, shows[i + 1].Y, 6);
    }

    [Fact]
    public void ARunDrawsInItsOwnFace()
    {
        var shows = Render(("plain ", 12, null), ("bold", 12, "Helvetica-Bold"));
        Assert.NotEqual(shows[0].Font, shows[1].Font);
        Assert.Equal(shows[0].Y, shows[1].Y, 6);
    }

    [Fact]
    public void AnInlinePictureStandsOnTheBaselineAndRaisesTheLineToItsHeight()
    {
        // A 30 x 20 picture between two 12 pt runs: the line's ascent grows from the
        // box's 12.6 (0.8 em plus half of 6 of leading) to the picture's 20, the text
        // sits on that baseline, the picture on it too, and the run after the picture
        // starts 30 further right. The next line pitches on the text's box again.
        var (shows, pictures) = RenderWithPicture(30, 20, "before ", " after and more words to wrap this paragraph onto another line of text, enough of them to pass the right margin of the page");
        var top = 842 - Margin;
        Assert.Equal(top - 20, shows[0].Y, 3);
        Assert.Equal(top - 20, shows[1].Y, 3);
        var (e, f, a, d) = pictures.Single();
        Assert.Equal(top - 20, f, 3);
        Assert.Equal(20, d, 3);
        Assert.Equal(30, a, 3);
        // "before " at 12 pt is 37.356 wide in Helvetica.
        Assert.Equal(Margin + 37.356, e, 3);
        Assert.Equal(e + 30, shows[1].X, 3);
        // The descent stays the text's 5.4, so the second line's baseline is 5.4 + 12.6 below.
        Assert.Equal(shows[1].Y - 18, shows[2].Y, 3);
    }

    [Fact]
    public void AParagraphOfAPictureAloneIsExactlyThePicture()
    {
        // No text on the line: the box is the picture's 20, nothing under it, so the
        // plain paragraph after it seats its baseline 20 + 12.6 below the top.
        var (shows, pictures) = RenderWithPicture(30, 20, "", "", then: true);
        var top = 842 - Margin;
        Assert.Equal(top - 20, pictures.Single().F, 3);
        Assert.Equal(top - 20 - 12.6, shows.Single().Y, 3);
    }

    [Fact]
    public void AShortInlinePictureLeavesTheTextBoxAlone()
    {
        var (shows, pictures) = RenderWithPicture(30, 8, "before ", " after");
        var top = 842 - Margin;
        Assert.Equal(top - 12.6, shows[0].Y, 3);
        Assert.Equal(top - 12.6, pictures.Single().F, 3);
    }

    private static (List<Show> Shows, List<(double E, double F, double A, double D)> Pictures) RenderWithPicture(
        int width, int height, string before, string after, bool then = false)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, Margin, Margin);
        var fragment = new TextFragment();
        fragment.TextState.FontSize = 12;
        fragment.TextState.LineSpacing = 6;
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.LineBoxAscentEm = 0.8;
        fragment.TextState.LineBoxDescentEm = 0.2;
        fragment.TextState.FormattingOptions.SegmentsFlowAsRuns = true;
        foreach (var text in new[] { before, null, after })
        {
            var segment = new TextSegment(text ?? string.Empty);
            segment.TextState.FontSize = 12;
            segment.TextState.LineSpacing = 6;
            segment.TextState.LineBoxAscentEm = 0.8;
            segment.TextState.LineBoxDescentEm = 0.2;
            if (text is null)
                segment.InlineImage = new Image
                {
                    ImageStream = new MemoryStream(Aspose.Pdf.Tests.FlowImageTests.Png(width, height)),
                    FixWidth = width,
                    FixHeight = height,
                };
            fragment.Segments.Add(segment);
        }
        page.Paragraphs.Add(fragment);
        if (then)
        {
            var end = new TextFragment("end");
            end.TextState.FontSize = 12;
            end.TextState.LineSpacing = 6;
            end.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
            end.TextState.LineBoxAscentEm = 0.8;
            end.TextState.LineBoxDescentEm = 0.2;
            page.Paragraphs.Add(end);
        }
        using var reopened = Document.Open(doc.ToArray());

        var shows = new List<Show>();
        var pictures = new List<(double E, double F, double A, double D)>();
        string font = "";
        double size = 0, x = 0, y = 0;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case SelectFont tf: font = tf.Name; size = tf.Size; break;
                case MoveTextPosition td: x = td.X; y = td.Y; break;
                case ShowText tj: shows.Add(new Show(font, size, x, y)); break;
                case ConcatenateMatrix cm when cm.Matrix.D > 1: pictures.Add((cm.Matrix.E, cm.Matrix.F, cm.Matrix.A, cm.Matrix.D)); break;
            }
        }
        Assert.NotEmpty(shows);
        return (shows, pictures);
    }

    private static List<Show> Render(params (string Text, double Size, string? Face)[] runs) => Render(runs, width: 0);

    private static List<Show> Render((string Text, double Size, string? Face)[] runs, double width)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        var right = width > 0 ? page.PageInfo.Width - Margin - width : Margin;
        page.PageInfo.Margin = new MarginInfo(Margin, Margin, right, Margin);
        var fragment = new TextFragment();
        fragment.TextState.FontSize = 12;
        fragment.TextState.LineSpacing = 6;
        fragment.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
        fragment.TextState.LineBoxAscentEm = 0.8;
        fragment.TextState.LineBoxDescentEm = 0.2;
        fragment.TextState.FormattingOptions.SegmentsFlowAsRuns = true;
        foreach (var (text, runSize, face) in runs)
        {
            var segment = new TextSegment(text);
            segment.TextState.FontSize = (float)runSize;
            segment.TextState.LineSpacing = (float)(runSize * 0.5);
            segment.TextState.LineBoxAscentEm = 0.8;
            segment.TextState.LineBoxDescentEm = 0.2;
            if (face is not null) segment.TextState.FontName = face;
            fragment.Segments.Add(segment);
        }
        page.Paragraphs.Add(fragment);
        using var reopened = Document.Open(doc.ToArray());

        var shows = new List<Show>();
        string font = "";
        double size = 0, x = 0, y = 0;
        foreach (var op in reopened.Pages[1].Contents)
        {
            switch (op)
            {
                case SelectFont tf: font = tf.Name; size = tf.Size; break;
                case MoveTextPosition td: x = td.X; y = td.Y; break;
                case ShowText tj: shows.Add(new Show(font, size, x, y)); break;
            }
        }
        Assert.NotEmpty(shows);
        return shows;
    }

    private sealed record Show(string Font, double Size, double X, double Y);
}
