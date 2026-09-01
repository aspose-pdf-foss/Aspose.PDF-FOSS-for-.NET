namespace Aspose.Pdf.Text.OpenType;

/// <summary>
/// Entry point for complex-script shaping: turns a run of text into the glyph run the
/// font's own layout rules call for. Non-Indic text is returned unshaped (one glyph per
/// codepoint), which is what every other script in this library already relied on.
/// </summary>
internal static class TextShaper
{
    // Features applied to a whole syllable, in the order the spec fixes. The masks let a
    // feature reach only the glyphs it is meant to: rphf only the leading ra, half only a
    // non-final consonant, and so on.
    private const uint MaskRphf = 1u << 1;
    private const uint MaskPref = 1u << 2;
    private const uint MaskBlwf = 1u << 3;
    private const uint MaskHalf = 1u << 4;
    private const uint MaskPstf = 1u << 5;
    private const uint MaskCjct = 1u << 6;
    private const uint MaskAll = uint.MaxValue;

    private static readonly (string tag, uint mask)[] SyllableFeatures =
    {
        ("nukt", MaskAll),
        ("akhn", MaskAll),
        ("rphf", MaskRphf),
        ("pref", MaskPref),
        ("blwf", MaskBlwf),
        ("half", MaskHalf),
        ("pstf", MaskPstf),
        ("vatu", MaskAll),
        ("cjct", MaskAll),
    };

