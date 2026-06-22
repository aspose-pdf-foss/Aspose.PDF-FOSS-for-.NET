using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspose.Pdf.Security;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

// Regression: RsaKey.SignSha256 must produce a non-negative signature
// regardless of whether the PKCS#1 RSAPrivateKey was emitted with p>q
// (typical OpenSSL / Windows 11 case) or p<q (observed on Windows Server
// 2022's CNG export). When p<q, (m1-m2) can be negative for some inputs,
// and the CRT formula's modular reduction must renormalise h into [0,p)
// before composing the final signature.
public class RsaKeyCrtOrderingTests
{
    [Fact]
    public void SignSha256_WithSwappedPrimes_ProducesValidNonNegativeSignature()
    {
        // 1. Generate a fresh 2048-bit RSA key via .NET so we have a real,
        //    fully-populated PKCS#1 parameter set.
        using var rsa = RSA.Create(2048);
        var rsaParams = rsa.ExportParameters(includePrivateParameters: true);

        // 2. Build the "correct" (p>q if applicable) baseline key and a
        //    deliberately-swapped variant. The swapped variant simulates the
        //    Windows Server 2022 export ordering and forces the renormalise
        //    branch.
        var baseline = BuildRsaKey(rsaParams);
        var swapped  = BuildSwappedRsaKey(rsaParams);

        // 3. Sign the same hash with both. Output must be 256 bytes (2048-bit
        //    key) and bit-identical (the maths is the same mod n; only the
        //    CRT bookkeeping changes).
        var hash = new byte[32];
        for (int i = 0; i < hash.Length; i++) hash[i] = (byte)i;

        var sigBaseline = baseline.SignSha256(hash);
        var sigSwapped  = swapped.SignSha256(hash);

        Assert.Equal(256, sigBaseline.Length);
        Assert.Equal(256, sigSwapped.Length);
        Assert.Equal(sigBaseline, sigSwapped);
    }

    [Fact]
    public void SignSha256_WithSwappedPrimes_VerifiesAgainstPublicKey()
    {
        using var rsa = RSA.Create(2048);
        var rsaParams = rsa.ExportParameters(includePrivateParameters: true);
        var swapped = BuildSwappedRsaKey(rsaParams);

        var hash = SHA256.HashData(new byte[] { 0x42, 0x42, 0x42 });
        var signature = swapped.SignSha256(hash);

        // Verify via the original RSA public key — round-trip proves that
        // even with p<q ordering the signature is the unique m^d mod n
        // value, just produced via a corrected CRT path.
        Assert.True(rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static RsaKey BuildRsaKey(RSAParameters p)
    {
        return new RsaKey(p.Modulus!, p.Exponent!, p.D!)
        {
            P = p.P!,
            Q = p.Q!,
            Dp = p.DP!,
            Dq = p.DQ!,
            InverseQ = p.InverseQ!,
        };
    }

    private static RsaKey BuildSwappedRsaKey(RSAParameters p)
    {
        // Reinterpret prime1↔prime2 so the parser sees p<q (if .NET gave
        // us p>q, which is the common case). Need to recompute the
        // coefficient (qInv = q^(-1) mod p) for the new labelling.
        var bigP = new BigInteger(p.P!, isUnsigned: true, isBigEndian: true);
        var bigQ = new BigInteger(p.Q!, isUnsigned: true, isBigEndian: true);
        var bigDp = new BigInteger(p.DP!, isUnsigned: true, isBigEndian: true);
        var bigDq = new BigInteger(p.DQ!, isUnsigned: true, isBigEndian: true);

        // New labels: newP = old Q, newQ = old P  (so newP < newQ if old P>Q)
        var newP = bigQ;
        var newQ = bigP;
        var newDp = bigDq;
        var newDq = bigDp;
        var newQInv = ModInverse(newQ % newP, newP);

        return new RsaKey(p.Modulus!, p.Exponent!, p.D!)
        {
            P = ToFixedBigEndian(newP, p.P!.Length),
            Q = ToFixedBigEndian(newQ, p.Q!.Length),
            Dp = ToFixedBigEndian(newDp, p.DP!.Length),
            Dq = ToFixedBigEndian(newDq, p.DQ!.Length),
            InverseQ = ToFixedBigEndian(newQInv, p.InverseQ!.Length),
        };
    }

    private static byte[] ToFixedBigEndian(BigInteger value, int len)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length == len) return raw;
        var padded = new byte[len];
        Array.Copy(raw, 0, padded, len - raw.Length, raw.Length);
        return padded;
    }

    private static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        var (g, x, _) = ExtendedGcd(a, m);
        if (g != BigInteger.One) throw new InvalidOperationException("no modular inverse");
        return ((x % m) + m) % m;
    }

    private static (BigInteger gcd, BigInteger x, BigInteger y) ExtendedGcd(BigInteger a, BigInteger b)
    {
        if (b == BigInteger.Zero) return (a, BigInteger.One, BigInteger.Zero);
        var (g, x1, y1) = ExtendedGcd(b, a % b);
        return (g, y1, x1 - (a / b) * y1);
    }
}
