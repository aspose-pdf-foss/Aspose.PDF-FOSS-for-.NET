using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// THE STYLED SHEET: the float-box formatter's third claim - a standards-mode page whose linked
// stylesheet declares its own faces (@font-face programs beside the page) and lays the page out
// by media features (a responsive site's factsheet). The reference converter (probed on
// synthetic fixtures) reads the sheet as media type PRINT in a real feature context - width and
// height are the page's content box in CSS px, the device 1024 x 768 - keeps a query's block in
// its source-order place when it evaluates true, cascades by specificity then source order,
// renders ::before / ::after content (attr() included), embeds the @font-face programs and sets
// the text in them, and lays the kept rules out as the same block / float / table flow.
internal static partial class HtmlToPdfConverter
{
    private const double FbRootFontPx = 16.0;                 // 1 rem
    private const double FbDeviceWidthPx = 1024.0;            // the media context's device (probed: fixed)
    private const double FbDeviceHeightPx = 768.0;
    private const int FbSpecId = 100, FbSpecClass = 10, FbSpecTag = 1;

    /// <summary>One compound of a selector and its relation to the compound on its left.</summary>
    private sealed class FbSelPart
    {
        public char Comb;                                     // '\0' first, ' ' descendant, '>' child, '+' adjacent, '~' sibling
        public string? Tag;                                   // null = any
        public string? Id;
        public List<string>? Classes;
        public List<(string name, string op, string val)>? Attrs;
        public List<string>? Pseudos;                         // pseudo-classes, lower case, with their argument
    }

    private sealed class FbSheetRule
    {
        public List<FbSelPart> Parts = null!;
        public string? PseudoElement;                         // "before" / "after"; null = the element itself
        public bool Unsupported;                              // a pseudo the formatter cannot match: the rule never applies
        public Dictionary<string, string> Decls = null!;
        public Dictionary<string, string>? Important;
        public int Spec, Order;
    }

    /// <summary>A font program the sheet ships: measured with its own advances and kerning, embedded on first use.</summary>
    private sealed class FbFace
    {
        public string Family = "";
        public byte[] Ttf = null!;
        public Text.GlyphOutlineParser Glyphs = null!;
        public Text.TrueTypeParser Metrics = null!;
        public string Res = "";
        public Core.PdfIndirectRef? FontRef;
        public HashSet<int> Pages = new();
    }

    private sealed class FbSheet
    {
        public List<FbSheetRule> Rules = new();
        public Dictionary<string, FbFace> Faces = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<HtmlNode, Dictionary<string, string>> Computed = new();
        public Dictionary<HtmlNode, Dictionary<string, string>> Generated = new();   // a ::before / ::after node's declarations
        public HashSet<HtmlNode> GeneratedDone = new();
        public HtmlLoadOptions? Options;
        public Dictionary<string, (int objNum, string embedName)> FontFileCache = new();
    }

    /// <summary>Why the styled-sheet claim declines a document, or null when it claims it: standards
    /// mode, no explicit margins, a sheet that declares its own faces from files beside the page and
    /// lays out by width media features, no frames.</summary>
    private static string? FbSheetDeclines(ConvertState cv, HtmlLoadOptions? options)
    {
        var html = cv.html;
        if (cv.marginsExplicit) return "margins";
        if (options is { HtmlMediaType: HtmlMediaType.Screen }) return "screen media";   // (the law is the PRINT context)
        if (ReadsInQuirksMode(html) && !ReadsInLimitedQuirks(html)) return "quirks";
        if (Regex.IsMatch(html, @"<(frameset|iframe|svg)\b", RegexOptions.IgnoreCase)) return "frames";
        if (!Regex.IsMatch(html, @"@font-face\s*\{[^}]*url\(\s*[""']?(?!https?:|data:)[^)]*\.(ttf|woff|otf)", RegexOptions.IgnoreCase)) return "no own faces";
        if (!Regex.IsMatch(html, @"@media[^{]*\(\s*max-width\s*:", RegexOptions.IgnoreCase)) return "no width features";
        return null;
    }

    // ── the media context ────────────────────────────────────────────────────────────────────

