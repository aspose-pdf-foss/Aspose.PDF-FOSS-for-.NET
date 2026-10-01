using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

/// <summary>
/// Signs PDF documents with X.509 certificates using PKCS#7/CMS detached signatures.
/// Spec: PDF32000_2008 §12.8 (Digital Signatures)
/// </summary>
public sealed partial class PdfSigner
{
    /// <summary>
    /// Add an RFC 3161 document timestamp (PAdES DocTimeStamp, /SubFilter
    /// <c>ETSI.RFC3161</c>) via an incremental update. Hashes the ByteRange,
    /// requests a timestamp token from the TSA in
    /// <see cref="SignatureOptions.TimestampUrl"/>, and writes the token into
    /// the signature /Contents. No signing certificate is involved — the token
    /// is produced by the TSA.
    /// </summary>
    public static byte[] SignDocumentTimestamp(byte[] pdfData, SignatureOptions options)
    {
        // A TSA token embeds the TSA certificate chain, so reserve generously.
        const int contentsSize = 16384;

        using var doc = OpenDoc(pdfData, options.Password);
        var reader = doc.Reader;
        var trailer = reader.Trailer;
        var xref = reader.XRefTable;

        var fieldName = ResolveSignatureFieldName(doc, options.FieldName);

        var maxObj = 0;
        foreach (var entry in xref.Entries.Values)
            if (entry.ObjectNumber > maxObj) maxObj = entry.ObjectNumber;
        var nextObj = maxObj + 1;

        var existingFieldRef = FindTopLevelFieldRef(doc, fieldName);
        var existingFieldDict = existingFieldRef is not null
            ? doc.Reader.ResolveDict(existingFieldRef)
            : null;

        var sigValObjNum = nextObj++;
        var sigFieldObjNum = existingFieldDict is not null
            ? existingFieldRef!.ObjectNumber
            : nextObj++;
        var acroFormObjNum = nextObj++;

        using var ms = new MemoryStream();
        ms.Write(pdfData);

        var sigValDict = BuildTimestampValueDict(contentsSize);
        var sigFieldDict = existingFieldDict is not null
            ? BuildUpdatedSignatureFieldDict(existingFieldDict, sigValObjNum)
            : BuildSignatureFieldDict(fieldName, sigValObjNum, doc);
        var acroFormDict = BuildAcroFormDict(doc, sigFieldObjNum,
            appendField: existingFieldDict is null);

        var offsets = new Dictionary<int, long>();

        offsets[sigValObjNum] = ms.Position;
        (var sigValBytes, var contentsOffset, var contentsLength) = SerializeObject(sigValObjNum, sigValDict, contentsSize);
        ms.Write(sigValBytes);
        contentsOffset += offsets[sigValObjNum];

        offsets[sigFieldObjNum] = ms.Position;
        WriteIndirectObject(ms, sigFieldObjNum, sigFieldDict);

        offsets[acroFormObjNum] = ms.Position;
        WriteIndirectObject(ms, acroFormObjNum, acroFormDict);

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

        var digest = options.TimestampDigest == DigestHashAlgorithm.Auto
            ? DigestHashAlgorithm.Sha256
            : options.TimestampDigest;
        var hashInput = new byte[(int)byteRange[1] + (int)byteRange[3]];
        Array.Copy(fileBytes, 0, hashInput, 0, (int)byteRange[1]);
        Array.Copy(fileBytes, (int)byteRange[2], hashInput, (int)byteRange[1], (int)byteRange[3]);
        var imprint = HashData(hashInput, digest);

        var token = Rfc3161.RequestTimestampToken(
            options.TimestampUrl ?? string.Empty, options.TimestampBasicAuth, digest, imprint);

        if (token.Length > contentsSize)
            throw new InvalidOperationException(
                $"Timestamp token ({token.Length} bytes) exceeds reserved space ({contentsSize} bytes).");

        var hexToken = Compat.ToHexString(token).PadRight(contentsSize * 2, '0');
        var hexBytes = Encoding.ASCII.GetBytes(hexToken);
        Array.Copy(hexBytes, 0, fileBytes, (int)contentsOffset + 1, hexBytes.Length);

        return fileBytes;
    }

