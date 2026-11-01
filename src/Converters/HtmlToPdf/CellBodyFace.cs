using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A grid no table or cell rule faces takes the body face for its cells where the flow sets the prose in
    /// it, unless a calibrated dialect carries a face of its own (a run face, the UA cell boxes, a default cell face,
    /// the form and mail grids). The body rule stands behind every other source: wherever a declaration naming
    /// another face reaches the grid (see <see cref="OtherFaceReachesGrid"/>), the cells keep the path they took.</summary>
    private static void InheritBodyFace(TableStyleConfig cfg, TableParseState ps, string? cssRunFace, bool uaCellBoxes, string html)
    {
        if (!(cfg.bodyFaceGrid || cfg.nestedGrid)) return;
        if (ps.cellFamily is not null || cfg.cssBaseFamily is not null || cssRunFace is not null || uaCellBoxes
            || cfg.defaultCellFace is not null || cfg.dwFormCells || cfg.wordMailCells || cfg.formGridDialect) return;
        if (BodyFaceForGrid(cfg, html) is not { } bodyFace) return;
        ps.cellFamily = bodyFace;
        cfg.cssBaseFamily = bodyFace;
        ps.bodyFaceCells = true;
    }

    /// <summary>The program a body-face grid's cells draw a bold line in (the cell writer's own lookup), as the
    /// font its column measure reads: measured in the regular program, a bold header floors its column, and
    /// the sheet the grid widens, for text narrower than the text drawn.</summary>
    private static Text.Font? BodyFaceBoldFont(string family)
    {
        if (Table.CellFaceTtf(family, bold: true) is not { Length: > 0 } ttf) return null;
        var font = new Text.FontData(family + " Bold", Text.FontType.TrueType);
        font.SetTtfData(ttf);
        return font;
    }

    /// <summary>The installed face the body rule names, for a grid whose cells the installed-face path draws whole:
    /// that path lays a cell's text, its bold runs and its colour runs, and nothing else. Null when the face is not
    /// installed or a cell holds what only the built-in path dresses - a link, an underline, a list, an indent, a
    /// paragraph or heading box, a rule, a picture, a raised or lowered run, a character outside the built-in
    /// encoding, or an element the sheet boxes - so such a grid keeps the path that draws all of it; null too where
    /// another face reaches the grid.</summary>
    private static string? BodyFaceForGrid(TableStyleConfig cfg, string html)
    {
        // A nested grid draws in the face its host draws in: the host judged the whole markup, its nested grids
        // included, and a nested grid in another face than its host's cells reflows the host around it.
        if (cfg.nestedGrid) return cfg.hostBodyFace;
        if (cfg.docCss is null || SheetBodyFace(cfg.docCss) is not { } face) return null;
        if (SafeRegex.IsMatch(FaceUndressedCellRx, html) || NeedsUnicode(DecodeEntities(html))) return null;
        if (SheetBoxesCellContent(cfg.chainRules, html)) return null;
        return OtherFaceReachesGrid(cfg, face, html) ? null : face;
    }

    /// <summary>True when a declaration naming a face other than the body's stands on an element of the grid's chain:
    /// the body, the containers open above the grid, or anything the grid's own markup opens - its table, sections,
    /// rows and cells and what they hold, nested grids and their wrappers included. Every declaration is asked, not a
    /// list of keys: each sheet rule that names a face, on the element its selector styles (read as loosely as
    /// <see cref="FaceRuleSubject"/> must), and each element's own style or face attribute. A family that resolves to
    /// the body face, or inherits, names no other face; a sheet whose rules were not kept stands the body face back.</summary>
    private static bool OtherFaceReachesGrid(TableStyleConfig cfg, string face, string html)
    {
        if (cfg.docCss is not Dictionary<string, Dictionary<string, string>> sheet || !KeptRulesTable.TryGetValue(sheet, out var kept)
            || kept.FamilyDecls.Count == 0)
            return true;
        var above = new List<CssElem> { kept.Body };
        above.AddRange(cfg.hostChain ?? cfg.cssAncestors ?? new List<CssElem>());
        GridMarkup? markup = null;
        foreach (var rule in kept.Families)
        {
            // (the body rule is the body face)
            if (rule.Selector.Equals("body", StringComparison.OrdinalIgnoreCase)) continue;
            if (rule.Subject is { } subject && !above.Exists(e => ChainSegMatches(subject, e))
                && !(markup ??= new GridMarkup(html)).Holds(subject)) continue;
            if (!IsBodyFace(rule.Family, face)) return true;
        }
        if (above.Exists(e => e.OwnFamily is { } own && !IsBodyFace(own, face))) return true;
        // (an element's own face is spelled with "font" - a style's font-family or font, a font tag's face)
        return html.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0
            && (markup ?? new GridMarkup(html)).Elements.Exists(e => e.OwnFamily is { } own && !IsBodyFace(own, face));
    }

    /// <summary>True when a declared family stack is the body face - its first installed family, read as the body
    /// face is read - or inherits (`inherit`, `unset`), naming no face of its own.</summary>
    private static bool IsBodyFace(string stack, string face)
    {
        var s = stack.Replace("&quot;", "\"").Replace("&#34;", "\"").Replace("&apos;", "'").Replace("&#39;", "'");
        // (an `!important` flag is no part of the stack)
        if (s.IndexOf('!') is >= 0 and var flag) s = s[..flag];
        s = s.Trim();
        return s.Equals("inherit", StringComparison.OrdinalIgnoreCase) || s.Equals("unset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(FirstInstalledFamily(s), face, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when a chain rule gives an element inside the cells a box of its own (a background or a
    /// display), and the markup carries that element's hook: the cell draws it as an inline box the
    /// installed-face path does not lay.</summary>
    private static bool SheetBoxesCellContent(List<CssChainRule>? rules, string html)
    {
        if (rules is null) return false;
        foreach (var rule in rules)
        {
            var last = rule.Segs[^1];
            if (last.Tag is "td" or "th" or "tr" or "table") continue;
            if (!rule.Decls.Keys.Any(k => k == "display" || k.StartsWith("background", StringComparison.Ordinal))) continue;
            if (last.Id is { } id && html.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (last.Classes is { } classes && classes.Exists(c => html.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            if (last.Id is null && last.Classes is null && last.Tag is { } tag
                && html.IndexOf("<" + tag, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    /// <summary>Cell content the installed-face path does not lay: a link, an underline, a list or indent, a
    /// paragraph, heading or preformatted box, a rule, a picture, a raised or lowered run.</summary>
    private static readonly Regex FaceUndressedCellRx = new Regex(
        @"<a\b[^>]*\bhref|<(?:li|u|ins|dd|dt|blockquote|p|h[1-6]|pre|hr|img|sup|sub)\b|text-decoration\s*:\s*underline|text-indent|margin-left",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);
}
