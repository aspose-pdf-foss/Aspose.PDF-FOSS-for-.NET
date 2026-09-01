namespace Aspose.Pdf.GroupProcessor;

/// <summary>
/// The ASCII spelling of every element the byte-level scanner matches, held as
/// byte arrays so a raw document buffer can be searched without decoding it.
/// Each token is paired with its length, which the scanner needs on the hot path
/// and which saves an array dereference per comparison.
///
/// Keywords (obj, stream, the header) are spelled exactly as they appear in the
/// file; dictionary keys keep the leading solidus of the PDF name.
/// </summary>
internal static class PdfDocElementsInBytes
{
    public static readonly byte[] Obj = Token("obj");
    public static readonly int ObjLength = Obj.Length;

    public static readonly byte[] Endobj = Token("endobj");
    public static readonly int EndobjLength = Endobj.Length;

    public static readonly byte[] Trailer = Token("trailer");
    public static readonly int TrailerLength = Trailer.Length;

    public static readonly byte[] Root = Token("/Root");
    public static readonly int RootLength = Root.Length;

    public static readonly byte[] Kids = Token("/Kids");
    public static readonly int KidsLength = Kids.Length;

    public static readonly byte[] Pages = Token("/Pages");
    public static readonly int PagesLength = Pages.Length;

    public static readonly byte[] Contents = Token("/Contents");
    public static readonly int ContentsLength = Contents.Length;

    public static readonly byte[] Stream = Token("stream");
    public static readonly int StreamLength = Stream.Length;

    public static readonly byte[] EndStream = Token("endstream");
    public static readonly int EndStreamLength = EndStream.Length;

    public static readonly byte[] Length = Token("/Length");
    public static readonly int LengthLength = Length.Length;

    public static readonly byte[] Filter = Token("/Filter");
    public static readonly int FilterLength = Filter.Length;

    public static readonly byte[] DecodeParms = Token("/DecodeParms");
    public static readonly int DecodeParmsLength = DecodeParms.Length;

    public static readonly byte[] Resources = Token("/Resources");
    public static readonly int ResourcesLength = Resources.Length;

    public static readonly byte[] Font = Token("/Font");
    public static readonly int FontLength = Font.Length;

    public static readonly byte[] Encoding = Token("/Encoding");
    public static readonly int EncodingLength = Encoding.Length;

    public static readonly byte[] Differences = Token("/Differences");
    public static readonly int DifferencesLength = Differences.Length;

    public static readonly byte[] Catalog = Token("/Catalog");
    public static readonly int CatalogLength = Catalog.Length;

    public static readonly byte[] Version = Token("/Version");
    public static readonly int VersionLength = Version.Length;

    public static readonly byte[] PDFHeader = Token("%PDF-");
    public static readonly int PDFHeaderLength = PDFHeader.Length;

    public static readonly byte[] Info = Token("/Info");
    public static readonly int InfoLength = Info.Length;

    public static readonly byte[] Metadata = Token("/Metadata");
    public static readonly int MetadataLength = Metadata.Length;

    public static readonly byte[] ToUnicode = Token("/ToUnicode");
    public static readonly int ToUnicodeLength = ToUnicode.Length;

    public static readonly byte[] Subtype = Token("/Subtype");
    public static readonly int SubtypeLength = Subtype.Length;

    public static readonly byte[] Linearized = Token("/Linearized");
    public static readonly int LinearizedLength = Linearized.Length;

    public static readonly byte[] BaseEncoding = Token("/BaseEncoding");
    public static readonly int BaseEncodingLength = BaseEncoding.Length;

    public static readonly byte[] True = Token("true");
    public static readonly int TrueLength = True.Length;

    public static readonly byte[] False = Token("false");
    public static readonly int FalseLength = False.Length;

    public static readonly byte[] Null = Token("null");
    public static readonly int NullLength = Null.Length;

    /// <summary>The bytes that spell the given element.</summary>
    public static byte[] ToByteArray(PdfDocElements pdfType)
    {
        switch (pdfType)
        {
            case PdfDocElements.Obj: return Obj;
            case PdfDocElements.Endobj: return Endobj;
            case PdfDocElements.Trailer: return Trailer;
            case PdfDocElements.Root: return Root;
            case PdfDocElements.Kids: return Kids;
            case PdfDocElements.Pages: return Pages;
            case PdfDocElements.Contents: return Contents;
            case PdfDocElements.Stream: return Stream;
            case PdfDocElements.EndStream: return EndStream;
            case PdfDocElements.Length: return Length;
            case PdfDocElements.Filter: return Filter;
            case PdfDocElements.DecodeParms: return DecodeParms;
            case PdfDocElements.Resources: return Resources;
            case PdfDocElements.Font: return Font;
            case PdfDocElements.Encoding: return Encoding;
            case PdfDocElements.Differences: return Differences;
            case PdfDocElements.Catalog: return Catalog;
            case PdfDocElements.Version: return Version;
            case PdfDocElements.PDFHeader: return PDFHeader;
            case PdfDocElements.Info: return Info;
            case PdfDocElements.Metadata: return Metadata;
            case PdfDocElements.ToUnicode: return ToUnicode;
            case PdfDocElements.Subtype: return Subtype;
            case PdfDocElements.Linearized: return Linearized;
            default:
                throw new Exception("Unknown PDF document element: " + pdfType);
        }
    }

    private static byte[] Token(string text) => System.Text.Encoding.ASCII.GetBytes(text);
}
