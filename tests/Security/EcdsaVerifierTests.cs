using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspose.Pdf.Security;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

// The managed ECDSA verifier against signatures the platform makes: every NIST prime curve, a hash shorter than,
// as long as and longer than the order (the last one truncated to its leftmost bits), and a changed hash, r or s
// refused.
public class EcdsaVerifierTests
{
    public static IEnumerable<object[]> Curves() =>
    [
        [ECCurve.NamedCurves.nistP256, HashAlgorithmName.SHA256],
        [ECCurve.NamedCurves.nistP384, HashAlgorithmName.SHA384],
        [ECCurve.NamedCurves.nistP521, HashAlgorithmName.SHA512],
    ];

    [Theory]
    [MemberData(nameof(Curves))]
    public void Verify_AcceptsWhatThePlatformSigned_AndRefusesAnyChange(ECCurve curve, HashAlgorithmName hashName)
    {
        using var key = ECDsa.Create(curve);
        var certDer = SelfSigned(key, hashName);
        var random = new Random(1);
        foreach (var length in new[] { 20, 32, 48, 64 })
        {
            for (var round = 0; round < 4; round++)
            {
                var hash = new byte[length];
                random.NextBytes(hash);
                var signature = Compat.SignHashDer(key, hash);

                Assert.True(EcdsaVerifier.Verify(certDer, hash, signature));

                var changedHash = (byte[])hash.Clone();
                changedHash[0] ^= 0x01;
                Assert.False(EcdsaVerifier.Verify(certDer, changedHash, signature));

                var changedSignature = (byte[])signature.Clone();
                changedSignature[changedSignature.Length - 1] ^= 0x01;
                Assert.False(EcdsaVerifier.Verify(certDer, hash, changedSignature));
            }
        }
    }

    [Fact]
    public void Verify_AnswersNullForAnRsaKey()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=rsa", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        Assert.Null(EcdsaVerifier.Verify(cert.RawData, new byte[32], new byte[] { 0x30, 0x06, 0x02, 0x01, 0x01, 0x02, 0x01, 0x01 }));
    }

    private static byte[] SelfSigned(ECDsa key, HashAlgorithmName hashName)
    {
        var request = new CertificateRequest("CN=ecdsa", key, hashName);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return cert.RawData;
    }
}
