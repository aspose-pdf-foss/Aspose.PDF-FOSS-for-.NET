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
}