    /// <summary>Does a media query hold in the print context? Types: print / all (and `not screen`) yes,
    /// anything else no; a comma list holds when one term does; width / height are the viewport, device
    /// sizes the fixed device, orientation landscape; colour, grid, scan and resolution always hold;
    /// monochrome, aspect ratios, hover, pointer, preferences, vendor and unknown features never; the
    /// numeral of a value is read as px whatever its unit.</summary>
    private static bool FbMediaApplies(string query, double vpW, double vpH)
    {
        query = query.Trim();
        if (query.Length == 0) return true;
        foreach (var termRaw in query.Split(','))
        {
            var term = termRaw.Trim().ToLowerInvariant();
            if (term.Length == 0) continue;
            var negate = false;
            if (term.StartsWith("only ")) term = term[5..].Trim();
            if (term.StartsWith("not ")) { negate = true; term = term[4..].Trim(); }
            var holds = true;
            foreach (var partRaw in Regex.Split(term, @"\s+and\s+"))
            {
                var part = partRaw.Trim();
                if (part.Length == 0) continue;
                if (part[0] != '(') { holds &= part is "print" or "all"; continue; }
                holds &= FbMediaFeatureHolds(part.Trim('(', ')').Trim(), vpW, vpH);
                if (!holds) break;
            }
            if (negate ? !holds : holds) return true;
        }
        return false;
    }

    private static bool FbMediaFeatureHolds(string feature, double vpW, double vpH)
    {
        var colon = feature.IndexOf(':');
        var name = (colon < 0 ? feature : feature[..colon]).Trim();
        var value = colon < 0 ? "" : feature[(colon + 1)..].Trim();
        var num = Regex.Match(value, @"-?\d+(?:\.\d+)?");
        var n = num.Success ? double.Parse(num.Value, System.Globalization.CultureInfo.InvariantCulture) : double.NaN;
        var prefix = name.StartsWith("min-") ? "min" : name.StartsWith("max-") ? "max" : "";
        var bare = prefix.Length > 0 ? name[4..] : name;
        double? measured = bare switch
        {
            "width" => vpW, "height" => vpH,
            "device-width" => FbDeviceWidthPx, "device-height" => FbDeviceHeightPx,
            _ => null,
        };
        if (measured is { } m)
        {
            if (double.IsNaN(n)) return prefix.Length == 0;      // a bare `(width)`
            return prefix switch { "min" => m >= n - FbEpsilon, "max" => m <= n + FbEpsilon, _ => Math.Abs(m - n) < FbEpsilon };
        }
        return bare switch
        {
            "orientation" => value == "landscape",
            "color" or "grid" or "scan" or "resolution" => true,
            _ => false,
        };
    }

    // ── reading the sheet ────────────────────────────────────────────────────────────────────

