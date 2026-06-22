namespace Aspose.Pdf.Security;

/// <summary>
/// Pure C# RC2 cipher (RFC 2268) with CBC mode.
/// Used by PKCS#12 parser for legacy PFX decryption (pbeWithSHA1And40BitRC2-CBC).
/// </summary>
internal sealed class Rc2Cipher
{
    private readonly ushort[] _k = new ushort[64];

    /// <summary>Create RC2 cipher with the given key and effective key bits (T1).</summary>
    public Rc2Cipher(byte[] key, int effectiveKeyBits)
    {
        // Key expansion (RFC 2268 Section 2)
        var t = key.Length;
        var t8 = (effectiveKeyBits + 7) / 8;
        var tm = 255 % (1 << (8 + effectiveKeyBits - 8 * t8));

        var l = new byte[128];
        Array.Copy(key, l, t);

        for (var i = t; i < 128; i++)
            l[i] = PiTable[(l[i - 1] + l[i - t]) & 0xFF];

        l[128 - t8] = PiTable[l[128 - t8] & tm];

        for (var i = 127 - t8; i >= 0; i--)
            l[i] = PiTable[l[i + 1] ^ l[i + t8]];

        // Convert to 16-bit words (little-endian)
        for (var i = 0; i < 64; i++)
            _k[i] = (ushort)(l[2 * i] | (l[2 * i + 1] << 8));
    }

    /// <summary>RC2-CBC decrypt with PKCS#5 unpadding.</summary>
    public byte[] DecryptCbc(byte[] data, byte[] iv)
    {
        var output = new byte[data.Length];
        var prev = (byte[])iv.Clone();
        var block = new byte[8];

        for (var i = 0; i < data.Length; i += 8)
        {
            DecryptBlock(data, i, block, 0);
            for (var j = 0; j < 8; j++)
                output[i + j] = (byte)(block[j] ^ prev[j]);
            Array.Copy(data, i, prev, 0, 8);
        }

        // PKCS#5 unpadding
        if (output.Length > 0)
        {
            var pad = output[^1];
            if (pad >= 1 && pad <= 8)
            {
                var valid = true;
                for (var i = output.Length - pad; i < output.Length; i++)
                    if (output[i] != pad) { valid = false; break; }
                if (valid) return output[..^pad];
            }
        }
        return output;
    }

    private void DecryptBlock(byte[] input, int inOff, byte[] output, int outOff)
    {
        var r0 = (ushort)(input[inOff] | (input[inOff + 1] << 8));
        var r1 = (ushort)(input[inOff + 2] | (input[inOff + 3] << 8));
        var r2 = (ushort)(input[inOff + 4] | (input[inOff + 5] << 8));
        var r3 = (ushort)(input[inOff + 6] | (input[inOff + 7] << 8));

        // Reverse of encryption: 5 de-mixing rounds, 1 de-mashing, 6 de-mixing, 1 de-mashing, 5 de-mixing
        for (var j = 15; j >= 12; j--) DemixRound(ref r0, ref r1, ref r2, ref r3, j);
        // j=11 is also part of the first group of 5
        DemixRound(ref r0, ref r1, ref r2, ref r3, 11);

        // De-mash
        r3 = (ushort)(r3 - _k[r2 & 63]);
        r2 = (ushort)(r2 - _k[r1 & 63]);
        r1 = (ushort)(r1 - _k[r0 & 63]);
        r0 = (ushort)(r0 - _k[r3 & 63]);

        for (var j = 10; j >= 6; j--) DemixRound(ref r0, ref r1, ref r2, ref r3, j);
        DemixRound(ref r0, ref r1, ref r2, ref r3, 5);

        // De-mash
        r3 = (ushort)(r3 - _k[r2 & 63]);
        r2 = (ushort)(r2 - _k[r1 & 63]);
        r1 = (ushort)(r1 - _k[r0 & 63]);
        r0 = (ushort)(r0 - _k[r3 & 63]);

        for (var j = 4; j >= 0; j--) DemixRound(ref r0, ref r1, ref r2, ref r3, j);

        output[outOff] = (byte)r0; output[outOff + 1] = (byte)(r0 >> 8);
        output[outOff + 2] = (byte)r1; output[outOff + 3] = (byte)(r1 >> 8);
        output[outOff + 4] = (byte)r2; output[outOff + 5] = (byte)(r2 >> 8);
        output[outOff + 6] = (byte)r3; output[outOff + 7] = (byte)(r3 >> 8);
    }

