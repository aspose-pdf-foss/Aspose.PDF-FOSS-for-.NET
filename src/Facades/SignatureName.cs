namespace Aspose.Pdf.Facades;

/// <summary>Identifies a signature field by both its partial name (<see cref="Name"/>)
/// and full hierarchical name (<see cref="FullName"/>), and reports whether the field
/// currently carries a signature value via <see cref="HasSignature"/>.</summary>
public sealed class SignatureName
{
    /// <summary>Partial (leaf) field name — last segment of the hierarchical name.</summary>
    public string Name = string.Empty;

    /// <summary>Full dot-separated hierarchical field name.</summary>
    public string FullName = string.Empty;

    private readonly bool _hasSignature;

    /// <summary>True when the underlying signature field has a /V entry (a real
    /// signature value); false for blank signature fields that exist on the
    /// form but have not been signed yet.</summary>
    public bool HasSignature => _hasSignature;

    public SignatureName() { }

    internal SignatureName(string fullName, string name, bool hasSignature)
    {
        FullName = fullName ?? string.Empty;
        Name = name ?? string.Empty;
        _hasSignature = hasSignature;
    }

    public override string ToString() => FullName ?? Name ?? string.Empty;

    public override bool Equals(object? obj)
        => obj is SignatureName other
           && string.Equals(FullName, other.FullName, System.StringComparison.Ordinal)
           && string.Equals(Name, other.Name, System.StringComparison.Ordinal)
           && HasSignature == other.HasSignature;

    public override int GetHashCode() => System.HashCode.Combine(FullName, Name, HasSignature);
}
