using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A table cell painted with a background as tall as the cell, whose runs are each drawn
/// in their own q ... Q under the cell's clip rectangle - a word processor's export. Replacing one
/// run takes the run's captured background out of the page, but the clip rectangles of the cell's
/// runs share that geometry and are no paint: they stay.</summary>
public class ReplacedRunClipTests
{
    private const string Cell = "300 500 260 190";
    private const int ClippedRuns = 4;

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static Document Build()
    {
        var content = new StringBuilder($"q 1 g {Cell} re f* Q\n");
        for (var i = 0; i < ClippedRuns; i++)
        {
            var text = i == 1 ? "KEY" : "Text" + i;
            content.Append($"q {Cell} re W* n BT /F1 11 Tf 1 0 0 1 320 {Num(670 - 18 * i)} Tm ({text}) Tj ET Q\n");
        }
        var body = content.ToString();
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> >>",
            $"<< /Length {body.Length} >>\nstream\n{body}\nendstream",
        };
        var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s), 0, s.Length);
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        ms.Position = 0;
        return new Document(ms);
    }

    /// <summary>The clip rectangles (an <c>re</c> followed by W or W*) the page draws.</summary>
    private static int ClipRectangles(Page page)
    {
        var ops = page.Contents.ToList();
        var count = 0;
        for (var i = 0; i + 1 < ops.Count; i++)
            if (ops[i] is Aspose.Pdf.Operators.Re && ops[i + 1].CommandName is "W" or "W*") count++;
        return count;
    }

    [Fact]
    public void ReplacingARunOverACellBackgroundKeepsTheCellsClipRectangles()
    {
        using var doc = Build();
        var absorber = new TextFragmentAbsorber("KEY");
        doc.Pages[1].Accept(absorber);
        var fragment = Assert.Single(absorber.TextFragments.Cast<TextFragment>());
        fragment.Text = "A longer value";
        var saved = new MemoryStream();
        doc.Save(saved);
        using var reopened = new Document(saved);
        Assert.Equal(ClippedRuns, ClipRectangles(reopened.Pages[1]));
        var all = new TextAbsorber();
        reopened.Pages[1].Accept(all);
        Assert.Contains("A longer value", all.Text);
    }
}
