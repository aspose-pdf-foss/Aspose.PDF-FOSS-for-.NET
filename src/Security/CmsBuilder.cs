namespace Aspose.Pdf.Security;

/// <summary>
/// Builds PKCS#7/CMS SignedData structures (RFC 5652) for PDF digital signatures.
/// Replaces System.Security.Cryptography.Pkcs.SignedCms.
/// </summary>
internal static class CmsBuilder
{


    private const string OidSignedData = "1.2.840.113549.1.7.2";
    private const string OidData = "1.2.840.113549.1.7.1";
    private const string OidSha1 = "1.3.14.3.2.26";
    private const string OidSha256 = "2.16.840.1.101.3.4.2.1";
    private const string OidSha384 = "2.16.840.1.101.3.4.2.2";
    private const string OidSha512 = "2.16.840.1.101.3.4.2.3";

    /// <summary>Hash <paramref name="data"/> with the algorithm named by a CMS
    /// digest OID (SHA-256/384/512), falling back to SHA-1 for the legacy OID or
    /// anything unrecognised. Verification must use the same digest the signer
    /// used, so SHA-384/512 signatures no longer fail against a SHA-1 fallback.</summary>
    private static byte[] HashByOid(string digestOid, byte[] data) => digestOid switch
    {
        OidSha256 => ShaDigest.Sha256(data),
        OidSha384 => ShaDigest.Sha384(data),
        OidSha512 => ShaDigest.Sha512(data),
        // Some signers put the signatureAlgorithm OID (sha*WithRSAEncryption) in
        // the signerInfo digestAlgorithm field; map those to their digest too.
        "1.2.840.113549.1.1.11" => ShaDigest.Sha256(data), // sha256WithRSA
        "1.2.840.113549.1.1.12" => ShaDigest.Sha384(data), // sha384WithRSA
        "1.2.840.113549.1.1.13" => ShaDigest.Sha512(data), // sha512WithRSA
        _ => HmacSha.Sha1Hash(data),
    };
    private const string OidRsaEncryption = "1.2.840.113549.1.1.1";
    private const string OidEcPublicKey = "1.2.840.10045.2.1";
    private const string OidEcdsaWithSha1 = "1.2.840.10045.4.1";
    private const string OidEcdsaWithSha256 = "1.2.840.10045.4.3.2";
    private const string OidDsa = "1.2.840.10040.4.1";
    private const string OidDsaWithSha1 = "1.2.840.10040.4.3";
    private const string OidDsaWithSha256 = "2.16.840.1.101.3.4.3.2";

    private static string DigestOid(DigestHashAlgorithm digest)
        => digest == DigestHashAlgorithm.Sha1 ? OidSha1 : OidSha256;

