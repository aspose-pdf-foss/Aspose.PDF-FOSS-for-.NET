
namespace Aspose.Pdf.Security;

internal static partial class CmsBuilder
{
    /// <summary>The signed-attribute read and the per-certificate check of a detached CMS signature: the platform key for DSA/ECDSA, the hand-rolled PKCS#1 v1.5 recovery for RSA.</summary>
    private static bool VerifySignerCert(byte[] certDer, byte[] expectedHash, byte[] sig)
    {
        // Extract public key from certificate
        var cert = new Asn1Reader(certDer).ReadSequence();
        var tbs = cert.ReadSequence();
        tbs.TryReadContextConstructed(0); // version
        tbs.Skip(); // serial
        tbs.Skip(); // signature algo
        tbs.Skip(); // issuer
        tbs.Skip(); // validity
        tbs.Skip(); // subject
        var spki = tbs.ReadSequence(); // SubjectPublicKeyInfo
        var spkiAlgo = spki.ReadSequence(); // AlgorithmIdentifier
        var keyAlgOid = spkiAlgo.ReadOid();

        // DSA / ECDSA: delegate the signature check to the platform key.
        if (keyAlgOid == OidEcPublicKey || keyAlgOid == OidDsa)
        {
            if (VerifyDsaOrEcdsa(certDer, keyAlgOid, expectedHash, sig))
                return true;
            return false;
        }

        var pubKeyBits = spki.ReadBitString();

        // Parse RSA public key
        var pkReader = new Asn1Reader(pubKeyBits).ReadSequence();
        var modulus = pkReader.ReadIntegerBytes();
        var exponent = pkReader.ReadIntegerBytes();

        // RSA verify: sig^e mod n, then check PKCS#1 v1.5 padding
        var n = Compat.BigIntegerFromUnsignedBigEndian(modulus);
        var e = Compat.BigIntegerFromUnsignedBigEndian(exponent);
        var s = Compat.BigIntegerFromUnsignedBigEndian(sig);
        var mVal = System.Numerics.BigInteger.ModPow(s, e, n);

        var decrypted = Compat.ToUnsignedBigEndian(mVal);
        if (decrypted.Length < modulus.Length)
        {
            var padded = new byte[modulus.Length];
            Array.Copy(decrypted, 0, padded, modulus.Length - decrypted.Length, decrypted.Length);
            decrypted = padded;
        }

        // Verify PKCS#1 v1.5: 00 01 FF.FF 00 DigestInfo
        if (decrypted.Length < 11 || decrypted[0] != 0x00 || decrypted[1] != 0x01)
            return false; // Try next cert

        var i = 2;
        while (i < decrypted.Length && decrypted[i] == 0xFF) i++;
        if (i >= decrypted.Length || decrypted[i] != 0x00) return false;
        i++;

        var digestInfo = decrypted[i..];
        var diReader = new Asn1Reader(digestInfo).ReadSequence();
        var algoSeq = diReader.ReadSequence();
        var hashOid = algoSeq.ReadOid();
        var recoveredHash = diReader.ReadOctetString();

        if (recoveredHash.AsSpan().SequenceEqual(expectedHash))
            return true;
        return false;
    }

    /// <summary></summary>
    private static (byte[] signedAttrsForHash, byte[]? messageDigest) ReadSignedAttributes(Asn1Reader si)
    {
        byte[]? signedAttrsForHash = default;
        byte[]? messageDigest = default;
        messageDigest = null;
        // Read the raw [0] TLV (for re-encoding as SET)
        var signedAttrsRaw = si.ReadRawTlv();
        // Re-encode as SET (0x31) instead of context [0] (0xA0) for hashing
        signedAttrsForHash = (byte[])signedAttrsRaw.Clone();
        signedAttrsForHash[0] = 0x31;

        // Parse attributes to find message-digest
        var attrsReader = new Asn1Reader(signedAttrsRaw);
        var attrs = attrsReader.ReadContextConstructed(0);
        while (attrs.HasData)
        {
            var attr = attrs.ReadSequence();
            var attrOid = attr.ReadOid();
            if (attrOid == "1.2.840.113549.1.9.4") // messageDigest
            {
                var attrValues = attr.ReadSet();
                messageDigest = attrValues.ReadOctetString();
            }
        }
        return (signedAttrsForHash, messageDigest);
    }
}
