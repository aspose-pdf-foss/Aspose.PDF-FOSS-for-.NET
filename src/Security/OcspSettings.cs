namespace Aspose.Pdf;

/// <summary>
/// OCSP (Online Certificate Status Protocol) settings for the signer's
/// revocation-check / OCSP-stapling path.
/// </summary>
public class OcspSettings
{
    /// <summary>OCSP responder URL. If null, the URL embedded in the
    /// signer certificate's AIA extension is used.</summary>
    public string? ServerUrl { get; set; }

    /// <summary>HTTP request timeout in milliseconds. Defaults to 60s.</summary>
    public int RequestTimeout { get; set; } = 60_000;

    public OcspSettings() { }

    public OcspSettings(string? serverUrl)
    {
        ServerUrl = serverUrl;
    }
}
