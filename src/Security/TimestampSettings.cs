namespace Aspose.Pdf;

/// <summary>
/// RFC 3161 Time-Stamp Authority (TSA) settings consumed by the signer
/// when embedding a timestamp token into the PKCS#7 envelope.
/// </summary>
public class TimestampSettings
{
    /// <summary>TSA service URL (e.g. http://timestamp.digicert.com).</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Optional Base64 BasicAuth value
    /// (<c>"&lt;user&gt;:&lt;pass&gt;"</c> Base64-encoded) injected as
    /// <c>Authorization: Basic &lt;value&gt;</c>.</summary>
    public string BasicAuthCredentials { get; set; } = string.Empty;

    /// <summary>Digest algorithm requested from the TSA for the
    /// timestamp's <c>messageImprint</c>.</summary>
    public DigestHashAlgorithm DigestHashAlgorithm { get; set; }
        = DigestHashAlgorithm.Sha256;

    /// <summary>Creates empty timestamp settings (no server URL, no credentials, SHA-256 digest).</summary>
    public TimestampSettings() { }

    /// <summary>Creates timestamp settings for the given TSA server URL, Base64 BasicAuth credentials and digest algorithm (SHA-256 by default). A <c>null</c> URL or credential string is stored as empty.</summary>
    public TimestampSettings(string serverUrl,
        string basicAuthCredentials,
        DigestHashAlgorithm digestHashAlgorithm = DigestHashAlgorithm.Sha256)
    {
        ServerUrl = serverUrl ?? string.Empty;
        BasicAuthCredentials = basicAuthCredentials ?? string.Empty;
        DigestHashAlgorithm = digestHashAlgorithm;
    }
}
