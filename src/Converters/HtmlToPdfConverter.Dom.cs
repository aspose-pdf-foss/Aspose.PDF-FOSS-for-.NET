using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>
    /// Extract markup regions that render as STYLED INLINE ROWS — layouts the block
    /// flow cannot express:
    ///  • a site nav bar: a fixed-height container with an absolutely-positioned
    ///    full-width background strip and inline-block tab lists (left + right groups);
    ///  • a centered line of inline links (text-align:center / &lt;center&gt; with only
    ///    inline children), where inter-link spacing comes from CSS margins and the link
    ///    color from the stylesheet.
    /// Each extracted region is replaced by a &lt;rowmark i="N"&gt;&lt;/rowmark&gt;
    /// placeholder; ParseBlocks emits the prebuilt row block at that position.
    /// </summary>
    private static string ExtractRowBlocks(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css, out List<Block> rowBlocks)
    {
        rowBlocks = new List<Block>();
        if (css is null || css.Count == 0 && html.IndexOf("text-align", StringComparison.OrdinalIgnoreCase) < 0)
            return html;
        HtmlNode dom;
        // Comments blank out too (space-padded so SrcIndex offsets survive) —
        // ParseDom has no comment handling and their text would leak into the
        // tree as literal content.
        try { dom = ParseDom(Regex.Replace(Regex.Replace(html,
                 @"<!--[\s\S]*?-->", m => new string(' ', m.Length)),
                 @"<(script|style|head)[^>]*>[\s\S]*?</\1>",
                 m => new string(' ', m.Length), RegexOptions.IgnoreCase)); }
        catch { return html; }

        var extracts = new List<(int start, int end, Block block)>();
        bool Overlaps(int s, int e) => extracts.Any(x => s < x.end && e > x.start);

        // ── nav bars ──
        foreach (var el in dom.Descendants())
        {
            if (el.Tag != "div") continue;
            if (DomDecl(el, "position", css)?.Equals("absolute", StringComparison.OrdinalIgnoreCase) != true) continue;
            if (DomDecl(el, "width", css)?.Trim() != "100%") continue;
            var barH = ParsePxValue(DomDecl(el, "height", css));
            if (barH <= 0) continue;
            var barColor = ParseCssColor(DomDecl(el, "background-color", css)
                                         ?? DomDecl(el, "background", css) ?? "");
            if (barColor is null) continue;
            // container: nearest ancestor with an explicit height
            HtmlNode? container = null;
            for (var p = el.Parent; p is not null && p.Tag.Length > 0; p = p.Parent)
                if (ParsePxValue(DomDecl(p, "height", css)) > 0) { container = p; break; }
            if (container is null || Overlaps(container.SrcIndex, container.SrcEnd)) continue;
            var block = BuildNavRowBlock(container, el, barColor, barH, css);
            if (block is not null)
                extracts.Add((container.SrcIndex, container.SrcEnd, block));
        }

        // ── positioned media cards (relative media box + absolute caption bars +
        //    float prose/info columns) ──
        foreach (var el in dom.Descendants())
        {
            if (el.Tag != "div") continue;
            if (DomDecl(el, "position", css)?.Trim()
                    .Equals("relative", StringComparison.OrdinalIgnoreCase) != true) continue;
            HtmlNode? cardHost = null;
            for (var p = el.Parent; p is not null && p.Tag.Length > 0; p = p.Parent)
                if (p.Tag == "div") { cardHost = p; break; }
            if (cardHost is null || Overlaps(cardHost.SrcIndex, cardHost.SrcEnd)) continue;
            var block = BuildPositionedCardBlock(cardHost, el, css);
            if (block is not null)
                extracts.Add((cardHost.SrcIndex, cardHost.SrcEnd, block));
        }

        // ── flex-row waybill grids (a full-width bordered container whose rows
        //    are display:flex divs of percent-width bordered columns) ──
        foreach (var el in dom.Descendants())
        {
            if (el.Tag != "div" || Overlaps(el.SrcIndex, el.SrcEnd)) continue;
            var fgBorder = DomDecl(el, "border", css);
            if (fgBorder is null || !fgBorder.Contains("solid", StringComparison.OrdinalIgnoreCase)) continue;
            if (DomDecl(el, "width", css)?.Trim() != "100%") continue;
            var flexRows = 0;
            foreach (var d in el.Descendants())
                if (d.Tag is "div" or "tr" && DomDecl(d, "display", css)?.Trim()
                        .Equals("flex", StringComparison.OrdinalIgnoreCase) == true)
                    flexRows++;
            if (flexRows < 4) continue;
            var block = BuildFlexGridBlock(el, css);
            if (block is not null)
            {
                // A positioned page wrapper above the grid may declare the sheet's
                // content width in physical units (width: 8in) — the widen reads it
                // off the block. The wrapper chain's HEIGHT (physical × any percent
                // factors on the way down, e.g. 10in × 107%) bounds the container
                // border, which overflows onto a continuation page.
                var hFactor = 1.0;
                for (var p = el.Parent; p is not null; p = p.Parent)
                {
                    if (p.Tag != "div") continue;
                    if (block.Flex!.PageContentPt <= 0 && DomDecl(p, "width", css) is { } pw
                        && Regex.IsMatch(pw, @"[\d.]+\s*(in|cm|mm|pt)\b", RegexOptions.IgnoreCase)
                        && TryParseLength(pw.Trim(), out var pwPt) && pwPt > 0)
                        block.Flex!.PageContentPt = pwPt;
                    if (DomDecl(p, "height", css) is { } ph)
                    {
                        var phv = ph.Trim();
                        if (Regex.Match(phv, @"^([\d.]+)\s*%$") is { Success: true } pctM
                            && double.TryParse(pctM.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var pctV)
                            && pctV > 0)
                            hFactor *= pctV / 100.0;
                        else if (Regex.IsMatch(phv, @"[\d.]+\s*(in|cm|mm|pt)\b", RegexOptions.IgnoreCase)
                                 && TryParseLength(phv, out var phPt) && phPt > 0)
                        {
                            block.Flex!.PageContentHPt = phPt * hFactor;
                            break;
                        }
                    }
                }
                // Swallow the positioned page-wrapper chain above the grid: each
                // wrapper is a div whose only content is the next one down, and
                // left behind it emits a page-filling height spacer ahead of the
                // grid (the blank-first-page failure).
                var fgTop = el;
                while (fgTop.Parent is { Tag: "div" } fgp)
                {
                    var others = 0;
                    foreach (var c in fgp.Children)
                        if ((c.Tag.Length > 0 && c != fgTop)
                            || (c.Tag.Length == 0 && c.Text.Trim().Length > 0)) others++;
                    if (others > 0) break;
                    fgTop = fgp;
                }
                extracts.Add((fgTop.SrcIndex, fgTop.SrcEnd, block));
            }
        }

        // ── positioned slides (a relative min/max-width canvas whose direct
        //    children are absolutely positioned text and background-image boxes —
        //    a slide editor's saved markup) ──
        foreach (var el in dom.Descendants())
        {
            if (el.Tag != "div" || Overlaps(el.SrcIndex, el.SrcEnd)) continue;
            if (DomDecl(el, "position", css)?.Trim()
                    .Equals("relative", StringComparison.OrdinalIgnoreCase) != true) continue;
            var slMinW = ParsePxValue(DomDecl(el, "min-width", css));
            var slMinH = ParsePxValue(DomDecl(el, "min-height", css));
            if (slMinW <= 0 || slMinH <= 0) continue;
            var block = BuildPositionedSlideBlock(el, slMinW, slMinH, css);
            if (block is not null)
                extracts.Add((el.SrcIndex, el.SrcEnd, block));
        }

        // ── centered search forms ──
        foreach (var el in dom.Descendants())
        {
            if (el.Tag != "form" || Overlaps(el.SrcIndex, el.SrcEnd)) continue;
            var centered = false;
            for (var p = el.Parent; p is not null; p = p.Parent)
                if (p.Tag == "center"
                    || DomDecl(p, "text-align", css)?.Contains("center", StringComparison.OrdinalIgnoreCase) == true)
                { centered = true; break; }
            if (!centered) continue;
            var block = BuildSearchFormBlock(el, css);
            if (block is not null)
                extracts.Add((el.SrcIndex, el.SrcEnd, block));
        }

        // ── RTL fixed-width diagram tables (figure + labels + svg legend row) ──
        if (Regex.IsMatch(html, @"<(?:html|body)[^>]*\bdir\s*=\s*[""']?rtl", RegexOptions.IgnoreCase))
        {
            foreach (var el in dom.Descendants())
            {
                if (el.Tag != "table" || Overlaps(el.SrcIndex, el.SrcEnd)) continue;
                var block = BuildRtlSvgTableBlock(el, css) ?? BuildRtlTopicsTableBlock(el, css);
                if (block is not null)
                    extracts.Add((el.SrcIndex, el.SrcEnd, block));
            }
        }

        // ── centered inline-link rows ──
        foreach (var el in dom.Descendants())
        {
            if (el.Tag is not ("div" or "p" or "span")) continue;
            if (Overlaps(el.SrcIndex, el.SrcEnd)) continue;
            var centered = DomDecl(el, "text-align", css)?.Contains("center", StringComparison.OrdinalIgnoreCase) == true;
            if (!centered)
                for (var p = el.Parent; p is not null; p = p.Parent)
                    if (p.Tag == "center") { centered = true; break; }
            if (!centered) continue;
            var hasLink = false;
            var onlyInline = true;
            foreach (var c in el.Children)
            {
                if (c.Tag.Length == 0) continue;
                if (IsHiddenElement(c.Tag, c.Attrs, css)) continue;
                if (c.Tag == "a") hasLink = true;
                else if (!InlineRowTags.Contains(c.Tag)) { onlyInline = false; break; }
            }
            if (!hasLink || !onlyInline) continue;
            var block = BuildCenteredLinkRow(el, css);
            if (block is not null)
                extracts.Add((el.SrcIndex, el.SrcEnd, block));
        }

        if (extracts.Count == 0) return html;
        // Assign indices in document order; substitute back-to-front so earlier
        // regions' source offsets stay valid.
        extracts.Sort((a, b) => a.start.CompareTo(b.start));
        var sb = new StringBuilder(html);
        for (var i = extracts.Count - 1; i >= 0; i--)
        {
            var (start, end, _) = extracts[i];
            sb.Remove(start, end - start);
            sb.Insert(start, $"<rowmark i=\"{i}\"></rowmark>");
        }
        foreach (var (_, _, b) in extracts) rowBlocks.Add(b);
        return sb.ToString();
    }

    /// <summary>Build the styled-run row for a nav-bar container: tabs from the
    /// left-pinned cluster, sign-in group from the right-pinned one, colors/weights
    /// resolved per element (including two-part descendant rules), the active tab's
    /// top strip from its border-top-color.</summary>
    private static Block? BuildNavRowBlock(HtmlNode container, HtmlNode barEl,
        Color barColor, double barHeightPx,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var fontPx = DomFontPx(container, 13, css);
        var rowHeightPx = Math.Max(ParsePxValue(DomDecl(container, "height", css)), barHeightPx);
        Color? barBorder = null;
        var bb = DomDecl(barEl, "border-bottom", css);
        if (!string.IsNullOrEmpty(bb) && !bb.Contains("none", StringComparison.OrdinalIgnoreCase))
            barBorder = ParseCssColor(bb) ?? Color.FromRgb(0, 0, 0);

        var runs = new List<RowRun>();
        double leftPad = 0, rightPad = 0;

        bool HiddenWithin(HtmlNode n, HtmlNode stopAt)
        {
            for (HtmlNode? p = n; p is not null && p != stopAt.Parent; p = p.Parent)
                if (p.Tag.Length > 0 && IsHiddenElement(p.Tag, p.Attrs, css)) return true;
            return false;
        }

        void CollectCluster(HtmlNode cluster, bool rightGroup)
        {
            foreach (var li in cluster.Descendants())
            {
                if (li.Tag != "li" || HiddenWithin(li, cluster)) continue;
                // An <li> nested inside another collected <li> (dropdown menus) is
                // not a tab of this row.
                var nested = false;
                for (var p = li.Parent; p is not null && p != cluster; p = p.Parent)
                    if (p.Tag == "li") { nested = true; break; }
                if (nested) continue;
                var text = DomText(li, css);
                if (text.Length == 0) continue;
                // style anchor: deepest element directly holding a text node
                HtmlNode styleEl = li;
                HtmlNode? Find(HtmlNode n)
                {
                    foreach (var c in n.Children)
                    {
                        if (c.Tag.Length > 0 && !IsHiddenElement(c.Tag, c.Attrs, css))
                        {
                            var inner = Find(c);
                            if (inner is not null) return inner;
                        }
                        else if (c.Tag.Length == 0 && c.Text.Trim().Length > 0)
                            return n;
                    }
                    return null;
                }
                styleEl = Find(li) ?? li;
                // horizontal padding/borders accumulated from the li down to the anchor
                double padL = 0, padR = 0;
                for (var n = styleEl; n is not null && n != li.Parent; n = n.Parent)
                {
                    var (l, r) = DomBoxLR(n, "padding", css);
                    padL += l; padR += r;
                    if (!string.IsNullOrEmpty(DomDecl(n, "border-left", css))) padL += 1;
                    if (!string.IsNullOrEmpty(DomDecl(n, "border-right", css))) padR += 1;
                }
                // active-tab strip: a descendant border-top with a real color
                Color? strip = null;
                double stripH = 2;
                foreach (var d in li.Descendants())
                {
                    if (d.Tag.Length == 0 || HiddenWithin(d, li)) continue;
                    // Zero-height elements are CSS-triangle tricks (dropdown arrows),
                    // not tab strips.
                    var hDecl = DomDecl(d, "height", css);
                    if (hDecl is not null && ParsePxValue(hDecl) <= 0) continue;
                    var stc = DomDecl(d, "border-top-color", css);
                    var c2 = stc is not null ? ParseCssColor(stc) : null;
                    if (c2 is null)
                    {
                        var bt = DomDecl(d, "border-top", css);
                        if (bt is not null && !bt.Contains("transparent", StringComparison.OrdinalIgnoreCase))
                            c2 = ParseCssColor(bt);
                    }
                    if (c2 is not null)
                    {
                        strip = c2;
                        var btw = DomDecl(d, "border-top", css);
                        var wpx = btw is not null ? ParsePxValue(btw) : 0;
                        if (wpx > 0) stripH = wpx;
                        break;
                    }
                }
                string? url = null;
                for (HtmlNode? n = styleEl; n is not null && n != li.Parent; n = n.Parent)
                    if (n.Tag == "a" && n.Attrs is not null && n.Attrs.TryGetValue("href", out var h)) { url = h; break; }

                runs.Add(new RowRun
                {
                    Text = text,
                    FontPx = DomFontPx(styleEl, fontPx, css),
                    Bold = DomBold(styleEl, css),
                    Color = DomColor(styleEl, css) ?? Color.FromRgb(204, 204, 204),
                    PadLeftPx = padL,
                    PadRightPx = padR,
                    TopStripColor = strip,
                    TopStripHeightPx = stripH,
                    RightGroup = rightGroup,
                    Url = url,
                });
            }
        }

        foreach (var d in container.Descendants())
        {
            if (d.Tag.Length == 0 || IsHiddenElement(d.Tag, d.Attrs, css)) continue;
            if (DomDecl(d, "position", css)?.Equals("absolute", StringComparison.OrdinalIgnoreCase) != true) continue;
            var isLeft = DomDecl(d, "left", css)?.Trim() == "0";
            var isRight = DomDecl(d, "right", css)?.Trim() == "0";
            if (!isLeft && !isRight) continue;
            var (pl, _) = DomBoxLR(d, "padding", css);
            var (_, pr) = DomBoxLR(d, "padding", css);
            if (isLeft) { leftPad = pl; CollectCluster(d, rightGroup: false); }
            else { rightPad = pr; CollectCluster(d, rightGroup: true); }
        }
        if (runs.Count == 0) return null;

        return new Block
        {
            Text = "",
            RowRuns = runs,
            RowHeightPx = rowHeightPx,
            RowBarColor = barColor,
            RowBarHeightPx = barHeightPx,
            RowBarBorderColor = barBorder,
            RowFontPx = fontPx,
            RowLeftPadPx = leftPad,
            RowRightPadPx = rightPad,
        };
    }

    /// <summary>Build an RTL diagram-table block: requires an explicit table width, a
    /// full-width inline-svg placeholder row, and a trailing legend row whose every
    /// cell holds one svg placeholder plus label text. Returns null when the table
    /// doesn't match (it then flows through the normal table pipeline).</summary>
    private static Block? BuildRtlSvgTableBlock(HtmlNode table,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var widthPx = ParsePxValue(DomDecl(table, "width", css));
        if (widthPx <= 0) return null;

        var rows = new List<HtmlNode>();
        foreach (var d in table.Descendants())
            if (d.Tag == "tr" && !IsHiddenElement(d.Tag, d.Attrs, css)) rows.Add(d);
        if (rows.Count < 2) return null;

        static List<HtmlNode> Cells(HtmlNode tr)
        {
            var cells = new List<HtmlNode>();
            foreach (var c in tr.Children)
                if (c.Tag is "td" or "th") cells.Add(c);
            return cells;
        }

        static (int idx, double w, double h)? SvgPlaceholder(HtmlNode scope)
        {
            foreach (var d in scope.Descendants())
            {
                if (d.Tag != "img" || d.Attrs is null) continue;
                if (!d.Attrs.TryGetValue("src", out var src)
                    || !src.StartsWith("inline-svg:", StringComparison.Ordinal)) continue;
                if (!int.TryParse(src.Substring("inline-svg:".Length), out var idx)) continue;
                double w = 0, h = 0;
                if (d.Attrs.TryGetValue("width", out var ws))
                    double.TryParse(ws, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out w);
                if (d.Attrs.TryGetValue("height", out var hs))
                    double.TryParse(hs, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out h);
                return (idx, w, h);
            }
            return null;
        }

        var dt = new RtlSvgTable { WidthPx = widthPx };

        // legend row: last row, >= 2 cells, every cell = one svg placeholder + text
        var legendCells = Cells(rows[^1]);
        if (legendCells.Count < 2) return null;
        foreach (var cell in legendCells)
        {
            var svg = SvgPlaceholder(cell);
            if (svg is null) return null;
            dt.Legend.Add((svg.Value.idx, DomText(cell, css)));
        }

        // main figure: the largest svg placeholder in the earlier rows
        for (var ri = 0; ri < rows.Count - 1; ri++)
        {
            var svg = SvgPlaceholder(rows[ri]);
            if (svg is not null && svg.Value.w > dt.MainSvgWPx)
            {
                dt.MainSvgIdx = svg.Value.idx;
                dt.MainSvgWPx = svg.Value.w;
                dt.MainSvgHPx = svg.Value.h;
            }
        }
        if (dt.MainSvgIdx < 0 || dt.MainSvgWPx <= 0) return null;

        // caption: first non-figure row's text; axis labels: the row of plain-text cells
        for (var ri = 0; ri < rows.Count - 1; ri++)
        {
            if (SvgPlaceholder(rows[ri]) is not null) continue;
            var cells = Cells(rows[ri]);
            var texts = new List<(string Text, int Col)>();
            for (var ci = 0; ci < cells.Count; ci++)
            {
                var t = DomText(cells[ci], css);
                if (t.Length > 0) texts.Add((t, ci));
            }
            if (texts.Count == 0) continue;
            if (dt.TitleText is null && texts.Count == 1 && ri == 0)
                dt.TitleText = texts[0].Text;
            else
                dt.MidLabels.AddRange(texts);
        }

        // The calibrated fractions describe the 3-legend auto-layout;
        // other shapes fall back to equal right-to-left columns.
        if (dt.Legend.Count != 3)
        {
            var n = dt.Legend.Count;
            dt.LegendXFrac = new double[n];
            dt.LegendWFrac = new double[n];
            dt.LegendLabelRightFrac = new double[n];
            dt.MidLabelRightFrac = new double[n];
            for (var i = 0; i < n; i++)
            {
                dt.LegendWFrac[i] = 1.0 / n;
                dt.LegendXFrac[i] = 1.0 - (i + 1.0) / n;
                dt.LegendLabelRightFrac[i] = 1.0 - (double)i / n - 0.01;
                dt.MidLabelRightFrac[i] = dt.LegendLabelRightFrac[i];
            }
        }

        return new Block { Text = "", Diagram = dt };
    }

    /// <summary>Build an RTL topics-table block: a visible table whose row pairs a
    /// cell holding ONLY an inline-svg placeholder (the matrix figure) with a cell
    /// holding a heading caption and a &lt;ul&gt; of topic items. Returns null when
    /// the table doesn't match (it then flows through the normal table pipeline).</summary>
    private static Block? BuildRtlTopicsTableBlock(HtmlNode table,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        // Pre-declaration templates carry a collapsed inline box (height:0) — skip.
        if (table.Attrs is not null && table.Attrs.TryGetValue("style", out var tst)
            && Regex.IsMatch(tst, @"height\s*:\s*0"))
            return null;

        HtmlNode? svgCell = null, listCell = null;
        var dt = new RtlTopicsTable();
        foreach (var d in table.Descendants())
        {
            if (d.Tag is not ("td" or "th") || IsHiddenElement(d.Tag, d.Attrs, css)) continue;
            var hasUl = false;
            var cellSvgIdx = -1; double cellSvgW = 0, cellSvgH = 0;
            foreach (var c in d.Descendants())
            {
                if (c.Tag == "img" && c.Attrs is not null
                    && c.Attrs.TryGetValue("src", out var src)
                    && src.StartsWith("inline-svg:", StringComparison.Ordinal))
                {
                    if (int.TryParse(src.Substring("inline-svg:".Length), out var idx))
                    {
                        cellSvgIdx = idx;
                        if (c.Attrs.TryGetValue("width", out var ws))
                            double.TryParse(ws, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out cellSvgW);
                        if (c.Attrs.TryGetValue("height", out var hs))
                            double.TryParse(hs, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out cellSvgH);
                    }
                }
                else if (c.Tag == "ul") hasUl = true;
            }
            if (cellSvgIdx >= 0 && !hasUl && svgCell is null && DomText(d, css).Length == 0)
            {
                svgCell = d;
                dt.SvgIdx = cellSvgIdx; dt.SvgWPx = cellSvgW; dt.SvgHPx = cellSvgH;
            }
            else if (hasUl && cellSvgIdx < 0 && listCell is null) listCell = d;
        }
        if (svgCell is null || listCell is null || dt.SvgIdx < 0 || dt.SvgWPx <= 0) return null;

        foreach (var c in listCell.Descendants())
        {
            if (c.Tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" && dt.CaptionText is null)
            {
                var t = DomText(c, css);
                if (t.Length > 0) dt.CaptionText = t;
            }
            else if (c.Tag == "li")
            {
                var t = DomText(c, css);
                if (t.Length > 0) dt.Items.Add(t);
            }
        }
        if (dt.Items.Count < 2) return null;

        return new Block { Text = "", TopicsList = dt };
    }

    /// <summary>Build a positioned-card block from a host div holding a
    /// position:relative media box (with absolute bottom-anchored caption bars)
    /// and float prose/info columns. Returns null when the shape doesn't match —
    /// the host then flows through the normal pipeline.</summary>
    private static Block? BuildPositionedCardBlock(HtmlNode host, HtmlNode media,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var card = new PositionedCard
        {
            MediaWPx = ParsePxValue(DomDecl(media, "width", css)),
            MediaHPx = ParsePxValue(DomDecl(media, "height", css)),
            ContainerHPx = ParsePxValue(DomDecl(host, "height", css)),
        };
        if (card.MediaWPx <= 0 || card.MediaHPx <= 0) return null;

        // rgba() fills paint OPAQUE (a black 0.8-alpha
        // address bar renders pure black) — parse the rgb triple, drop the alpha.
        static Color? CardColor(string? decl)
        {
            if (string.IsNullOrEmpty(decl)) return null;
            var m = Regex.Match(decl, @"rgba?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");
            if (m.Success)
                return Color.FromRgb(int.Parse(m.Groups[1].Value),
                    int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            return ParseCssColor(decl);
        }

        foreach (var c in media.Descendants())
        {
            if (c.Tag == "img") card.HasImg = true;
            if (c.Tag != "div") continue;
            if (DomDecl(c, "position", css)?.Trim()
                    .Equals("absolute", StringComparison.OrdinalIgnoreCase) != true) continue;
            var barH = ParsePxValue(DomDecl(c, "height", css));
            var barFill = CardColor(DomDecl(c, "background-color", css)
                                    ?? DomDecl(c, "background", css));
            if (barH <= 0 || barFill is null) return null;
            var barBottom = ParsePxValue(DomDecl(c, "bottom", css));
            var barText = CardColor(DomDecl(c, "color", css)) ?? Color.FromRgb(0, 0, 0);
            card.Bars.Add((barH, barBottom, barFill, barText, DomText(c, css)));
        }
        if (card.Bars.Count == 0 || !card.HasImg) return null;

        HtmlNode? prose = null, info = null;
        foreach (var d in host.Children)
        {
            if (d.Tag != "div" || d == media) continue;
            var fl = DomDecl(d, "float", css)?.Trim().ToLowerInvariant();
            if (fl == "left" && prose is null) prose = d;
            else if (fl == "right" && info is null) info = d;
        }
        if (prose is null || info is null) return null;

        card.TextWPx = ParsePxValue(DomDecl(prose, "width", css));
        card.TextHPx = ParsePxValue(DomDecl(prose, "height", css));
        card.ParaText = DomText(prose, css);
        if (card.TextWPx <= 0 || card.ParaText.Length == 0) return null;

        card.InfoWPx = ParsePxValue(DomDecl(info, "width", css));
        card.InfoMtPx = ParsePxValue(DomDecl(info, "margin-top", css));
        if (card.InfoWPx <= 0) return null;

        // the info panel's two float columns: label paragraphs left, value
        // paragraphs right
        HtmlNode? labCol = null, valCol = null;
        foreach (var d in info.Children)
        {
            if (d.Tag != "div") continue;
            var fl = DomDecl(d, "float", css)?.Trim().ToLowerInvariant();
            if (fl == "left" && labCol is null) labCol = d;
            else if (fl == "right" && valCol is null) valCol = d;
        }
        if (labCol is null || valCol is null) return null;

        static void CollectPs(HtmlNode col, IReadOnlyDictionary<string, Dictionary<string, string>>? css,
            List<(string Text, bool Bold, double MtPx, int Kind)> into)
        {
            foreach (var p in col.Descendants())
            {
                if (p.Tag != "p") continue;
                var text = DomText(p, css);
                var bold = p.Parent?.Tag == "b";
                if (!bold)
                    foreach (var pc in p.Children)
                        if (pc.Tag == "b" && DomText(pc, css).Length > 0) { bold = true; break; }
                var mt = ParsePxValue(DomDecl(p, "margin-top", css));
                var kind = text.Length > 0 ? 0
                    : p.Children.Any(pc => pc.Tag.Length > 0) ? 2 : 1;
                into.Add((text, bold, mt, kind));
            }
        }
        CollectPs(labCol, css, card.Labels);
        CollectPs(valCol, css, card.Values);
        if (card.Labels.Count < 2 || card.Values.Count < 2) return null;

        return new Block { Text = "", Card = card };
    }

    /// <summary>Build a flex-grid block from the waybill container: each
    /// display:flex row's percent-width column divs become cells — a dt/dd
    /// label-value pair, plain wrapping text, or the signature composite (a dt
    /// with a float:right span plus a padded full-width dd of float halves).</summary>
    private static Block? BuildFlexGridBlock(HtmlNode host,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var fg = new FlexGrid();
        foreach (var h in host.Descendants())
            if (h.Tag == "h1") { fg.Title = DomText(h, css); break; }

        static double PctFrac(string? v)
        {
            if (v is null) return 0;
            var m = Regex.Match(v, @"([\d.]+)\s*%");
            return m.Success && double.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p) ? p / 100.0 : 0;
        }

        foreach (var row in host.Descendants())
        {
            if (row.Tag is not ("div" or "tr") || DomDecl(row, "display", css)?.Trim()
                    .Equals("flex", StringComparison.OrdinalIgnoreCase) != true) continue;
            var fr = new FlexGridRow();
            if (row.Tag == "tr") fg.TableFlavor = true;
            foreach (var cell in row.Children)
            {
                if (cell.Tag is not ("div" or "td")) continue;
                var fc = new FlexGridCell
                {
                    WFrac = PctFrac(DomDecl(cell, "width", css)),
                    PadFrac = PctFrac(DomDecl(cell, "padding-left", css)),
                    Center = DomDecl(cell, "text-align", css)?.Contains("center",
                        StringComparison.OrdinalIgnoreCase) == true,
                };
                if (fc.WFrac <= 0) continue;
                static bool Side(HtmlNode n, string side,
                    IReadOnlyDictionary<string, Dictionary<string, string>>? css2)
                {
                    var v = DomDecl(n, side, css2);
                    return v is not null && !v.Contains("none", StringComparison.OrdinalIgnoreCase)
                           && !v.TrimStart().StartsWith("0", StringComparison.Ordinal);
                }
                fc.BL = Side(cell, "border-left", css);
                fc.BR = Side(cell, "border-right", css);
                fc.BT = Side(cell, "border-top", css);
                fc.BB = Side(cell, "border-bottom", css);
                HtmlNode? dl = null;
                foreach (var c in cell.Children) if (c.Tag == "dl") { dl = c; break; }
                if (dl is null)
                {
                    fc.PlainWrap = true;
                    fc.Label = DomText(cell, css);
                }
                else
                {
                    foreach (var c in dl.Children)
                    {
                        if (c.Tag == "dt")
                        {
                            var lbl = new StringBuilder();
                            foreach (var t in c.Children)
                            {
                                if (t.Tag.Length == 0) lbl.Append(DecodeEntities(t.Text));
                                else if (t.Tag == "span"
                                    && DomDecl(t, "float", css)?.Trim()
                                        .Equals("right", StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    fc.LabelRight = DomText(t, css);
                                    fc.LabelRightMrFrac = PctFrac(DomDecl(t, "margin-right", css));
                                }
                                else lbl.Append(DomText(t, css));
                            }
                            fc.Label = CollapseWs(lbl.ToString()).Trim();
                        }
                        else if (c.Tag == "dd")
                        {
                            fc.HasDd = true;
                            if (DomDecl(c, "width", css)?.Trim() == "100%")
                            {
                                fc.ValueWide = true;
                                var pm = Regex.Match(DomDecl(c, "padding", css) ?? "",
                                    @"([\d.]+)\s*px");
                                if (pm.Success && double.TryParse(pm.Groups[1].Value,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var vpp)) fc.ValuePadPx = vpp;
                                foreach (var t in c.Children)
                                {
                                    if (t.Tag != "span") continue;
                                    var fl = DomDecl(t, "float", css)?.Trim();
                                    if (fl?.Equals("left", StringComparison.OrdinalIgnoreCase) == true)
                                        fc.ValueLeft = DomText(t, css);
                                    else if (fl?.Equals("right", StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        fc.ValueRight = DomText(t, css);
                                        fc.ValueRightMrFrac = PctFrac(DomDecl(t, "margin-right", css));
                                    }
                                }
                            }
                            else fc.Value = DomText(c, css);
                        }
                    }
                }
                fr.Cells.Add(fc);
            }
            if (fr.Cells.Count > 0) fg.Rows.Add(fr);
        }
        return fg.Rows.Count >= 4 ? new Block { Text = "", Flex = fg } : null;
    }

    /// <summary>Build a positioned-slide block: each absolutely positioned child of
    /// the relative canvas becomes one item — a background-image box (declared px
    /// size, stretch when background-size:100% is set, else centre-cropped, and any
    /// CSS rotation) or a free text run. Hidden subtrees (the editor's chrome)
    /// contribute nothing.</summary>
    private static Block? BuildPositionedSlideBlock(HtmlNode slide, double minWPx, double minHPx,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var sl = new PositionedSlide { MinWPx = minWPx, MinHPx = minHPx };
        foreach (var ch in slide.Children)
        {
            if (ch.Tag != "div") continue;
            if (IsHiddenElement(ch.Tag, ch.Attrs, css)) continue;
            if (DomDecl(ch, "position", css)?.Trim()
                    .Equals("absolute", StringComparison.OrdinalIgnoreCase) != true) continue;
            var itLeft = ParsePxValue(DomDecl(ch, "left", css));
            var itTop = ParsePxValue(DomDecl(ch, "top", css));
            SlideItem? item = null;
            foreach (var d in ch.Descendants())
            {
                if (d.Tag != "div") continue;
                var hidden = false;
                for (var p = d; p is not null && p != ch; p = p.Parent)
                    if (p.Tag.Length > 0 && IsHiddenElement(p.Tag, p.Attrs, css)) { hidden = true; break; }
                if (hidden) continue;
                // The url lives in the RAW style attribute, entity-encoded
                // (url(&quot;26.jpg&quot;)): the entities' own semicolons truncate a
                // per-declaration split, so decode the whole attribute FIRST and
                // match the url expression on the decoded text.
                var rawStyle = d.Attrs is not null && d.Attrs.TryGetValue("style", out var rs) ? rs : null;
                if (string.IsNullOrEmpty(rawStyle)) continue;
                var um = Regex.Match(DecodeEntities(rawStyle),
                    @"background(?:-image)?\s*:\s*[^;]*?url\(\s*[""']?([^""')]+?)[""']?\s*\)",
                    RegexOptions.IgnoreCase);
                if (!um.Success) continue;
                var iw = ParsePxValue(DomDecl(d, "width", css));
                var ih = ParsePxValue(DomDecl(d, "height", css));
                if (iw <= 0 || ih <= 0) continue;
                double rot = 0;
                if (DomDecl(d, "transform", css) is { } tr
                    && Regex.Match(tr, @"rotate\(\s*(-?[\d.]+)\s*deg", RegexOptions.IgnoreCase)
                        is { Success: true } rm)
                    double.TryParse(rm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out rot);
                var bsz = DomDecl(d, "background-size", css) ?? "";
                item = new SlideItem
                {
                    IsImage = true,
                    Src = um.Groups[1].Value.Trim(),
                    LeftPx = itLeft, TopPx = itTop, WPx = iw, HPx = ih, RotDeg = rot,
                    Stretch = bsz.Contains('%'),
                };
                break;
            }
            if (item is null)
            {
                var text = DomText(ch, css);
                if (text.Length == 0) continue;
                item = new SlideItem { LeftPx = itLeft, TopPx = itTop, Text = text };
            }
            sl.Items.Add(item);
        }
        return sl.Items.Count > 0 ? new Block { Text = "", Slide = sl } : null;
    }

    /// <summary>Build a centered search-form block from a &lt;form&gt; with a text
    /// input and submit buttons. All geometry comes from the stylesheet: the centered
    /// cell takes the WIDEST width among the input's class rules, the input's outer
    /// width its narrowest class width + the inline padding shorthand + borders +
    /// the wrapper's margin-left, buttons their container height/background and
    /// button font, the side link its own color/size/margins.</summary>
    private static Block? BuildSearchFormBlock(HtmlNode form,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        HtmlNode? textInput = null;
        var buttons = new List<(string Label, string Name)>();
        HtmlNode? icon = null;
        HtmlNode? link = null;
        foreach (var d in form.Descendants())
        {
            if (d.Tag.Length == 0) continue;
            if (IsHiddenElement(d.Tag, d.Attrs, css)) continue;
            if (d.Tag == "input")
            {
                string? type = null;
                d.Attrs?.TryGetValue("type", out type);
                type = string.IsNullOrEmpty(type) ? "text" : type.ToLowerInvariant();
                if (type is "text" or "search" && textInput is null) textInput = d;
                else if (type == "submit")
                {
                    string? v = null, nm = null;
                    d.Attrs?.TryGetValue("value", out v);
                    d.Attrs?.TryGetValue("name", out nm);
                    if (!string.IsNullOrEmpty(v)) buttons.Add((DecodeEntities(v!), nm ?? ""));
                }
            }
            else if (d.Tag == "img" && icon is null && textInput is not null
                     && d.Attrs is not null && d.Attrs.TryGetValue("style", out var ist)
                     && ist.Contains("absolute", StringComparison.OrdinalIgnoreCase))
                icon = d;
            else if (d.Tag == "a" && link is null && textInput is not null
                     && d.Attrs?.ContainsKey("href") == true)
                link = d;
        }
        if (textInput is null || buttons.Count == 0) return null;

        var sf = new SearchForm { Buttons = buttons };
        string? inputName = null;
        textInput.Attrs?.TryGetValue("name", out inputName);
        sf.InputName = inputName;

        // input class widths: widest = the centered cell, plus the input's own box
        double cellW = 0, inputContentW = 0;
        double inputH = 25;
        if (textInput.Attrs is not null && textInput.Attrs.TryGetValue("class", out var icls)
            && css is not null)
        {
            foreach (var c in icls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!css.TryGetValue("." + c, out var decls)) continue;
                if (decls.TryGetValue("width", out var wv))
                {
                    var wpx = ParsePxValue(wv);
                    if (wpx > cellW) cellW = wpx;
                    if (wpx > 0 && (inputContentW == 0 || wpx < inputContentW)) inputContentW = wpx;
                }
                if (decls.TryGetValue("height", out var hv))
                {
                    var hpx = ParsePxValue(hv);
                    if (hpx > 0) inputH = hpx;
                }
            }
        }
        if (cellW <= 0) cellW = 496;
        if (inputContentW <= 0) inputContentW = cellW;
        // padding: the inline shorthand only (the
        // overlay-icon inset lays out of the box rather than the padding-right override)
        double padL = 6, padR = 8;
        if (textInput.Attrs is not null && textInput.Attrs.TryGetValue("style", out var istyle))
        {
            var pm = Regex.Match(istyle, @"(?<![\w-])padding\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (pm.Success)
            {
                var parts = pm.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (parts.Length)
                {
                    case 1: padL = padR = ParsePxValue(parts[0]); break;
                    case 2: case 3: padL = padR = ParsePxValue(parts[1]); break;
                    case 4: padR = ParsePxValue(parts[1]); padL = ParsePxValue(parts[3]); break;
                }
            }
        }
        var wrapMargin = 4.0; // .ds margin-left on the input wrapper
        if (textInput.Parent is not null)
        {
            var (wl, _) = DomBoxLR(textInput.Parent.Parent ?? textInput.Parent, "margin", css);
            if (wl > 0) wrapMargin = wl;
        }
        sf.CellWidthPx = cellW;
        sf.InputContentPx = inputContentW;
        sf.InputWidthPx = inputContentW + padL + padR + 2 + wrapMargin;
        sf.InputHeightPx = inputH + 2;

        if (icon is not null && icon.Attrs is not null)
        {
            icon.Attrs.TryGetValue("src", out var isrc);
            sf.IconSrc = isrc;
            if (icon.Attrs.TryGetValue("width", out var iw)) sf.IconWPx = ParsePxValue(iw + "px");
            if (icon.Attrs.TryGetValue("height", out var ih)) sf.IconHPx = ParsePxValue(ih + "px");
            if (icon.Attrs.TryGetValue("style", out var isty))
            {
                var rm = Regex.Match(isty, @"right\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                if (rm.Success) sf.IconRightPx = double.Parse(rm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var tm = Regex.Match(isty, @"top\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
                if (tm.Success) sf.IconTopPx = double.Parse(tm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // button box: wrapper height + bg, label font from the button class
        if (buttons.Count > 0 && css is not null)
        {
            foreach (var kv in css)
            {
                if (kv.Key is ".lsbb")
                {
                    if (kv.Value.TryGetValue("height", out var bh))
                    {
                        var v = ParsePxValue(bh);
                        if (v > 0) sf.ButtonHeightPx = v + 2;
                    }
                    if (kv.Value.TryGetValue("background", out var bg))
                    {
                        var c = ParseCssColor(bg);
                        if (c is not null) sf.ButtonBg = c;
                    }
                }
                else if (kv.Key is ".lsb")
                {
                    if (kv.Value.TryGetValue("font", out var bf))
                    {
                        var m = Regex.Match(bf, @"([\d.]+)\s*px");
                        if (m.Success) sf.ButtonFontPx = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    if (kv.Value.TryGetValue("color", out var bc))
                    {
                        var c = ParseCssColor(bc);
                        if (c is not null) sf.ButtonFg = c;
                    }
                }
            }
        }

        if (link is not null)
        {
            sf.LinkText = DomText(link, css);
            link.Attrs!.TryGetValue("href", out var href);
            sf.LinkUrl = href;
            var lc = DomColor(link, css);
            if (lc is not null) sf.LinkColor = lc;
            sf.LinkFontPx = DomFontPx(link, 11, css);
            // The side cell sits flush against the centered cell;
            // the link's own margin-left is not part of the rendered offset.
            sf.LinkMarginLeftPx = 0;
        }

        var (_, formMb) = DomBoxTB(form, "margin", css);
        if (formMb > 0) sf.MarginBottomPx = formMb;

        return new Block { Text = "", Form = sf };
    }

    /// <summary>Build a centered inline-link row: text runs and link runs in child
    /// order, spacing from each child's CSS margins, colors resolved per element.</summary>
    private static Block? BuildCenteredLinkRow(HtmlNode el,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var fontPx = DomFontPx(el, 16, css);
        var baseColor = DomColor(el, css) ?? Color.FromRgb(0, 0, 0);
        var runs = new List<RowRun>();
        foreach (var c in el.Children)
        {
            if (c.Tag.Length == 0)
            {
                // Collapse without trimming: a "&copy; 2025 - " text node keeps its
                // boundary spaces so adjacent link runs don't fuse with it.
                var t = Regex.Replace(DecodeEntities(c.Text), @"[ \t\r\n\f]+", " ");
                if (t.Trim().Length == 0) continue;
                runs.Add(new RowRun { Text = t, FontPx = fontPx, Color = baseColor, Bold = DomBold(el, css) });
                continue;
            }
            if (IsHiddenElement(c.Tag, c.Attrs, css)) continue;
            var text = DomText(c, css);
            if (text.Length == 0) continue;
            var (ml, mr) = DomBoxLR(c, "margin", css);
            string? url = null;
            if (c.Tag == "a" && c.Attrs is not null && c.Attrs.TryGetValue("href", out var h)) url = h;
            runs.Add(new RowRun
            {
                Text = text,
                FontPx = DomFontPx(c, fontPx, css),
                Bold = DomBold(c, css),
                Color = DomColor(c, css) ?? baseColor,
                MarginLeftPx = ml,
                MarginRightPx = mr,
                Url = url,
            });
        }
        if (runs.Count == 0) return null;
        var (marginT, _) = DomBoxTB(el, "margin", css);
        var (_, marginB) = DomBoxTB(el, "margin", css);
        // UA default: <p> carries a 1em top/bottom margin when none is declared.
        if (el.Tag == "p" && marginT == 0 && marginB == 0 && string.IsNullOrEmpty(DomDecl(el, "margin", css)))
            marginT = marginB = fontPx;
        return new Block
        {
            Text = "",
            RowRuns = runs,
            RowCentered = true,
            RowFontPx = fontPx,
            RowHeightPx = fontPx * 1.35,
            RowMarginTopPx = marginT,
            RowMarginBottomPx = marginB,
        };
    }

    /// <summary>The installed face an stl_ CSS font-family stack resolves to for
    /// measurement: first family of the stack, quotes and any "ABCDEF+" subset tag
    /// stripped; null when no installed font matches (extent pinning stays off).
    /// Shared with PdfToHtmlConverter so the save-side pin and the re-import measure
    /// with the same metrics.</summary>
    internal static string? ResolveStlFace(string familyStack)
    {
        if (string.IsNullOrEmpty(familyStack)) return null;
        var fam = familyStack.Split(',')[0].Trim().Trim('"', '\'').Trim();
        fam = Regex.Replace(fam, @"^[A-Z]{6}\+", "");
        return fam.Length > 0 && PosFace(fam).parser is not null ? fam : null;
    }

    /// <summary>See <see cref="MeasureFaceText"/> — exposed for the save-side extent pin.</summary>
    internal static double MeasureStlNaturalText(string faceName, string s, double fontSizePt)
        => MeasureFaceText(faceName, s, fontSizePt);

    /// <summary>One character's advance in the named face at full font-unit
    /// precision, in milli-em (units/upm×1000). Characters the face cannot map
    /// fall back to the Times New Roman metric (the CSS fallback face a viewer
    /// would substitute); half an em when even that fails.</summary>
    internal static double StlCharAdvanceMilli(string faceName, int cp)
    {
        if (cp == 0x00A0) cp = 0x20;
        var face = PosFace(faceName);
        var gid = face.parser is not null && face.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
        if (face.parser is not null && gid != 0)
            return face.parser.GetAdvanceWidth(gid) * 1000.0 / face.upm;
        return StlFallbackAdvanceMilli(cp);
    }

    /// <summary>The CSS fallback face's (Times New Roman) advance for one
    /// character, milli-em. A character Times cannot map falls back to the
    /// ideograph rule — a CJK glyph advances a FULL em in every real CJK face
    /// (and in this measurement model); the half-em guess is only for unmapped
    /// non-ideographs.</summary>
    internal static double StlFallbackAdvanceMilli(int cp)
    {
        if (cp == 0x00A0) cp = 0x20;
        var fb = PosFace("Times New Roman");
        var gid = fb.parser is not null && fb.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
        if (fb.parser is not null && gid != 0)
            return fb.parser.GetAdvanceWidth(gid) * 1000.0 / fb.upm;
        return StlIdeograph(cp) ? 1000.0 : 500.0;
    }

    /// <summary>A full-em CJK character: ideographs, kana/radicals, compatibility
    /// ideographs, fullwidth forms and the ideographic space. Also the IE-model
    /// word boundary — the em-compensation dialect charges word-spacing between
    /// two adjacent such characters exactly as at a drawn space.</summary>
    internal static bool StlIdeograph(int cp) =>
        (cp >= 0x2E80 && cp <= 0x9FFF)
        || (cp >= 0xF900 && cp <= 0xFAFF)
        || (cp >= 0xFF01 && cp <= 0xFF60)
        || cp == 0x3000;

    /// <summary>Text advance in the stl_ measurement model:
    /// per-glyph advances at full font-unit precision (units/upm, NOT the 1000-grid
    /// rounding of <see cref="MeasureFaceText"/>) evaluated at the FLOOR-3-DECIMALS
    /// quantized font size — the resolved CSS size quantizes to 0.001pt
    /// toward zero before measuring, while letter/word-spacing stay at the raw size.
    /// Both the save-side extent pin and the stl_ re-import measure with this so the
    /// letter-spacing classes and the reconstructed page width agree.</summary>
    internal static double MeasureStlExactText(string faceName, string s, double rawFontSizePt)
    {
        var face = PosFace(faceName);
        return MeasureParsedExact(face.parser, face.upm, s, rawFontSizePt);
    }

    /// <summary>The exact-model core of <see cref="MeasureStlExactText"/> against an
    /// already-parsed font program (installed face or a document @font-face).</summary>
    private static double MeasureParsedExact(Text.GlyphOutlineParser? parser, double upm,
        string s, double rawFontSizePt)
    {
        var fsEff = Math.Floor(rawFontSizePt * 1000.0) / 1000.0;
        double w = 0;
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            if (cp == 0x00A0) cp = 0x20; // nbsp measures as the space glyph
            var gid = parser is not null && parser.CMap.TryGetValue(cp, out var g) ? g : 0;
            w += parser is null || gid == 0
                ? UnmappedAdvance(cp, fsEff)
                : parser.GetAdvanceWidth(gid) * fsEff / upm;
        }
        return w;
    }

    /// <summary>Half an em — the last-resort advance for a codepoint no face on the
    /// machine can map.</summary>
    private const double UnmappedAdvanceEm = 0.5;

    /// <summary>The advance a codepoint the run's own face cannot map takes: the metric
    /// of the SUBSTITUTE face the draw path picks for it, so the measured extent and the
    /// drawn extent agree. A half-em guess is half an ideograph's full-width advance,
    /// which is what left a CJK page's widest line — and with it the reflow sheet's
    /// width — materially under its own drawn ink.</summary>
    private static double UnmappedAdvance(int cp, double fsEff)
    {
        var sub = PosFace(PosFaceNameFor(cp));
        if (sub.parser is not null && sub.parser.CMap.TryGetValue(cp, out var sgid) && sgid != 0)
            return sub.parser.GetAdvanceWidth(sgid) * fsEff / sub.upm;
        return UnmappedAdvanceEm * fsEff;
    }

    /// <summary>Text advance in a named face (via the PosFace cache), using the same
    /// rounded 1000-unit advances the embedded font declares. Unknown faces/glyphs
    /// fall back to a half-em estimate.</summary>
    private static double MeasureFaceText(string faceName, string s, double fontSizePt)
    {
        var face = PosFace(faceName);
        double w = 0;
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            // an nbsp missing from the cmap advances as the space glyph
            if (cp == 0xA0 && face.parser is not null && !face.parser.CMap.ContainsKey(cp))
                cp = ' ';
            var gid = face.parser is not null && face.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
            w += face.parser is null || gid == 0
                ? 0.5 * fontSizePt
                : Math.Round(face.parser.GetAdvanceWidth(gid) * 1000.0 / face.upm) * fontSizePt / 1000.0;
        }
        return w;
    }

    // ── CSS-faithful metric flow helpers ────────────────────────────────────────
    // Line model: a line box is
    // round(sizePx · (winAscent+winDescent)/em) px tall, and the baseline sits at
    // halfLeading + ascent below the box top, halfLeading = (box − size·(wa+wd)/em)/2.

    /// <summary>Parse a CSS `margin: a [b [c [d]]]` shorthand into a pt box
    /// (top/right/bottom/left, px at 0.75 pt/px). False when any component fails.</summary>
    private static bool TryParseCssMarginBox(string value,
        out (double top, double right, double bottom, double left) box)
    {
        box = default;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4) return false;
        var v = new double[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "0") { v[i] = 0; continue; }
            if (!TryParseLength(parts[i], out v[i])) return false;
        }
        box = parts.Length switch
        {
            1 => (v[0], v[0], v[0], v[0]),
            2 => (v[0], v[1], v[0], v[1]),
            3 => (v[0], v[1], v[2], v[1]),
            _ => (v[0], v[1], v[2], v[3]),
        };
        return true;
    }

    private static readonly Dictionary<string, (double asc, double sum)?> _winMetricsCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>OS/2 usWinAscent and usWinAscent+usWinDescent as fractions of em for a
    /// resolvable face; null when the family or its metrics are unavailable.</summary>
    private static (double asc, double sum)? WinMetricsFor(string family)
    {
        if (string.IsNullOrEmpty(family)) return null;
        if (_winMetricsCache.TryGetValue(family, out var cached)) return cached;
        (double, double)? m = null;
        try
        {
            var ttf = Text.FontRepository.GetTtfData(family);
            if (ttf is not null)
            {
                var tp = new Text.TrueTypeParser(ttf);
                tp.Parse();
                if (tp.UsWinAscent > 0 && tp.UnitsPerEm > 0)
                    m = ((double)tp.UsWinAscent / tp.UnitsPerEm,
                         (double)(tp.UsWinAscent + tp.UsWinDescent) / tp.UnitsPerEm);
            }
        }
        catch { /* face without usable metrics: stay on the legacy model */ }
        _winMetricsCache[family] = m;
        return m;
    }

    /// <summary>Margin box (pt) from an inline style declaration — the `margin`
    /// shorthand first, then longhands override — with em lengths resolved against
    /// <paramref name="emPt"/> (an inline body margin's em is the body's own font
    /// size, not the converter's 11 pt default that TryParseLength assumes).</summary>
    private static (double top, double right, double bottom, double left) ParseInlineMarginBox(
        string decl, double emPt)
    {
        double Len(string v)
        {
            v = v.Trim();
            var em = Regex.Match(v, @"^(-?(?:\d+(?:\.\d+)?|\.\d+))\s*em$", RegexOptions.IgnoreCase);
            if (em.Success)
                return double.Parse(em.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) * emPt;
            return v == "0" ? 0 : TryParseLength(v, out var pt) ? pt : 0;
        }
        double top = 0, right = 0, bottom = 0, left = 0;
        var sh = Regex.Match(decl, @"(?<![-\w])margin\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (sh.Success)
        {
            var parts = sh.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is >= 1 and <= 4)
            {
                var v = new double[parts.Length];
                for (var i = 0; i < parts.Length; i++) v[i] = Len(parts[i]);
                (top, right, bottom, left) = parts.Length switch
                {
                    1 => (v[0], v[0], v[0], v[0]),
                    2 => (v[0], v[1], v[0], v[1]),
                    3 => (v[0], v[1], v[2], v[1]),
                    _ => (v[0], v[1], v[2], v[3]),
                };
            }
        }
        foreach (var (name, set) in new (string, Action<double>)[]
                 {
                     ("margin-top", x => top = x), ("margin-right", x => right = x),
                     ("margin-bottom", x => bottom = x), ("margin-left", x => left = x),
                 })
        {
            var m = Regex.Match(decl, @"(?<![-\w])" + name + @"\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase);
            if (m.Success) set(Len(m.Groups[1].Value));
        }
        return (top, right, bottom, left);
    }

    private static readonly Dictionary<string, double?> _xHeightCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>OS/2 sxHeight as a fraction of em (Arial: 1062/2048 = 0.5186) — the
    /// x-height CSS vertical-align:middle centres against. Null when the face or
    /// the field is unavailable.</summary>
    private static double? XHeightFor(string family)
    {
        if (string.IsNullOrEmpty(family)) return null;
        if (_xHeightCache.TryGetValue(family, out var cached)) return cached;
        double? m = null;
        try
        {
            var ttf = Text.FontRepository.GetTtfData(family);
            if (ttf is not null)
            {
                var tp = new Text.TrueTypeParser(ttf);
                tp.Parse();
                if (tp.SxHeight > 0 && tp.UnitsPerEm > 0)
                    m = tp.SxHeight / (double)tp.UnitsPerEm;
            }
        }
        catch { /* face without usable metrics */ }
        _xHeightCache[family] = m;
        return m;
    }

    private static readonly Dictionary<string, double?> _hheaLineSumCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>hhea (ascender − descender + lineGap) as a fraction of em — the
    /// browser's `line-height: normal` box for faces whose hhea metrics carry a
    /// line gap the win metrics don't (Times New Roman: 1.1499 vs 1.1074, i.e.
    /// 17px lines at 11pt where the win sum gives 16px). Null when the face or
    /// its metrics are unavailable.</summary>
    private static double? HheaLineSumFor(string family)
    {
        if (string.IsNullOrEmpty(family)) return null;
        if (_hheaLineSumCache.TryGetValue(family, out var cached)) return cached;
        double? m = null;
        try
        {
            var ttf = Text.FontRepository.GetTtfData(family);
            if (ttf is not null)
            {
                var tp = new Text.TrueTypeParser(ttf);
                tp.Parse();
                if (tp.Ascent > 0 && tp.UnitsPerEm > 0)
                    m = (tp.Ascent - tp.Descent + tp.LineGap) / (double)tp.UnitsPerEm;
            }
        }
        catch { /* face without usable metrics: stay on the win-metric model */ }
        _hheaLineSumCache[family] = m;
        return m;
    }

    /// <summary>Line-box height (pt) under the metric model.</summary>
    private static double MetricLineHeight(double sizePt, double metricSum)
        => Math.Round(sizePt / 0.75 * metricSum, MidpointRounding.AwayFromZero) * 0.75;

    /// <summary>Baseline offset below the line-box top under the metric model.</summary>
    private static double MetricBaselineDrop(double sizePt, double lineHeight, (double asc, double sum) m)
        => (lineHeight - sizePt * m.sum) / 2 + sizePt * m.asc;

    // Thai mark-stacking geometry, measured on the reference render (Tahoma 11 pt):
    // a tone mark over an above vowel seats 2.42 pt higher than the baseline run
    // (0.220 em) and a small nudge right of the pen (1.64 pt = 0.149 em).
    private const double ThaiToneRaiseEm = 2.42 / 11.0;
    private const double ThaiToneNudgeEm = 1.64 / 11.0;

    /// <summary>Split a line into runs for Thai mark stacking: each tone mark
    /// (U+0E48..U+0E4C) directly following an ABOVE vowel (U+0E31, U+0E34..U+0E37,
    /// U+0E47) becomes its own raised zero-advance chunk. Null when the line has
    /// no such pair — callers keep their single-run emit byte-for-byte.</summary>
    private static List<(string Text, bool Raised)>? SplitThaiStackedTones(string text)
    {
        static bool AboveVowel(char c) => c == 'ั' || (c >= 'ิ' && c <= 'ื') || c == '็';
        static bool ToneMark(char c) => c >= '่' && c <= '์';
        List<(string Text, bool Raised)>? chunks = null;
        var start = 0;
        for (var i = 1; i < text.Length; i++)
            if (ToneMark(text[i]) && AboveVowel(text[i - 1]))
            {
                chunks ??= new();
                if (i > start) chunks.Add((text[start..i], false));
                chunks.Add((text[i].ToString(), true));
                start = i + 1;
            }
        if (chunks is null) return null;
        if (start < text.Length) chunks.Add((text[start..], false));
        return chunks;
    }

    /// <summary>The dash-delimited unbreakable segments of a text: pieces bounded by
    /// spaces and by after-dash positions (a segment keeps its trailing dash). The
    /// widest of these is a line's min-content — the quirks CSS-run wrap limit.</summary>
    private static IEnumerable<string> DashSegments(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                if (i > start) yield return text[start..i];
                start = i + 1;
            }
            else if (text[i] == '-')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text[start..];
    }

    /// <summary>Greedy wrap on space and after-dash breakpoints with real face
    /// advances (the quirks CSS-run model): a line takes breakpoints while its
    /// text fits maxWidth, and a segment longer than maxWidth occupies its line
    /// whole (the limit is the document's widest segment, so only the defining
    /// segment ever hits this). Trailing whitespace left after the final
    /// breakpoint stays on the last line — a collapsed newline before a br
    /// survives as the fragment's trailing space.</summary>
    private static string[] DashAwareWordWrap(string text, double maxWidth, string face, double fontSize)
    {
        var lines = new List<string>();
        var n = text.Length;
        var start = 0;
        while (start < n)
        {
            var end = -1;          // best line end (exclusive)
            var nextStart = n;
            var scan = start;
            while (scan < n)
            {
                var sp = text.IndexOf(' ', scan);
                var da = text.IndexOf('-', scan);
                int cut, resume;
                if (sp < 0 && da < 0) { cut = n; resume = n; }
                else if (da < 0 || (sp >= 0 && sp < da)) { cut = sp; resume = sp + 1; }
                else { cut = da + 1; resume = da + 1; }
                var w = MeasureFaceText(face, text[start..cut].TrimEnd(' '), fontSize);
                if (w <= maxWidth + 1e-6 || end < 0)
                {
                    end = cut;
                    nextStart = resume;
                    if (w > maxWidth + 1e-6) break;   // over-long first segment, taken whole
                    scan = resume;
                    continue;
                }
                break;
            }
            if (end < 0) { end = n; nextStart = n; }
            // Only whitespace left past the final breakpoint: it belongs to this line.
            if (nextStart >= n && end < n && text[end..].Trim().Length == 0) end = n;
            lines.Add(text[start..end]);
            start = Math.Max(nextStart, end);
        }
        return lines.Count == 0 ? new[] { text } : lines.ToArray();
    }

    /// <summary>Greedy word wrap with REAL font advances (the metric flow's wrap — the
    /// legacy estimate breaks lines that should stay whole). Breaks at ordinary spaces only;
    /// non-breaking spaces bind their words.</summary>
    /// <summary>A CSS font-size value in points: absolute keywords at the UA's
    /// px mapping (small = 13px, medium = 16px, ...), the relative keywords
    /// against the UA 16px base, or any parseable length.</summary>
    private static bool TryParseCssFontSize(string v, out double pt)
    {
        pt = v.Trim().ToLowerInvariant() switch
        {
            "xx-small" => 9 * 0.75,
            "x-small" => 10 * 0.75,
            "small" => 13 * 0.75,
            "medium" => 16 * 0.75,
            "large" => 18 * 0.75,
            "x-large" => 24 * 0.75,
            "xx-large" => 32 * 0.75,
            "larger" => 19.2 * 0.75,      // 1.2 x the 16px UA base
            "smaller" => 13.33 * 0.75,
            _ => 0,
        };
        if (pt > 0) return true;
        return TryParseLength(v, out pt) && pt > 0;
    }

    private static string[] MeasuredWordWrap(string text, double maxWidth, string face, double sizePt)
    {
        // Hard breaks (a cell's <br>) split first; each segment wraps on its own.
        if (text.Contains('\u0001'))
        {
            var all = new List<string>();
            foreach (var seg in text.Split('\u0001'))
                all.AddRange(MeasuredWordWrap(seg.Trim(' '), maxWidth, face, sizePt));
            return all.Count == 0 ? [""] : all.ToArray();
        }
        if (string.IsNullOrEmpty(text)) return [""];
        if (MeasureFaceText(face, text, sizePt) <= maxWidth) return [text];
        // An all-whitespace run (an &nbsp; spacer chain) is ONE line box —
        // U+00A0 offers no break opportunity and blank ink never wraps.
        var allWs = true;
        foreach (var ch in text) if (ch is not (' ' or '\u00A0')) { allWs = false; break; }
        if (allWs) return [text];
        // Spaceless CJK runs and over-wide words break per character: pack
        // greedily to the width (the source engine char-splits long words
        // inside table cells).
        if (!text.Contains(' ') || MaxSpaceWordWidth(text, face, sizePt) > maxWidth)
        {
            var outLines = new List<string>();
            var ln = new StringBuilder();
            double lw = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var chw = MeasureFaceText(face, text[i].ToString(), sizePt);
                if (ln.Length > 0 && lw + chw > maxWidth)
                {
                    outLines.Add(ln.ToString());
                    ln.Clear();
                    lw = 0;
                    if (text[i] == ' ') continue;   // a break eats the space
                }
                ln.Append(text[i]);
                lw += chw;
            }
            if (ln.Length > 0) outLines.Add(ln.ToString());
            return outLines.Count == 0 ? [""] : outLines.ToArray();
        }
        var words = text.Split(' ');
        var spaceW = MeasureFaceText(face, " ", sizePt);
        var result = new List<string>();
        var line = new StringBuilder();
        double lineW = 0;
        foreach (var word in words)
        {
            var w = MeasureFaceText(face, word, sizePt);
            if (line.Length > 0 && lineW + spaceW + w > maxWidth)
            {
                result.Add(line.ToString());
                line.Clear(); lineW = 0;
            }
            if (line.Length > 0) { line.Append(' '); lineW += spaceW; }
            line.Append(word); lineW += w;
        }
        if (line.Length > 0) result.Add(line.ToString());
        return result.Count == 0 ? [""] : result.ToArray();
    }

    private static double MaxSpaceWordWidth(string text, string face, double sizePt)
    {
        double mx = 0;
        foreach (var w in text.Split(' '))
            mx = Math.Max(mx, MeasureFaceText(face, w, sizePt));
        return mx;
    }

    // Adjacent tables stacked in ONE wrapper cell sit this far apart
    // (measured: the register's section wrappers at 145.5 -> 146.7).
    private const double WrapperSiblingGapPt = 1.2;


    // The legacy <font size=1..7> ladder in px (0.75 pt/px). Standard browser
    // mapping except size 1 = 9px — measured on the references: size1 headers draw
    // 6.75 pt, size2 9.75 (13px), size3 12 (16px), size4 13.5 (18px), size5 18 (24px).
    private static readonly double[] HtmlFontSizeLadderPx = { 9, 13, 16, 18, 24, 32, 48 };

    /// <summary>CSS `font-size: larger`: 1.2 x the current computed size, no
    /// rounding (measured on the newsletter title: 13px body → 15.6px = 11.7 pt,
    /// whose line box still px-rounds to 18px).</summary>
    private static double HtmlLargerStepPt(double pt) => pt * 1.2;

    /// <summary>Parse a legacy font size attribute ("2", "+1", "-1") to points.
    /// A signed value is relative to the default size 3.</summary>
    private static bool TryParseHtmlFontSize(string raw, out double pt)
    {
        pt = 0;
        raw = raw.Trim();
        if (raw.Length == 0) return false;
        var rel = raw[0] is '+' or '-';
        if (!int.TryParse(raw, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return false;
        var idx = Math.Clamp(rel ? 3 + n : n, 1, 7);
        pt = HtmlFontSizeLadderPx[idx - 1] * 0.75;
        return true;
    }

    /// <summary>Detect the legacy WRAPPER-TABLE idiom: a table whose every row is
    /// a single td holding only nested tables (whitespace/tbody chrome aside).
    /// Yields the wrapper tag's attribute text and the child tables in order.</summary>
    /// <summary>A legacy color ATTRIBUTE value: like CSS, but a bare 6-digit hex
    /// ("CCCCCC") counts — the browsers' error-tolerant attribute parser.</summary>
    private static Color? AttrColor(string v)
    {
        v = v.Trim();
        var c = ParseCssColor(v);
        if (c is null && Regex.IsMatch(v, @"^[0-9a-fA-F]{6}$")) c = ParseCssColor("#" + v);
        return c;
    }

    /// <summary>Cut every table nested INSIDE the top-level table out of
    /// <paramref name="tableHtml"/>, leaving a \u0002{index}\u0003 marker where
    /// each stood; the extracted HTML goes to <paramref name="subTables"/> in
    /// marker order. The cell that carries a marker renders that table as its
    /// own grid inside the cell.</summary>
    private static string ExtractNestedTables(string tableHtml, out List<string> subTables)
    {
        subTables = new List<string>();
        var sb = new StringBuilder(tableHtml.Length);
        var pos = 0; var depth = 0;
        foreach (Match t in Regex.Matches(tableHtml, @"<(/?)table\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var closing = t.Groups[1].Value.Length > 0;
            if (!closing)
            {
                depth++;
                if (depth == 2)
                {
                    sb.Append(tableHtml[pos..t.Index]);
                    sb.Append('\u0002').Append(subTables.Count).Append('\u0003');
                    pos = t.Index;              // start of the nested table
                }
            }
            else
            {
                if (depth == 2)
                {
                    subTables.Add(tableHtml[pos..(t.Index + t.Length)]);
                    pos = t.Index + t.Length;
                }
                depth--;
            }
        }
        sb.Append(tableHtml[pos..]);
        return sb.ToString();
    }

    /// <summary>Rough height of a nested table: its own rows at the given
    /// pitch plus its nested tables', recursively. Wrapped cell text is not
    /// modelled; the caller reserves at least this much row height.</summary>
    /// <summary>Emit one metric-cell line. Ideographs go out as SEPARATE runs at
    /// their cumulative advances — the source renderer segments CJK shaping runs
    /// per character, so each ideograph is its own text fragment (a plain latin
    /// line stays one run).</summary>
    private static void EmitCellLineRuns(Page page, string fontRes, double fontSize,
        double x, double y, string text, string measureFace)
    {
        var hasCjk = false;
        if (Environment.GetEnvironmentVariable("ASPOSE_H4_NOCJKSPLIT") is null)
        foreach (var ch in text) if (ch >= '⺀') { hasCjk = true; break; }
        if (!hasCjk)
        {
            EmitPositionedRun(page, fontRes, fontSize, x, y, text);
            return;
        }
        var runX = x;
        var runStart = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            var boundary = i == text.Length || text[i] >= '⺀' || text[i] == ' ';
            if (!boundary) continue;
            if (i > runStart)
            {
                var seg = text[runStart..i];
                EmitPositionedRun(page, fontRes, fontSize, runX, y, seg);
                runX += MeasureFaceText(measureFace, seg, fontSize);
            }
            if (i < text.Length)
            {
                if (text[i] == ' ')
                    runX += MeasureFaceText(measureFace, " ", fontSize);
                else
                {
                    var ideo = text[i].ToString();
                    EmitPositionedRun(page, fontRes, fontSize, runX, y, ideo);
                    runX += MeasureFaceText(measureFace, ideo, fontSize);
                }
            }
            runStart = i + 1;
        }
    }

    private static double EstimateNestedTableHeight(string html, double rowPitch)
    {
        var inner = ExtractNestedTables(html, out var subs);
        var h = Regex.Matches(inner, @"<tr\b", RegexOptions.IgnoreCase).Count * rowPitch;
        foreach (var sub in subs) h += EstimateNestedTableHeight(sub, rowPitch);
        return h;
    }

    /// <summary>Wrap-aware height of a nested table: the bordered draw strokes and
    /// fills each row box BEFORE its cells render, so it needs the real extent a
    /// nested grid will occupy. Each row is its tallest cell's wrapped line count
    /// on the row pitch — wrapping at the cell's width attribute (hard &lt;br&gt;
    /// breaks kept) — plus the table's cellpadding band.</summary>
    private static double NestedTableWrappedHeight(string html, double rowPitch,
        string face, double fontSize, double fallbackW)
    {
        var inner = ExtractNestedTables(html, out var subs);
        var p = 0.75;
        var cpm = Regex.Match(inner, @"<table\b[^>]*\bcellpadding\s*=\s*[""']?(\d+(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        if (cpm.Success) p = double.Parse(cpm.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        var h = 2 * p;
        foreach (Match rm in Regex.Matches(inner,
            @"<tr\b[^>]*>([\s\S]*?)(?=<tr\b|</table)", RegexOptions.IgnoreCase))
        {
            double rowH = 0;
            foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                @"<t[dh]\b([^>]*)>([\s\S]*?)</t[dh]>", RegexOptions.IgnoreCase))
            {
                var wAttr = Regex.Match(cm.Groups[1].Value, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)");
                var cw = wAttr.Success
                    ? double.Parse(wAttr.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) * 0.75
                    : fallbackW;
                var brText = Regex.Replace(cm.Groups[2].Value, @"<br\s*/?\s*>",
                    "\u0001", RegexOptions.IgnoreCase);
                var txt = CollapseWs(DecodeEntities(Regex.Replace(brText, "<[^>]+>", " "))).Trim();
                if (txt.Length == 0) continue;
                rowH = Math.Max(rowH,
                    MeasuredWordWrap(txt, cw, face, fontSize).Length * rowPitch);
            }
            h += rowH;
        }
        foreach (var sub in subs)
            h += NestedTableWrappedHeight(sub, rowPitch, face, fontSize, fallbackW);
        return h;
    }

    private static bool TrySplitWrapperStack(string tableHtml, out string wrapperAttrs,
        out List<(string Html, bool NewCell)> children)
    {
        wrapperAttrs = "";
        children = new List<(string, bool)>();
        var open = Regex.Match(tableHtml, @"<table\b([^>]*)>", RegexOptions.IgnoreCase);
        if (!open.Success) return false;
        wrapperAttrs = open.Groups[1].Value;
        // body of the OUTER table = up to its matching close
        var depth = 0;
        var bodyStart = open.Index + open.Length;
        var bodyEnd = -1;
        foreach (Match t in Regex.Matches(tableHtml, @"<(/?)table\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (t.Groups[1].Value.Length == 0) depth++;
            else if (--depth == 0) { bodyEnd = t.Index; break; }
        }
        if (bodyEnd < 0) { return false; }
        var body = tableHtml[bodyStart..bodyEnd];

        // Every row must be a single td; every td must contain only tables.
        var pos = 0;
        var sawChild = false;
        while (true)
        {
            var td = Regex.Match(body[pos..], @"<td\b[^>]*>", RegexOptions.IgnoreCase);
            if (!td.Success) break;
            var cellStart = pos + td.Index + td.Length;
            // find the matching </td> at table-depth 0
            var scan = cellStart;
            var tDepth = 0;
            var cellEnd = -1;
            foreach (Match t in Regex.Matches(body[cellStart..], @"<(/?)(table|td)\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var closing = t.Groups[1].Value.Length > 0;
                var tag = t.Groups[2].Value.ToLowerInvariant();
                if (tag == "table") { tDepth += closing ? -1 : 1; continue; }
                if (tag == "td" && closing && tDepth == 0) { cellEnd = cellStart + t.Index; break; }
                if (tag == "td" && !closing && tDepth == 0) { cellEnd = cellStart + t.Index; break; }
            }
            if (cellEnd < 0) cellEnd = body.Length;
            var cell = body[cellStart..cellEnd];
            // the cell must be ONLY tables (+ whitespace); collect them —
            // tables sharing one cell stack flush, a new CELL starts a padded row
            var firstInCell = true;
            var rest = cell;
            while (true)
            {
                rest = rest.TrimStart();
                if (rest.Length == 0) break;
                var ct = Regex.Match(rest, @"^<table\b", RegexOptions.IgnoreCase);
                if (!ct.Success) { return false; }
                var cDepth = 0; var cEnd = -1;
                foreach (Match t in Regex.Matches(rest, @"<(/?)table\b[^>]*>", RegexOptions.IgnoreCase))
                {
                    if (t.Groups[1].Value.Length == 0) cDepth++;
                    else if (--cDepth == 0) { cEnd = t.Index + t.Length; break; }
                }
                if (cEnd < 0) { return false; }
                children.Add((rest[..cEnd], firstInCell));
                firstInCell = false;
                sawChild = true;
                rest = rest[cEnd..];
            }
            pos = cellEnd;
            var closeTd = Regex.Match(body[pos..], @"</td\s*>", RegexOptions.IgnoreCase);
            pos = closeTd.Success ? pos + closeTd.Index + closeTd.Length : body.Length;
        }
        // rows with more than one td disqualify: a second <td> before a </tr>
        // was consumed above only when it held tables; a mixed grid keeps the
        // normal path. Approximate by requiring at least one child and NO bare
        // text between the wrapper's structural tags.
        if (!sawChild) { return false; }
        var stripped = Regex.Replace(body, @"<table\b[\s\S]*", "", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<[^>]+>", "");
        if (DecodeEntities(stripped).Trim().Length > 0) { return false; }
        return true;
    }

    /// <summary>One cell of a metric-flow table (see <see cref="RenderMetricTable"/>).</summary>
    private sealed partial class MetricCell
    {
        public string Text = "";
        public bool Bold;
        public HorizontalAlignment Align = HorizontalAlignment.Left;
        // Widest `WIDTH:Npx; DISPLAY:inline-table` span in the cell (pt); such a span
        // fixes its column's content width and grows the first line box by 3 pt.
        public double SpanW;
        public bool HasSpan;
        public int ColSpan = 1;          // colspan attribute
        public double WidthPct;          // width="40%" attribute (0 = none)
        public double WidthPx;           // width="300" / "300px" attribute, in pt (0 = none)
        public string? Face;             // <font face=…> / inline font-family (null = flow default)
        public bool FontTagSized;        // FontSize came from a <font size=N> attribute
        public bool Italic;              // inline font-style: italic
        public Color? Fore;              // <font color=…> ink
        public Color? Bg;                // bgcolor attribute / background-color style
        public double? FontSize;         // tr/td inline or class font-size (pt)
        public bool VAlignTop;           // valign='top' attribute
        public bool NoWrap;              // nowrap attribute / white-space:nowrap
        public List<string>? SubTables;  // nested tables rendered as grids in this cell
        // Interleaved cell content, kept in SOURCE order when a nested grid
        // precedes text ink: text runs (bold per run) and grids draw as one
        // flow. Null = the calibrated stacked draw (text, then grids).
        public List<(string? TableHtml, string Text, bool Bold)>? Flow;
        public double BorderRightW;      // style border-right width, in pt (0 = none)
        public Color BorderRightCol = Color.FromRgb(0, 0, 0);
        public bool VAlignBottom;        // vertical-align: bottom (class skin)
        public double PadLeft = -1;      // padding-left override, pt (-1 = table default)
        public double BorderLeftW;       // class border-left width, pt (0 = none)
        public double BorderBottomW;     // class border-bottom width, pt (0 = none)
        public double BorderTopW;        // class border-top width, pt (0 = none)
        public bool BorderTopDashed;     // border-top: dashed (the tear-off rule)
        public double HeightPt;          // class height, pt (0 = auto) — paces the row exactly
        public bool FontFromClass;       // FontSize came from a CLASS skin (row is content-paced)
        public List<string>? ClassNames; // td class attribute values
        // Div-stacked cell content (the boleto's .t/.c ladders): each div is one
        // styled line whose class height paces its band
        public List<MetricDivSeg>? DivSegs;
        public double ImgHPt;            // declared image box height in the cell, pt
        public double ImgWPt;            // declared image box width in the cell, pt
        // A data-URI PNG inside an absolutely positioned div (left:N%): drawn
        // at natural size, offset from the cell content left by the fraction.
        public byte[]? AbsPng;
        public double AbsPngLeftFrac;
        public bool AltTextOnly;         // cell text is a broken image's alt — wraps in ImgWPt
        public double PadTopPt;          // td style padding-top (newsletter cells)
        public string[] Lines = [];      // wrapped at layout time
        public double ContentH;          // Σ line boxes
        public bool Phantom;             // colspan filler / RTL pad slot — never draws
        public int RowSpan = 1;          // rowspan attr — content overlays rows below
        public double ClassWidthPct;     // class width % — pins only when over-full
        public byte[]? ImgJpegBytes;     // data-URI JPEG payload — draws ABOVE the cell's segments
        public bool WidthSetterCell;     // inline WIDTH+MIN-WIDTH pair (a report grid's sizing row)
    }

    /// <summary>One stacked div inside a metric cell (see MetricCell.DivSegs).</summary>
    private sealed partial class MetricDivSeg
    {
        public string Text = "";
        public double? FontSize;
        public string? Face;
        public bool Bold;
        public Color? Fore;
        public double LineBoxPt;         // class height (min band height, 0 = auto)
        public double PadLeft;           // class padding-left
        public bool BorderBottom;        // .BB underline band
        // Paragraph segments (the newsletter cells): the UA 1.12 em block
        // margins, collapsed max-wise between adjacent segments.
        public double MarginTopPt;
        public double MarginBottomPt;
        // the paragraph's class authored its margins (`margin: 0pt …`) — the
        // UA block margins yield to them at the segment close
        public bool MarginsExplicit;
        // class background-color: the band fills the cell's content width
        // (the green bar — measured 97.5..497.5 × its class height)
        public Color? Bg;
    }

    /// <summary>Metric-flow table renderer: real HTML table geometry — default
    /// cellspacing 2px (1.5 pt) and cellpadding 1px (0.75 pt), stylesheet cell font,
    /// win-metric line boxes with half-leading baselines, middle vertical alignment,
    /// column widths from width-% attributes / inline-table spans / content, and
    /// row-at-a-time pagination (continuation pages resume at the raw content top).
    /// Emits positioned runs directly and advances the flow cursor to the table
    /// bottom. Only the metric flow calls this; the legacy generator-table path is
    /// untouched.</summary>
    // RTL grid anchoring: the table's RIGHT edge sits this far inside the page's
    // right edge (measured 91.78 on the widened RTL sheet — the widest grid's
    // LEFT edge then lands exactly on the 90 pt page margin).
    private const double RtlGridRightInsetPt = 91.78;

    // Faces the SOURCE renderer's HTML engine actually resolves — a face outside
    // this set falls back to the flow default exactly like an unknown family
    // (probed: face="David" cells draw the UA serif; 'arial narrow' falls to the
    // flow face on the class-framework sheets).
    private static readonly HashSet<string> SourceEngineFaces =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Arial", "Helvetica", "Verdana", "Times New Roman", "Courier New",
            "Courier", "Tahoma", "Georgia", "Trebuchet MS", "Calibri", "SimSun",
            "MS Gothic",
        };

    // The source engine's image viewport: a cell photo wider than this draws
    // scaled down to it, preserving aspect (measured on the SSRS report export:
    // the 1024×768 px JPEG — 768 pt natural — lands exactly 612×459 pt, the
    // 8.5 in viewport width, and the sheet widens to hold it).
    private const double JpegViewportPt = 612.0;

    /// <summary>Intrinsic pixel size of a JPEG from its SOF marker (0 pair when
    /// the stream is not parseable).</summary>
    private static (int w, int h) JpegDims(byte[] jpg)
    {
        if (jpg.Length < 4 || jpg[0] != 0xFF || jpg[1] != 0xD8) return (0, 0);
        var i = 2;
        while (i + 9 < jpg.Length)
        {
            if (jpg[i] != 0xFF) { i++; continue; }
            var marker = jpg[i + 1];
            if (marker == 0xFF) { i++; continue; }
            // standalone markers carry no length payload
            if (marker is >= 0xD0 and <= 0xD9) { i += 2; continue; }
            var len = (jpg[i + 2] << 8) | jpg[i + 3];
            if (len < 2) return (0, 0);
            // SOF0..SOF15 (minus DHT/JPG/DAC): frame header holds the size
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                var h = (jpg[i + 5] << 8) | jpg[i + 6];
                var w = (jpg[i + 7] << 8) | jpg[i + 8];
                return (w, h);
            }
            i += 2 + len;
        }
        return (0, 0);
    }

    private static void RenderMetricTable(Document doc, ref Page page, ref double y,
        string tableHtml, IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double marginLeft, double contentWidth, double pageWidth, double pageHeight,
        double marginTop, double marginBottom, string face, (double asc, double sum) fm,
        Core.PdfDictionary docFontDict, bool stdSerif = false, double baseFontSize = 11,
        bool wrapperStacks = false, double symInsetPt = UaBodyMarginPt, bool rtl = false,
        bool paragraphCells = false, bool serifReportCells = false)
    {
        const double PxPt = 0.75;

        // Wrapper stacks (legacy nested-table markup): a table whose every row is
        // a single td holding only tables contributes CHROME, not a grid — its
        // children stack inside insets of (2 x border) + cellspacing + cellpadding,
        // and a border=1 wrapper draws the browser's two beveled 1px frames
        // (outset: #555 top+left over black bottom+right; inset the reverse)
        // around the stacked extent. Measured on the reference: margin 96 -> 96.75
        // (plain wrapper, p=1px) -> 98.25+0.75 = 99 through a bordered one.
        if (wrapperStacks
            && TrySplitWrapperStack(tableHtml, out var wAttrs, out var wChildren))
        {
            double wS = 1.5, wP = 0.75, wBw = 0;
            Color? wBg = null;
            var wcs = Regex.Match(wAttrs, @"cellspacing\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (wcs.Success) wS = double.Parse(wcs.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * PxPt;
            var wcp = Regex.Match(wAttrs, @"cellpadding\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (wcp.Success) wP = double.Parse(wcp.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * PxPt;
            var wbm = Regex.Match(wAttrs, @"\bborder\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (wbm.Success && double.TryParse(wbm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wbv) && wbv > 0)
                wBw = 0.75;
            var wbgm = Regex.Match(wAttrs, @"bgcolor\s*=\s*[""']?([#0-9a-zA-Z]+)", RegexOptions.IgnoreCase);
            if (wbgm.Success)
                wBg = ParseCssColor(wbgm.Groups[1].Value.StartsWith('#')
                    ? wbgm.Groups[1].Value : "#" + wbgm.Groups[1].Value);

            var wInset = 2 * wBw + wS + wP;
            var wPage0 = page;
            var wStreamMark = page.ContentStreamCount;
            var wX0 = marginLeft;
            var wTopTd = pageHeight - y;
            y -= wInset;
            var wAvail = contentWidth - symInsetPt;
            var wRight = wX0 + wAvail;
            var wFirst = true;
            var wPrevRendered = false;
            var wPrevBordered = false;
            foreach (var (childHtml, childNewCell) in wChildren)
            {
                // Each wrapper ROW pads its cell (bottom + top cellpadding);
                // SAME-CELL siblings sit the measured 1.2 pt apart — and an
                // EMPTY table (no cells) is fully transparent: no gap of its
                // own, and its neighbours share a single gap across it.
                var childRenders = Regex.IsMatch(childHtml, @"<td\b", RegexOptions.IgnoreCase);
                var childBordered = Regex.IsMatch(childHtml,
                    @"^\s*<table\b[^>]*\bborder\s*=\s*[""']?[1-9]", RegexOptions.IgnoreCase);
                if (!wFirst && childRenders && wPrevRendered)
                    y -= childNewCell ? 2 * wP
                        : childBordered || wPrevBordered ? WrapperSiblingGapPt : 0;
                wFirst = false;
                if (childRenders) { wPrevRendered = true; wPrevBordered = childBordered; }
                RenderMetricTable(doc, ref page, ref y, childHtml, css,
                    wX0 + wInset, wAvail - 2 * wInset, pageWidth, pageHeight,
                    marginTop, marginBottom, face, fm, docFontDict,
                    stdSerif, baseFontSize, wrapperStacks: true, symInsetPt: 0,
                    paragraphCells: paragraphCells, serifReportCells: serifReportCells);
            }
            y -= wInset;
            var wBotTd = pageHeight - y;
            if (wBw > 0)
            {
                var wInv = System.Globalization.CultureInfo.InvariantCulture;
                var dark = "0 0 0 RG";
                var gray = "0.333 0.333 0.333 RG";
                var wsb = new StringBuilder("q 0.75 w ");
                void WLine(string col, double lx0, double ly0d, double lx1, double ly1d)
                    => wsb.Append(string.Create(wInv,
                        $"{col} {lx0:F2} {pageHeight - ly0d:F2} m {lx1:F2} {pageHeight - ly1d:F2} l S "));
                // outset frame: #555 top+left, black bottom+right
                WLine(gray, wX0, wTopTd + 0.375, wRight, wTopTd + 0.375);
                WLine(gray, wX0 + 0.375, wTopTd, wX0 + 0.375, wBotTd);
                WLine(dark, wX0, wBotTd - 0.375, wRight, wBotTd - 0.375);
                WLine(dark, wRight - 0.375, wTopTd, wRight - 0.375, wBotTd);
                // inset frame, one border width inside: black top+left, #555 bottom+right
                WLine(dark, wX0 + 0.75, wTopTd + 1.125, wRight - 0.75, wTopTd + 1.125);
                WLine(dark, wX0 + 1.125, wTopTd + 0.75, wX0 + 1.125, wBotTd - 0.75);
                WLine(gray, wX0 + 0.75, wBotTd - 1.125, wRight - 0.75, wBotTd - 1.125);
                WLine(gray, wRight - 1.125, wTopTd + 0.75, wRight - 1.125, wBotTd - 0.75);
                wsb.Append("Q\n");
                page.AddContentStream(Encoding.ASCII.GetBytes(wsb.ToString()));
            }
            // The wrapper's bgcolor paints the whole band BENEATH its children:
            // the fill is inserted at the stream position the wrapper opened at,
            // so it underlays everything the children appended after it.
            if (wBg is { } wBand && ReferenceEquals(page, wPage0))
            {
                var wbInv = System.Globalization.CultureInfo.InvariantCulture;
                wPage0.InsertContentStreamAt(wStreamMark, Encoding.ASCII.GetBytes(string.Create(wbInv,
                    $"q {wBand.R / 255.0:0.###} {wBand.G / 255.0:0.###} {wBand.B / 255.0:0.###} rg " +
                    $"{wX0:F2} {pageHeight - wBotTd:F2} {wRight - wX0:F2} {wBotTd - wTopTd:F2} re f Q\n")));
            }
            return;
        }

        // Cell font: the stylesheet's table/td font-size (the metric flow honors the
        // CSS); otherwise the caller's base size (11 pt for the MSHTML metric flow,
        // the UA 16px base for the browser-default flow).
        double fontSize = baseFontSize;
        var tableClassFont = false;   // fontSize came from a table CLASS skin
        var widthClassTable = false;  // the table declares its box via a width CLASS (the framework fingerprint)
        if (TryGetCssLength(css, "table", "font-size", out var tfs)) fontSize = tfs;
        else if (TryGetCssLength(css, "td", "font-size", out var dfs)) fontSize = dfs;

        // The sheet's `table { font: 10pt Arial }` SHORTHAND seeds the grid's
        // size AND face — the longhand reads above never see either half. The
        // cells then DRAW in that face too (tableRuleFace below).
        var tableRuleFace = false;
        if (stdSerif && css.TryGetValue("table", out var tblFontRule)
            && tblFontRule.TryGetValue("font", out var tblFontV))
        {
            var tfsh = Regex.Match(tblFontV, @"([\d.]+)\s*(pt|px)\s+(.+)$", RegexOptions.IgnoreCase);
            if (tfsh.Success && double.TryParse(tfsh.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var tfshV) && tfshV > 0)
            {
                fontSize = tfsh.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase)
                    ? tfshV * 0.75 : tfshV;
                if (FirstFontFamily(tfsh.Groups[3].Value) is { Length: > 0 } tfshFam
                    && WinMetricsFor(tfshFam) is { } tfshFm)
                { face = tfshFam; fm = tfshFm; tableRuleFace = true; }
            }
        }

        // A face whose win metrics sum to one em or less (SimSun's bitmap-era
        // 220+36/256) would render zero-leading lines; the engine paces such
        // CJK faces at 1.2 em (measured on the official-letter reference).
        var fmSum = fm.sum <= 1.0 ? 1.2 : fm.sum;
        var lineH = MetricLineHeight(fontSize, fmSum);
        var boldFace = face + "-Bold";

        // Bordered separate-border mode (the UA-flow edge-to-edge dialect): the
        // sheet's table {border: 1px solid} + td {border} draw real grid borders —
        // outer box, then per-cell boxes inset by the 2px UA border-spacing.
        var bordered = false;
        Color borderColor = Color.FromRgb(0, 0, 0);
        const double bw = 0.75;   // the sheet's 1px border
        if (stdSerif && css.TryGetValue("table", out var tblRule)
            && tblRule.TryGetValue("border", out var tblBv)
            && tblBv.Contains("solid", StringComparison.OrdinalIgnoreCase)
            && !(tblRule.TryGetValue("border-collapse", out var tblBc)
                 && tblBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)))
        {
            bordered = true;
            if (ParseCssColor(tblBv) is { } tblBcol) borderColor = tblBcol;
        }
        // Outer-frame-only COLLAPSE grid: the TABLE rule alone carries a solid
        // border (longhands) under border-collapse — the frame collapses onto
        // the table box and the cells, declaring no borders of their own, draw
        // none. Columns come from the width classes, given back to the grid box
        // deficit ∝ slack (declared − min-content) when over-declared.
        var collapseBoxW = 0.0;
        if (stdSerif
            && css.TryGetValue("table", out var cbRule)
            && cbRule.TryGetValue("border-collapse", out var cbC)
            && cbC.Contains("collapse", StringComparison.OrdinalIgnoreCase)
            && cbRule.TryGetValue("border-style", out var cbS)
            && cbS.Contains("solid", StringComparison.OrdinalIgnoreCase)
            && !(css.TryGetValue("td", out var cbTd)
                 && (cbTd.ContainsKey("border") || cbTd.ContainsKey("border-style"))))
        {
            collapseBoxW = cbRule.TryGetValue("border-width", out var cbW)
                && TryParseLength(cbW.Trim(), out var cbWPt) && cbWPt > 0 ? cbWPt : 0.75;
            if (cbRule.TryGetValue("border-color", out var cbCol)
                && ParseCssColor(cbCol) is { } cbColV) borderColor = cbColV;
        }
        // width: 100% from the sheet's table rule — the grid fills the content box.
        var tableFills = css.TryGetValue("table", out var tblWr)
            && tblWr.TryGetValue("width", out var tblWv) && tblWv.Trim() == "100%";
        // table-layout from a table.<class> rule, resolved once the tag is seen.
        var layoutFixed = false;
        // border=N ATTRIBUTE mode (legacy HTML tables): real grid borders like the
        // css-bordered mode, but the outer box HUGS the column grid instead of
        // filling the content box, and align=center centres that box on the page
        // (the symmetric UA content frame's middle — measured on the reference:
        // a 229.5pt grid at (595−229.5)/2).
        var borderHugs = false;
        var centerTable = false;
        // Collapsed CLASS grid (border-collapse:collapse + border longhands on
        // the table's class): light 1px cell borders share row boundaries — the
        // pitch grows one border per row and the grid strokes in the rule's colour.
        var collapsedGrid = false;
        var collapsedCol = Color.FromRgb(193, 193, 193);
        var collapsedLineH = 0.0;      // the class rule's LINE-HEIGHT, in pt
        // Collapsed ATTRIBUTE grid (border=N + style border-collapse:collapse):
        // cell borders share their boundaries — the row pitch is the bare content
        // height, text seats at the cell box edge, and percent halves split the
        // table box beside the pixel-fixed columns (measured on the reference:
        // the 50% halves of a 1000px table land 372.75 wide each).
        var attrCollapse = false;

        // Parse the table structure. Geometry attributes sit on the <table> tag;
        // a class rule's MARGIN-LEFT indents the whole table box.
        double s = 1.5, p = 0.75, indent = 0, tablePct = 0, tableWpt = 0;
        double tableHeightPt = 0;                     // table height attr (RTL grid)
        if (collapseBoxW > 0) s = 0;                  // collapse zeroes the spacing
        // Element-rule collapse grid: the table AND td rules both carry a
        // solid border shorthand under border-collapse ("table, th, td
        // { border: 1px solid; border-collapse: collapse }") — the source
        // engine draws the shared-1px grid across the symmetric content
        // frame (measured on the reference: box 96..499 on the 409 pt
        // band, cell fills 97.5..497.5, zero spacing).
        var elemCollapseGrid = false;
        if (stdSerif && collapseBoxW == 0
            && css.TryGetValue("td", out var egTd)
            && egTd.TryGetValue("border-collapse", out var egBc)
            && egBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)
            && egTd.TryGetValue("border", out var egB)
            && egB.Contains("solid", StringComparison.OrdinalIgnoreCase))
        {
            collapsedGrid = true;
            elemCollapseGrid = true;
            s = 0;
            if (ParseCssColor(egB) is { } egCol) collapsedCol = egCol;
        }
        // pt-report sheets (non-serif wrapper mode): the TABLE rule's
        // border-collapse zeroes the spacing and its padding: 0 the cell
        // padding — cell/table attributes still win below.
        if (!stdSerif && wrapperStacks && css.TryGetValue("table", out var ptTblRule))
        {
            if (ptTblRule.TryGetValue("border-collapse", out var ptBc)
                && ptBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)) s = 0;
            if (ptTblRule.TryGetValue("padding", out var ptPad)
                && Regex.IsMatch(ptPad.Trim(), @"^0(px)?$")) p = 0;
        }
        var rows = new List<List<MetricCell>>();
        var rowHeights = new List<double>();          // tr style height (pt, 0 = auto)
        // a CLASS height paces its row EXACTLY (the boleto's h13/h12 grid rows:
        // label 9.75 + value 9 measured as the pitch, content fitted inside);
        // a STYLE height keeps the calibrated raise-only behaviour
        var rowHeightExact = new List<bool>();
        var pendingRowHExact = false;
        // Row-group ordering: thead rows render first and tfoot rows LAST regardless
        // of source order (a tfoot authored before the tbody still closes the table).
        var rowSections = new List<int>();            // 0 = thead, 1 = tbody/none, 2 = tfoot
        var curSection = 1;
        // Modern nesting (the UA-serif corpus): a table inside a CELL renders
        // as its own grid within that cell (extracted here, recursed at draw
        // time); the flat merge stays for the calibrated legacy dialects.
        List<string>? nestedTables = null;
        if (wrapperStacks)
            tableHtml = ExtractNestedTables(tableHtml, out nestedTables);
        MetricCell? cell = null;
        List<MetricCell>? row = null;
        var text = new StringBuilder();
        var boldDepth = 0;
        // b/strong transitions at raw-text positions — CloseCell rebuilds the
        // cell's interleaved Flow runs from them.
        var cellBoldMarks = new List<(int pos, bool on)>();
        var sawTable = false;
        double? rowFs = null;                         // tr style font-size
        HorizontalAlignment? rowAlign = null;         // tr style text-align
        Color? rowBg = null;                          // tr bgcolor attribute
        // tr class skins (the boleto micro-framework): row typography defaults
        // and `.cls td` descendant bags applied to every cell of the row
        string? rowFace = null;
        var rowFsFromClass = false;
        var rowBold = false;
        Color? rowFore = null;
        var rowVTop = false;
        var rowVBottom = false;
        List<Dictionary<string, string>>? rowTdBags = null;
        List<string>? rowClasses = null;
        Color? tableBg = null;                        // table bgcolor attribute
        double pendingRowH = 0;
        MetricDivSeg? curSeg = null;                  // open div segment in the cell
        var divText = new StringBuilder();
        var pendingAbsLeftFrac = -1.0;                // abs div left:N% awaiting its img
        var whiteSpans = new Stack<bool>();           // span color:white nesting
        // The sheet's `a { color: … }` rule inks anchor text in cells (the source
        // renderer applies it as an inline colour; the corpus wraps whole cell
        // contents in one <a>, so it styles the rest of the cell like <font color>).
        Color? rmtAnchorColor = null;
        if (css.TryGetValue("a", out var rmtARule)
            && rmtARule.TryGetValue("color", out var rmtACol))
            rmtAnchorColor = ParseCssColor(rmtACol);
        var whiteDepth = 0;
        // Report cells: a span's typography ends WITH the span — the state to
        // restore at its close (the whole-cell restyle stays for the legacy flows).
        var spanSaves = new Stack<(double? fs, string? fc, bool b, Color? fo)>();
        // the NEWSLETTER dialect only — the NHS/boleto report greens were
        // calibrated on the whole-cell typography model and keep it
        var reportCells = paragraphCells && (!stdSerif || serifReportCells) && wrapperStacks;
        // Report cells: run-bold accounting — b/strong lives in boldDepth, not
        // on the cell; a segment (or a p-less cell) is bold when ALL its ink is.
        int segBoldChars = 0, segPlainChars = 0;
        int cellBoldChars = 0, cellPlainChars = 0;
        // a SEGMENT's typography is what its FIRST ink saw — a trailing styled
        // span (the report paragraphs' nbsp tails) cannot restyle it at close
        double? segFs = null; string? segFace = null; Color? segFore = null;
        var segInkSeen = false;
        // …and the LEAD text's typography (a styled heading span) is captured
        // when its first ink arrives, before the spans close and restore.
        double? leadFs = null; string? leadFace = null; Color? leadFore = null;
        var leadBold = false; var leadSeen = false;
        var nestDepth = 0;                            // tables nested inside a cell
        var pendingNestSpan = 0;                      // discarded container cell's colspan

        void CloseSeg()
        {
            segBoldChars = 0; segPlainChars = 0; segInkSeen = false;
            segFs = null; segFace = null; segFore = null;
            if (curSeg is null || cell is null) { curSeg = null; divText.Clear(); return; }
            // Sub-table markers belong to the CELL (CloseCell lifts them into
            // SubTables) — never to a segment's drawn text.
            var segRaw = divText.ToString();
            if (segRaw.IndexOf('\u0002') >= 0)
            {
                var segMarkers = string.Concat(
                    from Match sm in Regex.Matches(segRaw, "\u0002\\d+\u0003")
                    select sm.Value);
                segRaw = Regex.Replace(segRaw, "\u0002\\d+\u0003", " ");
                text.Append(segMarkers);
            }
            curSeg.Text = CollapseWs(segRaw).Trim(' ').Trim('\u0001').Trim(' ');
            (cell.DivSegs ??= new List<MetricDivSeg>()).Add(curSeg);
            curSeg = null;
            divText.Clear();
        }
        void CloseCell()
        {
            CloseSeg();
            // report cells: a p-less cell wholly wrapped in b/strong is bold;
            // mixed-run cells stay in the body face
            if (reportCells && cell is not null && !cell.Bold
                && cellBoldChars > 0 && cellPlainChars == 0)
                cell.Bold = true;
            cellBoldChars = 0; cellPlainChars = 0;
            leadSeen = false; leadFs = null; leadFace = null; leadFore = null; leadBold = false;
            if (cell is null) return;
            cell.Text = CollapseWs(text.ToString());
            // Interleaved cell flow: a nested grid that comes BEFORE text ink
            // keeps its source position — the cell draws text runs (bold per
            // run) and grids in order. Cells whose grids all trail the text
            // keep the calibrated stacked draw (text lines, then grids).
            if (nestedTables is not null && !reportCells)
            {
                var raw = text.ToString();
                var mms = Regex.Matches(raw, "\u0002(\\d+)\u0003");
                var inkAfterMarker = false;
                if (mms.Count > 0)
                {
                    var afterFirst = raw[(mms[0].Index + mms[0].Length)..];
                    afterFirst = Regex.Replace(afterFirst, "\u0002\\d+\u0003", "");
                    foreach (var ch in afterFirst)
                        if (!char.IsWhiteSpace(ch) && ch is not ('\u0001' or '\u00A0'))
                        { inkAfterMarker = true; break; }
                }
                if (inkAfterMarker)
                {
                    bool BoldAt(int at)
                    {
                        var on = false;
                        foreach (var (mp, mo) in cellBoldMarks)
                        { if (mp > at) break; on = mo; }
                        return on;
                    }
                    var flow = new List<(string? TableHtml, string Text, bool Bold)>();
                    void AddRuns(int from, int to)
                    {
                        var runStart = from;
                        var runBold = BoldAt(from);
                        for (var ci = from + 1; ci <= to; ci++)
                        {
                            var b2 = ci < to ? BoldAt(ci) : !runBold;
                            if (b2 == runBold && ci < to) continue;
                            var chunk = CollapseWs(raw[runStart..ci]);
                            if (chunk.Length > 0) flow.Add((null, chunk, runBold));
                            runStart = ci; runBold = b2;
                        }
                    }
                    var fpos = 0;
                    foreach (Match fmm in mms)
                    {
                        if (fmm.Index > fpos) AddRuns(fpos, fmm.Index);
                        if (int.TryParse(fmm.Groups[1].Value, out var fti)
                            && fti < nestedTables.Count)
                            flow.Add((nestedTables[fti], "", false));
                        fpos = fmm.Index + fmm.Length;
                    }
                    if (fpos < raw.Length) AddRuns(fpos, raw.Length);
                    cell.Flow = flow;
                    cell.Bold = false;   // bold lives on the runs now
                }
            }
            // Nested-table markers lift out of the text into the cell's grids.
            if (nestedTables is not null && cell.Text.IndexOf('\u0002') >= 0)
            {
                foreach (Match nm in Regex.Matches(cell.Text, "\u0002(\\d+)\u0003"))
                    if (int.TryParse(nm.Groups[1].Value, out var nti) && nti < nestedTables.Count)
                        (cell.SubTables ??= new List<string>()).Add(nestedTables[nti]);
                cell.Text = Regex.Replace(cell.Text, "\u0002\\d+\u0003", " ");
                cell.Text = CollapseWs(cell.Text);
            }
            // A container cell's whitespace-only paragraph segments (a
            // tellfriend <p> whose img is dead and whose text is one &nbsp;)
            // hold no band — dropping them keeps the nested grids at the top.
            if (reportCells && cell.SubTables is { Count: > 0 }
                && cell.DivSegs is { Count: > 0 })
            {
                cell.DivSegs.RemoveAll(sg =>
                {
                    foreach (var ch in sg.Text)
                        if (ch is not (' ' or '\u00A0' or '\u0001')) return false;
                    return true;
                });
                if (cell.DivSegs.Count == 0) cell.DivSegs = null;
            }
            // A trailing <br> closes the cell's last line — it opens no new one
            // (mid-cell breaks keep their sentinel).
            cell.Text = cell.Text.TrimEnd('\u0001');
            text.Clear();
            cellBoldMarks.Clear();
            row!.Add(cell);
            cell = null;
        }
        void CloseRow()
        {
            CloseCell();
            if (row is { Count: > 0 })
            {
                rows.Add(row);
                rowHeights.Add(pendingRowH);
                rowHeightExact.Add(pendingRowHExact);
                rowSections.Add(curSection);
            }
            row = null;
            pendingRowH = 0;
            pendingRowHExact = false;
            rowFs = null;
            rowAlign = null;
            rowBg = null;
            rowFace = null;
            rowFsFromClass = false;
            rowBold = false;
            rowFore = null;
            rowVTop = false;
            rowVBottom = false;
            rowTdBags = null;
            rowClasses = null;
        }

        // Class-skin resolution (the boleto micro-framework): the metric grid
        // honours class typography, geometry and per-side borders on rows and
        // cells. A declared family only sticks when it RESOLVES — 'arial narrow'
        // falls back to the flow face exactly like the junk-family idiom.
        void ApplyCellClassBag(MetricCell mc, IReadOnlyDictionary<string, string> bag)
        {
            foreach (var (bProp, bVal) in bag)
                switch (bProp.ToLowerInvariant())
                {
                    case "font":
                    {
                        var fsh = Regex.Match(bVal, @"([\d.]+)\s*px\s+(.+)$");
                        if (fsh.Success)
                        {
                            mc.FontSize = DtpNum(fsh.Groups[1].Value) * PxPt;
                            // a QUOTED family inside the shorthand is dropped with
                            // the shorthand's grammar (the sheet parser does the
                            // same) — the cell keeps the flow face
                            var shRaw = fsh.Groups[2].Value.TrimStart();
                            if (shRaw.Length > 0 && shRaw[0] != '"' && shRaw[0] != '\''
                                && FirstFontFamily(shRaw) is { Length: > 0 } shFam
                                && WinMetricsFor(shFam) is not null)
                                mc.Face = shFam;
                        }
                        if (bVal.Contains("bold", StringComparison.OrdinalIgnoreCase))
                            mc.Bold = true;
                        if (mc.FontSize is not null) mc.FontFromClass = true;
                        break;
                    }
                    case "font-size":
                        // a PERCENT size resolves against the cell's current size
                        // (th { font-size: 80% } = 9.6 on the 12 pt base; the
                        // .firm span's 400% = 48)
                        if (bVal.Trim().EndsWith("%", StringComparison.Ordinal)
                            && double.TryParse(bVal.Trim().TrimEnd('%'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var bagPct) && bagPct > 0)
                        { mc.FontSize = (mc.FontSize ?? fontSize) * bagPct / 100.0; mc.FontFromClass = true; }
                        else if (TryParseCssFontSize(bVal.Trim(), out var bagFs))
                        { mc.FontSize = bagFs; mc.FontFromClass = true; }
                        break;
                    case "font-family":
                        // Under the UA flow only the faces the SOURCE engine
                        // resolves apply — 'Century Gothic' falls to the flow
                        // serif exactly as the reference draws it.
                        if (FirstFontFamily(bVal) is { Length: > 0 } bagFam
                            && WinMetricsFor(bagFam) is not null
                            && (!stdSerif || SourceEngineFaces.Contains(bagFam)))
                            mc.Face = bagFam;
                        break;
                    case "font-weight":
                        if (bVal.Contains("bold", StringComparison.OrdinalIgnoreCase)
                            || (int.TryParse(bVal.Trim(), out var bagFw) && bagFw >= 600))
                            mc.Bold = true;
                        break;
                    case "color":
                        if (ParseCssColor(bVal.Trim()) is { } bagFc
                            && (bagFc.R != 0 || bagFc.G != 0 || bagFc.B != 0)) mc.Fore = bagFc;
                        break;
                    case "background-color":
                    case "background":
                        if (ParseCssColor(bVal.Trim()) is { } bagBg) mc.Bg = bagBg;
                        break;
                    case "text-align":
                        mc.Align = bVal.Trim().ToLowerInvariant() switch
                        {
                            "right" => HorizontalAlignment.Right,
                            "center" => HorizontalAlignment.Center,
                            _ => HorizontalAlignment.Left,
                        };
                        break;
                    case "vertical-align":
                        // last declaration wins: a cell's own Ab overrides the
                        // row's At (and vice versa)
                        if (bVal.Contains("top", StringComparison.OrdinalIgnoreCase))
                        { mc.VAlignTop = true; mc.VAlignBottom = false; }
                        else if (bVal.Contains("bottom", StringComparison.OrdinalIgnoreCase))
                        { mc.VAlignBottom = true; mc.VAlignTop = false; }
                        break;
                    case "width":
                    {
                        var bwm2 = Regex.Match(bVal, @"([\d.]+)\s*px");
                        if (bwm2.Success) mc.WidthPx = DtpNum(bwm2.Groups[1].Value) * PxPt;
                        // a class PERCENT width pins its column only when the
                        // table is over-constrained (see the width solve)
                        else if (Regex.Match(bVal, @"([\d.]+)\s*%") is { Success: true } bwPct)
                            mc.ClassWidthPct = DtpNum(bwPct.Groups[1].Value);
                        break;
                    }
                    case "padding-top":
                    {
                        // th { padding-top: 1em } grows the row above its text
                        // (em against the cell's resolved size).
                        var bpt = Regex.Match(bVal, @"([\d.]+)\s*(px|pt|em)");
                        if (bpt.Success)
                        {
                            var bptV = DtpNum(bpt.Groups[1].Value);
                            mc.PadTopPt = bpt.Groups[2].Value.ToLowerInvariant() switch
                            {
                                "px" => bptV * PxPt,
                                "em" => bptV * (mc.FontSize ?? fontSize),
                                _ => bptV,
                            };
                        }
                        break;
                    }
                    case "padding-left":
                    {
                        var bpl = Regex.Match(bVal, @"([\d.]+)\s*(px|pt|em)");
                        if (bpl.Success)
                            mc.PadLeft = bpl.Groups[2].Value.ToLowerInvariant() switch
                            {
                                "px" => DtpNum(bpl.Groups[1].Value) * PxPt,
                                "em" => DtpNum(bpl.Groups[1].Value) * (mc.FontSize ?? fontSize),
                                _ => DtpNum(bpl.Groups[1].Value),
                            };
                        break;
                    }
                    case "height":
                    {
                        var bph = Regex.Match(bVal, @"([\d.]+)\s*px");
                        if (bph.Success) mc.HeightPt = DtpNum(bph.Groups[1].Value) * PxPt;
                        break;
                    }
                    case "border-left":
                    case "border-right":
                    case "border-bottom":
                    case "border-top":
                    {
                        var bbw = Regex.Match(bVal, @"([\d.]+)\s*px");
                        var dashed = bVal.Contains("dashed", StringComparison.OrdinalIgnoreCase);
                        if (!bbw.Success
                            || !(dashed || bVal.Contains("solid", StringComparison.OrdinalIgnoreCase)))
                            break;
                        var sidePt = DtpNum(bbw.Groups[1].Value) * PxPt;
                        switch (bProp.ToLowerInvariant())
                        {
                            case "border-left": mc.BorderLeftW = sidePt; break;
                            case "border-right": mc.BorderRightW = sidePt; break;
                            case "border-bottom": mc.BorderBottomW = sidePt; break;
                            default:
                                mc.BorderTopW = sidePt;
                                mc.BorderTopDashed = dashed;
                                break;
                        }
                        break;
                    }
                }
        }

        var hiddenDepth = 0;
        string? hiddenTag = null;
        foreach (var tok in Tokenize(StripNonContent(tableHtml)))
        {
            if (tok.Kind == TokenKind.Text)
            {
                if (cell is not null && hiddenDepth == 0)
                {
                    var ttext = DecodeEntities(tok.Value);
                    if (curSeg is not null && whiteDepth == 0)
                    {
                        var segInk = ttext.AsSpan().Trim().Length;
                        if (reportCells && segInk > 0)
                        {
                            if (!segInkSeen)
                            {
                                segInkSeen = true;
                                segFs = cell.FontSize; segFace = cell.Face;
                                segFore = cell.Fore;
                            }
                            if (boldDepth > 0 || cell.Bold) segBoldChars += segInk;
                            else segPlainChars += segInk;
                        }
                        divText.Append(ttext);
                        continue;
                    }
                    if (whiteDepth > 0)
                        // white-on-white ink keeps its advance: an ideograph
                        // becomes an ideographic space, latin a plain space
                        foreach (var ch in ttext)
                            text.Append(char.IsWhiteSpace(ch) ? ch : ch >= '⺀' ? '　' : ' ');
                    else
                    {
                        var ink = ttext.AsSpan().Trim().Length;
                        if (reportCells && ink > 0)
                        {
                            cell.AltTextOnly = false;   // real ink joined the alt
                            if (!leadSeen)
                            {
                                leadSeen = true;
                                leadFs = cell.FontSize; leadFace = cell.Face;
                                leadFore = cell.Fore;
                                leadBold = boldDepth > 0 || cell.Bold;
                            }
                            if (boldDepth > 0 || cell.Bold) cellBoldChars += ink;
                            else cellPlainChars += ink;
                        }
                        text.Append(ttext);
                    }
                }
                continue;
            }
            var tag = tok.Tag!.ToLowerInvariant();
            // display:none subtree (a hidden pager <select>, a state-carrier <input>):
            // none of its content reaches the cell text.
            if (hiddenDepth > 0)
            {
                if (tag == hiddenTag)
                {
                    if (tok.IsClose) { if (--hiddenDepth == 0) hiddenTag = null; }
                    else if (!tok.IsSelfClosing) hiddenDepth++;
                }
                continue;
            }
            if (!tok.IsClose && IsHiddenElement(tag, tok.Attributes, css))
            {
                if (!tok.IsSelfClosing && !VoidTags.Contains(tag))
                {
                    hiddenTag = tag;
                    hiddenDepth = 1;
                }
                continue;
            }
            if (tok.IsClose)
            {
                if (tag is "td" or "th") CloseCell();
                else if (tag is "tr") { if (nestDepth == 0) CloseRow(); }
                else if (tag is "table") { if (nestDepth > 0) nestDepth--; }
                else if (tag is "b" or "strong")
                {
                    boldDepth = Math.Max(0, boldDepth - 1);
                    if (cell is not null) cellBoldMarks.Add((text.Length, boldDepth > 0));
                }
                else if (tag is "div") { CloseSeg(); pendingAbsLeftFrac = -1.0; }
                else if (tag is "p" && wrapperStacks && !collapsedGrid && cell is not null)
                {
                    // Report cells: the closing paragraph SEGMENT snapshots the
                    // typography its spans left active, and carries the UA
                    // 1.12 em block margins (collapsed between neighbours).
                    if (reportCells && curSeg is not null)
                    {
                        curSeg.FontSize = segInkSeen ? segFs : cell.FontSize;
                        curSeg.Face = segInkSeen ? segFace : cell.Face;
                        // bold by MAJORITY of the paragraph's ink (its strong
                        // runs against its plain runs); style bold always wins
                        curSeg.Bold = cell.Bold || segBoldChars > segPlainChars;
                        curSeg.Fore = segInkSeen ? segFore : cell.Fore;
                        var pFs = cell.FontSize ?? fontSize;
                        if (!curSeg.MarginsExplicit)
                        {
                            curSeg.MarginTopPt = UaBlockMarginEm * pFs;
                            curSeg.MarginBottomPt = UaBlockMarginEm * pFs;
                        }
                        var pMarkers = string.Concat(
                            from Match pm in Regex.Matches(divText.ToString(), "\u0002\\d+\u0003")
                            select pm.Value);
                        if (pMarkers.Length > 0)
                        {
                            var cleaned = Regex.Replace(divText.ToString(), "\u0002\\d+\u0003", " ");
                            divText.Clear();
                            divText.Append(cleaned);
                            text.Append(pMarkers);
                        }
                        CloseSeg();
                    }
                    // other wrapper flows keep the calibrated blank-line gap
                    else if (curSeg is null && text.Length > 0)
                        text.Append('\u0001').Append('\u0001');
                }
                else if (tag is "span" && whiteSpans.Count > 0)
                {
                    if (whiteSpans.Pop()) whiteDepth = Math.Max(0, whiteDepth - 1);
                    // report cells: the span's typography ends here
                    if (reportCells && spanSaves.Count > 0 && cell is not null)
                        (cell.FontSize, cell.Face, cell.Bold, cell.Fore) = spanSaves.Pop();
                }
                continue;
            }
            switch (tag)
            {
                case "table" when sawTable:
                    // a table nested inside an open cell merges its cells into
                    // the OUTER row (the letter's item list sits beside its
                    // label); an empty container cell is discarded, but its
                    // COLSPAN carries over to the first merged cell so the
                    // columns stay aligned under the outer grid.
                    if (cell is not null && IsAllWhitespace(text))
                    {
                        if (cell.ColSpan > 1) pendingNestSpan = cell.ColSpan;
                        cell = null; text.Clear(); cellBoldMarks.Clear();
                    }
                    else CloseCell();
                    nestDepth++;
                    break;
                case "table" when !sawTable:
                    sawTable = true;
                    if (tok.Attributes is { } ta)
                    {
                        // Legacy attribute grid: border=N draws the bordered grid,
                        // align=center centres its box, bordercolor tints the strokes.
                        if (stdSerif && ta.TryGetValue("border", out var bav)
                            && double.TryParse(bav.TrimEnd('p', 'x'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var bavN)
                            && bavN > 0)
                        {
                            bordered = true;
                            borderHugs = true;
                        }
                        if (bordered && ta.TryGetValue("style", out var tcst)
                            && Regex.IsMatch(tcst, @"border-collapse\s*:\s*collapse",
                                RegexOptions.IgnoreCase))
                            attrCollapse = true;
                        if (ta.TryGetValue("align", out var talv)
                            && talv.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
                            centerTable = true;
                        if (ta.TryGetValue("bordercolor", out var tbcv)
                            && ParseCssColor(tbcv.Trim()) is { } tbcol)
                            borderColor = tbcol;
                        if (ta.TryGetValue("bgcolor", out var tabg)
                            && AttrColor(tabg) is { } tabgc)
                            tableBg = tabgc;
                        if (ta.TryGetValue("cellspacing", out var cs) && double.TryParse(cs.TrimEnd('p', 'x'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var csv))
                            s = csv * PxPt;
                        if (rtl && ta.TryGetValue("height", out var thv)
                            && double.TryParse(thv.TrimEnd('p', 'x'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var thPx))
                            tableHeightPt = thPx * PxPt;
                        if (ta.TryGetValue("cellpadding", out var cp) && double.TryParse(cp.TrimEnd('p', 'x'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var cpv))
                            p = cpv * PxPt;
                        if (ta.TryGetValue("class", out var tcls))
                            foreach (var c in tcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (wrapperStacks && css.TryGetValue("." + c, out var cgRule)
                                    && cgRule.TryGetValue("border-collapse", out var cgBc)
                                    && cgBc.Contains("collapse", StringComparison.OrdinalIgnoreCase)
                                    && cgRule.TryGetValue("border-top", out var cgBt))
                                {
                                    collapsedGrid = true;
                                    if (ParseCssColor(cgBt) is { } cgCol) collapsedCol = cgCol;
                                    if (cgRule.TryGetValue("line-height", out var cgLh)
                                        && TryParseLength(cgLh, out var cgLhPt))
                                        collapsedLineH = cgLhPt;
                                }
                                if (css.TryGetValue("." + c, out var cd)
                                    && cd.TryGetValue("margin-left", out var cml)
                                    && TryParseLength(cml, out var cmlPt))
                                    indent += cmlPt;
                                if (css.TryGetValue("table." + c, out var lcd)
                                    && lcd.TryGetValue("table-layout", out var tlv)
                                    && tlv.Contains("fixed", StringComparison.OrdinalIgnoreCase))
                                    layoutFixed = true;
                                // a width class on the table declares its fixed
                                // box (the boleto's .w666 skin); such a class-
                                // framework sheet also zeroes the grid chrome
                                // (table { border-collapse; padding: 0 })
                                // table class TYPOGRAPHY skins every cell that
                                // has no closer declaration (the boleto's ctN table)
                                if (wrapperStacks
                                    && css.TryGetValue("." + c, out var tclsBag))
                                {
                                    var tProbe = new MetricCell();
                                    ApplyCellClassBag(tProbe, tclsBag);
                                    if (tProbe.FontSize is { } tpFs)
                                    { fontSize = tpFs; tableClassFont = true; }
                                }
                                if (wrapperStacks
                                    && css.TryGetValue("." + c, out var wcd)
                                    && wcd.TryGetValue("width", out var wcv)
                                    && TryParseLength(wcv.Trim(), out var wcPt)
                                    && wcPt > 0)
                                {
                                    tableWpt = wcPt;
                                    widthClassTable = true;
                                    if (css.TryGetValue("table", out var shT))
                                    {
                                        if (shT.TryGetValue("border-collapse", out var shBc)
                                            && shBc.Contains("collapse", StringComparison.OrdinalIgnoreCase))
                                            s = 0;
                                        if (shT.TryGetValue("padding", out var shPad))
                                        {
                                            // TryParseLength treats 0 as "no length";
                                            // padding: 0 is a real declaration here
                                            if (Regex.IsMatch(shPad.Trim(), @"^0(px)?$"))
                                                p = 0;
                                            else if (TryParseLength(shPad.Trim(), out var shPadPt))
                                                p = shPadPt;
                                        }
                                    }
                                }
                            }
                        // table width:N% (inline style or attribute): the column grid
                        // scales up to fill the declared share of the content box.
                        var twm = ta.TryGetValue("style", out var tst)
                            ? Regex.Match(tst, @"width\s*:\s*(\d+(?:\.\d+)?)\s*%")
                            : Match.Empty;
                        if (!twm.Success && ta.TryGetValue("width", out var twa))
                            twm = Regex.Match(twa, @"^\s*(\d+(?:\.\d+)?)\s*%");
                        if (twm.Success)
                            double.TryParse(twm.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out tablePct);
                        // width="793" / "1000px": a pixel table width the grid
                        // fills exactly (auto columns share the surplus).
                        else if (ta.TryGetValue("width", out var twpx)
                            && double.TryParse(twpx.Trim().TrimEnd('p', 'x'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var twpxN)
                            && twpxN > 0)
                            tableWpt = twpxN * PxPt;
                    }
                    break;
                case "tr":
                    if (nestDepth > 0) break;         // nested rows merge into the outer row
                    CloseRow();
                    row = new List<MetricCell>();
                    if (tok.Attributes is { } trba && trba.TryGetValue("bgcolor", out var trbg)
                        && AttrColor(trbg) is { } trbgc)
                        rowBg = trbgc;
                    // tr class skins: inheritable typography becomes the row
                    // default, height paces the row, and `.cls td` descendant
                    // bags queue for every cell of the row.
                    if (wrapperStacks && tok.Attributes is { } trka
                        && trka.TryGetValue("class", out var trkc))
                        foreach (var tc in trkc.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            (rowClasses ??= new List<string>()).Add(tc);
                            if (css.TryGetValue("." + tc, out var trBag))
                            {
                                var probe = new MetricCell();
                                ApplyCellClassBag(probe, trBag);
                                if (probe.FontSize is { } pf) { rowFs = pf; rowFsFromClass = true; }
                                if (probe.Face is { } pfa) rowFace = pfa;
                                if (probe.Bold) rowBold = true;
                                if (probe.Fore is { } pfo) rowFore = pfo;
                                // a row class's background tints the row like a
                                // bgcolor attribute (`tr.head { background-color }`)
                                if (probe.Bg is { } pbg) rowBg = pbg;
                                if (probe.VAlignTop) rowVTop = true;
                                if (probe.VAlignBottom) rowVBottom = true;
                                if (trBag.ContainsKey("text-align")) rowAlign = probe.Align;
                                if (trBag.TryGetValue("height", out var trkh))
                                {
                                    var hm3 = Regex.Match(trkh, @"([\d.]+)\s*px");
                                    if (hm3.Success)
                                    {
                                        pendingRowH = DtpNum(hm3.Groups[1].Value) * PxPt;
                                        pendingRowHExact = true;
                                    }
                                }
                            }
                            if (css.TryGetValue("." + tc + " td", out var tdBag))
                                (rowTdBags ??= new List<Dictionary<string, string>>()).Add(tdBag);
                        }
                    if (tok.Attributes is { } tra && tra.TryGetValue("style", out var trst))
                    {
                        // per-row inline styles (the official-letter dialect
                        // sizes and paces every row this way)
                        var fsm = Regex.Match(trst, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                        if (fsm.Success && TryParseCssFontSize(fsm.Groups[1].Value.Trim(), out var trfs))
                            rowFs = trfs;
                        var ham = Regex.Match(trst, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
                        if (ham.Success)
                            rowAlign = ham.Groups[1].Value.ToLowerInvariant() switch
                            {
                                "right" => HorizontalAlignment.Right,
                                "center" => HorizontalAlignment.Center,
                                _ => HorizontalAlignment.Left,
                            };
                        var hm2 = Regex.Match(trst, @"height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                        if (hm2.Success) pendingRowH = DtpNum(hm2.Groups[1].Value) * PxPt;
                    }
                    break;
                case "td":
                case "th":
                    CloseCell();
                    row ??= new List<MetricCell>();
                    cell = new MetricCell { Bold = tag == "th" };
                    if (pendingNestSpan > 1) { cell.ColSpan = pendingNestSpan; pendingNestSpan = 0; }
                    // Browser UA default: <th> content is centered.
                    if (stdSerif && tag == "th") cell.Align = HorizontalAlignment.Center;
                    // The sheet's own th/td element rule styles the cell (the
                    // order-ticket th { font-size: 80%; text-align: left }).
                    if (stdSerif && css.TryGetValue(tag, out var cellTagRule))
                        ApplyCellClassBag(cell, cellTagRule);
                    if (rowFs is { } rfs) { cell.FontSize = rfs; if (rowFsFromClass) cell.FontFromClass = true; }
                    if (rowAlign is { } ra) cell.Align = ra;
                    if (rowBg is { } rbg) cell.Bg = rbg;
                    if (rowFace is { } rfc) cell.Face = rfc;
                    if (rowBold) cell.Bold = true;
                    if (rowFore is { } rfo) cell.Fore = rfo;
                    if (rowVTop) cell.VAlignTop = true;
                    if (rowVBottom) cell.VAlignBottom = true;
                    if (rowTdBags is not null)
                        foreach (var tb in rowTdBags) ApplyCellClassBag(cell, tb);
                    if (tok.Attributes is { } ca)
                    {
                        // NoWrap layout is part of the modern-nesting model;
                        // the dead-css greens stay on their calibrated wrap.
                        if (wrapperStacks && ca.ContainsKey("nowrap")) cell.NoWrap = true;
                        if (ca.TryGetValue("colspan", out var csp)
                            && int.TryParse(csp.Trim(), out var cspN) && cspN > 1)
                            cell.ColSpan = cspN;
                        if (ca.TryGetValue("rowspan", out var rsp)
                            && int.TryParse(rsp.Trim(), out var rspN) && rspN > 1)
                            cell.RowSpan = rspN;
                        if (ca.TryGetValue("bgcolor", out var tdbg)
                            && AttrColor(tdbg) is { } tdbgc)
                            cell.Bg = tdbgc;
                        if (ca.TryGetValue("class", out var tdcls))
                        {
                            cell.ClassNames = new List<string>(
                                tdcls.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                            foreach (var cn in tdcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            {
                                // TAG-prefixed selectors (TD.rubric — the pt-report
                                // sheets) resolve like the bare class.
                                if (!css.TryGetValue("." + cn, out var cnr))
                                    css.TryGetValue("td." + cn, out cnr);
                                if (cnr is null) continue;
                                if (cnr.TryGetValue("font-size", out var cnfs)
                                    && TryParseCssFontSize(cnfs.Trim(), out var cnpt))
                                    cell.FontSize = cnpt;
                                // class-driven cell chrome (the header band
                                // and boleto skins): typography, fill, ink,
                                // geometry and per-side borders
                                if (wrapperStacks)
                                    ApplyCellClassBag(cell, cnr);
                            }
                        }
                        if (ca.TryGetValue("style", out var tdst))
                        {
                            var twm2 = Regex.Match(tdst, @"width\s*:\s*(\d+(?:\.\d+)?)\s*%");
                            if (twm2.Success)
                                cell.WidthPct = double.Parse(twm2.Groups[1].Value,
                                    System.Globalization.CultureInfo.InvariantCulture);
                            // An ABSOLUTE inline width (the SSRS width-setter
                            // rows: `WIDTH: 12.7mm; MIN-WIDTH: 12.7mm`) fixes
                            // the column outright.
                            var twAbs = Regex.Match(tdst,
                                @"(?<![-\w])width\s*:\s*([\d.]+)\s*(mm|cm|in|pt|px)",
                                RegexOptions.IgnoreCase);
                            if (twAbs.Success && double.TryParse(twAbs.Groups[1].Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var twAbsV) && twAbsV > 0)
                                cell.WidthPx = twAbs.Groups[2].Value.ToLowerInvariant() switch
                                {
                                    "mm" => twAbsV * 72.0 / 25.4,
                                    "cm" => twAbsV * 72.0 / 2.54,
                                    "in" => twAbsV * 72.0,
                                    "px" => twAbsV * PxPt,
                                    _ => twAbsV,
                                };
                            if (twAbs.Success
                                && Regex.IsMatch(tdst, @"min-width\s*:", RegexOptions.IgnoreCase))
                                cell.WidthSetterCell = true;
                            // An ABSOLUTE physical-unit inline height (the report
                            // grid's row pacers: `HEIGHT: 6.35mm`) floors the row
                            // band; an EMPTY spacer row is EXACTLY that height.
                            var thAbs = Regex.Match(tdst,
                                @"(?<![-\w])height\s*:\s*([\d.]+)\s*(mm|cm|in|pt)\b",
                                RegexOptions.IgnoreCase);
                            if (thAbs.Success && double.TryParse(thAbs.Groups[1].Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var thAbsV) && thAbsV >= 0)
                                cell.HeightPt = Math.Max(cell.HeightPt,
                                    thAbs.Groups[2].Value.ToLowerInvariant() switch
                                    {
                                        "mm" => thAbsV * 72.0 / 25.4,
                                        "cm" => thAbsV * 72.0 / 2.54,
                                        "in" => thAbsV * 72.0,
                                        _ => thAbsV,
                                    });
                            var tfm = Regex.Match(tdst, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                            if (tfm.Success && TryParseCssFontSize(tfm.Groups[1].Value.Trim(), out var tdfs))
                                cell.FontSize = tdfs;
                            // newsletter cells honor a style padding-top as box space
                            var tptm = Regex.Match(tdst, @"padding-top\s*:\s*(\d+(?:\.\d+)?)\s*px",
                                RegexOptions.IgnoreCase);
                            if (reportCells && tptm.Success)
                                cell.PadTopPt = double.Parse(tptm.Groups[1].Value,
                                    System.Globalization.CultureInfo.InvariantCulture) * PxPt;
                            var tam = Regex.Match(tdst, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
                            if (tam.Success)
                                cell.Align = tam.Groups[1].Value.ToLowerInvariant() switch
                                {
                                    "right" => HorizontalAlignment.Right,
                                    "center" => HorizontalAlignment.Center,
                                    _ => HorizontalAlignment.Left,
                                };
                            var tbgm = Regex.Match(tdst, @"background(?:-color)?\s*:\s*([^;]+)",
                                RegexOptions.IgnoreCase);
                            if (tbgm.Success && ParseCssColor(tbgm.Groups[1].Value.Trim()) is { } tdsbg)
                                cell.Bg = tdsbg;
                            var tcm = Regex.Match(tdst, @"(?<![-\w])color\s*:\s*([^;]+)",
                                RegexOptions.IgnoreCase);
                            if (tcm.Success && ParseCssColor(tcm.Groups[1].Value.Trim()) is { } tdcol
                                && (tdcol.R != 0 || tdcol.G != 0 || tdcol.B != 0))
                                cell.Fore = tdcol;
                            if (Regex.IsMatch(tdst, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
                                cell.Bold = true;
                            if (Regex.IsMatch(tdst, @"white-space\s*:\s*nowrap", RegexOptions.IgnoreCase))
                                cell.NoWrap = true;
                            // a style border-right draws that one edge (the legacy
                            // separator-column idiom: border-right: solid black 2px)
                            var brm = Regex.Match(tdst,
                                @"border-right\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                            if (brm.Success)
                            {
                                var brv = brm.Groups[1].Value;
                                var bwm = Regex.Match(brv, @"([\d.]+)\s*px");
                                if (bwm.Success && brv.Contains("solid", StringComparison.OrdinalIgnoreCase))
                                {
                                    cell.BorderRightW = DtpNum(bwm.Groups[1].Value) * PxPt;
                                    if (ParseCssColor(Regex.Replace(brv,
                                            @"solid|[\d.]+\s*px", "", RegexOptions.IgnoreCase).Trim())
                                        is { } brc)
                                        cell.BorderRightCol = brc;
                                }
                            }
                        }
                        if (ca.TryGetValue("valign", out var va))
                        {
                            if (va.Trim().Equals("top", StringComparison.OrdinalIgnoreCase))
                            { cell.VAlignTop = true; cell.VAlignBottom = false; }
                            else if (va.Trim().Equals("bottom", StringComparison.OrdinalIgnoreCase))
                            { cell.VAlignBottom = true; cell.VAlignTop = false; }
                        }
                        if (ca.TryGetValue("align", out var al))
                            cell.Align = al.Trim().ToLowerInvariant() switch
                            {
                                "right" => HorizontalAlignment.Right,
                                "center" => HorizontalAlignment.Center,
                                _ => HorizontalAlignment.Left,
                            };
                        if (ca.TryGetValue("width", out var wv) && wv.Trim().EndsWith('%')
                            && double.TryParse(wv.Trim().TrimEnd('%'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var pct))
                            cell.WidthPct = pct;
                        // width="300" / width="300px": a pixel width fixes the
                        // column's content width outright (legacy attribute grid).
                        else if (ca.TryGetValue("width", out var wpv)
                            && double.TryParse(wpv.Trim().TrimEnd('p', 'x'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var wpx)
                            && wpx > 0)
                            cell.WidthPx = wpx * PxPt;
                        // height="69": a cell's pixel height floors its whole row
                        // (the RTL attr grid's banded rows; the report flow's
                        // spacer rows pace on it too).
                        if ((rtl || reportCells) && ca.TryGetValue("height", out var hpv)
                            && double.TryParse(hpv.Trim().TrimEnd('p', 'x'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var hpx)
                            && hpx * PxPt > pendingRowH)
                            pendingRowH = hpx * PxPt;
                    }
                    break;
                case "b":
                case "strong":
                    boldDepth++;
                    if (cell is not null) cellBoldMarks.Add((text.Length, true));
                    // report cells account bold per RUN (boldDepth); the
                    // whole-cell flag stays a legacy-flow behaviour
                    if (cell is not null && !reportCells) cell.Bold = true;
                    break;
                case "font":
                    // A <font> tag styles the rest of its cell — face, color, and the
                    // legacy 1..7 size ladder (the source renderer applies the tag's
                    // attributes to contained children, self-closing form included).
                    if (cell is not null && tok.Attributes is { } fa)
                    {
                        // A face the source engine does not resolve keeps the flow
                        // default (David and friends draw the UA serif there).
                        if (fa.TryGetValue("face", out var ffv)
                            && FirstFontFamily(ffv) is { Length: > 0 } ffam
                            && (!stdSerif || SourceEngineFaces.Contains(ffam)))
                            cell.Face = ffam;
                        if (fa.TryGetValue("color", out var fcv)
                            && ParseCssColor(fcv.Trim()) is { } fcol)
                            cell.Fore = fcol;
                        if (fa.TryGetValue("size", out var fsv)
                            && TryParseHtmlFontSize(fsv, out var fszPt))
                        {
                            cell.FontSize = fszPt;
                            cell.FontTagSized = true;
                        }
                        // an inline style on the font tag sizes the cell in points
                        // (`<font style="FONT-SIZE: 14pt">` — the RTL grid's dates)
                        if (fa.TryGetValue("style", out var fstv)
                            && Regex.Match(fstv, @"font-size\s*:\s*([\d.]+)\s*pt",
                                RegexOptions.IgnoreCase) is { Success: true } fptM)
                        {
                            cell.FontSize = double.Parse(fptM.Groups[1].Value,
                                System.Globalization.CultureInfo.InvariantCulture);
                            cell.FontTagSized = true;
                        }
                    }
                    break;
                case "a":
                    // The anchor's colour — its inline style, else the sheet's `a`
                    // rule — inks its text; like <font color> it styles the rest
                    // of its cell (cells wrap their whole content in one <a>).
                    if (cell is not null)
                    {
                        Color? aFore = null;
                        if (tok.Attributes is { } aatt && aatt.TryGetValue("style", out var ast)
                            && Regex.Match(ast, @"(?<![-\w])color\s*:\s*([^;]+)",
                                RegexOptions.IgnoreCase) is { Success: true } astm)
                            aFore = ParseCssColor(astm.Groups[1].Value.Trim());
                        aFore ??= rmtAnchorColor;
                        if (aFore is not null) cell.Fore = aFore;
                    }
                    break;
                case "span":
                {
                    // The sheet's class rules style the span's cell (the
                    // .firm { font-size: 400% } masthead on the 12 pt base).
                    if (stdSerif && cell is not null && tok.Attributes is { } spCls0
                        && spCls0.TryGetValue("class", out var spClsV) && spClsV is not null)
                        foreach (var sc0 in spClsV.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue("." + sc0, out var spRule0))
                                ApplyCellClassBag(cell, spRule0);
                    var sWhite = false;
                    if (tok.Attributes is { } sa0 && sa0.TryGetValue("style", out var sst0)
                        && Regex.IsMatch(sst0, @"color\s*:\s*(white|#fff(?:fff)?)\b", RegexOptions.IgnoreCase))
                    {
                        // White ink over an UNFILLED cell is invisible — it keeps its
                        // advance as spaces (the official-letter dialect). Over a
                        // bgcolor-filled cell/row/table it is REAL ink and draws white.
                        if (cell is not null
                            && (cell.Bg is not null || rowBg is not null || tableBg is not null))
                            cell.Fore = Color.FromRgb(255, 255, 255);
                        else
                        {
                            sWhite = true;
                            whiteDepth++;
                        }
                    }
                    if (!tok.IsSelfClosing) whiteSpans.Push(sWhite);
                    if (reportCells && !tok.IsSelfClosing && cell is not null)
                        spanSaves.Push((cell.FontSize, cell.Face, cell.Bold, cell.Fore));
                    if (cell is not null && tok.Attributes is { } sa
                        && sa.TryGetValue("style", out var sst))
                    {
                    // quote entities decode BEFORE the property scan — the ';'
                    // inside &quot; would otherwise truncate a value mid-entity
                    // (font-family: &quot;Arial&quot; parsed as the face '&quot')
                    if (sst.IndexOf('&') >= 0)
                        sst = sst.Replace("&quot;", "\"").Replace("&#34;", "\"")
                                 .Replace("&apos;", "'").Replace("&#39;", "'");
                    // WIDTH:Npx; DISPLAY:inline-table — the span fixes its column's
                    // content width and grows the line box.
                    var wm = Regex.Match(sst, @"width\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
                    if (wm.Success && Regex.IsMatch(sst, @"display\s*:\s*inline-table", RegexOptions.IgnoreCase))
                    {
                        cell.HasSpan = true;
                        cell.SpanW = Math.Max(cell.SpanW, double.Parse(wm.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) * PxPt);
                    }
                    // Inline span typography styles the rest of its cell — the
                    // legacy corpus wraps whole cell contents in one styled span.
                    var sfm = Regex.Match(sst, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                    if (sfm.Success && FirstFontFamily(sfm.Groups[1].Value) is { Length: > 0 } sfam)
                        cell.Face = sfam;
                    var ssm = Regex.Match(sst, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                    // font-size: larger is RELATIVE — 1.2 x the cell's current
                    // size (13px title → 15.6px = 11.7 pt, measured), so it must
                    // beat the keyword table's fixed UA-base mapping.
                    if (ssm.Success && ssm.Groups[1].Value.Trim()
                            .Equals("larger", StringComparison.OrdinalIgnoreCase))
                        cell.FontSize = HtmlLargerStepPt(cell.FontSize ?? fontSize);
                    else if (ssm.Success && TryParseCssFontSize(ssm.Groups[1].Value.Trim(), out var sfs))
                        cell.FontSize = sfs;
                    if (Regex.IsMatch(sst, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
                        cell.Italic = true;
                    if (Regex.IsMatch(sst, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
                        cell.Bold = true;
                    var scm = Regex.Match(sst, @"(?<![-\w])color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                    if (scm.Success && ParseCssColor(scm.Groups[1].Value.Trim()) is { } scol
                        && (scol.R != 255 || scol.G != 255 || scol.B != 255))
                        cell.Fore = scol;
                    }
                    break;
                }
                case "div":
                    // Div-stacked cell content (the .t/.c ladders): each div is
                    // one styled line; its classes resolve directly and through
                    // the row's descendant rules ('.rc6 .t', '.rc6 div'). The
                    // collapsed CLASS grid keeps its calibrated concatenation;
                    // the element-rule collapse grid needs its div bands (the
                    // green bar + abs image).
                    if (wrapperStacks && (!collapsedGrid || elemCollapseGrid) && cell is not null)
                    {
                        if (tok.IsClose) { CloseSeg(); break; }
                        CloseSeg();
                        var seg = new MetricDivSeg();
                        var segProbe = new MetricCell();
                        if (rowClasses is not null)
                            foreach (var rcn in rowClasses)
                                if (css.TryGetValue("." + rcn + " div", out var rdivBag))
                                    ApplyCellClassBag(segProbe, rdivBag);
                        if (tok.Attributes is { } da && da.TryGetValue("class", out var dcls))
                            foreach (var dcn in dcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (css.TryGetValue("." + dcn, out var dBag))
                                    ApplyCellClassBag(segProbe, dBag);
                                if (rowClasses is not null)
                                    foreach (var rcn in rowClasses)
                                        if (css.TryGetValue("." + rcn + " ." + dcn, out var rdBag))
                                            ApplyCellClassBag(segProbe, rdBag);
                                if (css.TryGetValue("." + dcn, out var dbb)
                                    && dbb.ContainsKey("border-bottom"))
                                    seg.BorderBottom = true;
                            }
                        seg.FontSize = segProbe.FontSize;
                        seg.Face = segProbe.Face;
                        seg.Bold = segProbe.Bold;
                        seg.Fore = segProbe.Fore;
                        seg.LineBoxPt = segProbe.HeightPt;
                        seg.PadLeft = segProbe.PadLeft > 0 ? segProbe.PadLeft : 0;
                        seg.Bg = segProbe.Bg;
                        // An absolutely positioned div (left:N%) is OUT of the
                        // band flow — its image draws at the offset instead.
                        if (tok.Attributes is { } absDa
                            && absDa.TryGetValue("style", out var absSt)
                            && Regex.IsMatch(absSt, @"position\s*:\s*absolute",
                                RegexOptions.IgnoreCase)
                            && Regex.Match(absSt, @"left\s*:\s*([\d.]+)\s*%",
                                RegexOptions.IgnoreCase) is { Success: true } absLm
                            && double.TryParse(absLm.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var absLv))
                            pendingAbsLeftFrac = absLv / 100.0;
                        curSeg = seg;
                    }
                    break;
                case "img":
                    // an image reserves its DECLARED box even when the file is
                    // unreadable — the row paces on it (the boleto's 40px logo)
                    if (wrapperStacks && cell is not null)
                    {
                        double imgH = 0;
                        if (tok.Attributes is { } ia && ia.TryGetValue("height", out var ihv)
                            && double.TryParse(ihv.Trim().TrimEnd('p', 'x'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var ihn))
                            imgH = ihn * PxPt;
                        if (imgH <= 0 && cell.ClassNames is not null)
                            foreach (var icn in cell.ClassNames)
                                if (css.TryGetValue("." + icn + " img", out var imgRule)
                                    && imgRule.TryGetValue("height", out var irh)
                                    && TryParseLength(irh.Trim(), out var irhPt))
                                    imgH = Math.Max(imgH, irhPt);
                        cell.ImgHPt = Math.Max(cell.ImgHPt, imgH);
                        if (tok.Attributes is { } iaw && iaw.TryGetValue("width", out var iwv)
                            && double.TryParse(iwv.Trim().TrimEnd('p', 'x'),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var iwn))
                            cell.ImgWPt = Math.Max(cell.ImgWPt, iwn * PxPt);
                        // A data-URI JPEG draws at its INTRINSIC aspect, ignoring
                        // the width/height attributes: an oversized photo clamps
                        // to the engine's image viewport and overflows its column
                        // (measured: a 1024×768 px photo lands 612×459 pt).
                        if (tok.Attributes is { } iaJ && iaJ.TryGetValue("src", out var jsrc)
                            && Regex.Match(jsrc, @"^data:image/jpe?g;base64,(.+)$",
                                RegexOptions.IgnoreCase | RegexOptions.Singleline)
                                is { Success: true } jdm)
                        {
                            byte[]? jb = null;
                            try { jb = System.Convert.FromBase64String(jdm.Groups[1].Value); }
                            catch { }
                            if (jb is not null && JpegDims(jb) is { w: > 0, h: > 0 } jd)
                            {
                                var jNatW = jd.w * PxPt;
                                var jDrawW = Math.Min(jNatW, JpegViewportPt);
                                cell.ImgJpegBytes = jb;
                                cell.ImgWPt = jDrawW;
                                cell.ImgHPt = jDrawW * jd.h / jd.w;
                            }
                        }
                        // A data-URI PNG inside an abs-positioned div (left:N%):
                        // drawn at natural size at the offset, out of the flow.
                        if (pendingAbsLeftFrac >= 0 && cell.AbsPng is null
                            && tok.Attributes is { } iaP
                            && iaP.TryGetValue("src", out var psrc)
                            && Regex.Match(psrc, @"^data:image/png;base64,(.+)$",
                                RegexOptions.IgnoreCase | RegexOptions.Singleline)
                                is { Success: true } pdm)
                        {
                            try
                            {
                                cell.AbsPng = System.Convert.FromBase64String(pdm.Groups[1].Value);
                                cell.AbsPngLeftFrac = pendingAbsLeftFrac;
                            }
                            catch { }
                        }
                        // a REMOTE image the renderer cannot fetch shows its alt
                        // text in the reserved box (the source engine's broken-
                        // image behaviour — the header logo draws its name)
                        if (reportCells && tok.Attributes is { } iaAlt
                            && iaAlt.TryGetValue("alt", out var altT)
                            && !string.IsNullOrWhiteSpace(altT)
                            && iaAlt.TryGetValue("src", out var altSrc)
                            && altSrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            if (curSeg is null && CollapseWs(text.ToString()).Trim(' ').Length == 0)
                                cell.AltTextOnly = true;
                            (curSeg is not null ? divText : text)
                                .Append(' ').Append(DecodeEntities(altT)).Append(' ');
                        }
                    }
                    break;
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    // A heading inside a cell styles the rest of the cell: UA
                    // bold plus the sheet's own element rule (the order ticket's
                    // h1 { font-size: 120% } on the 12 pt base).
                    if (stdSerif && cell is not null)
                    {
                        cell.Bold = true;
                        if (css.TryGetValue(tag, out var cellHeadRule))
                            ApplyCellClassBag(cell, cellHeadRule);
                    }
                    break;
                case "p":
                    // The sheet's tag.class / class rules style the paragraph's
                    // cell (`P.order { font-size: 120% }` on the 12 pt base).
                    if (stdSerif && cell is not null && tok.Attributes is { } pAttrs0
                        && pAttrs0.TryGetValue("class", out var pCls0) && pCls0 is not null)
                        foreach (var pc0 in pCls0.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue(tag + "." + pc0, out var pRule0)
                                || css.TryGetValue("." + pc0, out pRule0))
                                ApplyCellClassBag(cell, pRule0);
                    // Report cells: each paragraph is its own SEGMENT with the
                    // typography its spans set. The lead text (a styled heading
                    // span) becomes the first segment so a later span cannot
                    // restyle it retroactively; sub-table markers stay in the
                    // cell text for CloseCell's lift.
                    if (reportCells && !collapsedGrid && cell is not null)
                    {
                        if (curSeg is null && text.Length > 0)
                        {
                            var leadStr = text.ToString();
                            var leadMarkers = string.Concat(
                                from Match lm in Regex.Matches(leadStr, "\u0002\\d+\u0003")
                                select lm.Value);
                            leadStr = Regex.Replace(leadStr, "\u0002\\d+\u0003", " ");
                            text.Clear();
                            text.Append(leadMarkers);
                            if (CollapseWs(leadStr).Trim(' ').Length > 0)
                            {
                                // the lead draws with the typography its first
                                // ink SAW — its spans have closed and restored
                                // the cell state by now
                                curSeg = new MetricDivSeg
                                {
                                    FontSize = leadSeen ? leadFs : cell.FontSize,
                                    Face = leadSeen ? leadFace : cell.Face,
                                    Bold = leadSeen ? leadBold : cell.Bold,
                                    Fore = leadSeen ? leadFore : cell.Fore,
                                };
                                divText.Append(leadStr);
                                CloseSeg();
                            }
                            leadSeen = false;
                        }
                        CloseSeg();
                        curSeg = new MetricDivSeg();
                        // the paragraph's class authors its own margins
                        // (`margin: 0pt …`) — they replace the UA block margins
                        if (tok.Attributes is { } pMa
                            && pMa.TryGetValue("class", out var pMCls) && pMCls is not null)
                            foreach (var pmc in pMCls.Split(' ',
                                StringSplitOptions.RemoveEmptyEntries))
                                if (css.TryGetValue("." + pmc, out var pmr)
                                    && pmr.TryGetValue("margin", out var pmv))
                                {
                                    var pmParts = pmv.Trim().Split(' ',
                                        StringSplitOptions.RemoveEmptyEntries);
                                    if (pmParts.Length > 0)
                                    {
                                        curSeg.MarginsExplicit = true;
                                        curSeg.MarginTopPt = TryParseLength(
                                            pmParts[0], out var pmT) ? pmT : 0;
                                        var pmBi = pmParts.Length >= 3 ? 2 : 0;
                                        curSeg.MarginBottomPt = TryParseLength(
                                            pmParts[pmBi], out var pmB) ? pmB : 0;
                                    }
                                }
                    }
                    break;
                case "br":
                    // a <br> inside a cell is a hard line break (the letter's
                    // item list stacks its items with them)
                    if (cell is not null)
                        (curSeg is not null ? divText : text).Append('\u0001');
                    break;
                case "thead":
                case "tbody":
                case "tfoot":
                    if (nestDepth == 0)
                        curSection = tag == "thead" ? 0 : tag == "tfoot" ? 2 : 1;
                    break;
            }
        }
        CloseRow();
        if (rows.Count == 0) return;

        // Reorder row groups: thead, then tbody, then tfoot — each group keeping
        // its source order (a tfoot authored before the tbody still closes the
        // table; rowHeights travels with its row).
        if (rowSections.Contains(0) || rowSections.Contains(2))
        {
            var order = Enumerable.Range(0, rows.Count)
                .OrderBy(i => rowSections[i]).ToArray();
            rows = order.Select(i => rows[i]).ToList();
            rowHeights = order.Select(i => rowHeights[i]).ToList();
            rowHeightExact = order.Select(i => rowHeightExact[i]).ToList();
        }

        // A table HEIGHT attribute scales the declared row heights up
        // proportionally to fill it (probed: 19/69/22 px rows in a height=147
        // table land at 25.39/92.21/29.40 px).
        if (tableHeightPt > 0)
        {
            double declSum = 0;
            foreach (var rh in rowHeights) declSum += rh;
            if (declSum > 0 && declSum < tableHeightPt)
                for (var ri = 0; ri < rowHeights.Count; ri++)
                    rowHeights[ri] += (tableHeightPt - declSum) * rowHeights[ri] / declSum;
        }

        // colspan: a spanning cell keeps its own column and occupies phantom
        // empty slots after it, so per-column index arithmetic stays intact;
        // the wrap and draw passes extend the real cell's box over its phantoms.
        foreach (var r0 in rows)
            for (var i0 = 0; i0 < r0.Count; i0++)
                if (r0[i0].ColSpan > 1)
                    for (var k0 = 1; k0 < r0[i0].ColSpan; k0++)
                        r0.Insert(i0 + k0, new MetricCell { Text = "", Phantom = true });
        var nCols = 0;
        foreach (var r in rows) nCols = Math.Max(nCols, r.Count);
        // RTL document: cells fill columns from the RIGHT — mirror every row
        // onto the LTR grid (pad the visual-left slots, reverse, and move each
        // spanning cell back AHEAD of its phantom slots so the LTR draw loop's
        // spanner-then-phantoms convention holds).
        if (rtl)
            foreach (var rr in rows)
            {
                while (rr.Count < nCols) rr.Add(new MetricCell { Text = "", Phantom = true });
                rr.Reverse();
                for (var i = 0; i < rr.Count; i++)
                    if (rr[i].ColSpan > 1)
                    {
                        var lead = i;
                        var k = rr[i].ColSpan - 1;
                        while (k-- > 0 && lead > 0 && rr[lead - 1].Phantom) lead--;
                        if (lead < i)
                        {
                            var spanner = rr[i];
                            rr.RemoveAt(i);
                            rr.Insert(lead, spanner);
                        }
                    }
            }
        var tableX = marginLeft + indent;
        var availW = contentWidth - indent;

        // Column content widths: an inline-table span fixes the column; a width="%"
        // attribute takes its share of the table box; otherwise the widest measured
        // cell line. Over-wide natural columns are clamped from the right.
        var colW = new double[nCols];
        var colPct = new double[nCols];
        var colPx = new double[nCols];
        var colFixed = new bool[nCols];
        foreach (var r in rows)
            for (var c = 0; c < r.Count; c++)
            {
                if (r[c].SpanW > 0 && r[c].SpanW > (colFixed[c] ? colW[c] : 0))
                { colW[c] = r[c].SpanW; colFixed[c] = true; }
                var cSpan = Math.Max(1, r[c].ColSpan);
                // RTL attribute grids and the pt-report mode: a SPANNING cell's
                // declared width never pins the slots it crosses — the
                // non-spanning cells' declared widths fix their columns and the
                // spanner rides over them (measured: the 600px colspan cell
                // lands at 561.75 − the 19/98/91 px columns; the report's
                // width=84% colspan=3 cell must not widen its middle columns).
                if ((rtl || (!stdSerif && wrapperStacks)) && cSpan > 1) continue;
                for (var k = 0; k < cSpan && c + k < nCols; k++)
                {
                    if (r[c].WidthPct > 0)
                        colPct[c + k] = Math.Max(colPct[c + k], r[c].WidthPct / cSpan);
                    if (r[c].WidthPx > 0)
                        colPx[c + k] = Math.Max(colPx[c + k], r[c].WidthPx / cSpan);
                }
            }
        // Bordered mode: CSS table column resolution against the availW box.
        // FIXED layout: each column takes its declared percent of the table's
        // inner width (inside the outer border) — content neither wraps nor
        // widens it, so a long word OVERFLOWS across the neighbour (and the
        // table's chrome pushes its box past the declared width). AUTO layout:
        // a column is max(declared share, min-content) and — under width:100% —
        // the leftover goes to the LAST column (all measured on the reference).
        if (bordered)
        {
            var innerW = availW - 2 * bw;
            if (attrCollapse)
            {
                // Shared borders: a pixel column's box = its content plus the two
                // half-borders it absorbs; the percent columns split what the
                // table box leaves beside those (each taking its declared share
                // and losing its own shared borders). No symmetric inset and no
                // over-full shrink — the declared table keeps its width.
                double pxBoxes = 0;
                for (var c = 0; c < nCols; c++)
                    if (colPx[c] > 0)
                    {
                        colW[c] = colPx[c];
                        colFixed[c] = true;
                        pxBoxes += colPx[c] + 2 * bw;
                    }
                var pctBase = availW - 2 * bw - pxBoxes;
                for (var c = 0; c < nCols; c++)
                {
                    if (colFixed[c]) continue;
                    if (colPct[c] > 0)
                        colW[c] = colPct[c] / 100.0 * pctBase - 2 * p - 2 * bw;
                    else
                        foreach (var r in rows)
                            if (c < r.Count && r[c].Text.Length > 0)
                                colW[c] = Math.Max(colW[c], MeasureFaceText(
                                    CellFaceName(r[c]), r[c].Text.Replace('\u0001', ' '),
                                    r[c].FontSize ?? fontSize));
                    colFixed[c] = true;
                }
            }
            else if (layoutFixed)
            {
                for (var c = 0; c < nCols; c++)
                    colW[c] = Math.Max(fontSize, colPct[c] / 100.0 * innerW);
            }
            else
            {
                var chromeB = 2 * bw + (nCols + 1) * s + nCols * (2 * p + 2 * bw);
                var naturalB = new double[nCols];
                for (var c = 0; c < nCols; c++)
                {
                    foreach (var r in rows)
                        if (c < r.Count && r[c].Text.Length > 0)
                            naturalB[c] = Math.Max(naturalB[c],
                                MeasureFaceText(r[c].Bold ? boldFace : face, r[c].Text,
                                    r[c].FontSize ?? fontSize));
                    var share = colPct[c] > 0 ? colPct[c] / 100.0 * innerW - 2 * p - 2 * bw : 0;
                    // A pixel width attribute fixes the column (its text wraps at
                    // that width instead of widening it) — but a larger declared
                    // SHARE still wins (measured: a 50% column beats its 366px
                    // content cells).
                    if (colPx[c] > 0) { colW[c] = Math.Max(colPx[c] - 2 * p, share); continue; }
                    colW[c] = Math.Max(naturalB[c], share);
                }
                // Attribute grid: undeclared columns take what the grid box leaves
                // beside the pixel-fixed ones, floored at their min-content (the
                // widest unbreakable chunk) — an over-long word overflows the box
                // rather than shrinking below it. The grid box is the SYMMETRIC
                // content frame (one UA body margin inside the right content edge
                // too), and the outer border straddles OUTSIDE it — measured: the
                // grid spans 96..499 with its outer border edge at 500.5.
                var availB = availW - symInsetPt;
                var gridChrome = chromeB - 2 * bw;
                if (borderHugs)
                {
                    double sumH = 0; foreach (var w in colW) sumH += w;
                    if (sumH + gridChrome > availB)
                        for (var c = nCols - 1; c >= 0; c--)
                            if (colPx[c] <= 0 && colW[c] > 0)
                            {
                                double others = 0;
                                for (var o = 0; o < nCols; o++) if (o != c) others += colW[o];
                                double minC = 0;
                                foreach (var r in rows)
                                    if (c < r.Count && r[c].Text.Length > 0)
                                        foreach (var seg in DashSegments(r[c].Text.Replace('\u0001', ' ')))
                                            minC = Math.Max(minC, MeasureFaceText(
                                                r[c].Bold ? boldFace : face, seg, r[c].FontSize ?? fontSize));
                                colW[c] = Math.Max(minC, availB - gridChrome - others);
                                break;
                            }
                }
                // Declared shares apply only when they FIT beside the min-contents;
                // an over-constrained set falls back to min-content columns (the
                // 15%-column's long word forces its share past 15, so the 85%
                // partner cannot keep 85 — measured: it takes the REMAINDER).
                // Attribute grids resolved their overflow above — pixel-fixed
                // columns must not fall back to natural widths.
                double sumB = 0; foreach (var w in colW) sumB += w;
                if (sumB + chromeB > availW && !borderHugs)
                {
                    Array.Copy(naturalB, colW, nCols);
                    sumB = 0; foreach (var w in colW) sumB += w;
                }
                // Natural columns that STILL over-fill the box give the deficit
                // back ∝ their slack (max-content − min-content), floored at
                // min-content, and their text wraps at the solved width — the
                // banked auto-width rule (measured on the aggregate grid:
                // 62.1/79.2/64.1/66.2/58.4/52.1, reproduced within 0.3 pt).
                // An attribute grid reaches here only when its own last-column
                // shrink bottomed out at min-content and the grid still spills.
                var bankAvail = borderHugs ? availB - gridChrome + chromeB : availW;
                sumB = 0; foreach (var w in colW) sumB += w;
                if (sumB + chromeB > bankAvail)
                {
                    var minB = new double[nCols];
                    for (var c = 0; c < nCols; c++)
                        foreach (var r in rows)
                            if (c < r.Count && r[c].Text.Length > 0 && r[c].ColSpan <= 1)
                                foreach (var wSeg in r[c].Text.Replace('\u0001', ' ')
                                             .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                                    minB[c] = Math.Max(minB[c], MeasureFaceText(
                                        r[c].Bold ? boldFace : face, wSeg,
                                        r[c].FontSize ?? fontSize));
                    double slackSum = 0;
                    for (var c = 0; c < nCols; c++) slackSum += Math.Max(0, colW[c] - minB[c]);
                    var deficitB = sumB + chromeB - bankAvail;
                    if (slackSum > 0)
                        for (var c = 0; c < nCols; c++)
                            colW[c] -= Math.Min(
                                deficitB * Math.Max(0, colW[c] - minB[c]) / slackSum,
                                Math.Max(0, colW[c] - minB[c]));
                }
                if (tableFills)
                {
                    var leftoverB = availW - chromeB - sumB;
                    if (leftoverB > 0 && nCols > 0) colW[nCols - 1] += leftoverB;
                }
            }
        }
        // Outer-frame collapse grid: every column box (content + 2·padding)
        // shares the symmetric grid box minus the two half-frames; an
        // over-declared set gives its deficit back ∝ slack (declared −
        // min-content), floored at min-content — the banked auto-width rule.
        if (collapseBoxW > 0 && nCols > 0)
        {
            var cbAvail = availW - symInsetPt - collapseBoxW;
            var cbDeclBox = new double[nCols];
            var cbMinBox = new double[nCols];
            for (var c = 0; c < nCols; c++)
            {
                double minC = 0;
                foreach (var r in rows)
                    if (c < r.Count && r[c].ColSpan <= 1 && r[c].Text.Length > 0)
                        foreach (var word in r[c].Text.Replace('\u0001', ' ')
                                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            minC = Math.Max(minC, MeasureFaceText(
                                CellFaceName(r[c]), word, r[c].FontSize ?? fontSize));
                cbMinBox[c] = minC + 2 * p;
                cbDeclBox[c] = colPx[c] > 0 ? colPx[c] + 2 * p : cbMinBox[c];
            }
            double cbSumDecl = 0, cbSumSlack = 0;
            for (var c = 0; c < nCols; c++)
            {
                cbSumDecl += cbDeclBox[c];
                cbSumSlack += Math.Max(0, cbDeclBox[c] - cbMinBox[c]);
            }
            var cbDeficit = cbSumDecl - cbAvail;
            for (var c = 0; c < nCols; c++)
            {
                var box = cbDeclBox[c];
                if (cbDeficit > 0 && cbSumSlack > 0)
                    box = Math.Max(cbMinBox[c], cbDeclBox[c]
                        - cbDeficit * Math.Max(0, cbDeclBox[c] - cbMinBox[c]) / cbSumSlack);
                colW[c] = box - 2 * p;
                colFixed[c] = true;
            }
        }
        var usableW = availW - (nCols + 1) * s;
        // stdSerif percent grid: browser auto layout, measured on the reference —
        // declared share of the SYMMETRIC usable box (one UA body margin inside
        // the right content edge too), w = max(declared, min-content); an
        // over-full set gives its deficit back proportionally to each column's
        // SLACK (w − min-content). Non-percent columns ride along at their
        // natural width with zero slack.
        var uaPctGrid = false;
        if (stdSerif && !bordered)
            foreach (var pc in colPct) if (pc > 0) { uaPctGrid = true; break; }
        if (uaPctGrid)
        {
            var usableSym = availW - symInsetPt - (nCols + 1) * s;
            var minCol = new double[nCols];
            for (var c = 0; c < nCols; c++)
            {
                foreach (var r in rows)
                {
                    if (c < r.Count && r[c].SubTables is { Count: > 0 } pctSubs)
                        foreach (var sub in pctSubs)
                            foreach (var seg in DashSegments(CollapseWs(DecodeEntities(
                                Regex.Replace(sub, "<[^>]+>", " ")))))
                                minCol[c] = Math.Max(minCol[c], MeasureFaceText(
                                    CellFaceName(r[c]), seg, r[c].FontSize ?? fontSize) + 2 * p);
                    if (c < r.Count && r[c].Text.Length > 0)
                    {
                        // a NOWRAP cell's min-content is its WHOLE text
                        if (r[c].NoWrap)
                        {
                            minCol[c] = Math.Max(minCol[c], MeasureFaceText(
                                CellFaceName(r[c]), r[c].Text.Replace('\u0001', ' '),
                                r[c].FontSize ?? fontSize));
                            continue;
                        }
                        foreach (var seg in DashSegments(r[c].Text.Replace('\u0001', ' ')))
                            minCol[c] = Math.Max(minCol[c], MeasureFaceText(
                                CellFaceName(r[c]), seg, r[c].FontSize ?? fontSize));
                    }
                }
                if (colFixed[c]) { minCol[c] = colW[c]; continue; }
                var decl = colPct[c] > 0 ? colPct[c] / 100.0 * usableSym - 2 * p : minCol[c];
                colW[c] = Math.Max(decl, minCol[c]);
                colFixed[c] = true;
            }
            double sumCol = nCols * 2 * p;
            foreach (var w in colW) sumCol += w;
            var deficit = sumCol - usableSym;
            if (deficit > 0)
            {
                double slackSum = 0;
                for (var c = 0; c < nCols; c++) slackSum += Math.Max(0, colW[c] - minCol[c]);
                if (slackSum > 0)
                    for (var c = 0; c < nCols; c++)
                        colW[c] -= deficit * Math.Max(0, colW[c] - minCol[c]) / slackSum;
            }
            // …and a width:100% table's UNDECLARED columns absorb the surplus,
            // proportionally to their content (the label grid's value column
            // takes everything the 20% label leaves).
            else if (deficit < 0 && tablePct > 0)
            {
                var surplus = -deficit;
                for (var c = nCols - 1; c >= 0; c--)
                    if (colPct[c] <= 0)
                    {
                        colW[c] += surplus;
                        break;
                    }
            }
        }
        for (var c = 0; c < nCols && !bordered; c++)
        {
            if (colFixed[c]) continue;
            if (colPct[c] > 0) { colW[c] = colPct[c] / 100.0 * usableW - 2 * p; colFixed[c] = true; continue; }
            // Modern-nesting model: a width attribute or class fixes its column —
            // the nested grids and class-framework grids wrap at their declared
            // cols instead of their natural text extents.
            if (wrapperStacks && (tablePct == 0 && !tableFills || !stdSerif)
                && colPx[c] > 0)
            { colW[c] = colPx[c]; colFixed[c] = true; continue; }
            foreach (var r in rows)
            {
                // a SPANNING cell stretches over several columns and must
                // not pin its first one to its whole width; an alt-text cell
                // sizes to its image BOX, not to the alt's unwrapped advance
                if (c < r.Count && r[c].Text.Length > 0 && r[c].ColSpan <= 1
                    && !(r[c].AltTextOnly && r[c].ImgWPt > 0))
                    foreach (var brSeg in r[c].Text.Split('\u0001'))
                        colW[c] = Math.Max(colW[c], MeasureFaceText(r[c].Bold ? boldFace : face,
                            brSeg.Trim(), r[c].FontSize ?? fontSize)
                            // a class padding-left is part of the cell's box —
                            // the wrap pass subtracts it back out
                            + (r[c].PadLeft > 0 ? r[c].PadLeft : 0));
                // a report cell's declared IMAGE box is content width too — the
                // logo column sizes to its 210px box, not to its alt text
                if (paragraphCells && c < r.Count && r[c].ColSpan <= 1 && r[c].ImgWPt > 0)
                    colW[c] = Math.Max(colW[c], r[c].ImgWPt);
                // Div-stacked cell content sizes its column the same way — each
                // segment's unwrapped advance is the cell's max-content.
                if (c < r.Count && r[c].ColSpan <= 1 && r[c].DivSegs is { Count: > 0 } wSegs)
                    foreach (var wSeg in wSegs)
                        if (wSeg.Text.Trim().Length > 0)
                            colW[c] = Math.Max(colW[c], MeasureFaceText(
                                wSeg.Bold || r[c].Bold ? boldFace : wSeg.Face ?? face,
                                wSeg.Text.Trim(), wSeg.FontSize ?? r[c].FontSize ?? fontSize));
                // a cell whose content is a nested grid sizes for it: the
                // browser gives the container the sub-table's max-content,
                // capped by the available box (a width:100% sub then fills it)
                if (c < r.Count && r[c].ColSpan <= 1 && r[c].SubTables is { Count: > 0 } natSubs)
                {
                    double subMax = 0;
                    foreach (var sub in natSubs)
                    {
                        var subText = CollapseWs(DecodeEntities(
                            Regex.Replace(sub, "<[^>]+>", " "))).Trim();
                        if (subText.Length > 0)
                            subMax = Math.Max(subMax, MeasureFaceText(face, subText,
                                r[c].FontSize ?? fontSize));
                    }
                    if (subMax > 0)
                        colW[c] = Math.Max(colW[c], Math.Min(subMax, usableW - 2 * p));
                }
            }
        }
        // A pixel table width the grid fills exactly: the surplus over the natural
        // columns distributes proportionally to each column's content width
        // (auto-layout distribution — the reference's 285/305.2 boxes).
        if (!bordered && tableWpt > 0)
        {
            double natSum = 0;
            for (var c = 0; c < nCols; c++) if (!colFixed[c]) natSum += colW[c];
            var fixedSum = (nCols + 1) * s + nCols * 2 * p;
            for (var c = 0; c < nCols; c++) if (colFixed[c]) fixedSum += colW[c];
            var surplus = tableWpt - fixedSum - natSum;
            if (surplus > 0 && natSum > 0)
            {
                for (var c = 0; c < nCols; c++)
                    if (!colFixed[c]) colW[c] += surplus * colW[c] / natSum;
            }
            // an all-declared grid splits the surplus EQUALLY (measured on the
            // boleto: 666px over five declared cols lands +5.25 pt on each)…
            else if (surplus > 0 && nCols > 0)
            {
                // …but the RTL attr grid gives the remainder to the SPANNING
                // cell's open slots — its declared px columns keep their widths
                // exactly (measured: 561.75 − 19/98/91px = the 405.75 span box).
                var rtlOpen = 0;
                if (rtl) for (var c = 0; c < nCols; c++) if (!colFixed[c]) rtlOpen++;
                if (rtl && rtlOpen > 0)
                    for (var c = 0; c < nCols; c++)
                    { if (!colFixed[c]) colW[c] += surplus / rtlOpen; }
                else
                    for (var c = 0; c < nCols; c++) colW[c] += surplus / nCols;
            }
        }
        // A declared percent width scales the column grid UP to fill its share of
        // the content box — the extra width distributes proportionally to each
        // column's content width (browser auto-layout distribution). A BORDERED
        // grid already resolved its box against the avail (banked shrink /
        // hug) — re-inflating it here spills the border past the content edge.
        if ((stdSerif || wrapperStacks) && tablePct > 0 && !uaPctGrid && !bordered)
        {
            var targetContent = availW * tablePct / 100.0 - (nCols + 1) * s - nCols * 2 * p;
            double sumW = 0; foreach (var w in colW) sumW += w;
            if (sumW > 0 && sumW < targetContent)
                for (var c = 0; c < nCols; c++) colW[c] *= targetContent / sumW;
        }
        // width:100% from the sheet's table rule: the column grid fills the
        // content box — the leftover joins the last column (a centered single
        // cell then centers across the sheet, as the reference letter's title).
        if (!bordered && tableFills)
        {
            // the element-rule collapse grid fills the SYMMETRIC frame, its
            // shared borders inside it (measured: cols sum to 400 in the 403
            // box — 96..499 on the 409 band)
            var tfAvail = elemCollapseGrid
                ? availW - symInsetPt - 2 * 0.75 : availW;
            double sumW0 = (nCols + 1) * s;
            foreach (var w in colW) sumW0 += w + 2 * p;
            if (sumW0 < tfAvail && nCols > 0)
            {
                // pt-report grids spread the width:100% surplus over the auto
                // columns ∝ their content width (probed: the monitoring row's
                // 86/96 pt columns land at ~186/207, the nbsp spacers at ~5);
                // the UA-serif letter sheets keep their last-column stretch.
                double ptNatS = 0;
                if ((!stdSerif && wrapperStacks) || (stdSerif && symInsetPt > 0))
                    for (var c = 0; c < nCols; c++)
                        if (!colFixed[c]) ptNatS += colW[c];
                // …and the UA-flow report grids at the SYMMETRIC inset spread it
                // the same way (the order ticket's four label columns); only the
                // edge-to-edge letter sheets keep their calibrated last-column
                // stretch (title centering).
                if (ptNatS > 0
                    && ((!stdSerif && wrapperStacks) || (stdSerif && symInsetPt > 0)))
                {
                    var ptSur = tfAvail - sumW0;
                    for (var c = 0; c < nCols; c++)
                        if (!colFixed[c]) colW[c] += ptSur * colW[c] / ptNatS;
                }
                else
                {
                    colW[nCols - 1] += tfAvail - sumW0;
                    colFixed[nCols - 1] = true;
                }
            }
        }

        // Clamp: shrink the right-most non-fixed column into the remaining width.
        var total = (nCols + 1) * s;
        foreach (var w in colW) total += w + 2 * p;
        if (total > availW && !bordered)
        {
            // SEVERAL over-full auto columns distribute like the browser: each
            // keeps its min-content (longest word) and the remaining width goes
            // out proportionally to the max-content EXCESS over that floor
            // (probed on the three-paragraph grid: 207/155/44 of a 409 box).
            var autoCols = 0;
            for (var c = 0; c < nCols; c++)
                if (!colFixed[c] && colW[c] > 0) autoCols++;
            if (autoCols > 1)
            {
                var minW = new double[nCols];
                for (var c = 0; c < nCols; c++)
                {
                    if (colFixed[c] || colW[c] <= 0) continue;
                    foreach (var r in rows)
                    {
                        if (c >= r.Count || r[c].ColSpan > 1) continue;
                        var mcFs = r[c].FontSize ?? fontSize;
                        foreach (var word in r[c].Text.Split(
                            new[] { ' ', '\u0001' }, StringSplitOptions.RemoveEmptyEntries))
                            minW[c] = Math.Max(minW[c], MeasureFaceText(
                                r[c].Bold ? boldFace : face, word, mcFs)
                                + (r[c].PadLeft > 0 ? r[c].PadLeft : 0));
                        if (r[c].DivSegs is { Count: > 0 } mSegs)
                            foreach (var mSeg in mSegs)
                                foreach (var word in mSeg.Text.Split(' ',
                                    StringSplitOptions.RemoveEmptyEntries))
                                    minW[c] = Math.Max(minW[c], MeasureFaceText(
                                        mSeg.Bold || r[c].Bold ? boldFace : mSeg.Face ?? face,
                                        word, mSeg.FontSize ?? mcFs));
                    }
                }
                // A class PERCENT column pins at max(its share, min-content) in
                // an over-constrained table (probed: the worksheet's 10% label
                // grid wraps one word per line while its 2-column sibling —
                // which FITS — keeps max-content untouched).
                var colClassPct = new double[nCols];
                foreach (var r in rows)
                    for (var c = 0; c < Math.Min(r.Count, nCols); c++)
                        if (r[c].ColSpan <= 1 && r[c].ClassWidthPct > 0)
                            colClassPct[c] = Math.Max(colClassPct[c], r[c].ClassWidthPct);
                double fixedSumB = (nCols + 1) * s + nCols * 2 * p, minSum = 0, excessSum = 0;
                for (var c = 0; c < nCols; c++)
                {
                    if (colFixed[c] || colW[c] <= 0)
                    {
                        if (!colFixed[c] && colClassPct[c] > 0)
                        {
                            // an EMPTY percent column still takes its share
                            colW[c] = colClassPct[c] / 100.0 * availW;
                            colFixed[c] = true;
                        }
                        fixedSumB += colW[c];
                        continue;
                    }
                    if (colClassPct[c] > 0)
                    {
                        colW[c] = Math.Max(colClassPct[c] / 100.0 * availW, minW[c]);
                        colFixed[c] = true;
                        fixedSumB += colW[c];
                        continue;
                    }
                    minSum += minW[c];
                    excessSum += Math.Max(0, colW[c] - minW[c]);
                }
                var room = availW - fixedSumB - minSum;
                if (room > 0 && excessSum > 0)
                {
                    for (var c = 0; c < nCols; c++)
                        if (!colFixed[c] && colW[c] > 0)
                            colW[c] = minW[c] + room * Math.Max(0, colW[c] - minW[c]) / excessSum;
                }
                else if (excessSum > 0)
                {
                    for (var c = 0; c < nCols; c++)
                        if (!colFixed[c] && colW[c] > 0)
                            colW[c] = Math.Max(fontSize, minW[c]);
                }
            }
            else
            for (var c = nCols - 1; c >= 0; c--)
                if (!colFixed[c])
                {
                    var others = (nCols + 1) * s;
                    for (var o = 0; o < nCols; o++) if (o != c) others += colW[o] + 2 * p;
                    colW[c] = Math.Max(fontSize, availW - others - 2 * p);
                    break;
                }
            // still over-full: the declared percents over-fill the box (a
            // nested grid's 99% column beside its labels) — the right-most
            // percent column takes the remainder instead of overflowing
            total = (nCols + 1) * s;
            foreach (var w in colW) total += w + 2 * p;
            if (total > availW)
                for (var c = nCols - 1; c >= 0; c--)
                    if (colPct[c] > 0)
                    {
                        var others = (nCols + 1) * s;
                        for (var o = 0; o < nCols; o++) if (o != c) others += colW[o] + 2 * p;
                        colW[c] = Math.Max(fontSize, availW - others - 2 * p);
                        break;
                    }
        }

        // RTL grid: the (mirrored-LTR) table RIGHT-anchors one right inset
        // inside the page edge — the widest grid's left edge then sits on the
        // 90 pt page margin the RTL page-width model left for it.
        if (rtl)
        {
            var rtlTotal = (nCols + 1) * s;
            foreach (var w in colW) rtlTotal += w + 2 * p;
            tableX = Math.Max(0, pageWidth - RtlGridRightInsetPt - rtlTotal);
        }

        // Wrap cell text and size rows. An inline-table span grows the cell's first
        // line box by 3 pt (22px vs 18px line).
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        // Per-cell face/metrics: a <font face> cell wraps, paces and seats with its
        // own family's win metrics (the flow face otherwise).
        string CellFaceName(MetricCell mc) => mc.Face is { } cf
            ? cf + (mc.Bold ? " Bold" : mc.Italic ? " Italic" : "")
            : (mc.Bold ? boldFace : face);
        (double asc, double sum) CellFm(MetricCell mc) => mc.Face is { } cf
            ? (WinMetricsFor(cf) ?? fm) : fm;
        // Font-tag-sized cells pace on the face's HHEA line (the quirks strut
        // model, measured: a size-4 cell's 18px font sits in a 21px line and
        // never under the table base font's own 18px strut); CSS-sized cells
        // keep the calibrated win-metric line.
        var hheaSum = stdSerif ? (HheaLineSumFor(face) ?? fmSum) : fmSum;
        double CellLineOf(MetricCell mc, double cellFs)
        {
            // the collapsed class grid's LINE-HEIGHT pitches every cell line
            if (collapsedLineH > 0) return collapsedLineH;
            if (stdSerif && mc.FontTagSized)
                return Math.Max(MetricLineHeight(fontSize, hheaSum),
                                MetricLineHeight(cellFs, hheaSum));
            var cSum0 = CellFm(mc).sum;
            // pt-report cells pace on the face's hhea line (probed: 9pt Arial
            // rows pitch 10.5 = 14px, not the win-metric 13px).
            if (!stdSerif && wrapperStacks)
                cSum0 = HheaLineSumFor(mc.Face ?? face) ?? cSum0;
            return MetricLineHeight(cellFs, cSum0 <= 1.0 ? 1.2 : cSum0);
        }
        // …and their baselines align on the shared line: the drop is whichever
        // is deeper — the table base font's strut baseline or the cell font's
        // own seat (measured: size-2 rows seat on the 12pt strut's 10.8, the
        // size-4 row on its own 12.43).
        double CellDropOf(MetricCell mc, double cellFs, double box)
            => stdSerif && mc.FontTagSized
                ? Math.Max(MetricBaselineDrop(fontSize, box, fm),
                           MetricBaselineDrop(cellFs, box, CellFm(mc)))
                : MetricBaselineDrop(cellFs, box, CellFm(mc));
        foreach (var r in rows)
            for (var c = 0; c < r.Count; c++)
            {
                var mc = r[c];
                if (mc.Text.Length == 0 && mc.SubTables is not { Count: > 0 }
                    && mc.DivSegs is not { Count: > 0 })
                { mc.Lines = []; mc.ContentH = mc.ImgHPt; continue; }
                if (mc.Text.Length == 0) mc.Lines = [];
                var cellFs = mc.FontSize ?? fontSize;

                // FIXED layout never wraps — the content overflows its column.
                var effW = colW[c];
                for (var k = 1; k < mc.ColSpan && c + k < nCols; k++)
                    effW += 2 * p + s + colW[c + k];
                // class padding/border-left eat into the wrap width
                if (mc.PadLeft > 0 || mc.BorderLeftW > 0)
                    effW -= (mc.PadLeft > 0 ? mc.PadLeft : 0) + mc.BorderLeftW;
                // div-stacked content: each div is one styled band — its class
                // height floors the band, wrapped lines grow it
                if (mc.DivSegs is { Count: > 0 } dsegs)
                {
                    mc.Lines = [];
                    // an overflowing image GROWS the cell's content box — the
                    // paragraphs below it wrap at the image's width (measured:
                    // the report paragraphs break at the 612 pt photo, not the
                    // 504 pt column)
                    if (mc.ImgJpegBytes is not null && mc.ImgWPt > effW)
                        effW = mc.ImgWPt;
                    double dh = 0, prevMb = 0;
                    foreach (var sg in dsegs)
                    {
                        var sgFs = sg.FontSize ?? fontSize;
                        // newsletter segments pace on the cell line model (hhea);
                        // the calibrated div-seg dialects keep their win metrics
                        double sgLineH;
                        if (paragraphCells)
                            sgLineH = CellLineOf(new MetricCell
                                { Face = sg.Face, Bold = sg.Bold, FontSize = sg.FontSize }, sgFs);
                        else
                        {
                            var sgFmv = sg.Face is { } sgf ? (WinMetricsFor(sgf) ?? fm) : fm;
                            var sgSum = sgFmv.sum <= 1.0 ? 1.2 : sgFmv.sum;
                            sgLineH = MetricLineHeight(sgFs, sgSum);
                        }
                        var sgFaceN = sg.Face is { } f2
                            ? f2 + (sg.Bold ? " Bold" : "")
                            : (sg.Bold ? boldFace : face);
                        var nLines = sg.Text.Length == 0 ? 0
                            : MeasuredWordWrap(sg.Text, effW - sg.PadLeft, sgFaceN, sgFs).Length;
                        // paragraph segments carry the UA block margins,
                        // adjacent margins collapsing to the larger one
                        dh += Math.Max(sg.MarginTopPt, prevMb)
                              + Math.Max(sg.LineBoxPt, nLines * sgLineH);
                        prevMb = sg.MarginBottomPt;
                    }
                    // an intrinsic-aspect JPEG stacks ABOVE the segments — the
                    // reserved-box images centre in the band instead
                    mc.ContentH = mc.ImgJpegBytes is not null
                        ? dh + mc.ImgHPt : Math.Max(dh, mc.ImgHPt);
                    continue;
                }
                // newsletter cells: whitespace GLUE between nested tables
                // (the &nbsp; separators the markup leaves in the container td)
                // holds no line box of its own
                if (paragraphCells && mc.SubTables is { Count: > 0 } && mc.Text.Length > 0)
                {
                    var glueWs = true;
                    foreach (var ch in mc.Text)
                        if (ch is not (' ' or '\u00A0' or '\u0001')) { glueWs = false; break; }
                    if (glueWs) { mc.Text = ""; mc.Lines = []; }
                }
                if (mc.Text.Length > 0)
                mc.Lines = (bordered && layoutFixed) || mc.NoWrap
                    ? new[] { mc.Text.Replace('\u0001', ' ') }
                    // +0.05: a column sized to its own max-content must not
                    // wrap on the equality boundary
                    : MeasuredWordWrap(mc.Text, effW + 0.05, CellFaceName(mc), cellFs);
                mc.ContentH = mc.Lines.Length * CellLineOf(mc, cellFs)
                              + (mc.HasSpan ? 3.0 : 0) + mc.PadTopPt;
                if (mc.ImgHPt > 0)
                    mc.ContentH = mc.ImgJpegBytes is not null
                        ? mc.ContentH + mc.ImgHPt
                        : Math.Max(mc.ContentH, mc.ImgHPt);
                if (mc.SubTables is { Count: > 0 })
                    foreach (var sub in mc.SubTables)
                        mc.ContentH += bordered
                            // the bordered draw strokes the row box up front — it
                            // needs the sub-grid's REAL wrapped extent
                            ? NestedTableWrappedHeight(sub, lineH, face, fontSize, effW)
                            : EstimateNestedTableHeight(sub,
                                CellLineOf(mc, cellFs) + 2 * p) + s;
            }

        // A table with NO text anywhere (a logo strip whose only content is an image
        // that failed to load) collapses each row to its padding band — the flow
        // advances just the cell padding for it. A blank SPACER row inside a text
        // table keeps its line box (the calibrated metric behaviour).
        var tableHasText = false;
        foreach (var r in rows)
            foreach (var mc in r)
                if (mc.Text.Length > 0) { tableHasText = true; break; }

        // page-break-inside: avoid on the sheet's table rule — a table that cannot
        // finish in the space left on this page starts whole on a fresh one (and
        // still paginates row-at-a-time if it outgrows that full page). A table
        // already sitting at the page top has nothing to gain from breaking.
        if (css.TryGetValue("table", out var pbiRule)
            && pbiRule.TryGetValue("page-break-inside", out var pbiV)
            && pbiV.Contains("avoid", StringComparison.OrdinalIgnoreCase)
            && y < pageHeight - marginTop - 1e-6)
        {
            var tableH = s;
            for (var ri = 0; ri < rows.Count; ri++)
            {
                double rch = tableHasText ? lineH : 0;
                foreach (var mc in rows[ri]) rch = Math.Max(rch, mc.ContentH);
                var rbh = rch + 2 * p;
                if (ri < rowHeights.Count && rowHeights[ri] > rbh) rbh = rowHeights[ri];
                tableH += s + rbh;
            }
            if (y - tableH < marginBottom)
            {
                page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(page, docFontDict);
                y = pageHeight - marginTop;
            }
        }

        // Bordered draw: outer border box, per-cell border boxes on the 2px
        // border-spacing grid, text at border+padding insets. Cell box heights =
        // content + padding + borders; strokes centred half a width inside.
        if (bordered)
        {
            // Attribute grid: the outer box hugs the column grid; align=center
            // centres it on the page (the symmetric UA content frame's middle).
            if (borderHugs)
            {
                var hugW = 2 * bw + (nCols + 1) * s;
                foreach (var w in colW) hugW += w + 2 * p + 2 * bw;
                if (centerTable)
                    tableX = Math.Max(marginLeft, (pageWidth - hugW) / 2);
            }
            var sbB = new StringBuilder();
            void BLine(double x0, double y0d, double x1, double y1d)
                => sbB.Append(string.Create(invc,
                    $"{x0:F2} {pageHeight - y0d:F2} m {x1:F2} {pageHeight - y1d:F2} l S "));
            void BBox(double x0, double y0d, double x1, double y1d)
            {
                BLine(x0, y0d + bw / 2, x1, y0d + bw / 2);
                BLine(x0, y1d - bw / 2, x1, y1d - bw / 2);
                BLine(x0 + bw / 2, y0d, x0 + bw / 2, y1d);
                BLine(x1 - bw / 2, y0d, x1 - bw / 2, y1d);
            }
            // WinAnsi Type1 resources for <font face> cells (the Markdown pattern),
            // allocated from F8 up and registered on the page lazily. The bordered
            // branch never paginates, so the page snapshot stays valid throughout.
            var extraRes = new Dictionary<string, string>(StringComparer.Ordinal);
            var borderPage = page;
            string ResOf(MetricCell mc)
            {
                if (mc.Face is null)
                    return mc.Bold ? (stdSerif ? "F6" : "F2") : (stdSerif ? "F5" : "F1");
                var fn = CellFaceName(mc);
                if (!extraRes.TryGetValue(fn, out var rn))
                {
                    rn = "F" + (8 + extraRes.Count);
                    extraRes[fn] = rn;
                }
                EnsureFont(borderPage, fn.Replace(" ", ""), rn);
                return rn;
            }
            var tableTopTd = pageHeight - y;
            var rowTopTd = tableTopTd + bw + s;
            foreach (var r in rows)
            {
                // The attribute grid's row box hugs its tallest cell (a size=1
                // header row is a 9px band); the css-bordered mode keeps its
                // calibrated one-line floor.
                double rowContentB = borderHugs ? 0 : lineH;
                foreach (var mc in r) rowContentB = Math.Max(rowContentB, mc.ContentH);
                if (rowContentB <= 0) rowContentB = lineH;
                // collapse shares the borders across the boundary: the row pitch
                // carries no border of its own (measured: 13.5 exactly per strut row)
                var cellBoxH = rowContentB + 2 * p + (attrCollapse ? 0 : 2 * bw);
                var colXB = tableX + bw + s;
                var spanSkip = 0;
                double rowSubBotTd = 0;
                var rowEdgeStrokes = new StringBuilder();
                for (var c = 0; c < nCols; c++)
                {
                    var boxW = colW[c] + 2 * p + 2 * bw;
                    if (spanSkip > 0)
                    {
                        // a phantom slot under a spanning cell: no box of its own,
                        // no advance — the spanning cell already covered it.
                        spanSkip--;
                        continue;
                    }
                    if (c < r.Count && r[c].ColSpan > 1)
                        for (var k = 1; k < r[c].ColSpan && c + k < nCols; k++)
                        {
                            boxW += s + colW[c + k] + 2 * p + 2 * bw;
                            spanSkip++;
                        }
                    // bgcolor cell fill inside the cell border box.
                    if (c < r.Count && r[c].Bg is { } cbg)
                        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                            $"q {cbg.R / 255.0:0.###} {cbg.G / 255.0:0.###} {cbg.B / 255.0:0.###} rg " +
                            $"{colXB:F2} {pageHeight - rowTopTd - cellBoxH:F2} {boxW:F2} {cellBoxH:F2} re f Q\n")));
                    BBox(colXB, rowTopTd, colXB + boxW, rowTopTd + cellBoxH);
                    // a style border-right strokes that one edge in its own colour
                    // over the shared grid (the separator-column idiom); emitted
                    // after the row's fills so a neighbour's fill can't bury it
                    if (c < r.Count && r[c].BorderRightW > 0)
                    {
                        var brc = r[c].BorderRightCol;
                        rowEdgeStrokes.Append(string.Create(invc,
                            $"q {brc.R / 255.0:0.###} {brc.G / 255.0:0.###} {brc.B / 255.0:0.###} RG " +
                            $"{r[c].BorderRightW:0.##} w {colXB + boxW:F2} {pageHeight - rowTopTd:F2} m " +
                            $"{colXB + boxW:F2} {pageHeight - rowTopTd - cellBoxH:F2} l S Q\n"));
                    }
                    if (c < r.Count && (r[c].Lines.Length > 0 || r[c].SubTables is { Count: > 0 }))
                    {
                        var mc = r[c];
                        var cellFs = mc.FontSize ?? fontSize;
                        var cFm = CellFm(mc);
                        var cellLineH = CellLineOf(mc, cellFs);
                        var mFace = CellFaceName(mc);
                        var fontRes = ResOf(mc);
                        // Middle vertical alignment (the HTML cell default);
                        // a valign='top' cell seats its first line at the row top.
                        var lineTopTd = rowTopTd + (attrCollapse ? 0 : bw) + p
                            + (mc.VAlignTop ? 0 : (rowContentB - mc.ContentH) / 2);
                        if (mc.Fore is { } fc)
                            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                                $"{fc.R / 255.0:0.###} {fc.G / 255.0:0.###} {fc.B / 255.0:0.###} rg")));
                        foreach (var ln in mc.Lines)
                        {
                            var drop = CellDropOf(mc, cellFs, cellLineH);
                            var lw = MeasureFaceText(mFace, ln, cellFs);
                            var lx = mc.Align switch
                            {
                                HorizontalAlignment.Right => colXB + boxW - (attrCollapse ? 0 : bw) - p - lw,
                                HorizontalAlignment.Center => colXB + (boxW - lw) / 2,
                                _ => colXB + (attrCollapse ? 0 : bw) + p,
                            };
                            if (ln.Length > 0)
                                EmitCellLineRuns(page, fontRes, cellFs, lx,
                                    pageHeight - lineTopTd - drop, ln, mFace);
                            lineTopTd += cellLineH;
                        }
                        if (mc.Fore is not null)
                            page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
                        // nested grids render inside the cell, stacked below its
                        // own lines — the row then covers their real drawn extent
                        if (mc.SubTables is { Count: > 0 })
                        {
                            var subInset = attrCollapse ? 0 : bw + p;
                            var subY = pageHeight - lineTopTd;
                            foreach (var sub in mc.SubTables)
                                RenderMetricTable(doc, ref page, ref subY, sub, css,
                                    colXB + subInset, boxW - 2 * bw - 2 * p, pageWidth,
                                    pageHeight, marginTop, marginBottom, face, fm,
                                    docFontDict, stdSerif, baseFontSize,
                                    wrapperStacks: true, symInsetPt: 0);
                            rowSubBotTd = Math.Max(rowSubBotTd, pageHeight - subY);
                        }
                    }
                    colXB += boxW + s;
                }
                if (rowEdgeStrokes.Length > 0)
                    page.AddContentStream(Encoding.ASCII.GetBytes(rowEdgeStrokes.ToString()));
                rowTopTd += Math.Max(cellBoxH, rowSubBotTd - rowTopTd) + s;
            }
            var tableBottomTd = rowTopTd + bw;
            // The outer box spans the availW under width:100%; a FIXED grid's
            // chrome pushes its right edge past it.
            var outerW = 2 * bw + (nCols + 1) * s;
            foreach (var w in colW) outerW += w + 2 * p + 2 * bw;
            var outerR = tableX + (borderHugs ? outerW
                : tableFills && !layoutFixed ? availW : Math.Max(availW, outerW));
            BBox(tableX, tableTopTd, outerR, tableBottomTd);
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {borderColor.R / 255.0:0.###} {borderColor.G / 255.0:0.###} {borderColor.B / 255.0:0.###} RG {bw:0.##} w {sbB}Q\n")));
            y = pageHeight - tableBottomTd;
            return;
        }

        // WinAnsi Type1 resources for styled cells in the flat (borderless) grid,
        // registered on whichever page the row lands on.
        var flatRes = new Dictionary<string, string>(StringComparer.Ordinal);
        string ResOfFlatOn(Page pg, MetricCell mc)
        {
            var fn = CellFaceName(mc);
            if (!flatRes.TryGetValue(fn, out var rn))
            {
                // Pick a name no page-level font has already claimed for
                // something else — the flow's Type0 embeds share this /Font
                // dictionary and count through the same F-numbers.
                var fd = (pg.Dict.Get("Resources") as Core.PdfDictionary)?
                    .Get("Font") as Core.PdfDictionary;
                var idx = 8 + flatRes.Count;
                while (fd?.Get("F" + idx) is Core.PdfDictionary taken
                       && taken.GetName("BaseFont") != fn.Replace(" ", ""))
                    idx++;
                rn = "F" + idx;
                flatRes[fn] = rn;
            }
            EnsureFont(pg, fn.Replace(" ", ""), rn);
            return rn;
        }
        // table bgcolor: one band behind the whole grid (rows and spacings alike).
        // pt-report/newsletter mode: the band's real height is only known after
        // the sub-grids lay out — remember where to UNDERLAY it and paint after
        // the rows (the wrapper-stack pattern). Other flows keep the estimated
        // pre-paint their greens were calibrated on.
        var tableBgUnderlay = tableBg is not null && !stdSerif && wrapperStacks;
        var tableBgPage = page;
        var tableBgStartIdx = tableBgUnderlay ? page.ContentStreamCount : 0;
        var tableBgStartY = y;
        if (tableBg is { } tbgc0 && !tableBgUnderlay)
        {
            var bandH = s;
            for (var ri = 0; ri < rows.Count; ri++)
            {
                double rch = tableHasText ? lineH : 0;
                foreach (var mc in rows[ri]) rch = Math.Max(rch, mc.ContentH);
                var rbh = rch + 2 * p;
                if (ri < rowHeights.Count && rowHeights[ri] > rbh) rbh = rowHeights[ri];
                bandH += s + rbh;
            }
            var bandW = (nCols + 1) * s;
            foreach (var w in colW) bandW += w + 2 * p;
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q {tbgc0.R / 255.0:0.###} {tbgc0.G / 255.0:0.###} {tbgc0.B / 255.0:0.###} rg " +
                $"{tableX:F2} {y - bandH:F2} {bandW:F2} {bandH:F2} re f Q\n")));
        }
        // Outer-frame collapse grid: rows sit INSIDE the frame — content drops
        // one frame width; the frame strokes after the rows, around the box.
        var cbFrameTopY = y;
        var cbFramePage = page;
        if (collapseBoxW > 0) y -= collapseBoxW;
        // A ROWSPAN cell's content must FIT its spanned rows: they grow evenly
        // to cover the deficit (the order ticket's 48 pt masthead stretches
        // both title rows, their cells then centring in the taller boxes).
        var rowSpanExtra = new double[rows.Count];
        if (stdSerif)
            for (var ri0 = 0; ri0 < rows.Count; ri0++)
                foreach (var mcSpan in rows[ri0])
                    if (mcSpan.RowSpan > 1 && mcSpan.ContentH > 0)
                    {
                        var kSpan = Math.Min(mcSpan.RowSpan, rows.Count - ri0);
                        if (kSpan <= 0) continue;
                        var have = (kSpan - 1) * (s + 2 * p);
                        for (var rj = ri0; rj < ri0 + kSpan; rj++)
                        {
                            var rjH = tableHasText ? lineH : 0;
                            foreach (var mc2 in rows[rj])
                                if (mc2.RowSpan <= 1) rjH = Math.Max(rjH, mc2.ContentH);
                            have += rjH;
                        }
                        if (mcSpan.ContentH > have)
                        {
                            var addEach = (mcSpan.ContentH - have) / kSpan;
                            for (var rj = ri0; rj < ri0 + kSpan; rj++)
                                rowSpanExtra[rj] = Math.Max(rowSpanExtra[rj], addEach);
                        }
                    }
        for (var ri = 0; ri < rows.Count; ri++)
        {
            var r = rows[ri];
            // an all-empty row still holds one line box; a row whose every
            // sized cell takes its font from a CLASS skin is content-paced (the
            // boleto rows carry no base-font strut)
            var classPaced = false;
            if (widthClassTable)
                foreach (var mc in r)
                {
                    if (mc.Text.Length == 0) continue;
                    if (mc.FontFromClass || (tableClassFont && mc.FontSize is null))
                    { classPaced = true; }
                    else { classPaced = false; break; }
                }
            double rowContentH = tableHasText && !classPaced ? lineH : 0;
            // A ROWSPAN cell's content overlays the FOLLOWING rows — it never
            // inflates its own row's box (the header's rowspan=4 address cell).
            foreach (var mc in r)
                if (mc.RowSpan <= 1)
                    rowContentH = Math.Max(rowContentH, mc.ContentH);
            if (ri < rowSpanExtra.Length) rowContentH += rowSpanExtra[ri];
            // A row whose every cell is TRULY empty (no text, not even an
            // &nbsp;) keeps no line strut — its band is the padding alone
            // (measured: the empty spacer row is exactly 2p + s;
            // nbsp spacer rows keep their calibrated line boxes).
            if (stdSerif && wrapperStacks && rowContentH > 0)
            {
                var rowTrulyEmpty = true;
                foreach (var mc in r)
                    if (mc.Text.Length > 0 || mc.DivSegs is { Count: > 0 }
                        || mc.SubTables is { Count: > 0 } || mc.ImgHPt > 0)
                    { rowTrulyEmpty = false; break; }
                if (rowTrulyEmpty)
                    rowContentH = ri < rowSpanExtra.Length ? rowSpanExtra[ri] : 0;
            }
            // Outer-frame collapse grid: an all-empty row is its padding alone
            // (the height-0 width-setter and blank separator rows: 1.5 pt bands).
            if (collapseBoxW > 0)
            {
                var cbRowHasText = false;
                foreach (var mc in r)
                    if (mc.Text.Trim().Length > 0) { cbRowHasText = true; break; }
                if (!cbRowHasText) rowContentH = 0;
            }
            var rowBoxH = rowContentH + 2 * p + (collapsedGrid ? 0.75 : 0);
            // a tr style height floors the row's box (the letter's paced rows);
            // a CLASS height — on the row or a cell — paces it exactly (the
            // boleto's h13/h12 grid rows and its 1px .cut tear-off row)
            double rowCellClassH = 0;
            var rowHasText = false;
            foreach (var mc in r)
            {
                rowCellClassH = Math.Max(rowCellClassH, mc.HeightPt);
                // report cells hold their ink in SEGMENTS (and images) — a
                // declared row height is a MIN for those too, not an override
                if (mc.Text.Length > 0 || (reportCells
                    && (mc.DivSegs is { Count: > 0 } || mc.SubTables is { Count: > 0 }
                        || mc.ImgHPt > 0)))
                    rowHasText = true;
            }
            var rowClassH = rowCellClassH;
            if (ri < rowHeights.Count && rowHeightExact[ri])
                rowClassH = Math.Max(rowClassH, rowHeights[ri]);
            if (rowClassH > 0)
            {
                // a class height is a MIN-height: the two-line address row
                // outgrows its h12; an EMPTY spacer/rule row is EXACTLY the
                // declared height (the 1px .cut tear-off keeps no line floor)
                if (!rowHasText) rowBoxH = rowClassH;
                else if (rowClassH > rowBoxH) rowBoxH = rowClassH;
            }
            else if (ri < rowHeights.Count && rowHeights[ri] > rowBoxH) rowBoxH = rowHeights[ri];
            // A report grid's WIDTH-SETTER row (every cell empty, inline
            // WIDTH+MIN-WIDTH pairs, no height anywhere) sizes the columns
            // and occupies NO band of its own.
            if (!rowHasText && rowCellClassH == 0
                && !(ri < rowHeights.Count && rowHeights[ri] > 0))
            {
                var wsSetter = false;
                var wsBare = true;
                foreach (var mc in r)
                {
                    if (mc.Text.Length > 0 || mc.SubTables is { Count: > 0 }
                        || mc.DivSegs is { Count: > 0 } || mc.ImgHPt > 0)
                    { wsBare = false; break; }
                    if (mc.WidthSetterCell) wsSetter = true;
                }
                if (wsBare && wsSetter) rowBoxH = 0;
            }
            // report mode: a WHITESPACE-only row (an &nbsp; spacer) with a
            // declared height IS that height — its blank line box carries no
            // strut of its own (the sidebar's 13px separator rows)
            if (paragraphCells && !stdSerif && wrapperStacks && ri < rowHeights.Count && rowHeights[ri] > 0)
            {
                var allWsRow = true;
                foreach (var mc in r)
                {
                    if (mc.SubTables is { Count: > 0 } || mc.DivSegs is { Count: > 0 }
                        || mc.ImgHPt > 0) { allWsRow = false; break; }
                    foreach (var ch in mc.Text)
                        if (ch is not (' ' or '\u00A0' or '\u0001')) { allWsRow = false; break; }
                    if (!allWsRow) break;
                }
                if (allWsRow) rowBoxH = rowHeights[ri];
            }

            // Pagination: the row moves whole to the next page when its box bottom
            // would cross the bottom margin; the continuation page resumes at the raw
            // content top (no body top margin).
            if (y - s - rowBoxH < marginBottom)
            {
                page = doc.Pages.Add(pageWidth, pageHeight);
                EnsureFonts(page, docFontDict);
                y = pageHeight - marginTop;
            }

            var contentTop = y - s - p - (collapsedGrid ? 0.75 : 0);
            // collapsed class grid: the shared 1px borders — row rule across the
            // grid, column rules down this row, in the class rule's colour.
            if (collapsedGrid)
            {
                var gInv = System.Globalization.CultureInfo.InvariantCulture;
                var gW = availW - symInsetPt;
                var gsb = new StringBuilder(string.Create(gInv,
                    $"q {collapsedCol.R / 255.0:0.###} {collapsedCol.G / 255.0:0.###} {collapsedCol.B / 255.0:0.###} RG 0.75 w "));
                gsb.Append(string.Create(gInv,
                    $"{tableX:F2} {y - 0.38:F2} m {tableX + gW:F2} {y - 0.38:F2} l S "));
                gsb.Append(string.Create(gInv,
                    $"{tableX:F2} {y - s - rowBoxH + 0.38:F2} m {tableX + gW:F2} {y - s - rowBoxH + 0.38:F2} l S "));
                var gx = tableX;
                gsb.Append(string.Create(gInv,
                    $"{gx + 0.38:F2} {y:F2} m {gx + 0.38:F2} {y - s - rowBoxH:F2} l S "));
                for (var gc = 0; gc < nCols; gc++)
                {
                    gx += (gc == 0 ? s : 0) + colW[gc] + 2 * p + s;
                    var gxe = gc == nCols - 1 ? tableX + gW - 0.38 : gx + 0.38;
                    gsb.Append(string.Create(gInv,
                        $"{gxe:F2} {y:F2} m {gxe:F2} {y - s - rowBoxH:F2} l S "));
                }
                gsb.Append("Q\n");
                page.AddContentStream(Encoding.ASCII.GetBytes(gsb.ToString()));
            }
            var colX = tableX + s + (collapsedGrid ? 0.75 : 0)
                + (collapseBoxW > 0 ? collapseBoxW : 0);
            var rowSubBottom = double.MaxValue;
            var rowRealBottom = double.MaxValue;   // deepest drawn text bottom (wrapper rows)
            var flatSkip = 0;
            for (var c = 0; c < nCols; c++)
            {
                var boxW = colW[c] + 2 * p;
                if (flatSkip > 0)
                {
                    // a phantom slot under a spanning cell: nothing of its own,
                    // no advance — the spanning cell already covered it.
                    flatSkip--;
                    continue;
                }
                if (c < r.Count)
                    for (var k = 1; k < r[c].ColSpan && c + k < nCols; k++)
                    {
                        boxW += s + colW[c + k] + 2 * p;
                        flatSkip++;
                    }
                // bgcolor cell fill: the whole cell box, behind the text - inset
                // inside the collapsed grid so the shared border strokes stay.
                if (c < r.Count && r[c].Bg is { } cbg)
                {
                    var fIn = collapsedGrid ? 0.75 : 0;
                    page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                        $"q {cbg.R / 255.0:0.###} {cbg.G / 255.0:0.###} {cbg.B / 255.0:0.###} rg " +
                        $"{colX + fIn:F2} {y - s - rowBoxH + fIn:F2} {boxW - 2 * fIn:F2} {rowBoxH - 2 * fIn:F2} re f Q\n")));
                }
                // class-skin side borders (the boleto field grid): each declared
                // side strokes its own edge of the cell box in black; a dashed
                // top is the tear-off rule.
                if (c < r.Count && (r[c].BorderLeftW > 0 || r[c].BorderRightW > 0
                    || r[c].BorderBottomW > 0 || r[c].BorderTopW > 0))
                {
                    var bc2 = r[c];
                    var rowTopY = y - s;
                    var rowBotY = y - s - rowBoxH;
                    var bsb = new StringBuilder("q 0 0 0 RG ");
                    void SideLine(double w2, double sx0, double sy0, double sx1, double sy1, bool dash)
                        => bsb.Append(string.Create(invc,
                            $"{w2:0.##} w {(dash ? "[2.25 2.25] 0 d " : "")}" +
                            $"{sx0:F2} {sy0:F2} m {sx1:F2} {sy1:F2} l S "));
                    if (bc2.BorderLeftW > 0)
                        SideLine(bc2.BorderLeftW, colX, rowTopY, colX, rowBotY, false);
                    if (bc2.BorderRightW > 0)
                        SideLine(bc2.BorderRightW, colX + boxW, rowTopY, colX + boxW, rowBotY, false);
                    if (bc2.BorderBottomW > 0)
                        SideLine(bc2.BorderBottomW, colX, rowBotY, colX + boxW, rowBotY, false);
                    if (bc2.BorderTopW > 0)
                        SideLine(bc2.BorderTopW, colX, rowTopY, colX + boxW, rowTopY,
                            bc2.BorderTopDashed);
                    bsb.Append("Q\n");
                    page.AddContentStream(Encoding.ASCII.GetBytes(bsb.ToString()));
                }
                if (c < r.Count && (r[c].Lines.Length > 0 || r[c].SubTables is { Count: > 0 }
                    || r[c].DivSegs is { Count: > 0 } || r[c].ImgJpegBytes is not null))
                {
                    var mc = r[c];
                    var cellFs = mc.FontSize ?? fontSize;
                    var cellLineH = CellLineOf(mc, cellFs);
                    // Middle vertical alignment (the HTML cell default);
                    // a valign='top' cell seats its first line at the row top.
                    // the collapsed class grid top-aligns its cells
                    var lineTop = mc.VAlignBottom ? contentTop - (rowContentH - mc.ContentH)
                        // a rowspan cell hangs from its row top over the rows below
                        : mc.VAlignTop || collapsedGrid || mc.RowSpan > 1 ? contentTop
                        : contentTop - (rowContentH - mc.ContentH) / 2;
                    lineTop -= mc.PadTopPt;
                    var mFace = CellFaceName(mc);
                    // Browser-UA flow draws the Standard-14 serif faces (F5/F6);
                    // the MSHTML metric flow keeps its embedded-face resources;
                    // a <font face>/font-family cell brings its own WinAnsi face.
                    var fontRes = mc.Face is not null
                        ? ResOfFlatOn(page, mc)
                        // pt-report cells draw the table's own real face (the
                        // measure face) rather than the Standard-14 Helvetica;
                        // a `table { font: … }` shorthand face — or a UA-flow
                        // body face other than the serif — draws likewise.
                        : (tableRuleFace || (!stdSerif && wrapperStacks)
                            || (stdSerif && !face.Equals("Times New Roman",
                                StringComparison.OrdinalIgnoreCase)))
                            && PosFace(face + (mc.Bold ? " Bold" : "")).ttf is not null
                        ? ResOfFlatOn(page, new MetricCell
                            { Face = face, Bold = mc.Bold, FontSize = mc.FontSize })
                        : mc.Bold ? (stdSerif ? "F6" : "F2") : (stdSerif ? "F5" : "F1");
                    if (mc.Fore is { } fc)
                        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                            $"{fc.R / 255.0:0.###} {fc.G / 255.0:0.###} {fc.B / 255.0:0.###} rg")));
                    // div-stacked cells draw band by band, each with its own
                    // class typography; a .BB band strokes its bottom edge
                    if (mc.DivSegs is { Count: > 0 } dsegs2)
                    {
                        var segTop = lineTop;
                        // An abs-positioned data-URI PNG draws over the cell at
                        // its left:N% offset from the content box, natural size
                        // (50px = 37.5pt on the reference), out of the flow.
                        if (mc.AbsPng is { } apng && apng.Length >= 24)
                        {
                            var apW = ((apng[16] << 24) | (apng[17] << 16)
                                | (apng[18] << 8) | apng[19]) * PxPt;
                            var apH = ((apng[20] << 24) | (apng[21] << 16)
                                | (apng[22] << 8) | apng[23]) * PxPt;
                            var apIn = collapsedGrid ? 0.75 : 0.0;
                            if (apW > 0 && apH > 0)
                            {
                                var apx = colX + apIn + p
                                    + mc.AbsPngLeftFrac * (boxW - 2 * apIn - 2 * p);
                                page.AddImage(apng, new Rectangle(
                                    apx, segTop - apH, apx + apW, segTop));
                            }
                        }
                        // the intrinsic-aspect JPEG opens the cell — its
                        // paragraphs stack below it
                        if (mc.ImgJpegBytes is { } jpg2 && mc.ImgWPt > 0)
                        {
                            var jx = colX + mc.BorderLeftW
                                + (mc.PadLeft >= 0 ? mc.PadLeft : p);
                            page.AddImage(jpg2, new Rectangle(
                                jx, segTop - mc.ImgHPt, jx + mc.ImgWPt, segTop));
                            segTop -= mc.ImgHPt;
                        }
                        var sgPrevMb = 0.0;
                        foreach (var sg in dsegs2)
                        {
                            segTop -= Math.Max(sg.MarginTopPt, sgPrevMb);
                            sgPrevMb = sg.MarginBottomPt;
                            var sgFs = sg.FontSize ?? fontSize;
                            var sgProbe = new MetricCell
                            { Face = sg.Face, Bold = sg.Bold, FontSize = sg.FontSize };
                            var sgFace = CellFaceName(sgProbe);
                            var sgRes = sgProbe.Face is not null ? ResOfFlatOn(page, sgProbe)
                                // newsletter segments draw the flow's real face,
                                // exactly like the plain-cell path above
                                : paragraphCells
                                    && PosFace(face + (sg.Bold ? " Bold" : "")).ttf is not null
                                ? ResOfFlatOn(page, new MetricCell
                                    { Face = face, Bold = sg.Bold, FontSize = sg.FontSize })
                                : sg.Bold ? (stdSerif ? "F6" : "F2") : (stdSerif ? "F5" : "F1");
                            var sgFmv = CellFm(sgProbe);
                            var sgSum0 = sgFmv.sum <= 1.0 ? 1.2 : sgFmv.sum;
                            var sgLineH = paragraphCells
                                ? CellLineOf(sgProbe, sgFs)
                                : MetricLineHeight(sgFs, sgSum0);
                            // the overflowing image grew the content box — wrap
                            // at its width, exactly like the layout pass
                            var sgWrapW = mc.ImgJpegBytes is not null
                                && mc.ImgWPt > boxW - 2 * p
                                ? mc.ImgWPt
                                : boxW - 2 * p - sg.PadLeft - mc.BorderLeftW;
                            var sgLines = sg.Text.Length == 0 ? System.Array.Empty<string>()
                                : MeasuredWordWrap(sg.Text, sgWrapW, sgFace, sgFs);
                            // a class background fills the segment's band over
                            // the cell content width (the green bar:
                            // 97.5..497.5 × the class height, measured)
                            if (sg.Bg is { } sgBgC)
                            {
                                var sgBandH = Math.Max(sg.LineBoxPt,
                                    sgLines.Length * sgLineH);
                                var sgIn = collapsedGrid ? 0.75 : 0.0;
                                if (sgBandH > 0)
                                    page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                                        $"q {sgBgC.R / 255.0:0.###} {sgBgC.G / 255.0:0.###} {sgBgC.B / 255.0:0.###} rg " +
                                        $"{colX + sgIn:F2} {segTop - sgBandH:F2} {boxW - 2 * sgIn:F2} {sgBandH:F2} re f Q\n")));
                            }
                            if (sg.Fore is { } sgc)
                                page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                                    $"{sgc.R / 255.0:0.###} {sgc.G / 255.0:0.###} {sgc.B / 255.0:0.###} rg")));
                            var segLy = segTop;
                            foreach (var ln in sgLines)
                            {
                                var sgDrop = MetricBaselineDrop(sgFs, sgLineH, sgFmv);
                                var sgLw = MeasureFaceText(sgFace, ln, sgFs);
                                var sgLx = mc.Align switch
                                {
                                    HorizontalAlignment.Right => colX + boxW - p - sgLw,
                                    HorizontalAlignment.Center => colX + (boxW - sgLw) / 2,
                                    _ => colX + mc.BorderLeftW + sg.PadLeft + p,
                                };
                                if (ln.Length > 0)
                                    EmitCellLineRuns(page, sgRes, sgFs, sgLx, segLy - sgDrop, ln, sgFace);
                                segLy -= sgLineH;
                            }
                            if (sg.Fore is not null)
                                page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
                            var bandH = Math.Max(sg.LineBoxPt, sgLines.Length * sgLineH);
                            if (sg.BorderBottom)
                                page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                                    $"q 0 0 0 RG 0.75 w {colX + mc.BorderLeftW:F2} {segTop - bandH:F2} m {colX + boxW:F2} {segTop - bandH:F2} l S Q\n")));
                            segTop -= bandH;
                        }
                        lineTop = segTop;   // the cell's drawn bottom
                    }
                    else if (mc.Flow is null)
                    {
                    if (mc.ImgJpegBytes is { } jpg3 && mc.ImgWPt > 0)
                    {
                        var jx3 = colX + mc.BorderLeftW
                            + (mc.PadLeft >= 0 ? mc.PadLeft : p);
                        page.AddImage(jpg3, new Rectangle(
                            jx3, lineTop - mc.ImgHPt, jx3 + mc.ImgWPt, lineTop));
                        lineTop -= mc.ImgHPt;
                    }
                    for (var li = 0; li < mc.Lines.Length; li++)
                    {
                        var ln = mc.Lines[li];
                        var boxH = cellLineH + (mc.HasSpan && li == 0 ? 3.0 : 0);
                        var drop = CellDropOf(mc, cellFs, boxH);
                        var lw = MeasureFaceText(mFace, ln, cellFs);
                        var lx = mc.Align switch
                        {
                            HorizontalAlignment.Right => colX + boxW - p - lw,
                            HorizontalAlignment.Center => colX + (boxW - lw) / 2,
                            // A span cell's content sits 1.5 pt further in; a
                            // class border-left pushes the content past itself.
                            _ => colX + mc.BorderLeftW + (mc.PadLeft >= 0 ? mc.PadLeft : p)
                                 + (mc.HasSpan ? 1.5 : 0),
                        };
                        if (ln.Length > 0)
                            EmitCellLineRuns(page, fontRes, cellFs, lx, lineTop - drop, ln, mFace);
                        lineTop -= boxH;
                    }
                    }
                    if (mc.Fore is not null)
                        page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
                    if (mc.RowSpan <= 1 && mc.SubTables is not { Count: > 0 })
                        rowRealBottom = Math.Min(rowRealBottom, lineTop);
                    // Interleaved flow cells: text runs and nested grids draw in
                    // SOURCE order — a <br> closes its line (an empty one is a
                    // blank line box), runs carry their own bold, and a page
                    // break resumes at the raw content top like any table row.
                    if (mc.Flow is { Count: > 0 } flowRuns)
                    {
                        var fCursor = lineTop;
                        var fPage = page;
                        var effWf = boxW - 2 * p;
                        string FlowRes(bool fb) => mc.Face is not null
                            ? ResOfFlatOn(fPage, new MetricCell
                                { Face = mc.Face, Bold = fb, FontSize = mc.FontSize })
                            : (tableRuleFace || (!stdSerif && wrapperStacks)
                                || (stdSerif && !face.Equals("Times New Roman",
                                    StringComparison.OrdinalIgnoreCase)))
                               && PosFace(face + (fb ? " Bold" : "")).ttf is not null
                            ? ResOfFlatOn(fPage, new MetricCell
                                { Face = face, Bold = fb, FontSize = mc.FontSize })
                            : fb ? (stdSerif ? "F6" : "F2") : (stdSerif ? "F5" : "F1");
                        var pendingLine = new List<(string T, bool B)>();
                        void FlushLine()
                        {
                            if (fCursor - cellLineH < marginBottom)
                            {
                                fPage = doc.Pages.Add(pageWidth, pageHeight);
                                EnsureFonts(fPage, docFontDict);
                                fCursor = pageHeight - marginTop;
                            }
                            var fDrop = CellDropOf(mc, cellFs, cellLineH);
                            var fx = colX + mc.BorderLeftW + (mc.PadLeft >= 0 ? mc.PadLeft : p);
                            foreach (var (rt, rb) in pendingLine)
                            {
                                if (rt.Length == 0) continue;
                                EmitCellLineRuns(fPage, FlowRes(rb), cellFs, fx,
                                    fCursor - fDrop, rt, rb ? boldFace : face);
                                fx += MeasureFaceText(rb ? boldFace : face, rt, cellFs);
                            }
                            pendingLine.Clear();
                            fCursor -= cellLineH;
                        }
                        foreach (var fi in flowRuns)
                        {
                            if (fi.TableHtml is { } subHtml)
                            {
                                if (pendingLine.Count > 0) FlushLine();
                                // A nested grid moves to the next page WHOLE
                                // when it cannot fit — the reference never
                                // splits these tables across the break.
                                var subEst = NestedTableWrappedHeight(subHtml,
                                    cellLineH, face, cellFs, effWf);
                                if (fCursor - subEst < marginBottom
                                    && subEst <= pageHeight - marginTop - marginBottom)
                                {
                                    fPage = doc.Pages.Add(pageWidth, pageHeight);
                                    EnsureFonts(fPage, docFontDict);
                                    fCursor = pageHeight - marginTop;
                                }
                                RenderMetricTable(doc, ref fPage, ref fCursor, subHtml, css,
                                    colX + p, effWf, pageWidth, pageHeight,
                                    marginTop, marginBottom, face, fm, docFontDict,
                                    stdSerif, baseFontSize,
                                    wrapperStacks: true, symInsetPt: 0,
                                    paragraphCells: paragraphCells,
                                    serifReportCells: serifReportCells);
                                continue;
                            }
                            var fParts = fi.Text.Split('\u0001');
                            for (var fpi = 0; fpi < fParts.Length; fpi++)
                            {
                                if (fpi > 0) FlushLine();
                                var fpt = fParts[fpi];
                                if (fpt.Length == 0) continue;
                                if (pendingLine.Count == 0 && MeasureFaceText(
                                        fi.Bold ? boldFace : face, fpt, cellFs) > effWf)
                                {
                                    var fWls = MeasuredWordWrap(fpt, effWf,
                                        fi.Bold ? boldFace : face, cellFs);
                                    for (var wli = 0; wli < fWls.Length; wli++)
                                    {
                                        pendingLine.Add((fWls[wli], fi.Bold));
                                        if (wli < fWls.Length - 1) FlushLine();
                                    }
                                }
                                else pendingLine.Add((fpt, fi.Bold));
                            }
                        }
                        if (pendingLine.Count > 0) FlushLine();
                        page = fPage;
                        lineTop = fCursor;
                        if (mc.RowSpan <= 1)
                            rowSubBottom = Math.Min(rowSubBottom, fCursor);
                    }
                    // nested grids render inside the cell, stacked below its
                    // own lines at the cell's content width
                    else if (mc.SubTables is { Count: > 0 })
                    {
                        var subCursor = mc.Lines.Length > 0
                            ? lineTop - mc.Lines.Length * CellLineOf(mc, cellFs)
                            : lineTop;
                        foreach (var sub in mc.SubTables)
                            RenderMetricTable(doc, ref page, ref subCursor, sub, css,
                                colX + p, boxW - 2 * p, pageWidth, pageHeight,
                                marginTop, marginBottom, face, fm, docFontDict,
                                stdSerif, baseFontSize,
                                wrapperStacks: true, symInsetPt: 0,
                                paragraphCells: paragraphCells, serifReportCells: serifReportCells);
                        // a ROWSPAN cell's nested grid overlays the rows below —
                        // it must not carry its own row's bottom with it
                        if (mc.RowSpan <= 1)
                            rowSubBottom = Math.Min(rowSubBottom, subCursor);
                    }
                }
                colX += boxW + s;
            }
            // a recursed sub-grid that outgrew the estimate carries the row
            // with it — the next row opens below the real drawn bottom. In the
            // report/newsletter wrapper mode a sub-table row's advance IS its
            // real drawn extent — the estimate is a pre-pass floor only, and
            // letting it win strands the next table a page early.
            var rowAdvance = rowBoxH;
            if (rowSubBottom < double.MaxValue)
            {
                var subAdv = (y - s) - rowSubBottom + p;
                if (!stdSerif && wrapperStacks)
                {
                    var textAdv = rowRealBottom < double.MaxValue
                        ? (y - s) - rowRealBottom + p : 0;
                    rowAdvance = Math.Max(subAdv, textAdv);
                }
                else rowAdvance = Math.Max(rowAdvance, subAdv);
            }
            y -= s + rowAdvance;
        }
        y -= s;   // trailing cellspacing closes the table box
        // the sheet's table margin-bottom paces stacked grids (measured: 20px
        // between the collapse-grid boxes, measured pitch 55.875)
        if (elemCollapseGrid && css.TryGetValue("table", out var mbRule)
            && mbRule.TryGetValue("margin-bottom", out var mbV)
            && TryParseLength(mbV.Trim(), out var mbPt) && mbPt > 0)
            y -= mbPt;
        // Outer-frame collapse grid: close the box below the last row and stroke
        // the frame around the whole table (stroke centred half a width inside).
        if (collapseBoxW > 0)
        {
            y -= collapseBoxW;
            if (ReferenceEquals(cbFramePage, page))
            {
                double cbBoxW2 = 2 * collapseBoxW + (nCols + 1) * s;
                foreach (var w in colW) cbBoxW2 += w + 2 * p;
                var cbHalf = collapseBoxW / 2;
                page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                    $"q {borderColor.R / 255.0:0.###} {borderColor.G / 255.0:0.###} {borderColor.B / 255.0:0.###} RG " +
                    $"{collapseBoxW:0.##} w {tableX + cbHalf:F2} {y + cbHalf:F2} " +
                    $"{cbBoxW2 - collapseBoxW:F2} {cbFrameTopY - y - collapseBoxW:F2} re S Q\n")));
            }
        }

        // Paint the deferred band UNDER everything the rows drew, over the box's
        // REAL extent (sub-grids included). Same-page tables only — a paginated
        // band keeps whatever its rows drew.
        if (tableBgUnderlay && tableBg is { } tbgcU
            && ReferenceEquals(tableBgPage, page) && tableBgStartY > y)
        {
            var bandW = (nCols + 1) * s;
            foreach (var w in colW) bandW += w + 2 * p;
            tableBgPage.InsertContentStreamAt(tableBgStartIdx,
                Encoding.ASCII.GetBytes(string.Create(invc,
                    $"q {tbgcU.R / 255.0:0.###} {tbgcU.G / 255.0:0.###} {tbgcU.B / 255.0:0.###} rg " +
                    $"{tableX:F2} {y:F2} {bandW:F2} {tableBgStartY - y:F2} re f Q\n")));
        }
    }

    /// <summary>One cell of a collapsed-grid table (see <see cref="RenderBodyBoxGridTable"/>).</summary>
    private sealed partial class GridCell
    {
        public int ColSpan = 1;
        public double WidthPct;                               // width="40%" attribute
        public bool BorderLeftZero, BorderRightZero;          // style border-left/right: 0px
        public HorizontalAlignment Align = HorizontalAlignment.Left;
        public List<(string Text, bool Bold, bool Italic)> Runs = new();
        public string? ImgB64;                                // data-URI PNG payload
        public double ImgPct;                                 // img width="N%" attribute
        public List<List<(string Text, bool Bold, bool Italic)>> Lines = new();
        public int Col;                                       // first column index
    }

    /// <summary>Collapsed-grid table renderer for the inline-body-margin dialect:
    /// 1px border-collapse grid at real cellpadding, columns from width-% attributes
    /// resolved in source order with the LAST column taking the remainder (the
    /// sheet over-declares 110%), colspan splitting its share equally, char-level
    /// break-all wrapping at the face's real advances, a &lt;br&gt; inside a cell
    /// CONCATENATING (the reference draws the date cells as one line), and
    /// LINE-AT-A-TIME pagination: an over-tall row splits mid-row at the content
    /// limit, its side borders running to the page edge and the continuation page
    /// resuming half a border below the top edge. All geometry measured on the
    /// reference render. Emits runs + border strokes directly and advances the flow
    /// cursor past the table's bottom border and margin-bottom.</summary>
    private static void RenderBodyBoxGridTable(Document doc, ref Page page, ref double y,
        string tableHtml, double marginLeft, double contentWidth,
        double pageWidth, double pageHeight, double marginBottom,
        string face, (double asc, double sum) fm, double lineSum,
        Core.PdfDictionary docFontDict)
    {
        const double PxPt = 0.75;
        const double bw = 0.75;                    // the sheet's 1px collapsed border
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        // ── table tag attributes ─────────────────────────────────────────────
        double pad = PxPt, fontSize = 11, marTopPt = 0, marBottomPt = 0;
        if (Regex.Match(tableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase) is { Success: true } tt)
        {
            var tag = tt.Value;
            var cp = Regex.Match(tag, @"cellpadding\s*=\s*[""']?(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (cp.Success) pad = double.Parse(cp.Groups[1].Value, invc) * PxPt;
            var fs = Regex.Match(tag, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            if (fs.Success) fontSize = double.Parse(fs.Groups[1].Value, invc);
            var mt = Regex.Match(tag, @"margin-top\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (mt.Success) marTopPt = double.Parse(mt.Groups[1].Value, invc) * PxPt;
            var mb = Regex.Match(tag, @"margin-bottom\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (mb.Success) marBottomPt = double.Parse(mb.Groups[1].Value, invc) * PxPt;
        }

        // ── parse rows/cells: runs with bold/italic, cell <br> concatenates ──
        var rows = new List<List<GridCell>>();
        List<GridCell>? row = null;
        GridCell? cell = null;
        var text = new StringBuilder();
        int boldDepth = 0, italDepth = 0;
        void FlushRun()
        {
            if (cell is null || text.Length == 0) { text.Clear(); return; }
            var t = DecodeEntities(text.ToString());
            text.Clear();
            if (t.Length == 0) return;
            var b = boldDepth > 0; var it = italDepth > 0;
            if (cell.Runs.Count > 0 && cell.Runs[^1].Bold == b && cell.Runs[^1].Italic == it)
                cell.Runs[^1] = (cell.Runs[^1].Text + t, b, it);
            else cell.Runs.Add((t, b, it));
        }
        // a cell boundary resets emphasis: the sheet leaves a stray unclosed <b>
        // at a cell's end, and the reference draws the following cells regular
        void CloseCell() { FlushRun(); if (cell is not null) row!.Add(cell); cell = null; boldDepth = 0; italDepth = 0; }
        void CloseRow() { CloseCell(); if (row is { Count: > 0 }) rows.Add(row); row = null; }
        foreach (var tok in Tokenize(tableHtml))
        {
            if (tok.Kind == TokenKind.Text)
            {
                if (cell is not null)
                {
                    // whitespace runs collapse; a pure-whitespace stretch between
                    // tags carries nothing into the cell
                    var t = Regex.Replace(tok.Value, @"\s+", " ");
                    if (t != " " || text.Length > 0) text.Append(t);
                }
                continue;
            }
            var tag = tok.Tag!.ToLowerInvariant();
            if (tok.IsClose)
            {
                switch (tag)
                {
                    case "td" or "th": CloseCell(); break;
                    case "tr": CloseRow(); break;
                    case "b" or "strong": FlushRun(); boldDepth = Math.Max(0, boldDepth - 1); break;
                    case "i" or "em": FlushRun(); italDepth = Math.Max(0, italDepth - 1); break;
                }
                continue;
            }
            switch (tag)
            {
                case "tr": CloseRow(); row = new List<GridCell>(); break;
                case "td" or "th":
                    CloseCell();
                    row ??= new List<GridCell>();
                    cell = new GridCell();
                    if (tok.Attributes is { } ca)
                    {
                        if (ca.TryGetValue("colspan", out var csv)
                            && int.TryParse(csv.Trim(), out var csn) && csn > 1)
                            cell.ColSpan = csn;
                        if (ca.TryGetValue("width", out var wv) && wv.Trim().EndsWith('%')
                            && double.TryParse(wv.Trim().TrimEnd('%'),
                                System.Globalization.NumberStyles.Float, invc, out var pct))
                            cell.WidthPct = pct;
                        if (ca.TryGetValue("align", out var av))
                            cell.Align = av.Trim().ToLowerInvariant() switch
                            {
                                "center" => HorizontalAlignment.Center,
                                "right" => HorizontalAlignment.Right,
                                _ => HorizontalAlignment.Left,
                            };
                        if (ca.TryGetValue("style", out var st))
                        {
                            if (Regex.IsMatch(st, @"border-left\s*:\s*0", RegexOptions.IgnoreCase))
                                cell.BorderLeftZero = true;
                            if (Regex.IsMatch(st, @"border-right\s*:\s*0", RegexOptions.IgnoreCase))
                                cell.BorderRightZero = true;
                        }
                    }
                    break;
                case "b" or "strong": FlushRun(); boldDepth++; break;
                case "i" or "em": FlushRun(); italDepth++; break;
                case "br": break;   // a cell <br> concatenates (measured: the date cells draw as ONE line)
                case "img":
                    if (cell is not null && tok.Attributes is { } ia)
                    {
                        if (ia.TryGetValue("src", out var src)
                            && Regex.Match(src, @"^data:image/png;base64,(.+)$",
                                RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } dm)
                            cell.ImgB64 = dm.Groups[1].Value;
                        if (ia.TryGetValue("width", out var iw) && iw.Trim().EndsWith('%')
                            && double.TryParse(iw.Trim().TrimEnd('%'),
                                System.Globalization.NumberStyles.Float, invc, out var ipct))
                            cell.ImgPct = ipct / 100.0;
                    }
                    break;
            }
        }
        CloseRow();
        if (rows.Count == 0) return;

        // ── column grid from the first row: percents of the inner width resolved
        // in source order (a colspan splits its share equally); the LAST column
        // takes the remainder — the sheet's shares sum past 100%. ──
        var nCols = 0;
        foreach (var c0 in rows[0]) nCols += c0.ColSpan;
        var innerW = contentWidth - bw;            // between the outer border centers
        var colW = new double[nCols];
        {
            var ci = 0;
            foreach (var c0 in rows[0])
            {
                for (var k = 0; k < c0.ColSpan; k++)
                    colW[ci + k] = c0.WidthPct / 100.0 * innerW / c0.ColSpan;
                ci += c0.ColSpan;
            }
            double sum0 = 0;
            for (var c = 0; c < nCols - 1; c++) sum0 += colW[c];
            colW[nCols - 1] = innerW - sum0;
        }
        var edgeX = new double[nCols + 1];         // border centers, absolute
        edgeX[0] = marginLeft + bw / 2;
        for (var c = 0; c < nCols; c++) edgeX[c + 1] = edgeX[c] + colW[c];
        foreach (var r in rows)
        {
            var ci = 0;
            foreach (var c in r) { c.Col = ci; ci += c.ColSpan; }
        }

        // ── wrap cells: char-level break-all at the face's real advances ──
        var lineH = MetricLineHeight(fontSize, lineSum);
        var drop = MetricBaselineDrop(fontSize, lineH, fm);
        string RunFace(bool b, bool it) => b ? face + " Bold" : it ? face + " Italic" : face;
        foreach (var r in rows)
            foreach (var c in r)
            {
                if (c.Runs.Count == 0) continue;
                var cw = edgeX[c.Col + c.ColSpan] - edgeX[c.Col] - bw - 2 * pad;
                var line = new List<(string Text, bool Bold, bool Italic)>();
                double lw = 0;
                foreach (var (rt, rb, ri) in c.Runs)
                {
                    var rFace = RunFace(rb, ri);
                    foreach (var ch in rt)
                    {
                        var adv = MeasureFaceText(rFace, ch.ToString(), fontSize);
                        if (lw + adv > cw && line.Count > 0)
                        {
                            c.Lines.Add(line);
                            line = new List<(string, bool, bool)>();
                            lw = 0;
                        }
                        if (line.Count > 0 && line[^1].Bold == rb && line[^1].Italic == ri)
                            line[^1] = (line[^1].Text + ch, rb, ri);
                        else line.Add((ch.ToString(), rb, ri));
                        lw += adv;
                    }
                }
                if (line.Count > 0) c.Lines.Add(line);
            }

        // ── dialect font resources: WinAnsi entries under the face's TrueType
        // names (the raster side resolves the system face for them) ──
        var faceRes = face.Replace(" ", "");
        void EnsureGridFonts(Page p2)
        {
            EnsureFont(p2, faceRes, "F8");
            EnsureFont(p2, faceRes + "Bold", "F9");
            EnsureFont(p2, faceRes + "Italic", "F10");
        }
        EnsureGridFonts(page);

        // ── border strokes, buffered per page ──
        var bops = new StringBuilder();
        void HLine(double yTd)
            => bops.Append(string.Create(invc,
                $"{marginLeft:F2} {pageHeight - yTd:F2} m {marginLeft + contentWidth:F2} {pageHeight - yTd:F2} l S "));
        void VLine(double x, double y0Td, double y1Td)
            => bops.Append(string.Create(invc,
                $"{x:F2} {pageHeight - y0Td:F2} m {x:F2} {pageHeight - y1Td:F2} l S "));
        void FlushBorders(Page p2)
        {
            if (bops.Length == 0) return;
            p2.AddContentStream(Encoding.ASCII.GetBytes(
                string.Create(invc, $"q 0 0 0 RG {bw:0.##} w {bops}Q\n")));
            bops.Clear();
        }

        // Vertical border strengths for a row: outer edges always stroke; an
        // interior boundary strokes unless BOTH neighbouring cell sides zero it
        // (border-left:0 beside border-right:0 collapses to nothing); a boundary
        // inside a colspan has no border at all.
        bool[] RowEdges(List<GridCell> r)
        {
            var on = new bool[nCols + 1];
            on[0] = on[nCols] = true;
            for (var i = 0; i < r.Count; i++)
            {
                var c = r[i];
                if (i > 0)
                {
                    var left = r[i - 1];
                    on[c.Col] = !left.BorderRightZero || !c.BorderLeftZero;
                }
            }
            return on;
        }

        // ── layout: line-at-a-time with mid-row pagination ──
        var limit = pageHeight - marginBottom;
        var borderCenter = pageHeight - y + marTopPt + bw / 2;
        HLine(borderCenter);
        foreach (var r in rows)
        {
            var edgesOn = RowEdges(r);
            var maxLines = 1;                       // an all-empty row still holds one line box
            foreach (var c in r) maxLines = Math.Max(maxLines, c.Lines.Count);
            var contentTop = borderCenter + bw / 2 + pad;
            var segTop = borderCenter - bw / 2;     // border extent start on this page
            var lineTop = contentTop;
            var rowContentH = maxLines * lineH;
            for (var li = 0; li < maxLines; li++)
            {
                if (lineTop + lineH > limit)
                {
                    // split: side borders run to the page edge; the continuation
                    // page resumes half a border below its top edge
                    for (var e = 0; e <= nCols; e++)
                        if (edgesOn[e]) VLine(edgeX[e], segTop, limit + bw / 2);
                    FlushBorders(page);
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page, docFontDict);
                    EnsureGridFonts(page);
                    segTop = 0;
                    lineTop = bw / 2;
                    contentTop = lineTop - li * lineH;   // keeps lineTop = contentTop + li*lineH
                }
                foreach (var c in r)
                {
                    if (li >= c.Lines.Count) continue;
                    var ln = c.Lines[li];
                    double lnW = 0;
                    foreach (var (t, b, it) in ln) lnW += MeasureFaceText(RunFace(b, it), t, fontSize);
                    var cx0 = edgeX[c.Col] + bw / 2 + pad;
                    var cx1 = edgeX[c.Col + c.ColSpan] - bw / 2 - pad;
                    var x = c.Align switch
                    {
                        HorizontalAlignment.Center => cx0 + (cx1 - cx0 - lnW) / 2,
                        HorizontalAlignment.Right => cx1 - lnW,
                        _ => cx0,
                    };
                    foreach (var (t, b, it) in ln)
                    {
                        var res = b ? "F9" : it ? "F10" : "F8";
                        EmitPositionedRun(page, res, fontSize, x, pageHeight - (lineTop + drop), t);
                        x += MeasureFaceText(RunFace(b, it), t, fontSize);
                    }
                }
                lineTop += lineH;
            }
            // images: centered in the cell box, width a share of the cell content
            // (measured: 40% of span − 2·padding − half a border), height by the
            // PNG's natural aspect, centered in the row's content band
            foreach (var c in r)
            {
                if (c.ImgB64 is null) continue;
                byte[] png;
                try { png = System.Convert.FromBase64String(c.ImgB64); } catch { continue; }
                if (png.Length < 24) continue;
                var natW = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
                var natH = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
                if (natW <= 0 || natH <= 0) continue;
                var span = edgeX[c.Col + c.ColSpan] - edgeX[c.Col];
                var imgW = c.ImgPct > 0 ? c.ImgPct * (span - 2 * pad - bw / 2) : span - 2 * pad - bw;
                var imgH = imgW * natH / natW;
                var bx0 = edgeX[c.Col] + bw / 2 + pad;
                var bx1 = edgeX[c.Col + c.ColSpan] - bw / 2 - pad;
                var ix = bx0 + (bx1 - bx0 - imgW) / 2;
                var iyTop = contentTop + (rowContentH - imgH) / 2;
                page.AddImage(png, new Rectangle(
                    ix, pageHeight - iyTop - imgH, ix + imgW, pageHeight - iyTop));
            }
            var bottomCenter = lineTop + pad + bw / 2;
            for (var e = 0; e <= nCols; e++)
                if (edgesOn[e]) VLine(edgeX[e], segTop, bottomCenter + bw / 2);
            HLine(bottomCenter);
            borderCenter = bottomCenter;
        }
        FlushBorders(page);
        // the flow resumes one full border below the bottom stroke's center, plus
        // the table's own margin-bottom
        y = pageHeight - (borderCenter + bw + marBottomPt);
    }

    /// <summary>Draw one styled inline row at the flow cursor: optional full-content-width
    /// background bar (+1px bottom border), then the runs — left group at the row's left
    /// pad, right group right-aligned, or the whole group centered. Text renders in
    /// Arial (bold variant per run) as an embedded Type0 face so Cyrillic labels carry.</summary>
    private static void RenderRowBlock(Page page, Block block, ref double y,
        double marginLeft, double contentWidth,
        List<(Page page, Aspose.Pdf.Rectangle rect, string url, string? text)> pendingLinks)
    {
        const double PxPt = 0.75;
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        y -= block.RowMarginTopPx * PxPt;
        var rowTop = y;
        var runs = block.RowRuns!;

        var fontDict = page.Dict.Get("Resources") is Core.PdfDictionary res
            ? res.Get("Font") as Core.PdfDictionary : null;

        var g = new StringBuilder();
        static void Rect(StringBuilder sb, System.Globalization.CultureInfo inv,
            Color c, double x, double yBot, double w, double h)
        {
            sb.Append($"{(c.R / 255.0).ToString("F5", inv)} {(c.G / 255.0).ToString("F5", inv)} {(c.B / 255.0).ToString("F5", inv)} rg ");
            sb.Append($"{x.ToString("F2", inv)} {yBot.ToString("F2", inv)} {w.ToString("F2", inv)} {h.ToString("F2", inv)} re f ");
        }

        if (block.RowBarColor is { } barc)
        {
            var bh = block.RowBarHeightPx * PxPt;
            g.Append("q ");
            Rect(g, invc, barc, marginLeft, rowTop - bh, contentWidth, bh);
            if (block.RowBarBorderColor is { } bbc)
                Rect(g, invc, bbc, marginLeft, rowTop - bh - PxPt, contentWidth, PxPt);
            g.Append("Q ");
        }

        double RunBoxWidth(RowRun r) => r.ImgSrc is not null
            ? r.ImgWPx * PxPt
            : MeasureFaceText(r.Bold ? "Arial Bold" : "Arial", r.Text, r.FontPx * PxPt)
              + (r.PadLeftPx + r.PadRightPx) * PxPt;

        double leftX = marginLeft + block.RowLeftPadPx * PxPt;
        if (block.RowCentered)
        {
            double total = 0;
            foreach (var r in runs) total += RunBoxWidth(r) + (r.MarginLeftPx + r.MarginRightPx) * PxPt;
            leftX = marginLeft + (contentWidth - total) / 2;
        }
        double rightTotal = 0;
        foreach (var r in runs)
            if (r.RightGroup) rightTotal += RunBoxWidth(r) + (r.MarginLeftPx + r.MarginRightPx) * PxPt;
        var rightX = marginLeft + contentWidth - block.RowRightPadPx * PxPt - rightTotal;

        foreach (var r in runs)
        {
            var boxW = RunBoxWidth(r);
            var x = r.RightGroup ? rightX : leftX;
            x += r.MarginLeftPx * PxPt;

            // baseline: centered in the row box (bar rows), or a plain first-line
            // baseline drop for bar-less rows.
            var fpt = r.FontPx * PxPt;
            var baseline = block.RowBarColor is not null
                ? rowTop - ((block.RowHeightPx - r.FontPx) / 2 + 0.82 * r.FontPx) * PxPt
                : rowTop - 0.85 * fpt;

            if (r.TopStripColor is { } sc)
            {
                g.Append("q ");
                Rect(g, invc, sc, x, rowTop - r.TopStripHeightPx * PxPt, boxW, r.TopStripHeightPx * PxPt);
                g.Append("Q ");
            }

            if (r.Text.Length > 0 && fontDict is not null)
            {
                var faceName = r.Bold ? "Arial Bold" : "Arial";
                var face = PosFace(faceName);
                if (face.ttf is not null)
                {
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.ttf,
                        faceName, r.Text, stripSpacesInBaseFont: true);
                    g.Append("BT ");
                    g.Append($"{(r.Color.R / 255.0).ToString("F5", invc)} {(r.Color.G / 255.0).ToString("F5", invc)} {(r.Color.B / 255.0).ToString("F5", invc)} rg ");
                    g.Append($"/{rn} {fpt.ToString("F1", invc)} Tf ");
                    g.Append($"1 0 0 1 {(x + r.PadLeftPx * PxPt).ToString("F2", invc)} {baseline.ToString("F2", invc)} Tm ");
                    g.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
                    g.Append("ET ");
                }
            }

            if (!string.IsNullOrEmpty(r.Url))
                pendingLinks.Add((page, new Aspose.Pdf.Rectangle(x, baseline - 0.3 * fpt, x + boxW, baseline + fpt), r.Url!, r.Text));

            if (r.RightGroup) rightX += boxW + (r.MarginLeftPx + r.MarginRightPx) * PxPt;
            else leftX = x + boxW + r.MarginRightPx * PxPt;
        }

        if (g.Length > 0)
            page.AddContentStream(Encoding.ASCII.GetBytes(g.ToString()));
        y = rowTop - (block.RowHeightPx + block.RowMarginBottomPx) * PxPt;
    }

    /// <summary>Vertical components of a box shorthand + longhands (px).</summary>
    private static (double top, double bottom) DomBoxTB(HtmlNode el, string box,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        double top = 0, bottom = 0;
        var sh = DomDecl(el, box, css);
        if (!string.IsNullOrEmpty(sh))
        {
            var parts = sh.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts.Length)
            {
                case 1: top = bottom = ParsePxValue(parts[0]); break;
                case 2: top = bottom = ParsePxValue(parts[0]); break;
                case 3: top = ParsePxValue(parts[0]); bottom = ParsePxValue(parts[2]); break;
                case 4: top = ParsePxValue(parts[0]); bottom = ParsePxValue(parts[2]); break;
            }
        }
        var t2 = DomDecl(el, box + "-top", css);
        if (!string.IsNullOrEmpty(t2)) top = ParsePxValue(t2);
        var b2 = DomDecl(el, box + "-bottom", css);
        if (!string.IsNullOrEmpty(b2)) bottom = ParsePxValue(b2);
        return (top, bottom);
    }

    private enum TokenKind { Text, Tag }

    private sealed partial class Token
    {
        public TokenKind Kind;
        public string? Tag;
        public bool IsClose;
        public bool IsSelfClosing;
        public Dictionary<string, string>? Attributes;
        public string Value = "";
        // Source span of this token in the tokenized string (element extraction).
        public int SrcIndex;
        public int SrcEnd;
    }

    /// <summary>A lightweight DOM node built from the tokenizer: enough tree structure
    /// (tag, attributes, children, source span) to resolve descendant CSS and extract
    /// styled-run rows. Tag == "" marks a text node.</summary>
    private sealed partial class HtmlNode
    {
        public string Tag = "";
        public string Text = "";
        public Dictionary<string, string>? Attrs;
        public List<HtmlNode> Children = new();
        public HtmlNode? Parent;
        public int SrcIndex;
        public int SrcEnd;

        public IEnumerable<HtmlNode> Descendants()
        {
            foreach (var c in Children)
            {
                yield return c;
                foreach (var d in c.Descendants()) yield return d;
            }
        }
    }

    /// <summary>Parse the markup into a lightweight element tree. Void elements never
    /// nest; a mismatched close tag pops up to its nearest matching ancestor (or is
    /// dropped). Script/style/comment content must already be stripped.</summary>
    private static HtmlNode ParseDom(string html)
    {
        var root = new HtmlNode { Tag = "#root", SrcIndex = 0, SrcEnd = html.Length };
        var cur = root;
        foreach (var tok in Tokenize(html))
        {
            if (tok.Kind == TokenKind.Text)
            {
                if (tok.Value.Length > 0)
                    cur.Children.Add(new HtmlNode { Text = tok.Value, Parent = cur, SrcIndex = tok.SrcIndex, SrcEnd = tok.SrcEnd });
                continue;
            }
            if (tok.IsClose)
            {
                for (var n = cur; n is not null && n != root; n = n.Parent)
                {
                    if (n.Tag.Equals(tok.Tag, StringComparison.OrdinalIgnoreCase))
                    {
                        n.SrcEnd = tok.SrcEnd;
                        // Everything between stays where it landed; unclosed inner
                        // elements keep their children but end here too.
                        for (var m = cur; m != n; m = m.Parent!) m.SrcEnd = tok.SrcIndex;
                        cur = n.Parent ?? root;
                        break;
                    }
                }
                continue;
            }
            var el = new HtmlNode
            {
                Tag = tok.Tag!.ToLowerInvariant(),
                Attrs = tok.Attributes,
                Parent = cur,
                SrcIndex = tok.SrcIndex,
                SrcEnd = tok.SrcEnd,
            };
            cur.Children.Add(el);
            if (!tok.IsSelfClosing && !VoidTags.Contains(el.Tag))
                cur = el;
        }
        return root;
    }

    // Quote-aware tag scan: an attribute VALUE may carry raw markup with '>'
    // inside its quotes (Angular popover payloads embed whole <div> trees) —
    // the tag ends at the first '>' OUTSIDE any quoted value. A REAL closing
    // quote is followed by whitespace, '/' or '>' — a quote whose "close"
    // lands mid-word (the typo'd `class="clearfix>` reaching into a later
    // tag's attribute) falls back to a plain character, so that tag still
    // ends at its '>' exactly as the legacy scan did, while legitimate
    // multi-line style values keep their spans.
    private static readonly Regex TagRx = new(
        @"<(/?)([A-Za-z][A-Za-z0-9]*)\s*((?:[^>""']|""[^""]*""(?=[\s/>])|'[^']*'(?=[\s/>])|[""'])*?)(/?)>",
        RegexOptions.Compiled);

    private static readonly Regex EscapedAttrRx = new(
        @"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*(?:=\s*(\S+))?",
        RegexOptions.Compiled);

    private static readonly Regex AttrRx = new(
        "([A-Za-z_:][-A-Za-z0-9_:.]*)\\s*(?:=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\">]+)))?",
        RegexOptions.Compiled);

    private static List<Token> Tokenize(string html)
    {
        var tokens = new List<Token>();
        int idx = 0;
        foreach (Match m in TagRx.Matches(html))
        {
            if (m.Index > idx)
            {
                var text = html.Substring(idx, m.Index - idx);
                if (text.Length > 0)
                    tokens.Add(new Token { Kind = TokenKind.Text, Value = text, SrcIndex = idx, SrcEnd = m.Index });
            }
            var attrs = ParseAttributes(m.Groups[3].Value);
            tokens.Add(new Token
            {
                Kind = TokenKind.Tag,
                Tag = m.Groups[2].Value,
                IsClose = m.Groups[1].Value == "/",
                IsSelfClosing = m.Groups[4].Value == "/",
                Attributes = attrs,
                SrcIndex = m.Index,
                SrcEnd = m.Index + m.Length,
            });
            idx = m.Index + m.Length;
        }
        if (idx < html.Length)
        {
            var text = html.Substring(idx);
            if (text.Length > 0)
                tokens.Add(new Token { Kind = TokenKind.Text, Value = text, SrcIndex = idx, SrcEnd = html.Length });
        }
        return tokens;
    }

    private static Dictionary<string, string>? ParseAttributes(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // JSON-escaped HTML dialect (CMS/SharePoint exports wrap the markup in a quoted
        // string): attribute quotes arrive as \" — the value then reads as an UNQUOTED
        // token up to the next whitespace, KEEPING the \" wrappers (so a href URI keeps
        // them and a style value with spaces truncates at the first one) — this dialect
        // parses exactly so.
        if (s.IndexOf("\\\"", StringComparison.Ordinal) >= 0)
        {
            var dictEsc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in EscapedAttrRx.Matches(s))
                if (!dictEsc.ContainsKey(m.Groups[1].Value))
                    dictEsc[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : "";
            return dictEsc.Count > 0 ? dictEsc : null;
        }
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttrRx.Matches(s))
        {
            var name = m.Groups[1].Value;
            var val = m.Groups[2].Success ? m.Groups[2].Value
                     : m.Groups[3].Success ? m.Groups[3].Value
                     : m.Groups[4].Success ? m.Groups[4].Value
                     : "";
            // A repeated attribute keeps its FIRST value (the HTML parsing rule) —
            // generated markup carries duplicates like a display:none style followed
            // by a second layout style, and the hiding one must win.
            if (!dict.ContainsKey(name)) dict[name] = val;
            // …except that a repeated STYLE is not thrown away: generated markup splits
            // one declaration block over two attributes (`style='display:block' …
            // style="padding-right:20px"`) and BOTH are honoured. Merge them
            // first-wins PER PROPERTY, so the display:none case above still holds and
            // the second block only contributes what the first never declared.
            else if (val.Length > 0 && name.Equals("style", StringComparison.OrdinalIgnoreCase))
                dict[name] = MergeStyleFirstWins(dict[name], val);
        }
        return dict.Count > 0 ? dict : null;
    }

    /// <summary>Concatenate two `style` attribute values, keeping the FIRST declaration
    /// of each property so consumers that read either the first or the last occurrence
    /// of a property agree.</summary>
    private static string MergeStyleFirstWins(string first, string second)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        foreach (var block in new[] { first, second })
            foreach (Match d in StyleDeclRx.Matches(block))
            {
                var prop = d.Groups[1].Value.Trim();
                if (!seen.Add(prop)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(prop).Append(':').Append(d.Groups[2].Value.Trim());
            }
        return sb.Length > 0 ? sb.ToString() : first;
    }

    private static string DecodeEntities(string text)
    {
        // Numeric first (ConvertFromUtf32 also covers astral-plane references), then the
        // full HTML named-entity table. &nbsp; becomes a real no-break space (U+00A0) so
        // Trim() leaves it in place; an &nbsp;-only paragraph is a deliberate vertical
        // spacer in many CMS-generated HTMLs and should occupy a line.
        text = Regex.Replace(text, @"&#(\d+);", m =>
            int.TryParse(m.Groups[1].Value, out var code) ? char.ConvertFromUtf32(Cp1252Ref(code)) : m.Value);
        text = Regex.Replace(text, @"&#x([0-9A-Fa-f]+);", m =>
            int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var code)
                ? char.ConvertFromUtf32(Cp1252Ref(code)) : m.Value);
        // A numeric reference missing its semicolon still decodes (the WHATWG
        // parse-error recovery): form generators emit "&#8202<div".
        text = Regex.Replace(text, @"&#(\d+)(?![\d;])", m =>
            int.TryParse(m.Groups[1].Value, out var code) ? char.ConvertFromUtf32(Cp1252Ref(code)) : m.Value);
        return text.Contains('&') ? System.Net.WebUtility.HtmlDecode(text) : text;
    }

    /// <summary>An HTML numeric character reference in 128–159 refers to the
    /// Windows-1252 glyph at that byte (the WHATWG parser rule), not the C1 control
    /// block — legacy filing HTML writes &amp;#146; for the apostrophe ’.</summary>
    private static int Cp1252Ref(int code) => code switch
    {
        0x80 => 0x20AC, 0x82 => 0x201A, 0x83 => 0x0192, 0x84 => 0x201E, 0x85 => 0x2026,
        0x86 => 0x2020, 0x87 => 0x2021, 0x88 => 0x02C6, 0x89 => 0x2030, 0x8A => 0x0160,
        0x8B => 0x2039, 0x8C => 0x0152, 0x8E => 0x017D, 0x91 => 0x2018, 0x92 => 0x2019,
        0x93 => 0x201C, 0x94 => 0x201D, 0x95 => 0x2022, 0x96 => 0x2013, 0x97 => 0x2014,
        0x98 => 0x02DC, 0x99 => 0x2122, 0x9A => 0x0161, 0x9B => 0x203A, 0x9C => 0x0153,
        0x9E => 0x017E, 0x9F => 0x0178,
        _ => code,
    };

    /// <summary>Gap a left-floated box keeps between itself and the text beside it.</summary>
    private const double FloatGutterPt = 6;

    /// <summary>Greedy wrap where the first <paramref name="narrowLines"/> lines fit a
    /// narrower box — the ones running beside a left-floated image — and every line
    /// after them takes the full measure.</summary>
    private static string[] WordWrapPastFloat(string text, double narrowWidth,
        double fullWidth, int narrowLines, double charWidth)
    {
        if (string.IsNullOrEmpty(text)) return [""];
        var cw = Math.Max(charWidth, 1);
        var result = new List<string>();
        var remaining = text;
        while (remaining.Length > 0)
        {
            var maxChars = (int)((result.Count < narrowLines ? narrowWidth : fullWidth) / cw);
            if (maxChars <= 0) maxChars = 1;
            if (remaining.Length <= maxChars) { result.Add(remaining); break; }
            var breakAt = remaining.LastIndexOf(' ', maxChars);
            if (breakAt <= 0) breakAt = maxChars;
            result.Add(remaining[..breakAt]);
            remaining = remaining[breakAt..].TrimStart();
        }
        return result.Count == 0 ? [""] : result.ToArray();
    }

    private static string[] WordWrap(string text, double maxWidth, double charWidth)
    {
        if (string.IsNullOrEmpty(text)) return [""];
        var maxChars = (int)(maxWidth / Math.Max(charWidth, 1));
        if (maxChars <= 0) maxChars = 1;
        if (text.Length <= maxChars) return [text];

        var result = new List<string>();
        var remaining = text;
        while (remaining.Length > maxChars)
        {
            var breakAt = remaining.LastIndexOf(' ', maxChars);
            if (breakAt <= 0) breakAt = maxChars;
            result.Add(remaining[..breakAt]);
            remaining = remaining[breakAt..].TrimStart();
        }
        if (remaining.Length > 0) result.Add(remaining);
        return result.ToArray();
    }

    private static string EscapePdfString(string s)
    {
        // The content stream is written with Encoding.ASCII, so a raw non-ASCII char
        // (bullet U+2022, curly quotes, en/em dash, accented Latin) would be flattened
        // to '?'. Encode to Windows-1252 (the fonts declare /WinAnsiEncoding) and emit
        // any byte outside printable ASCII as an octal escape so it survives the ASCII
        // write and renders as the right glyph.
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            byte b = Aspose.Pdf.Text.Cp1252.TryGetByte(ch, out var wb) ? wb : (byte)'?';
            switch (b)
            {
                case (byte)'\\': sb.Append("\\\\"); break;
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                default:
                    if (b >= 0x20 && b <= 0x7E) sb.Append((char)b);
                    else sb.Append('\\').Append(System.Convert.ToString(b, 8).PadLeft(3, '0'));
                    break;
            }
        }
        return sb.ToString();
    }

    // Print-invoice sheet constants — every value measured off the reference
    // render of the @media-print invoice fixture (page 931.25 × 842):
    // sheet = 96 + the print container + the fitted right band; the body is the
    // sheet's width% of the container; text runs on a 19px (14.25pt) pitch with
    // a fresh table opening one 21px (15.75pt) band below the previous row.
    private const double PrintContainerPt = 751.25;   // the engine's 1000px-class print viewport
    private const double PrintRightBandPt = 84.0;     // fitted right band (112px)
    private const double PrintLeftPt = 96.0;
    private const double PrintRowPitchPt = 14.25;     // 19px line band @ 9pt Calibri
    private const double PrintTableOpenPt = 15.75;    // 21px first-row band of a fresh table
    private const double PrintFirstTopPt = 87.9;      // page top → first row glyph top
    private const double PrintCellInsetPt = 2.25;     // cell chrome (border-spacing + padding)
    private const double PrintValueColFrac = 0.4;     // label tables: value col at 40% of the container
    private const double PrintZoiValueOffPt = 123.7;  // trailer table: value col offset
    private const double PrintColRightInsetPt = 0.7;  // right-aligned runs off the column edge
    private const double PrintDashDropPt = 10.3;      // values glyph top → dashed cell bottoms
    private const double PrintTrailerGapPt = 28.5;    // totals → trailer rows (two bands)
    private const double PrintHrFromTrailerPt = 90.2; // trailer top → dashed hr (347.9→438.1)
    private const double PrintQrDropPt = 26.5;        // trailer top → QR image top
    private const double PrintQrSizePt = 48.19;       // 1.7cm QR bitmap
    private const double PrintQrRightInsetPt = 10.77; // body right → QR right edge
    // The item table's column RIGHT edges as fractions of the body width and the
    // trailer rows' measured pitches (dl margins ride the last two).
    private static readonly double[] PrintItemColEdgeFrac = { 0.1701, 0.3467, 0.5439, 0.7695, 1.0 };
    private static readonly double[] PrintTrailerPitchPt = { 14.25, 17.1, 19.9 };
    // Calibri's ascender (typo ascent 750/1000 + the win gap) — seats a baseline
    // from a measured glyph top.
    private const double PrintCalibriAscEm = 0.952;

    /// <summary>Render the @media-print invoice sheet (see the caller's gate):
    /// label/value tables, the item table with dashed cell bottoms and totals
    /// rows, the trailer table, a dashed hr, and the QR bitmap — at the measured
    /// geometry. Null when the document does not fit the shape (the caller falls
    /// through to the ordinary flow).</summary>
    private static Document? TryRenderPrintInvoice(string html, HtmlLoadOptions? options)
    {
        var bodyPctM = Regex.Match(html,
            @"body\s*\{[^}]*?width\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase);
        if (!bodyPctM.Success) return null;
        if (!double.TryParse(bodyPctM.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bodyPct)
            || bodyPct is <= 0 or > 100) return null;

        var css = ParseStyleSheet(html);
        HtmlNode dom;
        try
        {
            dom = ParseDom(Regex.Replace(Regex.Replace(html,
                @"<!--[\s\S]*?-->", m => new string(' ', m.Length)),
                @"<(script|style|head)[^>]*>[\s\S]*?</\1>",
                m => new string(' ', m.Length), RegexOptions.IgnoreCase));
        }
        catch { return null; }

        // Collect top-level tables in document order; each row's cells with
        // their emphasis. A table containing a row of 4+ populated cells is the
        // ITEM table; tables after it are trailer tables.
        var tables = new List<List<(List<(string Text, bool Th, bool Italic)> Cells, bool AnyTh)>>();
        foreach (var el in dom.Descendants())
        {
            if (el.Tag != "table") continue;
            var rows = new List<(List<(string, bool, bool)>, bool)>();
            foreach (var tr in el.Descendants())
            {
                if (tr.Tag != "tr") continue;
                var cells = new List<(string, bool, bool)>();
                var anyTh = false;
                foreach (var cd in tr.Children)
                {
                    if (cd.Tag is not ("td" or "th")) continue;
                    var italic = cd.Attrs is not null && cd.Attrs.TryGetValue("class", out var ccls)
                        && ccls.Contains("italic", StringComparison.OrdinalIgnoreCase);
                    foreach (var sp2 in cd.Descendants())
                        if (sp2.Tag == "span" && sp2.Attrs is not null
                            && sp2.Attrs.TryGetValue("class", out var scls)
                            && scls.Contains("italic", StringComparison.OrdinalIgnoreCase))
                            italic = true;
                    var txt = DomText(cd, css);
                    if (cd.Tag == "th") anyTh = true;
                    cells.Add((txt, cd.Tag == "th", italic));
                }
                if (cells.Count > 0) rows.Add((cells, anyTh));
            }
            if (rows.Count > 0) tables.Add(rows);
        }
        if (tables.Count < 2) return null;
        var itemTableIdx = -1;
        for (var t = 0; t < tables.Count; t++)
            foreach (var (cells, _) in tables[t])
            {
                var filled = 0;
                foreach (var (txt, _, _) in cells) if (txt.Length > 0) filled++;
                if (filled >= 4) { itemTableIdx = t; break; }
            }
        if (itemTableIdx < 0) return null;

        var doc = new Document();
        var pageW = PrintLeftPt + PrintContainerPt + PrintRightBandPt;
        var page = doc.Pages.Add(pageW, 842.0);
        var fontDict = new Core.PdfDictionary();
        EnsureFonts(page, fontDict);

        var x0 = PrintLeftPt;
        var bodyW = PrintContainerPt * bodyPct / 100.0;
        var bodyR = x0 + bodyW;
        var fontPt = 9.0;
        var reg = PosFace("Calibri");
        var bold = PosFace("Calibri Bold");
        var ital = PosFace("Calibri Italic");
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        double Measure(string t2, bool b2, bool i2)
            => MeasureFaceText(b2 ? "Calibri Bold" : i2 ? "Calibri Italic" : "Calibri", t2, fontPt);
        void Draw(string t2, double x, double glyphTop, bool b2, bool i2)
        {
            var f2 = b2 && bold.ttf is not null ? bold : i2 && ital.ttf is not null ? ital : reg;
            if (f2.ttf is null || t2.Length == 0) return;
            var baseline = 842.0 - glyphTop - PrintCalibriAscEm * fontPt;
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, f2.ttf,
                b2 ? "Calibri Bold" : i2 ? "Calibri Italic" : "Calibri", t2,
                stripSpacesInBaseFont: true);
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(inv,
                $"BT 0 0 0 rg /{rn} {fontPt:F1} Tf 1 0 0 1 {x:F2} {baseline:F2} Tm <{System.Convert.ToHexString(hex)}> Tj ET\n")));
        }
        void Dash(double xA, double xB, double yTd)
            => page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(inv,
                $"q 0 0 0 RG 0.75 w [1 0.5] 0 d {xA:F2} {842.0 - yTd:F2} m {xB:F2} {842.0 - yTd:F2} l S Q\n")));

        var colEdges = new double[PrintItemColEdgeFrac.Length];
        for (var c = 0; c < colEdges.Length; c++)
            colEdges[c] = x0 + PrintItemColEdgeFrac[c] * bodyW;

        var yTop = PrintFirstTopPt;
        double trailerTop = -1;   // glyph top of the first trailer (ZOI) row
        for (var t = 0; t < tables.Count; t++)
        {
            if (t > 0) yTop += PrintTableOpenPt - PrintRowPitchPt;
            var isItem = t == itemTableIdx;
            var trailerRow = 0;
            List<(string, bool, bool)>? lastValuesRow = null;
            foreach (var (cells, anyTh) in tables[t])
            {
                var filled = new List<(string Text, bool Th, bool Italic)>();
                foreach (var cc in cells) if (cc.Item1.Length > 0) filled.Add(cc);
                // An all-empty row collapses — it holds no line band.
                if (filled.Count == 0) continue;
                var pitch = PrintRowPitchPt;
                if (isItem && filled.Count >= 4)
                {
                    // Column header / values row: right-aligned at the col edges.
                    for (var c = 0; c < filled.Count && c < colEdges.Length; c++)
                    {
                        var (txt, th2, it2) = filled[c];
                        Draw(txt, colEdges[c] - PrintColRightInsetPt - Measure(txt, th2, it2),
                            yTop, th2, it2);
                    }
                    if (!anyTh) lastValuesRow = cells;
                }
                // A totals row carries an EMPHASISED label (th/bold); the
                // trailer rows (hash pairs, edition lines) are plain pairs.
                else if (isItem && filled.Count == 2 && (filled[0].Th || anyTh))
                {
                    // Close the values band with the dashed cell bottoms first.
                    if (lastValuesRow is not null)
                    {
                        var dashY = yTop - PrintRowPitchPt + PrintDashDropPt;
                        var segL = x0 + 1.5;
                        for (var c = 0; c < colEdges.Length; c++)
                        {
                            Dash(segL, colEdges[c], dashY);
                            segL = colEdges[c] + 1.5;
                        }
                        lastValuesRow = null;
                    }
                    var (lab, labTh, labIt) = filled[0];
                    var (val, valTh, valIt) = filled[1];
                    Draw(lab, colEdges[^2] - PrintColRightInsetPt - Measure(lab, labTh, labIt),
                        yTop, labTh, labIt);
                    Draw(val, colEdges[^1] - PrintColRightInsetPt - Measure(val, valTh, valIt),
                        yTop, valTh, valIt);
                }
                else if (filled.Count >= 2)
                {
                    var isTrailerRow = t > itemTableIdx || (isItem && !filled[0].Th && !anyTh);
                    if (isTrailerRow && trailerTop < 0)
                    {
                        // The trailer opens two bands below the totals.
                        yTop += PrintTrailerGapPt - PrintRowPitchPt;
                        trailerTop = yTop;
                    }
                    var (lab, labTh, labIt) = filled[0];
                    var (val, valTh, valIt) = filled[1];
                    Draw(lab, x0 + PrintCellInsetPt, yTop, labTh, labIt);
                    var valX = isTrailerRow
                        ? x0 + PrintZoiValueOffPt
                        : x0 + PrintCellInsetPt + PrintValueColFrac * PrintContainerPt;
                    Draw(val, valX, yTop, valTh, valIt);
                    if (isTrailerRow && trailerRow < PrintTrailerPitchPt.Length)
                        pitch = PrintTrailerPitchPt[trailerRow++];
                }
                else
                {
                    var (txt, th2, it2) = filled[0];
                    Draw(txt, x0 + PrintCellInsetPt, yTop, th2, it2);
                }
                yTop += pitch;
            }
        }

        // Trailing dashed hr and the QR bitmap, both anchored on the trailer top.
        if (trailerTop < 0) trailerTop = yTop;
        Dash(x0, bodyR, trailerTop + PrintHrFromTrailerPt);
        var qrM = Regex.Match(html, @"<img[^>]*src\s*=\s*[""'](data:image/[^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (qrM.Success && LoadConverterImage(qrM.Groups[1].Value, options) is { } qrBytes)
        {
            var zTop = trailerTop + PrintQrDropPt;
            var qx1 = bodyR - PrintQrRightInsetPt;
            try
            {
                page.AddImage(qrBytes, new Rectangle(
                    qx1 - PrintQrSizePt, 842.0 - zTop - PrintQrSizePt, qx1, 842.0 - zTop));
            }
            catch { }
        }
        return doc;
    }

    private static void EnsureFonts(Page page, Core.PdfDictionary? sharedFontDict = null)
    {
        // When the caller supplies a per-conversion font dict, every page of the
        // conversion shares that ONE /Font resource dictionary. Type0FontEmbedder's
        // cache is keyed on the font dict, so a fallback face's program (Arial,
        // SimSun, … — megabytes each) is embedded once per DOCUMENT instead of once
        // per page; the writer serializes the shared objects a single time.
        if (sharedFontDict is not null)
        {
            var resources = page.Dict.Get("Resources") as Core.PdfDictionary;
            if (resources is null)
            {
                resources = new Core.PdfDictionary();
                page.Dict.Set("Resources", resources);
            }
            if (resources.Get("Font") is null)
                resources.Set("Font", sharedFontDict);
        }
        EnsureFont(page, "Helvetica", "F1");
        EnsureFont(page, "Helvetica-Bold", "F2");
        EnsureFont(page, "Helvetica-Oblique", "F3");
        EnsureFont(page, "Courier", "F4");
        // Standard-14 serif faces for the UA-default flow (Times-Roman/-Bold/-Italic):
        // serif output that embeds nothing, so a font-family-free document renders in
        // the browser default face without bloating the file or embedding a program.
        EnsureFont(page, "Times-Roman", "F5");
        EnsureFont(page, "Times-Bold", "F6");
        EnsureFont(page, "Times-Italic", "F7");
    }

    private static void EnsureFont(Page page, string baseFontName, string resName)
    {
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary;
        if (resources is null)
        {
            resources = new Core.PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as Core.PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new Core.PdfDictionary();
            resources.Set("Font", fontDict);
        }
        if (!fontDict.ContainsKey(resName))
        {
            var font = new Core.PdfDictionary();
            font.Set("Type", new Core.PdfName("Font"));
            font.Set("Subtype", new Core.PdfName("Type1"));
            font.Set("BaseFont", new Core.PdfName(baseFontName));
            font.Set("Encoding", new Core.PdfName("WinAnsiEncoding"));
            fontDict.Set(resName, font);
        }
    }
}
