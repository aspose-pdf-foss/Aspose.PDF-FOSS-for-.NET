using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

/// <summary>
/// Options for signing a PDF document.
/// </summary>
public sealed class SignatureOptions
{
    /// <summary>The reason for signing.</summary>
    public string? Reason { get; set; }

    /// <summary>The location of signing.</summary>
    public string? Location { get; set; }

    /// <summary>Contact information of the signer.</summary>
    public string? ContactInfo { get; set; }

    /// <summary>The signature field name. If null, a default name is generated.</summary>
    public string? FieldName { get; set; }

    /// <summary>
    /// Size in bytes reserved for the /Contents hex string.
    /// Defaults to 8192 which is sufficient for most certificates.
    /// Increase if using large certificate chains or timestamps.
    /// </summary>
    public int ContentsSize { get; set; } = 8192;

    /// <summary>When true, the signer skips an estimation pass and uses
    /// <see cref="ContentsSize"/> directly. The estimation pass is not
    /// currently performed in the FOSS signer (ContentsSize is always
    /// honoured), so this flag is accepted for surface parity and stored
    /// internally.</summary>
    public bool AvoidEstimating { get; set; }

    /// <summary>External-signer callback. When set, the signer skips
    /// in-process PKCS#7 construction and hands the to-be-signed hash
    /// to the implementation; the returned bytes are written into
    /// /Contents verbatim and must already be a complete CMS envelope.</summary>
    public Forms.SignHash? CustomSignHash { get; set; }
}

