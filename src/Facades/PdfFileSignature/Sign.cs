using Aspose.Pdf.Forms;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileSignature
{
    /// <summary>Load a signing certificate from a PFX/PKCS#12 file. Stored
    /// for subsequent <see cref="Sign(int, string, string, string, bool, System.Drawing.Rectangle)"/>
    /// calls that don't take an explicit <see cref="Forms.Signature"/>.</summary>
    public void SetCertificate(string pfx, string pass)
    {
        _certificate = Security.PdfCertificate.FromPfx(pfx, pass ?? string.Empty);
    }

    /// <summary>Sign with the previously-loaded <see cref="SetCertificate"/>
    /// certificate. <paramref name="annotRect"/> selects the on-page
    /// rectangle for the visible appearance when <paramref name="visible"/>
    /// is true.</summary>
    public void Sign(int page, string SigReason, string SigContact, string SigLocation,
        bool visible, System.Drawing.Rectangle annotRect)
    {
        if (_certificate is null)
            throw new InvalidOperationException("No certificate set. Call SetCertificate() or use a Sign overload that takes Forms.Signature.");
        SignCore(_certificate, fieldName: null, page, SigReason, SigContact, SigLocation, visible, annotRect, sig: null);
    }

    /// <summary>Sign on the given <paramref name="page"/> using the certificate
    /// embedded in <paramref name="sig"/>.</summary>
    public void Sign(int page, bool visible, System.Drawing.Rectangle annotRect, Forms.Signature sig)
    {
        if (TrySignDocumentTimestamp(sig, fieldName: null)) return;
        var cert = RequireCertificate(sig);
        SignCore(cert, fieldName: null, page, sig.Reason, sig.ContactInfo, sig.Location, visible, annotRect, sig);
    }

    /// <summary>Sign on the given <paramref name="page"/> with explicit
    /// metadata, using the certificate embedded in <paramref name="sig"/>.</summary>
    public void Sign(int page, string SigReason, string SigContact, string SigLocation,
        bool visible, System.Drawing.Rectangle annotRect, Forms.Signature sig)
    {
        if (TrySignDocumentTimestamp(sig, fieldName: null)) return;
        var cert = RequireCertificate(sig);
        SignCore(cert, fieldName: null, page, SigReason, SigContact, SigLocation, visible, annotRect, sig);
    }

    /// <summary>Sign with explicit field name + metadata.</summary>
    public void Sign(int page, string SigName, string SigReason, string SigContact, string SigLocation,
        bool visible, System.Drawing.Rectangle annotRect, Forms.Signature sig)
    {
        if (TrySignDocumentTimestamp(sig, SigName)) return;
        var cert = RequireCertificate(sig);
        SignCore(cert, SigName, page, SigReason, SigContact, SigLocation, visible, annotRect, sig);
    }

    /// <summary>Sign an existing blank signature field by name.</summary>
    public void Sign(string SigName, Forms.Signature sig)
    {
        if (TrySignDocumentTimestamp(sig, SigName)) return;
        var cert = RequireCertificate(sig);
        SignCore(cert, SigName, page: 1, sig.Reason, sig.ContactInfo, sig.Location, visible: false, default, sig);
    }

    /// <summary>Sign an existing blank signature field by name with explicit metadata.</summary>
    public void Sign(string SigName, string SigReason, string SigContact, string SigLocation, Forms.Signature sig)
    {
        if (TrySignDocumentTimestamp(sig, SigName)) return;
        var cert = RequireCertificate(sig);
        SignCore(cert, SigName, page: 1, SigReason, SigContact, SigLocation, visible: false, default, sig);
    }

    /// <summary>When <paramref name="sig"/> is a standalone RFC 3161 document
    /// timestamp (built via <see cref="Forms.PKCS7Detached(Aspose.Pdf.TimestampSettings)"/>),
    /// produce an <c>ETSI.RFC3161</c> DocTimeStamp signature via the TSA and
    /// update the bound bytes. Returns true when handled (no certificate is
    /// required); false for an ordinary certificate-based signature.</summary>
    private bool TrySignDocumentTimestamp(Forms.Signature? sig, string? fieldName)
    {
        if (sig is null || !sig.IsDocumentTimestamp) return false;
        var input = RequireBound();
        var settings = sig.TimestampSettings ?? new TimestampSettings();
        var opts = new Security.SignatureOptions
        {
            Password = _password,
            FieldName = fieldName,
            SubFilter = "ETSI.RFC3161",
            TimestampUrl = settings.ServerUrl,
            TimestampBasicAuth = settings.BasicAuthCredentials,
            TimestampDigest = settings.DigestHashAlgorithm,
        };
        _boundPdf = Security.PdfSigner.SignDocumentTimestamp(input, opts);
        return true;
    }

    /// <summary>Sign on the given page with a /DocMDP reference so the
    /// resulting signature is a certifying signature (PDF 32000-1 §12.8.2.2).</summary>
    public void Certify(int page, string SigReason, string SigContact, string SigLocation,
        bool visible, System.Drawing.Rectangle annotRect, Forms.DocMDPSignature docMdpSignature)
    {
        if (docMdpSignature is null) throw new ArgumentNullException(nameof(docMdpSignature));
        var cert = RequireCertificate(docMdpSignature.Signature);
        CertifyCore(cert, fieldName: null, page, SigReason, SigContact, SigLocation,
            visible, annotRect, docMdpSignature.AccessPermissions);
    }

    /// <summary>Certify an existing blank signature field by name.</summary>
    public void Certify(string sigName, Forms.DocMDPSignature docMdpSignature)
    {
        if (docMdpSignature is null) throw new ArgumentNullException(nameof(docMdpSignature));
        var cert = RequireCertificate(docMdpSignature.Signature);
        CertifyCore(cert, sigName, page: 1,
            docMdpSignature.Signature.Reason,
            docMdpSignature.Signature.ContactInfo,
            docMdpSignature.Signature.Location,
            visible: false, default, docMdpSignature.AccessPermissions);
    }

    /// <summary>Extract the /AP /N (normal appearance) stream of the named
    /// signature's widget. Returns the raw appearance content stream as a
    /// read-only MemoryStream, or null if no appearance is present.</summary>
    public Stream? ExtractImage(string signName)
    {
        var input = RequireBound();
        using var doc = OpenDoc(input);
        var form = doc.Form;
        if (form is null) return null;
        foreach (var field in form.Fields)
        {
            if (field.Type != Forms.FieldType.Signature) continue;
            if (field.FullName != signName && field.PartialName != signName) continue;
            // Look in the field's /AP /N, then in any widget child's /AP /N.
            var stream = FindAppearanceStream(field.Dict, doc.Reader);
            var img = ExtractAppearanceImage(stream, doc.Reader);
            if (img is not null) return img;
            var kids = doc.Reader.Resolve(field.Dict.Get("Kids")) as Aspose.Pdf.Core.PdfArray;
            if (kids is null) continue;
            foreach (var kid in kids)
            {
                var kidDict = doc.Reader.ResolveDict(kid);
                if (kidDict is null) continue;
                stream = FindAppearanceStream(kidDict, doc.Reader);
                img = ExtractAppearanceImage(stream, doc.Reader);
                if (img is not null) return img;
            }
        }
        return null;
    }

    /// <summary>Return the image embedded in a signature appearance Form XObject:
    /// the first /Subtype /Image in its /Resources /XObject. For DCT/JPX-coded
    /// images the raw stream bytes are a standalone JPEG/JP2 file and are returned
    /// verbatim; otherwise the decoded appearance content stream is returned as a
    /// fallback.</summary>
    private static Stream? ExtractAppearanceImage(Aspose.Pdf.Core.PdfStream? apStream,
        Aspose.Pdf.IO.PdfReader reader)
    {
        if (apStream is null) return null;
        var raw = FindImageInForm(apStream, reader, depth: 0);
        if (raw is not null) return new MemoryStream(raw, writable: false);
        // Fallback: the decoded appearance content stream (legacy behaviour).
        return new MemoryStream(reader.DecodeStream(apStream), writable: false);
    }

    /// <summary>Recursively search a Form XObject (and any nested Form XObjects,
    /// e.g. Adobe's /FRM → /n2 signature-appearance layers) for the first
    /// DCT/JPX-coded image, returning its raw (standalone JPEG/JP2) bytes.</summary>
    private static byte[]? FindImageInForm(Aspose.Pdf.Core.PdfStream form,
        Aspose.Pdf.IO.PdfReader reader, int depth)
    {
        if (depth > 8) return null;
        var resources = reader.ResolveDict(form.Dict.Get("Resources"));
        var xobjs = resources is null ? null : reader.ResolveDict(resources.Get("XObject"));
        if (xobjs is null) return null;
        foreach (var key in xobjs.Keys)
        {
            if (reader.Resolve(xobjs.Get(key)) is not Aspose.Pdf.Core.PdfStream xs) continue;
            var subtype = xs.Dict.GetName("Subtype");
            if (subtype == "Image")
            {
                var filter = FirstFilterName(xs.Dict, reader);
                // JPEG / JPEG2000 payloads are already a self-contained image stream.
                if (filter is "DCTDecode" or "JPXDecode")
                    return xs.RawData;
                // Every other image (FlateDecode/raw/CCITT, e.g. a scanned signature
                // graphic) is decoded and re-encoded to PNG so the returned bytes are
                // a readable image rather than raw pixel data.
#pragma warning disable CA1416 // System.Drawing image encode — Windows-only at runtime
                try
                {
                    using var png = new MemoryStream();
                    new Aspose.Pdf.ImageXObject(key, xs, reader)
                        .Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    if (png.Length > 0) return png.ToArray();
                }
                catch { /* fall through to the next XObject / the caller's fallback */ }
#pragma warning restore CA1416
            }
            else if (subtype == "Form")
            {
                var nested = FindImageInForm(xs, reader, depth + 1);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static string? FirstFilterName(Aspose.Pdf.Core.PdfDictionary dict, Aspose.Pdf.IO.PdfReader reader)
    {
        var f = reader.Resolve(dict.Get("Filter"));
        return f switch
        {
            Aspose.Pdf.Core.PdfName n => n.Value,
            Aspose.Pdf.Core.PdfArray a when a.Count > 0 && reader.Resolve(a[^1]) is Aspose.Pdf.Core.PdfName ln => ln.Value,
            _ => null,
        };
    }

    public Stream? ExtractImage(SignatureName signName)
        => signName is null ? null : ExtractImage(signName.FullName);

    private static Aspose.Pdf.Core.PdfStream? FindAppearanceStream(
        Aspose.Pdf.Core.PdfDictionary fieldDict, Aspose.Pdf.IO.PdfReader reader)
    {
        var ap = reader.ResolveDict(fieldDict.Get("AP"));
        if (ap is null) return null;
        return reader.Resolve(ap.Get("N")) as Aspose.Pdf.Core.PdfStream;
    }

    private void SignCore(Security.PdfCertificate cert, string? fieldName,
        int page, string? reason, string? contact, string? location,
        bool visible, System.Drawing.Rectangle annotRect, Forms.Signature? sig,
        int? docMdpPermissions = null)
    {
        // PDF 2.0 (ISO 32000-2 §12.8.3) removes the SHA-1-era signature
        // subfilters — adbe.x509.rsa_sha1 (PKCS#1) and the enveloping
        // adbe.pkcs7.sha1 (PKCS#7); only adbe.pkcs7.detached remains legal.
        // Refuse them up front on a 2.0 document.
        if (sig is Forms.PKCS1 or Forms.PKCS7 && BoundDocumentIsPdf20())
            throw new DeprecatedFeatureException(
                "SHA-1-based signature subfilters are deprecated in PDF 2.0; use PKCS7Detached.");

        // A PKCS#1 (adbe.x509.rsa_sha1) signature is raw RSA and cannot carry a
        // DSA/ECDSA signature — the combination is rejected when the
        // document is written. Record the error and defer it to Save().
        if (sig is Forms.PKCS1 && cert.KeyKind != Security.SignatureKeyKind.Rsa)
        {
            _deferredSignException =
                new PdfException("DSA algorithm supported for PKCS7 and PKC7Detached only");
            return;
        }

        // A CERTIFIED (DocMDP) document refuses further signing for the same reason it
        // refuses encryption: appending a signature rewrites bytes the certification
        // covers, so the author's certificate would no longer validate. An ordinary
        // approval signature leaves /Perms absent and does not block a second signature.
        // Measured on an already-certified document (it throws with
        // this message) and on an uncertified one signed by the same certificate (it signs).
        if (docMdpPermissions is null && BoundDocumentIsCertified())
            throw new PdfException("You cannot change this document because it is certified.");

        var input = RequireBound();
        var opts = new Security.SignatureOptions
        {
            Reason = reason,
            Location = location,
            ContactInfo = contact,
            FieldName = fieldName,
            Password = _password,
            DocMdpPermissions = docMdpPermissions,
        };
        // Honour Forms.Signature signer knobs when present.
        if (sig is not null)
        {
            if (!string.IsNullOrEmpty(sig.Authority)) opts.SignerName = sig.Authority;
            if (sig.Date != default) opts.SigningDate = sig.Date;
            if (sig.DefaultSignatureLength > 0) opts.ContentsSize = sig.DefaultSignatureLength;
            if (sig.AvoidEstimatingSignatureLength)
            {
                opts.AvoidEstimating = true;
                // Skipping estimation reserves the fixed default size unless
                // the caller supplied an explicit length.
                if (sig.DefaultSignatureLength <= 0)
                    opts.ContentsSize = Security.SignatureOptions.DefaultSignatureSize;
            }
            if (sig.CustomSignHash is not null) opts.CustomSignHash = sig.CustomSignHash;
            opts.Digest = sig.RequestedDigest;
            if (sig.UseLtv) opts.UseLtv = true;
            // /SubFilter reflects the concrete signature subtype so the reloaded
            // signature round-trips to the same type (an enveloping PKCS7 vs a
            // detached PKCS7 — the CMS bytes are detached either way here).
            opts.SubFilter = sig switch
            {
                Forms.ExternalSignature ext => ext.Detached ? "adbe.pkcs7.detached" : "adbe.pkcs7.sha1",
                Forms.PKCS7Detached => "adbe.pkcs7.detached",
                Forms.PKCS7 => "adbe.pkcs7.sha1",
                _ => opts.SubFilter,
            };
        }
        var appearance = visible ? BuildAppearance(page, annotRect, sig) : null;
        if (appearance is not null)
            appearance.ImageBytes = ResolveAppearanceImageBytes();
        try
        {
            var signed = appearance is not null
                ? Security.PdfSigner.SignWithAppearance(input, cert, opts, appearance)
                : Security.PdfSigner.Sign(input, cert, opts);
            _boundPdf = signed;
        }
        catch (Security.SignatureLengthMismatchException ex)
        {
            // The facade surfaces an oversized un-estimated
            // signature at Save time, not at Sign — defer it.
            _deferredSignException = ex;
        }
    }

    private void CertifyCore(Security.PdfCertificate cert, string? fieldName,
        int page, string? reason, string? contact, string? location,
        bool visible, System.Drawing.Rectangle annotRect,
        Forms.DocMDPAccessPermissions accessPermissions)
    {
        // The /DocMDP transform reference and catalog /Perms are written by the
        // signer BEFORE the ByteRange is hashed, so the certifying reference is
        // covered by (and does not invalidate) the signature.
        SignCore(cert, fieldName, page, reason, contact, location, visible, annotRect,
            sig: null, docMdpPermissions: (int)accessPermissions);
    }

    private byte[]? ResolveAppearanceImageBytes()
    {
        if (_signatureAppearanceStream is not null)
        {
            if (_signatureAppearanceStream.CanSeek) _signatureAppearanceStream.Position = 0;
            using var ms = new MemoryStream();
            _signatureAppearanceStream.CopyTo(ms);
            return ms.ToArray();
        }
        if (!string.IsNullOrEmpty(_signatureAppearanceFile) && File.Exists(_signatureAppearanceFile))
            return File.ReadAllBytes(_signatureAppearanceFile);
        return null;
    }

    private static Security.SignatureAppearance BuildAppearance(int page, System.Drawing.Rectangle annotRect, Forms.Signature? sig)
    {
        var appearance = new Security.SignatureAppearance
        {
            PageNumber = page,
            Rect = new Rectangle(annotRect.Left, annotRect.Top, annotRect.Right, annotRect.Bottom),
        };
        // The banner carries the signature's own metadata WITHOUT any opt-in: measured
        // with ShowProperties left alone, and the appearance still states
        // the reason, location and contact under "Digitally signed by".
        appearance.Reason = sig?.Reason;
        appearance.Location = sig?.Location;
        appearance.ContactInfo = sig?.ContactInfo;
        appearance.SignerName = sig?.Authority;
        // Honour a custom appearance's font and size.
        if (sig?.CustomAppearance is { } ca)
        {
            appearance.FontFamily = ca.FontFamilyName;
            if (ca.FontSize > 0) appearance.FontSize = ca.FontSize;
        }
        return appearance;
    }

    private static Security.PdfCertificate RequireCertificate(Forms.Signature? sig)
    {
        if (sig is null) throw new ArgumentNullException(nameof(sig));
        if (sig.Certificate is null)
            throw new InvalidOperationException("Forms.Signature has no certificate. Construct it with a PFX file or stream.");
        return sig.Certificate;
    }

    /// <summary>FOSS-only convenience: return the bound (possibly signed)
    /// PDF bytes.</summary>
    public byte[] ToByteArray() => RequireBound();
}
