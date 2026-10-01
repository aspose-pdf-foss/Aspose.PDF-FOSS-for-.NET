using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A UA-boxed cell's block (p, ul, ol) opens: its top margin is left pending above the next
    /// pushed line and its bottom margin waits for its close. Each margin is the block's own inline
    /// declaration, else the sheet's rule for its tag, else the browser's default em.</summary>
    private static void UaOpenBlock(TableParseState ps, string tag, Token? tok)
    {
        var style = tok?.Attributes is not null && tok.Attributes.TryGetValue("style", out var st) ? st : null;
        ps.uaPendingMarginPt = Math.Max(ps.uaPendingMarginPt, UaBlockMarginPt(ps, tag, style, top: true));
        ps.uaBlockBottoms.Add((tag, UaBlockMarginPt(ps, tag, style, top: false)));
    }

    /// <summary>The block closes: its bottom margin is left pending, collapsing with whatever is pending.</summary>
    private static void UaCloseBlock(TableParseState ps, string tag)
    {
        for (var k = ps.uaBlockBottoms.Count - 1; k >= 0; k--)
            if (ps.uaBlockBottoms[k].Tag == tag)
            {
                ps.uaPendingMarginPt = Math.Max(ps.uaPendingMarginPt, ps.uaBlockBottoms[k].Pt);
                ps.uaBlockBottoms.RemoveAt(k);
                return;
            }
        // A stray </p> (its paragraph already closed by a block inside it) is an EMPTY paragraph to the
        // HTML parser: its two margins collapse into one, left pending like any block's.
        if (tag == "p") ps.uaPendingMarginPt = Math.Max(ps.uaPendingMarginPt, UaBlockMarginPt(ps, tag, null, top: true));
    }

    private static double UaBlockMarginPt(TableParseState ps, string tag, string? style, bool top)
    {
        var fs = ps.curFontPt > 0 ? ps.curFontPt : ps.uaBaseFontPt > 0 ? ps.uaBaseFontPt : DefaultBodyFontPt;
        var side = top ? "margin-top" : "margin-bottom";
        if (style is not null && CssBoxSidePt(style, "margin", side, top ? 0 : 2, fs) is { } own) return own;
        foreach (var sheet in new IReadOnlyDictionary<string, Dictionary<string, string>>?[] { ps.uaSheet, ps.uaDocSheet })
            if (sheet is not null && sheet.TryGetValue(tag, out var rule)
                && CssBoxSidePt(rule, "margin", side, top ? 0 : 2, fs) is { } sheetPt)
                return sheetPt;
        return UaBlockMarginEm * fs;
    }

    /// <summary>One side of a box property from a style string: the longhand, else the shorthand's value
    /// for that side (1-4 values in the CSS order top, right, bottom, left). Null when neither is declared.</summary>
    private static double? CssBoxSidePt(string style, string prop, string longhand, int sideIdx, double fs)
    {
        var lh = Regex.Match(style, @"(?<![-\w])" + longhand + @"\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
        if (lh.Success) return CssLengthOrZeroPt(lh.Groups[1].Value.Trim(), fs);
        var sh = Regex.Match(style, @"(?<![-\w])" + prop + @"\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
        if (!sh.Success) return null;
        var parts = sh.Groups[1].Value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var idx = parts.Length switch { 1 => 0, 2 => sideIdx % 2, 3 => sideIdx == 3 ? 1 : sideIdx, _ => sideIdx };
        return idx < parts.Length ? CssLengthOrZeroPt(parts[idx], fs) : null;
    }

    private static double? CssBoxSidePt(Dictionary<string, string> rule, string prop, string longhand, int sideIdx, double fs)
    {
        if (rule.TryGetValue(longhand, out var lv)) return CssLengthOrZeroPt(lv.Trim(), fs);
        if (!rule.TryGetValue(prop, out var sv)) return null;
        return CssBoxSidePt(prop + ": " + sv, prop, longhand, sideIdx, fs);
    }

    /// <summary>A CSS length in points, em against the given size; an explicit zero is 0; null when unparsable.</summary>
    private static double? CssLengthOrZeroPt(string v, double fs)
    {
        if (IsZeroLength(v) || v.Equals("auto", StringComparison.OrdinalIgnoreCase)) return 0;
        var m = Regex.Match(v, @"^(-?(?:\d+(?:\.\d+)?|\.\d+))\s*em$", RegexOptions.IgnoreCase);
        if (m.Success) return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * fs;
        return TryParseLength(v);
    }

    /// <summary>The bottom rule a style declares: the `border-bottom` shorthand, else its longhands
    /// (`border-bottom-width` with a non-none `border-bottom-style`, and `border-bottom-color`).</summary>
    private static (double W, Color? C)? UaRuleBelow(string style)
    {
        if (TryParseBorderShorthand(style, "border-bottom") is (var shW, var shC) && shW > 0) return (shW, shC);
        var st = Regex.Match(style, @"(?<![-\w])border-bottom-style\s*:\s*(\w+)", RegexOptions.IgnoreCase);
        if (!st.Success || st.Groups[1].Value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || st.Groups[1].Value.Equals("hidden", StringComparison.OrdinalIgnoreCase)) return null;
        var wm = Regex.Match(style, @"(?<![-\w])border-bottom-width\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
        var w = wm.Success ? TryParseLength(wm.Groups[1].Value.Trim()) ?? 0 : UaMediumBorderPt;
        if (w <= 0) return null;
        var cm = Regex.Match(style, @"(?<![-\w])border-bottom-color\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
        return (w, cm.Success ? ParseCssColor(cm.Groups[1].Value.Trim()) : null);
    }

    /// <summary>The top rule a style declares (the `border-top` shorthand or its longhands).</summary>
    private static (double W, Color? C)? UaRuleAbove(string style) =>
        UaRuleBelow(Regex.Replace(style, @"border-bottom(?=[\s:-])", "border-none", RegexOptions.IgnoreCase)
            .Replace("border-top", "border-bottom", StringComparison.OrdinalIgnoreCase));

    /// <summary>A band line was inserted at the cell's head: every per-line map keyed by index moves up by one.</summary>
    private static void UaShiftLineIndexMaps(TableParseState ps, int by)
    {
        static Dictionary<int, T>? Shift<T>(Dictionary<int, T>? map, int by)
        {
            if (map is null) return null;
            var shifted = new Dictionary<int, T>();
            foreach (var kv in map) shifted[kv.Key + by] = kv.Value;
            return shifted;
        }
        ps.lineRunsByIdx = Shift(ps.lineRunsByIdx, by);
        ps.lineMarkerByIdx = Shift(ps.lineMarkerByIdx, by);
        ps.lineAlignByIdx = Shift(ps.lineAlignByIdx, by);
        ps.lineDecorsByIdx = Shift(ps.lineDecorsByIdx, by);
        ps.lineColorRunsByIdx = Shift(ps.lineColorRunsByIdx, by);
        ps.lineHeightPctByIdx = Shift(ps.lineHeightPctByIdx, by);
        ps.uaRuleBands = Shift(ps.uaRuleBands, by);
        if (ps.hrRuleLines is not null) ps.hrRuleLines = new HashSet<int>(System.Linq.Enumerable.Select(ps.hrRuleLines, i => i + by));
        if (ps.underlinedLines is not null) ps.underlinedLines = new HashSet<int>(System.Linq.Enumerable.Select(ps.underlinedLines, i => i + by));
        if (ps.loneBrBlankLines is not null) ps.loneBrBlankLines = new HashSet<int>(System.Linq.Enumerable.Select(ps.loneBrBlankLines, i => i + by));
        if (ps.pendingCellImgAt is not null) for (var i = 0; i < ps.pendingCellImgAt.Count; i++) ps.pendingCellImgAt[i] += by;
        if (ps.pendingCellTables is not null) for (var i = 0; i < ps.pendingCellTables.Count; i++) ps.pendingCellTables[i] = (ps.pendingCellTables[i].T, ps.pendingCellTables[i].AnchorLine + by);
    }

    /// <summary>A line holding nothing but collapsible whitespace (a no-break space is content).</summary>
    private static bool UaBlankLine(System.Text.StringBuilder sb)
    {
        for (var i = 0; i < sb.Length; i++)
            if (sb[i] is not (' ' or (char)9 or (char)13 or (char)10)) return false;
        return true;
    }

    /// <summary>The CSS `medium` border width, 3px.</summary>
    private const double UaMediumBorderPt = 3 * 0.75;

    /// <summary>A UA-boxed list's class rules and inline style set the size and face its items inherit
    /// (`&lt;ul class="bullets">` under `.bullets { font-size: 9pt; font-family: Arial }`), restored at its close.</summary>
    private static void UaOpenListTypography(TableParseState ps, string tag, Token tok)
    {
        var prevPt = ps.curFontPt; var prevFam = ps.curFamily;
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("class", out var cls))
            foreach (var cn in cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                foreach (var sheet in new IReadOnlyDictionary<string, Dictionary<string, string>>?[] { ps.uaSheet, ps.uaDocSheet })
                {
                    if (sheet is null) continue;
                    if (!sheet.TryGetValue(tag + "." + cn, out var rule) && !sheet.TryGetValue("." + cn, out rule)) continue;
                    if (rule.TryGetValue("font-size", out var fs) && TryParseLength(fs.Trim()) is { } fsPt && fsPt > 0) ps.curFontPt = fsPt;
                    if (rule.TryGetValue("font-family", out var ff) && FirstFontFamily(ff) is { Length: > 0 } fam) ps.curFamily = fam;
                }
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var st) && st is not null)
        {
            var fsm = Regex.Match(st, @"font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (fsm.Success && TryParseLength(fsm.Groups[1].Value.Trim()) is { } sfs && sfs > 0) ps.curFontPt = sfs;
            var ffm = Regex.Match(st, @"font-family\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (ffm.Success && FirstFontFamily(ffm.Groups[1].Value) is { Length: > 0 } sfam) ps.curFamily = sfam;
        }
        ps.styleStack.Add((tag, prevPt, prevFam, false, ps.curColor, false, false));
    }

    private static void UaCloseListTypography(TableParseState ps, string tag)
    {
        for (var k = ps.styleStack.Count - 1; k >= 0; k--)
            if (ps.styleStack[k].Tag == tag)
            {
                ps.curFontPt = ps.styleStack[k].PrevPt; ps.curFamily = ps.styleStack[k].PrevFamily;
                ps.styleStack.RemoveAt(k);
                ps.uaUpperDepths.RemoveAll(d => d >= ps.styleStack.Count);
                return;
            }
    }

    /// <summary>The document sheet's cell padding for a UA-boxed cell: the rules addressing td/th bare, under
    /// `table`, or scoped by one of the table's classes (`table.default td { padding-right: 20px }`), in sheet
    /// order; the vertical pair bands the row, the horizontal pair rides the column and the left one indents.</summary>
    private static void ApplyUaSheetCellPadding(CellOpenState co)
    {
        if (co.docCss is null) return;
        var tag = co.tag.ToLowerInvariant();
        var tblClasses = co.ps.uaTableClasses;
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tag, "table " + tag };
        foreach (var c in tblClasses) { wanted.Add("table." + c + " " + tag); wanted.Add("." + c + " " + tag); }
        double? top = null, bottom = null, left = null, right = null;
        var fs = co.ps.curFontPt > 0 ? co.ps.curFontPt : co.cellFontSize;
        foreach (var kv in co.docCss)
        {
            var hit = false;
            foreach (var sel in kv.Key.Split(','))
                if (wanted.Contains(sel.Trim())) { hit = true; break; }
            if (!hit) continue;
            if (CssBoxSidePt(kv.Value, "padding", "padding-top", 0, fs) is { } t) top = t;
            if (CssBoxSidePt(kv.Value, "padding", "padding-right", 1, fs) is { } r) right = r;
            if (CssBoxSidePt(kv.Value, "padding", "padding-bottom", 2, fs) is { } b) bottom = b;
            if (CssBoxSidePt(kv.Value, "padding", "padding-left", 3, fs) is { } l) left = l;
        }
        if (top is { } tp) co.ps.cellChainPadTopPt = Math.Max(co.ps.cellChainPadTopPt, tp);
        if (bottom is { } bp) co.ps.cellChainPadBotPt = Math.Max(co.ps.cellChainPadBotPt, bp);
        if ((left ?? 0) + (right ?? 0) > 0 && co.ps.cellCssPadPt <= 0)
        {
            co.ps.cellCssPadPt = (left ?? 0) + (right ?? 0);
            co.ps.cellPadLeftPt = left ?? 0;
        }
    }

    /// <summary>A CSS line-height as a percent of the running size: a percent or bare number as is, an em
    /// value times a hundred, a length against the size; null when it parses as none.</summary>
    private static double? UaLineHeightPct(string decl, double fs)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (decl.EndsWith('%') && double.TryParse(decl.TrimEnd('%'), System.Globalization.NumberStyles.Float, inv, out var pct)) return pct;
        if (double.TryParse(decl, System.Globalization.NumberStyles.Float, inv, out var bare)) return bare * WholeWidthPercent;
        if (decl.EndsWith("em", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(decl[..^2].Trim(), System.Globalization.NumberStyles.Float, inv, out var em)) return em * WholeWidthPercent;
        return fs > 0 && TryParseLength(decl) is { } pt && pt > 0 ? pt / fs * WholeWidthPercent : null;
    }

    /// <summary>The element chain a UA-boxed grid's open cell stands in: the grid's ancestors, its table element
    /// (its classes) and the cell (its classes) - the chain a nested grid's cells hang from.</summary>
    private static List<CssElem> UaCellChain(List<CssElem>? ancestors, TableParseState ps)
    {
        var chain = ancestors is null ? new List<CssElem>() : new List<CssElem>(ancestors);
        chain.Add(new CssElem { Tag = "table", Classes = ps.uaTableClasses is { Length: > 0 } ? ps.uaTableClasses : null });
        chain.Add(new CssElem { Tag = "td", Classes = ps.uaCellClasses });
        return chain;
    }

    /// <summary>A simple selector segment (`.footer`, `td.footer`, `table.orange-blue`) names an element of this tag
    /// carrying one of these classes.</summary>
    private static bool UaSegNamesClass(string seg, string tag, string[] classes)
    {
        var dot = seg.IndexOf('.');
        if (dot < 0 || seg.IndexOf('#') >= 0 || seg.IndexOf(':') >= 0) return false;
        if (dot > 0 && !seg[..dot].Equals(tag, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var c in seg[(dot + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries))
            if (Array.FindIndex(classes, x => x.Equals(c, StringComparison.OrdinalIgnoreCase)) < 0) return false;
        return true;
    }

    /// <summary>The sheet's descendant rules addressing a UA-boxed cell through its ancestors (`.orange-blue .footer
    /// { background-color }`, `.orange-blue .left-column { border-right: 1px solid }`): the cell's background, its
    /// text colour and its own borders (measured on the mailing: the footer band and the column rule).</summary>
    private static void ApplyUaChainRules(CellOpenState co)
    {
        var chain = UaCellChain(co.cssAncestors, co.ps);
        var decls = MatchChainDecls(co.chainRules, chain);
        // (…and a two-part descendant class rule - `.orange-blue .footer { background-color }` - lives in the
        //  sheets, not the chain rules: an ancestor's class and this cell's class name it)
        if (co.ps.uaCellClasses is { Length: > 0 })
            foreach (var sheet in new IReadOnlyDictionary<string, Dictionary<string, string>>?[] { co.css, co.docCss })
            {
                if (sheet is null) continue;
                foreach (var kv in sheet)
                    foreach (var selRaw in kv.Key.Split(','))
                    {
                        var parts = selRaw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length != 2 || !UaSegNamesClass(parts[1], co.tag, co.ps.uaCellClasses)) continue;
                        var ancestorHit = false;
                        for (var k = 0; k < chain.Count - 1 && !ancestorHit; k++)
                            ancestorHit = chain[k].Classes is { } ac && UaSegNamesClass(parts[0], chain[k].Tag, ac);
                        if (!ancestorHit) continue;
                        decls ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var d in kv.Value) decls[d.Key] = d.Value;
                    }
            }
        if (decls is null) return;
        if (co.ps.cell!.BackgroundColor is null
            && (decls.TryGetValue("background-color", out var bg) || decls.TryGetValue("background", out bg))
            && ParseCssColor(bg) is { } bgc)
            co.ps.cell.BackgroundColor = bgc;
        if (co.ps.cellChainColor is null && decls.TryGetValue("color", out var fg) && ParseCssColor(fg) is { } fgc)
            co.ps.cellChainColor = fgc;
        if (co.ps.cell.Border is null)
        {
            BorderSide sides = 0; double w = 0; Color? color = null;
            foreach (var (prop, side) in new[]
            {
                ("border", BorderSide.Box), ("border-left", BorderSide.Left), ("border-top", BorderSide.Top),
                ("border-bottom", BorderSide.Bottom), ("border-right", BorderSide.Right),
            })
                if (decls.TryGetValue(prop, out var bv) && ChainBorder(bv) is { } bi)
                {
                    sides |= side; if (bi.Width > w) w = bi.Width; color ??= bi.Color;
                }
            if (sides != 0) co.ps.cell.Border = new BorderInfo(sides, w <= 0 ? PxToPt : w, color ?? Color.Black);
        }
    }

    /// <summary>The border-spacing the grid's own style or the sheets' table / universal rule declares, in points
    /// (the first value); null when none declares one.</summary>
    private static double? UaSheetBorderSpacingPt(TableStyleConfig cfg)
    {
        string? decl = null;
        if (cfg.tblStyle.TryGetValue("border-spacing", out var own)) decl = own;
        else
            foreach (var sheet in new IReadOnlyDictionary<string, Dictionary<string, string>>?[] { cfg.css, cfg.docCss })
                if (decl is null && sheet is not null)
                    foreach (var key in new[] { "table", "*" })
                        if (decl is null && sheet.TryGetValue(key, out var rule) && rule.TryGetValue("border-spacing", out var v)) decl = v;
        if (decl is null) return null;
        var first = decl.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return first.Length > 0 ? CssLengthOrZeroPt(first[0], cfg.cellFontSize) : null;
    }

    /// <summary>Every row of the grid is empty of cells (`&lt;table&gt;&lt;tr&gt;&lt;/tr&gt;&lt;/table&gt;`).</summary>
    private static bool UaGridHasNoCells(Table grid)
    {
        foreach (Row row in grid.Rows) if (row.Cells.Count > 0) return false;
        return true;
    }

    /// <summary>The sheet's td/th rule zeroes the cell padding (`td { padding: 0 }`): the UA pad is overridden.</summary>
    private static bool UaSheetZeroesCellPadding(IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        foreach (var cell in new[] { "td", "th" })
            if (css.TryGetValue(cell, out var rule) && rule.TryGetValue("padding", out var pad)
                && IsZeroLength(pad.Trim().Split(' ')[0])) return true;
        return false;
    }

    /// <summary>The grid collapses its borders: its own style, or the sheet's table / universal rule says so.</summary>
    private static bool UaBorderCollapse(TableStyleConfig cfg)
    {
        if (cfg.tblStyle.TryGetValue("border-collapse", out var own)) return own.Trim().Equals("collapse", StringComparison.OrdinalIgnoreCase);
        foreach (var sheet in new IReadOnlyDictionary<string, Dictionary<string, string>>?[] { cfg.css, cfg.docCss })
            if (sheet is not null)
                foreach (var key in new[] { "table", "*" })
                    if (sheet.TryGetValue(key, out var rule) && rule.TryGetValue("border-collapse", out var v))
                        return v.Trim().Equals("collapse", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>The width the document sheet's class rules give a UA-boxed cell (`table td.column-date { width:
    /// 80px }`, `.wide`), in points; 0 when none.</summary>
    private static double UaSheetCellWidthPt(CellOpenState co)
    {
        if (co.docCss is null || co.tok.Attributes is null || !co.tok.Attributes.TryGetValue("class", out var cls) || cls is null) return 0;
        var tag = co.tag.ToLowerInvariant();
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            wanted.Add("." + c); wanted.Add(tag + "." + c); wanted.Add("table " + tag + "." + c);
            foreach (var tc in co.ps.uaTableClasses) { wanted.Add("table." + tc + " " + tag + "." + c); wanted.Add("." + tc + " " + tag + "." + c); }
        }
        double found = 0;
        foreach (var kv in co.docCss)
        {
            var hit = false;
            foreach (var sel in kv.Key.Split(','))
                if (wanted.Contains(sel.Trim())) { hit = true; break; }
            if (hit && kv.Value.TryGetValue("width", out var wv) && !wv.Contains('%') && TryParseLength(wv.Trim()) is { } wPt && wPt > 0) found = wPt;
        }
        return found;
    }

    /// <summary>The value the sheets' cell rules (td/th bare, under table, or scoped by the table's classes, comma
    /// lists split) give a property, the last matching rule winning; null when none.</summary>
    private static string? UaSheetCellRuleValue(CellOpenState co, string prop)
    {
        var tag = co.tag.ToLowerInvariant();
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tag, "table " + tag };
        foreach (var c in co.ps.uaTableClasses) { wanted.Add("table." + c + " " + tag); wanted.Add("." + c + " " + tag); }
        string? found = null;
        foreach (var sheet in new IReadOnlyDictionary<string, Dictionary<string, string>>?[] { co.css, co.docCss })
        {
            if (sheet is null) continue;
            foreach (var kv in sheet)
            {
                var hit = false;
                foreach (var sel in kv.Key.Split(','))
                    if (wanted.Contains(sel.Trim())) { hit = true; break; }
                if (hit && kv.Value.TryGetValue(prop, out var v)) found = v;
            }
        }
        return found;
    }

    /// <summary>The background a row declares: its style, its bgcolor, or a sheet rule on its class (`.HEADER {
    /// background: #ddd }`, `tr.HEADER`).</summary>
    private static Color? UaRowBackground(TableStyleConfig cfg, TableParseState ps, Token tok)
    {
        if (tok.Attributes is null) return null;
        if (tok.Attributes.TryGetValue("style", out var st) && st is not null
            && Regex.Match(st, "background(-color)? *: *([^;]+)", RegexOptions.IgnoreCase) is { Success: true } sm
            && ParseCssColor(sm.Groups[2].Value.Trim()) is { } sc) return sc;
        if (tok.Attributes.TryGetValue("bgcolor", out var bga) && ParseCssColor(bga) is { } ac) return ac;
        if (!tok.Attributes.TryGetValue("class", out var cls) || cls is null) return null;
        foreach (var c in cls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            foreach (var sheet in new IReadOnlyDictionary<string, Dictionary<string, string>>?[] { cfg.css, cfg.docCss })
            {
                if (sheet is null) continue;
                if ((sheet.TryGetValue("tr." + c, out var rule) || sheet.TryGetValue("." + c, out rule))
                    && (rule.TryGetValue("background-color", out var bg) || rule.TryGetValue("background", out bg))
                    && ParseCssColor(bg.Trim()) is { } col) return col;
            }
        return null;
    }

    /// <summary>`text-transform: uppercase` declared on a style string.</summary>
    private static bool UaUppercaseDeclared(string? style) =>
        style is not null && Regex.IsMatch(style, @"text-transform\s*:\s*uppercase", RegexOptions.IgnoreCase);

    /// <summary>A block opening inside a paragraph (a list in a p) closes the paragraph first, the way the
    /// HTML parser does: its lines push, its bottom margin is left, and every run open inside it closes.</summary>
    private static void UaCloseParagraphForBlock(TableStyleConfig cfg, TableParseState ps)
        => UaCloseParagraphForBlock(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);

    private static void UaCloseParagraphForBlock(TableParseState ps, bool redlineCells, bool dwFormCells, bool widenProbe)
    {
        var pIdx = -1;
        for (var k = ps.styleStack.Count - 1; k >= 0; k--) if (ps.styleStack[k].Tag == "p") { pIdx = k; break; }
        if (pIdx < 0) return;
        if (ps.line.Length > 0) PushLine(ps, redlineCells, dwFormCells, widenProbe);
        UaCloseBlock(ps, "p");
        for (var k = ps.styleStack.Count - 1; k >= pIdx; k--)
        {
            var e = ps.styleStack[k];
            ps.curFontPt = e.PrevPt; ps.curFamily = e.PrevFamily; ps.curColor = e.PrevColor;
            if (e.BoldBump && ps.boldDepth > 0) ps.boldDepth--;
            if (e.ItalicBump && ps.italicDepth > 0) ps.italicDepth--;
            ps.styleStack.RemoveAt(k);
        }
        ps.cellDecorActive?.RemoveAll(d => d.Depth > ps.styleStack.Count);
        ps.uaUpperDepths.RemoveAll(d => d >= ps.styleStack.Count);
    }

    /// <summary>Times New Roman's win ascent + descent ratio (1825 + 443 over 2048): the marker box when no face metrics resolve.</summary>
    private const double UaSerifLineRatio = 1.107;

    /// <summary>A weight change mid-line in a UA-boxed cell marks a run boundary, so the line draws each run in its own face.</summary>
    private static void UaMarkBoldRun(TableParseState ps, bool bold)
    {
        if (!ps.uaCellBoxes || ps.cell is null) return;
        ps.lineRunMarks ??= new();
        if (ps.lineRunMarks.Count == 0) ps.lineRunMarks.Add((0, ps.boldDepth > 0 && !bold ? true : ps.lineRunMarks.Count > 0));
        ps.lineRunMarks.Add((ps.line.Length, bold));
    }
}
