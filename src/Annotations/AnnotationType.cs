using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Annotation subtype as defined in PDF spec Table 169.
/// </summary>
public enum AnnotationType
{
    Unknown,
    Text,
    Link,
    FreeText,
    Line,
    Square,
    Circle,
    Polygon,
    PolyLine,
    Highlight,
    Underline,
    Squiggly,
    StrikeOut,
    Stamp,
    Caret,
    Ink,
    Popup,
    FileAttachment,
    Sound,
    Movie,
    Widget,
    Screen,
    PrinterMark,
    TrapNet,
    Watermark,
    ThreeD,
    /// <summary>alias for <see cref="ThreeD"/>.</summary>
    PDF3D = ThreeD,
    Redact,
    /// <summary>alias for <see cref="Redact"/>.</summary>
    Redaction = Redact,
    RichMedia,
    /// <summary>Pre-press bleed-mark annotation.</summary>
    BleedMark,
    /// <summary>Pre-press color-bar annotation.</summary>
    ColorBar,
    /// <summary>Pre-press page-information annotation.</summary>
    PageInformation,
    /// <summary>Pre-press registration-mark annotation.</summary>
    RegistrationMark,
    /// <summary>Pre-press trim-mark annotation.</summary>
    TrimMark,
}
