namespace Aspose.Pdf.Text.OpenType;

/// <summary>One glyph as it travels through shaping: the glyph id plus the cluster it
/// came from, so reordering and substitution can be traced back to source characters.</summary>
internal sealed class ShapedGlyph(ushort glyph, int cluster)
{
    public ushort Glyph { get; set; } = glyph;
    public int Cluster { get; set; } = cluster;
    /// <summary>The Indic position this glyph was assigned by the shaper (see
    /// <see cref="IndicShaper"/>); untouched by non-Indic runs.</summary>
    public int Position { get; set; }
    /// <summary>The Indic category of the character behind this glyph.</summary>
    public int Category { get; set; }
    /// <summary>Which syllable this glyph belongs to. Carried through substitution — a
    /// ligature keeps its first component's slot — so the syllable can still be found
    /// after the buffer has shrunk, which is what reordering needs.</summary>
    public int Syllable { get; set; } = -1;
    /// <summary>Feature mask: a lookup only applies to a glyph whose mask carries the
    /// bit the feature was registered under. This is how per-syllable features
    /// (rphf on the leading ra, half on a non-final consonant) stay off the glyphs
    /// they must not touch.</summary>
    public uint Mask { get; set; } = uint.MaxValue;
}

/// <summary>
/// Applies GSUB lookups to a glyph run. Covers the subtable types Indic (and Latin)
/// shaping actually needs: single, multiple, alternate, ligature and chained-context
/// substitution, plus the extension indirection resolved by <see cref="OtfLayout"/>.
/// </summary>
internal sealed class GsubEngine(OtfLayout layout)
{
    private readonly OtfLayout _l = layout;

    /// <summary>Run one lookup over the whole buffer, left to right. Returns true when
    /// anything changed.</summary>
    internal bool ApplyLookup(List<ShapedGlyph> buf, int lookupIndex, uint mask)
    {
        var (type, _, subtables) = _l.Lookup("GSUB", lookupIndex);
        if (subtables.Count == 0) return false;
        var changed = false;
        var i = 0;
        while (i < buf.Count)
        {
            var advanced = false;
            if ((buf[i].Mask & mask) != 0)
            {
                foreach (var sub in subtables)
                {
                    var consumed = ApplySubtable(buf, i, type, sub, mask);
                    if (consumed <= 0) continue;
                    changed = true;
                    i += consumed;
                    advanced = true;
                    break;
                }
            }
            if (!advanced) i++;
        }
        return changed;
    }

    /// <summary>Try one subtable at one position. Returns how many buffer positions to
    /// step past on success (at least 1), or 0 when it does not apply.</summary>
    private int ApplySubtable(List<ShapedGlyph> buf, int i, int type, int sub, uint mask)
    {
        switch (type)
        {
            case 1: return SingleSub(buf, i, sub);
            case 2: return MultipleSub(buf, i, sub);
            case 3: return AlternateSub(buf, i, sub);
            case 4: return LigatureSub(buf, i, sub, mask);
            case 6: return ChainContextSub(buf, i, sub, mask);
            default: return 0;
        }
    }

    // ── type 1: single substitution ───────────────────────────────────────────
    private int SingleSub(List<ShapedGlyph> buf, int i, int sub)
    {
        var format = _l.U16(sub);
        var cov = sub + _l.U16(sub + 2);
        var idx = _l.CoverageIndex(cov, buf[i].Glyph);
        if (idx < 0) return 0;
        if (format == 1)
        {
            var delta = _l.S16(sub + 4);
            buf[i].Glyph = (ushort)((buf[i].Glyph + delta) & 0xFFFF);
            return 1;
        }
        if (format == 2)
        {
            var count = _l.U16(sub + 4);
            if (idx >= count) return 0;
            buf[i].Glyph = _l.U16(sub + 6 + idx * 2);
            return 1;
        }
        return 0;
    }

    // ── type 2: multiple substitution (one glyph becomes several) ─────────────
    private int MultipleSub(List<ShapedGlyph> buf, int i, int sub)
    {
        if (_l.U16(sub) != 1) return 0;
        var cov = sub + _l.U16(sub + 2);
        var idx = _l.CoverageIndex(cov, buf[i].Glyph);
        if (idx < 0) return 0;
        var count = _l.U16(sub + 4);
        if (idx >= count) return 0;
        var seq = sub + _l.U16(sub + 6 + idx * 2);
        var glyphCount = _l.U16(seq);
        if (glyphCount == 0) { buf.RemoveAt(i); return 1; }
        var cluster = buf[i].Cluster;
        var position = buf[i].Position;
        var category = buf[i].Category;
        var gmask = buf[i].Mask;
        var syllable = buf[i].Syllable;
        buf[i].Glyph = _l.U16(seq + 2);
        for (var k = 1; k < glyphCount; k++)
            buf.Insert(i + k, new ShapedGlyph(_l.U16(seq + 2 + k * 2), cluster)
            { Position = position, Category = category, Mask = gmask, Syllable = syllable });
        return glyphCount;
    }

