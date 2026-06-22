using System.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

public class FileSpecificationTests
{
    [Fact]
    public void EmptyDocument_HasZeroEmbeddedFiles()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        Assert.False(doc.HasEmbeddedFiles);
        Assert.Empty(doc.EmbeddedFiles);
    }

    [Fact]
    public void AddEmbeddedFile_SurvivesSaveReload()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var content = Encoding.UTF8.GetBytes("Hello, attachment!");
        doc.AddEmbeddedFile("test.txt", content);

        var saved = doc.ToArray();
        using var reloaded = Document.Open(saved);

        Assert.True(reloaded.HasEmbeddedFiles);
        Assert.NotNull(reloaded.EmbeddedFiles);
        Assert.Single(reloaded.EmbeddedFiles!);
    }

    [Fact]
    public void EmbeddedFiles_ReturnsCorrectCount()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.AddEmbeddedFile("a.txt", new byte[] { 1, 2, 3 });
        doc.AddEmbeddedFile("b.txt", new byte[] { 4, 5, 6 });

        var saved = doc.ToArray();
        using var reloaded = Document.Open(saved);

        Assert.Equal(2, reloaded.EmbeddedFiles!.Count);
    }

    [Fact]
    public void FileSpecification_Name_ReturnsFilename()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.AddEmbeddedFile("report.pdf", new byte[] { 0xFF });

        var saved = doc.ToArray();
        using var reloaded = Document.Open(saved);

        Assert.Equal("report.pdf", reloaded.EmbeddedFiles![1].Name);
    }

    [Fact]
    public void FileSpecification_GetData_ReturnsEmbeddedBytes()
    {
        var content = new byte[] { 10, 20, 30, 40, 50 };
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.AddEmbeddedFile("data.bin", content);

        var saved = doc.ToArray();
        using var reloaded = Document.Open(saved);

        var data = reloaded.EmbeddedFiles![1].GetData();
        Assert.NotNull(data);
        Assert.Equal(content, data);
    }

    [Fact]
    public void FileSpecification_MimeType_ReturnsTypeIfSet()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.AddEmbeddedFile("doc.txt", Encoding.UTF8.GetBytes("content"),
            mimeType: "text/plain");

        var saved = doc.ToArray();
        using var reloaded = Document.Open(saved);

        Assert.Equal("text/plain", reloaded.EmbeddedFiles![1].MimeType);
    }

    [Fact]
    public void FileSpecification_Description_ReturnsDescriptionIfSet()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.AddEmbeddedFile("notes.txt", Encoding.UTF8.GetBytes("notes"),
            description: "My notes file");

        var saved = doc.ToArray();
        using var reloaded = Document.Open(saved);

        Assert.Equal("My notes file", reloaded.EmbeddedFiles![1].Description);
    }

    [Fact]
    public void MultipleEmbeddedFiles_AllAccessible()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var data1 = Encoding.UTF8.GetBytes("First file");
        var data2 = Encoding.UTF8.GetBytes("Second file");
        var data3 = Encoding.UTF8.GetBytes("Third file");
        doc.AddEmbeddedFile("first.txt", data1, description: "First");
        doc.AddEmbeddedFile("second.txt", data2, description: "Second");
        doc.AddEmbeddedFile("third.txt", data3, description: "Third");

        var saved = doc.ToArray();
        using var reloaded = Document.Open(saved);

        var files = reloaded.EmbeddedFiles!;
        Assert.Equal(3, files.Count);

        // Collect names and verify all are present
        var names = new HashSet<string> { files[1].Name, files[2].Name, files[3].Name };
        Assert.Contains("first.txt", names);
        Assert.Contains("second.txt", names);
        Assert.Contains("third.txt", names);

        // Verify data for each
        foreach (var file in files)
        {
            var fileData = file.GetData();
            Assert.NotNull(fileData);
            Assert.True(fileData!.Length > 0);
        }
    }
}
