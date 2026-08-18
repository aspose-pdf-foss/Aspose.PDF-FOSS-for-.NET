using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    internal static Table? BuildTableFromHtml(string html, double availWidthPt, out double naturalWidthPt,
        HtmlLoadOptions? options, List<byte[]>? inlineSvgs,
        IReadOnlyDictionary<string, Dictionary<string, string>>? docCss,
        bool bandDialect = false, bool widenProbe = false, double cellLineHeightPt = 0,
        double defaultCellFontPt = 0, bool tightExtras = false, bool liftNestedTables = false,
        bool uaCellBoxes = false, string? cssRunFace = null, Color? bodyTextColor = null,
        // The cells carry their own presentational styling — inline border sides and the
        // legacy ALIGN attribute — rather than inheriting a frame from the table.
        bool authoredCellChrome = false,
        // The Verdana form-grid fragment dialect (see Document.cs): legacy ALIGN
        // honored, and a sized &nbsp;-only run binds its active font (the grid's
        // 36pt spacer row) — scoped here so no calibrated dialect moves.
        bool formGridDialect = false,
        // The dialect's CSS strut: the ambient font's own line box, flooring
        // every cell line (Verdana-12 → 14.25 inside the wrapper's font tag,
        // the serif default's 13.5 outside). A td that styles its OWN
        // font-size restruts its cell at that size's box instead.
        double formGridStrutPt = 0,
        // …and the strut's baseline drop (half-leading + winAscent within the
        // strut box) — the floor every line's baseline seat takes.
        double formGridStrutDropPt = 0,
        // The document's base face, inherited by the grid like defaultCellFontPt.
        string? defaultCellFace = null,
        List<CssChainRule>? chainRules = null, List<CssElem>? cssAncestors = null,
        // Factory for a radio <input> in a cell: (group name, checked) → an option
        // already added to its RadioButtonField group. The CONVERTER owns the groups
        // (it registers them on doc.Form after layout); the cell carries each option
        // inline in its text via Table.InlineRadioChar markers. Null = radios are
        // dropped from cell text, the pre-form-grid behaviour.
        Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio = null)
    {
        naturalWidthPt = 0;
        const double PxToPt = 0.75;
        var css = ParseStyleSheet(html);

        // The document sheet's `a { color: … }` rule colours anchor text in cells
        // (the source renderer applies it like any inline colour).
        Color? docAnchorColor = null;
        if (docCss is not null && docCss.TryGetValue("a", out var docARule)
            && docARule.TryGetValue("color", out var docACol))
            docAnchorColor = ParseCssColor(docACol);

        // The <table> tag's own inline style="…" / attributes (font, width, border, cellpadding)
        // take precedence over stylesheet rules — CMS/report HTML commonly styles the table
        // inline rather than via a <style> block, so honour both.
        var tblTag = Regex.Match(html, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        var tblStyle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? tblBorderAttr = null, tblCellPadAttr = null, tblBorderColorAttr = null;
        var tblCellSpacingPt = 0.0;
        // cellspacing="0" is a DECLARATION (no spacing), distinct from the absent
        // attribute (which leaves the UA's default border-spacing in force).
        var tblCellSpacingDeclared = false;
        double tblHeightPx = 0;
        // The table's declared cellspacing, in points; 0 when it declares none.
        double cellSpacingPt = 0;
        if (tblTag.Success)
        {
            var sm = Regex.Match(tblTag.Value, @"style\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            // JSON-escaped dialect: the style value is an unquoted token truncated at the
            // first whitespace (see ParseAttributes) — parse the declarations it kept.
            if (!sm.Success && tblTag.Value.IndexOf("\\\"", StringComparison.Ordinal) >= 0)
                sm = Regex.Match(tblTag.Value, @"style\s*=\s*(\S+)", RegexOptions.IgnoreCase);
            if (sm.Success)
                foreach (Match d in StyleDeclRx.Matches(sm.Groups[1].Value))
                    tblStyle[d.Groups[1].Value.Trim().ToLowerInvariant()] = d.Groups[2].Value.Trim();
            var bm = Regex.Match(tblTag.Value, @"(?<!\w)border\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (bm.Success) tblBorderAttr = bm.Groups[1].Value;
            var bcAttr = Regex.Match(tblTag.Value, @"bordercolor\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (bcAttr.Success) tblBorderColorAttr = bcAttr.Groups[1].Value;
            var cm = Regex.Match(tblTag.Value, @"cellpadding\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
            if (cm.Success) tblCellPadAttr = cm.Groups[1].Value;
            var csAttr = Regex.Match(tblTag.Value, @"cellspacing\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (csAttr.Success && double.TryParse(csAttr.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var csPx))
            {
                tblCellSpacingPt = csPx * 0.75;
                tblCellSpacingDeclared = true;
                cellSpacingPt = tblCellSpacingPt;
            }
            var hm = Regex.Match(tblTag.Value, @"height\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (hm.Success) double.TryParse(hm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out tblHeightPx);
        }

        // Stylesheet rules addressed at the table's own class(es). The table-level
        // rule (".listTable { font/width/color }") merges UNDER the inline style;
        // ".cls td" / ".cls th" (the parser collapses ".cls tr td" to these) style
        // the cell grid. Border declarations stay out of the merge — a class border
        // is the table's OUTER frame, not a box on every cell.
        var tblClasses = new List<string>();
        if (tblTag.Success)
        {
            var clm = Regex.Match(tblTag.Value, @"class\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
            if (clm.Success)
                foreach (var c in clm.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    tblClasses.Add(c);
        }
        Dictionary<string, string>? ClassRule(string suffix)
        {
            foreach (var c in tblClasses)
            {
                var key = "." + c + suffix;
                if (css.TryGetValue(key, out var d)) return d;
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
                if (css.TryGetValue(key, out var d)) return d;
                if (docCss is not null && docCss.TryGetValue(key, out var d2)) return d2;
            }
            return null;
        }
        var tblClassDecl = ClassRule("");
        if (tblClassDecl is not null)
            foreach (var kv in tblClassDecl)
                if (kv.Key is "font-size" or "font-family" or "color" or "width" or "max-width"
                    && !tblStyle.ContainsKey(kv.Key))
                    tblStyle[kv.Key] = kv.Value;

        double cellFontSize = defaultCellFontPt > 0 ? defaultCellFontPt : 11;
        // Cells sized by a `font:` SHORTHAND rule render as CSS line boxes (1.2 em
        // pitch) — the form-document dialect; longhand-sized cells keep the legacy
        // uniform grid their tests are calibrated to.
        var cellFontShorthand = false;
        if (tblStyle.TryGetValue("font-size", out var itfs) && TryParseLength(itfs, out var itfsp)) cellFontSize = itfsp;
        else if (TryGetCssLength(css, "table", "font-size", out var tfs)) cellFontSize = tfs;
        else if (TryGetCssLength(css, "td", "font-size", out var dfs)) cellFontSize = dfs;
        else if ((CssFontShorthand(css, "table") ?? CssFontShorthand(css, "td")) is { } fsh)
        {
            cellFontSize = fsh.sizePt;
            cellFontShorthand = true;
        }
        // The <table> segment rarely carries the stylesheet — a document-level
        // `td { font: 10px Verdana }` (shorthand or longhand) sizes the cells too.
        else if (docCss is not null && TryGetCssLength(docCss, "table td", "font-size", out var dttdfs)) cellFontSize = dttdfs;
        else if (docCss is not null && TryGetCssLength(docCss, "table", "font-size", out var dtfs)) cellFontSize = dtfs;
        else if (docCss is not null && TryGetCssLength(docCss, "td", "font-size", out var ddfs)) cellFontSize = ddfs;
        else if (docCss is not null
            && (CssFontShorthand(docCss, "table") ?? CssFontShorthand(docCss, "td")) is { } dcfsh)
        {
            cellFontSize = dcfsh.sizePt;
            cellFontShorthand = true;
        }
        // The shorthand expansion leaves the `font:` declaration beside its generated
        // longhands, so the longhand branches above win the size resolution. The rule
        // is still AUTHORED as a shorthand — when one exists and agrees with the size
        // that won, the form-document cell dialect applies. A longhand that OVERRODE
        // the shorthand (differing size — the cascade's later-wins) keeps the flag off.
        if (!cellFontShorthand
            && (CssFontShorthand(css, "table") ?? CssFontShorthand(css, "td")
                ?? (docCss is null ? null : CssFontShorthand(docCss, "table") ?? CssFontShorthand(docCss, "td")))
                is { } anySh
            && Math.Abs(anySh.sizePt - cellFontSize) < 1e-9)
            cellFontShorthand = true;

        // The caller's document base face — the grid inherits the page's own `body { }`
        // family the same way it inherits `defaultCellFontPt`. Any rule the table or its
        // cells declare still wins below.
        string? cellFamily = defaultCellFace is { Length: > 0 } dcf ? dcf : null;
        if (tblStyle.TryGetValue("font-family", out var iff)) cellFamily = FirstFontFamily(iff);
        else if (css.TryGetValue("table", out var tdecl) && tdecl.TryGetValue("font-family", out var ffv))
            cellFamily = FirstFontFamily(ffv);
        else if ((CssFontShorthand(css, "table") ?? CssFontShorthand(css, "td")) is { family: not null } fshf)
            cellFamily = fshf.family;
        else if (docCss is not null
            && (CssFontShorthand(docCss, "table") ?? CssFontShorthand(docCss, "td")) is { family: not null } dcff)
            cellFamily = dcff.family;

        // Document-level stylesheet base (a `body, table, td { font: … }` rule outside this
        // <table> snippet). Consulted ONLY by the styled-cell (mixed per-line font) path so
        // unstyled lines inherit the true CSS size/family; the legacy cellFontSize above —
        // and every measurement that feeds column widths — is deliberately left untouched.
        double cssBasePt = 0;
        string? cssBaseFamily = cellFamily;
        if (tblStyle.TryGetValue("font-size", out var bfs) && TryParseLength(bfs, out var bfsp)) cssBasePt = bfsp;
        else if (TryGetCssLength(css, "table", "font-size", out var bts)) cssBasePt = bts;
        else if (TryGetCssLength(css, "td", "font-size", out var bds)) cssBasePt = bds;
        else if (docCss is not null && TryGetCssLength(docCss, "table td", "font-size", out var dttds)) cssBasePt = dttds;
        else if (docCss is not null && TryGetCssLength(docCss, "table", "font-size", out var dts)) cssBasePt = dts;
        else if (docCss is not null && TryGetCssLength(docCss, "td", "font-size", out var dds)) cssBasePt = dds;
        if (cssBaseFamily is null && docCss is not null)
        {
            if (docCss.TryGetValue("table td", out var dttdf) && dttdf.TryGetValue("font-family", out var dtf2))
                cssBaseFamily = FirstFontFamily(dtf2);
            else if (docCss.TryGetValue("table", out var dtd) && dtd.TryGetValue("font-family", out var dtf))
                cssBaseFamily = FirstFontFamily(dtf);
            else if (docCss.TryGetValue("td", out var ddd) && ddd.TryGetValue("font-family", out var ddf))
                cssBaseFamily = FirstFontFamily(ddf);
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
                    if ((!css.TryGetValue(rk, out var rcd)
                            && (docCss is null || !docCss.TryGetValue(rk, out rcd)))
                        || !rcd.TryGetValue("font-size", out var rfs)
                        || !TryParseLength(rfs, out var rfsp) || rfsp <= 0
                        || Math.Abs(rfsp - cellFontSize) < 0.01) continue;
                    mixed = true;
                    break;
                }
                if (mixed) break;
            }
            if (!mixed) cssRunFace = null;
        }
        // A grid whose type came from the DOCUMENT's own body rule and which carries no
        // run styling of its own is laid out on the browser's box model throughout: the
        // UA stylesheet supplies its line box, its `td { padding: 1px }` and its
        // `border-spacing: 2px`. A grid that DOES style its runs is already sized by that
        // dialect, whose padding and box model are calibrated separately.
        var uaDocGrid = cssRunFace is null && defaultCellFace is { Length: > 0 };

        bool hasBorder = false; double borderWidth = 1; Color borderColor = Color.Black; double pad = 0;
        // The horizontal share of the cell padding, and the bottom one. Equal to `pad`
        // (the top) unless a `padding` shorthand declares the sides separately
        // ("7px 5px 6px" = top 7, sides 5, bottom 6).
        double padSide = -1, padBottom = -1;
        // border="1"/"1px" attribute on the table draws a 1px box on every cell.
        if (tblBorderAttr is not null && !tblBorderAttr.StartsWith("0"))
        {
            hasBorder = true;
            var wm = Regex.Match(tblBorderAttr, @"(\d+(?:\.\d+)?)");
            if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bwa) && bwa > 0)
                borderWidth = bwa * PxToPt;
            // The legacy BORDERCOLOR attribute colours the grid (form-grid dialect
            // only — the calibrated dialects keep their black default).
            if (formGridDialect && tblBorderColorAttr is not null
                && ParseCssColor(tblBorderColorAttr) is { } bcaCol)
                borderColor = bcaCol;
        }
        if (tblCellPadAttr is not null && double.TryParse(Regex.Match(tblCellPadAttr, @"[\d.]+").Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cpa) && cpa > 0)
            pad = cpa * PxToPt;
        foreach (var sel in new[] { "td", "th" })
        {
            if (!css.TryGetValue(sel, out var d)) continue;
            if (d.TryGetValue("border", out var bd))
            {
                var t = bd.Trim();
                hasBorder = !t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0;
                var wm = Regex.Match(bd, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bw))
                    borderWidth = bw * PxToPt;
                var bc = ParseCssColor(bd); if (bc is not null) borderColor = bc;
            }
            if (d.TryGetValue("padding", out var pv) && TryParseLength(pv, out var pp)) pad = pp;
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
                hasBorder = !t.StartsWith("0") && t.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0;
                var wm = Regex.Match(cbd, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var cbw))
                    borderWidth = cbw * PxToPt;
                var cbc = ParseCssColor(cbd); if (cbc is not null) borderColor = cbc;
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
                    pad = cpp;
                    padSide = cps.Length > 1 && TryParseLength(cps[1], out var cpsv) ? cpsv : cpp;
                    // The bottom entry only under the CSS run dialect: the legacy grid is
                    // calibrated against the top value standing in for both.
                    padBottom = cssRunFace is not null && cps.Length > 2
                        && TryParseLength(cps[2], out var cpbv) ? cpbv : cpp;
                }
            }
        }
        // A `border: Npx …` declaration on the <table> tag's own style boxes every cell
        // like the border attribute; cell text then insets by the stroke width plus
        // the UA-default 1px cell padding. The stroke share of that inset comes from
        // the bordered cell box itself, so only the UA padding is added here.
        if (!hasBorder && tblStyle.TryGetValue("border", out var tbstv))
        {
            var tb = tbstv.Trim();
            if (!tb.StartsWith("0") && tb.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
            {
                hasBorder = true;
                var wm = Regex.Match(tb, @"(\d+(?:\.\d+)?)\s*px");
                if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tbw) && tbw > 0)
                    borderWidth = tbw * PxToPt;
                var tbc = ParseCssColor(tb); if (tbc is not null) borderColor = tbc;
                if (pad <= 0) pad = 1 * PxToPt;
            }
        }

        // A LONGHAND border triplet (`table, td { border-style: solid; border-color:
        // #333 }` + `td { border-width: 1px }`) boxes the cells like the shorthand —
        // form documents commonly split the declaration across document-level rules.
        // A table whose own style says border-style:none opts out.
        var tblBorderNone = tblStyle.TryGetValue("border-style", out var tbsNone)
            && tbsNone.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hasBorder && !tblBorderNone)
        {
            foreach (var cssSrc in new[] { css, docCss })
            {
                if (cssSrc is null || hasBorder) continue;
                foreach (var sel in new[] { "td", "table" })
                {
                    if (!cssSrc.TryGetValue(sel, out var d0)) continue;
                    if (!d0.TryGetValue("border-style", out var bsv)
                        || bsv.IndexOf("solid", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    hasBorder = true;
                    double maxPx = 1;
                    foreach (var src2 in new[] { css, docCss })
                        if (src2 is not null && src2.TryGetValue("td", out var dtd2)
                            && dtd2.TryGetValue("border-width", out var bwv))
                            foreach (Match mm in Regex.Matches(bwv, @"([\d.]+)\s*(px)?"))
                                if (double.TryParse(mm.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var bwp)
                                    && bwp > 0 && bwp < 10)
                                    maxPx = Math.Max(maxPx, bwp);
                    borderWidth = maxPx * PxToPt;
                    foreach (var src2 in new[] { css, docCss })
                        if (src2 is not null)
                            foreach (var sel2 in new[] { "td", "table" })
                                if (src2.TryGetValue(sel2, out var d2)
                                    && d2.TryGetValue("border-color", out var bcv)
                                    && ParseCssColor(bcv) is { } bcc)
                                { borderColor = bcc; break; }
                    if (pad <= 0)
                        foreach (var src2 in new[] { css, docCss })
                            if (src2 is not null && src2.TryGetValue("td", out var dtp)
                                && dtp.TryGetValue("padding", out var pdv) && TryParseLength(pdv, out var pdp))
                                pad = pdp;
                    break;
                }
            }
        }

        // Cells styled inline (`<td style="…border: #000 1px solid…">`) draw a cell box just
        // like a `td { border: … }` stylesheet rule — CMS/spreadsheet exports style each cell
        // inline and have no <style> block at all. Sample the first bordered cell.
        if (!hasBorder)
        {
            var cbm = Regex.Match(html, @"<t[dh]\b[^>]*style\s*=\s*[""'][^""']*border\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (cbm.Success)
            {
                var bd = cbm.Groups[1].Value.Trim();
                if (!bd.StartsWith("0") && bd.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    hasBorder = true;
                    var wm = Regex.Match(bd, @"(\d+(?:\.\d+)?)\s*px");
                    if (wm.Success && double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bw) && bw > 0)
                        borderWidth = bw * PxToPt;
                    var bc = ParseCssColor(bd); if (bc is not null) borderColor = bc;
                }
            }
        }

        // <colgroup><col width="N"> declares the column grid up front. The layout engine gives
        // each column max(declared width, min-content) — the declared width stretches for an
        // unbreakable run but never squeezes below it — and IGNORES per-cell style widths when
        // a colgroup is present (spreadsheet exports carry a bogus full-table width on every
        // cell, which would otherwise blow each column up to the table's own width).
        // A table nested inside a cell is a table in its own right, not part of the
        // outer grid: lift each one out behind a placeholder BEFORE any structure
        // scan (a nested table's COLGROUP must not become this table's column
        // grid), then build it on its own when the placeholder reaches the cell
        // that held it.
        var nestedHtml = new List<string>();
        var scanHtml = liftNestedTables ? ExtractNestedTables(html, nestedHtml) : html;
        List<double>? colGroupPt = null;
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
            (colGroupPt ??= new List<double>()).Add(wpx * PxToPt);
        }

        // The chain-selector pass: rules addressed through the document tree
        // (`#ReportTable .Managers > tbody > tr > td`) reach this table and its
        // cells. The chain seats the table on the ancestors the caller threaded in
        // (nested builds inherit the outer cell's chain) and grows to its cells.
        List<CssElem>? chainBase = null;
        Dictionary<string, string>? tblChainDecls = null;
        if (chainRules is { Count: > 0 } && liftNestedTables)
        {
            var te = new CssElem { Tag = "table" };
            if (tblTag.Success)
            {
                var im2 = Regex.Match(tblTag.Value, @"\bid\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase);
                if (im2.Success) te.Id = im2.Groups[1].Value;
                var cm3 = Regex.Match(tblTag.Value, @"\bclass\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
                if (cm3.Success)
                    te.Classes = cm3.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            }
            chainBase = cssAncestors is { Count: > 0 }
                ? new List<CssElem>(cssAncestors) : new List<CssElem>();
            chainBase.Add(te);
            tblChainDecls = MatchChainDecls(chainRules, chainBase);
        }

        double tableWidthFrac = 1.0;
        // True only when the markup/CSS actually DECLARES a table width — the frac
        // itself defaults to a full box, so it cannot stand in for "declared".
        var tableWidthDeclared = false;
        // …and only an ABSOLUTE declaration ("9.75in") can PIN the natural width:
        // a percent width resolves against the box, and when the min-content floors
        // overflow that box the sheet grows to them (a
        // status report's 100%-wide milestone grid widens the page).
        var tableWidthDeclaredAbs = false;
        string? twVal = tblStyle.TryGetValue("width", out var itw) ? itw
            : (css.TryGetValue("table", out var tw2) && tw2.TryGetValue("width", out var tw) ? tw : null);
        // A chain rule addressing the table itself ('.Managers { width: 100% }'
        // under '#ReportTable') declares its width like a flat rule would.
        // The flag says the declared width is a PERCENT of a box only known at draw:
        // such a grid emits PERCENT columns and never sizes the sheet, whichever
        // spelling declared it.
        var tableWidthPctOfBox = false;
        if (twVal is null && tblChainDecls is not null
            && tblChainDecls.TryGetValue("width", out var twChain))
        {
            twVal = twChain;
            tableWidthPctOfBox = true;
        }
        // …and so does the presentational ATTRIBUTE: `<table width="290">` is HTML4's
        // spelling of `width: 290px` and `width="100%"` of `width: 100%`. That pair is
        // the only width an email template gives its inner boxes; without it such a grid
        // was "undeclared" and its columns fell to min-content — a headline column one
        // letter wide.
        if (twVal is null && liftNestedTables && tblTag.Success
            && Regex.Match(tblTag.Value, @"\bwidth\s*=\s*[""']?\s*(\d+(?:\.\d+)?)\s*(%?)\s*[""'\s>]",
                RegexOptions.IgnoreCase) is { Success: true } twAttr)
        {
            var twAttrPct = twAttr.Groups[2].Value.Length > 0;
            twVal = twAttr.Groups[1].Value + (twAttrPct ? "%" : "px");
            tableWidthPctOfBox = twAttrPct;
        }
        if (twVal is not null && twVal.EndsWith("%", StringComparison.Ordinal)
            && double.TryParse(twVal.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var twp))
        {
            tableWidthFrac = Math.Clamp(twp / 100.0, 0.05, 1.0);
            tableWidthDeclared = true;
        }
        // An absolute declared width ("9.75in"), capped by a type-rule max-width
        // ("table { max-width: 6.25in }" — the CSS constraint the fixed width must
        // respect), pins the table box inside the available width.
        else if (twVal is not null && availWidthPt > 0 && TryParseLength(twVal, out var twAbs) && twAbs > 0)
        {
            string? maxWv = tblStyle.TryGetValue("max-width", out var imw) ? imw
                : (css.TryGetValue("table", out var mt) && mt.TryGetValue("max-width", out var mw1) ? mw1
                    : docCss is not null && docCss.TryGetValue("table", out var mdt) && mdt.TryGetValue("max-width", out var mw2) ? mw2 : null);
            if (maxWv is not null && TryParseLength(maxWv, out var mwPt) && mwPt > 0)
                twAbs = Math.Min(twAbs, mwPt);
            tableWidthFrac = Math.Clamp(twAbs / availWidthPt, 0.05, 1.0);
            tableWidthDeclared = true;
            tableWidthDeclaredAbs = true;
        }

        // Cell-grid styling addressed through the table's class: side-specific cell
        // borders (".listTable td { border-top: … }" — the row-rule style), cell
        // padding, the header row's own bottom rule and alignment, and the class's
        // text colour. The class's own border strokes the table frame, not cells.
        BorderInfo? outerBorder = null;
        BorderInfo? cellSideBorder = null;
        BorderInfo? headerBorder = null;
        HorizontalAlignment? headerAlign = null;
        Color? cellTextColor = null;
        if (tblClassDecl is not null && tblClassDecl.TryGetValue("color", out var ctv))
            cellTextColor = ParseCssColor(ctv);
        else if (tblStyle.TryGetValue("color", out var ctv2))
            cellTextColor = ParseCssColor(ctv2);
        // …else the page stylesheet's own body colour, which the grid inherits: these
        // pages set a soft grey (`body { color: #444 }`) that the black default ignores.
        else cellTextColor ??= bodyTextColor;
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
            if (pad <= 0 && tdRule.TryGetValue("padding", out var pv3) && TryParseLength(pv3, out var pp3))
                pad = pp3;
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
                    headerBorder = new BorderInfo(BorderSide.Bottom, hw, ParseCssColor(t) ?? Color.Black);
                }
            }
            if (thRule.TryGetValue("text-align", out var hav))
                headerAlign = hav.Trim().ToLowerInvariant() switch
                {
                    "left" => HorizontalAlignment.Left,
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => null,
                };
            if (pad <= 0 && thRule.TryGetValue("padding", out var pv4) && TryParseLength(pv4, out var pp4))
                pad = pp4;
        }

        if (padSide < 0) padSide = pad;
        if (padBottom < 0) padBottom = pad;
        var table = new Table
        {
            IsBordersIncluded = hasBorder || outerBorder is not null || cellSideBorder is not null,
            // Mixed run sizes: each line takes its own size's line box (see CssRunBoxes).
            CssRunBoxes = cssRunFace is not null,
            // A grid whose base face came from the document's own `body { font-family }`
            // draws its cells in that face through the Type0 path, with the face's real
            // kerned advances. Without this the cell writer falls back to the Standard-14
            // pair and silently retypesets the table in Helvetica while the prose around
            // it sets in the declared face.
            HonorCellTtfFaces = defaultCellFace is { Length: > 0 },
            // …and takes the UA's own separate-borders `border-spacing: 2px` unless the
            // table declares a cellspacing of its own.
            RowSpacingPt = uaDocGrid ? (cellSpacingPt > 0 ? cellSpacingPt : UaCellSpacingPt) : 0,
        };
        if (hasBorder) table.DefaultCellBorder = new BorderInfo(BorderSide.Box, borderWidth, borderColor);
        else if (cellSideBorder is not null) table.DefaultCellBorder = cellSideBorder;
        if (outerBorder is not null) table.Border = outerBorder;
        if (pad > 0) table.DefaultCellPadding = new MarginInfo(padSide, padBottom, padSide, pad);
        // The UA stylesheet's own `td, th { padding: 1px }` — 0.75 pt above and below
        // every cell's content box. Only the vertical pair is taken: the horizontal
        // grid is already calibrated off the measured column footprints.
        // …and a grid laid out on the browser's box model — its face and line box taken
        // from the document's own CSS — carries that UA padding too, since the same
        // stylesheet supplies both.
        else if (uaCellBoxes || uaDocGrid)
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
        if (chainBase is not null && tblCellSpacingPt > 0 && table.DefaultCellPadding is null)
        {
            var vPad = tblCellSpacingPt / 2 + UaCellPadPt;
            table.DefaultCellPadding = new MarginInfo(ChainCellSideInsetPt, vPad,
                ChainCellSideInsetPt, vPad);
            table.HtmlRowSpacingPt = tblCellSpacingPt;
        }
        // A chain table WITHOUT border-collapse keeps the UA's default 2px
        // border-spacing: its rows pitch half a spacing wider on each side and its
        // cell borders draw a spacing thicker (the visible white separation).
        var chainBorderSeparate = chainBase is not null && !tblCellSpacingDeclared
            && !((tblChainDecls is not null && tblChainDecls.TryGetValue("border-collapse", out var bcColl)
                    && bcColl.Contains("collapse", StringComparison.OrdinalIgnoreCase))
                || (tblStyle.TryGetValue("border-collapse", out var bcColl2)
                    && bcColl2.Contains("collapse", StringComparison.OrdinalIgnoreCase)));
        // A DECLARED border-spacing is a real gap band on all four sides of every
        // cell (`.5ex` on the risks pill = 2 pt: its three cells sit 2 pt apart and
        // 2 pt inside the grid's edges). The UA's implicit 2 px default keeps the
        // calibrated vertical-only band below — declaring the property is what turns
        // the horizontal half on.
        var chainSpacingPt = 0.0;
        if (chainBorderSeparate)
        {
            string? bsDecl = null;
            if (tblChainDecls is not null) tblChainDecls.TryGetValue("border-spacing", out bsDecl);
            if (bsDecl is null) tblStyle.TryGetValue("border-spacing", out bsDecl);
            if (bsDecl is not null)
            {
                var bsFirst = bsDecl.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (bsFirst.Length > 0 && ChainLenPt(bsFirst[0], cellFontSize) is > 0 and var bsPt)
                    chainSpacingPt = bsPt;
            }
        }
        if (chainSpacingPt > 0 && table.DefaultCellPadding is null)
        {
            table.DefaultCellPadding = new MarginInfo(chainSpacingPt / 2, chainSpacingPt / 2,
                chainSpacingPt / 2, chainSpacingPt / 2);
            table.HtmlCellSpacingBandPt = chainSpacingPt;
            // …and the draw insets each cell's border box by the same half band, so
            // the gap is real white space between the boxes and not thicker chrome.
            table.HtmlRowSpacingPt = chainSpacingPt;
        }
        else if (chainBorderSeparate && table.DefaultCellPadding is null)
            table.DefaultCellPadding = new MarginInfo(0, SeparateBorderSpacingPt / 2,
                0, SeparateBorderSpacingPt / 2);
        // a class rule can box the cells even when the table itself declares none
        foreach (var tcls in tblClasses)
        {
            Dictionary<string, string>? clsCellRule = null;
            if (!css.TryGetValue("." + tcls + " td", out clsCellRule))
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
        table.HtmlAutoWidth = !tblTag.Success
            || !Regex.IsMatch(tblTag.Value, @"width\s*=\s*['""]?\s*[\d.]", RegexOptions.IgnoreCase);

        const double PxToPtW = 0.75;
        var tokens = Tokenize(StripNonContent(scanHtml));
        // <table> nesting depth: 1 inside the outer table, >1 inside a table
        // nested in a cell (whose structure tags must not drive the outer grid).
        var tableDepth = 0;
        Row? row = null; Cell? cell = null;
        var line = new StringBuilder();
        var lines = new List<(string Text, double FontPt, string? Family, bool Keep, bool JoinNext,
            List<(string Text, string Url)>? Anchors, bool Bold, double MarginTopPt, double MarginLeftPt,
            Color? Color, bool Italic)>();
        // Form-grid: a line whose bold state TOGGLES mid-run (the owner band's
        // 'Owner Team: <b>bv Designers</b>') carries per-run segments, keyed by
        // the line's index at push — the render draws each in its own face.
        Dictionary<int, List<(string Text, bool Bold)>>? lineRunsByIdx = null;
        List<(int Pos, bool Bold)>? lineRunMarks = null;
        // Cell images seen AFTER text on the same cell — appended to the cell's
        // paragraphs at CloseCell, after the text lines flush, to keep markup order.
        List<Image>? pendingCellImgs = null;
        // Tables lifted out of the current cell, added when it closes — each with the
        // LINE INDEX it anchored at, so the cell's paragraph order interleaves text
        // and grids the way the markup does (text after a grid stays below it).
        List<(Table T, int AnchorLine)>? pendingCellTables = null;
        // Line indices (per cell) of blanks pushed by a LONE <br> — see the br case.
        HashSet<int>? loneBrBlankLines = null;
        double pendingCellTablesNatW = 0;
        double pendingCellTablesPrefW = 0;
        // Column indices whose cells hold a nested grid (those columns stretch to
        // absorb the table's surplus — the grid fills them).
        HashSet<int>? nestedTableCols = null;
        // Inline <a href> tracking: the open anchor's start offset in `line`, and the
        // anchors already closed on the current line (inner text + URL).
        (int Start, string Url)? openAnchor = null;
        List<(string Text, string Url)>? lineAnchors = null;
        // <strong>/<b> nesting; a line is Bold only when EVERY text run on it arrived
        // while bold was active (mixed lines keep the regular face).
        var boldDepth = 0;
        var lineHadText = false;
        var lineAllBold = true;
        // `font-style: italic` nesting (style attributes on the run tags or the td
        // itself — the form-grid band titles); same all-runs rule as bold.
        var italicDepth = 0;
        var lineAllItalic = true;
        // Inline style context inside the current cell: <p>/<span>/<font> tags carrying a
        // font-size / font-family set the style of the lines they enclose. A line binds the
        // style active when its first text arrived (or when an explicit <br> created it),
        // so a close tag after the text doesn't retroactively restyle the line.
        var styleStack = new List<(string Tag, double PrevPt, string? PrevFamily, bool BoldBump,
            Color? PrevColor, bool ItalicBump)>();
        double curFontPt = 0; string? curFamily = null;
        // Colour declared by an enclosing run (`<p style="color:#004178">`): it belongs
        // to the LINE, not the cell — a coloured heading and a black paragraph are two
        // <p>s in one cell.
        Color? curColor = null; Color? lineColor = null;
        double lineFontPt = 0; string? lineFamily = null; bool lineStyleSet = false;
        // margin-top / margin-left of the <p> that opened the current line (band
        // dialect: they become the fragment's margins so a spacer gap above — and
        // the indent beside — a styled cell paragraph survive into the generator's
        // cell layout).
        double lineMarginTop = 0;
        double lineMarginLeft = 0;
        // Row-level defaults from the <tr>: its font-size cascades to cells that declare
        // none (so a `<tr style="font-size:1pt">` spacer row measures thin), and the tallest
        // cell HEIGHT="N" in the row floors the row height (an HTML row-height minimum).
        double rowFontPt = 0, rowMinHeightPt = 0;
        var rowMinHeightIsContent = false;
        // A <br> closed the cell's last line and no real text followed: the
        // break still opens a line box, which the row must be tall enough for.
        var cellPendingBrBlank = false;
        // Radio options collected for the CURRENT cell, one per inline marker char
        // appended to its line text, in document order; attached to the cell's
        // fragments at CloseCell (each fragment takes as many options as it holds
        // markers).
        List<Aspose.Pdf.Forms.RadioButtonOptionField>? cellInlineOptions = null;
        // Open <ol>/<ul> nesting in the cell walk: (ordered?, items seen). An <li>
        // breaks the line, prepends its marker ("1." / "•") and hangs it left of the
        // UA list indent; every line inside the item seats ON the indent.
        var listNesting = new List<(bool Ordered, int Count)>();
        double liStandingIndentPt = 0;
        // A pre/pre-wrap box was just opened: the next text token still carries the
        // newline that followed the tag, and that newline is content.
        var preWrapPending = false;
        bool isHeader = false; int colSpan = 1;
        // the cell's own weight and size, from its style or one of its classes
        var cellBold = false;
        var cellClassPt = 0.0;
        // ROWSPAN occupancy for the widen probe's column mapping: a row-spanning cell
        // keeps its column(s) occupied in the following rows, so their cells shift
        // right the way the HTML grid algorithm places them. (Probe-only: the render
        // grid has its own rowspan handling, and the calibrated legacy mapping is
        // left untouched.)
        int cellRowSpan = 1;
        var rowspanOcc = new List<(int col, int span, int remaining)>();
        HorizontalAlignment cellAlign = HorizontalAlignment.Left; bool alignSet = false;
        // ALIGN declared on the current <tr> — the fallback for its cells.
        HorizontalAlignment? rowAlign = null;
        int maxCols = 0;
        // Leading rows whose cells are all <th> are the table header; count them so they can be
        // repeated at the top of every page the table spans (RepeatingRowsCount).
        int headerRows = 0; bool countingHeaderRows = true; bool rowHasTd = false, rowHasCell = false;

        // Explicit per-column widths (points): captured from the first row whose cells are all
        // single-span and each carry an explicit CSS width (inline `width:Npx` or a class rule),
        // so a label : value table keeps its narrow ":" column instead of equal thirds.
        double cellWidthPt = 0;
        // Per-cell CSS padding (pt) and the widest fixed-width inner <div> (pt) — see
        // the CloseCell measurement: the div box, not its wrapped token, sizes the column.
        // The left half is kept apart because it also INDENTS the drawn text.
        double cellCssPadPt = 0, cellFixedDivPt = 0, cellPadLeftPt = 0;
        // Form-grid: a td styling its OWN font-size carries that size's strut
        // (the Description band's 10pt td rows at the 16px box, not the ambient),
        // and the strut's own baseline drop follows that size too.
        var cellFgStrutPt = 0.0;
        var cellFgStrutFontPt = 0.0;
        // The cell's own inline `line-height` (pt) — pitches its text lines.
        double cellOwnLineHPt = 0;
        // True when the cell declares an explicit height (attr or style): its box is
        // already fixed, and its internal pitch stays on the legacy model — an
        // embedded document's own line-height cascade lives inside such cells.
        var cellOwnHeightDecl = false;
        // The previous paragraph's margin-bottom in this cell (pt) — CSS-collapsed
        // into the next paragraph's gap.
        double cellPrevPBottomPt = 0;
        // Widest image the cell draws (pt). An image is replaced content: it never
        // wraps, so its box is the cell's min- AND max-content width. Email templates
        // build their whole column grid out of `<img width="15" height="1">` spacer
        // GIFs, and a text-only measure leaves every one of those columns empty.
        double cellImgWidthPt = 0;
        // Chain-selector state for the current cell: its own element node, the
        // div/span elements open inside it, and the vertical padding / text colour
        // a matched rule contributed (consumed at CloseCell).
        CssElem? chainTdElem = null;
        var chainOpenElems = chainBase is not null ? new List<CssElem>() : null;
        double cellChainPadTopPt = 0, cellChainPadBotPt = 0;
        Color? cellChainColor = null;
        // font-weight:normal on a chain-matched run CANCELS an enclosing bold
        // (`.SmallerTitle` under a bold title plate): the open stashes boldDepth,
        // the close restores it.
        var chainUnbold = new List<(string Tag, int PrevBoldDepth)>();
        // Open inline-box runs (title plates / status pills) and the per-line
        // segments they resolve to (consumed at CloseCell into InlineBoxDecorations).
        List<ChainBoxRun>? chainBoxOpen = null;
        List<(int LineIdx, ChainBoxRun Run, string Prefix, string Text)>? cellBoxSegs = null;
        // While a TrafficLight subtree is open, its text is the circle's letter,
        // not line content.
        CssElem? chainTrafficElem = null;
        ChainBoxRun? chainTrafficRun = null;
        // A rounded-capsule div (bg + border-radius) wrapping the NEXT lifted
        // table: (fill, corner radius, horizontal pad, vertical pad).
        (Color Fill, double RadiusPt, double PadHPt, double PadVPt, double MarginPt)? pendingCapsule = null;
        List<CssElem> BuildOpenChain()
        {
            var ch = new List<CssElem>(chainBase!);
            if (chainTdElem is not null) ch.Add(chainTdElem);
            if (chainOpenElems is not null) ch.AddRange(chainOpenElems);
            return ch;
        }
        // The innermost open element with an explicit display decides how a styled
        // run behaves: inside an inline-block box a size change RIDES the line
        // (the traffic-light letters); a block element still breaks it.
        string? EffectiveChainDisplay()
        {
            if (chainOpenElems is null) return null;
            for (var k = chainOpenElems.Count - 1; k >= 0; k--)
                if (chainOpenElems[k].Display is { } dsp) return dsp;
            return null;
        }
        // Record the span the current line holds for a box run (prefix = the text
        // before it, both collapsed the way PushLine will collapse them).
        void AddBoxSeg(ChainBoxRun r)
        {
            var raw = line.ToString();
            var start = Math.Min(r.StartLen, raw.Length);
            var boxText = CollapseWs(raw[start..]);
            if (boxText.Length == 0 && r.CircleFill is null) return;
            (cellBoxSegs ??= new List<(int, ChainBoxRun, string, string)>())
                .Add((lines.Count, r, CollapseWs(raw[..start]), boxText));
        }
        // A chain-matched element opens an inline box (bg + inline-block: plates,
        // pills) or — inside an open box — a background-image badge whose text
        // becomes the badge letter.
        void ChainBoxOpenMaybe(CssElem el, Dictionary<string, string> decls)
        {
            if (cell is null || chainTdElem is null) return;
            if (BackgroundBadge(decls, options) is { } badge)
            {
                // Inside an open box (a status pill) the badge is its trailing
                // circle; standing alone (the risks grid's category cells) it is
                // its OWN circle-only box run.
                ChainBoxRun host;
                if (chainBoxOpen is { Count: > 0 })
                    host = chainBoxOpen[^1];
                else
                {
                    host = new ChainBoxRun { Elem = el, StartLen = line.Length };
                    (chainBoxOpen ??= new List<ChainBoxRun>()).Add(host);
                }
                host.CircleFill = badge.Fill;
                host.CircleD = badge.DiameterPt;
                if (decls.TryGetValue("color", out var bcol) && ParseCssColor(bcol) is { } bcolc)
                    host.CircleLetterColor = bcolc;
                chainTrafficElem = el;
                chainTrafficRun = host;
                return;
            }
            if ((decls.TryGetValue("background-color", out var bgv)
                    || decls.TryGetValue("background", out bgv))
                && ParseCssColor(bgv) is { } bfill
                && (el.Display ?? EffectiveChainDisplay()) == "inline-block")
            {
                var runFontPt = curFontPt > 0 ? curFontPt : cellClassPt > 0 ? cellClassPt : cellFontSize;
                var run = new ChainBoxRun { Elem = el, StartLen = line.Length, Fill = bfill };
                if (decls.TryGetValue("padding", out var bpv))
                {
                    var (bt, br3, bb, bl3) = ChainPadPt(bpv, runFontPt);
                    run.PadT = bt; run.PadR = br3; run.PadB = bb; run.PadL = bl3;
                }
                if (decls.TryGetValue("border-radius", out var brv))
                    run.Radius = Math.Max(0, ChainLenPt(brv, runFontPt));
                if (decls.TryGetValue("height", out var hv2))
                    run.DeclH = Math.Max(0, ChainLenPt(hv2, runFontPt));
                if (decls.TryGetValue("letter-spacing", out var lsv))
                    run.LetterSpacing = Math.Max(0, ChainLenPt(lsv, runFontPt));
                (chainBoxOpen ??= new List<ChainBoxRun>()).Add(run);
            }
            // A plain styled run inside an open box: its padding-top spaces the
            // box's continuation line (the smaller ID line under a title plate).
            else if (chainBoxOpen is { Count: > 0 }
                && decls.TryGetValue("padding-top", out var cptv))
            {
                var cptBase = curFontPt > 0 ? curFontPt
                    : cellClassPt > 0 ? cellClassPt : cellFontSize;
                chainBoxOpen[^1].ContPadTop = Math.Max(0, ChainLenPt(cptv, cptBase));
            }
        }
        void ChainBoxCloseMaybe(CssElem el)
        {
            if (chainBoxOpen is { Count: > 0 } && ReferenceEquals(chainBoxOpen[^1].Elem, el))
            {
                AddBoxSeg(chainBoxOpen[^1]);
                chainBoxOpen.RemoveAt(chainBoxOpen.Count - 1);
            }
            if (ReferenceEquals(chainTrafficElem, el))
            {
                chainTrafficElem = null;
                chainTrafficRun = null;
            }
        }
        var rowWidths = new List<double>();
        bool rowAllSingleExplicit = true;
        List<double>? colWidthsPt = null;
        // Legacy WIDTH="N%" cell attributes (the classic empty sizing row of filing
        // HTML) declare the column split as fractions of the table width. Tracked
        // per column from single-span cells; honoured only when EVERY column got one.
        double cellWidthPct = 0;
        var colPctW = new List<double>();

        // Content-based auto width: per column, the min-content width (widest single word — the
        // narrowest the column can be while still wrapping) and the max-content width (widest full
        // line — no wrapping). A browser uses max-content when the table fits and shrinks toward
        // min-content otherwise. Tracks the current column cursor per row.
        var colMinW = new List<double>();
        var colMaxW = new List<double>();
        // Widest DECLARED cell width per column (pt, incl. cell padding): auto layout
        // treats a declared width as the column's floor, so an overflowing table's
        // min-content columns still honour it.
        var colDeclW = new List<double>();
        var colHdrW = new List<double>();
        // Dash-aware min-content (a token breaks after each hyphen/en-dash): the floor the
        // declared-percent grid uses, so "B13-9876" can wrap to "B13-"/"9876" instead of
        // holding its column at the full unbroken token width.
        var colMinBrkW = new List<double>();
        // Column-spanning cells constrain the SUM of the columns they cross, not each one;
        // recorded here and resolved after all single-column widths are known.
        var spanConstraints = new List<(int start, int span, double min, double max, double hdr)>();
        int colCursor = 0;
        Text.Font? measureFont = null;
        if (cellFamily is not null)
            try { measureFont = Text.FontRepository.FindFont(cellFamily); } catch { }
        // Probe measure resolves each run's own face (family + real bold metrics) —
        // the min-content the page-widen matches is computed from the styles the runs
        // actually render with, not the table's base face. Cached per (family, bold).
        Dictionary<(string fam, bool bold), Text.Font?>? probeFonts = null;
        Text.Font? ResolveProbeFont(string? fam, bool bold)
        {
            fam ??= cellFamily;
            if (fam is null) return null;
            probeFonts ??= new();
            if (probeFonts.TryGetValue((fam, bold), out var cached)) return cached;
            Text.Font? r = null;
            try { r = Text.FontRepository.FindFont(fam, bold ? Text.FontStyles.Bold : Text.FontStyles.Regular, ignoreCase: true); }
            catch { }
            probeFonts[(fam, bold)] = r;
            return r;
        }
        double MeasureLine(string s, bool bold = false, double pt = 0, string? fam = null)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var size = pt > 0 ? pt : cellFontSize;
            // An inline button's caption measures as text plus the button chrome —
            // face pads + outline outsets, em-scaled from the 12 pt probe (the
            // markers themselves have no glyphs).
            if (s.IndexOf(Table.InlineButtonChar) >= 0)
            {
                var bn = 0;
                foreach (var bc in s) if (bc == Table.InlineButtonChar) bn++;
                return MeasureLine(s.Replace(Table.InlineButtonChar.ToString(), "")
                        .Replace(Table.InlineButtonEndChar.ToString(), ""), bold, size, fam)
                    + bn * Table.InlineButtonChromePt * size / 12.0;
            }
            // Probe lines mark superscript/subscript runs with sentinel chars; those
            // runs measure at 85% of the line's size (the CSS the filing dialect uses),
            // the way the rendered glyphs shrink.
            if (s.IndexOfAny(ProbeSentinels) >= 0)
            {
                double total = 0; var sup = false; var bDepth = 0; var st = 0;
                for (var i = 0; i <= s.Length; i++)
                    if (i == s.Length || s[i] is >= '\uE000' and <= '\uE003')
                    {
                        if (i > st) total += MeasureLine(s[st..i], bold || bDepth > 0, sup ? size * 0.85 : size, fam);
                        if (i < s.Length)
                            switch (s[i])
                            {
                                case '\uE000': bDepth++; break;
                                case '\uE001': bDepth = Math.Max(0, bDepth - 1); break;
                                default: sup = s[i] == '\uE002'; break;
                            }
                        st = i + 1;
                    }
                return total;
            }
            try
            {
                var mf = measureFont;
                if (widenProbe && (bold || fam is not null))
                    mf = ResolveProbeFont(fam, bold) ?? measureFont;
                // A system font resolved via FindFont has an empty PDF font dict (no /Widths),
                // so Font.MeasureString would default every glyph to 1 em. Read the real glyph
                // advances from the source TTF (hmtx) instead when available.
                if (mf?.SourceFontData?.TtfData is { Length: > 0 })
                    return mf.SourceFontData.MeasureString(s, size);
                if (mf is not null) return mf.MeasureString(s, size);
                // No resolved font: the cell renders in a Standard-14 face (Helvetica, or
                // Helvetica-Bold for a <th>). Measure with that face's AFM advances so the
                // column width matches the rendered text — a flat 0.5-em average underestimates
                // real glyph widths (and a bold header most of all), wrapping a header that
                // should stay on one line (multi-word headers stay whole).
                var baseFont = bold ? "Helvetica-Bold" : "Helvetica";
                double total = 0;
                foreach (var ch in s) total += Text.Standard14Fonts.GetWidth(baseFont, ch);
                if (total > 0) return total / 1000.0 * size;
            }
            catch { }
            return s.Length * size * 0.5; // fallback: average glyph advance
        }
        // Width of a run in the document's real SERIF face. The cells render through the
        // Standard-14 sans stand-in, which runs ~5% wide, so a box wrap measured with it
        // breaks a token one line earlier than the browser does. Wrapping is decided on
        // the authored face; only the drawn glyphs stay in the stand-in.
        double MeasureSerifLine(string s, bool bold, double pt)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var size = pt > 0 ? pt : cellFontSize;
            var face = bold ? "Times-Bold" : "Times-Roman";
            double total = 0;
            foreach (var ch in s) total += Text.Standard14Fonts.GetWidth(face, ch);
            return total / 1000.0 * size;
        }
        // Min-content width: the widest single word — a wrappable cell ("Beginning Balance") can
        // shrink to its longest word ("Beginning"), so the column need only be that wide (matching
        // a browser's auto table layout). Single-token cells ("$0,000.00") keep their full width.
        double MeasureMinContent(string s, bool bold = false, double pt = 0, string? fam = null,
            bool breakDashes = false)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double w = 0;
            foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // The widen probe honours soft break opportunities after hyphens and
                // en-dashes, matching the min-content measure — an
                // unbreakable token ends after each dash (the segment keeps its
                // trailing dash), regardless of the surrounding character classes.
                // Slashes and non-breaking spaces are NOT opportunities.
                // The declared-percent grid asks for the same dash-aware floors
                // (breakDashes) — its min-content columns break at hyphens too.
                if ((widenProbe || breakDashes) && (word.IndexOf('-') > 0 || word.IndexOf('–') > 0))
                {
                    var start = 0;
                    for (var ci = 0; ci < word.Length; ci++)
                        if (word[ci] is '-' or '–' || ci == word.Length - 1)
                        {
                            w = Math.Max(w, MeasureLine(word.Substring(start, ci - start + 1), bold, pt, fam));
                            start = ci + 1;
                        }
                    continue;
                }
                w = Math.Max(w, MeasureLine(word, bold, pt, fam));
            }
            return w;
        }

        void PushLine(bool keepIfBlank = false, bool joinNext = false)
        {
            // A box run still open at a line break closes its segment on this line
            // and re-opens at the start of the next (title plates wrap onto the
            // smaller ID line).
            if (chainBoxOpen is { Count: > 0 })
                foreach (var br0 in chainBoxOpen)
                {
                    AddBoxSeg(br0);
                    br0.StartLen = 0;
                }
            // An anchor still open at a line break contributes its text so far to this
            // line and re-opens at the start of the next.
            if (openAnchor is { } oa0)
            {
                var part = CollapseWs(line.ToString()[oa0.Start..]);
                if (part.Length > 0) (lineAnchors ??= new()).Add((part, oa0.Url));
                openAnchor = (0, oa0.Url);
            }
            // The widen probe keeps &nbsp; (U+00A0) intact — it is NOT a break
            // opportunity, so nbsp-joined runs measure as one unbreakable token
            // (.NET's \s matches U+00A0, so the normal collapse would split them).
            var text = widenProbe
                ? Regex.Replace(line.ToString(), "[^\\S ]+", " ").Trim(' ')
                : CollapseWs(line.ToString());
            // A cell whose only content is a zero-width space holds a real line box all
            // the same — the browser gives that row a line's height. (The character
            // itself rides along in ordinary text: it is a soft break opportunity for
            // the wrap and is dropped when the line is drawn — see Table.StripZeroWidth.)
            var zwsOnly = text.Length > 0 && text.Trim(ZeroWidthSpace).Length == 0;
            if (zwsOnly) text = "";
            // Mixed bold runs on one line (form-grid): rebuild the run segments
            // against the raw buffer, keeping a single space at a run boundary the
            // source had whitespace at; discard unless they reconcile with the
            // collapsed line exactly.
            if (lineRunMarks is { Count: > 1 } && text.Length > 0)
            {
                var raw = line.ToString();
                var runSegs = new List<(string Text, bool Bold)>();
                for (var mi = 0; mi < lineRunMarks.Count; mi++)
                {
                    var segEnd = mi + 1 < lineRunMarks.Count ? lineRunMarks[mi + 1].Pos : raw.Length;
                    var rawSeg = raw[lineRunMarks[mi].Pos..segEnd];
                    var segText = CollapseWs(rawSeg);
                    if (segText.Length == 0) continue;
                    if (segEnd < raw.Length && char.IsWhiteSpace(raw[segEnd - 1])) segText += " ";
                    if (runSegs.Count > 0 && runSegs[^1].Bold == lineRunMarks[mi].Bold)
                        runSegs[^1] = (runSegs[^1].Text + segText, runSegs[^1].Bold);
                    else
                        runSegs.Add((segText, lineRunMarks[mi].Bold));
                }
                var joined = string.Concat(runSegs.ConvertAll(r => r.Text));
                if (runSegs.Count > 1 && joined == text)
                    (lineRunsByIdx ??= new())[lines.Count] = runSegs;
            }
            lineRunMarks = null;
            lines.Add((text, text.Length > 0 || keepIfBlank || zwsOnly ? lineFontPt : 0,
                lineFamily, (keepIfBlank || zwsOnly) && text.Length == 0, joinNext, lineAnchors,
                lineHadText && lineAllBold, lineMarginTop,
                // A line inside an <li> that set no indent of its own (a <br>
                // continuation, the answer under a question caption) seats on the
                // item's standing list indent.
                lineMarginLeft > 0 ? lineMarginLeft : liStandingIndentPt, lineColor,
                lineHadText && lineAllItalic));
            lineAnchors = null;
            line.Clear();
            lineFontPt = 0; lineFamily = null; lineStyleSet = false; lineMarginTop = 0; lineMarginLeft = 0;
            lineColor = null;
            lineHadText = false; lineAllBold = true; lineAllItalic = true;
        }
        void CloseCell()
        {
            if (cell is null || row is null) return;
            // A declared width is the cell's CONTENT box: its own horizontal padding
            // rides on top of it in the column footprint, the way it does in every other
            // measure here. An image column declaring `width="150"` plus a 20 px
            // padding-right is a 170 px column — the gutter between it and the text
            // column beside it was being dropped.
            rowWidths.Add(cellWidthPt > 0 ? cellWidthPt + cellCssPadPt : cellWidthPt);
            if (colSpan > 1 || cellWidthPt <= 0) rowAllSingleExplicit = false;
            // Band dialect: an &nbsp;-only cell is a text row in a browser — a line
            // box at the cell's font size — unlike a truly empty <td></td>, which
            // collapses. Keep its blank line so the row spacing holds
            // (the corner-mark spacer rows of proxy cards are built from these).
            if (bandDialect && lines.Count == 0
                && IsAllWhitespace(line)
                && line.ToString().IndexOf(' ') >= 0)
            {
                if (lineFontPt <= 0) lineFontPt = curFontPt > 0 ? curFontPt : rowFontPt;
                if (lineFontPt > 0) PushLine(keepIfBlank: true);
                else PushLine();
            }
            // The cell ended on a <br> with nothing after it — the break's own empty
            // line box is real vertical space, so it must not be swallowed here.
            else if (cellPendingBrBlank && IsAllWhitespace(line))
            {
                // The blank box takes the type the cell itself sets in — a cell that
                // declares no size of its own still has one, so the break's line is
                // real space rather than a zero-height line that gets swept away.
                if (lineFontPt <= 0)
                    lineFontPt = curFontPt > 0 ? curFontPt
                        : rowFontPt > 0 ? rowFontPt
                        : uaDocGrid ? cellFontSize : 0;
                PushLine(keepIfBlank: lineFontPt > 0);
            }
            else PushLine();
            // The div box is also the WRAP box: a run too long for it breaks inside the
            // div — after a hyphen when one fits, otherwise mid-token (break-word) —
            // instead of running the full width of the enclosing cell.
            if (cellFixedDivPt > 0 && lines.Count > 0)
            {
                var rewrapped = new List<(string Text, double FontPt, string? Family, bool Keep,
                    bool JoinNext, List<(string Text, string Url)>? Anchors, bool Bold,
                    double MarginTopPt, double MarginLeftPt, Color? Color, bool Italic)>();
                foreach (var spec in lines)
                {
                    var wPt = spec.FontPt > 0 ? spec.FontPt : 0.0;
                    Func<string, double> boxMeasure = uaCellBoxes
                        ? s => MeasureSerifLine(s, spec.Bold, wPt)
                        : s => MeasureLine(s, spec.Bold, wPt, spec.Family);
                    if (spec.Text.Length == 0 || boxMeasure(spec.Text) <= cellFixedDivPt)
                    {
                        rewrapped.Add(spec);
                        continue;
                    }
                    var firstPiece = true;
                    foreach (var piece in WrapToBox(spec.Text, cellFixedDivPt, boxMeasure))
                    {
                        rewrapped.Add((piece, spec.FontPt, spec.Family, spec.Keep,
                            firstPiece && spec.JoinNext, firstPiece ? spec.Anchors : null, spec.Bold,
                            firstPiece ? spec.MarginTopPt : 0, spec.MarginLeftPt, spec.Color,
                            spec.Italic));
                        firstPiece = false;
                    }
                }
                lines.Clear();
                lines.AddRange(rewrapped);
            }
            // A cell whose lines carry explicit per-line styles (font-size on a <p>/<span>)
            // renders as CSS line boxes: every line gets its TRUE size (styled size, else the
            // stylesheet base), plus line-box metrics for the generator's mixed-size layout.
            // The CSS run dialect lays EVERY cell out as CSS line boxes, styled or not:
            // the uniform per-row grid takes the tallest cell's pitch, so one 24 pt
            // column would otherwise stretch every 10 pt column in its row to match.
            var anyStyled = cellFontShorthand || cssRunFace is not null;
            foreach (var l in lines) if (l.FontPt > 0) anyStyled = true;
            // Unstyled cells keep the LEGACY line structure: lines split only at <br>/<img>/
            // cell close. Rejoin the paragraph (<p>) splits so plain-markup tables are
            // byte-identical to the pre-styled-cell behaviour.
            if (!anyStyled)
                for (var k = 0; k < lines.Count - 1; k++)
                    if (lines[k].JoinNext)
                    {
                        var merged = CollapseWs(lines[k].Text + " " + lines[k + 1].Text);
                        var mergedAnchors = lines[k].Anchors;
                        if (lines[k + 1].Anchors is { } nextAnchors)
                            (mergedAnchors ??= new()).AddRange(nextAnchors);
                        lines[k] = (merged, 0, lines[k].Family ?? lines[k + 1].Family,
                            false, lines[k + 1].JoinNext, mergedAnchors,
                            lines[k].Bold && lines[k + 1].Bold, lines[k].MarginTopPt, lines[k].MarginLeftPt,
                            lines[k].Color ?? lines[k + 1].Color,
                            lines[k].Italic && lines[k + 1].Italic);
                        lines.RemoveAt(k + 1);
                        k--;
                    }
            // Resolve the recorded box-run segments into per-line decorations. The
            // box model owns the pen: each box advances it by its full width (pads
            // take real space) plus a 3 pt sibling gap; text centres inside its box
            // (pill labels sit at the left pad, ahead of their circle). A run's
            // LATER segment (the ID line under a title plate) reuses the plate's
            // placed box — no rectangle, just its own centred text run.
            Dictionary<int, List<InlineBoxDecoration>>? boxByLine = null;
            if (cellBoxSegs is { Count: > 0 })
            {
                // A cell whose only content is a box (a standalone badge circle in
                // an otherwise-empty td) never pushed a line — materialise the
                // blank line(s) its segments point at.
                var needLines = 0;
                foreach (var (bLi0, _, _, _) in cellBoxSegs)
                    if (bLi0 + 1 > needLines) needLines = bLi0 + 1;
                while (lines.Count < needLines) PushLine();
                var runSegs = new Dictionary<ChainBoxRun, int>();
                foreach (var (_, bRun0, _, _) in cellBoxSegs)
                    runSegs[bRun0] = runSegs.TryGetValue(bRun0, out var n0) ? n0 + 1 : 1;
                Dictionary<ChainBoxRun, (double XOff, double W, int Seen, double FirstLineH)>? placed = null;
                var penByLine = new Dictionary<int, double>();
                foreach (var (bLi, bRun, _, bText) in cellBoxSegs)
                {
                    var bSpec = bLi < lines.Count ? lines[bLi] : default;
                    var bPt = bSpec.FontPt > 0 ? bSpec.FontPt
                        : cellClassPt > 0 ? cellClassPt : cellFontSize;
                    var bBold = bSpec.Bold || cellBold || isHeader;
                    // Measure the box text with the REAL face the box renders in
                    // (Arial advances run ~3% wider than the Standard-14 estimate on
                    // these bold runs). MAX with the AFM estimate: an uninstalled
                    // face degrades to a 0.5-em guess that must not narrow the box.
                    var bTw = Math.Max(
                            MeasureFaceText(bBold ? "Arial Bold" : "Arial", bText, bPt),
                            MeasureLine(bText, bBold, bPt))
                        + bRun.LetterSpacing * bText.Length;
                    var bCircleD = bRun.CircleFill is not null
                        ? (bRun.CircleD > 0 ? bRun.CircleD : 14.25) : 0;
                    penByLine.TryGetValue(bLi, out var pen);
                    InlineBoxDecoration deco;
                    if (placed is not null && placed.TryGetValue(bRun, out var bAt))
                    {
                        // Continuation: the plate was already painted (declared
                        // height); only the run's LAST segment keeps the bottom pad,
                        // TRIMMED so the cell's line stack sums exactly to the plate
                        // height (the row must not outgrow the plate).
                        var contPadB = bAt.Seen + 1 == runSegs[bRun] ? bRun.PadB : 0;
                        if (bRun.DeclH > 0)
                        {
                            var plateTotal = bRun.PadT + bRun.DeclH + bRun.PadB;
                            contPadB = Math.Max(0, Math.Min(contPadB,
                                plateTotal - bAt.FirstLineH - bRun.ContPadTop
                                - Math.Max(bPt * 1.2, 15.0)));
                        }
                        deco = new InlineBoxDecoration
                        {
                            XOff = bAt.XOff, Width = bAt.W, Fill = null,
                            PadTop = bRun.ContPadTop,
                            PadBottom = contPadB,
                            Text = bText, TextX = bAt.XOff + (bAt.W - bTw) / 2,
                            TextSize = bPt, TextBold = bBold,
                            TextLetterSpacing = bRun.LetterSpacing,
                        };
                        placed[bRun] = (bAt.XOff, bAt.W, bAt.Seen + 1, bAt.FirstLineH);
                        pen = Math.Max(pen, bAt.XOff + bAt.W);
                    }
                    else
                    {
                        var bW = bRun.PadL + bTw
                            + (bCircleD > 0 ? (bTw > 0 ? BadgeLabelGapPt : 0) + bCircleD : 0)
                            + bRun.PadR;
                        deco = new InlineBoxDecoration
                        {
                            XOff = pen, Width = bW,
                            PadTop = bRun.PadT,
                            PadBottom = runSegs[bRun] > 1 ? 0 : bRun.PadB,
                            PadRight = bRun.PadR, Radius = bRun.Radius, Fill = bRun.Fill,
                            Height = bRun.DeclH > 0 ? bRun.PadT + bRun.DeclH + bRun.PadB : 0,
                            // A block bar (h1, margin:0) hugs its line box — no inset.
                            // A circle-only run (a standalone badge in its own cell)
                            // carries no pill chrome — its line is the circle.
                            InsetV = bRun.DeclH > 0 ? PlateBreathingPt
                                : bRun.FullWidth || bText.Length == 0 ? 0 : PillLineInsetPt,
                            Text = bText,
                            TextX = pen + (bCircleD > 0 ? bRun.PadL : (bW - bTw) / 2),
                            TextSize = bPt, TextBold = bBold,
                            TextLetterSpacing = bRun.LetterSpacing,
                            FullWidth = bRun.FullWidth,
                            TextCentered = bRun.TextCentered,
                            TextColor = bRun.TextColor,
                            CircleFill = bRun.CircleFill, CircleD = bCircleD,
                            CircleLetter = bRun.CircleLetter.Length > 0 ? bRun.CircleLetter : null,
                            CircleLetterColor = bRun.CircleLetterColor,
                        };
                        (placed ??= new Dictionary<ChainBoxRun, (double, double, int, double)>())[bRun]
                            = (pen, bW, 1, bRun.PadT + Math.Max(bPt * 1.2, 15.0));
                        pen += bW + InlineBoxSiblingGapPt;
                    }
                    penByLine[bLi] = pen;
                    if (boxByLine is null || !boxByLine.TryGetValue(bLi, out var bList))
                        (boxByLine ??= new Dictionary<int, List<InlineBoxDecoration>>())[bLi]
                            = bList = new List<InlineBoxDecoration>();
                    bList.Add(deco);
                }
                cellBoxSegs.Clear();
            }
            var tfLineIdx = -1;
            var cellHadNestedTable = false;
            foreach (var spec in lines)
            {
                tfLineIdx++;
                var ln = spec.Text;
                // A nested grid anchored at (or before) this line joins the cell's
                // paragraphs HERE, so text that followed it in the markup stays
                // below it (an attachments grid draws between its caption and the
                // template list, not after both).
                while (pendingCellTables is { Count: > 0 } && pendingCellTables[0].AnchorLine <= tfLineIdx)
                {
                    cell.Paragraphs.Add(pendingCellTables[0].T);
                    pendingCellTables.RemoveAt(0);
                    cellHadNestedTable = true;
                }
                // A deliberately-kept blank line is a real line box (the newline a
                // pre-wrap box holds onto), so it survives in a UA-cell-box grid even
                // when the cell carries no per-line styling. The lifted dialect keeps
                // them too: an explicit <br> on an empty line (`<BR><BR>` between
                // form questions) is the vertical rhythm of the source document.
                // A blank line CARRYING inline boxes (a standalone badge circle in
                // an otherwise-empty cell) survives too — the decos ride the fragment.
                if (ln.Length == 0
                    && !((anyStyled || uaCellBoxes
                            || (liftNestedTables
                                && !(loneBrBlankLines?.Contains(tfLineIdx) ?? false))) && spec.Keep)
                    && !(boxByLine is not null && boxByLine.ContainsKey(tfLineIdx))) continue;
                var tf = new Text.TextFragment(ln);
                tf.HtmlAnchors = spec.Anchors;
                // A class on the cell (`.header { font-size:16pt }`) sizes its text even
                // when no line carries a style of its own — the class rule is more
                // specific than the sheet's `table td` base.
                tf.TextState.FontSize = (float)(cellClassPt > 0 ? cellClassPt : cellFontSize);
                if (anyStyled)
                {
                    var pt = spec.FontPt > 0 ? spec.FontPt
                        : cellClassPt > 0 ? cellClassPt
                        : (cssBasePt > 0 ? cssBasePt : cellFontSize);
                    tf.TextState.FontSize = (float)pt;
                    // The TextFragment ctor's segment carries its own default size which the
                    // generator prefers — set it too so the styled size actually applies.
                    foreach (var seg in tf.Segments)
                        if (!string.IsNullOrEmpty(seg.Text)) seg.TextState.FontSize = (float)pt;
                    var (asc, desc) = CssFamilyMetrics(spec.Family ?? cssBaseFamily);
                    tf.CssAscent = asc; tf.CssDescent = desc;
                    tf.CssKeepBlank = spec.Keep;
                    tf.CssLineBoxAlways = cellFontShorthand || cssRunFace is not null;
                }
                // The lifted dialect keeps deliberate blank line boxes (<br> on an
                // empty line) whether or not the cell is styled — the generator
                // needs the flag to price them as real boxes.
                if (liftNestedTables) tf.CssKeepBlank = spec.Keep;
                if (isHeader || spec.Bold || cellBold) tf.TextState.IsBold = true;
                if (spec.Italic) tf.TextState.IsItalic = true;
                // Mixed bold runs on this line (form-grid): the render draws each
                // segment in its own face variant.
                if (lineRunsByIdx is not null
                    && lineRunsByIdx.TryGetValue(tfLineIdx, out var fgRuns))
                    tf.FormGridRuns = fgRuns;
                // Source page's CSS line-height (e.g. body `font: 1em/1.4em …`):
                // wrapped cell lines pitch at the CSS box, not the bare font size.
                // UA cell boxes: every line takes the `line-height: normal` box of its
                // OWN size, so an 8 pt row pitches at 9 pt while a 10 pt one takes 11.25
                // — a single document-wide pitch oversizes every small-font row.
                if (uaCellBoxes)
                    tf.CssLineHeightPt = NormalLineHeightPt(tf.TextState.FontSize);
                // Form-grid dialect: every line takes its own size's px-rounded
                // Verdana line box, floored at the cell's strut — the td's own
                // declared size when it styles one, else the chunk's ambient box.
                // The 8pt rows sit on the strut; the 36pt spacer row grows to
                // its 58px box (43.5).
                else if (formGridDialect)
                {
                    var fgStrut = cellFgStrutPt > 0 ? cellFgStrutPt
                        : formGridStrutPt > 0 ? formGridStrutPt : VerdanaGridMinLinePt;
                    tf.CssLineHeightPt = Math.Max(
                        PxLinePt(tf.TextState.FontSize, VerdanaWinLineRatio), fgStrut);
                    // The line's baseline seat: the run's own drop within its box,
                    // floored at the strut's (the td-own strut carries its own).
                    var fgRunDrop = (tf.CssLineHeightPt
                        - tf.TextState.FontSize * VerdanaWinLineRatio) / 2
                        + tf.TextState.FontSize * VerdanaWinAscent;
                    var fgStrutDrop = cellFgStrutPt > 0
                        ? (cellFgStrutPt - cellFgStrutFontPt * VerdanaWinLineRatio) / 2
                            + cellFgStrutFontPt * VerdanaWinAscent
                        : formGridStrutDropPt;
                    tf.CssBaseDrop = Math.Max(fgStrutDrop, fgRunDrop);
                    if (Environment.GetEnvironmentVariable("ASPOSE_HTML_DEBUG_FG") is not null
                        && spec.FontPt > 12)
                        Console.WriteLine($"[fg] specPt={spec.FontPt} tfPt={tf.TextState.FontSize} " +
                            $"lh={tf.CssLineHeightPt:0.##} anyStyled={anyStyled} txt='{ln}'");
                }
                // `line-height: normal` is the BASE FACE's own win-metric box, not the
                // serif-calibrated constant — a 24 pt run in a Segoe UI page pitches on
                // Segoe's ratio (32.25), a 9.75 pt one on 12.75.
                else if (cssRunFace is not null && WinMetricsFor(cssRunFace) is { } crm)
                    tf.CssLineHeightPt = MetricLineHeight(tf.TextState.FontSize, crm.sum);
                else if (cellOwnLineHPt > 0)
                {
                    tf.CssLineHeightPt = cellOwnLineHPt;
                    // The cell's pitch becomes the LINE'S OWN BOX only for lines that
                    // did not style their own font: a run carrying its own size usually
                    // carries its own line-height context too (an embedded document's
                    // `p { line-height:1.2 }` overriding the host cell's 19px), and we
                    // do not model that cascade — the row-level pitch still applies.
                    tf.CssLineHeightFromCell = spec.FontPt <= 0 && !cellOwnHeightDecl;
                }
                // A grid whose face came from the DOCUMENT stacks on that face's normal
                // line box (a DECLARED cell line-height above wins, as in CSS). The
                // pitch is a property of the face — Arial 12 steps 13.50 whether or not
                // the table happens to mix sizes — so it does not ride the run-styling
                // gate above, which asks a different question.
                else if (uaDocGrid && defaultCellFace is { } dclf
                         && WinMetricsFor(dclf) is { } dcm)
                    tf.CssLineHeightPt = MetricLineHeight(tf.TextState.FontSize, dcm.sum);
                else if (cellLineHeightPt > 0) tf.CssLineHeightPt = cellLineHeightPt;
                if ((spec.Color ?? cellChainColor ?? cellTextColor) is { } tfColor)
                    tf.TextState.ForegroundColor = tfColor;
                // Band dialect: the paragraph's explicit margins become the fragment's
                // margins — a gap above its first line and an indent that narrows its
                // wrap box in the cell layout.
                if ((bandDialect || liftNestedTables) && (spec.MarginTopPt > 0 || spec.MarginLeftPt > 0))
                    tf.Margin = new MarginInfo { Top = spec.MarginTopPt, Left = spec.MarginLeftPt };
                // The cell's own padding-left indents its text the way it widened the
                // column — a left-aligned run starts that far inside the cell box.
                if (cellPadLeftPt > 0 && cellAlign != HorizontalAlignment.Right)
                    tf.Margin = new MarginInfo
                    {
                        Top = tf.Margin?.Top ?? 0,
                        Left = (tf.Margin?.Left ?? 0) + cellPadLeftPt,
                    };
                var famForFont = spec.Family ?? cellFamily;
                if (famForFont is not null && ln.Length > 0)
                {
                    try { var f = Text.FontRepository.FindFont(famForFont); if (f is not null) tf.TextState.Font = f; }
                    catch { }
                }
                // Non-WinAnsi text (CJK, Cyrillic, Greek, …) can't render in the Standard-14
                // WinAnsi fonts — it would collapse to '?'. Fall back to an embedded Unicode
                // face that covers the run so it flows through the Type0/CID render path.
                // RTL text is deliberately left alone (no font override, text unmodified):
                // the generator's cell pipeline shapes and embeds Arabic/Hebrew natively.
                if (ln.Length > 0 && tf.TextState.Font?.SourceFontData is null && NeedsUnicode(ln)
                    && !Text.BidiReorderer.ContainsRtl(ln))
                {
                    var uf = ResolveUnicodeFont(ln);
                    if (uf is not null) tf.TextState.Font = uf;
                }
                // Inline boxes recorded for this line (plates/pills) ride the
                // fragment; the box line height reserves the pill's full height,
                // and a continuation line is centred via the fragment margin.
                if (boxByLine is not null && boxByLine.TryGetValue(tfLineIdx, out var tfBoxes))
                {
                    tf.InlineBoxes = tfBoxes;
                    double bxPadT = 0, bxPadB = 0, bxInsetV = 0;
                    var bxCircle = false;
                    foreach (var b4 in tfBoxes)
                    {
                        bxPadT = Math.Max(bxPadT, b4.PadTop);
                        // A declared-height box (title plate) self-sizes its rect:
                        // its bottom pad lives inside the rect, not in the line stack
                        // (the continuation line follows at text pitch).
                        if (b4.Height <= 0) bxPadB = Math.Max(bxPadB, b4.PadBottom);
                        bxInsetV = Math.Max(bxInsetV, b4.InsetV);
                        if (b4.CircleFill is not null) bxCircle = true;
                    }
                    // The 15pt floor exists for the badge CIRCLE's diameter — a bar
                    // with no circle keeps its text's own line box (circle-less h2
                    // bars are ~12.8, not 15+).
                    var bxLineH = bxPadT
                        + Math.Max(tf.TextState.FontSize * Table.CssNormalLineHeight,
                            bxCircle ? 15.0 : 0.0)
                        + bxPadB + 2 * bxInsetV;
                    if (bxLineH > tf.CssLineHeightPt) tf.CssLineHeightPt = bxLineH;
                }
                // Hand this fragment the radio options its marker chars stand for
                // (in document order — options were queued as their inputs were
                // walked, and lines flush in the same order).
                if (cellInlineOptions is { Count: > 0 })
                {
                    var nMarks = 0;
                    foreach (var mch in ln)
                        if (mch is Table.InlineRadioChar or Table.InlineRadioCheckedChar) nMarks++;
                    if (nMarks > 0)
                    {
                        var take = Math.Min(nMarks, cellInlineOptions.Count);
                        tf.InlineOptions = cellInlineOptions.GetRange(0, take);
                        cellInlineOptions.RemoveRange(0, take);
                    }
                }
                cell.Paragraphs.Add(tf);
            }
            // Images that FOLLOWED text in the markup were deferred so the cell's
            // paragraph order matches the source ("label<br><img>" draws the label
            // line above the image box, not underneath its blit).
            if (pendingCellTables is { Count: > 0 })
            {
                foreach (var (ptbl, _) in pendingCellTables) cell.Paragraphs.Add(ptbl);
                pendingCellTables.Clear();
                cellHadNestedTable = true;
            }
            if (cellHadNestedTable)
            {
                (nestedTableCols ??= new HashSet<int>()).Add(row.Cells.Count);
                // This grid really does hold a lifted nested table — the browser line
                // box applies to it (see Table.HtmlLiftedGrid).
                table.HtmlLiftedGrid = true;
            }
            if (pendingCellImgs is { Count: > 0 })
            {
                foreach (var pimg in pendingCellImgs) cell.Paragraphs.Add(pimg);
                pendingCellImgs.Clear();
            }
            // The cell's own CSS padding becomes its box padding: it indents the drawn
            // text and narrows the wrap box exactly as it widened the column. A chain
            // rule's vertical padding rides the same box (the horizontal pair came in
            // through cellCssPadPt/cellPadLeftPt). A full Margin REPLACES the table's
            // DefaultCellPadding wholesale, so the chain dialect keeps the default's
            // vertical band (the cellspacing rhythm) when the cell adds none.
            if (cellPadLeftPt > 0 || cellCssPadPt > 0 || cellChainPadTopPt > 0 || cellChainPadBotPt > 0)
            {
                var vTop = Math.Max(pad, cellChainPadTopPt);
                var vBot = Math.Max(pad, cellChainPadBotPt);
                var hExtra = 0.0;
                if (chainBase is not null && table.DefaultCellPadding is { } dcpM)
                {
                    vTop = Math.Max(vTop, dcpM.Top);
                    vBot = Math.Max(vBot, dcpM.Bottom);
                }
                // A declared border-spacing is a gap OUTSIDE the cell's own padding,
                // so it adds to it rather than competing with it (the pill's detail
                // button keeps its 1ex pad and still sits 2 pt off its neighbours).
                if (chainSpacingPt > 0)
                {
                    vTop += chainSpacingPt / 2;
                    vBot += chainSpacingPt / 2;
                    hExtra = chainSpacingPt / 2;
                }
                // …and the table-level padSide is read from the SAME declaration that
                // gave the chain its cellCssPadPt, so adding both insets the text a
                // second pad in and narrows the wrap box by a whole pair — the drawn
                // twin of the column footprint's `padSideExtra` bill.
                var padSideBox = chainBase is not null && cellCssPadPt > 0 ? 0 : padSide;
                cell.Margin = new MarginInfo(padSideBox + cellPadLeftPt + hExtra, vBot,
                    padSideBox + (cellCssPadPt - cellPadLeftPt) + hExtra, vTop);
            }
            cell.IsWordWrapped = true;
            cell.ColSpan = Math.Max(1, colSpan);
            // A lifted table is measured by the layout pass, which reads the span from
            // the cell; the legacy grid keeps its own calibrated row mapping.
            if (liftNestedTables && cellRowSpan > 1) cell.RowSpan = cellRowSpan;
            cell.Alignment = alignSet ? cellAlign
                : rowAlign
                ?? (isHeader ? headerAlign ?? HorizontalAlignment.Center : HorizontalAlignment.Left);
            if (isHeader && headerBorder is not null) cell.Border = headerBorder;
            row.Cells.Add(cell);
            // Record this cell's content width against the column(s) it spans, so a table with no
            // explicit widths auto-fits each column to its widest content (+ cell padding).
            double cellMin = 0, cellMax = 0, cellHdr = 0, cellMinBrk = 0;
            foreach (var spec in lines)
            {
                var ln = spec.Text;
                // The probe measures each line with the styles it renders with: its own
                // font size (a 9pt header row in a 10pt table measures at 9), real bold
                // metrics for an all-bold line, and its own family.
                // …and so does the CSS run dialect, whose column floors must hold the
                // widest token AT ITS OWN SIZE (a 24 pt run in a 10 pt table).
                var mPt = (widenProbe || cssRunFace is not null || chainBase is not null)
                    && spec.FontPt > 0 ? spec.FontPt : 0.0;
                var mBold = isHeader || ((widenProbe || chainBase is not null) && spec.Bold);
                var mFam = widenProbe ? spec.Family : null;
                cellMin = Math.Max(cellMin, MeasureMinContent(ln, mBold, mPt, mFam));
                cellMinBrk = Math.Max(cellMinBrk, MeasureMinContent(ln, mBold, mPt, mFam, breakDashes: true));
                cellMax = Math.Max(cellMax, MeasureLine(ln, mBold, mPt, mFam));
                // A header cell's full (unwrapped) line width — used to keep <th> on one line when
                // the whole table still fits the available width (a browser does not wrap headers to
                // their widest word). Recorded separately so it never forces the page/table wider.
                if (isHeader) cellHdr = Math.Max(cellHdr, MeasureLine(ln, bold: true));
            }
            // white-space:nowrap under the chain dialect: the cell's floor is its
            // whole unwrapped line — nowrap labels never wrap,
            // so a space-broken min under-sizes the column and the cell
            // fill stops mid-text.
            if (cell.HtmlNoWrap && chainBase is not null && cellMax > cellMin)
            {
                cellMin = cellMax;
                cellMinBrk = Math.Max(cellMinBrk, cellMax);
            }
            // An inline-box line's width is its BOX extent (pads, circle and sibling
            // gaps included) — the flat text under-measures the plates by their
            // padding, and the column then leaves dead space beside its neighbour.
            if (boxByLine is not null)
            {
                double bxExt = 0;
                foreach (var bl3 in boxByLine.Values)
                    foreach (var b6 in bl3)
                        bxExt = Math.Max(bxExt, b6.XOff + b6.Width);
                if (bxExt > 0)
                {
                    cellMin = Math.Max(cellMin, bxExt + InlineBoxColumnSlackPt);
                    cellMinBrk = Math.Max(cellMinBrk, bxExt + InlineBoxColumnSlackPt);
                    cellMax = Math.Max(cellMax, bxExt + InlineBoxColumnSlackPt);
                }
            }
            // A fixed-width div inside the cell IS the cell's min-content: its long
            // token wraps inside the div box (break-word), so the div box — not the
            // token — sizes the column. The cell's own CSS padding rides on every
            // measure (the column footprint includes it).
            if (cellFixedDivPt > 0)
            {
                cellMin = cellFixedDivPt;
                cellMinBrk = Math.Min(cellMinBrk, cellFixedDivPt);
                if (cellFixedDivPt > cellMax) cellMax = cellFixedDivPt;
            }
            // …and an image the cell draws claims its own box in every measure.
            if (cellImgWidthPt > 0)
            {
                cellMin = Math.Max(cellMin, cellImgWidthPt);
                cellMinBrk = Math.Max(cellMinBrk, cellImgWidthPt);
                cellMax = Math.Max(cellMax, cellImgWidthPt);
            }
            // A NESTED table sizes its cell: the grid's own natural width is the
            // cell's min- and max-content (its flattened text lines measure a
            // fraction of the real grid).
            if (pendingCellTablesNatW > 0)
            {
                cellMin = Math.Max(cellMin, pendingCellTablesNatW);
                cellMinBrk = Math.Max(cellMinBrk, pendingCellTablesNatW);
                // The cell WANTS the grid's preferred (max-content) width — a
                // percent grid's natural is only its min floors, and sizing the
                // column off that leaves the grid squeezed below its due
                // width.
                cellMax = Math.Max(cellMax,
                    Math.Max(pendingCellTablesNatW, pendingCellTablesPrefW));
                pendingCellTablesNatW = 0;
                pendingCellTablesPrefW = 0;
            }
            if (cellCssPadPt > 0)
            {
                cellMin += cellCssPadPt; cellMinBrk += cellCssPadPt;
                cellMax += cellCssPadPt; if (cellHdr > 0) cellHdr += cellCssPadPt;
            }
            // A declared border-spacing is part of every column's footprint: the band
            // sits OUTSIDE the cell's box, half on each side (the draw insets the
            // border by the same half, so the box itself keeps its content width).
            if (chainSpacingPt > 0)
            {
                cellMin += chainSpacingPt; cellMinBrk += chainSpacingPt;
                cellMax += chainSpacingPt; if (cellHdr > 0) cellHdr += chainSpacingPt;
            }
            var span = Math.Max(1, colSpan);
            // Probe mapping: skip columns still occupied by ROWSPAN cells from rows
            // above (the HTML grid placement rule), so a row following a row-spanning
            // name cell doesn't record its money cells one column early and double
            // the summed min-content with ghost twins. The chain dialect measures
            // through the same mapping (the budget's rowspan-2 label cell shifted
            // its whole second header row one column left).
            if (widenProbe || chainBase is not null)
            {
                bool Occupied(int c)
                {
                    foreach (var (oc, os, orem) in rowspanOcc)
                        if (orem > 0 && c >= oc && c < oc + os) return true;
                    return false;
                }
                while (Occupied(colCursor)) colCursor++;
                // remaining counts the spanning row itself (aged at its own row close),
                // so the occupancy covers exactly the rowSpan−1 rows below it.
                if (cellRowSpan > 1) rowspanOcc.Add((colCursor, span, cellRowSpan));
            }
            // Column footprint = content + cell padding (both sides) + the cell box border the
            // generator draws around it, so the summed natural width matches the rendered grid.
            // The page-widen probe measures BARE content: the widen pass adds no
            // per-column slack for zero-padding zero-border tables.
            // The CSS run dialect measures the browser's own box — cell padding plus the
            // cell's own border — with none of the legacy per-column slack.
            // A chain rule's own horizontal padding is ALREADY inside cellMin/cellMax
            // (added just above), and the table-level padSide is read from the SAME
            // declaration — adding it again bills the pair twice per column and widens
            // the sheet by a full padding pair per column.
            var padSideExtra = chainBase is not null && cellCssPadPt > 0 ? 0 : 2 * padSide;
            var extra = widenProbe ? 0
                : padSideExtra + (hasBorder ? 2 * borderWidth : 0)
                    + (tightExtras || cssRunFace is not null ? 0 : 1.5);
            while (colMinW.Count < colCursor + span) { colMinW.Add(0); colMaxW.Add(0); colHdrW.Add(0); colMinBrkW.Add(0); colDeclW.Add(0); }
            if (span == 1)
            {
                if (cellMin + extra > colMinW[colCursor]) colMinW[colCursor] = cellMin + extra;
                if (cellMinBrk + extra > colMinBrkW[colCursor]) colMinBrkW[colCursor] = cellMinBrk + extra;
                if (cellMax + extra > colMaxW[colCursor]) colMaxW[colCursor] = cellMax + extra;
                if (cellHdr > 0 && cellHdr + extra > colHdrW[colCursor]) colHdrW[colCursor] = cellHdr + extra;
                // A cell holding NOTHING but an image declares its width as surely as a
                // `width=` attribute does: replaced content has one size and the column
                // must not stretch past it. Layout tables gutter with exactly this —
                // a `<td><img width="15" height="1"></td>` spacer.
                // (`cellMax` already carries the image's own box, so "no wider than its
                // image" is the test for a cell that holds nothing else.)
                var cellDeclPt = cellImgWidthPt > 0 && cellMax <= cellImgWidthPt + 0.01
                    ? Math.Max(cellWidthPt, cellImgWidthPt) : cellWidthPt;
                if (cellDeclPt > colDeclW[colCursor]) colDeclW[colCursor] = cellDeclPt;
            }
            else
            {
                // A spanning cell constrains the SUM of its columns — deferred so it does not
                // floor thin spacer columns it merely crosses (which would starve the wide
                // content column of the width a browser gives it).
                spanConstraints.Add((colCursor, span, cellMin + extra, cellMax + extra,
                    cellHdr > 0 ? cellHdr + extra : 0));
            }
            if (colSpan == 1 && cellWidthPct > 0)
            {
                while (colPctW.Count <= colCursor) colPctW.Add(0);
                if (cellWidthPct > colPctW[colCursor]) colPctW[colCursor] = cellWidthPct;
            }
            colCursor += span;
            preWrapPending = false;
            cell = null; lines.Clear(); loneBrBlankLines?.Clear(); line.Clear(); isHeader = false; colSpan = 1; cellRowSpan = 1; alignSet = false; cellWidthPt = 0; cellWidthPct = 0; cellCssPadPt = 0; cellFixedDivPt = 0; cellPadLeftPt = 0; cellImgWidthPt = 0; cellOwnLineHPt = 0; cellPrevPBottomPt = 0; cellOwnHeightDecl = false;
            chainTdElem = null; chainOpenElems?.Clear(); chainUnbold.Clear();
            cellChainPadTopPt = 0; cellChainPadBotPt = 0; cellChainColor = null;
            chainBoxOpen?.Clear(); cellBoxSegs?.Clear();
            chainTrafficElem = null; chainTrafficRun = null; pendingCapsule = null;
            openAnchor = null; lineAnchors = null;
        }
        void CloseRow()
        {
            if (cell is not null) CloseCell();
            if (row is null) return;
            // Row-span occupancy ages one row per ACTUAL row close. CloseRow also runs
            // for the redundant boundary calls explicit-</TR> markup produces (the
            // </TR> close and the next <TR> open both land here); aging on those would
            // expire an occupancy after a single row and unshift the rows below it.
            for (var oi = rowspanOcc.Count - 1; oi >= 0; oi--)
            {
                var (oc, os, orem) = rowspanOcc[oi];
                if (orem <= 1) rowspanOcc.RemoveAt(oi);
                else rowspanOcc[oi] = (oc, os, orem - 1);
            }
            var cols = 0; foreach (var c in row.Cells) cols += Math.Max(1, c.ColSpan);
            if (cols > maxCols) maxCols = cols;
            if (colGroupPt is null && colWidthsPt is null && rowAllSingleExplicit && rowWidths.Count > 1)
                colWidthsPt = new List<double>(rowWidths);
            rowWidths.Clear(); rowAllSingleExplicit = true;
            colCursor = 0;
            if (rowMinHeightPt > 0 && rowMinHeightPt > row.MinRowHeight)
            {
                row.MinRowHeight = rowMinHeightPt;
                row.MinRowHeightIsContent = rowMinHeightIsContent;
            }
            if (row.Cells.Count > 0)
            {
                table.Rows.Add(row);
                if (rowHasCell)
                {
                    if (countingHeaderRows && !rowHasTd) headerRows++;
                    else countingHeaderRows = false;
                }
            }
            rowHasTd = false; rowHasCell = false;
            row = null;
        }

        var hiddenSubDepth = 0;
        string? hiddenSubTag = null;
        // A webridge `<span class="htmlPage">` is the source generator's block
        // container: a run it opens starts on a FRESH line (a
        // trailing `&nbsp;` wrapper sets on its own line box between a control group
        // and its <BR><BR>). Deferred until the span shows VISIBLE content — the
        // ubiquitous EMPTY wrappers are inert — and cancelled by any structural tag.
        var htmlPageBreakPending = false;
        foreach (var tok in tokens)
        {
            if (tok.Kind == TokenKind.Text)
            {
                if (cell is not null && hiddenSubDepth == 0
                    && tok.Value.IndexOf(NestedMark, StringComparison.Ordinal) >= 0)
                {
                    foreach (Match nm in Regex.Matches(tok.Value, Regex.Escape(NestedMark) + @"(\d+)\]"))
                    {
                        var ni = int.Parse(nm.Groups[1].Value);
                        if (ni < 0 || ni >= nestedHtml.Count) continue;
                        var inner = BuildTableFromHtml(nestedHtml[ni],
                            (cellWidthPt > 0 ? cellWidthPt : availWidthPt) - liStandingIndentPt,
                            out var innerNatW, options, inlineSvgs,
                            docCss ?? css, bandDialect, false, cellLineHeightPt, defaultCellFontPt, tightExtras,
                            liftNestedTables: true,
                            // The inner grid inherits this cell's ancestor chain, so
                            // tree-addressed rules keep matching through the nesting.
                            chainRules: chainRules,
                            cssAncestors: chainBase is null ? null : BuildOpenChain(),
                            makeRadio: makeRadio);
                        if (inner is not null)
                        {
                            PushLine();
                            // The grid's own CSS `margin-top` is real space above it in
                            // the host cell (`<table style="…margin-top:35px">` — the
                            // columns section clears its heading by exactly that band).
                            if (liftNestedTables
                                && Regex.Match(nestedHtml[ni], @"<table\b[^>]*>",
                                    RegexOptions.IgnoreCase) is { Success: true } inTag
                                && Regex.Match(inTag.Value,
                                    @"(?<![-\w])margin-top\s*:\s*([\d.]+)\s*px",
                                    RegexOptions.IgnoreCase) is { Success: true } inMt
                                && double.TryParse(inMt.Groups[1].Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var inMtPx)
                                && inMtPx > 0)
                                inner.HtmlMarginTopPt = inMtPx * PxToPt;
                            // The rounded capsule the enclosing div declared paints
                            // behind this grid.
                            if (pendingCapsule is { } cap)
                            {
                                inner.HtmlCapsuleFill = cap.Fill;
                                inner.HtmlCapsuleRadiusPt = cap.RadiusPt;
                                inner.HtmlCapsulePadHPt = cap.PadHPt;
                                inner.HtmlCapsulePadVPt = cap.PadVPt;
                                inner.HtmlCapsuleMarginPt = cap.MarginPt;
                                pendingCapsule = null;
                            }
                            // A grid inside a list item sits ON the item's standing
                            // indent, like every other line of the item.
                            inner.HtmlListIndentPt = liStandingIndentPt;
                            (pendingCellTables ??= new List<(Table, int)>()).Add((inner, lines.Count));
                            // The nested grid's natural width IS this cell's content
                            // width — the flattened text lines under-measure it badly,
                            // and the page-widen probe needs the real number.
                            // A capsule wrapper is part of the grid's footprint in its
                            // host column (its padding/spacing/margin band).
                            var capOut = 2 * inner.HtmlCapsuleOutsetHPt;
                            if (innerNatW + capOut > pendingCellTablesNatW)
                                pendingCellTablesNatW = innerNatW + capOut;
                            if (inner.HtmlPreferredWidthPt + capOut > pendingCellTablesPrefW)
                                pendingCellTablesPrefW = inner.HtmlPreferredWidthPt + capOut;
                        }
                    }
                    continue;
                }
                // A background-image badge's text is its letter — drawn inside the
                // badge circle by the render pass, never flowed into the line.
                if (chainTrafficRun is not null && cell is not null && hiddenSubDepth == 0)
                {
                    var badgeTxt = DecodeEntities(tok.Value).Trim();
                    if (badgeTxt.Length > 0) chainTrafficRun.CircleLetter += badgeTxt;
                    continue;
                }
                if (cell is not null && hiddenSubDepth == 0)
                {
                    // Under `white-space: pre-wrap` the newline that follows the opening
                    // tag is CONTENT, so the box's first line box is empty and the text
                    // starts on the second. Collapsing whitespace would eat it.
                    if (preWrapPending)
                    {
                        preWrapPending = false;
                        if (tok.Value.StartsWith("\n") || tok.Value.StartsWith("\r"))
                            PushLine(keepIfBlank: true);
                    }
                    // First visible content of an htmlPage container span (an &nbsp;
                    // counts — it holds a line box) starts on a fresh line.
                    if (htmlPageBreakPending)
                    {
                        var hpVis = false;
                        foreach (var hpC in DecodeEntities(tok.Value))
                            if (!char.IsWhiteSpace(hpC) || hpC == ' ') { hpVis = true; break; }
                        if (hpVis)
                        {
                            htmlPageBreakPending = false;
                            // A pending line that is itself only whitespace/&nbsp;
                            // COLLAPSES at the container boundary instead of pushing —
                            // only an explicit <br> materialises a whitespace box.
                            if (IsAllWhitespace(line)) line.Clear();
                            else if (line.Length > 0) PushLine();
                        }
                    }
                    // A run whose size differs from the one this line is already bound to
                    // opens its OWN line box: the cell paragraph becomes a stack of
                    // same-size runs, each wrapped and pitched on its own size. (A browser
                    // reflows a mixed-size paragraph continuously; the sizes change on run
                    // boundaries, which is where its lines break anyway.)
                    if ((cssRunFace is not null || chainBase is not null) && lineStyleSet
                        && !string.IsNullOrWhiteSpace(tok.Value)
                        && EffectiveChainDisplay() != "inline-block"
                        && Math.Abs((curFontPt > 0 ? curFontPt : cellFontSize)
                            - (lineFontPt > 0 ? lineFontPt : cellFontSize)) > 0.01)
                    {
                        // …unless all the line holds so far is zero-width spaces. Those are
                        // invisible and carry no advance, so they are not a line of their
                        // own: they ride along on the run that follows, and the size change
                        // simply REBINDS this line instead of closing it. (A cell opening
                        // "&#8203;<span class=…>" must not spend a whole line box on it.)
                        if (line.ToString().Trim(ZeroWidthSpace).Trim().Length == 0)
                            lineStyleSet = false;
                        else PushLine();
                    }
                    // Bind the currently active inline style to the line when its first
                    // real text arrives, so a later close tag can't restyle it. In the
                    // form-grid dialect a sized &nbsp; is real content too — the grid's
                    // spacer row takes its declared 36pt line box (U+00A0 classes as
                    // whitespace, so the plain test skips it).
                    if (!lineStyleSet && (!string.IsNullOrWhiteSpace(tok.Value)
                            || (formGridDialect
                                && tok.Value.IndexOf("&nbsp;", StringComparison.OrdinalIgnoreCase) >= 0)))
                    { lineFontPt = curFontPt; lineFamily = curFamily; lineStyleSet = true; }
                    if (lineColor is null) lineColor = curColor;
                    if (!string.IsNullOrWhiteSpace(tok.Value))
                    {
                        lineHadText = true;
                        cellPendingBrBlank = false;
                        if (boldDepth == 0) lineAllBold = false;
                        if (italicDepth == 0) lineAllItalic = false;
                    }
                    line.Append(DecodeEntities(tok.Value));
                }
                continue;
            }
            var tag = tok.Tag!.ToLowerInvariant();
            // display:none subtree inside a cell (hidden pager selects, state-carrier
            // inputs): its content never reaches the cell text.
            if (hiddenSubDepth > 0)
            {
                if (tag == hiddenSubTag)
                {
                    if (tok.IsClose) { if (--hiddenSubDepth == 0) hiddenSubTag = null; }
                    else if (!tok.IsSelfClosing) hiddenSubDepth++;
                }
                continue;
            }
            if (!tok.IsClose && cell is not null && IsHiddenElement(tag, tok.Attributes, css))
            {
                if (!tok.IsSelfClosing && !VoidTags.Contains(tag))
                {
                    hiddenSubTag = tag;
                    hiddenSubDepth = 1;
                }
                continue;
            }
            // Any structural tag cancels a pending htmlPage-container break; inline
            // style tags ride along inside the container.
            if (tag is not ("span" or "font" or "strong" or "b" or "em" or "i" or "u" or "a"))
                htmlPageBreakPending = false;
            if (liftNestedTables && !tok.IsClose && tag == "span" && cell is not null
                && line.Length > 0 && tok.Attributes is not null
                && tok.Attributes.TryGetValue("class", out var hpClass)
                && string.Equals(hpClass?.Trim(), "htmlPage", StringComparison.OrdinalIgnoreCase))
                htmlPageBreakPending = true;
            if (tok.IsClose)
            {
                // Structure tags of a table NESTED inside a cell do not drive the
                // outer grid — the nested content flows as the host cell's text,
                // with a line break per nested CELL, so each nested cell keeps its
                // own text run the way it holds its own grid box.
                if (tag == "table")
                {
                    tableDepth--;
                    if (tableDepth <= 0) CloseRow();
                    else if (cell is not null && line.Length > 0) PushLine();
                }
                else if (tag is "td" or "th")
                {
                    if (tableDepth <= 1) CloseCell();
                    else if (cell is not null && line.Length > 0) PushLine();
                }
                else if (tag == "tr")
                {
                    if (tableDepth <= 1) CloseRow();
                    else if (cell is not null && line.Length > 0) PushLine();
                }
                else if (tag == "a")
                {
                    if (cell is not null && openAnchor is { } oaC)
                    {
                        var inner = CollapseWs(line.ToString()[oaC.Start..]);
                        if (inner.Length > 0) (lineAnchors ??= new()).Add((inner, oaC.Url));
                        openAnchor = null;
                    }
                }
                else if (tag is "ol" or "ul")
                {
                    if (cell is not null && line.Length > 0) PushLine();
                    if (listNesting.Count > 0) listNesting.RemoveAt(listNesting.Count - 1);
                    liStandingIndentPt = ListItemIndentPt * listNesting.Count;
                    // UA margin-block-end of a TOP-LEVEL list closing mid-cell: one
                    // line box below the last item, the twin of the open-side margin.
                    if (liftNestedTables && cell is not null && listNesting.Count == 0
                        && lines.Count > 0)
                    {
                        if (!lineStyleSet) { lineFontPt = curFontPt; lineFamily = curFamily; }
                        PushLine(keepIfBlank: true);
                    }
                }
                else if (tag == "li")
                {
                    if (cell is not null && line.Length > 0) PushLine();
                }
                else if (tag is "strong" or "b")
                {
                    if (boldDepth > 0)
                    {
                        boldDepth--;
                        // Form-grid: the bold run CLOSES here - mark the boundary so
                        // the tail of the line returns to the regular face.
                        if (formGridDialect && lineRunMarks is not null)
                            lineRunMarks.Add((line.Length, boldDepth > 0));
                        if (widenProbe && cell is not null) line.Append('\uE001');
                    }
                }
                else if (tag is "sup" or "sub")
                {
                    // Probe: close a superscript run (measured at 85% of the line size).
                    if (widenProbe && cell is not null) line.Append('\uE003');
                }
                else if (tag is "p" or "span" or "font" or "label" or "div" or "h1" or "h2")
                {
                    // A closing heading bar: its box segment records the line about
                    // to push (the segment index is the PUSHED line's), then the
                    // line closes — block semantics.
                    if (tag is "h1" or "h2" && cell is not null && chainBase is not null)
                    {
                        if (chainOpenElems is { Count: > 0 })
                            for (var k = chainOpenElems.Count - 1; k >= 0; k--)
                                if (chainOpenElems[k].Tag == tag)
                                {
                                    var hPopped = chainOpenElems[k];
                                    chainOpenElems.RemoveAt(k);
                                    ChainBoxCloseMaybe(hPopped);
                                    break;
                                }
                        if (line.Length > 0) PushLine();
                    }
                    // A closing paragraph ends its line; closing style tags restore the
                    // enclosing style context. Band dialect: a whitespace-only <p>
                    // (the styled &nbsp; spacer idiom) keeps its line box. A <div>
                    // reaches here only when the chain pass pushed an entry for it —
                    // nothing else stacks divs, so the scan finds nothing otherwise.
                    if (tag == "p" && cell is not null && line.Length > 0)
                    {
                        var pBlank = bandDialect && IsAllWhitespace(line)
                            && (lineFontPt > 0 || curFontPt > 0);
                        if (pBlank && lineFontPt <= 0) lineFontPt = curFontPt;
                        PushLine(keepIfBlank: pBlank, joinNext: true);
                    }
                    for (var k = styleStack.Count - 1; k >= 0; k--)
                        if (styleStack[k].Tag == tag)
                        {
                            curFontPt = styleStack[k].PrevPt; curFamily = styleStack[k].PrevFamily;
                            curColor = styleStack[k].PrevColor;
                            if (styleStack[k].BoldBump && boldDepth > 0) boldDepth--;
                            if (styleStack[k].ItalicBump && italicDepth > 0) italicDepth--;
                            styleStack.RemoveAt(k);
                            break;
                        }
                    // …and closes its chain-ancestor entry (span/font/div opens
                    // pushed one whenever the chain pass is active).
                    if (chainOpenElems is { Count: > 0 } && tag != "p")
                        for (var k = chainOpenElems.Count - 1; k >= 0; k--)
                            if (chainOpenElems[k].Tag == tag)
                            {
                                var poppedElem = chainOpenElems[k];
                                chainOpenElems.RemoveAt(k);
                                ChainBoxCloseMaybe(poppedElem);
                                break;
                            }
                    // A font-weight:normal run ends: the enclosing bold resumes.
                    if (chainUnbold.Count > 0 && chainUnbold[^1].Tag == tag)
                    {
                        boldDepth = chainUnbold[^1].PrevBoldDepth;
                        chainUnbold.RemoveAt(chainUnbold.Count - 1);
                    }
                }
                continue;
            }
            switch (tag)
            {
                case "table":
                    tableDepth++;
                    // A nested table's content opens on a fresh line of the host cell.
                    if (tableDepth > 1 && cell is not null && line.Length > 0) PushLine();
                    break;
                case "tr":
                    if (tableDepth > 1)
                    {
                        if (cell is not null && line.Length > 0) PushLine();
                        break;
                    }
                    // A hidden row (`<tr style="display:none">` — the empty-state
                    // tfoot band of a data grid) is out of the layout entirely: no
                    // cells, no height, no column measures. The in-cell hidden check
                    // above never sees it because no cell is open at a row boundary.
                    if (IsHiddenElement(tag, tok.Attributes, css))
                    {
                        hiddenSubTag = tag;
                        hiddenSubDepth = 1;
                        break;
                    }
                    CloseRow(); row = new Row();
                    rowFontPt = 0; rowMinHeightPt = 0; rowMinHeightIsContent = false; rowAlign = null;
                    // A row's declared fill paints its whole band behind the cells.
                    if (tok.Attributes is not null)
                    {
                        if (tok.Attributes.TryGetValue("style", out var trSt) && trSt is not null
                            && Regex.Match(trSt, @"background(?:-color)?\s*:\s*([^;]+)",
                                RegexOptions.IgnoreCase) is { Success: true } trBgm
                            && ParseCssColor(trBgm.Groups[1].Value) is { } trBg)
                            row.BackgroundColor = trBg;
                        else if (tok.Attributes.TryGetValue("bgcolor", out var trBgAttr)
                            && ParseCssColor(trBgAttr) is { } trBgA)
                            row.BackgroundColor = trBgA;
                    }
                    // ALIGN on the row is the default for every cell in it that
                    // declares none of its own.
                    if (liftNestedTables && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("align", out var trAl))
                        rowAlign = ParseAlignAttr(trAl);
                    // A row's CSS height (a `tr {height:28px}` rule, a `.medium` class
                    // variant, or an inline style) is a MINIMUM: content-driven rows
                    // still grow past it, matching the browser's table model. The rule
                    // usually lives in the document stylesheet, not the segment.
                    if (TryGetCssLength(css, "tr", "height", out var trh) && trh > 0)
                        rowMinHeightPt = trh;
                    else if (docCss is not null && TryGetCssLength(docCss, "tr", "height", out var dtrh) && dtrh > 0)
                        rowMinHeightPt = dtrh;
                    if (tok.Attributes is not null && tok.Attributes.TryGetValue("class", out var trCls))
                        foreach (var cls in trCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (TryGetCssLength(css, "tr." + cls, "height", out var trch) && trch > 0)
                                rowMinHeightPt = trch;
                            else if (docCss is not null && TryGetCssLength(docCss, "tr." + cls, "height", out var dtrch) && dtrch > 0)
                                rowMinHeightPt = dtrch;
                        }
                    if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var trStyle))
                    {
                        var trfs = Regex.Match(trStyle, @"font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
                        if (trfs.Success && TryParseLength(trfs.Groups[1].Value.Trim(), out var trfp)) rowFontPt = trfp;
                        var trhm = Regex.Match(trStyle, @"height\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
                        if (trhm.Success && TryParseLength(trhm.Groups[1].Value.Trim(), out var trhp) && trhp > 0)
                            rowMinHeightPt = trhp;
                    }
                    break;
                case "p":
                case "span":
                case "font":
                // A <label> is an ordinary inline box: the font-family/font-size it
                // declares style the run it wraps, exactly as a <span>'s would.
                case "label":
                    if (cell is null) break;
                    // NOTE: no keep here — the line flushed at a <p> OPEN is the
                    // inter-tag residue before the paragraph (e.g. "<td> <p>"), not
                    // paragraph content; keeping it would give every such cell a
                    // phantom blank first line.
                    if (tag == "p" && line.Length > 0) PushLine(joinNext: true);
                    {
                        var prevPt = curFontPt; var prevFamily = curFamily;
                        var prevColor = curColor;
                        var styleBold = false;
                        var styleItalic = false;
                        // Chain-selector run styling (`.Title span.SmallerTitle`,
                        // `.RiskCategory` pills): the class/inline handlers below win.
                        if (chainBase is not null && tag != "p")
                        {
                            var chSpanElem = ChainTokElem(tag, tok.Attributes);
                            chainOpenElems!.Add(chSpanElem);
                            if (chainTdElem is not null
                                && MatchChainDecls(chainRules, BuildOpenChain()) is { } srd)
                            {
                                if (srd.TryGetValue("display", out var sdisp))
                                    chSpanElem.Display = sdisp.Trim().ToLowerInvariant();
                                if (srd.TryGetValue("font-size", out var sfs))
                                {
                                    var sBase = curFontPt > 0 ? curFontPt
                                        : cellClassPt > 0 ? cellClassPt : cellFontSize;
                                    var spm = Regex.Match(sfs.Trim(), @"^([\d.]+)\s*%$");
                                    if (spm.Success && double.TryParse(spm.Groups[1].Value,
                                            System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var sPct)
                                        && sPct > 0)
                                        curFontPt = sBase * sPct / 100.0;
                                    else if (ChainLenPt(sfs, sBase) is > 0 and var sAbs)
                                        curFontPt = sAbs;
                                }
                                // font-weight:normal CANCELS an enclosing bold for
                                // this run (`.SmallerTitle` under a bold plate).
                                if (srd.TryGetValue("font-weight", out var sfwN)
                                    && boldDepth > 0
                                    && Regex.IsMatch(sfwN, @"^\s*(normal|[1-5]00)", RegexOptions.IgnoreCase))
                                {
                                    chainUnbold.Add((tag, boldDepth));
                                    boldDepth = 0;
                                }
                                ChainBoxOpenMaybe(chSpanElem, srd);
                                if (!styleBold && srd.TryGetValue("font-weight", out var sfw)
                                    && Regex.IsMatch(sfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                                {
                                    styleBold = true;
                                    boldDepth++;
                                    if (widenProbe) line.Append('');
                                }
                            }
                        }
                        // A stylesheet class named on the RUN itself ("<span
                        // class='rteFontSize-5'>") sizes that run inside the cell's
                        // paragraph. The run's own inline style still wins below.
                        if (cssRunFace is not null && tok.Attributes is not null
                            && tok.Attributes.TryGetValue("class", out var runCls))
                            foreach (var rc in runCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var rk = "." + rc;
                                if (!css.TryGetValue(rk, out var rcd)
                                    && (docCss is null || !docCss.TryGetValue(rk, out rcd))) continue;
                                if (rcd.TryGetValue("font-size", out var rfs)
                                    && TryParseLength(rfs, out var rfsp) && rfsp > 0) curFontPt = rfsp;
                                if (rcd.TryGetValue("font-family", out var rff)
                                    && FirstFontFamily(rff) is { Length: > 0 } rfam) curFamily = rfam;
                            }
                        // Legacy `<font size="1".."7">` attribute in a grid cell —
                        // browser-parsed (leading digits of junk like "7pt" count,
                        // clamped to the 1..7 scale, 7 = 36pt). The form grid's
                        // spacer row is sized from exactly this. Form-grid
                        // dialect only — the calibrated grids ignore the attribute.
                        if (formGridDialect && tag == "font" && tok.Attributes is not null
                            && tok.Attributes.TryGetValue("size", out var fSizeAttr))
                        {
                            var fst = fSizeAttr.Trim();
                            var fDigits = 0;
                            while (fDigits < fst.Length && (char.IsDigit(fst[fDigits])
                                   || (fDigits == 0 && fst[0] is '+' or '-'))) fDigits++;
                            if (fDigits > 0 && int.TryParse(fst[..fDigits], out var fSz))
                            {
                                if (fst[0] is '+' or '-') fSz = 3 + fSz;
                                curFontPt = HtmlFontSizeToPt(Math.Clamp(fSz, 1, 7));
                            }
                        }
                        if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var inl))
                        {
                            var fsm = Regex.Match(inl, @"font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
                            if (fsm.Success && TryParseLength(fsm.Groups[1].Value.Trim(), out var fsp)) curFontPt = fsp;
                            var ffm = Regex.Match(inl, @"font-family\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
                            if (ffm.Success && FirstFontFamily(ffm.Groups[1].Value) is { Length: > 0 } fam) curFamily = fam;
                            // …and its own colour: `<p style="color:#004178">` paints THIS
                            // paragraph, while its black sibling in the same cell stays black.
                            var colm = Regex.Match(inl, @"(?<![-\w])color\s*:\s*([^;""']+)",
                                RegexOptions.IgnoreCase);
                            if (colm.Success && ParseCssColor(colm.Groups[1].Value.Trim()) is { } inlCol)
                                curColor = inlCol;
                            // An inline font-weight (the expanded `font: bold …` shorthand)
                            // opens a bold run like <b> does, restored at the closing tag.
                            if (Regex.IsMatch(inl, @"font-weight\s*:\s*(bold|[7-9]00)", RegexOptions.IgnoreCase))
                            {
                                styleBold = true;
                                boldDepth++;
                                if (widenProbe) line.Append('');
                            }
                            // An inline font-style italic opens an italic run the same
                            // way (the form-grid band titles), restored at the close.
                            if (formGridDialect
                                && Regex.IsMatch(inl, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
                            {
                                styleItalic = true;
                                italicDepth++;
                            }
                            // Lifted dialect: a paragraph's vertical margins are real
                            // space between the cell's paragraphs. The gap ABOVE this
                            // one is the CSS-collapsed max of its own margin-top and
                            // the previous paragraph's margin-bottom; an em resolves
                            // against the paragraph's OWN font size (its inline
                            // font-size when declared, since it may not have applied
                            // to curFontPt yet).
                            if (liftNestedTables && !bandDialect && tag == "p")
                            {
                                var pEmBase = Regex.Match(inl, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                                        RegexOptions.IgnoreCase) is { Success: true } pfm
                                    && TryParseLength(pfm.Groups[1].Value.Trim(), out var pfsPt) && pfsPt > 0
                                    ? pfsPt
                                    : curFontPt > 0 ? curFontPt
                                    : cellClassPt > 0 ? cellClassPt : cellFontSize;
                                double pTop = 0, pBot = 0;
                                if (Regex.Match(inl, @"(?<![-\w])margin\s*:\s*([^;""']+)",
                                        RegexOptions.IgnoreCase) is { Success: true } pShm)
                                {
                                    var parts = pShm.Groups[1].Value
                                        .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    double PartPt(string v) =>
                                        v.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                                        && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var em)
                                            ? em * pEmBase
                                            : TryParseLength(v, out var abs) ? abs : 0;
                                    if (parts.Length > 0) pTop = pBot = PartPt(parts[0]);
                                    if (parts.Length >= 3) pBot = PartPt(parts[2]);
                                }
                                if (Regex.Match(inl, @"(?<![-\w])margin-top\s*:\s*([^;""']+)",
                                        RegexOptions.IgnoreCase) is { Success: true } pTm)
                                {
                                    var v = pTm.Groups[1].Value.Trim();
                                    pTop = v.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                                        && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var emT)
                                        ? emT * pEmBase
                                        : TryParseLength(v, out var absT) ? absT : pTop;
                                }
                                if (Regex.Match(inl, @"(?<![-\w])margin-bottom\s*:\s*([^;""']+)",
                                        RegexOptions.IgnoreCase) is { Success: true } pBm)
                                {
                                    var v = pBm.Groups[1].Value.Trim();
                                    pBot = v.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                                        && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var emB)
                                        ? emB * pEmBase
                                        : TryParseLength(v, out var absB) ? absB : pBot;
                                }
                                var pCollapsed = Math.Max(pTop, cellPrevPBottomPt);
                                if (pCollapsed > 0) lineMarginTop = pCollapsed;
                                cellPrevPBottomPt = pBot;
                            }
                            // Band dialect: a paragraph's explicit top margin survives as a
                            // gap above its first line in the cell layout.
                            if (bandDialect && tag == "p")
                            {
                                var mtm = Regex.Match(inl, @"margin-top\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
                                if (mtm.Success && double.TryParse(mtm.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var pmt) && pmt > 0)
                                    lineMarginTop = pmt;
                                // margin-left in pt or em (em against the paragraph's own
                                // resolved font size), netted against a NEGATIVE text-indent —
                                // the "margin-left:2em; text-indent:-2em" hanging-indent idiom
                                // leaves the first line at the content edge.
                                var mlm = Regex.Match(inl, @"margin-left\s*:\s*([\d.]+)\s*(pt|em)", RegexOptions.IgnoreCase);
                                if (mlm.Success && double.TryParse(mlm.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var pml) && pml > 0)
                                {
                                    var emBase = curFontPt > 0 ? curFontPt : 8;
                                    var ml = mlm.Groups[2].Value.Equals("em", StringComparison.OrdinalIgnoreCase)
                                        ? pml * emBase : pml;
                                    var tim = Regex.Match(inl, @"text-indent\s*:\s*(-?[\d.]+)\s*(pt|em)", RegexOptions.IgnoreCase);
                                    if (tim.Success && double.TryParse(tim.Groups[1].Value,
                                            System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var ti) && ti < 0)
                                        ml = Math.Max(0, ml + (tim.Groups[2].Value.Equals("em", StringComparison.OrdinalIgnoreCase)
                                            ? ti * emBase : ti));
                                    lineMarginLeft = ml;
                                }
                            }
                        }
                        styleStack.Add((tag, prevPt, prevFamily, styleBold, prevColor, styleItalic));
                    }
                    break;
                case "sup":
                case "sub":
                    // Probe: open a superscript/subscript run — its glyphs measure at
                    // 85% of the line size in the min-content pass (the filing-dialect
                    // CSS shrink), marked by a sentinel pair in the line buffer.
                    if (widenProbe && cell is not null) line.Append('\uE002');
                    break;
                case "h1":
                case "h2":
                    // Chain-styled section heading: a BLOCK box spanning the cell
                    // (the report's red bars) — own line, background, centred text
                    // in its own colour, sized by the heading rule's percent font.
                    if (chainBase is not null && cell is not null && chainTdElem is not null)
                    {
                        if (line.Length > 0) PushLine();
                        var chHElem = ChainTokElem(tag, tok.Attributes);
                        chainOpenElems!.Add(chHElem);
                        var hPrevPt = curFontPt; var hPrevFam = curFamily; var hBold = false;
                        var hPrevColor = curColor;
                        if (MatchChainDecls(chainRules, BuildOpenChain()) is { } hd)
                        {
                            if (hd.TryGetValue("font-size", out var hfs))
                            {
                                var hBase = curFontPt > 0 ? curFontPt
                                    : cellClassPt > 0 ? cellClassPt : cellFontSize;
                                var hpm = Regex.Match(hfs.Trim(), @"^([\d.]+)\s*%$");
                                if (hpm.Success && double.TryParse(hpm.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var hPct)
                                    && hPct > 0)
                                    curFontPt = hBase * hPct / 100.0;
                                else if (ChainLenPt(hfs, hBase) is > 0 and var hAbs)
                                    curFontPt = hAbs;
                            }
                            if (hd.TryGetValue("font-weight", out var hfw)
                                && Regex.IsMatch(hfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                            {
                                hBold = true;
                                boldDepth++;
                                if (widenProbe) line.Append('');
                            }
                            if ((hd.TryGetValue("background-color", out var hbg)
                                    || hd.TryGetValue("background", out hbg))
                                && ParseCssColor(hbg) is { } hFill)
                            {
                                var hFontPt = curFontPt > 0 ? curFontPt : cellFontSize;
                                var hRun = new ChainBoxRun
                                {
                                    Elem = chHElem, StartLen = line.Length, Fill = hFill,
                                    FullWidth = true,
                                    TextCentered = hd.TryGetValue("text-align", out var hta)
                                        && hta.Contains("center", StringComparison.OrdinalIgnoreCase),
                                };
                                if (hd.TryGetValue("color", out var hcol)
                                    && ParseCssColor(hcol) is { } hTextCol)
                                    hRun.TextColor = hTextCol;
                                if (hd.TryGetValue("padding", out var hpv))
                                {
                                    var (hpT, hpR, hpB, hpL) = ChainPadPt(hpv, hFontPt);
                                    hRun.PadT = hpT; hRun.PadR = hpR; hRun.PadB = hpB; hRun.PadL = hpL;
                                }
                                (chainBoxOpen ??= new List<ChainBoxRun>()).Add(hRun);
                            }
                        }
                        styleStack.Add((tag, hPrevPt, hPrevFam, hBold, hPrevColor, false));
                    }
                    break;
                case "div":
                    // True once a chain rule has styled this div — a styled div may be
                    // an inline-block plate or a box run, and those keep riding their
                    // line; only a PLAIN div takes the block break below.
                    var divChainStyled = false;
                    // Chain-selector block styling (`.Title > div` silver plates,
                    // `.TrafficLight` boxes): fonts ride the styleStack exactly like
                    // an inline span style; a styled box's fill is approximated as
                    // the cell's own until block boxes render for real.
                    if (chainBase is not null && cell is not null)
                    {
                        var chDivElem = ChainTokElem(tag, tok.Attributes);
                        chainOpenElems!.Add(chDivElem);
                        var dvPrevPt = curFontPt; var dvPrevFamily = curFamily; var dvBold = false;
                        var dvPrevColor = curColor;
                        if (chainTdElem is not null
                            && MatchChainDecls(chainRules, BuildOpenChain()) is { } dvd)
                        {
                            divChainStyled = true;
                            if (dvd.TryGetValue("display", out var ddisp))
                                chDivElem.Display = ddisp.Trim().ToLowerInvariant();
                            if (dvd.TryGetValue("font-size", out var dfs2))
                            {
                                var dBase = curFontPt > 0 ? curFontPt
                                    : cellClassPt > 0 ? cellClassPt : cellFontSize;
                                var dpm2 = Regex.Match(dfs2.Trim(), @"^([\d.]+)\s*%$");
                                if (dpm2.Success && double.TryParse(dpm2.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var dPct)
                                    && dPct > 0)
                                    curFontPt = dBase * dPct / 100.0;
                                else if (ChainLenPt(dfs2, dBase) is > 0 and var dAbs)
                                    curFontPt = dAbs;
                            }
                            if (dvd.TryGetValue("font-weight", out var dfw)
                                && Regex.IsMatch(dfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                            {
                                dvBold = true;
                                boldDepth++;
                                if (widenProbe) line.Append('');
                            }
                            // An inline-block div with a background is a real box run
                            // (title plates, badges); a BLOCK-level background still
                            // tints the cell as the closest approximation — EXCEPT a
                            // border-radius div (a rounded CAPSULE around the nested
                            // grid it wraps): that paints behind the grid instead.
                            ChainBoxOpenMaybe(chDivElem, dvd);
                            var divIsCapsule = dvd.TryGetValue("border-radius", out var capR)
                                && (dvd.ContainsKey("background-color") || dvd.ContainsKey("background"));
                            if ((chDivElem.Display ?? "") != "inline-block"
                                && !divIsCapsule
                                && cell.BackgroundColor is null
                                && (dvd.TryGetValue("background-color", out var dbg)
                                    || dvd.TryGetValue("background", out dbg))
                                && ParseCssColor(dbg) is { } dbgc)
                                cell.BackgroundColor = dbgc;
                            if (divIsCapsule
                                && (dvd.TryGetValue("background-color", out var capBg)
                                    || dvd.TryGetValue("background", out capBg))
                                && ParseCssColor(capBg) is { } capFill)
                            {
                                var capBase = curFontPt > 0 ? curFontPt
                                    : cellClassPt > 0 ? cellClassPt : cellFontSize;
                                var (cpT2, cpR2, _, cpL2) = dvd.TryGetValue("padding", out var capPad)
                                    ? ChainPadPt(capPad, capBase) : (0, 0, 0, 0);
                                // The capsule div's MARGIN is white space outside the
                                // pill: it insets the whole capsule from the host
                                // cell's content box (the risks td's `margin: 0.5ex`
                                // is the gap left above each pill).
                                var (cmT2, cmR2, _, cmL2) = dvd.TryGetValue("margin", out var capMar)
                                    ? ChainPadPt(capMar, capBase) : (0, 0, 0, 0);
                                pendingCapsule = (capFill,
                                    Math.Max(0, ChainLenPt(capR!, capBase)),
                                    Math.Max(cpL2, cpR2), cpT2, Math.Max(cmT2, Math.Max(cmL2, cmR2)));
                            }
                            // A BLOCK div's padding insets the cell's text on all
                            // sides (the description body's `div { padding: 1em }`).
                            // The sibling heading bar is immune: a full-width bar
                            // anchors at the cell's BORDER BOX at draw time.
                            // A CAPSULE div is exempt: its padding is already the
                            // pill's own outset around the grid it wraps, and folding
                            // it into the cell too would inset the pill twice.
                            if ((chDivElem.Display ?? "") != "inline-block" && !divIsCapsule
                                && dvd.TryGetValue("padding", out var dvPad2))
                            {
                                var dvBase = curFontPt > 0 ? curFontPt
                                    : cellClassPt > 0 ? cellClassPt : cellFontSize;
                                var (dpT, dpR, dpB, dpL) = ChainPadPt(dvPad2, dvBase);
                                if (dpL + dpR > 0)
                                {
                                    cellPadLeftPt += dpL;
                                    cellCssPadPt += dpL + dpR;
                                }
                                cellChainPadTopPt = Math.Max(cellChainPadTopPt, dpT);
                                cellChainPadBotPt = Math.Max(cellChainPadBotPt, dpB);
                            }
                        }
                        styleStack.Add((tag, dvPrevPt, dvPrevFamily, dvBold, dvPrevColor, false));
                    }
                    // A PLAIN div is a BLOCK box: it opens on a line of its own, so a
                    // run of them stacks. `<div>IntroText</div>` eight times is eight
                    // lines, not one — they were running together and the section that
                    // holds them came out a single line tall.
                    if (liftNestedTables && cell is not null && !divChainStyled)
                    {
                        // …but a line holding ONLY the pending list marker stays open:
                        // the ::marker rides the item's first CONTENT line even when
                        // the item opens with a block child (`<LI>\n<DIV>caption…`
                        // draws "1. caption" together, not an orphaned
                        // marker line). A whitespace/&nbsp;-only line COLLAPSES at the
                        // block boundary instead of becoming a phantom box.
                        if (IsAllWhitespace(line)) line.Clear();
                        else if (line.Length > 0
                            && !Regex.IsMatch(line.ToString(), @"^\s*(?:\d+\.|•)\s*$"))
                            PushLine();
                        // …and a block box inside a paragraph is RE-PARENTED out of it:
                        // `<p><span style="font-weight:bold"><div>…` closes the p, and
                        // the span is not rebuilt around the div, so those lines take
                        // the CELL's own font and none of the inline run's weight
                        // (these set regular, not bold).
                        if (styleStack.Count > 0)
                        {
                            curFontPt = styleStack[0].PrevPt;
                            curFamily = styleStack[0].PrevFamily;
                            curColor = styleStack[0].PrevColor;
                            foreach (var sf in styleStack)
                                if (sf.BoldBump && boldDepth > 0) boldDepth--;
                            foreach (var sf in styleStack)
                                if (sf.ItalicBump && italicDepth > 0) italicDepth--;
                            styleStack.Clear();
                        }
                    }
                    // …and a div's OWN inline font-size sizes its content whether or not
                    // a selector reached it (`<div style="font-size:24px">` is the email
                    // template's only headline size). The chain branch above already
                    // stacked a restore frame; without one the div stacks its own.
                    if (liftNestedTables && cell is not null && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("style", out var dvFontSt) && dvFontSt is not null
                        && Regex.Match(dvFontSt, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                            RegexOptions.IgnoreCase) is { Success: true } dvFsm
                        && TryParseLength(dvFsm.Groups[1].Value.Trim(), out var dvFsp) && dvFsp > 0)
                    {
                        if (chainBase is null) styleStack.Add((tag, curFontPt, curFamily, false, curColor, false));
                        curFontPt = dvFsp;
                    }
                    // A fixed-width div inside a cell: its box sizes the column (the
                    // content wraps inside it — see the CloseCell measurement). A
                    // percent margin-left resolves against the div's own box:
                    // x = W + p·x  ⇒  x = W / (1 − p).
                    // A block inside a cell contributes its own box to the row. The WIDTH
                    // half of that is content-box sizing (uaCellBoxes only); the HEIGHT is
                    // plain CSS — a fixed-height block floors its row in any dialect.
                    if (cell is not null && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("style", out var dvSt) && dvSt is not null)
                    {
                        // A pre/pre-wrap box keeps the source newline that follows its
                        // opening tag, which costs it a leading empty line box.
                        if (uaCellBoxes && Regex.IsMatch(dvSt,
                                @"white-space\s*:\s*(?:-\w+-)?pre(?:-wrap|-line)?\b", RegexOptions.IgnoreCase))
                            preWrapPending = true;
                        var dvW = uaCellBoxes
                            ? Regex.Match(dvSt, @"width\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase)
                            : Match.Empty;
                        if (dvW.Success && double.TryParse(dvW.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var dvPx) && dvPx > 0)
                        {
                            // Content-box: the div's own padding widens its box.
                            foreach (Match dpm in Regex.Matches(dvSt,
                                @"padding-(left|right)\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase))
                                dvPx += double.Parse(dpm.Groups[2].Value,
                                    System.Globalization.CultureInfo.InvariantCulture);
                            var mlPct = 0.0;
                            var dvMl = Regex.Match(dvSt,
                                @"margin\s*:\s*[\d.]+%?\s+[\d.]+%?\s+[\d.]+%?\s+(\d+(?:\.\d+)?)%|margin-left\s*:\s*(\d+(?:\.\d+)?)%",
                                RegexOptions.IgnoreCase);
                            if (dvMl.Success)
                                double.TryParse(dvMl.Groups[dvMl.Groups[1].Success ? 1 : 2].Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out mlPct);
                            if (mlPct is > 0 and < 100) dvPx /= 1 - mlPct / 100;
                            var dvPt = dvPx * PxToPtW;
                            if (dvPt > cellFixedDivPt) cellFixedDivPt = dvPt;
                        }
                        // A fixed-height div occupies its box inside the cell, so it
                        // floors the row the way a cell height does — plus its own top
                        // margin, whose percent form resolves against the width of the
                        // cell that contains it.
                        if (Regex.Match(dvSt, @"(?<!\w-)height\s*:\s*([\d.]+\s*(?:px|pt|cm|mm|in))",
                                RegexOptions.IgnoreCase) is { Success: true } dvHm
                            && TryParseLength(dvHm.Groups[1].Value.Replace(" ", ""), out var dvHPt)
                            && dvHPt > 0)
                        {
                            var dvMt = 0.0;
                            var dvMtm = Regex.Match(dvSt,
                                @"margin\s*:\s*(\d+(?:\.\d+)?)%|margin-top\s*:\s*(\d+(?:\.\d+)?)%",
                                RegexOptions.IgnoreCase);
                            // A percent margin resolves against the containing block's
                            // CONTENT width — the cell's declared width, without the
                            // padding ResolveCellWidthPt folded into the column footprint.
                            var dvBase = uaCellBoxes
                                ? Math.Max(0, cellWidthPt - cellCssPadPt) : cellWidthPt;
                            if (dvMtm.Success && dvBase > 0
                                && double.TryParse(dvMtm.Groups[dvMtm.Groups[1].Success ? 1 : 2].Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var dvMtPct))
                                dvMt = dvBase * dvMtPct / 100.0;
                            // The box's own horizontal rule sits under its content, so a
                            // bottom border adds to the height it claims in the row.
                            var dvBb = 0.0;
                            if (uaCellBoxes && Regex.Match(dvSt,
                                    @"border-bottom\s*:\s*(\d+(?:\.\d+)?)\s*px",
                                    RegexOptions.IgnoreCase) is { Success: true } dvBbm)
                                dvBb = double.Parse(dvBbm.Groups[1].Value,
                                    System.Globalization.CultureInfo.InvariantCulture) * PxToPtW;
                            rowMinHeightPt = Math.Max(rowMinHeightPt, dvHPt + dvMt + dvBb);
                            // A CSS height on a child is its CONTENT box: the cell's own
                            // padding sits outside it, unlike a legacy height="N" floor.
                            rowMinHeightIsContent = true;
                        }
                    }
                    break;
                case "td":
                case "th":
                    if (tableDepth > 1) break;   // nested cell: text flows into the host cell
                    if (cell is not null) CloseCell();
                    row ??= new Row();
                    styleStack.Clear(); curFontPt = rowFontPt; curFamily = null;
                    lineFontPt = 0; lineFamily = null; lineStyleSet = false;
                    boldDepth = 0; lineHadText = false; lineAllBold = true;
                    italicDepth = 0; lineAllItalic = true; lineRunMarks = null;
                    cell = new Cell(); isHeader = tag == "th";
                    cellPendingBrBlank = false;
                    cellInlineOptions = null;
                    // The cell's OWN inline font declarations open the run style its
                    // content inherits — a report table styles the td directly as often
                    // as it wraps the text in a span.
                    cellFgStrutPt = 0; cellFgStrutFontPt = 0;
                    if (tok.Attributes is not null
                        && tok.Attributes.TryGetValue("style", out var tdFontSt) && tdFontSt is not null)
                    {
                        var tdFs = Regex.Match(tdFontSt, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                            RegexOptions.IgnoreCase);
                        // …honoured only when SMALLER than the grid's base — the same
                        // deliberate limit the pitch model keeps elsewhere: an ENLARGED
                        // td (a letterhead's 16.5pt line) must not reflow the whole
                        // sheet, which lays out on the base rhythm.
                        if (tdFs.Success && TryParseLength(tdFs.Groups[1].Value.Trim(), out var tdFsPt)
                            && tdFsPt > 0 && tdFsPt < (curFontPt > 0 ? curFontPt : cellFontSize))
                            curFontPt = tdFsPt;
                        // A td styling its own size re-struts its cell at that size's
                        // box (the Description band's 10pt td → 16px = 12.0).
                        if (formGridDialect && tdFs.Success
                            && TryParseLength(tdFs.Groups[1].Value.Trim(), out var tdStrutPt)
                            && tdStrutPt > 0)
                        {
                            cellFgStrutPt = PxLinePt(tdStrutPt, VerdanaWinLineRatio);
                            cellFgStrutFontPt = tdStrutPt;
                        }
                        // …and a td styling font-style italic sets its whole cell
                        // italic (the Description band's own td style).
                        if (formGridDialect && Regex.IsMatch(tdFontSt,
                                @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
                            italicDepth = 1;
                        var tdFf = Regex.Match(tdFontSt, @"(?<![-\w])font-family\s*:\s*([^;""']+)",
                            RegexOptions.IgnoreCase);
                        if (tdFf.Success && FirstFontFamily(tdFf.Groups[1].Value) is { Length: > 0 } tdFam)
                            curFamily = tdFam;
                    }
                    if (tok.Attributes?.ContainsKey("nowrap") == true) cell.HtmlNoWrap = true;
                    // A cell's own fill paints over its row's band.
                    if (tok.Attributes is not null)
                    {
                        if (tok.Attributes.TryGetValue("style", out var tdBgSt) && tdBgSt is not null
                            && Regex.Match(tdBgSt, @"background(?:-color)?\s*:\s*([^;]+)",
                                RegexOptions.IgnoreCase) is { Success: true } tdBgm
                            && ParseCssColor(tdBgm.Groups[1].Value) is { } tdBg)
                            cell.BackgroundColor = tdBg;
                        else if (tok.Attributes.TryGetValue("bgcolor", out var tdBgAttr)
                            && ParseCssColor(tdBgAttr) is { } tdBgA)
                            cell.BackgroundColor = tdBgA;
                    }
                    // white-space:nowrap keeps a cell on one line whether it arrives
                    // inline or through one of the cell's classes
                    if (tok.Attributes is not null)
                    {
                        if (tok.Attributes.TryGetValue("style", out var nwStyle)
                            && Regex.IsMatch(nwStyle, @"white-space\s*:\s*nowrap", RegexOptions.IgnoreCase))
                            cell.HtmlNoWrap = true;
                        if (!cell.HtmlNoWrap && tok.Attributes.TryGetValue("class", out var nwCls))
                            foreach (var cn in nwCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                                if ((css.TryGetValue("." + cn, out var cRule)
                                        || (docCss?.TryGetValue("." + cn, out cRule) ?? false))
                                    && cRule.TryGetValue("white-space", out var ws)
                                    && ws.Contains("nowrap", StringComparison.OrdinalIgnoreCase))
                                { cell.HtmlNoWrap = true; break; }
                    }
                    // The legacy ALIGN attribute aligns the cell's own content, exactly
                    // like a `text-align` in its style (which, parsed below, still wins).
                    if ((liftNestedTables || uaCellBoxes || authoredCellChrome || formGridDialect)
                        && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("align", out var tdAl)
                        && ParseAlignAttr(tdAl) is { } tdAlign)
                    { alignSet = true; cellAlign = tdAlign; }
                    // A cell HEIGHT="N" (px) is an HTML minimum on its row's height.
                    if (tok.Attributes is not null && tok.Attributes.TryGetValue("height", out var tdH)
                        && double.TryParse(Regex.Match(tdH, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var tdHpx) && tdHpx > 0)
                    {
                        rowMinHeightPt = Math.Max(rowMinHeightPt, tdHpx * PxToPt);
                        cellOwnHeightDecl = true;
                    }
                    // A CSS height on the cell floors its row the same way the attribute
                    // does — including the unit forms an authored spacer row uses.
                    // …and a lifted grid floors its row on the cell's declared height
                    // too (`<td style="height:105px">` under a 85px logo keeps the
                    // 20px of band below the picture).
                    if ((uaCellBoxes || liftNestedTables)
                        && tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var tdHSt)
                        && tdHSt is not null
                        && Regex.Match(tdHSt, @"(?<!\w-)height\s*:\s*([\d.]+\s*(?:px|pt|cm|mm|in))",
                            RegexOptions.IgnoreCase) is { Success: true } tdHm
                        && TryParseLength(tdHm.Groups[1].Value.Replace(" ", ""), out var tdHPt) && tdHPt > 0)
                    {
                        rowMinHeightPt = Math.Max(rowMinHeightPt, tdHPt);
                        cellOwnHeightDecl = true;
                    }
                    // The legacy VALIGN attribute is `vertical-align` by another
                    // spelling: an explicit `valign="top"` beats the lifted dialect's
                    // centre default (a 129 pt grid was floating 10.5 pt down inside
                    // its 150 pt band cell that declared top).
                    if (liftNestedTables && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("valign", out var tdVaAttr)
                        && cell.VerticalAlignment == VerticalAlignment.None)
                        cell.VerticalAlignment = tdVaAttr.Trim().ToLowerInvariant() switch
                        {
                            "top" => VerticalAlignment.Top,
                            "middle" or "center" => VerticalAlignment.Center,
                            "bottom" => VerticalAlignment.Bottom,
                            _ => VerticalAlignment.None,
                        };
                    cellBold = false;
                    cellClassPt = 0;
                    if (tok.Attributes is not null
                        && tok.Attributes.TryGetValue("class", out var szCls))
                        foreach (var cn in szCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            Dictionary<string, string>? szRule = null;
                            if (!css.TryGetValue("." + cn, out szRule)) docCss?.TryGetValue("." + cn, out szRule);
                            if (szRule is null || !szRule.TryGetValue("font-size", out var szv)) continue;
                            var szm = Regex.Match(szv, @"([\d.]+)\s*(pt|px)?", RegexOptions.IgnoreCase);
                            if (!szm.Success || !double.TryParse(szm.Groups[1].Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var szn) || szn <= 0)
                                continue;
                            cellClassPt = szm.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                                ? szn * 0.75 : szn;
                            break;
                        }
                    if (tok.Attributes is not null)
                    {
                        if (tok.Attributes.TryGetValue("style", out var bStyle)
                            && Regex.IsMatch(bStyle, @"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase))
                            cellBold = true;
                        if (!cellBold && tok.Attributes.TryGetValue("class", out var bCls))
                            foreach (var cn in bCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                                if ((css.TryGetValue("." + cn, out var bRule)
                                        || (docCss?.TryGetValue("." + cn, out bRule) ?? false))
                                    && bRule.TryGetValue("font-weight", out var fw)
                                    && Regex.IsMatch(fw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                                { cellBold = true; break; }
                    }
                    rowHasCell = true; if (tag == "td") rowHasTd = true;
                    cellWidthPt = ResolveCellWidthPt(tok.Attributes, css, contentBox: uaCellBoxes,
                        readWidthAttr: liftNestedTables) * PxToPtW;
                    // The cell's own CSS padding is part of its column footprint — it
                    // rides on the measured content, and on a fixed-width inner div.
                    cellCssPadPt = 0; cellFixedDivPt = 0; cellPadLeftPt = 0;
                    // …and a lifted grid reads it too: an image column's `padding-right`
                    // is the gutter between it and the text column beside it, and its
                    // `padding-bottom` the gap under each picture in a stack of them.
                    if (liftNestedTables
                        && tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var tdVSt) && tdVSt is not null)
                    {
                        foreach (Match pm in Regex.Matches(tdVSt,
                            @"padding-(top|bottom)\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase))
                        {
                            var vPadPt = double.Parse(pm.Groups[2].Value,
                                System.Globalization.CultureInfo.InvariantCulture) * PxToPt;
                            if (pm.Groups[1].Value.Equals("top", StringComparison.OrdinalIgnoreCase))
                                cellChainPadTopPt = Math.Max(cellChainPadTopPt, vPadPt);
                            else cellChainPadBotPt = Math.Max(cellChainPadBotPt, vPadPt);
                        }
                        // …and the SHORTHAND's vertical value (`padding: 8px 0px`) is
                        // the same declaration in one token.
                        if (Regex.Match(tdVSt, @"(?<![-\w])padding\s*:\s*(\d+(?:\.\d+)?)\s*px",
                                RegexOptions.IgnoreCase) is { Success: true } pshm
                            && double.TryParse(pshm.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var pshPx)
                            && pshPx > 0)
                        {
                            cellChainPadTopPt = Math.Max(cellChainPadTopPt, pshPx * PxToPt);
                            cellChainPadBotPt = Math.Max(cellChainPadBotPt, pshPx * PxToPt);
                        }
                    }
                    if ((uaCellBoxes || liftNestedTables)
                        && tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var tdSt2) && tdSt2 is not null)
                        foreach (Match pm in Regex.Matches(tdSt2,
                            @"padding-(left|right)\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase))
                        {
                            var padPt = double.Parse(pm.Groups[2].Value,
                                System.Globalization.CultureInfo.InvariantCulture) * PxToPtW;
                            cellCssPadPt += padPt;
                            if (pm.Groups[1].Value.Equals("left", StringComparison.OrdinalIgnoreCase))
                                cellPadLeftPt += padPt;
                        }
                    if (tok.Attributes is not null
                        && tok.Attributes.TryGetValue("width", out var wPctAttr)
                        && wPctAttr.Trim().EndsWith('%')
                        && double.TryParse(wPctAttr.Trim().TrimEnd('%'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var wPct)
                        && wPct > 0)
                        cellWidthPct = wPct;
                    // An inline style="width: N%" declares the same percent grid the
                    // width attribute does.
                    if (cellWidthPct <= 0 && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("style", out var wPctStyle))
                    {
                        var pm = Regex.Match(wPctStyle, @"width\s*:\s*(\d+(?:\.\d+)?)\s*%");
                        if (pm.Success && double.TryParse(pm.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var wPctS)
                            && wPctS > 0)
                            cellWidthPct = wPctS;
                    }
                    if (tok.Attributes is not null)
                    {
                        if (tok.Attributes.TryGetValue("colspan", out var cs) && int.TryParse(cs, out var csn) && csn > 0)
                            colSpan = csn;
                        if (tok.Attributes.TryGetValue("rowspan", out var rs) && int.TryParse(rs, out var rsn) && rsn > 1)
                            cellRowSpan = rsn;
                        if (tok.Attributes.TryGetValue("style", out var st))
                        {
                            // A cell opting out of the table's borders keeps its box
                            // blank (`<td style="border-style:none">` in a bordered
                            // table — the layout-table idiom).
                            if (Regex.IsMatch(st, @"border(-style)?\s*:\s*none", RegexOptions.IgnoreCase))
                                cell.Border = new BorderInfo(BorderSide.None);
                            var am = Regex.Match(st, @"text-align\s*:\s*(left|right|center)", RegexOptions.IgnoreCase);
                            if (am.Success)
                            {
                                alignSet = true;
                                cellAlign = am.Groups[1].Value.ToLowerInvariant() switch
                                {
                                    "right" => HorizontalAlignment.Right,
                                    "center" => HorizontalAlignment.Center,
                                    _ => HorizontalAlignment.Left,
                                };
                            }
                            // Band-dialect per-cell border sides (BORDER-LEFT:1px solid #000…):
                            // proxy-card notice frames, corner marks and signature rules are
                            // drawn as TD border sides.
                            if (bandDialect || authoredCellChrome)
                            {
                                BorderSide bsSides = 0; double bsW = 0; Color? bsColor = null;
                                foreach (var (bprop, bside) in new[]
                                {
                                    ("border-left", BorderSide.Left), ("border-top", BorderSide.Top),
                                    ("border-bottom", BorderSide.Bottom), ("border-right", BorderSide.Right),
                                })
                                {
                                    if (!TryParseBorderShorthand(st, bprop, out var bpt, out var bcol)) continue;
                                    bsSides |= bside;
                                    if (bpt > bsW) bsW = bpt;
                                    bsColor ??= bcol;
                                }
                                if (bsSides != 0)
                                    cell.Border = new BorderInfo(bsSides, bsW <= 0 ? 0.75 : bsW,
                                        bsColor ?? Color.Black);
                            }
                        }
                    }
                    // Flat class rules on the cell (`.resulttableheadercelltables
                    // { background-color: silver; border: 1px solid white }`) —
                    // the grey header cells/columns of the report grids. Lifted
                    // dialect only; legacy paths never read class backgrounds.
                    if (liftNestedTables && cell.BackgroundColor is null
                        && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("class", out var bgCls))
                        foreach (var cn in bgCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            Dictionary<string, string>? bgRule = null;
                            if (!css.TryGetValue("." + cn, out bgRule))
                                docCss?.TryGetValue("." + cn, out bgRule);
                            if (bgRule is null) continue;
                            if ((bgRule.TryGetValue("background-color", out var clsBg)
                                    || bgRule.TryGetValue("background", out clsBg))
                                && ParseCssColor(clsBg) is { } clsBgc)
                                cell.BackgroundColor = clsBgc;
                            if (cell.Border is null && bgRule.TryGetValue("border", out var clsBrd)
                                && ChainBorder(clsBrd) is { } clsBi)
                                cell.Border = clsBi;
                            if (cell.BackgroundColor is not null) break;
                        }
                    // Chain-selector styling for this cell — the least specific
                    // layer: every inline/attribute handler above already had its
                    // say, so only the still-unset slots fill.
                    if (chainBase is not null)
                    {
                        chainTdElem = ChainTokElem(tag, tok.Attributes);
                        chainOpenElems?.Clear();
                        var tdChain = new List<CssElem>(chainBase) { chainTdElem };
                        if (MatchChainDecls(chainRules, tdChain) is { } cd)
                        {
                            // The stylesheet reaches this grid's cells (see
                            // Table.HtmlChainStyledCells).
                            table.HtmlChainStyledCells = true;
                            // Font first: the ex/em pads below resolve on the cell size.
                            if (cellClassPt <= 0 && cd.TryGetValue("font-size", out var cfs))
                            {
                                var pcm = Regex.Match(cfs.Trim(), @"^([\d.]+)\s*%$");
                                if (pcm.Success && double.TryParse(pcm.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out var fsPct)
                                    && fsPct > 0)
                                    cellClassPt = cellFontSize * fsPct / 100.0;
                                else if (ChainLenPt(cfs, cellFontSize) is > 0 and var fsAbs)
                                    cellClassPt = fsAbs;
                            }
                            if (!cellBold && cd.TryGetValue("font-weight", out var cfw)
                                && Regex.IsMatch(cfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                                cellBold = true;
                            if (cell.BackgroundColor is null
                                && (cd.TryGetValue("background-color", out var cbg)
                                    || cd.TryGetValue("background", out cbg))
                                && ParseCssColor(cbg) is { } cbgc)
                                cell.BackgroundColor = cbgc;
                            if (cd.TryGetValue("color", out var ccol) && ParseCssColor(ccol) is { } ccolc)
                                cellChainColor = ccolc;
                            if (cell.Border is null && cd.TryGetValue("border", out var cbord)
                                && ChainBorder(cbord) is { } cbi)
                            {
                                // Separate borders: the UA border-spacing shows as
                                // extra stroke between the cells' individual borders —
                                // but ONLY for white separator strokes (the Managers
                                // grid); a real coloured border (the detail buttons'
                                // 1px gray) keeps its declared width.
                                var cbWhite = cbi.Color is { R: > 240, G: > 240, B: > 240 };
                                var cbEff = chainBorderSeparate && cbWhite
                                    ? new BorderInfo(BorderSide.Box,
                                        cbi.Width + SeparateBorderSpacingPt, cbi.Color)
                                    : cbi;
                                // border-radius rounds the cell's box (the detail
                                // buttons); the bg fill follows it at draw.
                                if (cd.TryGetValue("border-radius", out var cbr)
                                    && ChainLenPt(cbr, cellClassPt > 0 ? cellClassPt : cellFontSize)
                                        is > 0 and var cbrPt)
                                    cbEff.RoundedBorderRadius = cbrPt;
                                cell.Border = cbEff;
                            }
                            if (!alignSet && cd.TryGetValue("text-align", out var cta))
                            {
                                var ca = cta.Trim().ToLowerInvariant() switch
                                {
                                    "right" => HorizontalAlignment.Right,
                                    "center" => HorizontalAlignment.Center,
                                    "left" => HorizontalAlignment.Left,
                                    _ => (HorizontalAlignment?)null,
                                };
                                if (ca is { } cav) { alignSet = true; cellAlign = cav; }
                            }
                            if (cd.TryGetValue("white-space", out var cws)
                                && cws.Contains("nowrap", StringComparison.OrdinalIgnoreCase))
                                cell.HtmlNoWrap = true;
                            // A chain rule's percent width declares the column share
                            // (`.CategoryName { width: 80% }` — the pill grid's name
                            // column absorbs the slack, the detail box hugs its text).
                            if (cellWidthPct <= 0 && cd.TryGetValue("width", out var cwv2)
                                && cwv2.TrimEnd().EndsWith("%", StringComparison.Ordinal)
                                && double.TryParse(cwv2.Trim().TrimEnd('%'),
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var cwPct)
                                && cwPct > 0)
                                cellWidthPct = cwPct;
                            if (cell.VerticalAlignment == VerticalAlignment.None
                                && cd.TryGetValue("vertical-align", out var cva))
                                cell.VerticalAlignment = cva.Trim().ToLowerInvariant() switch
                                {
                                    "top" => VerticalAlignment.Top,
                                    "middle" => VerticalAlignment.Center,
                                    "bottom" => VerticalAlignment.Bottom,
                                    _ => VerticalAlignment.None,
                                };
                            if (cellCssPadPt <= 0 && cd.TryGetValue("padding", out var cpad))
                            {
                                var padBase = cellClassPt > 0 ? cellClassPt : cellFontSize;
                                var (cpT, cpR, cpB, cpL) = ChainPadPt(cpad, padBase);
                                if (cpL + cpR > 0) { cellCssPadPt = cpL + cpR; cellPadLeftPt = cpL; }
                                cellChainPadTopPt = cpT; cellChainPadBotPt = cpB;
                            }
                        }
                    }
                    // …and the cell's OWN inline style outranks every selector that
                    // reached it: `<td style="font-size:10px;color:#9c9e9f">` sizes and
                    // colours that cell's text. Read last so it wins over the class and
                    // chain rules applied above.
                    if (liftNestedTables && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("style", out var tdOwnSt) && tdOwnSt is not null)
                    {
                        // ⚠ PARTIAL, deliberately: only a SMALLER declared size is
                        // honoured. Shrinking a cell's text can never wrap a line that
                        // fitted before, so it is safe today; growing it needs the
                        // column model to widen with the cell's own font, which it does
                        // not yet do — a 16.5 pt header cell then wraps a title that
                        // must stay whole. Lift the guard once that lands.
                        if (Regex.Match(tdOwnSt, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                                RegexOptions.IgnoreCase) is { Success: true } tdFsm
                            && TryParseLength(tdFsm.Groups[1].Value.Trim(), out var tdFsp) && tdFsp > 0
                            && tdFsp < (cellClassPt > 0 ? cellClassPt : cellFontSize))
                            cellClassPt = tdFsp;
                        if (Regex.Match(tdOwnSt, @"(?<![-\w])color\s*:\s*([^;""']+)",
                                RegexOptions.IgnoreCase) is { Success: true } tdColm
                            && ParseCssColor(tdColm.Groups[1].Value.Trim()) is { } tdCol)
                            cellChainColor = tdCol;
                        // The cell's own `line-height` pitches its lines: an em (or bare
                        // number) resolves against the cell's DECLARED font size even
                        // when the applied size kept a larger base (the guard above) —
                        // `line-height:1.1em; font-size:10px` is an 8.25 pt pitch.
                        if (Regex.Match(tdOwnSt, @"(?<![-\w])line-height\s*:\s*([\d.]+)\s*(em|px|pt)?",
                                RegexOptions.IgnoreCase) is { Success: true } tdLhm
                            && double.TryParse(tdLhm.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var tdLh)
                            && tdLh > 0)
                        {
                            var tdDeclPt = Regex.Match(tdOwnSt,
                                    @"(?<![-\w])font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase)
                                is { Success: true } fsm2
                                && TryParseLength(fsm2.Groups[1].Value.Trim(), out var declPt)
                                && declPt > 0 ? declPt
                                : cellClassPt > 0 ? cellClassPt : cellFontSize;
                            cellOwnLineHPt = tdLhm.Groups[2].Value.ToLowerInvariant() switch
                            {
                                "px" => tdLh * PxToPt,
                                "pt" => tdLh,
                                _ => tdLh * tdDeclPt,   // em or a bare number
                            };
                        }
                    }
                    break;
                case "a":
                    // Open an inline anchor: remember where its text starts on the
                    // current line and the target URL.
                    if (cell is not null)
                    {
                        // The anchor's colour — its inline style, else the sheet's
                        // `a { color: … }` rule — rides the style stack for the
                        // anchor's extent, exactly like a coloured <span>.
                        Color? aCol = null;
                        if (tok.Attributes is not null
                            && tok.Attributes.TryGetValue("style", out var aSt)
                            && Regex.Match(aSt, @"(?<![-\w])color\s*:\s*([^;]+)",
                                RegexOptions.IgnoreCase) is { Success: true } aCm)
                            aCol = ParseCssColor(aCm.Groups[1].Value.Trim());
                        aCol ??= docAnchorColor;
                        if (aCol is not null)
                        {
                            styleStack.Add(("a", curFontPt, curFamily, false, curColor, false));
                            curColor = aCol;
                        }
                    }
                    if (cell is not null && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("href", out var aHref)
                        && !string.IsNullOrEmpty(aHref))
                        openAnchor = (line.Length, aHref);
                    break;
                case "strong":
                case "b":
                    if (cell is not null)
                    {
                        // Form-grid: a bold run OPENING mid-line marks a style-run
                        // boundary (the segment so far keeps the regular face).
                        if (formGridDialect)
                        {
                            lineRunMarks ??= new();
                            if (lineRunMarks.Count == 0)
                                lineRunMarks.Add((0, boldDepth > 0));
                        }
                        boldDepth++;
                        if (formGridDialect) lineRunMarks!.Add((line.Length, true));
                        // Probe: the min-content measure applies real bold metrics per
                        // RUN (a bold word followed by a regular superscript measures
                        // each piece with its own face), marked by sentinels.
                        if (widenProbe) line.Append('\uE000');
                    }
                    break;
                case "br":
                    if (cell is not null)
                    {
                        // An explicit <br> on an empty line is a deliberate blank line: it
                        // keeps its line box (at the active style's size) as vertical space.
                        // A LONE br on an empty line (not preceded by another br — e.g.
                        // right after a block boundary or table close) is tagged: the
                        // lifted-unstyled dialect drops it, keeping only the N−1 blanks
                        // of an N-br run (the <BR><BR> rhythm); styled dialects keep
                        // every one — they were calibrated that way.
                        var loneBrBlank = line.Length == 0 && !cellPendingBrBlank;
                        if (!lineStyleSet) { lineFontPt = curFontPt; lineFamily = curFamily; }
                        if (lineColor is null) lineColor = curColor;
                        PushLine(keepIfBlank: true);
                        if (loneBrBlank) (loneBrBlankLines ??= new HashSet<int>()).Add(lines.Count - 1);
                        cellPendingBrBlank = true;
                    }
                    break;
                case "img":
                    // An image inside a cell (a logo, an inline-<svg> placeholder, an SVG
                    // diagram) becomes an Image paragraph; the generator's cell renderer
                    // rasterizes SVG sources and sizes unfixed images by the document rule.
                    if (cell is not null && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("src", out var cellSrc) && !string.IsNullOrEmpty(cellSrc))
                    {
                        byte[]? cellImgBytes;
                        if (cellSrc.StartsWith("inline-svg:", StringComparison.Ordinal)
                            && int.TryParse(cellSrc["inline-svg:".Length..], out var cellSvgIdx)
                            && inlineSvgs is not null && cellSvgIdx >= 0 && cellSvgIdx < inlineSvgs.Count)
                            cellImgBytes = inlineSvgs[cellSvgIdx];
                        else
                            cellImgBytes = LoadConverterImage(cellSrc, options);
                        double ciw = 0, cih = 0;
                        if (tok.Attributes.TryGetValue("width", out var ciwS))
                            double.TryParse(Regex.Match(ciwS, @"[\d.]+").Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out ciw);
                        if (tok.Attributes.TryGetValue("height", out var cihS))
                            double.TryParse(Regex.Match(cihS, @"[\d.]+").Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out cih);
                        // A CSS-sized cell image (style="width:240px; height:45px") is as
                        // explicit as the attribute form.
                        if ((ciw <= 0 || cih <= 0) && tok.Attributes.TryGetValue("style", out var ciStyle))
                        {
                            var cwm = Regex.Match(ciStyle, @"(?<![-\w])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                            if (ciw <= 0 && cwm.Success)
                                double.TryParse(cwm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out ciw);
                            var chm = Regex.Match(ciStyle, @"(?<![-\w])height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                            if (cih <= 0 && chm.Success)
                                double.TryParse(chm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out cih);
                        }
                        // Form-document dialect: an unreachable image with explicit CSS
                        // dimensions still occupies its box — the layout draws the
                        // broken-image frame (a bordered white box with a torn-page
                        // glyph) at the declared size instead of collapsing the cell.
                        if (cellImgBytes is null && cellFontShorthand && ciw > 4 && cih > 4)
                        {
                            var phInv = System.Globalization.CultureInfo.InvariantCulture;
                            var phSvg = "<svg xmlns='http://www.w3.org/2000/svg' width='" + ciw.ToString(phInv)
                                + "' height='" + cih.ToString(phInv) + "'>"
                                + "<rect x='0.5' y='0.5' width='" + (ciw - 1).ToString(phInv)
                                + "' height='" + (cih - 1).ToString(phInv)
                                + "' fill='white' stroke='#000000' stroke-width='1'/>"
                                + "<rect x='6.5' y='" + (cih / 2 - 8).ToString("0.##", phInv)
                                + "' width='12' height='16' fill='white' stroke='#808080' stroke-width='1'/>"
                                + "</svg>";
                            cellImgBytes = System.Text.Encoding.UTF8.GetBytes(phSvg);
                        }
                        // The image's DECLARED box sizes its column whether or not the
                        // bytes ever arrive — a spacer GIF that fails to load still holds
                        // its gutter open, the way a browser reserves a broken image's box.
                        if (liftNestedTables && ciw > 0)
                            cellImgWidthPt = Math.Max(cellImgWidthPt, ciw * PxToPtW);

                        if (cellImgBytes is not null)
                        {
                            PushLine();
                            var cellImg = new Image { ImageStream = new System.IO.MemoryStream(cellImgBytes) };
                            // A cell that declares an alignment aligns its IMAGE too, not
                            // only its text — an `align="right"` logo cell hangs its logo
                            // on the right edge of the cell the same way a right-aligned
                            // run seats there.
                            if (alignSet) cellImg.HorizontalAlignment = cellAlign;
                            if (liftNestedTables && ciw > 0)
                                cellImgWidthPt = Math.Max(cellImgWidthPt, ciw * PxToPtW);
                            if (IsSvgBytes(cellImgBytes)) cellImg.FileType = ImageFileType.Svg;
                            if (ciw > 0) cellImg.FixWidth = ciw * PxToPt;
                            if (cih > 0) cellImg.FixHeight = cih * PxToPt;
                            // Text already on the cell keeps its place ABOVE the image:
                            // defer the paragraph add until CloseCell flushes the lines.
                            if (lines.Count > 0) (pendingCellImgs ??= new List<Image>()).Add(cellImg);
                            else cell.Paragraphs.Add(cellImg);
                        }
                    }
                    break;
                case "ol":
                case "ul":
                    if (cell is not null && line.Length > 0) PushLine();
                    // UA margin-block-start on a TOP-LEVEL list opening mid-cell:
                    // one line box above the first item. A nested list carries none
                    // (`ul ul { margin-block-start: 0 }` in every UA sheet).
                    if (liftNestedTables && cell is not null && !tok.IsSelfClosing
                        && listNesting.Count == 0 && lines.Count > 0)
                    {
                        if (!lineStyleSet) { lineFontPt = curFontPt; lineFamily = curFamily; }
                        PushLine(keepIfBlank: true);
                    }
                    if (!tok.IsSelfClosing) listNesting.Add((tag == "ol", 0));
                    // Content of the list — including bare text before its first
                    // <li> — seats on the list's padding-inline-start indent.
                    liStandingIndentPt = ListItemIndentPt * listNesting.Count;
                    break;
                case "li":
                    if (cell is not null)
                    {
                        if (line.Length > 0) PushLine();
                        if (listNesting.Count > 0)
                        {
                            var (liOrd, liCnt) = listNesting[^1];
                            listNesting[^1] = (liOrd, liCnt + 1);
                            var liMarker = liOrd
                                ? (liCnt + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                                : "•";
                            liStandingIndentPt = ListItemIndentPt * listNesting.Count;
                            // Hanging marker: the item's text seats ON the list indent,
                            // the marker rides just left of it ("1." draws
                            // as its own run ending one gap before the text).
                            var liFs = curFontPt > 0 ? curFontPt : cellFontSize;
                            lineMarginLeft = Math.Max(0,
                                liStandingIndentPt - MeasureLine(liMarker + " ", false, liFs));
                            // No implicit gap above an item: the question rhythm
                            // (2 line boxes between items) is the
                            // markup's own explicit <BR><BR>, which survives as a
                            // kept blank line — a plain <ul> stacks its items at
                            // bare line pitch.
                            line.Append(liMarker).Append(' ');
                            lineHadText = true;
                        }
                    }
                    break;
                case "input":
                    // A form control INSIDE a grid cell occupies its line inline (it
                    // must not flush the cell's text flow). A checkbox/radio paints
                    // as a near-invisible white box, so only its advance matters —
                    // and that is within the wrap tolerance; a text-like input
                    // contributes its VALUE as cell text (the visible part of the
                    // filled-in control).
                    if (cell is not null && tok.Attributes is not null)
                    {
                        tok.Attributes.TryGetValue("type", out var inType);
                        inType = inType?.Trim().ToLowerInvariant() ?? "text";
                        // A radio in a form grid rides its text line INLINE as a marker
                        // char (`◯ ◯Yes ◉ ◉No` sets on one line); the
                        // factory-built option is drawn as the circle glyph and its
                        // widget placed there by the table render pass.
                        if (inType == "radio" && makeRadio is not null)
                        {
                            tok.Attributes.TryGetValue("name", out var rName);
                            var rChecked = tok.Attributes.ContainsKey("checked");
                            var rOpt = makeRadio(rName ?? "", rChecked);
                            line.Append(rChecked
                                ? Table.InlineRadioCheckedChar : Table.InlineRadioChar);
                            (cellInlineOptions ??= new List<Aspose.Pdf.Forms.RadioButtonOptionField>())
                                .Add(rOpt);
                            lineHadText = true;
                        }
                        // A push button in a form grid draws as its 3D chrome around
                        // the caption (the Print/Close controls); the
                        // caption rides the line between PUA markers so the column
                        // measures it and the render pass draws the box.
                        else if (inType is "button" or "submit" && makeRadio is not null
                            && tok.Attributes.TryGetValue("value", out var btnVal)
                            && !string.IsNullOrWhiteSpace(btnVal))
                        {
                            line.Append(Table.InlineButtonChar).Append(btnVal.Trim())
                                .Append(Table.InlineButtonEndChar);
                            lineHadText = true;
                        }
                        else if (inType is not ("checkbox" or "radio" or "hidden" or "submit" or "button" or "image")
                            && tok.Attributes.TryGetValue("value", out var inVal)
                            && !string.IsNullOrWhiteSpace(inVal))
                        {
                            if (line.Length > 0 && !char.IsWhiteSpace(line[^1])) line.Append(' ');
                            line.Append(inVal);
                            lineHadText = true;
                        }
                    }
                    break;
            }
        }
        CloseRow();
        // Apply the deferred column-span constraints: a spanning cell only forces its
        // columns' widths up when they don't already sum to its content — the deficit is
        // spread evenly, so a wide spanning line grows the columns it needs without
        // inflating thin spacer columns that other rows keep narrow.
        foreach (var (start, span, sMin, sMax, sHdr) in spanConstraints)
        {
            if (start + span > colMinW.Count) continue;
            void Raise(List<double> arr, double target)
            {
                double sum = 0; for (var k = 0; k < span; k++) sum += arr[start + k];
                if (sum >= target || span <= 0) return;
                // …and the deficit lands on the columns that can TAKE it: a column with a
                // declared width keeps it. A spanning logo cell beside a 15 px spacer was
                // spreading its own width over both, floor-ing the spacer at a third of
                // the logo and pushing everything in the row that far right.
                var takers = 0;
                for (var k = 0; k < span; k++)
                    if (start + k >= colDeclW.Count || colDeclW[start + k] <= 0) takers++;
                var add = (target - sum) / (takers > 0 ? takers : span);
                for (var k = 0; k < span; k++)
                    if (takers <= 0 || start + k >= colDeclW.Count || colDeclW[start + k] <= 0)
                        arr[start + k] += add;
            }
            Raise(colMinW, sMin);
            Raise(colMaxW, sMax);
            if (sHdr > 0) Raise(colHdrW, sHdr);
        }
        if (headerRows > 0 && headerRows < table.Rows.Count) table.RepeatingRowsCount = headerRows;

        // Form-document dialect: a `<table height="90">` attribute is a minimum on the
        // TABLE height, shared equally by its rows (the browser's table model) — each
        // row floors at its share, content still grows a row past it.
        if (cellFontShorthand && tblHeightPx > 0 && table.Rows.Count > 0)
        {
            var rowShare = tblHeightPx * PxToPt / table.Rows.Count;
            foreach (Row hr in table.Rows)
                if (rowShare > hr.MinRowHeight) hr.MinRowHeight = rowShare;
        }

        if (table.Rows.Count == 0) { naturalWidthPt = 0; return null; }
        naturalWidthPt = 0;
        // Colgroup grid: each column is its declared width, stretched to min-content when an
        // unbreakable run needs more (colMinW already includes padding/border slack).
        // A COLGROUP whose cols declare NO widths ("<col class=…>") pins nothing —
        // under the chain dialect those tables keep their content/percent column
        // model (legacy dialects keep the historical min-content pinning).
        if (colGroupPt is { Count: > 0 } && colGroupPt.Count == maxCols
            && (chainBase is null || colGroupPt.Exists(w => w > 0)))
        {
            colWidthsPt = new List<double>(maxCols);
            for (var i = 0; i < maxCols; i++)
            {
                var declared = colGroupPt[i];
                var minC = i < colMinW.Count ? colMinW[i] : 0;
                colWidthsPt.Add(Math.Max(declared, minC));
            }
        }
        // A per-column percent grid (the classic sizing row) fixes the split against
        // the table's width — honoured before any content fit when the declared
        // percents dominate the grid. Columns the row leaves unsized (spacer cells)
        // share the leftover percent evenly; every column is floored at its
        // min-content so an unbreakable run still gets room.
        var pctCapW = 0.0;
        var pctNaturalW = 0.0;
        double[]? pctMinsForDraw = null;
        if (colWidthsPt is null && availWidthPt > 0 && maxCols > 0 && colPctW.Count > 0)
        {
            while (colPctW.Count < maxCols) colPctW.Add(0);
            double sumPct = 0; var nSpec = 0;
            for (var i = 0; i < maxCols; i++)
                if (colPctW[i] > 0) { sumPct += colPctW[i]; nSpec++; }
            // Form-document dialect: a LONE declared percent lays out the way a browser
            // does — the declared column takes its percent, the auto columns share the
            // remainder ("<td style='width:25%'>" beside an auto cell splits 75/25).
            // Outside the dialect the legacy majority guard holds.
            if (nSpec * 2 >= maxCols && sumPct >= 50 || cellFontShorthand && nSpec > 0 && sumPct < 100
                // Chain dialect: a LONE declared percent lays out browser-style too
                // (`.CategoryName { width: 80% }` — the name column takes its
                // percent, the detail buttons hug their min-content).
                || chainBase is not null && nSpec > 0 && sumPct < 100)
            {
                var rem = Math.Max(0, 100 - sumPct) / Math.Max(1, maxCols - nSpec);
                var total = sumPct + rem * (maxCols - nSpec);
                var tableW = tableWidthFrac * availWidthPt;
                // A table that declares no width of its own is SHRINK-TO-FIT: its box is
                // only as wide as the content needs, and the declared percents split THAT,
                // not the page. The fitting width is the largest a column's own max-content
                // implies for the whole table (its share is pct/total of it), capped by
                // what is available.
                if (!tableWidthDeclared && uaDocGrid)
                {
                    var fitW = 0.0;
                    for (var i = 0; i < maxCols; i++)
                    {
                        var pct = colPctW[i] > 0 ? colPctW[i] : rem;
                        var maxC = i < colMaxW.Count ? colMaxW[i] : 0;
                        if (pct > 0 && maxC > 0) fitW = Math.Max(fitW, maxC * total / pct);
                    }
                    if (fitW > 0) tableW = Math.Min(tableW, fitW);
                }
                colWidthsPt = new List<double>(maxCols);
                var mins = new double[maxCols];
                pctMinsForDraw = mins;
                double sumW = 0;
                for (var i = 0; i < maxCols; i++)
                {
                    var w = (colPctW[i] > 0 ? colPctW[i] : rem) / total * tableW;
                    // Dash-aware floor: the percent grid lets a hyphenated token wrap
                    // after its dashes, so the floor is the widest POST-BREAK segment.
                    mins[i] = i < colMinBrkW.Count ? colMinBrkW[i] : 0;
                    var cwv = Math.Max(w, mins[i]);
                    colWidthsPt.Add(cwv);
                    sumW += cwv;
                }
                // Min-content floors (an unbreakable header/word wider than its declared %)
                // can push the sum past the table width, which would cascade into the page
                // auto-widen. Squeeze it back inside — but in two tiers so a wide CONTENT
                // column is protected the way a browser's auto layout protects it:
                //   1. reclaim WASTE first — the width a column holds above its own
                //      max-content (an empty spacer column allocated a few % it never fills);
                //   2. only if that is not enough, squeeze the remaining above-min slack
                //      proportionally (the legacy behaviour).
                // Without tier 1 the big body column (huge %, huge slack-above-min) absorbs
                // almost all of the excess and its text over-wraps to a sliver.
                // The NATURAL width of a non-absolute percent grid is its MIN-CONTENT
                // floor sum: percents distribute at layout time and never size the
                // sheet, and a paragraph column's max-content (its whole text on one
                // line) must not either — the SHEET grows to the
                // floors, and the percents then re-resolve against the wider box.
                if (!tableWidthDeclaredAbs)
                {
                    // Shrink-to-fit: the grid's preferred width is its max-content sum
                    // CLAMPED to the table box (a paragraph column's one-line max must
                    // not size the sheet), floored at the min-content floors (an
                    // unbreakable run still grows the sheet past the box).
                    double cnat = 0, cmax = 0;
                    for (var i = 0; i < maxCols; i++)
                    {
                        cnat += mins[i];
                        cmax += Math.Max(mins[i], i < colMaxW.Count ? colMaxW[i] : 0);
                    }
                    pctNaturalW = Math.Max(cnat, Math.Min(cmax, tableW));
                }
                if (sumW > tableW + 0.01)
                {
                    var excess = sumW - tableW;
                    double waste = 0;
                    var wasteCol = new double[maxCols];
                    for (var i = 0; i < maxCols; i++)
                    {
                        var cap = Math.Max(mins[i], i < colMaxW.Count ? colMaxW[i] : 0);
                        wasteCol[i] = Math.Max(0, colWidthsPt[i] - cap);
                        waste += wasteCol[i];
                    }
                    var takeW = Math.Min(excess, waste);
                    if (waste > 0)
                        for (var i = 0; i < maxCols; i++) colWidthsPt[i] -= wasteCol[i] / waste * takeW;
                    excess -= takeW;
                    if (excess > 0.01)
                    {
                        double slack = 0;
                        for (var i = 0; i < maxCols; i++) slack += colWidthsPt[i] - mins[i];
                        if (slack > 0)
                            for (var i = 0; i < maxCols; i++)
                                colWidthsPt[i] -= (colWidthsPt[i] - mins[i]) / slack * Math.Min(excess, slack);
                    }
                }
                // A percent grid inside a table that DECLARES its own ABSOLUTE width
                // never widens the page: the declared width pins the box and any
                // residual floor overflow spills inside it (browser overflow). A
                // percent-declared or undeclared table has nothing absolute to pin
                // against — its columns keep their min-content floors and the grid
                // overflows the box, so the sheet grows to it.
                if (tableWidthDeclaredAbs) pctCapW = tableW;
            }
        }
        if (colWidthsPt is { Count: > 0 } cw && cw.Count == maxCols)
        {
            // A chain-dialect percent grid (per-cell `width: N%`) resolves at DRAW
            // time against its real box — the build's available width is the outer
            // table's, and pt columns fixed against it come out ~2× wide and clip.
            double cwSum = 0;
            foreach (var w in cw) cwSum += w;
            var emitPctHere = chainBase is not null && tableWidthDeclared
                && !tableWidthDeclaredAbs && cwSum > 0 && colPctW.Count > 0;
            // Draw-time resolution data for the percent grid (see the fallback's
            // twin): declared shares floor at the dash-aware mins.
            if (emitPctHere && pctMinsForDraw is { } pmins && pmins.Length == cw.Count)
            {
                table.HtmlColMinPt = pmins;
                table.HtmlColPctDeclared = true;
                // Which of the emitted shares were really declared: the rest carry the
                // even leftover this branch synthesises for the auto columns, and the
                // draw-time resolver must not treat those as fill targets.
                var pctDecl = new bool[cw.Count];
                for (var i = 0; i < pctDecl.Length && i < colPctW.Count; i++)
                    pctDecl[i] = colPctW[i] > 0;
                table.HtmlColPctDeclaredCols = pctDecl;
                if (colMaxW.Count == cw.Count)
                {
                    var hMax2 = new double[colMaxW.Count];
                    for (var i = 0; i < colMaxW.Count; i++)
                        hMax2[i] = Math.Max(colMaxW[i], pmins[i]);
                    table.HtmlColMaxPt = hMax2;
                }
            }
            var sb = new StringBuilder();
            for (var i = 0; i < cw.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                if (emitPctHere)
                    sb.Append((cw[i] / cwSum * 100.0 * tableWidthFrac)
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
                else
                    sb.Append(cw[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                naturalWidthPt += cw[i];
            }
            table.ColumnWidths = sb.ToString();
            if (pctCapW > 0 && naturalWidthPt > pctCapW) naturalWidthPt = pctCapW;
            // The PREFERRED width of a percent grid is its shrink-to-fit CONTENT
            // preference, NOT the box-filling share sum (resolved against the
            // build's stand-in box, that sum hands the HOST cell absurd max-content
            // room — the risks pill column balloons on it).
            table.HtmlPreferredWidthPt = naturalWidthPt;
            if (pctMinsForDraw is { } prefMins && colMaxW.Count == prefMins.Length)
            {
                double prefMin = 0, prefMax = 0, autoMax = 0, declFrac = 0;
                for (var i = 0; i < prefMins.Length; i++)
                {
                    prefMin += prefMins[i];
                    var cMax = Math.Max(colMaxW[i], prefMins[i]);
                    prefMax += cMax;
                    if (i < colPctW.Count && colPctW[i] > 0) declFrac += colPctW[i] / 100.0;
                    else autoMax += cMax;
                }
                // CSS max-content of a grid with a DECLARED percent column: the auto
                // columns fill the remaining (1 − p) of the table, so the whole table
                // wants autoMax / (1 − p). It is what makes the risks pill's host
                // column ask for room beyond its content floors (the
                // 148.7 pt Risk-Category column) instead of pinning at min-content.
                if (chainBase is not null && declFrac > 0 && declFrac < 1 && autoMax > 0)
                    prefMax = Math.Max(prefMax, autoMax / (1 - declFrac));
                table.HtmlPreferredWidthPt = Math.Max(prefMin,
                    Math.Min(prefMax, naturalWidthPt));
            }
            // Content-driven natural for non-absolute percent grids (REPLACES the
            // box-filling sum): the layout keeps its percent columns, only the
            // reported preferred width changes.
            if (pctNaturalW > 0) naturalWidthPt = pctNaturalW;
        }
        else if (availWidthPt > 0 && colMaxW.Count == maxCols && colMaxW.Count > 0 && colMaxW.TrueForAll(w => w > 0))
        {
            // No explicit widths: content-fit. Only when the caller opts in with a real available
            // width (the wide-table ConvertFromHtml path); legacy callers (header/footer & in-flow
            // HtmlFragment tables) keep the equal-% fallback below so their layout is unchanged.
            // Use max-content (no wrapping) when the table fits the available width; otherwise fall
            // back to min-content (columns shrink to their widest word and multi-word cells wrap) —
            // matching a browser's auto table layout.
            double sumMax = 0; foreach (var w in colMaxW) sumMax += w;
            // Min-content, but keep header cells on one line when the resulting table still fits the
            // available width; if even that overflows, fall back to the pure widest-word min so a wide
            // header never forces the page/table wider (that would override the caller's page size).
            var minPref = new List<double>(colMinW);
            double sumPref = 0;
            for (var i = 0; i < minPref.Count; i++) { if (colHdrW[i] > minPref[i]) minPref[i] = colHdrW[i]; sumPref += minPref[i]; }
            var chosenMin = (sumPref <= availWidthPt) ? minPref : colMinW;
            var chosen = (availWidthPt <= 0 || sumMax <= availWidthPt) ? colMaxW : chosenMin;
            // A declared cell width is honoured only while the table FITS its box — the
            // fixed columns keep their declared width (incl. cell padding) and the auto
            // columns absorb the leftover. Once the table overflows, min-content takes
            // over and the declarations contribute nothing.
            // Scoped to the UA-cell-box grids: elsewhere a fitting table keeps its
            // natural (max-content) columns, and stretching a width:100% one to the
            // full box re-wraps every calibrated legacy layout.
            if (uaCellBoxes && ReferenceEquals(chosen, colMaxW))
            {
                List<double>? floored = null;
                for (var i = 0; i < chosen.Count && i < colDeclW.Count; i++)
                    if (colDeclW[i] > chosen[i])
                    {
                        floored ??= new List<double>(chosen);
                        floored[i] = colDeclW[i];
                    }
                if (floored is not null) chosen = floored;
                // A fitting table that DECLARES its width fills that box: the declared
                // columns keep exactly what they asked for and the AUTO columns absorb
                // all the leftover (an auto label column beside fixed date columns
                // stretches to the full container).
                if (tableWidthDeclared && availWidthPt > 0)
                {
                    var boxW = tableWidthFrac * availWidthPt;
                    double sumSel = 0; foreach (var w in chosen) sumSel += w;
                    double autoW = 0;
                    var autoCount = 0;
                    for (var i = 0; i < chosen.Count; i++)
                        if (i >= colDeclW.Count || colDeclW[i] <= 0) { autoW += chosen[i]; autoCount++; }
                    if (boxW > sumSel + 0.01 && autoCount > 0)
                    {
                        var grown = new List<double>(chosen);
                        var leftover = boxW - sumSel;
                        for (var i = 0; i < grown.Count; i++)
                            if (i >= colDeclW.Count || colDeclW[i] <= 0)
                                grown[i] += autoW > 0 ? leftover * chosen[i] / autoW : leftover / autoCount;
                        chosen = grown;
                    }
                }
            }
            // A width-declared table (WIDTH="N%") fills its box: when the natural columns
            // overflowed and collapsed to min-content, hand the leftover width to the
            // columns that still want to grow (room = max-content − chosen), proportionally,
            // so the flexible text column expands to fill instead of wrapping to a sliver.
            // Fixed columns (max ≈ min) keep their width. Only the overflow (collapsed) case.
            if (!ReferenceEquals(chosen, colMaxW))
            {
                var tableW = tableWidthFrac * availWidthPt;
                double sumChosen = 0; foreach (var w in chosen) sumChosen += w;
                if (tableW > sumChosen + 0.01)
                {
                    double sumRoom = 0;
                    for (var i = 0; i < chosen.Count; i++) sumRoom += Math.Max(0, colMaxW[i] - chosen[i]);
                    if (sumRoom > 0)
                    {
                        var filled = new List<double>(chosen);
                        var leftover = tableW - sumChosen;
                        for (var i = 0; i < filled.Count; i++)
                        {
                            var room = Math.Max(0, colMaxW[i] - chosen[i]);
                            filled[i] += leftover * room / sumRoom;
                        }
                        chosen = filled;
                    }
                }
            }
            var sb = new StringBuilder();
            // A chain-rule percent-width table fills whatever box it lands in at
            // DRAW time (the outer cell's real width is unknown while it builds),
            // so its columns are emitted as PERCENT shares — surplus rides every
            // column proportionally (the surplus rule).
            double sumChosenAll = 0;
            foreach (var w in chosen) sumChosenAll += w;
            var emitPctCols = tableWidthPctOfBox && tableWidthDeclared
                && !tableWidthDeclaredAbs && sumChosenAll > 0;
            // The generator re-resolves these columns at DRAW time by the same
            // rule (fit → max-content + surplus; else floors + slack squeeze) —
            // hand it the per-column min/max the decision needs.
            if (emitPctCols && colMinW.Count == chosen.Count)
            {
                table.HtmlColMinPt = colMinW.ToArray();
                if (colMaxW.Count == chosen.Count)
                {
                    var hMax = new double[colMaxW.Count];
                    for (var i = 0; i < colMaxW.Count; i++)
                        hMax[i] = Math.Max(colMaxW[i], colMinW[i]);
                    table.HtmlColMaxPt = hMax;
                }
                // A cell that DECLARED `width="100%"` is a real box-filling target, so
                // the draw-time resolver must hand IT the surplus instead of spreading
                // it over the max-content proportions — without the mask the emitted
                // share was recomputed away and the declared column kept a quarter of
                // its row, shrinking every grid nested inside it in the same ratio.
                // A column whose width was DECLARED absolutely (`<td width="15">` — the
                // layout-table spacer idiom) is FIXED in CSS auto layout: it keeps that
                // width and the box's surplus goes to the auto columns beside it.
                if (colDeclW.Count == chosen.Count)
                {
                    var fixedCols = new bool[chosen.Count];
                    var anyFixed = false;
                    for (var i = 0; i < chosen.Count; i++)
                        if (colDeclW[i] > 0 && (i >= colPctW.Count || colPctW[i] <= 0))
                            fixedCols[i] = anyFixed = true;
                    if (anyFixed) table.HtmlColFixedCols = fixedCols;
                }
                var anyFillDecl = false;
                for (var i = 0; i < colPctW.Count && i < chosen.Count; i++)
                    if (colPctW[i] >= 100) { anyFillDecl = true; break; }
                if (anyFillDecl)
                {
                    var pctDeclB = new bool[chosen.Count];
                    for (var i = 0; i < pctDeclB.Length && i < colPctW.Count; i++)
                        pctDeclB[i] = colPctW[i] >= 100;
                    table.HtmlColPctDeclared = true;
                    table.HtmlColPctDeclaredCols = pctDeclB;
                }
                // A trailing nested-grid column absorbs ALL the surplus (it
                // stretches to fill; its siblings hug their content on the left —
                // the title row). A LEADING grid column (the risks pills) keeps
                // its floor and the surplus stays proportional in the text columns.
                if (nestedTableCols is not null && nestedTableCols.Contains(chosen.Count - 1))
                    table.HtmlSurplusCol = chosen.Count - 1;
            }
            // Surplus goes to the LAST column (whose nested grid stretches to fill):
            // the earlier columns keep their content share, so a title cell hugs its
            // plates instead of pooling dead space beside them.
            // A chain percent-width grid resolves against a box UNKNOWN at build
            // (the outer avail stands in) — max-content chosen against that box is
            // meaningless, so these grids lay out on MIN-content
            // floors (the budget wraps `Period / Cost type`); the shares then
            // re-resolve at draw. Cells with nowrap/box floors keep them (their
            // min IS the unwrapped width).
            if (emitPctCols && ReferenceEquals(chosen, colMaxW)
                && colMinW.Count == chosen.Count)
            {
                // Plain min floors when max-content was chosen against the build's
                // stand-in box; a table already on its min/pref floors keeps them
                // (the Risks grid's wide text columns take the surplus).
                chosen = colMinW;
                sumChosenAll = 0;
                foreach (var w in chosen) sumChosenAll += w;
            }
            var emitBoxW = tableWidthFrac * availWidthPt;
            var lastAbsorbs = emitPctCols && chosen.Count > 1 && emitBoxW > sumChosenAll + 0.01
                // …and only when the last column HOLDS a nested grid (which fills
                // whatever it gets) — a text column's share must stay proportional,
                // and a nested build's availWidthPt may not be its real box anyway.
                && nestedTableCols is not null && nestedTableCols.Contains(chosen.Count - 1);
            // ONE column declaring `width="100%"` is the layout-table idiom for "give me
            // everything my siblings' content does not need": its row-mates are spacer
            // cells that must keep exactly their content, and the whole remainder is the
            // declared column's. Spreading the row proportionally instead left a
            // 100 %-wide cell about a quarter of its row, and the grid nested inside it
            // shrank in the same proportion at every further level.
            var fillCol = -1;
            if (emitPctCols && !lastAbsorbs && colPctW.Count <= chosen.Count
                && emitBoxW > sumChosenAll + 0.01)
            {
                var declCount = 0;
                for (var i = 0; i < colPctW.Count; i++)
                    if (colPctW[i] >= 100) { fillCol = i; declCount++; }
                if (declCount != 1) fillCol = -1;
            }
            double sumOtherMins = 0;
            if (fillCol >= 0)
                for (var i = 0; i < chosen.Count; i++)
                    if (i != fillCol) sumOtherMins += chosen[i];
            for (var i = 0; i < chosen.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                if (emitPctCols)
                {
                    var share = fillCol >= 0
                        ? (i == fillCol
                            ? Math.Max(0.01, 1.0 - sumOtherMins / emitBoxW)
                            : chosen[i] / emitBoxW)
                        : lastAbsorbs
                        ? (i == chosen.Count - 1
                            ? Math.Max(0.01, 1.0 - sumPrev(chosen, i) / emitBoxW)
                            : chosen[i] / emitBoxW)
                        : chosen[i] / sumChosenAll;
                    sb.Append((share * 100.0 * tableWidthFrac)
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
                }
                else
                    sb.Append(chosen[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                naturalWidthPt += chosen[i];
            }
            static double sumPrev(List<double> list, int count)
            {
                double s = 0;
                for (var k = 0; k < count; k++) s += list[k];
                return s;
            }
            table.ColumnWidths = sb.ToString();
            // A chain-rule percent width (the `.Budget > table { width: 100% }`
            // idiom) resolves against its box at layout time and never sizes the
            // sheet — the reported natural is the PLAIN min-content floor sum
            // (the multi-word `Period / Cost type` header wraps;
            // single-word headers hold their width because one word
            // cannot wrap), the same rule the percent-column grids apply above.
            table.HtmlPreferredWidthPt = naturalWidthPt;
            if (tableWidthPctOfBox && !tableWidthDeclaredAbs)
            {
                double sumMinPref = 0;
                foreach (var w in colMinW) sumMinPref += w;
                naturalWidthPt = sumMinPref;
            }
        }
        else if (maxCols > 0)
        {
            // Even shares are the fallback — and they are RIGHT for a real grid whose
            // columns all hold content (a five-column signature table splits its box
            // five ways; min-content-proportional shares under-size the wordy columns
            // and wrap lines that must stay whole). The min-content vector takes
            // over only when it is DEGENERATE — some column measures (near) nothing,
            // the signature of colspan debris: a stray `<td colspan="3">` in one row
            // gives the table three columns while every other row fills only the first,
            // and an even split left the one real column a third of what its content
            // needs — a headline one letter wide. An EMPTY column takes no share; the
            // real ones divide the width in proportion to their content.
            double sumMinCols = 0;
            var anyEmptyCol = false;
            if (colMinW.Count == maxCols)
            {
                foreach (var w in colMinW)
                {
                    sumMinCols += w;
                    if (w <= 0.01) anyEmptyCol = true;
                }
            }
            var minShares = anyEmptyCol && sumMinCols > 0;
            var sb = new StringBuilder();
            for (var i = 0; i < maxCols; i++)
            {
                if (i > 0) sb.Append(' ');
                var share = minShares
                    ? tableWidthFrac * 100.0 * colMinW[i] / sumMinCols
                    : tableWidthFrac * 100.0 / maxCols;
                sb.Append(share.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('%');
            }
            table.ColumnWidths = sb.ToString();
        }

        // The declared cellspacing is real horizontal space between and around the
        // columns, and each cell keeps the UA's 1px padding pair — both are part
        // of the sized sheet (the status report's pair
        // row + 3·cellspacing + 4·0.75 = its 698.62 content width).
        if (chainBase is not null && tblCellSpacingPt > 0 && maxCols > 0 && naturalWidthPt > 0)
            naturalWidthPt += (maxCols + 1) * tblCellSpacingPt + maxCols * 1.5;

        // A table declaring an ABSOLUTE width ATTRIBUTE (`width="680"`) FILLS it,
        // like a browser: when the columns' content fit stays narrower, they grow
        // proportionally — and a declared width beyond the available area carries
        // into the page auto-widen through the natural width. CSS width rules are
        // deliberately NOT fill targets here: the stylesheet grids resolve through
        // the percent/colgroup models above.
        double tableWidthAbsPt = 0;
        if (tblTag.Success)
        {
            var twAbs = Regex.Match(tblTag.Value, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)\s*(px)?\s*[""'\s/>]",
                RegexOptions.IgnoreCase);
            if (twAbs.Success && double.TryParse(twAbs.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var twAbsPx))
                tableWidthAbsPt = twAbsPx * PxToPt;
        }
        if (tableWidthAbsPt > 0 && naturalWidthPt > 0 && tableWidthAbsPt > naturalWidthPt
            && table.ColumnWidths is { Length: > 0 } cwAbs && !cwAbs.Contains('%'))
        {
            var scale = tableWidthAbsPt / naturalWidthPt;
            var parts = cwAbs.Split(' ');
            var sb = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                var w = double.Parse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture) * scale;
                sb.Append(w.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            table.ColumnWidths = sb.ToString();
            naturalWidthPt = tableWidthAbsPt;
        }
        // Space and no-break space share one glyph, and the first of the two to occur
        // in the document's rendered text decides how BOTH read back out of the page —
        // a document that opens with an &nbsp; cell reports nbsp between all its words,
        // one that opens with plain text reports plain spaces even for &nbsp; entities.
        // ...and the decision belongs to the DOCUMENT, not to one grid: a lifted
        // nested table renders as its own Table, so scanning this one alone let an
        // inner grid that opens with plain text report plain spaces while the sheet's
        // very first cell held an &nbsp;. Walk the nested grids in document order too,
        // and hand every one of them the same winner.
        var grids = new List<Table>();
        CollectGrids(table, grids);
        foreach (var g in grids)
        {
            foreach (var r in g.Rows)
                foreach (Cell c in r.Cells)
                    foreach (var p in c.Paragraphs)
                        if (p is Text.TextFragment { Text: { Length: > 0 } t })
                            foreach (var ch in t)
                                if (ch is ' ' or ' ')
                                {
                                    foreach (var gg in grids) gg.HtmlSpaceClassFirst = ch;
                                    goto winnerFound;
                                }
        }
        winnerFound:
        return table;
    }
}
