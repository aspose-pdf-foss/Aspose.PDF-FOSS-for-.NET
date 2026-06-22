using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// A stamp that adds page numbers to PDF pages.
/// Use the Format string with {0} for page number and {1} for total pages.
/// </summary>
public sealed class PageNumberStamp : Aspose.Pdf.Stamps.Stamp
{
    /// <summary>Page number format. Default: "Page {0} of {1}".</summary>
    public string Format { get; set; } = "Page {0} of {1}";

    /// <summary>Starting page number. Default: 1.</summary>
    public int StartingNumber { get; set; } = 1;

    /// <summary>Font name.</summary>
    public string FontName { get; set; } = "Helvetica";

    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; } = 10;

    /// <summary>Text state for the page number; setting <c>TextState.FontSize</c>
    /// updates the stamp's <see cref="FontSize"/> on the next stamp emission.</summary>
    public TextState TextState { get; } = new();

    /// <summary>Text color as RGB (0-1 range).</summary>
    public (double R, double G, double B) Color { get; set; } = (0, 0, 0);

    /// <summary>Total number of pages (set before stamping).</summary>
    internal int TotalPages { get; set; }

    /// <summary>Current page number (set before stamping).</summary>
    internal int CurrentPage { get; set; }

    internal override byte[] BuildContentStream(Page page) => BuildContentStream(page, "F1");

    internal override byte[] BuildContentStream(Page page, string fontResourceName)
    {
        var text = string.Format(Format, CurrentPage, TotalPages);
        var (x, y) = ComputePosition(page, text);

        var builder = new ContentStreamBuilder();
        builder.SaveState()
            .BeginText()
            .SetFillColor(Color.R, Color.G, Color.B)
            .SetFont(fontResourceName, FontSize)
            .MoveTextPosition(x, y)
            .ShowText(text)
            .EndText()
            .RestoreState();

        return builder.Build();
    }

    /// <summary>
    /// Apply this stamp to all pages in the document.
    /// </summary>
    public void ApplyToAll(Document document)
    {
        TotalPages = document.PageCount;
        for (var i = 1; i <= document.PageCount; i++)
        {
            CurrentPage = StartingNumber + i - 1;
            document.Pages[i].AddStamp(this);
        }
    }

    private (double x, double y) ComputePosition(Page page, string text)
    {
        var pageWidth = page.Width;
        var pageHeight = page.Height;
        var textWidth = text.Length * FontSize * 0.5;

        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (pageWidth - textWidth) / 2,
            HorizontalAlignment.Right => pageWidth - textWidth - XIndent,
            _ => XIndent,
        };

        var y = VerticalAlignment switch
        {
            VerticalAlignment.Top => pageHeight - FontSize - YIndent,
            VerticalAlignment.Center => (pageHeight - FontSize) / 2,
            _ => YIndent,
        };

        return (x, y);
    }
}
