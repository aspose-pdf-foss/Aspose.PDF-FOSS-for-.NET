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
    /// <summary>True when this document was opened from a writable+seekable stream, i.e. a
    /// no-arg <see cref="Save()"/> will do an incremental (append-only) update. Only then do
    /// page edits need to register their new content/form streams as indirect objects (an
    /// append writes only registered new/dirty objects); a full save promotes inline streams,
    /// so a document being written to a fresh output keeps the compact inline layout.</summary>
    internal bool HasWritableSourceStream => _sourceStream is { CanWrite: true };

    /// <summary>Serialise a freshly converted SVG-&gt;PDF <see cref="Document"/> to
    /// bytes so it can be re-opened through the normal PDF constructor chain
    /// (keeps the converted document's reader/xref consistent).</summary>
    private static byte[] SvgConvertToPdfBytes(Document converted)
    {
        using var ms = new MemoryStream();
        converted.Save(ms);
        return ms.ToArray();
    }

    private static bool IsPdfaFormat(Aspose.Pdf.PdfFormat f) => f switch
    {
        Aspose.Pdf.PdfFormat.PDF_A_1A or Aspose.Pdf.PdfFormat.PDF_A_1B
        or Aspose.Pdf.PdfFormat.PDF_A_2A or Aspose.Pdf.PdfFormat.PDF_A_2B or Aspose.Pdf.PdfFormat.PDF_A_2U
        or Aspose.Pdf.PdfFormat.PDF_A_3A or Aspose.Pdf.PdfFormat.PDF_A_3B or Aspose.Pdf.PdfFormat.PDF_A_3U
        or Aspose.Pdf.PdfFormat.PDF_A_4 or Aspose.Pdf.PdfFormat.PDF_A_4E or Aspose.Pdf.PdfFormat.PDF_A_4F => true,
        _ => false,
    };

    /// <summary>
    /// Whether the document has an interactive form.
    /// </summary>
    public bool HasForm => _reader.Catalog.ContainsKey("AcroForm");

    /// <summary>
    /// Reads a value from the document's /Catalog by name. Returns the
    /// resolved object's text representation (or its raw stream data
    /// decoded as UTF-8 for streams), or null if no entry by that name
    /// exists. Useful for inspecting custom catalog entries that vendors
    /// stash next to the standard PDF spec keys.
    /// </summary>
    public object? GetCatalogValue(string key)
    {
        var raw = _reader.Catalog.Get(key);
        if (raw is null) return null;
        var resolved = _reader.Resolve(raw);
        return resolved switch
        {
            Aspose.Pdf.Core.PdfString s => s.ToText(),
            Aspose.Pdf.Core.PdfName n => n.Value,
            Aspose.Pdf.Core.PdfStream stream => System.Text.Encoding.UTF8.GetString(_reader.DecodeStream(stream)),
            _ => resolved?.ToString(),
        };
    }

    /// <summary>
    /// Whether the document has bookmarks.
    /// </summary>
    public bool HasOutlines => Outlines is not null && Outlines.Count > 0;

    /// <summary>
    /// Whether the document has XMP metadata.
    /// </summary>
    public bool HasMetadata => _reader.Catalog.ContainsKey("Metadata");

    /// <summary>
    /// Get or create XMP metadata for this document.
    /// If no metadata exists, a new empty XmpMetadata instance is created.
    /// </summary>
    public XmpMetadata GetOrCreateMetadata() => GetOrCreateXmpMetadata();

    /// <summary>Whether the document has page labels.</summary>
    public bool HasPageLabels => _reader.Catalog.ContainsKey("PageLabels");

    /// <summary>Whether the document is a PDF Portfolio (has a /Collection dictionary in the catalog).</summary>
    public bool HasCollection => _reader.Catalog.ContainsKey("Collection");

    /// <summary>Whether the document has named destinations.</summary>
    public bool HasDestinations =>
        _reader.Catalog.ContainsKey("Dests") || _reader.Catalog.ContainsKey("Names");

    /// <summary>Whether the document has optional content (layers).</summary>
    public bool HasLayers => _reader.Catalog.ContainsKey("OCProperties");

    /// <summary>
    /// Set the PDF version for the output header (e.g., "1.7", "2.0").
    /// This overrides the version read from the original document.
    /// </summary>
    public void SetVersion(string version)
    {
        _versionOverride = version;
    }

    /// <summary>Resolve the structure-element kids (/K) of a structure dictionary.</summary>
    private static System.Collections.Generic.List<PdfDictionary> ResolveStructKids(
        PdfDictionary structDict, PdfReader reader)
    {
        var result = new System.Collections.Generic.List<PdfDictionary>();
        var k = reader.Resolve(structDict.Get("K"));
        if (k is PdfArray arr)
        {
            foreach (var item in arr)
                if (reader.ResolveDict(item) is { } d && d.GetName("Type") is null or "StructElem")
                    result.Add(d);
        }
        else if (k is PdfDictionary single && single.GetName("Type") is null or "StructElem")
        {
            result.Add(single);
        }
        return result;
    }

    private static string PdfStringToHex(PdfString s)
    {
        var bytes = s.Value;
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.AppendFormat("{0:X2}", b);
        return sb.ToString();
    }

    /// <summary>Whether the document has a structure tree.</summary>
    public bool HasStructTree => _reader.Catalog.ContainsKey("StructTreeRoot");

    /// <summary>True when the text carries CJK ideographs / kana / fullwidth
    /// forms (the same ranges <see cref="MeasureEntry"/> treats as full-width).</summary>
    private static bool ContainsCjkText(string s)
    {
        foreach (var c in s)
            if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0x3000 && c <= 0x303F))
                return true;
        return false;
    }
}
