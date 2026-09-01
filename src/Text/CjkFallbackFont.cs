namespace Aspose.Pdf.Text;

/// <summary>
/// On-demand fallback glyph source for CJK text rendered via non-embedded
/// predefined CID fonts (HYGoThic-Medium, STSong-Light, KozMinPro, etc.).
///
/// PDF 32000-1:2008 §9.6.6 allows a CIDFont to omit its /FontFile* entirely
/// — the reader is expected to pull a system font that matches the
/// /Ordering of its /CIDSystemInfo. Without embedded outlines our renderer
/// would draw nothing where CJK text should be. This provider plugs that
/// gap by loading an installed broad-coverage TrueType (Arial Unicode on
/// macOS) once per process and exposing the <see cref="IGlyphOutlineSource"/>
/// contract that the render pipeline already understands.
///
/// Widths and per-glyph metrics are the fallback font's, not the original
/// PDF font's — character advances can drift slightly. Acceptable for
/// close visual rendering; precise-typography workflows would need the
/// actual Adobe reference fonts.
/// </summary>
internal static class CjkFallbackFont
{
    private static readonly string[] CandidatePaths =
    [
        // macOS — "Arial Unicode" has the broadest single-file CJK coverage
        // and ships with the OS (Supplemental/). Keep other broad fonts as
        // backups in case it's been removed.
        "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
        "/Library/Fonts/Arial Unicode.ttf",
        // Windows — Arial Unicode MS used to ship until Office 2007; modern
        // installs only have SimSun (zh) / Yu Gothic (ja) / Malgun Gothic
        // (ko). Try the one that loads.
        @"C:\Windows\Fonts\ARIALUNI.TTF",
        @"C:\Windows\Fonts\simsun.ttc",
        @"C:\Windows\Fonts\malgun.ttf",
        @"C:\Windows\Fonts\YuGothR.ttc",
        // Linux — Noto Sans CJK is the de-facto standard on most distros.
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
        "/usr/share/fonts/truetype/noto/NotoSansCJK-Regular.ttc",
    ];

    private static IGlyphOutlineSource? _parser;
    private static bool _lookupAttempted;
    private static readonly object _lock = new();

    // Cache of named system CJK fonts (keyed by file path), so a SimHei title and a
    // SimSun body each load the right face once.
    private static readonly System.Collections.Generic.Dictionary<string, IGlyphOutlineSource?> _named =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve a system/registered CJK font matching a non-embedded font's /BaseFont
    /// (serif vs sans, weight): e.g. MS-PMincho → MS Mincho, SimHei/黑体 → a heavy
    /// sans, SimSun/宋体 → SimSun. Registered <see cref="FontRepository"/> sources are
    /// tried first — that's how callers supply fonts like MSMINCHO.TTF
    /// that aren't installed on the host — then the installed
    /// system fonts, then the generic broad-coverage fallback (<see cref="TryGet"/>).
    /// </summary>
    public static IGlyphOutlineSource? ResolveNamed(string? baseFont, string? ordering = null)
    {
        var (repoNames, sysFiles) = CjkCandidates(baseFont, ordering);

        foreach (var rn in repoNames)
        {
            var p = LoadCached("repo:" + rn, () =>
            {
                foreach (var source in FontRepository.Sources)
                {
                    var data = source.FindFont(rn, true)?.TtfData;
                    if (data is { Length: > 12 }) return LoadOutlineSource(data);
                }
                return null;
            });
            if (p is not null) return p;
        }
        foreach (var f in sysFiles)
        {
            var p = LoadCached(f, () =>
                LocateFontFile(f) is { } fp ? LoadOutlineSource(System.IO.File.ReadAllBytes(fp)) : null);
            if (p is not null) return p;
        }
        return TryGet();
    }

