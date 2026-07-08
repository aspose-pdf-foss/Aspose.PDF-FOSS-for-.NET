using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Type0/CIDFont byte-stream metadata: encoding width and CID→GID mapping.
/// Built from a font dictionary once per Tf operator; cached by the renderer.
/// </summary>
internal sealed class CidFontInfo
{
    /// <summary>Encoding produces 2-byte big-endian CIDs (Identity-H/V or 2-byte predefined CMaps).</summary>
    public required bool IsTwoByteEncoding { get; init; }

    /// <summary>CID→GID table. Null means Identity (GID == CID).</summary>
    public int[]? CidToGidMap { get; init; }

    /// <summary>
    /// Custom CMap byte-code → CID mapping parsed from /Encoding stream's
    /// `cidchar` / `cidrange` blocks. Null for predefined CMaps (Identity-H/V,
    /// UCS2, UTF16) where the input byte sequence already IS the CID.
    /// </summary>
    public System.Collections.Generic.Dictionary<int, int>? CMapCodeToCid { get; init; }

    /// <summary>
    /// True when the 2-byte encoding produces Unicode codepoints directly
    /// (Uni*-UCS2-*, Uni*-UTF16-*) rather than Adobe CIDs. Affects fallback
    /// rendering: Unicode codes go straight into the fallback font's cmap;
    /// CIDs need an AdobeCidTables CID→Unicode lookup first.
    /// </summary>
    public bool IsUnicodeEncoding { get; init; }

    /// <summary>
    /// Adobe CIDSystemInfo /Ordering value from the descendant CIDFont (e.g.
    /// "Japan1", "GB1", "CNS1", "Korea1", "Identity"). Needed to resolve
    /// CID→Unicode for fallback-font rendering when the embedded outline is
    /// missing. Null means we couldn't read the CIDSystemInfo.
    /// </summary>
    public string? Ordering { get; init; }

    /// <summary>
    /// Codepage of a predefined legacy national CMap whose byte-codes encode a
    /// national multi-byte charset (NOT Adobe CIDs). Currently only GBK / EUC-CN
    /// (936, Adobe-GB1) is backed by an embedded table (<see cref="GbkTable"/>);
    /// a non-embedded font using such a CMap is rendered by decoding the code to
    /// Unicode and looking the character up in a system CJK font. 0 when the
    /// encoding isn't a supported national CMap.
    /// </summary>
    public int LegacyCodepage { get; init; }

    /// <summary>
    /// The descendant /BaseFont name (e.g. "SimHei", or the GBK-mojibake "ºÚÌå").
    /// Used to pick a matching system CJK font (serif vs sans, weight) when the
    /// font is non-embedded. Null when unavailable.
    /// </summary>
    public string? CjkBaseFont { get; init; }

    /// <summary>
    /// Number of bytes the next code occupies given its lead byte, under this
    /// font's legacy codepage. GBK/EUC-CN: a lead byte of 0x81-0xFE starts a
    /// 2-byte code, everything else (ASCII) is a single byte.
    /// </summary>
    public int LegacyByteLength(byte lead) => LegacyCodepage switch
    {
        936 => lead is >= 0x81 and <= 0xFE ? 2 : 1,
        // Shift-JIS: 0x81-0x9F and 0xE0-0xFC start a 2-byte code; 0xA1-0xDF is a
        // single-byte half-width katakana; the rest are single-byte ASCII.
        932 => lead is (>= 0x81 and <= 0x9F) or (>= 0xE0 and <= 0xFC) ? 2 : 1,
        // EUC-KR / UHC: a lead byte of 0x81-0xFE starts a 2-byte code.
        949 => lead is >= 0x81 and <= 0xFE ? 2 : 1,
        // Big5: a lead byte of 0x81-0xFE starts a 2-byte code.
        950 => lead is >= 0x81 and <= 0xFE ? 2 : 1,
        _ => 1,
    };

    /// <summary>
    /// Decode one national byte-code to a Unicode codepoint via the embedded table
    /// for this font's legacy codepage. Returns null when unmapped/unsupported.
    /// </summary>
    public int? LegacyToUnicode(int code) => LegacyLookup(LegacyCodepage, code);

    /// <summary>Static form of <see cref="LegacyToUnicode"/> for callers that have a
    /// codepage but no CidFontInfo (e.g. decoding a CJK-encoded /BaseFont name).</summary>
    internal static int? LegacyLookup(int codepage, int code) => codepage switch
    {
        936 => GbkTable.ToUnicode(code),
        932 => SjisTable.ToUnicode(code),
        949 => KscTable.ToUnicode(code),
        950 => Big5Table.ToUnicode(code),
        _ => null,
    };