    /// <summary>
    /// Verify a signature in a PDF document. Returns true if the signature is cryptographically valid.
    /// </summary>
    public static bool Verify(byte[] pdfData, string? fieldName = null, string? password = null)
    {
        using var doc = OpenDoc(pdfData, password);
        var sigs = Forms.Signature.EnumerateSignatures(doc);

        var sig = fieldName is not null
            ? sigs.FirstOrDefault(s => s.FieldName == fieldName)
            : sigs.FirstOrDefault();

        if (sig is null || sig.ByteRangeRaw is null)
            return false;

        var br = sig.ByteRangeRaw;
        if (br.Length != 4) return false;

        // Read the PKCS#7 /Contents directly from the raw ByteRange gap
        // (between the two covered ranges). The signature /Contents is exempt
        // from document encryption (ISO 32000-1 §7.6.1), so the reader-decrypted
        // sig.ContentsRaw is garbled for encrypted documents — the on-disk hex
        // bytes in the gap are always the true, unencrypted envelope.
        var contents = ExtractContentsFromGap(pdfData, (int)br[1], (int)br[2])
                       ?? sig.ContentsRaw;
        if (contents is null) return false;

        // Collect the signed byte ranges (the raw data the signature covers).
        // Guard against a /ByteRange that does not fit the supplied bytes (e.g.
        // a re-serialized or truncated document): a signature we cannot slice
        // out is simply unverifiable, so return false rather than throwing.
        long off1 = br[0], len1 = br[1], off2 = br[2], len2 = br[3];
        if (off1 < 0 || len1 < 0 || off2 < 0 || len2 < 0 ||
            off1 + len1 > pdfData.Length || off2 + len2 > pdfData.Length)
            return false;

        // The /ByteRange gap is exempt from the digest, so the signature can
        // only vouch for the whole file if the gap holds exactly the /Contents
        // hex string (optionally wrapped in whitespace). Any other byte in the
        // gap — e.g. text overwriting the hex padding — is a post-signing
        // modification the digest cannot see: treat it as tampering.
        if (!GapIsSingleHexString(pdfData, (int)len1, (int)off2))
            return false;

        var signedData = new byte[len1 + len2];
        Array.Copy(pdfData, off1, signedData, 0, len1);
        Array.Copy(pdfData, off2, signedData, len1, len2);

        // ETSI.RFC3161 document timestamp: /Contents is an RFC 3161 TimeStampToken
        // (an enveloping CMS whose eContent is a TSTInfo), not a detached signature.
        // Verify the TSA's signature and that the token's message imprint matches
        // the digest of the covered ByteRange bytes.
        if (sig.SubFilter == "ETSI.RFC3161")
            return Rfc3161.VerifyToken(contents, signedData);

        // Raw adbe.x509.rsa_sha1 (PKCS#1): /Contents is a bare RSA signature over
        // SHA-1 of the ByteRange, and the certificate lives in the signature dict's
        // /Cert entry (not a CMS envelope).
        if (sig.SubFilter == "adbe.x509.rsa_sha1" && sig.CertRaw is { Count: > 0 })
        {
            var sha1 = HmacSha.Sha1Hash(signedData);
            var unwrapped = TryUnwrapOctetString(contents);
            foreach (var certDer in sig.CertRaw)
            {
                if (CmsBuilder.VerifyRsaPkcs1(certDer, sha1, contents)) return true;
                if (unwrapped is not null && CmsBuilder.VerifyRsaPkcs1(certDer, sha1, unwrapped)) return true;
            }
            return false;
        }

        // Try verification with raw signed bytes first (standard CMS detached),
        // then fall back to hash-as-content: adbe.pkcs7.sha1 embeds the SHA-1
        // digest of the ByteRange as the CMS content, and this library's own
        // Sign signs over the digest directly (SHA-256, or SHA-1 for pkcs7.sha1).
        if (TryVerifyCms(signedData, contents))
            return true;
        if (TryVerifyCms(ShaDigest.Sha256(signedData), contents))
            return true;
        if (TryVerifyCms(HmacSha.Sha1Hash(signedData), contents))
            return true;
        return false;
    }

