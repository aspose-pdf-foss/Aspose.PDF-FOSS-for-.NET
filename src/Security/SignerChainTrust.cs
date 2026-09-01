using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Aspose.Pdf.Security;

/// <summary>
/// Resolves a signer's certificate chain to a trusted root for
/// <see cref="ValidationOptions.CheckCertificateChain"/>.
/// </summary>
/// <remarks>
/// The walk uses ONLY the certificates the signature carries plus the machine-wide
/// stores: LocalMachine intermediates for the links, and the LocalMachine /
/// CurrentUser root stores for the anchor. Nothing is downloaded (no AIA fetching)
/// and the CurrentUser intermediate store is deliberately ignored — it is where
/// Windows caches certificates fetched during earlier online validations, a
/// transient artefact rather than a statement of trust. Probed against the
/// reference validator: it reports a leaf-only signature Undefined on a machine
/// where the platform chain engine completes the same chain from exactly such
/// cached intermediates, so a faithful resolver cannot consult them.
/// Every link is verified cryptographically (the child's signature against the
/// issuer's public key); a subject/issuer name match alone is not a link.
/// Validity is judged at the SIGNING time: a certificate that has expired since
/// the document was signed still validates (Valid is reported for a
/// signature whose leaf expired months before the check).
/// </remarks>
internal static class SignerChainTrust
{
    private const int MaxChainLength = 16;

    /// <summary>True when <paramref name="certificatesDer"/>[0] (the signer) chains
    /// to a trusted root under the rules above. <paramref name="reason"/> names the
    /// first failure for diagnostics.</summary>
    public static bool IsTrusted(IReadOnlyList<byte[]> certificatesDer, DateTime verificationTime, out string reason)
    {
        reason = string.Empty;
        if (certificatesDer.Count == 0) { reason = "no signer certificate"; return false; }

        var embedded = new List<X509Certificate2>();
        try
        {
            foreach (var der in certificatesDer) embedded.Add(Load(der));
        }
        catch (Exception ex)
        {
            reason = "unreadable certificate: " + ex.Message;
            return false;
        }

        // Certificate validity bounds surface as local-kind DateTimes while the
        // signing time is UTC; DateTime comparison ignores Kind, so normalise both.
        var at = verificationTime.Kind == DateTimeKind.Local ? verificationTime.ToUniversalTime() : verificationTime;
        var current = embedded[0];
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var depth = 0; depth < MaxChainLength; depth++)
        {
            if (!visited.Add(current.Thumbprint)) { reason = "certificate loop"; return false; }
            if (at < current.NotBefore.ToUniversalTime() || at > current.NotAfter.ToUniversalTime())
            {
                reason = $"'{current.Subject}' not valid at the signing time";
                return false;
            }

            if (IsSelfSigned(current))
            {
                if (InRootStores(current.Thumbprint)) return true;
                reason = $"self-signed '{current.Subject}' is not a trusted root";
                return false;
            }

            // A trusted root that directly issued the current certificate anchors
            // the chain; an intermediate continues it.
            var root = FindIssuer(current, RootStoreCertificates());
            if (root is not null) return true;

            var issuer = FindIssuer(current, embedded.Where(c => !ReferenceEquals(c, current)))
                         ?? FindIssuer(current, MachineIntermediates());
            if (issuer is null)
            {
                reason = $"no issuer found for '{current.Subject}' (partial chain)";
                return false;
            }
            current = issuer;
        }
        reason = "chain too long";
        return false;
    }

    private static X509Certificate2? FindIssuer(X509Certificate2 child, IEnumerable<X509Certificate2> candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.Equals(c.Subject, child.Issuer, StringComparison.Ordinal)) continue;
            if (SignatureVerifies(child, c)) return c;
        }
        return null;
    }

    private static bool IsSelfSigned(X509Certificate2 cert)
        => string.Equals(cert.Subject, cert.Issuer, StringComparison.Ordinal) && SignatureVerifies(cert, cert);

    /// <summary>Verify <paramref name="child"/>'s signature with <paramref name="issuer"/>'s
    /// public key: Certificate ::= SEQUENCE { tbsCertificate, signatureAlgorithm, signature }.</summary>
    private static bool SignatureVerifies(X509Certificate2 child, X509Certificate2 issuer)
    {
        try
        {
            var outer = new Asn1Reader(child.RawData).ReadSequence();
            var tbs = outer.ReadRawTlv();
            var sigAlgOid = outer.ReadSequence().ReadOid();
            var signature = outer.ReadBitString();
            var hash = HashFor(sigAlgOid);
            if (hash is null) return false;

            if (issuer.GetRSAPublicKey() is { } rsa)
                return rsa.VerifyData(tbs, signature, hash.Value, RSASignaturePadding.Pkcs1);
            if (issuer.GetECDsaPublicKey() is { } ec)
                return ec.VerifyData(tbs, signature, hash.Value, DSASignatureFormat.Rfc3279DerSequence);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static HashAlgorithmName? HashFor(string sigAlgOid) => sigAlgOid switch
    {
        "1.2.840.113549.1.1.5" or "1.2.840.10045.4.1" => HashAlgorithmName.SHA1,
        "1.2.840.113549.1.1.11" or "1.2.840.10045.4.3.2" => HashAlgorithmName.SHA256,
        "1.2.840.113549.1.1.12" or "1.2.840.10045.4.3.3" => HashAlgorithmName.SHA384,
        "1.2.840.113549.1.1.13" or "1.2.840.10045.4.3.4" => HashAlgorithmName.SHA512,
        _ => null,
    };

    private static bool InRootStores(string thumbprint)
    {
        foreach (var c in RootStoreCertificates())
            if (string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static IEnumerable<X509Certificate2> RootStoreCertificates()
    {
        foreach (var c in StoreCertificates(StoreName.Root, StoreLocation.LocalMachine)) yield return c;
        foreach (var c in StoreCertificates(StoreName.AuthRoot, StoreLocation.LocalMachine)) yield return c;
        foreach (var c in StoreCertificates(StoreName.Root, StoreLocation.CurrentUser)) yield return c;
    }

    /// <summary>The intermediate CAs this host knows, machine-wide and per-user. The
    /// per-user store matters as much as the machine one: it is where a user without
    /// administrator rights installs an intermediate, and away from Windows it is the only
    /// one that can be written at all - the machine store maps to a read-only system
    /// bundle. Reading just the machine store left a chain that had every certificate it
    /// needed sitting in the user's own store reported as a partial chain. The root walk
    /// has consulted CurrentUser all along; this is the matching half.</summary>
    private static IEnumerable<X509Certificate2> MachineIntermediates()
    {
        foreach (var c in StoreCertificates(StoreName.CertificateAuthority, StoreLocation.LocalMachine))
            yield return c;
        foreach (var c in StoreCertificates(StoreName.CertificateAuthority, StoreLocation.CurrentUser))
            yield return c;
    }

    private static IEnumerable<X509Certificate2> StoreCertificates(StoreName name, StoreLocation location)
    {
        X509Certificate2Collection certs;
        try
        {
            using var store = new X509Store(name, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            certs = store.Certificates;
        }
        catch
        {
            // A store that does not exist on this platform contributes nothing.
            yield break;
        }
        foreach (var c in certs) yield return c;
    }

#pragma warning disable SYSLIB0057
    private static X509Certificate2 Load(byte[] der) => new(der);
#pragma warning restore SYSLIB0057
}
