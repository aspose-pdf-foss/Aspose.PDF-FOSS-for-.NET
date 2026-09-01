using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Loads PostScript Type 1 fonts (PDF <c>/FontFile</c>) and serves glyph
/// outlines to the software rasterizer. Implements Adobe Technical Note #5040
/// (Type 1 Font Format): eexec stream decryption, charstring-decryption with
/// <c>/lenIV</c>, the §6.1 Type 1 CharString command set, and Standard
/// Encoding.
///
/// Only the geometry-bearing commands are honoured — hint operators
/// (hstem/vstem/hstem3/vstem3/dotsection) are popped off the stack as no-ops.
/// Type 1 <c>seac</c> (12 6, standard accent compose) is partially honoured:
/// the base glyph is drawn at the requested offset, the accent on top. flex
/// (callothersubr 0/1/2) is rendered as two cubic Béziers through the spec's
/// 6 control points + 1 endpoint, matching Adobe TN #5040 §5.2.
/// </summary>
internal sealed class Type1GlyphSource : IGlyphOutlineSource
{
    private readonly byte[][] _charStringsByGid;
    private readonly string?[] _namesByGid;
    private readonly byte[]?[] _subrs;
    private readonly int _lenIV;
    private readonly double _scaleToEm;

    public int UnitsPerEm { get; private set; } = 1000;
    public Dictionary<int, int> CMap { get; } = new();
    /// <summary>Glyph-name → GID. PDF /Differences entries reference glyph
    /// names that the renderer must resolve to outlines.</summary>
    public Dictionary<string, int> NameToGid { get; } = new(StringComparer.Ordinal);
    /// <summary>Byte → GID from the font's own /Encoding section. Renderers use
    /// this as the simple-encoding fallback when the PDF font dict's /Encoding
    /// is /StandardEncoding and the content stream emits raw bytes.</summary>
    public int[]? EncodingByteToGid { get; private set; }

    /// <summary>Try to parse a Type 1 font stream. <paramref name="length1"/>
    /// and <paramref name="length2"/> come from the /FontFile stream dict;
    /// when either is zero we scan for the <c>currentfile eexec</c> /
    /// <c>cleartomark</c> markers instead. Returns null when the stream is
    /// malformed or has no CharStrings.</summary>
    public static Type1GlyphSource? TryLoad(byte[] data, int length1, int length2)
    {
        if (data is null || data.Length < 16) return null;

        // Locate the boundary between the ASCII header and the eexec block.
        var headerEnd = length1 > 0 && length1 <= data.Length
            ? length1
            : FindHeaderEnd(data);
        if (headerEnd <= 0 || headerEnd >= data.Length) return null;

        // Length2 — eexec encrypted block. When the dict doesn't carry it, take
        // everything up to the trailing zero section that ends with cleartomark.
        var encryptedEnd = length2 > 0 && headerEnd + length2 <= data.Length
            ? headerEnd + length2
            : FindEncryptedEnd(data, headerEnd);
        if (encryptedEnd <= headerEnd) return null;

        var header = Encoding.Latin1.GetString(data, 0, headerEnd);
        var encrypted = ExtractEexecBytes(data, headerEnd, encryptedEnd);
        if (encrypted is null || encrypted.Length < 8) return null;

        var plain = EexecDecrypt(encrypted);
        if (plain is null || plain.Length == 0) return null;

        // Parse the relevant pieces out of the eexec plaintext.
        var lenIV = ParseLenIV(plain);
        var subrs = ParseSubrs(plain, lenIV);
        var (charStrings, ordering) = ParseCharStrings(plain, lenIV);
        if (charStrings.Count == 0) return null;

        var src = new Type1GlyphSource(charStrings, ordering, subrs, lenIV,
            unitsPerEm: ResolveUnitsPerEm(header));
        src.ParseHeaderEncoding(header);
        src.BuildUnicodeCMap();
        return src;
    }

    private Type1GlyphSource(Dictionary<string, byte[]> charStrings,
        List<string> ordering, byte[]?[] subrs, int lenIV, int unitsPerEm)
    {
        _lenIV = lenIV;
        _subrs = subrs;
        UnitsPerEm = unitsPerEm > 0 ? unitsPerEm : 1000;
        _scaleToEm = UnitsPerEm == 1000 ? 1.0 : UnitsPerEm / 1000.0;

        var n = ordering.Count;
        _charStringsByGid = new byte[n][];
        _namesByGid = new string?[n];
        for (var gid = 0; gid < n; gid++)
        {
            var name = ordering[gid];
            _namesByGid[gid] = name;
            _charStringsByGid[gid] = charStrings[name];
            NameToGid[name] = gid;
        }
    }

    /// <summary>Serve outlines with every vertex rounded to whole font units — how this
    /// program renders after a TrueType conversion (see
    /// <see cref="CffGlyphSource.QuantizeToFontUnits"/>).</summary>
    public bool QuantizeToFontUnits { get; set; }

    public GlyphOutline? GetOutline(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _charStringsByGid.Length) return null;
        var cs = _charStringsByGid[glyphId];
        if (cs is null || cs.Length == 0) return null;

