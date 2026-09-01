using System.Linq;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Stamps;

public partial class TextStamp
{
    /// <summary>True when at least one character of <paramref name="text"/> could not be
    /// encoded by the primary font (it collapsed to '?' although the source char was not '?').</summary>
    private static bool HasUnencodableGlyphs(string text, byte[] encoded)
    {
        var n = Math.Min(text.Length, encoded.Length);
        for (var i = 0; i < n; i++)
            if (text[i] != '?' && encoded[i] == (byte)'?')
                return true;
        return false;
    }

    /// <summary>Standard-14 family → the TrueType face a viewer substitutes for it. A
    /// non-embedded Standard-14 font can't actually display glyphs outside WinAnsi (the
    /// viewer's substitute lacks them), so for non-WinAnsi stamp text the matching TrueType
    /// is embedded instead.</summary>
    private static readonly Dictionary<string, string> Std14ToTrueType =
        new(StringComparer.Ordinal)
        {
            ["Helvetica"] = "Arial",
            ["Times-Roman"] = "Times New Roman",
            ["Times"] = "Times New Roman",
            ["Courier"] = "Courier New",
        };

    /// <summary>Resolve (creating if needed) the page's /Resources /Font dictionary so a new
    /// font can be registered there; AddStampForm later shares it into the stamp form.</summary>
    private static PdfDictionary GetPageFontDict(Page page)
    {
        var resources = page.Dict.Get("Resources") as PdfDictionary
            ?? page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as PdfDictionary
            ?? page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }
        return fontDict;
    }

    /// <summary>True when the text contains any supplementary-plane code point
    /// (encoded as a UTF-16 surrogate pair).</summary>
    private static bool HasSupplementaryChars(string text)
    {
        foreach (var ch in text)
            if (char.IsSurrogate(ch)) return true;
        return false;
    }

    private static bool CoversCp(byte[] ttf, int cp)
    {
        try
        {
            var parser = _runCoverage.GetValue(ttf, static t => new GlyphOutlineParser(t));
            return parser.CMap.TryGetValue(cp, out var gid) && gid != 0;
        }
        catch { return false; }
    }

    // Single-byte encoder targeting WinAnsiEncoding. Chars that Windows-1252
    // already maps go through as-is; chars that don't (Polish/Czech/etc.) get
    // assigned a custom byte code in the 0x80-0x9F (and as needed 0x7F/0xA0)
    // range and an AGL glyph name returned via `diffMap` so the caller can
    // emit /Encoding /Differences. Truly unrepresentable chars fall back to '?'.
    private static byte[] EncodeForWinAnsi(string text, out List<(byte code, string glyph)> diffMap)
    {
        diffMap = new List<(byte, string)>();
        if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();
        // Managed Windows-1252 (Cp1252): chars not in WinAnsi report as
        // unmappable so we route them through the AGL /Differences path instead
        // of silently transliterating them — the renderer never draws the wrong
        // letter, and we always know when a glyph is missing.
        var bytes = new byte[text.Length];
        // Pick byte codes in WinAnsi's "control" / unused range first so we
        // don't clobber existing glyphs (euro, smartquote, …) that the same
        // stamp text might also include. Start at 0x81 (unused), then 0x8D,
        // 0x8F, 0x90, 0x9D (also unused), then fill 0x80-0x9F by index.
        // We do not reach the 256-byte limit in practice for stamp strings.
        var unusedSlots = new byte[] { 0x81, 0x8D, 0x8F, 0x90, 0x9D, 0x80, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8E, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0x9B, 0x9C, 0x9E, 0x9F };
        var nextSlot = 0;
        var assigned = new Dictionary<char, byte>();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            // ASCII + WinAnsi-supplement: encode straight.
            // Windows-1252 returns 0x3F ('?') for unrepresentable chars by
            // default, so we have to probe each char individually rather than
            // GetBytes the whole string in one shot.
            if (ch < 0x80)
            {
                bytes[i] = (byte)ch;
                continue;
            }
            if (Aspose.Pdf.Text.Cp1252.TryGetByte(ch, out var wb))
            {
                bytes[i] = wb;
                continue;
            }
            // Not in WinAnsi — map via AGL glyph name through /Differences.
            if (assigned.TryGetValue(ch, out var code))
            {
                bytes[i] = code;
                continue;
            }
            var glyph = AglGlyphName(ch);
            if (glyph is null || nextSlot >= unusedSlots.Length)
            {
                bytes[i] = (byte)'?';
                continue;
            }
            code = unusedSlots[nextSlot++];
            assigned[ch] = code;
            diffMap.Add((code, glyph));
            bytes[i] = code;
        }
        return bytes;
    }

    // Inverse of AglGlyphName for the chars we actually map via /Differences,
    // used to build the /ToUnicode CMap so the content-stream decoder turns
    // our custom byte codes back into real Unicode chars. Has to stay in
    // sync with AglGlyphName — the lookup is keyed on the glyph name we
    // emitted so a future glyph addition only needs both directions covered.
    private static int? AglUnicodeForGlyph(string glyph) => glyph switch
    {
        "Aogonek" => 0x0104, "aogonek" => 0x0105,
        "Cacute" => 0x0106, "cacute" => 0x0107,
        "Eogonek" => 0x0118, "eogonek" => 0x0119,
        "Lslash" => 0x0141, "lslash" => 0x0142,
        "Nacute" => 0x0143, "nacute" => 0x0144,
        "Sacute" => 0x015A, "sacute" => 0x015B,
        "Zacute" => 0x0179, "zacute" => 0x017A,
        "Zdotaccent" => 0x017B, "zdotaccent" => 0x017C,
        "Ccaron" => 0x010C, "ccaron" => 0x010D,
        "Dcaron" => 0x010E, "dcaron" => 0x010F,
        "Ecaron" => 0x011A, "ecaron" => 0x011B,
        "Ncaron" => 0x0147, "ncaron" => 0x0148,
        "Rcaron" => 0x0158, "rcaron" => 0x0159,
        "Tcaron" => 0x0164, "tcaron" => 0x0165,
        "Abreve" => 0x0102, "abreve" => 0x0103,
        "Hungarumlaut" => 0x0150, "ohungarumlaut" => 0x0151,
        "Uhungarumlaut" => 0x0170, "uhungarumlaut" => 0x0171,
        "Gbreve" => 0x011E, "gbreve" => 0x011F,
        "Idotaccent" => 0x0130, "dotlessi" => 0x0131,
        "Scedilla" => 0x015E, "scedilla" => 0x015F,
        "Amacron" => 0x0100, "amacron" => 0x0101,
        "Emacron" => 0x0112, "emacron" => 0x0113,
        "Imacron" => 0x012A, "imacron" => 0x012B,
        "Omacron" => 0x014C, "omacron" => 0x014D,
        "Umacron" => 0x016A, "umacron" => 0x016B,
        _ => null,
    };

    // Build a minimal /ToUnicode CMap stream containing one bfchar entry
    // per /Differences slot we emitted. The format follows PDF 32000 §9.10.3
    // (Identity-CIDSystemInfo, single-byte codespace). The page-level content
    // parser parses bfchar/bfrange entries to populate its toUnicode map,
    // which DrawText.Latin1 fallback otherwise can't do.
    private static PdfStream BuildToUnicodeCMap(List<(byte code, string glyph)> diffMap)
    {
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n");
        sb.Append("12 dict begin\n");
        sb.Append("begincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n");
        sb.Append("/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<00> <FF>\nendcodespacerange\n");
        sb.Append(diffMap.Count).Append(" beginbfchar\n");
        foreach (var (code, glyph) in diffMap)
        {
            var u = AglUnicodeForGlyph(glyph) ?? '?';
            sb.Append('<').Append(code.ToString("X2")).Append("> <")
              .Append(u.ToString("X4")).Append(">\n");
        }
        sb.Append("endbfchar\nendcmap\n");
        sb.Append("CMapName currentdict /CMap defineresource pop\nend\nend\n");
        return new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(sb.ToString()));
    }

    // Subset of the Adobe Glyph List covering Latin-Extended-A/B chars
    // that Standard-14 PostScript fonts ship with but WinAnsiEncoding
    // doesn't expose by default. Returning null lets the caller fall
    // back to '?' for chars no Standard-14 font can render anyway.
    private static string? AglGlyphName(char ch) => ch switch
    {
        // Polish: ę,ą,ś,ł,ń,ź,ż,ć
        'Ą' => "Aogonek",   'ą' => "aogonek",
        'Ć' => "Cacute",    'ć' => "cacute",
        'Ę' => "Eogonek",   'ę' => "eogonek",
        'Ł' => "Lslash",    'ł' => "lslash",
        'Ń' => "Nacute",    'ń' => "nacute",
        'Ś' => "Sacute",    'ś' => "sacute",
        'Ź' => "Zacute",    'ź' => "zacute",
        'Ż' => "Zdotaccent",'ż' => "zdotaccent",
        // Czech / Slovak (caron forms not in WinAnsi)
        'Č' => "Ccaron",    'č' => "ccaron",
        'Ď' => "Dcaron",    'ď' => "dcaron",
        'Ě' => "Ecaron",    'ě' => "ecaron",
        'Ň' => "Ncaron",    'ň' => "ncaron",
        'Ř' => "Rcaron",    'ř' => "rcaron",
        'Ť' => "Tcaron",    'ť' => "tcaron",
        // S-caron and Z-caron ARE in WinAnsi (0x8A/0x9A, 0x8E/0x9E) — handled by the encoder.
        // Romanian / Turkish breves
        'Ă' => "Abreve",    'ă' => "abreve",
        'Ő' => "Hungarumlaut",'ő' => "ohungarumlaut",
        'Ű' => "Uhungarumlaut",'ű' => "uhungarumlaut",
        'Ğ' => "Gbreve",    'ğ' => "gbreve",
        'İ' => "Idotaccent",'ı' => "dotlessi",
        'Ş' => "Scedilla",  'ş' => "scedilla",
        // Macrons (Baltic)
        'Ā' => "Amacron",   'ā' => "amacron",
        'Ē' => "Emacron",   'ē' => "emacron",
        'Ī' => "Imacron",   'ī' => "imacron",
        'Ō' => "Omacron",   'ō' => "omacron",
        'Ū' => "Umacron",   'ū' => "umacron",
        _ => null,
    };

    // Apply Bold/Italic flags from TextState (or IsBold/IsItalic) onto the
    // base font name, mapping Helvetica/Courier/Times to their Standard-14
    // variants. Falls back to the FontName property when TextState is unset
    // or holds the default Helvetica.
    private string ResolveBaseFontName()
    {
        var fn = TextState?.FontName ?? FontName ?? "Helvetica";
        var bold = TextState?.IsBold ?? false;
        var italic = TextState?.IsItalic ?? false;

        // Strip any already-baked style suffix so callers can pass
        // "Helvetica-Bold" via FontName and still get Bold|Italic
        // honoured from FontStyle without doubling up.
        var family = fn;
        foreach (var suffix in new[] { "-BoldOblique", "-BoldItalic", "-Bold", "-Oblique", "-Italic" })
        {
            if (family.EndsWith(suffix, StringComparison.Ordinal))
            {
                family = family.Substring(0, family.Length - suffix.Length);
                break;
            }
        }

        return family switch
        {
            "Helvetica" => (bold, italic) switch
            {
                (true, true) => "Helvetica-BoldOblique",
                (true, false) => "Helvetica-Bold",
                (false, true) => "Helvetica-Oblique",
                _ => "Helvetica",
            },
            "Times-Roman" or "Times" => (bold, italic) switch
            {
                (true, true) => "Times-BoldItalic",
                (true, false) => "Times-Bold",
                (false, true) => "Times-Italic",
                _ => "Times-Roman",
            },
            "Courier" => (bold, italic) switch
            {
                (true, true) => "Courier-BoldOblique",
                (true, false) => "Courier-Bold",
                (false, true) => "Courier-Oblique",
                _ => "Courier",
            },
            // Non-standard-14 families (e.g. Arial): qualify the BaseFont with a
            // comma-separated style suffix so the requested style is reflected in
            // the font's reported name. Font.FontName strips the comma
            // ("Arial,Bold" → "ArialBold"), matching the style-qualified name a
            // styled text stamp is expected to carry.
            _ => (bold, italic) switch
            {
                (true, true) => family + ",BoldItalic",
                (true, false) => family + ",Bold",
                (false, true) => family + ",Italic",
                _ => fn,
            },
        };
    }

    // Register a Standard-14 font in the page /Resources /Font dictionary
    // and return its resource name. When `diffMap` is non-empty, the entry
    // gets an /Encoding dict with /BaseEncoding /WinAnsiEncoding plus a
    // /Differences array mapping the custom byte codes to AGL glyph names;
    // a separate /F* slot is allocated per distinct (BaseFont, diffMap) pair
    // so two stamps using the same Helvetica with different Polish chars
    // each get their own encoding table.
    private static string EnsureFontResource(Page page, string baseFontName,
        List<(byte code, string glyph)>? diffMap = null)
    {
        // /Resources and /Font are frequently indirect references; resolve them
        // (rather than a bare `as PdfDictionary` cast that yields null) so we
        // don't replace the real dictionary — and drop the page's existing
        // fonts — with a fresh empty one.
        var resources = page.Dict.Get("Resources") as PdfDictionary
            ?? page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }

        var fontDict = resources.Get("Font") as PdfDictionary
            ?? page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }

        var hasDiffs = diffMap is { Count: > 0 };

        foreach (var key in fontDict.Keys)
        {
            var entry = fontDict.Get(key) as PdfDictionary
                ?? page.Reader.ResolveDict(fontDict.Get(key));
            if (entry is null) continue;
            var existing = entry.GetName("BaseFont");
            if (!string.Equals(existing, baseFontName, StringComparison.Ordinal)) continue;

            // Reuse only when the encoding mode matches. A vanilla WinAnsi
            // entry can't be shared by a stamp that also needs /Differences,
            // and vice versa; we don't try to grow an existing /Differences
            // array (the test corpus never needs it).
            var enc = entry.Get("Encoding");
            var entryHasDiffs = enc is PdfDictionary;
            if (entryHasDiffs == hasDiffs && !hasDiffs)
                return key;
        }

        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFontName));
        if (hasDiffs)
        {
            var encoding = new PdfDictionary();
            encoding.Set("Type", new PdfName("Encoding"));
            encoding.Set("BaseEncoding", new PdfName("WinAnsiEncoding"));
            var diffs = new PdfArray();
            // /Differences format: [ <code> /glyph1 /glyph2 ... <code2> /glyph ... ]
            // Each integer resets the "next code" pointer; following names map
            // to consecutive code points. We emit one integer per glyph for
            // simplicity (codes aren't necessarily consecutive in our
            // unused-slot allocation order).
            foreach (var (code, glyph) in diffMap!)
            {
                diffs.Add(new PdfInteger(code));
                diffs.Add(new PdfName(glyph));
            }
            encoding.Set("Differences", diffs);
            font.Set("Encoding", encoding);
            // Emit a matching /ToUnicode CMap so the content-stream decoder
            // (which only honours /ToUnicode, not /Differences→AGL) can map
            // our custom byte codes back to real Unicode for the renderer's
            // parser.CMap[unicode]→GID lookup. Without this the Polish glyph
            // would route to char 0x81/0x8D/… and find no Helvetica.ttf cmap
            // entry, drawing nothing.
            font.Set("ToUnicode", BuildToUnicodeCMap(diffMap));
        }
        else
        {
            font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        }
        fontDict.Set(name, font);

        return name;
    }
}
