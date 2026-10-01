using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>Markdown laid out as pages.</summary>
public class MarkdownToPdfConverterTests
{
    private static Document Load(string markdown)
        => new(new MemoryStream(Encoding.UTF8.GetBytes(markdown)), new MdLoadOptions());

    /// <summary>The page's lines top to bottom: the baseline and the text.</summary>
    private static (double Y, string Text)[] Lines(Page page)
    {
        var absorber = new TextFragmentAbsorber();
        page.Accept(absorber);
        return absorber.TextFragments!.Cast<TextFragment>()
            .GroupBy(f => System.Math.Round(f.Rectangle!.LLY, 1))
            .OrderByDescending(g => g.Key)
            .Select(g => (g.Key, string.Concat(g.OrderBy(f => f.Rectangle!.LLX).Select(f => f.Text))))
            .ToArray();
    }

    [Fact]
    public void AParagraphAfterOneThatRanOverOntoThePageKeepsItsGap()
    {
        var sentence = "Amber basalt cedar delta ember fjord garnet harbor indigo juniper kestrel lagoon meadow nectar. ";
        var markdown = string.Concat(Enumerable.Repeat(sentence, 120)) + "\n\nNext paragraph starts here.\n";
        using var doc = Load(markdown);

        Assert.True(doc.Pages.Count >= 2);
        var lines = Lines(doc.Pages[2]);
        var next = System.Array.FindIndex(lines, l => l.Text.StartsWith("Next paragraph"));
        Assert.True(next > 0, "the second paragraph starts on the second page under the first's last lines");
        var pitch = lines[0].Y - lines[1].Y;
        var gap = lines[next - 1].Y - lines[next].Y;
        Assert.True(gap > 1.3 * pitch, $"gap {gap:0.0} over a pitch of {pitch:0.0}");
    }
}
