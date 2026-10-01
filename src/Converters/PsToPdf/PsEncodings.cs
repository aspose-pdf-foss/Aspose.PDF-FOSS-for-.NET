using System.Collections.Generic;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The two encoding vectors a PostScript program expects to find predefined. Both
/// are 256-element arrays of literal names, built from the library's existing
/// byte-to-glyph-name tables rather than from a second copy of them.
/// </summary>
internal static class PsEncodings
{
    /// <summary>How many codes an encoding vector covers.</summary>
    public const int CodeCount = 256;

    /// <summary>The name a code maps to when the encoding leaves it undefined.</summary>
    public const string NotDefined = ".notdef";

    /// <summary>A fresh <c>StandardEncoding</c> vector.</summary>
    public static PsArray StandardArray() => Build(useLatin1: false);

    /// <summary>A fresh <c>ISOLatin1Encoding</c> vector.</summary>
    public static PsArray IsoLatin1Array() => Build(useLatin1: true);

    /// <summary>Whether a face's codes stand for pictures rather than letters. Such a
    /// face still carries the standard vector: the program that holds the ornaments
    /// names each one after the letter whose code it sits at.</summary>
    public static bool IsSymbolic(string? face) =>
        face == SymbolFace || face == DingbatsFace;

    /// <summary>The face whose codes are mathematical and Greek characters.</summary>
    private const string SymbolFace = "Symbol";

    /// <summary>The face whose codes are ornaments.</summary>
    private const string DingbatsFace = "ZapfDingbats";

    private static PsArray Build(bool useLatin1)
    {
        var array = new PsArray(CodeCount);
        for (var code = 0; code < CodeCount; code++)
            array[code] = PsValue.LiteralName(NameFor(code, useLatin1) ?? NotDefined);
        return array;
    }

    /// <summary>ISO Latin-1 agrees with Standard over the ASCII range and follows the
    /// Latin-1 repertoire above it, which WinAnsi matches everywhere the two
    /// encodings both define a glyph.</summary>
    private static string? NameFor(int code, bool useLatin1)
    {
        if (!useLatin1) return Type1StandardEncoding.GetName(code);
        if (code < AsciiLimit) return Type1StandardEncoding.GetName(code);
        return PdfEncodings.WinAnsiName(code);
    }

    /// <summary>The first code above the plain ASCII range.</summary>
    private const int AsciiLimit = 0x80;

    /// <summary>The code the standard vector puts a name at, or -1 when it names no
    /// such glyph. A face of pictures names each ornament after the letter whose code
    /// it occupies, so this is where its program keeps that ornament.</summary>
    public static int StandardCodeOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        return StandardCodes.TryGetValue(name!, out var code) ? code : -1;
    }

    private static readonly Dictionary<string, int> StandardCodes = BuildStandardCodes();

    private static Dictionary<string, int> BuildStandardCodes()
    {
        var codes = new Dictionary<string, int>(CodeCount, StringComparer.Ordinal);
        for (var code = 0; code < CodeCount; code++)
        {
            var name = Type1StandardEncoding.GetName(code);
            if (!string.IsNullOrEmpty(name) && name != NotDefined && !codes.ContainsKey(name!))
                codes[name!] = code;
        }

        return codes;
    }

    /// <summary>The character a picture face's code stands for, or 0 when the face's
    /// own encoding leaves the code undefined. A face that has been SUBSTITUTED knows
    /// nothing of the missing face's glyph names, so the character is the only thing
    /// it can be asked for.</summary>
    public static int SymbolicCharacter(string? face, int code)
    {
        if (code < 0 || code > ByteMask) return 0;
        if (face == SymbolFace) return TextAbsorber.SymbolCharacter((byte)code);
        if (face == DingbatsFace) return TextAbsorber.DingbatCharacter((byte)code);
        return 0;
    }

    /// <summary>The largest code an encoding vector covers.</summary>
    private const int ByteMask = 0xFF;

    /// <summary>The glyph name a vector gives a code, falling back to the standard
    /// vector when the array is short or holds something other than a name.</summary>
    public static string Lookup(PsArray? encoding, int code)
    {
        if (encoding != null && code >= 0 && code < encoding.Length)
        {
            var entry = encoding[code];
            if (entry.Type == PsType.Name) return entry.Text ?? NotDefined;
        }

        return Type1StandardEncoding.GetName(code) ?? NotDefined;
    }
}