    /// <summary>
    /// True for a vertical writing-mode CMap (name ends in "-V"): glyphs stack
    /// top-to-bottom and the cursor advances down the page instead of across.
    /// </summary>
    public bool IsVertical { get; init; }

    /// <summary>Translate a byte-code from the show-string to a CID via the
    /// custom CMap. For predefined CMaps (Identity-H/V) returns code unchanged.</summary>
    public int CodeToCid(int code)
    {
        if (CMapCodeToCid is null) return code;
        return CMapCodeToCid.TryGetValue(code, out var cid) ? cid : 0;
    }

    /// <summary>Resolve a CID to a glyph index in the embedded TrueType font.</summary>
    public int ResolveGid(int cid)
    {
        if (CidToGidMap is null) return cid;
        return cid >= 0 && cid < CidToGidMap.Length ? CidToGidMap[cid] : 0;
    }

    /// <summary>
    /// Build CID font info from a Type0 font dictionary. Returns null for non-Type0 fonts.
    /// </summary>
    public static CidFontInfo? TryBuild(PdfDictionary fontDict, PdfReader reader)
    {
        if (fontDict.GetName("Subtype") != "Type0") return null;

        // /Encoding can be a direct name OR an indirect reference to a CMap stream
        // (PDF 32000 §9.7.5.2). Resolve through the reference first; if the resolved
        // object is a stream, peek at its dict's /CMapName for the predefined-name
        // lookup, otherwise inspect the stream's `begincodespacerange ... endcode-
        // spacerange` declarations to decide 2-byte vs 1-byte. Without this,
        // sub-setted PDFs that emit `/Encoding 23 0 R` for an embedded custom CMap
        // (CMapName = subset-prefix+family, not a predefined name) get isTwoByte =
        // false and the renderer walks raw bytes one at a time — every 2-byte CID
        // is drawn as two .notdef glyphs, doubling visible letter-spacing.
        string? encoding = fontDict.GetName("Encoding");
        PdfStream? encStream = null;
        if (encoding is null)
        {
            var encObj = reader.Resolve(fontDict.Get("Encoding"));
            if (encObj is PdfName encName) encoding = encName.Value;
            else if (encObj is PdfStream es)
            {
                encStream = es;
                encoding = es.Dict.GetName("CMapName");
            }
        }
        var isTwoByte = encoding switch
        {
            "Identity-H" => true,
            "Identity-V" => true,
            // UCS2/UTF16 variants and predefined CJK CMaps (UniJIS-*, UniGB-*, UniCNS-*, UniKS-*,
            // GB-EUC-H, ETen-B5-H, etc.) all use 2-byte big-endian codes.
            not null when encoding.Contains("-UCS2-") || encoding.Contains("-UTF16-") => true,
            not null when IsTwoByteCjkCMap(encoding) => true,
            _ => false,
        };
        // If the CMap name didn't identify a known encoding, fall back to parsing
        // the stream's codespace ranges. Custom subset CMaps name themselves after
        // the font (e.g. "NQTMYA+Lucida Sans Unicode,Bold") which can't be guessed.
        System.Collections.Generic.Dictionary<int, int>? cmapCodeToCid = null;
        if (encStream is not null)
        {
            try
            {
                var cmapBytes = reader.DecodeStream(encStream);
                if (!isTwoByte)
                    isTwoByte = CMapHasTwoByteCodespace(cmapBytes);
                // Custom CMaps map byte-codes → CIDs via `cidchar` / `cidrange`.
                // Without this, codes hit `CidToGidMap[code]` directly, which
                // produces wrong glyphs (e.g. 0x0046 looked up as CID 70 instead
                // of CID 4 via the CMap's `<0046>4` entry).
                cmapCodeToCid = ParseCMapCodeToCid(cmapBytes);
            }
            catch { }
        }
        // Uni*-UCS2-* and Uni*-UTF16-* CMaps emit Unicode codepoints (not
        // Adobe CIDs) for each 2-byte code. Identity-H/V and the legacy
        // bytecode CMaps (GB-EUC, ETen-B5, KSC-EUC, etc.) emit CIDs.
        var isUnicodeEnc = encoding is not null
            && (encoding.Contains("-UCS2-") || encoding.Contains("-UTF16-"));

        // DescendantFonts[0] holds the CIDFont dictionary that owns /CIDToGIDMap
        // and /CIDSystemInfo.
        int[]? cidToGid = null;
        string? ordering = null;
        double vertOriginY = 880, vertAdvance = -1000;
        Dictionary<int, (double, double, double)>? w2 = null;
        var descendantsObj = reader.Resolve(fontDict.Get("DescendantFonts"));
        if (descendantsObj is PdfArray descArr && descArr.Count > 0)
        {
            var cidFontDict = reader.ResolveDict(descArr[0]);
            if (cidFontDict is not null)
            {
                cidToGid = ReadCidToGidMap(cidFontDict, reader);
                // /CIDSystemInfo is a required entry on a CIDFont (§9.7.3).
                var sysInfo = reader.ResolveDict(cidFontDict.Get("CIDSystemInfo"));
                if (sysInfo is not null && sysInfo.Get("Ordering") is PdfString os)
                    ordering = os.ToText();
                // Vertical-writing defaults (/DW2 = [vy w1], default [880 -1000],
                // PDF 32000 §9.7.4.3) and the per-CID /W2 overrides.
                if (reader.Resolve(cidFontDict.Get("DW2")) is PdfArray dw2 && dw2.Count >= 2)
                {
                    vertOriginY = NumOf(dw2[0]);
                    vertAdvance = NumOf(dw2[1]);
                }
                w2 = ReadW2(cidFontDict, reader);
            }
        }

        // A predefined legacy national CMap (named, not a stream) encodes byte-codes
        // in the national charset rather than as Adobe CIDs. Record its codepage so
        // a non-embedded CIDFont can be rendered by decoding code → Unicode → system
        // CJK font. Stream CMaps (custom subset) and Unicode CMaps don't apply.
        var legacyCodepage = (encStream is null && !isUnicodeEnc)
            ? CodepageForCMap(encoding)
            : 0;

        return new CidFontInfo
        {
            IsTwoByteEncoding = isTwoByte,
            IsUnicodeEncoding = isUnicodeEnc,
            CidToGidMap = cidToGid,
            Ordering = ordering,
            CMapCodeToCid = cmapCodeToCid,
            LegacyCodepage = legacyCodepage,
            CjkBaseFont = fontDict.GetName("BaseFont"),
            IsVertical = encoding is not null && encoding.EndsWith("-V", StringComparison.Ordinal),
            VertOriginY = vertOriginY,
            VertAdvance = vertAdvance,
            W2 = w2,
        };
    }

