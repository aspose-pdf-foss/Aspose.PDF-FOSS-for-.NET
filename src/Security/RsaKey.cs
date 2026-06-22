using System.Numerics;

namespace Aspose.Pdf.Security;

/// <summary>
/// RSA private key with PKCS#1 v1.5 signing. Uses System.Numerics.BigInteger (not System.Security.Cryptography).
/// </summary>
internal sealed class RsaKey
{
    public byte[] Modulus { get; }       // n
    public byte[] PublicExponent { get; } // e
    public byte[] PrivateExponent { get; } // d
    // CRT components (optional, for faster signing)
    public byte[]? P { get; init; }
    public byte[]? Q { get; init; }
    public byte[]? Dp { get; init; }
    public byte[]? Dq { get; init; }
    public byte[]? InverseQ { get; init; }

    public RsaKey(byte[] modulus, byte[] publicExponent, byte[] privateExponent)
    {
        Modulus = modulus;
        PublicExponent = publicExponent;
        PrivateExponent = privateExponent;
    }

    /// <summary>Parse PKCS#8 DER-encoded private key.</summary>
    public static RsaKey FromPkcs8(byte[] der)
    {
        var r = new Asn1Reader(der);
        var seq = r.ReadSequence();
        seq.ReadInteger(); // version (0)
        var algoSeq = seq.ReadSequence();
        var oid = algoSeq.ReadOid();
        // 1.2.840.113549.1.1.1 = rsaEncryption
        var keyData = seq.ReadOctetString();
        return FromPkcs1(keyData);
    }

    /// <summary>Parse PKCS#1 RSAPrivateKey DER format.</summary>
    public static RsaKey FromPkcs1(byte[] der)
    {
        var r = new Asn1Reader(der);
        var seq = r.ReadSequence();
        seq.ReadInteger(); // version (0)
        var n = seq.ReadIntegerBytes();
        var e = seq.ReadIntegerBytes();
        var d = seq.ReadIntegerBytes();
        var p = seq.ReadIntegerBytes();
        var q = seq.ReadIntegerBytes();
        var dp = seq.ReadIntegerBytes();
        var dq = seq.ReadIntegerBytes();
        var iq = seq.ReadIntegerBytes();
        return new RsaKey(n, e, d) { P = p, Q = q, Dp = dp, Dq = dq, InverseQ = iq };
    }

    /// <summary>Sign a SHA-256 hash with PKCS#1 v1.5 padding.</summary>
    public byte[] SignSha256(byte[] hash)
    {
        // DigestInfo for SHA-256: SEQUENCE { SEQUENCE { OID 2.16.840.1.101.3.4.2.1, NULL }, OCTET STRING hash }
        var digestInfo = BuildDigestInfoSha256(hash);

        // PKCS#1 v1.5 padding: 0x00 0x01 [0xFF.] 0x00 [digestInfo]
        var keyLen = Modulus.Length;
        var padded = new byte[keyLen];
        padded[0] = 0x00;
        padded[1] = 0x01;
        var psLen = keyLen - digestInfo.Length - 3;
        for (var i = 0; i < psLen; i++) padded[2 + i] = 0xFF;
        padded[2 + psLen] = 0x00;
        Array.Copy(digestInfo, 0, padded, 3 + psLen, digestInfo.Length);

        // RSA private key operation: signature = padded^d mod n
        var m = new BigInteger(padded, isUnsigned: true, isBigEndian: true);

        BigInteger s;
        if (P is not null && Q is not null && Dp is not null && Dq is not null && InverseQ is not null)
        {
            // CRT optimization
            var p = new BigInteger(P, isUnsigned: true, isBigEndian: true);
            var q = new BigInteger(Q, isUnsigned: true, isBigEndian: true);
            var dp = new BigInteger(Dp, isUnsigned: true, isBigEndian: true);
            var dq = new BigInteger(Dq, isUnsigned: true, isBigEndian: true);
            var qInv = new BigInteger(InverseQ, isUnsigned: true, isBigEndian: true);

            var m1 = BigInteger.ModPow(m, dp, p);
            var m2 = BigInteger.ModPow(m, dq, q);
            // h = qInv · (m1 − m2) mod p. .NET BigInteger.% preserves the
            // dividend's sign, so (m1 − m2) being negative (which happens
            // whenever m2 > m1, possible for any p,q) leaves h negative.
            // Renormalise into [0, p) before composing s, otherwise s itself
            // ends up negative and ToByteArray(isUnsigned: true) throws.
            var h = (qInv * (m1 - m2)) % p;
            if (h.Sign < 0) h += p;
            s = m2 + h * q;
        }
        else
        {
            var n = new BigInteger(Modulus, isUnsigned: true, isBigEndian: true);
            var d = new BigInteger(PrivateExponent, isUnsigned: true, isBigEndian: true);
            s = BigInteger.ModPow(m, d, n);
        }

        var result = s.ToByteArray(isUnsigned: true, isBigEndian: true);
        // Pad to key length
        if (result.Length < keyLen)
        {
            var padResult = new byte[keyLen];
            Array.Copy(result, 0, padResult, keyLen - result.Length, result.Length);
            return padResult;
        }
        return result;
    }

    private static byte[] BuildDigestInfoSha256(byte[] hash)
    {
        // Pre-built DER prefix for SHA-256 DigestInfo
        // SEQUENCE { SEQUENCE { OID 2.16.840.1.101.3.4.2.1, NULL }, OCTET STRING(32) }
        byte[] prefix =
        [
            0x30, 0x31, 0x30, 0x0D, 0x06, 0x09, 0x60, 0x86,
            0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01, 0x05,
            0x00, 0x04, 0x20,
        ];
        var result = new byte[prefix.Length + hash.Length];
        prefix.CopyTo(result, 0);
        hash.CopyTo(result, prefix.Length);
        return result;
    }
}