/// <summary>
/// Signs PDF documents with X.509 certificates using PKCS#7/CMS detached signatures.
/// Spec: PDF32000_2008 §12.8 (Digital Signatures)
/// </summary>
public sealed class PdfSigner
{
    /// <summary>
    /// Sign a PDF document and return the signed bytes.
    /// Uses incremental update to preserve original content.
    /// </summary>
    public static byte[] Sign(byte[] pdfData, PdfCertificate certificate,
        SignatureOptions? options = null)
    {
        options ??= new SignatureOptions();
        var fieldName = options.FieldName ?? "Signature1";
        var contentsSize = options.ContentsSize;

        // Step 1: Parse the original document to find AcroForm and allocate objects
        using var doc = Document.Open(pdfData);
        var reader = doc.Reader;
        var trailer = reader.Trailer;
        var xref = reader.XRefTable;

        // Determine next available object numbers
        var maxObj = 0;
        foreach (var entry in xref.Entries.Values)
            if (entry.ObjectNumber > maxObj) maxObj = entry.ObjectNumber;
        var nextObj = maxObj + 1;

        // Allocate object numbers for: sig value dict, sig field, updated AcroForm
        var sigValObjNum = nextObj++;
        var sigFieldObjNum = nextObj++;
        var acroFormObjNum = nextObj++;

        // Step 2: Build the incremental update with placeholder
        using var ms = new MemoryStream();

        // Copy original PDF
        ms.Write(pdfData);

        // Build signature value dictionary with placeholder
        var sigValDict = BuildSignatureValueDict(certificate, options, contentsSize);

        // Build signature field widget
        var sigFieldDict = BuildSignatureFieldDict(fieldName, sigValObjNum, doc);

        // Build or update AcroForm
        var acroFormDict = BuildAcroFormDict(doc, sigFieldObjNum);

        // Write the new objects
        var offsets = new Dictionary<int, long>();

        // Write sig value dict — we need to track the /Contents placeholder position
        offsets[sigValObjNum] = ms.Position;
        var sigValBytes = SerializeObject(sigValObjNum, sigValDict, contentsSize, out var contentsOffset, out var contentsLength);
        ms.Write(sigValBytes);

        // Adjust contentsOffset to be relative to the full file
        contentsOffset += offsets[sigValObjNum];

        // Write sig field
        offsets[sigFieldObjNum] = ms.Position;
        WriteIndirectObject(ms, sigFieldObjNum, sigFieldDict);

        // Write AcroForm
        offsets[acroFormObjNum] = ms.Position;
        WriteIndirectObject(ms, acroFormObjNum, acroFormDict);

        // Write updated catalog (add/update AcroForm reference)
        var catalogRef = trailer.Get("Root") as PdfIndirectRef;
        var catalogObjNum = catalogRef?.ObjectNumber ?? 1;
        var catalogDict = CloneDict(reader.Catalog);
        catalogDict.Set("AcroForm", new PdfIndirectRef(acroFormObjNum, 0));
        offsets[catalogObjNum] = ms.Position;
        WriteIndirectObject(ms, catalogObjNum, catalogDict);

        // Write xref + trailer
        var originalStartXref = XRefTable.FindStartXref(pdfData);
        WriteXRefAndTrailer(ms, offsets, trailer, nextObj, originalStartXref);

        var fileBytes = ms.ToArray();

        // Step 3: Compute ByteRange — the two ranges that exclude /Contents value
        // ByteRange = [0, contentsOffset, contentsOffset + contentsLength, fileLength - (contentsOffset + contentsLength)]
        var byteRange = new long[]
        {
            0,
            contentsOffset,
            contentsOffset + contentsLength,
            fileBytes.Length - (contentsOffset + contentsLength)
        };

        // Step 4: Patch the ByteRange in the signature value dict
        // Find and replace the placeholder ByteRange
        PatchByteRange(fileBytes, byteRange);

        // Step 5: Compute the hash over the two byte ranges
        // Hash the two byte ranges with SHA-256
        var hashInput = new byte[(int)byteRange[1] + (int)byteRange[3]];
        Array.Copy(fileBytes, 0, hashInput, 0, (int)byteRange[1]);
        Array.Copy(fileBytes, (int)byteRange[2], hashInput, (int)byteRange[1], (int)byteRange[3]);
        var hash = ShaDigest.Sha256(hashInput);

        // Step 6: Create PKCS#7/CMS detached signature
        var signatureBytes = options.CustomSignHash is not null
            ? options.CustomSignHash(hash, DigestHashAlgorithm.Sha256)
            : CreatePkcs7Signature(hash, certificate);

        if (signatureBytes.Length > contentsSize)
            throw new InvalidOperationException(
                $"Signature ({signatureBytes.Length} bytes) exceeds reserved space ({contentsSize} bytes). " +
                "Increase SignatureOptions.ContentsSize.");

        // Step 7: Write the signature into the /Contents placeholder
        var hexSignature = Convert.ToHexString(signatureBytes);
        // Pad with zeros to fill the reserved space
        hexSignature = hexSignature.PadRight(contentsSize * 2, '0');

        // Write hex string into the placeholder (skip the leading '<' at contentsOffset)
        var hexBytes = Encoding.ASCII.GetBytes(hexSignature);
        Array.Copy(hexBytes, 0, fileBytes, (int)contentsOffset + 1, hexBytes.Length);

        return fileBytes;
    }

    /// <summary>
    /// Verify a signature in a PDF document. Returns true if the signature is cryptographically valid.
    /// </summary>
    public static bool Verify(byte[] pdfData, string? fieldName = null)
    {
        using var doc = Document.Open(pdfData);
        var sigs = Forms.Signature.EnumerateSignatures(doc);

        var sig = fieldName is not null
            ? sigs.FirstOrDefault(s => s.FieldName == fieldName)
            : sigs.FirstOrDefault();

        if (sig is null || sig.ByteRangeRaw is null || sig.ContentsRaw is null)
            return false;

        var br = sig.ByteRangeRaw;
        if (br.Length != 4) return false;

        // Collect the signed byte ranges (the raw data the signature covers)
        var signedData = new byte[(int)br[1] + (int)br[3]];
        Array.Copy(pdfData, (int)br[0], signedData, 0, (int)br[1]);
        Array.Copy(pdfData, (int)br[2], signedData, (int)br[1], (int)br[3]);

        // Compute SHA-256 hash over the byte ranges
        var hash = ShaDigest.Sha256(signedData);

        // Try verification with raw signed bytes first (standard CMS detached),
        // then fall back to hash-as-content (used by some signers including this library's Sign).
        if (TryVerifyCms(signedData, sig.ContentsRaw))
            return true;
        if (TryVerifyCms(hash, sig.ContentsRaw))
            return true;
        return false;
    }

