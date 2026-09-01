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
    /// <summary>True while a conformance conversion is running. PDF/A requires every face
    /// to be embedded, which overrides a face whose own licence permits only preview and
    /// printing; an ordinary save still refuses such a face.</summary>
    internal bool EmbeddingLicenceOverridden { get; set; }

    /// <summary>
    /// Add an embedded file to the document.
    /// </summary>
    public void AddEmbeddedFile(string fileName, byte[]? fileData, string? description = null,
        string? mimeType = null, bool compress = true,
        DateTime? creationDate = null, DateTime? modDate = null)
    {
        var fsDict = new PdfDictionary();
        fsDict.Set("Type", new PdfName("Filespec"));
        // /F uses Latin1; /UF uses UTF-16BE with BOM for non-ASCII file names
        fsDict.Set("F", new PdfString(Encoding.Latin1.GetBytes(fileName)));
        fsDict.Set("UF", Forms.Field.EncodePdfTextString(fileName));
        if (description is not null)
            fsDict.Set("Desc", Forms.Field.EncodePdfTextString(description));

        // A null payload registers a reference-only file specification — an external
        // file reference (/F) with no embedded /EF stream, e.g. a path that does not
        // resolve to a local file. A non-null payload embeds the bytes as an /EF stream.
        if (fileData is not null)
        {
            var fileStreamDict = new PdfDictionary();
            fileStreamDict.Set("Type", new PdfName("EmbeddedFile"));
            if (mimeType is not null)
                fileStreamDict.Set("Subtype", new PdfName(mimeType));
            var paramsDict = new PdfDictionary();
            paramsDict.Set("Size", new PdfInteger(fileData.Length));
            // /Params CreationDate and ModDate (PDF §7.11.3) record the source file's
            // timestamps when known. A STREAM-backed attachment has none — the
            // reference stamps BOTH with the embed time (probed:
            // a plainly saved stream spec carries CreationDate = ModDate = now),
            // and a reloaded spec's Params.ModDate must read a real date.
            var embedNow = DateTime.Now;
            var cd = creationDate ?? embedNow;
            var md = modDate ?? embedNow;
            paramsDict.Set("CreationDate", new PdfString(Encoding.Latin1.GetBytes(FormatPdfDate(cd))));
            paramsDict.Set("ModDate", new PdfString(Encoding.Latin1.GetBytes(FormatPdfDate(md))));
            fileStreamDict.Set("Params", paramsDict);
            var fileStream = new PdfStream(fileStreamDict, fileData);
            // FileEncoding.None embeds the bytes uncompressed (no /Filter).
            if (!compress) fileStream.DoNotCompress = true;

            var efDict = new PdfDictionary();
            efDict.Set("F", fileStream);
            fsDict.Set("EF", efDict);
        }

        // Register as new object
        var fsObjNum = AllocateObjectNumber();
        AddNewObject(fsObjNum, fsDict);

        // Get or create /Names dict in catalog
        var namesDict = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        if (namesDict is null)
        {
            namesDict = new PdfDictionary();
            _reader.Catalog.Set("Names", namesDict);
        }

        // Get or create /EmbeddedFiles name tree
        var efTree = _reader.ResolveDict(namesDict.Get("EmbeddedFiles"));
        PdfArray numsArray;
        if (efTree is not null)
        {
            numsArray = _reader.Resolve(efTree.Get("Names")) as PdfArray ?? new PdfArray();
        }
        else
        {
            efTree = new PdfDictionary();
            namesDict.Set("EmbeddedFiles", efTree);
            numsArray = new PdfArray();
        }

        // PDF name trees require lexical ordering on the Names array (PDF 32000-1 §7.9.6).
        // Find the first existing key that compares > fileName and insert before it; if none,
        // append. Reading and 1-based indexing then match the alphabetical order callers
        // expect from /Names/EmbeddedFiles.
        var insertAt = numsArray.Count;
        for (var i = 0; i + 1 < numsArray.Count; i += 2)
        {
            if (_reader.Resolve(numsArray[i]) is not PdfString s) continue;
            if (string.CompareOrdinal(s.ToText(), fileName) <= 0) continue;
            insertAt = i;
            break;
        }
        numsArray.Insert(insertAt, new PdfString(Encoding.Latin1.GetBytes(fileName)));
        numsArray.Insert(insertAt + 1, new PdfIndirectRef(fsObjNum, 0));
        efTree.Set("Names", numsArray);
    }
}
