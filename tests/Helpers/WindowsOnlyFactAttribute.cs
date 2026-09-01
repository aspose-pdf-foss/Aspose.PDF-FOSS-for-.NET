using Xunit;

namespace Aspose.Pdf.Tests.Helpers;

/// <summary>
/// A <see cref="FactAttribute"/> that skips the test at run time on a non-Windows host.
/// <c>[SupportedOSPlatform("windows")]</c> only informs the compile-time analyzer, so a
/// test exercising a Windows-only API still runs — and fails — everywhere else.
/// </summary>
internal sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows-only API (System.Drawing / GDI+).";
        }
    }
}
