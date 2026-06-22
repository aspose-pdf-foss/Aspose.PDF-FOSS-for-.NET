using Aspose.Pdf.Security;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

public class Rc4CipherTests
{
    [Fact]
    public void Encrypt_Decrypt_Roundtrip()
    {
        var key = "Secret"u8.ToArray();
        var plaintext = "Hello, World!"u8.ToArray();

        // Encrypt
        var encrypted = new byte[plaintext.Length];
        plaintext.CopyTo(encrypted, 0);
        new Rc4Cipher(key).Transform(encrypted);

        // Should be different from plaintext
        Assert.NotEqual(plaintext, encrypted);

        // Decrypt
        var decrypted = new byte[encrypted.Length];
        encrypted.CopyTo(decrypted, 0);
        new Rc4Cipher(key).Transform(decrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void KnownVector_Key_0x0102030405()
    {
        // RC4 test vector from RFC 6229 (partial)
        var key = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var data = new byte[16]; // all zeros
        new Rc4Cipher(key).Transform(data);

        // First bytes of keystream for this key (known)
        Assert.Equal(0xB2, data[0]);
        Assert.Equal(0x39, data[1]);
        Assert.Equal(0x63, data[2]);
    }

    [Fact]
    public void StaticDecrypt_ReturnsNewArray()
    {
        var key = new byte[] { 1, 2, 3 };
        var data = new byte[] { 0xAB, 0xCD, 0xEF };

        var result = Rc4Cipher.Decrypt(key, data);

        // Original should be unchanged
        Assert.Equal(new byte[] { 0xAB, 0xCD, 0xEF }, data);
        Assert.NotSame(data, result);
    }

    [Fact]
    public void EmptyData_ReturnsEmpty()
    {
        var key = "key"u8.ToArray();
        var result = Rc4Cipher.Decrypt(key, Array.Empty<byte>());
        Assert.Empty(result);
    }
}
