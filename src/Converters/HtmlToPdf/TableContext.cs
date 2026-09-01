using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The table builder's preamble, verbatim: parses the tag, stylesheet
    /// and chain rules, settles the dialect config, builds the empty Table and the
    /// token stream, and hands back the five context objects the parse loop works on.</summary>
    private static (TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table, List<Token> tokens)
        BuildTableParseContext(string html, double availWidthPt, HtmlLoadOptions? options, List<byte[]>? inlineSvgs, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, bool bandDialect, bool widenProbe, double cellLineHeightPt, double defaultCellFontPt, bool tightExtras, bool liftNestedTables, bool uaCellBoxes, ref string? cssRunFace, Color? bodyTextColor, bool uaSerifMin, bool authoredCellChrome, bool formGridDialect, bool ptCellWidths, bool redlineCells, bool dwFormCells, double formGridStrutPt, double formGridStrutDropPt, string? defaultCellFace, bool docElementGrid, bool fullWidthCjkMin, bool pinnedBodyGrid, bool overDeclaredDraw, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio)
    {
        var cfg = new TableStyleConfig();
        cfg.css = ParseStyleSheet(html);

        cfg.docAnchorColor = null;
        if (docCss is not null && docCss.TryGetValue("a", out var docARule)
            && docARule.TryGetValue("color", out var docACol))
            cfg.docAnchorColor = ParseCssColor(docACol);

        cfg.tblTag = Regex.Match(html, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        cfg.tblStyle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? tblBorderAttr = null, tblCellPadAttr = null, tblBorderColorAttr = null;
        var colModel = new TableColumnModel();
        // cellspacing="0" is a DECLARATION (no spacing), distinct from the absent
        // attribute (which leaves the UA's default border-spacing in force).
        var tblCellSpacingDeclared = false;
        if (cfg.tblTag.Success)
        {
            var sm = Regex.Match(cfg.tblTag.Value, @"style\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            // JSON-escaped dialect: the style value is an unquoted token truncated at the
            // first whitespace (see ParseAttributes) — parse the declarations it kept.
            if (!sm.Success && cfg.tblTag.Value.IndexOf("\\\"", StringComparison.Ordinal) >= 0)
                sm = Regex.Match(cfg.tblTag.Value, @"style\s*=\s*(\S+)", RegexOptions.IgnoreCase);
            if (sm.Success)
                foreach (Match d in StyleDeclRx.Matches(sm.Groups[1].Value))
                    cfg.tblStyle[d.Groups[1].Value.Trim().ToLowerInvariant()] = d.Groups[2].Value.Trim();
            var bm = Regex.Match(cfg.tblTag.Value, @"(?<!\w)border\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (bm.Success) tblBorderAttr = bm.Groups[1].Value;
            var bcAttr = Regex.Match(cfg.tblTag.Value, @"bordercolor\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (bcAttr.Success) tblBorderColorAttr = bcAttr.Groups[1].Value;
            var cm = Regex.Match(cfg.tblTag.Value, @"cellpadding\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (cm.Success) tblCellPadAttr = cm.Groups[1].Value;
            var csAttr = Regex.Match(cfg.tblTag.Value, @"cellspacing\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (csAttr.Success && double.TryParse(csAttr.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var csPx))
            {
                colModel.tblCellSpacingPt = csPx * 0.75;
                tblCellSpacingDeclared = true;
                colModel.cellSpacingPt = colModel.tblCellSpacingPt;
            }
            var hm = Regex.Match(cfg.tblTag.Value, @"height\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (hm.Success) double.TryParse(hm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out colModel.tblHeightPx);
        }

        // Stylesheet rules addressed at the table's own class(es). The table-level
        // rule (".listTable { font/width/color }") merges UNDER the inline style;
        // ".cls td" / ".cls th" (the parser collapses ".cls tr td" to these) style
        // the cell grid. Border declarations stay out of the merge — a class border
        // is the table's OUTER frame, not a box on every cell.
        var tblClasses = new List<string>();
        if (cfg.tblTag.Success)
        {
            var clm = Regex.Match(cfg.tblTag.Value, @"class\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (clm.Success)
                foreach (var c in clm.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    tblClasses.Add(c);
        }
        Dictionary<string, string>? ClassRule(string suffix)
        {
            foreach (var c in tblClasses)
            {
                var key = "." + c + suffix;
                if (cfg.css.TryGetValue(key, out var d)) return d;
                if (docCss is not null && docCss.TryGetValue(key, out var d2)) return d2;
            }
            return null;
        }
        // "td.cls" / "th.cls": the class sits on the CELL, not the table. Editor markup
        // repeats the table's class on every cell, so the table's own class list finds it.
        Dictionary<string, string>? CellClassRule(string cellTag)
        {
            foreach (var c in tblClasses)
            {
                var key = cellTag + "." + c;
                if (cfg.css.TryGetValue(key, out var d)) return d;
                if (docCss is not null && docCss.TryGetValue(key, out var d2)) return d2;
            }
            return null;
        }
        var tblClassDecl = ClassRule("");
        if (tblClassDecl is not null)
            foreach (var kv in tblClassDecl)
                if (kv.Key is "font-size" or "font-family" or "color" or "width" or "max-width"
                    && !cfg.tblStyle.ContainsKey(kv.Key))
                    cfg.tblStyle[kv.Key] = kv.Value;

        cfg.cellFontSize = defaultCellFontPt > 0 ? defaultCellFontPt : 11;
        cfg.cellFontShorthand = false;
        if (cfg.tblStyle.TryGetValue("font-size", out var itfs) && TryParseLength(itfs, out var itfsp)) cfg.cellFontSize = itfsp;
        else if (TryGetCssLength(cfg.css, "table", "font-size", out var tfs)) cfg.cellFontSize = tfs;
        else if (TryGetCssLength(cfg.css, "td", "font-size", out var dfs)) cfg.cellFontSize = dfs;
        else if ((CssFontShorthand(cfg.css, "table") ?? CssFontShorthand(cfg.css, "td")) is { } fsh)
        {
            cfg.cellFontSize = fsh.sizePt;
            cfg.cellFontShorthand = true;
        }
        // The <table> segment rarely carries the stylesheet — a document-level
        // `td { font: 10px Verdana }` (shorthand or longhand) sizes the cells too.
        else if (docCss is not null && TryGetCssLength(docCss, "table td", "font-size", out var dttdfs)) cfg.cellFontSize = dttdfs;
        else if (docCss is not null && TryGetCssLength(docCss, "table", "font-size", out var dtfs)) cfg.cellFontSize = dtfs;
        else if (docCss is not null && TryGetCssLength(docCss, "td", "font-size", out var ddfs)) cfg.cellFontSize = ddfs;
        else if (docCss is not null
            && (CssFontShorthand(docCss, "table") ?? CssFontShorthand(docCss, "td")) is { } dcfsh)
        {
            cfg.cellFontSize = dcfsh.sizePt;
            cfg.cellFontShorthand = true;
        }
        // The shorthand expansion leaves the `font:` declaration beside its generated
        // longhands, so the longhand branches above win the size resolution. The rule
        // is still AUTHORED as a shorthand — when one exists and agrees with the size
        // that won, the form-document cell dialect applies. A longhand that OVERRODE
        // the shorthand (differing size — the cascade's later-wins) keeps the flag off.
        if (!cfg.cellFontShorthand
            && (CssFontShorthand(cfg.css, "table") ?? CssFontShorthand(cfg.css, "td")
                ?? (docCss is null ? null : CssFontShorthand(docCss, "table") ?? CssFontShorthand(docCss, "td")))
                is { } anySh
            && Math.Abs(anySh.sizePt - cfg.cellFontSize) < 1e-9)
            cfg.cellFontShorthand = true;

        var ps = new TableParseState();
        ps.cellFamily = defaultCellFace is { Length: > 0 } dcf ? dcf : null;
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
                && !tblClasses.Contains("fh-row")
                && ps.cellFamily is { Length: > 0 } && WinMetricsFor(ps.cellFamily) is { } ifm)
                cfg.inlineFaceRatio = ifm.sum;
        }
        else if (cfg.css.TryGetValue("table", out var tdecl) && tdecl.TryGetValue("font-family", out var ffv))
            ps.cellFamily = FirstFontFamily(ffv);
        else if ((CssFontShorthand(cfg.css, "table") ?? CssFontShorthand(cfg.css, "td")) is { family: not null } fshf)
            ps.cellFamily = fshf.family;
        else if (docCss is not null
            && (CssFontShorthand(docCss, "table") ?? CssFontShorthand(docCss, "td")) is { family: not null } dcff)
            ps.cellFamily = dcff.family;

        cfg.cssBasePt = 0;
        cfg.cssBaseFamily = ps.cellFamily;
        if (cfg.tblStyle.TryGetValue("font-size", out var bfs) && TryParseLength(bfs, out var bfsp)) cfg.cssBasePt = bfsp;
        else if (TryGetCssLength(cfg.css, "table", "font-size", out var bts)) cfg.cssBasePt = bts;
        else if (TryGetCssLength(cfg.css, "td", "font-size", out var bds)) cfg.cssBasePt = bds;
        else if (docCss is not null && TryGetCssLength(docCss, "table td", "font-size", out var dttds)) cfg.cssBasePt = dttds;
        else if (docCss is not null && TryGetCssLength(docCss, "table", "font-size", out var dts)) cfg.cssBasePt = dts;
        else if (docCss is not null && TryGetCssLength(docCss, "td", "font-size", out var dds)) cfg.cssBasePt = dds;
        if (cfg.cssBaseFamily is null && docCss is not null)
        {
            if (docCss.TryGetValue("table td", out var dttdf) && dttdf.TryGetValue("font-family", out var dtf2))
                cfg.cssBaseFamily = FirstFontFamily(dtf2);
            else if (docCss.TryGetValue("table", out var dtd) && dtd.TryGetValue("font-family", out var dtf))
                cfg.cssBaseFamily = FirstFontFamily(dtf);
            else if (docCss.TryGetValue("td", out var ddd) && ddd.TryGetValue("font-family", out var ddf))
                cfg.cssBaseFamily = FirstFontFamily(ddf);
        }

        // The CSS run dialect costs this table its uniform per-row line grid, so it only
        // applies where that grid is actually wrong: a table holding a RUN whose class
        // resizes it away from the cell base. A grid of one size keeps the legacy layout
        // it is calibrated to, whatever the page stylesheet says.
        // ⚠ The page's base face is deliberately NOT adopted as the cell face here: the
        // installed Segoe UI resolves without usable /Widths, so the measure falls back to
        // one em per glyph and every column comes out ~4 pt WIDER, not the ~1.4 pt
        // narrower the real metrics would give. Left on the Standard-14 stand-in.
        if (cssRunFace is not null)
        {
            var mixed = false;
            foreach (Match rt in Regex.Matches(html, @"<(?:span|font|p)\b[^>]*\bclass\s*=\s*[""']([^""']*)[""']",
                         RegexOptions.IgnoreCase))
            {
                foreach (var rc in rt.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var rk = "." + rc;
                    if ((!cfg.css.TryGetValue(rk, out var rcd)
                            && (docCss is null || !docCss.TryGetValue(rk, out rcd)))
                        || !rcd.TryGetValue("font-size", out var rfs)
                        || !TryParseLength(rfs, out var rfsp) || rfsp <= 0
                        || Math.Abs(rfsp - cfg.cellFontSize) < 0.01) continue;
                    mixed = true;
                    break;
                }
                if (mixed) break;
            }
            if (!mixed) cssRunFace = null;
        }
        cfg.uaDocGrid = cssRunFace is null && defaultCellFace is { Length: > 0 } && !dwFormCells;

        cfg.breakAnywhereDoc = false;
        foreach (var wbSrc in new[] { cfg.css, docCss })
            if (wbSrc is not null && wbSrc.TryGetValue("*", out var wbR)
                && wbR.TryGetValue("word-break", out var wbV)
                && Regex.IsMatch(wbV, "break-word|break-all", RegexOptions.IgnoreCase))
                cfg.breakAnywhereDoc = true;

        cfg.hasBorder = false;
        cfg.borderWidth = 1;
        cfg.borderColor = Color.Black;
        cfg.pad = 0;
        cfg.elemRuleBorder = false;
        cfg.padSide = -1;
        cfg.padBottom = -1;
        // border="1"/"1px" attribute on the table draws a 1px box on every cell.
        if (tblBorderAttr is not null && !tblBorderAttr.StartsWith("0"))
        {
            cfg.hasBorder = true;
            var wm = Regex.Match(tblBorderAttr, @"(\d+(?:\.\d+)?)");
            if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bwa) && bwa > 0)
                cfg.borderWidth = bwa * PxToPt;
            // The legacy BORDERCOLOR attribute colours the grid (form-grid dialect
            // only — the calibrated dialects keep their black default).
            if (formGridDialect && tblBorderColorAttr is not null
                && ParseCssColor(tblBorderColorAttr) is { } bcaCol)
                cfg.borderColor = bcaCol;
        }
        if (tblCellPadAttr is not null && double.TryParse(Regex.Match(tblCellPadAttr, @"[\d.]+").Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cpa) && cpa > 0)
            cfg.pad = cpa * PxToPt;
        foreach (var sel in new[] { "td", "th" })
        {
            if (!cfg.css.TryGetValue(sel, out var d)) continue;
            if (d.TryGetValue("border", out var bd))
            {
                var t = bd.Trim();
                cfg.hasBorder = !t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0;
                var wm = Regex.Match(bd, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bw))
                    cfg.borderWidth = bw * PxToPt;
                var bc = ParseCssColor(bd); if (bc is not null) cfg.borderColor = bc;
            }
            if (d.TryGetValue("padding", out var pv) && TryParseLength(pv, out var pp)) cfg.pad = pp;
        }
        // Cell-qualified class rules — ".cls td" (from a ".cls > tbody > tr > td" chain) and
        // "td.cls" — name the CELL, so unlike a bare ".cls" border they DO box every cell and
        // pad its text. Editor-generated table styles carry their grid this way.
        foreach (var d in new[] { ClassRule(" td"), ClassRule(" th"), CellClassRule("td"), CellClassRule("th") })
        {
            if (d is null) continue;
            if (d.TryGetValue("border", out var cbd))
            {
                var t = cbd.Trim();
                cfg.hasBorder = !t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0;
                var wm = Regex.Match(cbd, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var cbw))
                    cfg.borderWidth = cbw * PxToPt;
                var cbc = ParseCssColor(cbd); if (cbc is not null) cfg.borderColor = cbc;
            }
            // `padding: 7px 5px 6px` — the shorthand's TOP value seeds the vertical
            // inset the row height is measured with, and its SIDE value (the second
            // entry, or the first when the shorthand is a single length) the horizontal
            // one the column footprint is measured with.
            if (d.TryGetValue("padding", out var cpv))
            {
                var cps = cpv.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (cps.Length > 0 && TryParseLength(cps[0], out var cpp))
                {
                    cfg.pad = cpp;
                    cfg.padSide = cps.Length > 1 && TryParseLength(cps[1], out var cpsv) ? cpsv : cpp;
                    // The bottom entry only under the CSS run dialect: the legacy grid is
                    // calibrated against the top value standing in for both.
                    cfg.padBottom = cssRunFace is not null && cps.Length > 2
                        && TryParseLength(cps[2], out var cpbv) ? cpbv : cpp;
                }
            }
        }
        // A `border: Npx …` declaration on the <table> tag's own style boxes every cell
        // like the border attribute; cell text then insets by the stroke width plus
        // the UA-default 1px cell padding. The stroke share of that inset comes from
        // the bordered cell box itself, so only the UA padding is added here.
        if (!cfg.hasBorder && cfg.tblStyle.TryGetValue("border", out var tbstv))
        {
            var tb = tbstv.Trim();
            if (!tb.StartsWith("0") && tb.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
            {
                cfg.hasBorder = true;
                var wm = Regex.Match(tb, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tbw) && tbw > 0)
                    cfg.borderWidth = tbw * PxToPt;
                var tbc = ParseCssColor(tb); if (tbc is not null) cfg.borderColor = tbc;
                if (cfg.pad <= 0) cfg.pad = 1 * PxToPt;
            }
        }

        // A LONGHAND border triplet (`table, td { border-style: solid; border-color:
        // #333 }` + `td { border-width: 1px }`) boxes the cells like the shorthand —
        // form documents commonly split the declaration across document-level rules.
        // A table whose own style says border-style:none opts out.
        var tblBorderNone = cfg.tblStyle.TryGetValue("border-style", out var tbsNone)
            && tbsNone.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!cfg.hasBorder && !tblBorderNone)
        {
            foreach (var cssSrc in new[] { cfg.css, docCss })
            {
                if (cssSrc is null || cfg.hasBorder) continue;
                foreach (var sel in new[] { "td", "table" })
                {
                    if (!cssSrc.TryGetValue(sel, out var d0)) continue;
                    // The SHORTHAND spelling of the same rule (`td { border: 1px
                    // solid #000 }`) boxes the cells identically — it stays one
                    // key in the rule map (no longhand expansion), so it is its
                    // own arm here (element-grid dialect only).
                    if (docElementGrid && d0.TryGetValue("border", out var bshv)
                        && ChainBorder(bshv) is { } bshi)
                    {
                        cfg.hasBorder = true;
                        cfg.elemRuleBorder = true;
                        cfg.borderWidth = bshi.Width;
                        cfg.borderColor = bshi.Color is { } bshc ? bshc : cfg.borderColor;
                        if (cfg.pad <= 0 && d0.TryGetValue("padding", out var bshPad)
                            && TryParseLength(bshPad, out var bshPadPt))
                            cfg.pad = bshPadPt;
                        break;
                    }
                    if (!d0.TryGetValue("border-style", out var bsv)
                        || bsv.IndexOf("solid", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    cfg.hasBorder = true;
                    double maxPx = 1;
                    foreach (var src2 in new[] { cfg.css, docCss })
                        if (src2 is not null && src2.TryGetValue("td", out var dtd2)
                            && dtd2.TryGetValue("border-width", out var bwv))
                            foreach (Match mm in Regex.Matches(bwv, @"([\d.]+)\s*(px)?"))
                                if (double.TryParse(mm.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var bwp)
                                    && bwp > 0 && bwp < 10)
                                    maxPx = Math.Max(maxPx, bwp);
                    cfg.borderWidth = maxPx * PxToPt;
                    foreach (var src2 in new[] { cfg.css, docCss })
                        if (src2 is not null)
                            foreach (var sel2 in new[] { "td", "table" })
                                if (src2.TryGetValue(sel2, out var d2)
                                    && d2.TryGetValue("border-color", out var bcv)
                                    && ParseCssColor(bcv) is { } bcc)
                                { cfg.borderColor = bcc; break; }
                    if (cfg.pad <= 0)
                        foreach (var src2 in new[] { cfg.css, docCss })
                            if (src2 is not null && src2.TryGetValue("td", out var dtp)
                                && dtp.TryGetValue("padding", out var pdv) && TryParseLength(pdv, out var pdp))
                                cfg.pad = pdp;
                    break;
                }
            }
        }

        // The pinned-body report's grid lines: the TABLE's own bgcolor shows
        // through a 1-2px cellspacing between white cells — every cell reads
        // as a thin box in the table's colour. Drawn as the cell border it
        // visually is (dialect-gated; the legacy paths never read table bg).
        if (!cfg.hasBorder && pinnedBodyGrid && cfg.tblTag.Success
            && tblCellSpacingDeclared && colModel.tblCellSpacingPt is > 0 and <= 1.6
            && Regex.Match(cfg.tblTag.Value, @"\bbgcolor\s*=\s*[""']?([^""'\s>]+)",
                RegexOptions.IgnoreCase) is { Success: true } tbgm
            && ParseCssColor(tbgm.Groups[1].Value) is { } tbgc)
        {
            cfg.hasBorder = true;
            cfg.borderWidth = colModel.tblCellSpacingPt;
            cfg.borderColor = tbgc;
            // The spacing line rides INSIDE the row pitch (a 22px row
            // = the line box + the cellpadding pair + the 1px band) — the border
            // this arm draws must not grow the row, so the vertical padding
            // yields the border's width back.
            cfg.pad = Math.Max(0, cfg.pad - cfg.borderWidth);
        }

        // Cells styled inline (`<td style="…border: #000 1px solid…">`) draw a cell box just
        // like a `td { border: … }` stylesheet rule — CMS/spreadsheet exports style each cell
        // inline and have no <style> block at all. Sample the first bordered cell.
        if (!cfg.hasBorder)
        {
            var cbm = Regex.Match(html, @"<t[dh]\b[^>]*style\s*=\s*[""'][^""']*border\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (cbm.Success)
            {
                var bd = cbm.Groups[1].Value.Trim();
                if (!bd.StartsWith("0") && bd.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    cfg.hasBorder = true;
                    var wm = Regex.Match(bd, @"(\d+(?:\.\d+)?)\s*px");
                    if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bw) && bw > 0)
                        cfg.borderWidth = bw * PxToPt;
                    var bc = ParseCssColor(bd); if (bc is not null) cfg.borderColor = bc;
                }
            }
        }

        cfg.nestedHtml = new List<string>();
        var scanHtml = liftNestedTables ? ExtractNestedTables(html, cfg.nestedHtml) : html;
        foreach (Match cm2 in Regex.Matches(scanHtml, @"<col\b[^>]*>", RegexOptions.IgnoreCase))
        {
            double wpx = 0;
            var wa = Regex.Match(cm2.Value, @"width\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (wa.Success) double.TryParse(wa.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out wpx);
            else
            {
                var ws = Regex.Match(cm2.Value, @"style\s*=\s*[""'][^""']*width\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
                if (ws.Success) double.TryParse(ws.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out wpx);
            }
            (colModel.colGroupPt ??= new List<double>()).Add(wpx * PxToPt);
        }

        cfg.chainBase = null;
        Dictionary<string, string>? tblChainDecls = null;
        if (chainRules is { Count: > 0 } && liftNestedTables)
        {
            var te = new CssElem { Tag = "table" };
            if (cfg.tblTag.Success)
            {
                var im2 = Regex.Match(cfg.tblTag.Value, @"\bid\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase);
                if (im2.Success) te.Id = im2.Groups[1].Value;
                var cm3 = Regex.Match(cfg.tblTag.Value, @"\bclass\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
                if (cm3.Success)
                    te.Classes = cm3.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            }
            cfg.chainBase = cssAncestors is { Count: > 0 }
                ? new List<CssElem>(cssAncestors) : new List<CssElem>();
            cfg.chainBase.Add(te);
            tblChainDecls = MatchChainDecls(chainRules, cfg.chainBase);
        }

        string? twVal = cfg.tblStyle.TryGetValue("width", out var itw) ? itw
            : (cfg.css.TryGetValue("table", out var tw2) && tw2.TryGetValue("width", out var tw) ? tw : null);
        if (twVal is null && docElementGrid && docCss is not null
            && docCss.TryGetValue("table", out var dtw2) && dtw2.TryGetValue("width", out var dtw))
        {
            twVal = dtw;
            colModel.tableWidthFromDocRule = true;
        }
        if (twVal is null && tblChainDecls is not null
            && tblChainDecls.TryGetValue("width", out var twChain))
        {
            twVal = twChain;
            colModel.tableWidthPctOfBox = true;
        }
        // …and so does the presentational ATTRIBUTE: `<table width="290">` is HTML4's
        // spelling of `width: 290px` and `width="100%"` of `width: 100%`. That pair is
        // the only width an email template gives its inner boxes; without it such a grid
        // was "undeclared" and its columns fell to min-content — a headline column one
        // letter wide.
        if (twVal is null && liftNestedTables && cfg.tblTag.Success
            && Regex.Match(cfg.tblTag.Value, @"\bwidth\s*=\s*[""']?\s*(\d+(?:\.\d+)?)\s*(%?)\s*[""'\s>]",
                RegexOptions.IgnoreCase) is { Success: true } twAttr)
        {
            var twAttrPct = twAttr.Groups[2].Value.Length > 0;
            twVal = twAttr.Groups[1].Value + (twAttrPct ? "%" : "px");
            colModel.tableWidthPctOfBox = twAttrPct;
        }
        if (twVal is not null && twVal.EndsWith("%", StringComparison.Ordinal)
            && double.TryParse(twVal.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var twp))
        {
            colModel.tableWidthFrac = Math.Clamp(twp / 100.0, 0.05, 1.0);
            colModel.tableWidthDeclared = true;
        }
        // An absolute declared width ("9.75in"), capped by a type-rule max-width
        // ("table { max-width: 6.25in }" — the CSS constraint the fixed width must
        // respect), pins the table box inside the available width.
        else if (twVal is not null && availWidthPt > 0 && TryParseLength(twVal, out var twAbs) && twAbs > 0)
        {
            string? maxWv = cfg.tblStyle.TryGetValue("max-width", out var imw) ? imw
                : (cfg.css.TryGetValue("table", out var mt) && mt.TryGetValue("max-width", out var mw1) ? mw1
                    : docCss is not null && docCss.TryGetValue("table", out var mdt) && mdt.TryGetValue("max-width", out var mw2) ? mw2 : null);
            if (maxWv is not null && TryParseLength(maxWv, out var mwPt) && mwPt > 0)
                twAbs = Math.Min(twAbs, mwPt);
            colModel.tableWidthFrac = Math.Clamp(twAbs / availWidthPt, 0.05, 1.0);
            colModel.tableWidthDeclared = true;
            colModel.tableWidthDeclaredAbs = true;
            colModel.tableWidthDeclAbsPt = twAbs;
        }

        // Cell-grid styling addressed through the table's class: side-specific cell
        // borders (".listTable td { border-top: … }" — the row-rule style), cell
        // padding, the header row's own bottom rule and alignment, and the class's
        // text colour. The class's own border strokes the table frame, not cells.
        BorderInfo? outerBorder = null;
        BorderInfo? cellSideBorder = null;
        if (tblClassDecl is not null && tblClassDecl.TryGetValue("color", out var ctv))
            ps.cellTextColor = ParseCssColor(ctv);
        else if (cfg.tblStyle.TryGetValue("color", out var ctv2))
            ps.cellTextColor = ParseCssColor(ctv2);
        // …else the page stylesheet's own body colour, which the grid inherits: these
        // pages set a soft grey (`body { color: #444 }`) that the black default ignores.
        else ps.cellTextColor ??= bodyTextColor;
        if (tblClassDecl is not null && tblClassDecl.TryGetValue("border", out var obv))
        {
            var t = obv.Trim();
            if (!t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
            {
                double obw = 1 * PxToPt;
                var wm = Regex.Match(t, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var obwv) && obwv > 0)
                    obw = obwv * PxToPt;
                outerBorder = new BorderInfo(BorderSide.Box, obw, ParseCssColor(t) ?? Color.Black);
            }
        }
        // A class rule that declares all FOUR border-side longhands (".blackBorder {
        // border-top: 1px solid black; border-right: …; }") boxes what carries the
        // class. This markup shape repeats the class on the table AND on every cell
        // (the same repetition CellClassRule reads), so the sides become a box on
        // every cell and the collapsed grid's outer frame alike — the "top border
        // missing on every table" defect class.
        if (outerBorder is null && tblClassDecl is not null
            && tblClassDecl.TryGetValue("border-top", out var clsBt)
            && tblClassDecl.TryGetValue("border-right", out var clsBr)
            && tblClassDecl.TryGetValue("border-bottom", out var clsBb)
            && tblClassDecl.TryGetValue("border-left", out var clsBl))
        {
            static bool Visible(string v) =>
                !v.Trim().StartsWith("0") && v.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0;
            if (Visible(clsBt) && Visible(clsBr) && Visible(clsBb) && Visible(clsBl))
            {
                double clsBw = 1 * PxToPt;
                var wm = Regex.Match(clsBt, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var clsBwv) && clsBwv > 0)
                    clsBw = clsBwv * PxToPt;
                var clsBc = ParseCssColor(clsBt) ?? Color.Black;
                outerBorder = new BorderInfo(BorderSide.Box, clsBw, clsBc);
                cellSideBorder ??= new BorderInfo(BorderSide.Box, clsBw, clsBc);
            }
        }
        if (ClassRule(" td") is { } tdRule)
        {
            if (tdRule.TryGetValue("border-top", out var btv))
            {
                var t = btv.Trim();
                if (!t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    double bw2 = 1 * PxToPt;
                    var wm = Regex.Match(t, @"(\d+(?:\.\d+)?)\s*px");
                    if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bwv) && bwv > 0)
                        bw2 = bwv * PxToPt;
                    cellSideBorder = new BorderInfo(BorderSide.Top, bw2, ParseCssColor(t) ?? Color.Black);
                }
            }
            if (cfg.pad <= 0 && tdRule.TryGetValue("padding", out var pv3) && TryParseLength(pv3, out var pp3))
                cfg.pad = pp3;
        }
        if (ClassRule(" th") is { } thRule)
        {
            if (thRule.TryGetValue("border-bottom", out var hbv))
            {
                var t = hbv.Trim();
                if (!t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    double hw = 1 * PxToPt;
                    var wm = Regex.Match(t, @"(\d+(?:\.\d+)?)\s*px");
                    if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hwv) && hwv > 0)
                        hw = hwv * PxToPt;
                    ps.headerBorder = new BorderInfo(BorderSide.Bottom, hw, ParseCssColor(t) ?? Color.Black);
                }
            }
            if (thRule.TryGetValue("text-align", out var hav))
                ps.headerAlign = hav.Trim().ToLowerInvariant() switch
                {
                    "left" => HorizontalAlignment.Left,
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => null,
                };
            if (cfg.pad <= 0 && thRule.TryGetValue("padding", out var pv4) && TryParseLength(pv4, out var pp4))
                cfg.pad = pp4;
        }

        if (cfg.padSide < 0) cfg.padSide = cfg.pad;
        if (cfg.padBottom < 0) cfg.padBottom = cfg.pad;
        var table = new Table
        {
            IsBordersIncluded = cfg.hasBorder || outerBorder is not null || cellSideBorder is not null,
            // Mixed run sizes: each line takes its own size's line box (see CssRunBoxes).
            CssRunBoxes = cssRunFace is not null,
            // A grid whose base face came from the document's own `body { font-family }`
            // draws its cells in that face through the Type0 path, with the face's real
            // kerned advances. Without this the cell writer falls back to the Standard-14
            // pair and silently retypesets the table in Helvetica while the prose around
            // it sets in the declared face.
            HonorCellTtfFaces = defaultCellFace is { Length: > 0 } || cfg.inlineFaceRatio > 0,
            InlineFaceGridRatio = cfg.inlineFaceRatio,
            DwFormCells = dwFormCells,
            // …and takes the UA's own separate-borders `border-spacing: 2px` unless the
            // table declares a cellspacing of its own.
            // The over-declared grid dialect carries REAL border spacing too: the
            // reference pitches every row a full cellspacing lower (probed: pitch
            // 20.25 at cellspacing=1, 21.0 at 2, 19.5 at 0; leading gap included),
            // and the shipped-era template needs the accumulation the modern
            // engine also has.
            RowSpacingPt = dwFormCells ? colModel.cellSpacingPt
                : cfg.uaDocGrid ? (colModel.cellSpacingPt > 0 ? colModel.cellSpacingPt : UaCellSpacingPt)
                : overDeclaredDraw ? (colModel.cellSpacingPt > 0 ? colModel.cellSpacingPt
                    : tblCellSpacingDeclared ? 0 : UaCellSpacingPt)
                : 0,
            HtmlOverDeclaredDraw = overDeclaredDraw,
            // The markup's cell rule is sized into the column by HtmlCellBorderPt.
            CellBorderInPitch = false,
        };
        if (cfg.hasBorder) table.DefaultCellBorder = new BorderInfo(BorderSide.Box, cfg.borderWidth, cfg.borderColor);
        // The border attribute frames the TABLE too (separate-borders model: the
        // outer 1px frame sits outside the cells' own borders, insetting the first
        // row by its width). The inline-face grid honours it; legacy corpora are
        // calibrated without the frame.
        if (cfg.hasBorder && cfg.inlineFaceRatio > 0 && outerBorder is null)
            table.Border = new BorderInfo(BorderSide.Box, cfg.borderWidth, cfg.borderColor);
        else if (cellSideBorder is not null) table.DefaultCellBorder = cellSideBorder;
        if (outerBorder is not null) table.Border = outerBorder;
        if (cfg.pad > 0) table.DefaultCellPadding = new MarginInfo(cfg.padSide, cfg.padBottom, cfg.padSide, cfg.pad);
        // The UA stylesheet's own `td, th { padding: 1px }` — 0.75 pt above and below
        // every cell's content box. Only the vertical pair is taken: the horizontal
        // grid is already calibrated off the measured column footprints.
        // …and a grid laid out on the browser's box model — its face and line box taken
        // from the document's own CSS — carries that UA padding too, since the same
        // stylesheet supplies both.
        else if (uaCellBoxes || cfg.uaDocGrid)
            table.DefaultCellPadding = new MarginInfo(0, UaCellPadPt, 0, UaCellPadPt);
        table.UaCellBoxes = uaCellBoxes;
        // Lifted nested tables render as real grids in place (measured into the row
        // plan, drawn by the slice pass); recursion levels inherit through the flag.
        table.NestedTableRender = liftNestedTables;
        // The declared cellspacing separates the rows VERTICALLY too: half a
        // spacing above and below each cell — the measured
        // row bands (row 1 = 69, flags = 44.3) hold once the reserve rows keep
        // their padding.
        // …and the side inset horizontally: the pills and grids keep a small white
        // gap off the row borders instead of touching them. The spacing also
        // separates each cell's BORDER BOX from the row band (HtmlRowSpacingPt).
        // Vertical decomposition of the row bands: the
        // border box insets HALF a spacing from the row band (the other half is
        // the visible white gap to the neighbouring row's border) and the content
        // keeps the UA pad inside the border — a section bar sits ~1 pt below its
        // border. Row heights follow from the tallest CONTENT
        // (row 1 = its Managers grid + these pads exactly).
        if (cfg.chainBase is not null && colModel.tblCellSpacingPt > 0 && table.DefaultCellPadding is null)
        {
            var vPad = colModel.tblCellSpacingPt / 2 + UaCellPadPt;
            table.DefaultCellPadding = new MarginInfo(ChainCellSideInsetPt, vPad,
                ChainCellSideInsetPt, vPad);
            table.HtmlRowSpacingPt = colModel.tblCellSpacingPt;
        }
        cfg.chainBorderSeparate = cfg.chainBase is not null && !tblCellSpacingDeclared && !((tblChainDecls is not null && tblChainDecls.TryGetValue("border-collapse", out var bcColl) && bcColl.Contains("collapse", StringComparison.OrdinalIgnoreCase)) || (cfg.tblStyle.TryGetValue("border-collapse", out var bcColl2) && bcColl2.Contains("collapse", StringComparison.OrdinalIgnoreCase)));
        cfg.chainSpacingPt = 0.0;
        if (cfg.chainBorderSeparate)
        {
            string? bsDecl = null;
            if (tblChainDecls is not null) tblChainDecls.TryGetValue("border-spacing", out bsDecl);
            if (bsDecl is null) cfg.tblStyle.TryGetValue("border-spacing", out bsDecl);
            if (bsDecl is not null)
            {
                var bsFirst = bsDecl.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (bsFirst.Length > 0 && ChainLenPt(bsFirst[0], cfg.cellFontSize) is > 0 and var bsPt)
                    cfg.chainSpacingPt = bsPt;
            }
        }
        if (cfg.chainSpacingPt > 0 && table.DefaultCellPadding is null)
        {
            table.DefaultCellPadding = new MarginInfo(cfg.chainSpacingPt / 2, cfg.chainSpacingPt / 2,
                cfg.chainSpacingPt / 2, cfg.chainSpacingPt / 2);
            table.HtmlCellSpacingBandPt = cfg.chainSpacingPt;
            // …and the draw insets each cell's border box by the same half band, so
            // the gap is real white space between the boxes and not thicker chrome.
            table.HtmlRowSpacingPt = cfg.chainSpacingPt;
        }
        else if (cfg.chainBorderSeparate && table.DefaultCellPadding is null)
            table.DefaultCellPadding = new MarginInfo(0, SeparateBorderSpacingPt / 2,
                0, SeparateBorderSpacingPt / 2);
        // a class rule can box the cells even when the table itself declares none
        foreach (var tcls in tblClasses)
        {
            Dictionary<string, string>? clsCellRule = null;
            if (!cfg.css.TryGetValue("." + tcls + " td", out clsCellRule))
                docCss?.TryGetValue("." + tcls + " td", out clsCellRule);
            if (clsCellRule is null || !clsCellRule.TryGetValue("border", out var clsBorder)) continue;
            var bm2 = Regex.Match(clsBorder, @"([\d.]+)\s*px");
            if (bm2.Success && double.TryParse(bm2.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var clsBwv) && clsBwv > 0)
            { table.HtmlCellBorderPt = clsBwv * PxToPt; table.HtmlCellBorderShared = true; break; }
        }
        if (tblBorderAttr is not null
            && double.TryParse(Regex.Match(tblBorderAttr, @"[\d.]+").Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var tblBw) && tblBw > 0)
        { table.HtmlCellBorderPt = tblBw * PxToPt; table.HtmlCellBorderShared = false; }
        table.HtmlAutoWidth = !cfg.tblTag.Success
            || !Regex.IsMatch(cfg.tblTag.Value, @"width\s*=\s*['""]?\s*[\d.]", RegexOptions.IgnoreCase);

        var tokens = Tokenize(StripNonContent(scanHtml));

        cfg.chainUnbold = new List<(string Tag, int PrevBoldDepth)>();
        // The innermost open element with an explicit display decides how a styled
        // run behaves: inside an inline-block box a size change RIDES the line
        // (the traffic-light letters); a block element still breaks it.
        // Record the span the current line holds for a box run (prefix = the text
        // before it, both collapsed the way PushLine will collapse them).
        // A chain-matched element opens an inline box (bg + inline-block: plates,
        // pills) or — inside an open box — a background-image badge whose text
        // becomes the badge letter.
        if (ps.cellFamily is not null)
            try { ps.measureFont = Text.FontRepository.TryFindFont(ps.cellFamily); } catch { }
        // Width of a run in the document's real SERIF face. The cells render through the
        // Standard-14 sans stand-in, which runs ~5% wide, so a box wrap measured with it
        // breaks a token one line earlier than the browser does. Wrapping is decided on
        // the authored face; only the drawn glyphs stay in the stand-in.
        // Min-content width: the widest single word — a wrappable cell ("Beginning Balance") can
        // shrink to its longest word ("Beginning"), so the column need only be that wide (matching
        // a browser's auto table layout). Single-token cells ("$0,000.00") keep their full width.
        return (cfg, ps, colModel, table, tokens);
    }
}
