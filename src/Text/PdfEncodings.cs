namespace Aspose.Pdf.Text;

/// <summary>
/// Full byte → glyph-name tables for the predefined simple-font encodings of
/// PDF 32000 Annex D (WinAnsiEncoding, MacRomanEncoding). StandardEncoding
/// lives in <see cref="Type1StandardEncoding"/> (same table as Type 1's
/// built-in StandardEncoding). The high range (0x80–0xFF) is what
/// distinguishes the encodings — MacRoman keeps the fi/fl ligatures at
/// 0xDE/0xDF, WinAnsi is CP1252 — and is required to resolve glyphs from a
/// name-keyed program (CFF/Type 1) when the show-string uses extended bytes.
/// </summary>
internal static class PdfEncodings
{
    public static string? WinAnsiName(int code)
        => code is >= 0 and < 256 ? _winAnsi[code] : null;

    public static string? MacRomanName(int code)
        => code is >= 0 and < 256 ? _macRoman[code] : null;

    private static readonly string?[] _winAnsi = BuildWinAnsi();
    private static readonly string?[] _macRoman = BuildMacRoman();

    private static void FillAscii(string?[] t)
    {
        // 0x20–0x7E, shared by all the predefined encodings except for
        // 0x27/0x60, which each Build* method sets per Annex D afterwards.
        var ascii = new[]
        {
            "space","exclam","quotedbl","numbersign","dollar","percent","ampersand","quotesingle",
            "parenleft","parenright","asterisk","plus","comma","hyphen","period","slash",
            "zero","one","two","three","four","five","six","seven","eight","nine",
            "colon","semicolon","less","equal","greater","question","at",
            "A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
            "bracketleft","backslash","bracketright","asciicircum","underscore","grave",
            "a","b","c","d","e","f","g","h","i","j","k","l","m","n","o","p","q","r","s","t","u","v","w","x","y","z",
            "braceleft","bar","braceright","asciitilde"
        };
        for (var i = 0; i < ascii.Length; i++) t[0x20 + i] = ascii[i];
    }

    private static string?[] BuildWinAnsi()
    {
        var t = new string?[256];
        FillAscii(t);
        void S(int c, string n) => t[c] = n;
        S(0x80, "Euro"); S(0x82, "quotesinglbase"); S(0x83, "florin"); S(0x84, "quotedblbase");
        S(0x85, "ellipsis"); S(0x86, "dagger"); S(0x87, "daggerdbl"); S(0x88, "circumflex");
        S(0x89, "perthousand"); S(0x8A, "Scaron"); S(0x8B, "guilsinglleft"); S(0x8C, "OE");
        S(0x8E, "Zcaron"); S(0x91, "quoteleft"); S(0x92, "quoteright"); S(0x93, "quotedblleft");
        S(0x94, "quotedblright"); S(0x95, "bullet"); S(0x96, "endash"); S(0x97, "emdash");
        S(0x98, "tilde"); S(0x99, "trademark"); S(0x9A, "scaron"); S(0x9B, "guilsinglright");
        S(0x9C, "oe"); S(0x9E, "zcaron"); S(0x9F, "Ydieresis");
        S(0xA0, "space"); S(0xA1, "exclamdown"); S(0xA2, "cent"); S(0xA3, "sterling");
        S(0xA4, "currency"); S(0xA5, "yen"); S(0xA6, "brokenbar"); S(0xA7, "section");
        S(0xA8, "dieresis"); S(0xA9, "copyright"); S(0xAA, "ordfeminine"); S(0xAB, "guillemotleft");
        S(0xAC, "logicalnot"); S(0xAD, "hyphen"); S(0xAE, "registered"); S(0xAF, "macron");
        S(0xB0, "degree"); S(0xB1, "plusminus"); S(0xB2, "twosuperior"); S(0xB3, "threesuperior");
        S(0xB4, "acute"); S(0xB5, "mu"); S(0xB6, "paragraph"); S(0xB7, "periodcentered");
        S(0xB8, "cedilla"); S(0xB9, "onesuperior"); S(0xBA, "ordmasculine"); S(0xBB, "guillemotright");
        S(0xBC, "onequarter"); S(0xBD, "onehalf"); S(0xBE, "threequarters"); S(0xBF, "questiondown");
        S(0xC0, "Agrave"); S(0xC1, "Aacute"); S(0xC2, "Acircumflex"); S(0xC3, "Atilde");
        S(0xC4, "Adieresis"); S(0xC5, "Aring"); S(0xC6, "AE"); S(0xC7, "Ccedilla");
        S(0xC8, "Egrave"); S(0xC9, "Eacute"); S(0xCA, "Ecircumflex"); S(0xCB, "Edieresis");
        S(0xCC, "Igrave"); S(0xCD, "Iacute"); S(0xCE, "Icircumflex"); S(0xCF, "Idieresis");
        S(0xD0, "Eth"); S(0xD1, "Ntilde"); S(0xD2, "Ograve"); S(0xD3, "Oacute");
        S(0xD4, "Ocircumflex"); S(0xD5, "Otilde"); S(0xD6, "Odieresis"); S(0xD7, "multiply");
        S(0xD8, "Oslash"); S(0xD9, "Ugrave"); S(0xDA, "Uacute"); S(0xDB, "Ucircumflex");
        S(0xDC, "Udieresis"); S(0xDD, "Yacute"); S(0xDE, "Thorn"); S(0xDF, "germandbls");
        S(0xE0, "agrave"); S(0xE1, "aacute"); S(0xE2, "acircumflex"); S(0xE3, "atilde");
        S(0xE4, "adieresis"); S(0xE5, "aring"); S(0xE6, "ae"); S(0xE7, "ccedilla");
        S(0xE8, "egrave"); S(0xE9, "eacute"); S(0xEA, "ecircumflex"); S(0xEB, "edieresis");
        S(0xEC, "igrave"); S(0xED, "iacute"); S(0xEE, "icircumflex"); S(0xEF, "idieresis");
        S(0xF0, "eth"); S(0xF1, "ntilde"); S(0xF2, "ograve"); S(0xF3, "oacute");
        S(0xF4, "ocircumflex"); S(0xF5, "otilde"); S(0xF6, "odieresis"); S(0xF7, "divide");
        S(0xF8, "oslash"); S(0xF9, "ugrave"); S(0xFA, "uacute"); S(0xFB, "ucircumflex");
        S(0xFC, "udieresis"); S(0xFD, "yacute"); S(0xFE, "thorn"); S(0xFF, "ydieresis");
        return t;
    }

