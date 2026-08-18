namespace Aspose.Pdf.Text;

/// <summary>
/// Glyph widths for the 14 standard PDF fonts (PDF32000_2008 §9.6.2.2).
/// Widths are in units of 1/1000 of a text unit.
/// Data sourced from Adobe Font Metrics (AFM) files.
/// </summary>
internal static class Standard14Fonts
{
    /// <summary>
    /// Get the width of a character code for the given standard font.
    /// Returns the width in 1/1000 units, or -1 if not a standard font.
    /// </summary>
    public static int GetWidth(string baseFontName, int charCode)
    {
        var canonical = ResolveAlias(baseFontName);
        if (canonical is null) return -1;
        if (charCode < 0 || charCode > 255) return -1;

        var widths = GetWidthTable(canonical);
        return widths?[charCode] ?? -1;
    }

    /// <summary>
    /// Get the default (missing glyph) width for the given standard font.
    /// </summary>
    public static int GetDefaultWidth(string baseFontName)
    {
        var canonical = ResolveAlias(baseFontName);
        if (canonical is null) return 0;

        // Courier variants are all 600
        if (canonical.StartsWith("Courier", StringComparison.Ordinal)) return 600;

        // For proportional fonts, use space width as a reasonable default
        var widths = GetWidthTable(canonical);
        return widths?[32] ?? 500;
    }

    /// <summary>
    /// Returns true if the given font name is one of the 14 standard fonts.
    /// Also handles common aliases.
    /// </summary>
    public static bool IsStandard14(string baseFontName) => ResolveAlias(baseFontName) is not null;

    /// <summary>
    /// True only for the Core-14 names themselves (e.g. "Helvetica"), not for the
    /// real-font aliases that merely map onto them (e.g. "Arial" → "Helvetica").
    /// Lets callers prefer a real font's own metrics for an aliased name while
    /// still using the AFM tables for the genuine Core-14 fonts.
    /// </summary>
    public static bool IsCoreName(string baseFontName) => ResolveAlias(baseFontName) == baseFontName;

    /// <summary>
    /// Vertical glyph-box descent for a Standard-14 font (1/1000 units, negative).
    /// Values from Adobe Font Metrics. Returns 0 when the name isn't Standard-14.
    /// Used as a fallback when a PDF omits FontDescriptor for an implied
    /// Standard-14 font (common in older PDFs).
    /// </summary>
    internal static int GetDescent(string baseFontName)
    {
        var canonical = ResolveAlias(baseFontName);
        if (canonical is null) return 0;
        return canonical switch
        {
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => -157,
            "Helvetica" or "Helvetica-Oblique" => -207,
            "Helvetica-Bold" or "Helvetica-BoldOblique" => -207,
            "Times-Roman" or "Times-Italic" => -217,
            "Times-Bold" or "Times-BoldItalic" => -217,
            "Symbol" => -14,
            "ZapfDingbats" => -14,
            _ => 0,
        };
    }

    /// <summary>
    /// Descent for a face the writer substitutes by NAME rather than through the
    /// Core-14 alias table. "Arial" means the system face, not the Helvetica AFM:
    /// its hhea descender (-434 in a 2048 em) truncates to -211 in 1000-units.
    /// Every other name keeps the Standard-14 AFM descent.
    /// </summary>
    internal static int GetWrittenFaceDescent(string baseFontName) =>
        string.Equals(baseFontName, "Arial", StringComparison.OrdinalIgnoreCase)
            ? -211
            : GetDescent(baseFontName);

    /// <summary>
    /// Glyph-box ascent for a Standard-14 font (1/1000 units), from the Adobe
    /// AFM Ascender values. Returns 0 when the name isn't Standard-14.
    /// </summary>
    internal static int GetAscent(string baseFontName)
    {
        var canonical = ResolveAlias(baseFontName);
        if (canonical is null) return 0;
        return canonical switch
        {
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 629,
            "Helvetica" or "Helvetica-Oblique" => 718,
            "Helvetica-Bold" or "Helvetica-BoldOblique" => 718,
            "Times-Roman" or "Times-Italic" => 683,
            "Times-Bold" or "Times-BoldItalic" => 683,
            "Symbol" => 693,
            "ZapfDingbats" => 693,
            _ => 0,
        };
    }

    /// <summary>
    /// Ascent for a face the writer substitutes by NAME (see
    /// <see cref="GetWrittenFaceDescent"/>): "Arial" means the system face,
    /// whose hhea ascender (1854 in a 2048 em) truncates to 905 in 1000-units.
    /// Every other name keeps the Standard-14 AFM ascent.
    /// </summary>
    internal static int GetWrittenFaceAscent(string baseFontName) =>
        string.Equals(baseFontName, "Arial", StringComparison.OrdinalIgnoreCase)
            ? 905
            : GetAscent(baseFontName);

    /// <summary>
    /// Line-box ascent for a Standard-14 font (1/1000 units): a Standard-14
    /// text line is modeled as a 1.1-em box standing on the
    /// AFM descent (Helvetica: descent -207, ascent 893). Extraction rectangles
    /// and baseline placement both use this box, so the two stay symmetric.
    /// Returns 0 when the name isn't Standard-14.
    /// </summary>
    internal static int GetLineBoxAscent(string baseFontName)
    {
        var descent = GetDescent(baseFontName);
        if (descent == 0 && !IsStandard14(baseFontName)) return 0;
        return 1100 + descent; // descent is negative
    }

    /// <summary>
    /// Cap height for a Standard-14 font (1/1000 units), from Adobe AFM files.
    /// Approximates the ascent of the first text line so its glyph tops align
    /// with the top margin. Returns 0 when the name isn't a cap-bearing Standard-14 font.
    /// </summary>
    internal static int GetCapHeight(string baseFontName)
    {
        var canonical = ResolveAlias(baseFontName);
        if (canonical is null) return 0;
        return canonical switch
        {
            "Helvetica" or "Helvetica-Oblique" => 718,
            "Helvetica-Bold" or "Helvetica-BoldOblique" => 718,
            "Times-Roman" => 662,
            "Times-Italic" => 653,
            "Times-Bold" => 676,
            "Times-BoldItalic" => 669,
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 562,
            _ => 0,
        };
    }

