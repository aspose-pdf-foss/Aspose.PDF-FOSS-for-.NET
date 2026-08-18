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

    public TimestampSettings() { }

    public TimestampSettings(string serverUrl,
        string basicAuthCredentials,
        DigestHashAlgorithm digestHashAlgorithm = DigestHashAlgorithm.Sha256)
    {
        ServerUrl = serverUrl ?? string.Empty;
        BasicAuthCredentials = basicAuthCredentials ?? string.Empty;
        DigestHashAlgorithm = digestHashAlgorithm;
    }
}
