namespace Aspose.Pdf.Text;

/// <summary>
/// Adobe CFF Standard Strings — 391 predefined glyph names that SIDs 0..390
/// reference directly (Appendix A of Adobe Technical Note #5176, "The Compact
/// Font Format Specification"). String INDEX entries are numbered from
/// SID 391 onward.
///
/// Needed for non-CID Type 1C fonts (FontFile3 /Subtype /Type1C). Their
/// Charset lists a SID per GID; resolving the SID to a glyph name lets the
/// renderer map standard PDF /Differences names back to glyph outlines.
/// </summary>
internal static class CffStandardStrings
{
    /// <summary>Total entries; SIDs 0..390 inclusive.</summary>
    public const int Count = 391;

    /// <summary>Return the glyph name for SID <paramref name="sid"/>, or
    /// null when out of range.</summary>
    public static string? Get(int sid) =>
        (uint)sid < (uint)_names.Length ? _names[sid] : null;

    // Order is taken verbatim from CFF spec Appendix A. Indices match SIDs.
    private static readonly string[] _names =
    {
        ".notdef", "space", "exclam", "quotedbl", "numbersign", "dollar",
        "percent", "ampersand", "quoteright", "parenleft", "parenright",
        "asterisk", "plus", "comma", "hyphen", "period", "slash",
        "zero", "one", "two", "three", "four", "five", "six", "seven",
        "eight", "nine", "colon", "semicolon", "less", "equal", "greater",
        "question", "at",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "bracketleft", "backslash", "bracketright", "asciicircum",
        "underscore", "quoteleft",
        "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m",
        "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
        "braceleft", "bar", "braceright", "asciitilde",
        "exclamdown", "cent", "sterling", "fraction", "yen", "florin",
        "section", "currency", "quotesingle", "quotedblleft",
        "guillemotleft", "guilsinglleft", "guilsinglright", "fi", "fl",
        "endash", "dagger", "daggerdbl", "periodcentered", "paragraph",
        "bullet", "quotesinglbase", "quotedblbase", "quotedblright",
        "guillemotright", "ellipsis", "perthousand", "questiondown",
        "grave", "acute", "circumflex", "tilde", "macron", "breve",
        "dotaccent", "dieresis", "ring", "cedilla", "hungarumlaut",
        "ogonek", "caron", "emdash", "AE", "ordfeminine", "Lslash",
        "Oslash", "OE", "ordmasculine", "ae", "dotlessi", "lslash",
        "oslash", "oe", "germandbls",
        // SID 150..170
        "onesuperior", "logicalnot", "mu", "trademark", "Eth", "onehalf",
        "plusminus", "Thorn", "onequarter", "divide", "brokenbar", "degree",
        "thorn", "threequarters", "twosuperior", "registered", "minus",
        "eth", "multiply", "threesuperior", "copyright",
        // SID 171..228 — accented Latin
        "Aacute", "Acircumflex", "Adieresis", "Agrave", "Aring", "Atilde",
        "Ccedilla", "Eacute", "Ecircumflex", "Edieresis", "Egrave",
        "Iacute", "Icircumflex", "Idieresis", "Igrave", "Ntilde", "Oacute",
        "Ocircumflex", "Odieresis", "Ograve", "Otilde", "Scaron", "Uacute",
        "Ucircumflex", "Udieresis", "Ugrave", "Yacute", "Ydieresis",
        "Zcaron", "aacute", "acircumflex", "adieresis", "agrave", "aring",
        "atilde", "ccedilla", "eacute", "ecircumflex", "edieresis",
        "egrave", "iacute", "icircumflex", "idieresis", "igrave", "ntilde",
        "oacute", "ocircumflex", "odieresis", "ograve", "otilde", "scaron",
        "uacute", "ucircumflex", "udieresis", "ugrave", "yacute",
        "ydieresis", "zcaron",
        // SID 229..248
        "exclamsmall", "Hungarumlautsmall", "dollaroldstyle",
        "dollarsuperior", "ampersandsmall", "Acutesmall",
        "parenleftsuperior", "parenrightsuperior", "twodotenleader",
        "onedotenleader", "zerooldstyle", "oneoldstyle", "twooldstyle",
        "threeoldstyle", "fouroldstyle", "fiveoldstyle", "sixoldstyle",
        "sevenoldstyle", "eightoldstyle", "nineoldstyle",
        // SID 249..268
        "commasuperior", "threequartersemdash", "periodsuperior",
        "questionsmall", "asuperior", "bsuperior", "centsuperior",
        "dsuperior", "esuperior", "isuperior", "lsuperior", "msuperior",
        "nsuperior", "osuperior", "rsuperior", "ssuperior", "tsuperior",
        "ff", "ffi", "ffl",
        // SID 269..298
        "parenleftinferior", "parenrightinferior", "Circumflexsmall",
        "hyphensuperior", "Gravesmall", "Asmall", "Bsmall", "Csmall",
        "Dsmall", "Esmall", "Fsmall", "Gsmall", "Hsmall", "Ismall",
        "Jsmall", "Ksmall", "Lsmall", "Msmall", "Nsmall", "Osmall",
        "Psmall", "Qsmall", "Rsmall", "Ssmall", "Tsmall", "Usmall",
        "Vsmall", "Wsmall", "Xsmall", "Ysmall",
        // SID 299..318
        "Zsmall", "colonmonetary", "onefitted", "rupiah", "Tildesmall",
        "exclamdownsmall", "centoldstyle", "Lslashsmall", "Scaronsmall",
        "Zcaronsmall", "Dieresissmall", "Brevesmall", "Caronsmall",
        "Dotaccentsmall", "Macronsmall", "figuredash", "hypheninferior",
        "Ogoneksmall", "Ringsmall", "Cedillasmall",
        // SID 319..346
        "questiondownsmall", "oneeighth", "threeeighths", "fiveeighths",
        "seveneighths", "onethird", "twothirds", "zerosuperior",
        "foursuperior", "fivesuperior", "sixsuperior", "sevensuperior",
        "eightsuperior", "ninesuperior", "zeroinferior", "oneinferior",
        "twoinferior", "threeinferior", "fourinferior", "fiveinferior",
        "sixinferior", "seveninferior", "eightinferior", "nineinferior",
        "centinferior", "dollarinferior", "periodinferior", "commainferior",
        // SID 347..378 — small caps
        "Agravesmall", "Aacutesmall", "Acircumflexsmall", "Atildesmall",
        "Adieresissmall", "Aringsmall", "AEsmall", "Ccedillasmall",
        "Egravesmall", "Eacutesmall", "Ecircumflexsmall", "Edieresissmall",
        "Igravesmall", "Iacutesmall", "Icircumflexsmall", "Idieresissmall",
        "Ethsmall", "Ntildesmall", "Ogravesmall", "Oacutesmall",
        "Ocircumflexsmall", "Otildesmall", "Odieresissmall", "OEsmall",
        "Oslashsmall", "Ugravesmall", "Uacutesmall", "Ucircumflexsmall",
        "Udieresissmall", "Yacutesmall", "Thornsmall", "Ydieresissmall",
        // SID 379..390
        "001.000", "001.001", "001.002", "001.003", "Black", "Bold",
        "Book", "Light", "Medium", "Regular", "Roman", "Semibold"
    };
}
