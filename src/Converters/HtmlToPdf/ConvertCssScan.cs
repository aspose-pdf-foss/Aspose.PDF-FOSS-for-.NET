using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stylesheet-scan predicates, lifted out of ConvertFromHtml: each takes
// the source markup and the scan flags it reads. Bodies are verbatim.
    private static bool SelectorUsed(string html, string sel)
    {
        // (the markup is read once for every selector's question, see MarkupIndex)
        var markup = MarkupIndex.For(html);
        var last = sel.Trim();
        var sp = last.LastIndexOfAny(new[] { ' ', '>', '+', '~' });
        if (sp >= 0) last = last[(sp + 1)..].Trim();
        if (last.Length == 0) return true;
        if (last[0] == '.') return markup.HasClass(last[1..]);
        // tag.class — the class decides presence: "br.altova-page-break"
        // matches only elements CARRYING the class, so a class nobody uses
        // cannot disqualify the flow no matter how common the tag is.
        if (last.IndexOf('.') > 0)
        {
            var cls = last[(last.IndexOf('.') + 1)..].Split('.')[0];
            return cls.Length > 0 && markup.HasClass(cls);
        }
        if (last[0] == '#') return markup.HasId(last[1..]);
        var tagOnly = Regex.Match(last, @"^[A-Za-z][A-Za-z0-9]*").Value;
        if (tagOnly.Length == 0) return true;
        return markup.HasTag(tagOnly);
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
        // (a pseudo-class on the tail - `th:first-child` - names the same table part)
        var lastTag = last.Split('.', ':')[0].ToLowerInvariant();
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
        var markup = MarkupIndex.For(html);
        var clsUses = markup.TaggedUsesOf(scopeCls).ToList();
        if (clsUses.Count == 0) return false;
        // A div/b/span/p carrying the class is table content only when it
        // sits INSIDE a table (the boleto's in-cell skins); a wrapper div
        // AROUND the tables (the official-letter .Content) keeps its
        // calibrated flow.
        return clsUses.All(u => u.Tag.ToLowerInvariant()
                is "table" or "td" or "th" or "tr" or "tbody" or "thead" or "tfoot"
            || (u.Tag.ToLowerInvariant() is "div" or "b" or "span" or "p"
                && markup.InsideTable(u.Index)));
    }

    private static bool ImgScopedClass(string html, string cls)
    {
        var uses = MarkupIndex.For(html).TaggedUsesOf(cls).ToList();
        if (uses.Count == 0) return false;
        foreach (var u in uses)
            if (!u.Tag.Equals("img", StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    /// <summary>A table cell whose inline style declares both its family and its line box.</summary>
    private static bool CellStylesAuthorTypography(string html)
    {
        foreach (Match cm in Regex.Matches(html,
                     @"<t[dh]\b[^>]*?style\s*=\s*(?:""(?<s>[^""]*)""|'(?<s>[^']*)')",
                     RegexOptions.IgnoreCase))
        {
            var st = cm.Groups["s"].Value;
            if (Regex.IsMatch(st, @"font-family\s*:", RegexOptions.IgnoreCase)
                && Regex.IsMatch(st, @"line-height\s*:", RegexOptions.IgnoreCase))
                return true;
        }
        return false;
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
            // the body tag's own attribute IS the flow's typography when the UA body face was read off it
            if (ownerTag is "body" && cv.uaBodyFaceFromAttr) continue;
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
            {
                // …and the flow this document is pushed ONTO must apply the very
                // typography that pushed it there: the span draws at its own
                // declared size (probed: a 22 pt span lands 22 pt at the UA body
                // top, not at the calibrated flow's 11 pt default).
                cv.profile.inlineSpanTypography = true;
                // A sized span in a face the flow can draw is a RUN of the UA flow
                // (the numbered-outline fragment's arial 13px paragraphs draw arial
                // 9.75 pt in the expected render); a face the flow has not got, or a
                // declaration that also states its PITCH (a verdana 9px line-height:2
                // paragraph beside its table), pushes the document onto the calibrated
                // flow as before - a run carries a face and a size, not a line box.
                if (WinMetricsFor(inlFam) is null
                    || Regex.IsMatch(sm.Groups["s"].Value, @"line-height", RegexOptions.IgnoreCase)) return true;
                cv.profile.inlineRunFaces = true;
                continue;
            }
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