    private static string?[] BuildMacRoman()
    {
        var t = new string?[256];
        FillAscii(t);
        void S(int c, string n) => t[c] = n;
        S(0x80, "Adieresis"); S(0x81, "Aring"); S(0x82, "Ccedilla"); S(0x83, "Eacute");
        S(0x84, "Ntilde"); S(0x85, "Odieresis"); S(0x86, "Udieresis"); S(0x87, "aacute");
        S(0x88, "agrave"); S(0x89, "acircumflex"); S(0x8A, "adieresis"); S(0x8B, "atilde");
        S(0x8C, "aring"); S(0x8D, "ccedilla"); S(0x8E, "eacute"); S(0x8F, "egrave");
        S(0x90, "ecircumflex"); S(0x91, "edieresis"); S(0x92, "iacute"); S(0x93, "igrave");
        S(0x94, "icircumflex"); S(0x95, "idieresis"); S(0x96, "ntilde"); S(0x97, "oacute");
        S(0x98, "ograve"); S(0x99, "ocircumflex"); S(0x9A, "odieresis"); S(0x9B, "otilde");
        S(0x9C, "uacute"); S(0x9D, "ugrave"); S(0x9E, "ucircumflex"); S(0x9F, "udieresis");
        S(0xA0, "dagger"); S(0xA1, "degree"); S(0xA2, "cent"); S(0xA3, "sterling");
        S(0xA4, "section"); S(0xA5, "bullet"); S(0xA6, "paragraph"); S(0xA7, "germandbls");
        S(0xA8, "registered"); S(0xA9, "copyright"); S(0xAA, "trademark"); S(0xAB, "acute");
        S(0xAC, "dieresis"); S(0xAD, "notequal"); S(0xAE, "AE"); S(0xAF, "Oslash");
        S(0xB0, "infinity"); S(0xB1, "plusminus"); S(0xB2, "lessequal"); S(0xB3, "greaterequal");
        S(0xB4, "yen"); S(0xB5, "mu"); S(0xB6, "partialdiff"); S(0xB7, "summation");
        S(0xB8, "product"); S(0xB9, "pi"); S(0xBA, "integral"); S(0xBB, "ordfeminine");
        S(0xBC, "ordmasculine"); S(0xBD, "Omega"); S(0xBE, "ae"); S(0xBF, "oslash");
        S(0xC0, "questiondown"); S(0xC1, "exclamdown"); S(0xC2, "logicalnot"); S(0xC3, "radical");
        S(0xC4, "florin"); S(0xC5, "approxequal"); S(0xC6, "Delta"); S(0xC7, "guillemotleft");
        S(0xC8, "guillemotright"); S(0xC9, "ellipsis"); S(0xCA, "space"); S(0xCB, "Agrave");
        S(0xCC, "Atilde"); S(0xCD, "Otilde"); S(0xCE, "OE"); S(0xCF, "oe");
        S(0xD0, "endash"); S(0xD1, "emdash"); S(0xD2, "quotedblleft"); S(0xD3, "quotedblright");
        S(0xD4, "quoteleft"); S(0xD5, "quoteright"); S(0xD6, "divide"); S(0xD7, "lozenge");
        S(0xD8, "ydieresis"); S(0xD9, "Ydieresis"); S(0xDA, "fraction"); S(0xDB, "currency");
        S(0xDC, "guilsinglleft"); S(0xDD, "guilsinglright"); S(0xDE, "fi"); S(0xDF, "fl");
        S(0xE0, "daggerdbl"); S(0xE1, "periodcentered"); S(0xE2, "quotesinglbase"); S(0xE3, "quotedblbase");
        S(0xE4, "perthousand"); S(0xE5, "Acircumflex"); S(0xE6, "Ecircumflex"); S(0xE7, "Aacute");
        S(0xE8, "Edieresis"); S(0xE9, "Egrave"); S(0xEA, "Iacute"); S(0xEB, "Icircumflex");
        S(0xEC, "Idieresis"); S(0xED, "Igrave"); S(0xEE, "Oacute"); S(0xEF, "Ocircumflex");
        S(0xF1, "Ograve"); S(0xF2, "Uacute"); S(0xF3, "Ucircumflex"); S(0xF4, "Ugrave");
        S(0xF5, "dotlessi"); S(0xF6, "circumflex"); S(0xF7, "tilde"); S(0xF8, "macron");
        S(0xF9, "breve"); S(0xFA, "dotaccent"); S(0xFB, "ring"); S(0xFC, "cedilla");
        S(0xFD, "hungarumlaut"); S(0xFE, "ogonek"); S(0xFF, "caron");
        return t;
    }
}
