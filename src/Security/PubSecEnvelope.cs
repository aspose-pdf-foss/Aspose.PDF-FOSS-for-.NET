using System;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;

namespace Aspose.Pdf.Security;

/// <summary>
/// PKCS#1 v1.5 RSA key-transport (encrypt/decrypt of a small content-encryption
/// key). Operates on the public fields of <see cref="RsaKey"/> so the shared
/// <see cref="RsaKey"/> type is not modified.
/// </summary>
internal static class RsaKeyTransport
{
    /// <summary>Public-key encrypt with PKCS#1 v1.5 type-2 padding.</summary>
    public static byte[] EncryptPkcs1(RsaKey key, byte[] data)
    {
        var keyLen = key.Modulus.Length;
        if (data.Length > keyLen - 11)
            throw new ArgumentException("RSA PKCS#1 v1.5: data too long for key size.");

        // EB = 0x00 0x02 [PS: >=8 non-zero random] 0x00 [data]
        var eb = new byte[keyLen];
        eb[1] = 0x02;
        var psLen = keyLen - data.Length - 3;
        var ps = CryptoRandom.GetBytes(psLen);
        for (var i = 0; i < psLen; i++)
            while (ps[i] == 0) ps[i] = CryptoRandom.GetBytes(1)[0];
        ps.CopyTo(eb, 2);
        eb[2 + psLen] = 0x00;
        data.CopyTo(eb, 3 + psLen);

        var m = new BigInteger(eb, isUnsigned: true, isBigEndian: true);
        var n = new BigInteger(key.Modulus, isUnsigned: true, isBigEndian: true);
        var e = new BigInteger(key.PublicExponent, isUnsigned: true, isBigEndian: true);
        return ToFixed(BigInteger.ModPow(m, e, n), keyLen);
    }

    /// <summary>Private-key decrypt and PKCS#1 v1.5 type-2 unpad.</summary>
    public static byte[] DecryptPkcs1(RsaKey key, byte[] ciphertext)
    {
        var keyLen = key.Modulus.Length;
        var c = new BigInteger(ciphertext, isUnsigned: true, isBigEndian: true);
        var eb = ToFixed(PrivateOp(key, c), keyLen);

        if (eb[0] != 0x00 || eb[1] != 0x02)
            throw new System.Security.Cryptography.CryptographicException("RSA PKCS#1 v1.5: bad padding.");
        var sep = 2;
        while (sep < eb.Length && eb[sep] != 0x00) sep++;
        if (sep >= eb.Length || sep < 10)
            throw new System.Security.Cryptography.CryptographicException("RSA PKCS#1 v1.5: separator not found.");
        return eb[(sep + 1)..];
    }

    private static BigInteger PrivateOp(RsaKey key, BigInteger m)
    {
        if (key.P is not null && key.Q is not null && key.Dp is not null && key.Dq is not null && key.InverseQ is not null)
        {
            var p = new BigInteger(key.P, isUnsigned: true, isBigEndian: true);
            var q = new BigInteger(key.Q, isUnsigned: true, isBigEndian: true);
            var dp = new BigInteger(key.Dp, isUnsigned: true, isBigEndian: true);
            var dq = new BigInteger(key.Dq, isUnsigned: true, isBigEndian: true);
            var qInv = new BigInteger(key.InverseQ, isUnsigned: true, isBigEndian: true);
            var m1 = BigInteger.ModPow(m, dp, p);
            var m2 = BigInteger.ModPow(m, dq, q);
            var h = (qInv * (m1 - m2)) % p;
            if (h.Sign < 0) h += p;
            return m2 + h * q;
        }
        var n = new BigInteger(key.Modulus, isUnsigned: true, isBigEndian: true);
        var d = new BigInteger(key.PrivateExponent, isUnsigned: true, isBigEndian: true);
        return BigInteger.ModPow(m, d, n);
    }

    private static byte[] ToFixed(BigInteger v, int length)
    {
        var raw = v.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length == length) return raw;
        var padded = new byte[length];
        Array.Copy(raw, 0, padded, length - Math.Min(raw.Length, length),
            Math.Min(raw.Length, length));
        return padded;
    }
}

/// <summary>
/// Minimal CMS/PKCS#7 EnvelopedData (RFC 5652) for the PDF public-key security
/// handler /Recipients array: RSA key-transport of a random seed to each recipient
/// certificate, with AES-CBC content encryption. Built on the library's own
/// ASN.1 primitives — no external CMS dependency.
/// </summary>
internal static class PubSecEnvelope
{
    private const string OidEnvelopedData = "1.2.840.113549.1.7.3";
    private const string OidData          = "1.2.840.113549.1.7.1";
    private const string OidRsaEncryption = "1.2.840.113549.1.1.1";
    private const string OidAes128Cbc     = "2.16.840.1.101.3.4.1.2";
    private const string OidAes192Cbc     = "2.16.840.1.101.3.4.1.22";
    private const string OidAes256Cbc     = "2.16.840.1.101.3.4.1.42";

