using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
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
                return Color.FromRgbBytes(int.Parse(m.Groups[1].Value),
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
            var barText = CardColor(DomDecl(c, "color", css)) ?? Color.FromArgb(0, 0, 0);
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

    /// <summary>Build a centered inline-link row: text runs and link runs in child
    /// order, spacing from each child's CSS margins, colors resolved per element.</summary>
    private static Block? BuildCenteredLinkRow(HtmlNode el,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var fontPx = DomFontPx(el, 16, css);
        var baseColor = DomColor(el, css) ?? Color.FromArgb(0, 0, 0);
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
}
