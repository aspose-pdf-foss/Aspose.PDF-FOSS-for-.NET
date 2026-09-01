using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The left offset (pt) the fragment's own stylesheet puts on its outermost
    /// block container — a rule selecting that element by id, class or tag name and
    /// declaring <c>margin-left</c> / <c>padding-left</c>. Linked stylesheets are folded
    /// in first, so an external <c>#header { margin-left: 200px }</c> indents the rendered
    /// run exactly as an inline style would. Compound selectors (<c>#header h1</c>) are
    /// ignored — only the container's own box offset moves the run. 0 when nothing matches.</summary>
    internal static double CssBlockLeftIndentPt(string? html, HtmlLoadOptions? options)
    {
        if (string.IsNullOrEmpty(html)) return 0;
        if (html!.IndexOf("<style", StringComparison.OrdinalIgnoreCase) < 0
            && html.IndexOf("<link", StringComparison.OrdinalIgnoreCase) < 0) return 0;

        var withCss = InlineLinkedStylesheets(html, options);
        var css = new StringBuilder();
        foreach (Match sm in Regex.Matches(withCss, @"<style\b[^>]*>([\s\S]*?)</style\s*>",
                     RegexOptions.IgnoreCase))
            css.Append(sm.Groups[1].Value).Append('\n');
        if (css.Length == 0) return 0;

        // selector token -> left offset in pt, for single-token selectors only.
        var rules = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (Match rm in Regex.Matches(css.ToString().Replace("\r", ""),
                     @"([^{}]+)\{([^{}]*)\}"))
        {
            var decls = rm.Groups[2].Value;
            var lm = Regex.Match(decls,
                @"(?<![-\w])(?:margin|padding)-left\s*:\s*(-?\d+(?:\.\d+)?)\s*(px|pt)?",
                RegexOptions.IgnoreCase);
            if (!lm.Success) continue;
            var val = double.Parse(lm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (val <= 0) continue;
            if (!lm.Groups[2].Success || lm.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase))
                val *= 0.75;
            foreach (var selRaw in rm.Groups[1].Value.Split(','))
            {
                var sel = selRaw.Trim();
                // Single-token selectors only: a descendant/compound selector styles a
                // nested element, whose offset is not the container's own.
                if (sel.Length == 0 || sel.IndexOfAny(new[] { ' ', '>', '+', '~', ':', '[' }) >= 0) continue;
                rules[sel] = val;
            }
        }
        if (rules.Count == 0) return 0;

        // The outermost block container in document order wins.
        foreach (Match em in Regex.Matches(withCss,
                     @"<(div|section|article|main|aside|header|footer|nav|p|h[1-6]|ul|ol|table)\b([^>]*)>",
                     RegexOptions.IgnoreCase))
        {
            var tag = em.Groups[1].Value.ToLowerInvariant();
            var attrs = em.Groups[2].Value;
            var idm = Regex.Match(attrs, @"\bid\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
            var id = idm.Success ? (idm.Groups[1].Success ? idm.Groups[1].Value
                : idm.Groups[2].Success ? idm.Groups[2].Value : idm.Groups[3].Value) : null;
            if (id is { Length: > 0 } && rules.TryGetValue("#" + id, out var byId)) return byId;
            var clm = Regex.Match(attrs, @"\bclass\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
            if (clm.Success)
            {
                var classes = (clm.Groups[1].Success ? clm.Groups[1].Value : clm.Groups[2].Value)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                foreach (var c in classes)
                    if (rules.TryGetValue("." + c, out var byClass)) return byClass;
            }
            if (rules.TryGetValue(tag, out var byTag)) return byTag;
        }
        return 0;
    }

    /// <summary>True when the markup contains an HTML &lt;table&gt; element.</summary>
    internal static bool ContainsTable(string? html) =>
        !string.IsNullOrEmpty(html) && Regex.IsMatch(html!, @"<\s*table\b", RegexOptions.IgnoreCase);

    /// <summary>Split mixed HTML into an ordered sequence of top-level segments: each
    /// <c>&lt;table&gt;…&lt;/table&gt;</c> block (isTable = true) and the markup between them
    /// (isTable = false). Lets the in-page HtmlFragment renderer flow text blocks and real
    /// column tables in document order. Nested tables are not supported.</summary>
    /// <summary>Block elements whose resolved CSS declares a visible border — FRAMED
    /// blocks. The box wraps everything the element contains, tables included, so the
    /// span is reported in the source's own coordinates: the offset of the open tag and
    /// the offset just past its matching close, with the border's width and colour and
    /// the element's top padding. Callers draw the sides as the content flows past, over
    /// however many pages it takes.</summary>
    internal static List<(int Start, int End, double BorderWidthPt, Color BorderColor, double PadTopPt)>
        FramedBlockSpans(string html)
    {
        var spans = new List<(int, int, double, Color, double)>();
        if (string.IsNullOrEmpty(html)) return spans;
        var css = ParseStyleSheet(html);
        if (css.Count == 0) return spans;
        // Blank out style/script bodies and comments so a selector's own text (".x {
        // border … }") cannot be read back as markup — the same trick SegmentHtmlTables
        // uses, keeping indices into the ORIGINAL string valid.
        var scan = Regex.Replace(html,
            @"<(script|style)\b[^>]*>(?:(?!</\1)[\s\S])*?</\1\s*>|<!--[\s\S]*?-->",
            mm => new string(' ', mm.Length), RegexOptions.IgnoreCase);
        foreach (Match open in Regex.Matches(scan,
                     @"<(?<tag>div|section|article|blockquote)\b(?<attrs>[^>]*)>",
                     RegexOptions.IgnoreCase))
        {
            if (open.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
            var tag = open.Groups["tag"].Value.ToLowerInvariant();
            var attrs = open.Groups["attrs"].Value;
            var decls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Take(string selector)
            {
                if (css.TryGetValue(selector, out var d))
                    foreach (var kv in d) decls[kv.Key] = kv.Value;
            }
            Take(tag);
            var clsM = Regex.Match(attrs, @"\bclass\s*=\s*(['""])(?<v>[^'""]*)\1",
                RegexOptions.IgnoreCase);
            if (clsM.Success)
                foreach (var c in clsM.Groups["v"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                { Take("." + c); Take(tag + "." + c); }
            var styleM = Regex.Match(attrs, @"\bstyle\s*=\s*(['""])(?<v>[^'""]*)\1",
                RegexOptions.IgnoreCase);
            if (styleM.Success)
                foreach (Match d in StyleDeclRx.Matches(styleM.Groups["v"].Value))
                    decls[d.Groups[1].Value.Trim().ToLowerInvariant()] = d.Groups[2].Value.Trim();
            if (decls.Count == 0) continue;

            var bs = new BlockStyle();
            foreach (var prop in new[] { "border", "border-width", "border-color",
                         "border-top", "border-bottom", "border-left", "border-right" })
                if (decls.TryGetValue(prop, out var bv)) ApplyDeclaration(prop, bv, bs);
            if (bs.BorderColor is null || bs.BorderWidth <= 0) continue;

            var padTop = 0.0;
            if (decls.TryGetValue("padding-top", out var pv)
                || decls.TryGetValue("padding", out pv))
            {
                var pm = Regex.Match(pv, @"([\d.]+)\s*(px|pt)?", RegexOptions.IgnoreCase);
                if (pm.Success && double.TryParse(pm.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var pn))
                    padTop = pm.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase)
                        ? pn : pn * 0.75;
            }

            // The matching close, by same-name depth — a framed div holding divs of its
            // own closes at ITS close tag, not the first one after it.
            var anyRx = new Regex($@"<(?<close>/?){tag}\b[^>]*>", RegexOptions.IgnoreCase);
            var depth = 1;
            var end = -1;
            for (var m2 = anyRx.Match(scan, open.Index + open.Length); m2.Success;
                 m2 = anyRx.Match(scan, m2.Index + m2.Length))
            {
                if (m2.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
                depth += m2.Groups["close"].Length > 0 ? -1 : 1;
                if (depth == 0) { end = m2.Index + m2.Length; break; }
            }
            if (end < 0) continue;   // unbalanced markup — no frame rather than a wrong one
            spans.Add((open.Index, end, bs.BorderWidth, bs.BorderColor, padTop));
        }
        return spans;
    }

    internal static List<(bool isTable, string html)> SegmentHtmlTables(string html)
    {
        var result = new List<(bool, string)>();
        if (string.IsNullOrEmpty(html)) return result;
        var idx = 0;
        // Table tags inside script/style bodies or comments are STRINGS, not
        // markup (jQuery's source mentions "<table>") — scan a same-length copy
        // with those regions blanked and slice the ORIGINAL by index.
        var scanHtml = Regex.Replace(html,
            @"<(script|style)\b[^>]*>(?:(?!</\1)[\s\S])*?</\1\s*>|<!--[\s\S]*?-->",
            mm => new string(' ', mm.Length), RegexOptions.IgnoreCase);
        // A <table> inside a subtree whose inline style hides it (display:none /
        // visibility:hidden container) must NOT become a table segment: segmenting
        // it out loses the hidden ancestor — the table rendered on its own and the
        // container's tail text leaked into the following text segment (the chart
        // tooltip idiom: a hidden fixed-position div holding a legend table).
        // Left in the text run, the block parser's hidden-subtree suppression
        // drops the container with everything inside it.
        // Only HTML container tags that can wrap a table are scanned — an SVG chart
        // ships hundreds of hidden <g>/<text> nodes and none can hold a <table>.
        var hiddenRanges = new List<(int start, int end)>();
        var hidOpenRx = new Regex(
            @"<(div|span|section|article|form|fieldset|center|p|li|ul|ol)\b[^>]*\bstyle\s*=\s*(['""])[^'""]*?(?:display\s*:\s*none|visibility\s*:\s*hidden)[^'""]*?\2[^>]*>",
            RegexOptions.IgnoreCase);
        for (var hm = hidOpenRx.Match(scanHtml); hm.Success;
             hm = hidOpenRx.Match(scanHtml, hm.Index + hm.Length))
        {
            if (hm.Value.EndsWith("/>", StringComparison.Ordinal)) continue;
            var hTag = hm.Groups[1].Value;
            var hRx = new Regex(@"<(?<c>/?)" + Regex.Escape(hTag) + @"\b[^>]*>", RegexOptions.IgnoreCase);
            var hDepth = 1;
            for (var hs = hRx.Match(scanHtml, hm.Index + hm.Length); hs.Success;
                 hs = hRx.Match(scanHtml, hs.Index + hs.Length))
            {
                hDepth += hs.Groups["c"].Length > 0 ? -1 : 1;
                if (hDepth == 0) { hiddenRanges.Add((hm.Index, hs.Index + hs.Length)); break; }
            }
        }
        bool InHiddenRange(int pos)
        {
            foreach (var r in hiddenRanges)
                if (pos >= r.start && pos < r.end) return true;
            return false;
        }
        var openRx = new Regex(@"<table\b[^>]*>", RegexOptions.IgnoreCase);
        var anyRx = new Regex(@"<(?<close>/?)table\b[^>]*>", RegexOptions.IgnoreCase);
        var m = openRx.Match(scanHtml);
        while (m.Success)
        {
            if (InHiddenRange(m.Index))
            {
                m = openRx.Match(scanHtml, m.Index + m.Length);
                continue;
            }
            // A table segment runs to the close MATCHING its open — a table nested
            // inside a cell stays part of the outer segment (the lazy first-close
            // match used to truncate the outer table at the inner </table>).
            var depth = 1;
            var end = -1;
            for (var scan = anyRx.Match(scanHtml, m.Index + m.Length); scan.Success;
                 scan = anyRx.Match(scanHtml, scan.Index + scan.Length))
            {
                depth += scan.Groups["close"].Length > 0 ? -1 : 1;
                if (depth == 0) { end = scan.Index + scan.Length; break; }
            }
            if (end < 0) break;   // unbalanced markup: the rest flows as text
            if (m.Index > idx) result.Add((false, html.Substring(idx, m.Index - idx)));
            result.Add((true, html.Substring(m.Index, end - m.Index)));
            idx = end;
            m = openRx.Match(scanHtml, idx);
        }
        if (idx < html.Length) result.Add((false, html.Substring(idx)));
        return result;
    }

    /// <summary>A &lt;table&gt; with no row of its OWN is not a grid: the HTML parser
    /// foster-parents its illegal children (divs, stray nested tables) out of the
    /// table, and a rowless table generates no boxes at all. Dropping the wrapper's
    /// open/close tags replays that — the children lay out as normal flow, and any
    /// nested tables with real rows become tables in their own right.</summary>
    internal static string UnwrapRowlessTables(string html)
    {
        if (html.IndexOf("<table", StringComparison.OrdinalIgnoreCase) < 0) return html;
        // Tags inside script/style bodies or comments are STRINGS, not markup —
        // scan a same-length blanked copy and slice the original by index.
        var scanHtml = Regex.Replace(html,
            @"<(script|style)\b[^>]*>(?:(?!</\1)[\s\S])*?</\1\s*>|<!--[\s\S]*?-->",
            mm => new string(' ', mm.Length), RegexOptions.IgnoreCase);
        // One pass over table/tr/td tokens: a tr or td marks the INNERMOST open
        // table as having rows (a cell-less tr still implies a row box).
        var opens = new List<(int start, int len)>();
        var closeOf = new Dictionary<int, (int start, int len)>();   // open idx -> close tag span
        var hasRows = new List<bool>();
        var stack = new Stack<int>();
        foreach (Match t in Regex.Matches(scanHtml, @"<(?<c>/?)(?<tag>table|tr|td|th)\b[^>]*>",
                     RegexOptions.IgnoreCase))
        {
            var isClose = t.Groups["c"].Length > 0;
            var tag = t.Groups["tag"].Value.ToLowerInvariant();
            if (tag == "table")
            {
                if (!isClose)
                {
                    stack.Push(opens.Count);
                    opens.Add((t.Index, t.Length));
                    hasRows.Add(false);
                }
                else if (stack.Count > 0)
                    closeOf[stack.Pop()] = (t.Index, t.Length);
                // an unmatched close is stray markup — ignored, like the browser does
            }
            else if (!isClose && stack.Count > 0)
                hasRows[stack.Peek()] = true;
        }
        var cuts = new List<(int start, int len)>();
        for (var i = 0; i < opens.Count; i++)
        {
            if (hasRows[i]) continue;
            cuts.Add(opens[i]);
            if (closeOf.TryGetValue(i, out var cl)) cuts.Add(cl);
        }
        if (cuts.Count == 0) return html;
        cuts.Sort((a, b) => a.start.CompareTo(b.start));
        var sb = new StringBuilder(html.Length);
        var pos = 0;
        foreach (var (start, len) in cuts)
        {
            sb.Append(html, pos, start - pos);
            pos = start + len;
        }
        sb.Append(html, pos, html.Length - pos);
        return sb.ToString();
    }

    /// <summary>Parse one HTML &lt;table&gt; into a generator <see cref="Table"/> so it can
    /// be laid out as real columns (rows × cells side-by-side) instead of the flat
    /// single-column stack that tag-stripping produces. One TextFragment is emitted per
    /// &lt;br&gt;-separated cell line; cells word-wrap to the column width. Header (&lt;th&gt;)
    /// cells are bold and centred; colspan, per-cell text-align, and CSS font-size / border /
    /// padding / table-width are honoured. Nested tables are not supported. Returns null when
    /// the markup yields no rows.</summary>
    /// <summary>A borderless outer table whose cells hold tables of their own is a
    /// LAYOUT table: it places blocks on the sheet rather than drawing a grid of its
    /// own. Such markup renders by laying each cell's table out at that cell's own
    /// position, so the nested tables must be lifted rather than flattened.</summary>
    internal static bool IsLayoutTableHtml(string? html)
    {
        var s = html ?? "";
        var open = Regex.Match(s, @"<table[^>]*>", RegexOptions.IgnoreCase);
        if (!open.Success) return false;
        // the outer table must not draw its own grid
        var bm = Regex.Match(open.Value, @"border\s*=\s*['""]?(\d+)", RegexOptions.IgnoreCase);
        if (bm.Success && bm.Groups[1].Value != "0") return false;
        if (Regex.IsMatch(open.Value, @"border\s*:\s*[1-9]", RegexOptions.IgnoreCase)) return false;
        // and it must actually contain a table inside a cell
        var depth = 0;
        var maxDepth = 0;
        foreach (Match t in Regex.Matches(s, @"</?table[^>]*>", RegexOptions.IgnoreCase))
        {
            depth += t.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
            if (depth > maxDepth) maxDepth = depth;
        }
        return maxDepth >= 2;
    }

    /// <summary>Line breaks written between a table's rows belong to no cell, so the
    /// parser moves them out in front of the table — each one still takes a line of the
    /// body's own height above everything the table draws.</summary>
    internal static int FosterParentedBreaks(string? html)
    {
        var s = html ?? "";
        var open = s.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return 0;
        // only the markup between cells can be fostered: drop every cell's content
        var outside = Regex.Replace(s[open..],
            @"<t[dh]\b[^>]*>[\s\S]*?</t[dh]\s*>", " ", RegexOptions.IgnoreCase);
        return Regex.Matches(outside, @"<br\b[^>]*>", RegexOptions.IgnoreCase).Count;
    }

    /// <summary>The width a document declares for its body, in points — the containing
    /// block a top-level percentage width resolves against. 0 when none is declared.</summary>
    internal static double DeclaredBodyWidthPt(string? html)
    {
        var m = Regex.Match(html ?? "", @"<body\b[^>]*style\s*=\s*(['""])([^'""]*)\1",
            RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        var w = Regex.Match(m.Groups[2].Value, @"(?<![-\w])width\s*:\s*([\d.]+)\s*px",
            RegexOptions.IgnoreCase);
        return w.Success
            ? double.Parse(w.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
            : 0;
    }

    /// <summary>Report label/span rows: a body of divs each holding an inline-block
    /// percentage LABEL beside an inline-block percentage SPAN — the label column is
    /// bold and right-aligned in its box, the span wraps in its own column, and an
    /// hr divides the sections at its own percentage width. Returns false unless at
    /// least one such row is present, leaving the ordinary flow untouched.</summary>
    /// <summary>Detect a fragment that is ONLY nested styled spans (no block or other
    /// tags): each style boundary starts a new run, sizes inherit down the span chain,
    /// a background-color belongs to its own span. The canonical renderer emits one
    /// text fragment per run, so the split must survive to the absorber. False unless
    /// the spans produce at least two distinctly-styled runs.</summary>
    internal static bool TryParseNestedStyledSpans(string? html,
        out List<(string Text, double SizePt, Color? Bg)> runs)
    {
        var parsed = new List<(string Text, double SizePt, Color? Bg)>();
        runs = parsed;
        var s = (html ?? "").Trim();
        if (!Regex.IsMatch(s, @"^<span\b", RegexOptions.IgnoreCase)) return false;
        // only span tags allowed anywhere
        foreach (Match tg in Regex.Matches(s, @"<[^>]*>"))
            if (!Regex.IsMatch(tg.Value, @"^<\s*/?\s*span\b", RegexOptions.IgnoreCase))
                return false;
        var sizeStack = new Stack<double>();
        var bgStack = new Stack<Color?>();
        double curSize = 0;
        Color? curBg = null;
        var pos = 0;
        var nested = false;
        void Emit(string raw)
        {
            var text = DecodeEntities(raw);
            if (text.Length == 0) return;
            if (parsed.Count > 0 && parsed[^1].SizePt == curSize && Equals(parsed[^1].Bg, curBg))
                parsed[^1] = (parsed[^1].Text + text, curSize, curBg);
            else parsed.Add((text, curSize, curBg));
        }
        foreach (Match tg in Regex.Matches(s, @"<[^>]*>"))
        {
            Emit(s[pos..tg.Index]);
            pos = tg.Index + tg.Length;
            if (Regex.IsMatch(tg.Value, @"^<\s*/", RegexOptions.IgnoreCase))
            {
                if (sizeStack.Count > 0) { curSize = sizeStack.Pop(); curBg = bgStack.Pop(); }
                continue;
            }
            if (sizeStack.Count > 0) nested = true;
            sizeStack.Push(curSize);
            bgStack.Push(curBg);
            var st = Regex.Match(tg.Value, @"style\s*=\s*(['""])(?<s>[^'""]*)\1", RegexOptions.IgnoreCase);
            if (st.Success)
            {
                var css = st.Groups["s"].Value;
                var fsm = Regex.Match(css, @"font-size\s*:\s*([\d.]+)\s*(pt|px)", RegexOptions.IgnoreCase);
                if (fsm.Success)
                {
                    var v = double.Parse(fsm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    curSize = fsm.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? v * 0.75 : v;
                }
                var bgm = Regex.Match(css, @"background-color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                // a background belongs to its own span, it does not inherit out
                curBg = bgm.Success ? ParseCssColor(bgm.Groups[1].Value.Trim()) : null;
            }
            else curBg = null;
        }
        Emit(s[pos..]);
        if (!nested || parsed.Count < 2) return false;
        var anySize = false;
        foreach (var r in parsed) if (r.SizePt > 0) anySize = true;
        return anySize;
    }

    /// <summary>Greedy word wrap on the report face's own kerned metrics — the
    /// wrap must break where the drawn face breaks.</summary>
    private static string[] ReportWordWrap(string text, double avail, double fs, bool bold)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var word in text.Split(' '))
        {
            var cand = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && HeaderFooter.MeasureReportText(cand, fs, bold) > avail)
            { lines.Add(cur); cur = word; }
            else cur = cand;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines.ToArray();
    }

    // ── report label/span dialect ───────────────────────────────────────────
    // Empirical fixed quantities of this dialect; the page band follows the
    // widened-sheet model below (saturating rows).
    private const double ReportFontPt = 9.75;         // div { font-size: small } = 13 css px

    private const double ReportSpanGapEm = 0.774;     // label margin-right (.5 em) + one space (.274 em)

    private const double ReportHrBelowBasePt = 7.89;  // last baseline → the groove's top line

    private const double ReportHrAfterPt = 17.25;     // groove top → next baseline is the

                                                      // standard 18.75 seat, less the groove's 1.5
    private const double ReportGroovePt = 0.75;       // each groove line, 1 css px

    private const double ReportPageRightPt = 87.3;    // the right band of the widened sheet

    // CSS reference pixel → PDF point (96 px per inch over 72 pt per inch).
    private const double CssPxToPt = 72.0 / 96.0;

    // The UA stylesheet's list indent, `ol, ul { padding-inline-start: 40px }` —
    // where an <li>'s content seats inside a table cell; the marker hangs left of it.
    private const double UaListPaddingStartPx = 40.0;

    private const double ListItemIndentPt = UaListPaddingStartPx * CssPxToPt;

    private const double ReportFirstSeatPt = 0.59;    // the first body baseline seats

                                                      // this far ABOVE the top-margin line (88.41)

    private static bool TryBuildReportLabelBlocks(string html, double bodyW, out List<Block> blocks)
    {
        blocks = new List<Block>();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var content = Regex.Match(html, @"(?s)<body\b[^>]*>(.*?)</body>",
            RegexOptions.IgnoreCase) is { Success: true } bm2 ? bm2.Groups[1].Value : html;
        content = Regex.Replace(content, @"(?s)<script\b.*?</script>|<!--.*?-->", "");
        const double fs = ReportFontPt;
        static double Pct(string st)
        {
            var m2 = Regex.Match(st, @"(?<![-\w])width\s*:\s*([\d.]+)%");
            return m2.Success
                ? double.Parse(m2.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0
                : 0;
        }
        var any = false;
        foreach (Match dv in Regex.Matches(content, @"(?s)<div>\s*(.*?)\s*</div>", RegexOptions.IgnoreCase))
        {
            var inner = dv.Groups[1].Value;
            var lm = Regex.Match(inner,
                @"(?s)<label\b[^>]*style\s*=\s*(['""])(?<st>[^'""]*)\1[^>]*>(?<t>.*?)</label>",
                RegexOptions.IgnoreCase);
            var sm2 = Regex.Match(inner,
                @"(?s)<span\b[^>]*style\s*=\s*(['""])(?<st>[^'""]*)\1[^>]*>(?<t>.*?)</span>",
                RegexOptions.IgnoreCase);
            if (lm.Success && sm2.Success)
            {
                var labelBox = Pct(lm.Groups["st"].Value) * bodyW;
                var spanBox = Pct(sm2.Groups["st"].Value) * bodyW;
                if (labelBox <= 0 || spanBox <= 0) continue;
                var labelText = Regex.Replace(DecodeEntities(
                    HtmlFragment.StripHtmlTags(lm.Groups["t"].Value)), @"\s+", " ").Trim();
                var spanText = Regex.Replace(DecodeEntities(
                    HtmlFragment.StripHtmlTags(sm2.Groups["t"].Value)), @"\s+", " ").Trim();
                any = true;
                // the label is bold and gives the row's height back to the span;
                // the span sits past the label's box, its space and its 0.5-em margin
                blocks.Add(new Block
                {
                    Text = labelText, FontSize = fs, FontRes = "F2", FontFamily = "Arial",
                    RightAlignBoxPt = labelBox, MaxWidthPt = labelBox,
                    NoAdvanceY = spanText.Length > 0,
                    MarginTop = blocks.Count == 0 ? -ReportFirstSeatPt : 0,
                    MarginTopAlways = blocks.Count == 0,
                });
                blocks.Add(new Block
                {
                    Text = spanText.Length > 0 ? spanText : " ",
                    FontSize = fs, FontFamily = "Arial",
                    LeftIndent = labelBox + ReportSpanGapEm * fs,
                    MaxWidthPt = spanBox,
                });
                continue;
            }
            var hrM = Regex.Match(inner, @"<hr\b[^>]*style\s*=\s*(['""])(?<st>[^'""]*)\1",
                RegexOptions.IgnoreCase);
            if (hrM.Success || Regex.IsMatch(inner, @"<hr\b", RegexOptions.IgnoreCase))
                blocks.Add(new Block
                {
                    IsHorizontalRule = true, RuleWidth = 1,
                    MaxWidthPt = (hrM.Success && Pct(hrM.Groups["st"].Value) > 0
                        ? Pct(hrM.Groups["st"].Value) : 1.0) * bodyW,
                });
        }
        return any;
    }

    // CSS auto table layout: a table asks each column how narrow it
    // can be (its widest unbreakable run) and how wide it would like
    // to be (its whole content), then takes clamp(available, min, max)
    // — never narrower than its minimum, overflowing its container
    // instead. With room to spare the surplus is shared in proportion
    // to what each column wanted; with too little, every column sits
    // at its minimum and wraps.
    internal static double LayoutCellPad(Table t)
        => (t.DefaultCellPadding?.Left ?? 0) + (t.DefaultCellPadding?.Right ?? 0);

    internal static (double Min, double Max) LayoutCellSpan(Cell cell, double pad, double border)
    {
        double min = 0, max = 0;
        foreach (var cp in cell.Paragraphs)
        {
            if (cp is not Aspose.Pdf.Text.TextFragment ctf || string.IsNullOrEmpty(ctf.Text)) continue;
            var cfs = ctf.TextState.FontSize > 0 ? ctf.TextState.FontSize : 8;
            var bold = ctf.TextState.IsBold
                || (ctf.TextState.Font?.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) ?? false);
            var cface = bold ? "Helvetica-Bold" : "Helvetica";
            double W(string t)
            {
                if (t.Length == 0) return 0;
                try
                {
                    return Aspose.Pdf.Text.FontRepository.TryFindFont(cface)?.MeasureString(t, cfs)
                           ?? t.Length * cfs * 0.5;
                }
                catch { return t.Length * cfs * 0.5; }
            }
            foreach (var lineText in ctf.Text!.Replace("\r\n", "\n").Split('\n'))
            {
                var whole = W(lineText);
                max = Math.Max(max, whole);
                if (cell.HtmlNoWrap) { min = Math.Max(min, whole); continue; }
                foreach (var word in lineText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    min = Math.Max(min, W(word));
            }
        }
        return (min + pad + border, max + pad + border);
    }

    internal static void ApplyAutoWidths(Table t, double avail, bool fill = false)
    {
        if (t.Rows.Count == 0) return;
        var cols = 0;
        foreach (var r in t.Rows)
        {
            var n = 0;
            foreach (var c in r.Cells) n += Math.Max(1, c.ColSpan);
            cols = Math.Max(cols, n);
        }
        if (cols == 0) return;

        var pad = LayoutCellPad(t);
        // A rule the cells carry from a style rule is shared with the neighbour, so the
        // column only advances by one of them; a rule the table's own BORDER attribute
        // put on every cell is the cell's alone, and costs both sides.
        var border = t.HtmlCellBorderPt > 0
            ? (t.HtmlCellBorderShared ? 1 : 2) * t.HtmlCellBorderPt : 0.0;
        var mins = new double[cols];
        var maxs = new double[cols];
        // a cell that spans rows keeps holding its columns on the rows
        // below, so the cells there start after it — without that the
        // sub-header row lands under the label columns
        var occupied = new int[cols];
        foreach (var r in t.Rows)
        {
            var ci = 0;
            foreach (var c in r.Cells)
            {
                while (ci < cols && occupied[ci] > 0) ci++;
                if (ci >= cols) break;
                var span = Math.Max(1, c.ColSpan);
                var (cmin, cmax) = LayoutCellSpan(c, pad, border);
                if (span == 1)
                {
                    mins[ci] = Math.Max(mins[ci], cmin);
                    maxs[ci] = Math.Max(maxs[ci], cmax);
                }
                if (c.RowSpan > 1)
                    for (var k = ci; k < Math.Min(cols, ci + span); k++)
                        occupied[k] = c.RowSpan;
                ci += span;
            }
            for (var k = 0; k < cols; k++)
                if (occupied[k] > 0) occupied[k]--;
        }
        double wmin = 0, wmax = 0;
        for (var i = 0; i < cols; i++)
        {
            if (maxs[i] <= 0) maxs[i] = mins[i];
            wmin += mins[i];
            wmax += maxs[i];
        }
        if (wmin <= 0) return;

        var used = fill
            ? Math.Max(avail, wmin)
            : Math.Clamp(avail, wmin, Math.Max(wmin, wmax));
        var outW = new double[cols];
        if ((fill || used >= wmax - 0.01) && wmax > 0)
            // sharing the width out in proportion must never take a column below the
            // narrowest it can be — that would force a wrap the layout must not have
            for (var i = 0; i < cols; i++) outW[i] = Math.Max(mins[i], maxs[i] * used / wmax);
        else
            for (var i = 0; i < cols; i++) outW[i] = mins[i];

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < cols; i++)
        {
            if (i > 0) sb.Append(' ');
            // Full precision: a column measured to the exact width of its widest word
            // must not lose a thousandth on the way to the renderer, or the word it was
            // sized for wraps. (The renderer wraps against the same measure and the same
            // box, so no slack is needed on top.)
            sb.Append(outW[i].ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        }
        t.ColumnWidths = sb.ToString();
    }

    /// <summary>Give every row the height its own content asks for: each cell wraps
    /// at the column width it was just given, and the row takes the tallest cell —
    /// its lines on the face's own line height, plus the cell's padding and the rule
    /// the grid boxes it with. Without this a row falls back to the generic model,
    /// which leaves the whole sheet drifting.</summary>
    internal static void ApplyAutoRowHeights(Table t)
    {
        if (t.Rows.Count == 0 || string.IsNullOrEmpty(t.ColumnWidths)) return;
        var cols = new List<double>();
        foreach (var w in t.ColumnWidths!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(w, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wv))
                cols.Add(wv);
        if (cols.Count == 0) return;

        var pad = LayoutCellPad(t);
        var padV = (t.DefaultCellPadding?.Top ?? 0) + (t.DefaultCellPadding?.Bottom ?? 0);
        var border = t.HtmlCellBorderPt > 0 ? 2 * t.HtmlCellBorderPt : 0.0;
        var occupied = new int[cols.Count];

        // a cell that spans rows asks for height across ALL of them, so it is measured
        // in a second pass and only makes up a shortfall — letting it drive the first
        // row alone would make every spanned row as tall as the whole cell
        var heights = new double[t.Rows.Count];
        var spanning = new List<(int Row, int Span, double Need)>();
        for (var ri = 0; ri < t.Rows.Count; ri++)
        {
            var row = t.Rows.At(ri);
            var rowH = 0.0;
            var ci = 0;
            foreach (var cell in row.Cells)
            {
                while (ci < cols.Count && occupied[ci] > 0) ci++;
                if (ci >= cols.Count) break;
                var span = Math.Max(1, cell.ColSpan);
                var boxW = 0.0;
                for (var k = ci; k < Math.Min(cols.Count, ci + span); k++) boxW += cols[k];
                var innerW = Math.Max(4, boxW - pad - border);

                var cellH = 0.0;
                foreach (var cp in cell.Paragraphs)
                {
                    if (cp is not Aspose.Pdf.Text.TextFragment ctf || string.IsNullOrEmpty(ctf.Text)) continue;
                    var size = ctf.TextState.FontSize > 0 ? ctf.TextState.FontSize : 8;
                    var bold = ctf.TextState.IsBold
                        || (ctf.TextState.Font?.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) ?? false);
                    var face = bold ? "Helvetica-Bold" : "Helvetica";
                    var lh = FaceLineHeight(face, size);
                    var logicals = ctf.Text!.Replace("\r\n", "\n").Split('\n');
                    for (var li = 0; li < logicals.Length; li++)
                    {
                        // a trailing break closes the last line, it does not open another
                        if (logicals[li].Length == 0 && logicals.Length > 1
                            && li == logicals.Length - 1) continue;
                        cellH += Math.Max(1, WrappedLineCount(logicals[li], face, size, innerW,
                            cell.HtmlNoWrap)) * lh;
                    }
                }
                if (cellH > 0)
                {
                    if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_ROWH") == "1")
                        Console.WriteLine($"    r{ri} ci={ci} span={span} rs={cell.RowSpan} "
                            + $"innerW={innerW:0.00} cellH={cellH:0.00} nowrap={cell.HtmlNoWrap} "
                            + $"text='{FirstText(cell)}'");
                    if (cell.RowSpan > 1) spanning.Add((ri, cell.RowSpan, cellH + padV + border));
                    else rowH = Math.Max(rowH, cellH + padV + border);
                }
                if (cell.RowSpan > 1)
                    for (var k = ci; k < Math.Min(cols.Count, ci + span); k++)
                        occupied[k] = cell.RowSpan;
                ci += span;
            }
            for (var k = 0; k < cols.Count; k++)
                if (occupied[k] > 0) occupied[k]--;
            heights[ri] = rowH;
        }
        foreach (var (ri, span, need) in spanning)
        {
            var have = 0.0;
            for (var k = ri; k < Math.Min(heights.Length, ri + span); k++) have += heights[k];
            var last = Math.Min(heights.Length, ri + span) - 1;
            if (last >= 0 && need > have) heights[last] += need - have;
        }
        for (var ri = 0; ri < t.Rows.Count; ri++)
            if (heights[ri] > 0) t.Rows.At(ri).FixedRowHeight = heights[ri];
    }

    private static string FirstText(Cell c)
    {
        foreach (var p in c.Paragraphs)
            if (p is Aspose.Pdf.Text.TextFragment tf && !string.IsNullOrEmpty(tf.Text))
                return tf.Text!.Length > 18 ? tf.Text![..18] : tf.Text!;
        return "";
    }

    /// <summary>How many lines a run takes in the width it is given — one when the
    /// cell refuses to break.</summary>
    private static int WrappedLineCount(string text, string face, double size, double width, bool noWrap)
    {
        if (text.Length == 0) return 1;
        double W(string t)
        {
            if (t.Length == 0) return 0;
            try
            {
                return Aspose.Pdf.Text.FontRepository.TryFindFont(face)?.MeasureString(t, size)
                       ?? t.Length * size * 0.5;
            }
            catch { return t.Length * size * 0.5; }
        }
        if (noWrap || W(text) <= width) return 1;
        var lines = 1;
        var cur = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // A word that opens a line always takes it, even when it is wider than
            // the column: it overflows, it does not buy a second line.
            if (cur.Length == 0) { cur = word; continue; }
            var cand = cur + " " + word;
            if (W(cand) <= width) { cur = cand; continue; }
            lines++;
            cur = word;
        }
        return lines;
    }

    /// <summary>Marker left in a cell where a nested table was lifted out.</summary>
    private const string NestedMark = "\u0001NT[";

    /// <summary>Lift every table nested inside a cell out of <paramref name="html"/>,
    /// leaving a marker in its place and collecting its markup in
    /// <paramref name="captured"/>. The outer table's own structure is untouched.</summary>
    private static string ExtractNestedTables(string html, List<string> captured)
    {
        var outerOpen = Regex.Match(html, @"<table[^>]*>", RegexOptions.IgnoreCase);
        if (!outerOpen.Success) return html;
        var sb = new StringBuilder(html[..(outerOpen.Index + outerOpen.Length)]);
        var i = outerOpen.Index + outerOpen.Length;
        while (i < html.Length)
        {
            var open = Regex.Match(html[i..], @"<table[^>]*>", RegexOptions.IgnoreCase);
            if (!open.Success) { sb.Append(html[i..]); break; }
            var start = i + open.Index;
            sb.Append(html[i..start]);
            // walk to this table's matching close
            var depth = 0;
            var j = start;
            var end = html.Length;
            foreach (Match t in Regex.Matches(html[start..], @"</?table[^>]*>", RegexOptions.IgnoreCase))
            {
                depth += t.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
                if (depth == 0) { end = start + t.Index + t.Length; break; }
            }
            _ = j;
            captured.Add(html[start..end]);
            sb.Append(NestedMark).Append(captured.Count - 1).Append(']');
            i = end;
        }
        return sb.ToString();
    }

    /// <summary>The right padding the header band's own container declares — e.g.
    /// <c>.header-changelog { padding-right: 42px }</c> — resolved against the fragment's
    /// inline styles AND its linked stylesheet when the load options can reach it. The
    /// band's right-aligned lines anchor that much inside the band's right margin. 0 when
    /// no reachable rule declares one.</summary>
    internal static double BandPaddingRightPt(string? html, HtmlLoadOptions? options)
    {
        if (string.IsNullOrEmpty(html)) return 0;
        var holder = Regex.Match(html,
            @"<div\b[^>]*class\s*=\s*(['""])(?<cls>[^'""]*header-right[^'""]*)\1",
            RegexOptions.IgnoreCase);
        if (!holder.Success) return 0;
        var withCss = options is not null ? InlineLinkedStylesheets(html, options) : html;
        foreach (var cls in holder.Groups["cls"].Value
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var rule = Regex.Match(withCss,
                @"\." + Regex.Escape(cls) + @"\s*\{[^}]*padding-right\s*:\s*([\d.]+)\s*px",
                RegexOptions.IgnoreCase);
            if (rule.Success && double.TryParse(rule.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var px) && px > 0)
                return px * 0.75;
        }
        return 0;
    }

    /// <summary>The base type an in-page HtmlFragment sets in: the size, family and
    /// background its own <c>body { … }</c> rule — or the <c>&lt;body style&gt;</c>
    /// attribute, which outranks the type rule exactly as CSS specificity says — declares.
    /// A fragment with no assigned TextState inherits this instead of the 11 pt Standard-14
    /// default, exactly as a browser would. The family is returned only when the first named
    /// face of the stack is INSTALLED, so callers can measure it; size is 0 and face null
    /// when the document declares neither.</summary>
    internal static (double SizePt, string? Face, Color? BgColor) BodyCssFont(string html)
    {
        if (string.IsNullOrEmpty(html)) return (0, null, null);
        var declared = new List<Dictionary<string, string>>();
        var css = ParseStyleSheet(html);
        if (css.TryGetValue("body", out var bodyRule)) declared.Add(bodyRule);
        // The element's own style attribute wins over the type rule, so it is read last.
        if (Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase) is { Success: true } bodyTag
            && Regex.Match(bodyTag.Value, @"style\s*=\s*(""([^""]*)""|'([^']*)')",
                RegexOptions.IgnoreCase) is { Success: true } styleAttr)
        {
            var inline = styleAttr.Groups[2].Success ? styleAttr.Groups[2].Value : styleAttr.Groups[3].Value;
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var decl in inline.Split(';'))
            {
                var c = decl.IndexOf(':');
                if (c <= 0) continue;
                props[decl[..c].Trim()] = decl[(c + 1)..].Trim();
            }
            if (props.Count > 0) declared.Add(props);
        }
        if (declared.Count == 0) return (0, null, null);

        var sizePt = 0.0;
        string? face = null;
        Color? bg = null;
        foreach (var rule in declared)
        {
            if (rule.TryGetValue("font-size", out var fs) && TryParseLength(fs, out var fsPt) && fsPt > 0)
                sizePt = fsPt;
            if (rule.TryGetValue("font-family", out var ff))
                foreach (var fam in ff.Split(','))
                {
                    var f = fam.Trim().Trim('"', '\'');
                    if (f.Length > 0 && WinMetricsFor(f) is not null) { face = f; break; }
                }
            if (rule.TryGetValue("background-color", out var bc) && ParseCssColor(bc.Trim()) is { } bgc)
                bg = bgc;
        }
        return (sizePt, face, bg);
    }

    internal static Table? BuildTableFromHtml(string html) => BuildTableFromHtml(html, 0, out _);

    /// <summary>The UA stylesheet's own <c>td, th { padding: 1px }</c>, in points —
    /// the vertical half of the cell's default content inset.</summary>
    /// <summary>The UA's own separate-borders <c>border-spacing: 2px</c>, in points.
    /// (The vertical pad partner is <see cref="UaCellPadPt"/>, declared with the
    /// chain-dialect constants.)</summary>
    private const double UaCellSpacingPt = 2.0 * 0.75;

    /// <summary>CSS visible border styles — a border only paints for one of these.</summary>
    private static readonly string[] BorderStyleKeywords =
        { "solid", "double", "dashed", "dotted", "groove", "ridge", "inset", "outset" };

    /// <summary>The CSS `thin | medium | thick` width keywords, in the px the UA gives them.</summary>
    private static readonly (string Word, double Px)[] BorderWidthKeywords =
        { ("thin", 1.0), ("medium", 3.0), ("thick", 5.0) };

    /// <summary>Read one `border-&lt;side&gt;` shorthand out of an inline style.
    /// The three components are order-INDEPENDENT in CSS, and report HTML writes them
    /// every way round (`1pt solid black`, `black 1pt solid`, `solid black`), so each
    /// whitespace-separated token is classified by shape rather than by position.
    /// Returns false unless the declaration names a visible style — a border with no
    /// style keyword paints nothing.</summary>
    private static bool TryParseBorderShorthand(string style, string prop,
        out double widthPt, out Color? color)
    {
        const double PxToPt = 0.75;
        // CSS default when the shorthand names a style but no width.
        const double MediumBorderPx = 3.0;
        widthPt = 0; color = null;
        var dm = Regex.Match(style, @"(?<![-\w])" + prop + @"\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (!dm.Success) return false;
        var hasStyle = false;
        double? px = null;
        foreach (var tok in dm.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = tok.Trim();
            if (Array.Exists(BorderStyleKeywords, k => string.Equals(k, t, StringComparison.OrdinalIgnoreCase)))
            { hasStyle = true; continue; }
            var wm = Regex.Match(t, @"^([\d.]+)\s*(px|pt)$", RegexOptions.IgnoreCase);
            if (wm.Success && double.TryParse(wm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wv))
            {
                px = wm.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase)
                    ? wv / PxToPt : wv;
                continue;
            }
            var kw = Array.FindIndex(BorderWidthKeywords,
                k => string.Equals(k.Word, t, StringComparison.OrdinalIgnoreCase));
            if (kw >= 0) { px = BorderWidthKeywords[kw].Px; continue; }
            color ??= ParseCssColor(t);
        }
        if (!hasStyle) return false;
        widthPt = (px ?? MediumBorderPx) * PxToPt;
        return true;
    }

    /// <summary>Build a generator Table from an HTML &lt;table&gt;. When it has no explicit column
    /// widths, columns auto-fit to content: max-content (no wrapping) if the table fits
    /// <paramref name="availWidthPt"/>, otherwise min-content (each column shrinks to its widest
    /// word). <paramref name="availWidthPt"/> ≤ 0 means unconstrained (always max-content).
    /// <paramref name="naturalWidthPt"/> returns the total width the chosen columns occupy.</summary>
    internal static Table? BuildTableFromHtml(string html, double availWidthPt, out double naturalWidthPt)
        => BuildTableFromHtml(html, availWidthPt, out naturalWidthPt, null, null);

    internal static Table? BuildTableFromHtml(string html, double availWidthPt, out double naturalWidthPt,
        HtmlLoadOptions? options, List<byte[]>? inlineSvgs)
        => BuildTableFromHtml(html, availWidthPt, out naturalWidthPt, options, inlineSvgs, null);

    // Widen-probe run markers embedded in measure lines:
    // U+E000 bold open, U+E001 bold close, U+E002 sup/sub open, U+E003 sup/sub close.
    private static readonly char[] ProbeSentinels = { '\uE000', '\uE001', '\uE002', '\uE003' };

    /// <summary>Size (pt) and family from a CSS <c>font:</c> SHORTHAND declaration on
    /// <paramref name="sel"/> ("font: bold 10px Verdana,Arial") \u2014 the form the
    /// font-size/font-family longhand probes miss. Null when the selector has no
    /// shorthand with a parsable size.</summary>
    private static (double sizePt, string? family)? CssFontShorthand(
        IReadOnlyDictionary<string, Dictionary<string, string>> css, string sel)
    {
        if (!css.TryGetValue(sel, out var d) || !d.TryGetValue("font", out var v)) return null;
        var m = Regex.Match(v, @"([\d.]+)\s*(px|pt)\s*([^/;]*)");
        if (!m.Success) return null;
        var size = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (m.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)) size *= 0.75;
        var fam = m.Groups[3].Value.Trim();
        return (size, fam.Length > 0 ? FirstFontFamily(fam) : null);
    }
}
