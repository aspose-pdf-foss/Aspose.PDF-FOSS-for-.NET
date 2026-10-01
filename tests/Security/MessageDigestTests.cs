using System;
using System.Text;
using Aspose.Pdf.Security;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>Digests fed in pieces, against the published test vectors.</summary>
public class MessageDigestTests
{
    private static string Digest(string name, string text)
    {
        var digest = MessageDigest.Create(name)!;
        var bytes = Encoding.ASCII.GetBytes(text);
        digest.Update(bytes, 0, bytes.Length / 2);
        digest.Update(bytes, bytes.Length / 2, bytes.Length - bytes.Length / 2);
        return BitConverter.ToString(digest.Finish()).Replace("-", "").ToLowerInvariant();
    }

    [Theory]
    [InlineData("MD2", "", "8350e5a3e24c153df2275c9f80692773")]
    [InlineData("MD2", "abc", "da853b0d3f88d99b30283a69e6ded6bb")]
    [InlineData("MD2", "message digest", "ab4f496bfb2a530b219ff33031fe06b0")]
    [InlineData("MD5", "abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("SHA-1", "abc", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData("SHA-224", "abc", "23097d223405d8228642a477bda255b32aadbce4bda0b3f7e36c9da7")]
    [InlineData("SHA-256", "abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("RIPEMD160", "abc", "8eb208f7e05d987a9b044a8e98c6b087f15a0bfc")]
    [InlineData("SHA3-224", "", "6b4e03423667dbb73b6e15454f0eb1abd4597f9a1b078e3f5b5a6bc7")]
    [InlineData("SHA3-256", "abc", "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532")]
    [InlineData("SHAKE128", "", "7f9c2ba4e88f827d616045507605853ed73b8093f6efbc88eb1a6eacfa66ef26")]
    [InlineData("SHAKE256", "", "46b9dd2b0ba88d13233b3feb743eeb243fcd52ea62b81b82b50c27646ed5762fd75dc4ddd8c0f200cb05019d67b592f6fc821c49479ab48640292eacb3b7c4be")]
    public void Digests_match_the_published_vectors(string name, string text, string expected) =>
        Assert.Equal(expected, Digest(name, text));

    [Fact]
    public void Finish_starts_over_and_unknown_names_give_null()
    {
        var digest = MessageDigest.Create("SHA-256")!;
        digest.Update([1, 2, 3], 0, 3);
        digest.Finish();
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", BitConverter.ToString(digest.Finish()).Replace("-", "").ToLowerInvariant());
        Assert.Null(MessageDigest.Create("SHA-999"));
        Assert.Equal(64, MessageDigest.Create("SHAKE256")!.Length);
    }
}
