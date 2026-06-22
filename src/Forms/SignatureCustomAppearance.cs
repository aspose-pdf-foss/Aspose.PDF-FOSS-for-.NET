namespace Aspose.Pdf.Forms;

/// <summary>
/// Visual-layout knobs for a signature's appearance stream — font, size,
/// padding and which signer metadata strings are rendered inside the
/// widget annotation.
/// </summary>
public class SignatureCustomAppearance
{
    /// <summary>Background color of the appearance XObject.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Foreground (text) color of the appearance XObject.</summary>
    public Color? ForegroundColor { get; set; }

    /// <summary>When true, the appearance image is composited as the
    /// foreground layer rather than the background.</summary>
    public bool IsForegroundImage { get; set; }

    /// <summary>Rotation applied to the appearance XObject.</summary>
    public Rotation Rotation { get; set; } = Rotation.None;

    /// <summary>Font family used for text inside the appearance XObject.</summary>
    public string FontFamilyName { get; set; } = "Helvetica";

    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; } = 10;

    /// <summary>Show the contact-info line in the appearance.</summary>
    public bool ShowContactInfo { get; set; } = true;

    /// <summary>Show the signing reason in the appearance.</summary>
    public bool ShowReason { get; set; } = true;

    /// <summary>Show the signing location in the appearance.</summary>
    public bool ShowLocation { get; set; } = true;

    /// <summary>Label for the digital-signature line.</summary>
    public string DigitalSignedLabel { get; set; } = "Digitally signed by";

    /// <summary>Label for the date-signed-at line.</summary>
    public string DateSignedAtLabel { get; set; } = "Date signed at";

    /// <summary>Label for the contact-info line.</summary>
    public string ContactInfoLabel { get; set; } = "Contact";

    /// <summary>Label for the reason line.</summary>
    public string ReasonLabel { get; set; } = "Reason";

    /// <summary>Label for the location line.</summary>
    public string LocationLabel { get; set; } = "Location";

    /// <summary>Culture used when formatting the date string.</summary>
    public System.Globalization.CultureInfo? Culture { get; set; }

    /// <summary>Standard date-time format string.</summary>
    public string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Locale-aware date-time format string.</summary>
    public string DateTimeLocalFormat { get; set; } = "F";

    /// <summary>When true, the signer DN is decomposed into the elements
    /// listed in <see cref="DigitalSubjectFormat"/> instead of being
    /// rendered as a single CN line.</summary>
    public bool UseDigitalSubjectFormat { get; set; }

    /// <summary>Order in which subject-DN components appear in the
    /// appearance when <see cref="UseDigitalSubjectFormat"/> is true.</summary>
    public SubjectNameElements[] DigitalSubjectFormat { get; set; }
        = new[] { SubjectNameElements.CN };
}