    /// <summary>Default vertical origin Y (/DW2[0], glyph-space 1/1000 em).</summary>
    public double VertOriginY { get; init; } = 880;

    /// <summary>Default vertical displacement (/DW2[1], negative = downward).</summary>
    public double VertAdvance { get; init; } = -1000;

    /// <summary>Per-CID vertical metrics from /W2: CID → (w1y, vx, vy). Null when absent.</summary>
    public Dictionary<int, (double w1y, double vx, double vy)>? W2 { get; init; }

    /// <summary>Vertical metrics for one CID: displacement w1 (negative down), position
    /// vector v = (vx, vy) from the glyph origin to the vertical writing origin.
    /// Defaults per §9.7.4.3: v = (w0/2, /DW2 vy), w1 = /DW2 w1.</summary>
    public (double w1y, double vx, double vy) VerticalMetrics(int cid, double w0)
    {
        if (W2 is not null && W2.TryGetValue(cid, out var m)) return m;
        return (VertAdvance, w0 / 2.0, VertOriginY);
    }

    /// <summary>Parse the /W2 array (PDF 32000 §9.7.4.3): entries are either
    /// «cFirst cLast w1y vx vy» (range) or «c [w1y₁ vx₁ vy₁ w1y₂ vx₂ vy₂ …]» (list).</summary>
    private static Dictionary<int, (double, double, double)>? ReadW2(PdfDictionary cidFontDict, PdfReader reader)
    {
        if (reader.Resolve(cidFontDict.Get("W2")) is not PdfArray w2) return null;
        var map = new Dictionary<int, (double, double, double)>();
        var i = 0;
        while (i < w2.Count)
        {
            if (reader.Resolve(w2[i]) is not PdfInteger cFirst) break;
            if (i + 1 >= w2.Count) break;
            var second = reader.Resolve(w2[i + 1]);
            if (second is PdfArray list)
            {
                var cid = (int)cFirst.Value;
                for (var k = 0; k + 2 < list.Count; k += 3)
                    map[cid++] = (NumOf(list[k]), NumOf(list[k + 1]), NumOf(list[k + 2]));
                i += 2;
            }
            else if (second is PdfInteger cLast && i + 4 < w2.Count)
            {
                var w1y = NumOf(w2[i + 2]);
                var vx = NumOf(w2[i + 3]);
                var vy = NumOf(w2[i + 4]);
                for (var c = (int)cFirst.Value; c <= (int)cLast.Value; c++)
                    map[c] = (w1y, vx, vy);
                i += 5;
            }
            else break;
        }
        return map.Count > 0 ? map : null;
    }

