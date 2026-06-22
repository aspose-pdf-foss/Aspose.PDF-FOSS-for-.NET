namespace Aspose.Pdf.Security;

/// <summary>
/// Builds PKCS#7/CMS SignedData structures (RFC 5652) for PDF digital signatures.
/// Replaces System.Security.Cryptography.Pkcs.SignedCms.
/// </summary>
internal static class CmsBuilder
{


    private const string OidSignedData = "1.2.840.113549.1.7.2";
    private const string OidData = "1.2.840.113549.1.7.1";
    private const string OidSha256 = "2.16.840.1.101.3.4.2.1";
    private const string OidRsaEncryption = "1.2.840.113549.1.1.1";

    /// <summary>
    /// Create a PKCS#7 detached signature over a SHA-256 hash.
    /// Returns DER-encoded ContentInfo containing SignedData.
    /// </summary>
    public static byte[] CreateDetachedSignature(byte[] hash, PdfCertificate certificate)
    {
        // Sign the hash with RSA PKCS#1 v1.5
        var signature = certificate.PrivateKey.SignSha256(hash);

        var w = new Asn1Writer();
        // ContentInfo { contentType: signedData, content: [0] SignedData }
        w.WriteSequence(ci =>
        {
            ci.WriteOid(OidSignedData);
            ci.WriteContextConstructed(0, ctx =>
            {
                // SignedData
                ctx.WriteSequence(sd =>
                {
                    // version: 1
                    sd.WriteInteger(1);

                    // digestAlgorithms: SET { AlgorithmIdentifier { sha256, NULL } }
                    sd.WriteSet(algos =>
                    {
                        algos.WriteSequence(algo =>
                        {
                            algo.WriteOid(OidSha256);
                            algo.WriteNull();
                        });
                    });

                    // encapContentInfo: ContentInfo { contentType: data }
                    // (no content — detached signature)
                    sd.WriteSequence(eci =>
                    {
                        eci.WriteOid(OidData);
                    });

                    // certificates: [0] IMPLICIT SET { certificate }
                    sd.WriteContextConstructed(0, certs =>
                    {
                        certs.WriteRaw(certificate.CertificateDer);
                    });

                    // signerInfos: SET { SignerInfo }
                    sd.WriteSet(infos =>
                    {
                        infos.WriteSequence(si =>
                        {
                            // version: 1
                            si.WriteInteger(1);

                            // sid: IssuerAndSerialNumber
                            si.WriteSequence(iasn =>
                            {
                                iasn.WriteRaw(certificate.IssuerDer);
                                iasn.WriteIntegerBytes(certificate.SerialNumber);
                            });

                            // digestAlgorithm
                            si.WriteSequence(algo =>
                            {
                                algo.WriteOid(OidSha256);
                                algo.WriteNull();
                            });

                            // signatureAlgorithm
                            si.WriteSequence(algo =>
                            {
                                algo.WriteOid(OidRsaEncryption);
                                algo.WriteNull();
                            });

                            // signature
                            si.WriteOctetString(signature);
                        });
                    });
                });
            });
        });

        return w.ToArray();
    }

