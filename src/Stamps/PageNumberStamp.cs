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
        // A stamp added straight to a page (rather than through ApplyToAll) carries no
        // page number of its own: it numbers the page it lands on, counted from
        // StartingNumber.
        if (CurrentPage <= 0) CurrentPage = StartingNumber + page.Number - 1;
        if (TotalPages <= 0) TotalPages = page.Number;
        var text = FormatNumber();
        var size = EffectiveFontSize;
        // The stamp's own TextState names the face it must be drawn in (with its
        // bold/italic flags), so the page-registered Helvetica the caller passed in
        // only applies when the stamp asked for nothing else.
        var face = EffectiveFontName;
        var resName = face is null ? fontResourceName
            : Aspose.Pdf.Table.RegisterFont(page, face);
        var (x, y) = ComputePosition(page, text, size, face);

        var color = TextState?.ForegroundColor is { } fg && !fg.IsEmpty
            ? (fg.R / 255.0, fg.G / 255.0, fg.B / 255.0)
            : Color;

        var builder = new ContentStreamBuilder();
        builder.SaveState()
            .BeginText()
            .SetFillColor(color.Item1, color.Item2, color.Item3)
            .SetFont(resName, size)
            .MoveTextPosition(x, y)
            .ShowText(text)
            .EndText()
            .RestoreState();

        return builder.Build();
    }

    /// <summary>The stamp's rendered text. <c>#</c> is the page-number placeholder
    /// ("Page # of 3"); the positional <c>{0}</c>/<c>{1}</c> form is also honoured.</summary>
    private string FormatNumber()
    {
        var fmt = Format ?? string.Empty;
        var text = fmt.Contains('{')
            ? string.Format(fmt, CurrentPage, TotalPages)
            : fmt;
        return text.Replace("#", CurrentPage.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Size from the stamp's TextState when it was set, else <see cref="FontSize"/>.</summary>
    private double EffectiveFontSize =>
        TextState is { FontSize: > 0 } ts ? ts.FontSize : FontSize;

    /// <summary>The Standard-14 base font the stamp's TextState asks for, with its
    /// bold/italic flags folded into the styled name; null when the stamp named no
    /// face of its own (the caller's page font stands).</summary>
    private string? EffectiveFontName
    {
        get
        {
            var name = TextState?.Font?.FontName ?? TextState?.FontName ?? FontName;
            var bold = TextState?.IsBold ?? false;
            var italic = TextState?.IsItalic ?? false;
            if (string.IsNullOrEmpty(name)) return bold || italic
                ? FontRepository.StandardStyledName("Helvetica", bold, italic) : null;
            return FontRepository.StandardStyledName(name, bold, italic);
        }
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

    private (double x, double y) ComputePosition(Page page, string text, double size, string? face)
    {
        var pageWidth = page.Width;
        var pageHeight = page.Height;
        // Centre on the real advance of the face the stamp draws in; the old
        // half-em-per-character estimate drifted the further the text ran.
        var textWidth = MeasureText(text, size, face);

        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => LeftMargin + (pageWidth - LeftMargin - RightMargin - textWidth) / 2,
            HorizontalAlignment.Right => pageWidth - RightMargin - textWidth - XIndent,
            _ => LeftMargin + XIndent,
        };

        // The stamp's own margins position it inside the page edge: a footer number
        // with BottomMargin=10 sits 10pt above the bottom, a header one TopMargin
        // below the top.
        var y = VerticalAlignment switch
        {
            VerticalAlignment.Top => pageHeight - TopMargin - size - YIndent,
            VerticalAlignment.Center => (pageHeight - size) / 2,
            _ => BottomMargin + YIndent,
        };

        return (x, y);
    }

    private static double MeasureText(string text, double size, string? face)
    {
        var name = string.IsNullOrEmpty(face) ? "Helvetica" : face;
        double w = 0;
        foreach (var ch in text)
        {
            var cw = Standard14Fonts.GetWidth(name, ch);
            if (cw <= 0) cw = Standard14Fonts.GetDefaultWidth(name);
            w += cw;
        }
        return w * size / 1000.0;
    }
}
