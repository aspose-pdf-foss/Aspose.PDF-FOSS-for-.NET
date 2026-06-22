using Aspose.Pdf.Core;

namespace Aspose.Pdf.Security;

/// <summary>
/// Handles PDF decryption for all standard security handler revisions (R2–R6).
/// Spec: PDF32000_2008 §7.6
/// </summary>
internal sealed class PdfDecryptor
{
    // Padding string used in password algorithms (Table 21, §7.6.3.3) — 32 bytes
    // Source: PDF32000_2008 §7.6.3.3 Table 21
    private static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    ];

    // sAlT bytes appended for AES mode in V4 (Algorithm 1, step e)
    private static readonly byte[] AesSalt = "sAlT"u8.ToArray();

    private readonly byte[] _encryptionKey;
    private readonly int _version;     // V value
    private readonly int _revision;    // R value
    private readonly bool _encryptMetadata;
    private readonly string _defaultStreamFilter;  // StmF
    private readonly string _defaultStringFilter;  // StrF
    private readonly bool _isOwnerAuthentication;
    private readonly bool _ownerPasswordEqualsUserPassword;

    private PdfDecryptor(byte[] encryptionKey, int version, int revision,
        bool encryptMetadata, string defaultStreamFilter, string defaultStringFilter,
        bool isOwnerAuthentication = false,
        bool ownerPasswordEqualsUserPassword = false)
    {
        _encryptionKey = encryptionKey;
        _version = version;
        _revision = revision;
        _encryptMetadata = encryptMetadata;
        _defaultStreamFilter = defaultStreamFilter;
        _defaultStringFilter = defaultStringFilter;
        _isOwnerAuthentication = isOwnerAuthentication;
        _ownerPasswordEqualsUserPassword = ownerPasswordEqualsUserPassword;
    }

    public bool EncryptMetadata => _encryptMetadata;

    /// <summary>True when the supplied password matched the /O (owner)
    /// path. False when it matched /U (user) — the default for an
    /// empty/user password.</summary>
    public bool IsOwnerAuthentication => _isOwnerAuthentication;

    /// <summary>True when the supplied password matched BOTH the user /U
    /// and owner /O entries — i.e. the file was encrypted with the same
    /// password for user and owner, so there is no effective owner password
    /// distinct from the user password.</summary>
    public bool OwnerPasswordEqualsUserPassword => _ownerPasswordEqualsUserPassword;

    /// <summary>
    /// Try to create a decryptor for the given encryption dictionary and password.
    /// Returns null if the password is incorrect.
    /// </summary>
    public static PdfDecryptor? TryCreate(PdfDictionary encryptDict, PdfArray? fileId, string password = "")
    {
        var v = (int)encryptDict.GetInt("V");
        var r = (int)encryptDict.GetInt("R");
        var keyLength = (int)encryptDict.GetInt("Length", 40) / 8; // bits → bytes

        var oBytes = GetStringBytes(encryptDict, "O");
        var uBytes = GetStringBytes(encryptDict, "U");
        var p = (int)encryptDict.GetInt("P");

        // Get file ID (first element of the /ID array in the trailer)
        var id0 = fileId is { Count: > 0 } && fileId[0] is PdfString idStr
            ? idStr.Value
            : Array.Empty<byte>();

        var encryptMetadata = true;
        if (v >= 4)
        {
            var emObj = encryptDict.Get("EncryptMetadata");
            if (emObj is PdfBoolean b) encryptMetadata = b.Value;
        }

        // Determine crypt filter names for V4+
        var stmF = "V2";
        var strF = "V2";
        if (v >= 4)
        {
            var stmFName = encryptDict.GetName("StmF") ?? "Identity";
            var strFName = encryptDict.GetName("StrF") ?? "Identity";

            // Resolve filter names through the /CF dict (StdCF → AESV2, etc.)
            var cfDict = encryptDict.Get("CF") as PdfDictionary;
            stmF = ResolveFilterName(stmFName, cfDict);
            strF = ResolveFilterName(strFName, cfDict);
        }

        if (v == 5)
        {
            // AES-256 (R5 or R6)
            return TryCreateV5(encryptDict, r, keyLength, oBytes, uBytes, password, encryptMetadata, stmF, strF);
        }

        // V1–V4: try user-path and owner-path. Test BOTH so we can detect when
        // the supplied password matches both /U and /O (file encrypted with the
        // same password for user and owner, no effective owner password).
        var passwordBytes = PadPassword(password);

        var userKey = ComputeEncryptionKey(passwordBytes, oBytes, p, id0, keyLength, r, encryptMetadata);
        bool userOk = VerifyUserPassword(userKey, uBytes, id0, r);

        var userFromOwner = RecoverUserPasswordFromOwner(passwordBytes, oBytes, keyLength, r);
        var ownerKey = ComputeEncryptionKey(userFromOwner, oBytes, p, id0, keyLength, r, encryptMetadata);
        bool ownerOk = VerifyUserPassword(ownerKey, uBytes, id0, r);

        if (userOk)
        {
            return new PdfDecryptor(userKey, v, r, encryptMetadata, stmF, strF,
                isOwnerAuthentication: false,
                ownerPasswordEqualsUserPassword: ownerOk);
        }
        if (ownerOk)
        {
            return new PdfDecryptor(ownerKey, v, r, encryptMetadata, stmF, strF,
                isOwnerAuthentication: true,
                ownerPasswordEqualsUserPassword: false);
        }

        return null; // Password incorrect
    }

    /// <summary>
    /// Decrypt a string value. The string must be a PdfString (not a PdfName).
    /// </summary>
    public byte[] DecryptString(byte[] data, int objectNumber, int generation)
    {
        var filterName = _defaultStringFilter;
        if (filterName == "Identity") return data;

        var objectKey = DeriveObjectKey(objectNumber, generation, filterName == "AESV2" || filterName == "AESV3");
        return DecryptData(data, objectKey, filterName);
    }

    /// <summary>
    /// Decrypt stream data.
    /// </summary>
    public byte[] DecryptStream(byte[] data, int objectNumber, int generation, string? cryptFilterName = null)
    {
        var filterName = cryptFilterName ?? _defaultStreamFilter;
        if (filterName == "Identity") return data;

        var objectKey = DeriveObjectKey(objectNumber, generation, filterName == "AESV2" || filterName == "AESV3");
        return DecryptData(data, objectKey, filterName);
    }

    private byte[] DeriveObjectKey(int objectNumber, int generation, bool isAes)
    {
        if (_revision >= 5)
        {
            // V5/R5+: use the file encryption key directly
            return _encryptionKey;
        }

        // Algorithm 1 (§7.6.2): per-object key derivation
        // encryption key + obj number (3 bytes LE) + gen number (2 bytes LE)
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
            AesSalt.CopyTo(input, offset + 5);
        }

        var hash = Md5Digest.Hash(input);
        var keyLen = Math.Min(_encryptionKey.Length + 5, 16);
        return hash[..keyLen];
    }

    private static byte[] DecryptData(byte[] data, byte[] key, string filterName)
    {
        return filterName switch
        {
            "V2" or "RC4" => Rc4Cipher.Decrypt(key, data),
            "AESV2" or "AESV3" => DecryptAesCbc(key, data),
            _ => data // Unknown filter — return as-is
        };
    }

    private static byte[] DecryptAesCbc(byte[] key, byte[] data)
    {
        // First 16 bytes are the IV, rest is ciphertext with PKCS#7 padding
        if (data.Length < 16) return data;

        var iv = data[..16];
        var ciphertext = data[16..];

        if (ciphertext.Length == 0 || ciphertext.Length % 16 != 0)
            return data; // Invalid AES data — return as-is

        try
        {
            var aes = new AesCipher(key);
            return aes.DecryptCbc(ciphertext, iv, pkcs7Padding: true);
        }
        catch
        {
            // Padding error — try without padding removal
            try
            {
                var aes = new AesCipher(key);
                return aes.DecryptCbc(ciphertext, iv, pkcs7Padding: false);
            }
            catch
            {
                return data;
            }
        }
    }

    #region V1–V4 Key Derivation (§7.6.3.3, §7.6.3.4)

    private static byte[] PadPassword(string password)
    {
        var result = new byte[32];
        var pwBytes = System.Text.Encoding.Latin1.GetBytes(password);
        var len = Math.Min(pwBytes.Length, 32);
        pwBytes.AsSpan(0, len).CopyTo(result);
        PasswordPadding.AsSpan(0, 32 - len).CopyTo(result.AsSpan(len));
        return result;
    }

    /// <summary>
    /// Algorithm 2 (§7.6.3.3): Compute file encryption key.
    /// </summary>
    private static byte[] ComputeEncryptionKey(byte[] paddedPassword, byte[] oValue,
        int permissions, byte[] fileId, int keyLength, int revision, bool encryptMetadata)
    {
        var md5 = new Md5Digest();
        md5.Update(paddedPassword, 0, paddedPassword.Length);
        md5.Update(oValue, 0, oValue.Length);

        var pBytes = new byte[4];
        pBytes[0] = (byte)(permissions & 0xFF);
        pBytes[1] = (byte)((permissions >> 8) & 0xFF);
        pBytes[2] = (byte)((permissions >> 16) & 0xFF);
        pBytes[3] = (byte)((permissions >> 24) & 0xFF);
        md5.Update(pBytes, 0, 4);

        md5.Update(fileId, 0, fileId.Length);

        // R4+: if metadata is not encrypted, add 4 bytes of 0xFF
        if (revision >= 4 && !encryptMetadata)
        {
            var ff = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            md5.Update(ff, 0, 4);
        }

        var hash = md5.Finish();

        // R3+: iterate MD5 50 times
        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = Md5Digest.Hash(hash, 0, keyLength);
            }
        }

        return hash[..keyLength];
    }

    /// <summary>
    /// Algorithm 6 (R2) / Algorithm 7 (R3+): Verify user password.
    /// </summary>
    private static bool VerifyUserPassword(byte[] key, byte[] uValue, byte[] fileId, int revision)
    {
        if (revision == 2)
        {
            // Algorithm 4: encrypt padding with RC4 using key
            var expected = Rc4Cipher.Decrypt(key, PasswordPadding);
            return expected.AsSpan().SequenceEqual(uValue.AsSpan(0, Math.Min(uValue.Length, 32)));
        }

        // Algorithm 5 (R3+):
        // 1. MD5(padding + fileId)
        var md5 = new Md5Digest();
        md5.Update(PasswordPadding, 0, PasswordPadding.Length);
        md5.Update(fileId, 0, fileId.Length);
        var hash = md5.Finish();

        // 2. Encrypt with RC4 using key
        var encrypted = Rc4Cipher.Decrypt(key, hash);

        // 3. Iterate 19 more times with modified key
        for (var i = 1; i <= 19; i++)
        {
            var tempKey = new byte[key.Length];
            for (var j = 0; j < key.Length; j++)
                tempKey[j] = (byte)(key[j] ^ i);
            encrypted = Rc4Cipher.Decrypt(tempKey, encrypted);
        }

        // Compare first 16 bytes
        return encrypted.AsSpan(0, 16).SequenceEqual(uValue.AsSpan(0, 16));
    }

    /// <summary>
    /// Algorithm 7 (§7.6.3.4): Recover user password from owner password.
    /// </summary>
    private static byte[] RecoverUserPasswordFromOwner(byte[] paddedOwnerPassword,
        byte[] oValue, int keyLength, int revision)
    {
        var hash = Md5Digest.Hash(paddedOwnerPassword);

        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
                hash = Md5Digest.Hash(hash, 0, keyLength);
        }

        var key = hash[..keyLength];

        if (revision == 2)
        {
            return Rc4Cipher.Decrypt(key, oValue);
        }

        // R3+: decrypt 20 times with modified keys (in reverse)
        var result = (byte[])oValue.Clone();
        for (var i = 19; i >= 0; i--)
        {
            var tempKey = new byte[key.Length];
            for (var j = 0; j < key.Length; j++)
                tempKey[j] = (byte)(key[j] ^ i);
            result = Rc4Cipher.Decrypt(tempKey, result);
        }

        return result;
    }

    #endregion

    #region V5 (AES-256) Key Derivation (§7.6.3.3.3, §7.6.3.4.1)

    private static PdfDecryptor? TryCreateV5(PdfDictionary encryptDict, int revision,
        int keyLength, byte[] oBytes, byte[] uBytes, string password,
        bool encryptMetadata, string stmF, string strF)
    {
        // UTF-8 password, truncated to 127 bytes
        var pwBytes = System.Text.Encoding.UTF8.GetBytes(password);
        if (pwBytes.Length > 127)
            pwBytes = pwBytes[..127];

        // V5: try both paths to detect owner == user
        var userKey = TryUserPasswordV5(pwBytes, uBytes, encryptDict, revision);
        var ownerKey = TryOwnerPasswordV5(pwBytes, oBytes, uBytes, encryptDict, revision);

        if (userKey is not null)
            return new PdfDecryptor(userKey, 5, revision, encryptMetadata, stmF, strF,
                isOwnerAuthentication: false,
                ownerPasswordEqualsUserPassword: ownerKey is not null);
        if (ownerKey is not null)
            return new PdfDecryptor(ownerKey, 5, revision, encryptMetadata, stmF, strF,
                isOwnerAuthentication: true,
                ownerPasswordEqualsUserPassword: false);

        return null;
    }

    private static byte[]? TryUserPasswordV5(byte[] password, byte[] uBytes,
        PdfDictionary encryptDict, int revision)
    {
        if (uBytes.Length < 48) return null;

        // U = hash(32 bytes) + validation salt(8 bytes) + key salt(8 bytes)
        var validationSalt = uBytes[32..40];
        var keySalt = uBytes[40..48];

        // Verify: hash(password + validationSalt) == U[0.32]
        byte[] hash;
        if (revision == 5)
        {
            hash = ShaDigest.Sha256(CryptoHelper.ConcatBytes(password, validationSalt));
        }
        else // R6
        {
            hash = CryptoHelper.ComputeHashR6(password, validationSalt, Array.Empty<byte>());
        }

        if (!hash.AsSpan(0, 32).SequenceEqual(uBytes.AsSpan(0, 32)))
            return null;

        // Derive file encryption key using key salt
        byte[] keyHash;
        if (revision == 5)
        {
            keyHash = ShaDigest.Sha256(CryptoHelper.ConcatBytes(password, keySalt));
        }
        else // R6
        {
            keyHash = CryptoHelper.ComputeHashR6(password, keySalt, Array.Empty<byte>());
        }

        // Decrypt /UE with AES-256 CBC using keyHash as key, zero IV
        var ueBytes = GetStringBytes(encryptDict, "UE");
        if (ueBytes.Length < 32) return null;

        return CryptoHelper.DecryptAes256NoIv(keyHash, ueBytes[..32]);
    }

    private static byte[]? TryOwnerPasswordV5(byte[] password, byte[] oBytes, byte[] uBytes,
        PdfDictionary encryptDict, int revision)
    {
        if (oBytes.Length < 48) return null;

        var validationSalt = oBytes[32..40];
        var keySalt = oBytes[40..48];

        // Verify: hash(password + validationSalt + U[0.48]) == O[0.32]
        var u48 = uBytes.Length >= 48 ? uBytes[..48] : uBytes;
        byte[] hash;
        if (revision == 5)
        {
            hash = ShaDigest.Sha256(CryptoHelper.ConcatBytes(password, validationSalt, u48));
        }
        else // R6
        {
            hash = CryptoHelper.ComputeHashR6(password, validationSalt, u48);
        }

        if (!hash.AsSpan(0, 32).SequenceEqual(oBytes.AsSpan(0, 32)))
            return null;

        // Derive key using key salt
        byte[] keyHash;
        if (revision == 5)
        {
            keyHash = ShaDigest.Sha256(CryptoHelper.ConcatBytes(password, keySalt, u48));
        }
        else // R6
        {
            keyHash = CryptoHelper.ComputeHashR6(password, keySalt, u48);
        }

        // Decrypt /OE
        var oeBytes = GetStringBytes(encryptDict, "OE");
        if (oeBytes.Length < 32) return null;

        return CryptoHelper.DecryptAes256NoIv(keyHash, oeBytes[..32]);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Resolve a crypt filter name to its cipher method (AESV2, AESV3, V2, Identity).
    /// For V4+, /StmF and /StrF may be named filters like "StdCF" that reference the /CF dict.
    /// </summary>
    private static string ResolveFilterName(string filterName, PdfDictionary? cfDict)
    {
        if (filterName == "Identity" || cfDict is null) return filterName;

        // Look up the named filter in the CF dict
        var filterEntry = cfDict.Get(filterName) as PdfDictionary;
        if (filterEntry is null) return filterName;

        // /CFM specifies the cipher method: AESV2, AESV3, V2, None
        var cfm = filterEntry.GetName("CFM") ?? "None";
        return cfm == "None" ? "Identity" : cfm;
    }

    private static byte[] GetStringBytes(PdfDictionary dict, string key)
    {
        var obj = dict.Get(key);
        return obj is PdfString s ? s.Value : Array.Empty<byte>();
    }

    #endregion
}
