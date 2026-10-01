namespace Aspose.Pdf;

/// <summary>
/// Build and version information for the library: its product name and version.
/// </summary>
public static class BuildVersionInfo
{
    /// <summary>Product name: "Aspose.PDF FOSS".</summary>
    public const string Product = "Aspose.PDF FOSS";

    /// <summary>Assembly version string (e.g. "26.5.0").</summary>
    public static readonly string AssemblyVersion =
        typeof(BuildVersionInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>File version string.</summary>
    public static readonly string FileVersion = AssemblyVersion;

    /// <summary>
    /// The /Info Producer string stamped on save: the producing library's own
    /// name and version, following the usual PDF convention that a producer
    /// identifies itself through its product string.
    /// </summary>
    internal static readonly string ProducerString = ProducerName + " " + AssemblyVersion;

    /// <summary>The library's name as <see cref="ProducerString"/> begins with it, any version.</summary>
    internal const string ProducerName = "Aspose.PDF.FOSS for .NET";

    /// <summary>
    /// The default /Info Creator stamped on a new document's save when the
    /// caller set none: the FOSS library's own identity, following the same
    /// self-identification rule as <see cref="ProducerString"/> (no company
    /// brand is ever written here, only the library's own name).
    /// </summary>
    internal const string CreatorString = "Aspose.PDF.FOSS for .NET";
}
