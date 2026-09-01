using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stylesheet-scan predicates, lifted out of ConvertFromHtml: each takes
// the source markup and the scan flags it reads. Bodies are verbatim.
    private static bool SelectorUsed(string html, string sel)
    {
        var last = sel.Trim();
        var sp = last.LastIndexOfAny(new[] { ' ', '>', '+', '~' });
        if (sp >= 0) last = last[(sp + 1)..].Trim();
        if (last.Length == 0) return true;
        if (last[0] == '.')
            return Regex.IsMatch(html,
                @"class\s*=\s*[""'][^""']*\b" + Regex.Escape(last[1..]) + @"\b",
                RegexOptions.IgnoreCase);
        // tag.class — the class decides presence: "br.altova-page-break"
        // matches only elements CARRYING the class, so a class nobody uses
        // cannot disqualify the flow no matter how common the tag is.
        if (last.IndexOf('.') > 0)
        {
            var cls = last[(last.IndexOf('.') + 1)..].Split('.')[0];
            return cls.Length > 0 && Regex.IsMatch(html,
                @"class\s*=\s*[""'][^""']*\b" + Regex.Escape(cls) + @"\b",
                RegexOptions.IgnoreCase);
        }
        if (last[0] == '#')
            return Regex.IsMatch(html,
                @"id\s*=\s*[""']?" + Regex.Escape(last[1..]) + @"\b",
                RegexOptions.IgnoreCase);
        var tagOnly = Regex.Match(last, @"^[A-Za-z][A-Za-z0-9]*").Value;
        if (tagOnly.Length == 0) return true;
        return Regex.IsMatch(html, @"<" + Regex.Escape(tagOnly) + @"\b", RegexOptions.IgnoreCase);
    }

    private static bool TableScopedSelector(string html, ConvertState cv, bool bodyAllTables, bool edgeToEdgePre, string sel, IReadOnlyDictionary<string, string> decls)
    {
        // Authored-margin documents (beyond the edge-to-edge zero-margin
        // dialect) were calibrated on the legacy flow — their table skins
        // must keep disqualifying it.
        if (cv.marginsExplicit && !edgeToEdgePre) return false;
        sel = sel.Trim();
        var selParts = sel.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (selParts.Length == 0) return false;
        var last = selParts[^1];
        var lastTag = last.Split('.')[0].ToLowerInvariant();
        if (lastTag is "table" or "td" or "th" or "tr" or "img") return true;
        string? scopeCls = null;
        if (last.StartsWith('.'))
            scopeCls = last[1..].Split('.')[0];
        // ".rc6 div" — a div/span/b/p under a table-scoped class ancestor is
        // itself table content in an all-table body.
        else if (selParts.Length > 1 && selParts[0].StartsWith('.') && bodyAllTables
            && lastTag is "div" or "span" or "b" or "p")
            scopeCls = selParts[0][1..].Split('.')[0];
        if (scopeCls is null || scopeCls.Length == 0) return false;
        var clsUses = Regex.Matches(html,
            @"<(\w+)\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(scopeCls) + @"\b",
            RegexOptions.IgnoreCase);
        if (clsUses.Count == 0) return false;
        // A div/b/span/p carrying the class is table content only when it
        // sits INSIDE a table (the boleto's in-cell skins); a wrapper div
        // AROUND the tables (the official-letter .Content) keeps its
        // calibrated flow.
        bool InsideTable(int pos)
        {
            var depth = 0;
            foreach (Match tm in Regex.Matches(html[..pos], @"<(/?)table\b",
                RegexOptions.IgnoreCase))
                depth += tm.Groups[1].Value.Length == 0 ? 1 : -1;
            return depth > 0;
        }
        return clsUses.All(u => u.Groups[1].Value.ToLowerInvariant()
                is "table" or "td" or "th" or "tr" or "tbody" or "thead" or "tfoot"
            || (u.Groups[1].Value.ToLowerInvariant() is "div" or "b" or "span" or "p"
                && InsideTable(u.Index)));
    }

    private static bool ImgScopedClass(string html, string cls)
    {
        var uses = Regex.Matches(html,
            @"<([a-zA-Z]+)\b[^>]*class\s*=\s*[""'][^""']*\b" + Regex.Escape(cls) + @"\b",
            RegexOptions.IgnoreCase);
        if (uses.Count == 0) return false;
        foreach (Match u in uses)
            if (!u.Groups[1].Value.Equals("img", StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static bool InlineFamiliesDisqualify(ConvertState cv, bool cssLayoutFree, string htmlSansTables)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match sm in Regex.Matches(htmlSansTables,
                     @"\bstyle\s*=\s*(?:""(?<s>[^""]*)""|'(?<s>[^']*)')",
                     RegexOptions.IgnoreCase))
        {
            if (Regex.Match(sm.Groups["s"].Value,
                    @"font-family\s*:\s*(?<f>[^;]*)",
                    RegexOptions.IgnoreCase) is not { Success: true } fam)
                continue;
            var ownerLt = htmlSansTables.LastIndexOf('<', sm.Index);
            var ownerTag = ownerLt >= 0
                ? Regex.Match(htmlSansTables.Substring(ownerLt + 1,
                        Math.Min(8, htmlSansTables.Length - ownerLt - 1)),
                        @"^[a-zA-Z]+").Value.ToLowerInvariant()
                : "";
            if (ownerTag is "td" or "th" or "tr" or "table" or "tbody")
                return true;
            // A family that fails to parse — or an EMPTY declaration
            // ("font-family:" with nothing after it, the Word-export idiom) —
            // disqualifies like any other face.
            if (FirstFontFamily(fam.Groups["f"].Value) is not { } inlFam
                || string.IsNullOrWhiteSpace(inlFam))
                return true;
            if (inlFam.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase))
                continue;   // the UA base face styles nothing new
            // A family declared WITH its own typography (a size or pitch in
            // the same style) is the statement idiom the calibrated flow was
            // measured on; a family declared ALONE is a candidate face swap.
            if (Regex.IsMatch(sm.Groups["s"].Value, @"font-size|font\s*:|line-height",
                    RegexOptions.IgnoreCase))
                return true;
            seen.Add(inlFam);
        }
        if (seen.Count == 0) return false;
        // …and only in a document with NO layout stylesheet: a sheet that
        // positions content (the Thai statement's mso block) marks the
        // calibrated corpus even when its flow spells one bare family.
        if (seen.Count == 1 && cssLayoutFree)
        {
            cv.singleFamilyFaceSwap = true;
            return false;   // one bare family everywhere: a face swap keeps UA structure
        }
        return true;
    }
}
