using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The table builder's preamble, verbatim: parses the tag, stylesheet
    /// and chain rules, settles the dialect config, builds the empty Table and the
    /// token stream, and hands back the five context objects the parse loop works on.</summary>
    private static (TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table, List<Token> tokens, string? cssRunFace)
        BuildTableParseContext(string html, double availWidthPt, HtmlLoadOptions? options, List<byte[]>? inlineSvgs, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, bool bandDialect, bool widenProbe, double cellLineHeightPt, double defaultCellFontPt, bool tightExtras, bool liftNestedTables, bool uaCellBoxes, string? cssRunFace, Color? bodyTextColor, bool uaSerifMin, bool authoredCellChrome, bool formGridDialect, bool ptCellWidths, bool redlineCells, bool dwFormCells, double formGridStrutPt, double formGridStrutDropPt, string? defaultCellFace, bool docElementGrid, bool fullWidthCjkMin, bool pinnedBodyGrid, bool overDeclaredDraw, List<CssChainRule>? chainRules, List<CssElem>? cssAncestors, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio, bool wordMailCells = false, Func<bool, Aspose.Pdf.Forms.CheckboxField>? makeCheckbox = null, bool nestedGrid = false, double uaLineFactor = 0, bool uaSheetGrid = false)
    {
        var cfg = new TableStyleConfig();
        cfg.options = options;
        cfg.availWidthPt = availWidthPt;
        cfg.inlineSvgs = inlineSvgs;
        cfg.docCss = docCss;
        cfg.bandDialect = bandDialect;
        cfg.widenProbe = widenProbe;
        cfg.cellLineHeightPt = cellLineHeightPt;
        cfg.uaBodyFace = uaCellBoxes ? cssRunFace : null;
        cfg.uaLineFactor = uaLineFactor;
        cfg.uaSheetGrid = uaSheetGrid;
        cfg.defaultCellFontPt = defaultCellFontPt;
        cfg.tightExtras = tightExtras;
        cfg.liftNestedTables = liftNestedTables;
        cfg.uaCellBoxes = uaCellBoxes;
        cfg.nestedGrid = nestedGrid;
        cfg.uaSerifMin = uaSerifMin;
        cfg.authoredCellChrome = authoredCellChrome;
        cfg.chainRules = chainRules;
        cfg.cssAncestors = cssAncestors;
        cfg.defaultCellFace = defaultCellFace;
        cfg.docElementGrid = docElementGrid;
        cfg.dwFormCells = dwFormCells;
        cfg.wordMailCells = wordMailCells;
        cfg.formGridDialect = formGridDialect;
        cfg.formGridStrutDropPt = formGridStrutDropPt;
        cfg.formGridStrutPt = formGridStrutPt;
        cfg.fullWidthCjkMin = fullWidthCjkMin;
        cfg.makeRadio = makeRadio;
        cfg.makeCheckbox = makeCheckbox;
        cfg.overDeclaredDraw = overDeclaredDraw;
        cfg.pinnedBodyGrid = pinnedBodyGrid;
        cfg.ptCellWidths = ptCellWidths;
        cfg.redlineCells = redlineCells;
        cfg.css = ParseStyleSheet(html);

        cfg.docAnchorColor = null;
        // …and its `a { font-weight: bold }` rule weights every anchor's text.
        cfg.docAnchorBold = cfg.docCss is not null && cfg.docCss.TryGetValue("a", out var docABold)
            && docABold.TryGetValue("font-weight", out var docAW)
            && Regex.IsMatch(docAW, "^\\s*(bold|[7-9]00)\\s*$", RegexOptions.IgnoreCase);
        if (cfg.docCss is not null && cfg.docCss.TryGetValue("a", out var docARule)
            && docARule.TryGetValue("color", out var docACol))
            cfg.docAnchorColor = ParseCssColor(docACol);

        cfg.tblTag = Regex.Match(html, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        cfg.tblStyle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        cfg.tblBorderAttr = null; cfg.tblCellPadAttr = null; cfg.tblBorderColorAttr = null;
        var colModel = new TableColumnModel();
        ReadTableTagAndChainRules(cfg, colModel, uaSheetGrid);

        ResolveCellFontSize(cfg, uaCellBoxes);

        var ps = SeedTableParseState(cfg, colModel, uaCellBoxes, uaLineFactor, uaSheetGrid, wordMailCells);

        cssRunFace = ResolveTableFaceAndCssBase(cfg, ps, cssRunFace, uaCellBoxes, html);

        ResolveBorderAndPaddingDefaults(cfg, ps, cssRunFace, html);
        ResolveTableFrameFromSheet(cfg, colModel, html);

        ExtractNestedGridsAndTableWidth(cfg, ps, colModel, html);

        ResolveTableColoursAndBorders(cfg, ps, bodyTextColor);

        // A document sheet that zeroes every cell's padding (`td { padding: 0 }`) leaves no UA pad either
        // (measured on the mailing's 600 px content grid: 630 px of padded content, no pad pair, no slack).
        if (uaCellBoxes && ps.sheetTdPadZero) { cfg.pad = 0; cfg.padSide = 0; cfg.padBottom = 0; }
        if (cfg.padSide < 0) cfg.padSide = cfg.pad;
        if (cfg.padBottom < 0) cfg.padBottom = cfg.pad;
        var table = NewContextTable(cfg, ps, colModel, cssRunFace);
        ApplyTableChromeFlags(cfg, ps, colModel, table);
        table.UaCellBoxes = cfg.uaCellBoxes;
        ApplyChainSpacingAndClassChrome(cfg, colModel, table);
        var tokens = Tokenize(StripNonContent(cfg.scanHtml));

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
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_TABLEW") == "1")
            Console.Error.WriteLine($"[tablecfg] cellFs={cfg.cellFontSize:0.##} basePt={cfg.cssBasePt:0.##} face={cfg.defaultCellFace ?? "-"} uaDocGrid={cfg.uaDocGrid} quirks={_quirksRowStrut} shorthand={cfg.cellFontShorthand} spacing={cfg.chainSpacingPt:0.##} pad={cfg.pad:0.##} padSide={cfg.padSide:0.##} lineH={cfg.cellLineHeightPt:0.##} scoped={colModel.ancestorScopedGrid} ratio={cfg.inlineFaceRatio:0.###}");
        ps.uaBaseFontPt = cfg.cellFontSize;
        return (cfg, ps, colModel, table, tokens, cssRunFace);
    }

    /// <summary>A property from the table tag's own class rules in the sheet (`.cls` and
    /// `table.cls`, the later class winning), or null when no class rule declares it.</summary>
    private static string? TableClassRuleValue(TableStyleConfig cfg, string prop)
    {
        if (cfg.tblTag is not { Success: true }) return null;
        var cm = Regex.Match(cfg.tblTag.Value, @"\bclass\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        if (!cm.Success) return null;
        // The document's sheet first, the table segment's own style block over it.
        string? found = null;
        foreach (var sheet in new[] { cfg.docCss, cfg.css })
        {
            if (sheet is null) continue;
            foreach (var c in cm.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (sheet.TryGetValue("." + c, out var clsRule) && clsRule.TryGetValue(prop, out var v1)) found = v1;
                if (sheet.TryGetValue("table." + c, out var tagClsRule) && tagClsRule.TryGetValue(prop, out var v2)) found = v2;
            }
        }
        return found;
    }

    /// <summary>The value a class-scoped CELL rule (`.cls td` / `.cls th`) gives a property on this
    /// table, the table segment's own sheet over the document's; null without one.</summary>
    private static string? TableClassCellRuleValue(TableStyleConfig cfg, string prop)
    {
        if (cfg.tblTag is not { Success: true }) return null;
        var cm = Regex.Match(cfg.tblTag.Value, @"\bclass\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        if (!cm.Success) return null;
        string? found = null;
        foreach (var sheet in new[] { cfg.docCss, cfg.css })
        {
            if (sheet is null) continue;
            foreach (var c in cm.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                foreach (var cellSel in new[] { "." + c + " td", "." + c + " th" })
                    if (sheet.TryGetValue(cellSel, out var cellRule) && cellRule.TryGetValue(prop, out var v)) found = v;
        }
        return found;
    }

    /// <summary>A sheet that zeroes every element's margin and boxes its cells by rule.</summary>
    private static bool IsResetSheetGrid(IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        if (css is null || !css.TryGetValue("*", out var star)
            || !star.TryGetValue("margin", out var m) || !m.Trim().StartsWith("0")) return false;
        foreach (var key in new[] { "table td", "td" })
            if (css.TryGetValue(key, out var cell) && cell.TryGetValue("border", out var b)
                && !b.Trim().StartsWith("0") && b.IndexOf("none", StringComparison.OrdinalIgnoreCase) < 0) return true;
        return false;
    }

    /// <summary>A border attribute of this many px or more frames the table that wide (the cells keep 1 px boxes).</summary>
    private const double ThickFrameAttrPx = 2;
    /// <summary>A dashed frame runs dashes of two widths with gaps of one width; a dotted one, one and one
    /// (measured on the reference's 5 px dashed frame: 7.7 pt dashes, 3.8 pt gaps).</summary>
    private const double DashedDashWidths = 2, DashedGapWidths = 1, DottedDashWidths = 1, DottedGapWidths = 1;

    /// <summary>The first `&lt;td style="…border: …">` of the grid's OWN cells - one inside a nested grid is skipped.</summary>
    private static Match FirstOwnCellStyleBorder(string html)
    {
        var depth = 0;
        foreach (Match m in Regex.Matches(html, @"<(/?)(table|t[dh])\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var close = m.Groups[1].Value.Length > 0;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            if (tag == "table") { depth += close ? -1 : 1; continue; }
            if (close || depth > 1) continue;
            var sm = Regex.Match(m.Value, @"style\s*=\s*[""'][^""']*border\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (sm.Success) return sm;
        }
        return Match.Empty;
    }

    /// <summary>The table tag's own frame: a thick border attribute frames the table that wide while its cells keep
    /// their 1 px boxes; the tag's `border-style` or a per-side `border-&lt;side>` shorthand of dashed or dotted breaks
    /// the frame's sides into dashes.</summary>
    private static void ApplyTableFrameStyle(Table table, TableStyleConfig cfg, string? borderAttr)
    {
        var attrPx = borderAttr is not null && double.TryParse(Regex.Match(borderAttr, @"\d+(?:\.\d+)?").Value,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var apx) ? apx : 0;
        if (attrPx >= ThickFrameAttrPx && table.Border is null)
        {
            table.Border = new BorderInfo(BorderSide.Box, attrPx * PxToPt, cfg.borderColor);
            if (table.DefaultCellBorder is { } cb) table.DefaultCellBorder = new BorderInfo(BorderSide.Box, PxToPt, cb.Color);
        }
        var sides = new (string Prop, BorderSide Side)[]
        {
            ("border-top", BorderSide.Top), ("border-right", BorderSide.Right),
            ("border-bottom", BorderSide.Bottom), ("border-left", BorderSide.Left),
        };
        // Per-side shorthands on the tag (`border-top: DASHED`): the named sides frame the table.
        BorderSide shSides = 0; double shW = 0; Color? shCol = null; var shDash = new Dictionary<BorderSide, string>();
        foreach (var (prop, side) in sides)
        {
            if (!cfg.tblStyle.TryGetValue(prop, out var sv) || BorderStyleKeywordOf(sv) is not { } kw || kw is "none" or "hidden") continue;
            if (TryParseBorderShorthand(prop + ":" + sv, prop) is not (var w, var c)) continue;
            shSides |= side; if (w > shW) shW = w; shCol ??= c; shDash[side] = kw;
        }
        // (…and the longhands - `border-top-width: 2px; border-top-style: dashed` - on a UA-boxed grid: the
        //  mailing's remittance form rules its top and bottom)
        if (cfg.uaCellBoxes)
            foreach (var (prop, side) in sides)
            {
                if ((shSides & side) != 0 || !cfg.tblStyle.TryGetValue(prop + "-style", out var lsv)
                    || BorderStyleKeywordOf(lsv) is not { } lkw || lkw is "none" or "hidden") continue;
                var lw = cfg.tblStyle.TryGetValue(prop + "-width", out var lwv) && TryParseLength(lwv.Trim()) is { } lwPt && lwPt > 0 ? lwPt : UaMediumBorderPt;
                var lc = cfg.tblStyle.TryGetValue(prop + "-color", out var lcv) ? ParseCssColor(lcv.Trim()) : null;
                shSides |= side; if (lw > shW) shW = lw; shCol ??= lc; shDash[side] = lkw;
            }
        if (shSides != 0 && table.Border is null)
            table.Border = new BorderInfo(shSides, shW, shCol ?? Color.Black);
        if (table.Border is not { } frame) return;
        var tagStyle = cfg.tblStyle.TryGetValue("border-style", out var tbs) ? BorderStyleKeywordOf(tbs) : null;
        foreach (var (_, side) in sides)
        {
            if ((frame.Side & side) == 0) continue;
            var kw = shDash.TryGetValue(side, out var sk) ? sk : tagStyle;
            if (kw is not ("dashed" or "dotted")) continue;
            var dashW = kw == "dashed" ? DashedDashWidths : DottedDashWidths;
            var gapW = kw == "dashed" ? DashedGapWidths : DottedGapWidths;
            var dash = new[] { Math.Max(1, (int)Math.Round(frame.Width * dashW)), Math.Max(1, (int)Math.Round(frame.Width * gapW)) };
            var gi = side switch { BorderSide.Top => frame.Top, BorderSide.Right => frame.Right, BorderSide.Bottom => frame.Bottom, _ => frame.Left };
            gi.DashArray = dash;
        }
    }

    /// <summary>The first border-style keyword in a declaration value, lowercased; null when it names none.</summary>
    private static string? BorderStyleKeywordOf(string value)
    {
        foreach (var tok in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            foreach (var k in BorderStyleKeywords)
                if (string.Equals(k, tok, StringComparison.OrdinalIgnoreCase)) return k;
        return value.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0 ? "none" : null;
    }

    /// <summary>The font-size KEYWORD the table's own style attribute states (`small` = 13 px, `smaller`,
    /// `x-small` …) in points; null when the tag states no keyword - a length there resolves through the
    /// merged style, and a class rule's keyword stays out of the calibrated resolution (probed: a
    /// `font-size: small` table under a `table {font-size: 10px}` sheet sets Arial 9.75).</summary>
    private static double? TableInlineFontSizeKeywordPt(Match tblTag)
    {
        if (!tblTag.Success) return null;
        var sm = Regex.Match(tblTag.Value, @"style\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        if (!sm.Success) return null;
        var fm = Regex.Match(sm.Groups[1].Value, @"(?<![-\w])font-size\s*:\s*([a-z-]+)\s*(?:;|$)", RegexOptions.IgnoreCase);
        return fm.Success && TryParseCssFontSize(fm.Groups[1].Value.Trim()) is { } pt && pt > 0 ? pt : null;
    }

    /// <summary>The declaration block a rule on one of the table tag's own classes gives
    /// (".cls" + suffix), the table segment's own sheet before the document's; null when
    /// no class rule matches.</summary>
    private static Dictionary<string, string>? TableOwnClassRule(TableStyleConfig cfg, List<string> tblClasses, string suffix)
    {
        foreach (var c in tblClasses)
        {
            var key = "." + c + suffix;
            if (cfg.css.TryGetValue(key, out var d)) return d;
            if (cfg.docCss is not null && cfg.docCss.TryGetValue(key, out var d2)) return d2;
        }
        return null;
    }

    /// <summary>"td.cls" / "th.cls": the class sits on the CELL, not the table. Editor markup
    /// repeats the table's class on every cell, so the table's own class list finds it.</summary>
    private static Dictionary<string, string>? TableOwnCellClassRule(TableStyleConfig cfg, List<string> tblClasses, string cellTag)
    {
        foreach (var c in tblClasses)
        {
            var key = cellTag + "." + c;
            if (cfg.css.TryGetValue(key, out var d)) return d;
            if (cfg.docCss is not null && cfg.docCss.TryGetValue(key, out var d2)) return d2;
        }
        return null;
    }
}