    /// <summary>
    /// FontBBox height (top − bottom) for a Standard-14 font (1/1000 units).
    /// Values from Adobe AFM files. Used for clipping-rect sizing in TextParagraph.
    /// Returns 0 when the name isn't Standard-14.
    /// </summary>
    internal static int GetFontBBoxHeight(string baseFontName)
    {
        var canonical = ResolveAlias(baseFontName);
        if (canonical is null) return 0;
        // FontBBox = [llx, lly, urx, ury]; height = ury - lly
        return canonical switch
        {
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique"
                => 833 - (-250), // BBox: [-23 -250 715 833]
            "Helvetica" or "Helvetica-Oblique"
                => 931 - (-225), // BBox: [-166 -225 1000 931]
            "Helvetica-Bold" or "Helvetica-BoldOblique"
                => 962 - (-228), // BBox: [-170 -228 1003 962]
            "Times-Roman" or "Times-Italic"
                => 898 - (-218), // BBox: [-168 -218 1000 898]
            "Times-Bold" or "Times-BoldItalic"
                => 921 - (-218), // BBox: [-168 -218 1000 921]
            "Symbol" => 1010 - (-293), // BBox: [-180 -293 1090 1010]
            "ZapfDingbats" => 820 - (-143), // BBox: [-1 -143 981 820]
            _ => 0,
        };
    }

    /// <summary>
    /// OS/2 usWinAscent + usWinDescent from the corresponding TrueType system font,
    /// scaled to 1/1000 PDF units. Used for background rectangle height computation.
    /// Values are from: Arial (Helvetica), Times New Roman (Times), Courier New (Courier).
    /// </summary>
    internal static int GetSystemWinLineHeight(string baseFontName)
    {
        var canonical = ResolveAlias(baseFontName);
        if (canonical is null) return 0;
        // (usWinAscent + usWinDescent) * 1000 / unitsPerEm
        return canonical switch
        {
            // Courier New: usWinAscent=1705, usWinDescent=615, upm=2048 → 1133
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 1133,
            // Arial: usWinAscent=1854, usWinDescent=434, upm=2048 → 1117
            "Helvetica" or "Helvetica-Oblique" => 1117,
            "Helvetica-Bold" or "Helvetica-BoldOblique" => 1117,
            // Times New Roman: usWinAscent=1825, usWinDescent=443, upm=2048 → 1108
            "Times-Roman" or "Times-Italic" => 1108,
            "Times-Bold" or "Times-BoldItalic" => 1108,
            _ => 0,
        };
    }

    private static string? ResolveAlias(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        // Strip subset prefix
        if (name.Length > 7 && name[6] == '+')
            name = name[7..];

        // Direct match
        if (s_widthTables.ContainsKey(name)) return name;

        // Common aliases
        return name switch
        {
            "Arial" or "ArialMT" => "Helvetica",
            "Arial,Bold" or "Arial-Bold" or "Arial-BoldMT" => "Helvetica-Bold",
            "Arial,Italic" or "Arial-Italic" or "Arial-ItalicMT" => "Helvetica-Oblique",
            "Arial,BoldItalic" or "Arial-BoldItalic" or "Arial-BoldItalicMT" => "Helvetica-BoldOblique",
            "TimesNewRoman" or "TimesNewRomanPSMT" or "TimesNewRomanPS" => "Times-Roman",
            "TimesNewRoman,Bold" or "TimesNewRoman-Bold" or "TimesNewRomanPS-BoldMT" => "Times-Bold",
            "TimesNewRoman,Italic" or "TimesNewRoman-Italic" or "TimesNewRomanPS-ItalicMT" => "Times-Italic",
            "TimesNewRoman,BoldItalic" or "TimesNewRoman-BoldItalic" or "TimesNewRomanPS-BoldItalicMT" => "Times-BoldItalic",
            "CourierNew" or "CourierNewPSMT" => "Courier",
            "CourierNew,Bold" or "CourierNew-Bold" or "CourierNewPS-BoldMT" => "Courier-Bold",
            "CourierNew,Italic" or "CourierNew-Italic" or "CourierNewPS-ItalicMT" => "Courier-Oblique",
            "CourierNew,BoldItalic" or "CourierNew-BoldItalic" or "CourierNewPS-BoldItalicMT" => "Courier-BoldOblique",
            _ => null,
        };
    }

    private static ushort[]? GetWidthTable(string canonical)
    {
        s_widthTables.TryGetValue(canonical, out var table);
        return table;
    }

    private static readonly Dictionary<string, ushort[]> s_widthTables = new(StringComparer.Ordinal)
    {
        ["Courier"] = MakeCourier(),
        ["Courier-Bold"] = MakeCourier(),
        ["Courier-Oblique"] = MakeCourier(),
        ["Courier-BoldOblique"] = MakeCourier(),
        ["Helvetica"] = MakeHelvetica(),
        ["Helvetica-Bold"] = MakeHelveticaBold(),
        ["Helvetica-Oblique"] = MakeHelveticaOblique(),
        ["Helvetica-BoldOblique"] = MakeHelveticaBoldOblique(),
        ["Times-Roman"] = MakeTimesRoman(),
        ["Times-Bold"] = MakeTimesBold(),
        ["Times-Italic"] = MakeTimesItalic(),
        ["Times-BoldItalic"] = MakeTimesBoldItalic(),
        ["Symbol"] = MakeSymbol(),
        ["ZapfDingbats"] = MakeZapfDingbats(),
    };

    // ── Courier: all glyphs 600 ──────────────────────────────────────

    private static ushort[] MakeCourier()
    {
        var w = new ushort[256];
        Array.Fill(w, (ushort)600);
        return w;
    }

    // ── Helvetica ────────────────────────────────────────────────────
    // Source: Helvetica.afm (Adobe Core 35)