    private static double NumOf(PdfObject? o) => o switch
    {
        PdfInteger pi => pi.Value,
        PdfReal pr => pr.Value,
        _ => 0,
    };

    /// <summary>
    /// Map a predefined CJK CMap name (PDF 32000-1:2008 Table 118) to the codepage
    /// that encodes its byte-codes, for the national charsets we can decode with an
    /// embedded table: Adobe-GB1 (GBK / EUC-CN → 936), Adobe-Japan1 (Shift-JIS → 932)
    /// and Adobe-Korea1 (EUC-KR / UHC → 949). Big5 would need its own embedded table.
    /// Returns 0 otherwise (Identity-H/V, Uni*-UCS2/UTF16, unsupported families).
    /// </summary>
    private static int CodepageForCMap(string? name)
    {
        if (name is null) return 0;
        // Adobe-GB1 (Simplified Chinese) — GBK / EUC-CN.
        if (name.StartsWith("GBK-EUC", StringComparison.Ordinal)
            || name.StartsWith("GBKp-EUC", StringComparison.Ordinal)
            || name.StartsWith("GBK2K", StringComparison.Ordinal)
            || name.StartsWith("GBpc-EUC", StringComparison.Ordinal)
            || name.StartsWith("GB-EUC", StringComparison.Ordinal)) return 936;
        // Adobe-Japan1 — Shift-JIS (RKSJ).
        if (name.StartsWith("90ms-RKSJ", StringComparison.Ordinal)
            || name.StartsWith("90msp-RKSJ", StringComparison.Ordinal)
            || name.StartsWith("90pv-RKSJ", StringComparison.Ordinal)
            || name.StartsWith("Add-RKSJ", StringComparison.Ordinal)
            || name.StartsWith("Ext-RKSJ", StringComparison.Ordinal)) return 932;
        // Adobe-Korea1 — EUC-KR (KS X 1001) and UHC (codepage 949).
        if (name.StartsWith("KSC-EUC", StringComparison.Ordinal)
            || name.StartsWith("KSCms-UHC", StringComparison.Ordinal)
            || name.StartsWith("KSCpc-EUC", StringComparison.Ordinal)) return 949;
        // Adobe-CNS1 (Traditional Chinese) — Big5 byte codes (NOT CNS-EUC, which is EUC-TW).
        if (name.StartsWith("ETen-B5", StringComparison.Ordinal)
            || name.StartsWith("ETenms-B5", StringComparison.Ordinal)
            || name.StartsWith("B5pc", StringComparison.Ordinal)
            || name.StartsWith("B5-", StringComparison.Ordinal)
            || name.StartsWith("HKscs-B5", StringComparison.Ordinal)) return 950;
        return 0;
    }

    /// <summary>Expose <see cref="CodepageForCMap"/> for callers that only have the
    /// /Encoding CMap name (e.g. decoding a CJK-encoded /BaseFont display name).</summary>
    internal static int CodepageForCMapName(string? name) => CodepageForCMap(name);

    private static int[]? ReadCidToGidMap(PdfDictionary cidFontDict, PdfReader reader)
    {
        // Per PDF 32000-1:2008 §9.7.4.2: missing /CIDToGIDMap or /Identity → Identity mapping.
        var mapObj = cidFontDict.Get("CIDToGIDMap");
        if (mapObj is null) return null;
        if (mapObj is PdfName pn && pn.Value == "Identity") return null;

        var stream = reader.ResolveStream(mapObj);
        if (stream is null) return null;

        byte[] bytes;
        try { bytes = reader.DecodeStream(stream); }
        catch { return null; }

        // Stream length is 2 × (highest CID + 1); pairs are big-endian GIDs.
        var count = bytes.Length / 2;
        var map = new int[count];
        for (var i = 0; i < count; i++)
            map[i] = (bytes[i * 2] << 8) | bytes[i * 2 + 1];
        return map;
    }

