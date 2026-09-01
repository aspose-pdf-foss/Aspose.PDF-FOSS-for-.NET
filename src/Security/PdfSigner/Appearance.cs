using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
    /// <summary>
    /// Sign a PDF document with an X.509 certificate and a visible signature appearance.
    /// Returns the signed PDF bytes.
    /// </summary>
    public static byte[] SignWithAppearance(byte[] pdfData, PdfCertificate certificate,
        SignatureOptions? options, SignatureAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        options ??= new SignatureOptions();
        var contentsSize = options.ContentsSize;

        // The banner's first line names the signer; without an explicit signer
        // name it shows the certificate's full subject DN.
        appearance.SignerName ??= certificate.SubjectDn;
        // The signature properties always ride on the visible banner.
        appearance.Reason ??= options.Reason;
        appearance.Location ??= options.Location;
        appearance.ContactInfo ??= options.ContactInfo;

        using var doc = OpenDoc(pdfData, options.Password);
        var reader = doc.Reader;
        var trailer = reader.Trailer;
        var xref = reader.XRefTable;

        var fieldName = ResolveSignatureFieldName(doc, options.FieldName);

        var maxObj = 0;
        foreach (var entry in xref.Entries.Values)
            if (entry.ObjectNumber > maxObj) maxObj = entry.ObjectNumber;
        var nextObj = maxObj + 1;

        var sigValObjNum = nextObj++;
        var sigFieldObjNum = nextObj++;
        var acroFormObjNum = nextObj++;
        var appearanceStreamObjNum = nextObj++;
        var dssCertObjNum = options.UseLtv ? nextObj++ : 0;

        using var ms = new MemoryStream();
        ms.Write(pdfData);

        var sigValDict = BuildSignatureValueDict(certificate, options, contentsSize);
        // The visible banner rides the same nested forms and embedded face as the
        // invisible one; only the box comes from the caller's rectangle here.
        var visRect = appearance.Rect;
        var visBanner = BuildBannerObjects(
            visRect is null ? 0 : Math.Abs(visRect.URX - visRect.LLX),
            visRect is null ? 0 : Math.Abs(visRect.URY - visRect.LLY),
            options, certificate, ref nextObj,
            appearance.FontFamily, appearance.FontSize, appearance);
        var sigFieldDict = BuildSignatureFieldWithAppearance(
            fieldName, sigValObjNum,
            visBanner?.ApObjNum ?? appearanceStreamObjNum, doc, appearance);
        var acroFormDict = BuildAcroFormDict(doc, sigFieldObjNum);
        var appearanceStreamDict = visBanner is null
            ? BuildSignatureAppearanceStream(appearance)
            : null;

        // When the source document is encrypted, every appended string/stream must
        // be encrypted with the document's per-object key (only the signature
        // /Contents is exempt). Otherwise the signature reason/location text and the
        // visible appearance would leak as plaintext into an encrypted PDF.
        var appendDecryptor = reader.Decryptor;
        if (appendDecryptor is not null)
        {
            // Encrypt the signer-supplied text (/Reason /Location /ContactInfo /M …)
            // and the visible appearance content. The field dict's /T is left as-is
            // because signature-name lookup reads it structurally during verify.
            EncryptAppendedDict(appendDecryptor, sigValObjNum, sigValDict);
            if (appearanceStreamDict is not null)
                EncryptAppendedStream(appendDecryptor, appearanceStreamObjNum, appearanceStreamDict);
            if (visBanner is { } encBanner)
                foreach (var (num, dict, isStream) in encBanner.Objects)
                {
                    if (isStream) EncryptAppendedStream(appendDecryptor, num, dict);
                    else EncryptAppendedDict(appendDecryptor, num, dict);
                }
        }

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
        if (appearanceStreamDict is not null)
        {
            offsets[appearanceStreamObjNum] = ms.Position;
            WriteStreamObject(ms, appearanceStreamObjNum, appearanceStreamDict);
        }
        if (visBanner is { } visObjs)
            foreach (var (num, dict, isStream) in visObjs.Objects)
            {
                offsets[num] = ms.Position;
                if (isStream) WriteStreamObject(ms, num, dict);
                else WriteIndirectObject(ms, num, dict);
            }

        // The widget must ride in its page's /Annots — viewers and rasterisers
        // paint annotations from the page tree, not from the AcroForm field
        // list — so the page object is rewritten with the widget appended.
        var pageRef = FindPageRef(reader, Math.Max(0, appearance.PageNumber - 1));
        if (pageRef is not null && reader.ResolveDict(pageRef) is { } sigPageDict)
        {
            var newPage = CloneDict(sigPageDict);
            var annots = new PdfArray();
            if (reader.Resolve(sigPageDict.Get("Annots")) is PdfArray oldAnnots)
                foreach (var a in oldAnnots) annots.Add(a);
            annots.Add(new PdfIndirectRef(sigFieldObjNum, 0));
            newPage.Set("Annots", annots);
            offsets[pageRef.ObjectNumber] = ms.Position;
            WriteIndirectObject(ms, pageRef.ObjectNumber, newPage);
        }

        var catalogRef = trailer.Get("Root") as PdfIndirectRef;
        var catalogObjNum = catalogRef?.ObjectNumber ?? 1;
        var catalogDict = CloneDict(reader.Catalog);
        catalogDict.Set("AcroForm", new PdfIndirectRef(acroFormObjNum, 0));

        if (options.DocMdpPermissions is not null)
        {
            var perms = new PdfDictionary();
            perms.Set("DocMDP", new PdfIndirectRef(sigValObjNum, 0));
            catalogDict.Set("Perms", perms);
        }

        if (options.UseLtv)
        {
            offsets[dssCertObjNum] = ms.Position;
            WriteStreamObject(ms, dssCertObjNum, BuildCertStreamDict(certificate.CertificateDer));
            catalogDict.Set("DSS", BuildDssDict(dssCertObjNum));
        }

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

        // Hash the two byte ranges (SHA-256, or SHA-1 for adbe.pkcs7.sha1 / adbe.x509.rsa_sha1).
        var digest = CmsBuilder.ResolveDigest(
            certificate.KeyKind, options.Digest, DigestForSubFilter(options.SubFilter));
        var hashInput = new byte[(int)byteRange[1] + (int)byteRange[3]];
        Array.Copy(fileBytes, 0, hashInput, 0, (int)byteRange[1]);
        Array.Copy(fileBytes, (int)byteRange[2], hashInput, (int)byteRange[1], (int)byteRange[3]);
        var hash = HashByteRange(hashInput, digest);

        var signatureBytes = options.CustomSignHash is not null
            ? options.CustomSignHash(hash, digest)
            : CreatePkcs7Signature(hash, certificate, digest);

        if (signatureBytes.Length > contentsSize)
            throw options.AvoidEstimating
                ? new SignatureLengthMismatchException(signatureBytes.Length)
                : new InvalidOperationException(
                    $"Signature ({signatureBytes.Length} bytes) exceeds reserved space ({contentsSize} bytes).");

        var hexSignature = Convert.ToHexString(signatureBytes);
        hexSignature = hexSignature.PadRight(contentsSize * 2, '0');
        var hexBytes = Encoding.ASCII.GetBytes(hexSignature);
        Array.Copy(hexBytes, 0, fileBytes, (int)contentsOffset + 1, hexBytes.Length);

        return fileBytes;
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

    /// <summary>Indirect reference of the pageIndex-th page leaf (0-based), by a
    /// recursive walk of the page tree.</summary>
    private static PdfIndirectRef? FindPageRef(IO.PdfReader reader, int pageIndex)
    {
        var pagesRoot = reader.ResolveDict(reader.Catalog.Get("Pages"));
        if (pagesRoot is null) return null;
        int counter = 0;
        return WalkPageTree(reader, pagesRoot, pageIndex, ref counter, depth: 0);
    }

    private static PdfIndirectRef? WalkPageTree(IO.PdfReader reader, PdfDictionary node,
        int target, ref int counter, int depth)
    {
        if (depth > 64) return null;
        if (reader.Resolve(node.Get("Kids")) is not PdfArray kids) return null;
        foreach (var kid in kids)
        {
            if (kid is not PdfIndirectRef kref) continue;
            var kd = reader.ResolveDict(kref);
            if (kd is null) continue;
            if (kd.GetName("Type") == "Pages")
            {
                var r = WalkPageTree(reader, kd, target, ref counter, depth + 1);
                if (r is not null) return r;
            }
            else
            {
                if (counter == target) return kref;
                counter++;
            }
        }
        return null;
    }

    // The visible-signature banner's text colour (a light blue).
    private const string BannerFillRgb = "0.301960784313725 0.501960784313725 1";

    private const string BannerStrokeRgb = "0.3 0.5 1";

    /// <summary>
    /// Build a Form XObject appearance stream showing signature details: a
    /// left-aligned block of banner lines — signer, date, then any of
    /// reason/location/contact that are present — set with the text object's
    /// leading (fontSize × 1.2) from the box top, in the banner blue.
    /// Returns a dictionary with stream content stored under the "__StreamData" key
    /// (used by WriteStreamObject).
    /// </summary>
    internal static PdfDictionary BuildSignatureAppearanceStream(SignatureAppearance appearance)
    {
        var r = appearance.Rect ?? new Rectangle(0, 0, 200, 100);
        var width = r.Width;
        var height = r.Height;
        var fontSize = appearance.FontSize;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(appearance.SignerName))
            lines.Add($"Digitally signed by '{appearance.SignerName}'");
        var signDate = appearance.SignDate ?? DateTime.Now;
        // The banner stamps a local wall-clock time with its own UTC offset. The
        // offset specifier always reports the LOCAL zone, so a caller-supplied UTC
        // instant is converted first — otherwise the digits stay UTC while the
        // offset beside them claims local, and the two disagree.
        if (signDate.Kind == DateTimeKind.Utc) signDate = signDate.ToLocalTime();
        lines.Add("Date: " + signDate.ToString("yyyy.MM.dd HH:mm:ss zzz",
            System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(appearance.Reason))
            lines.Add($"Reason: {appearance.Reason}");
        if (!string.IsNullOrEmpty(appearance.Location))
            lines.Add($"Location: {appearance.Location}");
        if (!string.IsNullOrEmpty(appearance.ContactInfo))
            lines.Add($"Contact: {appearance.ContactInfo}");

        var sb = new StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        sb.Append(BannerFillRgb).Append(" rg\n");
        sb.Append("q\n");
        sb.Append("1 0 0 1 0 0 cm\n");
        sb.Append("BT\n");
        // First baseline one font size below the box top; the leading advances
        // each further line via T*.
        sb.AppendFormat(inv, "1 0 0 1 0 {0:0.##} Tm\n", height - fontSize);
        sb.AppendFormat(inv, "{0:0.##} TL\n", fontSize * 1.2);
        sb.Append(BannerStrokeRgb).Append(" RG\n");
        sb.AppendFormat(inv, "/F1 {0:0.##} Tf\n", fontSize);
        foreach (var line in lines)
        {
            sb.AppendFormat("({0}) Tj\n", EscapePdfString(line));
            sb.Append("T*\n");
        }
        sb.Append("ET\n");
        sb.Append("Q\n");

        var streamContent = Encoding.Latin1.GetBytes(sb.ToString());

        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Form"));
        dict.Set("BBox", CreateBBoxArray(0, 0, width, height));
        dict.Set("Length", new PdfInteger(streamContent.Length));

        // Resources with the appearance font (a custom appearance may request a
        // specific family, e.g. "Times New Roman" → "TimesNewRoman"). The default is
        // Arial — the signer names the concrete host face rather than the
        // abstract Helvetica (a signed PDF/A must embed a concrete font, e.g. Arial).
        var baseFont = string.IsNullOrEmpty(appearance.FontFamily)
            ? "Arial"
            : appearance.FontFamily.Replace(" ", "");
        var fontDict = new PdfDictionary();
        var f1Dict = new PdfDictionary();
        f1Dict.Set("Type", new PdfName("Font"));
        f1Dict.Set("Subtype", new PdfName("Type1"));
        f1Dict.Set("BaseFont", new PdfName(baseFont));
        // The banner lines may carry accented text (a reason such as "Approuvé");
        // WinAnsi maps those Latin-1 codes to the right glyphs.
        f1Dict.Set("Encoding", new PdfName("WinAnsiEncoding"));
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
}