    /// <summary>
    /// Advance width (1/1000 em) for one legacy-CMap byte-code, taken from the
    /// resolved substitute font's own hmtx — proportional for MS-PMincho, full/half
    /// for SimSun. Both <see cref="ContentStreamParser"/> (cursor between show-strings)
    /// and the renderers (glyph placement within a string) call this so they advance
    /// in lockstep. Vertical text is one em per glyph; unmapped codes fall back to
    /// nominal full-/half-width by code length.
    /// </summary>
    public static int AdvanceEm(CidFontInfo cid, FontMetrics? metrics, int code, int step)
    {
        if (cid.IsVertical) return 1000;
        var uni = cid.LegacyToUnicode(code);
        if (uni is null) return step == 2 ? 1000 : 500;

        // Proportional widths the PDF authored live in the CID-keyed /W table. We
        // decoded straight to Unicode, so map back to the real Adobe CID and use /W
        // when it lists this glyph (e.g. MS-PMincho's narrow Latin/kana). Only an
        // EXPLICIT /W entry is used — full-width glyphs left to /DW fall through to
        // the substitute font, so fixed-width fonts (SimSun) are unaffected.
        if (metrics is not null && cid.Ordering is not null && cid.Ordering != "Identity"
            && AdobeCidTables.UnicodeToCid(cid.Ordering, uni.Value) is int realCid
            && metrics.ExplicitCidWidth(realCid) is int ew && ew > 0)
            return ew;

        var fb = ResolveNamed(cid.CjkBaseFont, cid.Ordering);
        if (fb is not null && fb.CMap.TryGetValue(uni.Value, out var gid) && gid > 0)
        {
            var upm = fb.UnitsPerEm > 0 ? fb.UnitsPerEm : 1000;
            var adv = fb.GetAdvanceWidth(gid);
            if (adv > 0) return adv * 1000 / upm;
        }
        return step == 2 ? 1000 : 500;
    }

    private static IGlyphOutlineSource? LoadCached(string key, System.Func<IGlyphOutlineSource?> load)
    {
        lock (_lock)
        {
            if (_named.TryGetValue(key, out var cached)) return cached;
            IGlyphOutlineSource? parser = null;
            try { parser = load(); } catch { /* unreadable / unparseable */ }
            _named[key] = parser;
            return parser;
        }
    }

