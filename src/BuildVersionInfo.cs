namespace Aspose.Pdf;

/// <summary>
/// Build and version information for the library. The <see cref="Product"/> name
/// is the prefix stamped into the /Info Producer string of generated documents.
/// </summary>
public static class BuildVersionInfo
{
    /// <summary>Product name (e.g. "Aspose.PDF").</summary>
    public const string Product = "Aspose.PDF";

    /// <summary>Assembly version string (e.g. "26.5.0").</summary>
    public static readonly string AssemblyVersion =
        typeof(BuildVersionInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>File version string.</summary>
    public static readonly string FileVersion = AssemblyVersion;
}
