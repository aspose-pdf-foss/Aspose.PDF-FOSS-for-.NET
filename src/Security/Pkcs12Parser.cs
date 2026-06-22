namespace Aspose.Pdf.Security;

/// <summary>
/// Parses PKCS#12/PFX files to extract X.509 certificates and RSA private keys.
/// Supports pbeWithSHA1And3-KeyTripleDES-CBC and PBES2/AES encryption.
/// </summary>
internal static class Pkcs12Parser
{
    private const string OidData = "1.2.840.113549.1.7.1";
    private const string OidEncryptedData = "1.2.840.113549.1.7.6";
    private const string OidCertBag = "1.2.840.113549.1.12.10.1.3";
    private const string OidKeyBag = "1.2.840.113549.1.12.10.1.1";
    private const string OidPkcs8ShroudedKeyBag = "1.2.840.113549.1.12.10.1.2";
    private const string OidX509Certificate = "1.2.840.113549.1.9.22.1";
    private const string OidPbeWithSha1And3Des = "1.2.840.113549.1.12.1.3";
    private const string OidPbeWithSha1And40Rc2 = "1.2.840.113549.1.12.1.6";
    private const string OidPbes2 = "1.2.840.113549.1.5.13";
    private const string OidPbkdf2 = "1.2.840.113549.1.5.12";
    private const string OidAes256Cbc = "2.16.840.1.101.3.4.1.42";
    private const string OidAes128Cbc = "2.16.840.1.101.3.4.1.2";
    private const string OidHmacSha256 = "1.2.840.113549.2.9";

    /// <summary>Parse PFX and extract the first certificate (DER) and RSA private key.</summary>
    public static (byte[] certificateDer, RsaKey privateKey) Parse(byte[] pfxData, string password)
    {
        var r = new Asn1Reader(pfxData).ReadSequence();
        r.ReadInteger(); // version (3)
        var authSafe = ReadContentInfo(r);

        // authSafe is a SEQUENCE of ContentInfo
        var safeContents = new Asn1Reader(authSafe).ReadSequence();
        byte[]? certDer = null;
        RsaKey? key = null;

        while (safeContents.HasData)
        {
            var ci = safeContents.ReadSequence();
            var contentType = ci.ReadOid();
            var content = ci.ReadContextConstructed(0);

            if (contentType == OidData)
            {
                // Unencrypted SafeContents (or contains encrypted bags)
                var octetData = content.ReadOctetString();
                ParseSafeBags(octetData, password, ref certDer, ref key);
            }
            else if (contentType == OidEncryptedData)
            {
                // Encrypted SafeContents
                var encData = content.ReadSequence();
                encData.ReadInteger(); // version
                var encContentInfo = encData.ReadSequence();
                encContentInfo.ReadOid(); // contentType (data)
                var algoSeq = encContentInfo.ReadSequence();
                var algoOid = algoSeq.ReadOid();

                byte[] decrypted;
                if (algoOid == OidPbeWithSha1And3Des || algoOid == OidPbeWithSha1And40Rc2)
                {
                    var pbeParams = algoSeq.ReadSequence();
                    var salt = pbeParams.ReadOctetString();
                    var iterations = pbeParams.ReadInteger();
                    var ciphertext = ReadContextImplicit(encContentInfo, 0);

                    var pwBytes = HmacSha.Pkcs12EncodePassword(password);
                    if (algoOid == OidPbeWithSha1And3Des)
                    {
                        var desKey = HmacSha.Pkcs12Kdf(pwBytes, salt, iterations, 24, 1);
                        var desIv = HmacSha.Pkcs12Kdf(pwBytes, salt, iterations, 8, 2);
                        decrypted = DesCipher.TripleDesDecryptCbc(desKey, desIv, ciphertext);
                    }
                    else // RC2-40
                    {
                        var rc2Key = HmacSha.Pkcs12Kdf(pwBytes, salt, iterations, 5, 1);
                        var rc2Iv = HmacSha.Pkcs12Kdf(pwBytes, salt, iterations, 8, 2);
                        var rc2 = new Rc2Cipher(rc2Key, 40);
                        decrypted = rc2.DecryptCbc(ciphertext, rc2Iv);
                    }
                }
                else if (algoOid == OidPbes2)
                {
                    decrypted = DecryptPbes2(algoSeq, encContentInfo, password);
                }
                else continue;

                ParseSafeBags(decrypted, password, ref certDer, ref key);
            }
        }

        return (certDer ?? throw new InvalidOperationException("No certificate found in PFX"),
                key ?? throw new InvalidOperationException("No private key found in PFX"));
    }

