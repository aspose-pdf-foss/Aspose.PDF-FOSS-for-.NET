namespace Aspose.Pdf.IO;

/// <summary>
/// Path handling for strings that arrive from CALLING code rather than from the file
/// system. The public API grew up on Windows, so callers routinely build a directory
/// with a backslash - <c>Path.GetDirectoryName(x) + "\\"</c> is the idiom in a decade of
/// sample code - and on Windows that is indistinguishable from the native form. Off
/// Windows a backslash is an ordinary filename character, so the same string silently
/// names one weirdly-spelled entry instead of a directory chain, and every resource
/// resolved against it goes missing. These two helpers restore the Windows reading
/// without changing anything Windows itself does.
/// </summary>
internal static class CallerPaths
{
    /// <summary>
    /// Read a caller-supplied directory the way Windows would: a backslash is a
    /// separator. Applied to roots that RESOLVE relative references (an HTML base path,
    /// against which <c>href</c>s and <c>src</c>s are joined), where the string names a
    /// place that must already exist and the caller's separator style is incidental.
    /// A no-op on Windows, where the platform separator is the backslash already.
    /// </summary>
    public static string? NormalizeDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path) || System.OperatingSystem.IsWindows()) return path;
        return path!.Replace('\\', System.IO.Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// The local path a <c>file:</c> URI names. Calling code composes these as
    /// <c>"file:///" + path</c>, which is right for a Windows path (<c>file:///C:/x</c>)
    /// but yields FOUR slashes for a POSIX one (<c>file:////home/x</c>). .NET then reads
    /// the third slash as the start of an AUTHORITY and hands back a UNC path -
    /// <c>\\\\home\\x</c> - so the file is never found. Strip the scheme here instead and
    /// leave <see cref="System.Uri"/> only the percent-decoding it is actually needed for.
    /// </summary>
    public static string FileUriToPath(string src)
    {
        if (string.IsNullOrEmpty(src)
            || !src.StartsWith("file:", System.StringComparison.OrdinalIgnoreCase))
            return src;
        var rest = src.Substring("file:".Length);
        var slashes = 0;
        while (slashes < rest.Length && (rest[slashes] == '/' || rest[slashes] == '\\')) slashes++;
        // Exactly two slashes is a genuine UNC reference (file://server/share/x); Windows
        // knows what to do with it, so hand that shape back to Uri unchanged.
        if (slashes == 2 && System.OperatingSystem.IsWindows()) return src;
        var body = rest.Substring(slashes);
        try { body = System.Uri.UnescapeDataString(body); } catch { /* keep it raw */ }
        // A drive letter stands on its own; anything else was absolute before the scheme
        // was glued on and keeps its leading separator.
        return body.Length > 1 && body[1] == ':' ? body : "/" + body;
    }

    /// <summary>
    /// Append a file name to a caller-supplied folder, honouring a trailing separator of
    /// EITHER kind. Applied to output folders the caller also spells names against
    /// itself - the resource-saving callbacks hand back a "supposed file name" that
    /// calling code concatenates onto the same folder string, so the two halves have to
    /// agree character for character or the caller cannot find what was written.
    /// <c>Path.Combine</c> would insert a second, native separator after a trailing
    /// backslash and put the file one directory deeper than the caller looks.
    /// </summary>
    public static string AppendName(string folder, string name)
    {
        if (string.IsNullOrEmpty(folder)) return name;
        var last = folder[folder.Length - 1];
        if (last == '/' || last == '\\' || last == System.IO.Path.DirectorySeparatorChar)
            return folder + name;
        return System.IO.Path.Combine(folder, name);
    }
}