    private static ushort[] MakeHelvetica()
    {
        var w = new ushort[256];
        // Default to 278 (space width) for undefined codes
        Array.Fill(w, (ushort)278);

        // ASCII printable range from AFM
        w[32] = 278;  // space
        w[33] = 278;  // exclam
        w[34] = 355;  // quotedbl
        w[35] = 556;  // numbersign
        w[36] = 556;  // dollar
        w[37] = 889;  // percent
        w[38] = 667;  // ampersand
        w[39] = 191;  // quotesingle
        w[40] = 333;  // parenleft
        w[41] = 333;  // parenright
        w[42] = 389;  // asterisk
        w[43] = 584;  // plus
        w[44] = 278;  // comma
        w[45] = 333;  // hyphen
        w[46] = 278;  // period
        w[47] = 278;  // slash
        w[48] = 556;  // zero
        w[49] = 556;  // one
        w[50] = 556;  // two
        w[51] = 556;  // three
        w[52] = 556;  // four
        w[53] = 556;  // five
        w[54] = 556;  // six
        w[55] = 556;  // seven
        w[56] = 556;  // eight
        w[57] = 556;  // nine
        w[58] = 278;  // colon
        w[59] = 278;  // semicolon
        w[60] = 584;  // less
        w[61] = 584;  // equal
        w[62] = 584;  // greater
        w[63] = 556;  // question
        w[64] = 1015; // at
        w[65] = 667;  // A
        w[66] = 667;  // B
        w[67] = 722;  // C
        w[68] = 722;  // D
        w[69] = 667;  // E
        w[70] = 611;  // F
        w[71] = 778;  // G
        w[72] = 722;  // H
        w[73] = 278;  // I
        w[74] = 500;  // J
        w[75] = 667;  // K
        w[76] = 556;  // L
        w[77] = 833;  // M
        w[78] = 722;  // N
        w[79] = 778;  // O
        w[80] = 667;  // P
        w[81] = 778;  // Q
        w[82] = 722;  // R
        w[83] = 667;  // S
        w[84] = 611;  // T
        w[85] = 722;  // U
        w[86] = 667;  // V
        w[87] = 944;  // W
        w[88] = 667;  // X
        w[89] = 667;  // Y
        w[90] = 611;  // Z
        w[91] = 278;  // bracketleft
        w[92] = 278;  // backslash
        w[93] = 278;  // bracketright
        w[94] = 469;  // asciicircum
        w[95] = 556;  // underscore
        w[96] = 333;  // grave
        w[97] = 556;  // a
        w[98] = 556;  // b
        w[99] = 500;  // c
        w[100] = 556; // d
        w[101] = 556; // e
        w[102] = 278; // f
        w[103] = 556; // g
        w[104] = 556; // h
        w[105] = 222; // i
        w[106] = 222; // j
        w[107] = 500; // k
        w[108] = 222; // l
        w[109] = 833; // m
        w[110] = 556; // n
        w[111] = 556; // o
        w[112] = 556; // p
        w[113] = 556; // q
        w[114] = 333; // r
        w[115] = 500; // s
        w[116] = 278; // t
        w[117] = 556; // u
        w[118] = 500; // v
        w[119] = 722; // w
        w[120] = 500; // x
        w[121] = 500; // y
        w[122] = 500; // z
        w[123] = 334; // braceleft
        w[124] = 260; // bar
        w[125] = 334; // braceright
        w[126] = 584; // asciitilde

        // Latin-1 supplement (128-255) from WinAnsiEncoding
        w[128] = 556; // Euro (often mapped here)
        w[130] = 222; // quotesinglbase
        w[131] = 556; // florin
        w[132] = 333; // quotedblbase
        w[133] = 1000; // ellipsis
        w[134] = 556; // dagger
        w[135] = 556; // daggerdbl
        w[136] = 333; // circumflex
        w[137] = 1000; // perthousand
        w[138] = 667; // Scaron
        w[139] = 333; // guilsinglleft
        w[140] = 1000; // OE
        w[142] = 611; // Zcaron
        w[145] = 222; // quoteleft
        w[146] = 222; // quoteright
        w[147] = 333; // quotedblleft
        w[148] = 333; // quotedblright
        w[149] = 350; // bullet
        w[150] = 556; // endash
        w[151] = 1000; // emdash
        w[152] = 333; // tilde
        w[153] = 1000; // trademark
        w[154] = 500; // scaron
        w[155] = 333; // guilsinglright
        w[156] = 944; // oe
        w[158] = 500; // zcaron
        w[159] = 667; // Ydieresis
        w[160] = 278; // nbspace
        w[161] = 333; // exclamdown
        w[162] = 556; // cent
        w[163] = 556; // sterling
        w[164] = 556; // currency
        w[165] = 556; // yen
        w[166] = 260; // brokenbar
        w[167] = 556; // section
        w[168] = 333; // dieresis
        w[169] = 737; // copyright
        w[170] = 370; // ordfeminine
        w[171] = 556; // guillemotleft
        w[172] = 584; // logicalnot
        w[173] = 333; // sfthyphen
        w[174] = 737; // registered
        w[175] = 333; // macron
        w[176] = 400; // degree
        w[177] = 584; // plusminus
        w[178] = 333; // twosuperior
        w[179] = 333; // threesuperior
        w[180] = 333; // acute
        w[181] = 556; // mu
        w[182] = 537; // paragraph
        w[183] = 278; // periodcentered
        w[184] = 333; // cedilla
        w[185] = 333; // onesuperior
        w[186] = 365; // ordmasculine
        w[187] = 556; // guillemotright
        w[188] = 834; // onequarter
        w[189] = 834; // onehalf
        w[190] = 834; // threequarters
        w[191] = 611; // questiondown
        w[192] = 667; // Agrave
        w[193] = 667; // Aacute
        w[194] = 667; // Acircumflex
        w[195] = 667; // Atilde
        w[196] = 667; // Adieresis
        w[197] = 667; // Aring
        w[198] = 1000; // AE
        w[199] = 722; // Ccedilla
        w[200] = 667; // Egrave
        w[201] = 667; // Eacute
        w[202] = 667; // Ecircumflex
        w[203] = 667; // Edieresis
        w[204] = 278; // Igrave
        w[205] = 278; // Iacute
        w[206] = 278; // Icircumflex
        w[207] = 278; // Idieresis
        w[208] = 722; // Eth
        w[209] = 722; // Ntilde
        w[210] = 778; // Ograve
        w[211] = 778; // Oacute
        w[212] = 778; // Ocircumflex
        w[213] = 778; // Otilde
        w[214] = 778; // Odieresis
        w[215] = 584; // multiply
        w[216] = 778; // Oslash
        w[217] = 722; // Ugrave
        w[218] = 722; // Uacute
        w[219] = 722; // Ucircumflex
        w[220] = 722; // Udieresis
        w[221] = 667; // Yacute
        w[222] = 667; // Thorn
        w[223] = 611; // germandbls
        w[224] = 556; // agrave
        w[225] = 556; // aacute
        w[226] = 556; // acircumflex
        w[227] = 556; // atilde
        w[228] = 556; // adieresis
        w[229] = 556; // aring
        w[230] = 889; // ae
        w[231] = 500; // ccedilla
        w[232] = 556; // egrave
        w[233] = 556; // eacute
        w[234] = 556; // ecircumflex
        w[235] = 556; // edieresis
        w[236] = 278; // igrave (actually 222 in some AFMs — using correct)
        w[237] = 278; // iacute
        w[238] = 278; // icircumflex
        w[239] = 278; // idieresis
        w[240] = 556; // eth
        w[241] = 556; // ntilde
        w[242] = 556; // ograve
        w[243] = 556; // oacute
        w[244] = 556; // ocircumflex
        w[245] = 556; // otilde
        w[246] = 556; // odieresis
        w[247] = 584; // divide
        w[248] = 611; // oslash
        w[249] = 556; // ugrave
        w[250] = 556; // uacute
        w[251] = 556; // ucircumflex
        w[252] = 556; // udieresis
        w[253] = 500; // yacute
        w[254] = 556; // thorn
        w[255] = 500; // ydieresis
        return w;
    }

    // ── Helvetica-Bold ───────────────────────────────────────────────

