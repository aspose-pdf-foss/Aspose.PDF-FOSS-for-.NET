using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Annotations;

// Rotation enum moved to Aspose.Pdf namespace (in Stubs/TypeStubs.cs) to match the public API

/// <summary>
/// Annotation characteristics (border, rotation, etc.).
/// </summary>
public sealed class Characteristics
{
    /// <summary>Rotation of the annotation appearance.</summary>
    public Rotation Rotate { get; set; }

    /// <summary>Border color used for the annotation's appearance.</summary>
    public System.Drawing.Color Border { get; set; } = System.Drawing.Color.Black;

    /// <summary>Background color used for the annotation's appearance.</summary>
    public System.Drawing.Color Background { get; set; } = System.Drawing.Color.Transparent;
}

/// <summary>
/// Represents a watermark annotation that can be added to a PDF page.
/// API-compatible with the public API WatermarkAnnotation(page, rect).
/// </summary>
public sealed partial class WatermarkAnnotation
{
    private readonly Page _page;
    private readonly Rectangle _rect;
    private string[]? _texts;
    private TextState? _textState;

    /// <summary>Annotation characteristics (rotation, etc.).</summary>
    public Characteristics Characteristics { get; } = new();

    public FixedPrint FixedPrint { get; } = new FixedPrint();

    /// <summary>
    /// Create a watermark annotation for the given page and rectangle.
    /// </summary>
    public WatermarkAnnotation(Page page, Rectangle rect)
    {
        _page = page;
        _rect = rect;
    }

    /// <summary>The annotation's rectangle. Reflects any translation applied via
    /// <see cref="ChangeAfterResize(Matrix)"/>; otherwise the rectangle passed to
    /// the constructor.</summary>
    public Rectangle Rect => _rectOverride ?? _rect;

    /// <summary>Text note associated with the annotation (written to /Contents).</summary>
    public string? Contents { get; set; }

    /// <summary>Annotation name (written to /NM).</summary>
    public string? Name { get; set; }

    /// <summary>Border styling written to the annotation's /BS entry.</summary>
    public Border? Border { get; set; }

    /// <summary>
    /// Set the text content and text state for the watermark.
    /// </summary>
    public void SetTextAndState(string[] text, TextState textState)
    {
        _texts = text;
        _textState = textState;
    }

    /// <summary>Set the watermark text from a <see cref="Aspose.Pdf.Facades.FormattedText"/>. Stored only.</summary>
    public void SetText(Aspose.Pdf.Facades.FormattedText text)
    {
        if (text is null) return;
        _texts = new[] { text.ToString() ?? string.Empty };
    }

    /// <summary>Always <see cref="AnnotationType.Watermark"/>.</summary>
    public AnnotationType AnnotationType => AnnotationType.Watermark;

    /// <summary>Watermark opacity (0..1). Stored only — the appearance stream uses 1.0.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Translate the watermark's rectangle through <paramref name="transform"/>.</summary>
    public void ChangeAfterResize(Matrix transform)
    {
        if (transform is null) return;
        transform.Transform(_rect.LLX, _rect.LLY, out var x1, out var y1);
        transform.Transform(_rect.URX, _rect.URY, out var x2, out var y2);
        _rectOverride = new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2),
                                       Math.Max(x1, x2), Math.Max(y1, y2));
    }

    private Rectangle? _rectOverride;

    /// <summary>
    /// Build the annotation dictionary with appearance stream.
    /// </summary>
    internal PdfDictionary Build()
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Watermark"));
        var rectArr = new PdfArray();
        rectArr.Add(new PdfReal(_rect.LLX)); rectArr.Add(new PdfReal(_rect.LLY));
        rectArr.Add(new PdfReal(_rect.URX)); rectArr.Add(new PdfReal(_rect.URY));
        dict.Set("Rect", rectArr);
        dict.Set("F", new PdfInteger(4)); // Print flag

        if (!string.IsNullOrEmpty(Contents))
            dict.Set("Contents", new PdfString(System.Text.Encoding.Latin1.GetBytes(Contents!)));
        if (!string.IsNullOrEmpty(Name))
            dict.Set("NM", new PdfString(System.Text.Encoding.Latin1.GetBytes(Name!)));
        if (Border is { Width: > 0 })
        {
            var bs = new PdfDictionary();
            bs.Set("W", new PdfReal(Border.Width));
            dict.Set("BS", bs);
        }

        // Build appearance stream
        if (_texts is not null && _texts.Length > 0)
        {
            var apDict = new PdfDictionary();
            var formDict = BuildAppearanceStream();
            apDict.Set("N", formDict);
            dict.Set("AP", apDict);
        }

        return dict;
    }

    private PdfStream BuildAppearanceStream()
    {
        var width = _rect.URX - _rect.LLX;
        var height = _rect.URY - _rect.LLY;
        var fontSize = _textState?.FontSize ?? 12;
        var fontName = _textState?.FontName ?? _textState?.Font?.BaseFont ?? "Helvetica";

        var builder = new ContentStreamBuilder();

        // Apply rotation if specified
        var rotation = (int)Characteristics.Rotate;
        if (rotation != 0)
        {
            var rad = rotation * Math.PI / 180;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var cx = width / 2;
            var cy = height / 2;
            builder.SetMatrix(cos, sin, -sin, cos,
                cx - cos * cx + sin * cy,
                cy - sin * cx - cos * cy);
        }

        // Set text color
        if (_textState?.ForegroundColor is { } fg)
            builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
        else
            builder.SetFillColor(0, 0, 0);

        builder.BeginText();
        builder.SetFont("F1", fontSize);

        var y = height - fontSize;
        builder.MoveTextPosition(2, y);

        for (var i = 0; i < _texts!.Length; i++)
        {
            if (i > 0)
                builder.MoveTextPosition(0, -fontSize * 1.2);
            builder.ShowText(_texts[i]);
        }

        builder.EndText();
        var streamBytes = builder.Build();

        // Build Form XObject as PdfStream
        var formDict = new PdfDictionary();
        formDict.Set("Type", new PdfName("XObject"));
        formDict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(width)); bbox.Add(new PdfReal(height));
        formDict.Set("BBox", bbox);

        // Resources with font
        var resDict = new PdfDictionary();
        var fontDict = new PdfDictionary();
        var f1Dict = new PdfDictionary();
        f1Dict.Set("Type", new PdfName("Font"));
        f1Dict.Set("Subtype", new PdfName("Type1"));
        var pdfFontName = MapToPdfFontName(fontName);
        f1Dict.Set("BaseFont", new PdfName(pdfFontName));
        f1Dict.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set("F1", f1Dict);
        resDict.Set("Font", fontDict);
        formDict.Set("Resources", resDict);

        return new PdfStream(formDict, streamBytes);
    }

    private static string MapToPdfFontName(string name)
    {
        // Map common font names to standard PDF Type1 fonts
        var lower = name.ToLowerInvariant();
        if (lower.Contains("arial") || lower.Contains("helvetica"))
            return "Helvetica";
        if (lower.Contains("times"))
            return "Times-Roman";
        if (lower.Contains("courier"))
            return "Courier";
        return "Helvetica";
    }
}
