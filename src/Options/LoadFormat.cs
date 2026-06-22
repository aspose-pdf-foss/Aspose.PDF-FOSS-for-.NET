namespace Aspose.Pdf;

/// <summary>Source format selector used by <see cref="LoadOptions"/>. HTML/XML/SVG
/// load paths are wired; all other values are accepted at the API surface but the
/// loader throws when asked to process them.</summary>
public enum LoadFormat
{
    /// <summary>HTML markup input.</summary>
    HTML = 0,
    /// <summary>XPS input (not implemented).</summary>
    XPS = 1,
    /// <summary>EPUB input (not implemented).</summary>
    EPUB = 2,
    /// <summary>PostScript input (not implemented).</summary>
    PS = 3,
    /// <summary>PCL input (not implemented).</summary>
    PCL = 4,
    /// <summary>SVG input.</summary>
    SVG = 5,
    /// <summary>MHT input (not implemented).</summary>
    MHT = 6,
    /// <summary>TeX input (not implemented).</summary>
    TEX = 7,
    /// <summary>DJVU input (not implemented).</summary>
    DJVU = 8,
    /// <summary>OFD input (not implemented).</summary>
    OFD = 9,
    /// <summary>CGM input (not implemented).</summary>
    CGM = 10,
    /// <summary>APS input (not implemented).</summary>
    APS = 11,
    /// <summary>XML input.</summary>
    XML = 12,
    /// <summary>Markdown input (not implemented).</summary>
    MD = 13,
    /// <summary>Plain-text input (not implemented).</summary>
    TXT = 14,
    /// <summary>CorelDraw input (not implemented).</summary>
    CDR = 15,
    /// <summary>PDFXML input (not implemented).</summary>
    PDFXML = 16,
    /// <summary>RTF input (not implemented).</summary>
    RTF = 17,
    /// <summary>XSL-FO input (not implemented).</summary>
    XSLFO = 18,
}