    /// <summary>
    /// Create a PKCS#7 detached signature over a SHA-256 hash.
    /// Returns DER-encoded ContentInfo containing SignedData.
    /// </summary>
    public static byte[] CreateDetachedSignature(byte[] hash, PdfCertificate certificate,
        DigestHashAlgorithm digest = DigestHashAlgorithm.Sha256)
    {
        // Sign the hash (SHA-256 by default, SHA-1 for adbe.pkcs7.sha1). RSA uses the
        // hand-rolled PKCS#1 v1.5 path; DSA/ECDSA delegate to the platform key (r,s emitted
        // as an RFC 3279 DER SEQUENCE, which is what CMS expects). The signatureAlgorithm
        // OID identifies which.
        var (signature, sigAlgOid, sigAlgHasNullParams) = SignHash(hash, certificate, digest);
        var digestOid = DigestOid(digest);

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

                    // digestAlgorithms: SET { AlgorithmIdentifier { digest, NULL } }
                    sd.WriteSet(algos =>
                    {
                        algos.WriteSequence(algo =>
                        {
                            algo.WriteOid(digestOid);
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
                                algo.WriteOid(digestOid);
                                algo.WriteNull();
                            });

                            // signatureAlgorithm
                            si.WriteSequence(algo =>
                            {
                                algo.WriteOid(sigAlgOid);
                                // RSA carries an explicit NULL parameter; the ECDSA/DSA
                                // "-with-SHA256" identifiers take absent parameters (RFC 5758).
                                if (sigAlgHasNullParams) algo.WriteNull();
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

    /// <summary>Sign a SHA-256 hash with the certificate's private key, returning the
    /// signature bytes, the CMS signatureAlgorithm OID, and whether that algorithm
    /// identifier carries an explicit NULL parameter (RSA does; ECDSA/DSA don't).</summary>
    private static (byte[] signature, string oid, bool hasNullParams) SignHash(
        byte[] hash, PdfCertificate certificate, DigestHashAlgorithm digest = DigestHashAlgorithm.Sha256)
    {
        bool sha1 = digest == DigestHashAlgorithm.Sha1;
        switch (certificate.KeyKind)
        {
            case SignatureKeyKind.Ecdsa:
            {
                using var ec = System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions.GetECDsaPrivateKey(certificate.DotNetCert!)
                    ?? throw new InvalidOperationException("Certificate has no ECDSA private key.");
                var sig = ec.SignHash(hash,
                    System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);
                return (sig, sha1 ? OidEcdsaWithSha1 : OidEcdsaWithSha256, false);
            }
            case SignatureKeyKind.Dsa:
            {
                using var dsa = System.Security.Cryptography.X509Certificates.DSACertificateExtensions.GetDSAPrivateKey(certificate.DotNetCert!)
                    ?? throw new InvalidOperationException("Certificate has no DSA private key.");
                var sig = dsa.CreateSignature(hash,
                    System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);
                return (sig, sha1 ? OidDsaWithSha1 : OidDsaWithSha256, false);
            }
            default:
                // RSA: the hand-rolled key when the PKCS#12 parser produced one,
                // otherwise the platform key (a PFX whose encoding the hand-rolled
                // parser rejected and which fell back to X509Certificate2).
                if (certificate.PrivateKey is not null)
                    return (sha1 ? certificate.PrivateKey.SignSha1(hash) : certificate.PrivateKey.SignSha256(hash),
                            OidRsaEncryption, true);
                using (var rsa = System.Security.Cryptography.X509Certificates
                           .RSACertificateExtensions.GetRSAPrivateKey(certificate.DotNetCert!)
                       ?? throw new InvalidOperationException("Certificate has no RSA private key."))
                    return (rsa.SignHash(hash,
                                sha1 ? System.Security.Cryptography.HashAlgorithmName.SHA1
                                     : System.Security.Cryptography.HashAlgorithmName.SHA256,
                                System.Security.Cryptography.RSASignaturePadding.Pkcs1),
                            OidRsaEncryption, true);
        }
    }

    /// <summary>Verify a DSA/ECDSA signature (RFC 3279 DER r,s) over <paramref name="hash"/>
    /// against the signer certificate's public key, delegating the curve/subgroup math to
    /// the platform. Returns false on any decode/verify failure.</summary>
    private static bool VerifyDsaOrEcdsa(byte[] certDer, string keyAlgOid, byte[] hash, byte[] sig)
    {
        try
        {
            using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(certDer);
            if (keyAlgOid == OidEcPublicKey)
            {
                using var ec = System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions.GetECDsaPublicKey(cert);
                return ec is not null && ec.VerifyHash(hash, sig,
                    System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);
            }
            if (keyAlgOid == OidDsa)
            {
                using var dsa = System.Security.Cryptography.X509Certificates.DSACertificateExtensions.GetDSAPublicKey(cert);
                if (dsa is null) return false;
                var qLen = dsa.ExportParameters(false).Q!.Length;
                var p1363 = DerSigToP1363(sig, qLen);
                // DSA operates on the leftmost N bits of the digest (FIPS 186-4 §4.6).
                // VerifySignature takes the raw hash without truncating, so feed it the
                // leftmost q-sized slice to match how CreateSignature reduced the hash.
                var z = hash.Length > qLen ? hash[..qLen] : hash;
                return dsa.VerifySignature(z, p1363);
            }
        }
        catch { }
        return false;
    }

    /// <summary>Convert an RFC 3279 DER ECDSA/DSA signature (SEQUENCE { INTEGER r,
    /// INTEGER s }) to the fixed-width IEEE P1363 concatenation r‖s, each element
    /// left-padded to <paramref name="elemLen"/> bytes.</summary>
    private static byte[] DerSigToP1363(byte[] der, int elemLen)
    {
        var seq = new Asn1Reader(der).ReadSequence();
        var r = StripLeadingZeros(seq.ReadIntegerBytes());
        var s = StripLeadingZeros(seq.ReadIntegerBytes());
        var outp = new byte[elemLen * 2];
        Array.Copy(r, 0, outp, elemLen - r.Length, r.Length);
        Array.Copy(s, 0, outp, elemLen * 2 - s.Length, s.Length);
        return outp;
    }

    private static byte[] StripLeadingZeros(byte[] b)
    {
        var i = 0;
        while (i < b.Length - 1 && b[i] == 0) i++;
        return i == 0 ? b : b[i..];
    }

    /// <summary>Verify a raw RSA PKCS#1 v1.5 signature (the adbe.x509.rsa_sha1
    /// handler, whose /Contents is a bare RSA signature and whose certificate
    /// lives in the signature dictionary's /Cert entry, not in a CMS). Recovers
    /// the DigestInfo hash from <paramref name="signature"/> using the cert's
    /// public key and compares it to <paramref name="expectedHash"/>. DSA/ECDSA
    /// certs delegate to the platform. Returns false on any failure.</summary>
    public static bool VerifyRsaPkcs1(byte[] certDer, byte[] expectedHash, byte[] signature)
    {
        try
        {
            var cert = new Asn1Reader(certDer).ReadSequence();
            var tbs = cert.ReadSequence();
            tbs.TryReadContextConstructed(0); // version
            tbs.Skip(); // serial
            tbs.Skip(); // signature algo
            tbs.Skip(); // issuer
            tbs.Skip(); // validity
            tbs.Skip(); // subject
            var spki = tbs.ReadSequence();          // SubjectPublicKeyInfo
            var spkiAlgo = spki.ReadSequence();     // AlgorithmIdentifier
            var keyAlgOid = spkiAlgo.ReadOid();

            if (keyAlgOid == OidEcPublicKey || keyAlgOid == OidDsa)
                return VerifyDsaOrEcdsa(certDer, keyAlgOid, expectedHash, signature);

            var pubKeyBits = spki.ReadBitString();
            var pkReader = new Asn1Reader(pubKeyBits).ReadSequence();
            var modulus = pkReader.ReadIntegerBytes();
            var exponent = pkReader.ReadIntegerBytes();

            var n = new System.Numerics.BigInteger(modulus, isUnsigned: true, isBigEndian: true);
            var e = new System.Numerics.BigInteger(exponent, isUnsigned: true, isBigEndian: true);
            var s = new System.Numerics.BigInteger(signature, isUnsigned: true, isBigEndian: true);
            var decrypted = System.Numerics.BigInteger.ModPow(s, e, n)
                .ToByteArray(isUnsigned: true, isBigEndian: true);
            if (decrypted.Length < modulus.Length)
            {
                var padded = new byte[modulus.Length];
                Array.Copy(decrypted, 0, padded, modulus.Length - decrypted.Length, decrypted.Length);
                decrypted = padded;
            }

            // PKCS#1 v1.5: 00 01 FF..FF 00 DigestInfo
            if (decrypted.Length < 11 || decrypted[0] != 0x00 || decrypted[1] != 0x01) return false;
            var i = 2;
            while (i < decrypted.Length && decrypted[i] == 0xFF) i++;
            if (i >= decrypted.Length || decrypted[i] != 0x00) return false;
            i++;

            var di = new Asn1Reader(decrypted[i..]).ReadSequence();
            di.Skip(); // digest algorithm
            var recoveredHash = di.ReadOctetString();
            return recoveredHash.AsSpan().SequenceEqual(expectedHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verify a CMS/PKCS#7 detached signature over content bytes.
    /// Handles RSA (hand-rolled) and DSA/ECDSA (delegated to the platform key).
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

            // crls [1] — optional revocation info (CAdES producers often emit it,
            // sometimes as an empty element); skip it so signerInfos parses.
            sd.TryReadContextConstructed(1);

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
            var contentHash = HashByOid(siDigestOid, content);

            // If signed attributes present, verify message-digest and hash attributes instead
            byte[] expectedHash;
            if (signedAttrsForHash is not null)
            {
                // Verify message-digest attribute matches content hash
                if (messageDigest is not null && !messageDigest.AsSpan().SequenceEqual(contentHash))
                    return false;

                // The RSA signature is over the hash of the DER-encoded SignedAttributes
                expectedHash = HashByOid(siDigestOid, signedAttrsForHash);
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
                    var spkiAlgo = spki.ReadSequence(); // AlgorithmIdentifier
                    var keyAlgOid = spkiAlgo.ReadOid();

                    // DSA / ECDSA: delegate the signature check to the platform key.
                    if (keyAlgOid == OidEcPublicKey || keyAlgOid == OidDsa)
                    {
                        if (VerifyDsaOrEcdsa(certDer, keyAlgOid, expectedHash, sig))
                            return true;
                        continue;
                    }

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
