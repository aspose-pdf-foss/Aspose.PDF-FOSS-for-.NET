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
            barBorder = ParseCssColor(bb) ?? Color.FromArgb(0, 0, 0);

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
                    Color = DomColor(styleEl, css) ?? Color.FromArgb(204, 204, 204),
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
}
