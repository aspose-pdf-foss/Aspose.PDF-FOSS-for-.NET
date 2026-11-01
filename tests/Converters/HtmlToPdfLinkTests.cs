using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>The link annotations HTML anchors become.</summary>
public class HtmlToPdfLinkTests
{
    private static Document Load(string html)
    {
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        using var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    [Fact]
    public void ALinksRectangleSpansItsAnchorsTextAsDrawn()
    {
        // Anchors deep in a paragraph, one of them in bold: each rectangle lies over its text.
        const string html = "<html><body><p>Amber basalt cedar delta ember fjord garnet harbor indigo juniper kestrel lagoon "
            + "meadow nectar orchid pebble quartz ripple saffron tundra umber valley willow xenon yarrow zephyr anchor birch "
            + "canyon dune amber basalt <a href=\"https://example.org/a\">cedar delta ember</a> fjord garnet harbor indigo "
            + "juniper kestrel lagoon meadow nectar orchid pebble quartz ripple saffron tundra umber valley willow xenon "
            + "yarrow zephyr anchor birch <a href=\"https://example.org/b\">canyon dune</a> amber.</p>"
            + "<p><b>Bold text with <a href=\"https://example.org/c\">a bold link</a> inside.</b></p></body></html>";
        using var doc = Load(html);
        var page = doc.Pages[1];
        var links = page.Annotations.OfType<LinkAnnotation>().ToList();
        Assert.Equal(3, links.Count);

        foreach (var (uri, phrase) in new[] { ("a", "cedar delta ember"), ("b", "canyon dune"), ("c", "a bold link") })
        {
            var link = Assert.Single(links, l => l.Action is GoToURIAction { URI: { } u } && u.EndsWith("/" + uri));
            var absorber = new TextFragmentAbsorber(phrase);
            page.Accept(absorber);
            // The phrase may occur earlier in plain text too: the linked one lies under the rectangle.
            var text = Assert.Single(absorber.TextFragments!.Cast<TextFragment>(),
                f => f.Rectangle!.LLY < link.Rect!.URY && f.Rectangle.URY > link.Rect.LLY
                     && f.Rectangle.LLX < link.Rect.URX && f.Rectangle.URX > link.Rect.LLX);
            Assert.InRange(link.Rect!.LLX, text.Rectangle!.LLX - 1, text.Rectangle.LLX + 1);
            Assert.InRange(link.Rect.URX, text.Rectangle.URX - 1, text.Rectangle.URX + 1);
        }
    }

    [Fact]
    public void TheLinksOfTableRowsThatSpillOntoTheNextPageStandOnThatPage()
    {
        var rows = new StringBuilder();
        for (var i = 1; i <= 120; i++)
            rows.Append("<tr><td>Name ").Append(i).Append("</td><td><a href=\"https://example.org/").Append(i).Append("\">Site ").Append(i).Append("</a></td></tr>");
        using var doc = Load("<html><head><style>.wrap { position: relative; top: 0 }</style></head><body><div class=\"wrap\"><table>"
            + rows + "</table></div><p>After table</p></body></html>");
        Assert.True(doc.Pages.Count >= 2, "the table runs onto a second page");
        for (var p = 1; p <= doc.Pages.Count; p++)
        {
            var absorber = new TextFragmentAbsorber();
            doc.Pages[p].Accept(absorber);
            var sites = absorber.TextFragments.Cast<TextFragment>().Where(f => f.Text.Trim().StartsWith("Site ")).ToList();
            var links = doc.Pages[p].Annotations.OfType<LinkAnnotation>().ToList();
            Assert.Equal(sites.Count, links.Count);
            foreach (var site in sites)
                Assert.Contains(links, l => l.Rect!.LLY < site.Rectangle!.URY && l.Rect.URY > site.Rectangle.LLY);
        }
    }
}
