using System.Net.Http;
using System.Net.Http.Headers;

namespace Aspose.Pdf.Security;

/// <summary>
/// RFC 3161 Time-Stamp Protocol client and token verifier. Builds a
/// <c>TimeStampReq</c> over a message imprint, POSTs it to a TSA, and returns
/// the DER <c>TimeStampToken</c> (a CMS ContentInfo) from the reply — the value
/// written into an <c>ETSI.RFC3161</c> document-timestamp signature's /Contents.
/// Verification checks both the TSA's CMS signature and that the token's
/// message imprint matches the timestamped bytes. No dependency on
/// <c>System.Security.Cryptography.Pkcs</c>; ASN.1 goes through
/// <see cref="Asn1Writer"/> / <see cref="Asn1Reader"/>.
/// </summary>
internal static class Rfc3161
{
    private const string OidSignedData = "1.2.840.113549.1.7.2";
    private const string OidTstInfo = "1.2.840.113549.1.9.16.1.4";

    private const string OidSha1 = "1.3.14.3.2.26";
    private const string OidSha256 = "2.16.840.1.101.3.4.2.1";
    private const string OidSha384 = "2.16.840.1.101.3.4.2.2";
    private const string OidSha512 = "2.16.840.1.101.3.4.2.3";

    // One shared client — HttpClient is designed to be reused across requests.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(100) };

    /// <summary>Request an RFC 3161 timestamp token over
    /// <paramref name="imprint"/> (already hashed with <paramref name="digest"/>)
    /// from the TSA at <paramref name="url"/>. Returns the DER TimeStampToken
    /// (ContentInfo). Throws when the URL is empty, the HTTP request fails, or
    /// the TSA rejects the request.</summary>
    public static byte[] RequestTimestampToken(string url, string? basicAuth,
        DigestHashAlgorithm digest, byte[] imprint)
    {
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException("Timestamp server URL is empty.");

        var reqDer = BuildRequest(digest, imprint);
        using var content = new ByteArrayContent(reqDer);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrEmpty(basicAuth))
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        using var response = Http.Send(message, HttpCompletionOption.ResponseContentRead);
        response.EnsureSuccessStatusCode();

