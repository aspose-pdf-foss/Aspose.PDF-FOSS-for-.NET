using System.IO;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

/// <summary>Blend-mode colour-space hint passed to a <see cref="Stamp"/>.
/// Stored only — the FOSS stamp emitter always operates in DeviceRGB.</summary>
public enum BlendingColorSpace
{
    DeviceRGB,
    DeviceCMYK,
    CalRGB,
    CalGray,
    /// <summary>Don't change the host page's blending colour space.</summary>
    DontChange,
    /// <summary>Pick a blending colour space automatically.</summary>
    Auto,
}

/// <summary>
/// Represents a stamp to be applied via PdfFileStamp facade.
/// This is distinct from Aspose.Pdf.Stamps.Stamp (the base class for page stamps).
/// </summary>
public sealed class Stamp
{
    /// <summary>
    /// The stamp identifier. Used for later retrieval/deletion via PdfContentEditor.
    /// Default is 0 (auto-assigned).
    /// </summary>
    public int StampId { get; set; }

    /// <summary>
    /// Pages to apply the stamp to (1-based). If null or empty, applies to all pages.
    /// </summary>
    public int[]? Pages { get; set; }

    /// <summary>
    /// Single page number to apply the stamp to (1-based).
    /// Setting this is equivalent to setting Pages = new[] { value }.
    /// </summary>
    public int PageNumber
    {
        get => Pages is { Length: > 0 } ? Pages[0] : 0;
        set => Pages = value > 0 ? new[] { value } : null;
    }

    /// <summary>X origin position in points.</summary>
    public double XOrigin { get; set; }

    /// <summary>Y origin position in points.</summary>
    public double YOrigin { get; set; }

    /// <summary>Rotation angle in degrees.</summary>
    public float Rotation { get; set; }

    /// <summary>Opacity (0.0-1.0).</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Whether this stamp is in the background.</summary>
    public bool IsBackground { get; set; }

    /// <summary>JPEG quality (1..100) when the stamp is emitted as a JPEG image.
    /// Stored only; the FOSS stamp emitter always uses the source bytes verbatim.</summary>
    public int Quality { get; set; } = 100;

    /// <summary>Blending colour space used when compositing the stamp onto
    /// the page. Stored only — FOSS treats every stamp as DeviceRGB.</summary>
    public BlendingColorSpace BlendingSpace { get; set; } = BlendingColorSpace.DeviceRGB;

    private FormattedText? _logoText;
    private byte[]? _logoImage;
    private byte[]? _pdfBytes;
    private int _pdfPageNumber;
    private TextState? _textState;
    private float _imageWidth;
    private float _imageHeight;

    /// <summary>
    /// Bind a FormattedText as the stamp's logo (text stamp).
    /// </summary>
    public void BindLogo(FormattedText formattedText)
    {
        _logoText = formattedText;
        _logoImage = null;
        _pdfBytes = null;
    }

    /// <summary>
    /// Bind raw image bytes as the stamp's logo (image stamp). FOSS-only
    /// extension — Aspose.Pdf only exposes <see cref="BindImage(Stream)"/>
    /// and <see cref="BindImage(string)"/>.
    /// </summary>
    public void BindImage(byte[] imageData)
    {
        _logoImage = imageData;
        _logoText = null;
        _pdfBytes = null;
    }

    /// <summary>Bind a stream as the stamp's image source.</summary>
    public void BindImage(Stream image)
    {
        if (image is null) throw new System.ArgumentNullException(nameof(image));
        using var ms = new MemoryStream();
        if (image.CanSeek) image.Position = 0;
        image.CopyTo(ms);
        BindImage(ms.ToArray());
    }

    /// <summary>Bind a file path as the stamp's image source.</summary>
    public void BindImage(string imageFile)
    {
        if (imageFile is null) throw new System.ArgumentNullException(nameof(imageFile));
        BindImage(File.ReadAllBytes(imageFile));
    }

    /// <summary>Bind a PDF page (as the stamp source) by stream + 1-based
    /// page number. Stored only — the FOSS stamp emitter doesn't currently
    /// flatten an arbitrary PDF page into a stamp XObject.</summary>
    public void BindPdf(Stream pdfStream, int pageNumber)
    {
        if (pdfStream is null) throw new System.ArgumentNullException(nameof(pdfStream));
        using var ms = new MemoryStream();
        if (pdfStream.CanSeek) pdfStream.Position = 0;
        pdfStream.CopyTo(ms);
        _pdfBytes = ms.ToArray();
        _pdfPageNumber = pageNumber;
        _logoImage = null;
        _logoText = null;
    }

    /// <summary>Bind a PDF page (as the stamp source) by file path + 1-based
    /// page number. See <see cref="BindPdf(Stream, int)"/>.</summary>
    public void BindPdf(string pdfFile, int pageNumber)
    {
        if (pdfFile is null) throw new System.ArgumentNullException(nameof(pdfFile));
        _pdfBytes = File.ReadAllBytes(pdfFile);
        _pdfPageNumber = pageNumber;
        _logoImage = null;
        _logoText = null;
    }

    /// <summary>Bind a <see cref="TextState"/> that overrides defaults when
    /// the stamp is text-typed. The text-stamp emitter honours its
    /// RenderingMode (Tr), StrokingColor (RG) and ForegroundColor (rg);
    /// font / size still come from the bound FormattedText.</summary>
    public void BindTextState(TextState textState) => _textState = textState;

    /// <summary>Set the explicit pixel size at which the image stamp is
    /// rendered. Stored only — the FOSS emitter scales by inferring the
    /// source image's native dimensions.</summary>
    public void SetImageSize(float width, float height)
    {
        _imageWidth = width;
        _imageHeight = height;
    }

    /// <summary>Set the origin position.</summary>
    public void SetOrigin(float originX, float originY)
    {
        XOrigin = originX;
        YOrigin = originY;
    }

    /// <summary>The TextState bound via <see cref="BindTextState"/> (rendering
    /// mode / stroking colour / foreground colour overrides for a text stamp),
    /// or null when none was bound.</summary>
    internal TextState? TextState => _textState;

    /// <summary>Whether this stamp is a text stamp.</summary>
    internal bool IsTextStamp => _logoText is not null;

    /// <summary>The text content (for text stamps).</summary>
    internal FormattedText? LogoText => _logoText;

    /// <summary>The image data (for image stamps).</summary>
    internal byte[]? LogoImage => _logoImage;

    /// <summary>The explicit pixel width set via <see cref="SetImageSize"/>.
    /// Zero means caller did not declare a width.</summary>
    internal float ImageWidth => _imageWidth;

    /// <summary>The explicit pixel height set via <see cref="SetImageSize"/>.
    /// Zero means caller did not declare a height.</summary>
    internal float ImageHeight => _imageHeight;

    /// <summary>The source-PDF bytes bound via <see cref="BindPdf(Stream, int)"/>,
    /// or null when the stamp is not a PDF-page stamp.</summary>
    internal byte[]? PdfBytes => _pdfBytes;

    /// <summary>The 1-based source page number bound via <see cref="BindPdf(Stream, int)"/>.</summary>
    internal int PdfPageNumber => _pdfPageNumber;
}
