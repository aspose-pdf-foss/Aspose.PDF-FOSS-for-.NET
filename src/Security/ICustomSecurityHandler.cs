namespace Aspose.Pdf.Security;

/// <summary>Parameters parsed from the document's /Encrypt dictionary and
/// passed to <see cref="ICustomSecurityHandler.Initialize"/> when a custom
/// security handler takes over for an alternative /Filter.</summary>
public sealed class EncryptionParameters
{
    private readonly Aspose.Pdf.Permissions _permissions;
    private readonly int _permissionsInt;

    public EncryptionParameters() { }

    internal EncryptionParameters(
        string filter, string subFilter, int version, int revision, int keyLength,
        byte[]? ownerKey, byte[]? userKey, byte[]? perms,
        Aspose.Pdf.Permissions permissions, int permissionsInt, string password)
    {
        Filter = filter ?? string.Empty;
        SubFilter = subFilter ?? string.Empty;
        Version = version;
        Revision = revision;
        KeyLength = keyLength;
        OwnerKey = ownerKey ?? System.Array.Empty<byte>();
        UserKey = userKey ?? System.Array.Empty<byte>();
        Perms = perms ?? System.Array.Empty<byte>();
        _permissions = permissions;
        _permissionsInt = permissionsInt;
        Password = password ?? string.Empty;
    }

    /// <summary>/Filter entry — security-handler name (e.g. "Standard").</summary>
    public string Filter { get; } = "Standard";

    /// <summary>/SubFilter entry — security-handler subtype.</summary>
    public string SubFilter { get; } = string.Empty;

    /// <summary>/V entry — algorithm version.</summary>
    public int Version { get; }

    /// <summary>/R entry — standard-handler revision.</summary>
    public int Revision { get; }

    /// <summary>/Length entry — key length in bits.</summary>
    public int KeyLength { get; }

    /// <summary>/O entry — owner-password validation hash.</summary>
    public byte[] OwnerKey { get; } = System.Array.Empty<byte>();

    /// <summary>/U entry — user-password validation hash.</summary>
    public byte[] UserKey { get; } = System.Array.Empty<byte>();

    /// <summary>/Perms entry — encrypted permissions byte string (revision 5+).</summary>
    public byte[] Perms { get; } = System.Array.Empty<byte>();

    /// <summary>The password supplied at decryption time (when known).</summary>
    public string Password { get; } = string.Empty;

    /// <summary>/P entry mapped to the <see cref="Aspose.Pdf.Permissions"/>
    /// flags enum.</summary>
    public Aspose.Pdf.Permissions Permissions => _permissions;

    /// <summary>Raw /P entry as a signed 32-bit integer.</summary>
    public int PermissionsInt => _permissionsInt;
}

/// <summary>Pluggable custom security handler — implement this to handle
/// non-Standard /Filter entries (e.g. Public-Key handlers /Adobe.PPKLite).
/// Aspose.PDF for .NET contract; the FOSS lib accepts custom handlers but the
/// built-in Standard security handler covers RC4/AES password encryption
/// without needing a custom implementation.</summary>
public interface ICustomSecurityHandler
{
    /// <summary>/Filter entry the handler is registered as.</summary>
    string Filter { get; }

    /// <summary>/SubFilter entry the handler is registered as.</summary>
    string SubFilter { get; }

    /// <summary>Algorithm version (PDF /Encrypt /V).</summary>
    int Version { get; }

    /// <summary>Standard handler revision (PDF /Encrypt /R) reported by
    /// the handler for emitted encryption dictionaries.</summary>
    int Revision { get; }

    /// <summary>Key length in bits.</summary>
    int KeyLength { get; }

    /// <summary>Initialise the handler from parsed /Encrypt parameters.
    /// Called once after the document's /Encrypt dictionary is parsed and
    /// before any Encrypt/Decrypt operation runs.</summary>
    void Initialize(EncryptionParameters parameters);

    /// <summary>Derive the per-document encryption key from the user
    /// password.</summary>
    byte[] CalculateEncryptionKey(string password);

    /// <summary>Compute the /U value for the given user password.</summary>
    byte[] GetUserKey(string userPassword);

    /// <summary>Compute the /O value for the given user/owner password pair.</summary>
    byte[] GetOwnerKey(string userPassword, string ownerPassword);

    /// <summary>Return true when <paramref name="password"/> matches the
    /// owner password.</summary>
    bool IsOwnerPassword(string password);

    /// <summary>Return true when <paramref name="password"/> matches the
    /// user password.</summary>
    bool IsUserPassword(string password);

    /// <summary>Encrypt the /Perms byte string for the given permissions
    /// (revision 5+).</summary>
    byte[] EncryptPermissions(int permissions);

    /// <summary>Encrypt one PDF object's content stream / string with the
    /// per-object key derived from <paramref name="key"/> +
    /// <paramref name="objectNumber"/> + <paramref name="generation"/>.</summary>
    byte[] Encrypt(byte[] data, int objectNumber, int generation, byte[] key);

    /// <summary>Inverse of <see cref="Encrypt"/>.</summary>
    byte[] Decrypt(byte[] data, int objectNumber, int generation, byte[] key);
}
