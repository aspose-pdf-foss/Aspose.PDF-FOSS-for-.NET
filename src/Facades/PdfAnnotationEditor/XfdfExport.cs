using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfAnnotationEditor
{
    /// <summary>
    /// Export annotations to an XFDF stream.
    /// </summary>
    /// <param name="xmlOutputStream">Output stream.</param>
    /// <param name="start">Start page (1-based).</param>
    /// <param name="end">End page (1-based).</param>
    /// <param name="annotTypes">Annotation types to export.</param>
    public void ExportAnnotationsXfdf(Stream xmlOutputStream, int start, int end, AnnotationType[] annotTypes)
    {
        var doc = Document;
        var typeSet = new HashSet<AnnotationType>(annotTypes);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            // Entitize carriage returns (&#xD;) instead of the default Replace, which
            // rewrites a lone \r in text as \r\n. XML parsers leave character references
            // unchanged, so an annotation's /Contents survives the export/import round-trip
            // byte-for-byte (a bare \r stays a bare \r).
            NewLineHandling = NewLineHandling.Entitize,
        };

        using var writer = XmlWriter.Create(xmlOutputStream, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("xfdf", "http://ns.adobe.com/xfdf/");
        writer.WriteAttributeString("xml", "space", null, "preserve");

        writer.WriteStartElement("fields");
        writer.WriteEndElement(); // fields

        writer.WriteStartElement("annots");

        for (int pageIdx = Math.Max(1, start); pageIdx <= Math.Min(end, doc.PageCount); pageIdx++)
        {
            var page = doc.Pages.At(pageIdx);
            foreach (var annot in page.Annotations)
            {
                if (!typeSet.Contains(annot.AnnotationType)) continue;
                // Skip Popup annotations — they are written as children of their parent annotations
                if (annot.AnnotationType == AnnotationType.Popup) continue;
                WriteXfdfAnnotation(writer, annot, pageIdx - 1, doc.Reader); // XFDF uses 0-based pages
            }
        }

        writer.WriteEndElement(); // annots
        writer.WriteEndElement(); // xfdf
        writer.WriteEndDocument();
        writer.Flush();

        // Truncate any stale data if the stream was previously longer (e.g. FileMode.OpenOrCreate)
        if (xmlOutputStream.CanSeek && xmlOutputStream.CanWrite)
            xmlOutputStream.SetLength(xmlOutputStream.Position);
    }

    /// <summary>
    /// Writes a single annotation as an XFDF XML element.
    /// Maps PDF annotation dictionary entries back to XFDF attributes/elements
    /// per the XFDF specification (PDF 32000 §12.7.8). Inverse of ImportXfdfAnnotation.
    /// </summary>
    internal static void WriteXfdfAnnotation(XmlWriter writer, Annotation annot, int zeroBasedPage,
        IO.PdfReader? reader = null, bool writeContents = true, bool normalizeRichText = false)
    {
        var tag = AnnotationTypeToXfdfTag(annot.AnnotationType);
        if (tag == "unknown") return;

        writer.WriteStartElement(tag);

        // Page
        writer.WriteAttributeString("page", zeroBasedPage.ToString(CultureInfo.InvariantCulture));

        // Rect
        WriteXfdfCommonAttributes(writer, annot, reader);

        // Width / style / dashes (/BS — may be an indirect reference)
        WriteXfdfBorderAndMarkup(writer, annot, reader);

        // Intent (/IT). The polyline dimension intent uses the lowercase-hyphenated
        // XFDF form; all other intents (PolygonCloud, LineArrow, …) keep the raw name.
        WriteXfdfIntentAndLine(writer, annot, reader);

        // Sound annotation sampling parameters (/Sound R/B/C/E) — the audio bytes
        // follow as a <data> child element below.
        WriteXfdfReplyAndState(writer, annot, reader);

        // InkList (ink annotations)
        WriteXfdfGeometry(writer, annot, reader);

        // Default style (/DS — free text), before contents-richtext per XFDF order
        WriteXfdfTextAndStyle(writer, annot, reader, writeContents, normalizeRichText);

        // Popup child (may be an indirect reference)
        WriteXfdfPopupAndAppearance(writer, annot, reader, zeroBasedPage);

        // Appearance (/AP) as a base64 <appearance> child (stamp annotations):
        // the WHOLE appearance object tree — the /N form XObject with its
        // resources, fonts and nested streams — serialized so the importer can
        // rebuild an identical appearance in the destination document.
        WriteXfdfStampImage(writer, annot, reader);

        writer.WriteEndElement(); // tag
    }

    private static string FormatFlags(int flags)
    {
        var parts = new List<string>();
        if ((flags & 1) != 0) parts.Add("invisible");
        if ((flags & 2) != 0) parts.Add("hidden");
        if ((flags & 4) != 0) parts.Add("print");
        if ((flags & 8) != 0) parts.Add("nozoom");
        if ((flags & 16) != 0) parts.Add("norotate");
        if ((flags & 32) != 0) parts.Add("noview");
        if ((flags & 64) != 0) parts.Add("readonly");
        if ((flags & 128) != 0) parts.Add("locked");
        if ((flags & 256) != 0) parts.Add("togglenoview");
        if ((flags & 512) != 0) parts.Add("lockedcontents");
        return string.Join(",", parts);
    }

    private static string AnnotationTypeToXfdfTag(AnnotationType type) => type switch
    {
        AnnotationType.Text => "text",
        AnnotationType.Link => "link",
        AnnotationType.FreeText => "freetext",
        AnnotationType.Line => "line",
        AnnotationType.Square => "square",
        AnnotationType.Circle => "circle",
        AnnotationType.Polygon => "polygon",
        AnnotationType.PolyLine => "polyline",
        AnnotationType.Highlight => "highlight",
        AnnotationType.Underline => "underline",
        AnnotationType.Squiggly => "squiggly",
        AnnotationType.StrikeOut => "strikeout",
        AnnotationType.Stamp => "stamp",
        AnnotationType.Caret => "caret",
        AnnotationType.Ink => "ink",
        AnnotationType.Popup => "popup",
        AnnotationType.FileAttachment => "fileattachment",
        AnnotationType.Sound => "sound",
        AnnotationType.Movie => "movie",
        AnnotationType.Widget => "widget",
        AnnotationType.Screen => "screen",
        AnnotationType.PrinterMark => "printermark",
        AnnotationType.TrapNet => "trapnet",
        AnnotationType.Watermark => "watermark",
        AnnotationType.ThreeD => "3d",
        AnnotationType.Redact => "redact",
        AnnotationType.RichMedia => "richmedia",
        _ => "unknown",
    };

    private static string F(double v) => v.ToString("G", CultureInfo.InvariantCulture);

    private static string FormatPdfDate(System.DateTime dt)
        => "D:" + dt.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    /// <summary>Write an embedded-stream payload as an XFDF &lt;data&gt; child:
    /// printable content is emitted filtered/ascii (decoded text); binary content
    /// is emitted raw/hex (the original encoded bytes). The original /Length and
    /// /Filter are recorded as attributes.</summary>
    private static void WriteDataElement(XmlWriter writer, IO.PdfReader reader, PdfStream stream)
    {
        var raw = stream.RawData ?? Array.Empty<byte>();
        var decoded = reader.DecodeStream(stream) ?? raw;
        long length = stream.Dict.Get("Length") is PdfInteger li ? li.Value : raw.Length;
        var filterName = stream.Dict.GetName("Filter");
        bool isAscii = decoded.Length > 0
            && Array.TrueForAll(decoded, b => b == 9 || b == 10 || b == 13 || (b >= 32 && b <= 126));
        writer.WriteStartElement("data");
        writer.WriteAttributeString("mode", isAscii ? "filtered" : "raw");
        writer.WriteAttributeString("encoding", isAscii ? "ascii" : "hex");
        writer.WriteAttributeString("length", length.ToString(CultureInfo.InvariantCulture));
        if (filterName is not null) writer.WriteAttributeString("filter", filterName);
        // Raw/hex payloads carry a single leading space (XFDF convention);
        // filtered/ascii payloads are written verbatim.
        writer.WriteString(isAscii ? Encoding.ASCII.GetString(decoded) : " " + ToHex(raw));
        writer.WriteEndElement();
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    /// <summary>Export every annotation in the bound document to an XFDF stream.</summary>
    public void ExportAnnotationsToXfdf(Stream xmlOutputStream)
    {
        var doc = Document;
        var allTypes = (AnnotationType[])System.Enum.GetValues(typeof(AnnotationType));
        ExportAnnotationsXfdf(xmlOutputStream, start: 1, end: doc.PageCount, allTypes);
    }

    /// <summary>Export annotations filtered by Subtype name strings.</summary>
    public void ExportAnnotationsXfdf(Stream xmlOutputStream, int start, int end, string[] annotTypes)
    {
        var enumTypes = MapStringToAnnotationTypes(annotTypes);
        ExportAnnotationsXfdf(xmlOutputStream, start, end, enumTypes);
    }
}