    private static ushort[] MakeHelveticaBold()
    {
        var w = new ushort[256];
        Array.Fill(w, (ushort)278);

        w[32] = 278;  w[33] = 333;  w[34] = 474;  w[35] = 556;
        w[36] = 556;  w[37] = 889;  w[38] = 722;  w[39] = 238;
        w[40] = 333;  w[41] = 333;  w[42] = 389;  w[43] = 584;
        w[44] = 278;  w[45] = 333;  w[46] = 278;  w[47] = 278;
        w[48] = 556;  w[49] = 556;  w[50] = 556;  w[51] = 556;
        w[52] = 556;  w[53] = 556;  w[54] = 556;  w[55] = 556;
        w[56] = 556;  w[57] = 556;  w[58] = 333;  w[59] = 333;
        w[60] = 584;  w[61] = 584;  w[62] = 584;  w[63] = 611;
        w[64] = 975;  w[65] = 722;  w[66] = 722;  w[67] = 722;
        w[68] = 722;  w[69] = 667;  w[70] = 611;  w[71] = 778;
        w[72] = 722;  w[73] = 278;  w[74] = 556;  w[75] = 722;
        w[76] = 611;  w[77] = 833;  w[78] = 722;  w[79] = 778;
        w[80] = 667;  w[81] = 778;  w[82] = 722;  w[83] = 667;
        w[84] = 611;  w[85] = 722;  w[86] = 667;  w[87] = 944;
        w[88] = 667;  w[89] = 667;  w[90] = 611;  w[91] = 333;
        w[92] = 278;  w[93] = 333;  w[94] = 584;  w[95] = 556;
        w[96] = 333;  w[97] = 556;  w[98] = 611;  w[99] = 556;
        w[100] = 611; w[101] = 556; w[102] = 333; w[103] = 611;
        w[104] = 611; w[105] = 278; w[106] = 278; w[107] = 556;
        w[108] = 278; w[109] = 889; w[110] = 611; w[111] = 611;
        w[112] = 611; w[113] = 611; w[114] = 389; w[115] = 556;
        w[116] = 333; w[117] = 611; w[118] = 556; w[119] = 778;
        w[120] = 556; w[121] = 556; w[122] = 500; w[123] = 389;
        w[124] = 280; w[125] = 389; w[126] = 584;

        // Latin-1 supplement
        w[128] = 556; w[130] = 278; w[131] = 556; w[132] = 500;
        w[133] = 1000; w[134] = 556; w[135] = 556; w[136] = 333;
        w[137] = 1000; w[138] = 667; w[139] = 333; w[140] = 1000;
        w[142] = 611; w[145] = 278; w[146] = 278; w[147] = 500;
        w[148] = 500; w[149] = 350; w[150] = 556; w[151] = 1000;
        w[152] = 333; w[153] = 1000; w[154] = 556; w[155] = 333;
        w[156] = 944; w[158] = 500; w[159] = 667;
        w[160] = 278; w[161] = 333; w[162] = 556; w[163] = 556;
        w[164] = 556; w[165] = 556; w[166] = 280; w[167] = 556;
        w[168] = 333; w[169] = 737; w[170] = 370; w[171] = 556;
        w[172] = 584; w[173] = 333; w[174] = 737; w[175] = 333;
        w[176] = 400; w[177] = 584; w[178] = 333; w[179] = 333;
        w[180] = 333; w[181] = 611; w[182] = 556; w[183] = 278;
        w[184] = 333; w[185] = 333; w[186] = 365; w[187] = 556;
        w[188] = 834; w[189] = 834; w[190] = 834; w[191] = 611;
        w[192] = 722; w[193] = 722; w[194] = 722; w[195] = 722;
        w[196] = 722; w[197] = 722; w[198] = 1000; w[199] = 722;
        w[200] = 667; w[201] = 667; w[202] = 667; w[203] = 667;
        w[204] = 278; w[205] = 278; w[206] = 278; w[207] = 278;
        w[208] = 722; w[209] = 722; w[210] = 778; w[211] = 778;
        w[212] = 778; w[213] = 778; w[214] = 778; w[215] = 584;
        w[216] = 778; w[217] = 722; w[218] = 722; w[219] = 722;
        w[220] = 722; w[221] = 667; w[222] = 667; w[223] = 611;
        w[224] = 556; w[225] = 556; w[226] = 556; w[227] = 556;
        w[228] = 556; w[229] = 556; w[230] = 889; w[231] = 556;
        w[232] = 556; w[233] = 556; w[234] = 556; w[235] = 556;
        w[236] = 278; w[237] = 278; w[238] = 278; w[239] = 278;
        w[240] = 611; w[241] = 611; w[242] = 611; w[243] = 611;
        w[244] = 611; w[245] = 611; w[246] = 611; w[247] = 584;
        w[248] = 611; w[249] = 611; w[250] = 611; w[251] = 611;
        w[252] = 611; w[253] = 556; w[254] = 611; w[255] = 556;
        return w;
    }

    // Helvetica-Oblique has the same widths as Helvetica
    private static ushort[] MakeHelveticaOblique() => MakeHelvetica();

    // Helvetica-BoldOblique has the same widths as Helvetica-Bold
    private static ushort[] MakeHelveticaBoldOblique() => MakeHelveticaBold();

    // ── Times-Roman ──────────────────────────────────────────────────

