using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private Aspose.Pdf.PdfFormat? DetectPdfFormatFromXmp()
    {
        try
        {
            // Prefer the in-memory Metadata dict (reflects Remove/Set after load) over raw stream.
            if (!HasMetadata) return null;
            var m = Metadata;
            var partRaw = m.Get("pdfaid:part");
            if (string.IsNullOrEmpty(partRaw)) return null;
            var part = partRaw.ToUpperInvariant();
            var conf = (m.Get("pdfaid:conformance") ?? "").ToUpperInvariant();
            return (part, conf) switch
            {
                ("1", "A") => Aspose.Pdf.PdfFormat.PDF_A_1A,
                ("1", "B") => Aspose.Pdf.PdfFormat.PDF_A_1B,
                ("2", "A") => Aspose.Pdf.PdfFormat.PDF_A_2A,
                ("2", "B") => Aspose.Pdf.PdfFormat.PDF_A_2B,
                ("2", "U") => Aspose.Pdf.PdfFormat.PDF_A_2U,
                ("3", "A") => Aspose.Pdf.PdfFormat.PDF_A_3A,
                ("3", "B") => Aspose.Pdf.PdfFormat.PDF_A_3B,
                ("3", "U") => Aspose.Pdf.PdfFormat.PDF_A_3U,
                ("4", _) => Aspose.Pdf.PdfFormat.PDF_A_4,
                _ => (Aspose.Pdf.PdfFormat?)null,
            };
        }
        catch { return null; }
    }

    private XmpMetadata GetOrCreateXmpMetadata()
    {
        if (_metadataChecked && _metadata is not null) return _metadata;
        _metadataChecked = true;
        var stream = _reader.ResolveStream(_reader.Catalog.Get("Metadata"));
        if (stream is not null)
            _metadata = new XmpMetadata(stream, _reader);
        _metadata ??= new XmpMetadata();
        // Standard XMP properties fall back to the document Info dictionary when
        // the packet is absent or omits them (per the XMP↔DocInfo mapping).
        _metadata.SetInfoFallback(ResolveInfoDerivedXmp);
        return _metadata;
    }

    /// <summary>Map a standard XMP key to its /Info-dictionary equivalent, or
    /// null when Info carries no such value. Used as the XMP value fallback so
    /// e.g. <c>Metadata["xmp:ModifyDate"]</c> resolves from /ModDate on documents
    /// that have no XMP packet.</summary>
    private string? ResolveInfoDerivedXmp(string key)
    {
        static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
        static string? XmpDate(DateTime dt)
            => dt == DateTime.MinValue ? null : dt.ToString("yyyy-MM-ddTHH:mm:ss");

        var info = Info;
        return key switch
        {
            "xmp:CreatorTool" => NonEmpty(info.Creator),
            "pdf:Producer" => NonEmpty(info.Producer),
            "dc:title" => NonEmpty(info.Title),
            "dc:creator" => NonEmpty(info.Author),
            "dc:description" => NonEmpty(info.Subject),
            "pdf:Keywords" => NonEmpty(info.Keywords),
            "xmp:CreateDate" => XmpDate(info.CreationDate),
            "xmp:ModifyDate" => XmpDate(info.ModDate),
            _ => null,
        };
    }
}
