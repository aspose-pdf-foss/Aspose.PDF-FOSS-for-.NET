using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

/// <summary>
/// PDF public-key (certificate) security handler — Adobe.PPKLite, ISO 32000-1 §7.6.5.
/// Encrypts the file key to a set of recipient certificates via CMS EnvelopedData
/// (<see cref="PubSecEnvelope"/>) and derives the file key from a shared random seed.
/// Object-level RC4/AES encryption reuses <see cref="PdfEncryptor"/>/<see cref="PdfDecryptor"/>.
/// </summary>
internal static class PubSecHandler
{
    // keyLen (bytes), hash is SHA-256 (else SHA-1), crypt-filter method, /V, /R
    private static (int keyLen, bool sha256, string cfm, int version, int revision) Info(CryptoAlgorithm a)
        => a switch
        {
            CryptoAlgorithm.RC4x40  => (5,  false, "V2",    4, 4),
            CryptoAlgorithm.RC4x128 => (16, false, "V2",    4, 4),
            CryptoAlgorithm.AESx128 => (16, false, "AESV2", 4, 4),
            CryptoAlgorithm.AESx256 => (32, true,  "AESV3", 5, 6),
            _                       => (16, false, "AESV2", 4, 4),
        };

    /// <summary>Build an encryptor that encrypts the document to the given recipient
    /// certificates with the requested algorithm and permissions.</summary>
    public static PdfEncryptor CreateEncryptor(Aspose.Pdf.Permissions permissions,
        CryptoAlgorithm algo, IList<X509Certificate2> certificates)
    {
        if (certificates is null || certificates.Count == 0)
            throw new ArgumentException("At least one recipient certificate is required.", nameof(certificates));

        var (keyLen, sha256, cfm, version, revision) = Info(algo);

        // Enveloped content = 20-byte seed + 4-byte permissions (big-endian).
        var seed = CryptoRandom.GetBytes(20);
        var p = (int)permissions;
        var content = new byte[24];
        Array.Copy(seed, content, 20);
        content[20] = (byte)((p >> 24) & 0xFF);
        content[21] = (byte)((p >> 16) & 0xFF);
        content[22] = (byte)((p >> 8) & 0xFF);
        content[23] = (byte)(p & 0xFF);

        var recipients = new List<byte[]>(certificates.Count);
        foreach (var cert in certificates)
            recipients.Add(PubSecEnvelope.Build(content, cert));

        var fileKey = ComputeFileKey(seed, recipients, sha256, keyLen);
        var dict = BuildEncryptDict(cfm, version, keyLen, recipients);
        return PdfEncryptor.CreateWithFileKey(fileKey, algo, version, revision, dict);
    }

    /// <summary>Open a certificate-encrypted document: recover the seed from a
    /// recipient envelope with the private key, derive the file key, attach a
    /// decryptor to <paramref name="reader"/>, and return the permissions.</summary>
    public static int Open(PdfReader reader, PdfDictionary encryptDict, CertificateEncryptionOptions options)
    {
        var privateKey = LoadPrivateKey(options);

        var version = (int)encryptDict.GetInt("V");

        // Two on-disk layouts: modern (/V 4-5) keeps /Recipients inside a crypt
        // filter (/CF); legacy RC4 (/V 1-2) keeps /Recipients at the top level with
        // no crypt filter (the method is implicitly RC4 = V2).
        var cf = reader.ResolveDict(encryptDict.Get("CF"));
        PdfDictionary? cfDict = null;
        var cfm = "V2";
        if (cf is not null) (cfDict, cfm) = ResolveCryptFilter(reader, cf);

        // Key length is fixed by the method for AES; RC4 takes it from /Length (bits).
        var keyLen = cfm switch
        {
            "AESV3" => 32,
            "AESV2" => 16,
            _ => (int)encryptDict.GetInt("Length", 40) / 8,
        };

        var recipientsSource = cfDict ?? encryptDict;
        var recipients = new List<byte[]>();
        if (reader.Resolve(recipientsSource.Get("Recipients")) is PdfArray recArr)
            foreach (var item in recArr)
                if (reader.Resolve(item) is PdfString s) recipients.Add(s.Value);

        byte[]? content = null;
        foreach (var blob in recipients)
        {
            content = PubSecEnvelope.TryDecrypt(blob, privateKey);
            if (content is not null) break;
        }
        if (content is null || content.Length < 24)
            throw new InvalidOperationException(
                "Public-key decryption failed: no recipient matched the supplied private key.");

        var seed = content[..20];
        var perms = (content[20] << 24) | (content[21] << 16) | (content[22] << 8) | content[23];
        var sha256 = cfm == "AESV3";
        var fileKey = ComputeFileKey(seed, recipients, sha256, keyLen);
        var revision = version == 5 ? 6 : 4;

        reader.AttachDecryptor(PdfDecryptor.CreateWithFileKey(fileKey, version, revision, cfm));
        return perms;
    }