    private static ushort[] MakeTimesRoman()
    {
        var w = new ushort[256];
        Array.Fill(w, (ushort)250);

        w[32] = 250;  w[33] = 333;  w[34] = 408;  w[35] = 500;
        w[36] = 500;  w[37] = 833;  w[38] = 778;  w[39] = 180;
        w[40] = 333;  w[41] = 333;  w[42] = 500;  w[43] = 564;
        w[44] = 250;  w[45] = 333;  w[46] = 250;  w[47] = 278;
        w[48] = 500;  w[49] = 500;  w[50] = 500;  w[51] = 500;
        w[52] = 500;  w[53] = 500;  w[54] = 500;  w[55] = 500;
        w[56] = 500;  w[57] = 500;  w[58] = 278;  w[59] = 278;
        w[60] = 564;  w[61] = 564;  w[62] = 564;  w[63] = 444;
        w[64] = 921;  w[65] = 722;  w[66] = 667;  w[67] = 667;
        w[68] = 722;  w[69] = 611;  w[70] = 556;  w[71] = 722;
        w[72] = 722;  w[73] = 333;  w[74] = 389;  w[75] = 722;
        w[76] = 611;  w[77] = 889;  w[78] = 722;  w[79] = 722;
        w[80] = 556;  w[81] = 722;  w[82] = 667;  w[83] = 556;
        w[84] = 611;  w[85] = 722;  w[86] = 722;  w[87] = 944;
        w[88] = 722;  w[89] = 722;  w[90] = 611;  w[91] = 333;
        w[92] = 278;  w[93] = 333;  w[94] = 469;  w[95] = 500;
        w[96] = 333;  w[97] = 444;  w[98] = 500;  w[99] = 444;
        w[100] = 500; w[101] = 444; w[102] = 333; w[103] = 500;
        w[104] = 500; w[105] = 278; w[106] = 278; w[107] = 500;
        w[108] = 278; w[109] = 778; w[110] = 500; w[111] = 500;
        w[112] = 500; w[113] = 500; w[114] = 333; w[115] = 389;
        w[116] = 278; w[117] = 500; w[118] = 500; w[119] = 722;
        w[120] = 500; w[121] = 500; w[122] = 444; w[123] = 480;
        w[124] = 200; w[125] = 480; w[126] = 541;

        // Latin-1 supplement
        w[128] = 500; w[130] = 333; w[131] = 500; w[132] = 444;
        w[133] = 1000; w[134] = 500; w[135] = 500; w[136] = 333;
        w[137] = 1000; w[138] = 556; w[139] = 333; w[140] = 889;
        w[142] = 611; w[145] = 333; w[146] = 333; w[147] = 444;
        w[148] = 444; w[149] = 350; w[150] = 500; w[151] = 1000;
        w[152] = 333; w[153] = 980; w[154] = 389; w[155] = 333;
        w[156] = 722; w[158] = 444; w[159] = 722;
        w[160] = 250; w[161] = 333; w[162] = 500; w[163] = 500;
        w[164] = 500; w[165] = 500; w[166] = 200; w[167] = 500;
        w[168] = 333; w[169] = 760; w[170] = 276; w[171] = 500;
        w[172] = 564; w[173] = 333; w[174] = 760; w[175] = 333;
        w[176] = 400; w[177] = 564; w[178] = 300; w[179] = 300;
        w[180] = 333; w[181] = 500; w[182] = 453; w[183] = 250;
        w[184] = 333; w[185] = 300; w[186] = 310; w[187] = 500;
        w[188] = 750; w[189] = 750; w[190] = 750; w[191] = 444;
        w[192] = 722; w[193] = 722; w[194] = 722; w[195] = 722;
        w[196] = 722; w[197] = 722; w[198] = 889; w[199] = 667;
        w[200] = 611; w[201] = 611; w[202] = 611; w[203] = 611;
        w[204] = 333; w[205] = 333; w[206] = 333; w[207] = 333;
        w[208] = 722; w[209] = 722; w[210] = 722; w[211] = 722;
        w[212] = 722; w[213] = 722; w[214] = 722; w[215] = 564;
        w[216] = 722; w[217] = 722; w[218] = 722; w[219] = 722;
        w[220] = 722; w[221] = 722; w[222] = 556; w[223] = 500;
        w[224] = 444; w[225] = 444; w[226] = 444; w[227] = 444;
        w[228] = 444; w[229] = 444; w[230] = 667; w[231] = 444;
        w[232] = 444; w[233] = 444; w[234] = 444; w[235] = 444;
        w[236] = 278; w[237] = 278; w[238] = 278; w[239] = 278;
        w[240] = 500; w[241] = 500; w[242] = 500; w[243] = 500;
        w[244] = 500; w[245] = 500; w[246] = 500; w[247] = 564;
        w[248] = 500; w[249] = 500; w[250] = 500; w[251] = 500;
        w[252] = 500; w[253] = 500; w[254] = 500; w[255] = 500;
        return w;
    }

    // ── Times-Bold ───────────────────────────────────────────────────

    private static ushort[] MakeTimesBold()
    {
        var w = new ushort[256];
        Array.Fill(w, (ushort)250);

        w[32] = 250;  w[33] = 333;  w[34] = 555;  w[35] = 500;
        w[36] = 500;  w[37] = 1000; w[38] = 833;  w[39] = 278;
        w[40] = 333;  w[41] = 333;  w[42] = 500;  w[43] = 570;
        w[44] = 250;  w[45] = 333;  w[46] = 250;  w[47] = 278;
        w[48] = 500;  w[49] = 500;  w[50] = 500;  w[51] = 500;
        w[52] = 500;  w[53] = 500;  w[54] = 500;  w[55] = 500;
        w[56] = 500;  w[57] = 500;  w[58] = 333;  w[59] = 333;
        w[60] = 570;  w[61] = 570;  w[62] = 570;  w[63] = 500;
        w[64] = 930;  w[65] = 722;  w[66] = 667;  w[67] = 722;
        w[68] = 722;  w[69] = 667;  w[70] = 611;  w[71] = 778;
        w[72] = 778;  w[73] = 389;  w[74] = 500;  w[75] = 778;
        w[76] = 667;  w[77] = 944;  w[78] = 722;  w[79] = 778;
        w[80] = 611;  w[81] = 778;  w[82] = 722;  w[83] = 556;
        w[84] = 667;  w[85] = 722;  w[86] = 722;  w[87] = 1000;
        w[88] = 722;  w[89] = 722;  w[90] = 667;  w[91] = 333;
        w[92] = 278;  w[93] = 333;  w[94] = 581;  w[95] = 500;
        w[96] = 333;  w[97] = 500;  w[98] = 556;  w[99] = 444;
        w[100] = 556; w[101] = 444; w[102] = 333; w[103] = 500;
        w[104] = 556; w[105] = 278; w[106] = 333; w[107] = 556;
        w[108] = 278; w[109] = 833; w[110] = 556; w[111] = 500;
        w[112] = 556; w[113] = 556; w[114] = 444; w[115] = 389;
        w[116] = 333; w[117] = 556; w[118] = 500; w[119] = 722;
        w[120] = 500; w[121] = 500; w[122] = 444; w[123] = 394;
        w[124] = 220; w[125] = 394; w[126] = 520;

        // Latin-1 supplement
        w[128] = 500; w[130] = 333; w[131] = 500; w[132] = 500;
        w[133] = 1000; w[134] = 500; w[135] = 500; w[136] = 333;
        w[137] = 1000; w[138] = 556; w[139] = 333; w[140] = 1000;
        w[142] = 667; w[145] = 333; w[146] = 333; w[147] = 500;
        w[148] = 500; w[149] = 350; w[150] = 500; w[151] = 1000;
        w[152] = 333; w[153] = 1000; w[154] = 389; w[155] = 333;
        w[156] = 722; w[158] = 444; w[159] = 722;
        w[160] = 250; w[161] = 333; w[162] = 500; w[163] = 500;
        w[164] = 500; w[165] = 500; w[166] = 220; w[167] = 500;
        w[168] = 333; w[169] = 747; w[170] = 300; w[171] = 500;
        w[172] = 570; w[173] = 333; w[174] = 747; w[175] = 333;
        w[176] = 400; w[177] = 570; w[178] = 300; w[179] = 300;
        w[180] = 333; w[181] = 556; w[182] = 540; w[183] = 250;
        w[184] = 333; w[185] = 300; w[186] = 330; w[187] = 500;
        w[188] = 750; w[189] = 750; w[190] = 750; w[191] = 500;
        w[192] = 722; w[193] = 722; w[194] = 722; w[195] = 722;
        w[196] = 722; w[197] = 722; w[198] = 1000; w[199] = 722;
        w[200] = 667; w[201] = 667; w[202] = 667; w[203] = 667;
        w[204] = 389; w[205] = 389; w[206] = 389; w[207] = 389;
        w[208] = 722; w[209] = 722; w[210] = 778; w[211] = 778;
        w[212] = 778; w[213] = 778; w[214] = 778; w[215] = 570;
        w[216] = 778; w[217] = 722; w[218] = 722; w[219] = 722;
        w[220] = 722; w[221] = 722; w[222] = 611; w[223] = 556;
        w[224] = 500; w[225] = 500; w[226] = 500; w[227] = 500;
        w[228] = 500; w[229] = 500; w[230] = 722; w[231] = 444;
        w[232] = 444; w[233] = 444; w[234] = 444; w[235] = 444;
        w[236] = 278; w[237] = 278; w[238] = 278; w[239] = 278;
        w[240] = 500; w[241] = 556; w[242] = 500; w[243] = 500;
        w[244] = 500; w[245] = 500; w[246] = 500; w[247] = 570;
        w[248] = 500; w[249] = 556; w[250] = 556; w[251] = 556;
        w[252] = 556; w[253] = 500; w[254] = 556; w[255] = 500;
        return w;
    }