    /// <summary>Read every style block of the document (a linked sheet is inlined with its media
    /// attribute) into the rule list and the face table, in source order.</summary>
    private static FbSheet FbReadSheet(string html, HtmlLoadOptions? options, double vpW, double vpH)
    {
        var sheet = new FbSheet { Options = options };
        var order = 0;
        foreach (Match block in Regex.Matches(html, @"<style\b([^>]*)>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var mediaAttr = Regex.Match(block.Groups[1].Value, @"\bmedia\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (mediaAttr.Success && !FbMediaApplies(mediaAttr.Groups[1].Value, vpW, vpH)) continue;
            var css = Regex.Replace(block.Groups[2].Value, @"/\*[\s\S]*?\*/", "");
            css = Regex.Replace(css, @"@(import|charset)\b[^;]*;", "", RegexOptions.IgnoreCase);
            FbReadCssBlock(sheet, css, ref order, vpW, vpH);
        }
        return sheet;
    }

    private static void FbReadCssBlock(FbSheet sheet, string css, ref int order, double vpW, double vpH)
    {
        var i = 0;
        while (i < css.Length)
        {
            var brace = css.IndexOf('{', i);
            if (brace < 0) break;
            var head = css[i..brace].Trim();
            var end = FbBlockEnd(css, brace);
            var body = css[(brace + 1)..end];
            i = end + 1;
            if (head.StartsWith("@media", StringComparison.OrdinalIgnoreCase))
            {
                if (FbMediaApplies(head[6..], vpW, vpH)) FbReadCssBlock(sheet, body, ref order, vpW, vpH);
                continue;
            }
            if (head.StartsWith("@font-face", StringComparison.OrdinalIgnoreCase)) { FbReadFontFace(sheet, body); continue; }
            if (head.StartsWith("@", StringComparison.Ordinal)) continue;     // keyframes, page, supports: not laid out
            var (decls, important) = FbParseDecls(body);
            if (decls.Count == 0 && important is null) continue;
            foreach (var selRaw in head.Split(','))
            {
                var sel = selRaw.Trim();
                if (sel.Length == 0) continue;
                var rule = FbParseSelector(sel);
                if (rule is null) continue;
                rule.Decls = decls;
                rule.Important = important;
                rule.Order = order++;
                sheet.Rules.Add(rule);
            }
        }
    }

    /// <summary>The index of the brace closing the block opened at <paramref name="open"/>.</summary>
    private static int FbBlockEnd(string css, int open)
    {
        var depth = 0;
        for (var j = open; j < css.Length; j++)
        {
            if (css[j] == '{') depth++;
            else if (css[j] == '}' && --depth == 0) return j;
        }
        return css.Length;
    }

    /// <summary>The declarations of a rule body: name to value, later declarations winning; the
    /// `!important` ones kept apart.</summary>
    private static (Dictionary<string, string> decls, Dictionary<string, string>? important) FbParseDecls(string body)
    {
        var decls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? important = null;
        foreach (var d in FbSplitDecls(body))
        {
            var colon = d.IndexOf(':');
            if (colon <= 0) continue;
            var name = d[..colon].Trim().ToLowerInvariant();
            var value = d[(colon + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0) continue;
            var imp = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
            if (imp >= 0) { (important ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))[name] = value[..imp].Trim(); continue; }
            decls[name] = value;
        }
        return (decls, important);
    }

    /// <summary>Split a rule body on the semicolons outside quotes and parentheses.</summary>
    private static List<string> FbSplitDecls(string body)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var depth = 0; var quote = '\0';
        foreach (var ch in body)
        {
            if (quote != '\0') { sb.Append(ch); if (ch == quote) quote = '\0'; continue; }
            if (ch is '"' or '\'') { quote = ch; sb.Append(ch); continue; }
            if (ch == '(') depth++; else if (ch == ')') depth--;
            if (ch == ';' && depth <= 0) { parts.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }

    /// <summary>Parse one selector into its compounds; null when it is not a selector at all.</summary>
    private static FbSheetRule? FbParseSelector(string sel)
    {
        var rule = new FbSheetRule { Parts = new List<FbSelPart>() };
        var part = new FbSelPart();
        var i = 0;
        var pendingComb = '\0';
        void Close()
        {
            if (part.Tag is null && part.Id is null && part.Classes is null && part.Attrs is null && part.Pseudos is null) return;
            rule.Parts.Add(part);
            part = new FbSelPart();
        }
        while (i < sel.Length)
        {
            var c = sel[i];
            if (char.IsWhiteSpace(c) || c is '>' or '+' or '~')
            {
                var comb = ' ';
                while (i < sel.Length && (char.IsWhiteSpace(sel[i]) || sel[i] is '>' or '+' or '~'))
                {
                    if (sel[i] is '>' or '+' or '~') comb = sel[i];
                    i++;
                }
                Close();
                pendingComb = comb;
                if (rule.Parts.Count > 0) part.Comb = pendingComb;
                continue;
            }
            if (c == '*') { i++; continue; }
            if (c == '#') { var j = FbIdentEnd(sel, i + 1); part.Id = sel[(i + 1)..j]; i = j; continue; }
            if (c == '.') { var j = FbIdentEnd(sel, i + 1); (part.Classes ??= new List<string>()).Add(sel[(i + 1)..j]); i = j; continue; }
            if (c == '[')
            {
                var j = sel.IndexOf(']', i);
                if (j < 0) return null;
                var m = Regex.Match(sel[(i + 1)..j], @"^\s*([\w-]+)\s*(?:([~|^$*]?=)\s*[""']?([^""']*?)[""']?)?\s*$");
                if (!m.Success) return null;
                (part.Attrs ??= new List<(string, string, string)>()).Add((m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value, m.Groups[3].Value));
                i = j + 1;
                continue;
            }
            if (c == ':')
            {
                var dbl = i + 1 < sel.Length && sel[i + 1] == ':';
                var j = FbIdentEnd(sel, i + (dbl ? 2 : 1));
                var name = sel[(i + (dbl ? 2 : 1))..j].ToLowerInvariant();
                var arg = "";
                if (j < sel.Length && sel[j] == '(')
                {
                    var k = sel.IndexOf(')', j);
                    if (k < 0) return null;
                    arg = sel[(j + 1)..k].Trim();
                    j = k + 1;
                }
                i = j;
                if (name is "before" or "after") { rule.PseudoElement = name; continue; }
                if (name is "first-line" or "first-letter" or "selection" or "placeholder") { rule.Unsupported = true; continue; }
                (part.Pseudos ??= new List<string>()).Add(arg.Length > 0 ? name + "(" + arg + ")" : name);
                continue;
            }
            if (char.IsLetter(c))
            {
                var j = FbIdentEnd(sel, i);
                part.Tag = sel[i..j].ToLowerInvariant();
                i = j;
                continue;
            }
            return null;                                         // not a selector this formatter reads
        }
        Close();
        if (rule.Parts.Count == 0) return null;
        foreach (var p in rule.Parts)
        {
            if (p.Id is not null) rule.Spec += FbSpecId;
            rule.Spec += ((p.Classes?.Count ?? 0) + (p.Attrs?.Count ?? 0) + (p.Pseudos?.Count ?? 0)) * FbSpecClass;
            if (p.Tag is not null) rule.Spec += FbSpecTag;
        }
        if (rule.PseudoElement is not null) rule.Spec += FbSpecTag;
        return rule;
    }

    private static int FbIdentEnd(string s, int from)
    {
        var j = from;
        while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] is '-' or '_' || s[j] > 127)) j++;
        return j;
    }

    /// <summary>An @font-face block: its family and the first program the page's folder holds (a
    /// WOFF is unwrapped; EOT, SVG and CFF programs are passed over).</summary>
    private static void FbReadFontFace(FbSheet sheet, string body)
    {
        var (decls, _) = FbParseDecls(body);
        if (!decls.TryGetValue("font-family", out var famRaw) || !decls.TryGetValue("src", out var src)) return;
        var family = famRaw.Trim().Trim('"', '\'');
        if (family.Length == 0 || sheet.Faces.ContainsKey(family)) return;
        foreach (Match u in Regex.Matches(src, @"url\(\s*[""']?([^)""']+?)[""']?\s*\)", RegexOptions.IgnoreCase))
        {
            var url = u.Groups[1].Value.Trim();
            var lower = url.ToLowerInvariant();
            if (lower.Contains(".eot") || lower.Contains(".svg") || lower.StartsWith("http:") || lower.StartsWith("https:")) continue;
            var bytes = LoadConverterImage(url, sheet.Options);
            if (bytes is null || bytes.Length < 12) continue;
            var sfnt = TryReadWoff(bytes) ?? (bytes[0] == 0x00 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00 ? bytes : null);
            if (sfnt is null) continue;
            try
            {
                var metrics = new Text.TrueTypeParser(sfnt);
                metrics.Parse();
                var face = new FbFace { Family = family, Ttf = sfnt, Glyphs = new Text.GlyphOutlineParser(sfnt), Metrics = metrics, Res = "FF" + (sheet.Faces.Count + 1) };
                sheet.Faces[family] = face;
                return;
            }
            catch { /* an unreadable program: try the next source */ }
        }
    }

    // ── matching and the cascade ─────────────────────────────────────────────────────────────

    private static bool FbPartMatches(FbSelPart p, HtmlNode el)
    {
        if (el.Tag.Length == 0) return false;
        if (p.Tag is not null && !p.Tag.Equals(el.Tag, StringComparison.OrdinalIgnoreCase)) return false;
        if (p.Id is not null && !(el.Attrs is not null && el.Attrs.TryGetValue("id", out var id) && id.Trim() == p.Id)) return false;
        if (p.Classes is not null)
        {
            if (el.Attrs is null || !el.Attrs.TryGetValue("class", out var cls)) return false;
            var have = cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var c in p.Classes) if (Array.IndexOf(have, c) < 0) return false;
        }
        if (p.Attrs is not null)
            foreach (var (name, op, val) in p.Attrs)
            {
                if (el.Attrs is null || !el.Attrs.TryGetValue(name, out var v)) return false;
                var ok = op switch
                {
                    "" => true,
                    "=" => v == val,
                    "~=" => Array.IndexOf(v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), val) >= 0,
                    "|=" => v == val || v.StartsWith(val + "-", StringComparison.Ordinal),
                    "^=" => val.Length > 0 && v.StartsWith(val, StringComparison.Ordinal),
                    "$=" => val.Length > 0 && v.EndsWith(val, StringComparison.Ordinal),
                    "*=" => val.Length > 0 && v.Contains(val, StringComparison.Ordinal),
                    _ => false,
                };
                if (!ok) return false;
            }
        if (p.Pseudos is not null)
            foreach (var ps in p.Pseudos)
                if (!FbPseudoClassMatches(ps, el)) return false;
        return true;
    }

    /// <summary>The structural pseudo-classes the formatter evaluates; the dynamic ones (hover, focus,
    /// visited, checked…) never hold on a printed page.</summary>
    private static bool FbPseudoClassMatches(string ps, HtmlNode el)
    {
        var paren = ps.IndexOf('(');
        var name = paren < 0 ? ps : ps[..paren];
        var arg = paren < 0 ? "" : ps[(paren + 1)..^1].Trim();
        var siblings = FbElementSiblings(el);
        var index = siblings.IndexOf(el);
        switch (name)
        {
            case "first-child": return index == 0;
            case "last-child": return index == siblings.Count - 1;
            case "only-child": return siblings.Count == 1;
            case "first-of-type": return FbTypeIndex(siblings, el) == 0;
            case "last-of-type": return FbTypeIndex(siblings, el) == FbTypeCount(siblings, el) - 1;
            case "nth-child": return FbNthMatches(arg, index + 1);
            case "nth-of-type": return FbNthMatches(arg, FbTypeIndex(siblings, el) + 1);
            case "root": return el.Parent is null || el.Parent.Tag.Length == 0;
            case "empty": return el.Children.Count == 0;
            case "link": return el.Tag == "a" && el.Attrs is not null && el.Attrs.ContainsKey("href");
            case "not":
                var inner = FbParseSelector(arg);
                return inner is { Parts.Count: 1 } && !FbPartMatches(inner.Parts[0], el);
            default: return false;
        }
    }

    private static List<HtmlNode> FbElementSiblings(HtmlNode el)
    {
        var list = new List<HtmlNode>();
        if (el.Parent is null) { list.Add(el); return list; }
        foreach (var c in el.Parent.Children) if (c.Tag.Length > 0) list.Add(c);
        return list;
    }

    private static int FbTypeIndex(List<HtmlNode> siblings, HtmlNode el)
    {
        var k = 0;
        foreach (var s in siblings) { if (ReferenceEquals(s, el)) return k; if (s.Tag == el.Tag) k++; }
        return k;
    }

    private static int FbTypeCount(List<HtmlNode> siblings, HtmlNode el)
    {
        var k = 0;
        foreach (var s in siblings) if (s.Tag == el.Tag) k++;
        return k;
    }

    /// <summary>`an+b`, `odd`, `even` or a number against a 1-based position.</summary>
    private static bool FbNthMatches(string arg, int pos)
    {
        arg = arg.Replace(" ", "").ToLowerInvariant();
        if (arg == "odd") return pos % 2 == 1;
        if (arg == "even") return pos % 2 == 0;
        var m = Regex.Match(arg, @"^(?:(-?\d*)n)?([+-]?\d+)?$");
        if (!m.Success) return false;
        var hasN = arg.Contains('n');
        var a = !hasN ? 0 : m.Groups[1].Value switch { "" or "+" => 1, "-" => -1, var s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture) };
        var b = m.Groups[2].Value.Length > 0 ? int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        if (a == 0) return pos == b;
        var k = pos - b;
        return k % a == 0 && k / a >= 0;
    }

    private static bool FbSelectorMatches(List<FbSelPart> parts, int i, HtmlNode el)
    {
        if (!FbPartMatches(parts[i], el)) return false;
        if (i == 0) return true;
        switch (parts[i].Comb)
        {
            case '>':
                return el.Parent is { Tag.Length: > 0 } parent && FbSelectorMatches(parts, i - 1, parent);
            case '+':
            {
                var sib = FbElementSiblings(el);
                var ix = sib.IndexOf(el);
                return ix > 0 && FbSelectorMatches(parts, i - 1, sib[ix - 1]);
            }
            case '~':
            {
                var sib = FbElementSiblings(el);
                var ix = sib.IndexOf(el);
                for (var k = ix - 1; k >= 0; k--) if (FbSelectorMatches(parts, i - 1, sib[k])) return true;
                return false;
            }
            default:
                for (var p = el.Parent; p is { Tag.Length: > 0 }; p = p.Parent)
                    if (FbSelectorMatches(parts, i - 1, p)) return true;
                return false;
        }
    }

    /// <summary>The element's declarations: every matching rule merged from the least specific
    /// (source order breaking ties), the important ones over them, the inline style over all.</summary>
    private static Dictionary<string, string> FbSheetDecls(FbSheet sheet, HtmlNode el)
    {
        if (sheet.Generated.TryGetValue(el, out var gen)) return gen;
        if (sheet.Computed.TryGetValue(el, out var done)) return done;
        var merged = FbCascade(sheet, el, null);
        if (el.Attrs is not null && el.Attrs.TryGetValue("style", out var inline) && !string.IsNullOrEmpty(inline))
        {
            var (decls, important) = FbParseDecls(inline);
            foreach (var kv in decls) FbMergeDecl(merged, kv.Key, kv.Value);
            if (important is not null) foreach (var kv in important) FbMergeDecl(merged, kv.Key, kv.Value);
        }
        sheet.Computed[el] = merged;
        return merged;
    }

    private static Dictionary<string, string> FbCascade(FbSheet sheet, HtmlNode el, string? pseudoElement)
    {
        var hits = new List<FbSheetRule>();
        foreach (var r in sheet.Rules)
            if (!r.Unsupported && r.PseudoElement == pseudoElement && FbSelectorMatches(r.Parts, r.Parts.Count - 1, el)) hits.Add(r);
        hits.Sort((a, b) => a.Spec != b.Spec ? a.Spec.CompareTo(b.Spec) : a.Order.CompareTo(b.Order));
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in hits) foreach (var kv in r.Decls) FbMergeDecl(merged, kv.Key, kv.Value);
        foreach (var r in hits) if (r.Important is not null) foreach (var kv in r.Important) FbMergeDecl(merged, kv.Key, kv.Value);
        return merged;
    }

    /// <summary>Set one declaration on the merged map: a shorthand that wins later resets the
    /// longhands an earlier rule set (`padding: 0 5%` after `padding-left: 60px` is 5% all round).</summary>
    private static void FbMergeDecl(Dictionary<string, string> merged, string name, string value)
    {
        switch (name)
        {
            case "margin":
            case "padding":
                foreach (var side in new[] { "-top", "-right", "-bottom", "-left" }) merged.Remove(name + side);
                break;
            case "border":
                foreach (var k in new List<string>(merged.Keys)) if (k.StartsWith("border-", StringComparison.Ordinal) && !k.StartsWith("border-radius", StringComparison.Ordinal) && !k.StartsWith("border-collapse", StringComparison.Ordinal) && !k.StartsWith("border-spacing", StringComparison.Ordinal)) merged.Remove(k);
                break;
            case "border-top": case "border-right": case "border-bottom": case "border-left":
                foreach (var part in new[] { "-width", "-style", "-color" }) merged.Remove(name + part);
                break;
            case "background":
                foreach (var k in new List<string>(merged.Keys)) if (k.StartsWith("background-", StringComparison.Ordinal)) merged.Remove(k);
                break;
            case "font":
                foreach (var k in new[] { "font-size", "font-family", "font-weight", "font-style", "line-height" }) merged.Remove(k);
                break;
        }
        merged[name] = value;
    }

    // ── generated content ────────────────────────────────────────────────────────────────────

    /// <summary>Give an element its ::before and ::after boxes once: a pseudo whose `content` is a
    /// string (attr() resolved) becomes a child node carrying that text, styled by the pseudo's rules.</summary>
    private static void FbInsertGenerated(FbState fb, HtmlNode el)
    {
        if (fb.sheet is not { } sheet || el.Tag.Length == 0 || el.Tag.StartsWith("::") || !sheet.GeneratedDone.Add(el)) return;
        foreach (var which in new[] { "before", "after" })
        {
            var decls = FbCascade(sheet, el, which);
            if (!decls.TryGetValue("content", out var content) || FbGeneratedText(content, el) is not { Length: > 0 } text) continue;
            var node = new HtmlNode { Tag = "::" + which, Parent = el };
            node.Children.Add(new HtmlNode { Text = text, Parent = node });
            sheet.Generated[node] = decls;
            if (which == "before") el.Children.Insert(0, node); else el.Children.Add(node);
        }
    }

    /// <summary>A `content` value as text: quoted strings (CSS escapes decoded) and attr() parts
    /// concatenated; null for none / normal / anything else.</summary>
    private static string? FbGeneratedText(string content, HtmlNode el)
    {
        var v = content.Trim();
        if (v.Length == 0 || v.Equals("none", StringComparison.OrdinalIgnoreCase) || v.Equals("normal", StringComparison.OrdinalIgnoreCase)) return null;
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(v, @"""((?:[^""\\]|\\.)*)""|'((?:[^'\\]|\\.)*)'|attr\(\s*([\w-]+)\s*\)"))
        {
            if (m.Groups[3].Success)
            {
                if (el.Attrs is not null && el.Attrs.TryGetValue(m.Groups[3].Value, out var av)) sb.Append(av);
                continue;
            }
            var s = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            sb.Append(Regex.Replace(s, @"\\([0-9a-fA-F]{1,6})\s?|\\(.)", e =>
                e.Groups[1].Success ? char.ConvertFromUtf32(System.Convert.ToInt32(e.Groups[1].Value, 16)) : e.Groups[2].Value));
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    // ── the sheet's faces ────────────────────────────────────────────────────────────────────

    /// <summary>The advance of a text in one of the sheet's faces: the program's own advances and pair kerning.</summary>
    private static double FbMeasureOwn(FbFace face, string text, double pt)
    {
        var g = face.Glyphs;
        double w = 0;
        var prev = 0;
        foreach (var ch in text)
        {
            int cp = ch;
            if (cp == 0xA0 && !g.CMap.ContainsKey(cp)) cp = ' ';
            var gid = g.CMap.TryGetValue(cp, out var id) ? id : 0;
            w += gid == 0 ? 0.5 * pt : g.GetAdvanceWidth(gid) * pt / g.UnitsPerEm;
            if (gid != 0 && prev != 0) w += g.GetKernAdjustment(prev, gid) * pt / g.UnitsPerEm;
            prev = gid;
        }
        return w;
    }

    /// <summary>The metrics of one of the sheet's faces as em fractions: the baseline seats on the OS/2
    /// WIN ascent and descent (probed: the hhea pair does not fit), the normal line is the hhea line.</summary>
    private static (double asc, double desc, double line) FbOwnMetrics(FbFace face)
    {
        var m = face.Metrics;
        double upm = m.UnitsPerEm > 0 ? m.UnitsPerEm : 1000;
        var hheaAsc = m.Ascent > 0 ? m.Ascent / upm : FbSansHheaAsc;
        var hheaDesc = m.Descent != 0 ? Math.Abs(m.Descent) / upm : FbSansHheaDesc;
        var asc = m.UsWinAscent > 0 ? m.UsWinAscent / upm : hheaAsc;
        var desc = m.UsWinDescent > 0 ? m.UsWinDescent / upm : hheaDesc;
        return (asc, desc, hheaAsc + hheaDesc + Math.Max(0, m.LineGap) / upm);
    }

    /// <summary>The resource name one of the sheet's faces draws as on a page: the program is embedded
    /// (WinAnsi TrueType) on the first use in the document and registered on each page that shows it.</summary>
    private static string FbOwnFaceRes(FbState fb, FbFace face, int page)
    {
        if (face.FontRef is null)
        {
            var baseName = face.Metrics.PostScriptName is { Length: > 0 } ps && ps != "Unknown" ? ps.Replace(" ", "") : face.Family.Replace(" ", "");
            var fontDict = new Core.PdfDictionary();
            Text.FontEmbedder.EmbedIntoFontDict(fb.doc, face.Ttf, fontDict, baseName, fb.sheet!.FontFileCache);
            var objNum = fb.doc.AllocateObjectNumber();
            fb.doc.AddNewObject(objNum, fontDict, registerOverlay: true);
            face.FontRef = new Core.PdfIndirectRef(objNum, 0);
        }
        if (page < fb.pageObjs.Count && face.Pages.Add(page)) RegisterPageFont(fb.pageObjs[page], face.Res, face.FontRef);
        return face.Res;
    }
}