    private static bool TryVerifyCms(byte[] content, byte[] signature)
        => CmsBuilder.VerifyDetached(content, signature);

    /// <summary>
    /// Sign a PDF document with an X.509 certificate and a visible signature appearance.
    /// Returns the signed PDF bytes.
    /// </summary>
    public static byte[] SignWithAppearance(byte[] pdfData, PdfCertificate certificate,
        SignatureOptions? options, SignatureAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        options ??= new SignatureOptions();
        var fieldName = options.FieldName ?? "Signature1";
        var contentsSize = options.ContentsSize;

        using var doc = Document.Open(pdfData);
        var reader = doc.Reader;
        var trailer = reader.Trailer;
        var xref = reader.XRefTable;

        var maxObj = 0;
        foreach (var entry in xref.Entries.Values)
            if (entry.ObjectNumber > maxObj) maxObj = entry.ObjectNumber;
        var nextObj = maxObj + 1;

        var sigValObjNum = nextObj++;
        var sigFieldObjNum = nextObj++;
        var acroFormObjNum = nextObj++;
        var appearanceStreamObjNum = nextObj++;

        using var ms = new MemoryStream();
        ms.Write(pdfData);

        var sigValDict = BuildSignatureValueDict(certificate, options, contentsSize);
        var sigFieldDict = BuildSignatureFieldWithAppearance(
            fieldName, sigValObjNum, appearanceStreamObjNum, doc, appearance);
        var acroFormDict = BuildAcroFormDict(doc, sigFieldObjNum);
        var appearanceStreamDict = BuildSignatureAppearanceStream(appearance);

        // Write objects
        var offsets = new Dictionary<int, long>();

        offsets[sigValObjNum] = ms.Position;
        var sigValBytes = SerializeObject(sigValObjNum, sigValDict, contentsSize,
            out var contentsOffset, out var contentsLength);
        ms.Write(sigValBytes);
        contentsOffset += offsets[sigValObjNum];

        offsets[sigFieldObjNum] = ms.Position;
        WriteIndirectObject(ms, sigFieldObjNum, sigFieldDict);

        offsets[acroFormObjNum] = ms.Position;
        WriteIndirectObject(ms, acroFormObjNum, acroFormDict);

        // Write appearance stream object (stream with dict)
        offsets[appearanceStreamObjNum] = ms.Position;
        WriteStreamObject(ms, appearanceStreamObjNum, appearanceStreamDict);

        var catalogRef = trailer.Get("Root") as PdfIndirectRef;
        var catalogObjNum = catalogRef?.ObjectNumber ?? 1;
        var catalogDict = CloneDict(reader.Catalog);
        catalogDict.Set("AcroForm", new PdfIndirectRef(acroFormObjNum, 0));
        offsets[catalogObjNum] = ms.Position;
        WriteIndirectObject(ms, catalogObjNum, catalogDict);

        var originalStartXref = XRefTable.FindStartXref(pdfData);
        WriteXRefAndTrailer(ms, offsets, trailer, nextObj, originalStartXref);

        var fileBytes = ms.ToArray();

        var byteRange = new long[]
        {
            0,
            contentsOffset,
            contentsOffset + contentsLength,
            fileBytes.Length - (contentsOffset + contentsLength)
        };

        PatchByteRange(fileBytes, byteRange);

        // Hash the two byte ranges with SHA-256
        var hashInput = new byte[(int)byteRange[1] + (int)byteRange[3]];
        Array.Copy(fileBytes, 0, hashInput, 0, (int)byteRange[1]);
        Array.Copy(fileBytes, (int)byteRange[2], hashInput, (int)byteRange[1], (int)byteRange[3]);
        var hash = ShaDigest.Sha256(hashInput);

        var signatureBytes = options.CustomSignHash is not null
            ? options.CustomSignHash(hash, DigestHashAlgorithm.Sha256)
            : CreatePkcs7Signature(hash, certificate);

        if (signatureBytes.Length > contentsSize)
            throw new InvalidOperationException(
                $"Signature ({signatureBytes.Length} bytes) exceeds reserved space ({contentsSize} bytes).");

        var hexSignature = Convert.ToHexString(signatureBytes);
        hexSignature = hexSignature.PadRight(contentsSize * 2, '0');
        var hexBytes = Encoding.ASCII.GetBytes(hexSignature);
        Array.Copy(hexBytes, 0, fileBytes, (int)contentsOffset + 1, hexBytes.Length);

        return fileBytes;
    }

