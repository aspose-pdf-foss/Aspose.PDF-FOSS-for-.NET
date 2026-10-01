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
    /// <summary>The embedded file's own stream object, with its media type, its dates and the size the reader checks it against.</summary>
    private void WriteEmbeddedFileStream(PdfDictionary fsDict, byte[]? fileData, string? mimeType, DateTime? creationDate, DateTime? modDate, bool compress)
    {
        if (fileData is not null)
        {
            var fileStreamDict = new PdfDictionary();
            fileStreamDict.Set("Type", new PdfName("EmbeddedFile"));
            if (mimeType is not null)
                fileStreamDict.Set("Subtype", new PdfName(mimeType));
            var paramsDict = new PdfDictionary();
            paramsDict.Set("Size", new PdfInteger(fileData.Length));
            // The checksum is the MD5 of the bytes as embedded, written as the
            // raw 16 bytes (PDF 32000-1 table 46). The reference writes none, so
            // this is off unless a caller asks for it.
            if (ChecksumEmbeddedFiles)
                paramsDict.Set("CheckSum", new PdfString(Md5Digest.Hash(fileData, 0, fileData.Length)));
            // /Params CreationDate and ModDate (PDF §7.11.3) record the source file's
            // timestamps when known. A STREAM-backed attachment has none — the
            // reference stamps BOTH with the embed time (probed:
            // a plainly saved stream spec carries CreationDate = ModDate = now),
            // and a reloaded spec's Params.ModDate must read a real date.
            var embedNow = DateTime.Now;
            var cd = creationDate ?? embedNow;
            var md = modDate ?? embedNow;
            paramsDict.Set("CreationDate", new PdfString(Compat.Latin1.GetBytes(FormatPdfDate(cd))));
            paramsDict.Set("ModDate", new PdfString(Compat.Latin1.GetBytes(FormatPdfDate(md))));
            fileStreamDict.Set("Params", paramsDict);
            var fileStream = new PdfStream(fileStreamDict, fileData);
            // FileEncoding.None embeds the bytes uncompressed (no /Filter).
            if (!compress) fileStream.DoNotCompress = true;

            var efDict = new PdfDictionary();
            efDict.Set("F", fileStream);
            fsDict.Set("EF", efDict);
        }
    }
}