    // Predefined CJK CMaps that use 2-byte codes (PDF 32000-1:2008 Table 118).
    // We only need the horizontal ones; -V variants decode the same codes.
    /// <summary>
    /// True when the CMap stream declares any `begincodespacerange` entry where the
    /// low/high pair is two bytes wide (e.g. `<0000> <FFFF>`). PDF 32000 §9.7.5.4
    /// allows a CMap to mix codespace lengths, but in practice custom subset CMaps
    /// emitted by PDF generators for Type0 fonts use either all-1-byte or all-2-byte
    /// ranges. Spot the first 2-byte range and treat the whole CMap as 2-byte.
    /// </summary>
    private static bool CMapHasTwoByteCodespace(byte[] cmapBytes)
    {
        if (cmapBytes is null || cmapBytes.Length == 0) return false;
        // Treat as ASCII — CMap header / operators are PostScript-style ASCII tokens.
        // Look for "begincodespacerange ... endcodespacerange" and inspect the first
        // hex-string pair inside.
        var text = System.Text.Encoding.Latin1.GetString(cmapBytes);
        var start = text.IndexOf("begincodespacerange", StringComparison.Ordinal);
        if (start < 0) return false;
        var end = text.IndexOf("endcodespacerange", start, StringComparison.Ordinal);
        if (end < 0) return false;
        var slice = text.AsSpan(start, end - start);
        // Find the first '<...>' literal.
        var lt = slice.IndexOf('<');
        if (lt < 0) return false;
        var gt = slice[lt..].IndexOf('>');
        if (gt < 0) return false;
        // The hex chars between '<' and '>'. 2 hex chars = 1 byte; 4 = 2 bytes.
        var hex = slice.Slice(lt + 1, gt - 1);
        var hexCount = 0;
        foreach (var c in hex)
        {
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')) hexCount++;
        }
        return hexCount >= 4; // ≥4 hex digits → ≥2 bytes per code
    }

    /// <summary>
    /// Parse a custom CMap stream's `cidchar` and `cidrange` blocks into a
    /// code → CID dictionary. Skips `bfchar`/`bfrange` (those map to Unicode,
    /// not CIDs, and are used for /ToUnicode CMaps — separate concern).
    /// Returns null if the stream contains no recognised cid mappings, which
    /// signals "use byte-code as CID directly" (Identity-style).
    /// </summary>
    private static System.Collections.Generic.Dictionary<int, int>? ParseCMapCodeToCid(byte[] cmapBytes)
    {
        if (cmapBytes is null || cmapBytes.Length == 0) return null;
        var text = System.Text.Encoding.Latin1.GetString(cmapBytes);
        System.Collections.Generic.Dictionary<int, int>? map = null;

        // cidchar: lines like `<0041>2` between begincidchar / endcidchar
        var idx = 0;
        while (true)
        {
            var start = text.IndexOf("begincidchar", idx, StringComparison.Ordinal);
            if (start < 0) break;
            var end = text.IndexOf("endcidchar", start, StringComparison.Ordinal);
            if (end < 0) break;
            var block = text.Substring(start + "begincidchar".Length, end - (start + "begincidchar".Length));
            ParseCidCharLines(block, ref map);
            idx = end + 1;
        }

        // cidrange: lines like `<0054><0055>7` (codes 0x54-0x55 → CIDs 7-8)
        idx = 0;
        while (true)
        {
            var start = text.IndexOf("begincidrange", idx, StringComparison.Ordinal);
            if (start < 0) break;
            var end = text.IndexOf("endcidrange", start, StringComparison.Ordinal);
            if (end < 0) break;
            var block = text.Substring(start + "begincidrange".Length, end - (start + "begincidrange".Length));
            ParseCidRangeLines(block, ref map);
            idx = end + 1;
        }

        return map;
    }

    private static void ParseCidCharLines(string block, ref System.Collections.Generic.Dictionary<int, int>? map)
    {
        var p = 0;
        while (p < block.Length)
        {
            // Expect "<code> cid" — find next '<'.
            while (p < block.Length && block[p] != '<') p++;
            if (p >= block.Length) break;
            var endHex = block.IndexOf('>', p);
            if (endHex < 0) break;
            var hex = block.Substring(p + 1, endHex - p - 1);
            if (!TryParseHex(hex, out var code)) { p = endHex + 1; continue; }
            p = endHex + 1;
            // Skip whitespace and read a decimal number = CID.
            while (p < block.Length && (block[p] == ' ' || block[p] == '\t' || block[p] == '\n' || block[p] == '\r')) p++;
            var numStart = p;
            while (p < block.Length && block[p] >= '0' && block[p] <= '9') p++;
            if (p == numStart) continue;
            var cid = int.Parse(block.AsSpan(numStart, p - numStart));
            (map ??= new System.Collections.Generic.Dictionary<int, int>())[code] = cid;
        }
    }

