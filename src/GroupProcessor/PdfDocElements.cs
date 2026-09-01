namespace Aspose.Pdf.GroupProcessor;

/// <summary>
/// The PDF file elements the byte-level scanner looks for while walking a
/// document's raw bytes: the object keywords, the trailer keys and the font
/// dictionary keys it has to recognise without parsing the document.
/// </summary>
internal enum PdfDocElements : byte
{
    Obj,
    Endobj,
    Trailer,
    Root,
    Kids,
    Pages,
    Contents,
    Stream,
    EndStream,
    Length,
    Filter,
    DecodeParms,
    Resources,
    Font,
    Encoding,
    Differences,
    Catalog,
    Version,
    PDFHeader,
    Info,
    Metadata,
    ToUnicode,
    Subtype,
    Linearized
}
