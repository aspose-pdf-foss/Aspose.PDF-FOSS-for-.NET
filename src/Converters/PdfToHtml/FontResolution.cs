using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    private sealed class HtmlFontRecord
    {
        public string Family { get; init; } = "sans-serif";
        /// <summary>Human-readable single family for fixed-layout CSS rules
        /// ("Century Gothic", "Calibri") — the BaseFont with subset prefix and
        /// style suffix removed and camel-case words re-spaced. The flow-layout
        /// path keeps <see cref="Family"/>'s generic fallback stack instead.</summary>
        public string CssFamily { get; init; } = "sans-serif";
        public string Weight { get; init; } = "normal";
        public string Style { get; init; } = "normal";
        public Func<byte[], string>? ToUnicode { get; init; }
        /// <summary>Base-encoding decode for fonts without a ToUnicode CMap
        /// (named encodings, /Differences glyph names, embedded cmap/post).</summary>
        public Func<byte[], string>? BaseDecode { get; init; }
        /// <summary>Whether the embedded program's cmap covers a codepoint;
        /// null when no embedded program (full coverage assumed).</summary>
        public Func<int, bool>? SubsetHas { get; init; }
        /// <summary>Whether a shown CHARACTER CODE resolves to a glyph the
        /// embedded program's own cmap can address (a GID-only variant glyph
        /// renders in the CSS fallback face instead). Null = always mapped.</summary>
        public Func<int, bool>? GlyphMapped { get; init; }
        /// <summary>The embedded program's advance for a shown CHARACTER CODE,
        /// milli-em — the browser-model metric when the subset itself is the
        /// served face. Null func or null result = measure by the resolved
        /// installed face instead.</summary>
        public Func<int, double?>? EmbeddedAdvMilli { get; init; }
        /// <summary>The embedded program's own hmtx advance for a shown CHARACTER
        /// CODE (cid → gid → hmtx/upm), milli-em, unquantized. The em-compensation
        /// dialect solves its spacing against exactly this basis: the line is
        /// measured with the glyph advances of the program being re-served,
        /// so a ligature code weighs its LIGATURE advance and every /W-vs-face
        /// rounding residue stays in the word-spacing numerator. Null when the
        /// font embeds no parsable TrueType program.</summary>
        public Func<int, double?>? ProgramAdvMilli { get; init; }
        /// <summary>Embedded program advance by CHARACTER (reverse ToUnicode →
        /// code → gid → hmtx), for ligature-component measuring.</summary>
        public Func<int, double?>? ProgramCharAdvMilli { get; init; }
        public bool IsCidFont { get; init; }
        /// <summary>A Type3 face: its glyphs are content-stream procedures, not a
        /// program a browser can be handed, so the text drawn with it is only ever a
        /// best-effort transcription.</summary>
        public bool IsType3 { get; init; }
        /// <summary>OS/2 usWinAscent / unitsPerEm of the embedded font program —
        /// the ascent fraction the fixed-layout `top` subtracts (not the
        /// FontDescriptor /Ascent). 1.0 when no embedded sfnt provides it.</summary>
        public double AscentFactor { get; init; } = 1.0;
        /// <summary>hhea (asc+|desc|)/upm — the stl_ line-height class value; 0 = no program.</summary>
        public double LineHeightEm { get; init; }
        /// <summary>Advance of one character code in em fractions (1000-unit widths
        /// / 1000), from /Widths (simple) or /W + /DW (CID); null = no width data.</summary>
        public Func<int, double>? AdvanceOf { get; init; }
        /// <summary>The font serves a SUBSTITUTE face's subset (SimSun standing in
        /// for a non-embedded, non-installed CJK font).</summary>
        public bool SubstituteFace { get; init; }
    }

    /// <summary>Build a code → advance (em fraction) lookup for <paramref name="font"/>:
    /// simple fonts from /FirstChar + /Widths (+ /MissingWidth), Type0 from the
    /// descendant's /W ranges with /DW as the default. Falls back to the embedded
    /// program's hmtx (through its cmap for simple fonts, CID→GID for composites)
    /// when the dictionary carries no widths; null when nothing is available.</summary>
    private static Func<int, double>? BuildAdvanceMap(PdfDictionary font, PdfReader reader, bool isCid)
    {
        try
        {
            if (!isCid)
            {
                var widths = reader.Resolve(font.Get("Widths")) as PdfArray;
                if (widths is { Count: > 0 })
                {
                    var first = (reader.Resolve(font.Get("FirstChar")) as PdfInteger)?.Value ?? 0;
                    var desc = reader.ResolveDict(font.Get("FontDescriptor"));
                    var missing = desc is not null
                        && reader.Resolve(desc.Get("MissingWidth")) is PdfInteger mw ? mw.Value : 0;
                    var arr = new double[widths.Count];
                    for (var i = 0; i < widths.Count; i++)
                        arr[i] = widths[i] is PdfInteger wi ? wi.Value
                            : widths[i] is PdfReal wr ? wr.Value : 0;
                    return code => code >= first && code - first < arr.Length
                        ? arr[code - first] / 1000.0 : missing / 1000.0;
                }
            }
            else
            {
                var descArr = reader.Resolve(font.Get("DescendantFonts")) as PdfArray;
                var descFont = descArr is { Count: > 0 } ? reader.ResolveDict(descArr[0]) : null;
                if (descFont is not null)
                {
                    double dw = reader.Resolve(descFont.Get("DW")) is PdfInteger d ? d.Value : 1000;
                    var map = reader.Resolve(descFont.Get("W")) is PdfArray w
                        ? ReadCidWidths(reader, w) : new Dictionary<int, double>();
                    if (map.Count > 0 || dw != 1000)
                        return code => map.TryGetValue(code, out var v) ? v / 1000.0 : dw / 1000.0;
                }
            }

            // No dictionary widths: try the embedded program's own advances.
            var ttf = GetEmbeddedTtf(font, reader);
            if (ttf is not null)
            {
                var parser = new Text.GlyphOutlineParser(ttf);
                var upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000.0;
                if (!isCid)
                    return code => parser.CMap.TryGetValue(code, out var gid) && gid != 0
                        ? parser.GetAdvanceWidth(gid) / upm : 0.5;
                return code => // Identity CID: the code is (usually) the glyph id
                    parser.GetAdvanceWidth(code) is var adv && adv > 0 ? adv / upm : 0.5;
            }
        }
        catch { /* fall through to null: extent pinning simply stays off */ }
        return null;
    }

    /// <summary>The CID font's /W array as a code -> width map: `c [w1 w2 ...]` runs and
    /// `cfirst clast w` ranges (a range is capped at 65536 codes).</summary>
    private static Dictionary<int, double> ReadCidWidths(PdfReader reader, PdfArray w)
    {
        var map = new Dictionary<int, double>();
        var i = 0;
        while (i < w.Count)
        {
            if (i + 1 < w.Count && reader.Resolve(w[i]) is PdfInteger c0
                && reader.Resolve(w[i + 1]) is PdfArray ws)
            {
                for (var k = 0; k < ws.Count; k++) map[(int)c0.Value + k] = NumAt(ws[k]);
                i += 2;
            }
            else if (i + 2 < w.Count && reader.Resolve(w[i]) is PdfInteger ca
                && reader.Resolve(w[i + 1]) is PdfInteger cb)
            {
                var val = NumAt(reader.Resolve(w[i + 2]));
                for (var c = (int)ca.Value; c <= cb.Value && c - ca.Value < 65536; c++) map[c] = val;
                i += 3;
            }
            else i++;
        }
        return map;
    }

    private static double NumAt(PdfObject? o) => o is PdfInteger pi ? pi.Value
        : o is PdfReal pr ? pr.Value : 0;

    /// <summary>
    /// Per-font output-character registry for ligature and unmapped-code handling.
    /// A char code whose ToUnicode sequence cannot be rendered from component glyphs
    /// (the embedded font has no cmap entries for them) is emitted as ONE character:
    /// the standard Unicode ligature char when the sequence has one, else the
    /// sequence's first character. When that character is already owned by a
    /// different char code of the same font, a fresh code is minted from U+A880
    /// upward instead. Identity-encoded CID codes with no unicode mapping at all
    /// mint directly — their char code is a glyph id, not text.
    /// </summary>
    private sealed class LigatureSubstitutor
    {
        private readonly Dictionary<int, string> _codeToText = new();
        private readonly HashSet<char> _owned = new();
        private char _mint = '\uA880';

        /// <summary>Register a collapsed ligature code with its preferred character.</summary>
        public string Register(int code, char desired)
        {
            if (_codeToText.TryGetValue(code, out var existing)) return existing;
            var ch = desired;
            while (_owned.Contains(ch)) ch = _mint++;
            _owned.Add(ch);
            var text = ch.ToString();
            _codeToText[code] = text;
            return text;
        }

        /// <summary>Register a code that has no derivable unicode at all.</summary>
        public string Mint(int code)
        {
            if (_codeToText.TryGetValue(code, out var existing)) return existing;
            var ch = _mint++;
            while (_owned.Contains(ch)) ch = _mint++;
            _owned.Add(ch);
            var text = ch.ToString();
            _codeToText[code] = text;
            return text;
        }
    }

    /// <summary>Resolve the /Font entries of one resource dictionary (a page's or a
    /// Form XObject's own) into decode-ready <see cref="HtmlFontRecord"/> records.</summary>
    private static Dictionary<string, HtmlFontRecord> ResolveFontsFromResources(PdfDictionary? resources,
        PdfReader reader, bool preferFontCmap = false,
        Dictionary<int, LigatureSubstitutor>? substitutors = null,
        string? defaultFontName = null, bool friendlyFamilies = false)
    {
        var result = new Dictionary<string, HtmlFontRecord>(StringComparer.Ordinal);
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;

        foreach (var key in fontDict.Keys)
        {
            if (!ResolveFontResource(key, fontDict, reader, preferFontCmap, substitutors, defaultFontName, friendlyFamilies, result)) break;
        }
        return result;
    }

    /// <summary>OS/2 usWinAscent / head unitsPerEm from an sfnt (TrueType or OTTO),
    /// or 0 when either table is missing/short.</summary>
    private static double SfntWinAscentFactor(byte[] sfnt)
    {
        try
        {
            if (sfnt.Length < 12) return 0;
            int U16(int at) => (sfnt[at] << 8) | sfnt[at + 1];
            var numTables = U16(4);
            int os2 = 0, head = 0;
            for (var t = 0; t < numTables; t++)
            {
                var rec = 12 + t * 16;
                if (rec + 16 > sfnt.Length) return 0;
                var tag = System.Text.Encoding.ASCII.GetString(sfnt, rec, 4);
                var off = (sfnt[rec + 8] << 24) | (sfnt[rec + 9] << 16) | (sfnt[rec + 10] << 8) | sfnt[rec + 11];
                if (tag == "OS/2") os2 = off;
                else if (tag == "head") head = off;
            }
            if (os2 == 0 || head == 0) return 0;
            if (os2 + 76 > sfnt.Length || head + 20 > sfnt.Length) return 0;
            var upm = U16(head + 18);
            var winAscent = U16(os2 + 74);
            return upm > 0 ? winAscent / (double)upm : 0;
        }
        catch { return 0; }
    }

    /// <summary>hhea (ascender + |descender|) / unitsPerEm — the
    /// line-height class value for a font (1.117188 for Arial); 0 when unreadable.</summary>
    private static double SfntLineHeightFactor(byte[] sfnt)
    {
        try
        {
            if (sfnt.Length < 12) return 0;
            int U16(int at) => (sfnt[at] << 8) | sfnt[at + 1];
            int S16(int at) { var v = U16(at); return v >= 0x8000 ? v - 0x10000 : v; }
            var numTables = U16(4);
            int hhea = 0, head = 0, os2 = 0;
            for (var t = 0; t < numTables; t++)
            {
                var rec = 12 + t * 16;
                if (rec + 16 > sfnt.Length) return 0;
                var tag = System.Text.Encoding.ASCII.GetString(sfnt, rec, 4);
                var off = (sfnt[rec + 8] << 24) | (sfnt[rec + 9] << 16) | (sfnt[rec + 10] << 8) | sfnt[rec + 11];
                if (tag == "hhea") hhea = off;
                else if (tag == "head") head = off;
                else if (tag == "OS/2") os2 = off;
            }
            if (head == 0 || head + 20 > sfnt.Length) return 0;
            var upm = U16(head + 18);
            if (upm <= 0) return 0;
            var hheaLh = 0.0;
            if (hhea != 0 && hhea + 8 <= sfnt.Length)
            {
                var ascender = S16(hhea + 4);
                var descender = S16(hhea + 6);
                hheaLh = (ascender + Math.Abs(descender)) / (double)upm;
            }
            // A subset whose hhea is the degenerate 1-em placeholder (1536/-512
            // at 2048) still carries the real face metrics in OS/2
            // usWinAscent/usWinDescent; a live hhea stays authoritative.
            if (hheaLh != 0.0 && hheaLh != 1.0) return hheaLh;
            if (os2 > 0 && os2 + 78 <= sfnt.Length)
            {
                var winA = U16(os2 + 74);
                var winD = U16(os2 + 76);
                if (winA + winD > 0) return (winA + winD) / (double)upm;
            }
            return hheaLh;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Apply a ToUnicode CMap to raw string bytes.
    /// For CID fonts (Type0), character codes are 2 bytes each.
    /// For simple fonts, character codes are 1 byte each.
    /// </summary>
    private static string ApplyToUnicode(byte[] bytes, Dictionary<int, string> map, bool isCid,
        Dictionary<int, int>? reverseCmap = null, HashSet<int>? cmapChars = null,
        LigatureSubstitutor? substitutor = null, bool isIdentity = false,
        Dictionary<int, int>? gidToUnicode = null, bool cidCodeIsNotGid = false)
    {
        var sb = new StringBuilder();

        if (isCid)
        {
            // 2-byte character codes
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                // A reverse font-cmap entry (gid → unicode) outranks /ToUnicode when
                // the caller asked for cmap priority (see ResolveFonts).
                if (reverseCmap is not null && reverseCmap.TryGetValue(code, out var cmapCh))
                    sb.Append(char.ConvertFromUtf32(cmapCh));
                else if (map.TryGetValue(code, out var unicode))
                    sb.Append(MapDst(code, unicode, cmapChars, substitutor));
                else if (isIdentity && gidToUnicode is not null
                         && gidToUnicode.TryGetValue(code, out var uniCh))
                    sb.Append(char.ConvertFromUtf32(uniCh));
                else if (isIdentity && cidCodeIsNotGid)
                    // A CIDToGIDMap STREAM marks the code as a true CID, not a bare
                    // glyph id — nothing to mint. Fall back to the CID
                    // as a raw character (producers commonly assign CID = Unicode).
                    sb.Append((char)code);
                else if (isIdentity && substitutor is not null)
                    sb.Append(substitutor.Mint(code));
                else
                    sb.Append('?');
            }
        }
        else
        {
            // 1-byte character codes
            foreach (var b in bytes)
            {
                if (map.TryGetValue(b, out var unicode))
                    sb.Append(MapDst(b, unicode, cmapChars, substitutor));
                else
                    sb.Append((char)b);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The output text for one char code's ToUnicode sequence. Multi-char sequences
    /// stay expanded when the font's cmap can render every component character;
    /// otherwise the sequence collapses to a single stand-in registered with the
    /// font's substitutor (see <see cref="LigatureSubstitutor"/>).
    /// </summary>
    private static string MapDst(int code, string dst, HashSet<int>? cmapChars,
        LigatureSubstitutor? substitutor)
    {
        if (CodePointCount(dst) <= 1)
        {
            // A single-char dst naming an UNASSIGNED Unicode code point (category
            // Cn — e.g. a custom-encoded font whose identity ToUnicode lands raw
            // char codes in the U+FFDD / U+FFF0–FFF8 reserved gaps) is not real
            // text: each such char CODE is replaced with a
            // fresh minted character (U+A880 upward, in first-use order across the
            // conversion) so distinct glyphs keep distinct text. Assigned chars —
            // including private-use — pass through untouched.
            if (substitutor is not null && dst.Length == 1
                && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(dst[0])
                    == System.Globalization.UnicodeCategory.OtherNotAssigned)
                return substitutor.Mint(code);
            return dst;
        }
        if (dst == SpaceLigature) return " ";
        if (cmapChars is null || substitutor is null || AllInCmap(dst, cmapChars)) return dst;
        return substitutor.Register(code, StandardLigatureChar(dst));
    }

    /// <summary>The human-readable CSS family for a /BaseFont name: subset prefix
    /// ("ABCDEF+") and style suffix (the first "-"/"," segment and any trailing
    /// Bold/Italic/Oblique words) stripped, then glued camel-case words re-spaced —
    /// "CenturyGothic" → "Century Gothic", "Calibri-Bold" → "Calibri",
    /// "TimesNewRomanPSMT" → "Times New Roman".</summary>
    internal static string FriendlyFontFamily(string baseFont)
    {
        var name = baseFont;
        if (name.Length > 7 && name[6] == '+') name = name[7..];
        var cut = name.IndexOfAny(new[] { '-', ',' });
        if (cut > 0) name = name[..cut];
        // PostScript naming tails that are not part of the family.
        foreach (var tail in new[] { "PSMT", "PS", "MT" })
            if (name.Length > tail.Length && name.EndsWith(tail, StringComparison.Ordinal))
            { name = name[..^tail.Length]; break; }
        foreach (var styleWord in new[] { "BoldItalic", "BoldOblique", "Bold", "Italic", "Oblique" })
            if (name.Length > styleWord.Length && name.EndsWith(styleWord, StringComparison.Ordinal))
            { name = name[..^styleWord.Length]; break; }
        if (name.Length == 0) return "sans-serif";
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])
                && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }
}