        var interp = new Type1Interpreter(this);
        try
        {
            var outline = interp.Run(cs);
            return QuantizeToFontUnits && outline is not null
                ? GlyphOutlineQuantizer.Quantize(outline, UnitsPerEm)
                : outline;
        }
        catch { return null; }
    }

    public int GidForName(string name) => NameToGid.TryGetValue(name, out var gid) ? gid : 0;

    /// <summary>Number of charstrings the font carries.</summary>
    public int GlyphCount => _charStringsByGid.Length;

    /// <summary>Advance width in font units, taken from the charstring's own
    /// hsbw/sbw. 0 when the glyph is missing or unreadable.</summary>
    public int GetAdvanceWidth(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _charStringsByGid.Length) return 0;
        var cs = _charStringsByGid[glyphId];
        if (cs is null || cs.Length == 0) return 0;
        var interp = new Type1Interpreter(this);
        try
        {
            interp.Run(cs);
            return (int)Math.Round(interp.Width);
        }
        catch { return 0; }
    }

    internal byte[]? GetSubr(int idx) =>
        idx >= 0 && idx < _subrs.Length ? _subrs[idx] : null;

    internal byte[]? GetCharStringByName(string name) =>
        NameToGid.TryGetValue(name, out var gid) ? _charStringsByGid[gid] : null;

    internal int LenIV => _lenIV;
    internal double ScaleToEm => _scaleToEm;

    // ── Header scanning ──────────────────────────────────────────────────

    /// <summary>Find the byte offset immediately after <c>currentfile eexec</c>
    /// in the ASCII header. The PostScript convention puts one whitespace byte
    /// between the <c>eexec</c> token and the first encrypted byte.</summary>
    private static int FindHeaderEnd(byte[] data)
    {
        var marker = Encoding.ASCII.GetBytes("currentfile eexec");
        var idx = IndexOf(data, marker, 0);
        if (idx < 0) return -1;
        var end = idx + marker.Length;
        // Skip exactly one whitespace byte after eexec (LF / CR / space).
        if (end < data.Length && (data[end] == ' ' || data[end] == '\t'
            || data[end] == '\r' || data[end] == '\n')) end++;
        // Some fonts have CRLF.
        if (end < data.Length && data[end - 1] == '\r' && data[end] == '\n') end++;
        return end;
    }

    private static int FindEncryptedEnd(byte[] data, int start)
    {
        // Section 3 (trailing zeros + cleartomark) begins with 512 ASCII '0'
        // bytes. Scan from the end backwards to locate the run.
        var marker = Encoding.ASCII.GetBytes("cleartomark");
        var idx = IndexOf(data, marker, start);
        if (idx < 0) return data.Length;
        // Walk back over the zero/CR/LF padding that precedes cleartomark.
        var p = idx - 1;
        while (p > start && (data[p] == '\r' || data[p] == '\n' ||
            data[p] == '0' || data[p] == ' ' || data[p] == '\t'))
            p--;
        return p + 1;
    }

    /// <summary>Return the eexec-encrypted bytes, converting from ASCII hex if
    /// that variant was used (detected by the first four bytes being hex
    /// digits + whitespace).</summary>
    private static byte[]? ExtractEexecBytes(byte[] data, int start, int end)
    {
        if (start >= end) return null;
        // Hex-encoded eexec: leading bytes are all ASCII hex digits.
        var sniffEnd = Math.Min(start + 8, end);
        var hexLooking = true;
        for (var i = start; i < sniffEnd; i++)
        {
            var b = data[i];
            if (IsHexDigit(b) || b == ' ' || b == '\r' || b == '\n' || b == '\t') continue;
            hexLooking = false; break;
        }
        if (!hexLooking)
        {
            var raw = new byte[end - start];
            Array.Copy(data, start, raw, 0, raw.Length);
            return raw;
        }
        var buf = new List<byte>((end - start) / 2);
        int nibble = -1;
        for (var i = start; i < end; i++)
        {
            var b = data[i];
            if (b == ' ' || b == '\r' || b == '\n' || b == '\t') continue;
            if (!IsHexDigit(b)) break;
            var v = b <= '9' ? b - '0'
                  : b <= 'F' ? b - 'A' + 10
                  :            b - 'a' + 10;
            if (nibble < 0) { nibble = v; }
            else { buf.Add((byte)((nibble << 4) | v)); nibble = -1; }
        }
        return buf.ToArray();
    }

    private static bool IsHexDigit(byte b) =>
        (b >= '0' && b <= '9') || (b >= 'A' && b <= 'F') || (b >= 'a' && b <= 'f');

    /// <summary>eexec stream decryption. Per TN #5040 §7.2 with R₀ = 55665,
    /// c₁ = 52845, c₂ = 22719. Discards the four salt bytes at the front.</summary>
    private static byte[]? EexecDecrypt(byte[] cipher)
    {
        const int c1 = 52845, c2 = 22719;
        ushort r = 55665;
        var plain = new byte[cipher.Length];
        for (var i = 0; i < cipher.Length; i++)
        {
            var c = cipher[i];
            plain[i] = (byte)(c ^ (r >> 8));
            r = (ushort)((c + r) * c1 + c2);
        }
        if (plain.Length <= 4) return null;
        var stripped = new byte[plain.Length - 4];
        Array.Copy(plain, 4, stripped, 0, stripped.Length);
        return stripped;
    }

    private static byte[] CharStringDecrypt(byte[] cipher, int lenIV)
    {
        const int c1 = 52845, c2 = 22719;
        ushort r = 4330;
        var plain = new byte[cipher.Length];
        for (var i = 0; i < cipher.Length; i++)
        {
            var c = cipher[i];
            plain[i] = (byte)(c ^ (r >> 8));
            r = (ushort)((c + r) * c1 + c2);
        }
        if (lenIV >= plain.Length) return Array.Empty<byte>();
        if (lenIV <= 0) return plain;
        var stripped = new byte[plain.Length - lenIV];
        Array.Copy(plain, lenIV, stripped, 0, stripped.Length);
        return stripped;
    }

    // ── Plaintext PostScript parsing ─────────────────────────────────────

    private static int ParseLenIV(byte[] plain)
    {
        var s = Encoding.Latin1.GetString(plain);
        var idx = s.IndexOf("/lenIV", StringComparison.Ordinal);
        if (idx < 0) return 4;
        var p = idx + "/lenIV".Length;
        // Skip whitespace.
        while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
        var start = p;
        while (p < s.Length && char.IsDigit(s[p])) p++;
        if (p == start) return 4;
        return int.TryParse(s.AsSpan(start, p - start), out var v) ? v : 4;
    }

    /// <summary>Parse the /Subrs array. Each entry has the shape
    /// <c>dup INDEX LEN RD &lt;LEN encrypted bytes&gt; NP</c> (or <c>noaccess put</c>).</summary>
    private static byte[]?[] ParseSubrs(byte[] plain, int lenIV)
    {
        // Locate the "/Subrs N array" line.
        var s = Encoding.Latin1.GetString(plain);
        var idx = s.IndexOf("/Subrs", StringComparison.Ordinal);
        if (idx < 0) return Array.Empty<byte[]?>();
        // Bytes-based scanning from here on; PostScript strings can carry binary
        // payload after "RD " so character-string scanning past that point isn't
        // safe.
        var pos = idx;
        var nEnd = s.IndexOf("array", pos, StringComparison.Ordinal);
        if (nEnd < 0) return Array.Empty<byte[]?>();
        var nStr = s.Substring(pos + "/Subrs".Length, nEnd - (pos + "/Subrs".Length)).Trim();
        if (!int.TryParse(nStr, out var count) || count <= 0) return Array.Empty<byte[]?>();

        var result = new byte[count][]!;
        // Find the byte offset of "array" in the plain stream.
        var binStart = nEnd + "array".Length;
        var p = binStart;
        for (var entry = 0; entry < count; entry++)
        {
            // Find the next "dup".
            var dupAt = IndexOfToken(plain, "dup", p);
            if (dupAt < 0) break;
            // Following: "<spaces><index> <length> RD<space>" then LENGTH raw bytes,
            // then "NP" or "noaccess put".
            var cursor = dupAt + 3;
            cursor = SkipWhitespace(plain, cursor);
            cursor = ParseInt(plain, cursor, out var subIdx);
            cursor = SkipWhitespace(plain, cursor);
            cursor = ParseInt(plain, cursor, out var subLen);
            cursor = SkipWhitespace(plain, cursor);
            // "RD" or "-|" — both 2 ASCII chars; skip until the single whitespace
            // that precedes the binary payload.
            cursor = SkipToken(plain, cursor); // skip RD/-|
            // Exactly one whitespace byte before the payload (PostScript convention).
            if (cursor < plain.Length && (plain[cursor] == ' ' || plain[cursor] == '\t'
                || plain[cursor] == '\r' || plain[cursor] == '\n'))
                cursor++;
            if (subLen < 0 || cursor + subLen > plain.Length) break;
            var rawCipher = new byte[subLen];
            Array.Copy(plain, cursor, rawCipher, 0, subLen);
            cursor += subLen;
            if (subIdx >= 0 && subIdx < count)
                result[subIdx] = CharStringDecrypt(rawCipher, lenIV);
            p = cursor;
        }
        return result!;
    }

    private static (Dictionary<string, byte[]>, List<string>) ParseCharStrings(byte[] plain, int lenIV)
    {
        var s = Encoding.Latin1.GetString(plain);
        var idx = s.IndexOf("/CharStrings", StringComparison.Ordinal);
        var dict = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var ordering = new List<string>();
        if (idx < 0) return (dict, ordering);

        // Walk in bytes from just after "/CharStrings N dict".
        var dictAt = s.IndexOf("dict", idx, StringComparison.Ordinal);
        if (dictAt < 0) return (dict, ordering);
        var beginAt = s.IndexOf("begin", dictAt, StringComparison.Ordinal);
        if (beginAt < 0) beginAt = dictAt + "dict".Length;
        var p = beginAt + "begin".Length;

        while (p < plain.Length)
        {
            // Find next '/' that introduces a glyph name.
            while (p < plain.Length && plain[p] != '/' && !IsEndOfCharStringsDict(plain, p))
                p++;
            if (p >= plain.Length) break;
            if (IsEndOfCharStringsDict(plain, p)) break;
            // Read glyph name (until whitespace).
            p++; // past '/'
            var nameStart = p;
            while (p < plain.Length && !IsAsciiWhite(plain[p])) p++;
            var name = Encoding.Latin1.GetString(plain, nameStart, p - nameStart);
            // Skip whitespace.
            p = SkipWhitespace(plain, p);
            // Read length.
            p = ParseInt(plain, p, out var charLen);
            p = SkipWhitespace(plain, p);
            // Skip "RD" or "-|".
            p = SkipToken(plain, p);
            // One whitespace byte separator before binary payload.
            if (p < plain.Length && IsAsciiWhite(plain[p])) p++;
            if (charLen < 0 || p + charLen > plain.Length) break;
            var rawCipher = new byte[charLen];
            Array.Copy(plain, p, rawCipher, 0, charLen);
            p += charLen;
            var cs = CharStringDecrypt(rawCipher, lenIV);
            if (!dict.ContainsKey(name))
            {
                dict[name] = cs;
                ordering.Add(name);
            }
        }

        // Ensure .notdef is at GID 0 if present.
        if (ordering.Count > 1)
        {
            var notdefIdx = ordering.IndexOf(".notdef");
            if (notdefIdx > 0)
            {
                ordering.RemoveAt(notdefIdx);
                ordering.Insert(0, ".notdef");
            }
        }
        return (dict, ordering);
    }

    private static bool IsEndOfCharStringsDict(byte[] plain, int p)
    {
        // "end" followed by whitespace or end-of-buffer marks the dict close.
        if (p + 3 > plain.Length) return true;
        if (plain[p] == 'e' && plain[p + 1] == 'n' && plain[p + 2] == 'd' &&
            (p + 3 == plain.Length || IsAsciiWhite(plain[p + 3])))
            return true;
        return false;
    }

    private static bool IsAsciiWhite(byte b) =>
        b == ' ' || b == '\t' || b == '\r' || b == '\n';

    private static int SkipWhitespace(byte[] data, int p)
    {
        while (p < data.Length && IsAsciiWhite(data[p])) p++;
        return p;
    }

    private static int SkipToken(byte[] data, int p)
    {
        while (p < data.Length && !IsAsciiWhite(data[p])) p++;
        return p;
    }

    private static int ParseInt(byte[] data, int p, out int value)
    {
        value = 0;
        var sign = 1;
        if (p < data.Length && (data[p] == '+' || data[p] == '-'))
        {
            if (data[p] == '-') sign = -1;
            p++;
        }
        var any = false;
        while (p < data.Length && data[p] >= '0' && data[p] <= '9')
        {
            value = value * 10 + (data[p] - '0');
            p++; any = true;
        }
        if (!any) value = -1;
        else value *= sign;
        return p;
    }

    private static int IndexOf(byte[] hay, byte[] needle, int start)
    {
        for (var i = start; i <= hay.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }

    private static int IndexOfToken(byte[] hay, string needle, int start)
    {
        var bytes = Encoding.ASCII.GetBytes(needle);
        for (var i = start; i <= hay.Length - bytes.Length; i++)
        {
            if (i > 0 && !IsAsciiWhite(hay[i - 1])) continue;
            var ok = true;
            for (var j = 0; j < bytes.Length; j++)
                if (hay[i + j] != bytes[j]) { ok = false; break; }
            if (!ok) continue;
            if (i + bytes.Length < hay.Length && !IsAsciiWhite(hay[i + bytes.Length])) continue;
            return i;
        }
        return -1;
    }

    private static int ResolveUnitsPerEm(string header)
    {
        // /FontMatrix [a b c d e f] readonly def — a usually 0.001 → UnitsPerEm 1000.
        var m = System.Text.RegularExpressions.Regex.Match(header,
            @"/FontMatrix\s*\[\s*([0-9.+\-eE]+)\b");
        if (!m.Success) return 1000;
        if (!double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var a)) return 1000;
        if (a <= 0) return 1000;
        return (int)Math.Round(1.0 / a);
    }

    // ── Header /Encoding parsing ─────────────────────────────────────────

    private void ParseHeaderEncoding(string header)
    {
        // Two flavours:
        //   /Encoding StandardEncoding def
        //   /Encoding 256 array 0 1 255 {1 index exch /.notdef put} for
        //     dup 32 /space put dup 65 /A put ... readonly def
        var table = new int[256];
        var standardIdx = header.IndexOf("/Encoding StandardEncoding", StringComparison.Ordinal);
        if (standardIdx >= 0)
        {
            for (var b = 0; b < 256; b++)
            {
                var name = Type1StandardEncoding.GetName(b);
                if (name is not null && NameToGid.TryGetValue(name, out var gid))
                    table[b] = gid;
            }
            EncodingByteToGid = table;
            return;
        }
        // Explicit table — scan all "dup <code> /<name> put" entries.
        var rx = new System.Text.RegularExpressions.Regex(
            @"dup\s+(\d+)\s+/([^\s]+)\s+put",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        var any = false;
        foreach (System.Text.RegularExpressions.Match m in rx.Matches(header))
        {
            if (!int.TryParse(m.Groups[1].Value, out var code)) continue;
            if (code < 0 || code >= 256) continue;
            var name = m.Groups[2].Value;
            if (NameToGid.TryGetValue(name, out var gid)) { table[code] = gid; any = true; }
        }
        if (any) EncodingByteToGid = table;
    }

    private void BuildUnicodeCMap()
    {
        for (var gid = 0; gid < _namesByGid.Length; gid++)
        {
            var name = _namesByGid[gid];
            if (string.IsNullOrEmpty(name)) continue;
            // ResolveGlyphName also handles uniXXXX/uXXXX forms and AGL
            // underscore ligatures (/f_i → U+FB01); only a single-codepoint
            // resolution can occupy a cmap slot.
            var u = TextAbsorber.ResolveGlyphName(name!);
            if (u is { Length: > 0 } && (u.Length == 1 || char.IsHighSurrogate(u[0])))
                CMap.TryAdd(char.ConvertToUtf32(u, 0), gid);
        }
    }

    // ── Type 1 CharString interpreter ────────────────────────────────────

    private sealed class Type1Interpreter
    {
        private readonly Type1GlyphSource _src;
        private readonly double[] _stack = new double[24];
        private readonly double[] _psStack = new double[16];
        private int _sp;
        private int _psSp;

        private readonly List<List<ContourPoint>> _contours = new();
        private List<ContourPoint>? _current;
        private double _x, _y;
        private double _xMin = double.MaxValue, _yMin = double.MaxValue;
        private double _xMax = double.MinValue, _yMax = double.MinValue;
        private double _wx;

        /// <summary>The advance width the charstring declared through hsbw/sbw,
        /// in the font's own units.</summary>
        public double Width => _wx;
        private double _sbX;

        // Flex state — collected between callothersubr 1 .. callothersubr 0.
        private bool _inFlex;
        private readonly List<(double x, double y)> _flexPts = new();

        private int _depth;

        public Type1Interpreter(Type1GlyphSource src) { _src = src; }

        public GlyphOutline? Run(byte[] cs)
        {
            Interpret(cs);
            FlushCurrent();
            if (_contours.Count == 0)
            {
                // .notdef and empty glyphs are legitimate — return a zero-area outline
                // so the renderer still advances the pen if needed via metrics.
                if (_xMin == double.MaxValue) { _xMin = _yMin = _xMax = _yMax = 0; }
                return null;
            }
            var arr = new ContourPoint[_contours.Count][];
            for (var i = 0; i < _contours.Count; i++) arr[i] = _contours[i].ToArray();
            return new GlyphOutline(arr, _xMin, _yMin, _xMax, _yMax);
        }

        private void Interpret(byte[] bytes)
        {
            if (_depth > 10) return;
            _depth++;
            var pos = 0;
            while (pos < bytes.Length)
            {
                var b = bytes[pos];
                if (b >= 32 && b <= 246) { Push(b - 139); pos++; continue; }
                if (b >= 247 && b <= 250)
                {
                    if (pos + 1 >= bytes.Length) break;
                    Push((b - 247) * 256 + bytes[pos + 1] + 108);
                    pos += 2; continue;
                }
                if (b >= 251 && b <= 254)
                {
                    if (pos + 1 >= bytes.Length) break;
                    Push(-(b - 251) * 256 - bytes[pos + 1] - 108);
                    pos += 2; continue;
                }
                if (b == 255)
                {
                    if (pos + 4 >= bytes.Length) break;
                    var v = (bytes[pos + 1] << 24) | (bytes[pos + 2] << 16)
                          | (bytes[pos + 3] << 8) | bytes[pos + 4];
                    Push(v);
                    pos += 5; continue;
                }
                // Operators.
                if (b == 12)
                {
                    if (pos + 1 >= bytes.Length) break;
                    var b2 = bytes[pos + 1];
                    pos += 2;
                    if (!ExecuteEscape(b2)) { _depth--; return; }
                    continue;
                }
                pos++;
                if (!Execute(b)) { _depth--; return; }
            }
            _depth--;
        }

        private bool Execute(byte op)
        {
            switch (op)
            {
                case 13: // hsbw  sbx wx
                    if (_sp >= 2) { _sbX = _stack[0]; _wx = _stack[1]; _x = _sbX; _y = 0; }
                    _sp = 0; return true;
                case 14: // endchar
                    return false;
                case 21: // rmoveto  dx dy
                    if (_inFlex) FlexCollect(_stack[0], _stack[1]);
                    else { ClosePathStart(); _x += _stack[0]; _y += _stack[1]; MoveTo(); }
                    _sp = 0; return true;
                case 22: // hmoveto  dx
                    if (_inFlex) FlexCollect(_stack[0], 0);
                    else { ClosePathStart(); _x += _stack[0]; MoveTo(); }
                    _sp = 0; return true;
                case 4: // vmoveto  dy
                    if (_inFlex) FlexCollect(0, _stack[0]);
                    else { ClosePathStart(); _y += _stack[0]; MoveTo(); }
                    _sp = 0; return true;
                case 5: // rlineto  dx dy
                    _x += _stack[0]; _y += _stack[1]; LineTo();
                    _sp = 0; return true;
                case 6: // hlineto  dx
                    _x += _stack[0]; LineTo();
                    _sp = 0; return true;
                case 7: // vlineto  dy
                    _y += _stack[0]; LineTo();
                    _sp = 0; return true;
                case 8: // rrcurveto  dx1 dy1 dx2 dy2 dx3 dy3
                    CurveTo(
                        _x + _stack[0], _y + _stack[1],
                        _x + _stack[0] + _stack[2], _y + _stack[1] + _stack[3],
                        _x + _stack[0] + _stack[2] + _stack[4], _y + _stack[1] + _stack[3] + _stack[5]);
                    _x = _x + _stack[0] + _stack[2] + _stack[4];
                    _y = _y + _stack[1] + _stack[3] + _stack[5];
                    _sp = 0; return true;
                case 30: // vhcurveto  dy1 dx2 dy2 dx3
                    CurveTo(
                        _x, _y + _stack[0],
                        _x + _stack[1], _y + _stack[0] + _stack[2],
                        _x + _stack[1] + _stack[3], _y + _stack[0] + _stack[2]);
                    _x = _x + _stack[1] + _stack[3];
                    _y = _y + _stack[0] + _stack[2];
                    _sp = 0; return true;
                case 31: // hvcurveto  dx1 dx2 dy2 dy3
                    CurveTo(
                        _x + _stack[0], _y,
                        _x + _stack[0] + _stack[1], _y + _stack[2],
                        _x + _stack[0] + _stack[1], _y + _stack[2] + _stack[3]);
                    _x = _x + _stack[0] + _stack[1];
                    _y = _y + _stack[2] + _stack[3];
                    _sp = 0; return true;
                case 9: // closepath
                    ClosePath();
                    _sp = 0; return true;
                case 10: // callsubr  idx
                    if (_sp >= 1)
                    {
                        var idx = (int)_stack[_sp - 1];
                        _sp--;
                        var sub = _src.GetSubr(idx);
                        if (sub is not null) Interpret(sub);
                    }
                    return true;
                case 11: // return
                    return true;
                case 1: // hstem
                case 3: // vstem
                    _sp = 0; return true;
            }
            // Unknown operator — clear stack & continue rather than abort.
            _sp = 0; return true;
        }

        private bool ExecuteEscape(byte op)
        {
            switch (op)
            {
                case 0: // dotsection
                    _sp = 0; return true;
                case 1: // vstem3
                case 2: // hstem3
                    _sp = 0; return true;
                case 6: // seac  asb adx ady bchar achar
                    if (_sp >= 5) ExecuteSeac(_stack[0], _stack[1], _stack[2], (int)_stack[3], (int)_stack[4]);
                    _sp = 0;
                    return false; // seac implies endchar
                case 7: // sbw  sbx sby wx wy
                    if (_sp >= 4) { _sbX = _stack[0]; _wx = _stack[2]; _x = _sbX; _y = _stack[1]; }
                    _sp = 0; return true;
                case 12: // div
                    if (_sp >= 2)
                    {
                        var divisor = _stack[_sp - 1];
                        var divid = _stack[_sp - 2];
                        _sp -= 2;
                        Push(divisor != 0 ? divid / divisor : 0);
                    }
                    return true;
                case 16: // callothersubr  arg1..argN N othersubr#
                    return ExecuteCallOtherSubr();
                case 17: // pop
                    if (_psSp > 0) Push(_psStack[--_psSp]);
                    return true;
                case 33: // setcurrentpoint  x y
                    if (_sp >= 2) { _x = _stack[0]; _y = _stack[1]; }
                    _sp = 0; return true;
                case 35: // flex 7-args alternative (Type 1 §6.1, only seen via OtherSubrs in practice)
                    _sp = 0; return true;
            }
            _sp = 0; return true;
        }

        private bool ExecuteCallOtherSubr()
        {
            if (_sp < 2) { _sp = 0; return true; }
            var subrNum = (int)_stack[_sp - 1];
            var n = (int)_stack[_sp - 2];
            _sp -= 2;
            // Arguments below (top-of-stack now is argN, ..., arg1 at depth n-1).
            // Type 1 OtherSubrs (TN #5040 §5.2):
            //   0 — end flex: pops 3 args (flex height, x, y) → real endpoint via setcurrentpoint
            //   1 — start flex: no args
            //   2 — flex midpoint marker
            //   3 — hint replacement; pops 1 (subr #) and pushes it for callothersubr-pop dance
            switch (subrNum)
            {
                case 1:
                    _inFlex = true; _flexPts.Clear();
                    _flexPts.Add((_x, _y));
                    _sp = 0; return true;
                case 2:
                    // Flex midpoint — handled implicitly via accumulated rmoveto in flex mode.
                    _sp = 0; return true;
                case 0:
                    // End flex per TN #5040 §5.2. The flex sequence emitted seven
                    // rmovetos between OtherSubr 1 and OtherSubr 0, so _flexPts
                    // holds:
                    //   [0] start (pen pos when OtherSubr 1 fired)
                    //   [1] reference point (typically equals start)
                    //   [2,3] control points of the first cubic Bézier
                    //   [4] midpoint (where OtherSubr 2 was called)
                    //   [5,6] control points of the second cubic Bézier
                    //   [7] endpoint
                    // Emit the two Béziers; on a malformed sequence fall back to
                    // a line so a partial flex still renders something.
                    if (_flexPts.Count == 8)
                    {
                        var start = _flexPts[1]; // honour reference rmoveto in case it shifted
                        _x = start.x; _y = start.y;
                        if (_current is null || _current.Count == 0)
                            MoveTo();
                        CurveTo(_flexPts[2].x, _flexPts[2].y,
                                _flexPts[3].x, _flexPts[3].y,
                                _flexPts[4].x, _flexPts[4].y);
                        _x = _flexPts[4].x; _y = _flexPts[4].y;
                        CurveTo(_flexPts[5].x, _flexPts[5].y,
                                _flexPts[6].x, _flexPts[6].y,
                                _flexPts[7].x, _flexPts[7].y);
                        _x = _flexPts[7].x; _y = _flexPts[7].y;
                    }
                    else if (_flexPts.Count > 0)
                    {
                        var last = _flexPts[_flexPts.Count - 1];
                        _x = last.x; _y = last.y;
                        LineTo();
                    }
                    _inFlex = false; _flexPts.Clear();
                    // The args (height, x, y) are normally consumed by the
                    // following two `pop`s — emulate by pushing zeros so the
                    // pops don't underflow.
                    while (n-- > 0 && _sp > 0) _sp--;
                    _psStack[_psSp++ % _psStack.Length] = 0;
                    _psStack[_psSp++ % _psStack.Length] = 0;
                    _psStack[_psSp++ % _psStack.Length] = 0;
                    return true;
                case 3:
                    // Hint replacement — push the subr-id arg back so the
                    // CharString's "pop callsubr" sequence works.
                    if (_sp > 0)
                    {
                        var arg = _stack[_sp - 1];
                        _sp--;
                        _psStack[_psSp++ % _psStack.Length] = arg;
                    }
                    return true;
            }
            // Unknown OtherSubr — drop args and push zero so following pops succeed.
            while (n-- > 0 && _sp > 0) _sp--;
            _psStack[_psSp++ % _psStack.Length] = 0;
            return true;
        }

        private void FlexCollect(double dx, double dy)
        {
            _x += dx; _y += dy;
            _flexPts.Add((_x, _y));
        }

        private void ExecuteSeac(double asb, double adx, double ady, int bChar, int aChar)
        {
            var bName = Type1StandardEncoding.GetName(bChar);
            var aName = Type1StandardEncoding.GetName(aChar);
            // Draw base glyph at current origin.
            if (bName is not null && _src.GetCharStringByName(bName) is { } baseCs)
            {
                var saveX = _x; var saveY = _y;
                Interpret(baseCs);
                _x = saveX; _y = saveY;
            }
            // Draw accent translated by (adx - asb, ady).
            if (aName is not null && _src.GetCharStringByName(aName) is { } accentCs)
            {
                var offsetX = adx - asb;
                var offsetY = ady;
                var sub = new SeacInterpreter(_src, this, offsetX, offsetY);
                sub.Interpret(accentCs);
            }
        }

        // ── Path build-up helpers ────────────────────────────────────────

        private void ClosePathStart()
        {
            if (_current is { Count: > 0 } && _contours.IndexOf(_current) < 0)
                _contours.Add(_current);
            _current = null;
        }

        private void MoveTo()
        {
            _current = new List<ContourPoint>();
            _contours.Add(_current);
            _current.Add(new ContourPoint(_x, _y, true));
            Bound(_x, _y);
        }

        private void LineTo()
        {
            _current ??= NewContour();
            _current.Add(new ContourPoint(_x, _y, true));
            Bound(_x, _y);
        }

        private void CurveTo(double cx1, double cy1, double cx2, double cy2, double x3, double y3)
        {
            _current ??= NewContour();
            // Flatten to 8 line segments — typical text sizes don't justify more.
            const int steps = 8;
            var sx = _x; var sy = _y;
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (double)steps;
                var omt = 1 - t;
                var bx = omt * omt * omt * sx + 3 * omt * omt * t * cx1 + 3 * omt * t * t * cx2 + t * t * t * x3;
                var by = omt * omt * omt * sy + 3 * omt * omt * t * cy1 + 3 * omt * t * t * cy2 + t * t * t * y3;
                _current.Add(new ContourPoint(bx, by, true));
                Bound(bx, by);
            }
        }

        private void ClosePath()
        {
            // Type 1 closepath ends the current sub-path but does NOT change the pen.
            if (_current is { Count: > 0 })
            {
                _contours.Add(_current);
                _current = null;
            }
        }

        private void FlushCurrent()
        {
            if (_current is { Count: > 0 } && !_contours.Contains(_current))
                _contours.Add(_current);
            _current = null;
        }

        private List<ContourPoint> NewContour()
        {
            var c = new List<ContourPoint>();
            _contours.Add(c);
            c.Add(new ContourPoint(_x, _y, true));
            Bound(_x, _y);
            return c;
        }

        private void Push(double v)
        {
            if (_sp < _stack.Length) _stack[_sp++] = v;
        }

        private void Bound(double x, double y)
        {
            if (x < _xMin) _xMin = x; if (y < _yMin) _yMin = y;
            if (x > _xMax) _xMax = x; if (y > _yMax) _yMax = y;
        }

        /// <summary>Glue interpreter for seac's accent component — shares the
        /// parent's contour list and bbox; applies a fixed translation.</summary>
        private sealed class SeacInterpreter
        {
            private readonly Type1GlyphSource _src;
            private readonly Type1Interpreter _parent;
            private readonly double _ox, _oy;
            public SeacInterpreter(Type1GlyphSource src, Type1Interpreter parent, double ox, double oy)
            { _src = src; _parent = parent; _ox = ox; _oy = oy; }
            public void Interpret(byte[] cs)
            {
                var save = (_parent._x, _parent._y);
                _parent._x += _ox; _parent._y += _oy;
                _parent.Interpret(cs);
                _parent._x = save._x; _parent._y = save._y;
            }
        }
    }
}

