using System;
using System.IO;
using Aspose.Pdf.Comparison;
using Aspose.Pdf.Text;
using Xunit;

#pragma warning disable CA1416 // the image format is a name handed to the comparer, not a GDI+ call
namespace Aspose.Pdf.Tests.GraphicalComparison;

public class GraphicalPdfComparerTests
{
    private const int LowDpi = 36;

    /// <summary>A one-page document showing <paramref name="text"/> near the top.</summary>
    private static Document PageSaying(string text)
    {
        var document = new Document();
        var page = document.Pages.Add();
        page.Paragraphs.Add(new TextFragment(text) { TextState = { FontSize = 36 } });
        using var saved = new MemoryStream();
        document.Save(saved);
        return new Document(new MemoryStream(saved.ToArray()));
    }

    private static string FreshFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "comparer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static GraphicalPdfComparer Comparer() => new() { Resolution = new Aspose.Pdf.Devices.Resolution(LowDpi) };

    [Fact]
    public void GetDifference_SeesNoChange_BetweenAPageAndItself()
    {
        var document = PageSaying("Same");

        using var difference = Comparer().GetDifference(document.Pages[1], document.Pages[1]);

        Assert.Equal(difference.Width * difference.Height, difference.Difference.Length);
        Assert.All(difference.Difference, entry => Assert.Equal(ImagesDifference.Same, entry));
        Assert.Equal(ImagesDifference.StrideFor(difference.Width), difference.Stride);
        Assert.Equal(0, difference.Stride % ImagesDifference.RowAlignment);
    }

    [Fact]
    public void GetDifference_MarksThePixels_WhereTheTextDiffers()
    {
        var first = PageSaying("Alpha");
        var second = PageSaying("Omega");

        using var difference = Comparer().GetDifference(first.Pages[1], second.Pages[1]);

        var changed = 0;
        foreach (var entry in difference.Difference)
            if (entry != ImagesDifference.Same) changed++;
        Assert.True(changed > 0, "two different words must differ somewhere");
        Assert.True(changed < difference.Difference.Length / 2, "most of two nearly blank pages agrees");
    }

    [Fact]
    public void ComparePagesToImage_WritesAPng_WithoutThePlatformBitmap()
    {
        var first = PageSaying("Alpha");
        var second = PageSaying("Omega");
        var path = Path.Combine(FreshFolder(), "diff.png");

        Comparer().ComparePagesToImage(first.Pages[1], second.Pages[1], path);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, bytes[..4]);
    }

    [Fact]
    public void CompareDocumentsToImages_WritesOneJpegPerPage()
    {
        var first = PageSaying("Alpha");
        var second = PageSaying("Omega");
        var folder = FreshFolder();

        Comparer().CompareDocumentsToImages(first, second, folder, "page", System.Drawing.Imaging.ImageFormat.Jpeg);

        var bytes = File.ReadAllBytes(Path.Combine(folder, "page1.jpg"));
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, bytes[..2]);
    }

    [Fact]
    public void CompareDocumentsToPdf_WritesAReportPagePerComparedPage()
    {
        var first = PageSaying("Alpha");
        var second = PageSaying("Omega");
        var path = Path.Combine(FreshFolder(), "report.pdf");

        Comparer().CompareDocumentsToPdf(first, second, path);

        using var report = new Document(path);
        Assert.Equal(1, report.Pages.Count);
        Assert.Equal(1, report.Pages[1].Resources.Images.Count);
    }

    [Fact]
    public void Threshold_HidesSmallColourDifferences()
    {
        var first = PageSaying("Alpha");
        var second = PageSaying("Omega");
        var exact = Comparer();
        var lenient = Comparer();
        lenient.Threshold = 100;

        using var strict = exact.GetDifference(first.Pages[1], second.Pages[1]);
        using var loose = lenient.GetDifference(first.Pages[1], second.Pages[1]);

        Assert.Contains(strict.Difference, entry => entry != ImagesDifference.Same);
        Assert.All(loose.Difference, entry => Assert.Equal(ImagesDifference.Same, entry));
    }

    [Fact]
    public void ComposedDestination_RebuildsTheSecondPage()
    {
        var first = PageSaying("Alpha");
        var second = PageSaying("Omega");
        var comparer = Comparer();

        using var difference = comparer.GetDifference(first.Pages[1], second.Pages[1]);
        using var reverse = comparer.GetDifference(second.Pages[1], first.Pages[1]);

        var rebuilt = difference.Compose(ImagesDifference.ModeDestination, Color.Black, Color.Black);
        Assert.Equal(reverse.SourceRgb, rebuilt);
    }
}
