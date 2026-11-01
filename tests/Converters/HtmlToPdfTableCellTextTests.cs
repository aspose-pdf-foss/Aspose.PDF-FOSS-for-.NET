using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>The text an HTML table cell draws: its lines, their runs, and what the sheet hides.</summary>
public class HtmlToPdfTableCellTextTests
{
    private const string Sheet = "<style>a.tip { text-decoration:none; } a.tip span { {0} white-space:nowrap; }</style>";

    private const string Body = "<table><tr><td width=200>Unit: </td><td width=200>Ward<br></td></tr><tr>"
        + "<td align=right> <font size=-2> <p><a class=\"tip\" href=\"#i\">89&nbsp;H<span> <b> Note:</b> <br/>Checked by hand</span></a></p> </font> </td>"
        + "<td align=right> <font size=-2> 87&nbsp;H </font> </td></tr></table>";

    private static string[] Lines(string html)
    {
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        using var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        using var reopened = new Document(saved);
        var absorber = new TextFragmentAbsorber();
        reopened.Pages[1].Accept(absorber);
        return absorber.TextFragments.Cast<TextFragment>().Select(f => f.Text.Replace(' ', ' ').Trim())
            .Where(t => t.Length > 0).ToArray();
    }

    [Fact]
    public void ACellLineWithABoldRunIsDrawnOnceAndTheNextLineKeepsItsText()
    {
        // The whitespace before the paragraph rejoins its first line; the bold run's marks follow it.
        var lines = Lines("<html><head>" + Sheet.Replace("{0}", "") + "</head><body>" + Body + "</body></html>");
        Assert.Single(lines, t => t.StartsWith("89 H"));
        Assert.Single(lines, t => t == "Note:");
        Assert.Contains("Checked by hand", lines);
    }

    [Fact]
    public void ASpanTheSheetHidesThroughItsAncestorIsNotDrawn()
    {
        var lines = Lines("<html><head>" + Sheet.Replace("{0}", "visibility:hidden;") + "</head><body>" + Body + "</body></html>");
        Assert.Single(lines, t => t == "89 H");
        Assert.DoesNotContain(lines, t => t.Contains("Note") || t.Contains("Checked"));
        Assert.Contains("87 H", lines);
    }
}
