namespace Aspose.Pdf;

/// <summary>
/// Save format enumeration. Only <see cref="Pdf"/> and <see cref="Html"/> are implemented;
/// other values exist for source-level API compatibility but throw
/// <see cref="System.NotSupportedException"/> at save time.
/// </summary>
public enum SaveFormat
{
    /// <summary>Plain PDF.</summary>
    Pdf = 0,
    /// <summary>HTML.</summary>
    Html = 1,
    /// <summary>XPS (not implemented).</summary>
    Xps = 2,
    /// <summary>DOC (not implemented).</summary>
    Doc = 3,
    /// <summary>TIFF (not implemented).</summary>
    Tiff = 4,
    /// <summary>EPUB (not implemented).</summary>
    Epub = 5,
    /// <summary>SVG (not implemented).</summary>
    Svg = 7,
    /// <summary>MobiXml (not implemented).</summary>
    MobiXml = 8,
    /// <summary>DOCX (not implemented).</summary>
    DocX = 9,
    /// <summary>PPTX (not implemented).</summary>
    Pptx = 10,
    /// <summary>Excel (not implemented).</summary>
    Excel = 11,
    /// <summary>Markdown (not implemented).</summary>
    Markdown = 12,
    /// <summary>APS (not implemented).</summary>
    Aps = 13,
    /// <summary>No save format selected.</summary>
    None = 14,
    /// <summary>Encapsulated PostScript (not implemented).</summary>
    Eps = 15,
    /// <summary>PDF/XML hybrid (not implemented).</summary>
    PdfXml = 16,
    /// <summary>PostScript (not implemented).</summary>
    Ps = 17,
    /// <summary>TeX (not implemented).</summary>
    TeX = 18,
    /// <summary>XML (not implemented).</summary>
    Xml = 19,
}