    /// <summary>
    /// Map a /BaseFont name to candidate CJK faces: names to look up via
    /// <see cref="FontRepository"/> (registered/test fonts) and installed-system
    /// file paths. The name is often the GBK bytes of a Chinese family
    /// ("ºÚÌå" = 黑体); decode it so we can match the family in either form.
    /// </summary>
    private static (string[] repoNames, string[] sysFiles) CjkCandidates(string? baseFont, string? ordering = null)
    {
        var empty = System.Array.Empty<string>();
        if (string.IsNullOrEmpty(baseFont)) return (empty, empty);
        var name = baseFont;
        var plus = name.IndexOf('+');
        if (plus is >= 0 and <= 6) name = name[(plus + 1)..];

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length;)
        {
            int lead = name[i];
            if (lead is >= 0x81 and <= 0xFE && i + 1 < name.Length
                && GbkTable.ToUnicode((lead << 8) | (name[i + 1] & 0xFF)) is int u)
            { sb.Append((char)u); i += 2; }
            else { sb.Append(name[i]); i++; }
        }
        var hay = name + " " + sb;
        bool Has(params string[] keys)
        {
            foreach (var k in keys)
                if (hay.Contains(k, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        const string F = @"C:\Windows\Fonts\";
        // Korean (Adobe-Korea1). Checked first because Korean family names such as
        // "HYGoThic" contain the substring "Gothic" and would otherwise be routed to
        // the Japanese MS Gothic branch, which has no Hangul. Match on the Korea1
        // ordering or on Korean-specific family names. Malgun Gothic ships with
        // modern Windows and covers all Hangul; Batang/Gulim are classic fallbacks.
        if (ordering == "Korea1"
            || Has("HYGoThic", "HYGothic", "HYSMyeong", "HYMyeong", "Batang", "바탕",
                   "Dotum", "돋움", "Gulim", "굴림", "Gungsuh", "궁서", "명조", "고딕", "Malgun", "맑은"))
            return (new[] { "Malgun Gothic", "Batang", "Gulim" },
                    new[] { F + "malgun.ttf", F + "batang.ttc", F + "gulim.ttc" });
        // Japanese Mincho (serif). MS Mincho isn't installed on modern Windows; callers
        // typically register MSMINCHO.TTF, so try the repository first.
        if (Has("Mincho", "明朝", "Ryumin"))      // 明朝
            return (new[] { "MSMINCHO", "MS Mincho", "MS-Mincho" },
                    new[] { F + "msmincho.ttc", F + "yumin.ttf", F + "msgothic.ttc" });
        // Japanese Gothic (sans) — MS Gothic ships with Windows.
        if (Has("Gothic", "ゴシック"))    // ゴシック
            return (new[] { "MSGOTHIC", "MS Gothic", "MS-Gothic" },
                    new[] { F + "msgothic.ttc", F + "YuGothR.ttc", F + "meiryo.ttc" });
        // Chinese heavy sans (SimHei / 黑体) — no simhei.ttf on modern Windows.
        if (Has("黑体", "SimHei", "STHeiti", "Heiti"))
            return (new[] { "SimHei" }, new[] { F + "simhei.ttf", F + "msyhbd.ttc", F + "msyh.ttc" });
        if (Has("雅黑", "YaHei"))
            return (new[] { "Microsoft YaHei" }, new[] { F + "msyh.ttc" });
        if (Has("楷", "KaiTi"))                        // 楷
            return (new[] { "KaiTi", "SimKai" }, new[] { F + "simkai.ttf", F + "simsun.ttc" });
        if (Has("仿宋", "FangSong"))               // 仿宋
            return (new[] { "FangSong", "SimFang" }, new[] { F + "simfang.ttf", F + "simsun.ttc" });
        if (Has("宋", "SimSun", "NSimSun", "STSong", "Song"))
            return (new[] { "SimSun", "NSimSun" }, new[] { F + "simsun.ttc" });
        return (empty, empty);
    }

    /// <summary>
    /// Lazily locate a system CJK-capable TrueType font and return its
    /// <see cref="GlyphOutlineParser"/>. Returns null when no candidate
    /// exists on the host — callers should fall through to the existing
    /// no-outline behaviour (advance cursor, draw nothing).
    /// </summary>
    /// <summary>
    /// Build an outline source from a font FILE's bytes: reduce a collection to its first
    /// face, then pick the parser that matches the outline format. A CFF-flavoured
    /// OpenType face ('OTTO') carries no glyf table, so handing it to
    /// <see cref="GlyphOutlineParser"/> yields a source that resolves every glyph to
    /// nothing and renders the text BLANK rather than failing - which is what the standard
    /// Linux fallback, Noto Sans CJK, did. Returns null when neither parser can read it,
    /// so the caller moves to the next candidate.
    /// </summary>
    private static IGlyphOutlineSource? LoadOutlineSource(byte[] data)
    {
        if (data is null || data.Length < 4) return null;
        var sfnt = NormalizeToSfnt(data);
        if (sfnt.Length >= 4 && sfnt[0] == (byte)'O' && sfnt[1] == (byte)'T'
            && sfnt[2] == (byte)'T' && sfnt[3] == (byte)'O')
            return CffGlyphSource.TryLoad(sfnt);
        return new GlyphOutlineParser(sfnt);
    }

    public static IGlyphOutlineSource? TryGet()
    {
        if (_parser is not null) return _parser;
        if (_lookupAttempted) return null;
        lock (_lock)
        {
            if (_parser is not null) return _parser;
            if (_lookupAttempted) return null;
            _lookupAttempted = true;
            foreach (var path in CandidatePaths)
            {
                if (LocateFontFile(path) is not { } located) continue;
                try
                {
                    var bytes = System.IO.File.ReadAllBytes(located);
                    _parser = LoadOutlineSource(bytes);
                    if (_parser is null) continue;
                    return _parser;
                }
                catch
                {
                    // Bad font file / unreadable / .ttc without a TTF wrapper — skip
                    // silently; the next candidate might work.
                }
            }
            return null;
        }
    }

    /// <summary>Resolve a CID in the given Adobe ordering to a GID in the
    /// loaded fallback font via CID → Unicode → fallback cmap. Returns 0
    /// when anything in the chain fails, which the caller treats as "draw
    /// nothing for this CID".</summary>
    public static int ResolveFallbackGid(string? ordering, int cid)
        => ResolveFallbackGid(ordering, cid, TryGet());

    /// <summary>
    /// Glyph id for an Adobe CID in a SPECIFIC face: CID to Unicode through the
    /// collection's table, then that face's own cmap. Callers that resolved a NAMED face
    /// (see <see cref="ResolveNamed"/>) must pass it here. The overload above consults the
    /// generic broad-coverage font, which is only the right cmap when the caller also
    /// DRAWS from that font - pairing one font's glyph ids with another font's outlines
    /// picks real but unrelated glyphs, which is exactly how a page of Chinese comes out
    /// fluent and wrong.
    /// </summary>
    public static int ResolveFallbackGid(string? ordering, int cid, IGlyphOutlineSource? face)
    {
        if (string.IsNullOrEmpty(ordering) || face is null) return 0;
        var unicode = AdobeCidTables.LookupCid(ordering, cid);
        if (unicode is null) return 0;
        face.CMap.TryGetValue(unicode.Value, out var gid);
        return gid;
    }

    // ── Embeddable-bytes resolution (for GENERATING content that names a CJK font not
    //    installed, e.g. "Arial Unicode MS"). Returns a standalone single-font sfnt that
    //    covers the text's script, ready to embed as a CIDFontType2 FontFile2.

    private static readonly System.Collections.Generic.Dictionary<string, byte[]?> _embeddable =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raw, embeddable single-font sfnt bytes of an installed system font that covers the
    /// CJK <paramref name="text"/> — picked by script (kana→Japanese, hangul→Korean, else
    /// Han→Chinese). If the system face is a TrueType Collection (.ttc) its first font is
    /// extracted into a standalone sfnt so it embeds as a valid FontFile2. Returns null
    /// when no covering system font is found. Cached per file path.
    /// </summary>
    /// <summary>
    /// A fallback candidate's real location on THIS machine. The chains below name their
    /// faces by the path the Windows platform stores them at - a literal
    /// C:\\Windows\\Fonts entry - which resolves to nothing anywhere else, so the run those
    /// candidates were meant to carry was dropped from the document even on a box where the
    /// same face is installed where every other tool can see it. When the literal path
    /// misses, look the FILE NAME up in the platform's own font directories.
    /// </summary>
    private static string? LocateFontFile(string candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return null;
        if (System.IO.File.Exists(candidate)) return candidate;
        // Split on BOTH separators by hand: off Windows a backslash is an ordinary
        // filename character, so Path.GetFileName would hand back the whole path.
        var cut = candidate.LastIndexOfAny(new[] { '/', '\\' });
        var name = cut >= 0 ? candidate.Substring(cut + 1) : candidate;
        return name.Length == 0 ? null : SystemFontResolver.FindFontFile(name);
    }

    public static byte[]? ResolveEmbeddableBytes(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        lock (_lock)
        {
            foreach (var f in EmbeddableCandidates(text))
            {
                if (_embeddable.TryGetValue(f, out var cached))
                {
                    if (cached is not null) return cached;
                    continue;
                }
                byte[]? bytes = null;
                try
                {
                    if (LocateFontFile(f) is { } fp)
                        bytes = NormalizeToSfnt(System.IO.File.ReadAllBytes(fp));
                }
                catch { bytes = null; }
                _embeddable[f] = bytes;
                if (bytes is not null) return bytes;
            }
        }
        return null;
    }

    // ── Coverage-chain fallback (mixed-script cell text) ────────────────────
    // Substitution runs PER CODEPOINT, not per run: SimSun carries the
    // han and radical glyphs, Segoe UI Symbol the technical symbols, and
    // SimSun-ExtB the plane-2 ideographs — one drawn line can mix all three.
    private static List<(byte[] Bytes, string Name)>? _chain;

    /// <summary>The UA's serif Latin face (Times New Roman), normalised for
    /// embedding: a CJK cell's ASCII runs — digits, parens, SPACES — are drawn
    /// in the UA serif, not in the CJK face's full/half-width forms
    /// (a SimSun space is 0.5 em where the drawn Times space is 0.25).</summary>
    public static (byte[] Bytes, string Name)? SerifLatinFace(bool bold = false)
    {
        lock (_lock)
        {
            var file = bold ? @"C:\Windows\Fonts\timesbd.ttf" : @"C:\Windows\Fonts\times.ttf";
            var name = bold ? "TimesNewRomanBold" : "TimesNewRoman";
            _latinBytes ??= new System.Collections.Generic.Dictionary<string, byte[]?>(
                System.StringComparer.OrdinalIgnoreCase);
            if (_latinBytes.TryGetValue(file, out var cached))
                return cached is null ? null : (cached, name);
            byte[]? bytes = null;
            try
            {
                if (LocateFontFile(file) is { } fp)
                    bytes = NormalizeToSfnt(System.IO.File.ReadAllBytes(fp));
            }
            catch { bytes = null; }
            _latinBytes[file] = bytes;
            return bytes is null ? null : (bytes, name);
        }
    }

    private static System.Collections.Generic.Dictionary<string, byte[]?>? _latinBytes;

    /// <summary>Embeddable faces, in substitution priority order, for text the
    /// single script-picked face cannot fully cover. Only faces present on the
    /// host are returned; each is normalised to a standalone sfnt.</summary>
    public static System.Collections.Generic.IReadOnlyList<(byte[] Bytes, string Name)> ChainFaces()
    {
        lock (_lock)
        {
            if (_chain is not null) return _chain;
            _chain = new System.Collections.Generic.List<(byte[], string)>();
            const string F = @"C:\Windows\Fonts\";
            foreach (var (file, name) in new[]
            {
                (F + "simsun.ttc", "SimSun"),
                // YaHei carries the CJK-radical range SimSun's cmap lacks
                // (U+2E86… — 21 of the filing form's 30 address radicals).
                (F + "msyh.ttc", "MicrosoftYaHei"),
                (F + "seguisym.ttf", "SegoeUISymbol"),
                (F + "simsunb.ttf", "SimSun-ExtB"),
                (F + "msgothic.ttc", "MSGothic"),
                (F + "malgun.ttf", "MalgunGothic"),
            })
            {
                try
                {
                    if (LocateFontFile(file) is { } fp)
                        _chain.Add((NormalizeToSfnt(System.IO.File.ReadAllBytes(fp)), name));
                }
                catch { /* unreadable candidate — the next may work */ }
            }
            return _chain;
        }
    }

    private static string[] EmbeddableCandidates(string text)
    {
        const string F = @"C:\Windows\Fonts\";
        bool kana = false, hangul = false, han = false;
        foreach (var c in text)
        {
            if (c >= 0x3040 && c <= 0x30FF) kana = true;
            else if (c >= 0xAC00 && c <= 0xD7AF) hangul = true;
            else if ((c >= 0x3400 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)) han = true;
        }
        if (kana) return new[] { F + "msgothic.ttc", F + "YuGothR.ttc", F + "meiryo.ttc" };
        if (hangul) return new[] { F + "malgun.ttf", F + "gulim.ttc", F + "batang.ttc" };
        if (han) return new[] { F + "simsun.ttc", F + "msyh.ttc", F + "simhei.ttf" };
        return System.Array.Empty<string>();
    }

    /// <summary>Number of fonts in a TrueType Collection ('ttcf'); 1 for a plain sfnt.</summary>
    internal static int TtcFaceCount(byte[] data)
    {
        if (data.Length < 16 || data[0] != (byte)'t' || data[1] != (byte)'t'
            || data[2] != (byte)'c' || data[3] != (byte)'f')
            return 1;
        var n = U32(data, 8);
        if (n < 1) return 1;
        // Each face needs its 4-byte offset entry inside the header.
        var max = (data.Length - 12) / 4;
        return (int)System.Math.Min(n, (uint)System.Math.Max(1, max));
    }

    /// <summary>
    /// If <paramref name="data"/> is a TrueType Collection ('ttcf'), rebuild a standalone
    /// sfnt from its FIRST embedded font (copying that font's tables with corrected offsets)
    /// so it embeds as a single-font FontFile2. A plain sfnt is returned unchanged.
    /// </summary>
    internal static byte[] NormalizeToSfnt(byte[] data) => NormalizeToSfnt(data, 0);

    /// <summary>
    /// Extract face <paramref name="faceIndex"/> of a TrueType Collection as a standalone
    /// sfnt. A plain (non-'ttcf') font is returned unchanged regardless of the index.
    /// </summary>
    internal static byte[] NormalizeToSfnt(byte[] data, int faceIndex)
    {
        if (data.Length < 16 || data[0] != (byte)'t' || data[1] != (byte)'t'
            || data[2] != (byte)'c' || data[3] != (byte)'f')
            return data;
        if (faceIndex < 0 || 12 + (faceIndex + 1) * 4 > data.Length) return data;
        int dirOff = (int)U32(data, 12 + faceIndex * 4);
        if (dirOff < 0 || dirOff + 12 > data.Length) return data;
        int numTables = U16(data, dirOff + 4);
        if (numTables <= 0 || dirOff + 12 + numTables * 16 > data.Length) return data;

        var tag = new byte[numTables][];
        var sum = new uint[numTables];
        var srcOff = new int[numTables];
        var len = new int[numTables];
        for (int i = 0; i < numTables; i++)
        {
            int e = dirOff + 12 + i * 16;
            tag[i] = new byte[4]; System.Array.Copy(data, e, tag[i], 0, 4);
            sum[i] = U32(data, e + 4);
            srcOff[i] = (int)U32(data, e + 8);
            len[i] = (int)U32(data, e + 12);
        }

        int hdr = 12 + numTables * 16;
        long total = hdr;
        for (int i = 0; i < numTables; i++) total += (len[i] + 3) & ~3;
        if (total > int.MaxValue) return data;
        var outb = new byte[total];
        // Offset table: the font's sfnt version (4 bytes at dirOff), then table counts.
        System.Array.Copy(data, dirOff, outb, 0, 4);
        int sr = 16, es = 0; while (sr * 2 <= numTables * 16) { sr *= 2; es++; }
        W16(outb, 4, (ushort)numTables);
        W16(outb, 6, (ushort)sr);
        W16(outb, 8, (ushort)es);
        W16(outb, 10, (ushort)(numTables * 16 - sr));
        int cursor = hdr;
        for (int i = 0; i < numTables; i++)
        {
            int e = 12 + i * 16;
            System.Array.Copy(tag[i], 0, outb, e, 4);
            W32(outb, e + 4, sum[i]);
            W32(outb, e + 8, (uint)cursor);
            W32(outb, e + 12, (uint)len[i]);
            if (srcOff[i] >= 0 && (long)srcOff[i] + len[i] <= data.Length)
                System.Array.Copy(data, srcOff[i], outb, cursor, len[i]);
            cursor += (len[i] + 3) & ~3;
        }
        return outb;
    }

    private static uint U32(byte[] d, int o) =>
        (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
    private static ushort U16(byte[] d, int o) => (ushort)((d[o] << 8) | d[o + 1]);
    private static void W16(byte[] d, int o, ushort v) { d[o] = (byte)(v >> 8); d[o + 1] = (byte)v; }
    private static void W32(byte[] d, int o, uint v)
    { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }
}
