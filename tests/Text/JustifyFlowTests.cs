using System;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A paragraph aligned <see cref="HorizontalAlignment.Justify"/> in the page flow.</summary>
public class JustifyFlowTests
{
    private const string Text = "Amber basalt cedar delta ember fjord garnet harbor indigo juniper kestrel lagoon meadow "
        + "nectar orchid pebble quartz ripple saffron tundra umber valley willow xenon yarrow zephyr anchor birch canyon "
        + "dune amber basalt cedar delta ember fjord garnet harbor indigo juniper kestrel lagoon meadow nectar orchid pebble.";

    /// <summary>The right end of every line of the page, top to bottom.</summary>
    private static double[] LineEnds(HorizontalAlignment alignment)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        var fragment = new TextFragment(Text) { HorizontalAlignment = alignment };
        fragment.TextState.FontSize = 11;
        page.Paragraphs.Add(fragment);
        using var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        using var read = new Document(saved);
        var absorber = new TextFragmentAbsorber();
        read.Pages[1].Accept(absorber);
        return absorber.TextFragments!.Cast<TextFragment>()
            .GroupBy(f => Math.Round(f.Rectangle!.LLY))
            .OrderByDescending(g => g.Key)
            .Select(g => g.Max(f => f.Rectangle!.URX))
            .ToArray();
    }

    [Fact]
    public void EveryLineButTheLastIsStretchedToTheMeasure()
    {
        var ends = LineEnds(HorizontalAlignment.Justify);
        Assert.True(ends.Length >= 3);
        var measure = ends[0];
        Assert.All(ends.Take(ends.Length - 1), end => Assert.InRange(end, measure - 0.5, measure + 0.5));
        Assert.True(ends[^1] < measure - 5, "the last line keeps its natural width");

        var ragged = LineEnds(HorizontalAlignment.Left);
        Assert.True(ragged.Take(ragged.Length - 1).Any(end => end < measure - 5), "the left-aligned lines are ragged");
    }
}
