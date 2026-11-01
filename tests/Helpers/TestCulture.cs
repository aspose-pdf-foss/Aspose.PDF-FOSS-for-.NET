using System.Globalization;
using System.Runtime.CompilerServices;

namespace Aspose.Pdf.Tests.Helpers;

/// <summary>
/// Runs the whole suite under the culture named by the <c>FOSS_TEST_CULTURE</c> environment
/// variable (e.g. <c>sv-SE</c>, or <c>synthetic</c> for <see cref="CultureScope.Synthetic"/>), so a
/// number, sign or date that follows the machine's locale turns a test red on any build machine.
/// Unset, the suite runs under the machine's own culture.
/// </summary>
internal static class TestCulture
{
    internal const string Variable = "FOSS_TEST_CULTURE";

    /// <summary>The culture the variable names, or null when it is unset.</summary>
    internal static string? Requested => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } name ? name : null;

    // A module initializer runs before any code of this assembly, on every framework the
    // project targets; the thread defaults reach the runner's worker threads, which never
    // set a culture of their own.
    [ModuleInitializer]
    internal static void Apply()
    {
        if (Requested is not { } name) return;
        var culture = CultureScope.Resolve(name);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
