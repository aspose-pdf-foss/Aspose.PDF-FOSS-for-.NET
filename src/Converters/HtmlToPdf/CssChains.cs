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
        public Color? Fill;
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
        var um = Regex.Match(bg, @"url\(\s*[""']?\s*data:image/[^;,]+;base64,([A-Za-z0-9+/=]+)",
            RegexOptions.IgnoreCase);
        if (!um.Success) return null;
        if (decls.TryGetValue("background-repeat", out var rep)
            && !rep.Trim().Equals("repeat", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var bytes = System.Convert.FromBase64String(um.Groups[1].Value);
            // The managed GIF decoder answers the classic 1x1-GIF tile on every
            // platform - the same decoder ImageStamp trusts - so the fill no
            // longer vanishes off Windows. Other tile formats keep the GDI+
            // decode below, Windows-only by the repo-wide convention.
            if (IO.GifDecoder.TryDecode(bytes, out var gifRgb, out var gifAlpha,
                    out var gifW, out var gifH))
            {
                if (gifW > MaxUniformTilePx || gifH > MaxUniformTilePx) return null;
                var ci = (gifH / 2) * gifW + gifW / 2;
                if (gifAlpha.Length > ci && gifAlpha[ci] < 32) return null;
                return Color.FromRgbBytes(gifRgb[ci * 3], gifRgb[ci * 3 + 1], gifRgb[ci * 3 + 2]);
            }
            if (!OperatingSystem.IsWindows()) return null;
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

    private static List<CssChainSeg>? ParseChainSelector(string sel, out int spec, out bool chainOnly)
    {
        spec = 0; chainOnly = false;
        sel = sel.Trim();
        if (sel.Length == 0 || sel.IndexOfAny(new[] { '+', '~', ':', '[', '*', '@' }) >= 0) return null;
        var segs = new List<CssChainSeg>();
        var child = false; var hadChild = false; var parts = 0; var hasId = false;
        var hadDrop = false; var dropAllChild = true;
        foreach (var tokRaw in Regex.Split(sel, @"(>)|\s+"))
        {
            var t = tokRaw?.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            if (t == ">") { child = true; hadChild = true; continue; }
            var m = Regex.Match(t, @"^([a-zA-Z][\w-]*)?((?:[.#][\w-]+)+)?$");
            if (!m.Success || (m.Groups[1].Length == 0 && m.Groups[2].Length == 0)) return null;
            parts++;
            var seg = new CssChainSeg
            {
                Child = child,
                Tag = m.Groups[1].Length > 0 ? m.Groups[1].Value.ToLowerInvariant() : null,
            };
            child = false;
            if (seg.Tag is not null) spec += 1;
            foreach (Match h in Regex.Matches(m.Groups[2].Value, @"[.#][\w-]+"))
            {
                if (h.Value[0] == '#') { seg.Id = h.Value[1..]; spec += 100; hasId = true; }
                else { (seg.Classes ??= new List<string>()).Add(h.Value[1..]); spec += 10; }
            }
            // Structural table containers add no selectivity between a table and
            // its cells — collapse them, keeping the child relation only when every
            // dropped hop was a child combinator.
            if (seg.Id is null && seg.Classes is null
                && seg.Tag is "tbody" or "thead" or "tfoot" or "tr")
            {
                hadDrop = true;
                dropAllChild &= seg.Child;
                continue;
            }
            if (hadDrop)
            {
                seg.Child = seg.Child && dropAllChild;
                hadDrop = false; dropAllChild = true;
            }
            segs.Add(seg);
        }
        chainOnly = hasId || hadChild || parts > 2;
        return segs.Count > 0 ? segs : null;
    }

    /// <summary>Parse every style block into full-chain rules (see
    /// <see cref="CssChainRule"/>). Screen-only blocks — the media attribute an
    /// inlined &lt;link&gt; carries, or an @media group — are excluded. Returns null
    /// when the document has no rule the flat map could not express.</summary>
    internal static List<CssChainRule>? ParseChainRules(string html)
    {
        List<CssChainRule>? rules = null;
        var order = 0;
        foreach (Match block in Regex.Matches(html, @"<style\b([^>]*)>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var mAttr = Regex.Match(block.Groups[1].Value, @"media\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (mAttr.Success)
            {
                var mv = mAttr.Groups[1].Value;
                if (mv.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0
                    && mv.IndexOf("print", StringComparison.OrdinalIgnoreCase) < 0
                    && mv.IndexOf("all", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }
            var cssText = FlattenMediaBlocks(Regex.Replace(block.Groups[2].Value, @"/\*[\s\S]*?\*/", ""));
            foreach (Match rule in Regex.Matches(cssText, @"([^{}]+)\{([^{}]*)\}"))
            {
                Dictionary<string, string>? decls = null;
                foreach (Match d in StyleDeclRx.Matches(rule.Groups[2].Value))
                    (decls ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                        [d.Groups[1].Value.Trim().ToLowerInvariant()] = d.Groups[2].Value.Trim();
                if (decls is null) continue;
                foreach (var selRaw in rule.Groups[1].Value.Split(','))
                {
                    var segs = ParseChainSelector(selRaw, out var spec, out var chainOnly);
                    if (segs is null || !chainOnly) continue;
                    (rules ??= new List<CssChainRule>()).Add(new CssChainRule
                    { Segs = segs, Decls = decls, Spec = spec, Order = order++ });
                }
            }
        }
        return rules;
    }

    private static bool ChainSegMatches(CssChainSeg s, CssElem e)
    {
        if (s.Tag is not null && !s.Tag.Equals(e.Tag, StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Id is not null
            && !(e.Id is not null && s.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase))) return false;
        if (s.Classes is not null)
        {
            if (e.Classes is null) return false;
            foreach (var c in s.Classes)
            {
                var ok = false;
                foreach (var ec in e.Classes)
                    if (string.Equals(c, ec, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
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
        foreach (var r in rules)
            if (MatchChainAt(r.Segs, r.Segs.Count - 1, chain, chain.Count - 1))
                (hit ??= new List<CssChainRule>()).Add(r);
        if (hit is null) return null;
        hit.Sort((a, b) => a.Spec != b.Spec ? a.Spec.CompareTo(b.Spec) : a.Order.CompareTo(b.Order));
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in hit)
            foreach (var kv in r.Decls) merged[kv.Key] = kv.Value;
        return merged;
    }

    /// <summary>Resolve a CSS length to points against a font-size context: px at
    /// 0.75, em on the font, ex at half an em. 0 when unparsable (percent lengths
    /// need their own base and are the caller's job).</summary>
    private static double ChainLenPt(string v, double fontPt)
    {
        var m = Regex.Match(v.Trim(), @"^(-?[\d.]+)\s*(px|pt|em|ex|in|cm|mm)?$", RegexOptions.IgnoreCase);
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
        var wm = Regex.Match(t, @"([\d.]+)\s*(px|pt)", RegexOptions.IgnoreCase);
        if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var wv) && wv > 0)
            w = wm.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? wv : wv * 0.75;
        var residue = Regex.Replace(t,
            @"([\d.]+)\s*(px|pt)|solid|outset|inset|dotted|dashed|double|groove|ridge|currentcolor", "",
            RegexOptions.IgnoreCase).Trim();
        return new BorderInfo(BorderSide.Box, w, ParseCssColor(residue) ?? Color.Black);
    }

    /// <summary>Chain element for an open tag: its name plus the id/classes a
    /// selector can address.</summary>
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
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NthChildRx = new(
        @"^(?:(?<a>-?\d*)n\s*(?:(?<sign>[+-])\s*(?<b>\d+))?|(?<lit>-?\d+))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BeforeContentRx = new(
        @"content\s*:\s*(['""])(?<v>.*?)\1",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>Scan the document's &lt;style&gt; blocks for
    /// <c>li:nth-child(An+B)::before { content: "…" }</c> rules and return them as generated-content
    /// markers, so an <c>&lt;ol&gt;</c> whose CSS supplies its own markers (list-style:none + ::before)
    /// renders those instead of the numeric default.</summary>
    private static List<BeforeMarker> ParseBeforeMarkers(string html)
    {
        var result = new List<BeforeMarker>();
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var css = Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
            {
                var sel = BeforeSelectorRx.Match(rule.Groups[1].Value);
                if (!sel.Success) continue;
                var cm = BeforeContentRx.Match(rule.Groups[2].Value);
                if (!cm.Success) continue;
                var nc = NthChildRx.Match(sel.Groups["nc"].Value.Trim());
                if (!nc.Success) continue;
                int a, b;
                if (nc.Groups["lit"].Success) { a = 0; b = int.Parse(nc.Groups["lit"].Value); }
                else
                {
                    var av = nc.Groups["a"].Value;
                    a = av.Length == 0 ? 1 : av == "-" ? -1 : int.Parse(av);
                    b = nc.Groups["b"].Success
                        ? int.Parse(nc.Groups["b"].Value) * (nc.Groups["sign"].Value == "-" ? -1 : 1)
                        : 0;
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
        foreach (var m in markers)
            if (m.ContainerClass is null || System.Array.IndexOf(classes, m.ContainerClass) >= 0)
                (hits ??= new()).Add(m);
        return hits;
    }
}
