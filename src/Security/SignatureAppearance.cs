namespace Aspose.Pdf.Security;

/// <summary>
/// Defines the visual appearance for a digital signature annotation.
/// When provided to <see cref="PdfSigner.SignWithAppearance"/>, a visible
/// stamp is placed on the specified page showing signer information.
/// </summary>
public sealed class SignatureAppearance
{
    /// <summary>Display name of the signer.</summary>
    public string? SignerName { get; set; }

    /// <summary>The reason for signing.</summary>
    public string? Reason { get; set; }

    /// <summary>The location of signing.</summary>
    public string? Location { get; set; }

    /// <summary>Contact information for the signer.</summary>
    public string? ContactInfo { get; set; }

    /// <summary>The date of signing. If null, the current UTC time is used.</summary>
    public DateTime? SignDate { get; set; }

    /// <summary>The rectangle on the page where the signature appearance is shown.</summary>
    public Rectangle? Rect { get; set; }

    /// <summary>The 1-based page number on which to place the signature. Defaults to 1.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>Font size for the text in the appearance stream. Defaults to 10.</summary>
    public double FontSize { get; set; } = 10;

    /// <summary>Font family for the appearance text (from a
    /// <c>SignatureCustomAppearance</c>). Null keeps the default Helvetica.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Optional image bytes embedded into the appearance XObject
    /// as the signature graphic.</summary>
    public byte[]? ImageBytes { get; set; }

    /// <summary>The metadata labels a caller-shaped appearance keeps even when their
    /// value is empty; null for the plain banner, which drops empty rows.</summary>
    internal BannerLabels? Labels { get; set; }

    /// <summary>The banner text's colour; null keeps the banner blue.</summary>
    internal Color? TextColor { get; set; }

    /// <summary>The colour the box is filled with under the text; null leaves it unfilled.</summary>
    internal Color? BackgroundColor { get; set; }

    /// <summary>False draws the <see cref="ImageBytes"/> picture alone, with no banner text over it.</summary>
    internal bool ShowText { get; set; } = true;

    /// <summary>True draws the <see cref="ImageBytes"/> picture over the background fill, in the text
    /// layer, instead of as a layer of its own under it.</summary>
    internal bool ForegroundImage { get; set; }
}
