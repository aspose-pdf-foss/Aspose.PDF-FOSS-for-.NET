using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // The typography and table-level declarations a lifted grid takes through its ANCESTOR
    // chain: the sheet's `X table` rules and the face, size and line box the containers and
    // the body above it declare.

    /// <summary>The declarations the sheet's two-part descendant rules (`#id table`, `.cls table`,
    /// `tag table`) make on a grid through its ancestors, innermost ancestor first so its rule wins;
    /// null when none applies.</summary>
    private static Dictionary<string, string>? AncestorTableDecls(TableStyleConfig cfg)
    {
        if (cfg.cssAncestors is not { Count: > 0 }) return null;
        var sheet = cfg.docCss ?? cfg.css;
        Dictionary<string, string>? merged = null;
        void Overlay(string key)
        {
            if (!sheet.TryGetValue(key, out var rule)) return;
            merged ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in rule) if (!merged.ContainsKey(kv.Key)) merged[kv.Key] = kv.Value;
        }
        for (var i = cfg.cssAncestors.Count - 1; i >= 0; i--)
        {
            var a = cfg.cssAncestors[i];
            if (a.Id is { Length: > 0 }) Overlay("#" + a.Id + " table");
            if (a.Classes is not null) foreach (var c in a.Classes) Overlay("." + c + " table");
            Overlay(a.Tag + " table");
        }
        return merged;
    }

    /// <summary>The face, size and line box a grid inherits from the containers above it and the body:
    /// the innermost ancestor that declares them wins, the body rule stands behind all of them, and a
    /// grid whose own rules already sized its cells keeps that size.</summary>
    /// <summary>True when the sheet reaches this grid through its ancestors: a chain rule matches a
    /// plain cell of it, or a two-part `X table` rule addresses the table.</summary>
    private static bool AncestorScopesGrid(TableStyleConfig cfg)
    {
        if (cfg.chainBase is null || cfg.cssAncestors is not { Count: > 0 }) return false;
        if (AncestorTableDecls(cfg) is not null) return true;
        // (a cell rule the table's OWN hooks satisfy is the table's, not the ancestors')
        var bare = new List<CssElem> { cfg.chainBase[^1] };
        foreach (var cellTag in new[] { "td", "th" })
        {
            var cell = new CssElem { Tag = cellTag };
            if (MatchChainDecls(cfg.chainRules, new List<CssElem>(cfg.chainBase) { cell }) is { } withAnc
                && (MatchChainDecls(cfg.chainRules, new List<CssElem>(bare) { cell }) is not { } alone || alone.Count < withAnc.Count))
                return true;
        }
        return false;
    }

    /// <summary>The size a quirks grid that declares none is seated on: its first row group's inline
    /// font-size, else the font-size the sheet's chain rule gives a plain cell of this grid; null when neither.</summary>
    private static double? QuirksGridSizePt(TableStyleConfig cfg, string html)
    {
        var rg = Regex.Match(html, @"<(?:tbody|thead|tfoot)\b[^>]*\bstyle\s*=\s*([""'])(?<v>[^""']*)\1", RegexOptions.IgnoreCase);
        if (rg.Success && Regex.Match(rg.Groups["v"].Value, @"(?<![-\w])font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } rgFs
            && TryParseLength(rgFs.Groups[1].Value.Trim()) is { } rgPt && rgPt > 0)
            return rgPt;
        if (cfg.chainBase is not null
            && MatchChainDecls(cfg.chainRules, new List<CssElem>(cfg.chainBase) { new CssElem { Tag = "td" } }) is { } tdDecls
            && tdDecls.TryGetValue("font-size", out var tdFs) && TryParseLength(tdFs.Trim()) is { } tdPt && tdPt > 0)
            return tdPt;
        return null;
    }

    private static void ApplyAncestorTypography(TableStyleConfig cfg, TableParseState ps, bool cellSizeDeclared, string html)
    {
        // Only a grid the sheet SCOPES through its ancestors - a chain rule on its cells or a
        // two-part rule on the table itself - is the ancestor-styled dialect; a grid the sheet
        // never addresses that way keeps the calibrated measure it was probed with (the report
        // export's class-faced divs must not re-face the tables inside them: its 690 pt sheet was
        // measured without them).
        if (!AncestorScopesGrid(cfg)) return;
        var sheet = cfg.docCss ?? cfg.css;
        string? family = null; double sizePt = 0; double linePt = 0;
        void Take(Dictionary<string, string> rule)
        {
            if (family is null)
            {
                if (rule.TryGetValue("font-family", out var ff) && FirstFontFamily(ff) is { Length: > 0 } fam) family = fam;
                else if (rule.TryGetValue("font", out var fsh) && CssFontShorthandValue(fsh) is { family: { Length: > 0 } shFam }) family = shFam;
            }
            if (sizePt <= 0)
            {
                if (rule.TryGetValue("font-size", out var fs) && TryParseLength(fs) is { } fsPt && fsPt > 0) sizePt = fsPt;
                else if (rule.TryGetValue("font", out var fsh2) && CssFontShorthandValue(fsh2) is { sizePt: > 0 } sh2) sizePt = sh2.sizePt;
            }
            if (linePt <= 0 && rule.TryGetValue("line-height", out var lh))
            {
                var v = lh.Trim();
                if (v.EndsWith("%", StringComparison.Ordinal)
                    && double.TryParse(v.TrimEnd('%'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct > 0)
                    linePt = -pct / 100.0;   // a factor of the size, resolved once the size is known
                else if (TryParseLength(v) is { } lhPt && lhPt > 0) linePt = lhPt;
                else if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f) && f > 0) linePt = -f;
            }
        }
        for (var i = cfg.cssAncestors!.Count - 1; i >= 0; i--)
        {
            var a = cfg.cssAncestors[i];
            if (a.Id is { Length: > 0 } && sheet.TryGetValue("#" + a.Id, out var idRule)) Take(idRule);
            if (a.Classes is not null)
                foreach (var c in a.Classes)
                {
                    if (sheet.TryGetValue(a.Tag + "." + c, out var tagClsRule)) Take(tagClsRule);
                    if (sheet.TryGetValue("." + c, out var clsRule)) Take(clsRule);
                }
        }
        if (sheet.TryGetValue("body", out var bodyRule)) Take(bodyRule);
        if (family is not null && WinMetricsFor(family) is not null)
        {
            ps.cellFamily ??= family;
            cfg.cssBaseFamily ??= family;
            // …and the grid DRAWS in it: the inherited face is the cells' face where the
            // dialect supplied none (measured: the wrapper's Arial, the body's Verdana).
            cfg.defaultCellFace ??= family;
        }
        // A quirks-mode table (no doctype) resets the size and line box it would inherit - only
        // the face comes through (measured: a `BODY { LINE-HEIGHT: 21px }` sheet's TH row stands 9 pt
        // tall at its 10 px size; a `.datagrid { font: 12px/150% Arial }` wrapper's rows pitch on the
        // normal line of the sizes the cell rules declare). Such a grid's own size - the box its
        // rows are seated on - is the size its first row group declares inline, else the size the
        // sheet's cell rule gives its cells (measured: the 10 px TBODY's rows stand 9 pt tall).
        if (_quirksRowStrut)
        {
            if (!cellSizeDeclared && QuirksGridSizePt(cfg, html) is { } qPt && qPt > 0) cfg.cellFontSize = qPt;
            return;
        }
        if (!cellSizeDeclared && sizePt > 0)
        {
            cfg.cellFontSize = sizePt;
            if (cfg.cssBasePt <= 0) cfg.cssBasePt = sizePt;
        }
        if (cfg.cellLineHeightPt <= 0 && linePt != 0)
            cfg.cellLineHeightPt = linePt > 0 ? linePt : -linePt * cfg.cellFontSize;
    }

    /// <summary>The size and family a `font:` shorthand VALUE names (px/pt size, the first family), or null.</summary>
    private static (double sizePt, string? family)? CssFontShorthandValue(string v)
    {
        var m = Regex.Match(v, @"([\d.]+)\s*(px|pt)\s*(?:/\s*[^\s]+\s*)?([^;]*)");
        if (!m.Success) return null;
        var size = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (m.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)) size *= PxToPt;
        var fam = m.Groups[3].Value.Trim();
        return (size, fam.Length > 0 ? FirstFontFamily(fam) : null);
    }
}