/// <summary>Type 1 StandardEncoding (Adobe TN #5040 Appendix E) — byte → glyph
/// name. Used when the font's /Encoding line reads <c>/Encoding StandardEncoding
/// def</c> instead of an explicit <c>dup CODE /NAME put</c> table.</summary>
internal static class Type1StandardEncoding
{
    public static string? GetName(int code)
    {
        if (code < 0 || code >= 256) return null;
        return _names[code];
    }

    private static readonly string?[] _names = BuildTable();

    private static string?[] BuildTable()
    {
        var t = new string?[256];
        void S(int c, string n) => t[c] = n;
        S(0x20, "space"); S(0x21, "exclam"); S(0x22, "quotedbl"); S(0x23, "numbersign");
        S(0x24, "dollar"); S(0x25, "percent"); S(0x26, "ampersand"); S(0x27, "quoteright");
        S(0x28, "parenleft"); S(0x29, "parenright"); S(0x2A, "asterisk"); S(0x2B, "plus");
        S(0x2C, "comma"); S(0x2D, "hyphen"); S(0x2E, "period"); S(0x2F, "slash");
        S(0x30, "zero"); S(0x31, "one"); S(0x32, "two"); S(0x33, "three");
        S(0x34, "four"); S(0x35, "five"); S(0x36, "six"); S(0x37, "seven");
        S(0x38, "eight"); S(0x39, "nine"); S(0x3A, "colon"); S(0x3B, "semicolon");
        S(0x3C, "less"); S(0x3D, "equal"); S(0x3E, "greater"); S(0x3F, "question");
        S(0x40, "at"); S(0x41, "A"); S(0x42, "B"); S(0x43, "C"); S(0x44, "D");
        S(0x45, "E"); S(0x46, "F"); S(0x47, "G"); S(0x48, "H"); S(0x49, "I");
        S(0x4A, "J"); S(0x4B, "K"); S(0x4C, "L"); S(0x4D, "M"); S(0x4E, "N");
        S(0x4F, "O"); S(0x50, "P"); S(0x51, "Q"); S(0x52, "R"); S(0x53, "S");
        S(0x54, "T"); S(0x55, "U"); S(0x56, "V"); S(0x57, "W"); S(0x58, "X");
        S(0x59, "Y"); S(0x5A, "Z"); S(0x5B, "bracketleft"); S(0x5C, "backslash");
        S(0x5D, "bracketright"); S(0x5E, "asciicircum"); S(0x5F, "underscore");
        S(0x60, "quoteleft");
        S(0x61, "a"); S(0x62, "b"); S(0x63, "c"); S(0x64, "d"); S(0x65, "e");
        S(0x66, "f"); S(0x67, "g"); S(0x68, "h"); S(0x69, "i"); S(0x6A, "j");
        S(0x6B, "k"); S(0x6C, "l"); S(0x6D, "m"); S(0x6E, "n"); S(0x6F, "o");
        S(0x70, "p"); S(0x71, "q"); S(0x72, "r"); S(0x73, "s"); S(0x74, "t");
        S(0x75, "u"); S(0x76, "v"); S(0x77, "w"); S(0x78, "x"); S(0x79, "y");
        S(0x7A, "z"); S(0x7B, "braceleft"); S(0x7C, "bar"); S(0x7D, "braceright"); S(0x7E, "asciitilde");
        S(0xA1, "exclamdown"); S(0xA2, "cent"); S(0xA3, "sterling"); S(0xA4, "fraction");
        S(0xA5, "yen"); S(0xA6, "florin"); S(0xA7, "section"); S(0xA8, "currency");
        S(0xA9, "quotesingle"); S(0xAA, "quotedblleft"); S(0xAB, "guillemotleft");
        S(0xAC, "guilsinglleft"); S(0xAD, "guilsinglright"); S(0xAE, "fi"); S(0xAF, "fl");
        S(0xB1, "endash"); S(0xB2, "dagger"); S(0xB3, "daggerdbl"); S(0xB4, "periodcentered");
        S(0xB6, "paragraph"); S(0xB7, "bullet"); S(0xB8, "quotesinglbase");
        S(0xB9, "quotedblbase"); S(0xBA, "quotedblright"); S(0xBB, "guillemotright");
        S(0xBC, "ellipsis"); S(0xBD, "perthousand"); S(0xBF, "questiondown");
        S(0xC1, "grave"); S(0xC2, "acute"); S(0xC3, "circumflex"); S(0xC4, "tilde");
        S(0xC5, "macron"); S(0xC6, "breve"); S(0xC7, "dotaccent"); S(0xC8, "dieresis");
        S(0xCA, "ring"); S(0xCB, "cedilla"); S(0xCD, "hungarumlaut"); S(0xCE, "ogonek");
        S(0xCF, "caron"); S(0xE1, "AE"); S(0xE3, "ordfeminine"); S(0xE8, "Lslash");
        S(0xE9, "Oslash"); S(0xEA, "OE"); S(0xEB, "ordmasculine"); S(0xF1, "ae");
        S(0xF5, "dotlessi"); S(0xF8, "lslash"); S(0xF9, "oslash"); S(0xFA, "oe");
        S(0xFB, "germandbls");
        return t;
    }
}
