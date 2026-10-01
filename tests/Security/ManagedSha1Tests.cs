using System.Text;
using Aspose.Pdf.Security;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>
/// The library's own SHA-1 is what <c>Compat.Sha1</c> answers with where the platform has no cryptography (the
/// WebAssembly build), so HTML export and the image extractor name pictures by it there. It must give the same
/// digests as the platform's.
/// </summary>
public class ManagedSha1Tests
{
    [Theory]
    [InlineData("", "da39a3ee5e6b4b0d3255bfef95601890afd80709")]
    [InlineData("abc", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "84983e441c3bd26ebaae4aa1f95129e5e54670f1")]
    public void Sha1Hash_MatchesTheStandardVectors(string text, string hex)
    {
        Assert.Equal(hex, Compat.ToHexString(HmacSha.Sha1Hash(Encoding.ASCII.GetBytes(text))).ToLowerInvariant());
    }

    [Fact]
    public void CompatSha1_AgreesWithTheManagedHash()
    {
        var data = new byte[1000];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i * 31 + 7);
        Assert.Equal(HmacSha.Sha1Hash(data), Compat.Sha1(data));
    }
}
