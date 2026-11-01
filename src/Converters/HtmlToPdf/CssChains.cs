using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>An element on a CSS ancestor chain: its tag plus the id/class hooks a
    /// selector can address. The lifted-table builder grows a chain as it walks the
    /// markup (table → td → div/span …) and threads it into nested builds, so rules
    /// addressed through the document tree reach inner grids.</summary>
    internal sealed class CssElem
    {
        public string Tag = "";
        public string? Id;
        public string[]? Classes;
        // The element's matched `display` value (chain hooks fill it in): the
        // nearest non-null one decides whether a styled run rides its line
        // (inline-block) or may break it.
        public string? Display;
        // The element's 1-based position among its siblings, and the position of the row a cell
        // stands in; 0 = not known, which no structural position matches.
        public int Ordinal;
        public int RowOrdinal;
        // The face the element's own markup names (its style attribute's font-family or `font` family, or a
        // face attribute); null where it names none.
        public string? OwnFamily;
    }

    internal sealed class CssChainSeg
    {
        // Relation to the segment on its LEFT: true = direct child ('>'), false =
        // descendant. Structural table containers (tbody/thead/tfoot/tr) are
        // collapsed at parse; a cell reached from its table purely through child
        // hops stays a CHILD, because the builder's chains seat cells directly
        // under their table node.
        public bool Child;
        public string? Tag;
        public string? Id;
        public List<string>? Classes;
        // A structural position the segment's element must hold (`tr:nth-child(2n+1)`), or the position
        // of the ROW a cell segment stands in (the collapsed `tr:nth-child(2) th`).
        public CssNth? Nth;
        public CssNth? RowNth;
    }

    /// <summary>The position `An+B` a `:nth-child()` selector names (A = 0 for one fixed position).</summary>
    internal readonly struct CssNth
    {
        public readonly int A;
        public readonly int B;

        public CssNth(int a, int b) { A = a; B = b; }

        /// <summary>True when the 1-based ordinal is one the position names; an unknown ordinal (0) is none.</summary>
        public bool Matches(int ordinal)
        {
            if (ordinal <= 0) return false;
            if (A == 0) return ordinal == B;
            var steps = ordinal - B;
            return steps % A == 0 && steps / A >= 0;
        }
    }

    /// <summary>The position a structural pseudo-class names: `:nth-child(An+B | odd | even)` or `:first-child`.
    /// Null for every other pseudo-class, which keeps rejecting the selector.</summary>
    private static CssNth? ParseStructuralPseudo(string pseudo)
    {
        var p = pseudo.Trim().ToLowerInvariant();
        if (p == ":first-child") return new CssNth(0, 1);
        var m = SafeRegex.Match(p, @"^:nth-child\(\s*(?:(odd|even)|(?:([+-]?\d*)n)?\s*([+-]?\s*\d+)?)\s*\)$");
        if (!m.Success) return null;
        if (m.Groups[1].Success) return m.Groups[1].Value == "odd" ? new CssNth(2, 1) : new CssNth(2, 0);
        if (!m.Groups[2].Success && !m.Groups[3].Success) return null;
        var a = 0;
        if (m.Groups[2].Success)
        {
            var coefficient = m.Groups[2].Value;
            a = coefficient is "" or "+" ? 1 : coefficient == "-" ? -1 : int.Parse(coefficient, System.Globalization.CultureInfo.InvariantCulture);
        }
        var b = m.Groups[3].Success
            ? int.Parse(m.Groups[3].Value.Replace(" ", ""), System.Globalization.CultureInfo.InvariantCulture) : 0;
        return new CssNth(a, b);
    }

    /// <summary>A stylesheet rule kept with its FULL selector chain. Only rules the
    /// flat <see cref="ParseStyleSheet"/> map cannot express are kept (an id anywhere,
    /// a child combinator, or three-plus compound parts), so every existing flat-rule
    /// consumer keeps its exact behaviour and the chain pass adds styling only where
    /// none could exist before.</summary>
    internal sealed class CssChainRule
    {
        public List<CssChainSeg> Segs = null!;
        public Dictionary<string, string> Decls = null!;
        public int Spec;      // id 100 / class 10 / tag 1, summed
        public int Order;     // source order, ties broken towards later rules
        // True for a DESCENDANT CELL rule the flat map cannot express either (`.bluesheet td { … }`):
        // those are admitted only for a sheet that states no other tree-addressed rule.
        public bool CellRule;
    }

    /// <summary>An open inline-box run during the cell token walk: a chain-matched
    /// background + inline-block element (title plate, status pill) collecting the
    /// line span it covers; a TrafficLight child adds the trailing circle and its
    /// letter (which leaves the flowed text).</summary>
    private sealed class ChainBoxRun
    {
        public CssElem Elem = null!;
        public int StartLen;
        public double PadL, PadR, PadT, PadB, Radius;
        // CSS-declared box height (the title plates' `height: 4ex`): the box is
        // drawn once with pads + this height and may span following lines.
        public double DeclH;
        // padding-top of a run INSIDE the box that continues onto the next line
        // (`.SmallerTitle { padding-top: 0.75ex }`): the continuation line's gap.
        public double ContPadTop;
        // CSS letter-spacing on the box's text (the title plates' 0.05ex).
        public double LetterSpacing;
        // Null = no rectangle (a standalone badge draws only its circle).
#pragma warning disable S3218 // data-only nested type: its members are always reached through an instance
        public Color? Fill;
#pragma warning restore S3218
        public Color? CircleFill;
        public double CircleD;
        public string CircleLetter = "";
        public Color? CircleLetterColor;
        // Block-level box (a section <h1> bar): spans the cell's content width at
        // draw time, its text centred, in its own colour (white on the red bars).
        public bool FullWidth;
        public bool TextCentered;
        public Color? TextColor;
    }

    /// <summary>The uniform fill a tiny repeated background tile paints: a
    /// `background-image: url(data:…)` whose bitmap is at most a few pixels
    /// (the classic 1×1-GIF pattern) tiles to a solid colour, sampled from the
    /// tile's centre pixel. Null when the declarations carry no such tile, when
    /// the repeat mode is not a full tile (`no-repeat`, `repeat-x`, …), or when
    /// the tile is large enough that its own drawing would show.</summary>
    private static Color? DataUriTileFill(Dictionary<string, string> decls)
    {
        if (!decls.TryGetValue("background-image", out var bg)
            && !decls.TryGetValue("background", out bg)) return null;
        var um = SafeRegex.Match(bg, @"url\(\s*[""']?\s*data:image/[^;,]+;base64,([A-Za-z0-9+/=]+)",
            RegexOptions.IgnoreCase);
        if (!um.Success) return null;
        if (decls.TryGetValue("background-repeat", out var rep)
            && !rep.Trim().Equals("repeat", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var bytes = Base64Payload(um.Groups[1].Value);
            // The managed GIF decoder answers the classic 1x1-GIF tile on every
            // platform - the same decoder ImageStamp trusts - so the fill no
            // longer vanishes off Windows. Other tile formats keep the GDI+
            // decode below, Windows-only by the repo-wide convention.
            if (IO.GifDecoder.TryDecode(bytes) is (var gifRgb, var gifAlpha, var gifW, var gifH))
            {
                if (gifW > MaxUniformTilePx || gifH > MaxUniformTilePx) return null;
                var ci = (gifH / 2) * gifW + gifW / 2;
                if (gifAlpha.Length > ci && gifAlpha[ci] < 32) return null;
                return Color.FromRgbBytes(gifRgb[ci * 3], gifRgb[ci * 3 + 1], gifRgb[ci * 3 + 2]);
            }
            if (!Compat.IsWindows()) return null;
#pragma warning disable CA1416
            using var ms = new System.IO.MemoryStream(bytes);
            using var bmp = new System.Drawing.Bitmap(ms);
            if (bmp.Width > MaxUniformTilePx || bmp.Height > MaxUniformTilePx) return null;
            var px = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
            if (px.A < 32) return null;
            return Color.FromRgbBytes(px.R, px.G, px.B);
#pragma warning restore CA1416
        }
        catch { return null; }
    }

    // A repeated tile up to this many pixels per side reads as a uniform fill at
    // render resolution (4px = 3pt — smaller than one 8px comparator block).
    private const int MaxUniformTilePx = 4;

    /// <summary>@media handling for the chain parser: a screen-only group is dropped
    /// whole, any other group is unwrapped in place — the PDF renderer is a print
    /// target (print rules are honoured, screen-only
    /// linked sheets ignored).</summary>
    private static string FlattenMediaBlocks(string css)
    {
        if (css.IndexOf("@media", StringComparison.OrdinalIgnoreCase) < 0) return css;
        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            var at = css.IndexOf("@media", i, StringComparison.OrdinalIgnoreCase);
            if (at < 0) { sb.Append(css, i, css.Length - i); break; }
            sb.Append(css, i, at - i);
            var brace = css.IndexOf('{', at);
            if (brace < 0) break;
            var depth = 1; var j = brace + 1;
            while (j < css.Length && depth > 0)
            {
                if (css[j] == '{') depth++;
                else if (css[j] == '}') depth--;
                j++;
            }
            var contentEnd = depth == 0 ? j - 1 : css.Length;
            var media = css[(at + 6)..brace];
            var screenOnly = media.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0
                && media.IndexOf("print", StringComparison.OrdinalIgnoreCase) < 0
                && media.IndexOf("all", StringComparison.OrdinalIgnoreCase) < 0;
            if (!screenOnly) sb.Append(css, brace + 1, contentEnd - brace - 1);
            i = j;
        }
        return sb.ToString();
    }

    private static (List<CssChainSeg>? result, int spec, bool chainOnly, bool cellRule) ParseChainSelector(string sel)
    {
        int spec = default;
        bool chainOnly = default;
        bool cellRule = default;
        spec = 0; chainOnly = false; cellRule = false;
        sel = sel.Trim();
        // (the combinators are read outside a pseudo-class argument: `nth-child(2n+1)` carries a plus of its own)
        if (sel.Length == 0 || SafeRegex.Replace(sel, @"\([^)]*\)", "").IndexOfAny(new[] { '+', '~', '[', '*', '@' }) >= 0) return (null, spec, chainOnly, cellRule);
        var segs = new List<CssChainSeg>();
        var child = false; var hadChild = false; var parts = 0; var hasId = false;
        var hadDrop = false; var dropAllChild = true;
        CssNth? rowNth = null;
        foreach (var tokRaw in SafeRegex.Split(sel, @"(>)|\s+"))
        {
            var t = tokRaw?.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            if (t == ">") { child = true; hadChild = true; continue; }
            // A structural pseudo-class is read on a ROW alone (the striped grid's `tr:nth-child(2n+1)`);
            // every other pseudo-class, on any element, still rejects the selector.
            CssNth? nth = null;
            var colon = t.IndexOf(':');
            if (colon >= 0)
            {
                nth = ParseStructuralPseudo(t[colon..]);
                t = t[..colon];
                if (nth is null || !t.Equals("tr", StringComparison.OrdinalIgnoreCase)) return (null, spec, chainOnly, cellRule);
            }
            var m = SafeRegex.Match(t, @"^([a-zA-Z][\w-]*)?((?:[.#][\w-]+)+)?$");
            if (!m.Success || (m.Groups[1].Length == 0 && m.Groups[2].Length == 0)) return (null, spec, chainOnly, cellRule);
            parts++;
            var seg = new CssChainSeg
            {
                Child = child,
                Tag = m.Groups[1].Length > 0 ? m.Groups[1].Value.ToLowerInvariant() : null,
            };
            child = false;
            if (seg.Tag is not null) spec += 1;
            if (nth is not null) spec += 10;
            foreach (var value in SafeRegex.Matches(m.Groups[2].Value, @"[.#][\w-]+").Select(h => h.Value))
            {
                if (value[0] == '#') { seg.Id = value[1..]; spec += 100; hasId = true; }
                else { (seg.Classes ??= new List<string>()).Add(value[1..]); spec += 10; }
            }
            // Structural table containers add no selectivity between a table and
            // its cells — collapse them, keeping the child relation only when every
            // dropped hop was a child combinator. A row's position is kept for the
            // cell that follows it, or stands as the selector's last segment.
            if (seg.Id is null && seg.Classes is null
                && seg.Tag is "tbody" or "thead" or "tfoot" or "tr")
            {
                if (nth is not null) rowNth = nth;
                hadDrop = true;
                dropAllChild &= seg.Child;
                continue;
            }
            if (hadDrop)
            {
                seg.Child = seg.Child && dropAllChild;
                hadDrop = false; dropAllChild = true;
            }
            if (rowNth is not null) { seg.RowNth = rowNth; rowNth = null; }
            segs.Add(seg);
        }
        if (rowNth is not null) segs.Add(new CssChainSeg { Tag = "tr", Nth = rowNth });
        chainOnly = hasId || hadChild || parts > 2;
        // A two-part DESCENDANT CELL rule (`.bluesheet td { padding-left: 5px }`) addresses the cells
        // of every grid under a container the flat class map cannot reach either: the cascade has to
        // walk the ancestors to know which cells it dresses. It is reported apart from the rules above
        // because a sheet that already has one of those was calibrated with its own cascade, and adding
        // to it changes documents this rule has nothing to say about.
        // …and it must still be TWO segments after the structural containers collapse:  is a
        // plain cell rule the flat map expresses perfectly well, and admitting it here dresses the cells
        // of a document that never asked for a cascade.
        cellRule = parts == 2 && segs.Count == 2
            && segs[^1].Tag is "td" or "th" && segs[^1].Classes is null && segs[^1].Id is null
            && (segs[0].Id is not null || segs[0].Classes is not null);
        return (segs.Count > 0 ? segs : null, spec, chainOnly, cellRule);
    }

    /// <summary>A cell rule addressed through its LEFT SIBLING
    /// (<c>.label + td { padding-left: .5em }</c>): the cell that closed immediately
    /// before this one decides whether the declarations apply. The relation is not an
    /// ancestor one, so <see cref="CssChainRule"/> cannot carry it — the report family's
    /// label/value grids state their value inset this way and nothing else in the
    /// corpus addresses a cell by its sibling.</summary>
    internal sealed class CssSiblingCellRule
    {
        /// <summary>What the cell to the LEFT must be.</summary>
        public CssChainSeg Left = null!;
        /// <summary>What THIS cell must be — always a td/th, so the rule reaches cells only.</summary>
        public CssChainSeg Right = null!;
        public Dictionary<string, string> Decls = null!;
        public int Spec;      // class 10 / tag 1, summed over both sides
        public int Order;     // source order, ties broken towards later rules
    }

    /// <summary>The one simple selector of a sibling pair: a tag with optional class
    /// hooks, no id and no nested combinator. Null when the text is anything else.</summary>
    private static CssChainSeg? ParseSimpleSeg(string raw, ref int spec)
    {
        var t = raw.Trim();
        var m = SafeRegex.Match(t, @"^([a-zA-Z][\w-]*)?((?:\.[\w-]+)+)?$");
        if (!m.Success || (m.Groups[1].Length == 0 && m.Groups[2].Length == 0)) return null;
        var seg = new CssChainSeg { Tag = m.Groups[1].Length > 0 ? m.Groups[1].Value.ToLowerInvariant() : null };
        if (seg.Tag is not null) spec += 1;
        foreach (Match h in SafeRegex.Matches(m.Groups[2].Value, @"\.[\w-]+"))
        {
            (seg.Classes ??= new List<string>()).Add(h.Value[1..]);
            spec += 10;
        }
        return seg;
    }

    /// <summary>Parse every style block's adjacent-sibling CELL rules (see
    /// <see cref="CssSiblingCellRule"/>). Only the two-part form whose right side is a
    /// td/th is kept, so a sheet that reaches cells through an ancestor as well
    /// (<c>.basicTable th + th</c>) is left to the flat map exactly as before.
    /// Returns null when the document declares none.</summary>
    internal static List<CssSiblingCellRule>? ParseSiblingCellRules(string html)
    {
        List<CssSiblingCellRule>? rules = null;
        var order = 0;
        foreach (Match block in SafeRegex.Matches(html, @"<style\b([^>]*)>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var cssText = FlattenMediaBlocks(SafeRegex.Replace(block.Groups[2].Value, @"/\*[\s\S]*?\*/", ""));
            foreach (var groups in SafeRegex.Matches(cssText, @"([^{}]+)\{([^{}]*)\}").Select(rule => rule.Groups))
            {
                Dictionary<string, string>? decls = null;
                foreach (var dGroups in SafeRegex.Matches(StyleDeclRx, groups[2].Value).Select(d => d.Groups))
                    (decls ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                        [dGroups[1].Value.Trim().ToLowerInvariant()] = dGroups[2].Value.Trim();
                if (decls is null) continue;
                foreach (var selRaw in groups[1].Value.Split(','))
                {
                    var plus = selRaw.Split('+');
                    if (plus.Length != 2) continue;
                    var spec = 0;
                    if (ParseSimpleSeg(plus[0], ref spec) is not { } left) continue;
                    if (ParseSimpleSeg(plus[1], ref spec) is not { } right) continue;
                    if (right.Tag is not ("td" or "th")) continue;
                    (rules ??= new List<CssSiblingCellRule>()).Add(new CssSiblingCellRule
                    { Left = left, Right = right, Decls = decls, Spec = spec, Order = order++ });
                }
            }
        }
        return rules;
    }

    /// <summary>Merged declarations of every sibling-cell rule that matches this cell
    /// beside its left neighbour — lower specificity first, source order breaking ties.
    /// Null when nothing matches.</summary>
    internal static Dictionary<string, string>? MatchSiblingCellDecls(
        List<CssSiblingCellRule>? rules, CssElem left, CssElem right)
    {
        if (rules is null) return null;
        List<CssSiblingCellRule>? hits = null;
        foreach (var r in rules.Where(r => ChainSegMatches(r.Left, left) && ChainSegMatches(r.Right, right)))
            (hits ??= new List<CssSiblingCellRule>()).Add(r);
        if (hits is null) return null;
        hits.Sort((a, b) => a.Spec != b.Spec ? a.Spec.CompareTo(b.Spec) : a.Order.CompareTo(b.Order));
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in hits)
            foreach (var kv in r.Decls) merged[kv.Key] = kv.Value;
        return merged;
    }

    /// <summary>Parse every style block into full-chain rules (see
    /// <see cref="CssChainRule"/>). Screen-only blocks — the media attribute an
    /// inlined &lt;link&gt; carries, or an @media group — are excluded. Returns null
    /// when the document has no rule the flat map could not express.</summary>
    internal static List<CssChainRule>? ParseChainRules(string html, out bool cellRulesOnly)
    {
        List<CssChainRule>? rules = null;
        List<CssChainRule>? cellRules = null;
        List<CssChainRule>? positionRules = null;
        cellRulesOnly = false;
        var order = 0;
        foreach (var groups in SafeRegex.Matches(html, @"<style\b([^>]*)>([\s\S]*?)</style>", RegexOptions.IgnoreCase).Select(block => block.Groups))
        {
            var mAttr = SafeRegex.Match(groups[1].Value, @"media\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (mAttr.Success)
            {
                var mv = mAttr.Groups[1].Value;
                if (mv.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0
                    && mv.IndexOf("print", StringComparison.OrdinalIgnoreCase) < 0
                    && mv.IndexOf("all", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }
            var cssText = FlattenMediaBlocks(SafeRegex.Replace(groups[2].Value, @"/\*[\s\S]*?\*/", ""));
            foreach (var ruleGroups in SafeRegex.Matches(cssText, @"([^{}]+)\{([^{}]*)\}").Select(rule => rule.Groups))
            {
                Dictionary<string, string>? decls = null;
                foreach (var dGroups in SafeRegex.Matches(StyleDeclRx, ruleGroups[2].Value).Select(d => d.Groups))
                    (decls ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                        [dGroups[1].Value.Trim().ToLowerInvariant()] = dGroups[2].Value.Trim();
                if (decls is null) continue;
                foreach (var selRaw in ruleGroups[1].Value.Split(','))
                {
                    (var segs, var spec, var chainOnly, var cellRule) = ParseChainSelector(selRaw);
                    if (segs is null || !(chainOnly || cellRule)) continue;
                    var parsed = new CssChainRule
                    { Segs = segs, Decls = decls, Spec = spec, Order = order++, CellRule = cellRule };
                    // A rule that addresses a row by its position joins a sheet's cascade only where that
                    // sheet already has one: it was rejected whole before, and admitting it alone would turn
                    // the cascade on for a document that never had it.
                    if (chainOnly && segs.Any(s => s.Nth is not null || s.RowNth is not null))
                        (positionRules ??= new List<CssChainRule>()).Add(parsed);
                    else if (chainOnly) (rules ??= new List<CssChainRule>()).Add(parsed);
                    // …and a cell rule earns the cascade only by stating the cells' BOX: one that only
                    // dresses them has nothing the flat map cannot do, and turning the cascade on for it
                    // moves a document that never needed it.
                    else if (DeclaresCellBox(decls)) (cellRules ??= new List<CssChainRule>()).Add(parsed);
                }
            }
        }
        // A sheet that already states a rule the flat map cannot express was calibrated with that
        // cascade, and its descendant CELL rules stay out of it. A sheet whose only such rule is a
        // cell rule is a document the flat map never dressed at all, and it gets them.
        cellRulesOnly = rules is null && cellRules is not null;
        if (rules is not null && positionRules is not null) rules.AddRange(positionRules);
        return rules ?? cellRules;
    }

    /// <summary>True when the sheet states a DESCENDANT CELL rule that declares the cells' own box
    /// (<c>.bluesheet td { padding-left: 5px; padding-right: 5px }</c>). A sheet that sizes its cells this
    /// way leaves no UA padding pair for the legacy slack to stand in for, and its grids measure on the
    /// declared box instead.</summary>
    /// <summary>The declarations state a cell's own horizontal box: the shorthand, or both longhands.
    /// The legacy slack stands in for the UA's padding PAIR, so one side is not a replacement for it.</summary>
    private static bool DeclaresCellBox(Dictionary<string, string> decls)
        => decls.ContainsKey("padding")
            || (decls.ContainsKey("padding-left") && decls.ContainsKey("padding-right"));

    internal static bool SheetDeclaresCellBox(List<CssChainRule>? rules)
    {
        if (rules is null) return false;
        foreach (var r in rules)
        {
            if (!r.CellRule || r.Segs.Count != 2) continue;
            var last = r.Segs[1];
            if (last.Tag is not ("td" or "th") || last.Classes is not null || last.Id is not null) continue;
            if (DeclaresCellBox(r.Decls)) return true;
        }
        return false;
    }

    private static readonly char[] FontFamilyQuotes = { '"', (char)39 };

    /// <summary>The first INSTALLED family the sheet's <c>body</c> rule names, null when it names
    /// none (or none is installed): the face a quirks grid draws and pitches in.</summary>
    internal static string? SheetBodyFace(IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        if (!css.TryGetValue("body", out var bodyDecls)
            || !bodyDecls.TryGetValue("font-family", out var fams)) return null;
        return FirstInstalledFamily(fams);
    }

    /// <summary>The first family of a font-family stack whose metrics are installed (quotes stripped), as the body
    /// face is read; null when none of them is.</summary>
    internal static string? FirstInstalledFamily(string stack)
    {
        foreach (var fam in stack.Split(','))
        {
            var f = fam.Trim().Trim(FontFamilyQuotes);
            if (f.Length > 0 && WinMetricsFor(f) is not null) return f;
        }
        return null;
    }

    private static bool ChainSegMatches(CssChainSeg s, CssElem e)
    {
        if (s.Tag is not null && !s.Tag.Equals(e.Tag, StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Id is not null
            && !(e.Id is not null && s.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase))) return false;
        if (s.Nth is { } nth && !nth.Matches(e.Ordinal)) return false;
        if (s.RowNth is { } rowNth && !rowNth.Matches(e.RowOrdinal)) return false;
        if (s.Classes is not null)
        {
            if (e.Classes is null) return false;
            foreach (var c in s.Classes)
            {
                var ok = false;
                if (e.Classes.Any(ec => string.Equals(c, ec, StringComparison.OrdinalIgnoreCase))) { ok = true; }
                if (!ok) return false;
            }
        }
        return true;
    }

    private static bool MatchChainAt(List<CssChainSeg> segs, int si, IReadOnlyList<CssElem> chain, int ci)
    {
        if (ci < 0 || !ChainSegMatches(segs[si], chain[ci])) return false;
        if (si == 0) return true;
        if (segs[si].Child) return MatchChainAt(segs, si - 1, chain, ci - 1);
        for (var k = ci - 1; k >= 0; k--)
            if (MatchChainAt(segs, si - 1, chain, k)) return true;
        return false;
    }

    /// <summary>Merged declarations of every chain rule whose selector matches the
    /// chain's LAST element through its ancestors — lower specificity first, source
    /// order breaking ties, so the most specific rule's property wins. Null when
    /// nothing matches.</summary>
    internal static Dictionary<string, string>? MatchChainDecls(List<CssChainRule>? rules, List<CssElem> chain)
    {
        if (rules is null || chain.Count == 0) return null;
        List<CssChainRule>? hit = null;
        foreach (var r in rules.Where(r => MatchChainAt(r.Segs, r.Segs.Count - 1, chain, chain.Count - 1)))
            (hit ??= new List<CssChainRule>()).Add(r);
        if (hit is null) return null;
        hit.Sort((a, b) => a.Spec != b.Spec ? a.Spec.CompareTo(b.Spec) : a.Order.CompareTo(b.Order));
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in hit)
            foreach (var kv in r.Decls) merged[kv.Key] = kv.Value;
        return merged;
    }

    /// <summary>The value the chain rules matching <paramref name="chain"/> give one property, with the
    /// specificity of the rule it comes from (the most specific, then the latest, as the merge above
    /// resolves it); null when no matching rule declares it.</summary>
    internal static (string value, int spec)? MatchChainDecl(List<CssChainRule>? rules, List<CssElem> chain, string property)
    {
        if (rules is null || chain.Count == 0) return null;
        CssChainRule? winner = null;
        foreach (var r in rules.Where(r => r.Decls.ContainsKey(property) && MatchChainAt(r.Segs, r.Segs.Count - 1, chain, chain.Count - 1)))
            if (winner is null || r.Spec > winner.Spec || (r.Spec == winner.Spec && r.Order > winner.Order)) winner = r;
        return winner is null ? null : (winner.Decls[property], winner.Spec);
    }

    /// <summary>Resolve a CSS length to points against a font-size context: px at
    /// 0.75, em on the font, ex at half an em. 0 when unparsable (percent lengths
    /// need their own base and are the caller's job).</summary>
    private static double ChainLenPt(string v, double fontPt)
    {
        var m = SafeRegex.Match(v.Trim(), @"^(-?[\d.]+)\s*(px|pt|em|ex|in|cm|mm)?$", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return 0;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "pt" => n,
            "em" => n * fontPt,
            "ex" => n * fontPt / 2,
            "in" => n * 72,
            "cm" => n * 72 / 2.54,
            "mm" => n * 72 / 25.4,
            _ => n * 0.75,
        };
    }

    /// <summary>A `border: 1px solid white` shorthand as a box BorderInfo; null for
    /// zero-width/none borders. currentColor and a missing colour fall to black.</summary>
    private static BorderInfo? ChainBorder(string v)
    {
        var t = v.Trim();
        if (t.StartsWith("0", StringComparison.Ordinal)
            || t.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0) return null;
        var w = 0.75;
        var wm = SafeRegex.Match(t, @"([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
        if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wv) && wv > 0)
            w = wm.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? wv : wv * 0.75;
        var residue = SafeRegex.Replace(t,
            @"([\d.]+)\s*(px|pt)|solid|outset|inset|dotted|dashed|double|groove|ridge|currentcolor", "",
            RegexOptions.IgnoreCase).Trim();
        return new BorderInfo(BorderSide.Box, w, ParseCssColor(residue) ?? Color.Black);
    }

    /// <summary>Chain element for an open tag: its name plus the id/classes a
    /// selector can address.</summary>
    /// <summary>The container elements still OPEN after <paramref name="html"/>, appended to
    /// <paramref name="open"/>: the ancestor chain a grid further on stands inside. The block
    /// builder advances one stack across a fragment's segments, so a table carries the divs open
    /// above it into its own build and the sheet's scoped cell rules reach its cells.</summary>
    private static void AdvanceOpenContainerChain(List<CssElem> open, string html)
    {
        foreach (Match m in SafeRegex.Matches(ContainerTagRx, html))
        {
            var tag = m.Groups["tag"].Value.ToLowerInvariant();
            // the framed-wrapper marker stands for the div it renamed
            if (tag == FrameDivTag) tag = "div";
            if (m.Groups["close"].Length > 0)
            {
                for (var i = open.Count - 1; i >= 0; i--)
                    if (string.Equals(open[i].Tag, tag, StringComparison.Ordinal))
                    { open.RemoveRange(i, open.Count - i); break; }
                continue;
            }
            if (m.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
            var e = new CssElem { Tag = tag };
            if (SafeRegex.Match(m.Value, @"\bid\s*=\s*(?:[""']([^""']*)[""']|([\w-]+))",
                    RegexOptions.IgnoreCase) is { Success: true } im)
            {
                var v = (im.Groups[1].Success ? im.Groups[1].Value : im.Groups[2].Value).Trim();
                if (v.Length > 0) e.Id = v;
            }
            if (SafeRegex.Match(m.Value, @"\bclass\s*=\s*(?:[""']([^""']*)[""']|([\w-]+))",
                    RegexOptions.IgnoreCase) is { Success: true } cm)
            {
                var v = cm.Groups[1].Success ? cm.Groups[1].Value : cm.Groups[2].Value;
                var cls = v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (cls.Length > 0) e.Classes = cls;
            }
            // (…and the face its own style names: the grids it holds inherit that face, not the body's)
            if (m.Value.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0) e.OwnFamily = OwnFamily(DivStyleOf(m.Value), null);
            open.Add(e);
        }
    }

    /// <summary>The container elements the open-chain scan tracks: the block wrappers a
    /// stylesheet scopes its grids through, all of which carry an explicit close tag.</summary>
    private static readonly Regex ContainerTagRx = new Regex(
        @"<(?<close>/?)(?<tag>div|" + FrameDivTag + @"|section|article|aside|header|footer|main|nav|form|fieldset|blockquote|center)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    private static CssElem ChainTokElem(string tag, Dictionary<string, string>? attrs)
    {
        var e = new CssElem { Tag = tag };
        if (attrs is not null)
        {
            if (attrs.TryGetValue("id", out var idv) && !string.IsNullOrWhiteSpace(idv)) e.Id = idv.Trim();
            if (attrs.TryGetValue("class", out var clv) && !string.IsNullOrWhiteSpace(clv))
                e.Classes = clv.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        }
        return e;
    }

    /// <summary>A CSS <c>li:nth-child(An+B)::before { content: "…" }</c> generated-content
    /// marker: the item text a matching &lt;li&gt; is prefixed with. Only the small subset used
    /// by list styling (an optional container class, an nth-child index, a literal content
    /// string) is modelled — enough to reproduce editor-authored ordered-list markers.</summary>
    private sealed class BeforeMarker
    {
        public string? ContainerClass; // class on the enclosing <ol>/<ul> (null = any list)
        public int A;                  // nth-child(An+B) coefficient
        public int B;                  // nth-child(An+B) offset
        public string Content = "";    // generated text, logical order
        public bool Matches(int index1Based) => A == 0
            ? index1Based == B
            : (index1Based - B) % A == 0 && (index1Based - B) / A >= 0;
    }

    // .class > li:nth-child(An+B)::before  /  li:nth-child(An+B):before  — the container class
    // and combinator are optional; nth-child arg captured raw for NthChildRx.
    private static readonly Regex BeforeSelectorRx = new(
        @"(?:\.(?<cc>[A-Za-z_][\w-]*)\s*[>\s]\s*)?[A-Za-z]+:nth-child\(\s*(?<nc>[^)]+?)\s*\)\s*::?before",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    private static readonly Regex NthChildRx = new(
        @"^(?:(?<a>-?\d*)n\s*(?:(?<sign>[+-])\s*(?<b>\d+))?|(?<lit>-?\d+))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    private static readonly Regex BeforeContentRx = new(
        @"content\s*:\s*(['""])(?<v>.*?)\1",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, RegexLimits.Timeout);

    /// <summary>Scan the document's &lt;style&gt; blocks for
    /// <c>li:nth-child(An+B)::before { content: "…" }</c> rules and return them as generated-content
    /// markers, so an <c>&lt;ol&gt;</c> whose CSS supplies its own markers (list-style:none + ::before)
    /// renders those instead of the numeric default.</summary>
    private static List<BeforeMarker> ParseBeforeMarkers(string html)
    {
        var result = new List<BeforeMarker>();
        foreach (Match block in SafeRegex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var css = SafeRegex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            foreach (var groups in SafeRegex.Matches(css, @"([^{}]+)\{([^{}]*)\}").Select(rule => rule.Groups))
            {
                var sel = SafeRegex.Match(BeforeSelectorRx, groups[1].Value);
                if (!sel.Success) continue;
                var cm = SafeRegex.Match(BeforeContentRx, groups[2].Value);
                if (!cm.Success) continue;
                var nc = SafeRegex.Match(NthChildRx, sel.Groups["nc"].Value.Trim());
                if (!nc.Success) continue;
                int a, b;
                if (nc.Groups["lit"].Success) { a = 0; b = int.Parse(nc.Groups["lit"].Value, System.Globalization.CultureInfo.InvariantCulture); }
                else
                {
                    var av = nc.Groups["a"].Value;
                    if (av.Length == 0) a = 1;
                    else if (av == "-") a = -1;
                    else a = int.Parse(av, System.Globalization.CultureInfo.InvariantCulture);
                    if (nc.Groups["b"].Success) b = int.Parse(nc.Groups["b"].Value, System.Globalization.CultureInfo.InvariantCulture) * (nc.Groups["sign"].Value == "-" ? -1 : 1);
                    else b = 0;
                }
                result.Add(new BeforeMarker
                {
                    ContainerClass = sel.Groups["cc"].Success ? sel.Groups["cc"].Value : null,
                    A = a,
                    B = b,
                    Content = DecodeEntities(cm.Groups["v"].Value),
                });
            }
        }
        return result;
    }

    /// <summary>The subset of <paramref name="markers"/> that applies to an <c>&lt;ol&gt;/&lt;ul&gt;</c>
    /// carrying <paramref name="classAttr"/> — a rule with no container class matches any list; a
    /// rule scoped to <c>.foo</c> matches only when the list has class <c>foo</c>. Null when none.</summary>
    private static List<BeforeMarker>? ResolveListBeforeRules(IReadOnlyList<BeforeMarker>? markers, string? classAttr)
    {
        if (markers is null || markers.Count == 0) return null;
        var classes = classAttr?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? System.Array.Empty<string>();
        List<BeforeMarker>? hits = null;
        foreach (var m in markers.Where(m => m.ContainerClass is null || System.Array.IndexOf(classes, m.ContainerClass) >= 0))
            (hits ??= new()).Add(m);
        return hits;
    }
}
