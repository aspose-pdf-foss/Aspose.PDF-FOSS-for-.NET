using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>The patterns the library builds once and keeps read markup, style sheets and content the same
/// whatever the culture of the machine: one that ignores case folds it by the invariant culture's rules, under
/// which 'I' is the capital of 'i' - under a Turkish one it is not. A pattern kept in a static field folds case by
/// the culture current when its type was first used, so a test converting under a Turkish culture shows a missing
/// option only when it is the first to use the pattern; this one finds each such pattern whatever ran before.</summary>
public class CaseInsensitivePatternTests
{
    // An inline group that switches case-insensitive matching on: (?i) (?i: (?si) (?i-m: ...
    private static readonly Regex InlineIgnoreCase = new(@"(?<!\\)\(\?[mnsx]*i[imnsx]*(?:-[imnsx]*)?[):]", RegexOptions.CultureInvariant);

    private static IEnumerable<Type> LibraryTypes()
    {
        try { return typeof(Document).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); }
    }

    [Fact]
    public void EveryCaseInsensitivePatternTheLibraryKeepsFoldsCaseByTheInvariantCulture()
    {
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var kept = 0;
        var cultureBound = new List<string>();
        foreach (var type in LibraryTypes().Where(t => !t.ContainsGenericParameters))
        {
            foreach (var field in type.GetFields(statics).Where(f => f.FieldType == typeof(Regex)))
            {
                if (field.GetValue(null) is not Regex regex) continue;
                kept++;
                var ignoresCase = (regex.Options & RegexOptions.IgnoreCase) != 0 || InlineIgnoreCase.IsMatch(regex.ToString());
                if (ignoresCase && (regex.Options & RegexOptions.CultureInvariant) == 0)
                    cultureBound.Add(type.FullName + "." + field.Name);
            }
        }

        Assert.True(kept > 0, "no pattern found: the test reads the wrong assembly");
        Assert.True(cultureBound.Count == 0, cultureBound.Count + " of " + kept
            + " kept patterns fold case by the culture current when they were built: " + string.Join(", ", cultureBound));
    }
}