    /// <summary>Wrap <paramref name="content"/> (seed + permissions) in an
    /// EnvelopedData for one recipient certificate. Returns the DER blob stored
    /// as one /Recipients entry.</summary>
    public static byte[] Build(byte[] content, X509Certificate2 recipient)
    {
        var cek = CryptoRandom.GetBytes(16);   // AES-128 content-encryption key
        var iv  = CryptoRandom.GetBytes(16);
        var encryptedContent = new AesCipher(cek).EncryptCbc(content, iv, pkcs7Padding: true);

        using var rsa = recipient.GetRSAPublicKey()
            ?? throw new NotSupportedException("Recipient certificate has no RSA public key.");
        var pp = rsa.ExportParameters(false);
        var recipientKey = new RsaKey(pp.Modulus!, pp.Exponent!, Array.Empty<byte>());
        var encryptedKey = RsaKeyTransport.EncryptPkcs1(recipientKey, cek);

        var issuerDer = recipient.IssuerName.RawData;
        var serialBe = (byte[])recipient.GetSerialNumber().Clone();
        Array.Reverse(serialBe);               // GetSerialNumber() is little-endian

        var w = new Asn1Writer();
        w.WriteSequence(ci =>                                   // ContentInfo
        {
            ci.WriteOid(OidEnvelopedData);
            ci.WriteContextConstructed(0, c0 =>
                c0.WriteSequence(ed =>                          // EnvelopedData
                {
                    ed.WriteInteger(0);                        // version
                    ed.WriteSet(ris =>
                        ris.WriteSequence(kt =>                 // KeyTransRecipientInfo
                        {
                            kt.WriteInteger(0);                // version
                            kt.WriteSequence(ias =>            // IssuerAndSerialNumber
                            {
                                ias.WriteRaw(issuerDer);
                                ias.WriteIntegerBytes(serialBe);
                            });
                            kt.WriteSequence(alg =>            // keyEncryptionAlgorithm
                            {
                                alg.WriteOid(OidRsaEncryption);
                                alg.WriteNull();
                            });
                            kt.WriteOctetString(encryptedKey);
                        }));
                    ed.WriteSequence(eci =>                     // EncryptedContentInfo
                    {
                        eci.WriteOid(OidData);
                        eci.WriteSequence(alg =>
                        {
                            alg.WriteOid(OidAes128Cbc);
                            alg.WriteOctetString(iv);
                        });
                        eci.WriteContextImplicit(0, encryptedContent); // [0] IMPLICIT encryptedContent
                    });
                }));
        });
        return w.ToArray();
    }

    /// <summary>Attempt to recover the enveloped content using
    /// <paramref name="privateKey"/>. Returns null if this key is not a recipient
    /// or the content encryption is unsupported.</summary>
    public static byte[]? TryDecrypt(byte[] envelope, RsaKey privateKey)
    {
        try
        {
            var ci = new Asn1Reader(envelope).ReadSequence();
            ci.ReadOid();                          // envelopedData
            var ed = ci.ReadContextConstructed(0).ReadSequence();
            ed.ReadInteger();                      // version

            byte[]? cek = null;
            var ris = ed.ReadSet();
            while (ris.HasData)
            {
                var kt = ris.ReadSequence();
                kt.ReadInteger();                  // version
                kt.Skip();                         // rid (issuerAndSerial | [0] subjectKeyId)
                kt.ReadSequence();                 // keyEncryptionAlgorithm
                var encryptedKey = kt.ReadOctetString();
                try { cek = RsaKeyTransport.DecryptPkcs1(privateKey, encryptedKey); break; }
                catch { /* not this recipient — try next */ }
            }
            if (cek is null) return null;

            var eci = ed.ReadSequence();
            eci.ReadOid();                         // data
            var alg = eci.ReadSequence();
            var algOid = alg.ReadOid();
            var iv = alg.ReadOctetString();
            var (_, encryptedContent) = eci.ReadTlv();  // [0] IMPLICIT encryptedContent

            return algOid is OidAes128Cbc or OidAes192Cbc or OidAes256Cbc
                ? new AesCipher(cek).DecryptCbc(encryptedContent, iv, pkcs7Padding: true)
                : null;                            // other content ciphers not yet supported
        }
        catch { return null; }
    }
}