    private static byte[] ComputeFileKey(byte[] seed, List<byte[]> recipients, bool sha256, int keyLen)
    {
        // §7.6.5.2: hash(seed || bytes of each recipient item), truncated to key length.
        var total = seed.Length;
        foreach (var r in recipients) total += r.Length;
        var buf = new byte[total];
        var off = 0;
        Array.Copy(seed, 0, buf, off, seed.Length); off += seed.Length;
        foreach (var r in recipients) { Array.Copy(r, 0, buf, off, r.Length); off += r.Length; }

        var hash = sha256 ? ShaDigest.Sha256(buf) : HmacSha.Sha1Hash(buf);
        return hash[..keyLen];
    }

    private static PdfDictionary BuildEncryptDict(string cfm, int version, int keyLen, List<byte[]> recipients)
    {
        var recArr = new PdfArray();
        foreach (var r in recipients) recArr.Add(new PdfString(r, isHex: true));

        var cf0 = new PdfDictionary();
        cf0.Set("Type", new PdfName("CryptFilter"));
        cf0.Set("CFM", new PdfName(cfm));
        cf0.Set("Length", new PdfInteger(keyLen));
        cf0.Set("Recipients", recArr);

        var cf = new PdfDictionary();
        cf.Set("DefaultCryptFilter", cf0);

        var dict = new PdfDictionary();
        dict.Set("Filter", new PdfName("Adobe.PPKLite"));
        dict.Set("V", new PdfInteger(version));
        dict.Set("Length", new PdfInteger(keyLen * 8));
        dict.Set("CF", cf);
        dict.Set("StmF", new PdfName("DefaultCryptFilter"));
        dict.Set("StrF", new PdfName("DefaultCryptFilter"));
        return dict;
    }

    private static (PdfDictionary dict, string cfm) ResolveCryptFilter(PdfReader reader, PdfDictionary? cf)
    {
        if (cf is not null)
        {
            // Prefer the filter named by StmF (DefaultCryptFilter), else the first entry.
            foreach (var name in new[] { "DefaultCryptFilter", "StdCF" })
                if (reader.ResolveDict(cf.Get(name)) is PdfDictionary d)
                    return (d, d.GetName("CFM") ?? "AESV2");
            foreach (var key in cf.Keys)
                if (reader.ResolveDict(cf.Get(key)) is PdfDictionary d)
                    return (d, d.GetName("CFM") ?? "AESV2");
        }
        throw new InvalidOperationException("Public-key /Encrypt dictionary has no crypt filter.");
    }

    private static RsaKey LoadPrivateKey(CertificateEncryptionOptions options)
    {
        if (options.PfxPath is not null)
        {
            var pfx = File.ReadAllBytes(options.PfxPath);
            return Pkcs12Parser.Parse(pfx, options.PfxPassword ?? string.Empty).privateKey;
        }
        throw new NotSupportedException(
            "Public-key decryption from a certificate store is not supported; supply a PFX private key.");
    }
}
