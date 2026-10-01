using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
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
    /// passed as the first argument). A detached instance keeps plain property
    /// semantics.</summary>
    internal System.Action<string, System.Drawing.Color>? WriteThrough;

    /// <summary>Creates characteristics with a black border and a transparent background.</summary>
    public Characteristics() { }

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
/// Represents a watermark annotation that can be added to a PDF page: an annotation of
/// subtype /Watermark, whose appearance is painted from the text it is given.
/// </summary>
public partial class WatermarkAnnotation : Annotation
{
    private string[]? _texts;
    private TextState? _textState;

    public FixedPrint FixedPrint { get; } = new FixedPrint();

    /// <summary>
    /// Create a watermark annotation for the given page and rectangle.
    /// </summary>
    public WatermarkAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Watermark"));
        Dict.Set("F", new PdfInteger(4)); // Print flag
    }

    /// <summary>Wrap a /Watermark annotation read from a document.</summary>
    internal WatermarkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

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

    /// <summary>Watermark opacity (0..1). Painted through an /ExtGState with
    /// matching fill/stroke alpha in the appearance stream, which is what carries
    /// the translucency of a watermark rather than the annotation's own /CA.</summary>
    public new double Opacity
    {
        get => _opacity;
        set { _opacity = value; RefreshAppearance(); }
    }
    private double _opacity = 1.0;

    // A watermark may be configured AFTER it has joined a page (Add, then
    // SetTextAndState/Opacity), and the page holds this annotation's own
    // dictionary, so a mutator paints the appearance again in place.
    private void RefreshAppearance()
    {
        if (_texts is null || _texts.Length == 0) return;
        // The text IS the appearance, so it replaces the normal one - but only that one: any other
        // state the annotation has been given keeps its place in the /AP it already has.
        var apDict = InternalReader.ResolveDict(Dict.Get("AP")) ?? new PdfDictionary();
        apDict.Set("N", BuildAppearanceStream());
        Dict.Set("AP", apDict);
        InvalidateAppearanceCache();
    }

    /// <summary>
    /// The annotation's dictionary, with its appearance stream painted.
    /// </summary>
    internal PdfDictionary Build()
    {
        RefreshAppearance();
        return Dict;
    }

    private PdfStream BuildAppearanceStream()
    {
        var rect = Rect ?? new Rectangle(0, 0, 0, 0);
        var width = rect.URX - rect.LLX;
        var height = rect.URY - rect.LLY;
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
