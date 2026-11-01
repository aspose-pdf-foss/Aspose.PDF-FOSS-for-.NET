using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Converters;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

public class SvgSwitchTests
{
    private static string PageContent(string svg)
    {
        using var doc = SvgToPdfConverter.Convert(Encoding.UTF8.GetBytes(svg));
        var page = doc.Pages[1];
        var reader = page.Reader;
        var contents = reader.Resolve(page.Dict.Get("Contents"));
        var text = new StringBuilder();
        if (contents is Core.PdfStream s) text.Append(Compat.Latin1.GetString(reader.DecodeStream(s)));
        else if (contents is Core.PdfArray parts)
            foreach (var part in parts)
                if (reader.ResolveStream(part) is { } ps) text.Append(Compat.Latin1.GetString(reader.DecodeStream(ps)));
        return text.ToString();
    }

    [Fact]
    public void ForeignObjectGatedByAnUnsupportedExtension_FallsThroughToTheDrawing()
    {
        // An illustrator file: its private payload sits in a foreignObject only its own reader supports.
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:i=\"http://ns.adobe.com/AdobeIllustrator/10.0/\" viewBox=\"0 0 100 100\">" +
            "<switch><foreignObject requiredExtensions=\"http://ns.adobe.com/AdobeIllustrator/10.0/\" x=\"0\" y=\"0\" width=\"1\" height=\"1\">" +
            "<i:pgfRef /></foreignObject><g><rect x=\"10\" y=\"10\" width=\"50\" height=\"50\" fill=\"#ff0000\" /></g></switch></svg>";
        Assert.Contains("re", PageContent(svg));
        Assert.Contains("1 0 0 rg", PageContent(svg));
    }

    [Fact]
    public void EmptyHtmlLabel_DrawsNeitherItNorTheFallbackText()
    {
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\"><switch>" +
            "<foreignObject width=\"80\" height=\"12\" requiredFeatures=\"http://www.w3.org/TR/SVG11/feature#Extensibility\">" +
            "<div xmlns=\"http://www.w3.org/1999/xhtml\" style=\"font-size: 11px\"></div></foreignObject>" +
            "<text x=\"40\" y=\"12\">[Not supported by viewer]</text></switch></svg>";
        Assert.DoesNotContain("Not supported", PageContent(svg));
    }
}
