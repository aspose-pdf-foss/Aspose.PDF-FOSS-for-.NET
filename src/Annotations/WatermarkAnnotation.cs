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

    /// <summary>When attached, told of a rotation set on the characteristics, so the annotation's appearance
    /// turns whenever it is set.</summary>
    internal System.Action? RotateChanged;

    private Rotation _rotate;

    /// <summary>Creates characteristics with a black border and a transparent background.</summary>
    public Characteristics() { }

    /// <summary>Rotation of the annotation appearance.</summary>
    public Rotation Rotate
    {
        get => _rotate;
        set { _rotate = value; RotateChanged?.Invoke(); }
    }

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
        FixedPrint.Changed = WriteFixedPrint;
    }

    /// <summary>Wrap a /Watermark annotation read from a document.</summary>
    internal WatermarkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader)
    {
        if (reader.ResolveDict(dict.Get("FixedPrint")) is { } fixedPrint)
        {
            var m = reader.Resolve(fixedPrint.Get("Matrix")) is PdfArray { Count: MatrixEntries } a
                ? new Matrix(Number(a[0]), Number(a[1]), Number(a[2]), Number(a[3]), Number(a[4]), Number(a[5]))
                : new Matrix(1, 0, 0, 1, 0, 0);
            FixedPrint.Load(m, Number(fixedPrint.Get("H")), Number(fixedPrint.Get("V")));
        }
        FixedPrint.Changed = WriteFixedPrint;
    }

    private const int MatrixEntries = 6;

    private static double Number(PdfObject? value) => value switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => 0,
    };

    /// <summary>Writes the fixed-print placement the caller set as the annotation's /FixedPrint dictionary
    /// (PDF 32000-1 §12.5.6.22), the matrix and translations a printer applies; setting them used to keep
    /// them on the object only.</summary>
    private void WriteFixedPrint()
    {
        var m = FixedPrint.Matrix;
        var matrix = new PdfArray();
        foreach (var v in new[] { m.A, m.B, m.C, m.D, m.E, m.F })
            matrix.Add(new PdfReal(v));
        var fixedPrint = new PdfDictionary();
        fixedPrint.Set("Type", new PdfName("FixedPrint"));
        fixedPrint.Set("Matrix", matrix);
        if (FixedPrint.HorizontalTranslation != 0) fixedPrint.Set("H", new PdfReal(FixedPrint.HorizontalTranslation));
        if (FixedPrint.VerticalTranslation != 0) fixedPrint.Set("V", new PdfReal(FixedPrint.VerticalTranslation));
        Dict.Set("FixedPrint", fixedPrint);
    }

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

        var (fonts, resName, glyphLines) = BuildTextFont(fontName);
        builder.BeginText();
        builder.SetFont(resName, fontSize);

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
            if (glyphLines is null) builder.ShowText(_texts[i]);
            else builder.ShowTextHex(glyphLines[i]);
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
        resDict.Set("Font", fonts);
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

    /// <summary>The font the lines are shown with. A line that reaches beyond Latin-1 cannot
    /// ride a WinAnsi standard face - its letters would come out as question marks - so when
    /// the text state's face travels with it, that face is embedded as a composite font with
    /// the glyphs of every line, and the lines are shown by glyph id (in visual order, for a
    /// right-to-left script). Otherwise the standard Type1 face named by the state is used.
    /// Returns the font resources, the resource name, and each line's glyph ids for an
    /// embedded face.</summary>
    private (PdfDictionary fonts, string resName, byte[][]? glyphLines) BuildTextFont(string fontName)
    {
        const string resourceName = "F1";
        var fonts = new PdfDictionary();
        var texts = _texts ?? [];
        var ttf = _textState?.Font?.SourceFontData?.TtfData;
        if (ttf is { Length: > 0 } && texts.Any(t => t.Any(ch => ch > 'ÿ')))
        {
            var glyphLines = new byte[texts.Length][];
            var resName = resourceName;
            for (var i = 0; i < texts.Length; i++)
                (resName, glyphLines[i]) = Type0FontEmbedder.Embed(
                    fonts, ttf, fontName, BidiText.ToVisualOrder(texts[i]), resNameHint: resourceName);
            return (fonts, resName, glyphLines);
        }
        var f1Dict = new PdfDictionary();
        f1Dict.Set("Type", new PdfName("Font"));
        f1Dict.Set("Subtype", new PdfName("Type1"));
        f1Dict.Set("BaseFont", new PdfName(MapToPdfFontName(fontName)));
        f1Dict.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fonts.Set(resourceName, f1Dict);
        return (fonts, resourceName, null);
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