    #region Private helpers

    private static PdfDictionary BuildSignatureValueDict(PdfCertificate cert,
        SignatureOptions options, int contentsSize)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Sig"));
        dict.Set("Filter", new PdfName("Adobe.PPKLite"));
        dict.Set("SubFilter", new PdfName("adbe.pkcs7.detached"));

        // Name from certificate
        var name = cert.SubjectName;
        dict.Set("Name", EncodePdfText(name));

        if (options.Reason is not null)
            dict.Set("Reason", EncodePdfText(options.Reason));
        if (options.Location is not null)
            dict.Set("Location", EncodePdfText(options.Location));
        if (options.ContactInfo is not null)
            dict.Set("ContactInfo", EncodePdfText(options.ContactInfo));

        // Signing date
        var now = DateTime.UtcNow;
        var dateStr = $"D:{now:yyyyMMddHHmmss}+00'00'";
        dict.Set("M", new PdfString(Encoding.Latin1.GetBytes(dateStr)));

        // ByteRange placeholder — will be patched later
        // Use a distinctive pattern for easy replacement
        var byteRangeArray = new PdfArray();
        byteRangeArray.Add(new PdfInteger(0));
        byteRangeArray.Add(new PdfInteger(9999999999)); // placeholder
        byteRangeArray.Add(new PdfInteger(9999999999)); // placeholder
        byteRangeArray.Add(new PdfInteger(9999999999)); // placeholder
        dict.Set("ByteRange", byteRangeArray);

        // /Contents placeholder — hex string of zeros
        var contentsPlaceholder = new byte[contentsSize];
        dict.Set("Contents", new PdfString(contentsPlaceholder, isHex: true));