    /// <summary>
    /// Verify a CMS/PKCS#7 detached signature over content bytes.
    /// Basic verification: checks RSA signature against embedded certificate.
    /// </summary>
    public static bool VerifyDetached(byte[] content, byte[] signatureDer)
    {
        try
        {
            // The PDF /Contents value is zero-padded to a fixed reserved size, so
            // signatureDer carries trailing zero bytes after the DER structure. The
            // ASN.1 reader reads the ContentInfo by its declared length and ignores
            // the padding — do NOT strip trailing zeros here, since a CMS whose final
            // byte (the low byte of the RSA signature) is legitimately 0x00 would lose
            // real signature bytes and fail to verify (~1 in 256 signatures).
            var r = new Asn1Reader(signatureDer).ReadSequence(); // ContentInfo
            var contentType = r.ReadOid();
            if (contentType != OidSignedData) return false;

            var sdCtx = r.ReadContextConstructed(0);
            var sd = sdCtx.ReadSequence(); // SignedData
            sd.ReadInteger(); // version
            sd.Skip(); // digestAlgorithms
            sd.Skip(); // encapContentInfo

            // certificates [0] — may contain multiple certs (chain)
            var certDers = new List<byte[]>();
            var certCtx = sd.TryReadContextConstructed(0);
            if (certCtx is not null)
            {
                while (certCtx.HasData)
                    certDers.Add(certCtx.ReadRawTlv());
            }

            // signerInfos
            var siSet = sd.ReadSet();
            var si = siSet.ReadSequence();
            si.ReadInteger(); // version
            si.Skip(); // sid

            // Read digest algorithm from signerInfo
            var siDigestAlgoSeq = si.ReadSequence();
            var siDigestOid = siDigestAlgoSeq.ReadOid();

            // Read signed attributes if present (context [0])
            byte[]? signedAttrsForHash = null;
            byte[]? messageDigest = null;
            if (si.PeekTag() == 0xA0)
            {
                // Read the raw [0] TLV (for re-encoding as SET)
                var signedAttrsRaw = si.ReadRawTlv();
                // Re-encode as SET (0x31) instead of context [0] (0xA0) for hashing
                signedAttrsForHash = (byte[])signedAttrsRaw.Clone();
                signedAttrsForHash[0] = 0x31;

                // Parse attributes to find message-digest
                var attrsReader = new Asn1Reader(signedAttrsRaw);
                var attrs = attrsReader.ReadContextConstructed(0);
                while (attrs.HasData)
                {
                    var attr = attrs.ReadSequence();
                    var attrOid = attr.ReadOid();
                    if (attrOid == "1.2.840.113549.1.9.4") // messageDigest
                    {
                        var attrValues = attr.ReadSet();
                        messageDigest = attrValues.ReadOctetString();
                    }
                }
            }

            si.Skip(); // signatureAlgorithm
            var sig = si.ReadOctetString();

            if (certDers.Count == 0) return false;

            // Determine which hash algorithm to use
            var contentHash = siDigestOid == OidSha256
                ? ShaDigest.Sha256(content)
                : HmacSha.Sha1Hash(content); // SHA-1 fallback

            // If signed attributes present, verify message-digest and hash attributes instead
            byte[] expectedHash;
            if (signedAttrsForHash is not null)
            {
                // Verify message-digest attribute matches content hash
                if (messageDigest is not null && !messageDigest.AsSpan().SequenceEqual(contentHash))
                    return false;

                // The RSA signature is over the hash of the DER-encoded SignedAttributes
                expectedHash = siDigestOid == OidSha256
                    ? ShaDigest.Sha256(signedAttrsForHash)
                    : HmacSha.Sha1Hash(signedAttrsForHash);
            }
            else
            {
                expectedHash = contentHash;
            }

            // Try each certificate in the chain to find the signer's cert
            foreach (var certDer in certDers)
            {
                try
                {
                    // Extract public key from certificate
                    var cert = new Asn1Reader(certDer).ReadSequence();
                    var tbs = cert.ReadSequence();
                    tbs.TryReadContextConstructed(0); // version
                    tbs.Skip(); // serial
                    tbs.Skip(); // signature algo
                    tbs.Skip(); // issuer
                    tbs.Skip(); // validity
                    tbs.Skip(); // subject
                    var spki = tbs.ReadSequence(); // SubjectPublicKeyInfo
                    spki.Skip(); // algorithm
                    var pubKeyBits = spki.ReadBitString();

                    // Parse RSA public key
                    var pkReader = new Asn1Reader(pubKeyBits).ReadSequence();
                    var modulus = pkReader.ReadIntegerBytes();
                    var exponent = pkReader.ReadIntegerBytes();

                    // RSA verify: sig^e mod n, then check PKCS#1 v1.5 padding
                    var n = new System.Numerics.BigInteger(modulus, isUnsigned: true, isBigEndian: true);
                    var e = new System.Numerics.BigInteger(exponent, isUnsigned: true, isBigEndian: true);
                    var s = new System.Numerics.BigInteger(sig, isUnsigned: true, isBigEndian: true);
                    var mVal = System.Numerics.BigInteger.ModPow(s, e, n);

                    var decrypted = mVal.ToByteArray(isUnsigned: true, isBigEndian: true);
                    if (decrypted.Length < modulus.Length)
                    {
                        var padded = new byte[modulus.Length];
                        Array.Copy(decrypted, 0, padded, modulus.Length - decrypted.Length, decrypted.Length);
                        decrypted = padded;
                    }

                    // Verify PKCS#1 v1.5: 00 01 FF.FF 00 DigestInfo
                    if (decrypted.Length < 11 || decrypted[0] != 0x00 || decrypted[1] != 0x01)
                        continue; // Try next cert

                    var i = 2;
                    while (i < decrypted.Length && decrypted[i] == 0xFF) i++;
                    if (i >= decrypted.Length || decrypted[i] != 0x00) continue;
                    i++;

                    var digestInfo = decrypted[i..];
                    var diReader = new Asn1Reader(digestInfo).ReadSequence();
                    var algoSeq = diReader.ReadSequence();
                    var hashOid = algoSeq.ReadOid();
                    var recoveredHash = diReader.ReadOctetString();

                    if (recoveredHash.AsSpan().SequenceEqual(expectedHash))
                        return true;
                }
                catch
                {
                    // Try next certificate
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
