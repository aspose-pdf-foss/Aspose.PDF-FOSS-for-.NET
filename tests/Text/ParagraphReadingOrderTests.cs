using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class ParagraphReadingOrderTests
{
    private const double LinePitch = 14;

    /// <summary>A page with a block of <paramref name="lines"/> lines at each (x, top) given, each line its
    /// block's name and line number ("A1", "A2", ...).</summary>
    private static Document Page(params (string name, double x, double top, int lines)[] blocks)
    {
        var doc = new Document();
        var page = doc.Pages.Add();
        var builder = new TextBuilder(page);
        foreach (var (name, x, top, lines) in blocks)
            for (var i = 0; i < lines; i++)
            {
                var fragment = new TextFragment($"{name}{i + 1} lorem ipsum") { Position = new Position(x, top - i * LinePitch) };
                fragment.TextState.FontSize = 10;
                builder.AppendText(fragment);
            }
        var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    /// <summary>The first line name of each block in the order the absorber reports the sections' paragraphs.</summary>
    private static List<string> BlockOrder(Document doc)
    {
        var absorber = new ParagraphAbsorber();
        absorber.Visit(doc);
        var order = new List<string>();
        foreach (var section in absorber.PageMarkups[0].Sections)
            foreach (var paragraph in section.Paragraphs)
                foreach (var word in paragraph.Text.Split(' ', '\r', '\n').Where(w => w.Length > 1 && char.IsUpper(w[0]) && char.IsDigit(w[1])))
                    if (!order.Contains(word.Substring(0, 1))) order.Add(word.Substring(0, 1));
        return order;
    }

    [Fact]
    public void TwoColumnsThatStartOnOneLineAreReadLeftFirst_WhenTheLeftOneIsTheLonger()
    {
        using var doc = Page(("H", 72, 760, 1), ("L", 72, 700, 12), ("R", 320, 700, 4));
        Assert.Equal(new[] { "H", "L", "R" }, BlockOrder(doc));
    }

    [Fact]
    public void ThreeColumnsAreReadColumnByColumn_UnderAHeadingAcrossThem()
    {
        using var doc = Page(
            ("H", 72, 760, 1),
            ("A", 72, 700, 5), ("B", 72, 600, 6),
            ("C", 240, 700, 3), ("D", 240, 620, 8),
            ("E", 410, 700, 2), ("F", 410, 640, 3));
        var order = BlockOrder(doc);
        Assert.Equal("H", order[0]);
        Assert.True(order.IndexOf("A") < order.IndexOf("B") && order.IndexOf("B") < order.IndexOf("C"), string.Join(",", order));
        Assert.True(order.IndexOf("D") < order.IndexOf("E") && order.IndexOf("E") < order.IndexOf("F"), string.Join(",", order));
    }

    [Fact]
    public void RowsOfLabelsAndValuesAreReadRowByRow()
    {
        using var doc = Page(("A", 72, 700, 1), ("B", 300, 700, 1), ("C", 72, 660, 1), ("D", 300, 660, 1));
        Assert.Equal(new[] { "A", "B", "C", "D" }, BlockOrder(doc));
    }
}
