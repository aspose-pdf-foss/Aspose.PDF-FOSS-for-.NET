using System;

namespace Aspose.Pdf;

/// <summary>
/// Thrown when a feature needs an optional dependency the consuming application has not
/// referenced - a NuGet package the library deliberately does not force on every consumer,
/// such as System.Drawing.Common for printing. The message names what to install.
/// </summary>
public sealed class MissingOptionalDependencyException : PdfException
{
    /// <summary>An optional dependency is missing, with the generic message.</summary>
    public MissingOptionalDependencyException()
        : base("An optional dependency required for this operation is missing.")
    {
    }

    /// <summary>An optional dependency is missing, with an actionable message.</summary>
    public MissingOptionalDependencyException(string message) : base(message)
    {
    }

    /// <summary>An optional dependency is missing, keeping the load failure that revealed it.</summary>
    public MissingOptionalDependencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