    // ── Times-Italic ─────────────────────────────────────────────────

    private static ushort[] MakeTimesItalic()
    {
        var w = new ushort[256];
        Array.Fill(w, (ushort)250);

        w[32] = 250;  w[33] = 333;  w[34] = 420;  w[35] = 500;
        w[36] = 500;  w[37] = 833;  w[38] = 778;  w[39] = 214;
        w[40] = 333;  w[41] = 333;  w[42] = 500;  w[43] = 675;
        w[44] = 250;  w[45] = 333;  w[46] = 250;  w[47] = 278;
        w[48] = 500;  w[49] = 500;  w[50] = 500;  w[51] = 500;
        w[52] = 500;  w[53] = 500;  w[54] = 500;  w[55] = 500;
        w[56] = 500;  w[57] = 500;  w[58] = 333;  w[59] = 333;
        w[60] = 675;  w[61] = 675;  w[62] = 675;  w[63] = 500;
        w[64] = 920;  w[65] = 611;  w[66] = 611;  w[67] = 667;
        w[68] = 722;  w[69] = 611;  w[70] = 611;  w[71] = 722;
        w[72] = 722;  w[73] = 333;  w[74] = 444;  w[75] = 667;
        w[76] = 556;  w[77] = 833;  w[78] = 667;  w[79] = 722;
        w[80] = 611;  w[81] = 722;  w[82] = 611;  w[83] = 500;
        w[84] = 556;  w[85] = 722;  w[86] = 611;  w[87] = 833;
        w[88] = 611;  w[89] = 556;  w[90] = 556;  w[91] = 389;
        w[92] = 278;  w[93] = 389;  w[94] = 422;  w[95] = 500;
        w[96] = 333;  w[97] = 500;  w[98] = 500;  w[99] = 444;
        w[100] = 500; w[101] = 444; w[102] = 278; w[103] = 500;
        w[104] = 500; w[105] = 278; w[106] = 278; w[107] = 444;
        w[108] = 278; w[109] = 722; w[110] = 500; w[111] = 500;
        w[112] = 500; w[113] = 500; w[114] = 389; w[115] = 389;
        w[116] = 278; w[117] = 500; w[118] = 444; w[119] = 667;
        w[120] = 444; w[121] = 444; w[122] = 389; w[123] = 400;
        w[124] = 275; w[125] = 400; w[126] = 541;

        // Latin-1 supplement
        w[128] = 500; w[130] = 333; w[131] = 500; w[132] = 556;
        w[133] = 889; w[134] = 500; w[135] = 500; w[136] = 333;
        w[137] = 1000; w[138] = 500; w[139] = 333; w[140] = 944;
        w[142] = 556; w[145] = 333; w[146] = 333; w[147] = 556;
        w[148] = 556; w[149] = 350; w[150] = 500; w[151] = 889;
        w[152] = 333; w[153] = 980; w[154] = 389; w[155] = 333;
        w[156] = 667; w[158] = 389; w[159] = 556;
        w[160] = 250; w[161] = 389; w[162] = 500; w[163] = 500;
        w[164] = 500; w[165] = 500; w[166] = 275; w[167] = 500;
        w[168] = 333; w[169] = 760; w[170] = 276; w[171] = 500;
        w[172] = 675; w[173] = 333; w[174] = 760; w[175] = 333;
        w[176] = 400; w[177] = 675; w[178] = 300; w[179] = 300;
        w[180] = 333; w[181] = 500; w[182] = 523; w[183] = 250;
        w[184] = 333; w[185] = 300; w[186] = 310; w[187] = 500;
        w[188] = 750; w[189] = 750; w[190] = 750; w[191] = 500;
        w[192] = 611; w[193] = 611; w[194] = 611; w[195] = 611;
        w[196] = 611; w[197] = 611; w[198] = 889; w[199] = 667;
        w[200] = 611; w[201] = 611; w[202] = 611; w[203] = 611;
        w[204] = 333; w[205] = 333; w[206] = 333; w[207] = 333;
        w[208] = 722; w[209] = 667; w[210] = 722; w[211] = 722;
        w[212] = 722; w[213] = 722; w[214] = 722; w[215] = 675;
        w[216] = 722; w[217] = 722; w[218] = 722; w[219] = 722;
        w[220] = 722; w[221] = 556; w[222] = 611; w[223] = 500;
        w[224] = 500; w[225] = 500; w[226] = 500; w[227] = 500;
        w[228] = 500; w[229] = 500; w[230] = 667; w[231] = 444;
        w[232] = 444; w[233] = 444; w[234] = 444; w[235] = 444;
        w[236] = 278; w[237] = 278; w[238] = 278; w[239] = 278;
        w[240] = 500; w[241] = 500; w[242] = 500; w[243] = 500;
        w[244] = 500; w[245] = 500; w[246] = 500; w[247] = 675;
        w[248] = 500; w[249] = 500; w[250] = 500; w[251] = 500;
        w[252] = 500; w[253] = 444; w[254] = 500; w[255] = 444;
        return w;
    }

    // ── Times-BoldItalic ─────────────────────────────────────────────