    /// <summary>True when the bytes between <paramref name="gapStart"/> and
    /// <paramref name="gapEnd"/> are exactly one PDF hex string —
    /// <c>&lt;hex-digits&gt;</c> with PDF whitespace allowed inside and around it.</summary>
    private static bool GapIsSingleHexString(byte[] data, int gapStart, int gapEnd)
    {
        if (gapStart < 0 || gapEnd > data.Length || gapStart >= gapEnd) return false;
        static bool IsWs(byte c) => c is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;
        static bool IsHex(byte c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        var i = gapStart;
        while (i < gapEnd && IsWs(data[i])) i++;
        if (i >= gapEnd || data[i] != (byte)'<') return false;
        i++;
        while (i < gapEnd && data[i] != (byte)'>')
        {
            if (!IsHex(data[i]) && !IsWs(data[i])) return false;
            i++;
        }
        if (i >= gapEnd) return false; // no closing '>'
        i++;
        while (i < gapEnd && IsWs(data[i])) i++;
        return i >= gapEnd;
    }

    /// <summary>If <paramref name="data"/> is a single DER OCTET STRING, return its
    /// contents; otherwise null. Some producers wrap the adbe.x509.rsa_sha1 signature
    /// value in an OCTET STRING.</summary>
    private static byte[]? TryUnwrapOctetString(byte[]? data)
    {
        if (data is null || data.Length < 2 || data[0] != 0x04) return null;
        try { return new Asn1Reader(data).ReadOctetString(); }
        catch { return null; }
    }

    /// <summary>Extract the raw PKCS#7 bytes from a signature's /Contents hex
    /// string, read straight from the on-disk ByteRange gap
    /// (<c>&lt;hex…&gt;</c> between <paramref name="gapStart"/> and
    /// <paramref name="gapEnd"/>). Returns null when the gap holds no hex string.</summary>
    private static byte[]? ExtractContentsFromGap(byte[] pdfData, int gapStart, int gapEnd)
    {
        if (gapStart < 0 || gapEnd > pdfData.Length || gapStart >= gapEnd) return null;

        // Locate the '<' … '>' delimiting the hex /Contents within the gap.
        var open = -1;
        for (var i = gapStart; i < gapEnd; i++)
        {
            if (pdfData[i] == (byte)'<') { open = i + 1; break; }
        }
        if (open < 0) return null;
        var close = -1;
        for (var i = open; i < gapEnd; i++)
        {
            if (pdfData[i] == (byte)'>') { close = i; break; }
        }
        if (close < 0) return null;

        // Decode hex nibbles, ignoring whitespace; a signer pads the reserved
        // space with trailing '0's, which decode to trailing zero bytes that
        // the CMS parser tolerates (they follow the DER envelope's own length).
        var nibbles = new System.Collections.Generic.List<byte>(close - open);
        for (var i = open; i < close; i++)
        {
            var c = pdfData[i];
            int v;
            if (c >= '0' && c <= '9') v = c - '0';
            else if (c >= 'A' && c <= 'F') v = c - 'A' + 10;
            else if (c >= 'a' && c <= 'f') v = c - 'a' + 10;
            else continue; // skip whitespace / EOL
            nibbles.Add((byte)v);
        }
        if (nibbles.Count < 2) return null;

        var bytes = new byte[nibbles.Count / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((nibbles[2 * i] << 4) | nibbles[2 * i + 1]);
        return bytes;
    }

    private static bool TryVerifyCms(byte[] content, byte[] signature)
        => CmsBuilder.VerifyDetached(content, signature);

    /// <summary>Open PDF bytes, authenticating with <paramref name="password"/>
    /// when the document is encrypted.</summary>
    private static Document OpenDoc(byte[] pdfData, string? password)
        => password is not null ? Document.Open(pdfData, password) : Document.Open(pdfData);

    #region Private helpers

    /// <summary>Find the indirect reference of a top-level AcroForm field whose
    /// /T equals <paramref name="fieldName"/>, or null when no such field
    /// exists. Used to reuse a pre-existing blank signature field rather than
    /// appending a duplicate one when signing.</summary>
    /// <summary>The named top-level field, however /Fields carries it. A form built by
    /// <c>FormEditor.AddField</c> states the field INLINE, with no object number of its
    /// own, and a lookup that only accepts an indirect reference misses it - the signer
    /// then appends a SECOND field of the same name and the original keeps its blank
    /// placeholder appearance. The direct case reports a null ref, and the caller gives
    /// the adopted dict a fresh object number and rewrites the /Fields entry to match.</summary>
    private static (PdfIndirectRef? Ref, PdfDictionary? Dict) FindTopLevelField(
        Document doc, string fieldName)
    {
        var acroForm = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("AcroForm"));
        var fields = acroForm is null ? null : doc.Reader.Resolve(acroForm.Get("Fields")) as PdfArray;
        if (fields is null) return (null, null);
        foreach (var f in fields)
        {
            var dict = f is PdfIndirectRef r ? doc.Reader.ResolveDict(r) : f as PdfDictionary;
            if (dict is null) continue;
            var name = doc.Reader.Resolve(dict.Get("T")) as PdfString;
            if (name is null || name.ToText() != fieldName) continue;
            return (f as PdfIndirectRef, dict);
        }
        return (null, null);
    }

    /// <summary>
    /// Build the banner appearance for a signature widget and every object it needs,
    /// ready for the incremental update: the outer form, the /FRM and /n2 nesting, and
    /// the embedded Type0 face. Returns null when the widget has no usable /Rect or no
    /// face resolves, and the caller then leaves the field's own appearance alone.
    /// </summary>
    /// <remarks>See <see cref="SignatureBanner"/> for the shape that is written.</remarks>
    private static (int ApObjNum, List<(int Num, PdfDictionary Dict, bool IsStream)> Objects, int NextObj)?
        BuildBannerObjects(PdfDictionary fieldDict, PdfReader reader, SignatureOptions options,
            PdfCertificate certificate, int nextObj)
    {
        if (reader.Resolve(fieldDict.Get("Rect")) is not PdfArray rect || rect.Count < 4)
            return null;
        double N(int i) => rect[i] switch { PdfReal r => r.Value, PdfInteger n => n.Value, _ => 0.0 };
        return BuildBannerObjects(Math.Abs(N(2) - N(0)), Math.Abs(N(3) - N(1)),
            options, certificate, nextObj);
    }

    /// <summary>The banner for a box of the given size — the shape both signing paths
    /// share, the invisible one taking the box from the field it adopts and the visible
    /// one from the caller's rectangle.</summary>
    private static (int ApObjNum, List<(int Num, PdfDictionary Dict, bool IsStream)> Objects, int NextObj)?
        BuildBannerObjects(double w, double h, SignatureOptions options,
            PdfCertificate certificate, int nextObj,
            string? fontFamily = null, double bannerFontSize = 10,
            SignatureAppearance? appearance = null)
    {
        if (w < 1 || h < 1) return null;

        // A caller-supplied appearance states the metadata it wants shown, and that is
        // what the banner states: the same object drives BuildSignatureAppearanceStream
        // on the path with no box, so both must read it or one visible signature says
        // the signer's name and the other says the certificate's. First NON-EMPTY wins,
        // not first non-null: the facade builds its appearance from a Forms.Signature
        // whose string properties default to "" — a bare ?? would let those blanks
        // shadow the options' real reason/location (a CJK location silently dropping
        // out re-fonts the whole banner to Arial).
        static string? Meta(string? fromAppearance, string? fromOptions)
            => !string.IsNullOrEmpty(fromAppearance) ? fromAppearance : fromOptions;
        var lines = SignatureBanner.Lines(
            Meta(appearance?.SignerName, options.SignerName) ?? certificate.SubjectName,
            appearance?.SignDate ?? options.SigningDate ?? DateTime.Now,
            Meta(appearance?.Reason, options.Reason),
            Meta(appearance?.Location, options.Location),
            Meta(appearance?.ContactInfo, options.ContactInfo),
            appearance?.Labels);
        if (lines.Count == 0) return null;
        if (SignatureBanner.ResolveFace(lines, fontFamily) is not { } face) return null;

        Aspose.Pdf.Text.GlyphOutlineParser parser;
        try { parser = new Aspose.Pdf.Text.GlyphOutlineParser(face.Ttf); }
        catch { return null; }

        var widths = new SortedDictionary<int, int>();
        var toUnicode = new SortedDictionary<int, char>();
        var hexLines = new List<string>();
        foreach (var line in lines)
            hexLines.Add(SignatureBanner.HexGlyphs(line, parser, widths, toUnicode));

        var objects = new List<(int, PdfDictionary, bool)>();
        var fontObj = AppendBannerFace(face, widths, toUnicode, objects, ref nextObj);
        const string fontRes = "C0_0";
        var n2Obj = AppendBannerTextForm(w, h, hexLines, fontObj, fontRes,
            bannerFontSize > 0 ? bannerFontSize : 10, objects, ref nextObj);

        // A caller-supplied graphic is the /n0 layer under the text: the frame draws
        // the picture first, then the banner over it.
        var layers = new List<(string Name, int Obj)>();
        if (appearance?.ImageBytes is { Length: > 0 } picture
            && SignatureBanner.PictureForm(picture, w, h, ref nextObj, objects) is { } n0Obj)
            layers.Add(("n0", n0Obj));
        layers.Add(("n2", n2Obj));
        var frmObj = nextObj++;
        objects.Add((frmObj, SignatureBanner.Wrapper(layers, w, h, "FRM"), true));
        var apObj = nextObj++;
        objects.Add((apObj, SignatureBanner.Wrapper("FRM", frmObj, w, h, null), true));

        return (apObj, objects, nextObj);
    }

    /// <summary>The banner's face, bottom up: program, descriptor, CID font, Type0. Returns the
    /// Type0 font's object number, the one the appearance stream names.</summary>
    private static int AppendBannerFace((byte[] Ttf, string Name) face,
        SortedDictionary<int, int> widths, SortedDictionary<int, char> toUnicode,
        List<(int, PdfDictionary, bool)> objects, ref int nextObj)
    {
        var fileObj = nextObj++;
        var fileDict = new PdfDictionary();
        // The program is DEFLATED. Raw, a font's tables carry
        // long runs of zero bytes straight into the file, and a signed document is
        // checked for exactly that (a run of 50 NULs reads as an unfilled placeholder).
        var packed = DeflateBytes(face.Ttf);
        fileDict.Set("Filter", new PdfName("FlateDecode"));
        fileDict.Set("Length1", new PdfInteger(face.Ttf.Length));
        fileDict.Set("Length", new PdfInteger(packed.Length));
        fileDict.Set("__StreamData", new PdfString(packed));
        objects.Add((fileObj, fileDict, true));

        var descObj = nextObj++;
        var descDict = new PdfDictionary();
        descDict.Set("Type", new PdfName("FontDescriptor"));
        descDict.Set("FontName", new PdfName(face.Name));
        descDict.Set("Flags", new PdfInteger(32));
        descDict.Set("ItalicAngle", new PdfInteger(0));
        var (faceAscent, faceDescent, _, _) = Aspose.Pdf.Text.FontRepository.ReadTtfMetrics(face.Ttf);
        descDict.Set("Ascent", new PdfInteger(faceAscent != 0 ? faceAscent : 900));
        descDict.Set("Descent", new PdfInteger(faceDescent != 0 ? faceDescent : -200));
        descDict.Set("CapHeight", new PdfInteger(650));
        descDict.Set("StemV", new PdfInteger(0));
        var fbox = new PdfArray();
        foreach (var v in new[] { -1000, -400, 2000, 1100 }) fbox.Add(new PdfInteger(v));
        descDict.Set("FontBBox", fbox);
        descDict.Set("FontFile2", new PdfIndirectRef(fileObj, 0));
        objects.Add((descObj, descDict, false));

        var cidObj = nextObj++;
        var cidDict = new PdfDictionary();
        cidDict.Set("Type", new PdfName("Font"));
        cidDict.Set("Subtype", new PdfName("CIDFontType2"));
        cidDict.Set("BaseFont", new PdfName(face.Name));
        var sysInfo = new PdfDictionary();
        sysInfo.Set("Registry", new PdfString(Encoding.ASCII.GetBytes("Adobe")));
        sysInfo.Set("Ordering", new PdfString(Encoding.ASCII.GetBytes("Identity")));
        sysInfo.Set("Supplement", new PdfInteger(0));
        cidDict.Set("CIDSystemInfo", sysInfo);
        cidDict.Set("FontDescriptor", new PdfIndirectRef(descObj, 0));
        cidDict.Set("CIDToGIDMap", new PdfName("Identity"));
        cidDict.Set("DW", new PdfInteger(1000));
        var warr = new PdfArray();
        foreach (var (gid, adv) in widths)
        {
            warr.Add(new PdfInteger(gid));
            var one = new PdfArray();
            one.Add(new PdfInteger(adv));
            warr.Add(one);
        }
        if (widths.Count > 0) cidDict.Set("W", warr);
        objects.Add((cidObj, cidDict, false));

        var fontObj = nextObj++;
        var fontDict = new PdfDictionary();
        fontDict.Set("Type", new PdfName("Font"));
        fontDict.Set("Subtype", new PdfName("Type0"));
        fontDict.Set("BaseFont", new PdfName(face.Name));
        fontDict.Set("Encoding", new PdfName("Identity-H"));
        var descFonts = new PdfArray();
        descFonts.Add(new PdfIndirectRef(cidObj, 0));
        fontDict.Set("DescendantFonts", descFonts);
        if (toUnicode.Count > 0)
        {
            var tuObj = nextObj++;
            var tuDict = new PdfDictionary();
            var cmap = SignatureBanner.ToUnicodeCMap(toUnicode);
            tuDict.Set("Length", new PdfInteger(cmap.Length));
            tuDict.Set("__StreamData", new PdfString(cmap));
            objects.Add((tuObj, tuDict, true));
            fontDict.Set("ToUnicode", new PdfIndirectRef(tuObj, 0));
        }
        objects.Add((fontObj, fontDict, false));
        return fontObj;
    }

    /// <summary>/n2 holds the text; /FRM invokes it; the widget's /N invokes /FRM.</summary>
    private static int AppendBannerTextForm(double w, double h, List<string> hexLines,
        int fontObj, string fontRes, double fontSize,
        List<(int, PdfDictionary, bool)> objects, ref int nextObj)
    {
        var n2Obj = nextObj++;
        var n2 = new PdfDictionary();
        n2.Set("Type", new PdfName("XObject"));
        n2.Set("Subtype", new PdfName("Form"));
        n2.Set("FormType", new PdfInteger(1));
        n2.Set("Name", new PdfName("n2"));
        var n2Box = new PdfArray();
        n2Box.Add(new PdfReal(0)); n2Box.Add(new PdfReal(0));
        n2Box.Add(new PdfReal(w)); n2Box.Add(new PdfReal(h));
        n2.Set("BBox", n2Box);
        var n2Matrix = new PdfArray();
        foreach (var v in new double[] { 1, 0, 0, 1, 0, 0 }) n2Matrix.Add(new PdfReal(v));
        n2.Set("Matrix", n2Matrix);
        var n2Fonts = new PdfDictionary();
        n2Fonts.Set(fontRes, new PdfIndirectRef(fontObj, 0));
        var n2Res = new PdfDictionary();
        n2Res.Set("Font", n2Fonts);
        n2.Set("Resources", n2Res);
        var content = SignatureBanner.Content(hexLines, fontRes, fontSize, h);
        n2.Set("Length", new PdfInteger(content.Length));
        n2.Set("__StreamData", new PdfString(content));
        objects.Add((n2Obj, n2, true));
        return n2Obj;
    }


    #endregion
}
