using System;
using System.Collections.Generic;
using System.IO;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Resolves the font names a PostScript program asks for. The rule the reference
/// converter follows, measured across the corpus without a single counter-example:
/// a name that is one of the PDF base-14 is referenced by name and never embedded;
/// any other name is satisfied from a font program found in the search folders.
/// </summary>
internal sealed class PsFontLibrary
{
    private static readonly string[] Base14 =
    {
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        "Symbol", "ZapfDingbats",
    };

    private static readonly HashSet<string> Base14Set =
        new(Base14, StringComparer.Ordinal);

    private readonly List<string> _folders = new();
    private readonly Dictionary<string, string> _programs = new(StringComparer.OrdinalIgnoreCase);
    private bool _scanned;

    /// <summary>Create a library searching the given folders.</summary>
    public PsFontLibrary(IEnumerable<string> folders)
    {
        if (folders is null) return;
        foreach (var folder in folders)
            if (!string.IsNullOrEmpty(folder))
                _folders.Add(folder);
    }

    /// <summary>Whether a name is one of the base-14 the writer may reference
    /// without embedding anything.</summary>
    public static bool IsBase14(string? name) => name != null && Base14Set.Contains(name);

    /// <summary>Whether this library can satisfy a name at all.</summary>
    public bool Knows(string? name) =>
        IsBase14(name) || FindProgram(name) != null;

    /// <summary>The path of the font program backing a name, or null when the search
    /// folders hold none. A face that cannot be found produces no text at all, which
    /// is what the reference converter does for the corpus's missing faces.</summary>
    public string? FindProgram(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        Scan();
        var key = Normalise(name!);
        if (key.Length == 0) return null;
        if (_programs.TryGetValue(key, out var exact)) return exact;
        // A face the source names with a style word the file spells differently still
        // matches when one name is a prefix of the other. Take the LONGEST such key, so
        // a bold face prefers the bold file over the plain one it also prefixes.
        string? best = null;
        var bestLength = 0;
        foreach (var pair in _programs)
        {
            if (!pair.Key.StartsWith(key, StringComparison.Ordinal) &&
                !key.StartsWith(pair.Key, StringComparison.Ordinal))
                continue;
            if (pair.Key.Length <= bestLength) continue;
            best = pair.Value;
            bestLength = pair.Key.Length;
        }

        return best;
    }

    /// <summary>Index the search folders once, on first use.</summary>
    private void Scan()
    {
        if (_scanned) return;
        _scanned = true;
        foreach (var folder in _folders) ScanFolder(folder);
    }

    private void ScanFolder(string folder)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(folder)) return;
            files = Directory.GetFiles(folder);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            if (!IsFontProgram(file)) continue;
            var key = Normalise(Path.GetFileNameWithoutExtension(file));
            if (key.Length > 0 && !_programs.ContainsKey(key)) _programs[key] = file;
        }
    }

    /// <summary>The comparison form of a face name. A font file is rarely named
    /// exactly as the PostScript source spells the face — the corpus holds
    /// <c>Palatino-Bold-Italic.ttf</c> for <c>Palatino-BoldItalic</c> and
    /// <c>Palatino-Roman-Regular.ttf</c> for <c>Palatino-Roman</c> — so separators,
    /// case and a trailing weight word are all dropped before matching.</summary>
    private static string Normalise(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        var text = sb.ToString();
        // Strip until nothing more comes off, so that a face and the file that holds
        // it reduce to the SAME form however many of these words each one spells out:
        // "Palatino-Roman" and "Palatino-Roman-Regular" must not end up different.
        var reducing = true;
        while (reducing)
        {
            reducing = false;
            foreach (var suffix in RedundantSuffixes)
            {
                if (text.Length <= suffix.Length ||
                    !text.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                text = text.Substring(0, text.Length - suffix.Length);
                reducing = true;
                break;
            }
        }

        return text;
    }

    /// <summary>Words a file name adds that the PostScript name leaves off.</summary>
    private static readonly string[] RedundantSuffixes = { "regular", "roman", "medium", "book" };

    private static bool IsFontProgram(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension)) return false;
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pfb", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pfa", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The base-14 name closest to a requested one, used when a program asks
    /// for a face the folders do not hold but whose family is a standard one.</summary>
    public static string NearestBase14(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "Helvetica";
        if (IsBase14(name)) return name;
        var bold = name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0;
        var italic = name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;
        if (name.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
            return Style("Courier", bold, italic, "Oblique");
        if (name.StartsWith("Times", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Palatino", StringComparison.OrdinalIgnoreCase))
            return Style("Times", bold, italic, "Italic", plain: "Times-Roman");
        if (name == "Symbol" || name == "ZapfDingbats") return name;
        return Style("Helvetica", bold, italic, "Oblique");
    }

    private static string Style(string family, bool bold, bool italic, string slant,
        string? plain = null)
    {
        if (bold && italic) return family + "-Bold" + slant;
        if (bold) return family + "-Bold";
        if (italic) return family + "-" + slant;
        return plain ?? family;
    }
}

/// <summary>
/// A font as the graphics state holds it: the dictionary the program built, the name
/// it was found under, and the matrix that scales glyph space to user space.
/// </summary>
internal sealed class PsFontInstance
{
    /// <summary>Create an instance.</summary>
    public PsFontInstance(string? name, PsDictionary dict, PsMatrix matrix, PsArray? encoding,
        string? face = null)
    {
        Name = name;
        Dict = dict;
        Matrix = matrix;
        Encoding = encoding;
        Face = string.IsNullOrEmpty(face) ? name : face;
    }

    /// <summary>The name the program asked for.</summary>
    public string? Name { get; }

    /// <summary>The face the letters come from, which is the name the font was found
    /// under rather than the name it was later given: a program derives a font by
    /// copying a found one and renaming it, and the copy still draws with the
    /// original's letters.</summary>
    public string? Face { get; }

    /// <summary>The font dictionary.</summary>
    public PsDictionary Dict { get; }

    /// <summary>The font matrix, already scaled by <c>scalefont</c>.</summary>
    public PsMatrix Matrix { get; }

    /// <summary>The encoding vector, or null to use the standard one.</summary>
    public PsArray? Encoding { get; }

    /// <summary>The size a PDF text object should be set at: the uniform scale the
    /// font matrix carries, which <c>scalefont</c> and <c>makefont</c> both fold in.
    /// Anything the matrix holds beyond that scale — a rotation, a shear, a mirror —
    /// is written into the CTM instead, so it must not be counted twice here.</summary>
    public double Size
    {
        get
        {
            var scale = Matrix.ApproximateScale;
            return scale > 0 ? scale : 1;
        }
    }

    /// <summary>How far this font's matrix carries a unit of glyph space along the
    /// x axis. A string advances by this rather than by <see cref="Size"/>: the two
    /// part company as soon as a program derives a font with a matrix that scales the
    /// two axes differently, which is how a fraction's numerator is made.</summary>
    public double Advance
    {
        get
        {
            var scale = Matrix.HorizontalScale;
            return scale > 0 ? scale : 1;
        }
    }

    /// <summary>This instance scaled by a further matrix, which is what
    /// <c>makefont</c> and <c>scalefont</c> produce.</summary>
    public PsFontInstance Scaled(PsMatrix by) =>
        new(Name, Dict, by.Concat(Matrix), Encoding, Face);
}