        return dict;
    }

    private static PdfDictionary BuildSignatureFieldDict(string fieldName,
        int sigValObjNum, Document doc)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Sig"));
        dict.Set("T", new PdfString(Encoding.Latin1.GetBytes(fieldName)));
        dict.Set("V", new PdfIndirectRef(sigValObjNum, 0));
        // Invisible signature (zero rect)
        var rect = new PdfArray();
        rect.Add(new PdfInteger(0));
        rect.Add(new PdfInteger(0));
        rect.Add(new PdfInteger(0));
        rect.Add(new PdfInteger(0));
        dict.Set("Rect", rect);
        // Set F flag (bit 2 = Hidden is NOT set, bit 3 = Print)
        dict.Set("F", new PdfInteger(4)); // Print flag

        // Reference to first page
        if (doc.PageCount > 0)
        {
            var firstPage = doc.Pages[1];
            // Find the page's object number from the reader
            var pagesDict = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("Pages"));
            if (pagesDict is not null)
            {
                var kids = doc.Reader.Resolve(pagesDict.Get("Kids")) as PdfArray;
                if (kids is { Count: > 0 } && kids[0] is PdfIndirectRef pageRef)
                {
                    dict.Set("P", pageRef);
                }
            }
        }

        return dict;
    }

    private static PdfDictionary BuildAcroFormDict(Document doc, int sigFieldObjNum)
    {
        var dict = new PdfDictionary();

        // Copy existing fields if AcroForm already exists
        var fields = new PdfArray();
        if (doc.Form is not null)
        {
            var existingAcroForm = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("AcroForm"));
            if (existingAcroForm is not null)
            {
                var existingFields = doc.Reader.Resolve(existingAcroForm.Get("Fields")) as PdfArray;
                if (existingFields is not null)
                {
                    foreach (var f in existingFields)
                        fields.Add(f);
                }
            }
        }

        // Add the new signature field
        fields.Add(new PdfIndirectRef(sigFieldObjNum, 0));
        dict.Set("Fields", fields);

        // SigFlags: 1 = SignaturesExist, 2 = AppendOnly → 3
        dict.Set("SigFlags", new PdfInteger(3));

        return dict;
    }

    private static byte[] CreatePkcs7Signature(byte[] hash, PdfCertificate certificate)
        => CmsBuilder.CreateDetachedSignature(hash, certificate);

    /// <summary>
    /// Serialize an indirect object, tracking the position and length of the /Contents hex string.
    /// </summary>
    private static byte[] SerializeObject(int objNum, PdfDictionary dict, int contentsSize,
        out long contentsOffset, out long contentsLength)
    {
        contentsOffset = 0;
        contentsLength = 0;

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write($"{objNum} 0 obj\n");
        Write("<< ");
        foreach (var key in dict.Keys)
        {
            Write($"/{key} ");
            var val = dict.Get(key)!;

            if (key == "Contents")
            {
                // Track the hex string position
                contentsOffset = ms.Position;
                var hexStr = new string('0', contentsSize * 2);
                Write($"<{hexStr}>");
                contentsLength = ms.Position - contentsOffset;
            }
            else
            {
                SerializeValue(ms, val);
            }
            Write(" ");
        }
        Write(">>\nendobj\n");

        return ms.ToArray();
    }

    private static void SerializeValue(MemoryStream ms, PdfObject val)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        switch (val)
        {
            case PdfNull:
                Write("null");
                break;
            case PdfBoolean b:
                Write(b.Value ? "true" : "false");
                break;
            case PdfInteger i:
                Write(i.Value.ToString());
                break;
            case PdfReal r:
                Write(r.Value.ToString("G"));
                break;
            case PdfString s when s.IsHex:
                Write($"<{Convert.ToHexString(s.Value)}>");
                break;
            case PdfString s:
                Write("(");
                foreach (var c in s.Value)
                {
                    if (c is (byte)'(' or (byte)')' or (byte)'\\')
                        ms.WriteByte((byte)'\\');
                    ms.WriteByte(c);
                }
                Write(")");
                break;
            case PdfName n:
                Write($"/{n.Value}");
                break;
            case PdfArray arr:
                Write("[");
                for (var i = 0; i < arr.Count; i++)
                {
                    if (i > 0) Write(" ");
                    SerializeValue(ms, arr[i]);
                }
                Write("]");
                break;
            case PdfDictionary d:
                Write("<< ");
                foreach (var key in d.Keys)
                {
                    Write($"/{key} ");
                    SerializeValue(ms, d.Get(key)!);
                    Write(" ");
                }
                Write(">>");
                break;
            case PdfIndirectRef iref:
                Write($"{iref.ObjectNumber} {iref.Generation} R");
                break;
        }
    }

    private static void WriteIndirectObject(MemoryStream ms, int objNum, PdfObject obj)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        Write($"{objNum} 0 obj\n");
        SerializeValue(ms, obj);
        Write("\nendobj\n");
    }

    private static void WriteXRefAndTrailer(MemoryStream ms, Dictionary<int, long> offsets,
        PdfDictionary originalTrailer, int nextObjNum, long originalStartXref)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        var xrefOffset = ms.Position;

        // Group consecutive object numbers into subsections
        var sortedNums = offsets.Keys.OrderBy(k => k).ToList();
        Write("xref\n");

        var i = 0;
        while (i < sortedNums.Count)
        {
            var start = sortedNums[i];
            var count = 1;
            while (i + count < sortedNums.Count && sortedNums[i + count] == start + count)
                count++;

            Write($"{start} {count}\n");
            for (var j = 0; j < count; j++)
            {
                Write($"{offsets[sortedNums[i + j]]:D10} 00000 n \n");
            }
            i += count;
        }

        // Trailer
        var newTrailer = new PdfDictionary();
        foreach (var key in new[] { "Root", "Info", "Encrypt", "ID" })
        {
            var val = originalTrailer.Get(key);
            if (val is not null) newTrailer.Set(key, val);
        }
        newTrailer.Set("Size", new PdfInteger(nextObjNum));
        newTrailer.Set("Prev", new PdfInteger(originalStartXref));

        Write("trailer\n");
        SerializeValue(ms, newTrailer);
        Write($"\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    private static void PatchByteRange(byte[] fileBytes, long[] byteRange)
    {
        // Find the ByteRange placeholder pattern: [0 9999999999 9999999999 9999999999]
        var placeholder = Encoding.ASCII.GetBytes("[0 9999999999 9999999999 9999999999]");
        var replacement = $"[{byteRange[0]} {byteRange[1]} {byteRange[2]} {byteRange[3]}]";
        // Pad replacement to same length as placeholder
        replacement = replacement.PadRight(placeholder.Length);
        var replacementBytes = Encoding.ASCII.GetBytes(replacement);

        var idx = FindBytes(fileBytes, placeholder);
        if (idx < 0)
            throw new InvalidOperationException("Could not find ByteRange placeholder in signed PDF.");

        Array.Copy(replacementBytes, 0, fileBytes, idx, replacementBytes.Length);
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    private static PdfDictionary CloneDict(PdfDictionary source)
    {
        var clone = new PdfDictionary();
        foreach (var key in source.Keys)
        {
            var val = source.Get(key);
            if (val is not null) clone.Set(key, val);
        }
        return clone;
    }

    private static PdfDictionary BuildSignatureFieldWithAppearance(
        string fieldName, int sigValObjNum, int appearanceStreamObjNum,
        Document doc, SignatureAppearance appearance)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Sig"));
        dict.Set("T", new PdfString(Encoding.Latin1.GetBytes(fieldName)));
        dict.Set("V", new PdfIndirectRef(sigValObjNum, 0));

        // Set rect from appearance
        var rect = new PdfArray();
        if (appearance.Rect is not null)
        {
            rect.Add(new PdfReal(appearance.Rect.LLX));
            rect.Add(new PdfReal(appearance.Rect.LLY));
            rect.Add(new PdfReal(appearance.Rect.URX));
            rect.Add(new PdfReal(appearance.Rect.URY));
        }
        else
        {
            rect.Add(new PdfInteger(0));
            rect.Add(new PdfInteger(0));
            rect.Add(new PdfInteger(0));
            rect.Add(new PdfInteger(0));
        }
        dict.Set("Rect", rect);
        dict.Set("F", new PdfInteger(4)); // Print flag

        // AP — appearance dictionary with /N (normal) pointing to form XObject
        var apDict = new PdfDictionary();
        apDict.Set("N", new PdfIndirectRef(appearanceStreamObjNum, 0));
        dict.Set("AP", apDict);

        // Page reference (0-based index from 1-based PageNumber)
        var pageIndex = Math.Max(0, appearance.PageNumber - 1);
        if (doc.PageCount > pageIndex)
        {
            var pagesDict = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("Pages"));
            if (pagesDict is not null)
            {
                var kids = doc.Reader.Resolve(pagesDict.Get("Kids")) as PdfArray;
                if (kids is not null && pageIndex < kids.Count && kids[pageIndex] is PdfIndirectRef pageRef)
                {
                    dict.Set("P", pageRef);
                }
            }
        }

        return dict;
    }

    /// <summary>
    /// Build a Form XObject appearance stream showing signature details.
    /// Returns a dictionary with stream content stored under the "__StreamData" key
    /// (used by WriteStreamObject).
    /// </summary>
    internal static PdfDictionary BuildSignatureAppearanceStream(SignatureAppearance appearance)
    {
        var r = appearance.Rect ?? new Rectangle(0, 0, 200, 100);
        var width = r.Width;
        var height = r.Height;
        var fontSize = appearance.FontSize;
        var lineHeight = fontSize * 1.4;

        // Build content stream text
        var sb = new StringBuilder();

        // Border rectangle
        sb.Append("q\n");
        sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
            "0 0 {0:F2} {1:F2} re S\n", width, height);

        // Text
        sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
            "BT\n/F1 {0:F1} Tf\n", fontSize);

        var y = height - lineHeight;
        var x = 4.0;

        if (appearance.SignerName is not null)
        {
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", x, y);
            sb.AppendFormat("({0}) Tj\n", EscapePdfString($"Digitally signed by: {appearance.SignerName}"));
            y -= lineHeight;
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", 0.0, -lineHeight);
        }

        if (appearance.Reason is not null)
        {
            if (appearance.SignerName is null)
            {
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F2} {1:F2} Td\n", x, y);
                y -= lineHeight;
            }
            sb.AppendFormat("({0}) Tj\n", EscapePdfString($"Reason: {appearance.Reason}"));
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", 0.0, -lineHeight);
        }

        if (appearance.Location is not null)
        {
            if (appearance.SignerName is null && appearance.Reason is null)
            {
                sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F2} {1:F2} Td\n", x, y);
                y -= lineHeight;
            }
            sb.AppendFormat("({0}) Tj\n", EscapePdfString($"Location: {appearance.Location}"));
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", 0.0, -lineHeight);
        }

        var signDate = appearance.SignDate ?? DateTime.UtcNow;
        var dateStr = signDate.ToString("yyyy-MM-dd HH:mm:ss");
        if (appearance.SignerName is null && appearance.Reason is null && appearance.Location is null)
        {
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F2} {1:F2} Td\n", x, y);
        }
        sb.AppendFormat("({0}) Tj\n", EscapePdfString($"Date: {dateStr}"));

        sb.Append("ET\n");
        sb.Append("Q\n");

        var streamContent = Encoding.Latin1.GetBytes(sb.ToString());

        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Form"));
        dict.Set("BBox", CreateBBoxArray(0, 0, width, height));
        dict.Set("Length", new PdfInteger(streamContent.Length));

        // Resources with a Helvetica font reference
        var fontDict = new PdfDictionary();
        var f1Dict = new PdfDictionary();
        f1Dict.Set("Type", new PdfName("Font"));
        f1Dict.Set("Subtype", new PdfName("Type1"));
        f1Dict.Set("BaseFont", new PdfName("Helvetica"));
        fontDict.Set("F1", f1Dict);
        var resources = new PdfDictionary();
        resources.Set("Font", fontDict);
        dict.Set("Resources", resources);

        // Store stream data for WriteStreamObject
        dict.Set("__StreamData", new PdfString(streamContent));

        return dict;
    }

    private static PdfArray CreateBBoxArray(double x1, double y1, double x2, double y2)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(x1));
        arr.Add(new PdfReal(y1));
        arr.Add(new PdfReal(x2));
        arr.Add(new PdfReal(y2));
        return arr;
    }

    private static string EscapePdfString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    /// <summary>
    /// Encode a text string as a PdfString, using UTF-16BE with BOM if it contains
    /// non-Latin-1 characters, otherwise Latin-1.
    /// </summary>
    private static PdfString EncodePdfText(string text)
    {
        // Check if all characters fit in Latin-1 (0x00-0xFF)
        var needsUnicode = false;
        foreach (var ch in text)
        {
            if (ch > 0xFF) { needsUnicode = true; break; }
        }

        if (!needsUnicode)
            return new PdfString(Encoding.Latin1.GetBytes(text));

        // UTF-16BE with BOM (0xFE 0xFF)
        var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var withBom = new byte[2 + utf16.Length];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        Array.Copy(utf16, 0, withBom, 2, utf16.Length);
        return new PdfString(withBom);
    }

    private static void WriteStreamObject(MemoryStream ms, int objNum, PdfDictionary dict)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        // Extract stream data
        var streamDataObj = dict.Get("__StreamData") as PdfString;
        var streamData = streamDataObj?.Value ?? [];

        Write($"{objNum} 0 obj\n<< ");
        foreach (var key in dict.Keys)
        {
            if (key == "__StreamData") continue;
            Write($"/{key} ");
            SerializeValue(ms, dict.Get(key)!);
            Write(" ");
        }
        Write(">>\nstream\n");
        ms.Write(streamData);
        Write("\nendstream\nendobj\n");
    }

    #endregion
}