    private static ushort[] MakeTimesBoldItalic()
    {
        var w = new ushort[256];
        Array.Fill(w, (ushort)250);

        w[32] = 250;  w[33] = 389;  w[34] = 555;  w[35] = 500;
        w[36] = 500;  w[37] = 833;  w[38] = 778;  w[39] = 278;
        w[40] = 333;  w[41] = 333;  w[42] = 500;  w[43] = 570;
        w[44] = 250;  w[45] = 333;  w[46] = 250;  w[47] = 278;
        w[48] = 500;  w[49] = 500;  w[50] = 500;  w[51] = 500;
        w[52] = 500;  w[53] = 500;  w[54] = 500;  w[55] = 500;
        w[56] = 500;  w[57] = 500;  w[58] = 333;  w[59] = 333;
        w[60] = 570;  w[61] = 570;  w[62] = 570;  w[63] = 500;
        w[64] = 832;  w[65] = 667;  w[66] = 667;  w[67] = 667;
        w[68] = 722;  w[69] = 667;  w[70] = 667;  w[71] = 722;
        w[72] = 778;  w[73] = 389;  w[74] = 500;  w[75] = 667;
        w[76] = 611;  w[77] = 889;  w[78] = 722;  w[79] = 722;
        w[80] = 611;  w[81] = 722;  w[82] = 667;  w[83] = 556;
        w[84] = 611;  w[85] = 722;  w[86] = 667;  w[87] = 889;
        w[88] = 667;  w[89] = 611;  w[90] = 611;  w[91] = 333;
        w[92] = 278;  w[93] = 333;  w[94] = 570;  w[95] = 500;
        w[96] = 333;  w[97] = 500;  w[98] = 500;  w[99] = 444;
        w[100] = 500; w[101] = 444; w[102] = 333; w[103] = 500;
        w[104] = 556; w[105] = 278; w[106] = 278; w[107] = 500;
        w[108] = 278; w[109] = 778; w[110] = 556; w[111] = 500;
        w[112] = 500; w[113] = 500; w[114] = 389; w[115] = 389;
        w[116] = 278; w[117] = 556; w[118] = 444; w[119] = 667;
        w[120] = 500; w[121] = 444; w[122] = 389; w[123] = 348;
        w[124] = 220; w[125] = 348; w[126] = 570;

        // Latin-1 supplement
        w[128] = 500; w[130] = 333; w[131] = 500; w[132] = 500;
        w[133] = 1000; w[134] = 500; w[135] = 500; w[136] = 333;
        w[137] = 1000; w[138] = 556; w[139] = 333; w[140] = 944;
        w[142] = 611; w[145] = 333; w[146] = 333; w[147] = 500;
        w[148] = 500; w[149] = 350; w[150] = 500; w[151] = 1000;
        w[152] = 333; w[153] = 1000; w[154] = 389; w[155] = 333;
        w[156] = 722; w[158] = 389; w[159] = 611;
        w[160] = 250; w[161] = 389; w[162] = 500; w[163] = 500;
        w[164] = 500; w[165] = 500; w[166] = 220; w[167] = 500;
        w[168] = 333; w[169] = 747; w[170] = 266; w[171] = 500;
        w[172] = 606; w[173] = 333; w[174] = 747; w[175] = 333;
        w[176] = 400; w[177] = 570; w[178] = 300; w[179] = 300;
        w[180] = 333; w[181] = 576; w[182] = 500; w[183] = 250;
        w[184] = 333; w[185] = 300; w[186] = 300; w[187] = 500;
        w[188] = 750; w[189] = 750; w[190] = 750; w[191] = 500;
        w[192] = 667; w[193] = 667; w[194] = 667; w[195] = 667;
        w[196] = 667; w[197] = 667; w[198] = 944; w[199] = 667;
        w[200] = 667; w[201] = 667; w[202] = 667; w[203] = 667;
        w[204] = 389; w[205] = 389; w[206] = 389; w[207] = 389;
        w[208] = 722; w[209] = 722; w[210] = 722; w[211] = 722;
        w[212] = 722; w[213] = 722; w[214] = 722; w[215] = 570;
        w[216] = 722; w[217] = 722; w[218] = 722; w[219] = 722;
        w[220] = 722; w[221] = 611; w[222] = 611; w[223] = 500;
        w[224] = 500; w[225] = 500; w[226] = 500; w[227] = 500;
        w[228] = 500; w[229] = 500; w[230] = 722; w[231] = 444;
        w[232] = 444; w[233] = 444; w[234] = 444; w[235] = 444;
        w[236] = 278; w[237] = 278; w[238] = 278; w[239] = 278;
        w[240] = 500; w[241] = 556; w[242] = 500; w[243] = 500;
        w[244] = 500; w[245] = 500; w[246] = 500; w[247] = 570;
        w[248] = 500; w[249] = 556; w[250] = 556; w[251] = 556;
        w[252] = 556; w[253] = 444; w[254] = 500; w[255] = 444;
        return w;
    }

    // ── Symbol ───────────────────────────────────────────────────────