        byte[] respBytes;
        using (var stream = response.Content.ReadAsStream())
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            respBytes = buffer.ToArray();
        }
        return ExtractToken(respBytes);
    }

    /// <summary>Verify a timestamp token: its TSA CMS signature is valid and its
    /// message imprint equals the digest of <paramref name="timestampedData"/>.</summary>
    public static bool VerifyToken(byte[] token, byte[] timestampedData)
    {
        try
        {
            var tstInfo = ExtractEContent(token);
            if (tstInfo is null) return false;

            // The TSA's CMS signature covers the eContent (TSTInfo). VerifyDetached
            // hashes the passed content for the messageDigest attribute and verifies
            // the signer's signature over the signed attributes — exactly the check
            // needed here with the eContent supplied as the "detached" content.
            if (!CmsBuilder.VerifyDetached(tstInfo, token)) return false;

            if (!TryReadMessageImprint(tstInfo, out var hashOid, out var hashedMessage))
                return false;
            var actual = HashByOid(hashOid, timestampedData);
            return actual.AsSpan().SequenceEqual(hashedMessage);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Read the message-imprint digest algorithm from a timestamp token
    /// (the content hash the timestamp covers). Returns false on any parse
    /// failure.</summary>
    public static bool TryGetContentHashAlgorithm(byte[] token, out DigestHashAlgorithm digest)
    {
        digest = DigestHashAlgorithm.Sha256;
        try
        {
            var tstInfo = ExtractEContent(token);
            if (tstInfo is null) return false;
            if (!TryReadMessageImprint(tstInfo, out var hashOid, out _)) return false;
            digest = MapDigest(hashOid);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Request building ───────────────────────────────────────────────

    private static byte[] BuildRequest(DigestHashAlgorithm digest, byte[] imprint)
    {
        // TimeStampReq ::= SEQUENCE {
        //   version INTEGER { v1(1) },
        //   messageImprint MessageImprint,
        //   reqPolicy TSAPolicyId OPTIONAL,          -- omitted
        //   nonce INTEGER OPTIONAL,                   -- random, for freshness
        //   certReq BOOLEAN DEFAULT FALSE }           -- TRUE: embed TSA cert
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        var w = new Asn1Writer();
        w.WriteSequence(req =>
        {
            req.WriteInteger(1);
            req.WriteSequence(mi =>
            {
                mi.WriteSequence(alg =>
                {
                    alg.WriteOid(DigestOid(digest));
                    alg.WriteNull();
                });
                mi.WriteOctetString(imprint);
            });
            req.WriteIntegerBytes(nonce);
            req.WriteBoolean(true);
        });
        return w.ToArray();
    }

    /// <summary>Pull the DER TimeStampToken (ContentInfo) out of a TimeStampResp,
    /// validating the PKIStatus.</summary>
    private static byte[] ExtractToken(byte[] response)
    {
        // TimeStampResp ::= SEQUENCE { status PKIStatusInfo, timeStampToken ContentInfo OPTIONAL }
        var resp = new Asn1Reader(response).ReadSequence();
        var statusInfo = resp.ReadSequence();           // PKIStatusInfo
        var status = statusInfo.ReadInteger();          // 0 granted, 1 grantedWithMods
        if (status != 0 && status != 1)
            throw new InvalidOperationException($"TSA rejected the timestamp request (PKIStatus {status}).");
        if (!resp.HasData)
            throw new InvalidOperationException("TSA reply contains no timestamp token.");
        return resp.ReadRawTlv();                        // ContentInfo (the token)
    }

    // ── Token parsing ──────────────────────────────────────────────────

    /// <summary>Extract the eContent (DER TSTInfo octets) from a TimeStampToken's
    /// SignedData.encapContentInfo, or null when it is absent.</summary>
    private static byte[]? ExtractEContent(byte[] token)
    {
        var contentInfo = new Asn1Reader(token).ReadSequence();
        if (contentInfo.ReadOid() != OidSignedData) return null;
        var signedData = contentInfo.ReadContextConstructed(0).ReadSequence();
        signedData.ReadInteger();                 // version
        signedData.ReadSet();                     // digestAlgorithms
        var encap = signedData.ReadSequence();    // encapContentInfo
        encap.ReadOid();                          // eContentType (id-ct-TSTInfo)
        var eContent = encap.TryReadContextConstructed(0); // [0] EXPLICIT
        return eContent is null || !eContent.HasData ? null : eContent.ReadOctetString();
    }

    private static bool TryReadMessageImprint(byte[] tstInfo, out string hashOid, out byte[] hashedMessage)
    {
        hashOid = string.Empty;
        hashedMessage = [];
        var tst = new Asn1Reader(tstInfo).ReadSequence();
        tst.ReadInteger();                        // version
        tst.Skip();                               // policy (TSAPolicyId OID)
        var messageImprint = tst.ReadSequence();  // MessageImprint
        hashOid = messageImprint.ReadSequence().ReadOid();
        hashedMessage = messageImprint.ReadOctetString();
        return hashOid.Length > 0 && hashedMessage.Length > 0;
    }

    // ── Digest helpers ─────────────────────────────────────────────────

    private static string DigestOid(DigestHashAlgorithm digest) => digest switch
    {
        DigestHashAlgorithm.Sha1 => OidSha1,
        DigestHashAlgorithm.Sha384 => OidSha384,
        DigestHashAlgorithm.Sha512 => OidSha512,
        _ => OidSha256,
    };

    private static DigestHashAlgorithm MapDigest(string oid) => oid switch
    {
        OidSha1 => DigestHashAlgorithm.Sha1,
        OidSha384 => DigestHashAlgorithm.Sha384,
        OidSha512 => DigestHashAlgorithm.Sha512,
        _ => DigestHashAlgorithm.Sha256,
    };

    private static byte[] HashByOid(string oid, byte[] data) => oid switch
    {
        OidSha1 => HmacSha.Sha1Hash(data),
        OidSha384 => ShaDigest.Sha384(data),
        OidSha512 => ShaDigest.Sha512(data),
        _ => ShaDigest.Sha256(data),
    };
}
