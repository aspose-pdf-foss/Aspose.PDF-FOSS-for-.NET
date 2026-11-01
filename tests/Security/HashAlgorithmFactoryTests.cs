using System;
using System.Text;
using Aspose.Pdf.Security;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>The factory's MD5, against the RFC 1321 test vectors.</summary>
public class HashAlgorithmFactoryTests
{
    [Theory]
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("message digest", "f96b697d7cb7938d525a2f31aaf161d0")]
    public void Md5_matches_the_published_vectors(string text, string expected)
    {
        using var md5 = HashAlgorithmFactory.CreateMd5();
        var hash = md5.ComputeHash(Encoding.ASCII.GetBytes(text));
        Assert.Equal(expected, BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant());
    }
}
