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
    private System.Drawing.Color _border = System.Drawing.Color.Black;
    private System.Drawing.Color _background = System.Drawing.Color.Transparent;

    /// <summary>When the characteristics are attached to an annotation, setting a
    /// colour writes through to the annotation's /MK dictionary ("BC"/"BG" key
    /// passed as the first argument). Detached instances (WatermarkAnnotation)
    /// keep plain property semantics.</summary>
    internal System.Action<string, System.Drawing.Color>? WriteThrough;

    /// <summary>Rotation of the annotation appearance.</summary>
    public Rotation Rotate { get; set; }

    /// <summary>Border color used for the annotation's appearance.</summary>
    public System.Drawing.Color Border
    {
        get => _border;
        set { _border = value; WriteThrough?.Invoke("BC", value); }
    }

    /// <summary>Background color used for the annotation's appearance.</summary>
    public System.Drawing.Color Background
    {
        get => _background;
        set { _background = value; WriteThrough?.Invoke("BG", value); }
    }
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
        RefreshAppearance();
    }

    /// <summary>Set the watermark text from a <see cref="Aspose.Pdf.Facades.FormattedText"/>. Stored only.</summary>
    public void SetText(Aspose.Pdf.Facades.FormattedText text)
    {
        if (text is null) return;
        _texts = new[] { text.ToString() ?? string.Empty };
    }

    /// <summary>Always <see cref="AnnotationType.Watermark"/>.</summary>
    public AnnotationType AnnotationType => AnnotationType.Watermark;

    /// <summary>Watermark opacity (0..1). Painted through an /ExtGState with
    /// matching fill/stroke alpha in the appearance stream.</summary>
    public double Opacity
    {
        get => _opacity;
        set { _opacity = value; RefreshAppearance(); }
    }
    private double _opacity = 1.0;

    // The dictionary handed to AnnotationCollection.Add — the public API allows
    // configuring the annotation AFTER adding it to the page (Add, then
    // SetTextAndState/Opacity), so mutators rebuild the appearance in place.
    private PdfDictionary? _builtDict;

    private void RefreshAppearance()
    {
        if (_builtDict is null || _texts is null || _texts.Length == 0) return;
        var apDict = new PdfDictionary();
        apDict.Set("N", BuildAppearanceStream());
        _builtDict.Set("AP", apDict);
    }

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

        _builtDict = dict;
        return dict;
    }

    private PdfStream BuildAppearanceStream()
    {
        var width = _rect.URX - _rect.LLX;
        var height = _rect.URY - _rect.LLY;
        var fontSize = _textState?.FontSize ?? 12;
        var fontName = _textState?.FontName ?? _textState?.Font?.BaseFont ?? "Helvetica";

        var builder = new ContentStreamBuilder();

        // A translucent watermark paints through an /ExtGState carrying the
        // fill/stroke alpha (PDF 32000-1 §11.6.4.2); the state is selected
        // before any painting so every line of text takes the opacity.
        if (_opacity < 1.0)
            builder.SetGraphicsState("GS0");

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

        // The text block sits on the bottom edge of the annotation rectangle:
        // the last line's baseline is one descent above the box floor so the
        // descenders stay inside the rectangle; earlier lines stack above it.
        var descent = Math.Abs(Standard14Fonts.GetWrittenFaceDescent(fontName)) * fontSize / 1000.0;
        if (descent <= 0) descent = fontSize * 0.2;
        var y = descent + (_texts!.Length - 1) * fontSize * 1.2;
        builder.MoveTextPosition(0, y);

        for (var i = 0; i < _texts.Length; i++)
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
        if (_opacity < 1.0)
        {
            var gsDict = new PdfDictionary();
            var gs0 = new PdfDictionary();
            gs0.Set("Type", new PdfName("ExtGState"));
            gs0.Set("ca", new PdfReal(_opacity));
            gs0.Set("CA", new PdfReal(_opacity));
            gsDict.Set("GS0", gs0);
            resDict.Set("ExtGState", gsDict);
        }
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
