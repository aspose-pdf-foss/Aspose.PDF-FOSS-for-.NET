using Aspose.Pdf.Devices;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts PDF pages to SVG format.
/// </summary>
public sealed class PdfToSvgConverter
{
    private readonly SvgDevice _device = new();

    /// <summary>
    /// Convert a single page to SVG string.
    /// </summary>
    public string SavePageAsSvg(Document doc, int pageNumber)
    {
        return _device.Process(doc.Pages.At(pageNumber));
    }

    /// <summary>
    /// Convert each page to SVG separately.
    /// </summary>
    public string[] SaveAllPagesAsSvg(Document doc)
    {
        var results = new string[doc.PageCount];
        for (var i = 1; i <= doc.PageCount; i++)
            results[i - 1] = _device.Process(doc.Pages[i]);
        return results;
    }

    /// <summary>
    /// Save a page's SVG output to a file.
    /// </summary>
    public void SavePageToFile(Document doc, int pageNumber, string path)
    {
        var svg = SavePageAsSvg(doc, pageNumber);
        File.WriteAllText(path, svg);
    }

    /// <summary>
    /// Save each page as a separate SVG file.
    /// File names are generated as: basePath_1.svg, basePath_2.svg, etc.
    /// </summary>
    public void SaveAllPagesToFiles(Document doc, string directory, string prefix = "page")
    {
        Directory.CreateDirectory(directory);
        for (var i = 1; i <= doc.PageCount; i++)
        {
            var svg = _device.Process(doc.Pages[i]);
            var path = Path.Combine(directory, $"{prefix}_{i}.svg");
            File.WriteAllText(path, svg);
        }
    }
}
