using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The face the cells draw in and the CSS base the sizes resolve against: the tag's inline family, the sheet's table and cell rules, the ancestor grid and the run face the document carries in.</summary>
    private static string? ResolveTableFaceAndCssBase(TableStyleConfig cfg, TableParseState ps, string? cssRunFace, bool uaCellBoxes, string html)
    {
        // rule merged above - is the weight its cells START at. The complaint report's band
        // says it through `.h5 { font-weight: bold }` on the table alone, and every cell of
        // that row draws bold in the reference.
        ps.tableBold = cfg.tblStyle.TryGetValue("font-weight", out var itfw)
            && Regex.IsMatch(itfw, @"bold|[6-9]00", RegexOptions.IgnoreCase);
        cfg.inlineFaceRatio = 0.0;
        if (cfg.tblStyle.TryGetValue("font-family", out var iff))
        {
            ps.cellFamily = FirstFontFamily(iff);
            // The grid dialect needs the face AUTHORED on the source table's own
            // tag — a table the converter SYNTHESIZED (a form-horizontal row
            // rebuilt as table markup, marked class="fh-row"/data-fhw) carries
            // the body face in its synthetic style and keeps the calibrated
            // legacy metrics.
            if (cfg.tblTag.Success
                && Regex.IsMatch(cfg.tblTag.Value, @"font-family", RegexOptions.IgnoreCase)
                && cfg.tblTag.Value.IndexOf("data-fhw", StringComparison.OrdinalIgnoreCase) < 0
                && !cfg.tblClasses.Contains("fh-row")
                && ps.cellFamily is { Length: > 0 } && WinMetricsFor(ps.cellFamily) is { } ifm)
                cfg.inlineFaceRatio = ifm.sum;
        }
        else if (cfg.css.TryGetValue("table", out var tdecl) && tdecl.TryGetValue("font-family", out var ffv))
            ps.cellFamily = FirstFontFamily(ffv);
        else if ((CssFontShorthand(cfg.css, "table") ?? CssFontShorthand(cfg.css, "td")) is { family: not null } fshf)
            ps.cellFamily = fshf.family;
        else if (cfg.docCss is not null
            && (CssFontShorthand(cfg.docCss, "table") ?? CssFontShorthand(cfg.docCss, "td")) is { family: not null } dcff)
            ps.cellFamily = dcff.family;

        cfg.cssBasePt = 0;
        // (a UA-boxed grid the document sheet faces through its table/td rules draws in that face)
        if (uaCellBoxes && cfg.docCss is not null
            && (ps.cellFamily is null || ps.cellFamily == "Times New Roman") && cfg.defaultCellFace is null)
        {
            if (cfg.docCss.TryGetValue("table td", out var uaTdF) && uaTdF.TryGetValue("font-family", out var uaTdFam) && FirstFontFamily(uaTdFam) is { Length: > 0 } uaTdFace) ps.cellFamily = uaTdFace;
            else if (cfg.docCss.TryGetValue("td", out var uaDdF) && uaDdF.TryGetValue("font-family", out var uaDdFam) && FirstFontFamily(uaDdFam) is { Length: > 0 } uaDdFace) ps.cellFamily = uaDdFace;
            else if (cfg.docCss.TryGetValue("table", out var uaTbF) && uaTbF.TryGetValue("font-family", out var uaTbFam) && FirstFontFamily(uaTbFam) is { Length: > 0 } uaTbFace) ps.cellFamily = uaTbFace;
            // (…and in the face the sheet's BODY rule names where no table/td rule does - the mailing's
            //  `body { font-family: Arial }` draws its grids in Arial)
            else if (cssRunFace is { Length: > 0 } && WinMetricsFor(cssRunFace) is not null) ps.cellFamily = cssRunFace;
        }
        cfg.cssBaseFamily = ps.cellFamily;
        if (TableInlineFontSizeKeywordPt(cfg.tblTag) is { } bfsk) cfg.cssBasePt = bfsk;
        else if (cfg.tblStyle.TryGetValue("font-size", out var bfs) && TryParseLength(bfs) is { } bfsp) cfg.cssBasePt = bfsp;
        else if (TryGetCssLength(cfg.css, "table", "font-size") is { } bts) cfg.cssBasePt = bts;
        else if (TryGetCssLength(cfg.css, "td", "font-size") is { } bds) cfg.cssBasePt = bds;
        else if (cfg.docCss is not null && TryGetCssLength(cfg.docCss, "table td", "font-size") is { } dttds) cfg.cssBasePt = dttds;
        else if (cfg.docCss is not null && TryGetCssLength(cfg.docCss, "table", "font-size") is { } dts) cfg.cssBasePt = dts;
        else if (cfg.docCss is not null && TryGetCssLength(cfg.docCss, "td", "font-size") is { } dds) cfg.cssBasePt = dds;
        if (cfg.cssBaseFamily is null && cfg.docCss is not null)
        {
            if (cfg.docCss.TryGetValue("table td", out var dttdf) && dttdf.TryGetValue("font-family", out var dtf2))
                cfg.cssBaseFamily = FirstFontFamily(dtf2);
            else if (cfg.docCss.TryGetValue("table", out var dtd) && dtd.TryGetValue("font-family", out var dtf))
                cfg.cssBaseFamily = FirstFontFamily(dtf);
            else if (cfg.docCss.TryGetValue("td", out var ddd) && ddd.TryGetValue("font-family", out var ddf))
                cfg.cssBaseFamily = FirstFontFamily(ddf);
        }
        // A grid built through its ancestor chain INHERITS the typography those containers and the
        // body declare, where nothing on the table or its cells' element rules says otherwise: the
        // nearest ancestor's rule (its id, then its classes) and then the body rule supply the face,
        // the size and the line box (measured: a `BODY { 13px; Verdana }` sheet draws its grid in
        // Verdana; a `.datagrid { font: 12px/150% Arial }` wrapper draws its cells in Arial 9).
        if (_ancestorGridSheet && cfg.cssAncestors is { Count: > 0 } && cfg.chainRules is { Count: > 0 })
            ApplyAncestorTypography(cfg, ps, cfg.cellSizeDeclared, html);

        // The CSS run dialect costs this table its uniform per-row line grid, so it only
        // applies where that grid is actually wrong: a table holding a RUN whose class
        // resizes it away from the cell base. A grid of one size keeps the legacy layout
        // it is calibrated to, whatever the page stylesheet says.
        // ⚠ The page's base face is deliberately NOT adopted as the cell face here: the
        // installed Segoe UI resolves without usable /Widths, so the measure falls back to
        cssRunFace = ApplyRunFaceToCells(cfg, cssRunFace, html);
        cfg.uaDocGrid = cssRunFace is null && cfg.defaultCellFace is { Length: > 0 } && !cfg.dwFormCells;

        cfg.breakAnywhereDoc = false;
        foreach (var wbSrc in new[] { cfg.css, cfg.docCss })
            if (wbSrc is not null && wbSrc.TryGetValue("*", out var wbR)
                && wbR.TryGetValue("word-break", out var wbV)
                && Regex.IsMatch(wbV, "break-word|break-all", RegexOptions.IgnoreCase))
                cfg.breakAnywhereDoc = true;
        // Word mail: every cell holds its declared width - a token wider than its cell
        // (a signature picture's alt path) breaks inside it instead of widening the column.
        if (cfg.wordMailCells) cfg.breakAnywhereDoc = true;

        // …and a class-scoped rule on THIS table's cells (`.longTextTable tr, .longTextTable td
        // { word-break: break-all }`): every token in the grid breaks, so a long-string cell keeps
        // the grid's declared box instead of widening the sheet to the string (probed on the
        // quotation: the page is 96 + 487.5 + 90 = 673.5 with the rule, 2339.51 without it).
        if (!cfg.breakAnywhereDoc
            && (TableClassRuleValue(cfg, "word-break") ?? TableClassCellRuleValue(cfg, "word-break")) is { } clsWb
            && Regex.IsMatch(clsWb, "break-word|break-all", RegexOptions.IgnoreCase))
            cfg.breakAnywhereDoc = true;
        return cssRunFace;
    }

    /// <summary>The cell font size, from the tag's own keyword or declaration down to the sheet's table and cell rules and the document's own.</summary>
    private static void ResolveCellFontSize(TableStyleConfig cfg, bool uaCellBoxes)
    {
        // A grid the document sizes nowhere draws at the legacy 11 pt - except under a sheet that
        // sizes the BODY: a quirks-mode table does not inherit that size, it resets to the UA 16px
        // (probed: a `body { font-size: 15px }` sheet's unclassed cells draw at 12 pt). Documents
        // with no body size keep the calibrated 11 (the diff report and the attachment grid page
        // 27 and 20 pt wider at 12).
        cfg.cellFontSize = cfg.defaultCellFontPt > 0 ? cfg.defaultCellFontPt
            // (a UA-boxed grid the document sizes nowhere draws at the browser's 16px base)
            : uaCellBoxes ? UaDefaultFontPt
            : _quirksRowStrut && cfg.docCss is not null && TryGetCssLength(cfg.docCss, "body", "font-size") is > 0 ? UaDefaultFontPt
            : LegacyCellFontPt;
        cfg.cellFontShorthand = false;
        cfg.cellSizeDeclared = true;
        // (a keyword the table's OWN style attribute states - `font-size: small` - is a size too, the
        // classic 13 px; a class rule's keyword stays out of the calibrated resolution)
        if (TableInlineFontSizeKeywordPt(cfg.tblTag) is { } itfsk) cfg.cellFontSize = itfsk;
        else if (cfg.tblStyle.TryGetValue("font-size", out var itfs) && TryParseLength(itfs) is { } itfsp) cfg.cellFontSize = itfsp;
        else if (TryGetCssLength(cfg.css, "table", "font-size") is { } tfs) cfg.cellFontSize = tfs;
        else if (TryGetCssLength(cfg.css, "td", "font-size") is { } dfs) cfg.cellFontSize = dfs;
        else if ((CssFontShorthand(cfg.css, "table") ?? CssFontShorthand(cfg.css, "td")) is { } fsh)
        {
            cfg.cellFontSize = fsh.sizePt;
            cfg.cellFontShorthand = true;
        }
        // The <table> segment rarely carries the stylesheet — a document-level
        // `td { font: 10px Verdana }` (shorthand or longhand) sizes the cells too.
        else if (cfg.docCss is not null && TryGetCssLength(cfg.docCss, "table td", "font-size") is { } dttdfs) cfg.cellFontSize = dttdfs;
        else if (cfg.docCss is not null && TryGetCssLength(cfg.docCss, "table", "font-size") is { } dtfs) cfg.cellFontSize = dtfs;
        else if (cfg.docCss is not null && TryGetCssLength(cfg.docCss, "td", "font-size") is { } ddfs) cfg.cellFontSize = ddfs;
        else if (cfg.docCss is not null
            && (CssFontShorthand(cfg.docCss, "table") ?? CssFontShorthand(cfg.docCss, "td")) is { } dcfsh)
        {
            cfg.cellFontSize = dcfsh.sizePt;
            cfg.cellFontShorthand = true;
        }
        else cfg.cellSizeDeclared = false;
        // The shorthand expansion leaves the `font:` declaration beside its generated
        // longhands, so the longhand branches above win the size resolution. The rule
        // is still AUTHORED as a shorthand — when one exists and agrees with the size
        // that won, the form-document cell dialect applies. A longhand that OVERRODE
        // the shorthand (differing size — the cascade's later-wins) keeps the flag off.
        if (!cfg.cellFontShorthand
            && (CssFontShorthand(cfg.css, "table") ?? CssFontShorthand(cfg.css, "td")
                ?? (cfg.docCss is null ? null : CssFontShorthand(cfg.docCss, "table") ?? CssFontShorthand(cfg.docCss, "td")))
                is { } anySh
            && Math.Abs(anySh.sizePt - cfg.cellFontSize) < 1e-9)
            cfg.cellFontShorthand = true;
    }
}