    private static void ParseSafeBags(byte[] data, string password, ref byte[]? certDer, ref RsaKey? key)
    {
        var bags = new Asn1Reader(data).ReadSequence();
        while (bags.HasData)
        {
            var bag = bags.ReadSequence();
            var bagType = bag.ReadOid();
            var bagValue = bag.ReadContextConstructed(0);

            if (bagType == OidCertBag)
            {
                var certBag = bagValue.ReadSequence();
                var certType = certBag.ReadOid();
                if (certType == OidX509Certificate)
                {
                    var certData = certBag.ReadContextConstructed(0);
                    certDer ??= certData.ReadOctetString();
                }
            }
            else if (bagType == OidPkcs8ShroudedKeyBag)
            {
                // Encrypted PKCS#8 private key
                var encKeyInfo = new Asn1Reader(bagValue.ReadRawTlv());
                key ??= DecryptPkcs8ShroudedKey(encKeyInfo, password);
            }
            else if (bagType == OidKeyBag)
            {
                // Unencrypted PKCS#8 private key
                key ??= RsaKey.FromPkcs8(bagValue.ReadRawTlv());
            }
        }
    }

    private static RsaKey DecryptPkcs8ShroudedKey(Asn1Reader reader, string password)
    {
        var seq = reader.ReadSequence();
        var algoSeq = seq.ReadSequence();
        var algoOid = algoSeq.ReadOid();
        var ciphertext = seq.ReadOctetString();

        byte[] pkcs8Der;
        if (algoOid == OidPbeWithSha1And3Des)
        {
            var pbeParams = algoSeq.ReadSequence();
            var salt = pbeParams.ReadOctetString();
            var iterations = pbeParams.ReadInteger();

            var pwBytes = HmacSha.Pkcs12EncodePassword(password);
            var desKey = HmacSha.Pkcs12Kdf(pwBytes, salt, iterations, 24, 1);
            var desIv = HmacSha.Pkcs12Kdf(pwBytes, salt, iterations, 8, 2);
            pkcs8Der = DesCipher.TripleDesDecryptCbc(desKey, desIv, ciphertext);
        }
        else if (algoOid == OidPbes2)
        {
            pkcs8Der = DecryptPbes2Data(algoSeq, ciphertext, password);
        }
        else
        {
            throw new NotSupportedException($"Unsupported PKCS#8 encryption: {algoOid}");
        }

        return RsaKey.FromPkcs8(pkcs8Der);
    }

    private static byte[] DecryptPbes2(Asn1Reader algoSeq, Asn1Reader encContentInfo, string password)
    {
        var ciphertext = ReadContextImplicit(encContentInfo, 0);
        return DecryptPbes2Data(algoSeq, ciphertext, password);
    }

    private static byte[] DecryptPbes2Data(Asn1Reader algoSeq, byte[] ciphertext, string password)
    {
        var pbes2Params = algoSeq.ReadSequence();
        var kdfSeq = pbes2Params.ReadSequence();
        var kdfOid = kdfSeq.ReadOid(); // must be PBKDF2
        var kdfParams = kdfSeq.ReadSequence();
        var salt = kdfParams.ReadOctetString();
        var iterations = kdfParams.ReadInteger();
        var keyLength = kdfParams.HasData && kdfParams.PeekTag() == 0x02 ? kdfParams.ReadInteger() : 0;

        // PRF (optional — defaults to HMAC-SHA1)
        var useSha256 = false;
        if (kdfParams.HasData && kdfParams.PeekTag() == 0x30)
        {
            var prfSeq = kdfParams.ReadSequence();
            var prfOid = prfSeq.ReadOid();
            useSha256 = prfOid == OidHmacSha256;
        }

        var encSeq = pbes2Params.ReadSequence();
        var encOid = encSeq.ReadOid();
        var iv = encSeq.ReadOctetString();

        // Determine key length from cipher
        if (keyLength == 0)
            keyLength = encOid == OidAes256Cbc ? 32 : 16;

        var pwBytes = System.Text.Encoding.UTF8.GetBytes(password);
        var derivedKey = useSha256
            ? HmacSha.Pbkdf2Sha256(pwBytes, salt, iterations, keyLength)
            : HmacSha.Pbkdf2Sha1(pwBytes, salt, iterations, keyLength);

        var aes = new AesCipher(derivedKey);
        return aes.DecryptCbc(ciphertext, iv, pkcs7Padding: true);
    }

    private static byte[] ReadContentInfo(Asn1Reader r)
    {
        var ci = r.ReadSequence();
        var contentType = ci.ReadOid();
        var content = ci.ReadContextConstructed(0);
        return content.ReadOctetString();
    }

    private static byte[] ReadContextImplicit(Asn1Reader r, int n)
    {
        var (tag, value) = r.ReadTlv();
        return value;
    }
}
