namespace Aspose.Pdf.Security;

/// <summary>
/// AES in Galois/Counter Mode (NIST SP 800-38D) with a 12-byte nonce and a 16-byte tag, as the
/// AESV4 crypt filter of ISO/TS 32003 uses it. The data goes through in pieces: every piece but
/// the last is a whole number of blocks. No additional authenticated data is used.
/// </summary>
internal sealed class AesGcmCipher
{
    public const int NonceLength = 12;
    public const int TagLength = 16;
    private const int BlockSize = 16;

    private readonly AesCipher _aes;
    private readonly bool _encrypting;
    private readonly byte[] _counter = new byte[BlockSize];
    private readonly byte[] _tagMask = new byte[BlockSize];
    private readonly ulong _hHigh, _hLow;
    private ulong _xHigh, _xLow;
    private long _dataLength;
    private bool _finished;

    /// <summary>A cipher for the key (16, 24 or 32 bytes) and the 12-byte nonce, encrypting or decrypting.</summary>
    public AesGcmCipher(byte[] key, byte[] nonce, bool encrypting)
    {
        if (nonce.Length != NonceLength) throw new ArgumentException($"GCM nonce must be {NonceLength} bytes: {nonce.Length}");
        _aes = new AesCipher(key);
        _encrypting = encrypting;

        var h = new byte[BlockSize];
        _aes.EncryptBlock(new byte[BlockSize], 0, h, 0);
        _hHigh = ReadBE64(h, 0);
        _hLow = ReadBE64(h, 8);

        // J0 = nonce || 0x00000001; the tag is masked with E(J0), the data counted from J0 + 1.
        nonce.CopyTo(_counter, 0);
        _counter[BlockSize - 1] = 1;
        _aes.EncryptBlock(_counter, 0, _tagMask, 0);
    }

    /// <summary>
    /// Encrypts or decrypts the next piece. Only the last piece may end part-way through a block;
    /// the authentication covers the ciphertext either way.
    /// </summary>
    public byte[] Process(byte[] data, int offset, int count)
    {
        if (_finished) throw new InvalidOperationException("GCM data after a partial block");
        var output = new byte[count];
        var keyStream = new byte[BlockSize];
        var block = new byte[BlockSize];
        for (var done = 0; done < count; done += BlockSize)
        {
            var length = Math.Min(BlockSize, count - done);
            if (length < BlockSize) _finished = true;
            IncrementCounter();
            _aes.EncryptBlock(_counter, 0, keyStream, 0);
            for (var i = 0; i < length; i++) output[done + i] = (byte)(data[offset + done + i] ^ keyStream[i]);

            var cipherText = _encrypting ? output : data;
            var cipherOffset = _encrypting ? done : offset + done;
            Array.Clear(block, 0, BlockSize);
            Array.Copy(cipherText, cipherOffset, block, 0, length);
            Absorb(block);
        }
        _dataLength += count;
        return output;
    }

    /// <summary>The authentication tag over everything processed.</summary>
    public byte[] Tag()
    {
        var lengths = new byte[BlockSize];
        WriteBE64(lengths, 8, (ulong)_dataLength * 8);
        Absorb(lengths);
        var tag = new byte[BlockSize];
        WriteBE64(tag, 0, _xHigh);
        WriteBE64(tag, 8, _xLow);
        for (var i = 0; i < BlockSize; i++) tag[i] ^= _tagMask[i];
        return tag;
    }

    /// <summary>The data encrypted under the nonce: the ciphertext, then the tag.</summary>
    public static byte[] Seal(byte[] key, byte[] nonce, byte[] plain)
    {
        var cipher = new AesGcmCipher(key, nonce, true);
        var body = cipher.Process(plain, 0, plain.Length);
        return CryptoHelper.ConcatBytes(body, cipher.Tag());
    }

    /// <summary>An object's bytes encrypted for the AESV4 filter: a random nonce, the ciphertext, the tag.</summary>
    public static byte[] EncryptObject(byte[] key, byte[] data)
    {
        var nonce = Compat.RandomBytes(NonceLength);
        return CryptoHelper.ConcatBytes(nonce, Seal(key, nonce, data));
    }

    /// <summary>
    /// An object's bytes decrypted from the AESV4 filter's nonce, ciphertext and tag; data too short
    /// to hold them, or whose tag does not match, comes back as it is.
    /// </summary>
    public static byte[] DecryptObject(byte[] key, byte[] data)
    {
        if (data.Length < NonceLength + TagLength) return data;
        try
        {
            return Open(key, data[..NonceLength], data, NonceLength, data.Length - NonceLength);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return data;
        }
    }

    /// <summary>The data a ciphertext-then-tag decrypts to under the nonce; a tag that does not match is refused.</summary>
    public static byte[] Open(byte[] key, byte[] nonce, byte[] sealedData, int offset, int count)
    {
        if (count < TagLength) throw new System.Security.Cryptography.CryptographicException("GCM data shorter than its tag");
        var cipher = new AesGcmCipher(key, nonce, false);
        var plain = cipher.Process(sealedData, offset, count - TagLength);
        var tag = cipher.Tag();
        var difference = 0;
        for (var i = 0; i < TagLength; i++) difference |= tag[i] ^ sealedData[offset + count - TagLength + i];
        if (difference != 0) throw new System.Security.Cryptography.CryptographicException("GCM tag mismatch");
        return plain;
    }

    // The last 32 bits of the counter block, incremented (inc32).
    private void IncrementCounter()
    {
        for (var i = BlockSize - 1; i >= BlockSize - 4; i--)
            if (++_counter[i] != 0) break;
    }

    // X = (X xor block) * H in GF(2^128), the GHASH step.
    private void Absorb(byte[] block)
    {
        var aHigh = _xHigh ^ ReadBE64(block, 0);
        var aLow = _xLow ^ ReadBE64(block, 8);
        ulong zHigh = 0, zLow = 0;
        ulong vHigh = _hHigh, vLow = _hLow;
        for (var i = 0; i < 128; i++)
        {
            var bit = i < 64 ? (aHigh >> (63 - i)) & 1 : (aLow >> (127 - i)) & 1;
            if (bit != 0)
            {
                zHigh ^= vHigh;
                zLow ^= vLow;
            }
            var carry = vLow & 1;
            vLow = (vLow >> 1) | (vHigh << 63);
            vHigh >>= 1;
            if (carry != 0) vHigh ^= 0xE100000000000000UL;
        }
        _xHigh = zHigh;
        _xLow = zLow;
    }

    private static ulong ReadBE64(byte[] b, int i)
    {
        ulong v = 0;
        for (var k = 0; k < 8; k++) v = (v << 8) | b[i + k];
        return v;
    }

    private static void WriteBE64(byte[] b, int i, ulong v)
    {
        for (var k = 7; k >= 0; k--)
        {
            b[i + k] = (byte)v;
            v >>= 8;
        }
    }
}