    private static void ParseCidRangeLines(string block, ref System.Collections.Generic.Dictionary<int, int>? map)
    {
        var p = 0;
        while (p < block.Length)
        {
            // Expect "<startCode> <endCode> startCid"
            while (p < block.Length && block[p] != '<') p++;
            if (p >= block.Length) break;
            var endHex1 = block.IndexOf('>', p);
            if (endHex1 < 0) break;
            var hex1 = block.Substring(p + 1, endHex1 - p - 1);
            if (!TryParseHex(hex1, out var startCode)) { p = endHex1 + 1; continue; }
            p = endHex1 + 1;
            while (p < block.Length && block[p] != '<') p++;
            if (p >= block.Length) break;
            var endHex2 = block.IndexOf('>', p);
            if (endHex2 < 0) break;
            var hex2 = block.Substring(p + 1, endHex2 - p - 1);
            if (!TryParseHex(hex2, out var endCode)) { p = endHex2 + 1; continue; }
            p = endHex2 + 1;
            while (p < block.Length && (block[p] == ' ' || block[p] == '\t' || block[p] == '\n' || block[p] == '\r')) p++;
            var numStart = p;
            while (p < block.Length && block[p] >= '0' && block[p] <= '9') p++;
            if (p == numStart) continue;
            var startCid = int.Parse(block.AsSpan(numStart, p - numStart));
            map ??= new System.Collections.Generic.Dictionary<int, int>();
            for (int c = startCode, cid = startCid; c <= endCode; c++, cid++)
                map[c] = cid;
        }
    }

    private static bool TryParseHex(string hex, out int value)
    {
        value = 0;
        foreach (var c in hex)
        {
            int d;
            if (c >= '0' && c <= '9') d = c - '0';
            else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
            else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
            else return false;
            value = (value << 4) | d;
        }
        return true;
    }

    private static bool IsTwoByteCjkCMap(string name)
    {
        return name.StartsWith("UniJIS", StringComparison.Ordinal)
            || name.StartsWith("UniGB", StringComparison.Ordinal)
            || name.StartsWith("UniCNS", StringComparison.Ordinal)
            || name.StartsWith("UniKS", StringComparison.Ordinal)
            || name.StartsWith("GB-EUC", StringComparison.Ordinal)
            || name.StartsWith("GBK-EUC", StringComparison.Ordinal)
            || name.StartsWith("GBKp-EUC", StringComparison.Ordinal)
            || name.StartsWith("GBK2K-", StringComparison.Ordinal)
            || name.StartsWith("B5pc-", StringComparison.Ordinal)
            || name.StartsWith("ETen-B5-", StringComparison.Ordinal)
            || name.StartsWith("ETenms-B5-", StringComparison.Ordinal)
            || name.StartsWith("HKscs-B5-", StringComparison.Ordinal)
            || name.StartsWith("CNS-EUC-", StringComparison.Ordinal)
            || name.StartsWith("ETHK-B5-", StringComparison.Ordinal)
            || name.StartsWith("90ms-", StringComparison.Ordinal)
            || name.StartsWith("90msp-", StringComparison.Ordinal)
            || name.StartsWith("90pv-", StringComparison.Ordinal)
            || name.StartsWith("Add-", StringComparison.Ordinal)
            || name.StartsWith("EUC-", StringComparison.Ordinal)
            || name.StartsWith("Ext-", StringComparison.Ordinal)
            || name.StartsWith("NWP-", StringComparison.Ordinal)
            || name.StartsWith("V", StringComparison.Ordinal)
            || name.StartsWith("WP-Symbol", StringComparison.Ordinal)
            || name.StartsWith("KSC-EUC-", StringComparison.Ordinal)
            || name.StartsWith("KSCms-UHC-", StringComparison.Ordinal)
            || name.StartsWith("KSCpc-EUC-", StringComparison.Ordinal)
            || name.StartsWith("KSCms-UHC-HW-", StringComparison.Ordinal);
    }
}
