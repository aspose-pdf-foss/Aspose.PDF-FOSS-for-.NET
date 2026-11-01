namespace Aspose.Pdf.Text;

public partial class FontRepository
{
    // The TrueType programs callers opened, by PostScript name and by family name, spaces left out (the
    // latest of a name wins): a document names an embedded face either way ("SegoeUI", "Segoe UI"). A
    // document embeds such a face as a subset; asked later to carry it whole, the face it came from is
    // this program, not an installed face of the same name that may be another version.
    private static readonly Dictionary<string, byte[]> OpenedPrograms = new(StringComparer.Ordinal);
    private static readonly object OpenedProgramsLock = new();

    private static void RememberOpenedProgram(byte[] program)
    {
        var names = new[] { ReadTtfPostScriptName(program), ReadTtfFontName(program) };
        lock (OpenedProgramsLock)
            foreach (var name in names)
                if (name is { Length: > 0 }) OpenedPrograms[name.Replace(" ", "")] = program;
    }

    /// <summary>The program a caller opened under <paramref name="postScriptName"/> (a PostScript or family
    /// name, spaces ignored), or null.</summary>
    internal static byte[]? OpenedProgram(string postScriptName)
    {
        lock (OpenedProgramsLock)
            return OpenedPrograms.TryGetValue(postScriptName.Replace(" ", ""), out var program) ? program : null;
    }
}