    // ── type 3: alternate substitution (take the first alternate) ─────────────
    private int AlternateSub(List<ShapedGlyph> buf, int i, int sub)
    {
        if (_l.U16(sub) != 1) return 0;
        var cov = sub + _l.U16(sub + 2);
        var idx = _l.CoverageIndex(cov, buf[i].Glyph);
        if (idx < 0) return 0;
        var count = _l.U16(sub + 4);
        if (idx >= count) return 0;
        var set = sub + _l.U16(sub + 6 + idx * 2);
        if (_l.U16(set) == 0) return 0;
        buf[i].Glyph = _l.U16(set + 2);
        return 1;
    }

    // ── type 4: ligature substitution ─────────────────────────────────────────
    private int LigatureSub(List<ShapedGlyph> buf, int i, int sub, uint mask)
    {
        if (_l.U16(sub) != 1) return 0;
        var cov = sub + _l.U16(sub + 2);
        var idx = _l.CoverageIndex(cov, buf[i].Glyph);
        if (idx < 0) return 0;
        var setCount = _l.U16(sub + 4);
        if (idx >= setCount) return 0;
        var set = sub + _l.U16(sub + 6 + idx * 2);
        var ligCount = _l.U16(set);
        for (var k = 0; k < ligCount; k++)
        {
            var lig = set + _l.U16(set + 2 + k * 2);
            var ligGlyph = _l.U16(lig);
            var compCount = _l.U16(lig + 2);           // includes the first glyph
            if (i + compCount > buf.Count) continue;
            var match = true;
            for (var c = 1; c < compCount; c++)
            {
                var g = buf[i + c];
                if (g.Glyph != _l.U16(lig + 2 + c * 2) || (g.Mask & mask) == 0) { match = false; break; }
            }
            if (!match) continue;
            // The ligature takes the first component's slot and cluster; the rest go.
            buf[i].Glyph = ligGlyph;
            for (var c = compCount - 1; c >= 1; c--) buf.RemoveAt(i + c);
            return 1;
        }
        return 0;
    }

    // ── type 6: chained context substitution ──────────────────────────────────
    private int ChainContextSub(List<ShapedGlyph> buf, int i, int sub, uint mask)
    {
        var format = _l.U16(sub);
        if (format == 3)
        {
            var p = sub + 2;
            var backCount = _l.U16(p); p += 2;
            var backCov = new int[backCount];
            for (var k = 0; k < backCount; k++) { backCov[k] = sub + _l.U16(p); p += 2; }
            var inputCount = _l.U16(p); p += 2;
            var inputCov = new int[inputCount];
            for (var k = 0; k < inputCount; k++) { inputCov[k] = sub + _l.U16(p); p += 2; }
            var aheadCount = _l.U16(p); p += 2;
            var aheadCov = new int[aheadCount];
            for (var k = 0; k < aheadCount; k++) { aheadCov[k] = sub + _l.U16(p); p += 2; }
            var substCount = _l.U16(p); p += 2;

            if (inputCount == 0 || i + inputCount > buf.Count) return 0;
            // backtrack runs BACKWARDS from the glyph before i
            for (var k = 0; k < backCount; k++)
            {
                var at = i - 1 - k;
                if (at < 0 || _l.CoverageIndex(backCov[k], buf[at].Glyph) < 0) return 0;
            }
            for (var k = 0; k < inputCount; k++)
                if (_l.CoverageIndex(inputCov[k], buf[i + k].Glyph) < 0) return 0;
            for (var k = 0; k < aheadCount; k++)
            {
                var at = i + inputCount + k;
                if (at >= buf.Count || _l.CoverageIndex(aheadCov[k], buf[at].Glyph) < 0) return 0;
            }
            for (var k = 0; k < substCount; k++)
            {
                var seqIndex = _l.U16(p + k * 4);
                var lookupIndex = _l.U16(p + k * 4 + 2);
                if (seqIndex >= buf.Count - i) continue;
                ApplyNested(buf, i + seqIndex, lookupIndex, mask);
            }
            return inputCount;
        }
        return 0;
    }

    /// <summary>A context rule's nested lookup, applied at one position only.</summary>
    private void ApplyNested(List<ShapedGlyph> buf, int at, int lookupIndex, uint mask)
    {
        var (type, _, subtables) = _l.Lookup("GSUB", lookupIndex);
        foreach (var sub in subtables)
            if (ApplySubtable(buf, at, type, sub, mask) > 0) return;
    }
}