    // Applied to the whole run once the syllables are reordered.
    private static readonly string[] PresentationFeatures =
        { "abvs", "blws", "psts", "haln", "calt", "liga" };

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], OtfLayout?> _cache = new();

    /// <summary>
    /// Shape <paramref name="text"/> with <paramref name="ttf"/>. Returns null when the run
    /// needs no shaping or the font carries no rules for it — the caller then keeps its
    /// straight cmap mapping.
    /// </summary>
    internal static ushort[]? Shape(byte[] ttf, string text, Func<int, ushort> glyphOf)
    {
        if (string.IsNullOrEmpty(text) || !IndicShaper.NeedsShaping(text)) return null;
        OtfLayout? layout;
        lock (_cache)
        {
            if (!_cache.TryGetValue(ttf, out layout))
            {
                layout = OtfLayout.Open(ttf);
                _cache.Add(ttf, layout);
            }
        }
        if (layout is null) return null;

        var buf = new List<ShapedGlyph>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                buf.Add(new ShapedGlyph(glyphOf(char.ConvertToUtf32(text[i], text[i + 1])), i)
                { Category = IndicShaper.CatOther });
                i++;
                continue;
            }
            buf.Add(new ShapedGlyph(glyphOf(cp), i) { Category = IndicShaper.Categorise(cp) });
        }

        var gsub = new GsubEngine(layout);
        var hasGsub = layout.Has("GSUB");
        var scripts = hasGsub ? layout.Scripts("GSUB") : new HashSet<string>(StringComparer.Ordinal);

        // 1. Segment into syllables and give every glyph its position and feature mask.
        //    Done for the WHOLE run first: substitution can merge glyphs away, and a
        //    syllable is then found again by its id rather than by an index range.
        var syllableScripts = new List<string?>();
        {
            var start = 0;
            while (start < buf.Count)
            {
                if (buf[start].Category == IndicShaper.CatOther) { start++; continue; }
                var end = SyllableEnd(buf, start);
                var id = syllableScripts.Count;
                syllableScripts.Add(ScriptOfCluster(text, buf, start));
                MarkSyllable(buf, start, end, id);
                start = end;
            }
        }

        // 2. Substitute. The masks scope each feature to the glyphs it may touch, so the
        //    lookups can run over the whole buffer.
        foreach (var scriptTag in ScriptsPresent(text))
        {
            if (!scripts.Contains(scriptTag)) continue;
            foreach (var (tag, mask) in SyllableFeatures)
                foreach (var lk in layout.LookupsForFeature("GSUB", scriptTag, tag))
                    gsub.ApplyLookup(buf, lk, mask);
        }

        // 3. Reorder each syllable into visual order.
        for (var i = 0; i < buf.Count;)
        {
            var id = buf[i].Syllable;
            if (id < 0) { i++; continue; }
            var j = i;
            while (j < buf.Count && buf[j].Syllable == id) j++;
            Reorder(buf, i, j);
            i = j;
        }

        // Presentation features run over the whole run, per script present.
        if (hasGsub)
        {
            foreach (var scriptTag in ScriptsPresent(text))
            {
                if (!scripts.Contains(scriptTag)) continue;
                foreach (var feat in PresentationFeatures)
                    foreach (var lk in layout.LookupsForFeature("GSUB", scriptTag, feat))
                        gsub.ApplyLookup(buf, lk, MaskAll);
            }
        }

        var result = new ushort[buf.Count];
        for (var i = 0; i < buf.Count; i++) result[i] = buf[i].Glyph;
        return result;
    }

    private static IEnumerable<string> ScriptsPresent(string text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ch in text)
            if (IndicShaper.ScriptTagOf(ch) is { } tag && seen.Add(tag))
                yield return tag;
    }

    private static string? ScriptOfCluster(string text, List<ShapedGlyph> buf, int at)
    {
        var cluster = buf[at].Cluster;
        if (cluster < 0 || cluster >= text.Length) return null;
        return IndicShaper.ScriptTagOf(text[cluster]);
    }

    /// <summary>One syllable: a run of consonants joined by viramas, its matras, and the
    /// signs that ride on it. Stops at the first character that cannot continue it.</summary>
    private static int SyllableEnd(List<ShapedGlyph> buf, int start)
    {
        var i = start;
        // consonant (+ nukta) { virama consonant (+ nukta) }  |  independent vowel
        if (buf[i].Category is IndicShaper.CatConsonant or IndicShaper.CatRa)
        {
            i++;
            if (i < buf.Count && buf[i].Category == IndicShaper.CatNukta) i++;
            while (i + 1 < buf.Count && buf[i].Category == IndicShaper.CatVirama
                   && buf[i + 1].Category is IndicShaper.CatConsonant or IndicShaper.CatRa)
            {
                i += 2;
                if (i < buf.Count && buf[i].Category == IndicShaper.CatNukta) i++;
                // a ZW(N)J after the cluster steers the join and belongs to the syllable
                if (i < buf.Count && buf[i].Category is IndicShaper.CatZwj or IndicShaper.CatZwnj) i++;
            }
            // a trailing virama (a dead consonant at the end of a word) stays in
            if (i < buf.Count && buf[i].Category == IndicShaper.CatVirama) i++;
        }
        else if (buf[i].Category == IndicShaper.CatVowel)
        {
            i++;
        }
        else
        {
            return start + 1;
        }
        // matras and signs
        while (i < buf.Count && buf[i].Category is IndicShaper.CatMatraPre or IndicShaper.CatMatraAbove
               or IndicShaper.CatMatraBelow or IndicShaper.CatMatraPost or IndicShaper.CatBindu)
            i++;
        return i;
    }

    /// <summary>Give one syllable's glyphs their id, their position within the syllable
    /// and the feature mask that decides which substitutions may reach them.</summary>
    private static void MarkSyllable(List<ShapedGlyph> buf, int start, int end, int id)
    {
        if (end - start <= 0) return;
        for (var i = start; i < end; i++) buf[i].Syllable = id;

        // The BASE is the last consonant not carrying a below/post-base form — for the
        // scripts here, the final consonant of the cluster.
        var baseIndex = -1;
        for (var i = start; i < end; i++)
            if (buf[i].Category is IndicShaper.CatConsonant or IndicShaper.CatRa or IndicShaper.CatVowel)
                baseIndex = i;

        // A leading "ra + virama" becomes a reph — but only when the cluster continues
        // past it (otherwise the ra IS the base).
        var hasReph = false;
        if (end - start >= 3 && buf[start].Category == IndicShaper.CatRa
            && buf[start + 1].Category == IndicShaper.CatVirama
            && baseIndex > start + 1)
        {
            hasReph = true;
            buf[start].Position = IndicShaper.PosRaToBecomeReph;
            buf[start].Mask = MaskRphf | MaskAll;
            buf[start + 1].Mask = MaskRphf | MaskAll;
        }

        for (var i = start; i < end; i++)
        {
            if (hasReph && (i == start || i == start + 1)) continue;
            var g = buf[i];
            g.Position = g.Category switch
            {
                IndicShaper.CatMatraPre => IndicShaper.PosPreBase,
                IndicShaper.CatMatraAbove => IndicShaper.PosAboveBase,
                IndicShaper.CatMatraBelow => IndicShaper.PosBelowBase,
                IndicShaper.CatMatraPost => IndicShaper.PosPostBase,
                IndicShaper.CatBindu => IndicShaper.PosSyllableEnd,
                _ => i <= baseIndex ? IndicShaper.PosBase : IndicShaper.PosAfterBase,
            };
            // A consonant before the base takes a half or below-base form.
            if (g.Category is IndicShaper.CatConsonant or IndicShaper.CatRa && i < baseIndex)
                g.Mask = MaskHalf | MaskBlwf | MaskCjct | MaskPref;
            else if (g.Category is IndicShaper.CatConsonant && i > baseIndex)
                g.Mask = MaskPstf | MaskBlwf | MaskCjct;
        }

    }

    /// <summary>
    /// Put the syllable's glyphs in visual order: the pre-base matra first, then the base
    /// and what hangs off it, and the reph last. A stable sort on the assigned position
    /// keeps everything that shares a position in its original order.
    /// </summary>
    private static void Reorder(List<ShapedGlyph> buf, int start, int end)
    {
        if (end - start <= 1 || end > buf.Count) return;
        var slice = buf.GetRange(start, end - start);
        // The reph rides at the END of the syllable once it has been substituted.
        foreach (var g in slice)
            if (g.Position == IndicShaper.PosRaToBecomeReph)
                g.Position = IndicShaper.PosRephAfterBase;
        var sorted = slice
            .Select((g, i) => (g, i))
            .OrderBy(t => t.g.Position)
            .ThenBy(t => t.i)
            .Select(t => t.g)
            .ToList();
        for (var i = 0; i < sorted.Count; i++) buf[start + i] = sorted[i];
    }
}
