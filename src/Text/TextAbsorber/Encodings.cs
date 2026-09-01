using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    // ────────────────────────────────────────────────────────────────────────
    // WinAnsiEncoding table (codes 128-255) — PDF spec Table D.1
    // ────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<byte, char> WinAnsiEncoding = new()
    {
        [128] = '\u20AC', // Euro sign
        [130] = '\u201A', // single low-9 quotation mark
        [131] = '\u0192', // f with hook
        [132] = '\u201E', // double low-9 quotation mark
        [133] = '\u2026', // horizontal ellipsis
        [134] = '\u2020', // dagger
        [135] = '\u2021', // double dagger
        [136] = '\u02C6', // modifier letter circumflex accent
        [137] = '\u2030', // per mille sign
        [138] = '\u0160', // S with caron
        [139] = '\u2039', // single left-pointing angle quotation mark
        [140] = '\u0152', // OE ligature
        [142] = '\u017D', // Z with caron
        [145] = '\u2018', // left single quotation mark
        [146] = '\u2019', // right single quotation mark
        [147] = '\u201C', // left double quotation mark
        [148] = '\u201D', // right double quotation mark
        [149] = '\u2022', // bullet
        [150] = '\u2013', // en dash
        [151] = '\u2014', // em dash
        [152] = '\u02DC', // small tilde
        [153] = '\u2122', // trade mark sign
        [154] = '\u0161', // s with caron
        [155] = '\u203A', // single right-pointing angle quotation mark
        [156] = '\u0153', // oe ligature
        [158] = '\u017E', // z with caron
        [159] = '\u0178', // Y with diaeresis
        [160] = '\u00A0', // no-break space
        [161] = '\u00A1', // inverted exclamation mark
        [162] = '\u00A2', // cent sign
        [163] = '\u00A3', // pound sign
        [164] = '\u00A4', // currency sign
        [165] = '\u00A5', // yen sign
        [166] = '\u00A6', // broken bar
        [167] = '\u00A7', // section sign
        [168] = '\u00A8', // diaeresis
        [169] = '\u00A9', // copyright sign
        [170] = '\u00AA', // feminine ordinal indicator
        [171] = '\u00AB', // left-pointing double angle quotation mark
        [172] = '\u00AC', // not sign
        [173] = '\u00AD', // soft hyphen
        [174] = '\u00AE', // registered sign
        [175] = '\u00AF', // macron
        [176] = '\u00B0', // degree sign
        [177] = '\u00B1', // plus-minus sign
        [178] = '\u00B2', // superscript two
        [179] = '\u00B3', // superscript three
        [180] = '\u00B4', // acute accent
        [181] = '\u00B5', // micro sign
        [182] = '\u00B6', // pilcrow sign
        [183] = '\u00B7', // middle dot
        [184] = '\u00B8', // cedilla
        [185] = '\u00B9', // superscript one
        [186] = '\u00BA', // masculine ordinal indicator
        [187] = '\u00BB', // right-pointing double angle quotation mark
        [188] = '\u00BC', // vulgar fraction one quarter
        [189] = '\u00BD', // vulgar fraction one half
        [190] = '\u00BE', // vulgar fraction three quarters
        [191] = '\u00BF', // inverted question mark
        [192] = '\u00C0', // A with grave
        [193] = '\u00C1', // A with acute
        [194] = '\u00C2', // A with circumflex
        [195] = '\u00C3', // A with tilde
        [196] = '\u00C4', // A with diaeresis
        [197] = '\u00C5', // A with ring above
        [198] = '\u00C6', // AE
        [199] = '\u00C7', // C with cedilla
        [200] = '\u00C8', // E with grave
        [201] = '\u00C9', // E with acute
        [202] = '\u00CA', // E with circumflex
        [203] = '\u00CB', // E with diaeresis
        [204] = '\u00CC', // I with grave
        [205] = '\u00CD', // I with acute
        [206] = '\u00CE', // I with circumflex
        [207] = '\u00CF', // I with diaeresis
        [208] = '\u00D0', // Eth
        [209] = '\u00D1', // N with tilde
        [210] = '\u00D2', // O with grave
        [211] = '\u00D3', // O with acute
        [212] = '\u00D4', // O with circumflex
        [213] = '\u00D5', // O with tilde
        [214] = '\u00D6', // O with diaeresis
        [215] = '\u00D7', // multiplication sign
        [216] = '\u00D8', // O with stroke
        [217] = '\u00D9', // U with grave
        [218] = '\u00DA', // U with acute
        [219] = '\u00DB', // U with circumflex
        [220] = '\u00DC', // U with diaeresis
        [221] = '\u00DD', // Y with acute
        [222] = '\u00DE', // Thorn
        [223] = '\u00DF', // sharp s
        [224] = '\u00E0', // a with grave
        [225] = '\u00E1', // a with acute
        [226] = '\u00E2', // a with circumflex
        [227] = '\u00E3', // a with tilde
        [228] = '\u00E4', // a with diaeresis
        [229] = '\u00E5', // a with ring above
        [230] = '\u00E6', // ae
        [231] = '\u00E7', // c with cedilla
        [232] = '\u00E8', // e with grave
        [233] = '\u00E9', // e with acute
        [234] = '\u00EA', // e with circumflex
        [235] = '\u00EB', // e with diaeresis
        [236] = '\u00EC', // i with grave
        [237] = '\u00ED', // i with acute
        [238] = '\u00EE', // i with circumflex
        [239] = '\u00EF', // i with diaeresis
        [240] = '\u00F0', // eth
        [241] = '\u00F1', // n with tilde
        [242] = '\u00F2', // o with grave
        [243] = '\u00F3', // o with acute
        [244] = '\u00F4', // o with circumflex
        [245] = '\u00F5', // o with tilde
        [246] = '\u00F6', // o with diaeresis
        [247] = '\u00F7', // division sign
        [248] = '\u00F8', // o with stroke
        [249] = '\u00F9', // u with grave
        [250] = '\u00FA', // u with acute
        [251] = '\u00FB', // u with circumflex
        [252] = '\u00FC', // u with diaeresis
        [253] = '\u00FD', // y with acute
        [254] = '\u00FE', // thorn
        [255] = '\u00FF', // y with diaeresis
    };

    /// <summary>Unicode codepoint for a MacRomanEncoding code (ASCII passes
    /// through; 0 for an unmapped high code). Used by width-metric code that
    /// must translate MacRoman codes before a WinAnsi-shaped table lookup.</summary>
    internal static int MacRomanCodeToUnicode(int code)
    {
        if (code is >= 0 and < 0x80) return code;
        return code is >= 0 and <= 255 && MacRomanEncoding.TryGetValue((byte)code, out var ch) ? ch : 0;
    }

    // ────────────────────────────────────────────────────────────────────────
    // MacRomanEncoding table (codes 128-255) — PDF spec Table D.2
    // ────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<byte, char> MacRomanEncoding = new()
    {
        [128] = '\u00C4', // A with diaeresis
        [129] = '\u00C5', // A with ring above
        [130] = '\u00C7', // C with cedilla
        [131] = '\u00C9', // E with acute
        [132] = '\u00D1', // N with tilde
        [133] = '\u00D6', // O with diaeresis
        [134] = '\u00DC', // U with diaeresis
        [135] = '\u00E1', // a with acute
        [136] = '\u00E0', // a with grave
        [137] = '\u00E2', // a with circumflex
        [138] = '\u00E4', // a with diaeresis
        [139] = '\u00E3', // a with tilde
        [140] = '\u00E5', // a with ring above
        [141] = '\u00E7', // c with cedilla
        [142] = '\u00E9', // e with acute
        [143] = '\u00E8', // e with grave
        [144] = '\u00EA', // e with circumflex
        [145] = '\u00EB', // e with diaeresis
        [146] = '\u00ED', // i with acute
        [147] = '\u00EC', // i with grave
        [148] = '\u00EE', // i with circumflex
        [149] = '\u00EF', // i with diaeresis
        [150] = '\u00F1', // n with tilde
        [151] = '\u00F3', // o with acute
        [152] = '\u00F2', // o with grave
        [153] = '\u00F4', // o with circumflex
        [154] = '\u00F6', // o with diaeresis
        [155] = '\u00F5', // o with tilde
        [156] = '\u00FA', // u with acute
        [157] = '\u00F9', // u with grave
        [158] = '\u00FB', // u with circumflex
        [159] = '\u00FC', // u with diaeresis
        [160] = '\u2020', // dagger
        [161] = '\u00B0', // degree sign
        [162] = '\u00A2', // cent sign
        [163] = '\u00A3', // pound sign
        [164] = '\u00A7', // section sign
        [165] = '\u2022', // bullet
        [166] = '\u00B6', // pilcrow sign
        [167] = '\u00DF', // sharp s
        [168] = '\u00AE', // registered sign
        [169] = '\u00A9', // copyright sign
        [170] = '\u2122', // trade mark sign
        [171] = '\u00B4', // acute accent
        [172] = '\u00A8', // diaeresis
        [174] = '\u00C6', // AE
        [175] = '\u00D8', // O with stroke
        [177] = '\u00B1', // plus-minus sign
        [180] = '\u00A5', // yen sign
        [181] = '\u00B5', // micro sign
        [187] = '\u00AA', // feminine ordinal indicator
        [188] = '\u00BA', // masculine ordinal indicator
        [190] = '\u00E6', // ae
        [191] = '\u00F8', // o with stroke
        [192] = '\u00BF', // inverted question mark
        [193] = '\u00A1', // inverted exclamation mark
        [194] = '\u00AC', // not sign
        [196] = '\u0192', // f with hook
        [199] = '\u00AB', // left-pointing double angle quotation mark
        [200] = '\u00BB', // right-pointing double angle quotation mark
        [201] = '\u2026', // horizontal ellipsis
        [202] = '\u00A0', // no-break space
        [203] = '\u00C0', // A with grave
        [204] = '\u00C3', // A with tilde
        [205] = '\u00D5', // O with tilde
        [206] = '\u0152', // OE ligature
        [207] = '\u0153', // oe ligature
        [208] = '\u2013', // en dash
        [209] = '\u2014', // em dash
        [210] = '\u201C', // left double quotation mark
        [211] = '\u201D', // right double quotation mark
        [212] = '\u2018', // left single quotation mark
        [213] = '\u2019', // right single quotation mark
        [214] = '\u00F7', // division sign
        [215] = '\u25CA', // lozenge
        [218] = '\u00FF', // y with diaeresis
        [219] = '\u0178', // Y with diaeresis
        [220] = '\u2044', // fraction slash
        [222] = '\uFB01', // fi ligature
        [223] = '\uFB02', // fl ligature
        [226] = '\u00AE', // registered sign (alt)
        [227] = '\u00A9', // copyright sign (alt)
        [228] = '\u2122', // trade mark sign (alt)
        [229] = '\u00B4', // acute accent (alt)
        [230] = '\u00A8', // diaeresis (alt)
        [232] = '\u00C8', // E with grave
        [233] = '\u00CA', // E with circumflex
        [234] = '\u00CB', // E with diaeresis
        [235] = '\u00CC', // I with grave
        [236] = '\u00CD', // I with acute
        [237] = '\u00CE', // I with circumflex
        [238] = '\u00CF', // I with diaeresis
        [241] = '\u00D2', // O with grave
        [242] = '\u00D3', // O with acute
        [243] = '\u00D4', // O with circumflex
        [245] = '\u00D2', // O with grave (alt)
        [246] = '\u00DA', // U with acute
        [247] = '\u00DB', // U with circumflex
        [248] = '\u00D9', // U with grave
        [249] = '\u0131', // dotless i
        [250] = '\u02C6', // modifier letter circumflex accent
        [251] = '\u02DC', // small tilde
        [252] = '\u00AF', // macron
        [253] = '\u02D8', // breve
        [254] = '\u02D9', // dot above
        [255] = '\u02DA', // ring above
    };

    // ────────────────────────────────────────────────────────────────────────
    // Adobe Glyph List (core subset) — glyph name to Unicode mapping
    // ────────────────────────────────────────────────────────────────────────
    internal static readonly Dictionary<string, string> GlyphNameToUnicode = new(StringComparer.Ordinal)
    {
        // ASCII printable characters
        ["space"] = "\u0020",
        ["exclam"] = "\u0021",
        ["quotedbl"] = "\u0022",
        ["numbersign"] = "\u0023",
        ["dollar"] = "\u0024",
        ["percent"] = "\u0025",
        ["ampersand"] = "\u0026",
        ["quotesingle"] = "\u0027",
        ["parenleft"] = "\u0028",
        ["parenright"] = "\u0029",
        ["asterisk"] = "\u002A",
        ["plus"] = "\u002B",
        ["comma"] = "\u002C",
        ["hyphen"] = "\u002D",
        ["period"] = "\u002E",
        ["slash"] = "\u002F",
        ["zero"] = "\u0030",
        ["one"] = "\u0031",
        ["two"] = "\u0032",
        ["three"] = "\u0033",
        ["four"] = "\u0034",
        ["five"] = "\u0035",
        ["six"] = "\u0036",
        ["seven"] = "\u0037",
        ["eight"] = "\u0038",
        ["nine"] = "\u0039",
        ["colon"] = "\u003A",
        ["semicolon"] = "\u003B",
        ["less"] = "\u003C",
        ["equal"] = "\u003D",
        ["greater"] = "\u003E",
        ["question"] = "\u003F",
        ["at"] = "\u0040",
        ["A"] = "\u0041",
        ["B"] = "\u0042",
        ["C"] = "\u0043",
        ["D"] = "\u0044",
        ["E"] = "\u0045",
        ["F"] = "\u0046",
        ["G"] = "\u0047",
        ["H"] = "\u0048",
        ["I"] = "\u0049",
        ["J"] = "\u004A",
        ["K"] = "\u004B",
        ["L"] = "\u004C",
        ["M"] = "\u004D",
        ["N"] = "\u004E",
        ["O"] = "\u004F",
        ["P"] = "\u0050",
        ["Q"] = "\u0051",
        ["R"] = "\u0052",
        ["S"] = "\u0053",
        ["T"] = "\u0054",
        ["U"] = "\u0055",
        ["V"] = "\u0056",
        ["W"] = "\u0057",
        ["X"] = "\u0058",
        ["Y"] = "\u0059",
        ["Z"] = "\u005A",
        ["bracketleft"] = "\u005B",
        ["backslash"] = "\u005C",
        ["bracketright"] = "\u005D",
        ["asciicircum"] = "\u005E",
        ["underscore"] = "\u005F",
        ["grave"] = "\u0060",
        ["a"] = "\u0061",
        ["b"] = "\u0062",
        ["c"] = "\u0063",
        ["d"] = "\u0064",
        ["e"] = "\u0065",
        ["f"] = "\u0066",
        ["g"] = "\u0067",
        ["h"] = "\u0068",
        ["i"] = "\u0069",
        ["j"] = "\u006A",
        ["k"] = "\u006B",
        ["l"] = "\u006C",
        ["m"] = "\u006D",
        ["n"] = "\u006E",
        ["o"] = "\u006F",
        ["p"] = "\u0070",
        ["q"] = "\u0071",
        ["r"] = "\u0072",
        ["s"] = "\u0073",
        ["t"] = "\u0074",
        ["u"] = "\u0075",
        ["v"] = "\u0076",
        ["w"] = "\u0077",
        ["x"] = "\u0078",
        ["y"] = "\u0079",
        ["z"] = "\u007A",
        ["braceleft"] = "\u007B",
        ["bar"] = "\u007C",
        ["braceright"] = "\u007D",
        ["asciitilde"] = "\u007E",

        // Common punctuation and symbols
        ["bullet"] = "\u2022",
        ["endash"] = "\u2013",
        ["emdash"] = "\u2014",
        ["quoteleft"] = "\u2018",
        ["quoteright"] = "\u2019",
        ["quotedblleft"] = "\u201C",
        ["quotedblright"] = "\u201D",
        ["quotesinglbase"] = "\u201A",
        ["quotedblbase"] = "\u201E",
        ["dagger"] = "\u2020",
        ["daggerdbl"] = "\u2021",
        ["ellipsis"] = "\u2026",
        ["perthousand"] = "\u2030",
        ["guilsinglleft"] = "\u2039",
        ["guilsinglright"] = "\u203A",
        ["trademark"] = "\u2122",
        ["minus"] = "\u2212",
        ["Euro"] = "\u20AC",

        // Latin-1 supplement
        ["exclamdown"] = "\u00A1",
        ["cent"] = "\u00A2",
        ["sterling"] = "\u00A3",
        ["currency"] = "\u00A4",
        ["yen"] = "\u00A5",
        ["brokenbar"] = "\u00A6",
        ["section"] = "\u00A7",
        ["dieresis"] = "\u00A8",
        ["copyright"] = "\u00A9",
        ["ordfeminine"] = "\u00AA",
        ["guillemotleft"] = "\u00AB",
        ["logicalnot"] = "\u00AC",
        ["registered"] = "\u00AE",
        ["macron"] = "\u00AF",
        ["degree"] = "\u00B0",
        ["plusminus"] = "\u00B1",
        ["twosuperior"] = "\u00B2",
        ["threesuperior"] = "\u00B3",
        ["acute"] = "\u00B4",
        ["mu"] = "\u00B5",
        ["paragraph"] = "\u00B6",
        ["periodcentered"] = "\u00B7",
        ["cedilla"] = "\u00B8",
        ["onesuperior"] = "\u00B9",
        ["ordmasculine"] = "\u00BA",
        ["guillemotright"] = "\u00BB",
        ["onequarter"] = "\u00BC",
        ["onehalf"] = "\u00BD",
        ["threequarters"] = "\u00BE",
        ["questiondown"] = "\u00BF",

        // Accented uppercase
        ["Agrave"] = "\u00C0",
        ["Aacute"] = "\u00C1",
        ["Acircumflex"] = "\u00C2",
        ["Atilde"] = "\u00C3",
        ["Adieresis"] = "\u00C4",
        ["Aring"] = "\u00C5",
        ["AE"] = "\u00C6",
        ["Ccedilla"] = "\u00C7",
        ["Egrave"] = "\u00C8",
        ["Eacute"] = "\u00C9",
        ["Ecircumflex"] = "\u00CA",
        ["Edieresis"] = "\u00CB",
        ["Igrave"] = "\u00CC",
        ["Iacute"] = "\u00CD",
        ["Icircumflex"] = "\u00CE",
        ["Idieresis"] = "\u00CF",
        ["Eth"] = "\u00D0",
        ["Ntilde"] = "\u00D1",
        ["Ograve"] = "\u00D2",
        ["Oacute"] = "\u00D3",
        ["Ocircumflex"] = "\u00D4",
        ["Otilde"] = "\u00D5",
        ["Odieresis"] = "\u00D6",
        ["multiply"] = "\u00D7",
        ["Oslash"] = "\u00D8",
        ["Ugrave"] = "\u00D9",
        ["Uacute"] = "\u00DA",
        ["Ucircumflex"] = "\u00DB",
        ["Udieresis"] = "\u00DC",
        ["Yacute"] = "\u00DD",
        ["Thorn"] = "\u00DE",
        ["germandbls"] = "\u00DF",

        // Accented lowercase
        ["agrave"] = "\u00E0",
        ["aacute"] = "\u00E1",
        ["acircumflex"] = "\u00E2",
        ["atilde"] = "\u00E3",
        ["adieresis"] = "\u00E4",
        ["aring"] = "\u00E5",
        ["ae"] = "\u00E6",
        ["ccedilla"] = "\u00E7",
        ["egrave"] = "\u00E8",
        ["eacute"] = "\u00E9",
        ["ecircumflex"] = "\u00EA",
        ["edieresis"] = "\u00EB",
        ["igrave"] = "\u00EC",
        ["iacute"] = "\u00ED",
        ["icircumflex"] = "\u00EE",
        ["idieresis"] = "\u00EF",
        ["eth"] = "\u00F0",
        ["ntilde"] = "\u00F1",
        ["ograve"] = "\u00F2",
        ["oacute"] = "\u00F3",
        ["ocircumflex"] = "\u00F4",
        ["otilde"] = "\u00F5",
        ["odieresis"] = "\u00F6",
        ["divide"] = "\u00F7",
        ["oslash"] = "\u00F8",
        ["ugrave"] = "\u00F9",
        ["uacute"] = "\u00FA",
        ["ucircumflex"] = "\u00FB",
        ["udieresis"] = "\u00FC",
        ["yacute"] = "\u00FD",
        ["thorn"] = "\u00FE",
        ["ydieresis"] = "\u00FF",

        // Latin Extended-A
        ["Amacron"] = "\u0100", ["amacron"] = "\u0101",
        ["Abreve"] = "\u0102", ["abreve"] = "\u0103",
        ["Aogonek"] = "\u0104", ["aogonek"] = "\u0105",
        ["Cacute"] = "\u0106", ["cacute"] = "\u0107",
        ["Ccircumflex"] = "\u0108", ["ccircumflex"] = "\u0109",
        ["Cdotaccent"] = "\u010A", ["cdotaccent"] = "\u010B",
        ["Ccaron"] = "\u010C", ["ccaron"] = "\u010D",
        ["Dcaron"] = "\u010E", ["dcaron"] = "\u010F",
        ["Dcroat"] = "\u0110", ["dcroat"] = "\u0111",
        ["Emacron"] = "\u0112", ["emacron"] = "\u0113",
        ["Ebreve"] = "\u0114", ["ebreve"] = "\u0115",
        ["Edotaccent"] = "\u0116", ["edotaccent"] = "\u0117",
        ["Eogonek"] = "\u0118", ["eogonek"] = "\u0119",
        ["Ecaron"] = "\u011A", ["ecaron"] = "\u011B",
        ["Gcircumflex"] = "\u011C", ["gcircumflex"] = "\u011D",
        ["Gbreve"] = "\u011E", ["gbreve"] = "\u011F",
        ["Gdotaccent"] = "\u0120", ["gdotaccent"] = "\u0121",
        ["Gcommaaccent"] = "\u0122", ["gcommaaccent"] = "\u0123",
        ["Hcircumflex"] = "\u0124", ["hcircumflex"] = "\u0125",
        ["Hbar"] = "\u0126", ["hbar"] = "\u0127",
        ["Itilde"] = "\u0128", ["itilde"] = "\u0129",
        ["Imacron"] = "\u012A", ["imacron"] = "\u012B",
        ["Ibreve"] = "\u012C", ["ibreve"] = "\u012D",
        ["Iogonek"] = "\u012E", ["iogonek"] = "\u012F",
        ["Idotaccent"] = "\u0130", ["dotlessi"] = "\u0131",
        ["IJ"] = "\u0132", ["ij"] = "\u0133",
        ["Jcircumflex"] = "\u0134", ["jcircumflex"] = "\u0135",
        ["Kcommaaccent"] = "\u0136", ["kcommaaccent"] = "\u0137",
        ["kgreenlandic"] = "\u0138",
        ["Lacute"] = "\u0139", ["lacute"] = "\u013A",
        ["Lcommaaccent"] = "\u013B", ["lcommaaccent"] = "\u013C",
        ["Lcaron"] = "\u013D", ["lcaron"] = "\u013E",
        ["Ldot"] = "\u013F", ["ldot"] = "\u0140",
        ["Lslash"] = "\u0141", ["lslash"] = "\u0142",
        ["Nacute"] = "\u0143", ["nacute"] = "\u0144",
        ["Ncommaaccent"] = "\u0145", ["ncommaaccent"] = "\u0146",
        ["Ncaron"] = "\u0147", ["ncaron"] = "\u0148",
        ["napostrophe"] = "\u0149",
        ["Eng"] = "\u014A", ["eng"] = "\u014B",
        ["Omacron"] = "\u014C", ["omacron"] = "\u014D",
        ["Obreve"] = "\u014E", ["obreve"] = "\u014F",
        ["Ohungarumlaut"] = "\u0150", ["ohungarumlaut"] = "\u0151",
        ["OE"] = "\u0152", ["oe"] = "\u0153",
        ["Racute"] = "\u0154", ["racute"] = "\u0155",
        ["Rcommaaccent"] = "\u0156", ["rcommaaccent"] = "\u0157",
        ["Rcaron"] = "\u0158", ["rcaron"] = "\u0159",
        ["Sacute"] = "\u015A", ["sacute"] = "\u015B",
        ["Scircumflex"] = "\u015C", ["scircumflex"] = "\u015D",
        ["Scedilla"] = "\u015E", ["scedilla"] = "\u015F",
        ["Scaron"] = "\u0160", ["scaron"] = "\u0161",
        ["Tcommaaccent"] = "\u0162", ["tcommaaccent"] = "\u0163",
        ["Tcaron"] = "\u0164", ["tcaron"] = "\u0165",
        ["Tbar"] = "\u0166", ["tbar"] = "\u0167",
        ["Utilde"] = "\u0168", ["utilde"] = "\u0169",
        ["Umacron"] = "\u016A", ["umacron"] = "\u016B",
        ["Ubreve"] = "\u016C", ["ubreve"] = "\u016D",
        ["Uring"] = "\u016E", ["uring"] = "\u016F",
        ["Uhungarumlaut"] = "\u0170", ["uhungarumlaut"] = "\u0171",
        ["Uogonek"] = "\u0172", ["uogonek"] = "\u0173",
        ["Wcircumflex"] = "\u0174", ["wcircumflex"] = "\u0175",
        ["Ycircumflex"] = "\u0176", ["ycircumflex"] = "\u0177",
        ["Ydieresis"] = "\u0178",
        ["Zacute"] = "\u0179", ["zacute"] = "\u017A",
        ["Zdotaccent"] = "\u017B", ["zdotaccent"] = "\u017C",
        ["Zcaron"] = "\u017D", ["zcaron"] = "\u017E",
        ["longs"] = "\u017F",

        // Latin Extended-B
        ["florin"] = "\u0192",
        ["Aringacute"] = "\u01FA", ["aringacute"] = "\u01FB",
        ["AEacute"] = "\u01FC", ["aeacute"] = "\u01FD",

        // Spacing Modifier Letters
        ["circumflex"] = "\u02C6", ["caron"] = "\u02C7",
        ["breve"] = "\u02D8", ["dotaccent"] = "\u02D9",
        ["ring"] = "\u02DA", ["ogonek"] = "\u02DB",
        ["tilde"] = "\u02DC", ["hungarumlaut"] = "\u02DD",

        // Greek
        ["Alpha"] = "\u0391", ["Beta"] = "\u0392", ["Gamma"] = "\u0393", ["Delta"] = "\u0394",
        ["Epsilon"] = "\u0395", ["Zeta"] = "\u0396", ["Eta"] = "\u0397", ["Theta"] = "\u0398",
        ["Iota"] = "\u0399", ["Kappa"] = "\u039A", ["Lambda"] = "\u039B", ["Mu"] = "\u039C",
        ["Nu"] = "\u039D", ["Xi"] = "\u039E", ["Omicron"] = "\u039F", ["Pi"] = "\u03A0",
        ["Rho"] = "\u03A1", ["Sigma"] = "\u03A3", ["Tau"] = "\u03A4", ["Upsilon"] = "\u03A5",
        ["Phi"] = "\u03A6", ["Chi"] = "\u03A7", ["Psi"] = "\u03A8", ["Omega"] = "\u03A9",
        ["alpha"] = "\u03B1", ["beta"] = "\u03B2", ["gamma"] = "\u03B3", ["delta"] = "\u03B4",
        ["epsilon"] = "\u03B5", ["zeta"] = "\u03B6", ["eta"] = "\u03B7", ["theta"] = "\u03B8",
        ["iota"] = "\u03B9", ["kappa"] = "\u03BA", ["lambda"] = "\u03BB",
        ["nu"] = "\u03BD", ["xi"] = "\u03BE", ["omicron"] = "\u03BF", ["pi"] = "\u03C0",
        ["rho"] = "\u03C1", ["sigma"] = "\u03C3", ["tau"] = "\u03C4", ["upsilon"] = "\u03C5",
        ["phi"] = "\u03C6", ["chi"] = "\u03C7", ["psi"] = "\u03C8", ["omega"] = "\u03C9",
        ["sigma1"] = "\u03C2", ["theta1"] = "\u03D1", ["Upsilon1"] = "\u03D2",
        ["phi1"] = "\u03D5", ["omega1"] = "\u03D6",
        // Greek with tonos / dialytika (modern Greek, AGL standard names)
        ["Alphatonos"] = "\u0386", ["Epsilontonos"] = "\u0388",
        ["Etatonos"] = "\u0389", ["Iotatonos"] = "\u038A",
        ["Omicrontonos"] = "\u038C", ["Upsilontonos"] = "\u038E",
        ["Omegatonos"] = "\u038F",
        ["Iotadieresis"] = "\u03AA", ["Upsilondieresis"] = "\u03AB",
        ["alphatonos"] = "\u03AC", ["epsilontonos"] = "\u03AD",
        ["etatonos"] = "\u03AE", ["iotatonos"] = "\u03AF",
        ["upsilondieresistonos"] = "\u03B0",
        ["iotadieresis"] = "\u03CA", ["upsilondieresis"] = "\u03CB",
        ["omicrontonos"] = "\u03CC", ["upsilontonos"] = "\u03CD",
        ["omegatonos"] = "\u03CE",
        ["iotadieresistonos"] = "\u0390",

        // Cyrillic (afii series)
        ["afii10017"] = "\u0410", ["afii10018"] = "\u0411", ["afii10019"] = "\u0412", ["afii10020"] = "\u0413",
        ["afii10021"] = "\u0414", ["afii10022"] = "\u0415", ["afii10023"] = "\u0401", ["afii10024"] = "\u0416",
        ["afii10025"] = "\u0417", ["afii10026"] = "\u0418", ["afii10027"] = "\u0419", ["afii10028"] = "\u041A",
        ["afii10029"] = "\u041B", ["afii10030"] = "\u041C", ["afii10031"] = "\u041D", ["afii10032"] = "\u041E",
        ["afii10033"] = "\u041F", ["afii10034"] = "\u0420", ["afii10035"] = "\u0421", ["afii10036"] = "\u0422",
        ["afii10037"] = "\u0423", ["afii10038"] = "\u0424", ["afii10039"] = "\u0425", ["afii10040"] = "\u0426",
        ["afii10041"] = "\u0427", ["afii10042"] = "\u0428", ["afii10043"] = "\u0429", ["afii10044"] = "\u042A",
        ["afii10045"] = "\u042B", ["afii10046"] = "\u042C", ["afii10047"] = "\u042D", ["afii10048"] = "\u042E",
        ["afii10049"] = "\u042F",
        ["afii10065"] = "\u0430", ["afii10066"] = "\u0431", ["afii10067"] = "\u0432", ["afii10068"] = "\u0433",
        ["afii10069"] = "\u0434", ["afii10070"] = "\u0435", ["afii10071"] = "\u0451", ["afii10072"] = "\u0436",
        ["afii10073"] = "\u0437", ["afii10074"] = "\u0438", ["afii10075"] = "\u0439", ["afii10076"] = "\u043A",
        ["afii10077"] = "\u043B", ["afii10078"] = "\u043C", ["afii10079"] = "\u043D", ["afii10080"] = "\u043E",
        ["afii10081"] = "\u043F", ["afii10082"] = "\u0440", ["afii10083"] = "\u0441", ["afii10084"] = "\u0442",
        ["afii10085"] = "\u0443", ["afii10086"] = "\u0444", ["afii10087"] = "\u0445", ["afii10088"] = "\u0446",
        ["afii10089"] = "\u0447", ["afii10090"] = "\u0448", ["afii10091"] = "\u0449", ["afii10092"] = "\u044A",
        ["afii10093"] = "\u044B", ["afii10094"] = "\u044C", ["afii10095"] = "\u044D", ["afii10096"] = "\u044E",
        ["afii10097"] = "\u044F",
        // Additional Cyrillic (Bulgarian, Serbian, Ukrainian)
        ["afii10050"] = "\u0490", ["afii10098"] = "\u0491",
        ["afii10051"] = "\u0402", ["afii10099"] = "\u0452",
        ["afii10052"] = "\u0403", ["afii10100"] = "\u0453",
        ["afii10053"] = "\u0404", ["afii10101"] = "\u0454",
        ["afii10054"] = "\u0405", ["afii10102"] = "\u0455",
        ["afii10055"] = "\u0406", ["afii10103"] = "\u0456",
        ["afii10056"] = "\u0407", ["afii10104"] = "\u0457",
        ["afii10057"] = "\u0408", ["afii10105"] = "\u0458",
        ["afii10058"] = "\u0409", ["afii10106"] = "\u0459",
        ["afii10059"] = "\u040A", ["afii10107"] = "\u045A",
        ["afii10060"] = "\u040B", ["afii10108"] = "\u045B",
        ["afii10061"] = "\u040C", ["afii10109"] = "\u045C",
        ["afii10062"] = "\u040E", ["afii10110"] = "\u045E",
        ["afii10145"] = "\u040F", ["afii10193"] = "\u045F",
        ["afii10146"] = "\u0462", ["afii10194"] = "\u0463",
        ["afii10147"] = "\u0472", ["afii10195"] = "\u0473",
        ["afii10148"] = "\u0474", ["afii10196"] = "\u0475",

        // General Punctuation & Typography
        ["afii00208"] = "\u2015",
        ["onedotenleader"] = "\u2024", ["twodotenleader"] = "\u2025",
        ["minute"] = "\u2032", ["second"] = "\u2033",
        ["sfthyphen"] = "\u00AD",

        // Mathematical \u2014 common operators and relations
        ["radical"] = "\u221A", ["infinity"] = "\u221E", ["integral"] = "\u222B",
        ["approxequal"] = "\u2248", ["notequal"] = "\u2260",
        ["lessequal"] = "\u2264", ["greaterequal"] = "\u2265",
        ["partialdiff"] = "\u2202", ["summation"] = "\u2211",
        ["product"] = "\u220F", ["lozenge"] = "\u25CA",
        ["middot"] = "\u00B7",
        // Set-theory and additional math (Adobe Glyph List standard names)
        ["universal"] = "\u2200", ["existential"] = "\u2203",
        ["element"] = "\u2208", ["notelement"] = "\u2209",
        ["suchthat"] = "\u220B",
        ["minus"] = "\u2212", ["plusminus"] = "\u00B1", ["multiply"] = "\u00D7", ["divide"] = "\u00F7",
        ["asteriskmath"] = "\u2217", ["proportional"] = "\u221D",
        ["angle"] = "\u2220", ["logicaland"] = "\u2227", ["logicalor"] = "\u2228",
        ["intersection"] = "\u2229", ["union"] = "\u222A",
        ["therefore"] = "\u2234", ["similar"] = "\u223C",
        ["congruent"] = "\u2245", ["equivalence"] = "\u2261",
        ["propersubset"] = "\u2282", ["propersuperset"] = "\u2283",
        ["notsubset"] = "\u2284",
        ["reflexsubset"] = "\u2286", ["reflexsuperset"] = "\u2287",
        ["perpendicular"] = "\u22A5",
        ["dotmath"] = "\u22C5", ["bullet"] = "\u2022",
        // Arrows (single)
        ["arrowleft"] = "\u2190", ["arrowup"] = "\u2191",
        ["arrowright"] = "\u2192", ["arrowdown"] = "\u2193",
        ["arrowboth"] = "\u2194", ["arrowupdn"] = "\u2195",
        ["arrowupdnbse"] = "\u21A8",
        ["carriagereturn"] = "\u21B5",
        // Arrows (double)
        ["arrowdblleft"] = "\u21D0", ["arrowdblup"] = "\u21D1",
        ["arrowdblright"] = "\u21D2", ["arrowdbldown"] = "\u21D3",
        ["arrowdblboth"] = "\u21D4",

        // Currency
        ["euro"] = "\u20AC", ["afii08941"] = "\u20AC",

        // Ligatures
        ["fi"] = "\uFB01", ["fl"] = "\uFB02",
        ["ff"] = "\uFB00", ["ffi"] = "\uFB03", ["ffl"] = "\uFB04",

        // Letterlike Symbols
        ["afii61664"] = "\u200B", ["afii301"] = "\u200E", ["afii299"] = "\u200F",
        ["numero"] = "\u2116", ["estimated"] = "\u212E",

        // Box drawing / Geometric
        ["square"] = "\u25A1", ["triagup"] = "\u25B2", ["triagrt"] = "\u25BA",
        ["triagdn"] = "\u25BC", ["triaglf"] = "\u25C4",

        // Dingbats (common)
        ["a1"] = "\u2701", ["a2"] = "\u2702", ["a3"] = "\u2703", ["a4"] = "\u2704",
        ["a5"] = "\u260E", ["a6"] = "\u2706", ["a7"] = "\u2707", ["a8"] = "\u2708",
        ["a9"] = "\u2709", ["a10"] = "\u261B", ["a11"] = "\u261E",

        // Miscellaneous
        ["notdef"] = "\uFFFD", [".notdef"] = "\uFFFD",
        ["null"] = "\u0000", ["CR"] = "\u000D",

        // Additional common glyphs
        ["nbspace"] = "\u00A0", ["nonbreakingspace"] = "\u00A0",
        ["softhyphen"] = "\u00AD",
        ["fraction"] = "\u2044",
    };

    // ────────────────────────────────────────────────────────────────────────
    // Symbol font encoding — full 189-entry table
    // Source: Adobe Symbol font mapping, PDF32000_2008 §D.5
    // ────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<byte, char> SymbolEncoding = new()
    {
        // 0x20-0x3F: spacing, operators, digits
        [0x20] = '\u0020', // space
        [0x21] = '\u0021', // exclam
        [0x22] = '\u2200', // universal
        [0x23] = '\u0023', // numbersign
        [0x24] = '\u2203', // existential
        [0x25] = '\u0025', // percent
        [0x26] = '\u0026', // ampersand
        [0x27] = '\u220B', // suchthat
        [0x28] = '\u0028', // parenleft
        [0x29] = '\u0029', // parenright
        [0x2A] = '\u2217', // asteriskmath
        [0x2B] = '\u002B', // plus
        [0x2C] = '\u002C', // comma
        [0x2D] = '\u2212', // minus
        [0x2E] = '\u002E', // period
        [0x2F] = '\u002F', // slash
        [0x30] = '\u0030', [0x31] = '\u0031', [0x32] = '\u0032', [0x33] = '\u0033',
        [0x34] = '\u0034', [0x35] = '\u0035', [0x36] = '\u0036', [0x37] = '\u0037',
        [0x38] = '\u0038', [0x39] = '\u0039', // 0-9
        [0x3A] = '\u003A', // colon
        [0x3B] = '\u003B', // semicolon
        [0x3C] = '\u003C', // less
        [0x3D] = '\u003D', // equal
        [0x3E] = '\u003E', // greater
        [0x3F] = '\u003F', // question
        // 0x40: congruent
        [0x40] = '\u2245', // congruent
        // 0x41-0x5A: Greek uppercase
        [0x41] = '\u0391', // Alpha
        [0x42] = '\u0392', // Beta
        [0x43] = '\u03A7', // Chi
        [0x44] = '\u0394', // Delta
        [0x45] = '\u0395', // Epsilon
        [0x46] = '\u03A6', // Phi
        [0x47] = '\u0393', // Gamma
        [0x48] = '\u0397', // Eta
        [0x49] = '\u0399', // Iota
        [0x4A] = '\u03D1', // theta1
        [0x4B] = '\u039A', // Kappa
        [0x4C] = '\u039B', // Lambda
        [0x4D] = '\u039C', // Mu
        [0x4E] = '\u039D', // Nu
        [0x4F] = '\u039F', // Omicron
        [0x50] = '\u03A0', // Pi
        [0x51] = '\u0398', // Theta
        [0x52] = '\u03A1', // Rho
        [0x53] = '\u03A3', // Sigma
        [0x54] = '\u03A4', // Tau
        [0x55] = '\u03A5', // Upsilon
        [0x56] = '\u03C2', // sigma1
        [0x57] = '\u03A9', // Omega
        [0x58] = '\u039E', // Xi
        [0x59] = '\u03A8', // Psi
        [0x5A] = '\u0396', // Zeta
        [0x5B] = '\u005B', // bracketleft
        [0x5C] = '\u2234', // therefore
        [0x5D] = '\u005D', // bracketright
        [0x5E] = '\u22A5', // perpendicular
        [0x5F] = '\u005F', // underscore
        [0x60] = '\uF8E5', // radicalex (PUA)
        // 0x61-0x7A: Greek lowercase
        [0x61] = '\u03B1', // alpha
        [0x62] = '\u03B2', // beta
        [0x63] = '\u03C7', // chi
        [0x64] = '\u03B4', // delta
        [0x65] = '\u03B5', // epsilon
        [0x66] = '\u03C6', // phi
        [0x67] = '\u03B3', // gamma
        [0x68] = '\u03B7', // eta
        [0x69] = '\u03B9', // iota
        [0x6A] = '\u03D5', // phi1
        [0x6B] = '\u03BA', // kappa
        [0x6C] = '\u03BB', // lambda
        [0x6D] = '\u00B5', // mu \u2014 the glyph-list maps it to MICRO SIGN, not Greek mu
        [0x6E] = '\u03BD', // nu
        [0x6F] = '\u03BF', // omicron
        [0x70] = '\u03C0', // pi
        [0x71] = '\u03B8', // theta
        [0x72] = '\u03C1', // rho
        [0x73] = '\u03C3', // sigma
        [0x74] = '\u03C4', // tau
        [0x75] = '\u03C5', // upsilon
        [0x76] = '\u03D6', // omega1
        [0x77] = '\u03C9', // omega
        [0x78] = '\u03BE', // xi
        [0x79] = '\u03C8', // psi
        [0x7A] = '\u03B6', // zeta
        [0x7B] = '\u007B', // braceleft
        [0x7C] = '\u007C', // bar
        [0x7D] = '\u007D', // braceright
        [0x7E] = '\u223C', // similar
        // 0xA0-0xFE: extended symbols
        [0xA0] = '\u20AC', // Euro
        [0xA1] = '\u03D2', // Upsilon1
        [0xA2] = '\u2032', // prime
        [0xA3] = '\u2264', // lessequal
        [0xA4] = '\u2044', // fraction
        [0xA5] = '\u221E', // infinity
        [0xA6] = '\u0192', // florin
        [0xA7] = '\u2663', // club
        [0xA8] = '\u2666', // diamond
        [0xA9] = '\u2665', // heart
        [0xAA] = '\u2660', // spade
        [0xAB] = '\u2194', // arrowboth
        [0xAC] = '\u2190', // arrowleft
        [0xAD] = '\u2191', // arrowup
        [0xAE] = '\u2192', // arrowright
        [0xAF] = '\u2193', // arrowdown
        [0xB0] = '\u00B0', // degree
        [0xB1] = '\u00B1', // plusminus
        [0xB2] = '\u2033', // second
        [0xB3] = '\u2265', // greaterequal
        [0xB4] = '\u00D7', // multiply
        [0xB5] = '\u221D', // proportional
        [0xB6] = '\u2202', // partialdiff
        [0xB7] = '\u2022', // bullet
        [0xB8] = '\u00F7', // divide
        [0xB9] = '\u2260', // notequal
        [0xBA] = '\u2261', // equivalence
        [0xBB] = '\u2248', // approxequal
        [0xBC] = '\u2026', // ellipsis
        [0xBD] = '\uF8E6', // arrowvertex (PUA)
        [0xBE] = '\uF8E7', // arrowhorizex (PUA)
        [0xBF] = '\u21B5', // carriagereturn
        [0xC0] = '\u2135', // aleph
        [0xC1] = '\u2111', // Ifraktur
        [0xC2] = '\u211C', // Rfraktur
        [0xC3] = '\u2118', // weierstrass
        [0xC4] = '\u2297', // circlemultiply
        [0xC5] = '\u2295', // circleplus
        [0xC6] = '\u2205', // emptyset
        [0xC7] = '\u2229', // intersection
        [0xC8] = '\u222A', // union
        [0xC9] = '\u2283', // propersuperset
        [0xCA] = '\u2287', // reflexsuperset
        [0xCB] = '\u2284', // notsubset
        [0xCC] = '\u2282', // propersubset
        [0xCD] = '\u2286', // reflexsubset
        [0xCE] = '\u2208', // element
        [0xCF] = '\u2209', // notelement
        [0xD0] = '\u2220', // angle
        [0xD1] = '\u2207', // gradient
        [0xD2] = '\uF6DA', // registerserif (PUA)
        [0xD3] = '\uF6D9', // copyrightserif (PUA)
        [0xD4] = '\uF6DB', // trademarkserif (PUA)
        [0xD5] = '\u220F', // product
        [0xD6] = '\u221A', // radical
        [0xD7] = '\u22C5', // dotmath
        [0xD8] = '\u00AC', // logicalnot
        [0xD9] = '\u2227', // logicaland
        [0xDA] = '\u2228', // logicalor
        [0xDB] = '\u21D4', // arrowdblboth
        [0xDC] = '\u21D0', // arrowdblleft
        [0xDD] = '\u21D1', // arrowdblup
        [0xDE] = '\u21D2', // arrowdblright
        [0xDF] = '\u21D3', // arrowdbldown
        [0xE0] = '\u25CA', // lozenge
        [0xE1] = '\u2329', // angleleft
        [0xE2] = '\uF8E8', // registersans (PUA)
        [0xE3] = '\uF8E9', // copyrightsans (PUA)
        [0xE4] = '\uF8EA', // trademarksans (PUA)
        [0xE5] = '\u2211', // summation
        [0xE6] = '\uF8EB', // parenlefttp (PUA)
        [0xE7] = '\uF8EC', // parenleftex (PUA)
        [0xE8] = '\uF8ED', // parenleftbt (PUA)
        [0xE9] = '\uF8EE', // bracketlefttp (PUA)
        [0xEA] = '\uF8EF', // bracketleftex (PUA)
        [0xEB] = '\uF8F0', // bracketleftbt (PUA)
        [0xEC] = '\uF8F1', // bracelefttp (PUA)
        [0xED] = '\uF8F2', // braceleftmid (PUA)
        [0xEE] = '\uF8F3', // braceleftbt (PUA)
        [0xEF] = '\uF8F4', // braceex (PUA)
        [0xF1] = '\u232A', // angleright
        [0xF2] = '\u222B', // integral
        [0xF3] = '\u2320', // integraltp
        [0xF4] = '\uF8F5', // integralex (PUA)
        [0xF5] = '\u2321', // integralbt
        [0xF6] = '\uF8F6', // parenrighttp (PUA)
        [0xF7] = '\uF8F7', // parenrightex (PUA)
        [0xF8] = '\uF8F8', // parenrightbt (PUA)
        [0xF9] = '\uF8F9', // bracketrighttp (PUA)
        [0xFA] = '\uF8FA', // bracketrightex (PUA)
        [0xFB] = '\uF8FB', // bracketrightbt (PUA)
        [0xFC] = '\uF8FC', // bracerighttp (PUA)
        [0xFD] = '\uF8FD', // bracerightmid (PUA)
        [0xFE] = '\uF8FE', // bracerightbt (PUA)
    };

    // ────────────────────────────────────────────────────────────────────────
    // ZapfDingbats font encoding — full 202-entry table
    // Source: Adobe ZapfDingbats font mapping, PDF32000_2008 §D.6
    // ────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<byte, char> ZapfDingbatsEncoding = new()
    {
        [0x20] = '\u0020', // space
        [0x21] = '\u2701', [0x22] = '\u2702', [0x23] = '\u2703', [0x24] = '\u2704',
        [0x25] = '\u260E', [0x26] = '\u2706', [0x27] = '\u2707', [0x28] = '\u2708',
        [0x29] = '\u2709', [0x2A] = '\u261B', [0x2B] = '\u261E', [0x2C] = '\u270C',
        [0x2D] = '\u270D', [0x2E] = '\u270E', [0x2F] = '\u270F',
        [0x30] = '\u2710', [0x31] = '\u2711', [0x32] = '\u2712', [0x33] = '\u2713',
        [0x34] = '\u2714', [0x35] = '\u2715', [0x36] = '\u2716', [0x37] = '\u2717',
        [0x38] = '\u2718', [0x39] = '\u2719', [0x3A] = '\u271A', [0x3B] = '\u271B',
        [0x3C] = '\u271C', [0x3D] = '\u271D', [0x3E] = '\u271E', [0x3F] = '\u271F',
        [0x40] = '\u2720', [0x41] = '\u2721', [0x42] = '\u2722', [0x43] = '\u2723',
        [0x44] = '\u2724', [0x45] = '\u2725', [0x46] = '\u2726', [0x47] = '\u2727',
        [0x48] = '\u2605', [0x49] = '\u2729', [0x4A] = '\u272A', [0x4B] = '\u272B',
        [0x4C] = '\u272C', [0x4D] = '\u272D', [0x4E] = '\u272E', [0x4F] = '\u272F',
        [0x50] = '\u2730', [0x51] = '\u2731', [0x52] = '\u2732', [0x53] = '\u2733',
        [0x54] = '\u2734', [0x55] = '\u2735', [0x56] = '\u2736', [0x57] = '\u2737',
        [0x58] = '\u2738', [0x59] = '\u2739', [0x5A] = '\u273A', [0x5B] = '\u273B',
        [0x5C] = '\u273C', [0x5D] = '\u273D', [0x5E] = '\u273E', [0x5F] = '\u273F',
        [0x60] = '\u2740', [0x61] = '\u2741', [0x62] = '\u2742', [0x63] = '\u2743',
        [0x64] = '\u2744', [0x65] = '\u2745', [0x66] = '\u2746', [0x67] = '\u2747',
        [0x68] = '\u2748', [0x69] = '\u2749', [0x6A] = '\u274A', [0x6B] = '\u274B',
        [0x6C] = '\u25CF', [0x6D] = '\u274D', [0x6E] = '\u25A0', [0x6F] = '\u274F',
        [0x70] = '\u2750', [0x71] = '\u2751', [0x72] = '\u2752', [0x73] = '\u25B2',
        [0x74] = '\u25BC', [0x75] = '\u25C6', [0x76] = '\u2756', [0x77] = '\u25D7',
        [0x78] = '\u2758', [0x79] = '\u2759', [0x7A] = '\u275A', [0x7B] = '\u275B',
        [0x7C] = '\u275C', [0x7D] = '\u275D', [0x7E] = '\u275E',
        [0x80] = '\u2768', [0x81] = '\u2769', [0x82] = '\u276A', [0x83] = '\u276B',
        [0x84] = '\u276C', [0x85] = '\u276D', [0x86] = '\u276E', [0x87] = '\u276F',
        [0x88] = '\u2770', [0x89] = '\u2771', [0x8A] = '\u2772', [0x8B] = '\u2773',
        [0x8C] = '\u2774', [0x8D] = '\u2775',
        [0xA1] = '\u2761', [0xA2] = '\u2762', [0xA3] = '\u2763', [0xA4] = '\u2764',
        [0xA5] = '\u2765', [0xA6] = '\u2766', [0xA7] = '\u2767',
        [0xA8] = '\u2663', [0xA9] = '\u2666', [0xAA] = '\u2665', [0xAB] = '\u2660',
        [0xAC] = '\u2460', [0xAD] = '\u2461', [0xAE] = '\u2462', [0xAF] = '\u2463',
        [0xB0] = '\u2464', [0xB1] = '\u2465', [0xB2] = '\u2466', [0xB3] = '\u2467',
        [0xB4] = '\u2468', [0xB5] = '\u2469',
        [0xB6] = '\u2776', [0xB7] = '\u2777', [0xB8] = '\u2778', [0xB9] = '\u2779',
        [0xBA] = '\u277A', [0xBB] = '\u277B', [0xBC] = '\u277C', [0xBD] = '\u277D',
        [0xBE] = '\u277E', [0xBF] = '\u277F',
        [0xC0] = '\u2780', [0xC1] = '\u2781', [0xC2] = '\u2782', [0xC3] = '\u2783',
        [0xC4] = '\u2784', [0xC5] = '\u2785', [0xC6] = '\u2786', [0xC7] = '\u2787',
        [0xC8] = '\u2788', [0xC9] = '\u2789',
        [0xCA] = '\u278A', [0xCB] = '\u278B', [0xCC] = '\u278C', [0xCD] = '\u278D',
        [0xCE] = '\u278E', [0xCF] = '\u278F',
        [0xD0] = '\u2790', [0xD1] = '\u2791', [0xD2] = '\u2792', [0xD3] = '\u2793',
        [0xD4] = '\u2794', [0xD5] = '\u2192', [0xD6] = '\u2194', [0xD7] = '\u2195',
        [0xD8] = '\u2798', [0xD9] = '\u2799', [0xDA] = '\u279A', [0xDB] = '\u279B',
        [0xDC] = '\u279C', [0xDD] = '\u279D', [0xDE] = '\u279E', [0xDF] = '\u279F',
        [0xE0] = '\u27A0', [0xE1] = '\u27A1', [0xE2] = '\u27A2', [0xE3] = '\u27A3',
        [0xE4] = '\u27A4', [0xE5] = '\u27A5', [0xE6] = '\u27A6', [0xE7] = '\u27A7',
        [0xE8] = '\u27A8', [0xE9] = '\u27A9', [0xEA] = '\u27AA', [0xEB] = '\u27AB',
        [0xEC] = '\u27AC', [0xED] = '\u27AD', [0xEE] = '\u27AE', [0xEF] = '\u27AF',
        [0xF1] = '\u27B1', [0xF2] = '\u27B2', [0xF3] = '\u27B3', [0xF4] = '\u27B4',
        [0xF5] = '\u27B5', [0xF6] = '\u27B6', [0xF7] = '\u27B7', [0xF8] = '\u27B8',
        [0xF9] = '\u27B9', [0xFA] = '\u27BA', [0xFB] = '\u27BB', [0xFC] = '\u27BC',
        [0xFD] = '\u27BD', [0xFE] = '\u27BE',
    };
}
