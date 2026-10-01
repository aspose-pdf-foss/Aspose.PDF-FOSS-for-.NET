using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

/// <summary>What the stylesheet scan asks of the markup, read ONCE per document
/// instead of once per selector: which tag names it opens, which class names its
/// class attributes carry and on what tag, whether an id occurs, and how deep in
/// tables a position stands.
///
/// The scan used to run a fresh whole-document regex for every one of a sheet's
/// selectors -- thousands of them on a 1 MB Angular page -- and then, for every
/// class use found, another regex over the prefix of the document to count the
/// tables around it. On the .NET Framework regex interpreter that came to more
/// than half an hour for one conversion (the same page converts in 13 s on .NET
/// 10). The answers are the same ones the regexes gave; only the work is shared.</summary>
internal sealed class MarkupIndex
{
    private static readonly ConditionalWeakTable<string, MarkupIndex> Cache = new();

    /// <summary>A tag opening exactly at the match start.</summary>
    private static readonly Regex TagOpen = new(@"\G<(\w+)\b");

    /// <summary>The index of this markup, built on first use and kept with the string.</summary>
    internal static MarkupIndex For(string html) => Cache.GetValue(html, h => new MarkupIndex(h));

    /// <summary>One <c>class="…"</c> attribute: the tag that carries it (empty when the
    /// attribute stands outside any tag), where the tag opens, and the value.</summary>
    internal readonly record struct ClassUse(string Tag, int Index, string Value);

    private readonly string _html;
    private readonly List<ClassUse> _classUses = new();
    private readonly Dictionary<string, List<int>> _usesByToken = new(StringComparer.Ordinal);
    private readonly HashSet<string> _tags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<int>> _usesOfClass = new(StringComparer.Ordinal);
    private readonly int[] _tableOpens;
    private readonly int[] _tableCloses;

    private MarkupIndex(string html)
    {
        _html = html;
        // Every class attribute, with the tag it stands in when one opens before
        // it on the same tag (a `class=` inside a script or a stylesheet has none).
        foreach (Match m in Regex.Matches(html, @"class\s*=\s*[""']([^""']*)", RegexOptions.IgnoreCase))
        {
            var value = m.Groups[1].Value;
            var lt = html.LastIndexOf('<', m.Index);
            var gt = lt >= 0 ? html.IndexOf('>', lt) : -1;
            var tag = string.Empty;
            var index = m.Index;
            if (lt >= 0 && (gt < 0 || gt > m.Index))
            {
                var tagM = TagOpen.Match(html, lt, Math.Min(html.Length - lt, 64));
                if (tagM.Success) { tag = tagM.Groups[1].Value; index = lt; }
            }
            var at = _classUses.Count;
            _classUses.Add(new ClassUse(tag, index, value));
            foreach (var token in Regex.Split(value.ToLowerInvariant(), @"\W+"))
            {
                if (token.Length == 0) continue;
                if (!_usesByToken.TryGetValue(token, out var list)) _usesByToken[token] = list = new List<int>();
                if (list.Count == 0 || list[^1] != at) list.Add(at);
            }
        }
        foreach (Match m in Regex.Matches(html, @"<(\w+)")) _tags.Add(m.Groups[1].Value.ToLowerInvariant());
        var opens = new List<int>();
        var closes = new List<int>();
        foreach (Match m in Regex.Matches(html, @"<(/?)table\b", RegexOptions.IgnoreCase))
            (m.Groups[1].Value.Length == 0 ? opens : closes).Add(m.Index);
        _tableOpens = opens.ToArray();
        _tableCloses = closes.ToArray();
    }

    /// <summary>True when the document opens a tag of that name: what
    /// <c>&lt;tag\b</c> matched, case-blind.</summary>
    internal bool HasTag(string tag) => _tags.Contains(tag.ToLowerInvariant());

    /// <summary>True when an id attribute starts with that id at a word boundary:
    /// what <c>id\s*=\s*["']?ID\b</c> matched, case-blind; memoised per id.</summary>
    internal bool HasId(string id)
    {
        if (_ids.TryGetValue(id, out var known)) return known;
        return _ids[id] = Regex.IsMatch(_html, @"id\s*=\s*[""']?" + Regex.Escape(id) + @"\b", RegexOptions.IgnoreCase);
    }

    /// <summary>True when some class attribute carries the class as a word: what
    /// <c>class\s*=\s*["'][^"']*\bCLS\b</c> matched, case-blind.</summary>
    internal bool HasClass(string cls) => UsesOf(cls).Count > 0;

    /// <summary>The class attributes carrying the class as a word, each with its
    /// tag and position -- what <c>&lt;(\w+)\b[^&gt;]*class\s*=\s*["'][^"']*\bCLS\b</c>
    /// matched, case-blind, tagless uses left out.</summary>
    internal IEnumerable<ClassUse> TaggedUsesOf(string cls)
    {
        foreach (var i in UsesOf(cls))
            if (_classUses[i].Tag.Length > 0) yield return _classUses[i];
    }

    /// <summary>How many tables are open at a position: the <c>&lt;table</c> tags
    /// before it less the <c>&lt;/table</c> tags before it.</summary>
    internal bool InsideTable(int position) => CountBefore(_tableOpens, position) - CountBefore(_tableCloses, position) > 0;

    private static int CountBefore(int[] sorted, int position)
    {
        var at = Array.BinarySearch(sorted, position);
        return at >= 0 ? at : ~at;
    }

    private List<int> UsesOf(string cls)
    {
        if (_usesOfClass.TryGetValue(cls, out var found)) return found;
        var word = new Regex(@"\b" + Regex.Escape(cls) + @"\b", RegexOptions.IgnoreCase);
        // A class name opening on a word character can only match where its first
        // word stands as a token of the value; any other shape checks every value.
        var firstToken = Regex.Match(cls.ToLowerInvariant(), @"^\w+").Value;
        IEnumerable<int> candidates = firstToken.Length > 0
            ? _usesByToken.TryGetValue(firstToken, out var byToken) ? byToken : Array.Empty<int>()
            : Enumerable.Range(0, _classUses.Count);
        var uses = new List<int>();
        foreach (var i in candidates)
            if (word.IsMatch(_classUses[i].Value)) uses.Add(i);
        return _usesOfClass[cls] = uses;
    }
}
