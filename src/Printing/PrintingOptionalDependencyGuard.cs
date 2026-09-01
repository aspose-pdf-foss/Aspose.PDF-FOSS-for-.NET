using System;
using System.IO;

namespace Aspose.Pdf.Printing
{
    /// <summary>
    /// Guards the printing API's optional platform dependency: on modern .NET the printing
    /// pipeline needs System.Drawing.Common, which ships as a NuGet package the consuming
    /// application must reference itself. When it is absent the runtime surfaces a bare
    /// <see cref="FileNotFoundException"/> at the first printing call; this guard turns that
    /// into an actionable error naming the package to add.
    /// </summary>
    public static class PrintingOptionalDependencyGuard
    {
        internal const string DependencyAssemblyName = "System.Drawing.Common";

        /// <summary>
        /// Wrap a missing-dependency failure in an exception whose message tells the
        /// developer what to install.
        /// </summary>
        /// <param name="innerException">The load failure that revealed the missing
        /// dependency. Required - the guard never invents a failure it did not observe.</param>
        public static MissingOptionalDependencyException CreateException(Exception innerException)
        {
            if (innerException is null) throw new ArgumentNullException(nameof(innerException));
            return new MissingOptionalDependencyException(
                "Printing requires the optional " + DependencyAssemblyName + " package. " +
                "This Aspose.PDF build was validated with " + DependencyAssemblyName + " " +
                AssemblyConstants.SystemDrawingCommonVersion + ". " +
                "Install " + DependencyAssemblyName + " " + AssemblyConstants.SystemDrawingCommonVersion +
                ". A newer version may also work, but compatibility with this Aspose.PDF build " +
                "should be verified. " + DependencyAssemblyName + " requires a Windows-compatible " +
                "runtime (or libgdiplus on non-Windows platforms) to function correctly.",
                innerException);
        }

        /// <summary>
        /// Verify the optional printing dependency can be loaded, throwing the actionable
        /// exception from <see cref="CreateException"/> when it cannot. A no-op when the
        /// application references System.Drawing.Common.
        /// </summary>
        public static void EnsureDependenciesAvailable()
        {
            try
            {
                // Touching a System.Drawing type forces the assembly load; the probe type
                // itself is irrelevant.
                _ = typeof(System.Drawing.Printing.PrinterSettings).Assembly;
            }
            catch (FileNotFoundException e)
            {
                throw CreateException(e);
            }
        }
    }
}