    private static ushort[] MakeSymbol()
    {
        var w = new ushort[256];
        Array.Fill(w, (ushort)250);

        w[32] = 250;  w[33] = 333;  w[34] = 713;  w[35] = 500;
        w[36] = 549;  w[37] = 833;  w[38] = 778;  w[39] = 439;
        w[40] = 333;  w[41] = 333;  w[42] = 500;  w[43] = 549;
        w[44] = 250;  w[45] = 549;  w[46] = 250;  w[47] = 278;
        w[48] = 500;  w[49] = 500;  w[50] = 500;  w[51] = 500;
        w[52] = 500;  w[53] = 500;  w[54] = 500;  w[55] = 500;
        w[56] = 500;  w[57] = 500;  w[58] = 278;  w[59] = 278;
        w[60] = 549;  w[61] = 549;  w[62] = 549;  w[63] = 444;
        w[64] = 549;  w[65] = 722;  w[66] = 667;  w[67] = 722;
        w[68] = 612;  w[69] = 611;  w[70] = 763;  w[71] = 603;
        w[72] = 722;  w[73] = 333;  w[74] = 631;  w[75] = 722;
        w[76] = 686;  w[77] = 889;  w[78] = 722;  w[79] = 722;
        w[80] = 768;  w[81] = 741;  w[82] = 556;  w[83] = 592;
        w[84] = 611;  w[85] = 690;  w[86] = 439;  w[87] = 768;
        w[88] = 645;  w[89] = 795;  w[90] = 611;  w[91] = 333;
        w[92] = 863;  w[93] = 333;  w[94] = 658;  w[95] = 500;
        w[96] = 500;  w[97] = 631;  w[98] = 549;  w[99] = 549;
        w[100] = 494; w[101] = 439; w[102] = 521; w[103] = 411;
        w[104] = 603; w[105] = 329; w[106] = 603; w[107] = 549;
        w[108] = 549; w[109] = 576; w[110] = 521; w[111] = 549;
        w[112] = 549; w[113] = 521; w[114] = 549; w[115] = 603;
        w[116] = 439; w[117] = 576; w[118] = 713; w[119] = 686;
        w[120] = 493; w[121] = 686; w[122] = 494;
        w[123] = 480; w[124] = 200; w[125] = 480; w[126] = 549;
        // Higher codes for Symbol font special chars
        w[160] = 250; w[161] = 620; w[162] = 247; w[163] = 549;
        w[164] = 167; w[165] = 713; w[166] = 500; w[167] = 753;
        w[168] = 753; w[169] = 753; w[170] = 753; w[171] = 1042;
        w[172] = 713; w[173] = 603; w[174] = 987; w[175] = 603;
        w[176] = 400; w[177] = 549; w[178] = 411; w[179] = 549;
        w[180] = 549; w[181] = 713; w[182] = 494; w[183] = 460;
        w[184] = 549; w[185] = 549; w[186] = 549; w[187] = 549;
        w[188] = 1000; w[189] = 603; w[190] = 1000; w[191] = 658;
        w[192] = 823; w[193] = 686; w[194] = 795; w[195] = 987;
        w[196] = 768; w[197] = 768; w[198] = 823; w[199] = 768;
        w[200] = 768; w[201] = 713; w[202] = 713; w[203] = 713;
        w[204] = 713; w[205] = 713; w[206] = 713; w[207] = 713;
        w[208] = 768; w[209] = 713; w[210] = 790; w[211] = 790;
        w[212] = 890; w[213] = 823; w[214] = 549; w[215] = 250;
        w[216] = 713; w[217] = 603; w[218] = 603; w[219] = 1042;
        w[220] = 987; w[221] = 603; w[222] = 987; w[223] = 603;
        w[224] = 494; w[225] = 329; w[226] = 790; w[227] = 790;
        w[228] = 786; w[229] = 713; w[230] = 384; w[231] = 384;
        w[232] = 384; w[233] = 384; w[234] = 384; w[235] = 384;
        w[236] = 494; w[237] = 494; w[238] = 494; w[239] = 494;
        w[241] = 329; w[242] = 274; w[243] = 686; w[244] = 686;
        w[245] = 686; w[246] = 384; w[247] = 549; w[248] = 384;
        w[249] = 384; w[250] = 384; w[251] = 384; w[252] = 494;
        w[253] = 494; w[254] = 494;
        return w;
    }

    // ── ZapfDingbats ─────────────────────────────────────────────────

    private static ushort[] MakeZapfDingbats()
    {
        var w = new ushort[256];
        Array.Fill(w, (ushort)278);

        w[32] = 278;  w[33] = 974;  w[34] = 961;  w[35] = 974;
        w[36] = 980;  w[37] = 719;  w[38] = 789;  w[39] = 790;
        w[40] = 791;  w[41] = 690;  w[42] = 960;  w[43] = 939;
        w[44] = 549;  w[45] = 855;  w[46] = 911;  w[47] = 933;
        w[48] = 911;  w[49] = 945;  w[50] = 974;  w[51] = 755;
        w[52] = 846;  w[53] = 762;  w[54] = 761;  w[55] = 571;
        w[56] = 677;  w[57] = 763;  w[58] = 760;  w[59] = 759;
        w[60] = 754;  w[61] = 494;  w[62] = 552;  w[63] = 537;
        w[64] = 577;  w[65] = 692;  w[66] = 786;  w[67] = 788;
        w[68] = 788;  w[69] = 790;  w[70] = 793;  w[71] = 794;
        w[72] = 816;  w[73] = 823;  w[74] = 789;  w[75] = 841;
        w[76] = 823;  w[77] = 833;  w[78] = 816;  w[79] = 831;
        w[80] = 923;  w[81] = 744;  w[82] = 723;  w[83] = 749;
        w[84] = 790;  w[85] = 792;  w[86] = 695;  w[87] = 776;
        w[88] = 768;  w[89] = 792;  w[90] = 759;  w[91] = 707;
        w[92] = 708;  w[93] = 682;  w[94] = 701;  w[95] = 826;
        w[96] = 815;  w[97] = 789;  w[98] = 789;  w[99] = 707;
        w[100] = 687; w[101] = 696; w[102] = 689; w[103] = 786;
        w[104] = 787; w[105] = 713; w[106] = 791; w[107] = 785;
        w[108] = 791; w[109] = 873; w[110] = 761; w[111] = 762;
        w[112] = 762; w[113] = 759; w[114] = 759; w[115] = 892;
        w[116] = 892; w[117] = 788; w[118] = 784; w[119] = 438;
        w[120] = 138; w[121] = 277; w[122] = 415; w[123] = 392;
        w[124] = 392; w[125] = 668; w[126] = 668;

        w[128] = 390; w[130] = 390; w[131] = 317;
        w[132] = 401; w[133] = 938; w[134] = 1024; w[135] = 461;
        w[136] = 480; w[137] = 896; w[138] = 734; w[139] = 496;
        w[140] = 873; w[141] = 461;
        w[161] = 732; w[162] = 544; w[163] = 544; w[164] = 910;
        w[165] = 667; w[166] = 760; w[167] = 760; w[168] = 776;
        w[169] = 595; w[170] = 694; w[171] = 626; w[172] = 788;
        w[173] = 788; w[174] = 788; w[175] = 788; w[176] = 788;
        w[177] = 788; w[178] = 788; w[179] = 788; w[180] = 788;
        w[181] = 788; w[182] = 788; w[183] = 788; w[184] = 788;
        w[185] = 788; w[186] = 788; w[187] = 788; w[188] = 788;
        w[189] = 788; w[190] = 788; w[191] = 788; w[192] = 788;
        w[193] = 788; w[194] = 788; w[195] = 788; w[196] = 788;
        w[197] = 788; w[198] = 788; w[199] = 788; w[200] = 788;
        w[201] = 788; w[202] = 788; w[203] = 788; w[204] = 894;
        w[205] = 838; w[206] = 1016; w[207] = 458; w[208] = 748;
        w[209] = 924; w[210] = 748; w[211] = 918; w[212] = 927;
        w[213] = 928; w[214] = 928; w[215] = 834; w[216] = 873;
        w[217] = 828; w[218] = 924; w[219] = 924; w[220] = 917;
        w[221] = 930; w[222] = 931; w[223] = 463; w[224] = 883;
        w[225] = 836; w[226] = 836; w[227] = 867; w[228] = 867;
        w[229] = 696; w[230] = 696; w[231] = 874;
        w[234] = 874; w[235] = 760; w[236] = 946; w[237] = 771;
        w[238] = 865; w[239] = 771; w[240] = 888; w[241] = 967;
        w[242] = 888; w[243] = 831; w[244] = 873; w[245] = 927;
        w[246] = 970; w[247] = 918;
        return w;
    }
}
