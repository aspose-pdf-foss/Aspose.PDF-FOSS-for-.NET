using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.PdfToMarkdown;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>
/// Covers the <c>doc.Save(path, new MarkdownSaveOptions())</c> save path
/// (distinct from the <see cref="Aspose.Pdf.Converters.PdfToMarkdownConverter"/> fragment API).
/// </summary>
public class MarkdownSaveOptionsTests
{
    [Fact]
    public void MarkdownSaveOptions_Defaults()
    {
        var opts = new MarkdownSaveOptions();
        Assert.Equal("resources", opts.ResourcesDirectoryName);
        Assert.False(opts.UseImageHtmlTag);
        Assert.Null(opts.AreaToExtract);
        Assert.Equal(SaveFormat.Markdown, opts.SaveFormat);
    }

    [Fact]
    public void Save_ToFile_LargeFontBecomesHeading()
    {
        var content = Encoding.ASCII.GetBytes(
            "BT /F1 20 Tf 72 700 Td (Big Heading) Tj ET\n" +
            "BT /F1 10 Tf 72 650 Td (Ordinary body text goes here) Tj ET");
        using var doc = Document.Open(PdfBuilder.BuildWithTextContent(content));

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"md_save_{System.Guid.NewGuid():N}.md");
        try
        {
            doc.Save(path, new MarkdownSaveOptions());
            var lines = System.IO.File.ReadAllLines(path);

            Assert.Equal("# Big Heading", lines[0]);
            Assert.Contains(lines, l => l.Contains("Ordinary body text goes here"));
            // Body text must not be promoted to a heading.
            Assert.DoesNotContain(lines, l => l.StartsWith("#") && l.Contains("Ordinary"));
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Save_ToStream_ProducesMarkdown()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 700 Td (Hello stream) Tj ET");
        using var doc = Document.Open(PdfBuilder.BuildWithTextContent(content));

        using var ms = new System.IO.MemoryStream();
        doc.Save(ms, new MarkdownSaveOptions());
        var text = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("Hello stream", text);
    }
}
