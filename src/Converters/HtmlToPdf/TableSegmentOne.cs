using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Flow blocks: one segment of a table-bearing fragment parsed into blocks.</summary>
    private static bool BuildOneTableSegment(FlowBlocksState fbk, List<(bool isTable, string html)> segs, HashSet<int>? breakAfterSegs, int segIdx)
    {
        var (isTable, seg) = segs[segIdx];
        // HTML parsing: text sitting directly inside a table or a row belongs to no cell at
        // all, and a parser FOSTER-PARENTS it out of the table, immediately in front of it
        // (measured: the request form's `Telephone Reference Number: 199130`, authored
        // between a `<tr>` and its first `<td>` half way down the grid, is the reference's
        // FIRST line on the page). Ours dropped it on the floor.
        if (isTable && TakeFosterParentedText(ref seg) is { Length: > 0 } fostered)
            BuildNonTableSegment(fbk, fostered, segs, segIdx);
        if (UnwrapSingleColumnTable(fbk, seg, isTable)) return true;
        // The escaped-attr dialect grids EVERY table — form controls
        // draw INSIDE grid cells rather than flattening the table.
        // A radio-only table grids too: its options ride the cells inline.
        if (isTable && (fbk.cv.profile.escapedAttrDoc || fbk.cv.profile.dwFormDoc
            // A UA form document grids its tables too: the metric grid seats their text
            // inputs and checkboxes as replaced boxes in the cells.
            || fbk.cv.profile.uaFormCells
            || !(fbk.perTableFormGate && HasVisibleFormControl(seg)
                 && !RadioGridableControls(seg, fbk.cv.profile.uaStdSerif)
                 && !ButtonFamilyControlsOnly(seg))))
        {
            // The sheet-typography box flow: the table's own sheet margins (its tag and
            // class rules) are the box space above and below it (probed: `table
            // { margin-top: 5px }` seats a grid 3.75 under the box above).
            var (tableMt, tableMb) = fbk.cv.profile.sheetBoxFlow ? SheetTableMargins(fbk.cv.css, seg) : (0.0, 0.0);
            // A UA-flow table opened by a bare <p> (the quirks idiom `<p> <table>`: the grid sits
            // inside the paragraph) stands the paragraph's block margin above it (measured: the
            // surgery-lights' second grid opens 13.5 under the first, the bare <p> between them).
            // (quirks documents only: a standards parser closes the paragraph before the table,
            // and the empty paragraph's margins collapse away - the statement's grid keeps its seat)
            if (fbk.cv.profile.uaStdSerif && _quirksRowStrut && segIdx > 0 && !segs[segIdx - 1].isTable
                && Regex.IsMatch(segs[segIdx - 1].html, @"<p\b(?![^>]*/\s*>)[^>]*>\s*$", RegexOptions.IgnoreCase))
                tableMt = Math.Max(tableMt, UaParagraphMarginPt);
            // A UA-flow table's OWN inline margin-top is box space above it (measured: the
            // dunning letter's address grid opens 3.6 cm below the page top).
            if ((fbk.cv.profile.uaStdSerif || fbk.cv.profile.ptFormDoc)
                && Regex.Match(seg, @"^\s*<table\b[^>]*>", RegexOptions.IgnoreCase) is { Success: true } tagM
                && Regex.Match(DivStyleOf(tagM.Value), MarginTopDeclRx, RegexOptions.IgnoreCase) is { Success: true } mtM
                && UaTableMarginPt(fbk.cv.css, mtM.Groups[1].Value.Trim()) is { } mtPt && mtPt > 0)
                tableMt = Math.Max(tableMt, mtPt);
            // …and the margin-top the innermost wrapper's id/class rule declares over a grid it
            // opens with (`#divTable { margin-top: 20px }` seats the grid 15 under the image).
            if (fbk.cv.profile.uaStdSerif && HostChainMarginTop(fbk.cv.css, fbk.hostChain) is { } hostMt && hostMt > 0)
                tableMt = Math.Max(tableMt, hostMt);
            fbk.list.Add(new Block
            {
                IsTable = true,
                TableHtml = seg,
                FloatFirst = Regex.IsMatch(seg, @"^<table\b[^>]*\balign\s*=\s*[""']?left",
                    RegexOptions.IgnoreCase),
                PageBreakAfterTable = breakAfterSegs?.Contains(segIdx) ?? false,
                MarginTop = tableMt,
                MarginBottom = tableMb,
                CssAncestors = fbk.openChain,
                HostFace = HostChainTypography(fbk.cv.css, fbk.hostChain).face,
                HostFontPt = HostChainTypography(fbk.cv.css, fbk.hostChain).pt,
                HostWidthPt = HostChainWidth(fbk.cv.css, fbk.hostChain),
            });
        }
        else
        {
            BuildNonTableSegment(fbk, seg, segs, segIdx);
        }
        return true;
    }

    /// <summary>The face and size the nearest wrapper's id or class rule declares over a table (the
    /// inherited typography its cells draw in); null / 0 where no wrapper states one.</summary>
    private static (string? face, double pt) HostChainTypography(IReadOnlyDictionary<string, Dictionary<string, string>> css, List<CssElem>? chain)
    {
        string? face = null; double pt = 0;
        if (chain is null) return (face, pt);
        foreach (var el in chain)
        {
            // (the wrapper's ID rule alone - a class-typed wrapper keeps the calibrated grid its greens were measured on)
            var keys = new List<string>();
            if (el.Id is { Length: > 0 } id) { keys.Add("#" + id); keys.Add(el.Tag + "#" + id); }
            foreach (var key in keys)
            {
                if (!css.TryGetValue(key, out var rule)) continue;
                if (rule.TryGetValue("font-family", out var ff) && FirstFontFamily(ff) is { Length: > 0 } fam
                    && WinMetricsFor(fam) is not null) face = fam;
                if (rule.TryGetValue("font-size", out var fs) && TryParseCssFontSize(fs.Trim()) is { } fsPt && fsPt > 0) pt = fsPt;
            }
        }
        return (face, pt);
    }

    /// <summary>The box the innermost wrapper's id or class rule declares over a table (`#divTable {
    /// width: 2000px }`): the grid lays out in it, however narrow the page (probed: the 20-column grid
    /// shares the 1500 pt box equally and the sheet grows to it); 0 where no wrapper states one.</summary>
    private static double HostChainWidth(IReadOnlyDictionary<string, Dictionary<string, string>> css, List<CssElem>? chain)
    {
        if (chain is null) return 0;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var el = chain[i];
            var keys = new List<string>();
            if (el.Id is { Length: > 0 } id) { keys.Add("#" + id); keys.Add(el.Tag + "#" + id); }
            foreach (var key in keys)
                if (css.TryGetValue(key, out var rule) && rule.TryGetValue("width", out var w)
                    && !w.Contains('%') && TryParseLength(w.Trim()) is { } wPt && wPt > 0)
                    return wPt;
        }
        return 0;
    }

    /// <summary>The margin-top the innermost wrapper's id or class rule declares (pt); null where none.</summary>
    private static double? HostChainMarginTop(IReadOnlyDictionary<string, Dictionary<string, string>> css, List<CssElem>? chain)
    {
        if (chain is null || chain.Count == 0) return null;
        var el = chain[^1];
        var keys = new List<string>();
        if (el.Id is { Length: > 0 } id) { keys.Add("#" + id); keys.Add(el.Tag + "#" + id); }
        foreach (var key in keys)
            if (css.TryGetValue(key, out var rule) && rule.TryGetValue("margin-top", out var m)
                && TryParseLength(m.Trim()) is { } mPt && mPt > 0)
                return mPt;
        return null;
    }

    /// <summary>A `margin-top:` declaration in a style attribute, up to its terminator.</summary>
    private const string MarginTopDeclRx = @"(?<![-\w])margin-top\s*:\s*([^;""']+)";

    /// <summary>A table's own margin in points, its `em` resolved against the size THE TABLE
    /// carries - the sheet's own `table { font-size }` when it states one, and the UA flow's
    /// base otherwise. The shared length parser assumes the flow's 11 pt default, which spends
    /// half a point too little above a 12 pt grid and moves everything under it.</summary>
    private static double? UaTableMarginPt(IReadOnlyDictionary<string, Dictionary<string, string>> css, string decl)
    {
        var em = Regex.Match(decl, @"^(-?(?:\d+(?:\.\d+)?|\.\d+))\s*r?em$", RegexOptions.IgnoreCase);
        if (!em.Success) return TryParseLength(decl);
        if (!double.TryParse(em.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return null;
        var basePt = css.TryGetValue("table", out var tRule)
            && tRule.TryGetValue("font-size", out var fsV)
            && TryParseCssFontSize(fsV.Trim()) is { } fsPt && fsPt > 0
            ? fsPt : DefaultBodyFontPt;
        return n * basePt;
    }

    /// <summary>The margins the sheet's `table` rule and the table's class rules declare, in points.</summary>
    private static (double top, double bottom) SheetTableMargins(Dictionary<string, Dictionary<string, string>> css, string tableHtml)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Overlay(string key)
        {
            if (css.TryGetValue(key, out var rule))
                foreach (var kv in rule) merged[kv.Key] = kv.Value;
        }
        Overlay("table");
        var clsM = Regex.Match(tableHtml, @"^<table\b[^>]*\bclass\s*=\s*[""']([^""']+)", RegexOptions.IgnoreCase);
        if (clsM.Success)
            foreach (var c in clsM.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                Overlay("." + c);
                Overlay("table." + c);
            }
        if (!merged.ContainsKey("margin") && !merged.ContainsKey("margin-top") && !merged.ContainsKey("margin-bottom"))
            return (0, 0);
        var sb = new StringBuilder();
        foreach (var kv in merged) sb.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
        var box = ParseInlineMarginBox(sb.ToString(), DefaultBodyFontPt);
        return (Math.Max(0, box.top), Math.Max(0, box.bottom));
    }

    /// <summary>Text authored directly inside a ROW, in no cell: removed from
    /// <paramref name="tableHtml"/> and returned as the markup a parser foster-parents in front
    /// of the table. Whitespace and comments return empty and leave the table untouched.</summary>
    private static string TakeFosterParentedText(ref string tableHtml)
    {
        var inRow = false;
        var inCell = false;
        var last = 0;
        // A tag inside a COMMENT is not markup: scan a same-length copy with the comment
        // regions blanked and slice the original by index (a commented-out `<td>` otherwise
        // reads as a real cell open and the stray `-->` after it as authored text).
        var scan = HtmlCommentRx.Replace(tableHtml, cm => new string(' ', cm.Length));
        var rowStyle = "";
        // Every stray stretch is fostered, in source order, onto ONE line in front of the
        // table (measured: two rows each opening with text put both on the line above the
        // grid); the cuts are applied after the scan so the indices stay valid.
        var fostered = new StringBuilder();
        var cuts = new List<(int start, int end)>();
        foreach (Match m in FosterScanRx.Matches(scan))
        {
            var tag = m.Groups["tag"].Value.ToLowerInvariant();
            var close = m.Groups["close"].Length > 0;
            if (inRow && !inCell && m.Index > last
                && FosterVisibleText(scan[last..m.Index]).Trim().Length > 0)
            {
                var between = tableHtml[last..m.Index];
                cuts.Add((last, m.Index));
                // The text draws in the typography it inherits where the parser puts it,
                // which the row it was authored in states (measured: the request form's
                // line is Arial 10, its row's own declaration, not the flow's UA serif).
                // the cell markup that separated two stretches is a word boundary
                if (fostered.Length > 0) fostered.Append(' ');
                fostered.Append(rowStyle.Length > 0
                    ? "<span style=\"" + rowStyle + "\">" + between + "</span>"
                    : between);
            }
            switch (tag)
            {
                case "table": inRow = false; inCell = false; break;
                case "tr":
                    inRow = !close;
                    inCell = false;
                    rowStyle = close ? "" : DivStyleOf(tableHtml[m.Index..(m.Index + m.Length)]);
                    break;
                case "td":
                case "th": inCell = !close; break;
                default: inCell = false; break;
            }
            last = m.Index + m.Length;
        }
        for (var i = cuts.Count - 1; i >= 0; i--)
            tableHtml = tableHtml[..cuts[i].start] + tableHtml[cuts[i].end..];
        return fostered.ToString();
    }

    /// <summary>An HTML comment, whose content is never markup.</summary>
    private static readonly Regex HtmlCommentRx = new Regex(@"<!--[\s\S]*?-->", RegexOptions.Compiled);

    /// <summary>The tags the foster scan walks: table structure only.</summary>
    private static readonly Regex FosterScanRx = new Regex(
        @"<(?<close>/?)(?<tag>table|thead|tbody|tfoot|tr|td|th)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The text a stretch of markup would DRAW: tags removed, entities resolved.</summary>
    private static string FosterVisibleText(string html)
        => DecodeEntities(Regex.Replace(html, "<[^>]*>", " ")).Replace(' ', ' ');
}