    private void DemixRound(ref ushort r0, ref ushort r1, ref ushort r2, ref ushort r3, int j)
    {
        // Reverse of mix round j (RFC 2268 Section 4)
        // Encrypt step 4: R[3] = (R[3] + K[4j+3] + (R[2] & R[1]) + (~R[2] & R[0])) <<< 5
        r3 = RotR(r3, 5);
        r3 = (ushort)(r3 - _k[4 * j + 3] - (r2 & r1) - (~r2 & r0));

        // Encrypt step 3: R[2] = (R[2] + K[4j+2] + (R[1] & R[0]) + (~R[1] & R[3])) <<< 3
        r2 = RotR(r2, 3);
        r2 = (ushort)(r2 - _k[4 * j + 2] - (r1 & r0) - (~r1 & r3));

        // Encrypt step 2: R[1] = (R[1] + K[4j+1] + (R[0] & R[3]) + (~R[0] & R[2])) <<< 2
        r1 = RotR(r1, 2);
        r1 = (ushort)(r1 - _k[4 * j + 1] - (r0 & r3) - (~r0 & r2));

        // Encrypt step 1: R[0] = (R[0] + K[4j] + (R[3] & R[2]) + (~R[3] & R[1])) <<< 1
        r0 = RotR(r0, 1);
        r0 = (ushort)(r0 - _k[4 * j] - (r3 & r2) - (~r3 & r1));
    }

    private static ushort RotR(ushort val, int n)
        => (ushort)((val >> n) | (val << (16 - n)));

    // RFC 2268 PITABLE
    private static readonly byte[] PiTable =
    [
        0xD9, 0x78, 0xF9, 0xC4, 0x19, 0xDD, 0xB5, 0xED,
        0x28, 0xE9, 0xFD, 0x79, 0x4A, 0xA0, 0xD8, 0x9D,
        0xC6, 0x7E, 0x37, 0x83, 0x2B, 0x76, 0x53, 0x8E,
        0x62, 0x4C, 0x64, 0x88, 0x44, 0x8B, 0xFB, 0xA2,
        0x17, 0x9A, 0x59, 0xF5, 0x87, 0xB3, 0x4F, 0x13,
        0x61, 0x45, 0x6D, 0x8D, 0x09, 0x81, 0x7D, 0x32,
        0xBD, 0x8F, 0x40, 0xEB, 0x86, 0xB7, 0x7B, 0x0B,
        0xF0, 0x95, 0x21, 0x22, 0x5C, 0x6B, 0x4E, 0x82,
        0x54, 0xD6, 0x65, 0x93, 0xCE, 0x60, 0xB2, 0x1C,
        0x73, 0x56, 0xC0, 0x14, 0xA7, 0x8C, 0xF1, 0xDC,
        0x12, 0x75, 0xCA, 0x1F, 0x3B, 0xBE, 0xE4, 0xD1,
        0x42, 0x3D, 0xD4, 0x30, 0xA3, 0x3C, 0xB6, 0x26,
        0x6F, 0xBF, 0x0E, 0xDA, 0x46, 0x69, 0x07, 0x57,
        0x27, 0xF2, 0x1D, 0x9B, 0xBC, 0x94, 0x43, 0x03,
        0xF8, 0x11, 0xC7, 0xF6, 0x90, 0xEF, 0x3E, 0xE7,
        0x06, 0xC3, 0xD5, 0x2F, 0xC8, 0x66, 0x1E, 0xD7,
        0x08, 0xE8, 0xEA, 0xDE, 0x80, 0x52, 0xEE, 0xF7,
        0x84, 0xAA, 0x72, 0xAC, 0x35, 0x4D, 0x6A, 0x2A,
        0x96, 0x1A, 0xD2, 0x71, 0x5A, 0x15, 0x49, 0x74,
        0x4B, 0x9F, 0xD0, 0x5E, 0x04, 0x18, 0xA4, 0xEC,
        0xC2, 0xE0, 0x41, 0x6E, 0x0F, 0x51, 0xCB, 0xCC,
        0x24, 0x91, 0xAF, 0x50, 0xA1, 0xF4, 0x70, 0x39,
        0x99, 0x7C, 0x3A, 0x85, 0x23, 0xB8, 0xB4, 0x7A,
        0xFC, 0x02, 0x36, 0x5B, 0x25, 0x55, 0x97, 0x31,
        0x2D, 0x5D, 0xFA, 0x98, 0xE3, 0x8A, 0x92, 0xAE,
        0x05, 0xDF, 0x29, 0x10, 0x67, 0x6C, 0xBA, 0xC9,
        0xD3, 0x00, 0xE6, 0xCF, 0xE1, 0x9E, 0xA8, 0x2C,
        0x63, 0x16, 0x01, 0x3F, 0x58, 0xE2, 0x89, 0xA9,
        0x0D, 0x38, 0x34, 0x1B, 0xAB, 0x33, 0xFF, 0xB0,
        0xBB, 0x48, 0x0C, 0x5F, 0xB9, 0xB1, 0xCD, 0x2E,
        0xC5, 0xF3, 0xDB, 0x47, 0xE5, 0xA5, 0x9C, 0x77,
        0x0A, 0xA6, 0x20, 0x68, 0xFE, 0x7F, 0xC1, 0xAD,
    ];
}
