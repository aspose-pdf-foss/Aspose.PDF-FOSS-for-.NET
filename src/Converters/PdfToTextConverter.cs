using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts PDF pages to plain text.
/// </summary>
public sealed class PdfToTextConverter
{
    /// <summary>
    /// Extract text from a single page.
    /// </summary>
    public string SavePageAsText(Document doc, int pageNumber)
    {
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages.At(pageNumber));
        return absorber.Text;
    }

    /// <summary>
    /// Extract text from each page separately.
    /// </summary>
    public string[] SaveAllPagesAsText(Document doc)
    {
        var results = new string[doc.PageCount];
        for (var i = 1; i <= doc.PageCount; i++)
        {
            var absorber = new TextAbsorber();
            absorber.Visit(doc.Pages[i]);
            results[i - 1] = absorber.Text;
        }
        return results;
    }

    /// <summary>
    /// Extract text from a page range (1-based, inclusive).
    /// </summary>
    public string SavePageRangeAsText(Document doc, int fromPage, int toPage)
    {
        var absorber = new TextAbsorber();
        for (var i = fromPage; i <= toPage; i++)
        {
            absorber.Visit(doc.Pages.At(i));
        }
        return absorber.Text;
    }

    /// <summary>
    /// Extract text from the entire document.
    /// </summary>
    public string SaveAsText(Document doc)
    {
        var absorber = new TextAbsorber();
        absorber.Visit(doc);
        return absorber.Text;
    }
}
