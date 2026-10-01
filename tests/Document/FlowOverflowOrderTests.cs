using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>Where a page's overflow lands when the document already holds pages
/// after it: immediately after the page that ran out of room, never at the end.</summary>
public sealed class FlowOverflowOrderTests
{
    [Fact]
    public void AMiddlePagesOverflowIsInsertedRightAfterIt()
    {
        using var doc = new Aspose.Pdf.Document();
        for (var p = 1; p <= 7; p++)
        {
            var page = doc.Pages.Add(595, 842);
            page.PageInfo.Margin = new MarginInfo(36, 36, 36, 36);
            var spills = p is 1 or 3 or 5;
            var count = spills ? 28 : 3;
            for (var i = 0; i < count; i++)
            {
                var tf = new TextFragment($"page {p} line {i}");
                tf.TextState.FontSize = 12;
                tf.TextState.LineSpacing = 6;
                tf.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
                tf.TextState.LineBoxAscentEm = 0.8;
                tf.TextState.LineBoxDescentEm = 0.2;
                tf.Margin = new MarginInfo(0, 4, 0, 4);
                page.Paragraphs.Add(tf);
            }
            if (spills)
            {
                // The last paragraph straddles the page bottom by one line.
                var tall = new TextFragment($"page {p} tall " + string.Join(" ", Enumerable.Repeat("words that wrap onto three lines", 8)));
                tall.TextState.FontSize = 12;
                tall.TextState.LineSpacing = 6;
                tall.TextState.FormattingOptions.LineSpacing = TextFormattingOptions.LineSpacingMode.LineBox;
                tall.TextState.LineBoxAscentEm = 0.8;
                tall.TextState.LineBoxDescentEm = 0.2;
                tall.Margin = new MarginInfo(0, 4, 0, 4);
                page.Paragraphs.Add(tall);
            }
        }
        using var reopened = Aspose.Pdf.Document.Open(doc.ToArray());
        Assert.Equal(10, reopened.Pages.Count);
        var firstLines = new List<string>();
        for (var p = 1; p <= reopened.Pages.Count; p++)
        {
            var absorber = new TextFragmentAbsorber();
            reopened.Pages[p].Accept(absorber);
            firstLines.Add(absorber.TextFragments.Count > 0 ? absorber.TextFragments[1].Text : "");
        }
        // Pages 1, 3 and 5 each spill one line; each spill sits right after its page.
        Assert.StartsWith("page 1 line 0", firstLines[0]);
        Assert.Contains("wrap onto three", firstLines[1]);
        Assert.StartsWith("page 2 line 0", firstLines[2]);
        Assert.Contains("wrap onto three", firstLines[4]);
        Assert.StartsWith("page 5 line 0", firstLines[6]);
        Assert.Contains("wrap onto three", firstLines[7]);
        Assert.StartsWith("page 6 line 0", firstLines[8]);
        Assert.StartsWith("page 7 line 0", firstLines[9]);
    }
}
