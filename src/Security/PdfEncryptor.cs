using Aspose.Pdf.Core;

namespace Aspose.Pdf.Security;

/// <summary>
/// Encrypts PDF documents with passwords and permissions.
/// Supports RC4 (40/128-bit), AES-128, and AES-256 encryption.
/// </summary>
internal sealed class PdfEncryptor
{
    // Padding string (Table 21, §7.6.3.3) — 32 bytes
    // Source: PDF32000_2008 §7.6.3.3 Table 21
    private static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    ];

    private readonly byte[] _encryptionKey;
    private readonly CryptoAlgorithm _algorithm;
    private readonly int _revision;
    private readonly int _version;
    private readonly byte[] _oValue;
    private readonly byte[] _uValue;
    private readonly int _permissions;
    private readonly byte[] _fileId;

    // AES-256 (V5/R6) specific values
    private readonly byte[]? _oeValue;
    private readonly byte[]? _ueValue;
    private readonly byte[]? _permsValue;

    private PdfEncryptor(byte[] encryptionKey, CryptoAlgorithm algorithm,
        int version, int revision, byte[] oValue, byte[] uValue,
        int permissions, byte[] fileId,
        byte[]? oeValue = null, byte[]? ueValue = null, byte[]? permsValue = null)
    {
        _encryptionKey = encryptionKey;
        _algorithm = algorithm;
        _version = version;
        _revision = revision;
        _oValue = oValue;
        _uValue = uValue;
        _permissions = permissions;
        _fileId = fileId;
        _oeValue = oeValue;
        _ueValue = ueValue;
        _permsValue = permsValue;
    }

    public byte[] OValue => _oValue;
    public byte[] UValue => _uValue;
    public int Permissions => _permissions;
    public int Version => _version;
    public int Revision => _revision;
    public int KeyLengthBits => _encryptionKey.Length * 8;
    public byte[] FileId => _fileId;

    /// <summary>
    /// Create an encryptor for RC4 40-bit encryption.
    /// </summary>
    public static PdfEncryptor CreateRC4x40(string userPassword, string ownerPassword,
        int permissions = -4, byte[]? fileId = null)
    {
        return Create(CryptoAlgorithm.RC4x40, 1, 2, 5, userPassword, ownerPassword, permissions, fileId);
    }

    /// <summary>
    /// Create an encryptor for RC4 128-bit encryption.
    /// </summary>
    public static PdfEncryptor CreateRC4x128(string userPassword, string ownerPassword,
        int permissions = -4, byte[]? fileId = null)
    {
        return Create(CryptoAlgorithm.RC4x128, 2, 3, 16, userPassword, ownerPassword, permissions, fileId);
    }

    /// <summary>
    /// Create an encryptor for AES 128-bit encryption.
    /// </summary>
    public static PdfEncryptor CreateAES128(string userPassword, string ownerPassword,
        int permissions = -4, byte[]? fileId = null)
    {
        return Create(CryptoAlgorithm.AESx128, 4, 4, 16, userPassword, ownerPassword, permissions, fileId);
    }

    /// <summary>
    /// Create an encryptor for AES 256-bit encryption (V5/R6).
    /// </summary>
    public static PdfEncryptor CreateAES256(string userPassword, string ownerPassword,
        int permissions = -4, byte[]? fileId = null)
    {
        fileId ??= CryptoRandom.GetBytes(16);

        // UTF-8 encode password, truncate to 127 bytes
        var userPwBytes = System.Text.Encoding.UTF8.GetBytes(userPassword);
        if (userPwBytes.Length > 127) userPwBytes = userPwBytes[..127];
        var ownerPwBytes = System.Text.Encoding.UTF8.GetBytes(ownerPassword);
        if (ownerPwBytes.Length > 127) ownerPwBytes = ownerPwBytes[..127];

        // Generate random 32-byte file encryption key (FEK)
        var fek = CryptoRandom.GetBytes(32);

        // --- U value ---
        var uValidationSalt = CryptoRandom.GetBytes(8);
        var uKeySalt = CryptoRandom.GetBytes(8);

        // U[0.32] = ComputeHashR6(pw, valSalt, [])
        var uHash = CryptoHelper.ComputeHashR6(userPwBytes, uValidationSalt, []);
        // U = hash(32) + validationSalt(8) + keySalt(8) = 48 bytes
        var uValue = new byte[48];
        uHash.CopyTo(uValue, 0);
        uValidationSalt.CopyTo(uValue, 32);
        uKeySalt.CopyTo(uValue, 40);

        // UE = AES-256-CBC-encrypt(ComputeHashR6(pw, keySalt, []), zeroIV, FEK)
        var ueKeyHash = CryptoHelper.ComputeHashR6(userPwBytes, uKeySalt, []);
        var ueValue = CryptoHelper.EncryptAes256NoIv(ueKeyHash, fek);

        // --- O value ---
        var oValidationSalt = CryptoRandom.GetBytes(8);
        var oKeySalt = CryptoRandom.GetBytes(8);

        // O[0.32] = ComputeHashR6(ownerPw, valSalt, U[0.48])
        var oHash = CryptoHelper.ComputeHashR6(ownerPwBytes, oValidationSalt, uValue);
        // O = hash(32) + validationSalt(8) + keySalt(8) = 48 bytes
        var oValue = new byte[48];
        oHash.CopyTo(oValue, 0);
        oValidationSalt.CopyTo(oValue, 32);
        oKeySalt.CopyTo(oValue, 40);

        // OE = AES-256-CBC-encrypt(ComputeHashR6(ownerPw, keySalt, U[0.48]), zeroIV, FEK)
        var oeKeyHash = CryptoHelper.ComputeHashR6(ownerPwBytes, oKeySalt, uValue);
        var oeValue = CryptoHelper.EncryptAes256NoIv(oeKeyHash, fek);

        // --- Perms value ---
        // Build 16-byte permissions block
        var permsBlock = new byte[16];
        permsBlock[0] = (byte)(permissions & 0xFF);
        permsBlock[1] = (byte)((permissions >> 8) & 0xFF);
        permsBlock[2] = (byte)((permissions >> 16) & 0xFF);
        permsBlock[3] = (byte)((permissions >> 24) & 0xFF);
        permsBlock[4] = 0xFF; // must be 0xFF
        permsBlock[5] = 0xFF;
        permsBlock[6] = 0xFF;
        permsBlock[7] = 0xFF;
        permsBlock[8] = (byte)'T'; // EncryptMetadata = true
        permsBlock[9] = (byte)'a';
        permsBlock[10] = (byte)'d';
        permsBlock[11] = (byte)'b';
        // Bytes 12-15: random
        var randomTail = CryptoRandom.GetBytes(4);
        randomTail.CopyTo(permsBlock, 12);

        var permsValue = CryptoHelper.EncryptAes256Ecb(fek, permsBlock);

        return new PdfEncryptor(fek, CryptoAlgorithm.AESx256, 5, 6,
            oValue, uValue, permissions, fileId,
            oeValue, ueValue, permsValue);
    }

    private static PdfEncryptor Create(CryptoAlgorithm algorithm, int version, int revision,
        int keyLength, string userPassword, string ownerPassword, int permissions, byte[]? fileId)
    {
        fileId ??= CryptoRandom.GetBytes(16);

        // Ensure required permission bits are set (spec says bits 7-8 + 13-32 must be 1 for R>=3)
        if (revision >= 3)
            permissions |= unchecked((int)0xFFFFF000) | 0xC0; // Set reserved bits

        var paddedUser = PadPassword(userPassword);
        var paddedOwner = PadPassword(ownerPassword);

        // Compute O value (Algorithm 3)
        var oValue = ComputeOValue(paddedOwner, paddedUser, keyLength, revision);

        // Compute encryption key (Algorithm 2)
        var encKey = ComputeEncryptionKey(paddedUser, oValue, permissions, fileId, keyLength, revision);

        // Compute U value (Algorithm 4/5)
        var uValue = ComputeUValue(encKey, fileId, revision);

        return new PdfEncryptor(encKey, algorithm, version, revision, oValue, uValue, permissions, fileId);
    }

    /// <summary>
    /// Build the /Encrypt dictionary to add to the trailer.
    /// </summary>
    public PdfDictionary BuildEncryptDict()
    {
        var dict = new PdfDictionary();
        dict.Set("Filter", new PdfName("Standard"));
        dict.Set("V", new PdfInteger(_version));
        dict.Set("R", new PdfInteger(_revision));
        dict.Set("P", new PdfInteger(_permissions));
        dict.Set("O", new PdfString(_oValue, isHex: true));
        dict.Set("U", new PdfString(_uValue, isHex: true));

        if (_version == 5)
        {
            // AES-256 (V5/R6)
            dict.Set("Length", new PdfInteger(256));

            dict.Set("OE", new PdfString(_oeValue!, isHex: true));
            dict.Set("UE", new PdfString(_ueValue!, isHex: true));
            dict.Set("Perms", new PdfString(_permsValue!, isHex: true));

            dict.Set("StmF", new PdfName("StdCF"));
            dict.Set("StrF", new PdfName("StdCF"));

            var cfDict = new PdfDictionary();
            var stdCF = new PdfDictionary();
            stdCF.Set("Type", new PdfName("CryptFilter"));
            stdCF.Set("CFM", new PdfName("AESV3"));
            stdCF.Set("Length", new PdfInteger(32));
            cfDict.Set("StdCF", stdCF);
            dict.Set("CF", cfDict);
        }
        else
        {
            dict.Set("Length", new PdfInteger(KeyLengthBits));

            if (_version == 4)
            {
                // AES-128: add crypt filter configuration
                dict.Set("StmF", new PdfName("StdCF"));
                dict.Set("StrF", new PdfName("StdCF"));

                var cfDict = new PdfDictionary();
                var stdCF = new PdfDictionary();
                stdCF.Set("Type", new PdfName("CryptFilter"));
                stdCF.Set("CFM", new PdfName("AESV2"));
                stdCF.Set("Length", new PdfInteger(16));
                cfDict.Set("StdCF", stdCF);
                dict.Set("CF", cfDict);
            }
        }

        return dict;
    }

    /// <summary>
    /// Encrypt a string value for the given object.
    /// </summary>
    public byte[] EncryptString(byte[] data, int objectNumber, int generation)
    {
        var key = DeriveObjectKey(objectNumber, generation);
        return EncryptData(data, key);
    }

    /// <summary>
    /// Encrypt stream data for the given object.
    /// </summary>
    public byte[] EncryptStream(byte[] data, int objectNumber, int generation)
    {
        var key = DeriveObjectKey(objectNumber, generation);
        return EncryptData(data, key);
    }

    private byte[] DeriveObjectKey(int objectNumber, int generation)
    {
        // V5 (AES-256): use the file encryption key directly — no per-object derivation
        if (_version == 5)
            return _encryptionKey;

        var isAes = _algorithm == CryptoAlgorithm.AESx128;

        var input = new byte[_encryptionKey.Length + 5 + (isAes ? 4 : 0)];
        _encryptionKey.CopyTo(input, 0);
        var offset = _encryptionKey.Length;
        input[offset] = (byte)(objectNumber & 0xFF);
        input[offset + 1] = (byte)((objectNumber >> 8) & 0xFF);
        input[offset + 2] = (byte)((objectNumber >> 16) & 0xFF);
        input[offset + 3] = (byte)(generation & 0xFF);
        input[offset + 4] = (byte)((generation >> 8) & 0xFF);

        if (isAes)
        {
            "sAlT"u8.CopyTo(input.AsSpan(offset + 5));
        }

        var hash = Md5Digest.Hash(input);
        return hash[..Math.Min(_encryptionKey.Length + 5, 16)];
    }

    private byte[] EncryptData(byte[] data, byte[] key)
    {
        if (_algorithm is CryptoAlgorithm.AESx128 or CryptoAlgorithm.AESx256)
        {
            return EncryptAesCbc(key, data);
        }

        // RC4
        return Rc4Cipher.Decrypt(key, data); // RC4 is symmetric
    }

    private static byte[] EncryptAesCbc(byte[] key, byte[] data)
    {
        var iv = CryptoRandom.GetBytes(16);
        var aes = new AesCipher(key);
        var encrypted = aes.EncryptCbc(data, iv, pkcs7Padding: true);

        // Prepend IV
        var result = new byte[16 + encrypted.Length];
        iv.CopyTo(result, 0);
        encrypted.CopyTo(result, 16);
        return result;
    }

    #region Key derivation (same algorithms as PdfDecryptor)

    private static byte[] PadPassword(string? password)
    {
        var result = new byte[32];
        if (string.IsNullOrEmpty(password))
        {
            PasswordPadding.AsSpan(0, 32).CopyTo(result);
            return result;
        }
        var pwBytes = System.Text.Encoding.Latin1.GetBytes(password);
        var len = Math.Min(pwBytes.Length, 32);
        pwBytes.AsSpan(0, len).CopyTo(result);
        PasswordPadding.AsSpan(0, 32 - len).CopyTo(result.AsSpan(len));
        return result;
    }

    private static byte[] ComputeOValue(byte[] paddedOwner, byte[] paddedUser, int keyLength, int revision)
    {
        var hash = Md5Digest.Hash(paddedOwner);
        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
                hash = Md5Digest.Hash(hash, 0, keyLength);
        }
        var key = hash[..keyLength];

        if (revision == 2)
            return Rc4Cipher.Decrypt(key, paddedUser);

        var result = Rc4Cipher.Decrypt(key, paddedUser);
        for (var i = 1; i <= 19; i++)
        {
            var tempKey = new byte[key.Length];
            for (var j = 0; j < key.Length; j++)
                tempKey[j] = (byte)(key[j] ^ i);
            result = Rc4Cipher.Decrypt(tempKey, result);
        }
        return result;
    }

    private static byte[] ComputeEncryptionKey(byte[] paddedUser, byte[] oValue,
        int permissions, byte[] fileId, int keyLength, int revision)
    {
        var md5 = new Md5Digest();
        md5.Update(paddedUser, 0, paddedUser.Length);
        md5.Update(oValue, 0, oValue.Length);

        var pBytes = BitConverter.GetBytes(permissions);
        md5.Update(pBytes, 0, 4);
        md5.Update(fileId, 0, fileId.Length);

        var hash = md5.Finish();

        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
                hash = Md5Digest.Hash(hash, 0, keyLength);
        }

        return hash[..keyLength];
    }

    private static byte[] ComputeUValue(byte[] encKey, byte[] fileId, int revision)
    {
        if (revision == 2)
        {
            return Rc4Cipher.Decrypt(encKey, PasswordPadding);
        }

        // R3+: Algorithm 5
        var md5 = new Md5Digest();
        md5.Update(PasswordPadding, 0, PasswordPadding.Length);
        md5.Update(fileId, 0, fileId.Length);
        var hash = md5.Finish();

        var encrypted = Rc4Cipher.Decrypt(encKey, hash);
        for (var i = 1; i <= 19; i++)
        {
            var tempKey = new byte[encKey.Length];
            for (var j = 0; j < encKey.Length; j++)
                tempKey[j] = (byte)(encKey[j] ^ i);
            encrypted = Rc4Cipher.Decrypt(tempKey, encrypted);
        }

        // Pad to 32 bytes
        var result = new byte[32];
        encrypted.CopyTo(result, 0);
        return result;
    }

    #endregion
}
