namespace Aspose.Pdf.Security;

/// <summary>
/// RC4 stream cipher implementation (ARCFOUR).
/// </summary>
internal sealed class Rc4Cipher
{
    private readonly byte[] _state = new byte[256];
    private int _i;
    private int _j;

    public Rc4Cipher(byte[] key)
    {
        // KSA (Key Scheduling Algorithm)
        for (var i = 0; i < 256; i++)
            _state[i] = (byte)i;

        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + _state[i] + key[i % key.Length]) & 0xFF;
            (_state[i], _state[j]) = (_state[j], _state[i]);
        }
    }

    /// <summary>
    /// Encrypt or decrypt data in place. RC4 is symmetric — same operation for both.
    /// </summary>
    public void Transform(Span<byte> data)
    {
        for (var k = 0; k < data.Length; k++)
        {
            _i = (_i + 1) & 0xFF;
            _j = (_j + _state[_i]) & 0xFF;
            (_state[_i], _state[_j]) = (_state[_j], _state[_i]);
            var t = (_state[_i] + _state[_j]) & 0xFF;
            data[k] ^= _state[t];
        }
    }

    /// <summary>
    /// Decrypt a byte array and return the result.
    /// </summary>
    public static byte[] Decrypt(byte[] key, byte[] data)
    {
        var result = new byte[data.Length];
        data.CopyTo(result, 0);
        new Rc4Cipher(key).Transform(result);
        return result;
    }
}
